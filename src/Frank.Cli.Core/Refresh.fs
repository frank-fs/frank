module Frank.Cli.Core.Refresh

open System
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabClassifier

// ── Per-entry outcome ─────────────────────────────────────────────────────────

/// Outcome for a single vocabulary entry in a refresh run.
type EntryOutcome =
    | SkippedFresh
    | EvidenceRefreshed
    | DriftDetected of reason: string
    | ProbeFailed of reason: string

type RefreshReport =
    { Outcomes: (string * EntryOutcome) list }

/// Map a RefreshReport to the CLI exit code.
/// Drift-dominates: exit 2 if any DriftDetected, 1 if any ProbeFailed (but no drift), 0 otherwise.
let refreshExitCode (report: RefreshReport) : int =
    let hasDrift =
        report.Outcomes
        |> List.exists (fun (_, o) ->
            match o with
            | DriftDetected _ -> true
            | _ -> false)

    let hasFailed =
        report.Outcomes
        |> List.exists (fun (_, o) ->
            match o with
            | ProbeFailed _ -> true
            | _ -> false)

    if hasDrift then 2
    elif hasFailed then 1
    else 0

// ── Entry update helpers (pure) ───────────────────────────────────────────────

let private goneEntry (now: DateTimeOffset) (status: int) (reason: string) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        HttpStatus = Some status
        Validated =
            { IsValidated = false
              Reason = Some reason
              LastChecked = Some now } }

let private probeFailedEntry (now: DateTimeOffset) (reason: string) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        Validated =
            { entry.Validated with
                Reason = Some reason
                LastChecked = Some now } }

let private reachableEntry
    (now: DateTimeOffset)
    (status: int)
    (etag: string option)
    (lastMod: string option)
    (entry: VocabularyEntry)
    : VocabularyEntry =
    { entry with
        FetchedAt = now
        HttpStatus = Some status
        ETag = etag
        LastModified = lastMod
        Validated =
            { entry.Validated with
                LastChecked = Some now } }

let private updatedEntry (now: DateTimeOffset) (ev: FetchEvidence) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        FetchedAt = now
        Hash = ev.Hash
        MediaType = ev.MediaType
        Validated = ev.Validated
        Terms = ev.Terms
        HttpStatus = ev.HttpStatus
        ETag = ev.ETag
        LastModified = ev.LastModified }

let private unchangedEntry (now: DateTimeOffset) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        FetchedAt = now
        Validated =
            { entry.Validated with
                LastChecked = Some now } }

// ── Per-entry classification ──────────────────────────────────────────────────

let private classifyOwned
    (now: DateTimeOffset)
    (entry: VocabularyEntry)
    (result: ConnegFetchResult)
    : EntryOutcome * VocabularyEntry =
    match result with
    | HttpErrorStatus(404, _) ->
        let r = "HTTP 404 — owned vocab gone"
        DriftDetected r, goneEntry now 404 r entry
    | HttpErrorStatus(410, _) ->
        let r = "HTTP 410 — owned vocab permanently gone"
        DriftDetected r, goneEntry now 410 r entry
    | HttpErrorStatus(status, _) ->
        let r = $"HTTP {status} probe-failed"
        ProbeFailed r, probeFailedEntry now r entry
    | FetchFailed reason ->
        let r = $"network error: {reason}"
        ProbeFailed r, probeFailedEntry now r entry
    | RedirectCapHit ->
        let r = $"redirect cap ({RdfConneg.maxRedirectHops} hops) exceeded"
        ProbeFailed r, probeFailedEntry now r entry
    | NotModified -> EvidenceRefreshed, unchangedEntry now entry
    | NonRdfContent r ->
        let reason = $"owned vocab serving non-RDF: {r.MediaType} (HTTP {r.HttpStatus})"
        DriftDetected reason, goneEntry now r.HttpStatus reason entry
    | RdfContent r -> EvidenceRefreshed, reachableEntry now r.HttpStatus r.ETag r.LastModified entry

let private classifyUnowned
    (now: DateTimeOffset)
    (namespaceBase: Uri)
    (entry: VocabularyEntry)
    (result: ConnegFetchResult)
    : EntryOutcome * VocabularyEntry =
    let evidence = RdfConneg.buildEvidence namespaceBase now result

    match evidence with
    | TransientFailure reason -> ProbeFailed reason, probeFailedEntry now reason entry
    | Unchanged -> EvidenceRefreshed, unchangedEntry now entry
    | Undereferenceable reason ->
        let status =
            match result with
            | HttpErrorStatus(s, _) -> s
            | NonRdfContent r -> r.HttpStatus
            | _ -> 0

        DriftDetected reason, goneEntry now status reason entry
    | Updated ev ->
        if ev.Hash <> entry.Hash then
            let reason = $"content hash changed: {entry.Hash} → {ev.Hash}"
            DriftDetected reason, goneEntry now (ev.HttpStatus |> Option.defaultValue 0) reason entry
        else
            EvidenceRefreshed, updatedEntry now ev entry

// ── Per-entry driver ──────────────────────────────────────────────────────────

let private processEntry
    (fetch: ConnegFetch)
    (policy: SlaPolicy)
    (now: DateTimeOffset)
    (force: bool)
    (prefix: string)
    (entry: VocabularyEntry)
    : Async<EntryOutcome * VocabularyEntry> =
    async {
        if not force && not (isStale policy prefix entry now) then
            return SkippedFresh, entry
        else
            let namespaceBase = Uri(entry.Uri)
            let! result = fetch namespaceBase entry.ETag entry.LastModified

            if entry.Owned then
                return classifyOwned now entry result
            else
                return classifyUnowned now namespaceBase entry result
    }

// ── Main refresh ──────────────────────────────────────────────────────────────

/// Re-verify every vocabulary entry according to the SLA policy.
/// Per-entry continuation: no early abort. Transient failures record last-checked without
/// flipping Validated. Durable rot (404/410/hash-change/lying-IRI) marks Validated=false.
/// Returns a RefreshReport (outcomes by prefix) and an updated LockFile.
/// The caller is responsible for stamping integrity and writing the updated lock.
let refresh
    (fetch: ConnegFetch)
    (policy: SlaPolicy)
    (now: DateTimeOffset)
    (force: bool)
    (lf: LockFile)
    : Async<RefreshReport * LockFile> =
    async {
        let entries = lf.Vocabularies |> Map.toList
        let mutable outcomes: (string * EntryOutcome) list = []
        let mutable updatedVocabs = lf.Vocabularies

        for prefix, entry in entries do
            let! outcome, updatedEntry = processEntry fetch policy now force prefix entry
            outcomes <- outcomes @ [ prefix, outcome ]
            updatedVocabs <- Map.add prefix updatedEntry updatedVocabs

        let report = { Outcomes = outcomes }
        let updatedLf = { lf with Vocabularies = updatedVocabs }
        return report, updatedLf
    }
