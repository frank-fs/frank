namespace Frank.Cli.Core.Analysis

open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

type HttpMethod = Get | Post | Put | Delete | Patch | Head | Options

type SourceLocation = { File: string; Line: int; Column: int }

type AnalyzedResource = {
    RouteTemplate: string
    Name: string option
    HttpMethods: HttpMethod list
    HasLinkedData: bool
    Location: SourceLocation
}

module AstAnalyzer =

    let httpMethodNames = Set.ofList ["get"; "post"; "put"; "delete"; "patch"; "head"; "options"]

    let parseHttpMethod (name: string) =
        match name.ToLowerInvariant() with
        | "get" -> Some Get
        | "post" -> Some Post
        | "put" -> Some Put
        | "delete" -> Some Delete
        | "patch" -> Some Patch
        | "head" -> Some Head
        | "options" -> Some Options
        | _ -> None

    /// Walk a CE body expression collecting HTTP methods, name, and linkedData
    let rec private walkCeBody
        (methods: ResizeArray<HttpMethod>)
        (nameRef: string option ref)
        (linkedDataRef: bool ref)
        (expr: SynExpr) =
        match expr with
        | SynExpr.Sequential(expr1 = expr1; expr2 = expr2) ->
            walkCeBody methods nameRef linkedDataRef expr1
            walkCeBody methods nameRef linkedDataRef expr2

        | SynExpr.App(funcExpr = SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = argExpr); argExpr = _handlerExpr) ->
            let idText = ident.idText.ToLowerInvariant()
            if httpMethodNames.Contains idText then
                parseHttpMethod idText |> Option.iter methods.Add
            elif idText = "name" then
                match argExpr with
                | SynExpr.Const(SynConst.String(text, _, _), _) ->
                    nameRef.Value <- Some text
                | _ -> ()
            elif idText = "linkeddata" then
                linkedDataRef.Value <- true

        | SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = argExpr) ->
            let idText = ident.idText.ToLowerInvariant()
            if httpMethodNames.Contains idText then
                parseHttpMethod idText |> Option.iter methods.Add
            elif idText = "name" then
                match argExpr with
                | SynExpr.Const(SynConst.String(text, _, _), _) ->
                    nameRef.Value <- Some text
                | _ -> ()
            elif idText = "linkeddata" then
                linkedDataRef.Value <- true

        | SynExpr.Ident ident ->
            if ident.idText.ToLowerInvariant() = "linkeddata" then
                linkedDataRef.Value <- true

        | SynExpr.LetOrUse(bindings = _; body = body) ->
            walkCeBody methods nameRef linkedDataRef body

        | SynExpr.Paren(expr = innerExpr) ->
            walkCeBody methods nameRef linkedDataRef innerExpr

        | SynExpr.Lambda(body = body) ->
            walkCeBody methods nameRef linkedDataRef body

        | _ -> ()

    /// Try to extract an AnalyzedResource from a resource CE expression
    let private tryExtractResource (expr: SynExpr) (file: string) : AnalyzedResource option =
        // Pattern: resource "/route" { ... }
        // AST: SynExpr.App(funcExpr = SynExpr.App(funcExpr = SynExpr.Ident "resource", argExpr = SynConst.String route), argExpr = SynExpr.ComputationExpr(ceBody))
        match expr with
        | SynExpr.App(
            funcExpr = SynExpr.App(
                funcExpr = SynExpr.Ident ident;
                argExpr = SynExpr.Const(SynConst.String(routeTemplate, _, _), _));
            argExpr = SynExpr.ComputationExpr(expr = ceBody);
            range = r) when ident.idText = "resource" ->
            let methods = ResizeArray<HttpMethod>()
            let nameRef = ref None
            let linkedDataRef = ref false
            walkCeBody methods nameRef linkedDataRef ceBody
            Some {
                RouteTemplate = routeTemplate
                Name = nameRef.Value
                HttpMethods = methods |> Seq.toList
                HasLinkedData = linkedDataRef.Value
                Location = { File = file; Line = r.StartLine; Column = r.StartColumn }
            }
        | _ -> None

    /// Walk an expression tree looking for resource CE invocations
    let rec private walkExpr (file: string) (results: ResizeArray<AnalyzedResource>) (expr: SynExpr) =
        match tryExtractResource expr file with
        | Some resource ->
            results.Add(resource)
        | None ->
            match expr with
            | SynExpr.App(funcExpr = funcExpr; argExpr = argExpr) ->
                walkExpr file results funcExpr
                walkExpr file results argExpr
            | SynExpr.Sequential(expr1 = expr1; expr2 = expr2) ->
                walkExpr file results expr1
                walkExpr file results expr2
            | SynExpr.LetOrUse(bindings = bindings; body = body) ->
                for binding in bindings do
                    match binding with
                    | SynBinding(expr = bindExpr) ->
                        walkExpr file results bindExpr
                walkExpr file results body
            | SynExpr.Paren(expr = innerExpr) ->
                walkExpr file results innerExpr
            | SynExpr.Lambda(body = body) ->
                walkExpr file results body
            | SynExpr.ComputationExpr(expr = ceBody) ->
                walkExpr file results ceBody
            | SynExpr.IfThenElse(ifExpr = ifExpr; thenExpr = thenExpr; elseExpr = elseExprOpt) ->
                walkExpr file results ifExpr
                walkExpr file results thenExpr
                elseExprOpt |> Option.iter (walkExpr file results)
            | SynExpr.Match(expr = matchExpr; clauses = clauses) ->
                walkExpr file results matchExpr
                for clause in clauses do
                    match clause with
                    | SynMatchClause(resultExpr = resultExpr) ->
                        walkExpr file results resultExpr
            | SynExpr.Tuple(exprs = exprs) ->
                for e in exprs do walkExpr file results e
            | SynExpr.ArrayOrList(exprs = exprs) ->
                for e in exprs do walkExpr file results e
            | _ -> ()

    /// Analyze a single parsed file for resource CE invocations
    let analyzeFile (parsedInput: ParsedInput) : AnalyzedResource list =
        let results = ResizeArray<AnalyzedResource>()
        match parsedInput with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            for moduleOrNs in modules do
                match moduleOrNs with
                | SynModuleOrNamespace(decls = decls) ->
                    for decl in decls do
                        match decl with
                        | SynModuleDecl.Let(bindings = bindings) ->
                            for binding in bindings do
                                match binding with
                                | SynBinding(expr = expr) ->
                                    walkExpr (parsedInput.FileName) results expr
                        | SynModuleDecl.Expr(expr = expr) ->
                            walkExpr (parsedInput.FileName) results expr
                        | _ -> ()
        | ParsedInput.SigFile _ -> ()
        results |> Seq.toList

    /// Analyze multiple parsed files for resource CE invocations
    let analyzeFiles (parsedInputs: ParsedInput list) : AnalyzedResource list =
        parsedInputs |> List.collect analyzeFile
