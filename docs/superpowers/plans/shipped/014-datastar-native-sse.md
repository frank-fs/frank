---
source: specs/014-datastar-native-sse
type: plan
---

# Implementation Plan: Frank.Datastar Native SSE Implementation

**Branch**: `014-datastar-native-sse` | **Date**: 2026-02-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/014-datastar-native-sse/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Replace StarFederation.Datastar.FSharp dependency with a custom SSE implementation for Frank.Datastar. The implementation provides zero-copy buffer writing optimized for Datastar's specific SSE event format (patch-elements, patch-signals, remove-element, execute-script). Uses only APIs available across net8.0/net9.0/net10.0 to maintain broad compatibility while eliminating external dependencies. Preserves complete API compatibility for seamless upgrades. Version 7.1.0 (minor bump).

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0, 9.0, and 10.0 (multi-targeting: `net8.0;net9.0;net10.0`)
**Primary Dependencies**:
  - Frank 7.0.0+ (project reference - core framework)
  - Microsoft.AspNetCore.App (framework reference - HttpContext, HttpResponse, IBufferWriter)
  - Microsoft.Extensions.Primitives (included in framework - StringTokenizer for line splitting)
  - System.Text.Json (framework - signal deserialization)

**Storage**: N/A (stateless SSE event streaming)

**Testing**:
  - NUnit (existing test framework for Frank.Datastar.Tests)
  - Manual testing via sample projects: Frank.Datastar.Basic, Frank.Datastar.Hox, Frank.Datastar.Oxpecker
  - SSE output validation against Datastar SDK ADR specification reference test vectors

**Target Platform**: ASP.NET Core web applications on Linux/Windows/macOS

**Project Type**: Single library project (F# library targeting ASP.NET Core)

**Performance Goals**:
  - Zero additional allocations per event vs. baseline (StarFederation.Datastar.FSharp)
  - Direct buffer writing to `HttpResponse.BodyWriter` (IBufferWriter<byte>)
  - Pre-allocated byte arrays for SSE field prefixes
  - Zero-allocation string segmentation for multi-line payloads
  - Inline function wrappers for zero overhead (Constitution Principle V)

**Constraints**:
  - MUST preserve existing public API surface (Frank.Datastar module, datastar custom operation)
  - MUST conform to Datastar SDK ADR specification for SSE message format
  - MUST use only APIs available across net8.0/net9.0/net10.0 (no .NET 10-specific features)
  - MUST NOT depend on StarFederation.Datastar.FSharp NuGet package
  - MUST ensure SSE stream initialization occurs exactly once per request
  - MUST respect HttpContext.RequestAborted cancellation tokens

**Scale/Scope**:
  - Single library project with 5 source files (~500 LOC)
  - 4 option types (PatchElementsOptions, PatchSignalsOptions, RemoveElementOptions, ExecuteScriptOptions)
  - 10 public API functions in Datastar module
  - 2 SSE event types (datastar-patch-elements, datastar-patch-signals)
  - 3 sample projects consuming the library
  - 18+ existing tests to preserve compatibility

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Resource-Oriented Design ✅

**Status**: COMPLIANT - Not applicable. This feature is a library implementation detail (SSE event generation) that enhances Frank's resource handlers. The `datastar` custom operation on `ResourceBuilder` maintains resource-oriented design by allowing resource handlers to stream SSE responses. No changes to resource semantics.

**Evidence**: FR-004 preserves the `datastar` custom operation on `ResourceBuilder`. The implementation is purely internal (buffer writing) and does not affect Frank's resource abstraction.

### II. Idiomatic F# ✅

**Status**: COMPLIANT

**Evidence**:
- Option types: `EventId: string voption`, `Selector: Selector voption` (FR-012)
- Struct value types for options: `[<Struct>] type PatchElementsOptions` (FR-012)
- Static `Defaults` for each option type (idiomatic F# pattern)
- Inline functions in `Datastar` module for zero overhead (FR-004)
- Pipeline-friendly signatures: `Datastar.patchElements html ctx`
- Computation expression: `datastar` custom operation on `ResourceBuilder`

### III. Library, Not Framework ✅

**Status**: COMPLIANT

**Evidence**: Frank.Datastar is a focused library for SSE integration. It provides SSE event streaming and nothing else. Developers can use it within any ASP.NET Core application without adopting additional opinions. FR-009 removes the external dependency to reduce lock-in further.

### IV. ASP.NET Core Native ✅

**Status**: COMPLIANT

**Evidence**:
- Exposes `HttpContext` directly in handlers (FR-004: `operation: HttpContext -> Task<unit>`)
- Uses `HttpResponse.BodyWriter` (IBufferWriter<byte>) directly (FR-007)
- Respects `HttpContext.RequestAborted` cancellation tokens (FR-011)
- Sets standard ASP.NET Core response headers (FR-003: Content-Type, Cache-Control, Connection)
- No abstractions hiding the platform - developers work with raw HttpContext

### V. Performance Parity ✅

**Status**: COMPLIANT

**Evidence**:
- SC-005: "allocates no more memory per event than the existing StarFederation.Datastar.FSharp implementation"
- FR-007: Direct buffer writing techniques
- Pre-allocated byte arrays for SSE prefixes (User Story 3, Acceptance Scenario 1)
- Zero-allocation string segmentation (User Story 3, Acceptance Scenario 2)
- Inline functions for zero overhead (User Story 3, Acceptance Scenario 3)
- Struct option types to avoid heap allocations

### Overall Assessment

**PASS** - All five core principles are satisfied. No violations requiring justification.

## Project Structure

### Documentation (this feature)

```text
specs/014-datastar-native-sse/
├── plan.md              # This file (/speckit.plan command output)
├── spec.md              # Feature specification
├── research.md          # Phase 0 output (/speckit.plan command) - ALREADY EXISTS
├── data-model.md        # Phase 1 output (/speckit.plan command) - ALREADY EXISTS
├── quickstart.md        # Phase 1 output (/speckit.plan command) - ALREADY EXISTS
├── contracts/           # Phase 1 output (/speckit.plan command) - ALREADY EXISTS
│   └── api-surface.md   # Public API contract
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan) - ALREADY EXISTS
```

### Source Code (repository root)

```text
src/Frank.Datastar/
├── Consts.fs                      # SSE field prefixes, default values, byte constants
├── Types.fs                       # Option types (PatchElementsOptions, etc.), type aliases
├── ServerSentEvent.fs             # Low-level SSE field writers, buffer operations
├── ServerSentEventGenerator.fs    # Public API: StartServerEventStreamAsync, PatchElementsAsync, etc.
└── Frank.Datastar.fs              # ResourceBuilder extension (datastar operation), Datastar module helpers

test/Frank.Datastar.Tests/
├── DatastarTests.fs               # Existing tests - must pass without modification (SC-001)
└── Frank.Datastar.Tests.fsproj

samples/Frank.Datastar.Basic/      # Existing sample - must compile unchanged (SC-002)
samples/Frank.Datastar.Hox/        # Existing sample - must compile unchanged (SC-002)
samples/Frank.Datastar.Oxpecker/   # Existing sample - must compile unchanged (SC-002)
```

**Structure Decision**: Single project structure (Option 1) is appropriate. Frank.Datastar is a focused library with clear separation of concerns:
- **Consts.fs**: Compile-time constants and byte array allocation
- **Types.fs**: Public option types and domain aliases
- **ServerSentEvent.fs**: Internal SSE protocol implementation (buffer writing)
- **ServerSentEventGenerator.fs**: Public API facade (ADR-compliant operations)
- **Frank.Datastar.fs**: Frank integration (ResourceBuilder extension + helper module)

This structure matches the existing Frank.Datastar project layout and aligns with Frank's philosophy (Principle III: Library, Not Framework).

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

*No violations - table omitted.*

## Phase 0: Outline & Research

**Status**: ✅ COMPLETE (pre-existing)

The research phase has already been completed and documented in `research.md`. Key findings:

### Research Outcomes

All technical unknowns have been resolved through analysis of:
1. Datastar SDK ADR specification for SSE message format
2. StarFederation.Datastar.FSharp implementation as baseline reference
3. .NET APIs available across net8.0/net9.0/net10.0 for buffer writing
4. Performance profiling requirements for allocation tracking

### Key Decisions (from research.md)

**Decision 1: Custom SSE Implementation vs. System.Net.ServerSentEvents**
- **Chosen**: Custom implementation using `IBufferWriter<byte>` and pre-allocated byte arrays
- **Rationale**:
  - System.Net.ServerSentEvents is .NET 10 only; requirement changed to multi-target net8.0/net9.0/net10.0
  - Custom implementation provides fine-grained control over Datastar-specific event formatting
  - Allows optimization for Datastar's two event types and specific data line prefixes
  - Eliminates external dependency (StarFederation.Datastar.FSharp)
- **Alternatives Considered**:
  - System.Net.ServerSentEvents: Rejected due to .NET 10-only availability
  - Keeping StarFederation.Datastar.FSharp: Rejected to eliminate external dependency chain

**Decision 2: Buffer Writing Strategy**
- **Chosen**: Direct writing to `HttpResponse.BodyWriter` (IBufferWriter<byte>) with pre-allocated byte arrays for SSE field prefixes
- **Rationale**:
  - Matches StarFederation.Datastar implementation's allocation profile
  - Available across all three target frameworks
  - Enables zero-copy writes for constant prefixes (event:, id:, retry:, data:)
  - Supports efficient multi-line payload handling via StringTokenizer
- **Alternatives Considered**:
  - StreamWriter: Rejected due to additional allocations for string-to-byte conversion
  - Response.WriteAsync: Rejected due to overhead of encoding layer

**Decision 3: Multi-line Payload Handling**
- **Chosen**: StringTokenizer from Microsoft.Extensions.Primitives (included in framework reference)
- **Rationale**:
  - Zero-allocation string segmentation via ReadOnlySpan<char>
  - Handles both `\n` and `\r\n` line endings correctly
  - Available across net8.0/net9.0/net10.0
- **Alternatives Considered**:
  - String.Split: Rejected due to string[] allocation
  - Manual indexing: Rejected due to complexity and error-proneness

**Decision 4: Option Types as Structs**
- **Chosen**: `[<Struct>]` value types for PatchElementsOptions, PatchSignalsOptions, RemoveElementOptions, ExecuteScriptOptions
- **Rationale**:
  - Avoids heap allocations when passing options to functions
  - Matches StarFederation.Datastar implementation
  - Each struct includes static `Defaults` for ergonomic usage
- **Alternatives Considered**:
  - Record types: Rejected due to reference type allocation overhead
  - Optional parameters: Rejected due to poor F# idiomatics

**Decision 5: Version Number**
- **Chosen**: 7.1.0 (minor bump from 7.0.0)
- **Rationale**:
  - No breaking API changes (FR-004, SC-002)
  - Internal implementation change only (dependency removal)
  - Multi-targeting addition is additive, not breaking
- **Alternatives Considered**:
  - 7.0.0 (major): Rejected because API is fully compatible
  - 6.6.0 (patch): Rejected because this is more than a bug fix

## Phase 1: Design & Contracts

**Status**: ✅ COMPLETE (pre-existing)

The design phase has already been completed and documented in `data-model.md`, `quickstart.md`, and `contracts/api-surface.md`.

### Design Artifacts

All design artifacts exist and are consistent:

1. **data-model.md**: Complete SSE event format specification, option type definitions, field semantics
2. **contracts/api-surface.md**: Public API contract for ServerSentEventGenerator and Datastar module
3. **quickstart.md**: Usage examples for common scenarios (basic streaming, signal reading, options)

### Key Design Elements

**SSE Event Structure** (from data-model.md):
- 2 event types: `datastar-patch-elements`, `datastar-patch-signals`
- Field ordering: `event:`, `[id:]`, `[retry:]`, `data: <key> <value>`, blank line terminator
- Multi-line payloads split on `\n`, each non-empty line becomes a separate `data:` line
- Optional fields omitted when defaults are used (reduces wire size)

**Option Types** (from data-model.md):
- `PatchElementsOptions`: Selector, PatchMode, UseViewTransition, Namespace, EventId, Retry
- `PatchSignalsOptions`: OnlyIfMissing, EventId, Retry
- `RemoveElementOptions`: UseViewTransition, EventId, Retry
- `ExecuteScriptOptions`: AutoRemove, Attributes (string[]), EventId, Retry

**Public API Surface** (from contracts/api-surface.md):
- `ResourceBuilder.datastar`: Custom operation for SSE handlers (1 overload for GET, 1 for custom method)
- `Datastar` module: 10 helper functions (patchElements, patchSignals, removeElement, executeScript, tryReadSignals + WithOptions variants)
- `ServerSentEventGenerator`: Public type with static methods for low-level SSE operations (ADR compliance - FR-014)

### Agent Context Update

✅ **COMPLETE** - Updated CLAUDE.md with feature technology stack:
- Language: F# 8.0+ targeting .NET 8.0, 9.0, and 10.0 (multi-targeting: `net8.0;net9.0;net10.0`)
- Storage: N/A (stateless SSE event streaming)

## Phase 2: Task Planning

**Status**: ⏸️ DEFERRED - Use `/speckit.tasks` command to generate tasks.md

The planning phase ends here. Task generation is handled by a separate command (`/speckit.tasks`) which will:
1. Extract implementation tasks from the plan and design artifacts
2. Establish task dependencies and ordering
3. Generate `tasks.md` with actionable task list

**Note**: For this feature, `tasks.md` already exists (implementation is complete). The `/speckit.tasks` command would regenerate it based on the current plan state.

## Implementation Notes

### Critical Success Factors

1. **API Compatibility** (SC-001, SC-002):
   - All existing tests must pass without modification
   - All sample projects must compile without source changes
   - This is a hard requirement - any API changes constitute plan failure

2. **Performance Parity** (SC-005):
   - Allocation profiling must show no regression vs. StarFederation.Datastar.FSharp
   - Direct buffer writing eliminates intermediate allocations
   - Pre-allocated byte arrays for constant SSE prefixes

3. **Multi-Framework Compatibility** (FR-008, User Story 4):
   - Implementation uses only APIs available in net8.0/net9.0/net10.0
   - No conditional compilation required
   - Project file targets: `<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>`

4. **SSE Stream Initialization** (FR-010, SC-006):
   - Headers and initial flush occur exactly once per request
   - Implemented via `HttpContext.Items` flag check
   - Subsequent calls to start stream are no-ops

5. **Cancellation Support** (FR-011):
   - All async operations accept `HttpContext.RequestAborted` token
   - Client disconnection stops writing gracefully without exceptions

### Integration Points

**Frank Core** (no changes required):
- `ResourceBuilder.AddHandler` mechanism sufficient for `datastar` custom operation
- F# computation expression infrastructure supports custom operations
- HttpContext exposure aligns with Principle IV (ASP.NET Core Native)

**Sample Projects** (must compile unchanged):
- Frank.Datastar.Basic: Basic SSE streaming with patchElements
- Frank.Datastar.Hox: Integration with Hox view engine
- Frank.Datastar.Oxpecker: Integration with Oxpecker view engine

**Test Suite** (must pass unchanged):
- Frank.Datastar.Tests: 18+ tests covering all event types and options
- Only permissible change: namespace imports if needed

### Validation Strategy

1. **Unit Tests**: Run existing Frank.Datastar.Tests suite (SC-001)
2. **Sample Compilation**: Build all three sample projects without changes (SC-002)
3. **SSE Format Validation**: Compare output against Datastar SDK ADR reference vectors (SC-004)
4. **Allocation Profiling**: Benchmark against baseline with BenchmarkDotNet (SC-005)
5. **Manual Testing**: Run sample applications and verify browser interaction

### Rollout Plan

Version 7.1.0 release:
1. Update Frank.Datastar.fsproj version to 7.1.0
2. Verify multi-targeting: `<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>`
3. Verify StarFederation.Datastar.FSharp dependency removed (SC-003)
4. Run full test suite (SC-001)
5. Build and test sample projects (SC-002)
6. Publish NuGet package with updated description emphasizing native implementation

### Risk Mitigation

**Risk**: API compatibility breaks despite specification
- **Mitigation**: Run existing tests before any implementation changes
- **Detection**: CI failure on test suite or sample compilation
- **Response**: Revert changes and review API surface differences

**Risk**: Performance regression due to different buffer writing approach
- **Mitigation**: Profile early and compare against baseline
- **Detection**: Allocation count increase in profiling results
- **Response**: Optimize buffer writing strategy or reconsider approach

**Risk**: SSE format incompatibility with Datastar SDK
- **Mitigation**: Validate output against ADR reference test vectors
- **Detection**: Browser-side Datastar client rejects events
- **Response**: Compare byte-level output with ADR specification and fix formatting

## Conclusion

The implementation plan is complete and all design artifacts exist. The feature is ready for implementation via `/speckit.tasks` followed by execution.

**Key Constraints**:
- ✅ API compatibility preserved (FR-004)
- ✅ Multi-framework targeting (net8.0/net9.0/net10.0) (FR-008)
- ✅ Zero external dependencies (FR-009, SC-003)
- ✅ Constitution compliance (all 5 principles)
- ✅ Version 7.1.0 (minor bump, no breaking changes)

**Next Steps**:
1. Run `/speckit.tasks` to generate task list (if tasks.md doesn't exist or needs regeneration)
2. Execute implementation following tasks.md
3. Validate against success criteria (SC-001 through SC-007)
4. Release version 7.1.0 to NuGet

---

**Plan Status**: ✅ COMPLETE
**Feature Branch**: `014-datastar-native-sse`
**Plan Path**: `/Users/ryanr/Code/frank-datastar/specs/014-datastar-native-sse/plan.md`
**Generated Artifacts**:
- ✅ research.md (Phase 0 - pre-existing)
- ✅ data-model.md (Phase 1 - pre-existing)
- ✅ quickstart.md (Phase 1 - pre-existing)
- ✅ contracts/api-surface.md (Phase 1 - pre-existing)
- ✅ CLAUDE.md updated (agent context)

**Constitution Re-check**: ✅ PASS (all principles satisfied post-design)

