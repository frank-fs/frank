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

type ExtractSummary =
    { Confirmed: int
      Proposed: int
      Unresolved: int }

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
            let isProtected = m.Status = Confirmed && (m.Source = Llm || m.Source = Manual)

            match Map.tryFind m.FSharpType freshByType with
            | None -> m
            | Some r -> if isProtected then m else r)

    let existingTypes = existing |> List.map (fun m -> m.FSharpType) |> Set.ofList

    let newEntries =
        fresh |> List.filter (fun m -> not (Set.contains m.FSharpType existingTypes))

    updatedExisting @ newEntries

/// Compute counts per status bucket.
let private summarize (mappings: Mapping list) : ExtractSummary =
    { Confirmed = mappings |> List.filter (fun m -> m.Status = Confirmed) |> List.length
      Proposed = mappings |> List.filter (fun m -> m.Status = Proposed) |> List.length
      Unresolved = mappings |> List.filter (fun m -> m.Status = Unresolved) |> List.length }

// ── Effectful steps ───────────────────────────────────────────────────────────

/// Fetch all in-scope vocabularies and return merged VocabTerms.
let private fetchVocabTerms (registry: VocabularyRegistry) : Async<Result<VocabTerms, string>> =
    async {
        use client = new HttpClient()
        let fetch = VocabFetcher.httpFetch client
        let cacheDir = Path.Combine(Path.GetTempPath(), "frank-vocab-cache")
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
                          Properties = Map.fold (fun m k v -> Map.add k v m) acc.Properties t.Properties })
                    { Classes = Map.empty
                      Properties = Map.empty }

            return Ok terms
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
let private writeLock (lockPath: string) (existing: LockFile) (fresh: Mapping list) : ExtractSummary =
    let merged = mergeWithPreservation existing.Mappings fresh

    let updated =
        { existing with
            Generated = DateTimeOffset.UtcNow
            Mappings = merged }

    Directory.CreateDirectory(Path.GetDirectoryName lockPath) |> ignore
    LockFile.write lockPath updated
    summarize merged

// ── Main entry point ──────────────────────────────────────────────────────────

/// Run the extract pipeline.
/// No child processes; all FCS evaluation is in-process.
let run (opts: ExtractOptions) : Result<ExtractSummary, string> =
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

                let domainFiles = allSourceFiles |> List.filter (fun f -> f <> vocabFile)

                match tryEvalRegistry opts.AssemblyRefs allSourceFiles with
                | Error e -> Error $"registry eval failed: {e}"
                | Ok registry ->

                    match extractFromFiles domainFiles with
                    | Error e -> Error $"type extraction failed: {e}"
                    | Ok typeInfos ->

                        match fetchVocabTerms registry |> Async.RunSynchronously with
                        | Error e -> Error $"vocab fetch failed: {e}"
                        | Ok terms ->

                            let freshMappings = typeInfos |> List.map (ConventionEngine.score terms registry)
                            let lockPath = lockFilePath projectFile
                            let existingLock = readOrEmptyLock lockPath
                            Ok(writeLock lockPath existingLock freshMappings)
