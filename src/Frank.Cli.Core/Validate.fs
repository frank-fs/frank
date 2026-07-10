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
    EntryUpdate.outcomesExitCode
        (function
        | LyingIri _ -> true
        | _ -> false)
        (function
        | ValidateTransient _ -> true
        | _ -> false)
        report.Outcomes

// ── Single-entry validation ───────────────────────────────────────────────────

let private validateOne
    (fetch: ConnegFetch)
    (now: DateTimeOffset)
    (prefix: string)
    (entry: VocabularyEntry)
    : Async<ValidateOutcome * VocabularyEntry> =
    async {
        // L4: guard Uri parse — a malformed persisted URI is a modeled error, not an exception.
        match Uri.TryCreate(entry.Uri, UriKind.Absolute) with
        | false, _ ->
            let reason = $"malformed vocabulary URI: {entry.Uri}"
            return ValidateTransient reason, EntryUpdate.probeFailedEntry now reason entry
        | true, namespaceBase ->
            let! result = fetch namespaceBase entry.ETag entry.LastModified
            let evidence = RdfConneg.buildEvidence namespaceBase now result

            match evidence with
            | Updated ev -> return Validated, EntryUpdate.updatedEntry now ev entry
            | Unchanged -> return Validated, EntryUpdate.unchangedEntry now entry
            | TransientFailure reason -> return ValidateTransient reason, EntryUpdate.probeFailedEntry now reason entry
            | Undereferenceable reason ->
                return LyingIri reason, EntryUpdate.goneEntry now reason (RdfConneg.statusOf result) entry
            | UnverifiableNonRdf reason ->
                // A-C7: owned endpoint returning possibly-RDFa content is a LyingIri — the endpoint
                // claims to serve RDF (it is owned and declared as a vocab IRI) but does not.
                // validate only runs on Owned=true entries; UnverifiableNonRdf is still a lying IRI here.
                return LyingIri reason, EntryUpdate.goneEntry now reason (RdfConneg.statusOf result) entry
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
            outcomes <- (prefix, outcome) :: outcomes
            updatedVocabs <- Map.add prefix updatedEntry updatedVocabs

        let report = { Outcomes = List.rev outcomes }
        let updatedLf = { lf with Vocabularies = updatedVocabs }
        return report, updatedLf
    }
