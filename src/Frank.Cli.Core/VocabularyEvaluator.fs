module Frank.Cli.Core.VocabularyEvaluator

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open Frank.Semantic

// ── FCS typecheck ─────────────────────────────────────────────────────────────

/// Build FCS project options using script resolution on the primary (last) source file,
/// then typecheck all source files together. Returns implementation file contents.
let private typecheckFiles
    (assemblyRefs: string list)
    (sourceFiles: string list)
    : Result<FSharpImplementationFileContents list, string> =
    let primaryFile = List.last sourceFiles
    let extraRefArgs = assemblyRefs |> List.map (fun r -> $"-r:{r}") |> List.toArray
    let primaryText = SourceText.ofString (File.ReadAllText primaryFile)
    let checker = FSharpChecker.Create(keepAssemblyContents = true)

    let scriptOpts, diags =
        checker.GetProjectOptionsFromScript(
            primaryFile,
            primaryText,
            assumeDotNetFramework = false,
            useSdkRefs = true,
            otherFlags = extraRefArgs
        )
        |> Async.RunSynchronously

    let scriptErrors =
        diags
        |> List.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)

    if not scriptErrors.IsEmpty then
        let msg = scriptErrors |> List.map (fun d -> d.ToString()) |> String.concat "; "
        Error $"script options error: {msg}"
    else

        let opts =
            { scriptOpts with
                SourceFiles = sourceFiles |> List.toArray }

        let results = checker.ParseAndCheckProject(opts) |> Async.RunSynchronously

        let checkErrors =
            results.Diagnostics
            |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)

        if checkErrors.Length > 0 then
            let msg = checkErrors |> Array.map (fun d -> d.ToString()) |> String.concat "; "
            Error msg
        else
            Ok results.AssemblyContents.ImplementationFiles

// ── Type identity resolution ──────────────────────────────────────────────────

/// Resolve the FCS entity from a typeInstArgs type argument of a TypeOf/TypeDefOf call.
/// Returns Error for type abbreviations (C1) or typeof with constructed generics (C2).
let private resolveTypeArg (isTypeDefOf: bool) (t: FSharpType) : Result<string, string> =
    if not t.HasTypeDefinition then
        Error "type argument has no type definition (generic parameter or unknown type)"
    else

        let ent = t.TypeDefinition

        if ent.IsFSharpAbbreviation then
            Error $"type abbreviation '{ent.DisplayName}' cannot be mapped; use the underlying declared type instead"
        else

            match ent.TryFullName with
            | None -> Error $"type '{ent.DisplayName}' has no FullName (may be a compiler-generated or anonymous type)"
            | Some fullName ->
                if isTypeDefOf then
                    Ok fullName
                else
                    let hasConcreteArgs =
                        t.GenericArguments.Count > 0
                        && t.GenericArguments |> Seq.forall (fun a -> not a.IsGenericParameter)

                    if hasConcreteArgs then
                        Error
                            $"type '{ent.DisplayName}' is a constructed generic applied with typeof; use typedefof<{ent.DisplayName}<_>> for generic type definitions"
                    else
                        Ok fullName

// ── Argument extraction helpers ───────────────────────────────────────────────

/// Extract a string literal from a Const expression, or return Error.
let private requireConst (expr: FSharpExpr) : Result<string, string> =
    match expr with
    | FSharpExprPatterns.Const(:? string as s, _) -> Ok s
    | other -> Error $"expected string literal, got {other.GetType().Name}"

/// Extract a ProvOClass from a NewUnionCase expression, or return Error.
let private requireProvOClass (expr: FSharpExpr) : Result<ProvOClass, string> =
    match expr with
    | FSharpExprPatterns.NewUnionCase(_, case, _) ->
        match case.Name with
        | "Entity" -> Ok Entity
        | "Activity" -> Ok Activity
        | "Agent" -> Ok Agent
        | other -> Error $"unknown ProvOClass case '{other}'"
    | other -> Error $"expected ProvOClass union case, got {other.GetType().Name}"

/// Extract the resolved type key from a typeof/typedefof Call expression.
let private requireTypeKey (expr: FSharpExpr) : Result<string, string> =
    match expr with
    | FSharpExprPatterns.Call(_, mfv, _, typeInstArgs, _) when
        mfv.CompiledName = "TypeOf" || mfv.CompiledName = "TypeDefOf"
        ->
        match typeInstArgs with
        | [ t ] -> resolveTypeArg (mfv.CompiledName = "TypeDefOf") t
        | [] -> Error "typeof/typedefof call has no type instantiation arguments"
        | _ -> Error $"typeof/typedefof has unexpected arity {typeInstArgs.Length}"
    | other -> Error $"expected typeof/typedefof call, got {other.GetType().Name}"

// ── IRI resolution ────────────────────────────────────────────────────────────

/// Resolve a prefixed IRI, converting InvalidOperationException (undeclared prefix) to Error.
let private tryResolveIri (prefixes: Map<string, Uri>) (iri: string) : Result<Uri, string> =
    try
        Ok(VocabularyRegistry.resolveIri prefixes iri)
    with :? InvalidOperationException as ex ->
        Error ex.Message

// ── Per-operation helpers (Task C) ────────────────────────────────────────────

/// Resolve IRI and insert into a singleton-value map, detecting conflicts.
let private insertSingleton<'K when 'K: comparison>
    (label: string)
    (key: 'K)
    (resolved: Uri)
    (getMap: VocabularyRegistry -> Map<'K, Uri>)
    (setMap: VocabularyRegistry -> Map<'K, Uri> -> VocabularyRegistry)
    (state: VocabularyRegistry)
    : Result<VocabularyRegistry, string> =
    match Map.tryFind key (getMap state) with
    | Some existing when existing = resolved -> Ok state
    | Some _ -> Error $"{label} for '{key}' declared with conflicting IRIs"
    | None -> Ok(setMap state (Map.add key resolved (getMap state)))

/// Resolve a type+IRI pair and apply a singleton-map insert combinator.
let private applyTypeIriOp<'K when 'K: comparison>
    (label: string)
    (typeExpr: FSharpExpr)
    (iriExpr: FSharpExpr)
    (makeKey: string -> 'K)
    (prefixes: Map<string, Uri>)
    (getMap: VocabularyRegistry -> Map<'K, Uri>)
    (setMap: VocabularyRegistry -> Map<'K, Uri> -> VocabularyRegistry)
    (state: VocabularyRegistry)
    : Result<VocabularyRegistry, string> =
    match requireTypeKey typeExpr, requireConst iriExpr with
    | Error e, _
    | _, Error e -> Error e
    | Ok typeName, Ok iri ->
        match tryResolveIri prefixes iri with
        | Error e -> Error e
        | Ok resolved -> insertSingleton label (makeKey typeName) resolved getMap setMap state

let private applyPrefix (args: FSharpExpr list) (state: VocabularyRegistry) : Result<VocabularyRegistry, string> =
    match args with
    | _ :: nameExpr :: uriExpr :: _ ->
        match requireConst nameExpr, requireConst uriExpr with
        | Error e, _
        | _, Error e -> Error e
        | Ok name, Ok uri ->
            let baseUri = Uri(uri)

            match Map.tryFind name state.Prefixes with
            | Some existing when existing = baseUri -> Ok state
            | Some _ -> Error $"prefix '{name}' declared with conflicting URIs"
            | None ->
                Ok
                    { state with
                        Prefixes = Map.add name baseUri state.Prefixes }
    | _ -> Error "Prefix: wrong argument count"

let private applyUsing (args: FSharpExpr list) (state: VocabularyRegistry) : Result<VocabularyRegistry, string> =
    match args with
    | _ :: prefixExpr :: _ ->
        match requireConst prefixExpr with
        | Error e -> Error e
        | Ok prefix ->
            if Set.contains prefix state.Using then
                Error $"prefix '{prefix}' already in using set"
            else
                Ok
                    { state with
                        Using = Set.add prefix state.Using }
    | _ -> Error "Using: wrong argument count"

let private applyEquivalentClass
    (args: FSharpExpr list)
    (state: VocabularyRegistry)
    : Result<VocabularyRegistry, string> =
    match args with
    | _ :: typeExpr :: iriExpr :: _ ->
        applyTypeIriOp
            "EquivalentClass"
            typeExpr
            iriExpr
            id
            state.Prefixes
            (fun s -> s.EquivalentClasses)
            (fun s m -> { s with EquivalentClasses = m })
            state
    | _ -> Error "EquivalentClass: wrong argument count"

/// Resolve IRI and append to a list-value map entry (no conflict, always appends).
let private resolveAndAppend<'K when 'K: comparison>
    (key: 'K)
    (iri: string)
    (prefixes: Map<string, Uri>)
    (getMap: VocabularyRegistry -> Map<'K, Uri list>)
    (setMap: VocabularyRegistry -> Map<'K, Uri list> -> VocabularyRegistry)
    (state: VocabularyRegistry)
    : Result<VocabularyRegistry, string> =
    match tryResolveIri prefixes iri with
    | Error e -> Error e
    | Ok resolved ->
        let existing = getMap state |> Map.tryFind key |> Option.defaultValue []
        Ok(setMap state (Map.add key (existing @ [ resolved ]) (getMap state)))

let private applySeeAlso (args: FSharpExpr list) (state: VocabularyRegistry) : Result<VocabularyRegistry, string> =
    match args with
    | _ :: typeExpr :: iriExpr :: _ ->
        match requireTypeKey typeExpr, requireConst iriExpr with
        | Error e, _
        | _, Error e -> Error e
        | Ok key, Ok iri ->
            resolveAndAppend key iri state.Prefixes (fun s -> s.SeeAlso) (fun s m -> { s with SeeAlso = m }) state
    | _ -> Error "SeeAlso: wrong argument count"

let private applyFieldSeeAlso (args: FSharpExpr list) (state: VocabularyRegistry) : Result<VocabularyRegistry, string> =
    match args with
    | _ :: typeExpr :: fieldExpr :: iriExpr :: _ ->
        match requireTypeKey typeExpr, requireConst fieldExpr, requireConst iriExpr with
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e
        | Ok key, Ok field, Ok iri ->
            resolveAndAppend
                (key, field)
                iri
                state.Prefixes
                (fun s -> s.FieldSeeAlso)
                (fun s m -> { s with FieldSeeAlso = m })
                state
    | _ -> Error "FieldSeeAlso: wrong argument count"

/// Insert into a singleton-value map by arbitrary key, detecting conflicts.
let private insertValue<'K, 'V when 'K: comparison and 'V: equality>
    (label: string)
    (key: 'K)
    (value: 'V)
    (getMap: VocabularyRegistry -> Map<'K, 'V>)
    (setMap: VocabularyRegistry -> Map<'K, 'V> -> VocabularyRegistry)
    (state: VocabularyRegistry)
    : Result<VocabularyRegistry, string> =
    match Map.tryFind key (getMap state) with
    | Some existing when existing = value -> Ok state
    | Some _ -> Error $"{label} declared with conflicting value for '{key}'"
    | None -> Ok(setMap state (Map.add key value (getMap state)))

let private applyProvClass (args: FSharpExpr list) (state: VocabularyRegistry) : Result<VocabularyRegistry, string> =
    match args with
    | _ :: typeExpr :: provClassExpr :: _ ->
        match requireTypeKey typeExpr, requireProvOClass provClassExpr with
        | Error e, _
        | _, Error e -> Error e
        | Ok key, Ok cls ->
            insertValue "ProvClass" key cls (fun s -> s.ProvClasses) (fun s m -> { s with ProvClasses = m }) state
    | _ -> Error "ProvClass: wrong argument count"

let private applyConstrainPattern
    (args: FSharpExpr list)
    (state: VocabularyRegistry)
    : Result<VocabularyRegistry, string> =
    match args with
    | _ :: typeExpr :: fieldExpr :: patternExpr :: _ ->
        match requireTypeKey typeExpr, requireConst fieldExpr, requireConst patternExpr with
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e
        | Ok key, Ok field, Ok pattern ->
            insertValue
                "ConstrainPattern"
                (key, field)
                pattern
                (fun s -> s.ConstraintPatterns)
                (fun s m -> { s with ConstraintPatterns = m })
                state
    | _ -> Error "ConstrainPattern: wrong argument count"

// ── include merge helper ──────────────────────────────────────────────────────

/// Merge two registries via VocabularyRegistry.include', converting exceptions to Error.
let private tryMerge (state: VocabularyRegistry) (other: VocabularyRegistry) : Result<VocabularyRegistry, string> =
    try
        Ok(VocabularyRegistry.include' state other)
    with :? InvalidOperationException as ex ->
        Error ex.Message

// ── Binding search ────────────────────────────────────────────────────────────

/// Find the CE body expression of the named binding across all implementation files.
/// Supports qualified names (e.g. "CliTestVocab.registry") and simple names ("registry").
let private findBindingBody
    (implFiles: FSharpImplementationFileContents list)
    (bindingName: string)
    : Result<FSharpExpr, string> =
    let simpleName =
        let idx = bindingName.LastIndexOf('.')
        if idx >= 0 then bindingName.[idx + 1 ..] else bindingName

    let found = ResizeArray<FSharpExpr>()

    let rec search (decls: FSharpImplementationFileDeclaration list) : unit =
        for decl in decls do
            match decl with
            | FSharpImplementationFileDeclaration.Entity(_, ds) -> search ds
            | FSharpImplementationFileDeclaration.MemberOrFunctionOrValue(mfv, _, body) ->
                if mfv.CompiledName = simpleName then
                    found.Add(body)
            | FSharpImplementationFileDeclaration.InitAction _ -> ()

    for f in implFiles do
        search f.Declarations

    match found.Count with
    | 0 -> Error $"binding '{bindingName}' not found in source files"
    | 1 -> Ok found.[0]
    | n -> Error $"binding '{bindingName}' found {n} times (ambiguous)"

// ── CE traversal (mutually recursive group) ───────────────────────────────────

// All four functions are mutually recursive (rec/and) because:
//   resolveIncludedRegistry → walkCEBody
//   applyInclude → resolveIncludedRegistry
//   walkCEBody → applyOperation
//   applyOperation → applyInclude

[<Literal>]
let private MaxIncludeDepth = 16

/// Resolve the included registry from an include arg expression.
/// Module-level zero-arg bindings appear as Call(mfv, args=[]) in the typed tree
/// (property getter calls). Value mfv is a fallback for local bindings.
/// Any other expression (e.g. an inline CE) is walked directly.
let rec private resolveIncludedRegistry
    (implFiles: FSharpImplementationFileContents list)
    (depth: int)
    (visited: Set<string>)
    (argExpr: FSharpExpr)
    : Result<VocabularyRegistry, string> =
    let lookupByName (name: string) =
        if Set.contains name visited then
            Error $"include cycle detected: '{name}' is already being resolved"
        elif depth >= MaxIncludeDepth then
            Error "include cycle or depth exceeded"
        else
            match findBindingBody implFiles name with
            | Error e -> Error e
            | Ok body -> walkCEBody implFiles (depth + 1) (Set.add name visited) body

    match argExpr with
    | FSharpExprPatterns.Call(_, mfv, _, _, []) -> lookupByName mfv.CompiledName
    | FSharpExprPatterns.Value mfv -> lookupByName mfv.CompiledName
    | other -> walkCEBody implFiles depth visited other

/// Apply the Include operation: resolve the included registry and merge into state.
and private applyInclude
    (implFiles: FSharpImplementationFileContents list)
    (depth: int)
    (visited: Set<string>)
    (args: FSharpExpr list)
    (state: VocabularyRegistry)
    : Result<VocabularyRegistry, string> =
    match args with
    | _ :: otherExpr :: _ ->
        match resolveIncludedRegistry implFiles depth visited otherExpr with
        | Error e -> Error e
        | Ok other -> tryMerge state other
    | _ -> Error "Include: wrong argument count"

/// Walk the vocabulary CE typed-tree body, recursively applying operations bottom-up.
/// The CE desugars to Application(func=Lambda(body), typeArgs, args=[builder]).
/// The Lambda body is the nested Call chain; args[0] is the previous state expression.
/// Recursion terminates at Yield (returns empty) or unrecognized nodes (returns empty).
and private walkCEBody
    (implFiles: FSharpImplementationFileContents list)
    (depth: int)
    (visited: Set<string>)
    (expr: FSharpExpr)
    : Result<VocabularyRegistry, string> =
    match expr with
    | FSharpExprPatterns.Application(func, _, _) -> walkCEBody implFiles depth visited func
    | FSharpExprPatterns.Lambda(_, body) -> walkCEBody implFiles depth visited body
    | FSharpExprPatterns.Call(_, mfv, _, _, _) when mfv.CompiledName = "Yield" -> Ok VocabularyRegistry.empty
    | FSharpExprPatterns.Call(_, mfv, _, _, args) ->
        match args with
        | prevStateExpr :: _ ->
            match walkCEBody implFiles depth visited prevStateExpr with
            | Error e -> Error e
            | Ok state -> applyOperation implFiles depth visited mfv.CompiledName args state
        | [] -> Ok VocabularyRegistry.empty
    | _ -> Ok VocabularyRegistry.empty

/// Dispatch a single CE operation by CompiledName to its per-op handler.
and private applyOperation
    (implFiles: FSharpImplementationFileContents list)
    (depth: int)
    (visited: Set<string>)
    (compiledName: string)
    (args: FSharpExpr list)
    (state: VocabularyRegistry)
    : Result<VocabularyRegistry, string> =
    match compiledName with
    | "Prefix" -> applyPrefix args state
    | "Using" -> applyUsing args state
    | "EquivalentClass" -> applyEquivalentClass args state
    | "SeeAlso" -> applySeeAlso args state
    | "FieldSeeAlso" -> applyFieldSeeAlso args state
    | "ProvClass" -> applyProvClass args state
    | "ConstrainPattern" -> applyConstrainPattern args state
    | "Include" -> applyInclude implFiles depth visited args state
    | _ -> Ok state

// ── Public entry point ────────────────────────────────────────────────────────

/// Evaluate the project's vocabulary CE by FCS-typechecking the source files and
/// walking the typed AST. No code execution, no reflection.
///
/// assemblyRefs: explicit paths to referenced assemblies (must include Frank.Semantic.dll).
///
/// sourceFiles: F# source files in dependency order. The last file drives SDK resolution.
///   All files are typechecked together.
///
/// bindingName: the binding name of the registry (e.g. "CliTestVocab.registry").
///   Qualified or simple names are both supported.
///
/// Returns the reconstructed VocabularyRegistry or a diagnostic Error string.
let evalRegistry
    (assemblyRefs: string list)
    (sourceFiles: string list)
    (bindingName: string)
    : Result<VocabularyRegistry, string> =
    if assemblyRefs.IsEmpty then
        invalidArg (nameof assemblyRefs) "assemblyRefs must not be empty"

    if sourceFiles.IsEmpty then
        invalidArg (nameof sourceFiles) "sourceFiles must not be empty"

    if String.IsNullOrWhiteSpace bindingName then
        invalidArg (nameof bindingName) "bindingName must not be empty"

    match typecheckFiles assemblyRefs sourceFiles with
    | Error e -> Error e
    | Ok implFiles ->
        match findBindingBody implFiles bindingName with
        | Error e -> Error e
        | Ok body -> walkCEBody implFiles 0 Set.empty body
