---
source: specs/012-conditional-routing
status: complete
type: spec
---

# Feature Specification: Conditional Before-Routing Middleware

**Feature Branch**: `012-conditional-routing`
**Created**: 2026-02-04
**Status**: Draft
**Input**: User description: "add plugBeforeRoutingWhen and plugBeforeRoutingWhenNot extensions to the Frank Builder"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Conditional HTTPS Redirection (Priority: P1)

As a Frank developer, I want to apply HTTPS redirection middleware only in non-development environments, where the middleware needs to run before routing decisions are made.

**Why this priority**: This is the most common use case for conditional before-routing middleware. HTTPS redirection is critical for production security but interferes with local development workflows.

**Independent Test**: Can be tested by configuring a Frank application with conditional HTTPS redirection and verifying it applies only when the environment condition is met.

**Acceptance Scenarios**:

1. **Given** a Frank application with `plugBeforeRoutingWhen` using a production environment check and HTTPS redirection, **When** running in production, **Then** HTTPS redirection middleware executes before routing.

2. **Given** a Frank application with `plugBeforeRoutingWhen` using a production environment check and HTTPS redirection, **When** running in development, **Then** HTTPS redirection middleware does not execute.

3. **Given** a Frank application with `plugBeforeRoutingWhenNot` using a development environment check and HTTPS redirection, **When** running in development, **Then** HTTPS redirection middleware does not execute.

---

### User Story 2 - Conditional Static File Serving (Priority: P2)

As a Frank developer, I want to conditionally serve static files based on environment or configuration, where static file middleware must run before routing.

**Why this priority**: Static file serving configuration often varies between environments (e.g., CDN in production vs local files in development).

**Independent Test**: Can be tested by configuring static file middleware with a condition and verifying files are served only when the condition is met.

**Acceptance Scenarios**:

1. **Given** a Frank application with `plugBeforeRoutingWhen` for static files with a local-files-enabled condition, **When** the condition evaluates to true, **Then** static files are served from the local filesystem.

2. **Given** a Frank application with `plugBeforeRoutingWhen` for static files with a local-files-enabled condition, **When** the condition evaluates to false, **Then** static file requests fall through to routing.

---

### User Story 3 - Conditional Security Headers (Priority: P2)

As a Frank developer, I want to apply security header middleware conditionally based on environment, where headers must be added before routing.

**Why this priority**: Security headers like HSTS have different requirements in development vs production environments.

**Independent Test**: Can be tested by adding conditional security header middleware and verifying headers are present only when the condition is met.

**Acceptance Scenarios**:

1. **Given** a Frank application with `plugBeforeRoutingWhenNot` for HSTS using a development check, **When** running in production, **Then** HSTS headers are applied to responses.

2. **Given** a Frank application with `plugBeforeRoutingWhenNot` for HSTS using a development check, **When** running in development, **Then** HSTS headers are not applied.

---

### Edge Cases

- What happens when the condition function throws an exception? The exception propagates as with any middleware, following standard error handling.
- What happens when multiple conditional before-routing middleware have conflicting conditions? Each middleware is evaluated independently in registration order.
- What happens when the condition function has side effects? Side effects occur each time the condition is evaluated per request.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Frank `WebHostBuilder` MUST provide a `plugBeforeRoutingWhen` operation that accepts a condition function and middleware function.
- **FR-002**: The `plugBeforeRoutingWhen` operation MUST execute the middleware before routing only when the condition evaluates to true.
- **FR-003**: The `plugBeforeRoutingWhen` operation MUST skip the middleware when the condition evaluates to false.
- **FR-004**: The Frank `WebHostBuilder` MUST provide a `plugBeforeRoutingWhenNot` operation that accepts a condition function and middleware function.
- **FR-005**: The `plugBeforeRoutingWhenNot` operation MUST execute the middleware before routing only when the condition evaluates to false.
- **FR-006**: The `plugBeforeRoutingWhenNot` operation MUST skip the middleware when the condition evaluates to true.
- **FR-007**: Both operations MUST accept the same condition function signature as existing `plugWhen` and `plugWhenNot` operations.
- **FR-008**: Both operations MUST accept the same middleware function signature as existing `plugBeforeRouting` operation.
- **FR-009**: Multiple `plugBeforeRoutingWhen` and `plugBeforeRoutingWhenNot` operations MUST be composable and execute in registration order.
- **FR-010**: The new operations MUST work correctly when combined with existing `plugBeforeRouting`, `plug`, `plugWhen`, and `plugWhenNot` operations.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All acceptance scenarios pass automated testing.
- **SC-002**: Existing Frank applications using `plugBeforeRouting`, `plugWhen`, and `plugWhenNot` continue to work without modification.
- **SC-003**: Documentation examples demonstrate at least two real-world use cases for the new operations.
- **SC-004**: The operations follow the same naming convention and usage patterns as existing Frank middleware operations.

## Scope

**In Scope**:
- Adding `plugBeforeRoutingWhen` custom operation to `WebHostBuilder`
- Adding `plugBeforeRoutingWhenNot` custom operation to `WebHostBuilder`
- Unit tests for both new operations
- Documentation updates for the README

**Out of Scope**:
- Changing existing `plug`, `plugWhen`, `plugWhenNot`, or `plugBeforeRouting` API signatures
- Adding other conditional middleware variants
- Performance optimizations for condition evaluation

## Assumptions

- The condition function signature follows the existing pattern: `IApplicationBuilder -> bool`
- The middleware function signature follows the existing pattern: `IApplicationBuilder -> IApplicationBuilder`
- Developers understand that condition functions are evaluated per middleware registration, not per request (the condition is evaluated once during application startup when building the middleware pipeline)

## Research

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
