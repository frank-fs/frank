# Struct/Inline Benchmark Harness

**Date**: 2026-08-08
**Branch**: `worktree-performance`
**Status**: Draft — awaiting review

## Context

frank-fs/frank#485 started as a narrow question: `Frank.Provenance`'s new `ProvClass`/`ProvRelation` vocabulary DUs are data-free enumerations and obvious `[<Struct>]` candidates, which raised whether existing types in `Frank.Rdf`/`Frank.JsonHome` deserved the same treatment. A full-repo sweep (this branch, 2026-08-08) found the question is bigger than two packages: every `src/Frank.*` package has struct candidates of varying safety, `inline` is a second under-used optimization axis (both for hot-path functions and for the ~187 computation-expression builder members across 8 `*Builder` types, none currently `inline`), and a measurement harness already exists on `master` (`benchmarks/Frank.Benchmarks`, BenchmarkDotNet) alongside a recoverable allocation-delta harness from a rolled-back branch (`test/Frank.TestSupport/AllocationHarness.fs`, commit `a222d19f` on `origin/feature/v7.3.2`).

This spec covers the measurement mechanism and the evaluation methodology. It does not itself convert any type — #485's governing rule is **no conversions without measurement**, confirmed by one concrete near-miss: `Frank.Datastar`'s `PatchElementsOptions` is already `[<Struct>]`, but its nested `ElementPatchMode`/`PatchElementNamespace` fields are not, so the struct still holds a heap reference for them today. Precedent-shaped code slipping past isn't hypothetical here — it already happened once in this codebase.

## Goals

- Give every candidate type/function a repeatable way to answer "does this actually help" with a number, not an assumption.
- Reuse existing infrastructure (`benchmarks/Frank.Benchmarks`, the recoverable `#437`/`#468` harnesses) rather than inventing a third approach.
- Produce a classification (below) that downstream child issues can point to instead of re-deriving it.
- Land one artifact — this harness — that is itself independently testable and mergeable, ahead of any type conversion.

## Non-goals

- Converting any type to `[<Struct>]` or marking any function `inline`. That's downstream work, gated on this harness landing, tracked as separate child issues (see "Follow-on issues").
- Building new harness architecture. `benchmarks/Frank.Benchmarks` (BenchmarkDotNet, throughput + `MemoryDiagnoser` allocation columns) and the recoverable Expecto-based `AllocationHarness` (CI-safe growth-regression gate) already cover this from two angles; this spec wires them up for comparative use, it doesn't replace them.
- `Frank.Alps` conversions. Its protocol-type refactor is inbound and will change the shapes being measured; catalogued in #485, excluded from this pass.

## Classification taxonomy

Carried forward from #485's full-sweep table — every candidate type/function gets one of:

| Verdict | Meaning |
|---|---|
| `easy-win` | Data-free DU cases, or all cases/fields share one type. No polymorphic-storage cost. Still measured, not assumed. |
| `needs-measurement` | Mixed field types across DU cases, or a large record. A struct DU reserves a slot per distinct field across *all* cases simultaneously — can end up larger than the class version's single object reference. |
| `boxed-skip` | Flows through an `obj`/`obj list`/`IList<object>`-typed collection (`HandlerDefinition.Metadata`, ASP.NET `EndpointBuilder.Metadata`). Boxing happens regardless of struct-ness; benefit unlikely to materialize, deprioritized rather than measured first. |
| `not-eligible` | Self- or mutually-recursive type. Cannot be a struct DU (unbounded size). |
| `inline-candidate` | Small, hot-path, non-recursive function or CE builder member. |
| `precedent` | Already `[<Struct>]` or `inline`. Cited as a working example, not touched. |

## Harness design

Two complementary tools, both already established in this repo — this work wires them together for A/B comparison, it doesn't invent a new mechanism:

### 1. Allocation-delta harness (Expecto, CI-safe regression gate)

Recover `test/Frank.TestSupport/AllocationHarness.fs` from `origin/feature/v7.3.2`@`a222d19f` verbatim:

```fsharp
module Frank.TestSupport.AllocationHarness

let measureAllocationDeltas (warmup: unit -> unit) (request: unit -> unit) (iterations: int) : int64[] =
    // warms once, then returns per-call GC.GetAllocatedBytesForCurrentThread() delta, one per iteration
```

Originally built as a growth-*regression* gate (`assertNoAllocationGrowth`: does allocation per call grow across repeats, e.g. an unbounded cache). This work reuses the lower-level `measureAllocationDeltas` primitive directly for a *comparative* purpose it wasn't originally built for: call it once against a baseline representation (current class DU/record) and once against a candidate representation (temporarily `[<Struct>]`-annotated), and compare the resulting per-call byte deltas. No new harness code needed for this — `measureAllocationDeltas`'s return type (`int64[]`) already supports being called twice and diffed by the caller.

### 2. Throughput/allocation benchmarks (BenchmarkDotNet, manual deep-dive)

Extend `benchmarks/Frank.Benchmarks` (already on `master`, already has `MemoryDiagnoser`-style allocation columns via BenchmarkDotNet's own instrumentation) with one benchmark class per measured type/hot-path function, added incrementally by the child issues that do the actual measurement — not built out in this pass beyond confirming the project still builds and runs after the `Frank.TestSupport` addition.

### Per-type measurement workflow (used by downstream child issues, not this one)

1. Temporarily apply `[<Struct>]` (or `inline`) to the candidate on a scratch commit.
2. Run the allocation-delta comparison (harness above) and, for anything on a per-request path, the BenchmarkDotNet suite.
3. Record before/after numbers in the child issue.
4. Keep the change if it measurably helps (smaller allocation delta, equal-or-better throughput); revert if it doesn't, and record why — a null result is still a result worth keeping in the issue so the question doesn't get re-asked later.

## Scope / phased rollout

- **Phase 0 (this spec's plan)**: recover and wire the allocation-delta harness into `test/Frank.TestSupport`, prove it works (restore its own proof tests), confirm `benchmarks/Frank.Benchmarks` still builds. No type conversions.
- **Phase 1** (`easy-win`/`precedent`-gap types — lowest risk): `Frank.Datastar` `ElementPatchMode`/`PatchElementNamespace` (the confirmed gap), `Frank.Provenance` `ProvenanceQuery`/`ProvenanceStoreConfig`, `Frank.Rdf` `Node`, `Frank.Validation` `ValidationOutcome`.
- **Phase 2** (`needs-measurement` types): `Frank.Rdf` `Literal`/`Value`, `Frank.Validation` `Violation` (prioritized — genuine per-request hot path via the SHACL interceptor middleware) and `TargetSpec`, `Frank.Provenance` `SparqlQueryResult`.
- **Phase 3** (`inline-candidate` functions): `Frank/MediaTypeNegotiation.fs`'s `isWildcard`/`matches`/`specificity` (confirmed per-request hot path); CE builder members across the 8 `*Builder` types (187 members, call-site frequency needs checking per builder before assuming startup-only).
- **Phase 4 (blocked)**: `Frank.Alps` candidates (`StateComposition`, `ProtocolTransition`), gated on the inbound protocol-type refactor landing.

Phases 1-4 become child issues under #485 (next step, via `decompose`), each scoped to one measurement-then-decide unit of work.

## Success criteria

- `test/Frank.TestSupport/AllocationHarness.fs` restored, added to the solution, its own proof tests (known-good/known-bad cases) pass.
- `benchmarks/Frank.Benchmarks` still builds and runs after the solution change.
- No type is converted in this pass — verified by `git diff` touching only `test/Frank.TestSupport/**`, the `.sln`, and `test/Frank.Tests/Frank.Tests.fsproj`'s project reference.

## Follow-on issues

To be filed as children of #485 via `decompose`, one per phase above (Phase 1 candidates may split further, one issue per type, per decompose's own task-sizing rules).
