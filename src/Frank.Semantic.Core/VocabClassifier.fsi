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
    val defaultPolicy: SlaPolicy

// ── Pure classifier ───────────────────────────────────────────────────────────

module VocabClassifier =

    open LockFile

    /// Normalize a URI's authority to a canonical form for ownership comparison
    /// (lowercased scheme+host, default port dropped, www. stripped, http/https equivalent).
    /// Exposed so callers classifying many URIs against a fixed candidate set can
    /// precompute normalized authorities once (see Frank.Cli.Core.EmitterShared.declaredOnlyBases).
    val normalizeAuthority: uriStr: string -> string option

    /// True iff vocabUri is owned by appBaseUri.
    /// Owned = same authority (scheme+host normalized: http↔https, www.↔apex).
    /// Explicit Owned field in VocabularyEntry is the recorded fact;
    /// this function derives the fact from URIs.
    val isOwnedByAuthority: appBaseUri: string -> vocabUri: string -> bool

    /// True iff uriStr's normalized authority is a member of a precomputed authority set.
    /// Shared primitive for callers that test many candidate URIs against a fixed owned-authority
    /// set (see Frank.Cli.Core.EmitterShared.declaredOnlyBases).
    val authorityInSet: authorities: Set<string> -> uriStr: string -> bool

    /// True iff the entry's age (since FetchedAt) exceeds the policy threshold.
    /// A future-stamped FetchedAt (now < FetchedAt) yields negative elapsed days and is treated as not-stale;
    /// staleness only ever flags entries older than the SLA.
    /// `now` is injected — never reads DateTimeOffset.UtcNow internally.
    val isStale: policy: SlaPolicy -> prefix: string -> entry: VocabularyEntry -> now: DateTimeOffset -> bool

    // Build a URI → entry lookup for IRI-identity matching (AT7 / #378).
    // Linear over Vocabularies (lock is small; O(n) acceptable).
    val buildVocabUriIndex: vocabularies: Map<string, VocabularyEntry> -> Map<string, VocabularyEntry>

    /// IRI-first prefix → entry lookup (IRI-identity / AT7):
    /// Resolve prefix → IRI via DeclaredPrefixes, then match by IRI in byUri.
    /// Identity is always the namespace IRI, never the prefix label (spec: compare on IRI).
    val lookupEntry: lock: LockFile -> byUri: Map<string, VocabularyEntry> -> prefix: string -> VocabularyEntry option

    /// Classify each referenced namespace prefix against a pre-built URI index.
    /// Prefer when byUri is already built at the call site to avoid redundant Map construction.
    /// Pure, deterministic, offline-safe. `now` is injected — never reads the system clock.
    val classifyReferencedVocabWith:
        lock: LockFile ->
        byUri: Map<string, VocabularyEntry> ->
        now: DateTimeOffset ->
        referencedNs: string list ->
            VocabState list

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
    val classifyReferencedVocab: lock: LockFile -> now: DateTimeOffset -> referencedNs: string list -> VocabState list

    /// Canonical string form of a VocabState, shared across all surfaces (accept, status, JSON).
    val vocabStateToString: state: VocabState -> string
