module Frank.Cli.Core.Accept

open Frank.Semantic

type ResolvedField = { Name: string; Iri: string option }

type ResolvedCase =
    { Name: string
      Iri: string option
      Payload: ResolvedField list }

[<RequireQualifiedAccess>]
type ResolvedShape =
    | Record of ResolvedField list
    | Union of ResolvedCase list

type ResolvedEntry =
    { FSharpType: string
      Iri: string option
      Status: MappingStatus option
      Shape: ResolvedShape }

type ResolvedDoc =
    { SchemaVersion: int
      Resolved: ResolvedEntry list }

type RejectedEntry = { FSharpType: string; Reason: string }

/// Location context for a vocab warning: the type (simple name) and optional field that references the namespace.
/// None at the record level means no mapping reference was found (status path).
type VocabWarningLocation = { Type: string; Field: string option }

/// Warning emitted when a referenced vocabulary namespace is Undereferenceable.
/// State is typed as VocabState and stringified only at the JSON boundary.
/// Location is None when no mapping reference to this namespace was found (status scan path).
type VocabWarning =
    { Prefix: string
      State: VocabState
      Iri: string
      Location: VocabWarningLocation option
      Hint: string }

type AcceptSummary =
    { Merged: int
      Excluded: int
      Rejected: RejectedEntry list
      Unchanged: int
      AlreadyConfirmed: int
      FieldsUnresolved: int
      Warnings: VocabWarning list }

val parseResolved: json: string -> Result<ResolvedDoc, string>

/// Term existence oracle built from cached vocabulary graphs.
/// Classes/Properties/Individuals hold absolute IRI strings per category.
/// CoveredBases = base URI strings (e.g. "https://schema.org/") whose cache was loaded.
/// An empty oracle (all Set.empty, CoveredBases=[]) disables existence checking (back-compat).
type TermOracle =
    { Classes: Set<string>
      Properties: Set<string>
      Individuals: Set<string>
      CoveredBases: string list }

val apply:
    lf: LockFile.LockFile ->
    doc: ResolvedDoc ->
    source: MappingSource ->
    oracle: TermOracle ->
        LockFile.LockFile * AcceptSummary

val internal prefixOfCurie: iri: string -> string option

/// Format the "host-it" hint for a vocabulary namespace IRI.
/// Strips a trailing '#' so the hint names the dereferenceable document, not the fragment root.
val vocabWarningHint: iri: string -> string

/// Build a TermOracle from cached vocabulary graphs in cacheDir.
/// Vocabs with no cache file contribute nothing (offline / un-fetched).
/// The resulting oracle only enforces existence for namespaces whose cache loaded.
val buildOracle: vocabs: Map<string, LockFile.VocabularyEntry> -> cacheDir: string -> TermOracle

/// Serialize a list of VocabWarnings as a JSON array string.
/// Used by the status --format json path (standalone array) and accept --format json (embedded in summary).
val vocabWarningsToJson: warnings: VocabWarning list -> string

/// Serialize one ConventionDiagnostic as a single-line JSON object string, escaping
/// string fields properly (they can contain quotes/backslashes/backticks) — never
/// hand-built via printfn string interpolation.
val conventionDiagnosticToJson: d: ConventionDiagnostic -> string

val summaryToJson: s: AcceptSummary -> string
