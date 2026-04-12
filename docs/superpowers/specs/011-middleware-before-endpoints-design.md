---
source: specs/011-middleware-before-endpoints
status: complete
type: spec
---

# Feature Specification: Middleware Before Endpoints

**Feature Branch**: `011-middleware-before-endpoints`
**Created**: 2026-02-04
**Status**: Draft
**Input**: User description: "Builder.fs should apply middleware before calling UseEndpoints"

**Scope Update**: Added `plugBeforeRouting` operation for pre-routing middleware (HttpsRedirection, StaticFiles, ResponseCompression, etc.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Middleware Executes Before Endpoint Routing (Priority: P1)

As a Frank developer, I want middleware registered via the `plug` operation to execute before the endpoint routing occurs, so that my middleware can intercept requests, modify the HttpContext, and perform cross-cutting concerns (authentication, logging, CORS, etc.) before the request reaches route handlers.

**Why this priority**: This is the core bug fix. Without proper middleware ordering, middleware cannot function correctly in the ASP.NET Core pipeline. This affects any Frank application that relies on middleware.

**Independent Test**: Can be fully tested by creating a Frank web application with middleware that sets a header, and verifying the header is present when endpoints execute.

**Acceptance Scenarios**:

1. **Given** a Frank application with middleware configured via `plug`, **When** a request is made to any endpoint, **Then** the middleware executes before the endpoint handler.

2. **Given** middleware that modifies the HttpContext (e.g., adds headers, sets user context), **When** an endpoint handler runs, **Then** it has access to the modifications made by middleware.

3. **Given** middleware that short-circuits the request (e.g., returns early), **When** the middleware returns without calling next, **Then** the endpoint handler is not invoked.

---

### User Story 2 - Existing Middleware Patterns Work Correctly (Priority: P1)

As a Frank developer using standard ASP.NET Core middleware (authentication, CORS, static files, etc.), I want these middleware components to work correctly when registered via the `plug` operation.

**Why this priority**: Equal priority to Story 1 because this is the same fix from the perspective of standard middleware usage.

**Independent Test**: Can be tested by configuring common middleware (e.g., UseStaticFiles, UseAuthentication) and verifying they function as expected.

**Acceptance Scenarios**:

1. **Given** a Frank application with `UseAuthentication` middleware, **When** a request is made to a protected endpoint, **Then** authentication occurs before the endpoint handler executes.

2. **Given** a Frank application with CORS middleware, **When** a preflight request is made, **Then** the CORS middleware handles it before endpoint routing.

---

### User Story 3 - Conditional Middleware Works Correctly (Priority: P2)

As a Frank developer using `plugWhen` or `plugWhenNot` operations, I want conditional middleware to be evaluated and applied before endpoint routing.

**Why this priority**: This is a secondary concern that builds on the core fix, but affects users who use conditional middleware registration.

**Independent Test**: Can be tested by using `plugWhen` with a condition and verifying the middleware runs when the condition is met.

**Acceptance Scenarios**:

1. **Given** a Frank application with conditional middleware via `plugWhen`, **When** the condition evaluates to true, **Then** the middleware executes before endpoints.

2. **Given** a Frank application with conditional middleware via `plugWhenNot`, **When** the condition evaluates to false, **Then** the middleware executes before endpoints.

---

### User Story 4 - Pre-Routing Middleware via plugBeforeRouting (Priority: P1)

As a Frank developer, I want to register middleware that executes before `UseRouting()` via a `plugBeforeRouting` operation, so that I can correctly position middleware like HttpsRedirection, StaticFiles, ResponseCompression, and ResponseCaching that should run before routing.

**Why this priority**: Critical for correct ASP.NET Core middleware ordering. Some middleware (HttpsRedirection, StaticFiles, compression) should execute before routing for correctness and performance.

**Independent Test**: Can be tested by configuring `plugBeforeRouting` with StaticFiles middleware and verifying static files are served without routing overhead.

**Acceptance Scenarios**:

1. **Given** a Frank application with `plugBeforeRouting` configured with HttpsRedirection, **When** an HTTP request is made, **Then** the redirect occurs before routing is evaluated.

2. **Given** a Frank application with `plugBeforeRouting` configured with StaticFiles, **When** a request for a static file is made, **Then** the file is served without invoking endpoint routing.

3. **Given** a Frank application with both `plugBeforeRouting` and `plug` middleware, **When** a request is made, **Then** the execution order is: plugBeforeRouting → UseRouting → plug → UseEndpoints.

---

### Edge Cases

- What happens when no middleware is registered? The application should still work normally with the default `id` function.
- How does the system handle middleware that throws exceptions? Standard ASP.NET Core exception handling behavior should apply.
- What happens when multiple middleware are registered? They should execute in registration order, all before endpoints.
- What happens when only `plugBeforeRouting` is used without `plug`? The pipeline should work: plugBeforeRouting → UseRouting → UseEndpoints.
- What happens when only `plug` is used without `plugBeforeRouting`? The pipeline should work: UseRouting → plug → UseEndpoints.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Frank `WebHostBuilder` MUST apply middleware registered via `plug` after `UseRouting()` but before `UseEndpoints()`.
- **FR-002**: The Frank `WebHostBuilder` MUST maintain the correct ASP.NET Core pipeline order: early middleware → `UseRouting()` → middleware → `UseEndpoints()`.
- **FR-003**: Middleware registered via `plug` MUST be able to modify the request context before endpoints execute.
- **FR-004**: Middleware registered via `plug` MUST be able to short-circuit the pipeline (not call the next delegate).
- **FR-005**: The `plugWhen` and `plugWhenNot` operations MUST evaluate conditions and apply middleware before endpoints.
- **FR-006**: When no middleware is registered (default `id` function), the pipeline MUST still function correctly.
- **FR-007**: The Frank `WebHostBuilder` MUST provide a `plugBeforeRouting` operation for middleware that executes before `UseRouting()`.
- **FR-008**: Middleware registered via `plugBeforeRouting` MUST execute before `UseRouting()` in the pipeline.
- **FR-009**: The `WebHostSpec` record MUST include a `BeforeRoutingMiddleware` field (default `id` function).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All middleware registered via `plug` executes after `UseRouting()` but before any endpoint handler.
- **SC-002**: Existing Frank sample applications and tests continue to pass after the change.
- **SC-003**: Standard ASP.NET Core middleware patterns (authentication, CORS) work correctly when configured via Frank's `plug` operation.
- **SC-004**: The fix aligns with ASP.NET Core's documented middleware pipeline ordering requirements.
- **SC-005**: Middleware registered via `plugBeforeRouting` executes before `UseRouting()`.
- **SC-006**: Pre-routing middleware (HttpsRedirection, StaticFiles, ResponseCompression) works correctly via `plugBeforeRouting`.

## Assumptions

- The `IApplicationBuilder.UseRouting()` must be called before any endpoint-aware middleware.
- The `IApplicationBuilder.UseEndpoints()` must be called after all request-processing middleware.
- The current behavior where middleware is applied after `UseEndpoints()` is a bug, not intended behavior.
- Some middleware (HttpsRedirection, StaticFiles, compression) should execute before routing per ASP.NET Core best practices.

## Scope Boundaries

**In Scope**:
- Fixing the middleware ordering in `WebHostBuilder.Run` method in `Builder.fs`
- Adding `BeforeRoutingMiddleware` field to `WebHostSpec` record
- Adding `plugBeforeRouting` custom operation to `WebHostBuilder`
- Updating README documentation with new `plugBeforeRouting` operation
- Incrementing minor version in `src/Directory.Build.props` (reset patch to 0)
- Verifying the fix with existing tests

**Out of Scope**:
- Adding `plugBeforeRoutingWhen` or similar conditional variants (can be added later if needed)
- Changing existing `plug`, `plugWhen`, or `plugWhenNot` API signatures

## Test Cases

### Authentication Middleware Test

A simple authentication test case to verify `plug` middleware (authentication) executes correctly after routing but before endpoints:

1. Create a test sample that uses basic authentication middleware via `plug`
2. Verify that unauthenticated requests to a protected endpoint return 401
3. Verify that authenticated requests reach the endpoint handler

## Research

# Research: Middleware Before Endpoints

**Feature**: 011-middleware-before-endpoints
**Date**: 2026-02-04

## Summary

Bug fix with API enhancement. ASP.NET Core middleware has two distinct positions: before routing and after routing. Frank now supports both via `plugBeforeRouting` and `plug`.

## Decision: Two-Stage Middleware Pipeline

**Decision**: Provide two middleware hooks:
1. `plugBeforeRouting` - Middleware before `UseRouting()` (HttpsRedirection, StaticFiles, compression)
2. `plug` - Middleware between `UseRouting()` and `UseEndpoints()` (Authentication, Authorization, CORS)

**Rationale**: ASP.NET Core's recommended middleware order distinguishes pre-routing and post-routing middleware:

```
Pre-routing (plugBeforeRouting):
  1. ExceptionHandler
  2. HSTS
  3. HttpsRedirection
  4. StaticFiles
  5. ResponseCompression
  6. ResponseCaching

UseRouting()

Post-routing (plug):
  7. CORS (endpoint-aware)
  8. Authentication
  9. Authorization
  10. Custom middleware

UseEndpoints()
```

**Alternatives Considered**:

| Alternative | Why Rejected |
|-------------|--------------|
| Single `plug` after UseRouting only | Doesn't support pre-routing middleware correctly |
| Single `plug` before UseRouting only | Breaks endpoint-aware middleware (auth, CORS) |
| Automatic middleware sorting | Over-engineered; users know their middleware needs |

## Verification of ASP.NET Core Documentation

The official Microsoft documentation states:

> The order that middleware components are added in the `Startup.Configure` method defines the order in which the middleware components are invoked on requests and the reverse order for the response.

And specifically for endpoint routing:

> `UseRouting` adds route matching to the middleware pipeline.
> `UseEndpoints` adds endpoint execution to the middleware pipeline.
> Middleware between `UseRouting` and `UseEndpoints` can see which endpoint was matched.

Source: [ASP.NET Core Middleware](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/middleware/)

## Impact Analysis

### Files Affected

| File | Change Type | Description |
|------|-------------|-------------|
| `src/Frank/Builder.fs` | Modify | Add `BeforeRoutingMiddleware` field, `plugBeforeRouting` operation, fix pipeline |
| `src/Directory.Build.props` | Modify | Increment minor version, reset patch |
| `README.md` | Modify | Document `plugBeforeRouting` and middleware ordering |
| `test/Frank.Tests/` (new) | Add | Authentication middleware test |

### Sample Applications Using `plug`

All these samples will serve as integration tests. Note which middleware should ideally use `plugBeforeRouting`:

| Sample | Middleware | Recommended Hook |
|--------|------------|------------------|
| Sample | HttpsRedirection | `plugBeforeRouting` |
| Sample | ResponseCaching | `plugBeforeRouting` |
| Sample | ResponseCompression | `plugBeforeRouting` |
| Sample | StaticFiles | `plugBeforeRouting` |
| Frank.Falco | HttpsRedirection | `plugBeforeRouting` |
| Frank.Falco | StaticFiles | `plugBeforeRouting` |
| Frank.Giraffe | HttpsRedirection | `plugBeforeRouting` |
| Frank.Giraffe | StaticFiles | `plugBeforeRouting` |
| Frank.Oxpecker | HttpsRedirection | `plugBeforeRouting` |
| Frank.Datastar.Basic | DefaultFiles, StaticFiles | `plugBeforeRouting` |
| Frank.Datastar.Hox | DefaultFiles, StaticFiles | `plugBeforeRouting` |
| Frank.Datastar.Oxpecker | DefaultFiles, StaticFiles | `plugBeforeRouting` |

**Note**: Existing samples using `plug` will continue to work (middleware still executes), but users can optionally migrate to `plugBeforeRouting` for optimal performance.

### No NEEDS CLARIFICATION Items

All technical details are clear from ASP.NET Core documentation and the existing codebase.
