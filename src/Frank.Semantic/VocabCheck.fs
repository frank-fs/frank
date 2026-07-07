module Frank.Semantic.VocabCheck

open System
open FSharp.Compiler.Syntax
open Frank.Semantic.LockFile

let private routeCoversNsPath (routePath: string) (nsPath: string) : bool =
    routePath = nsPath || nsPath.StartsWith(routePath + "/")

let private extractAbsolutePath (uri: string) : string option =
    match Uri.TryCreate(uri, UriKind.Absolute) with
    | true, u -> Some u.AbsolutePath
    | _ -> None

let private isDereferenceable (prefix: string) (lock: LockFile) : bool =
    Map.containsKey prefix lock.Vocabularies

let private nsUriFor (prefix: string) (lock: LockFile) : string option =
    match Map.tryFind prefix lock.DeclaredPrefixes with
    | Some uri -> Some uri
    | None -> Map.tryFind prefix lock.Vocabularies |> Option.map (fun entry -> entry.Uri)

let private isRouted (nsUri: string) (routes: string list) : bool =
    match extractAbsolutePath nsUri with
    | None -> false
    | Some nsPath -> routes |> List.exists (fun r -> routeCoversNsPath r nsPath)

/// Check each prefix in referencedNs against the lock and routes.
/// Returns the prefixes that are neither dereferenceable (in lock.Vocabularies)
/// nor routed (any route covers the namespace deref path).
/// No network I/O. Deterministic, offline-safe, CI-safe.
let checkUndereferenceableVocab (lock: LockFile) (routes: string list) (referencedNs: string list) : string list =
    referencedNs
    |> List.filter (fun prefix ->
        not (isDereferenceable prefix lock)
        && (match nsUriFor prefix lock with
            | None -> false
            | Some nsUri -> not (isRouted nsUri routes)))

// ── Route extraction from ParsedInput ────────────────────────────────────────

[<Literal>]
let private AstWalkDepthCap = 200

let private tryExtractResourceRoute (expr: SynExpr) : string option =
    match expr with
    | SynExpr.App(
        funcExpr = SynExpr.App(funcExpr = SynExpr.Ident ident; argExpr = SynExpr.Const(constant = synConst))
        argExpr = SynExpr.ComputationExpr _) when ident.idText = "resource" ->
        match synConst with
        | SynConst.String(route, _, _) -> Some route
        | _ -> None
    | _ -> None

let rec private walkExpr (depth: int) (expr: SynExpr) (acc: ResizeArray<string>) : unit =
    if depth > AstWalkDepthCap then
        ()
    else
        match tryExtractResourceRoute expr with
        | Some route -> acc.Add route
        | None ->
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

                for SynMatchClause(resultExpr = re; whenExpr = we) in clauses do
                    we |> Option.iter (fun e -> walkExpr (depth + 1) e acc)
                    walkExpr (depth + 1) re acc
            | _ -> ()

let rec private walkDecls (decls: SynModuleDecl list) (acc: ResizeArray<string>) : unit =
    for decl in decls do
        match decl with
        | SynModuleDecl.Let(_, bindings, _) ->
            for SynBinding(expr = e) in bindings do
                walkExpr 0 e acc
        | SynModuleDecl.Expr(e, _) -> walkExpr 0 e acc
        | SynModuleDecl.NestedModule(decls = innerDecls) -> walkDecls innerDecls acc
        | _ -> ()

/// Extract all resource route literals from a parsed F# file.
/// Pure: no network or file I/O. Bounded walk (cap = 200 nesting levels).
let extractRoutes (parseTree: ParsedInput) : string list =
    let acc = ResizeArray<string>()

    match parseTree with
    | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
        for SynModuleOrNamespace(decls = decls) in modules do
            walkDecls decls acc
    | ParsedInput.SigFile _ -> ()

    acc |> Seq.toList
