# Implementation Plan: Frank.Datastar Streaming HTML Generation

**Branch**: `015-datastar-streaming-html` | **Date**: 2026-02-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/015-datastar-streaming-html/spec.md`

## Summary

Add stream-based overloads to Frank.Datastar's SSE event operations that accept a `TextWriter -> Task` writer callback instead of a `string`. A custom internal `SseDataLineWriter` (TextWriter subclass) bridges the caller's text output to the existing `IBufferWriter<byte>` write pipeline, auto-emitting SSE `data:` lines on newlines. This eliminates full HTML string materialization, reducing allocations by 50%+ in high-throughput scenarios (1000+ events/sec). The existing string-based API remains unchanged (backward compatible).

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank.Datastar 8.0.0 from spec 014)
**Primary Dependencies**: ASP.NET Core (HttpResponse, IBufferWriter, PipeWriter), System.IO (TextWriter), System.Buffers (ArrayPool)
**Storage**: N/A
**Testing**: Expecto + DefaultHttpContext/MemoryStream mock pattern (existing Frank.Datastar.Tests)
**Target Platform**: .NET 8.0/9.0/10.0 (multi-targeting: Linux/Windows/macOS server)
**Project Type**: Single library (Frank.Datastar)
**Performance Goals**: 50%+ allocation reduction per SSE event; measurable throughput improvement at 1000+ events/sec
**Constraints**: Backward compatible — no changes to existing string-based API; byte-for-byte output equivalence (SC-004)
**Scale/Scope**: 1 new internal type (SseDataLineWriter), 12 new public method overloads on ServerSentEventGenerator, 8 new module functions on Datastar

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | Feature adds streaming capability to existing resource-oriented SSE API. No route-centric patterns introduced. |
| II. Idiomatic F# | PASS | `TextWriter -> Task` callback is idiomatic. Module functions use curried `writer -> ctx -> Task` pattern. `inline` functions for zero overhead. |
| III. Library, Not Framework | PASS | No view engine adapters. Frank.Datastar provides the streaming primitives; view engines adopt at their own pace. |
| IV. ASP.NET Core Native | PASS | Uses `HttpResponse.BodyWriter` (PipeWriter/IBufferWriter), `TextWriter`, `ArrayPool` — all standard .NET/ASP.NET Core types. No custom abstractions hiding the platform. |
| V. Performance Parity | PASS | Core motivation. Eliminates string materialization. ArrayPool for line buffer. Pre-allocated byte literals. Matches or exceeds allocation profile of direct ASP.NET Core usage. |

### Post-Design Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | Stream overloads are additive to existing resource API. `datastar` computation expression unchanged. |
| II. Idiomatic F# | PASS | `Datastar.streamPatchElements` follows existing naming convention. `inline` for zero overhead. Curried signatures pipeline-friendly. |
| III. Library, Not Framework | PASS | No view engine code. No opinions beyond SSE streaming. Users choose when to use string vs stream. |
| IV. ASP.NET Core Native | PASS | `SseDataLineWriter` wraps `IBufferWriter<byte>` from ASP.NET Core's PipeWriter. `TextWriter` is standard .NET. |
| V. Performance Parity | PASS | Eliminates 500-2000 byte string allocation per event. Line buffer rented from ArrayPool (~0 steady-state allocation). |

**GATE RESULT: ALL PASS** — No violations.

## Project Structure

### Documentation (this feature)

```text
specs/015-datastar-streaming-html/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0: research decisions
├── data-model.md        # Phase 1: entity model
├── quickstart.md        # Phase 1: usage examples
├── contracts/
│   └── api-surface.md   # Phase 1: public API contracts
├── checklists/
│   └── requirements.md  # Specification quality checklist
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/Frank.Datastar/
├── Consts.fs                      # [unchanged] Byte constants and defaults
├── Types.fs                       # [unchanged] Option record types
├── SseDataLineWriter.fs           # [NEW] Internal TextWriter subclass for SSE line buffering
├── ServerSentEvent.fs             # [unchanged] Internal byte-level write helpers
├── ServerSentEventGenerator.fs    # [MODIFIED] Add stream-based method overloads
└── Frank.Datastar.fs              # [MODIFIED] Add stream* module functions

src/Frank.Datastar/Frank.Datastar.fsproj  # [MODIFIED] Add SseDataLineWriter.fs compile entry

test/Frank.Datastar.Tests/
└── DatastarTests.fs               # [MODIFIED] Add stream-based tests
```

**Structure Decision**: No new projects. One new source file (`SseDataLineWriter.fs`) added to existing `Frank.Datastar` project. Two existing files modified to add stream-based overloads. Existing file compilation order preserved — `SseDataLineWriter.fs` inserted before `ServerSentEventGenerator.fs` (which depends on it).

## Complexity Tracking

No violations to justify. Feature adds one internal type and extends the existing public API surface with additive overloads. No new projects, no new abstractions, no new dependencies.

## Design Decisions

### D-001: SseDataLineWriter placement in compilation order

`SseDataLineWriter.fs` must compile before `ServerSentEventGenerator.fs` (which uses it) and after `ServerSentEvent.fs` (which provides the byte-level write helpers it calls). Insertion point in `.fsproj`:

```xml
<Compile Include="ServerSentEvent.fs" />
<Compile Include="SseDataLineWriter.fs" />    <!-- NEW -->
<Compile Include="ServerSentEventGenerator.fs" />
```

### D-002: SseDataLineWriter depends on ServerSentEvent helpers

The `SseDataLineWriter.emitLine()` method calls `ServerSentEvent.writeUtf8Literal`, `writeSpace`, `writeNewline`, and encodes chars using `Encoding.UTF8.GetBytes(charSpan, byteSpan)` — the same pattern as `ServerSentEvent.writeUtf8String`. This reuses the existing write pipeline rather than duplicating it.

### D-003: ExecuteScript stream wrapping

The stream-based `ExecuteScriptAsync` emits `<script>` open/close tags itself, then creates the `SseDataLineWriter` for just the script body. The writer callback writes only the script content, not the wrapping tags. This matches the string-based API where `ExecuteScriptAsync(response, scriptBody)` wraps the body.

### D-004: RemoveElement stream semantics

The stream-based `RemoveElementAsync` creates an `SseDataLineWriter` with `dataLineType = Bytes.DatalineSelector`. The writer callback provides the CSS selector. While the benefit is minimal for short selectors, it maintains API consistency (FR-001) and enables dynamic selector generation without string allocation.

### D-005: Version bump

This is a minor, backward-compatible addition. Version bump from 8.0.0 to 8.1.0 (new public API surface, no breaking changes).

## References

- [spec.md](spec.md) — Feature specification with 10 FRs, 6 SCs
- [research.md](research.md) — 7 research decisions (R-001 through R-007)
- [data-model.md](data-model.md) — Entity model and relationships
- [contracts/api-surface.md](contracts/api-surface.md) — Full public API surface with F# signatures
- [quickstart.md](quickstart.md) — Usage examples for string-based and stream-based APIs
