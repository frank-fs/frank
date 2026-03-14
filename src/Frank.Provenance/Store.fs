namespace Frank.Provenance

open System
open System.Threading.Tasks

/// Interface for storing and querying provenance records.
type IProvenanceStore =
    inherit IDisposable

    /// Appends a provenance record to the store (fire-and-forget).
    abstract Append: record: ProvenanceRecord -> unit

    /// Queries records by resource URI.
    abstract QueryByResource: resourceUri: string -> Task<ProvenanceRecord list>

    /// Queries records by agent ID.
    abstract QueryByAgent: agentId: string -> Task<ProvenanceRecord list>

    /// Queries records within a time range.
    abstract QueryByTimeRange: start: DateTimeOffset * end_: DateTimeOffset -> Task<ProvenanceRecord list>
