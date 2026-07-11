module Frank.Analyzers.AstExtractors

open System
open System.Text.RegularExpressions
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

// ── Shared depth cap (Holzmann #10) ──────────────────────────────────────────

/// Maximum AST walk depth for both walkExpr and walkDecls.
/// Cap-hit = emit no results, never throw. Prevents stack-overflow on
/// adversarially or pathologically nested source files.
[<Literal>]
let AstWalkDepthCap = 200

// ── Route matching (informational hint logic) ─────────────────────────────────

/// True iff routePath covers nsPath under the prefix-match rule:
/// exact match OR nsPath starts with (routePath stripped of trailing slash + "/").
/// Case-insensitive (ASP.NET Core route matching is case-insensitive by default).
let routeCoversNsPath (routes: string list) (nsPath: string) : bool =
    if String.IsNullOrEmpty nsPath then
        invalidArg (nameof nsPath) "must not be empty"

    let lowerNs = nsPath.ToLowerInvariant()

    routes
    |> List.exists (fun route ->
        let r = route.TrimEnd('/').ToLowerInvariant()
        lowerNs = r || lowerNs.StartsWith(r + "/"))

// ── Route extraction from ParsedInput ────────────────────────────────────────

let private tryExtractResourceRoute (expr: SynExpr) : string option =
    match expr with
    | SynExpr.App(
        funcExpr = SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = SynExpr.Const(constant = synConst))
        argExpr = SynExpr.ComputationExpr _) when ident.idText = "resource" ->
        match synConst with
        | SynConst.String(route, _, _) -> Some route
        | _ -> None
    | _ -> None

let rec private walkExprArms (depth: int) (expr: SynExpr) (acc: ResizeArray<string>) : unit =
    match expr with
    | SynExpr.App(funcExpr = e1; argExpr = e2) ->
        walkExpr (depth + 1) e1 acc
        walkExpr (depth + 1) e2 acc
    | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
        walkExpr (depth + 1) e1 acc
        walkExpr (depth + 1) e2 acc
    | SynExpr.LetOrUse(bindings = bindings; body = body) ->
        for SynBinding(expr = e) in bindings do
            walkExpr (depth + 1) e acc

        walkExpr (depth + 1) body acc
    | SynExpr.ComputationExpr(expr = e) -> walkExpr (depth + 1) e acc
    | SynExpr.Paren(expr = e) -> walkExpr (depth + 1) e acc
    | SynExpr.IfThenElse(ifExpr = e1; thenExpr = e2; elseExpr = e3) ->
        walkExpr (depth + 1) e1 acc
        walkExpr (depth + 1) e2 acc
        e3 |> Option.iter (fun e -> walkExpr (depth + 1) e acc)
    | SynExpr.Lambda(body = body) -> walkExpr (depth + 1) body acc
    | SynExpr.Match(expr = me; clauses = clauses) ->
        walkExpr (depth + 1) me acc
        let walkWhenClause e = walkExpr (depth + 1) e acc

        for SynMatchClause(resultExpr = re; whenExpr = we) in clauses do
            we |> Option.iter walkWhenClause
            walkExpr (depth + 1) re acc
    | _ -> ()

and private walkExpr (depth: int) (expr: SynExpr) (acc: ResizeArray<string>) : unit =
    if depth > AstWalkDepthCap then
        ()
    else
        match tryExtractResourceRoute expr with
        | Some route -> acc.Add route
        | None -> walkExprArms depth expr acc

// ── Generic decl walker (shared by routes and terms) ─────────────────────────

let rec private walkDeclsWith (walkExprFn: int -> SynExpr -> unit) (depth: int) (decls: SynModuleDecl list) : unit =
    if depth > AstWalkDepthCap then
        ()
    else
        for decl in decls do
            handleDeclWith walkExprFn depth decl

and private handleDeclWith (walkExprFn: int -> SynExpr -> unit) (depth: int) (decl: SynModuleDecl) : unit =
    match decl with
    | SynModuleDecl.Let(_, bindings, _) ->
        for SynBinding(expr = e) in bindings do
            walkExprFn (depth + 1) e
    | SynModuleDecl.Expr(e, _) -> walkExprFn (depth + 1) e
    | SynModuleDecl.NestedModule(decls = innerDecls) -> walkDeclsWith walkExprFn (depth + 1) innerDecls
    | _ -> ()

/// Extract all resource route literals from a parsed F# file.
/// Pure: no network or file I/O. Bounded walk (cap = AstWalkDepthCap nesting levels).
let extractRoutes (parseTree: ParsedInput) : string list =
    let acc = ResizeArray<string>()

    match parseTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
        for SynModuleOrNamespace(decls = decls) in modules do
            walkDeclsWith (fun d e -> walkExpr d e acc) 0 decls
    | ParsedInput.SigFile _ -> ()

    acc |> Seq.toList

// ── Term extraction from ParsedInput ─────────────────────────────────────────

// CURIE pattern: ^[a-zA-Z][a-zA-Z0-9_]*:[a-zA-Z][a-zA-Z0-9_]*$
// Excludes URLs (://), MIME types (/), and other colon-containing strings.
let private curiePattern =
    Regex(@"^[a-zA-Z][a-zA-Z0-9_]*:[a-zA-Z][a-zA-Z0-9_]*$", RegexOptions.Compiled)

let private tryExtractCurie (s: string) : (string * string) option =
    if curiePattern.IsMatch s then
        let idx = s.IndexOf(':')
        Some(s.[0 .. idx - 1], s.[idx + 1 ..])
    else
        None

let rec private walkExprForTermsArms (depth: int) (expr: SynExpr) (acc: ResizeArray<string * string>) : unit =
    match expr with
    | SynExpr.App(funcExpr = e1; argExpr = e2) ->
        walkExprForTerms (depth + 1) e1 acc
        walkExprForTerms (depth + 1) e2 acc
    | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
        walkExprForTerms (depth + 1) e1 acc
        walkExprForTerms (depth + 1) e2 acc
    | SynExpr.LetOrUse(bindings = bindings; body = body) ->
        for SynBinding(expr = e) in bindings do
            walkExprForTerms (depth + 1) e acc

        walkExprForTerms (depth + 1) body acc
    | SynExpr.ComputationExpr(expr = e) -> walkExprForTerms (depth + 1) e acc
    | SynExpr.Paren(expr = e) -> walkExprForTerms (depth + 1) e acc
    | SynExpr.IfThenElse(ifExpr = e1; thenExpr = e2; elseExpr = e3) ->
        walkExprForTerms (depth + 1) e1 acc
        walkExprForTerms (depth + 1) e2 acc
        e3 |> Option.iter (fun e -> walkExprForTerms (depth + 1) e acc)
    | SynExpr.Lambda(body = body) -> walkExprForTerms (depth + 1) body acc
    | SynExpr.Match(expr = me; clauses = clauses) ->
        walkExprForTerms (depth + 1) me acc
        let walkWhenClause e = walkExprForTerms (depth + 1) e acc

        for SynMatchClause(resultExpr = re; whenExpr = we) in clauses do
            we |> Option.iter walkWhenClause
            walkExprForTerms (depth + 1) re acc
    | SynExpr.Tuple(exprs = exprs) ->
        for e in exprs do
            walkExprForTerms (depth + 1) e acc
    | _ -> ()

and private walkExprForTerms (depth: int) (expr: SynExpr) (acc: ResizeArray<string * string>) : unit =
    if depth > AstWalkDepthCap then
        ()
    else
        match expr with
        | SynExpr.Const(SynConst.String(s, _, _), _) -> tryExtractCurie s |> Option.iter acc.Add
        | _ -> walkExprForTermsArms depth expr acc

/// Extract all CURIE-pattern string literals (prefix:localname) from a parsed F# file.
/// Returns (prefix, localname) pairs. Pure, no I/O, bounded.
let extractReferencedTerms (parseTree: ParsedInput) : (string * string) list =
    let acc = ResizeArray<string * string>()

    match parseTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
        for SynModuleOrNamespace(decls = decls) in modules do
            walkDeclsWith (fun d e -> walkExprForTerms d e acc) 0 decls
    | ParsedInput.SigFile _ -> ()

    acc |> Seq.toList

// ── File range utility ────────────────────────────────────────────────────────

/// Extract the source file name from a ParsedInput for constructing real ranges.
let fileNameOf (parseTree: ParsedInput) : string =
    match parseTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(fileName = fn)) -> fn
    | ParsedInput.SigFile(ParsedSigFileInput(fileName = fn)) -> fn

/// A real range pointing to the start of the file (not Range.range0).
let fileStartRange (parseTree: ParsedInput) : Range =
    let fn = fileNameOf parseTree
    Range.mkRange fn (Position.mkPos 1 0) (Position.mkPos 1 1)
