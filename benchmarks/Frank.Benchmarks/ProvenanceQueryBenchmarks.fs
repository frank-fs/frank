namespace Frank.Benchmarks

open System
open BenchmarkDotNet.Attributes
open Microsoft.Extensions.Logging.Abstractions
open Frank.Rdf
open Frank.Provenance

/// #500: SparqlQueryResult is on a genuine per-request hot path -- e.g.
/// sample/Frank.Provenance.Sample/Program.fs's `getProvenance` handler (GET /provenance) and
/// sample/Frank.Alps.Sample's `stateResolver`/`pingPongStateResolver` (invoked on every ALPS
/// content-negotiated GET) all call `IProvenanceStore.Query` and pattern-match its result. This
/// benchmark calls `MailboxProcessorProvenanceStore.Query` directly -- the exact call site that
/// allocates a `SparqlQueryResult` -- through Frank.Provenance's public API, same as
/// ShaclValidateBenchmarks alongside it. Only `ByResource` is exercised: every real
/// `ProvenanceQuery` case compiles to a CONSTRUCT or DESCRIBE query (see ProvenanceStore.fs's
/// `toSparqlQuery`), so `SparqlQueryResult.Bindings` is structurally unreachable via any real call
/// site -- the same "unreachable in practice" fact the samples document at their own Bindings
/// branches.
[<MemoryDiagnoser>]
type ProvenanceQueryBenchmarks() =

    let store: IProvenanceStore =
        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance) :> IProvenanceStore

    do
        let now = DateTimeOffset.UtcNow

        store.Append(
            { Activity = Node.Iri "https://example.org/activities/bench"
              Resource = Node.Iri "https://example.org/games/bench"
              Agent = Node.Iri "https://example.org/users/bench"
              StartedAt = now
              EndedAt = now.AddSeconds(1.0)
              ActivityType = None
              Properties = [] }
        )

    [<Benchmark(Baseline = true)>]
    member _.ByResource() = store.Query(ProvenanceQuery.ByResource "https://example.org/games/bench")
