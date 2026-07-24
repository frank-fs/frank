module Frank.Cli.Core.Pipeline

open System
open System.IO
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

/// Result of the extract pipeline: the status-count summary plus any
/// ConventionDiagnostics raised while extracting vocab terms and scoring types
/// against the registry.
type ExtractResult =
    { Summary: ExtractSummary
      Diagnostics: ConventionDiagnostic list }

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
    let empty () =
        { SchemaVersion = 1
          Generated = DateTimeOffset.UtcNow
          Integrity = None
          Vocabularies = Map.empty
          DeclaredPrefixes = Map.empty
          Mappings = [] }

    if File.Exists path then
        LockFile.read path |> Result.defaultWith (fun _ -> empty ())
    else
        empty ()

/// Merge semantics: all decided (confirmed/excluded) entries preserved regardless of source;
/// undecided convention guesses (proposed/unresolved) are replaced by fresh extract output.
let private mergeWithPreservation (existing: Mapping list) (fresh: Mapping list) : Mapping list =
    let freshByType = fresh |> List.map (fun m -> m.FSharpType, m) |> Map.ofList

    let updatedExisting =
        existing
        |> List.map (fun m ->
            match Map.tryFind m.FSharpType freshByType with
            | None -> m
            | Some r -> if LockFile.isDecided m.Status then m else r)

    let existingTypes = existing |> List.map (fun m -> m.FSharpType) |> Set.ofList

    let newEntries =
        fresh |> List.filter (fun m -> not (Set.contains m.FSharpType existingTypes))

    updatedExisting @ newEntries

let private summarize (mappings: Mapping list) : ExtractSummary = LockFile.countByStatus mappings

let private accumulateTerms (acc: VocabTerms * ConventionDiagnostic list) g : VocabTerms * ConventionDiagnostic list =
    let accTerms, accDiagnostics = acc
    let t, diagnostics = ConventionEngine.extractVocabTermsDetailed g

    { Classes = Map.fold (fun m k v -> Map.add k v m) accTerms.Classes t.Classes
      Properties = Map.fold (fun m k v -> Map.add k v m) accTerms.Properties t.Properties
      Individuals = Map.fold (fun m k v -> Map.add k v m) accTerms.Individuals t.Individuals },
    accDiagnostics @ diagnostics

// ── Effectful steps ───────────────────────────────────────────────────────────

/// Cache-aware, content-negotiated vocabulary fetch: same cache-hit/miss shape as
/// VocabFetcher.fetchAndCache, but sources bytes via RdfConneg's ConnegFetch instead of a
/// plain GET. A vocab source that responds successfully with non-RDF content (HTML, a
/// redirect the server never resolves, a 4xx page) is classified via RdfConneg.buildEvidence
/// — the same classification refresh/validate already report — instead of being handed to
/// the RDF parser and surfacing an opaque parse exception.
let private fetchAndCacheConneg
    (fetch: ConnegFetch)
    (clock: unit -> DateTimeOffset)
    (cacheDir: string)
    (name: string)
    (uri: Uri)
    : Async<Result<VocabFetcher.CachedVocab, string>> =
    if String.IsNullOrWhiteSpace name then
        invalidArg (nameof name) "name must not be empty"

    async {
        match VocabFetcher.loadCachedVocab cacheDir name with
        | Some cached -> return cached
        | None ->
            let! result = fetch uri None None

            match result with
            | RdfContent r ->
                let format = VocabFetcher.detectFormat (Some r.MediaType) uri
                return VocabFetcher.parseAndCacheBytes cacheDir name format r.Body
            | _ ->
                match RdfConneg.buildEvidence uri (clock ()) result with
                | UnverifiableNonRdf reason -> return Error $"unverifiable-non-rdf: {reason}"
                | Undereferenceable reason -> return Error $"undereferenceable: {reason}"
                | TransientFailure reason -> return Error $"transient: {reason}"
                | Unchanged -> return Error "unexpected 304 Not Modified for an uncached vocab"
                | Updated _ -> return failwith "unreachable: buildEvidence returned Updated for a non-RdfContent result"
    }

/// Fetch all in-scope vocabularies and return merged VocabTerms with per-prefix entries.
let private fetchVocabTerms
    (fetch: ConnegFetch)
    (clock: unit -> DateTimeOffset)
    (projectDir: string)
    (registry: VocabularyRegistry)
    : Async<Result<VocabTerms * Map<string, VocabularyEntry> * ConventionDiagnostic list, string>> =
    async {
        let cacheDir = Path.Combine(projectDir, ".frank", "vocab")
        Directory.CreateDirectory cacheDir |> ignore

        let inScopePrefixes =
            registry.Using
            |> Set.toList
            |> List.choose (fun prefix -> Map.tryFind prefix registry.Prefixes |> Option.map (fun uri -> prefix, uri))

        let! results =
            inScopePrefixes
            |> List.map (fun (n, u) -> fetchAndCacheConneg fetch clock cacheDir n u)
            |> Async.Parallel

        let firstError =
            results
            |> Array.tryPick (function
                | Error e -> Some e
                | Ok _ -> None)

        match firstError with
        | Some e -> return Error e
        | None ->
            let emptyTerms =
                { Classes = Map.empty
                  Properties = Map.empty
                  Individuals = Map.empty }

            let terms, termDiagnostics =
                results
                |> Array.choose (fun r ->
                    match r with
                    | Ok cv -> Some cv.Graph
                    | Error _ -> None)
                |> Array.fold accumulateTerms (emptyTerms, [])

            let fetchedAt = clock ()

            let vocabEntries =
                List.zip inScopePrefixes (Array.toList results)
                |> List.choose (fun ((prefix, uri), r) ->
                    match r with
                    | Ok cv ->
                        Some(
                            prefix,
                            { v1Empty with
                                Uri = uri.AbsoluteUri
                                FetchedAt = fetchedAt
                                Hash = cv.Hash }
                        )
                    | Error _ -> None)
                |> Map.ofList

            return Ok(terms, vocabEntries, termDiagnostics)
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

/// Write the updated lock file to disk, stamping integrity with the injected clock.
let private writeLock
    (lockPath: string)
    (clock: unit -> DateTimeOffset)
    (existing: LockFile)
    (fresh: Mapping list)
    (vocabularies: Map<string, VocabularyEntry>)
    (declaredPrefixes: Map<string, string>)
    : ExtractSummary =
    let merged = mergeWithPreservation existing.Mappings fresh

    let updated =
        { existing with
            Generated = clock ()
            Vocabularies = vocabularies
            DeclaredPrefixes = declaredPrefixes
            Mappings = merged }

    let stamped = LockFile.withIntegrity updated
    Directory.CreateDirectory(Path.GetDirectoryName lockPath) |> ignore
    LockFile.write lockPath stamped
    summarize merged

// ── Main entry point ──────────────────────────────────────────────────────────

let private resolveSources
    (opts: ExtractOptions)
    (projectFile: string)
    : Result<string * string list * string list, string> =
    resolveVocabFile projectFile opts.VocabularyFile
    |> Result.bind (fun vocabFile ->
        sourceFilesFromFsproj projectFile
        |> Result.map (fun allSourceFiles ->
            let curated = curateSourceFiles allSourceFiles
            let domain = curated |> List.filter (fun f -> f <> vocabFile)
            vocabFile, curated, domain))

let private buildMappings
    (fetch: ConnegFetch)
    (clock: unit -> DateTimeOffset)
    (opts: ExtractOptions)
    (projectFile: string)
    (curated: string list)
    (domain: string list)
    : Result<Map<string, VocabularyEntry> * Mapping list * VocabularyRegistry * ConventionDiagnostic list, string> =
    tryEvalRegistry opts.AssemblyRefs curated
    |> Result.mapError (fun e -> $"registry eval failed: {e}")
    |> Result.bind (fun registry ->
        extractFromFiles domain
        |> Result.mapError (fun e -> $"type extraction failed: {e}")
        |> Result.bind (fun typeInfos ->
            let projectDir = Path.GetDirectoryName projectFile

            fetchVocabTerms fetch clock projectDir registry
            |> Async.RunSynchronously
            |> Result.mapError (fun e -> $"vocab fetch failed: {e}")
            |> Result.map (fun (terms, vocabEntries, termDiagnostics) ->
                let scored = typeInfos |> List.map (ConventionEngine.scoreDetailed terms registry)
                let fresh, diagnosticLists = List.unzip scored
                let diagnostics = termDiagnostics @ List.concat diagnosticLists
                vocabEntries, fresh, registry, diagnostics)))

/// Pipeline core with the vocabulary fetcher and clock injected.
/// `run` wraps this with the production HttpClient-backed fetcher and real clock.
let internal runWithFetch
    (fetch: ConnegFetch)
    (clock: unit -> DateTimeOffset)
    (opts: ExtractOptions)
    : Result<ExtractResult, string> =
    let projectFile = Path.GetFullPath opts.ProjectFile

    if not (File.Exists projectFile) then
        Error $"project file not found: {projectFile}"
    else
        resolveSources opts projectFile
        |> Result.bind (fun (_vocabFile, curated, domain) ->
            buildMappings fetch clock opts projectFile curated domain
            |> Result.map (fun (vocabEntries, fresh, registry, diagnostics) ->
                let lockPath = lockFilePath projectFile
                let existingLock = readOrEmptyLock lockPath

                let declaredPrefixes =
                    registry.Prefixes |> Map.map (fun _ (u: Uri) -> u.AbsoluteUri)

                let summary =
                    writeLock lockPath clock existingLock fresh vocabEntries declaredPrefixes

                { Summary = summary
                  Diagnostics = diagnostics }))

/// Run the extract pipeline.
/// No child processes; all FCS evaluation is in-process.
let run (opts: ExtractOptions) : Result<ExtractResult, string> =
    use client = RdfConneg.makeNoRedirectClient ()
    runWithFetch (RdfConneg.rdfFetch client) (fun () -> DateTimeOffset.UtcNow) opts
