# Data Model: Frank.Auth

**Feature**: 013-frank-auth
**Date**: 2026-02-05

## Entities

### ResourceSpec (Frank Core — Modified)

The existing `ResourceSpec` record is extended with a generic metadata collection.

| Field | Type | Description |
|-------|------|-------------|
| Name | `string` | Display name for the resource |
| Handlers | `(string * RequestDelegate) list` | HTTP method + handler pairs |
| **Metadata** | **`(EndpointBuilder -> unit) list`** | **NEW. Convention functions that configure endpoint metadata. Empty by default.** |

**Empty state**: `{ Name = null; Handlers = []; Metadata = [] }`

**Relationships**: During `Build()`, each handler is constructed via `RouteEndpointBuilder`. All metadata functions are applied to the builder (adding items to `EndpointBuilder.Metadata`) before `Build()` freezes the metadata into an immutable `EndpointMetadataCollection`. Each endpoint receives the convention-applied metadata plus its per-handler `HttpMethodMetadata`.

---

### AuthRequirement (Frank.Auth)

A discriminated union representing a single authorization constraint.

| Variant | Fields | Description |
|---------|--------|-------------|
| `Authenticated` | (none) | Requires any authenticated user |
| `Claim` | `claimType: string`, `claimValues: string list` | Requires claim type with accepted values (OR semantics within) |
| `Role` | `name: string` | Requires membership in a named role |
| `Policy` | `name: string` | References a named authorization policy |

**Validation rules**:
- `Claim.claimType` must not be null or empty
- `Claim.claimValues` empty list means "claim must exist with any value"
- `Policy.name` must match a policy registered at the application level
- `Role.name` must not be null or empty

---

### AuthConfig (Frank.Auth)

Collected authorization requirements for a resource.

| Field | Type | Description |
|-------|------|-------------|
| Requirements | `AuthRequirement list` | Ordered list of authorization constraints |

**Empty state**: `{ Requirements = [] }`

**Operations**:
- `AuthConfig.empty` — no requirements
- `AuthConfig.addRequirement` — appends a requirement (requirements are additive)
- `AuthConfig.isEmpty` — returns true when no requirements present

**Semantics**:
- Requirements use AND semantics: all must pass for access to be granted
- Within a `Claim` requirement, multiple values use OR semantics: user needs at least one

---

## Translation: AuthConfig → Metadata Objects

`AuthConfig` is not stored in `ResourceSpec.Metadata` directly. Instead, each `AuthRequirement` is translated into a typed `(EndpointBuilder -> unit)` convention function that adds ASP.NET Core metadata objects to `EndpointBuilder.Metadata`:

| AuthRequirement | Metadata Objects Added via Convention |
|---|---|
| `Authenticated` | `AuthorizeAttribute()` |
| `Claim(type, values)` | `AuthorizeAttribute()`, `AuthorizationPolicy` (built via `AuthorizationPolicyBuilder`) |
| `Role(name)` | `AuthorizeAttribute()`, `AuthorizationPolicy` (built via `AuthorizationPolicyBuilder`) |
| `Policy(name)` | `AuthorizeAttribute(name)` |

The translation happens in the `ResourceBuilder` custom operation implementations — each `require*` operation translates its `AuthRequirement` into a convention function that adds the corresponding metadata objects to `EndpointBuilder.Metadata`, and appends that function to `spec.Metadata`.

---

## State Transitions

Resources have no runtime state transitions. Authorization configuration is fully determined at application startup:

1. **Build time**: Custom operations accumulate `AuthRequirement` values → translate to `(EndpointBuilder -> unit)` convention functions → append to `ResourceSpec.Metadata`
2. **Endpoint construction**: `ResourceSpec.Build()` creates `RouteEndpointBuilder` per handler, applies all metadata convention functions, then calls `Build()` to freeze metadata into `EndpointMetadataCollection`
3. **Request time**: ASP.NET Core authorization middleware reads endpoint metadata and enforces policies

No mutable state. No lifecycle changes after startup.
