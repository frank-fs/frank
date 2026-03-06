namespace Frank.Cli.Core.Analysis

open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

type HttpMethod =
    | Get
    | Post
    | Put
    | Delete
    | Patch
    | Head
    | Options

type SourceLocation =
    { File: string; Line: int; Column: int }

type AnalyzedResource =
    { RouteTemplate: string
      Name: string option
      HttpMethods: HttpMethod list
      HasLinkedData: bool
      Location: SourceLocation }

module AstAnalyzer =

    let httpMethodNames =
        Set.ofList [ "get"; "post"; "put"; "delete"; "patch"; "head"; "options" ]

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

    type private CeAccum =
        { Methods: HttpMethod list
          Name: string option
          HasLinkedData: bool }

    let private emptyCeAccum =
        { Methods = []
          Name = None
          HasLinkedData = false }

    /// Walk a CE body expression collecting HTTP methods, name, and linkedData
    let rec private walkCeBody (acc: CeAccum) (expr: SynExpr) : CeAccum =
        match expr with
        | SynExpr.Sequential(expr1 = expr1; expr2 = expr2) -> walkCeBody (walkCeBody acc expr1) expr2

        | SynExpr.App(funcExpr = SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = argExpr); argExpr = _handlerExpr) ->
            let idText = ident.idText.ToLowerInvariant()

            if httpMethodNames.Contains idText then
                match parseHttpMethod idText with
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
                match parseHttpMethod idText with
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

        | SynExpr.LetOrUse(bindings = _; body = body) -> walkCeBody acc body

        | SynExpr.Paren(expr = innerExpr) -> walkCeBody acc innerExpr

        | SynExpr.Lambda(body = body) -> walkCeBody acc body

        | _ -> acc

    /// Try to extract an AnalyzedResource from a resource CE expression
    let private tryExtractResource (expr: SynExpr) (file: string) : AnalyzedResource option =
        // Pattern: resource "/route" { ... }
        // AST: SynExpr.App(funcExpr = SynExpr.App(funcExpr = SynExpr.Ident "resource", argExpr = SynConst.String route), argExpr = SynExpr.ComputationExpr(ceBody))
        match expr with
        | SynExpr.App(
            funcExpr = SynExpr.App(
                funcExpr = SynExpr.Ident ident; argExpr = SynExpr.Const(SynConst.String(routeTemplate, _, _), _))
            argExpr = SynExpr.ComputationExpr(expr = ceBody)
            range = r) when ident.idText = "resource" ->
            let result = walkCeBody emptyCeAccum ceBody

            Some
                { RouteTemplate = routeTemplate
                  Name = result.Name
                  HttpMethods = List.rev result.Methods
                  HasLinkedData = result.HasLinkedData
                  Location =
                    { File = file
                      Line = r.StartLine
                      Column = r.StartColumn } }
        | _ -> None

    /// Walk an expression tree looking for resource CE invocations
    let rec private walkExpr (file: string) (results: ResizeArray<AnalyzedResource>) (expr: SynExpr) =
        match tryExtractResource expr file with
        | Some resource -> results.Add(resource)
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
                    | SynBinding(expr = bindExpr) -> walkExpr file results bindExpr

                walkExpr file results body
            | SynExpr.Paren(expr = innerExpr) -> walkExpr file results innerExpr
            | SynExpr.Lambda(body = body) -> walkExpr file results body
            | SynExpr.ComputationExpr(expr = ceBody) -> walkExpr file results ceBody
            | SynExpr.IfThenElse(ifExpr = ifExpr; thenExpr = thenExpr; elseExpr = elseExprOpt) ->
                walkExpr file results ifExpr
                walkExpr file results thenExpr
                elseExprOpt |> Option.iter (walkExpr file results)
            | SynExpr.Match(expr = matchExpr; clauses = clauses) ->
                walkExpr file results matchExpr

                for clause in clauses do
                    match clause with
                    | SynMatchClause(resultExpr = resultExpr) -> walkExpr file results resultExpr
            | SynExpr.Tuple(exprs = exprs) ->
                for e in exprs do
                    walkExpr file results e
            | SynExpr.ArrayOrList(exprs = exprs) ->
                for e in exprs do
                    walkExpr file results e
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
                                | SynBinding(expr = expr) -> walkExpr (parsedInput.FileName) results expr
                        | SynModuleDecl.Expr(expr = expr) -> walkExpr (parsedInput.FileName) results expr
                        | _ -> ()
        | ParsedInput.SigFile _ -> ()

        results |> Seq.toList

    /// Analyze multiple parsed files for resource CE invocations
    let analyzeFiles (parsedInputs: ParsedInput list) : AnalyzedResource list =
        parsedInputs |> List.collect analyzeFile
