namespace Frank.Provenance

open System.Threading.Tasks

type IProvenanceStore =
    abstract Append: ProvenanceRecord -> unit
    abstract QueryByResource: string -> Task<ProvenanceRecord list>
    abstract QueryByAgent: string -> Task<ProvenanceRecord list>
    abstract QueryByActivityId: string -> Task<ProvenanceRecord option>
