---
source: specs/012-conditional-routing
type: plan
---

# Implementation Plan: Conditional Before-Routing Middleware

**Branch**: `012-conditional-routing` | **Date**: 2026-02-04 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/012-conditional-routing/spec.md`

## Summary

Add `plugBeforeRoutingWhen` and `plugBeforeRoutingWhenNot` custom operations to the Frank `WebHostBuilder` to enable conditional middleware registration in the before-routing pipeline position. These operations mirror the existing `plugWhen`/`plugWhenNot` pattern but apply to `BeforeRoutingMiddleware` instead of `Middleware`.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting)
**Primary Dependencies**: ASP.NET Core (Microsoft.AspNetCore.*)
**Storage**: N/A
**Testing**: Expecto with Microsoft.AspNetCore.TestHost
**Target Platform**: .NET 8.0/9.0/10.0 (cross-platform server)
**Project Type**: Library
**Performance Goals**: No measurable overhead compared to existing `plugWhen`/`plugWhenNot`
**Constraints**: Must maintain backward compatibility with existing APIs
**Scale/Scope**: Small feature addition (~20 lines of implementation code)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | ✅ PASS | Middleware operations are orthogonal to resource definition; this extends existing infrastructure capability |
| II. Idiomatic F# | ✅ PASS | Custom operations in computation expressions follow established Frank patterns |
| III. Library, Not Framework | ✅ PASS | Adds optional middleware helper; no new opinions imposed |
| IV. ASP.NET Core Native | ✅ PASS | Directly wraps `IApplicationBuilder` following existing patterns |
| V. Performance Parity | ✅ PASS | Same condition evaluation pattern as existing `plugWhen`; no additional overhead |

**Gate Result**: PASS - No violations. Feature extends existing pattern to new pipeline position.

## Project Structure

### Documentation (this feature)

```text
specs/012-conditional-routing/
├── plan.md              # This file
├── research.md          # Phase 0 output (minimal - well-understood pattern)
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/Frank/
└── Builder.fs           # Add plugBeforeRoutingWhen and plugBeforeRoutingWhenNot

test/Frank.Tests/
└── MiddlewareOrderingTests.fs  # Add tests for new operations
```

**Structure Decision**: Single project structure. All changes are in existing files - no new files required.

## Complexity Tracking

No violations to justify. Feature follows established patterns exactly.

## Implementation Details

### Existing Pattern (plugWhen)

```fsharp
[<CustomOperation("plugWhen")>]
member __.PlugWhen(spec, cond, f) =
    { spec with
        Middleware = fun app ->
            if cond app then
                f(spec.Middleware(app))
            else spec.Middleware(app) }

[<CustomOperation("plugWhenNot")>]
member __.PlugWhenNot(spec, cond, f) =
    __.PlugWhen(spec, not << cond, f)
```

### New Operations (plugBeforeRoutingWhen)

```fsharp
[<CustomOperation("plugBeforeRoutingWhen")>]
member __.PlugBeforeRoutingWhen(spec, cond, f) =
    { spec with
        BeforeRoutingMiddleware = fun app ->
            if cond app then
                f(spec.BeforeRoutingMiddleware(app))
            else spec.BeforeRoutingMiddleware(app) }

[<CustomOperation("plugBeforeRoutingWhenNot")>]
member __.PlugBeforeRoutingWhenNot(spec, cond, f) =
    __.PlugBeforeRoutingWhen(spec, not << cond, f)
```

### Type Signatures

Both operations have the signature:
- `cond`: `IApplicationBuilder -> bool`
- `f`: `IApplicationBuilder -> IApplicationBuilder`

This matches the existing `plugWhen` and `plugWhenNot` signatures.

## Testing Strategy

Add tests to `test/Frank.Tests/MiddlewareOrderingTests.fs`:

1. **plugBeforeRoutingWhen executes middleware when condition is true**
2. **plugBeforeRoutingWhen skips middleware when condition is false**
3. **plugBeforeRoutingWhenNot executes middleware when condition is false**
4. **plugBeforeRoutingWhenNot skips middleware when condition is true**
5. **Multiple conditional before-routing middleware compose correctly**
6. **Conditional before-routing middleware works with regular plugBeforeRouting**

## Documentation Updates

Update `README.md` to add:
1. `plugBeforeRoutingWhen` and `plugBeforeRoutingWhenNot` to the middleware operations section
2. Example usage showing conditional HTTPS redirection

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Breaking existing code | Very Low | High | No changes to existing API signatures |
| Performance regression | Very Low | Medium | Same pattern as existing plugWhen |
| Incorrect middleware ordering | Low | Medium | Comprehensive test coverage |

## Acceptance Criteria Mapping

| Requirement | Implementation |
|-------------|----------------|
| FR-001: plugBeforeRoutingWhen operation | `WebHostBuilder.PlugBeforeRoutingWhen` method |
| FR-002/003: Execute/skip based on condition | Conditional application in BeforeRoutingMiddleware |
| FR-004: plugBeforeRoutingWhenNot operation | `WebHostBuilder.PlugBeforeRoutingWhenNot` method |
| FR-005/006: Execute/skip based on negated condition | Delegates to PlugBeforeRoutingWhen with `not << cond` |
| FR-007: Same condition signature | `IApplicationBuilder -> bool` |
| FR-008: Same middleware signature | `IApplicationBuilder -> IApplicationBuilder` |
| FR-009: Composable operations | Function composition via `>>` |
| FR-010: Works with existing operations | No changes to existing operations |
