# Research: Conditional Before-Routing Middleware

**Feature**: 012-conditional-routing
**Date**: 2026-02-04

## Summary

This feature extends an existing, well-established pattern in Frank. No significant research was required as the implementation directly mirrors existing code.

## Existing Pattern Analysis

### Decision: Mirror existing `plugWhen`/`plugWhenNot` pattern

**Rationale**: The existing conditional middleware pattern (`plugWhen`, `plugWhenNot`) is proven and well-understood. Applying the same pattern to `BeforeRoutingMiddleware` ensures consistency and minimizes learning curve for existing Frank users.

**Alternatives considered**:
1. **Different condition signature** - Rejected: Would create API inconsistency
2. **Lazy evaluation** - Rejected: Current eager evaluation at startup matches existing pattern and ASP.NET Core conventions
3. **Fluent API instead of computation expression** - Rejected: Would violate Constitution Principle II (Idiomatic F#)

## Codebase Analysis

### Existing Implementation (Builder.fs:275-285)

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

### WebHostSpec Record (Builder.fs:218-224)

```fsharp
type WebHostSpec =
    { Host: (IWebHostBuilder -> IWebHostBuilder)
      BeforeRoutingMiddleware: (IApplicationBuilder -> IApplicationBuilder)
      Middleware: (IApplicationBuilder -> IApplicationBuilder)
      Endpoints: Endpoint[]
      Services: (IServiceCollection -> IServiceCollection)
      UseDefaults: bool }
```

**Finding**: The `BeforeRoutingMiddleware` field already exists and follows the same `IApplicationBuilder -> IApplicationBuilder` signature as `Middleware`. The new operations simply need to target this field instead.

## ASP.NET Core Middleware Pipeline

The Frank pipeline executes as:

```
Request → BeforeRoutingMiddleware → UseRouting → Middleware → UseEndpoints → Response
```

Conditional middleware in `BeforeRoutingMiddleware` will be evaluated and applied (or skipped) before routing decisions are made, exactly as specified in the feature requirements.

## Testing Approach

### Decision: Extend existing test file

**Rationale**: `MiddlewareOrderingTests.fs` already contains the test infrastructure for middleware ordering verification using `Microsoft.AspNetCore.TestHost`. Adding new tests here maintains test organization and reuses existing helpers.

**Alternatives considered**:
1. **New test file** - Rejected: Would duplicate test infrastructure
2. **Integration tests only** - Rejected: Unit-level tests using TestHost are sufficient and faster

## Documentation Approach

### Decision: Update existing README sections

**Rationale**: The README already has a "Middleware Execution" section explaining `plugBeforeRouting` and a "Conditional Middleware" section explaining `plugWhen`/`plugWhenNot`. The new operations should be documented in context with these existing sections.

## Conclusion

No unknowns remain. The implementation is a straightforward extension of existing, well-tested patterns.
