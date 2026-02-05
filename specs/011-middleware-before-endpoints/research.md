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
