module Frank.Cli.Core.Pipeline

open System
open System.Net.Http
open Frank.Semantic

// ── Types ─────────────────────────────────────────────────────────────────────

type OutputFormat =
    | Text
    | Json

type ExtractOptions =
    { ProjectFile: string
      VocabularyFile: string option
      AssemblyRefs: string list
      OutputFormat: OutputFormat }

type ExtractSummary = LockFile.StatusCounts

/// Exclude files that FCS cannot typecheck in the pipeline's reduced assembly context.
/// Mirrors the MSBuild _FrankVocabSource item exclusion in Frank.Cli.MSBuild.targets:
///   Extension != '.fsi'  AND  Filename+Extension != 'Program.fs'  AND  NOT StartsWith('Generated').
/// Cross-boundary duplication (XML vs F#) is unavoidable; keep rules in sync.
val internal curateSourceFiles: files: string list -> string list

/// Pipeline core with the vocabulary fetcher and clock injected.
/// `run` wraps this with the production HttpClient-backed fetcher and real clock.
val internal runWithFetch:
    fetch: VocabFetcher.Fetch ->
    clock: (unit -> DateTimeOffset) ->
    opts: ExtractOptions ->
        Result<ExtractSummary, string>

/// Run the extract pipeline.
/// No child processes; all FCS evaluation is in-process.
val run: opts: ExtractOptions -> Result<ExtractSummary, string>
