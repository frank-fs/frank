module Frank.Analyzers.DuplicateHandlerAnalyzer

open FSharp.Analyzers.SDK
open FSharp.Analyzers.SDK.ASTCollecting
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// HTTP methods supported by Frank's ResourceBuilder
type HttpMethod =
    | GET
    | POST
    | PUT
    | DELETE
    | PATCH
    | HEAD
    | OPTIONS
    | CONNECT
    | TRACE

/// Set of HTTP method operation names (lowercase) from Frank's ResourceBuilder
let httpMethodOperations =
    Set.ofList [ "get"; "post"; "put"; "delete"; "patch"; "head"; "options"; "connect"; "trace" ]

/// Try to extract HTTP method from datastar operation with explicit method argument
let tryGetDatastarMethodFromArg (argExpr: SynExpr) : string option =
    match argExpr with
    | SynExpr.LongIdent(longDotId = SynLongIdent(id = ids)) ->
        let methodName = (List.last ids).idText
        // Check for HttpMethods.Get, HttpMethods.Post, etc.
        if List.length ids >= 2 then
            let typeName = (List.item (List.length ids - 2) ids).idText
            if typeName = "HttpMethods" then
                Some(methodName.ToUpperInvariant())
            else
                None
        else
            None
    | SynExpr.Ident ident ->
        let name = ident.idText
        // Direct method name like Get, Post, etc.
        let upperName = name.ToUpperInvariant()
        if [ "GET"; "POST"; "PUT"; "DELETE"; "PATCH"; "HEAD"; "OPTIONS"; "CONNECT"; "TRACE" ] |> List.contains upperName then
            Some upperName
        else
            None
    | _ -> None

/// Create a message for a duplicate HTTP handler
let createDuplicateMessage (methodName: string) (duplicateRange: range) (firstRange: range) : Message =
    {
        Type = "Duplicate HTTP handler"
        Message = sprintf "HTTP method '%s' handler is already defined for this resource at line %d. Only one handler per HTTP method is allowed." methodName firstRange.StartLine
        Code = "FRANK001"
        Severity = Severity.Warning
        Range = duplicateRange
        Fixes = []
    }

/// Analyze a parsed F# file for duplicate HTTP handlers
let analyzeFile (parseTree: ParsedInput) : Message list =
    let messages = ResizeArray<Message>()

    // Use a mutable stack to track context per CE block
    let contextStack = ResizeArray<System.Collections.Generic.Dictionary<string, range>>()

    let pushContext () =
        contextStack.Add(System.Collections.Generic.Dictionary<string, range>())

    let popContext () =
        if contextStack.Count > 0 then
            contextStack.RemoveAt(contextStack.Count - 1)

    let tryRegisterMethod (methodName: string) (r: range) =
        if contextStack.Count > 0 then
            let current = contextStack.[contextStack.Count - 1]
            if current.ContainsKey(methodName) then
                // Duplicate found
                messages.Add(createDuplicateMessage methodName r current.[methodName])
            else
                // Register this method
                current.[methodName] <- r

    // Walk the tree ourselves for CE detection using named field patterns
    let rec walkExprForCE (expr: SynExpr) =
        match expr with
        | SynExpr.ComputationExpr(expr = bodyExpr) ->
            // Push context for this CE
            pushContext()
            // Walk body
            walkExprForCE bodyExpr
            // Pop context
            popContext()

        | SynExpr.App(funcExpr = funcExpr; argExpr = argExpr; range = r) ->
            // Track whether we handled a datastar curried application (to avoid double-processing)
            let mutable handledDatastarCurried = false

            // Check if this is an HTTP method operation (only if inside a CE)
            if contextStack.Count > 0 then
                match funcExpr with
                | SynExpr.Ident ident ->
                    let name = ident.idText.ToLowerInvariant()
                    if httpMethodOperations.Contains name then
                        tryRegisterMethod (name.ToUpperInvariant()) r
                    elif name = "datastar" then
                        match tryGetDatastarMethodFromArg argExpr with
                        | Some explicitMethod -> tryRegisterMethod explicitMethod r
                        | None -> tryRegisterMethod "GET" r

                | SynExpr.App(funcExpr = innerFunc; argExpr = methodArg) ->
                    match innerFunc with
                    | SynExpr.Ident ident when ident.idText.ToLowerInvariant() = "datastar" ->
                        match tryGetDatastarMethodFromArg methodArg with
                        | Some explicitMethod -> tryRegisterMethod explicitMethod r
                        | None -> tryRegisterMethod "GET" r
                        handledDatastarCurried <- true  // Mark that we handled this
                    | _ -> ()

                | _ -> ()

            // Continue walking - but skip funcExpr if we already handled datastar curried application
            if not handledDatastarCurried then
                walkExprForCE funcExpr
            walkExprForCE argExpr

        | SynExpr.Sequential(expr1 = expr1; expr2 = expr2) ->
            walkExprForCE expr1
            walkExprForCE expr2

        | SynExpr.Paren(expr = innerExpr) ->
            walkExprForCE innerExpr

        | SynExpr.Lambda(body = body) ->
            walkExprForCE body

        | SynExpr.IfThenElse(ifExpr = ifExpr; thenExpr = thenExpr; elseExpr = elseExprOpt) ->
            walkExprForCE ifExpr
            walkExprForCE thenExpr
            elseExprOpt |> Option.iter walkExprForCE

        | SynExpr.Match(expr = matchExpr; clauses = clauses) ->
            walkExprForCE matchExpr
            for clause in clauses do
                match clause with
                | SynMatchClause(whenExpr = whenExprOpt; resultExpr = resultExpr) ->
                    whenExprOpt |> Option.iter walkExprForCE
                    walkExprForCE resultExpr

        | SynExpr.LetOrUse(bindings = bindings; body = body) ->
            for binding in bindings do
                match binding with
                | SynBinding(expr = expr) ->
                    walkExprForCE expr
            walkExprForCE body

        | SynExpr.Tuple(exprs = exprs) ->
            for e in exprs do
                walkExprForCE e

        | SynExpr.ArrayOrList(exprs = exprs) ->
            for e in exprs do
                walkExprForCE e

        | SynExpr.Record(copyInfo = copyInfoOpt; recordFields = fields) ->
            copyInfoOpt |> Option.iter (fun (e, _) -> walkExprForCE e)
            for field in fields do
                match field with
                | SynExprRecordField(expr = exprOpt) ->
                    exprOpt |> Option.iter walkExprForCE

        | SynExpr.ObjExpr(argOptions = argOpt; bindings = bindings) ->
            argOpt |> Option.iter (fun (e, _) -> walkExprForCE e)
            for binding in bindings do
                match binding with
                | SynBinding(expr = expr) ->
                    walkExprForCE expr

        | SynExpr.Do(expr = doExpr) ->
            walkExprForCE doExpr

        | SynExpr.DoBang(expr = doExpr) ->
            walkExprForCE doExpr

        | SynExpr.YieldOrReturn(expr = yieldExpr) ->
            walkExprForCE yieldExpr

        | SynExpr.YieldOrReturnFrom(expr = yieldExpr) ->
            walkExprForCE yieldExpr

        | SynExpr.TryWith(tryExpr = tryExpr; withCases = withCases) ->
            walkExprForCE tryExpr
            for withCase in withCases do
                match withCase with
                | SynMatchClause(whenExpr = whenExprOpt; resultExpr = resultExpr) ->
                    whenExprOpt |> Option.iter walkExprForCE
                    walkExprForCE resultExpr

        | SynExpr.TryFinally(tryExpr = tryExpr; finallyExpr = finallyExpr) ->
            walkExprForCE tryExpr
            walkExprForCE finallyExpr

        | SynExpr.For(identBody = identBody; toBody = toBody; doBody = doBody) ->
            walkExprForCE identBody
            walkExprForCE toBody
            walkExprForCE doBody

        | SynExpr.ForEach(enumExpr = enumExpr; bodyExpr = bodyExpr) ->
            walkExprForCE enumExpr
            walkExprForCE bodyExpr

        | SynExpr.While(whileExpr = whileExpr; doExpr = doExpr) ->
            walkExprForCE whileExpr
            walkExprForCE doExpr

        | _ -> ()

    // Use the SDK walker to find module-level expressions, then process them
    let exprCollector =
        { new SyntaxCollectorBase() with
            override _.WalkExpr(_, expr: SynExpr) =
                walkExprForCE expr
        }

    walkAst exprCollector parseTree

    messages |> Seq.toList

[<Literal>]
let name = "DuplicateHandlerAnalyzer"

[<Literal>]
let shortDescription = "Detects duplicate HTTP method handlers in Frank resource definitions"

[<Literal>]
let helpUri = "https://github.com/frank-fs/frank/issues/59"

/// Editor analyzer for IDE integration (Ionide, Visual Studio, Rider)
[<EditorAnalyzer(name, shortDescription, helpUri)>]
let editorAnalyzer: Analyzer<EditorContext> =
    fun (ctx: EditorContext) ->
        async {
            return analyzeFile ctx.ParseFileResults.ParseTree
        }

/// CLI analyzer for command-line and CI/CD usage
[<CliAnalyzer(name, shortDescription, helpUri)>]
let cliAnalyzer: Analyzer<CliContext> =
    fun (ctx: CliContext) ->
        async {
            return analyzeFile ctx.ParseFileResults.ParseTree
        }
