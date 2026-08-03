namespace Frank.Provenance

/// The PROV-O "starting-point" classes this package uses. Data-free cases -- [<Struct>]
/// is a clear win here (no heap allocation, no field-reservation cost, since no case carries data).
[<Struct; RequireQualifiedAccess>]
type ProvClass =
    | Activity
    | Entity
    | Agent

module ProvClass =
    /// The absolute PROV-O IRI for a class, e.g. "http://www.w3.org/ns/prov#Activity".
    val toIri: c: ProvClass -> string

/// The PROV-O relations this package uses. Data-free cases, same [<Struct>] reasoning as ProvClass.
[<Struct; RequireQualifiedAccess>]
type ProvRelation =
    | WasGeneratedBy
    | WasAssociatedWith
    | Used
    | StartedAtTime
    | EndedAtTime
    | WasDerivedFrom
    | SpecializationOf

module ProvRelation =
    /// The absolute PROV-O IRI for a relation, e.g. "http://www.w3.org/ns/prov#wasGeneratedBy".
    val toIri: r: ProvRelation -> string
