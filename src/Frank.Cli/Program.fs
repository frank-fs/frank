module Frank.Cli.Program

open System
open System.IO
open System.Reflection
open Argu
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core
open Frank.Cli.Core.Refresh

// ── CLI argument definitions ──────────────────────────────────────────────────

/// Arguments for the `frank semantic clarify` subcommand.
[<CliPrefix(CliPrefix.DoubleDash)>]
type ClarifyArgs =
    | [<AltCommandLine("-p")>] Project of path: string
    | [<AltCommandLine("-l")>] Lock_File of path: string
    | Output_Format of format: string

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Project _ -> "path to the .fsproj (defaults to first .fsproj in current directory)"
            | Lock_File _ -> "path to the lock file (defaults to <projectdir>/.frank/semantic-mappings.lock.json)"
            | Output_Format _ -> "output format: 'json' (default) or 'markdown'"

/// Arguments for the `frank semantic extract` subcommand.
[<CliPrefix(CliPrefix.DoubleDash)>]
type ExtractArgs =
    | [<AltCommandLine("-p")>] Project of path: string
    | [<AltCommandLine("-v")>] Vocabulary_File of path: string
    | Output_Format of format: string

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Project _ -> "path to the .fsproj file (defaults to first .fsproj in current directory)"
            | Vocabulary_File _ -> "path to the vocabulary CE file (defaults to Vocabulary.fs)"
            | Output_Format _ -> "output format: 'text' (default) or 'json'"

/// Arguments for the `frank semantic accept` subcommand.
[<CliPrefix(CliPrefix.DoubleDash)>]
type AcceptArgs =
    | [<Mandatory; AltCommandLine("-i")>] Input of path: string
    | Source of source: string
    | [<AltCommandLine("-p")>] Project of path: string
    | [<AltCommandLine("-l")>] Lock_File of path: string

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Input _ -> "path to resolved.json"
            | Source _ -> "mapping source: 'llm' (default) or 'manual'"
            | Project _ -> "path to the .fsproj (defaults to first .fsproj in current directory)"
            | Lock_File _ -> "path to the lock file (defaults to <projectdir>/.frank/semantic-mappings.lock.json)"

/// Arguments for the `frank semantic refresh` subcommand.
[<CliPrefix(CliPrefix.DoubleDash)>]
type RefreshArgs =
    | [<AltCommandLine("-p")>] Project of path: string
    | [<AltCommandLine("-l")>] Lock_File of path: string

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Project _ -> "path to the .fsproj (defaults to first .fsproj in current directory)"
            | Lock_File _ -> "path to the lock file (defaults to <projectdir>/.frank/semantic-mappings.lock.json)"

[<CliPrefix(CliPrefix.None)>]
type SemanticArgs =
    | [<CliPrefix(CliPrefix.None)>] Extract of ParseResults<ExtractArgs>
    | [<CliPrefix(CliPrefix.None)>] Clarify of ParseResults<ClarifyArgs>
    | [<CliPrefix(CliPrefix.None)>] Accept of ParseResults<AcceptArgs>
    | [<CliPrefix(CliPrefix.None)>] Refresh of ParseResults<RefreshArgs>

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Extract _ -> "extract semantic mappings from a Frank project"
            | Clarify _ -> "emit unresolved/proposed mappings as an LLM contract"
            | Accept _ -> "merge LLM/hand-resolved mappings into the lock file"
            | Refresh _ -> "re-fetch vocabularies and report hash drift"

[<CliPrefix(CliPrefix.None)>]
type FrankArgs =
    | [<CliPrefix(CliPrefix.None)>] Semantic of ParseResults<SemanticArgs>

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Semantic _ -> "semantic discovery commands"

// ── Helpers ───────────────────────────────────────────────────────────────────

/// Find the first .fsproj in the current directory.
let private findProjectFile (dir: string) : Result<string, string> =
    let files = Directory.GetFiles(dir, "*.fsproj")

    match files with
    | [||] -> Error $"no .fsproj found in '{dir}'; use --project to specify one"
    | [| f |] -> Ok f
    | _ -> Error $"multiple .fsproj files in '{dir}'; use --project to specify one"

/// Assembly references needed by the FSI evaluator:
///   - Frank.Semantic.dll loaded by THIS process (same assembly → same type identity)
///   - FSharp.Core.dll loaded by THIS process
let private runtimeAssemblyRefs () : string list =
    [ Assembly.GetAssembly(typeof<Frank.Semantic.VocabularyRegistry>).Location
      Assembly.GetAssembly(typeof<int list>).Location ]

/// Parse the output format string.
let private parseOutputFormat (s: string) : Result<Pipeline.OutputFormat, string> =
    match s.ToLowerInvariant() with
    | "text" -> Ok Pipeline.Text
    | "json" -> Ok Pipeline.Json
    | other -> Error $"unknown output format '{other}'; expected 'text' or 'json'"

/// Format and print the summary line to stdout.
let private printSummary (fmt: Pipeline.OutputFormat) (s: Pipeline.ExtractSummary) : unit =
    match fmt with
    | Pipeline.Text -> printfn "Confirmed: %d, Proposed: %d, Unresolved: %d" s.Confirmed s.Proposed s.Unresolved
    | Pipeline.Json -> printfn """{"confirmed":%d,"proposed":%d,"unresolved":%d}""" s.Confirmed s.Proposed s.Unresolved

// ── Command handlers ──────────────────────────────────────────────────────────

let private handleExtract (args: ParseResults<ExtractArgs>) : int =
    let formatResult =
        args.TryGetResult(ExtractArgs.Output_Format)
        |> Option.map parseOutputFormat
        |> Option.defaultValue (Ok Pipeline.Text)

    match formatResult with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok fmt ->

        let projectResult =
            match args.TryGetResult(ExtractArgs.Project) with
            | Some p -> Ok p
            | None -> findProjectFile (Directory.GetCurrentDirectory())

        match projectResult with
        | Error e ->
            eprintfn "error: %s" e
            1
        | Ok projectFile ->

            let opts: Pipeline.ExtractOptions =
                { ProjectFile = projectFile
                  VocabularyFile = args.TryGetResult(ExtractArgs.Vocabulary_File)
                  AssemblyRefs = runtimeAssemblyRefs ()
                  OutputFormat = fmt }

            match Pipeline.run opts with
            | Error e ->
                eprintfn "error: %s" e
                1
            | Ok summary ->
                printSummary fmt summary
                0

let private lockPathFrom (lockFile: string option) (project: string option) : Result<string, string> =
    match lockFile with
    | Some p -> Ok p
    | None ->
        let projectResult =
            match project with
            | Some p -> Ok p
            | None -> findProjectFile (Directory.GetCurrentDirectory())

        match projectResult with
        | Error e -> Error e
        | Ok projectFile ->
            let dir = Path.GetDirectoryName(Path.GetFullPath projectFile)
            Ok(Path.Combine(dir, ".frank", "semantic-mappings.lock.json"))

let private handleClarify (args: ParseResults<ClarifyArgs>) : int =
    let formatStr =
        args.TryGetResult(ClarifyArgs.Output_Format) |> Option.defaultValue "json"

    let emit =
        match formatStr.ToLowerInvariant() with
        | "json" -> Some Clarify.toJson
        | "markdown" -> Some Clarify.toMarkdown
        | other ->
            eprintfn "error: unknown output format '%s'; expected 'json' or 'markdown'" other
            None

    match emit with
    | None -> 1
    | Some render ->
        match lockPathFrom (args.TryGetResult ClarifyArgs.Lock_File) (args.TryGetResult ClarifyArgs.Project) with
        | Error e ->
            eprintfn "error: %s" e
            1
        | Ok lockPath ->
            match read lockPath with
            | Error e ->
                eprintfn "error: %s" e
                1
            | Ok lf ->
                printfn "%s" (render lf)
                0

let private parseSource (s: string) : Result<MappingSource, string> =
    match s.ToLowerInvariant() with
    | "llm" -> Ok Llm
    | "manual" -> Ok Manual
    | other -> Error $"unknown source '{other}'; expected 'llm' or 'manual'"

let private handleAccept (args: ParseResults<AcceptArgs>) : int =
    let inputPath = args.GetResult(AcceptArgs.Input)

    let jsonResult =
        try
            Ok(File.ReadAllText inputPath)
        with ex ->
            Error $"could not read '{inputPath}': {ex.Message}"

    match jsonResult with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok json ->

    match Accept.parseResolved json with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok doc ->

    let sourceStr = args.TryGetResult(AcceptArgs.Source) |> Option.defaultValue "llm"

    match parseSource sourceStr with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok source ->

    match lockPathFrom (args.TryGetResult AcceptArgs.Lock_File) (args.TryGetResult AcceptArgs.Project) with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok lockPath ->

    match read lockPath with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok lf ->

    let updated, summary = Accept.apply lf doc source
    write lockPath { updated with Generated = DateTimeOffset.UtcNow }

    for t in summary.Rejected do
        eprintfn "%s not in lock file; ignored" t

    printfn "Merged %d mapping(s); %d rejected; %d unchanged" summary.Merged summary.Rejected.Length summary.Unchanged
    0

let private handleRefresh (args: ParseResults<RefreshArgs>) : int =
    match lockPathFrom (args.TryGetResult RefreshArgs.Lock_File) (args.TryGetResult RefreshArgs.Project) with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok lockPath ->
        match read lockPath with
        | Error e ->
            eprintfn "error: %s" e
            1
        | Ok lf ->
            use client = new System.Net.Http.HttpClient()
            let fetch = VocabFetcher.httpFetch client

            match refresh fetch lf |> Async.RunSynchronously with
            | Error e ->
                eprintfn "error: %s" e
                1
            | Ok report ->
                for d in report.Drifted do
                    printfn "%s vocabulary hash drift: %s → %s" d.Prefix d.Recorded d.Current

                if report.Drifted <> [] then
                    1
                else
                    printfn "%d vocabulary(ies) checked; no drift" report.Checked
                    0

let private handleSemantic (args: ParseResults<SemanticArgs>) : int =
    match args.GetSubCommand() with
    | SemanticArgs.Extract extractArgs -> handleExtract extractArgs
    | SemanticArgs.Clarify clarifyArgs -> handleClarify clarifyArgs
    | SemanticArgs.Accept acceptArgs -> handleAccept acceptArgs
    | SemanticArgs.Refresh refreshArgs -> handleRefresh refreshArgs

// ── Entry point ───────────────────────────────────────────────────────────────

[<EntryPoint>]
let main (argv: string[]) : int =
    let parser = ArgumentParser.Create<FrankArgs>(programName = "frank")

    try
        let results = parser.ParseCommandLine(argv, raiseOnUsage = true)

        match results.GetSubCommand() with
        | FrankArgs.Semantic semanticArgs -> handleSemantic semanticArgs

    with
    | :? ArguParseException as ex ->
        eprintfn "%s" ex.Message
        1
    | ex ->
        eprintfn "fatal: %s" ex.Message
        1
