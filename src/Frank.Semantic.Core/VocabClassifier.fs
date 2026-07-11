namespace Frank.Semantic

open System

// ── Vocabulary state DU ───────────────────────────────────────────────────────

/// Classification state for a referenced vocabulary namespace.
type VocabState =
    | Confirmed // In lock, validated reachable
    | Proposed // In lock but not yet validated
    | Undereferenceable // Not in lock and not locally served
    | LocallyServedUnconfirmed // In declaredPrefixes but not fetched
    | Stale // In lock but exceeds SLA age threshold

// ── SLA policy ────────────────────────────────────────────────────────────────

/// Policy controlling staleness thresholds.
/// unownedMaxAgeDays: max age in days before an unowned vocab is stale (default 30).
/// ownedReachabilityDays: max age before an owned vocab's reachability is re-checked (default 90).
/// perVocabOverrides: prefix → max age days; overrides both owned and unowned defaults.
type SlaPolicy =
    { UnownedMaxAgeDays: int
      OwnedReachabilityDays: int
      PerVocabOverrides: Map<string, int> }

module SlaPolicy =
    let defaultPolicy: SlaPolicy =
        { UnownedMaxAgeDays = 30
          OwnedReachabilityDays = 90
          PerVocabOverrides = Map.empty }

// ── Pure classifier ───────────────────────────────────────────────────────────

module VocabClassifier =

    open LockFile

    // ── Authority normalization ───────────────────────────────────────────────

    // Normalize a URI's authority to a canonical form for ownership comparison.
    // Invariants: lowercased scheme+host, default port dropped, www. prefix stripped,
    // http and https treated as equivalent (content-based authority, not transport).
    let private normalizeAuthority (uriStr: string) : string option =
        match Uri.TryCreate(uriStr, UriKind.Absolute) with
        | false, _ -> None
        | true, u ->
            // Scheme is dropped entirely; http↔https are equivalent for authority identity.
            // www. prefix stripped so www.example.org and example.org compare equal.
            let lowHost = u.Host.ToLowerInvariant()

            let host =
                if lowHost.StartsWith("www.") then
                    lowHost.[4..]
                else
                    lowHost

            let port = if u.IsDefaultPort then "" else $":{u.Port}"

            Some $"{host}{port}"

    /// True iff vocabUri is owned by appBaseUri.
    /// Owned = same authority (scheme+host normalized: http↔https, www.↔apex).
    /// Explicit Owned field in VocabularyEntry is the recorded fact;
    /// this function derives the fact from URIs.
    let isOwnedByAuthority (appBaseUri: string) (vocabUri: string) : bool =
        match normalizeAuthority appBaseUri, normalizeAuthority vocabUri with
        | Some a, Some b -> a = b
        | _ -> false

    // ── Staleness ─────────────────────────────────────────────────────────────

    /// True iff the entry's age (since FetchedAt) exceeds the policy threshold.
    /// A future-stamped FetchedAt (now < FetchedAt) yields negative elapsed days and is treated as not-stale;
    /// staleness only ever flags entries older than the SLA.
    /// `now` is injected — never reads DateTimeOffset.UtcNow internally.
    let isStale (policy: SlaPolicy) (prefix: string) (entry: VocabularyEntry) (now: DateTimeOffset) : bool =
        let maxDays =
            match Map.tryFind prefix policy.PerVocabOverrides with
            | Some d -> d
            | None ->
                if entry.Owned then
                    policy.OwnedReachabilityDays
                else
                    policy.UnownedMaxAgeDays

        (now - entry.FetchedAt).TotalDays > float maxDays

    // ── Shared classifier ─────────────────────────────────────────────────────

    // Build a URI → entry lookup for IRI-identity matching (AT7 / #378).
    // Linear over Vocabularies (lock is small; O(n) acceptable).
    let buildVocabUriIndex (vocabularies: Map<string, VocabularyEntry>) : Map<string, VocabularyEntry> =
        vocabularies |> Map.toSeq |> Seq.map (fun (_, e) -> e.Uri, e) |> Map.ofSeq

    /// IRI-first prefix → entry lookup (IRI-identity / AT7):
    /// Resolve prefix → IRI via DeclaredPrefixes, then match by IRI in byUri.
    /// Identity is always the namespace IRI, never the prefix label (spec: compare on IRI).
    let lookupEntry (lock: LockFile) (byUri: Map<string, VocabularyEntry>) (prefix: string) : VocabularyEntry option =
        match Map.tryFind prefix lock.DeclaredPrefixes with
        | None -> None
        | Some iri -> Map.tryFind iri byUri

    let private classifyEntry
        (policy: SlaPolicy)
        (prefix: string)
        (entry: VocabularyEntry)
        (now: DateTimeOffset)
        : VocabState =
        if isStale policy prefix entry now then
            Stale
        elif entry.Owned && not entry.Validated.IsValidated then
            LocallyServedUnconfirmed
        elif entry.Validated.IsValidated then
            Confirmed
        else
            Proposed

    /// Classify each referenced namespace prefix against a pre-built URI index.
    /// Prefer when byUri is already built at the call site to avoid redundant Map construction.
    /// Pure, deterministic, offline-safe. `now` is injected — never reads the system clock.
    let classifyReferencedVocabWith
        (lock: LockFile)
        (byUri: Map<string, VocabularyEntry>)
        (now: DateTimeOffset)
        (referencedNs: string list)
        : VocabState list =
        let policy = SlaPolicy.defaultPolicy

        referencedNs
        |> List.map (fun prefix ->
            match lookupEntry lock byUri prefix with
            | Some entry -> classifyEntry policy prefix entry now
            | None -> Undereferenceable)

    /// Classify each referenced namespace prefix against the lock using the default SLA policy.
    /// Pure, deterministic, offline-safe. `now` is injected — never reads the system clock.
    /// This is the SINGLE classifier all surfaces (status, analyzer, CI) project from.
    ///
    /// Lookup strategy (IRI-identity / AT7):
    ///  Resolve prefix → IRI via DeclaredPrefixes[prefix], then match by IRI in byUri.
    ///  Identity is always the namespace IRI, never the prefix label — handles the case
    ///  where the same namespace IRI is stored under a different prefix key (e.g. sdo/schema).
    ///
    /// H2 boundary: classification is NAMESPACE-LEVEL only (does the namespace dereference to
    /// parseable RDF?). Term-level membership (is the referenced term ∈ entry.Terms?) is enforced
    /// by the #378 analyzer which consumes entry.Terms. Do NOT add term-membership checking here.
    let classifyReferencedVocab (lock: LockFile) (now: DateTimeOffset) (referencedNs: string list) : VocabState list =
        classifyReferencedVocabWith lock (buildVocabUriIndex lock.Vocabularies) now referencedNs
