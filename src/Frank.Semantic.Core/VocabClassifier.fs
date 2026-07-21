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
    // Public (not private): callers that classify many URIs against a fixed set of
    // candidates can precompute normalized authorities once instead of paying
    // isOwnedByAuthority's re-normalization cost on every comparison (see EmitterShared.declaredOnlyBases).
    let normalizeAuthority (uriStr: string) : string option =
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

    // ── Ownership derived from the app's own produced artifact (#419) ─────────

    // A deployed app may bind a different domain than any dev-time/CLI-supplied base URI,
    // so no externally supplied URL can be a build-time static-analysis fact (#419 rework —
    // see EmitterShared.declaredOnlyBases, the same "derive ownership from the app's own
    // resolved resource identity IRIs" pattern already used by the emitters for #396).
    // This resolves CURIEs directly against the lock's own prefix map rather than reusing
    // Frank.Semantic's VocabularyRegistry.tryResolveIri: Frank.Semantic.Core has no
    // dotNetRdf/FCS dependency by design (Frank.Analyzers depends on Core alone — see
    // Frank.Analyzers.fsproj — so the analyzer's real build/editor channel can compute
    // ownership with zero manual flags, config, or FCS evaluation).
    let private expandCurie (prefixes: Map<string, Uri>) (curie: string) : Uri option =
        match curie.IndexOf(':') with
        | -1 -> None
        | idx ->
            let prefix = curie.[.. idx - 1]
            let local = curie.[idx + 1 ..]

            Map.tryFind prefix prefixes
            |> Option.bind (fun baseUri ->
                match Uri.TryCreate(baseUri.AbsoluteUri + local, UriKind.Absolute) with
                | true, u -> Some u
                | false, _ -> None)

    // Every CURIE that identifies one of the app's own resources: the type IRI of every
    // non-Excluded Mapping, every non-Excluded field's IRI, and every Confirmed union
    // case's IRI. Mirrors ResolvedModel.build's inclusion rules (buildResource/buildField/
    // buildCase) so the derived ownership set matches what the app actually emits.
    let private ownResourceIdentityCuries (lock: LockFile) : string list =
        let included = lock.Mappings |> List.filter (fun m -> m.Status <> Excluded)

        let typeCuries = included |> List.choose (fun m -> m.Iri)

        let fieldCuries =
            included
            |> List.collect (fun m ->
                MappingShape.activePayloadFields m.Shape
                |> List.filter (fun f -> f.Status <> Excluded)
                |> List.choose (fun f -> f.Iri))

        let caseCuries =
            included
            |> List.collect (fun m ->
                MappingShape.caseMappings m.Shape
                |> List.filter (fun c -> c.Status = MappingStatus.Confirmed)
                |> List.choose (fun c -> c.Iri))

        typeCuries @ fieldCuries @ caseCuries

    /// The set of normalized authorities the app's own lock demonstrably owns, derived
    /// solely from the produced artifact (lock.Mappings) — never from an externally
    /// supplied base URI. See EmitterShared.declaredOnlyBases for the equivalent
    /// ResolvedModel-based computation used by the emitters (#396); this variant works
    /// directly off LockFile so it is available in Frank.Semantic.Core's FCS/dotNetRdf-free
    /// load closure (#419).
    let ownedIdentityAuthorities (lock: LockFile) : Set<string> =
        let prefixes = buildPrefixMap lock.Vocabularies lock.DeclaredPrefixes

        ownResourceIdentityCuries lock
        |> List.choose (expandCurie prefixes)
        |> List.choose (fun u -> normalizeAuthority u.AbsoluteUri)
        |> Set.ofList

    // True iff `prefix` is declared in `lock.DeclaredPrefixes` and its IRI's authority
    // matches one of the app's own resolved resource-identity authorities (#419).
    let private isOwnedDeclaredPrefix (ownAuthorities: Set<string>) (lock: LockFile) (prefix: string) : bool =
        match Map.tryFind prefix lock.DeclaredPrefixes with
        | None -> false
        | Some iri ->
            match normalizeAuthority iri with
            | None -> false
            | Some authority -> Set.contains authority ownAuthorities

    /// Classify each referenced namespace prefix against a pre-built URI index.
    /// Prefer when byUri is already built at the call site to avoid redundant Map construction.
    /// #419: when the prefix has no `lock.Vocabularies` entry (never fetched), distinguishes
    /// an app-owned declared prefix (LocallyServedUnconfirmed, backed by the app's own
    /// resolved resource identity — see ownedIdentityAuthorities) from a genuinely external,
    /// uncached one (Undereferenceable). No base URI, config, or flag is ever consulted.
    /// Pure, deterministic, offline-safe. `now` is injected — never reads the system clock.
    let classifyReferencedVocabWith
        (lock: LockFile)
        (byUri: Map<string, VocabularyEntry>)
        (now: DateTimeOffset)
        (referencedNs: string list)
        : VocabState list =
        let policy = SlaPolicy.defaultPolicy
        let ownAuthorities = ownedIdentityAuthorities lock

        referencedNs
        |> List.map (fun prefix ->
            match lookupEntry lock byUri prefix with
            | Some entry -> classifyEntry policy prefix entry now
            | None ->
                if isOwnedDeclaredPrefix ownAuthorities lock prefix then
                    LocallyServedUnconfirmed
                else
                    Undereferenceable)

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

    /// Canonical string form of a VocabState, shared across all surfaces (accept, status, JSON).
    let vocabStateToString (state: VocabState) : string =
        match state with
        | Confirmed -> "Confirmed"
        | Proposed -> "Proposed"
        | Undereferenceable -> "Undereferenceable"
        | LocallyServedUnconfirmed -> "LocallyServedUnconfirmed"
        | Stale -> "Stale"
