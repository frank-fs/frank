# Implementation Plan: Frank.Auth Resource-Level Authorization

**Branch**: `013-frank-auth` | **Date**: 2026-02-05 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/013-frank-auth/spec.md`

## Summary

Add resource-level authorization to the Frank web framework via a new `Frank.Auth` library that provides `ResourceBuilder` custom operations (`requireAuth`, `requireClaim`, `requireRole`, `requirePolicy`) and `WebHostBuilder` extensions for authentication/authorization service registration. This requires a prerequisite change to Frank core: adding a generic `Metadata` field to `ResourceSpec` so that extension libraries can attach arbitrary endpoint metadata (such as authorization attributes) during resource construction.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core)
**Primary Dependencies**: Frank 6.5.0+ (core, modified), Microsoft.AspNetCore.Authorization (framework reference)
**Storage**: N/A (no persistence — metadata is compile-time/startup-time configuration)
**Testing**: Expecto + YoloDev.Expecto.TestSdk + Microsoft.AspNetCore.TestHost (matching Frank.Tests)
**Target Platform**: .NET 8.0/9.0/10.0 (cross-platform)
**Project Type**: F# library (NuGet package)
**Performance Goals**: Zero overhead for unprotected resources; authorization adds only the metadata convention application at startup and the metadata lookup cost already inherent in ASP.NET Core's authorization middleware
**Constraints**: Must not break existing Frank applications (the `Build()` switch to `RouteEndpointBuilder` is an internal change); must not introduce new dependencies on Frank core's .fsproj
**Scale/Scope**: ~5 source files in Frank.Auth, 1 modified file in Frank core, 1 new test project
**Version Impact**: Major version bump (6.5.0 → 7.0.0) — adding a field to `ResourceSpec` is a binary-breaking change for compiled consumers

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Pre-Research Check

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Resource-Oriented Design** | PASS | Authorization is expressed per-resource via the `resource` CE, reinforcing resource-centric thinking |
| **II. Idiomatic F#** | PASS | Uses computation expressions, discriminated unions (`AuthRequirement`), pipeline-friendly module functions (`AuthConfig.addRequirement`) |
| **III. Library, Not Framework** | PASS | Frank.Auth is a separate opt-in library. Frank core's `Metadata` field is generic infrastructure, not auth-specific. "No authentication system" — Frank.Auth provides authorization *integration* with ASP.NET Core's auth, not a custom auth system |
| **IV. ASP.NET Core Native** | PASS | Uses standard `AuthorizeAttribute`, `AuthorizationPolicy`, `AuthorizationPolicyBuilder` — no custom abstractions hiding the platform. The `(EndpointBuilder -> unit)` convention pattern mirrors ASP.NET Core's own `IEndpointConventionBuilder.Add(Action<EndpointBuilder>)`. Authorization middleware is ASP.NET Core's built-in |
| **V. Performance Parity** | PASS | Zero overhead for resources without metadata (empty list is a no-op in `Build()`). `RouteEndpointBuilder` is ASP.NET Core's own endpoint construction path — no additional overhead vs direct `RouteEndpoint` construction |

### Post-Design Check

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Resource-Oriented Design** | PASS | `requireAuth`, `requireClaim`, etc. are operations on the `resource` CE. Auth is a property of the resource, not a route decorator |
| **II. Idiomatic F#** | PASS | `AuthRequirement` DU with `[<RequireQualifiedAccess>]`, `AuthConfig` record with companion module, `[<CustomOperation>]` extensions, pipeline composition. `(EndpointBuilder -> unit)` convention functions are idiomatic F# function types |
| **III. Library, Not Framework** | PASS | Separate package, opt-in, no opinions beyond what ASP.NET Core provides. Core change is a generic `(EndpointBuilder -> unit) list` — not auth-specific |
| **IV. ASP.NET Core Native** | PASS | Directly uses `AuthorizeAttribute`, `AuthorizationPolicyBuilder`, `IAuthorizationPolicyProvider`. Convention function pattern mirrors `IEndpointConventionBuilder.Add(Action<EndpointBuilder>)`. Users' ASP.NET Core auth knowledge transfers directly |
| **V. Performance Parity** | PASS | `Metadata` default is `[]` — `Build()` produces identical endpoints for existing resources. Convention application is a one-time startup cost. `RouteEndpointBuilder` is ASP.NET Core's own construction path |

No violations. No complexity tracking needed.

## Project Structure

### Documentation (this feature)

```text
specs/013-frank-auth/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0: research findings
├── data-model.md        # Phase 1: entity definitions
├── quickstart.md        # Phase 1: developer getting started guide
├── contracts/
│   └── api-surface.md   # Phase 1: public API contract
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── Frank/
│   ├── Frank.fsproj              # Existing — no new dependencies needed
│   ├── ContentNegotiation.fs     # Existing — unchanged
│   └── Builder.fs                # MODIFIED — add Metadata field to ResourceSpec + AddMetadata static method
├── Frank.Auth/
│   ├── Frank.Auth.fsproj         # NEW — multi-target net8.0;net9.0;net10.0, references Frank.fsproj
│   ├── AuthRequirement.fs        # NEW — AuthRequirement discriminated union
│   ├── AuthConfig.fs             # NEW — AuthConfig record and module functions
│   ├── EndpointAuth.fs           # NEW — AuthConfig → metadata object translation
│   ├── ResourceBuilderExtensions.fs  # NEW — ResourceBuilder custom operations
│   └── WebHostBuilderExtensions.fs   # NEW — WebHostBuilder custom operations
├── Frank.Datastar/               # Existing — unchanged
└── Frank.Analyzers/              # Existing — unchanged

test/
├── Frank.Tests/
│   ├── Frank.Tests.fsproj        # Existing — unchanged
│   └── MiddlewareOrderingTests.fs # Existing — unchanged (add MetadataTests.fs for core extensibility)
│   └── MetadataTests.fs          # NEW — tests for ResourceSpec.Metadata extensibility
├── Frank.Auth.Tests/
│   ├── Frank.Auth.Tests.fsproj   # NEW — targets net10.0, Expecto + TestHost
│   ├── AuthorizationTests.fs     # NEW — integration tests for all auth patterns
│   └── Program.fs                # NEW — Expecto entry point
├── Frank.Datastar.Tests/         # Existing — unchanged
└── Frank.Analyzers.Tests/        # Existing — unchanged
```

**Structure Decision**: Follows the established pattern from Frank.Datastar — new library under `src/`, new test project under `test/`, both added to `Frank.sln`. The core change to `Builder.fs` is minimal (one new record field + one static method + `Build()` switches internally from `RouteEndpoint` constructor to `RouteEndpointBuilder`).

## Complexity Tracking

No violations to justify — all constitution checks pass.
