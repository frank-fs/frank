namespace Frank.Semantic

open System
open System.Text.Json.Nodes

// ── Lock file types ───────────────────────────────────────────────────────────

module LockFile =

    // Invariant: v1 entries read with IsValidated=false so legacy locks are
    // never silently laundered into "validated" state (A-C6, A-C11).
    type ValidationStatus =
        { IsValidated: bool
          Reason: string option
          LastChecked: DateTimeOffset option }

    type VocabularyEntry =
        {
            Uri: string
            FetchedAt: DateTimeOffset
            Hash: string
            // Schema-v2 evidence fields (absent in v1 JSON; safe defaults applied on read)
            MediaType: string option
            Validated: ValidationStatus
            /// Populated by `frank semantic validate` (V2/V3); consumed by the #378 analyzer for
            /// term-level dereferenceability. None = not yet fetched or parsed (unknown);
            /// Some Set.empty = vocabulary parsed but asserts no terms (suppresses Undereferenceable check).
            /// The V1 classifier does not read this field — Terms are captured in the lock for #378 to consume.
            Terms: Set<string> option
            HttpStatus: int option
            Owned: bool
            ETag: string option
            LastModified: string option
        }

    // Default for v1 backward-compat and test construction.
    // IsValidated=false with explicit reason; never trusted as validated.
    val v1Empty: VocabularyEntry

    type LockFile =
        { SchemaVersion: int
          Generated: DateTimeOffset
          Integrity: string option
          Vocabularies: Map<string, VocabularyEntry>
          DeclaredPrefixes: Map<string, string>
          Mappings: Mapping list }

    val mappingSourceToString: s: MappingSource -> string

    val mappingSourceFromString: s: string -> Result<MappingSource, string>

    val mappingStatusToString: s: MappingStatus -> string

    val mappingStatusFromString: s: string -> Result<MappingStatus, string>

    val isDecided: status: MappingStatus -> bool

    // ── JSON deserialization helpers (pure) ───────────────────────────────────
    // Shared with Frank.Cli.Core.Accept (parsing resolved.json against the same JSON conventions).

    val parseIso8601: s: string -> Result<DateTimeOffset, string>

    val requireString: node: JsonNode -> key: string -> Result<string, string>

    val optionalString: node: JsonNode -> key: string -> string option

    val requireFloat: node: JsonNode -> key: string -> Result<float, string>

    // ── Integrity ─────────────────────────────────────────────────────────────

    /// Compute the SHA-256 integrity hash of a lock file's canonical form.
    /// Invariant to the lock's Integrity field value — always hashes with Integrity = None.
    val computeIntegrity: lf: LockFile -> string

    /// Return a new lock with Integrity stamped to the computed hash.
    val withIntegrity: lf: LockFile -> LockFile

    /// Verify the stored Integrity against the recomputed hash.
    /// None → Error "lock is unstamped; regenerate"
    /// Mismatch → Error "lock appears hand-edited; regenerate"
    val verifyIntegrity: lf: LockFile -> Result<unit, string>

    /// Verify integrity only if the lock carries a stamp, or if the lock is schema v2 or later.
    /// M3: v2 locks without a stamp are rejected — a hand-authored v2 with validated=true and no
    /// integrity field would otherwise launder as trusted. Only v1 (legacy) may be unstamped.
    val verifyIfStamped: lf: LockFile -> Result<unit, string>

    // ── Effectful I/O ─────────────────────────────────────────────────────────

    /// Read and validate a lock file from disk.
    /// Returns Error with message on version mismatch, missing fields, or malformed JSON.
    val read: path: string -> Result<LockFile, string>

    /// Write a lock file to disk with deterministic serialization.
    /// v2 vocabulary entries include all evidence fields; v1 entries include only uri/fetchedAt/hash.
    /// Vocabularies keys are sorted alphabetically. Mappings preserve given order.
    val write: path: string -> lf: LockFile -> unit

    // ── Status counts ─────────────────────────────────────────────────────────

    type StatusCounts =
        { Confirmed: int
          Proposed: int
          Unresolved: int
          Excluded: int }

    type PackageGroup =
        { Namespace: string
          Counts: StatusCounts
          Vocabs: (string * int) list }

    val countByStatus: mappings: Mapping list -> StatusCounts

    // ── Package grouping ─────────────────────────────────────────────────────

    /// Derive the F# namespace from a fully-qualified type name.
    /// "A.B.C" → "A.B"; "A" → "(global)".
    val namespaceOf: fsharpType: string -> string

    /// Group mappings by derived namespace and aggregate status counts and vocab usage.
    /// Groups are sorted by namespace; vocabs within each group are sorted by key.
    val countByPackage: knownPrefixes: Set<string> -> mappings: Mapping list -> PackageGroup list

    // ── Prefix utilities ─────────────────────────────────────────────────────

    /// Build the combined prefix map from vocabularies and declared prefixes.
    /// Declared prefixes take precedence over vocabulary entries on key conflict.
    /// L4: malformed URIs are silently excluded (a persisted bad URI is a modeled error, not a crash).
    val buildPrefixMap:
        vocabularies: Map<string, VocabularyEntry> -> declaredPrefixes: Map<string, string> -> Map<string, Uri>

    // ── Pure merge ────────────────────────────────────────────────────────────

    /// Merge resolved mappings into an existing lock file.
    /// Matching is by FSharpType. Unmatched existing entries are kept.
    /// New resolved entries (not in existing) are appended.
    /// Pure: returns a new LockFile, leaves lf unchanged.
    val merge: lf: LockFile -> resolved: Mapping list -> LockFile
