# Implementation Plan: Datastar SSE Streaming Support

**Branch**: `001-datastar-support` | **Date**: 2025-01-25 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/001-datastar-support/spec.md`

## Summary

Add Datastar SSE streaming support to Frank through a new `Frank.Datastar` extension library. The library extends Frank's `ResourceBuilder` computation expression with custom operations for SSE stream management, HTML patching, signal handling, and script execution. Standalone functions are provided for use outside computation expressions.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting)
**Primary Dependencies**: Frank 6.x, StarFederation.Datastar.FSharp (latest)
**Storage**: N/A
**Testing**: Expecto 10.x
**Target Platform**: ASP.NET Core web applications
**Project Type**: Library extension with sample applications
**Performance Goals**: Parity with direct StarFederation.Datastar.FSharp usage (no overhead from Frank integration)
**Constraints**: Must follow F# idioms per constitution; no C#-style APIs
**Scale/Scope**: Single extension library + 2 sample applications

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Justification |
|-----------|--------|---------------|
| I. Resource-Oriented Design | ✅ PASS | Extension integrates with `resource` computation expression as a custom operation. SSE streaming is a server-push pattern that fits within the resource model. |
| II. Idiomatic F# | ✅ PASS | Uses computation expressions, Task-based async (F# idiomatic for interop), curried functions in standalone module, voption for signal reading. |
| III. Library, Not Framework | ✅ PASS | Frank.Datastar is an optional extension that adapts StarFederation.Datastar to Frank. No view engine imposed - samples demonstrate both string templates and Hox. |
| IV. ASP.NET Core Native | ✅ PASS | Exposes HttpContext directly in handlers. Uses ASP.NET Core's Response object for SSE. No platform hiding. |
| V. Performance Parity | ✅ PASS (presumed) | Thin wrapper over StarFederation.Datastar.FSharp. No additional allocations in hot paths. Need benchmarks to confirm. |

**Gate Result**: PASS - All constitutional principles satisfied.

## Project Structure

### Documentation (this feature)

```text
specs/001-datastar-support/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── Frank/                        # Core Frank library (existing)
│   ├── Builder.fs               # ResourceBuilder, WebHostBuilder
│   └── ContentNegotiation.fs    # Content negotiation utilities
└── Frank.Datastar/              # NEW: Datastar extension library
    ├── Frank.Datastar.fsproj    # Multi-target net8.0;net9.0;net10.0
    ├── Frank.Datastar.fs        # ResourceBuilder extensions + standalone module
    └── README.md                # Library documentation

test/
└── Frank.Datastar.Tests/        # NEW: Unit tests
    ├── Frank.Datastar.Tests.fsproj
    └── DatastarTests.fs

sample/
├── Frank.Datastar.Basic/        # NEW: Basic sample with F# strings
│   ├── Frank.Datastar.Basic.fsproj
│   ├── Program.fs
│   └── wwwroot/                 # Static files including index.html
└── Frank.Datastar.Hox/          # NEW: Hox integration sample
    ├── Frank.Datastar.Hox.fsproj
    ├── Program.fs
    └── wwwroot/
```

**Structure Decision**: Single library extending Frank core with two sample applications demonstrating different HTML rendering approaches (string templates vs Hox). This follows Frank's existing sample structure pattern.

## Complexity Tracking

| Issue | Why It Exists | Resolution |
|-------|---------------|------------|
| Sample apps use undefined `UseResource` | Samples were written for minimal API pattern | Must either: (a) create UseResource extension, or (b) refactor to use `webHost` builder |
| String interpolation format specifiers fail | F# 8+ restriction on format strings in interpolation | Use explicit `.ToString("format")` calls |
| Hox API case sensitivity | Hox uses lowercase (`asStringAsync`) | Use correct Hox API names |

## Current Implementation Status

### Implemented (on branch)

> **⚠️ FR-005 Compliance Required**: The custom operations listed below (except `datastar`) violate FR-005 and are scheduled for removal in tasks.md Phase 1 (T001-T007). After Phase 1 completion, only the `datastar` custom operation will remain on ResourceBuilder.

1. **Frank.Datastar library** (`src/Frank.Datastar/Frank.Datastar.fs`)
   - `DatastarExtensions` module with `ResourceBuilder` extensions
   - `datastar` custom operation for multi-event streaming ✅ (KEEP)
   - `patchElements` (3 overloads) ❌ (REMOVE per FR-005)
   - `removeElement` ❌ (REMOVE per FR-005)
   - `patchSignals` (2 overloads) ❌ (REMOVE per FR-005)
   - `executeScript` ❌ (REMOVE per FR-005)
   - `readSignals` ❌ (REMOVE per FR-005)
   - `transformSignals` ❌ (REMOVE per FR-005)
   - `Datastar` standalone module with curried functions ✅ (KEEP - these are helper functions, not custom operations)

2. **Test suite** (`test/Frank.Datastar.Tests/DatastarTests.fs`)
   - 9 unit tests covering core operations
   - Mock HttpContext testing pattern
   - Tests pass (verified against library build)

3. **Sample applications** (have compilation errors - need fixes)
   - Basic sample demonstrates hypermedia-first patterns
   - Hox sample demonstrates type-safe HTML rendering

### Issues to Resolve

1. **Sample compilation errors**:
   - `UseResource` method doesn't exist - samples need refactoring
   - F# format string syntax issues (partially fixed)
   - Hox API case mismatches

2. **Missing validation**:
   - Samples don't compile = SC-007 not met
   - No HTTP method flexibility testing (FR supports any method but only GET is registered)

---

## Phase Completion Status

### Phase 0: Research ✅ COMPLETE

**Output**: `research.md`

Resolved questions:
- How to register resources with minimal APIs → Create `UseResource` extension
- Hox API naming conventions → Use lowercase (`asStringAsync`)
- F# format string limitations → Use explicit `.ToString()`
- HTTP method support → Need to update library to support methods other than GET

### Phase 1: Design & Contracts ✅ COMPLETE

**Output**: `data-model.md`, `contracts/frank-datastar-api.md`, `quickstart.md`

- Data model documented with type relationships
- API contract defined for all public operations
- Quickstart guide created with usage examples
- Agent context updated via script

### Post-Design Constitution Re-Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | ✅ PASS | Design maintains resource-centric approach |
| II. Idiomatic F# | ✅ PASS | API uses F# idioms throughout |
| III. Library, Not Framework | ✅ PASS | Extension adapts, doesn't impose |
| IV. ASP.NET Core Native | ✅ PASS | HttpContext exposed directly |
| V. Performance Parity | ⚠️ NEEDS VERIFICATION | Benchmarks not yet implemented |

**Gate Result**: PASS (with benchmark task for Phase 2)

---

## Next Steps (Phase 2 - Tasks)

Run `/speckit.tasks` to generate actionable implementation tasks for:
1. Fix sample application compilation errors
2. Add `UseResource` extension to Frank or refactor samples to use `webHost`
3. Fix Hox sample API usage
4. Add HTTP method flexibility support
5. Add performance benchmarks
6. Final validation against success criteria
