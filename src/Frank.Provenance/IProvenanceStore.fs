namespace Frank.Provenance

type IProvenanceStore =
    abstract Append: ProvenanceRecord -> unit
    abstract QueryByResource: string -> ProvenanceRecord list
    abstract QueryByAgent: string -> ProvenanceRecord list
