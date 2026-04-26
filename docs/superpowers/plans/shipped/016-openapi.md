---
source: specs/016-openapi
type: plan
---

# Implementation Plan: OpenAPI Document Generation Support

**Branch**: `016-openapi` | **Date**: 2026-02-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/016-openapi/spec.md`

## Summary

Add a new `Frank.OpenApi` extension library that enables Frank applications to serve OpenAPI documents via ASP.NET Core's built-in `AddOpenApi`/`MapOpenApi` infrastructure. The library provides:

1. A `HandlerBuilder` computation expression for defining handlers with embedded OpenAPI metadata (operation name, description, tags, produces, accepts)
2. `ResourceBuilder` type extensions that accept `HandlerDefinition` values alongside plain handler functions
3. `WebHostBuilder` type extensions (`useOpenApi`) for one-line OpenAPI setup
4. Integration with `FSharp.Data.JsonSchema.OpenApi` for accurate F# type schema generation

The approach uses endpoint metadata — attaching `ProducesResponseTypeMetadata`, `AcceptsMetadata`, `EndpointNameMetadata`, `TagsMetadata`, and `EndpointDescriptionMetadata` to `EndpointBuilder` during resource registration — so ASP.NET Core's built-in `EndpointMetadataApiDescriptionProvider` discovers Frank endpoints without a custom `IApiDescriptionProvider`.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 9.0 and .NET 10.0 (multi-targeting)
**Primary Dependencies**: Frank 7.1.0 (project reference), FSharp.Data.JsonSchema.OpenApi 3.0.0 (NuGet), Microsoft.AspNetCore.OpenApi (9.0.x / 10.0.x conditional), Microsoft.AspNetCore.App (framework reference)
**Storage**: N/A (no persistence — metadata is compile-time/startup-time configuration)
**Testing**: Expecto + Microsoft.AspNetCore.TestHost (matching existing Frank test patterns)
**Target Platform**: ASP.NET Core on .NET 9.0+ (Linux, macOS, Windows)
**Project Type**: Extension library (follows Frank.Auth / Frank.Datastar pattern)
**Performance Goals**: OpenAPI document generation in <500ms for 50 endpoints on first request
**Constraints**: Must not modify core Frank library; net9.0 and net10.0 only (Microsoft.AspNetCore.OpenApi is not available on net8.0)
**Scale/Scope**: Single extension library (~5 source files) + test project

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Resource-Oriented Design

- **Status**: PASS
- Frank.OpenApi enriches the existing `resource` CE with metadata; it does not introduce route-centric APIs
- The `HandlerBuilder` produces values consumed by `resource` operations — it reinforces, not replaces, resource-oriented thinking
- Constitution notes "Hypermedia enables evolvability; static specifications (OpenAPI) create coupling" — Frank.OpenApi is opt-in and lives in a separate library, not core Frank. Developers who prefer hypermedia-only are unaffected

### II. Idiomatic F#

- **Status**: PASS
- `HandlerBuilder` is a computation expression (idiomatic F# pattern)
- `HandlerDefinition` is a record type
- Type extensions use `[<AutoOpen>]` modules (consistent with Frank.Auth, Frank.Datastar)
- Pipeline-friendly: handler definitions are values that compose naturally

### III. Library, Not Framework

- **Status**: PASS
- Frank.OpenApi is a separate NuGet package — not bundled into core Frank
- No opinions beyond OpenAPI metadata attachment
- Easy to adopt incrementally (add package reference + `useOpenApi`)

### IV. ASP.NET Core Native

- **Status**: PASS
- Uses ASP.NET Core's built-in `AddOpenApi`/`MapOpenApi` directly
- Attaches standard ASP.NET Core metadata types (`ProducesResponseTypeMetadata`, `AcceptsMetadata`, etc.)
- Does not create abstractions that hide the platform — `useOpenApi` accepts `OpenApiOptions -> unit` for full customization
- Schema transformation uses the standard `IOpenApiSchemaTransformer` interface

### V. Performance Parity

- **Status**: PASS (monitoring required)
- Metadata attachment is startup-time only (no hot-path impact)
- Schema generation is lazy (on first OpenAPI document request)
- `HandlerDefinition` uses the same `RequestDelegate` under the hood — no runtime overhead for request handling

### Post-Design Re-check

- All gates still pass after Phase 1 design
- The `HandlerBuilder` uses standard F# CE patterns and produces values consumed by standard ASP.NET Core metadata APIs
- No abstractions hide the platform; OpenApiOptions callback provides escape hatch

## Project Structure

### Documentation (this feature)

```text
specs/016-openapi/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api-surface.md   # F# API surface definition
└── tasks.md             # Phase 2 output (/speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── Frank/                        # Core library (UNCHANGED)
│   ├── Frank.fsproj
│   ├── ContentNegotiation.fs
│   └── Builder.fs
├── Frank.Auth/                   # Existing extension (UNCHANGED)
├── Frank.Datastar/               # Existing extension (UNCHANGED)
└── Frank.OpenApi/                # NEW extension library
    ├── Frank.OpenApi.fsproj      # net9.0;net10.0, refs Frank + FSharp.Data.JsonSchema.OpenApi
    ├── HandlerDefinition.fs      # HandlerDefinition record type
    ├── HandlerBuilder.fs         # HandlerBuilder CE + `handler` instance
    ├── ResourceBuilderExtensions.fs  # Type extensions: get/post/put/delete/patch overloads
    └── WebHostBuilderExtensions.fs   # Type extension: useOpenApi

test/
├── Frank.Tests/                  # Existing (UNCHANGED)
├── Frank.Auth.Tests/             # Existing (UNCHANGED)
├── Frank.Datastar.Tests/         # Existing (UNCHANGED)
└── Frank.OpenApi.Tests/          # NEW test project
    ├── Frank.OpenApi.Tests.fsproj  # net10.0, Expecto + TestHost
    ├── HandlerBuilderTests.fs    # Unit tests for HandlerBuilder CE
    ├── MetadataTests.fs          # Integration tests for endpoint metadata
    ├── OpenApiDocumentTests.fs   # End-to-end tests for generated OpenAPI document
    └── Program.fs                # Expecto test runner entry point
```

**Structure Decision**: Follows the established Frank extension library pattern (Frank.Auth, Frank.Datastar). Single new project under `src/` with a corresponding test project under `test/`. The library targets net9.0 and net10.0 (not net8.0, because `Microsoft.AspNetCore.OpenApi` is only available starting .NET 9).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| net9.0/net10.0 only (not net8.0) | Microsoft.AspNetCore.OpenApi requires .NET 9+ | Cannot target net8.0 without re-implementing OpenAPI document generation from scratch |
| Conditional package references | Microsoft.AspNetCore.OpenApi 9.x vs 10.x have different APIs (Microsoft.OpenApi v1 vs v2) | Single version would limit to one TFM; conditional references match FSharp.Data.JsonSchema.OpenApi pattern |
