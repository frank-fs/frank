# Frank.Validation.Benchmarks

BenchmarkDotNet harness for `ValidationMiddleware`'s two response paths, driven end-to-end
through a real `TestServer` HTTP pipeline (not a proxy — exercises the actual middleware,
including `EnableBuffering`, `JsonLdParser`, and `Validator.validate`).
Not part of `Frank.sln` — run manually.

## How to run

```bash
cd /path/to/frank
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
  dotnet run -c Release \
  --project bench/Frank.Validation.Benchmarks/Frank.Validation.Benchmarks.fsproj
```

Artifacts (HTML + GitHub-flavoured Markdown) land in
`bench/Frank.Validation.Benchmarks/BenchmarkDotNet.Artifacts/results/`.

## What is measured

| Benchmark | What it exercises |
|-----------|------------------|
| `PassThrough200` (baseline) | valid ld+json body: parse → merge graphs → static SHACL validate (conforms) → `next.Invoke` |
| `Reject422` | invalid ld+json body (missing datatype): parse → merge graphs → static SHACL validate (rejects) → re-store the `Normalised` report graph → serialize report as JSON-LD → 422 |

## Measured results

Environment: Apple M2 Pro, .NET 10.0.9, Arm64 RyuJIT, macOS 26.5.2.

| Method         | Mean     | Error     | StdDev    | Ratio | Allocated | Alloc Ratio |
|--------------- |---------:|----------:|----------:|------:|----------:|------------:|
| PassThrough200 | 724.2 us |  87.08 us |  81.46 us |  1.00 |   1.05 MB |        1.00 |
| Reject422      | 769.1 us | 112.11 us | 104.87 us |  1.07 |   1.14 MB |        1.09 |

For this shape configuration (single required-property SHACL shape, ~250-byte body),
the 422 path is modestly heavier than the passthrough baseline: ~7% more wall time and
~9% more allocation, driven by the additional static-validate-reject branch (report
normalisation + JSON-LD serialization of the SHACL report graph) versus the passthrough
branch's `next.Invoke`. The absolute mean (~700-800 us) is dominated by `TestServer`/
`HttpClient` request-pipeline overhead, not by the RDF/SHACL work itself — for scale,
`Frank.Provenance.Benchmarks.SerializeOneRecord` (JSON-LD serialization alone, no HTTP
pipeline) costs ~85 us. The relative delta between the two rows is the meaningful signal;
absolute microsecond figures are pipeline-dominated. A workload with a larger/more-complex
shape graph or bigger invalid bodies (deeper property paths, more violations) would widen
this gap further, since only the 422 branch pays for report construction and serialization.
