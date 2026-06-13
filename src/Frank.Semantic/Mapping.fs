namespace Frank.Semantic

/// Input type: FCS-extracted field metadata. Populated by Frank.Cli.Core (B6).
/// Defined here so Frank.Semantic (convention engine) and Frank.Cli.Core share one definition.
type FieldInfo =
    { Name: string
      TypeName: string
      Attributes: Map<string, string>
      DocComment: string option }

/// Input type: FCS-extracted type metadata. Populated by Frank.Cli.Core (B6).
/// Defined here so Frank.Semantic (convention engine) and Frank.Cli.Core share one definition.
type TypeInfo =
    { FullName: string
      Namespace: string
      LocalName: string
      Fields: FieldInfo list
      Attributes: Map<string, string>
      DocComment: string option }

/// How a mapping was resolved. B5 serializes these as "convention" | "llm" | "manual".
type MappingSource =
    | Convention
    | Llm
    | Manual

/// Confidence threshold result. B5 serializes these as "confirmed" | "proposed" | "unresolved".
type MappingStatus =
    | Confirmed
    | Proposed
    | Unresolved

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
