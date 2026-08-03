namespace Frank.Provenance

open VDS.RDF
open VDS.RDF.Query

/// The closed, public vocabulary of query shapes this package recognizes as provenance-meaningful.
/// This is the ONLY way a caller queries a store -- there is no public API accepting a raw SparqlQuery
/// or query string. Adding a new provenance-meaningful query shape means adding a case here, not
/// widening the surface to open query text.
[<RequireQualifiedAccess>]
type ProvenanceQuery =
    | ByResource of resourceIri: string
    | ByAgent of agentIri: string
    | ByActivityId of activityIri: string

/// SPARQL SELECT/ASK return bindings; CONSTRUCT/DESCRIBE return a graph. A store's Query can produce
/// either, depending on the underlying SparqlQuery shape.
[<RequireQualifiedAccess>]
type SparqlQueryResult =
    | Bindings of SparqlResultSet
    | Graph of IGraph

/// A provenance store: append records, query them via the closed ProvenanceQuery vocabulary.
type IProvenanceStore =
    abstract Append: record: ProvenanceRecord -> unit
    abstract Query: query: ProvenanceQuery -> SparqlQueryResult

/// Bounds an in-memory store.
type ProvenanceStoreConfig =
    { MaxRecords: int
      EvictionBatchSize: int }

module ProvenanceStoreConfig =
    val defaults: ProvenanceStoreConfig

/// Translates a ProvenanceQuery into a pre-built, parameterized SparqlQuery. Internal: SPARQL is the
/// implementation mechanism, never part of the public surface.
[<AutoOpen>]
module ProvenanceStore =
    val internal toSparqlQuery: query: ProvenanceQuery -> SparqlQuery
