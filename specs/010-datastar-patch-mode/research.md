# Research: Add WithOptions Variants for Datastar Helpers

**Branch**: `010-datastar-patch-mode` | **Date**: 2026-02-03

## Summary

No significant unknowns required research. The implementation is straightforward:
- All required types and overloads exist in `StarFederation.Datastar.FSharp`
- The pattern follows existing code conventions

## Dependency Verification

### StarFederation.Datastar.FSharp API

Verified by reading `/Users/ryanr/Code/datastar-dotnet/src/fsharp/`:

| Type/Method | File | Status |
|-------------|------|--------|
| `PatchElementsOptions` | Types.fs | ✓ Available with `Defaults` |
| `PatchSignalsOptions` | Types.fs | ✓ Available with `Defaults` |
| `RemoveElementOptions` | Types.fs | ✓ Available with `Defaults` |
| `ExecuteScriptOptions` | Types.fs | ✓ Available with `Defaults` |
| `ElementPatchMode` | Consts.fs | ✓ Available (8 variants) |
| `PatchElementNamespace` | Consts.fs | ✓ Available (3 variants) |
| `PatchElementsAsync(response, html, options)` | ServerSentEventGenerator.fs | ✓ Overload exists |
| `PatchSignalsAsync(response, signals, options)` | ServerSentEventGenerator.fs | ✓ Overload exists |
| `RemoveElementAsync(response, selector, options)` | ServerSentEventGenerator.fs | ✓ Overload exists |
| `ExecuteScriptAsync(response, script, options)` | ServerSentEventGenerator.fs | ✓ Overload exists |
| `ReadSignalsAsync<'T>(request, jsonOptions)` | ServerSentEventGenerator.fs | ✓ Overload exists |

## Design Decision

**Decision**: Use Approach B - Simple helpers + WithOptions variants

**Rationale**:
- Avoids combinatorial explosion of permutation functions
- Simple case stays simple (`patchElements`)
- Full power available when needed (`patchElementsWithOptions`)
- Consistent with F# idioms (`{ Defaults with ... }` syntax)

**Alternatives Considered**:
- Individual functions per option (rejected - API bloat)
- Options-only functions (rejected - verbose for common case)
- Drop helpers entirely (rejected - loses HttpContext convenience)

## No Further Research Required

All implementation details are clear from the existing codebase and StarFederation.Datastar.FSharp library.
