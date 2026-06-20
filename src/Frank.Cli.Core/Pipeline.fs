module Frank.Cli.Core.Pipeline

open System
open System.IO
open System.Net.Http
open Frank.Semantic
open Frank.Semantic.LockFile

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

// ── Pure helpers ──────────────────────────────────────────────────────────────

let private lockFilePath (projectFile: string) : string =
    let dir = Path.GetDirectoryName(Path.GetFullPath projectFile)
    Path.Combine(dir, ".frank", "semantic-mappings.lock.json")

/// Locate the vocabulary file from explicit arg or convention Vocabulary.fs.
let private resolveVocabFile (projectFile: string) (explicit: string option) : Result<string, string> =
    match explicit with
    | Some path ->
        if File.Exists path then
            Ok path
        else
            Error $"vocabulary file not found: {path}"
    | None ->
        let dir = Path.GetDirectoryName(Path.GetFullPath projectFile)
        let candidate = Path.Combine(dir, "Vocabulary.fs")

        if File.Exists candidate then
            Ok candidate
        else
            Error "no Vocabulary.fs found in project directory; use --vocabulary-file to specify one"

/// Exclude files that FCS cannot typecheck in the pipeline's reduced assembly context.
/// Mirrors the MSBuild _FrankVocabSource item exclusion in Frank.Cli.MSBuild.targets:
///   Extension != '.fsi'  AND  Filename+Extension != 'Program.fs'  AND  NOT StartsWith('Generated').
/// Cross-boundary duplication (XML vs F#) is unavoidable; keep rules in sync.
let internal curateSourceFiles (files: string list) : string list =
    files
    |> List.filter (fun f ->
        let name = System.IO.Path.GetFileName f
        let ext = System.IO.Path.GetExtension f

        ext <> ".fsi"
        && name <> "Program.fs"
        && not (System.IO.Path.GetFileNameWithoutExtension(f).StartsWith("Generated")))

/// Read all <Compile> source file paths from the .fsproj in declaration order.
let private sourceFilesFromFsproj (projectFile: string) : Result<string list, string> =
    let dir = Path.GetDirectoryName(Path.GetFullPath projectFile)

    try
        let doc = System.Xml.Linq.XDocument.Load projectFile

        let files =
            doc.Descendants(System.Xml.Linq.XName.Get "Compile")
            |> Seq.choose (fun el ->
                el.Attribute(System.Xml.Linq.XName.Get "Include")
                |> Option.ofObj
                |> Option.map (fun a -> Path.GetFullPath(Path.Combine(dir, a.Value))))
            |> Seq.toList

        Ok files
    with ex ->
        Error $"could not read .fsproj: {ex.Message}"

/// Read existing lock file, or return an empty one if absent.
let private readOrEmptyLock (path: string) : LockFile =
    if File.Exists path then
        LockFile.read path
        |> Result.defaultValue
            { SchemaVersion = 1
              Generated = DateTimeOffset.UtcNow
              Vocabularies = Map.empty
              Mappings = [] }
    else
        { SchemaVersion = 1
          Generated = DateTimeOffset.UtcNow
          Vocabularies = Map.empty
          Mappings = [] }

/// Merge semantics: confirmed llm/manual entries are preserved; convention entries re-run.
let private mergeWithPreservation (existing: Mapping list) (fresh: Mapping list) : Mapping list =
    let freshByType = fresh |> List.map (fun m -> m.FSharpType, m) |> Map.ofList

    let updatedExisting =
        existing
        |> List.map (fun m ->
            let isProtected =
                m.Status = Excluded
                || (m.Status = Confirmed && (m.Source = Llm || m.Source = Manual))

            match Map.tryFind m.FSharpType freshByType with
            | None -> m
            | Some r -> if isProtected then m else r)

    let existingTypes = existing |> List.map (fun m -> m.FSharpType) |> Set.ofList

    let newEntries =
        fresh |> List.filter (fun m -> not (Set.contains m.FSharpType existingTypes))

    updatedExisting @ newEntries

let private summarize (mappings: Mapping list) : ExtractSummary = LockFile.countByStatus mappings

// ── Effectful steps ───────────────────────────────────────────────────────────

/// Fetch all in-scope vocabularies and return merged VocabTerms with per-prefix entries.
let private fetchVocabTerms
    (fetch: VocabFetcher.Fetch)
    (projectDir: string)
    (registry: VocabularyRegistry)
    : Async<Result<VocabTerms * Map<string, VocabularyEntry>, string>> =
    async {
        let cacheDir = Path.Combine(projectDir, ".frank", "vocab")
        Directory.CreateDirectory cacheDir |> ignore

        let inScopePrefixes =
            registry.Using
            |> Set.toList
            |> List.choose (fun prefix -> Map.tryFind prefix registry.Prefixes |> Option.map (fun uri -> prefix, uri))

        let! results =
            inScopePrefixes
            |> List.map (fun (n, u) -> VocabFetcher.fetchAndCache fetch cacheDir n u)
            |> Async.Parallel

        let firstError =
            results
            |> Array.tryFind (fun r ->
                match r with
                | Error _ -> true
                | Ok _ -> false)

        match firstError with
        | Some(Error e) -> return Error e
        | _ ->
            let terms =
                results
                |> Array.choose (fun r ->
                    match r with
                    | Ok cv -> Some cv.Graph
                    | Error _ -> None)
                |> Array.fold
                    (fun acc g ->
                        let t = ConventionEngine.extractVocabTerms g

                        { Classes = Map.fold (fun m k v -> Map.add k v m) acc.Classes t.Classes
                          Properties = Map.fold (fun m k v -> Map.add k v m) acc.Properties t.Properties
                          Individuals = Map.fold (fun m k v -> Map.add k v m) acc.Individuals t.Individuals })
                    { Classes = Map.empty
                      Properties = Map.empty
                      Individuals = Map.empty }

            let vocabEntries =
                List.zip inScopePrefixes (Array.toList results)
                |> List.choose (fun ((prefix, uri), r) ->
                    match r with
                    | Ok cv ->
                        Some(
                            prefix,
                            { Uri = uri.AbsoluteUri
                              FetchedAt = DateTimeOffset.UtcNow
                              Hash = cv.Hash }
                        )
                    | Error _ -> None)
                |> Map.ofList

            return Ok(terms, vocabEntries)
    }

/// Evaluate the registry binding. The VocabularyEvaluator handles fallback resolution.
let private tryEvalRegistry
    (assemblyRefs: string list)
    (sourceFiles: string list)
    : Result<VocabularyRegistry, string> =
    VocabularyEvaluator.evalRegistry assemblyRefs sourceFiles "registry"

/// Extract TypeInfos from source files using in-process FCS (no child processes).
let private extractFromFiles (sourceFiles: string list) : Result<TypeInfo list, string> =
    let combined = sourceFiles |> List.map File.ReadAllText |> String.concat "\n\n"
    Extractor.extractTypeInfosFromSource combined

/// Write the updated lock file to disk.
let private writeLock
    (lockPath: string)
    (existing: LockFile)
    (fresh: Mapping list)
    (vocabularies: Map<string, VocabularyEntry>)
    : ExtractSummary =
    let merged = mergeWithPreservation existing.Mappings fresh

    let updated =
        { existing with
            Generated = DateTimeOffset.UtcNow
            Vocabularies = vocabularies
            Mappings = merged }

    Directory.CreateDirectory(Path.GetDirectoryName lockPath) |> ignore
    LockFile.write lockPath updated
    summarize merged

// ── Main entry point ──────────────────────────────────────────────────────────

/// Pipeline core with the vocabulary fetcher injected. `run` wraps this with the production HttpClient-backed fetcher.
let internal runWithFetch (fetch: VocabFetcher.Fetch) (opts: ExtractOptions) : Result<ExtractSummary, string> =
    let projectFile = Path.GetFullPath opts.ProjectFile

    if not (File.Exists projectFile) then
        Error $"project file not found: {projectFile}"
    else

        match resolveVocabFile projectFile opts.VocabularyFile with
        | Error e -> Error e
        | Ok vocabFile ->

            match sourceFilesFromFsproj projectFile with
            | Error e -> Error e
            | Ok allSourceFiles ->

                let curatedFiles = curateSourceFiles allSourceFiles
                let domainFiles = curatedFiles |> List.filter (fun f -> f <> vocabFile)

                match tryEvalRegistry opts.AssemblyRefs curatedFiles with
                | Error e -> Error $"registry eval failed: {e}"
                | Ok registry ->

                    match extractFromFiles domainFiles with
                    | Error e -> Error $"type extraction failed: {e}"
                    | Ok typeInfos ->

                        let projectDir = Path.GetDirectoryName projectFile

                        match fetchVocabTerms fetch projectDir registry |> Async.RunSynchronously with
                        | Error e -> Error $"vocab fetch failed: {e}"
                        | Ok(terms, vocabEntries) ->

                            let freshMappings = typeInfos |> List.map (ConventionEngine.score terms registry)
                            let lockPath = lockFilePath projectFile
                            let existingLock = readOrEmptyLock lockPath
                            Ok(writeLock lockPath existingLock freshMappings vocabEntries)

/// Run the extract pipeline.
/// No child processes; all FCS evaluation is in-process.
let run (opts: ExtractOptions) : Result<ExtractSummary, string> =
    use client = new HttpClient()
    runWithFetch (VocabFetcher.httpFetch client) opts
