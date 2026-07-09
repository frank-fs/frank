module Frank.Cli.Core.Validate

open System
open Frank.Semantic
open Frank.Semantic.LockFile

// ── Per-entry outcome ─────────────────────────────────────────────────────────

/// Outcome for a single vocab endpoint validated by frank semantic validate.
type ValidateOutcome =
    | Validated
    | LyingIri of reason: string
    | ValidateTransient of reason: string

type ValidateReport =
    { Outcomes: (string * ValidateOutcome) list }

/// Map a ValidateReport to the CLI exit code.
/// 2 if any LyingIri (durable — endpoint claims RDF IRI but doesn't serve RDF).
/// 1 if any ValidateTransient (operational, no lying-IRI).
/// 0 if all Validated.
let validateExitCode (report: ValidateReport) : int =
    let hasLying =
        report.Outcomes
        |> List.exists (fun (_, o) ->
            match o with
            | LyingIri _ -> true
            | _ -> false)

    let hasTransient =
        report.Outcomes
        |> List.exists (fun (_, o) ->
            match o with
            | ValidateTransient _ -> true
            | _ -> false)

    if hasLying then 2
    elif hasTransient then 1
    else 0

// ── Entry update helpers (pure) ───────────────────────────────────────────────

let private validatedEntry (now: DateTimeOffset) (ev: FetchEvidence) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        FetchedAt = now
        Hash = ev.Hash
        MediaType = ev.MediaType
        Validated = ev.Validated
        Terms = ev.Terms
        HttpStatus = ev.HttpStatus
        ETag = ev.ETag
        LastModified = ev.LastModified }

let private lyingEntry (now: DateTimeOffset) (reason: string) (status: int) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        HttpStatus = Some status
        Validated =
            { IsValidated = false
              Reason = Some reason
              LastChecked = Some now } }

let private transientEntry (now: DateTimeOffset) (reason: string) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        Validated =
            { entry.Validated with
                Reason = Some reason
                LastChecked = Some now } }

let private unchangedEntry (now: DateTimeOffset) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        FetchedAt = now
        Validated =
            { entry.Validated with
                LastChecked = Some now } }

// ── Single-entry validation ───────────────────────────────────────────────────

let private validateOne
    (fetch: ConnegFetch)
    (now: DateTimeOffset)
    (prefix: string)
    (entry: VocabularyEntry)
    : Async<ValidateOutcome * VocabularyEntry> =
    async {
        let namespaceBase = Uri(entry.Uri)
        let! result = fetch namespaceBase entry.ETag entry.LastModified
        let evidence = RdfConneg.buildEvidence namespaceBase now result

        match evidence with
        | Updated ev -> return Validated, validatedEntry now ev entry
        | Unchanged -> return Validated, unchangedEntry now entry
        | TransientFailure reason -> return ValidateTransient reason, transientEntry now reason entry
        | Undereferenceable reason ->
            let status =
                match result with
                | HttpErrorStatus(s, _) -> s
                | NonRdfContent r -> r.HttpStatus
                | _ -> 0

            return LyingIri reason, lyingEntry now reason status entry
    }

// ── Main validate ─────────────────────────────────────────────────────────────

/// Validate all Owned=true vocabulary entries by fetching them via the conneg path.
/// An endpoint serving non-RDF content when RDF is requested → LyingIri (Validated=false).
/// Returns a ValidateReport and the updated LockFile.
/// The caller is responsible for stamping integrity and writing the updated lock.
let validate (fetch: ConnegFetch) (now: DateTimeOffset) (lf: LockFile) : Async<ValidateReport * LockFile> =
    async {
        let ownedEntries =
            lf.Vocabularies |> Map.toList |> List.filter (snd >> (fun e -> e.Owned))

        let mutable outcomes: (string * ValidateOutcome) list = []
        let mutable updatedVocabs = lf.Vocabularies

        for prefix, entry in ownedEntries do
            let! outcome, updatedEntry = validateOne fetch now prefix entry
            outcomes <- outcomes @ [ prefix, outcome ]
            updatedVocabs <- Map.add prefix updatedEntry updatedVocabs

        let report = { Outcomes = outcomes }
        let updatedLf = { lf with Vocabularies = updatedVocabs }
        return report, updatedLf
    }
