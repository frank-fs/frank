module Frank.Analyzers.UndereferenceableVocabAnalyzer

open System
open System.IO
open FSharp.Analyzers.SDK
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabClassifier
open Frank.Analyzers.AstExtractors

// ── Diagnostic codes ──────────────────────────────────────────────────────────

[<Literal>]
let Code = "FRANK002"

[<Literal>]
let private LockIntegrityCode = "FRANK003"

[<Literal>]
let private StaleCode = "FRANK004"

[<Literal>]
let private RouteHintCode = "FRANK005"

[<Literal>]
let private UnknownTermCode = "FRANK006"

[<Literal>]
let private OwnershipNudgeCode = "FRANK007"

[<Literal>]
let private LockFileName = ".frank/semantic-mappings.lock.json"

[<Literal>]
let private WalkUpCap = 15

// ── Lock walk-up (bounded per Holzmann #10) ──────────────────────────────────

let private findLockFile (startDir: string) : string option =
    let rec walk (dir: string) (depth: int) =
        let candidate = Path.Combine(dir, LockFileName)
        let parent = Directory.GetParent dir

        if depth > WalkUpCap then None
        elif File.Exists candidate then Some candidate
        elif isNull parent then None
        else walk parent.FullName (depth + 1)

    walk startDir 0

// ── I/O shell: find, read, and verify the lock ──────────────────────────────

let private verifyLock (path: string) : Result<LockFile, string> option =
    match LockFile.read path with
    | Error msg -> Some(Error msg)
    | Ok lf -> verifyIfStamped lf |> Result.map (fun () -> lf) |> Some

/// Returns:
///   None                   — no lock file found; emit nothing
///   Some (Error msg)       — lock found but tampered/unreadable; emit FRANK003
///   Some (Ok lock)         — lock valid; proceed to classify
let loadAndVerifyLock (dir: string) : Result<LockFile, string> option =
    if String.IsNullOrEmpty dir then
        invalidArg (nameof dir) "directory must not be null or empty"

    findLockFile dir |> Option.bind verifyLock

// ── Diagnostic builders ───────────────────────────────────────────────────────

let private makeMsg ty text code sev range : Message =
    { Type = ty
      Message = text
      Code = code
      Severity = sev
      Range = range
      Fixes = [] }

// Error: trust failure — the lock cannot be used for any classification.
// Analyzer Error diagnostics do NOT gate dotnet build (no fsc pipeline integration).
let private makeIntegrityDiag (range: Range) (msg: string) : Message =
    makeMsg
        "Lock file integrity"
        $"The semantic lock file cannot be trusted: {msg}. Regenerate it with 'frank semantic finalize'."
        LockIntegrityCode
        Severity.Error
        range

let private makeUndereferenceable (range: Range) (prefix: string) : Message =
    makeMsg
        "Undereferenceable vocabulary"
        $"Vocabulary '{prefix}' is not confirmed-dereferenceable in the semantic lock. Run 'frank semantic validate' to confirm reachability."
        Code
        Severity.Warning
        range

let private makeRouteHint (range: Range) (prefix: string) : Message =
    makeMsg
        "Validation nudge"
        $"A route path matches the namespace path for vocabulary '{prefix}'; run 'frank semantic validate' to confirm reachability and remove this warning."
        RouteHintCode
        Severity.Info
        range

let private makeOwnershipNudge (range: Range) (prefix: string) : Message =
    makeMsg
        "Validation nudge"
        $"Vocabulary '{prefix}' is recorded as owned but not yet confirmed reachable; run 'frank semantic validate'."
        OwnershipNudgeCode
        Severity.Info
        range

let private makeStale (range: Range) (prefix: string) : Message =
    makeMsg
        "Stale vocabulary"
        $"Vocabulary '{prefix}' has not been refreshed recently; a scheduled refresh will update it."
        StaleCode
        Severity.Info
        range

let private makeUnknownTerm (range: Range) (curie: string) : Message =
    makeMsg
        "Unknown vocabulary term"
        $"Term '{curie}' is not present in the confirmed term set for its vocabulary. Verify the CURIE is correct."
        UnknownTermCode
        Severity.Warning
        range

// ── Namespace path helpers ────────────────────────────────────────────────────

let private extractAbsolutePath (uri: string) : string option =
    match Uri.TryCreate(uri, UriKind.Absolute) with
    | true, u -> Some u.AbsolutePath
    | _ -> None

// ── Pure analysis logic (thin adapter over Core) ─────────────────────────────

let private prefixDiags (range: Range) (prefix: string) (state: VocabState) (routeCovers: bool) : Message list =
    match state with
    | VocabState.Confirmed
    | VocabState.Proposed -> []
    | VocabState.Undereferenceable ->
        let hint = if routeCovers then [ makeRouteHint range prefix ] else []
        makeUndereferenceable range prefix :: hint
    | VocabState.Stale -> [ makeStale range prefix ]
    | VocabState.LocallyServedUnconfirmed ->
        if routeCovers then
            [ makeRouteHint range prefix ]
        else
            [ makeOwnershipNudge range prefix ]

let private vocabDiagnostics
    (lock: LockFile)
    (byUri: Map<string, VocabularyEntry>)
    (now: DateTimeOffset)
    (routes: string list)
    (referencedTerms: (string * string) list)
    (range: Range)
    : Message list =
    let prefixKeys = referencedTerms |> List.map fst |> List.distinct
    let states = classifyReferencedVocabWith lock byUri now prefixKeys

    List.zip prefixKeys states
    |> List.collect (fun (prefix, state) ->
        let nsPath =
            lock.DeclaredPrefixes |> Map.tryFind prefix |> Option.bind extractAbsolutePath

        let routeCovers = nsPath |> Option.exists (routeCoversNsPath routes)
        prefixDiags range prefix state routeCovers)

let private checkTermMembership
    (range: Range)
    (prefix: string)
    (localName: string)
    (entry: VocabularyEntry)
    : Message option =
    match entry.Terms with
    | None -> None
    | Some terms when terms.IsEmpty -> None
    | Some terms ->
        let curie = $"{prefix}:{localName}"
        // Terms are stored as bare local names by RdfConneg.termsInNamespace ("Game", not "schema:Game").
        // Compare localName against the set; reconstruct curie only for the diagnostic message.
        if terms.Contains localName then
            None
        else
            Some(makeUnknownTerm range curie)

let private termDiagnostics
    (lock: LockFile)
    (byUri: Map<string, VocabularyEntry>)
    (referencedTerms: (string * string) list)
    (range: Range)
    : Message list =
    referencedTerms
    |> List.choose (fun (prefix, localName) ->
        lookupEntry lock byUri prefix
        |> Option.bind (checkTermMembership range prefix localName))

/// Analyze a parse tree given a pre-loaded lock result. Emits all diagnostics including FRANK004.
///   None             → no lock found → no diagnostics
///   Some (Error msg) → tampered/unreadable lock → FRANK003 diagnostic
///   Some (Ok lock)   → classify vocabs + terms
/// `now` is injected; Core never reads the clock.
/// #419: a declared-but-unfetched owned prefix classifies as LocallyServedUnconfirmed
/// (Info nudge) instead of Undereferenceable (Warning), ownership derived entirely from
/// the lock's own Mappings (see VocabClassifier.ownedIdentityAuthorities) — never from a
/// base URI, config, or flag, so the editor/CLI analyzer channel needs none of those.
/// For the CLI/CI path use analyzeWithLockCli which suppresses the clock-dependent FRANK004.
let analyzeWithLock
    (lockResult: Result<LockFile, string> option)
    (now: DateTimeOffset)
    (parseTree: ParsedInput)
    : Message list =
    let range = fileStartRange parseTree

    match lockResult with
    | None -> []
    | Some(Error msg) -> [ makeIntegrityDiag range msg ]
    | Some(Ok lock) ->
        let routes = extractRoutes parseTree
        let referencedTerms = extractReferencedTerms parseTree
        let byUri = buildVocabUriIndex lock.Vocabularies

        vocabDiagnostics lock byUri now routes referencedTerms range
        @ termDiagnostics lock byUri referencedTerms range

/// CLI/CI variant: same as analyzeWithLock but suppresses FRANK004 (staleness).
/// Staleness is editor-only advisory; CI gates must not depend on the system clock.
let analyzeWithLockCli
    (lockResult: Result<LockFile, string> option)
    (now: DateTimeOffset)
    (parseTree: ParsedInput)
    : Message list =
    analyzeWithLock lockResult now parseTree
    |> List.filter (fun m -> m.Code <> StaleCode)

// ── Analyzer registrations ────────────────────────────────────────────────────

[<Literal>]
let name = "UndereferenceableVocabAnalyzer"

[<Literal>]
let shortDescription =
    "Detects vocabulary namespaces not confirmed-dereferenceable in the semantic lock"

// Per-code documentation: https://github.com/frank-fs/frank/blob/master/docs/diagnostics.md
// SDK 0.35.0 supports one helpUri per analyzer registration (no per-Message field).
[<Literal>]
let helpUri = "https://github.com/frank-fs/frank/blob/master/docs/diagnostics.md"

let private runAnalysis (fileName: string) (parseTree: ParsedInput) : Message list =
    let dir = Path.GetDirectoryName fileName

    if String.IsNullOrEmpty dir then
        []
    else
        let lockResult = loadAndVerifyLock dir
        analyzeWithLock lockResult DateTimeOffset.UtcNow parseTree

let private runAnalysisCli (fileName: string) (parseTree: ParsedInput) : Message list =
    let dir = Path.GetDirectoryName fileName

    if String.IsNullOrEmpty dir then
        []
    else
        analyzeWithLockCli (loadAndVerifyLock dir) DateTimeOffset.UtcNow parseTree

[<EditorAnalyzer(name, shortDescription, helpUri)>]
let editorAnalyzer: Analyzer<EditorContext> =
    fun ctx -> async { return runAnalysis ctx.FileName ctx.ParseFileResults.ParseTree }

[<CliAnalyzer(name, shortDescription, helpUri)>]
let cliAnalyzer: Analyzer<CliContext> =
    fun ctx -> async { return runAnalysisCli ctx.FileName ctx.ParseFileResults.ParseTree }
