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

/// Resolved mapping for one union case. Payload is [] for a nullary case;
/// for a payload-carrying case it holds the case's field mappings.
type CaseMapping =
    { Name: string
      Iri: string option
      Confidence: float
      Source: MappingSource
      Status: MappingStatus
      Payload: FieldMapping list }

/// A mapped type is a product (record fields) or a sum (union cases).
type MappingShape =
    | Record of FieldMapping list
    | Union of CaseMapping list

/// Candidate mapping produced by the convention engine for one TypeInfo.
/// B5 serializes this shape into the lock file.
type Mapping =
    { FSharpType: string
      Iri: string option
      Confidence: float
      Source: MappingSource
      Status: MappingStatus
      Alternates: string list
      Shape: MappingShape }

[<RequireQualifiedAccess>]
module MappingShape =

    /// All leaf field mappings in a shape (record fields, or every case's payload).
    let payloadFields (shape: MappingShape) : FieldMapping list =
        match shape with
        | Record fs -> fs
        | Union cases -> cases |> List.collect (fun c -> c.Payload)

    let caseMappings (shape: MappingShape) : CaseMapping list =
        match shape with
        | Record _ -> []
        | Union cases -> cases

    /// Payload/record fields contributed by non-excluded cases (Union) or all
    /// record fields (Record). Does NOT apply a leaf status filter — callers
    /// apply their own (resolution keeps non-Excluded; the gate counts undecided).
    let activePayloadFields (shape: MappingShape) : FieldMapping list =
        match shape with
        | Record fs -> fs
        | Union cases ->
            cases
            |> List.filter (fun c -> c.Status <> Excluded)
            |> List.collect (fun c -> c.Payload)

    /// Map every field mapping (record field or case payload field) through f,
    /// preserving the tree.
    let mapFields (f: FieldMapping -> FieldMapping) (shape: MappingShape) : MappingShape =
        match shape with
        | Record fs -> Record(List.map f fs)
        | Union cases ->
            Union(
                cases
                |> List.map (fun c ->
                    { c with
                        Payload = List.map f c.Payload })
            )
