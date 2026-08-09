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
    /// The single most-recently-ended activity that prov:wasGeneratedBy the given resource, plus that
    /// activity's own triples (including any domain ActivityType) -- everything ByResource returns for
    /// this resource, narrowed to its latest generating activity when there have been several over the
    /// resource's lifetime. An empty graph if the resource was never recorded.
    | Latest of resourceIri: string

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

/// Bounds an in-memory store. Eviction is clamped defensively at the store: regardless of the values
/// configured here (including pathological ones, e.g. MaxRecords <= 0 or EvictionBatchSize >=
/// MaxRecords), the store never evicts the record most recently appended.
[<Struct>]
type ProvenanceStoreConfig =
    { /// The number of records to retain before the store starts evicting the oldest ones. A value
      /// <= 0 does not stop the store from accepting appends -- it just means eviction kicks in on
      /// (almost) every append, subject to the "never evict the newest record" clamp below.
      MaxRecords: int
      /// The number of oldest records to evict at once, once MaxRecords is exceeded. Clamped so it can
      /// never evict the record just appended, even when configured >= MaxRecords.
      EvictionBatchSize: int
      /// Number of Append calls between snapshots, when a journal is attached (see
      /// MailboxProcessorProvenanceStore). Ignored entirely when no journal is present. Values <= 0
      /// are clamped to 1 (snapshot on every Append) rather than raising or dividing by zero.
      SnapshotEvery: int }

module ProvenanceStoreConfig =
    val defaults: ProvenanceStoreConfig

/// Translates a ProvenanceQuery into a pre-built, parameterized SparqlQuery. Internal: SPARQL is the
/// implementation mechanism, never part of the public surface.
[<AutoOpen>]
module ProvenanceStore =
    val internal toSparqlQuery: query: ProvenanceQuery -> SparqlQuery
