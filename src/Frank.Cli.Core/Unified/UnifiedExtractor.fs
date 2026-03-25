module Frank.Cli.Core.Unified.UnifiedExtractor

open System.IO
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open FSharp.Compiler.Symbols
open FSharp.Compiler.CodeAnalysis
open Frank.Statecharts
open Frank.Resources.Model
open Frank.Cli.Core.Shared
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Statechart
open Frank.Cli.Core.Statechart.StatechartError

// ══════════════════════════════════════════════════════════════════════════════
// Syntax AST extraction helpers (merged from AstAnalyzer)
// ══════════════════════════════════════════════════════════════════════════════

// ── HTTP method helpers ──

let private httpMethodNames =
    Set.ofList [ "get"; "post"; "put"; "delete"; "patch"; "head"; "options" ]

let private httpMethodToString (name: string) : string option =
    match name.ToLowerInvariant() with
    | "get" -> Some "GET"
    | "post" -> Some "POST"
    | "put" -> Some "PUT"
    | "delete" -> Some "DELETE"
    | "patch" -> Some "PATCH"
    | "head" -> Some "HEAD"
    | "options" -> Some "OPTIONS"
    | _ -> None

// ── Plain resource CE helpers (from AstAnalyzer) ──

type private ResourceCeAccum =
    { Methods: string list
      Name: string option
      HasLinkedData: bool }

let private emptyResourceCeAccum =
    { Methods = []
      Name = None
      HasLinkedData = false }

let rec private walkResourceCeBody (acc: ResourceCeAccum) (expr: SynExpr) : ResourceCeAccum =
    match expr with
    | SynExpr.Sequential(expr1 = expr1; expr2 = expr2) -> walkResourceCeBody (walkResourceCeBody acc expr1) expr2

    | SynExpr.App(funcExpr = SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = argExpr); argExpr = _handlerExpr) ->
        let idText = ident.idText.ToLowerInvariant()

        if httpMethodNames.Contains idText then
            match httpMethodToString idText with
            | Some m -> { acc with Methods = m :: acc.Methods }
            | None -> acc
        elif idText = "name" then
            match argExpr with
            | SynExpr.Const(SynConst.String(text, _, _), _) -> { acc with Name = Some text }
            | _ -> acc
        elif idText = "linkeddata" then
            { acc with HasLinkedData = true }
        else
            acc

    | SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = argExpr) ->
        let idText = ident.idText.ToLowerInvariant()

        if httpMethodNames.Contains idText then
            match httpMethodToString idText with
            | Some m -> { acc with Methods = m :: acc.Methods }
            | None -> acc
        elif idText = "name" then
            match argExpr with
            | SynExpr.Const(SynConst.String(text, _, _), _) -> { acc with Name = Some text }
            | _ -> acc
        elif idText = "linkeddata" then
            { acc with HasLinkedData = true }
        else
            acc

    | SynExpr.Ident ident ->
        if ident.idText.ToLowerInvariant() = "linkeddata" then
            { acc with HasLinkedData = true }
        else
            acc

    | SynExpr.LetOrUse(bindings = _; body = body) -> walkResourceCeBody acc body
    | SynExpr.Paren(expr = innerExpr) -> walkResourceCeBody acc innerExpr
    | SynExpr.Lambda(body = body) -> walkResourceCeBody acc body
    | _ -> acc

let private tryExtractResource (expr: SynExpr) (file: string) : AnalyzedResource option =
    match expr with
    | SynExpr.App(
        funcExpr = SynExpr.App(
            funcExpr = SynExpr.Ident ident; argExpr = SynExpr.Const(SynConst.String(routeTemplate, _, _), _))
        argExpr = SynExpr.ComputationExpr(expr = ceBody)
        range = r) when ident.idText = "resource" ->
        let result = walkResourceCeBody emptyResourceCeAccum ceBody

        Some
            { RouteTemplate = routeTemplate
              Name = result.Name
              HttpMethods =
                result.Methods
                |> List.rev
                |> List.choose (fun s -> AstAnalyzer.parseHttpMethod (s.ToLowerInvariant()))
              HasLinkedData = result.HasLinkedData
              Location =
                { File = file
                  Line = r.StartLine
                  Column = r.StartColumn } }
    | _ -> None

// ── Stateful resource CE helpers ──

let rec private tryExtractStateCaseName (expr: SynExpr) : string option =
    match expr with
    | SynExpr.Ident ident -> Some ident.idText
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) -> ids |> List.tryLast |> Option.map _.idText
    | SynExpr.App(funcExpr = funcExpr) -> tryExtractStateCaseName funcExpr
    | SynExpr.Paren(expr = inner) -> tryExtractStateCaseName inner
    | _ -> None

let rec private extractHttpMethods (expr: SynExpr) : string list =
    match expr with
    | SynExpr.ArrayOrList(exprs = items) -> items |> List.choose extractSingleMethod
    | SynExpr.ArrayOrListComputed(expr = inner) -> extractHttpMethods inner
    | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
        (extractSingleMethod e1 |> Option.toList)
        @ (extractSingleMethod e2 |> Option.toList)
    | _ -> extractSingleMethod expr |> Option.toList

and private extractSingleMethod (expr: SynExpr) : string option =
    match expr with
    | SynExpr.App(funcExpr = SynExpr.LongIdent(longDotId = SynLongIdent(id = ids))) ->
        ids |> List.tryLast |> Option.bind (fun id -> httpMethodToString id.idText)
    | SynExpr.App(funcExpr = SynExpr.Ident ident) -> httpMethodToString ident.idText
    | SynExpr.Paren(expr = inner) -> extractSingleMethod inner
    | _ -> None

type internal ForStateInfo =
    { CaseName: string
      Methods: string list }

let rec private tryExtractForState (expr: SynExpr) : ForStateInfo option =
    match expr with
    | SynExpr.Paren(expr = inner) -> tryExtractForState inner
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
                Some
                    { CaseName = caseName
                      Methods = extractHttpMethods handlersExpr }
            | None -> None
        else
            None
    | _ -> None

type private StatefulCeAccum =
    { StateHandlers: ForStateInfo list
      MachineName: string option
      RoleNames: string list }

let private emptyStatefulCeAccum =
    { StateHandlers = []
      MachineName = None
      RoleNames = [] }

let rec private walkStatefulCeBody (acc: StatefulCeAccum) (expr: SynExpr) : StatefulCeAccum =
    match expr with
    | SynExpr.Sequential(expr1 = e1; expr2 = e2) -> walkStatefulCeBody (walkStatefulCeBody acc e1) e2

    | SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = argExpr) when ident.idText = "inState" ->
        match tryExtractForState argExpr with
        | Some info ->
            { acc with
                StateHandlers = info :: acc.StateHandlers }
        | None -> acc

    | SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = argExpr) when ident.idText = "machine" ->
        let machineName =
            match argExpr with
            | SynExpr.Ident id -> Some id.idText
            | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) -> ids |> List.tryLast |> Option.map _.idText
            | _ -> None

        { acc with MachineName = machineName }

    // role "PlayerX" (fun user -> ...)
    | SynExpr.App(
        funcExpr = SynExpr.App(
            funcExpr = SynExpr.Ident ident; argExpr = SynExpr.Const(SynConst.String(roleName, _, _), _))) when
        ident.idText = "role"
        ->
        { acc with
            RoleNames = roleName :: acc.RoleNames }

    | SynExpr.LetOrUse(body = body) -> walkStatefulCeBody acc body
    | SynExpr.Paren(expr = inner) -> walkStatefulCeBody acc inner
    | _ -> acc

type internal SyntaxStatefulResource =
    { RouteTemplate: string
      MachineName: string option
      StateHandlers: ForStateInfo list
      RoleNames: string list }

let private tryExtractStatefulResource (expr: SynExpr) : SyntaxStatefulResource option =
    match expr with
    | SynExpr.App(
        funcExpr = SynExpr.App(
            funcExpr = SynExpr.Ident ident; argExpr = SynExpr.Const(SynConst.String(routeTemplate, _, _), _))
        argExpr = SynExpr.ComputationExpr(expr = ceBody)) when ident.idText = "statefulResource" ->
        let acc = walkStatefulCeBody emptyStatefulCeAccum ceBody

        Some
            { RouteTemplate = routeTemplate
              MachineName = acc.MachineName
              StateHandlers = List.rev acc.StateHandlers
              RoleNames = List.rev acc.RoleNames }
    | _ -> None

// ══════════════════════════════════════════════════════════════════════════════
// Unified single-pass syntax AST walker
// ══════════════════════════════════════════════════════════════════════════════

type internal SyntaxFinding =
    | FoundPlainResource of resource: AnalyzedResource
    | FoundStatefulResource of resource: SyntaxStatefulResource

/// Single-pass expression walker that dispatches to both resource and statefulResource extraction.
let rec private walkExpr (file: string) (results: ResizeArray<SyntaxFinding>) (expr: SynExpr) =
    // Try statefulResource first (more specific pattern)
    match tryExtractStatefulResource expr with
    | Some sr -> results.Add(FoundStatefulResource sr)
    | None ->
        // Try plain resource
        match tryExtractResource expr file with
        | Some ar -> results.Add(FoundPlainResource ar)
        | None ->
            // Continue walking -- identical traversal logic
            match expr with
            | SynExpr.App(funcExpr = f; argExpr = a) ->
                walkExpr file results f
                walkExpr file results a
            | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
                walkExpr file results e1
                walkExpr file results e2
            | SynExpr.LetOrUse(bindings = bindings; body = body) ->
                for binding in bindings do
                    match binding with
                    | SynBinding(expr = bindExpr) -> walkExpr file results bindExpr

                walkExpr file results body
            | SynExpr.Paren(expr = inner) -> walkExpr file results inner
            | SynExpr.Lambda(body = body) -> walkExpr file results body
            | SynExpr.ComputationExpr(expr = ceBody) -> walkExpr file results ceBody
            | SynExpr.IfThenElse(ifExpr = i; thenExpr = t; elseExpr = e) ->
                walkExpr file results i
                walkExpr file results t
                e |> Option.iter (walkExpr file results)
            | SynExpr.Match(expr = matchExpr; clauses = clauses) ->
                walkExpr file results matchExpr

                for clause in clauses do
                    match clause with
                    | SynMatchClause(resultExpr = re) -> walkExpr file results re
            | SynExpr.Tuple(exprs = exprs) ->
                for e in exprs do
                    walkExpr file results e
            | SynExpr.ArrayOrList(exprs = exprs) ->
                for e in exprs do
                    walkExpr file results e
            | _ -> ()

let rec private walkDecl (file: string) (results: ResizeArray<SyntaxFinding>) (decl: SynModuleDecl) =
    match decl with
    | SynModuleDecl.Let(bindings = bindings) ->
        for binding in bindings do
            match binding with
            | SynBinding(expr = expr) -> walkExpr file results expr
    | SynModuleDecl.NestedModule(decls = decls) ->
        for d in decls do
            walkDecl file results d
    | SynModuleDecl.Expr(expr = expr) -> walkExpr file results expr
    | _ -> ()

let private findAllResources (parsedFiles: (string * ParsedInput) list) : SyntaxFinding list =
    let results = ResizeArray<SyntaxFinding>()

    for fileName, parsedInput in parsedFiles do
        match parsedInput with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            for m in modules do
                match m with
                | SynModuleOrNamespace(decls = decls) ->
                    for d in decls do
                        walkDecl fileName results d
        | _ -> ()

    results |> Seq.toList

/// Find resources from a single parsed input (for testing).
let internal findResourcesInParsedInput (parsedInput: ParsedInput) : SyntaxFinding list =
    let results = ResizeArray<SyntaxFinding>()
    let fileName = parsedInput.FileName

    match parsedInput with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
        for m in modules do
            match m with
            | SynModuleOrNamespace(decls = decls) ->
                for d in decls do
                    walkDecl fileName results d
    | _ -> ()

    results |> Seq.toList

// ══════════════════════════════════════════════════════════════════════════════
// Typed AST walking
// ══════════════════════════════════════════════════════════════════════════════

type private TypedMachineBinding =
    { BindingName: string
      StateTypeCases: string list
      InitialStateName: string option
      GuardNames: string list }

let private tryExtractStateMachineInfo (fsharpType: FSharpType) : (string list) option =
    if fsharpType.HasTypeDefinition then
        let td = fsharpType.TypeDefinition

        if td.DisplayName = "StateMachine" && fsharpType.GenericArguments.Count >= 1 then
            let stateType = fsharpType.GenericArguments.[0]

            if stateType.HasTypeDefinition && stateType.TypeDefinition.IsFSharpUnion then
                let cases = stateType.TypeDefinition.UnionCases |> Seq.map _.Name |> Seq.toList

                Some cases
            else
                None
        else
            None
    else
        None

type private UnifiedTypedResult =
    { AnalyzedTypes: AnalyzedType list
      MachineBindings: TypedMachineBinding list }

let private emptyTypedResult =
    { AnalyzedTypes = []
      MachineBindings = [] }

let private collectEntityType (entity: FSharpEntity) : AnalyzedType list =
    let entityFullName =
        TypeAnalyzer.tryGetFullName entity |> Option.defaultValue entity.DisplayName

    if entity.DisplayName.StartsWith("<") then
        []
    elif entity.IsFSharpUnion then
        let cases =
            entity.UnionCases
            |> Seq.map (fun uc ->
                { Name = uc.Name
                  Fields =
                    uc.Fields
                    |> Seq.map (fun f -> TypeAnalyzer.makeField f.Name f.FieldType)
                    |> Seq.toList })
            |> Seq.toList

        [ { FullName = entityFullName
            ShortName = entity.DisplayName
            Kind = DiscriminatedUnion cases
            GenericParameters = entity.GenericParameters |> Seq.map (fun p -> p.Name) |> Seq.toList
            SourceLocation = TypeAnalyzer.entityToSourceLocation entity
            IsClosed = false } ]
    elif entity.IsFSharpRecord then
        let fields =
            entity.FSharpFields
            |> Seq.map TypeAnalyzer.makeFieldFromFSharpField
            |> Seq.toList

        [ { FullName = entityFullName
            ShortName = entity.DisplayName
            Kind = Record fields
            GenericParameters = entity.GenericParameters |> Seq.map (fun p -> p.Name) |> Seq.toList
            SourceLocation = TypeAnalyzer.entityToSourceLocation entity
            IsClosed = true } ]
    elif entity.IsEnum then
        let values =
            entity.FSharpFields
            |> Seq.filter (fun f -> f.Name <> "value__")
            |> Seq.map (fun f -> f.Name)
            |> Seq.toList

        [ { FullName = entityFullName
            ShortName = entity.DisplayName
            Kind = Enum values
            GenericParameters = []
            SourceLocation = TypeAnalyzer.entityToSourceLocation entity
            IsClosed = false } ]
    else
        []

let rec private walkEntity (entity: FSharpEntity) : UnifiedTypedResult =
    let nested =
        try
            entity.NestedEntities
            |> Seq.map walkEntity
            |> Seq.fold
                (fun acc r ->
                    { AnalyzedTypes = acc.AnalyzedTypes @ r.AnalyzedTypes
                      MachineBindings = acc.MachineBindings @ r.MachineBindings })
                emptyTypedResult
        with :? System.InvalidOperationException ->
            emptyTypedResult

    let typeResult = collectEntityType entity

    let machineBindings =
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
        with :? System.InvalidOperationException ->
            []

    { AnalyzedTypes = typeResult @ nested.AnalyzedTypes
      MachineBindings = machineBindings @ nested.MachineBindings }

let private analyzeTypedAst (checkResults: FSharpCheckProjectResults) : UnifiedTypedResult =
    checkResults.AssemblySignature.Entities
    |> Seq.map walkEntity
    |> Seq.fold
        (fun acc r ->
            { AnalyzedTypes = acc.AnalyzedTypes @ r.AnalyzedTypes
              MachineBindings = acc.MachineBindings @ r.MachineBindings })
        emptyTypedResult

// ══════════════════════════════════════════════════════════════════════════════
// Cross-reference syntax CEs with typed bindings -> UnifiedResource
// ══════════════════════════════════════════════════════════════════════════════

let private buildUnifiedResources
    (syntaxFindings: SyntaxFinding list)
    (typedResult: UnifiedTypedResult)
    : UnifiedResource list =

    let bindingsByName =
        typedResult.MachineBindings
        |> List.map (fun b -> b.BindingName, b)
        |> Map.ofList

    syntaxFindings
    |> List.map (fun finding ->
        match finding with
        | FoundPlainResource ar ->
            let slug = ResourceModel.resourceSlug ar.RouteTemplate

            let methodStrings =
                ar.HttpMethods
                |> List.map (fun m ->
                    match m with
                    | Get -> "GET"
                    | Post -> "POST"
                    | Put -> "PUT"
                    | Delete -> "DELETE"
                    | Patch -> "PATCH"
                    | Head -> "HEAD"
                    | Options -> "OPTIONS")

            let capabilities =
                methodStrings
                |> List.map (fun m ->
                    { Method = m
                      StateKey = None
                      LinkRelation = if m = "GET" then "self" else m.ToLowerInvariant()
                      IsSafe = m = "GET" || m = "HEAD" || m = "OPTIONS" })

            { RouteTemplate = ar.RouteTemplate
              ResourceSlug = slug
              TypeInfo = []
              Statechart = None
              HttpCapabilities = capabilities
              DerivedFields = ResourceModel.emptyDerivedFields }

        | FoundStatefulResource sr ->
            let slug = ResourceModel.resourceSlug sr.RouteTemplate

            let machineInfo =
                sr.MachineName |> Option.bind (fun n -> Map.tryFind n bindingsByName)

            let stateNames =
                match machineInfo with
                | Some info -> info.StateTypeCases
                | None -> sr.StateHandlers |> List.map _.CaseName |> List.distinct

            let initialStateKey =
                match machineInfo with
                | Some info ->
                    info.InitialStateName
                    |> Option.defaultValue (stateNames |> List.tryHead |> Option.defaultValue "Unknown")
                | None -> stateNames |> List.tryHead |> Option.defaultValue "Unknown"

            let guardNames =
                match machineInfo with
                | Some info -> info.GuardNames
                | None -> []

            let stateHttpMethods =
                sr.StateHandlers |> List.map (fun fs -> fs.CaseName, fs.Methods) |> Map.ofList

            let stateMetadata =
                stateNames
                |> List.map (fun name ->
                    let methods = Map.tryFind name stateHttpMethods |> Option.defaultValue []

                    name,
                    { IsFinal = false
                      AllowedMethods = methods
                      Description = None })
                |> Map.ofList

            let roles =
                sr.RoleNames
                |> List.map (fun name -> { Name = name; Description = None }: RoleInfo)

            let statechart =
                StatechartExtractor.toExtractedStatechart
                    sr.RouteTemplate
                    stateNames
                    initialStateKey
                    guardNames
                    stateMetadata
                    roles

            let capabilities =
                sr.StateHandlers
                |> List.collect (fun fs ->
                    fs.Methods
                    |> List.map (fun m ->
                        { Method = m
                          StateKey = Some fs.CaseName
                          LinkRelation = if m = "GET" then "self" else m.ToLowerInvariant()
                          IsSafe = m = "GET" || m = "HEAD" || m = "OPTIONS" }))

            { RouteTemplate = sr.RouteTemplate
              ResourceSlug = slug
              TypeInfo = []
              Statechart = Some statechart
              HttpCapabilities = capabilities
              DerivedFields = ResourceModel.emptyDerivedFields })

// ══════════════════════════════════════════════════════════════════════════════
// Type association
// ══════════════════════════════════════════════════════════════════════════════

let private associateTypes (resources: UnifiedResource list) (allTypes: AnalyzedType list) : UnifiedResource list =
    resources
    |> List.map (fun r ->
        match r.Statechart with
        | Some sc ->
            let stateTypes =
                allTypes
                |> List.filter (fun t ->
                    match t.Kind with
                    | DiscriminatedUnion cases ->
                        let caseNames = cases |> List.map _.Name
                        sc.StateNames |> List.forall (fun s -> List.contains s caseNames)
                    | _ -> false)

            let referencedTypeNames =
                stateTypes
                |> List.collect (fun t ->
                    match t.Kind with
                    | DiscriminatedUnion cases ->
                        cases
                        |> List.collect (fun c ->
                            c.Fields
                            |> List.choose (fun f ->
                                match f.Kind with
                                | Reference name -> Some name
                                | _ -> None))
                    | _ -> [])
                |> List.distinct

            let referencedTypes =
                allTypes |> List.filter (fun t -> List.contains t.ShortName referencedTypeNames)

            { r with
                TypeInfo = stateTypes @ referencedTypes |> List.distinctBy _.FullName }
        | None -> { r with TypeInfo = allTypes })

// ══════════════════════════════════════════════════════════════════════════════
// DerivedResourceFields computation
// ══════════════════════════════════════════════════════════════════════════════

let computeDerivedFields (resource: UnifiedResource) (allTypes: AnalyzedType list) : DerivedResourceFields =
    match resource.Statechart with
    | None -> ResourceModel.emptyDerivedFields
    | Some sc ->
        let stateDuCases =
            allTypes
            |> List.tryPick (fun t ->
                match t.Kind with
                | DiscriminatedUnion cases ->
                    let caseNames = cases |> List.map _.Name

                    if sc.StateNames |> List.forall (fun s -> List.contains s caseNames) then
                        Some cases
                    else
                        None
                | _ -> None)
            |> Option.defaultValue []

        let handledStates =
            resource.HttpCapabilities |> List.choose _.StateKey |> List.distinct

        let orphanStates =
            sc.StateNames |> List.filter (fun s -> not (List.contains s handledStates))

        let unhandledCases =
            stateDuCases
            |> List.map _.Name
            |> List.filter (fun c -> not (List.contains c sc.StateNames))

        let stateStructure =
            sc.StateNames
            |> List.map (fun stateName ->
                let fields =
                    stateDuCases
                    |> List.tryFind (fun c -> c.Name = stateName)
                    |> Option.map _.Fields
                    |> Option.defaultValue []

                stateName, fields)
            |> Map.ofList

        let typeCoverage =
            if sc.StateNames.IsEmpty then
                1.0
            else
                let covered =
                    sc.StateNames
                    |> List.filter (fun s -> Map.containsKey s stateStructure)
                    |> List.length

                float covered / float sc.StateNames.Length

        { OrphanStates = orphanStates
          UnhandledCases = unhandledCases
          StateStructure = stateStructure
          TypeCoverage = typeCoverage }

let private enrichWithDerivedFields
    (allTypes: AnalyzedType list)
    (resources: UnifiedResource list)
    : UnifiedResource list =
    resources
    |> List.map (fun r ->
        { r with
            DerivedFields = computeDerivedFields r allTypes })

// ══════════════════════════════════════════════════════════════════════════════
// Spec file co-extraction: bridge transitions from spec files into extracted statecharts
// ══════════════════════════════════════════════════════════════════════════════

let internal specExtensions =
    [ ".wsd"; ".smcat"; ".alps.json"; ".alps.xml"; ".scxml" ]

let internal tryParseSpecFile (filePath: string) : Result<Frank.Statecharts.Ast.StatechartDocument, string> =
    try
        let text = File.ReadAllText(filePath)
        let ext = Path.GetExtension(filePath).ToLowerInvariant()

        let parseResult =
            if ext = ".wsd" then
                Some(Frank.Statecharts.Wsd.Parser.parseWsd text)
            elif filePath.EndsWith(".alps.json", System.StringComparison.OrdinalIgnoreCase) then
                Some(Frank.Statecharts.Alps.JsonParser.parseAlpsJson text)
            elif filePath.EndsWith(".alps.xml", System.StringComparison.OrdinalIgnoreCase) then
                Some(Frank.Statecharts.Alps.XmlParser.parseAlpsXml text)
            elif ext = ".smcat" then
                Some(Frank.Statecharts.Smcat.Parser.parseSmcat text)
            elif ext = ".scxml" then
                Some(Frank.Statecharts.Scxml.Parser.parseString text)
            else
                None

        match parseResult with
        | Some r -> Ok r.Document
        | None -> Error $"Unsupported spec file format: '{filePath}'"
    with ex ->
        Error $"Failed to parse spec file '{filePath}': {ex.Message}"

/// Find all spec files in {projectDir}/specs/ directory.
let internal findSpecFiles (projectDir: string) : string list =
    let specsDir = Path.Combine(projectDir, "specs")

    if Directory.Exists(specsDir) then
        Directory.GetFiles(specsDir)
        |> Array.filter (fun f ->
            specExtensions
            |> List.exists (fun ext -> f.EndsWith(ext, System.StringComparison.OrdinalIgnoreCase)))
        |> Array.toList
    else
        []

/// Extract state names from a StatechartDocument for matching against resources.
/// Collects both source and target states from transitions, plus explicit state declarations.
let internal documentStateNames (doc: Frank.Statecharts.Ast.StatechartDocument) : Set<string> =
    doc.Elements
    |> List.collect (fun elem ->
        match elem with
        | Frank.Statecharts.Ast.StateDecl node -> node.Identifier |> Option.toList
        | Frank.Statecharts.Ast.TransitionElement edge -> edge.Source :: (edge.Target |> Option.toList)
        | _ -> [])
    |> Set.ofList

/// Check if a spec document's states overlap sufficiently with a resource's states.
let internal statesOverlap (docStates: Set<string>) (resourceStates: Set<string>) : bool =
    let overlap = Set.intersect resourceStates docStates
    overlap.Count > 0 && overlap.Count * 2 >= resourceStates.Count

/// Check if a parsed spec document matches a single resource by state name overlap.
let internal matchesResource (docStates: Set<string>) (resource: UnifiedResource) : bool =
    match resource.Statechart with
    | Some sc -> statesOverlap docStates (Set.ofList sc.StateNames)
    | None -> false

/// Match a parsed spec document to a resource by state name overlap.
let internal matchDocToResource
    (doc: Frank.Statecharts.Ast.StatechartDocument)
    (resources: UnifiedResource list)
    : UnifiedResource option =
    let docStates = documentStateNames doc
    resources |> List.tryFind (matchesResource docStates)

/// Pure function: merge transitions from pre-parsed spec documents into resources.
/// For resources with statecharts that have empty transitions, find a matching
/// spec document by state name overlap and apply its transitions and roles.
let internal applySpecTransitions
    (docs: Frank.Statecharts.Ast.StatechartDocument list)
    (resources: UnifiedResource list)
    : UnifiedResource list =
    if docs.IsEmpty then
        resources
    else
        let docsWithStates = docs |> List.map (fun doc -> doc, documentStateNames doc)

        resources
        |> List.map (fun r ->
            match r.Statechart with
            | Some sc when sc.Transitions.IsEmpty ->
                let matchingDoc =
                    docsWithStates
                    |> List.tryPick (fun (doc, docStates) -> if matchesResource docStates r then Some doc else None)

                match matchingDoc with
                | Some doc ->
                    let transitions = TransitionExtractor.extract doc
                    let specRoles = TransitionExtractor.extractRoles doc

                    let mergedRoles =
                        if not sc.Roles.IsEmpty then sc.Roles
                        elif not specRoles.IsEmpty then specRoles
                        else []

                    { r with
                        Statechart =
                            Some
                                { sc with
                                    Transitions = transitions
                                    Roles = mergedRoles } }
                | None -> r
            | _ -> r)

/// Co-extract transitions from spec files and merge into resources.
/// Returns enriched resources and any parse warnings.
/// This is the impure shell: does file I/O, then delegates to pure applySpecTransitions.
let internal enrichWithSpecTransitions
    (projectDir: string)
    (resources: UnifiedResource list)
    : UnifiedResource list * string list =
    let specFiles = findSpecFiles projectDir

    if specFiles.IsEmpty then
        resources, []
    else
        let parseResults = specFiles |> List.map (fun f -> f, tryParseSpecFile f)

        let parseWarnings =
            parseResults
            |> List.choose (fun (_, r) ->
                match r with
                | Error msg -> Some msg
                | Ok _ -> None)

        let docs =
            parseResults
            |> List.choose (fun (_, r) ->
                match r with
                | Ok doc -> Some doc
                | Error _ -> None)

        applySpecTransitions docs resources, parseWarnings

// ══════════════════════════════════════════════════════════════════════════════
// Public API
// ══════════════════════════════════════════════════════════════════════════════

/// Extract unified resource descriptions from an F# project using FCS.
/// Performs a single FCS typecheck and produces both type and behavioral data.
let extract (projectPath: string) : Async<Result<UnifiedResource list, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error(FileNotFound projectPath)
        else
            match! ProjectLoader.loadProject projectPath with
            | Error msg -> return Error(AssemblyLoadError(projectPath, msg))
            | Ok loaded ->
                // Phase 1: Single-pass syntax walk
                let syntaxFindings = findAllResources loaded.ParsedFiles

                // Phase 2: Single-pass typed AST walk
                let typedResult = analyzeTypedAst loaded.CheckResults

                // Phase 3: Cross-reference and build UnifiedResource records
                let resources = buildUnifiedResources syntaxFindings typedResult

                // Phase 3.5: Co-extract transitions from spec files
                let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))
                let withTransitions, _specWarnings = enrichWithSpecTransitions projectDir resources

                // Phase 4: Associate types with resources
                let withTypes = associateTypes withTransitions typedResult.AnalyzedTypes

                // Phase 5: Compute derived fields
                let enriched = enrichWithDerivedFields typedResult.AnalyzedTypes withTypes

                return Ok enriched
    }

/// Result of loading or extracting resources.
type LoadResult =
    { Resources: UnifiedResource list
      FromCache: bool }

/// Load resources from cache if fresh, otherwise extract from source and write cache.
/// Shared by all commands that need resources (generate, validate, project).
/// Cache write failures are logged but do not fail the operation.
let loadOrExtract (projectPath: string) (force: bool) : Async<Result<LoadResult, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error(FileNotFound projectPath)
        else
            let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))

            match UnifiedCache.tryLoadFresh projectDir force with
            | Ok state ->
                return
                    Ok
                        { Resources = state.Resources
                          FromCache = true }
            | Error _ ->
                match! extract projectPath with
                | Ok resources ->
                    match UnifiedCache.saveExtractionState projectDir resources "" [] with
                    | Ok _ -> ()
                    | Error msg -> eprintfn "Warning: failed to write cache: %s" msg

                    return
                        Ok
                            { Resources = resources
                              FromCache = false }
                | Error e -> return Error e
    }
