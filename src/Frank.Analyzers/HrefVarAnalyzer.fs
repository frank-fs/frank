module Frank.Analyzers.HrefVarAnalyzer

open FSharp.Analyzers.SDK
open FSharp.Analyzers.SDK.ASTCollecting
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Frank.JsonHome

// Note: only the NAME argument needs to be a string literal (that's the
// value the diff runs against); the uri argument is deliberately unmatched
// (`argExpr = _`) since diff never looks at it -- requiring it to also be a
// literal would silently drop declarations using a computed/shared uri
// value. LetOrUse is handled so a `let` between CE statements doesn't drop
// a real hrefVar declaration into a false-positive "Missing".
let rec collectHrefVars (bodyExpr: SynExpr) : (string * range) list =
    match bodyExpr with
    | SynExpr.App(
        funcExpr = SynExpr.App(funcExpr = SynExpr.Ident hrefVarIdent;
                                argExpr = SynExpr.Const(constant = SynConst.String(text = varName)));
        argExpr = _;
        range = r) when hrefVarIdent.idText = "hrefVar" -> [ varName, r ]

    | SynExpr.Sequential(expr1 = e1; expr2 = e2) -> collectHrefVars e1 @ collectHrefVars e2
    | SynExpr.Paren(expr = e) -> collectHrefVars e
    | SynExpr.LetOrUse(bindings = bindings; body = body) ->
        (bindings |> List.collect (fun (SynBinding(expr = e)) -> collectHrefVars e))
        @ collectHrefVars body
    | _ -> []

let tryResourceLiteral (expr: SynExpr) : (string * range * SynExpr) option =
    match expr with
    | SynExpr.App(
        funcExpr = SynExpr.App(funcExpr = SynExpr.Ident resourceIdent;
                                argExpr = SynExpr.Const(constant = SynConst.String(text = routeTemplate); range = templateRange));
        argExpr = SynExpr.ComputationExpr(expr = bodyExpr)) when resourceIdent.idText = "resource" ->
        Some(routeTemplate, templateRange, bodyExpr)
    | _ -> None

let createMissingMessage (varName: string) (resourceRange: range) : Message =
    { Type = "hrefVar / route template mismatch"
      Message = sprintf "Route template variable '{%s}' has no matching hrefVar declaration in this resource." varName
      Code = "FRANK003"
      Severity = Severity.Error
      Range = resourceRange
      Fixes = [] }

let createExtraMessage (varName: string) (declRange: range) : Message =
    { Type = "hrefVar / route template mismatch"
      Message = sprintf "hrefVar '%s' does not match any variable in this resource's route template." varName
      Code = "FRANK003"
      Severity = Severity.Error
      Range = declRange
      Fixes = [] }

let analyzeFile (parseTree: ParsedInput) : Message list =
    let messages = ResizeArray<Message>()

    let rec walk (expr: SynExpr) =
        match tryResourceLiteral expr with
        | Some(routeTemplate, templateRange, bodyExpr) ->
            let declared = collectHrefVars bodyExpr
            let mismatch = HrefVarValidation.diff routeTemplate (declared |> List.map fst)

            for varName in mismatch.Missing do
                messages.Add(createMissingMessage varName templateRange)

            for varName in mismatch.Extra do
                let declRange = declared |> List.find (fun (n, _) -> n = varName) |> snd
                messages.Add(createExtraMessage varName declRange)

        | None ->
            match expr with
            | SynExpr.App(funcExpr = f; argExpr = a) ->
                walk f
                walk a
            | SynExpr.ComputationExpr(expr = e) -> walk e
            | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
                walk e1
                walk e2
            | SynExpr.Paren(expr = e) -> walk e
            | SynExpr.Lambda(body = b) -> walk b
            | SynExpr.LetOrUse(bindings = bindings; body = body) ->
                for binding in bindings do
                    match binding with
                    | SynBinding(expr = e) -> walk e

                walk body
            | SynExpr.IfThenElse(ifExpr = i; thenExpr = t; elseExpr = eOpt) ->
                walk i
                walk t
                eOpt |> Option.iter walk
            | _ -> ()

    let exprCollector =
        { new SyntaxCollectorBase() with
            override _.WalkExpr(_, expr: SynExpr) = walk expr }

    walkAst exprCollector parseTree

    messages |> List.ofSeq

[<Literal>]
let name = "HrefVarAnalyzer"

[<Literal>]
let shortDescription =
    "Detects hrefVar declarations that don't match the resource's route template variables (FRANK003)"

[<Literal>]
let helpUri = "https://github.com/frank-fs/frank/issues/474"

[<EditorAnalyzer(name, shortDescription, helpUri)>]
let editorAnalyzer: Analyzer<EditorContext> =
    fun (ctx: EditorContext) -> async { return analyzeFile ctx.ParseFileResults.ParseTree }

[<CliAnalyzer(name, shortDescription, helpUri)>]
let cliAnalyzer: Analyzer<CliContext> =
    fun (ctx: CliContext) -> async { return analyzeFile ctx.ParseFileResults.ParseTree }
