# Implementation Plan: OPTIONS and Link Header Discovery

**Branch**: `019-options-link-discovery` | **Date**: 2026-03-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/019-options-link-discovery/spec.md`
**GitHub Issue**: #102

## Summary

Add HTTP-native discovery to Frank so that agents can learn available media types and methods via OPTIONS responses and Link headers (RFC 8288). The feature is split across three touch points:

1. **Frank core** (`src/Frank/`): Add `DiscoveryMediaType` struct to endpoint metadata vocabulary
2. **Frank.Discovery** (new package `src/Frank.Discovery/`): Two middlewares -- `OptionsDiscoveryMiddleware` (implicit OPTIONS responses) and `LinkHeaderMiddleware` (RFC 8288 Link header injection on GET/HEAD 2xx responses)
3. **Frank.LinkedData** and **Frank.Statecharts** (existing packages): Each contributes its supported media types as `DiscoveryMediaType` endpoint metadata entries during resource registration

The design follows the established endpoint metadata aggregation pattern: extensions add metadata at registration time, middleware reads it at request time. No cross-dependencies between extension packages.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core)
**Primary Dependencies**: Frank core (project reference), Microsoft.AspNetCore.App (framework reference)
**Storage**: N/A (metadata is compile-time/startup-time configuration)
**Testing**: Expecto + Microsoft.AspNetCore.TestHost (matching Frank.Auth.Tests pattern)
**Target Platform**: ASP.NET Core (cross-platform server)
**Project Type**: Multi-package library extension
**Performance Goals**: Zero overhead on resources without semantic metadata (SC-007); OPTIONS/Link responses within same time envelope as normal requests (SC-001)
**Constraints**: Must coexist with CORS middleware; explicit `options` handlers take precedence; only 2xx responses get Link headers
**Scale/Scope**: ~4 source files in new Frank.Discovery package, ~2 lines each in Frank.LinkedData and Frank.Statecharts to add media type metadata, ~1 type added to Frank core

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | Discovery is inherently resource-oriented -- per-resource metadata, not global. OPTIONS/Link headers are HTTP uniform interface semantics. |
| II. Idiomatic F# | PASS | `DiscoveryMediaType` is a struct record. Middleware registered via `WebHostBuilder` custom operations. No imperative configuration. |
| III. Library, Not Framework | PASS | Entirely opt-in. `useOptionsDiscovery` and `useLinkHeaders` are separate custom operations. No mandatory middleware. Extensions contribute media types without knowing about Frank.Discovery. |
| IV. ASP.NET Core Native | PASS | Uses endpoint metadata, `IApplicationBuilder` middleware pipeline, `HttpContext`, and standard routing infrastructure. No platform-hiding abstractions. |
| V. Performance Parity | PASS | `DiscoveryMediaType` is a struct. Middleware checks endpoint metadata -- O(1) `GetMetadata<T>()` call. No allocation on pass-through. Endpoint enumeration for OPTIONS is bounded by route count. |
| VI. Resource Disposal Discipline | PASS | No disposable resources introduced. Middleware is stateless. |
| VII. No Silent Exception Swallowing | PASS | Middleware will log via `ILogger` on any unexpected conditions. No catch-all handlers. |
| VIII. No Duplicated Logic Across Modules | PASS | `DiscoveryMediaType` is defined once in Frank core. Media type string constants are defined at their source (each extension knows its own types). |

## Project Structure

### Documentation (this feature)

```
kitty-specs/019-options-link-discovery/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (N/A -- no external API contracts)
└── tasks.md             # Phase 2 output (/spec-kitty.tasks command)
```

### Source Code (repository root)

```
src/
├── Frank/
│   ├── Builder.fs                          # MODIFY: Add DiscoveryMediaType struct type
│   └── Frank.fsproj                        # NO CHANGE
├── Frank.Discovery/                        # NEW PACKAGE
│   ├── Frank.Discovery.fsproj              # New project: net8.0;net9.0;net10.0, refs Frank
│   ├── OptionsDiscoveryMiddleware.fs       # Implicit OPTIONS response middleware
│   ├── LinkHeaderMiddleware.fs             # RFC 8288 Link header injection middleware
│   └── WebHostBuilderExtensions.fs         # useOptionsDiscovery, useLinkHeaders, useDiscovery custom ops
├── Frank.LinkedData/
│   └── ResourceBuilderExtensions.fs        # MODIFY: Add DiscoveryMediaType entries in linkedData op
└── Frank.Statecharts/
    └── ResourceBuilderExtensions.fs        # MODIFY: Add DiscoveryMediaType entries in stateMachine op

test/
└── Frank.Discovery.Tests/                  # NEW TEST PROJECT
    ├── Frank.Discovery.Tests.fsproj        # net10.0, refs Frank.Discovery + TestHost + Expecto
    ├── OptionsDiscoveryTests.fs            # Tests for US1 acceptance scenarios
    ├── LinkHeaderTests.fs                  # Tests for US2-US4 acceptance scenarios
    ├── EdgeCaseTests.fs                    # CORS coexistence, explicit handler precedence, etc.
    └── Program.fs                          # Expecto entry point

Frank.sln                                   # MODIFY: Add Frank.Discovery + Frank.Discovery.Tests
```

**Structure Decision**: New extension package `Frank.Discovery` follows the established pattern (Frank.Auth, Frank.LinkedData, Frank.Statecharts): source under `src/`, tests under `test/`, multi-target `net8.0;net9.0;net10.0` for the library, `net10.0` for tests.

## Complexity Tracking

No constitution violations to justify. The design is minimal and follows established patterns.

## Architecture Decisions

### AD-01: DiscoveryMediaType lives in Frank core

`DiscoveryMediaType` is a `[<Struct>]` record type defined in `src/Frank/Builder.fs`. This allows any extension package to contribute media types without taking a dependency on Frank.Discovery. The discovery middleware only depends on Frank core and reads these metadata entries at request time.

**Dependency graph**:
```
Frank.LinkedData ──→ Frank (core)
Frank.Statecharts ──→ Frank (core)
Frank.Discovery ──→ Frank (core)
```

No cross-dependencies between extension packages.

### AD-02: Two separate middlewares

- `OptionsDiscoveryMiddleware`: Intercepts OPTIONS requests. Enumerates all endpoints for the matched route pattern to build the `Allow` header. Collects `DiscoveryMediaType` entries from endpoint metadata. Returns 200 with empty body.
- `LinkHeaderMiddleware`: Runs after handlers. On 2xx responses for GET/HEAD requests, inspects the matched endpoint's metadata for `DiscoveryMediaType` entries and appends RFC 8288 `Link` headers.

Registered via three custom operations on `WebHostBuilder`: `useOptionsDiscovery` (OPTIONS only), `useLinkHeaders` (Link headers only), and `useDiscovery` (convenience shortcut that registers both).

### AD-03: OPTIONS endpoint enumeration strategy

Frank creates one `Endpoint` per HTTP method per resource (e.g., GET `/items` and POST `/items` are separate endpoints). The OPTIONS middleware needs to find all sibling endpoints for the same route.

Strategy: The middleware receives `EndpointDataSource` via DI. On OPTIONS request, it finds the matched endpoint (via `ctx.GetEndpoint()`), extracts its route pattern, then filters all endpoints from the data source by matching route pattern to build the complete `Allow` set and aggregate all `DiscoveryMediaType` metadata.

If `ctx.GetEndpoint()` is null (no route match), the middleware passes through.

### AD-04: Explicit OPTIONS handler precedence

If a resource defines an explicit `options` handler via the `ResourceBuilder`, ASP.NET Core routing will match that endpoint for OPTIONS requests. The `OptionsDiscoveryMiddleware` detects this by checking whether the matched endpoint has an `HttpMethodMetadata` containing "OPTIONS". If so, it defers to the explicit handler (calls `next`). If not, it generates the implicit response.

### AD-05: CORS coexistence

The `OptionsDiscoveryMiddleware` must run after CORS middleware in the pipeline. CORS middleware handles preflight requests (OPTIONS with `Origin` + `Access-Control-Request-Method` headers). The discovery middleware checks for the presence of `Access-Control-Request-Method` header -- if present, it passes through to let CORS handle it. Non-CORS OPTIONS requests are handled by the discovery middleware.

### AD-06: Per-resource Link header opt-in is implicit

No separate `DiscoveryMarker` or `discoverable` custom operation. The presence of any `DiscoveryMediaType` entries in an endpoint's metadata is sufficient to trigger Link header emission (when the `LinkHeaderMiddleware` is registered). This reduces API surface and avoids redundant configuration.
