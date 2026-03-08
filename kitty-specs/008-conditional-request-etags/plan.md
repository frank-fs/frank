# Implementation Plan: Conditional Request ETags

**Branch**: `008-conditional-request-etags` | **Date**: 2026-03-07 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/008-conditional-request-etags/spec.md`
**Research**: [research.md](research.md) | **Data Model**: [data-model.md](data-model.md)

## Summary

Implement automatic ETag generation and conditional request handling in Frank core (`src/Frank/`). The feature adds a framework-wide opt-in middleware that computes strong ETags from resource state via an `IETagProvider` interface, caches them in a MailboxProcessor-backed store, and evaluates `If-None-Match` (304 Not Modified) and `If-Match` (412 Precondition Failed) headers per RFC 9110. A default `StatechartETagProvider` integrates with Frank.Statecharts' `IStateMachineStore` for statechart-backed resources, while custom `IETagProvider` implementations support plain resources. Resources without a provider are untouched -- no headers, no overhead.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core)
**Primary Dependencies**: ASP.NET Core built-ins only (Microsoft.AspNetCore.App framework reference). Frank.Statecharts (project reference, for `StatechartETagProvider` default implementation)
**Storage**: N/A (MailboxProcessor-backed in-memory ETag cache; no persistent storage)
**Testing**: Expecto + ASP.NET Core TestHost (matching existing Frank.Tests patterns)
**Target Platform**: .NET multi-target library (net8.0;net9.0;net10.0)
**Project Type**: Extension to existing Frank core library (`src/Frank/`)
**Performance Goals**: ETag computation < 1ms for typical state sizes; zero overhead for non-ETag-enabled resources; MailboxProcessor per-message overhead ~1-5us
**Constraints**: No external NuGet dependencies; strong ETags only (weak ETags deferred); must not alter behavior of resources without an `IETagProvider`
**Cache Invalidation Note**: `IStateMachineStore.Subscribe` is per-instance only (`instanceId: string -> observer: IObserver<'State * 'Context> -> IDisposable`) -- there is no way to subscribe to all state changes globally. Cache invalidation uses a middleware-driven approach: after the handler returns a 2xx response for a mutation (POST/PUT/DELETE), the middleware sends an `InvalidateETag` message to the cache.
**Scale/Scope**: Framework-wide middleware; all resources with an `IETagProvider` participate automatically

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Resource-Oriented Design -- PASS

Conditional requests are fundamental HTTP resource semantics (RFC 9110 Section 13). ETags represent resource state, not URL patterns. The middleware operates on resources via endpoint metadata (`ETagMetadata`), and the `IETagProvider` abstraction computes ETags from resource domain state -- not from request/response bytes. This strengthens Frank's resource-oriented model by making resources aware of their own representation identity.

### II. Idiomatic F# -- PASS

- IETagProvider uses a non-generic interface (ComputeETag: string -> Task<string option>). StatechartETagProvider<'State, 'Context> is generic at the class level.
- ETag cache uses MailboxProcessor (idiomatic F# concurrency primitive)
- Registration via computation expression custom operations on `WebHostBuilder`
- Provider interface is pipeline-friendly: `('State * 'Context) -> string`
- Endpoint metadata pattern uses discriminated unions where appropriate

### III. Library, Not Framework -- PASS

Conditional request support is entirely opt-in. The middleware is registered explicitly by the developer in the `webHost` CE. Resources without an `IETagProvider` experience zero behavioral change. No opinions imposed on how state is stored, computed, or managed -- only how it maps to ETags. Easy to adopt for one resource, easy to remove.

### IV. ASP.NET Core Native -- PASS

- Standard ASP.NET Core middleware pattern (`IMiddleware` or `RequestDelegate` pipeline)
- Uses endpoint metadata (`EndpointBuilder.Metadata`) for resource discovery -- same pattern as Frank.Auth and Frank.LinkedData
- HTTP status codes (304, 412) follow ASP.NET Core response conventions
- `ETag` header set via `HttpResponse.Headers` -- no custom abstractions over response headers
- Integrates with ASP.NET Core DI (`IServiceCollection`) for provider registration

### V. Performance Parity -- PASS

- Non-ETag resources: single metadata check per request (presence of `ETagMetadata`), then pass-through. Negligible cost.
- ETag-enabled resources: one MailboxProcessor message (~1-5us) to retrieve cached ETag, plus SHA-256 hash on cache miss (< 1ms for typical state sizes)
- No allocations in the hot path beyond the ETag string itself
- MailboxProcessor serializes access, preventing lock contention under load

### VI. Resource Disposal Discipline -- PASS

- `MailboxProcessor` implements `IDisposable`; the `ETagCache` wraps it and itself implements `IDisposable`, registered as a singleton in DI (disposed on application shutdown)
- No `StreamReader`/`StreamWriter` usage in this feature
- SHA-256 `HashAlgorithm` instances created via `using` bindings for each hash computation (or pooled)

### VII. No Silent Exception Swallowing -- PASS

- If `IETagProvider.ComputeETag` throws, the exception propagates to the ASP.NET Core error handling pipeline -- no catch-and-ignore
- MailboxProcessor errors surface via the `Error` event and propagate to callers via the reply channel
- Cache miss (provider returns error) is logged via `ILogger`, not silently swallowed

### VIII. No Duplicated Logic -- PASS

- ETag comparison logic (strong comparison per RFC 9110) is defined once in a shared `ETagComparison` module, used by both `If-None-Match` and `If-Match` evaluation
- ETag formatting (quoting, escaping) in a single `ETagFormat` helper
- Endpoint metadata discovery pattern is consistent with existing Frank extensions (no per-module reimplementation)

## Project Structure

### Documentation (this feature)

```
kitty-specs/008-conditional-request-etags/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Hashing strategy, RFC compliance, cache design decisions
├── data-model.md        # Entity definitions and F# type signatures
├── quickstart.md        # Developer quickstart with usage examples
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── research/
    ├── evidence-log.csv
    └── source-register.csv
```

### Source Code (repository root)

```
src/
├── Frank/                               # Existing core library (MODIFIED)
│   ├── Frank.fsproj                     # No changes needed (all deps are framework refs)
│   ├── Builder.fs                       # Existing (ResourceBuilder, WebHostBuilder)
│   ├── ContentNegotiation.fs            # Existing (unchanged)
│   ├── ETag.fs                          # NEW: IETagProvider, ETagFormat, ETagComparison
│   ├── ETagCache.fs                     # NEW: MailboxProcessor-backed ETag cache
│   └── ConditionalRequestMiddleware.fs  # NEW: If-None-Match / If-Match evaluation
│
└── Frank.Statecharts/                   # Existing statecharts library (MODIFIED)
    └── StatechartETagProvider.fs         # NEW: Default IETagProvider for statechart resources

test/
└── Frank.Tests/                         # Existing test project (EXTENDED)
    ├── Frank.Tests.fsproj               # Add new test module references
    ├── ETagTests.fs                     # NEW: ETag computation and formatting tests
    ├── ConditionalRequestTests.fs       # NEW: 304/412 integration tests via TestHost
    └── ETagCacheTests.fs                # NEW: MailboxProcessor cache tests
```

**Structure Decision**: New files added to existing Frank core library and Frank.Tests project. No new projects created. The `StatechartETagProvider` lives in `Frank.Statecharts` since it depends on `IStateMachineStore`; the core ETag types and middleware live in `Frank` since conditional requests are fundamental HTTP semantics. This follows the same cross-project pattern as Frank.Auth metadata living in core but auth-specific providers living in Frank.Auth.

## Parallel Work Analysis

The following work streams are independent and can proceed in parallel:

### Stream A: Core ETag Types and Interface (ETag.fs)
- `IETagProvider` interface definition. IETagProvider signature: ComputeETag: string -> Task<string option>
- `ETagMetadata` endpoint metadata marker
- `ETagFormat` module (quoting, strong ETag formatting per RFC 9110)
- `ETagComparison` module (strong comparison, wildcard matching)
- **No dependencies** on other new files

Note: The `('State * 'Context) -> string` hashing pipeline refers to the concrete `StatechartETagProvider<'State, 'Context>` implementation, not the `IETagProvider` interface itself. The non-generic `IETagProvider` accepts a resource instance key string and returns an ETag.

### Stream B: ETag Cache (ETagCache.fs)
- `ETagCacheMessage` discriminated union (message types for MailboxProcessor)
- `ETagCache` type wrapping MailboxProcessor
- Cache lookup, insert, invalidate, eviction logic
- **Depends on**: Stream A (uses ETag string type, but can use `string` initially)

### Stream C: Conditional Request Middleware (ConditionalRequestMiddleware.fs)
- `ConditionalRequestMiddleware` implementation
- `If-None-Match` evaluation (GET -> 304)
- `If-Match` evaluation (POST/PUT/DELETE -> 412)
- `WebHostBuilder` extension for `useConditionalRequests` registration
- **Depends on**: Stream A (ETagMetadata, ETagComparison) and Stream B (ETagCache)

### Stream D: Statechart Integration (StatechartETagProvider.fs)
- `StatechartETagProvider<'State, 'Context>` implementation
- SHA-256 hashing of `('State * 'Context)` pair
- Integration with `IStateMachineStore` for state retrieval
- Cache invalidation via middleware (after 2xx mutation response)
- **Depends on**: Stream A (IETagProvider interface) and Frank.Statecharts types

### Stream E: Tests
- Unit tests for ETag formatting and comparison (Stream A dependency only)
- Cache tests (Stream B dependency)
- Integration tests via TestHost (Streams A + B + C)
- Statechart integration tests (all streams)
- **Depends on**: Streams A-D progressively

**Recommended execution order**: A -> B and D in parallel -> C -> E (progressive)

## Design Decisions

### DD-01: ETag Middleware as Frank Core, Not Separate Project

Conditional requests are fundamental HTTP semantics (RFC 9110 Section 13), not a domain-specific extension. Placing the middleware in `src/Frank/` ensures all Frank users can opt in without adding a dependency. The `StatechartETagProvider` default lives in `Frank.Statecharts` since it couples to `IStateMachineStore`.

### DD-02: Endpoint Metadata Pattern for Resource Discovery

Following the established Frank pattern (LinkedData, Auth, Statecharts), the middleware discovers ETag-enabled resources via `ETagMetadata` on endpoint metadata. This avoids URL pattern matching and respects the resource-oriented design. Resources without `ETagMetadata` are skipped with zero overhead.

### DD-03: MailboxProcessor Cache Over ConcurrentDictionary

The MailboxProcessor pattern is consistent with Frank.Statecharts' `MailboxProcessorStore` and provides serialized access that prevents race conditions between ETag reads and state transitions. A `ConcurrentDictionary` would require additional locking for atomic read-compute-write cycles.

### DD-04: Strong ETags Only (RFC 9110 Section 8.8.3)

Initial implementation produces strong ETags (`"<value>"`) only. Weak ETags (`W/"<value>"`) are deferred as a future enhancement. Strong ETags guarantee byte-for-byte equivalence, which is appropriate for state-derived hashing where identical state always produces identical representations.

## Complexity Tracking

No constitution violations requiring justification. All new code lives in existing projects following established patterns.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Middleware ordering conflicts with Statecharts/Auth middleware | Medium | Document recommended ordering; the ETag middleware should run after Auth but before Statecharts state interception |
| Hash determinism across .NET versions | Low | Use SHA-256 (deterministic by definition) rather than `HashCode.Combine` (runtime-seeded) |
| Race between ETag check and state transition | Low | MailboxProcessor serializes cache access; inherent to optimistic concurrency -- 412 on next attempt |
| `IETagProvider` generic constraints leak to resource builders | Low | Use boxed/erased interface at metadata level (same pattern as `StateMachineMetadata.Machine: obj`) |
| Cache memory growth for long-running applications | Low | Bounded cache with LRU eviction; configurable max entries |
