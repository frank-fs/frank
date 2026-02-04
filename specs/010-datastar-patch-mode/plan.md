# Implementation Plan: Add WithOptions Variants for Datastar Helpers

**Branch**: `010-datastar-patch-mode` | **Date**: 2026-02-03 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/010-datastar-patch-mode/spec.md`

## Summary

Add 5 new `WithOptions` variants to the Frank.Datastar helper module, enabling developers to specify full options when calling SSE event functions. The simple helpers remain unchanged for backward compatibility; the new variants accept the corresponding options records from `StarFederation.Datastar.FSharp`.

**Functions to add:**
- `patchElementsWithOptions` (PatchElementsOptions)
- `patchSignalsWithOptions` (PatchSignalsOptions)
- `removeElementWithOptions` (RemoveElementOptions)
- `executeScriptWithOptions` (ExecuteScriptOptions)
- `tryReadSignalsWithOptions` (JsonSerializerOptions)

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting)
**Primary Dependencies**: Frank 6.x, StarFederation.Datastar.FSharp, ASP.NET Core
**Storage**: N/A
**Testing**: Expecto (existing test infrastructure)
**Target Platform**: .NET server (ASP.NET Core)
**Project Type**: Library (single project)
**Performance Goals**: Zero-overhead wrapper calls via `inline` keyword
**Constraints**: Must maintain backward compatibility; no breaking changes to existing API
**Scale/Scope**: 5 new inline functions (~30 lines of code)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Resource-Oriented Design** | PASS | Helpers support hypermedia-first SSE streaming; no route-centric changes |
| **II. Idiomatic F#** | PASS | Functions use curried signatures, inline for performance, F# idioms |
| **III. Library, Not Framework** | PASS | Additive helper functions; no new opinions imposed |
| **IV. ASP.NET Core Native** | PASS | Exposes HttpContext directly; delegates to StarFederation.Datastar |
| **V. Performance Parity** | PASS | All functions marked `inline`; zero-overhead wrappers |

**Gate Result**: PASS - All constitution principles satisfied.

## Project Structure

### Documentation (this feature)

```text
specs/010-datastar-patch-mode/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output (minimal - no unknowns)
├── checklists/
│   └── requirements.md  # Quality checklist
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
└── Frank.Datastar/
    └── Frank.Datastar.fs    # Add 5 WithOptions functions to Datastar module

tests/
└── Frank.Datastar.Tests/
    └── DatastarTests.fs     # Add tests for WithOptions variants
```

**Structure Decision**: Single project modification - adding functions to existing `Datastar` module in `Frank.Datastar.fs` and corresponding tests.

## Complexity Tracking

> No violations - feature is a straightforward additive change.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | — | — |

## Implementation Approach

### Phase 1: Add WithOptions Functions

Add 5 new inline functions to the `Datastar` module in `src/Frank.Datastar/Frank.Datastar.fs`:

```fsharp
module Datastar =
    // Existing functions unchanged...

    /// Patch HTML elements with custom options.
    let inline patchElementsWithOptions (options: PatchElementsOptions) (html: string) (ctx: HttpContext) =
        ServerSentEventGenerator.PatchElementsAsync(ctx.Response, html, options)

    /// Patch client-side signals with custom options.
    let inline patchSignalsWithOptions (options: PatchSignalsOptions) (signals: string) (ctx: HttpContext) =
        ServerSentEventGenerator.PatchSignalsAsync(ctx.Response, signals, options)

    /// Remove an element by CSS selector with custom options.
    let inline removeElementWithOptions (options: RemoveElementOptions) (selector: string) (ctx: HttpContext) =
        ServerSentEventGenerator.RemoveElementAsync(ctx.Response, selector, options)

    /// Execute JavaScript on the client with custom options.
    let inline executeScriptWithOptions (options: ExecuteScriptOptions) (script: string) (ctx: HttpContext) =
        ServerSentEventGenerator.ExecuteScriptAsync(ctx.Response, script, options)

    /// Read and deserialize signals with custom JSON serializer options.
    let inline tryReadSignalsWithOptions<'T> (jsonOptions: JsonSerializerOptions) (ctx: HttpContext) : Task<voption<'T>> =
        ServerSentEventGenerator.ReadSignalsAsync<'T>(ctx.Request, jsonOptions)
```

### Phase 2: Add Tests

Add tests to `test/Frank.Datastar.Tests/DatastarTests.fs` verifying:
1. Each `WithOptions` variant correctly passes options to underlying library
2. Defaults produce equivalent output to simple helpers
3. Non-default options are reflected in SSE output

### Key Files to Modify

| File | Change |
|------|--------|
| `src/Frank.Datastar/Frank.Datastar.fs` | Add 5 `WithOptions` functions |
| `test/Frank.Datastar.Tests/DatastarTests.fs` | Add tests for new functions |

### Dependencies Verified

The `StarFederation.Datastar.FSharp` library exposes:
- `PatchElementsOptions` with `Defaults` static member ✓
- `PatchSignalsOptions` with `Defaults` static member ✓
- `RemoveElementOptions` with `Defaults` static member ✓
- `ExecuteScriptOptions` with `Defaults` static member ✓
- `ServerSentEventGenerator.PatchElementsAsync(response, html, options)` overload ✓
- `ServerSentEventGenerator.PatchSignalsAsync(response, signals, options)` overload ✓
- `ServerSentEventGenerator.RemoveElementAsync(response, selector, options)` overload ✓
- `ServerSentEventGenerator.ExecuteScriptAsync(response, script, options)` overload ✓
- `ServerSentEventGenerator.ReadSignalsAsync<'T>(request, jsonOptions)` overload ✓

### Usage Examples

```fsharp
// Simple case (unchanged)
do! Datastar.patchElements "<div>Hi</div>" ctx

// With custom mode
let opts = { PatchElementsOptions.Defaults with PatchMode = Inner }
do! Datastar.patchElementsWithOptions opts "<div>Hi</div>" ctx

// With selector
let opts = { PatchElementsOptions.Defaults with Selector = ValueSome "#target" }
do! Datastar.patchElementsWithOptions opts "<div>Hi</div>" ctx

// Patch signals only if missing
let signalOpts = { PatchSignalsOptions.Defaults with OnlyIfMissing = true }
do! Datastar.patchSignalsWithOptions signalOpts """{"count": 0}""" ctx

// Remove with view transition
let removeOpts = { RemoveElementOptions.Defaults with UseViewTransition = true }
do! Datastar.removeElementWithOptions removeOpts "#old-element" ctx

// Execute script without auto-remove
let scriptOpts = { ExecuteScriptOptions.Defaults with AutoRemove = false }
do! Datastar.executeScriptWithOptions scriptOpts "console.log('persistent')" ctx

// Read signals with custom JSON options
let jsonOpts = JsonSerializerOptions(PropertyNameCaseInsensitive = false)
let! signals = Datastar.tryReadSignalsWithOptions<MySignals> jsonOpts ctx
```

## Verification

1. **Build**: `dotnet build src/Frank.Datastar/Frank.Datastar.fsproj`
2. **Tests**: `dotnet test test/Frank.Datastar.Tests/`
3. **Multi-target**: Verify builds for net8.0, net9.0, net10.0
4. **Backward Compatibility**: Existing tests continue to pass unchanged
