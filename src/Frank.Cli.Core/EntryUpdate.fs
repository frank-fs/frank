module Frank.Cli.Core.EntryUpdate

open System
open Frank.Semantic
open Frank.Semantic.LockFile

/// Update a lock entry with full fetched evidence (hash, terms, media-type, status, cache fields).
let updatedEntry (now: DateTimeOffset) (ev: FetchEvidence) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        FetchedAt = now
        Hash = ev.Hash
        MediaType = ev.MediaType
        Validated = ev.Validated
        Terms = ev.Terms
        HttpStatus = ev.HttpStatus
        ETag = ev.ETag
        LastModified = ev.LastModified }

/// Mark an entry as durable-gone (404/410/LyingIri/Undereferenceable).
/// Stamps HttpStatus, sets Validated=false with reason, updates LastChecked.
let goneEntry (now: DateTimeOffset) (reason: string) (status: int) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        HttpStatus = Some status
        Validated =
            { IsValidated = false
              Reason = Some reason
              LastChecked = Some now } }

/// Mark an entry as transiently failed (5xx/network).
/// Preserves IsValidated; updates Reason and LastChecked only.
let probeFailedEntry (now: DateTimeOffset) (reason: string) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        Validated =
            { entry.Validated with
                Reason = Some reason
                LastChecked = Some now } }

/// Mark an entry as non-durable unverifiable (text/html or application/xhtml+xml — possibly RDFa).
/// Sets Validated=false without touching HttpStatus (not a durable-gone marker).
let unverifiableEntry (now: DateTimeOffset) (reason: string) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        Validated =
            { IsValidated = false
              Reason = Some reason
              LastChecked = Some now } }

/// Update a lock entry's LastChecked clock without changing hash or validation state.
let unchangedEntry (now: DateTimeOffset) (entry: VocabularyEntry) : VocabularyEntry =
    { entry with
        FetchedAt = now
        Validated =
            { entry.Validated with
                LastChecked = Some now } }

/// Map a list of (prefix, outcome) pairs to the CLI exit code.
/// 2 if any isDurable; 1 if any isTransient (no durable); 0 otherwise.
let outcomesExitCode (isDurable: 'o -> bool) (isTransient: 'o -> bool) (outcomes: (string * 'o) list) : int =
    let hasDurable = outcomes |> List.exists (snd >> isDurable)
    let hasTransient = outcomes |> List.exists (snd >> isTransient)

    if hasDurable then 2
    elif hasTransient then 1
    else 0
