module Benchmarks

open System
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Jobs
open Frank.Provenance
open Microsoft.Extensions.Logging.Abstractions

let private sampleRecord: ProvenanceRecord =
    { Id = "https://example.org/activity/01HXYZ"
      ResourceUri = "https://example.org/orders/42"
      HttpMethod = "POST"
      StatusCode = 201
      DomainType = Some(Frank.Semantic.ProvOClass.Activity, Uri "https://schema.org/CreateAction")
      Agent = { Id = "https://example.org/agents/alice"; Label = Some "Alice" }
      StartedAt = DateTimeOffset.UtcNow.AddMilliseconds(-5.0)
      EndedAt = DateTimeOffset.UtcNow }

[<MemoryDiagnoser>]
[<SimpleJob(warmupCount = 1, iterationCount = 3)>]
type ProvenanceBenchmarks() =

    let store =
        new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)

    [<Benchmark>]
    member _.SerializeOneRecord() =
        ProvenanceGraph.toJsonLd sampleRecord |> ignore

    [<Benchmark>]
    member _.AppendAndQuery() =
        (store :> IProvenanceStore).Append(sampleRecord)

        (store :> IProvenanceStore).QueryByResource(sampleRecord.ResourceUri)
        |> Async.AwaitTask
        |> Async.RunSynchronously
        |> ignore
