# API Surface: Frank.Auth

**Feature**: 013-frank-auth
**Date**: 2026-02-05

This document defines the public API contract for Frank.Auth and the required Frank core changes.

## Frank Core Changes (Builder.fs)

### ResourceSpec — Modified Record

```fsharp
type ResourceSpec =
    { Name : string
      Handlers : (string * RequestDelegate) list
      Metadata : (EndpointBuilder -> unit) list }
    static member Empty = { Name = Unchecked.defaultof<_>; Handlers = []; Metadata = [] }
    member spec.Build(routeTemplate) : Resource
```

**Contract**: `Build()` uses `RouteEndpointBuilder` internally (instead of constructing `RouteEndpoint` directly). For each handler, it creates a `RouteEndpointBuilder`, adds `HttpMethodMetadata`, applies all functions from `Metadata` to the builder, then calls `Build()`. This is an internal change — the return type and endpoint behavior are unchanged for existing resources.

### ResourceBuilder — New Static Method

```fsharp
type ResourceBuilder with
    static member AddMetadata(spec: ResourceSpec, convention: EndpointBuilder -> unit) : ResourceSpec
```

**Contract**: Appends a single metadata convention function to `spec.Metadata`. Analogous to `AddHandler` for handlers. The convention function receives an `EndpointBuilder` and adds metadata objects to its `Metadata` collection.

---

## Frank.Auth — Namespace: `Frank.Auth`

### AuthRequirement (AuthRequirement.fs)

```fsharp
namespace Frank.Auth

[<RequireQualifiedAccess>]
type AuthRequirement =
    | Authenticated
    | Claim of claimType: string * claimValues: string list
    | Policy of name: string
    | Role of name: string
```

### AuthConfig (AuthConfig.fs)

```fsharp
namespace Frank.Auth

type AuthConfig = { Requirements: AuthRequirement list }

module AuthConfig =
    val empty : AuthConfig
    val addRequirement : AuthRequirement -> AuthConfig -> AuthConfig
    val isEmpty : AuthConfig -> bool
```

### EndpointAuth (EndpointAuth.fs)

```fsharp
namespace Frank.Auth

open Frank.Builder

module EndpointAuth =
    /// Translate an AuthConfig into (EndpointBuilder -> unit) convention functions and append them to a ResourceSpec.
    val applyAuth : AuthConfig -> ResourceSpec -> ResourceSpec
```

### ResourceBuilder Extensions (ResourceBuilderExtensions.fs)

```fsharp
namespace Frank.Auth

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =

    type ResourceBuilder with

        [<CustomOperation("requireAuth")>]
        member _.RequireAuth(spec: ResourceSpec) : ResourceSpec

        [<CustomOperation("requireClaim")>]
        member _.RequireClaim(spec: ResourceSpec, claimType: string, claimValue: string) : ResourceSpec

        [<CustomOperation("requireClaim")>]
        member _.RequireClaim(spec: ResourceSpec, claimType: string, claimValues: string list) : ResourceSpec

        [<CustomOperation("requireRole")>]
        member _.RequireRole(spec: ResourceSpec, role: string) : ResourceSpec

        [<CustomOperation("requirePolicy")>]
        member _.RequirePolicy(spec: ResourceSpec, policyName: string) : ResourceSpec
```

**Contract**: Each operation translates its authorization requirement into a typed `(EndpointBuilder -> unit)` convention function that adds the appropriate ASP.NET Core metadata objects to `EndpointBuilder.Metadata`, and appends that function to `spec.Metadata` via `ResourceBuilder.AddMetadata`.

### WebHostBuilder Extensions (WebHostBuilderExtensions.fs)

```fsharp
namespace Frank.Auth

open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =

    type WebHostBuilder with

        [<CustomOperation("useAuthentication")>]
        member _.UseAuthentication(
            spec: WebHostSpec,
            configure: AuthenticationBuilder -> AuthenticationBuilder) : WebHostSpec

        [<CustomOperation("useAuthorization")>]
        member _.UseAuthorization(spec: WebHostSpec) : WebHostSpec

        [<CustomOperation("authorizationPolicy")>]
        member _.AuthorizationPolicy(
            spec: WebHostSpec,
            name: string,
            configure: AuthorizationPolicyBuilder -> unit) : WebHostSpec
```

**Contract**:
- `useAuthentication`: Adds authentication services to `spec.Services` and authentication middleware to `spec.Middleware` (post-routing position).
- `useAuthorization`: Adds authorization services to `spec.Services` and authorization middleware to `spec.Middleware` (post-routing position, after authentication).
- `authorizationPolicy`: Adds a named policy to authorization options via `spec.Services`.

---

## Usage Contract (End-to-End)

```fsharp
open Frank.Builder
open Frank.Auth

// Resource with authorization
let dashboard =
    resource "/dashboard" {
        name "Dashboard"
        requireAuth
        get handler
    }

// Application wiring
webHost args {
    useDefaults
    useAuthentication (fun auth -> auth)
    useAuthorization
    authorizationPolicy "Admin" (fun p -> p.RequireRole("admin") |> ignore)
    resource dashboard
}
```

**Invariants**:
- `requireAuth` with no other auth operations → 401 for unauthenticated, 200 for authenticated
- Multiple `require*` operations → AND semantics (all must pass)
- `requireClaim` with multiple values → OR semantics within that claim (at least one value must match)
- No `require*` operations → resource is publicly accessible, zero overhead
- `useAuthentication` + `useAuthorization` must both be called for authorization to work
