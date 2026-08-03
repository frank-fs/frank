namespace Frank.Provenance

open System
open Frank.Rdf

/// A single PROV-O record: an Activity, the Resource (Entity) it acted on, the Agent responsible,
/// when it ran, an optional domain type for the Activity, and any additional properties.
type ProvenanceRecord =
    { Activity: Node
      Resource: Node
      Agent: Node
      StartedAt: DateTimeOffset
      EndedAt: DateTimeOffset
      ActivityType: Uri option
      Properties: (string * Value) list }

module ProvenanceRecord =
    /// Projects a ProvenanceRecord into a Doc: Activity typed prov:Activity (plus ActivityType, if
    /// Some, as an additional rdf:type), Resource typed prov:Entity and connected via
    /// prov:wasGeneratedBy, Agent typed prov:Agent and connected via prov:wasAssociatedWith,
    /// StartedAt/EndedAt on the Activity, Properties attached to the Activity as-is.
    val toDoc: record: ProvenanceRecord -> Doc
