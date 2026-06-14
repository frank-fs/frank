module Frank.Cli.Core.VocabularyEvaluator

open System
open System.IO
open System.Text
open FSharp.Compiler.Interactive.Shell
open Frank.Semantic

// ── FSI session configuration ─────────────────────────────────────────────────

/// Build FSI startup arguments that disable most interactive features.
/// No nested dotnet/msbuild — pure in-process FSharp.Compiler.Interactive.
let private fsiArgs () : string[] =
    [| "fsi"; "--noninteractive"; "--nologo"; "--quiet"; "--exec"; "--warn:0" |]

/// Create and return an FsiEvaluationSession with the given assembly references loaded.
/// The caller is responsible for disposing the session.
let private createSession (assemblyRefs: string list) : Result<FsiEvaluationSession, string> =
    let inStream = new StringReader("")
    let outSb = StringBuilder()
    let errSb = StringBuilder()
    let outWriter = new StringWriter(outSb)
    let errWriter = new StringWriter(errSb)

    let fsiConfig = FsiEvaluationSession.GetDefaultConfiguration()
    let args = fsiArgs ()

    let session =
        FsiEvaluationSession.Create(fsiConfig, args, inStream, outWriter, errWriter, collectible = true)

    let loadRef (path: string) : Result<unit, string> =
        if String.IsNullOrWhiteSpace path then
            invalidArg (nameof path) "assembly ref path must not be empty"

        if not (File.Exists path) then
            Error $"assembly ref not found: {path}"
        else

            let interaction = $"#r \"{path}\""
            let _, diags = session.EvalInteractionNonThrowing(interaction)

            let errors =
                diags
                |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)

            if errors.Length = 0 then
                Ok()
            else
                let msg = errors |> Array.map (fun d -> d.ToString()) |> String.concat "; "
                Error $"#r failed for '{path}': {msg}"

    let refResults = assemblyRefs |> List.map loadRef

    let firstError =
        refResults
        |> List.tryFind (fun r ->
            match r with
            | Error _ -> true
            | Ok _ -> false)

    match firstError with
    | Some(Error e) -> Error e
    | _ -> Ok session

// ── Source file loading ───────────────────────────────────────────────────────

let private loadSourceFiles (session: FsiEvaluationSession) (sourceFiles: string list) : Result<unit, string> =
    let loadOne (path: string) : Result<unit, string> =
        if String.IsNullOrWhiteSpace path then
            invalidArg (nameof path) "source file path must not be empty"

        if not (File.Exists path) then
            Error $"source file not found: {path}"
        else

            let interaction = $"#load \"{path}\""
            let _, diags = session.EvalInteractionNonThrowing(interaction)

            let errors =
                diags
                |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)

            if errors.Length = 0 then
                Ok()
            else
                let msg = errors |> Array.map (fun d -> d.ToString()) |> String.concat "; "

                Error $"compile error in '{path}': {msg}"

    let results = sourceFiles |> List.map loadOne

    let firstError =
        results
        |> List.tryFind (fun r ->
            match r with
            | Error _ -> true
            | Ok _ -> false)

    match firstError with
    | Some(Error e) -> Error e
    | _ -> Ok()

// ── Binding name resolution ───────────────────────────────────────────────────

let private tryEvalExpr (session: FsiEvaluationSession) (expr: string) : Result<FsiValue, string> =
    let value, diags = session.EvalExpressionNonThrowing(expr)

    let errors =
        diags
        |> Array.filter (fun d -> d.Severity = FSharp.Compiler.Diagnostics.FSharpDiagnosticSeverity.Error)

    if errors.Length > 0 then
        let msg = errors |> Array.map (fun d -> d.ToString()) |> String.concat "; "
        Error msg
    else
        match value with
        | Choice1Of2(Some v) -> Ok v
        | Choice1Of2 None -> Error $"expression '{expr}' evaluated to None — binding not found"
        | Choice2Of2 ex -> Error $"exception evaluating '{expr}': {ex.Message}"

/// Try to evaluate `bindingName` as-is; if that fails and it looks like
/// "Namespace.name", open the namespace and retry just "name".
/// Also tries opening each loaded source file's derived module path (no-dot fallback).
/// Returns (resolvedExpr, FsiValue) — resolvedExpr is the name that succeeded in-session.
let private resolveBinding
    (session: FsiEvaluationSession)
    (bindingName: string)
    (sourceFiles: string list)
    : Result<string * FsiValue, string> =
    match tryEvalExpr session bindingName with
    | Ok v -> Ok(bindingName, v)
    | Error firstErr ->
        let lastDot = bindingName.LastIndexOf('.')

        if lastDot > 0 then
            let ns = bindingName.[.. lastDot - 1]
            let localName = bindingName.[lastDot + 1 ..]
            session.EvalInteractionNonThrowing($"open {ns}") |> ignore

            match tryEvalExpr session localName with
            | Ok v -> Ok(localName, v)
            | Error e -> Error e
        else
            // No dot in binding name — try opening each source file's derived module name.
            // Derive candidates from file base names: "Vocabulary.fs" → try open "Vocabulary", etc.
            let candidates =
                sourceFiles
                |> List.map (fun f -> Path.GetFileNameWithoutExtension f)
                |> List.distinct
                |> List.truncate 20

            let rec tryNext (remaining: string list) =
                match remaining with
                | [] -> Error firstErr
                | modName :: rest ->
                    session.EvalInteractionNonThrowing($"open {modName}") |> ignore

                    match tryEvalExpr session bindingName with
                    | Ok v -> Ok(bindingName, v)
                    | Error _ -> tryNext rest

            tryNext candidates

// ── Public entry point ────────────────────────────────────────────────────────

/// Evaluate the project's vocabulary CE IN-PROCESS via FSI. No child processes.
///
/// assemblyRefs: explicit paths to #r (must include Frank.Semantic.dll and FSharp.Core.dll).
///   Use the SAME assembly the calling process has loaded so the returned type IS
///   our VocabularyRegistry — no version mismatch possible.
///
/// sourceFiles: F# files needed to evaluate the registry (domain types + vocabulary file),
///   in dependency order. Each is #loaded into FSI.
///
/// bindingName: the fully-qualified or module-qualified binding name of the registry
///   (e.g. "CliTestVocab.registry" or "MyApp.Vocabulary.registry").
///
/// Returns the fully-evaluated VocabularyRegistry (all alignments intact) or a
/// diagnostic error string with file:line:col info on compile/eval failure.
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

    match createSession assemblyRefs with
    | Error e -> Error e
    | Ok session ->
        use _session = session

        match loadSourceFiles session sourceFiles with
        | Error e -> Error e
        | Ok() ->
            match resolveBinding session bindingName sourceFiles with
            | Error e -> Error e
            | Ok(resolvedExpr, _) ->
                // Serialize INSIDE FSI where the registry's VocabularyRegistry type is
                // in the same ALC as Frank.Semantic.VocabularyRegistry.serialize.
                // Strings cross ALC boundaries; typed values do not — this avoids the cast.
                let serializedExpr = $"Frank.Semantic.VocabularyRegistry.serialize ({resolvedExpr})"

                match tryEvalExpr session serializedExpr with
                | Error e -> Error $"serialize inside FSI failed: {e}"
                | Ok sv ->
                    match sv.ReflectionValue with
                    | :? string as json -> VocabularyRegistry.deserialize json
                    | other -> Error $"expected string from serialize, got {other.GetType().Name}"
