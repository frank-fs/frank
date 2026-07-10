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

// M2: non-durable, non-drift outcome for unowned text/html (possibly RDFa).
// Sets Validated=false so the entry is not trusted, but does not raise a drift alarm.
let private unverifiableEntry (now: DateTimeOffset) (reason: string) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        Validated =
            { IsValidated = false
              Reason = Some reason
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

// M4: classifyOwned is now a TRANSFORM over buildEvidence (kills the fork/asymmetry).
// Owned and unowned agree on reachability failures (404/410/406/redirect-cap → durable).
// Content-drift is suppressed for owned: hash change is NOT flagged as DriftDetected.
// M7: capture Terms/MediaType in the owned refresh path via updatedEntry (side effect of M4).
let private classifyOwned
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
        let status = RdfConneg.statusOf result
        DriftDetected reason, goneEntry now status reason entry
    | UnverifiableNonRdf reason ->
        // Owned endpoint serving text/html is a LyingIri — same semantics as Undereferenceable.
        // An owned vocab that claims to serve RDF but returns HTML is durable drift.
        let status = RdfConneg.statusOf result
        DriftDetected reason, goneEntry now status reason entry
    | Updated ev ->
        // Content change does NOT constitute drift for owned (suppress content-drift).
        // Still capture all evidence (Terms, MediaType, Hash) so term evidence stays current.
        EvidenceRefreshed, updatedEntry now ev entry

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
        let status = RdfConneg.statusOf result
        DriftDetected reason, goneEntry now status reason entry
    | UnverifiableNonRdf reason ->
        // M2: external text/html (possibly RDFa) is not verifiable offline.
        // Not durable drift — do not raise exit 2. Mark Validated=false but return EvidenceRefreshed.
        EvidenceRefreshed, unverifiableEntry now reason entry
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
            // L4: guard Uri parse — a malformed persisted URI is a modeled error, not an exception.
            match Uri.TryCreate(entry.Uri, UriKind.Absolute) with
            | false, _ ->
                let reason = $"malformed vocabulary URI: {entry.Uri}"
                return ProbeFailed reason, probeFailedEntry now reason entry
            | true, namespaceBase ->
                let! result = fetch namespaceBase entry.ETag entry.LastModified

                if entry.Owned then
                    return classifyOwned now namespaceBase entry result
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
