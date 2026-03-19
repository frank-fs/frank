module Frank.Cli.Core.Statechart.StatechartSourceExtractor

open System.IO
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Frank.Statecharts
open Frank.Statecharts.Unified
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Statechart.StatechartError

// ── Types ──

/// Intermediate representation of a stateful resource found in source.
type private SourceStatefulResource =
    { RouteTemplate: string
      /// State DU case names extracted from the machine's type parameter.
      StateNames: string list
      /// Name of the initial state case.
      InitialStateKey: string
      /// Guard names extracted from the machine binding's Guards field.
      GuardNames: string list
      /// Per-state HTTP methods found from inState/forState calls in the CE body.
      StateHttpMethods: Map<string, string list> }

// ── Syntax AST walking: find statefulResource CEs ──

/// HTTP method names corresponding to StateHandlerBuilder functions.
let private httpMethodOf (identText: string) : string option =
    match identText with
    | "get" -> Some "GET"
    | "post" -> Some "POST"
    | "put" -> Some "PUT"
    | "delete" -> Some "DELETE"
    | "patch" -> Some "PATCH"
    | _ -> None

/// Try to extract a state DU case name from a forState's state argument expression.
/// Handles: bare ident (XTurn), application (Won "X"), and qualified ident (State.XTurn).
let rec private tryExtractStateCaseName (expr: SynExpr) : string option =
    match expr with
    | SynExpr.Ident ident -> Some ident.idText
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) ->
        ids |> List.tryLast |> Option.map _.idText
    | SynExpr.App(funcExpr = funcExpr) ->
        // Application like (Won "X") — the func is the case constructor
        tryExtractStateCaseName funcExpr
    | SynExpr.Paren(expr = inner) -> tryExtractStateCaseName inner
    | _ -> None

/// Extract HTTP methods from a handler list expression.
/// Matches patterns like: [ StateHandlerBuilder.get h; StateHandlerBuilder.post h ]
let rec private extractHttpMethods (expr: SynExpr) : string list =
    match expr with
    | SynExpr.ArrayOrList(exprs = items) ->
        items |> List.choose extractSingleMethod
    | SynExpr.ArrayOrListComputed(expr = inner) ->
        extractHttpMethods inner
    | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
        (extractSingleMethod e1 |> Option.toList) @ (extractSingleMethod e2 |> Option.toList)
    | _ ->
        extractSingleMethod expr |> Option.toList

and private extractSingleMethod (expr: SynExpr) : string option =
    match expr with
    // StateHandlerBuilder.get handler
    | SynExpr.App(funcExpr = SynExpr.LongIdent(longDotId = SynLongIdent(id = ids))) ->
        ids |> List.tryLast |> Option.bind (fun id -> httpMethodOf id.idText)
    // get handler (if opened)
    | SynExpr.App(funcExpr = SynExpr.Ident ident) ->
        httpMethodOf ident.idText
    | SynExpr.Paren(expr = inner) -> extractSingleMethod inner
    | _ -> None

/// A single (stateCaseName, httpMethods) extracted from a forState call.
type private ForStateInfo =
    { CaseName: string
      Methods: string list }

/// Try to extract state case + HTTP methods from a forState(...) call expression.
/// Pattern: forState XTurn [ get h; post h ]
let rec private tryExtractForState (expr: SynExpr) : ForStateInfo option =
    match expr with
    | SynExpr.Paren(expr = inner) -> tryExtractForState inner
    // forState StateName handlerList
    | SynExpr.App(funcExpr = SynExpr.App(funcExpr = funcExpr; argExpr = stateExpr); argExpr = handlersExpr) ->
        let isForState =
            match funcExpr with
            | SynExpr.Ident id -> id.idText = "forState"
            | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) ->
                ids |> List.exists (fun id -> id.idText = "forState")
            | _ -> false
        if isForState then
            match tryExtractStateCaseName stateExpr with
            | Some caseName ->
                Some { CaseName = caseName; Methods = extractHttpMethods handlersExpr }
            | None -> None
        else None
    | _ -> None

/// Accumulator for walking a statefulResource CE body.
type private CeAccum =
    { StateHandlers: ForStateInfo list
      MachineName: string option }

let private emptyCeAccum = { StateHandlers = []; MachineName = None }

/// Walk the CE body to extract inState calls and the machine binding name.
let rec private walkStatefulCeBody (acc: CeAccum) (expr: SynExpr) : CeAccum =
    match expr with
    | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
        walkStatefulCeBody (walkStatefulCeBody acc e1) e2

    // inState (forState ...)
    | SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = argExpr) when ident.idText = "inState" ->
        match tryExtractForState argExpr with
        | Some info -> { acc with StateHandlers = info :: acc.StateHandlers }
        | None -> acc

    // machine someMachine
    | SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = argExpr) when ident.idText = "machine" ->
        let machineName =
            match argExpr with
            | SynExpr.Ident id -> Some id.idText
            | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) ->
                ids |> List.tryLast |> Option.map _.idText
            | _ -> None
        { acc with MachineName = machineName }

    | SynExpr.LetOrUse(body = body) -> walkStatefulCeBody acc body
    | SynExpr.Paren(expr = inner) -> walkStatefulCeBody acc inner
    | _ -> acc

/// Result of finding a statefulResource CE in syntax.
type private SyntaxStatefulResource =
    { RouteTemplate: string
      MachineName: string option
      StateHandlers: ForStateInfo list }

/// Try to extract a statefulResource CE from a syntax expression.
let private tryExtractStatefulResource (expr: SynExpr) : SyntaxStatefulResource option =
    match expr with
    // statefulResource "/route" { ... }
    | SynExpr.App(
        funcExpr = SynExpr.App(
            funcExpr = SynExpr.Ident ident
            argExpr = SynExpr.Const(SynConst.String(routeTemplate, _, _), _))
        argExpr = SynExpr.ComputationExpr(expr = ceBody))
        when ident.idText = "statefulResource" ->
        let acc = walkStatefulCeBody emptyCeAccum ceBody
        Some
            { RouteTemplate = routeTemplate
              MachineName = acc.MachineName
              StateHandlers = List.rev acc.StateHandlers }
    | _ -> None

/// Recursively walk all expressions in a syntax tree to find statefulResource CEs.
let rec private walkExprForStateful (results: ResizeArray<SyntaxStatefulResource>) (expr: SynExpr) =
    match tryExtractStatefulResource expr with
    | Some r -> results.Add(r)
    | None ->
        match expr with
        | SynExpr.App(funcExpr = f; argExpr = a) ->
            walkExprForStateful results f
            walkExprForStateful results a
        | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
            walkExprForStateful results e1
            walkExprForStateful results e2
        | SynExpr.LetOrUse(bindings = bindings; body = body) ->
            for binding in bindings do
                match binding with
                | SynBinding(expr = bindExpr) -> walkExprForStateful results bindExpr
            walkExprForStateful results body
        | SynExpr.Paren(expr = inner) -> walkExprForStateful results inner
        | SynExpr.Lambda(body = body) -> walkExprForStateful results body
        | SynExpr.ComputationExpr(expr = ceBody) -> walkExprForStateful results ceBody
        | SynExpr.IfThenElse(ifExpr = i; thenExpr = t; elseExpr = e) ->
            walkExprForStateful results i
            walkExprForStateful results t
            e |> Option.iter (walkExprForStateful results)
        | SynExpr.Match(expr = matchExpr; clauses = clauses) ->
            walkExprForStateful results matchExpr
            for clause in clauses do
                match clause with
                | SynMatchClause(resultExpr = resultExpr) -> walkExprForStateful results resultExpr
        | SynExpr.Tuple(exprs = exprs) ->
            for e in exprs do walkExprForStateful results e
        | _ -> ()

/// Walk all declarations in a module to find statefulResource CEs.
let rec private walkDecl (results: ResizeArray<SyntaxStatefulResource>) (decl: SynModuleDecl) =
    match decl with
    | SynModuleDecl.Let(bindings = bindings) ->
        for binding in bindings do
            match binding with
            | SynBinding(expr = expr) -> walkExprForStateful results expr
    | SynModuleDecl.NestedModule(decls = decls) ->
        for d in decls do walkDecl results d
    | SynModuleDecl.Expr(expr = expr) -> walkExprForStateful results expr
    | _ -> ()

/// Find all statefulResource CEs across all parsed files.
let private findStatefulResources (parsedFiles: (string * ParsedInput) list) : SyntaxStatefulResource list =
    let results = ResizeArray<SyntaxStatefulResource>()
    for _, parsedInput in parsedFiles do
        match parsedInput with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            for m in modules do
                match m with
                | SynModuleOrNamespace(decls = decls) ->
                    for d in decls do walkDecl results d
        | _ -> ()
    results |> Seq.toList

// ── Typed AST: resolve StateMachine type parameters ──

open FSharp.Compiler.Symbols

/// Information about a StateMachine binding found in the typed AST.
type private TypedMachineBinding =
    { BindingName: string
      StateTypeCases: string list
      InitialStateName: string option
      GuardNames: string list }

/// Check if an FSharpType is StateMachine<'S,'E,'C> and extract the state type's DU cases.
let private tryExtractStateMachineInfo (fsharpType: FSharpType) : (string list) option =
    if fsharpType.HasTypeDefinition then
        let td = fsharpType.TypeDefinition
        if td.DisplayName = "StateMachine" && fsharpType.GenericArguments.Count >= 1 then
            let stateType = fsharpType.GenericArguments.[0]
            if stateType.HasTypeDefinition && stateType.TypeDefinition.IsFSharpUnion then
                let cases =
                    stateType.TypeDefinition.UnionCases
                    |> Seq.map _.Name
                    |> Seq.toList
                Some cases
            else
                None
        else
            None
    else
        None

/// Recursively walk FCS entities to find let-bindings of type StateMachine<'S,'E,'C>.
let rec private findMachineBindings (entity: FSharpEntity) : TypedMachineBinding list =
    let nested =
        try entity.NestedEntities |> Seq.collect findMachineBindings |> Seq.toList
        with :? System.InvalidOperationException -> []  // FCS: unresolved external entity

    let fromMembers =
        try
            entity.MembersFunctionsAndValues
            |> Seq.choose (fun mfv ->
                if mfv.IsModuleValueOrMember && not mfv.IsMember then
                    tryExtractStateMachineInfo mfv.FullType
                    |> Option.map (fun cases ->
                        { BindingName = mfv.DisplayName
                          StateTypeCases = cases
                          InitialStateName = cases |> List.tryHead
                          GuardNames = [] })
                else
                    None)
            |> Seq.toList
        with :? System.InvalidOperationException -> []  // FCS: unresolved external entity

    fromMembers @ nested

/// Find all StateMachine bindings across the project's typed AST.
let private findAllMachineBindings (checkResults: FSharp.Compiler.CodeAnalysis.FSharpCheckProjectResults) : TypedMachineBinding list =
    checkResults.AssemblySignature.Entities
    |> Seq.collect findMachineBindings
    |> Seq.toList

// ── Combining syntax + typed results ──

/// Build ExtractedStatechart by cross-referencing syntax CEs with typed machine bindings.
let private buildExtractedStatecharts
    (syntaxResources: SyntaxStatefulResource list)
    (typedBindings: TypedMachineBinding list)
    : ExtractedStatechart list =

    // Index typed bindings by name for lookup
    let bindingsByName =
        typedBindings
        |> List.map (fun b -> b.BindingName, b)
        |> Map.ofList

    syntaxResources
    |> List.map (fun sr ->
        // Try to resolve the machine binding's state type
        let machineInfo =
            sr.MachineName
            |> Option.bind (fun name -> Map.tryFind name bindingsByName)

        let stateNames =
            match machineInfo with
            | Some info -> info.StateTypeCases
            | None ->
                // Fallback: use state case names from forState calls in the CE
                sr.StateHandlers |> List.map _.CaseName |> List.distinct

        let initialStateKey =
            match machineInfo with
            | Some info -> info.InitialStateName |> Option.defaultValue (stateNames |> List.tryHead |> Option.defaultValue "Unknown")
            | None -> stateNames |> List.tryHead |> Option.defaultValue "Unknown"

        let guardNames =
            match machineInfo with
            | Some info -> info.GuardNames
            | None -> []

        // Build per-state HTTP methods from forState calls
        let stateHttpMethods =
            sr.StateHandlers
            |> List.map (fun fs -> fs.CaseName, fs.Methods)
            |> Map.ofList

        // Build StateMetadata from the extracted information
        let stateMetadata =
            stateNames
            |> List.map (fun name ->
                let methods =
                    Map.tryFind name stateHttpMethods
                    |> Option.defaultValue []
                name,
                { IsFinal = false
                  AllowedMethods = methods
                  Description = None })
            |> Map.ofList

        StatechartExtractor.toExtractedStatechart sr.RouteTemplate stateNames initialStateKey guardNames stateMetadata)

// ── Public API ──

/// Extract statechart metadata from an F# project using the compiler.
/// No assembly loading required — uses FSharp.Compiler.Service to analyze source.
let extract (projectPath: string) : Async<Result<ExtractedStatechart list, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error (FileNotFound projectPath)
        else
            match! ProjectLoader.loadProject projectPath with
            | Error msg ->
                return Error (AssemblyLoadError(projectPath, msg))
            | Ok loaded ->
                // Phase 1: Walk syntax to find statefulResource CEs
                let syntaxResources = findStatefulResources loaded.ParsedFiles

                // Phase 2: Walk typed AST to find StateMachine bindings
                let typedBindings = findAllMachineBindings loaded.CheckResults

                // Phase 3: Cross-reference and build ExtractedStatecharts
                let results = buildExtractedStatecharts syntaxResources typedBindings
                return Ok results
    }
