namespace Frank.Benchmarks

open System
open BenchmarkDotNet.Attributes
open Frank.Rdf
open Frank.Provenance

/// Scratch benchmark for issue #504 (ProvBuilder inline). Exercises the real per-request call
/// site found by `git grep` for ProvBuilder usage: sample/Frank.Provenance.Sample/Program.fs's
/// `catalogLineage`, called from `getCatalogLineage` on every GET /provenance/lineage request.
/// That call site only exercises `entity`/`wasDerivedFrom`; this benchmark widens coverage to
/// every inlined member (`WasGeneratedBy`, `WasAssociatedWith`, `Used`, `StartedAtTime`,
/// `EndedAtTime`, `WasDerivedFrom`, `SpecializationOf`) via one `activity { }` block and one
/// `entity { }` block per call, matching ProvBuilderTests.fs's usage shapes. `Baseline` here means
/// "this worktree's current ProvBuilder.fs/.fsi" -- run once before the `inline` edit and once
/// after, on the same machine, comparing BenchmarkDotNet's Mean/StdDev columns across the two
/// runs (not a single-process A/B, since `inline` is a source-level attribute on one type, not
/// two coexisting implementations).
[<MemoryDiagnoser>]
type ProvBuilderBenchmarks() =

    let activityId = Node.Iri "https://example.org/activities/1"
    let agentId = Node.Iri "https://example.org/agents/1"
    let entityId1 = Node.Iri "https://example.org/entities/1"
    let entityId2 = Node.Iri "https://example.org/entities/2"
    let t0 = DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero)
    let t1 = DateTimeOffset(2026, 8, 9, 1, 0, 0, TimeSpan.Zero)

    [<Benchmark>]
    member _.ActivityBlock() =
        activity activityId {
            wasAssociatedWith agentId
            used entityId1
            startedAtTime t0
            endedAtTime t1
        }

    [<Benchmark>]
    member _.EntityBlock() =
        entity entityId2 {
            wasGeneratedBy activityId
            wasDerivedFrom entityId1
            specializationOf entityId1
        }
