# Frank.Auth: Handler-Level Authorization

**Date**: 2026-07-29
**Branch**: `worktree-handler-auth`
**Status**: Implemented
**Issue**: [#476](https://github.com/frank-fs/frank/issues/476)

## Context

`Frank.Auth`'s `requireAuth` / `requireClaim` / `requireRole` / `requirePolicy` are `ResourceBuilder` custom operations, so their metadata lands on *every* endpoint in a resource (`Frank.Auth/ResourceBuilderExtensions.fs`). There is no way to say "this resource is public, but `DELETE` requires `admin`" — a resource is protected uniformly or not at all. [013-frank-auth](../../../specs/013-frank-auth/spec.md) explicitly deferred this: "Per-HTTP-method authorization ... deferred to a future specification." This is that specification.

It also blocks `Frank.JsonHome`'s `hints.allow` from being trustworthy at anything finer than whole-resource granularity — [2026-07-28-frank-jsonhome-design.md](2026-07-28-frank-jsonhome-design.md) called this out directly: filtering is "resource-granular" because `Frank.Auth` had no per-method concept to read. That doc anticipated the JSON Home side would need "no change" once handler-level auth existed; this design finds one small change is in fact needed there too (see "Frank.JsonHome" below) — `ApiSurface` currently merges metadata across a resource's methods into one field, which loses per-method granularity even once `Frank.Auth` produces it.

## Goals

1. Let a single HTTP method within a resource carry its own authorization requirements, additive to whatever the resource declares.
2. Let a single method opt out of authorization entirely, overriding both its own and the resource's requirements — matching ASP.NET Core's own `AllowAnonymous` precedent exactly, not inventing new override semantics.
3. Make `Frank.JsonHome`'s `hints.allow` (and the other method-keyed hints) accurate per method, not merged across the whole resource.

## Non-goals

- Any "downgrade to a laxer-but-still-restricted policy" mechanism. ASP.NET Core has no such concept — `[AllowAnonymous]` is a binary bypass of *all* authorization on that endpoint, not a policy substitution. This design mirrors that exactly (see "Composition semantics" below).
- Authentication scheme configuration, custom `IAuthorizationHandler`/requirement types, rate limiting, CORS — unchanged from 013's out-of-scope list.
- Any Frank core change. The metadata-scoping mechanism this relies on (`HandlerDefinition` + `ResourceBuilder.AddHandlerDefinition` / `AddMethodMetadata`) already exists and is already tested (`test/Frank.Tests/ResourceBuilderMetadataTests.fs`, "handler definition metadata is scoped to its own HTTP method").

## Composition semantics

Two independent mechanisms, both mapping directly onto stock ASP.NET Core behavior:

1. **Additive** (`requireAuth` / `requireClaim` / `requireRole` / `requirePolicy` at handler level): the platform's authorization middleware combines every `IAuthorizeData` / `AuthorizationPolicy` it finds on an endpoint — resource-level and handler-level alike — with **AND** semantics. So "resource has no requirements, one handler adds `requireRole "admin"`" already works with zero extra logic: ANDing "nothing" with "something" yields "something." This covers issue #476's motivating case (`GET` public, `DELETE` admin-only) directly.
2. **Override** (`allowAnonymous` at handler level): if `IAllowAnonymous` metadata is present anywhere on an endpoint, ASP.NET Core's authorization middleware skips *all* `IAuthorizeData`/policy evaluation for that endpoint outright — regardless of how many `Authorize`-shaped requirements are also present, resource- or handler-level. It is not a merge or a downgrade; it is a full bypass. `allowAnonymous` on a handler therefore overrides a restrictive resource-level requirement for that one method only. Combining `allowAnonymous` with a handler-level `requireRole` on the *same* handler is contradictory in the same way it is in vanilla ASP.NET Core: `AllowAnonymous` wins outright, and the co-declared requirement is never evaluated. This is called out explicitly in a test (see "Testing") so it's documented behavior, not a surprise.

## Frank.Auth changes

### `HandlerBuilder` gets the same four operations as `ResourceBuilder`, plus `allowAnonymous`

New file `src/Frank.Auth/HandlerBuilderExtensions.fs` (+ `.fsi`), mirroring `ResourceBuilderExtensions.fs`:

```fsharp
type HandlerBuilder with
    [<CustomOperation("requireAuth")>]
    member _.RequireAuth(def: HandlerDefinition) : HandlerDefinition = ...

    [<CustomOperation("requireClaim")>]
    member _.RequireClaim(def: HandlerDefinition, claimType: string, claimValue: string) : HandlerDefinition = ...
    member _.RequireClaim(def: HandlerDefinition, claimType: string, claimValues: string list) : HandlerDefinition = ...

    [<CustomOperation("requireRole")>]
    member _.RequireRole(def: HandlerDefinition, role: string) : HandlerDefinition = ...

    [<CustomOperation("requirePolicy")>]
    member _.RequirePolicy(def: HandlerDefinition, policyName: string) : HandlerDefinition = ...

    [<CustomOperation("allowAnonymous")>]
    member _.AllowAnonymous(def: HandlerDefinition) : HandlerDefinition =
        HandlerDefinition.addMetadata (AllowAnonymousAttribute()) def
```

Usage:

```fsharp
resource "/widgets" {
    get listWidgets                                    // public
    delete (handler {
        requireRole "admin"
        handle deleteWidget
    })
}

resource "/profile" {
    requireAuth                                         // resource-wide
    get (handler {
        allowAnonymous                                  // this method opts back out
        handle getPublicProfileSummary
    })
    put updateProfile                                    // still requires auth
}
```

No core change is needed for this to scope correctly: `ResourceBuilder.AddHandlerDefinition` already runs `HandlerDefinitionMetadata.toConventions def` through `AddMethodMetadata`, so anything appended to `HandlerDefinition.Metadata` only ever lands on the endpoint(s) registered under that specific HTTP method.

### `EndpointAuth.fs` refactor

Extract the object-construction logic currently inline in `toConvention` into a pure function:

```fsharp
let toMetadataObjects (requirement: AuthRequirement) : obj list = ...
```

`toConvention` becomes `fun b -> toMetadataObjects requirement |> List.iter b.Metadata.Add` (used by the existing resource-level `applyAuth`, unchanged in behavior). A new function serves the handler-level path:

```fsharp
let applyAuthToHandler (config: AuthConfig) (def: HandlerDefinition) : HandlerDefinition =
    if AuthConfig.isEmpty config then def
    else
        config.Requirements
        |> List.collect toMetadataObjects
        |> List.fold (fun d m -> HandlerDefinition.addMetadata m d) def
```

Both paths produce identical metadata shapes (a bare `AuthorizeAttribute()` plus, for `Claim`/`Role`, a built `AuthorizationPolicy`) — the only difference is where the objects are appended.

## Frank.JsonHome changes

### `ApiSurface`: retain metadata per method

`ResourceDescription` gains a field:

```fsharp
MethodMetadata: (string * obj list) list   // one (httpMethod, endpointMetadata) pair per ApiDescription in the group
```

Populated the same way the existing merged `Metadata` field is (`group |> List.map (fun d -> d.HttpMethod, metadataOf d)`), just kept separate instead of concatenated. Each entry is already that specific endpoint's *effective* metadata — resource-wide conventions and that handler's own were both applied when the `RouteEndpoint` was built — so no extra lookup against the route/endpoint is needed.

The existing merged `Metadata` field is unchanged and still backs the resource-wide picks (`RelMetadata`, `AuthSchemeMetadata`, `DocsMetadata`, `HrefVarMetadata`, `StatusMetadata`, ...) that have no per-method meaning.

### `AuthorizationFilter`: evaluate and filter per method

- `isAllowed` becomes `isMethodAllowed (ctx) (metadata: obj list)`, operating on one method's metadata list instead of a whole `ResourceDescription`. It first checks for `IAllowAnonymous` and short-circuits to `true` if present — this is a genuine fix, not just a refactor: today's filter has no `AllowAnonymous` awareness at all, which was harmless while auth was resource-only but would silently over-restrict once `allowAnonymous` exists. Otherwise it evaluates exactly as today: combine `IAuthorizeData`/explicit `AuthorizationPolicy`, call `IAuthorizationService.AuthorizeAsync`, fail closed on any evaluation error.
- `apply` evaluates `isMethodAllowed` against every entry in a resource's `MethodMetadata`, and rewrites that resource's:
  - `Methods` — filtered to the allowed set
  - `Accepts` — filtered to `(httpMethod, _)` pairs whose method is allowed
  - `Formats` — cleared if `GET` is not in the allowed set (it's derived from GET's response types)
- If the resulting `Methods` is empty, the resource is **dropped from the output list entirely** — matching today's all-or-nothing behavior rather than emitting a `hints`-degraded entry. (`hasHints` in `JsonHome.fs` ORs together several resource-wide fields alongside the method-derived ones, so "keep the entry but empty the method hints" would not cleanly produce "no hints at all" whenever a resource also declares e.g. `docs` or `authSchemes` — dropping the whole entry avoids that ambiguity.)
- `varies` is unchanged: an existence-check over the merged `Metadata` field still correctly answers "could this document differ by principal," and duplicate entries across methods (the merged field can contain the same resource-wide object once per method) don't affect an existence check.

## Testing

### `Frank.Auth.Tests`

New test list, alongside the existing US1–US6 lists in `AuthorizationTests.fs`:

- Resource with no resource-level auth; `get` public, `delete` uses `handler { requireRole "admin"; handle ... }` — unauthenticated/wrong-role `DELETE` → 401/403, `GET` unaffected. (Direct #476 scenario.)
- Resource-level `requireAuth` + handler-level `requireRole` on one method — that method needs auth AND role; other methods on the same resource need only auth.
- Handler-level `allowAnonymous` overriding a resource-level `requireAuth` — that method succeeds unauthenticated while sibling methods still 401.
- Edge case: `allowAnonymous` + handler-level `requireRole` on the *same* handler — unauthenticated request still succeeds, pinning down that `AllowAnonymous` wins outright rather than the co-declared role requirement narrowing it.

### `Frank.JsonHome` tests

- A resource with mixed method visibility (e.g. `GET` public, `DELETE` admin-only) — anonymous request's document shows `allow: ["GET"]`; admin request's shows `allow: ["GET", "DELETE"]`.
- A resource whose only method is hidden for the current principal — the resource is absent from `resources` entirely, not present with empty hints.
- A method with `allowAnonymous` under an otherwise-restricted resource — that method appears in `allow` even for an anonymous request.

## Implementation order

1. `Frank.Auth`: `EndpointAuth.fs` refactor (`toMetadataObjects` extraction) — no behavior change, existing tests stay green.
2. `Frank.Auth`: `HandlerBuilderExtensions.fs` (four additive operations + `allowAnonymous`) and its tests.
3. `Frank.JsonHome`: `ApiSurface.MethodMetadata` field.
4. `Frank.JsonHome`: `AuthorizationFilter` rewritten to per-method evaluation, including the `IAllowAnonymous` fix, and its tests.

Each stage is independently verifiable; 1–2 and 3–4 could also ship as separate PRs if preferred at implementation time.
