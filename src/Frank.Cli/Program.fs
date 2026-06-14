module Frank.Cli.Program

open System
open System.IO
open System.Reflection
open Argu
open Frank.Cli

// ── CLI argument definitions ──────────────────────────────────────────────────

/// Arguments for the `frank semantic extract` subcommand.
/// B8/B9 will add `clarify`, `accept`, `refresh`, `status` as siblings under SemanticArgs.
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

[<CliPrefix(CliPrefix.None)>]
type SemanticArgs =
    | [<CliPrefix(CliPrefix.None)>] Extract of ParseResults<ExtractArgs>

    interface IArgParserTemplate with
        member a.Usage =
            match a with
            | Extract _ -> "extract semantic mappings from a Frank project"

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
let private parseOutputFormat (s: string) : Result<ExtractPipeline.OutputFormat, string> =
    match s.ToLowerInvariant() with
    | "text" -> Ok ExtractPipeline.Text
    | "json" -> Ok ExtractPipeline.Json
    | other -> Error $"unknown output format '{other}'; expected 'text' or 'json'"

/// Format and print the summary line to stdout.
let private printSummary (fmt: ExtractPipeline.OutputFormat) (s: ExtractPipeline.ExtractSummary) : unit =
    match fmt with
    | ExtractPipeline.Text -> printfn "Confirmed: %d, Proposed: %d, Unresolved: %d" s.Confirmed s.Proposed s.Unresolved
    | ExtractPipeline.Json ->
        printfn """{"confirmed":%d,"proposed":%d,"unresolved":%d}""" s.Confirmed s.Proposed s.Unresolved

// ── Command handlers ──────────────────────────────────────────────────────────

let private handleExtract (args: ParseResults<ExtractArgs>) : int =
    let formatResult =
        args.TryGetResult(ExtractArgs.Output_Format)
        |> Option.map parseOutputFormat
        |> Option.defaultValue (Ok ExtractPipeline.Text)

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

            let opts: ExtractPipeline.ExtractOptions =
                { ProjectFile = projectFile
                  VocabularyFile = args.TryGetResult(ExtractArgs.Vocabulary_File)
                  AssemblyRefs = runtimeAssemblyRefs ()
                  OutputFormat = fmt }

            match ExtractPipeline.run opts with
            | Error e ->
                eprintfn "error: %s" e
                1
            | Ok summary ->
                printSummary fmt summary
                0

let private handleSemantic (args: ParseResults<SemanticArgs>) : int =
    match args.GetSubCommand() with
    | SemanticArgs.Extract extractArgs -> handleExtract extractArgs

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
