module Frank.Cli.SemanticCommands

open System
open System.IO
open System.Text
open System.Text.Json
open System.Collections.Generic
open System.Collections.ObjectModel
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Frank.Semantic
open Frank.Cli.Core

// ── Summary types ─────────────────────────────────────────────────────────────

type ExtractSummary =
    { Confirmed: int
      Proposed: int
      Unresolved: int }

// ── Public helpers ────────────────────────────────────────────────────────────

/// Count mappings by status.
let summarizeMappings (mappings: TypeMapping list) : ExtractSummary =
    let mutable confirmed = 0
    let mutable proposed = 0
    let mutable unresolved = 0

    for m in mappings do
        match m.Status with
        | Confirmed -> confirmed <- confirmed + 1
        | Proposed -> proposed <- proposed + 1
        | Unresolved -> unresolved <- unresolved + 1

    { Confirmed = confirmed
      Proposed = proposed
      Unresolved = unresolved }

/// Format summary for stdout.
let formatSummary (s: ExtractSummary) : string =
    $"Confirmed: {s.Confirmed}, Proposed: {s.Proposed}, Unresolved: {s.Unresolved}"

/// Reconstruct a VocabularyRegistry from parsed prefix/using data.
let buildRegistryFromParsed (prefixes: Map<string, Uri>) (usingSet: Set<string>) : VocabularyRegistry =
    { Prefixes = prefixes
      Using = usingSet
      EquivalentClasses = ReadOnlyDictionary<Type, Uri>(Dictionary<Type, Uri>())
      SeeAlso = ReadOnlyDictionary<Type, Uri list>(Dictionary<Type, Uri list>())
      FieldSeeAlso = ReadOnlyDictionary<(Type * string), Uri list>(Dictionary<(Type * string), Uri list>())
      ProvClasses = ReadOnlyDictionary<Type, ProvOClass>(Dictionary<Type, ProvOClass>())
      ConstraintPatterns =
        ReadOnlyDictionary<(Type * string), string>(Dictionary<(Type * string), string>()) }

/// Write a lock file to the given path, creating parent dirs as needed.
let writeLockFile
    (lockPath: string)
    (vocabularies: Map<string, VocabularyEntry>)
    (mappings: TypeMapping list)
    : unit =
    let lf =
        { SchemaVersion = 1
          Generated = DateTimeOffset.UtcNow
          Vocabularies = vocabularies
          Mappings = mappings }

    LockFile.write lockPath lf

// ── AST vocabulary parser ─────────────────────────────────────────────────────

module private AstParser =

    /// Extract a string literal value from an FCS SynExpr, or None.
    let private tryStringLiteral (expr: SynExpr) : string option =
        match expr with
        | SynExpr.Const(SynConst.String(s, _, _), _) -> Some s
        | _ -> None

    /// Walk CE body sequentials to collect custom operation calls.
    let rec private collectCeOps (expr: SynExpr) (acc: (string * string list) list) =
        match expr with
        | SynExpr.Sequential(_, _, left, right, _, _) ->
            let acc' = collectCeOps left acc
            collectCeOps right acc'
        | SynExpr.App(_, _, SynExpr.App(_, _, SynExpr.Ident ident, arg1, _), arg2, _) ->
            let args =
                [ tryStringLiteral arg1; tryStringLiteral arg2 ] |> List.choose id

            acc @ [ ident.idText, args ]
        | SynExpr.App(_, _, SynExpr.Ident ident, arg, _) ->
            match tryStringLiteral arg with
            | Some s -> acc @ [ ident.idText, [ s ] ]
            | None -> acc
        | _ -> acc

    /// Find vocabulary CE bodies within module declarations.
    let rec private findVocabCeBodies (decls: SynModuleDecl list) : SynExpr list =
        decls
        |> List.collect (fun decl ->
            match decl with
            | SynModuleDecl.Let(_, bindings, _) ->
                bindings
                |> List.choose (fun binding ->
                    let (SynBinding(_, _, _, _, _, _, _, _, _, body, _, _, _)) = binding

                    match body with
                    | SynExpr.App(_, _, _, SynExpr.ComputationExpr(_, ceBody, _), _) -> Some ceBody
                    | _ -> None)
            | SynModuleDecl.NestedModule(_, _, nestedDecls, _, _, _) -> findVocabCeBodies nestedDecls
            | _ -> [])

    type ParsedVocabulary =
        { Prefixes: Map<string, Uri>
          Using: Set<string> }

    let parseFile (filePath: string) : Result<ParsedVocabulary, string> =
        if not (File.Exists filePath) then
            Error $"Vocabulary file not found: {filePath}"
        else

            let checker = FSharpChecker.Create()
            let source = File.ReadAllText(filePath)
            let sourceText = SourceText.ofString source

            let parsingOpts =
                { FSharpParsingOptions.Default with
                    SourceFiles = [| filePath |] }

            let parseResult =
                checker.ParseFile(filePath, sourceText, parsingOpts)
                |> Async.RunSynchronously

            if parseResult.ParseHadErrors then
                let errors =
                    parseResult.Diagnostics
                    |> Array.map (fun d ->
                        let r = d.Range
                        $"{r.FileName}:{r.StartLine}:{r.StartColumn}: {d.Message}")
                    |> String.concat "\n"

                Error errors
            else

                let bodies =
                    match parseResult.ParseTree with
                    | ParsedInput.ImplFile(ParsedImplFileInput(contents = contents)) ->
                        contents
                        |> List.collect (fun (SynModuleOrNamespace(decls = decls)) ->
                            findVocabCeBodies decls)
                    | _ -> []

                let ops =
                    bodies |> List.collect (fun body -> collectCeOps body [])

                let mutable prefixes = Map.empty<string, Uri>
                let mutable usingSet = Set.empty<string>

                for name, args in ops do
                    match name, args with
                    | "prefix", [ prefixName; iri ] ->
                        try
                            prefixes <- prefixes |> Map.add prefixName (Uri(iri))
                        with _ ->
                            ()
                    | "using", [ vocabName ] -> usingSet <- usingSet |> Set.add vocabName
                    | _ -> ()

                Ok { Prefixes = prefixes; Using = usingSet }

// ── Public API ────────────────────────────────────────────────────────────────

/// Parse a vocabulary file and return (prefixes, usingSet) or an error string.
let parseVocabularyFile (filePath: string) : Result<Map<string, Uri> * Set<string>, string> =
    match AstParser.parseFile filePath with
    | Error e -> Error e
    | Ok v -> Ok(v.Prefixes, v.Using)

// ── Extract pipeline ──────────────────────────────────────────────────────────

type ExtractOptions =
    { ProjectFile: string
      VocabularyFile: string option
      CacheDir: string option }

type ExtractResult =
    { LockPath: string
      Summary: ExtractSummary }

/// Locate the vocabulary file. Checks explicit option first, then looks for Vocabulary.fs.
let private locateVocabularyFile (projectDir: string) (explicitPath: string option) : Result<string option, string> =
    match explicitPath with
    | Some p when File.Exists p -> Ok(Some p)
    | Some p -> Error $"Specified vocabulary file not found: {p}"
    | None ->
        let candidate = Path.Combine(projectDir, "Vocabulary.fs")
        Ok(if File.Exists candidate then Some candidate else None)

/// Build VocabularyEntry records for use in the lock file's vocabularies section.
let private buildVocabularyEntries (fetchResults: FetchResult list) : Map<string, VocabularyEntry> =
    fetchResults
    |> List.map (fun r ->
        r.Prefix,
        { Uri = r.Uri
          FetchedAt = Some(DateTimeOffset.UtcNow.ToString("O"))
          Hash = Some r.Hash })
    |> Map.ofList

/// Merge fetched graphs into a single IGraph for ConventionEngine.
let private mergeGraphs (fetchResults: FetchResult list) : VDS.RDF.IGraph =
    let merged = new VDS.RDF.Graph()

    for r in fetchResults do
        merged.Merge(r.Graph)

    merged

/// Run the full extract pipeline.
let extract (opts: ExtractOptions) : Result<ExtractResult, string> =
    let projectFile =
        if opts.ProjectFile = "" then
            let candidates = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.fsproj")

            if candidates.Length = 0 then
                ""
            else
                candidates.[0]
        else
            opts.ProjectFile

    if projectFile = "" then
        Error "No .fsproj found in current directory"
    elif not (File.Exists projectFile) then
        Error $"Project file not found: {projectFile}"
    else

        let projectDir = Path.GetDirectoryName(projectFile)
        let lockPath = Path.Combine(projectDir, ".frank", "semantic-mappings.lock.json")
        let cacheDir = opts.CacheDir |> Option.defaultValue (Path.Combine(projectDir, ".frank", "cache"))

        match locateVocabularyFile projectDir opts.VocabularyFile with
        | Error e -> Error e
        | Ok vocabFileOpt ->

            let registryResult =
                match vocabFileOpt with
                | None -> Ok(buildRegistryFromParsed Map.empty Set.empty, [])
                | Some vocabFile ->
                    match parseVocabularyFile vocabFile with
                    | Error e -> Error e
                    | Ok(prefixes, usingSet) ->
                        let fetchResults =
                            usingSet
                            |> Set.toList
                            |> List.choose (fun name ->
                                prefixes
                                |> Map.tryFind name
                                |> Option.map (fun uri ->
                                    VocabFetcher.fetchOrLoad cacheDir name (uri.ToString())))

                        Ok(buildRegistryFromParsed prefixes usingSet, fetchResults)

            match registryResult with
            | Error e -> Error e
            | Ok(registry, fetchResults) ->
                let graph = mergeGraphs fetchResults
                let vocabularyEntries = buildVocabularyEntries fetchResults

                let types = Extractor.extractTypeInfos projectFile

                let existingLock =
                    if File.Exists lockPath then
                        match LockFile.read lockPath with
                        | Ok lf -> lf
                        | Error _ ->
                            { SchemaVersion = 1
                              Generated = DateTimeOffset.UtcNow
                              Vocabularies = vocabularyEntries
                              Mappings = [] }
                    else
                        { SchemaVersion = 1
                          Generated = DateTimeOffset.UtcNow
                          Vocabularies = vocabularyEntries
                          Mappings = [] }

                let resolved = ConventionEngine.matchTypes registry graph existingLock.Mappings types
                let merged = LockFile.merge existingLock resolved

                let updatedLock =
                    { merged with
                        Generated = DateTimeOffset.UtcNow
                        Vocabularies = vocabularyEntries }

                LockFile.write lockPath updatedLock

                let summary = summarizeMappings updatedLock.Mappings

                Ok
                    { LockPath = lockPath
                      Summary = summary }

// ── Clarify ───────────────────────────────────────────────────────────────────

type ClarifyFormat =
    | Json
    | Markdown

/// Render clarify output as indented JSON.
let private clarifyJson (unresolved: TypeMapping list) (proposed: TypeMapping list) : string =
    let opts = JsonWriterOptions(Indented = true)
    use ms = new MemoryStream()
    use writer = new Utf8JsonWriter(ms, opts)
    writer.WriteStartObject()
    writer.WriteNumber("schemaVersion", 1)
    writer.WriteStartArray("unresolved")

    for m in unresolved do
        writer.WriteStartObject()
        writer.WriteString("fsharpType", m.FsharpType)
        writer.WriteStartArray("fields")

        for f in m.Fields do
            writer.WriteStartObject()
            writer.WriteString("name", f.Name)
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()

    writer.WriteEndArray()
    writer.WriteStartArray("proposed")

    for m in proposed do
        writer.WriteStartObject()
        writer.WriteString("fsharpType", m.FsharpType)
        writer.WriteString("currentCandidate", m.Iri)
        writer.WriteNumber("confidence", m.Confidence)
        writer.WriteEndObject()

    writer.WriteEndArray()
    writer.WriteEndObject()
    writer.Flush()
    Text.Encoding.UTF8.GetString(ms.ToArray())

/// Render clarify output as Markdown.
let private clarifyMarkdown (unresolved: TypeMapping list) (proposed: TypeMapping list) : string =
    let sb = StringBuilder()

    if unresolved <> [] then
        sb.AppendLine("## Unresolved Types") |> ignore

        for m in unresolved do
            sb.AppendLine($"\n### {m.FsharpType}") |> ignore
            sb.AppendLine("\n| Field | Type |") |> ignore
            sb.AppendLine("|-------|------|") |> ignore

            for f in m.Fields do
                sb.AppendLine($"| {f.Name} | {f.Iri} |") |> ignore

    if proposed <> [] then
        sb.AppendLine("\n## Proposed Types") |> ignore

        for m in proposed do
            sb.AppendLine($"\n### {m.FsharpType}") |> ignore
            sb.AppendLine($"\n**Current candidate:** {m.Iri} (confidence: {m.Confidence:F2})") |> ignore

    sb.ToString()

// ── Accept ────────────────────────────────────────────────────────────────────

/// Merge LLM-resolved or hand-edited mappings from resolvedJson into the lock file.
/// Returns Ok summaryMessage on success, or Error errorMessage on schema-version mismatch or parse failure.
/// Unknown F# types in resolvedJson are skipped with a warning but do not cause failure.
let accept (lockFilePath: string) (resolvedJson: string) (source: MappingSource) : Result<string, string> =
    use doc =
        try
            JsonDocument.Parse(resolvedJson)
        with ex ->
            raise (invalidArg "resolvedJson" $"Invalid JSON: {ex.Message}")

    let root = doc.RootElement
    let schemaVersion = root.GetProperty("schemaVersion").GetInt32()

    if schemaVersion <> 1 then
        Error $"schema version {schemaVersion} not supported"
    else

        match LockFile.read lockFilePath with
        | Error msg -> Error $"Cannot read lock file: {msg}"
        | Ok lockFile ->

            let existingByType = lockFile.Mappings |> List.map (fun m -> m.FsharpType, m) |> Map.ofList

            let mutable merged = 0
            let mutable rejected = 0
            let mutable unchanged = 0

            let resolved =
                root.GetProperty("resolved").EnumerateArray()
                |> Seq.choose (fun entry ->
                    let fsharpType = entry.GetProperty("fsharpType").GetString()
                    let iri = entry.GetProperty("iri").GetString()

                    match existingByType |> Map.tryFind fsharpType with
                    | None ->
                        eprintfn $"{fsharpType} not in lock file; ignored"
                        rejected <- rejected + 1
                        None
                    | Some existing ->
                        if existing.Status = Confirmed && existing.Iri = iri && existing.Source = source then
                            unchanged <- unchanged + 1
                            None
                        else
                            merged <- merged + 1

                            Some
                                { existing with
                                    Status = Confirmed
                                    Source = source
                                    Iri = iri })
                |> List.ofSeq

            let resolvedByType = resolved |> List.map (fun m -> m.FsharpType, m) |> Map.ofList

            let updatedMappings =
                lockFile.Mappings
                |> List.map (fun m ->
                    resolvedByType |> Map.tryFind m.FsharpType |> Option.defaultValue m)

            let updatedLock =
                { lockFile with
                    Generated = DateTimeOffset.UtcNow
                    Mappings = updatedMappings }

            LockFile.write lockFilePath updatedLock
            Ok $"Merged {merged} mapping; {rejected} rejected; {unchanged} unchanged"

// ── Refresh ───────────────────────────────────────────────────────────────────

/// Re-fetch vocabularies and report drift. Returns list of (prefix, oldHash, newHash).
/// Does not mutate the lock file.
let refresh (lockFilePath: string) (cacheDir: string) : (string * string * string) list =
    match LockFile.read lockFilePath with
    | Error _ -> []
    | Ok lockFile -> VocabFetcher.detectDrift cacheDir lockFile

// ── Status ────────────────────────────────────────────────────────────────────

/// Summarize lock-file mapping counts by status.
let status (lockFilePath: string) : string =
    match LockFile.read lockFilePath with
    | Error msg -> invalidArg "lockFilePath" $"Cannot read lock file: {msg}"
    | Ok lockFile ->
        let s = summarizeMappings lockFile.Mappings

        $"Confirmed: {s.Confirmed,3}\nProposed: {s.Proposed,4}\nUnresolved: {s.Unresolved,2}"

/// Emit unresolved and proposed entries from a lock file as structured output.
let clarify (lockFilePath: string) (format: ClarifyFormat) : string =
    let lockFile =
        match LockFile.read lockFilePath with
        | Ok lf -> lf
        | Error msg -> invalidArg "lockFilePath" $"Cannot read lock file: {msg}"

    let unresolved = lockFile.Mappings |> List.filter (fun m -> m.Status = Unresolved)
    let proposed = lockFile.Mappings |> List.filter (fun m -> m.Status = Proposed)

    match format with
    | Json -> clarifyJson unresolved proposed
    | Markdown -> clarifyMarkdown unresolved proposed
