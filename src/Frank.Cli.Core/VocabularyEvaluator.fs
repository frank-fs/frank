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

// ── CE operation application ──────────────────────────────────────────────────

/// Resolve a prefixed IRI, converting InvalidOperationException (undeclared prefix) to Error.
let private tryResolveIri (prefixes: Map<string, Uri>) (iri: string) : Result<Uri, string> =
    try
        Ok(VocabularyRegistry.resolveIri prefixes iri)
    with :? InvalidOperationException as ex ->
        Error ex.Message

/// Apply one recognized CE builder operation to the running registry state.
/// `args` = positional arguments (args[0] is the previous state expression, not used here).
let private applyOperation
    (compiledName: string)
    (args: FSharpExpr list)
    (state: VocabularyRegistry)
    : Result<VocabularyRegistry, string> =
    match compiledName, args with
    | "Prefix", _ :: nameExpr :: uriExpr :: _ ->
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

    | "Using", _ :: prefixExpr :: _ ->
        match requireConst prefixExpr with
        | Error e -> Error e
        | Ok prefix ->
            if Set.contains prefix state.Using then
                Error $"prefix '{prefix}' already in using set"
            else
                Ok
                    { state with
                        Using = Set.add prefix state.Using }

    | "EquivalentClass", _ :: typeExpr :: iriExpr :: _ ->
        match requireTypeKey typeExpr, requireConst iriExpr with
        | Error e, _
        | _, Error e -> Error e
        | Ok key, Ok iri ->
            match tryResolveIri state.Prefixes iri with
            | Error e -> Error e
            | Ok resolved ->
                match Map.tryFind key state.EquivalentClasses with
                | Some existing when existing = resolved -> Ok state
                | Some _ -> Error $"EquivalentClass for '{key}' declared with conflicting IRIs"
                | None ->
                    Ok
                        { state with
                            EquivalentClasses = Map.add key resolved state.EquivalentClasses }

    | "SeeAlso", _ :: typeExpr :: iriExpr :: _ ->
        match requireTypeKey typeExpr, requireConst iriExpr with
        | Error e, _
        | _, Error e -> Error e
        | Ok key, Ok iri ->
            match tryResolveIri state.Prefixes iri with
            | Error e -> Error e
            | Ok resolved ->
                let existing = state.SeeAlso |> Map.tryFind key |> Option.defaultValue []

                Ok
                    { state with
                        SeeAlso = Map.add key (existing @ [ resolved ]) state.SeeAlso }

    | "FieldSeeAlso", _ :: typeExpr :: fieldExpr :: iriExpr :: _ ->
        match requireTypeKey typeExpr, requireConst fieldExpr, requireConst iriExpr with
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e
        | Ok key, Ok field, Ok iri ->
            match tryResolveIri state.Prefixes iri with
            | Error e -> Error e
            | Ok resolved ->
                let tupleKey = (key, field)
                let existing = state.FieldSeeAlso |> Map.tryFind tupleKey |> Option.defaultValue []

                Ok
                    { state with
                        FieldSeeAlso = Map.add tupleKey (existing @ [ resolved ]) state.FieldSeeAlso }

    | "ProvClass", _ :: typeExpr :: provClassExpr :: _ ->
        match requireTypeKey typeExpr, requireProvOClass provClassExpr with
        | Error e, _
        | _, Error e -> Error e
        | Ok key, Ok cls ->
            match Map.tryFind key state.ProvClasses with
            | Some existing when existing = cls -> Ok state
            | Some _ -> Error $"ProvClass for '{key}' declared with conflicting classes"
            | None ->
                Ok
                    { state with
                        ProvClasses = Map.add key cls state.ProvClasses }

    | "ConstrainPattern", _ :: typeExpr :: fieldExpr :: patternExpr :: _ ->
        match requireTypeKey typeExpr, requireConst fieldExpr, requireConst patternExpr with
        | Error e, _, _
        | _, Error e, _
        | _, _, Error e -> Error e
        | Ok key, Ok field, Ok pattern ->
            let tupleKey = (key, field)

            match Map.tryFind tupleKey state.ConstraintPatterns with
            | Some existing when existing = pattern -> Ok state
            | Some _ -> Error $"ConstraintPattern for '{key}.{field}' declared with conflicting patterns"
            | None ->
                Ok
                    { state with
                        ConstraintPatterns = Map.add tupleKey pattern state.ConstraintPatterns }

    | "Include", _ ->
        Error "include CE operation is not supported by the FCS evaluator; merge registries at the call site"

    | _ -> Ok state

// ── CE body traversal ─────────────────────────────────────────────────────────

/// Walk the vocabulary CE typed-tree body, recursively applying operations bottom-up.
/// The CE desugars to Application(func=Lambda(body), typeArgs, args=[builder]).
/// The Lambda body is the nested Call chain; the args list holds the builder instance.
/// Recursion terminates at Yield (returns empty) or unrecognized nodes (returns empty).
let rec private walkCEBody (expr: FSharpExpr) : Result<VocabularyRegistry, string> =
    match expr with
    | FSharpExprPatterns.Application(func, _, _) -> walkCEBody func

    | FSharpExprPatterns.Lambda(_, body) -> walkCEBody body

    | FSharpExprPatterns.Call(_, mfv, _, _, args) when mfv.CompiledName = "Yield" -> Ok VocabularyRegistry.empty

    | FSharpExprPatterns.Call(_, mfv, _, _, args) ->
        match args with
        | prevStateExpr :: _ ->
            match walkCEBody prevStateExpr with
            | Error e -> Error e
            | Ok state ->
                match applyOperation mfv.CompiledName args state with
                | Error e -> Error e
                | Ok s -> Ok s
        | [] -> Ok VocabularyRegistry.empty

    | _ -> Ok VocabularyRegistry.empty

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
        | Ok body -> walkCEBody body
