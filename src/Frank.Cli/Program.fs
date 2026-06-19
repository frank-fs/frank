module Frank.Cli.Program

open System
open System.IO
open System.Reflection
open Argu
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core
open Frank.Cli.Core.Refresh
open Frank.Cli.Core.Status

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
            | Output_Format _ -> "output format: 'json' (default) | 'markdown' | 'resolved-template'"

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
    | Output_Format of format: string

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Input _ -> "path to resolved.json"
            | Source _ -> "mapping source: 'llm' (default) or 'manual'"
            | Project _ -> "path to the .fsproj (defaults to first .fsproj in current directory)"
            | Lock_File _ -> "path to the lock file (defaults to <projectdir>/.frank/semantic-mappings.lock.json)"
            | Output_Format _ -> "output format: 'text' (default) or 'json'"

/// Arguments for the `frank semantic status` subcommand.
[<CliPrefix(CliPrefix.DoubleDash)>]
type StatusArgs =
    | [<AltCommandLine("-p")>] Project of path: string
    | [<AltCommandLine("-l")>] Lock_File of path: string

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Project _ -> "path to the .fsproj (defaults to first .fsproj in current directory)"
            | Lock_File _ -> "path to the lock file (defaults to <projectdir>/.frank/semantic-mappings.lock.json)"

/// Arguments for the `frank semantic finalize` subcommand.
[<CliPrefix(CliPrefix.DoubleDash)>]
type FinalizeArgs =
    | [<AltCommandLine("-p")>] Project of path: string
    | [<AltCommandLine("-l")>] Lock_File of path: string

    interface IArgParserTemplate with
        member a.Usage =
            match a with
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
    | [<CliPrefix(CliPrefix.None)>] Status of ParseResults<StatusArgs>
    | [<CliPrefix(CliPrefix.None)>] Finalize of ParseResults<FinalizeArgs>

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Extract _ -> "extract semantic mappings from a Frank project"
            | Clarify _ -> "emit unresolved/proposed mappings as an LLM contract"
            | Accept _ -> "merge LLM/hand-resolved mappings into the lock file"
            | Refresh _ -> "re-fetch vocabularies and report hash drift"
            | Status _ -> "summarize lock-file mapping counts"
            | Finalize _ -> "decide a draft lock: confirm exact matches, exclude the rest (zero LLM)"

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

let private runtimeAssemblyRefs () : string list =
    [ Assembly.GetAssembly(typeof<Frank.Semantic.VocabularyRegistry>).Location
      Assembly.GetAssembly(typeof<unit>).Location ]

let private parseChoice (label: string) (choices: (string * 'a) list) (s: string) : Result<'a, string> =
    let key = s.ToLowerInvariant()

    match choices |> List.tryFind (fun (k, _) -> k = key) with
    | Some(_, v) -> Ok v
    | None ->
        let valid = choices |> List.map fst |> String.concat "', '"
        Error $"unknown {label} '{s}'; expected '{valid}'"

let private parseOutputFormat: string -> Result<Pipeline.OutputFormat, string> =
    parseChoice "output format" [ "text", Pipeline.Text; "json", Pipeline.Json ]

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

let private clarifyFormats =
    [ "json", Clarify.toJson
      "markdown", Clarify.toMarkdown
      "resolved-template", Clarify.toResolvedTemplate ]

let private handleClarify (args: ParseResults<ClarifyArgs>) : int =
    let formatStr =
        args.TryGetResult(ClarifyArgs.Output_Format) |> Option.defaultValue "json"

    match parseChoice "output format" clarifyFormats formatStr with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok render ->
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

let private parseSource: string -> Result<MappingSource, string> =
    parseChoice "source" [ "llm", Llm; "manual", Manual ]

let private handleAccept (args: ParseResults<AcceptArgs>) : int =
    let formatStr =
        args.TryGetResult(AcceptArgs.Output_Format) |> Option.defaultValue "text"

    match parseOutputFormat formatStr with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok fmt ->

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

                    match
                        lockPathFrom (args.TryGetResult AcceptArgs.Lock_File) (args.TryGetResult AcceptArgs.Project)
                    with
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

                            write
                                lockPath
                                { updated with
                                    Generated = DateTimeOffset.UtcNow }

                            for r in summary.Rejected do
                                eprintfn "%s: %s" r.FSharpType r.Reason

                            match fmt with
                            | Pipeline.Json -> printfn "%s" (Accept.summaryToJson summary)
                            | Pipeline.Text ->
                                printfn
                                    "Merged %d mapping(s); %d rejected; %d unchanged; %d already-confirmed; %d field(s) still unresolved"
                                    summary.Merged
                                    summary.Rejected.Length
                                    summary.Unchanged
                                    summary.AlreadyConfirmed
                                    summary.FieldsUnresolved

                            0

let private handleFinalize (args: ParseResults<FinalizeArgs>) : int =
    match lockPathFrom (args.TryGetResult FinalizeArgs.Lock_File) (args.TryGetResult FinalizeArgs.Project) with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok lockPath ->
        match read lockPath with
        | Error e ->
            eprintfn "error: %s" e
            1
        | Ok lf ->
            let updated, summary = Finalize.run lf
            write lockPath updated

            printfn
                "Finalized: %d confirmed, %d excluded (%d already decided)"
                summary.Confirmed
                summary.Excluded
                summary.AlreadyDecided

            0

let private handleStatus (args: ParseResults<StatusArgs>) : int =
    match lockPathFrom (args.TryGetResult StatusArgs.Lock_File) (args.TryGetResult StatusArgs.Project) with
    | Error e ->
        eprintfn "error: %s" e
        1
    | Ok lockPath ->
        match read lockPath with
        | Error e ->
            eprintfn "error: %s" e
            1
        | Ok lf ->
            printfn "%s" (format lf)
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
                    2
                else
                    printfn "%d vocabulary(ies) checked; no drift" report.Checked
                    0

let private handleSemantic (args: ParseResults<SemanticArgs>) : int =
    match args.GetSubCommand() with
    | SemanticArgs.Extract extractArgs -> handleExtract extractArgs
    | SemanticArgs.Clarify clarifyArgs -> handleClarify clarifyArgs
    | SemanticArgs.Accept acceptArgs -> handleAccept acceptArgs
    | SemanticArgs.Refresh refreshArgs -> handleRefresh refreshArgs
    | SemanticArgs.Status statusArgs -> handleStatus statusArgs
    | SemanticArgs.Finalize finalizeArgs -> handleFinalize finalizeArgs

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
