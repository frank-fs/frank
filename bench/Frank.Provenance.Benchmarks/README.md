# Frank.Provenance.Benchmarks

BenchmarkDotNet harness for the two hot-path surfaces in `Frank.Provenance`.
Not part of `Frank.sln` — run manually.

## How to run

```bash
cd /path/to/frank
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
  dotnet run -c Release \
  --project bench/Frank.Provenance.Benchmarks/Frank.Provenance.Benchmarks.fsproj
```

BenchmarkDotNet spawns child processes; the `[SimpleJob(warmupCount=1, iterationCount=3)]`
attribute keeps total wall time under 30 seconds.
Artifacts (HTML + GitHub-flavoured Markdown) land in
`bench/Frank.Provenance.Benchmarks/BenchmarkDotNet.Artifacts/results/`.

## What is measured

| Benchmark | What it exercises |
|-----------|------------------|
| `SerializeOneRecord` | `ProvenanceGraph.toJsonLd` — full PROV-O RDF graph build + JSON-LD compact serialization for one record |
| `AppendAndQuery` | `MailboxProcessorProvenanceStore.Append` + `QueryByResource` round-trip over the actor mailbox |

## Measured results

Environment: Apple M2 Pro, .NET 10.0.9, Arm64 RyuJIT, macOS 26.5.1.

| Method             | Mean     | Error    | StdDev   | Gen0    | Gen1   | Gen2   | Allocated |
|------------------- |---------:|---------:|---------:|--------:|-------:|-------:|----------:|
| SerializeOneRecord | 85.18 us | 1.524 us | 0.084 us | 31.6162 | 3.5400 |      - | 258.68 KB |
| AppendAndQuery     | 96.12 us | 2.571 us | 0.141 us | 38.8184 | 0.6104 | 0.1221 | 317.15 KB |

PROV-O serialization costs ~85 µs and ~259 KB per record, incurred **only** when a client
content-negotiates `application/ld+json` (the graph is never built on the no-prov path).
Store append + query over the MailboxProcessor costs ~96 µs and ~317 KB per call; the extra
~58 KB over serialization alone reflects the `AsyncReplyChannel` and list allocation on the
actor boundary, not the store itself.
