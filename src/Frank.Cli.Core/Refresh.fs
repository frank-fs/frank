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
    EntryUpdate.outcomesExitCode
        (function
        | DriftDetected _ -> true
        | _ -> false)
        (function
        | ProbeFailed _ -> true
        | _ -> false)
        report.Outcomes

// ── Per-entry classification ──────────────────────────────────────────────────

// Owned and unowned agree on reachability failures (404/410/406/redirect-cap → durable).
// Content-drift is suppressed for owned: hash change is NOT flagged as DriftDetected.
// Unowned text/html or xhtml+xml (possibly RDFa) is non-durable: not verifiable offline, not exit-2.
let private classify
    (owned: bool)
    (now: DateTimeOffset)
    (namespaceBase: Uri)
    (entry: VocabularyEntry)
    (result: ConnegFetchResult)
    : EntryOutcome * VocabularyEntry =
    let evidence = RdfConneg.buildEvidence namespaceBase now result

    match evidence with
    | TransientFailure reason -> ProbeFailed reason, EntryUpdate.probeFailedEntry now reason entry
    | Unchanged -> EvidenceRefreshed, EntryUpdate.unchangedEntry now entry
    | Undereferenceable reason ->
        DriftDetected reason, EntryUpdate.goneEntry now reason (RdfConneg.statusOf result) entry
    | UnverifiableNonRdf reason ->
        if owned then
            // Owned endpoint serving possibly-RDFa content is a LyingIri — durable drift.
            DriftDetected reason, EntryUpdate.goneEntry now reason (RdfConneg.statusOf result) entry
        else
            // External possibly-RDFa content is not verifiable offline, not durable drift.
            EvidenceRefreshed, EntryUpdate.unverifiableEntry now reason entry
    | Updated ev ->
        if owned then
            // Content change does NOT constitute drift for owned (suppress content-drift).
            // Still capture all evidence (Terms, MediaType, Hash) so term evidence stays current.
            EvidenceRefreshed, EntryUpdate.updatedEntry now ev entry
        elif ev.Hash <> entry.Hash then
            let reason = $"content hash changed: {entry.Hash} → {ev.Hash}"
            DriftDetected reason, EntryUpdate.goneEntry now reason (ev.HttpStatus |> Option.defaultValue 0) entry
        else
            EvidenceRefreshed, EntryUpdate.updatedEntry now ev entry

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
                return ProbeFailed reason, EntryUpdate.probeFailedEntry now reason entry
            | true, namespaceBase ->
                let! result = fetch namespaceBase entry.ETag entry.LastModified
                return classify entry.Owned now namespaceBase entry result
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
            outcomes <- (prefix, outcome) :: outcomes
            updatedVocabs <- Map.add prefix updatedEntry updatedVocabs

        let report = { Outcomes = List.rev outcomes }
        let updatedLf = { lf with Vocabularies = updatedVocabs }
        return report, updatedLf
    }
