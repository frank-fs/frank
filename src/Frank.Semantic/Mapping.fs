namespace Frank.Semantic

/// Input type: FCS-extracted field/payload metadata. Populated by Frank.Cli.Core.
type FieldInfo =
    { Name: string
      TypeName: string
      Attributes: Map<string, string>
      DocComment: string option }

/// One case of a discriminated union. Payload is [] for a nullary case.
type CaseInfo =
    { Name: string
      Payload: FieldInfo list
      Attributes: Map<string, string>
      DocComment: string option }

/// A type is either a product (record) or a sum (union). Preserves the
/// type → cases → payload tree; never flattened into one field list.
type TypeShape =
    | Record of FieldInfo list
    | Union of CaseInfo list

/// Input type: FCS-extracted type metadata. Populated by Frank.Cli.Core.
type TypeInfo =
    { FullName: string
      Namespace: string
      LocalName: string
      Shape: TypeShape
      Attributes: Map<string, string>
      DocComment: string option }

/// How a mapping was resolved. B5 serializes these as "convention" | "llm" | "manual".
type MappingSource =
    | Convention
    | Llm
    | Manual

/// Confidence threshold result. B5 serializes these as "confirmed" | "proposed" | "unresolved" | "excluded".
/// confirmed = asserted equivalence; proposed = suggestion (not asserted); unresolved = no candidate found; excluded = deliberately no external mapping (decided-absent).
type MappingStatus =
    | Confirmed
    | Proposed
    | Unresolved
    | Excluded

/// Resolved mapping for a single field.
type FieldMapping =
    { Name: string
      Iri: string option
      Confidence: float
      Source: MappingSource
      Status: MappingStatus }

/// Candidate mapping produced by the convention engine for one TypeInfo.
/// B5 serializes this shape into the lock file.
type Mapping =
    { FSharpType: string
      Iri: string option
      Confidence: float
      Source: MappingSource
      Status: MappingStatus
      Alternates: string list
      Fields: FieldMapping list }
