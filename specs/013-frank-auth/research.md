# Research: Frank.Auth Resource-Level Authorization

**Feature**: 013-frank-auth
**Date**: 2026-02-05

## R1: Core Extensibility Mechanism for Endpoint Metadata

### Decision

Add a `Metadata` field to `ResourceSpec` as `(EndpointBuilder -> unit) list`. Update `ResourceSpec.Build()` to use ASP.NET Core's `RouteEndpointBuilder` internally instead of constructing `RouteEndpoint` directly. For each handler, create a `RouteEndpointBuilder`, add `HttpMethodMetadata`, apply all metadata convention functions, then call `Build()`.

### Rationale

- ASP.NET Core's `EndpointMetadataCollection` is **immutable after construction**. Metadata must be added during endpoint construction, not after.
- ASP.NET Core's idiomatic pattern is the **convention builder**: `Action<EndpointBuilder>` callbacks that mutate the `EndpointBuilder.Metadata` list before `Build()` freezes it into `EndpointMetadataCollection`. This is what `RequireAuthorization()`, `AllowAnonymous()`, `WithMetadata()`, and all other ASP.NET Core endpoint extensions use via `IEndpointConventionBuilder.Add(Action<EndpointBuilder>)`.
- Using `(EndpointBuilder -> unit) list` instead of `obj list` keeps the public API type-safe. The `obj` boundary is confined to individual convention function implementations (e.g., `fun b -> b.Metadata.Add(AuthorizeAttribute())`), never exposed in the record type. Extension libraries provide typed helper functions that callers use without seeing `obj`.
- `RouteEndpointBuilder` is ASP.NET Core's own endpoint construction path. Switching to it is a transparent internal change — the return type (`Resource` containing `Endpoint array`) and endpoint behavior are unchanged for existing resources. It also provides free benefits: automatic `IRouteDiagnosticsMetadata` and CORS preflight detection.
- The convention function approach is future-proof: any extension library can provide typed `(EndpointBuilder -> unit)` helpers without modifying Frank core's record type.

### Alternatives Considered

1. **`Metadata: obj list`**: Simpler — pass objects directly to `EndpointMetadataCollection`. Rejected because it exposes `obj` in the public API, breaking F# type safety conventions. Extension library users would have to work with untyped lists.

2. **Typed metadata DU**: E.g., `type EndpointMetadata = Authorize of string | AllowAnonymous | Custom of obj`. Rejected because every new metadata type requires modifying the DU. Extension libraries cannot add cases without forking. The `Custom of obj` escape hatch defeats the purpose.

3. **Wrapper builder**: Create a new `authResource` builder that wraps `ResourceBuilder`. Rejected because it breaks the compositional model — auth operations should be mixed freely with handler operations in the same `resource { }` CE.

4. **Post-processing `Resource.Endpoints`**: Impossible. `Endpoint` and `EndpointMetadataCollection` are immutable after construction.

## R2: Authorization Metadata Objects

### Decision

Frank.Auth translates `AuthRequirement` values into ASP.NET Core metadata objects that the authorization middleware recognizes:

| AuthRequirement | Metadata Object Added |
|---|---|
| `Authenticated` | `AuthorizeAttribute()` (implements `IAuthorizeData`, triggers default policy) |
| `Claim(type, values)` | Pre-built `AuthorizationPolicy` (via `AuthorizationPolicyBuilder().RequireClaim(type, values).Build()`) + `AuthorizeAttribute()` |
| `Role(name)` | Pre-built `AuthorizationPolicy` (via `AuthorizationPolicyBuilder().RequireRole(name).Build()`) + `AuthorizeAttribute()` |
| `Policy(name)` | `AuthorizeAttribute(name)` (resolved by `IAuthorizationPolicyProvider` at runtime) |

### Rationale

- The authorization middleware looks for `IAuthorizeData` (the `AuthorizeAttribute` interface) and `AuthorizationPolicy` objects in endpoint metadata.
- `AuthorizeAttribute()` with no arguments triggers the default policy (authenticated user).
- Named policies use `AuthorizeAttribute(policyName)` which the middleware resolves via `IAuthorizationPolicyProvider`.
- For claim and role requirements, we build an `AuthorizationPolicy` directly and attach it alongside an `AuthorizeAttribute()` as the authorization signal. The middleware combines all attached policies.

### Alternatives Considered

1. **Only use `AuthorizeAttribute`**: Insufficient — `AuthorizeAttribute` can reference named policies but can't express inline claim requirements.
2. **Custom `IAuthorizationRequirement` types**: Over-engineers it. The built-in `AuthorizationPolicyBuilder` methods (`RequireClaim`, `RequireRole`) produce the standard requirement types. No custom handlers needed.

## R3: ResourceSpec Record Extension

### Decision

Add `Metadata: (EndpointBuilder -> unit) list` to the `ResourceSpec` record in Frank core's `Builder.fs`:

```fsharp
type ResourceSpec =
    { Name : string
      Handlers : (string * RequestDelegate) list
      Metadata : (EndpointBuilder -> unit) list }
    static member Empty = { Name = Unchecked.defaultof<_>; Handlers = []; Metadata = [] }
```

### Rationale

- `(EndpointBuilder -> unit)` convention functions match ASP.NET Core's own `Action<EndpointBuilder>` pattern used by `IEndpointConventionBuilder.Add()`. The `obj` boundary is pushed down into typed helper functions.
- Empty list by default ensures backward compatibility — existing resources are unaffected.
- `Build()` creates `RouteEndpointBuilder` per handler, applies all convention functions, then calls `Build()`.
- F# record update syntax (`{ spec with Metadata = convention :: spec.Metadata }`) makes it trivial for extensions to append conventions.

### Alternatives Considered

1. **`obj list`**: Simpler storage but exposes `obj` in the public API. Rejected — breaks F# type safety conventions.
2. **Typed metadata DU**: Would need to anticipate all possible metadata types, violating the generic extensibility requirement (FR-017).
3. **`ResizeArray<obj>`**: Mutable. Rejected — violates F# record immutability conventions.
4. **`(RouteEndpointBuilder -> unit) list`**: More specific but couples the type to `RouteEndpointBuilder` rather than the abstract `EndpointBuilder`. Rejected — `EndpointBuilder` is the appropriate abstraction level.

## R4: WebHostBuilder Authorization Extensions

### Decision

Frank.Auth extends `WebHostBuilder` with three custom operations:

- `useAuthentication`: Registers authentication services and adds authentication middleware (via `plug`)
- `useAuthorization`: Registers authorization services and adds authorization middleware (via `plug`)
- `authorizationPolicy`: Registers a named policy via `AuthorizationOptions.AddPolicy()`

### Rationale

- These are thin wrappers around `IServiceCollection.AddAuthentication()`, `IServiceCollection.AddAuthorization()`, and `AuthorizationOptions.AddPolicy()`.
- Authentication and authorization middleware must be in the `plug` (post-routing) position — they need endpoint metadata which is only available after routing.
- Following the same pattern as existing `WebHostBuilder` extensions (e.g., `service`, `plug`, `configure`).

## R5: Test Strategy

### Decision

Use the same test infrastructure as Frank.Tests:

- **Framework**: Expecto with YoloDev.Expecto.TestSdk
- **Test Host**: `Microsoft.AspNetCore.TestHost` for integration tests
- **Pattern**: Create test servers with configured resources and middleware, make HTTP requests, assert status codes

Two test projects:
1. **Frank.Tests**: Add tests for the `Metadata` convention function extensibility point on `ResourceSpec` (core concern — verify that convention functions are applied during `Build()` and that `RouteEndpointBuilder` produces equivalent endpoints)
2. **Frank.Auth.Tests**: New project for authorization-specific tests (integration tests using TestHost with authentication/authorization middleware configured)

### Rationale

- Consistent with existing test patterns (MiddlewareOrderingTests.fs)
- Integration tests are more valuable than unit tests for authorization — they verify the full pipeline from request through middleware to handler
- TestHost provides in-memory HTTP client without network overhead

## R6: Project Structure

### Decision

New project `src/Frank.Auth/` with:
- `Frank.Auth.fsproj` — multi-targets net8.0/net9.0/net10.0 (matches Frank core)
- `AuthRequirement.fs` — `AuthRequirement` discriminated union
- `AuthConfig.fs` — `AuthConfig` record and module functions
- `ResourceBuilderExtensions.fs` — `ResourceBuilder` type extensions with custom operations
- `WebHostBuilderExtensions.fs` — `WebHostBuilder` type extensions for service registration
- `EndpointAuth.fs` — Translation: `AuthConfig` → `(EndpointBuilder -> unit)` convention functions

New test project `test/Frank.Auth.Tests/` with:
- `Frank.Auth.Tests.fsproj` — targets net10.0 (matches Frank.Tests)
- `AuthorizationTests.fs` — integration tests using TestHost

### Rationale

- Mirrors the Frank.Datastar project structure
- Separate source files per concern (matching the draft specification's module structure)
- Multi-targeting matches Frank core
- Inherits `src/Directory.Build.props` for consistent packaging
