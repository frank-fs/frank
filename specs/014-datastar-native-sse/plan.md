# Implementation Plan: Frank.Datastar Native SSE

**Branch**: `014-datastar-native-sse` | **Date**: 2026-02-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/014-datastar-native-sse/spec.md`

## Summary

Replace Frank.Datastar's dependency on StarFederation.Datastar.FSharp with a purpose-built SSE implementation targeting .NET 10 only. The new implementation writes Datastar SSE events directly to `IBufferWriter<byte>` using pre-allocated byte arrays and zero-allocation string splitting, conforming to the Datastar SDK ADR specification. The existing public API (`Datastar` module functions and `datastar` custom operation) is preserved unchanged. A public `ServerSentEventGenerator` is exposed for advanced users per ADR compliance. The `ExecuteScriptOptions` type gains an `Attributes` field (`string[]`) for full ADR coverage.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0 (single target, down from multi-target)
**Primary Dependencies**: Frank 7.0.0 (project reference), Microsoft.AspNetCore.App (framework reference), Microsoft.Extensions.Primitives (for StringTokenizer, included in framework)
**Storage**: N/A (no persistence)
**Testing**: Expecto 10.x via YoloDev.Expecto.TestSdk, targeting net10.0
**Target Platform**: .NET 10.0 (ASP.NET Core server)
**Project Type**: Single library project (src/Frank.Datastar/)
**Performance Goals**: Match or exceed StarFederation.Datastar.FSharp allocation profile; zero intermediate string/byte allocations in hot paths
**Constraints**: Byte-for-byte SSE format compliance with Datastar SDK ADR; preserve existing public API surface
**Scale/Scope**: ~300-400 lines of F# replacing 153-line wrapper + ~600-line external dependency

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Resource-Oriented Design** | PASS | `datastar` custom operation on `ResourceBuilder` preserved; resource-centric API unchanged |
| **II. Idiomatic F#** | PASS | F# struct records, discriminated unions, value options, inline functions, pipeline-friendly signatures |
| **III. Library, Not Framework** | PASS | Frank.Datastar remains a composable library; no view engine or ORM opinions; users compose with Hox/Oxpecker/raw strings |
| **IV. ASP.NET Core Native** | PASS | Writes directly to `HttpResponse.BodyWriter` (PipeWriter/IBufferWriter); uses `HttpContext` directly; no platform-hiding abstractions |
| **V. Performance Parity** | PASS | Pre-allocated byte arrays, inline functions, IBufferWriter pattern, StringTokenizer for zero-alloc line splitting — matching or exceeding current StarFederation approach |

**Technical Standards Check:**

| Standard | Status | Notes |
|----------|--------|-------|
| Target Framework .NET 8.0+ | PASS | Targets net10.0 (superset of 8.0+); Frank core unchanged at net8.0/9.0/10.0 |
| Dependencies: minimize external | PASS | Removes StarFederation.Datastar.FSharp dependency; zero external NuGet packages |
| Nullability: Option types | PASS | `voption` (ValueOption) for optional fields; no nulls in public API |
| Testing: all public APIs | PASS | 18 existing tests + new tests for Attributes field and public ServerSentEventGenerator API |
| Benchmarks for perf changes | PASS | SC-005 requires allocation profiling comparison |

## Project Structure

### Documentation (this feature)

```text
specs/014-datastar-native-sse/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api-surface.md   # Public API contract
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/Frank.Datastar/
├── Frank.Datastar.fsproj        # Modified: net10.0 only, remove StarFederation dep
├── Consts.fs                    # NEW: Constants, enums, pre-allocated byte arrays
├── Types.fs                     # NEW: Option types (struct records), type aliases
├── ServerSentEvent.fs           # NEW: Low-level IBufferWriter<byte> write functions
├── ServerSentEventGenerator.fs  # NEW: Public SSE generator (ADR-compliant)
└── Frank.Datastar.fs            # Modified: rewire to internal types, preserve public API

test/Frank.Datastar.Tests/
├── Frank.Datastar.Tests.fsproj  # Unchanged (already targets net10.0)
└── DatastarTests.fs             # Modified: update import from StarFederation to Frank.Datastar

sample/Frank.Datastar.Basic/    # Unchanged (no direct StarFederation imports)
sample/Frank.Datastar.Hox/      # Unchanged (no direct StarFederation imports)
sample/Frank.Datastar.Oxpecker/ # Unchanged (no direct StarFederation imports)
```

**Structure Decision**: The existing single-project layout under `src/Frank.Datastar/` is preserved. Four new source files are added following the same module decomposition as the upstream StarFederation.Datastar.FSharp project (Consts → Types → ServerSentEvent → ServerSentEventGenerator), maintaining a clear dependency chain. The existing `Frank.Datastar.fs` is modified to reference the new internal types instead of the external dependency.

## Complexity Tracking

No constitution violations to justify. The design adds 4 source files but each is small and focused (~50-100 lines), matching the decomposition of the upstream implementation being replaced.
