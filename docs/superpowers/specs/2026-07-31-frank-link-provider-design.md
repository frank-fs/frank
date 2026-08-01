# Frank core: Shared Response Link Provider

**Date**: 2026-07-31
**Branch**: `link-header`
**Status**: Draft — awaiting review

## Context

`Frank.JsonHome`'s `linkHeaderMiddleware` (`JsonHome.fs`) and `Frank.OpenApi`'s `addServiceDescLinkHeader` (`WebHostBuilderExtensions.fs:24-33`) are independent, nearly-identical copies of the same pattern: build an RFC 8288 `Link` field value once, append (not assign) it via `Response.OnStarting` so it survives exception-handler-regenerated responses, installed as app-wide before-routing middleware.

The original [Frank.JsonHome design doc](2026-07-28-frank-jsonhome-design.md) proposed a shared `IResponseLinkProvider`/`WebLink` mechanism in Frank core specifically to avoid this — "`Link` is a header multiple extensions want to contribute to, and it only works if they cooperate." That mechanism was never built; both packages ended up rolling their own instead. [GitHub issue #481](https://github.com/frank-fs/frank/issues/481) tracks promoting it.

The reason to do this now rather than later: designing `Frank.Rdf` ([design doc](2026-07-30-frank-rdf-design.md)) surfaced a third need — its JSON-LD representation of a specific resource (e.g. `/games/{id}`) wants to advertise itself via a `Link` header too, and unlike the two existing uses, that's a **resource-scoped** contribution, not an app-wide singleton. [GitHub issue #483](https://github.com/frank-fs/frank/issues/483) (Frank.Rdf's HTTP-serving story) is blocked on that capability existing. So this feature covers both: deduplicating the two existing app-wide uses, and adding the resource-scoped capability #483 needs, in one core change rather than two.

### Reference specifications

| Spec | Version | Media type |
|---|---|---|
| Web Linking | [RFC 8288](https://www.rfc-editor.org/rfc/rfc8288) | — |
| Link Relation Types for Web Services | [RFC 8631](https://www.rfc-editor.org/rfc/rfc8631) | — |

## Goals

- One shared `WebLink` type and one shared RFC 8288 formatter/escaper, used everywhere a package wants to contribute a `Link` header entry.
- App-wide contributions (today's `home` and `service-desc`) keep their existing behavior exactly: present on every response, including unmatched routes and exception-handler-regenerated responses.
- A new resource-scoped contribution capability: a `resource { }` block can contribute a `Link` entry that appears only on that resource's own responses — unblocking #483.
- Delete the two private implementations once both packages are migrated onto the shared mechanism.

## Non-goals

- Contributing the `profile` (ALPS) relation — deferred, separate work.
- Per-response (handler-time) links such as pagination's `next`/`prev` — those vary per request in a way this app-wide/resource-scoped model doesn't address; a different mechanism, if one is ever built.
- Any change to what `Frank.JsonHome` or `Frank.OpenApi`'s links actually say (target URI, relation type) — only how the value reaches the response.
- Deduplicating contributions that happen to emit the same relation type — each contributor owns its own relation choice.
- Deciding whether or how `Frank.Rdf` (#483) actually uses the resource-scoped capability — this work only makes the mechanism available.
- Restricting `Link` contribution by HTTP method. RFC 8288 doesn't restrict `Link` to any method, and a provider function already receives `HttpContext` and can check `ctx.Request.Method` itself if it wants to limit where it applies — the framework doesn't need an opinion here.
- DI-based provider registration (`IServiceCollection`/`IServiceProvider`). Every other `WebHostSpec`/`ResourceSpec` field is a plain composed function or list, with no DI involved for wiring; this mechanism follows that existing shape rather than introducing a new one.

## Frank core changes

### 1. `WebLink` — the shared type and formatter

New file `src/Frank/WebLink.fs` (+ `.fsi`):

```fsharp
type WebLink =
    { Target: string                      // URI-Reference
      Rel: string
      Params: (string * string) list }    // title, type, hreflang, anchor, ...

module WebLink =
    val format : WebLink -> string
```

`format` produces one RFC 8288 field value (`<target>; rel="rel"; param="value"...`), including the quoted-parameter escaping (`\`, `"`) both existing implementations already do independently. This is the single implementation replacing both private copies.

A `private` helper in the same file, `appendToResponse : HttpContext -> WebLink list -> unit`, is shared by both middlewares below: given a non-empty list of links for the current request, register **one** `Response.OnStarting` callback that formats all of them and appends a single `StringValues` array to the `Link` header. Given an empty list, it does nothing — no empty or malformed header is ever added.

### 2. App-wide contributions — `WebHostSpec.LinkProviders`

`WebHostSpec` (in `src/Frank/WebHostBuilder.fs`) gains:

```fsharp
LinkProviders: (HttpContext -> WebLink seq) list   // WebHostSpec.Empty: []
```

`WebHostBuilder` gains a `link` custom operation, two overloads:

```fsharp
[<CustomOperation("link")>]
member _.Link(spec, target: string, rel: string) : WebHostSpec =
    { spec with LinkProviders = spec.LinkProviders @ [ fun _ -> Seq.singleton { Target = target; Rel = rel; Params = [] } ] }

member _.Link(spec, provider: HttpContext -> WebLink seq) : WebHostSpec =
    { spec with LinkProviders = spec.LinkProviders @ [ provider ] }
```

The first is sugar for the common static case (`link "https://example.com/license" "license"`); the second is the general form a package like `Frank.JsonHome` uses when the value depends on `JsonHomeOptions`.

`WebHostBuilder.Run()` synthesizes one middleware from the accumulated `spec.LinkProviders` and splices it in **immediately before `spec.BeforeRoutingMiddleware` runs** — preserving today's before-routing placement so unmatched-route (404) and exception-handler-regenerated responses still carry app-wide links. For each request it calls every provider with `ctx`, concatenates the results, and calls `WebLink.appendToResponse`.

### 3. Resource-scoped contributions — `ResourceBuilder.link` writing into `ResourceSpec.Metadata`

`ResourceSpec` (in `src/Frank/ResourceBuilder.fs`) is **unchanged** — it gains no new field. Instead, `ResourceBuilder` gains a `link` custom operation, two overloads, that write directly into the pre-existing `Metadata: (EndpointBuilder -> unit) list` extensibility point (already threaded through to `RouteEndpointBuilder.Metadata` in `Build()`, and already used by `Frank.Auth`):

```fsharp
[<CustomOperation("link")>]
member __.Link(spec: ResourceSpec, target: string, rel: string) : ResourceSpec =
    __.Link(spec, fun (_: HttpContext) -> Seq.singleton { Target = target; Rel = rel; Params = [] })

[<CustomOperation("link")>]
member __.Link(spec: ResourceSpec, provider: HttpContext -> WebLink seq) : ResourceSpec =
    ResourceBuilder.AddMetadata(spec, fun builder -> builder.Metadata.Add(ResourceLinkProvider provider))
```

From the author's side, app-wide and resource-scoped contributions look the same — the same static sugar / general-provider choice — only which builder you call `link` inside determines the scope.

Under the hood, delivery differs, because reaching "only this resource's responses" requires request-time knowledge of which endpoint matched — something `WebHostSpec.LinkProviders` doesn't need. There is no separate `ResourceSpec.LinkProviders` list to accumulate first: each resource-scoped `link` call wraps its provider immediately in an `internal` marker type and adds it directly to the endpoint's metadata via the existing `AddMetadata` convention mechanism:

```fsharp
type internal ResourceLinkProvider = ResourceLinkProvider of (HttpContext -> WebLink seq)
```

(Defined in `src/Frank/WebLink.fs`, not `ResourceBuilder.fs` — `WebLink.useResourceScopedLinks` is the file that needs to read it back at request time, and `ResourceBuilder.fs` only needs to construct it. `internal` — not part of the public `.fsi` surface; the only public surface is the `link` operation itself, per `CLAUDE.md`'s rule that private/internal wiring stays out of signature files unless another file in the same assembly needs it — which `WebLink.fs` does.)

`WebHostBuilder.Run()` synthesizes a second middleware, spliced in **immediately after `UseRouting()`, before `spec.Middleware` runs**. For each request it reads `ctx.GetEndpoint()`; if non-null, it resolves `endpoint.Metadata.GetOrderedMetadata<ResourceLinkProvider>()`, calls each wrapped provider with `ctx`, concatenates the results, and calls `WebLink.appendToResponse` — same helper, same append-not-assign, same single-`OnStarting`-callback shape as the app-wide middleware. On an unmatched route, `GetEndpoint()` is null, so this middleware contributes nothing — only app-wide contributions can reach a 404 response, since no resource ever ran for it.

Final `WebHostBuilder.Run()` pipeline, in order:

```
app
|> appWideLinkMiddleware          // NEW — before routing
|> spec.BeforeRoutingMiddleware
|> fun app -> app.UseRouting()
|> resourceLinkMiddleware         // NEW — after routing, before spec.Middleware
|> spec.Middleware
|> fun app -> app.UseEndpoints(...)
```

The app-wide middleware only installs itself when `spec.LinkProviders` is non-empty — `WebLink.useAppWideLinks` short-circuits and returns `app` unchanged for an empty list, so an app that never calls `link` doesn't pay even a no-op `app.Use` per request. The resource-scoped middleware, by contrast, is installed unconditionally, same as `UseRouting`/`UseEndpoints` are today; with no resource ever registering a `link`, it's a cheap per-request check (`GetEndpoint()` non-null, no matching metadata) that appends nothing.

### 4. Error handling

A provider function that throws propagates the exception normally, whether during collection or from inside the `OnStarting` callback — no framework-level try/catch swallowing per provider. This matches how Frank treats every other user-supplied delegate (handlers, middleware): errors are visible, not silently absorbed. Nothing here calls for new resilience machinery.

## Migration

`Frank.JsonHome`: delete `JsonHome.linkHeaderMiddleware` and its private `escapeParam` (redundant with `WebLink.format`'s escaping). `WebHostBuilderExtensions.fs`'s `install` replaces the `BeforeRoutingMiddleware` mutation with:

```fsharp
LinkProviders = spec.LinkProviders @ [ fun _ -> Seq.singleton { Target = options.Path; Rel = options.Rel; Params = [] } ]
```

`Frank.OpenApi`: delete `addServiceDescLinkHeader` and `serviceDescLinkHeaderValue`. `UseOpenApi`'s two overloads replace `BeforeRoutingMiddleware = spec.BeforeRoutingMiddleware >> addServiceDescLinkHeader` with:

```fsharp
LinkProviders = spec.LinkProviders @ [ fun _ -> Seq.singleton { Target = openApiRoutePattern; Rel = "service-desc"; Params = [ "type", "application/json" ] } ]
```

Both migrations are pure substitution of the delivery mechanism — the computed `Target`/`Rel`/`Params` are unchanged, so the resulting header bytes are unchanged. This is directly testable as a before/after string-equality assertion.

## Error handling and edge cases

| Situation | Behaviour |
|---|---|
| No providers registered anywhere | No `Link` header appended — never an empty or malformed one. |
| Two providers emit the same `rel` | Both entries appear; no deduplication. |
| Target or rel needs quoted-string escaping (`\`, `"`) | `WebLink.format` escapes it. |
| Resource-scoped provider registered, request matches no route | Contributes nothing — that resource's pipeline segment never ran. |
| Resource-scoped and app-wide both registered, request matches that resource | Both appear, combined in one `Link` header. |
| A provider throws | Propagates normally; no framework-level swallowing. |
| Exception-handling middleware regenerates the response | App-wide entries survive — registered via `OnStarting`, same as today. |

## Implementation order

1. **`WebLink.fs`/`.fsi`** — type, `format`, escaping, `appendToResponse`. Unit-tested standalone, no server involved.
2. **`WebHostBuilder.fs`/`.fsi`** — `LinkProviders` field, `link` operation, app-wide middleware synthesis in `Run()`.
3. **`ResourceBuilder.fs`/`.fsi`** — `LinkProviders` field, `link` operation, `ResourceLinkProvider` internal wrapper, `Build()` wiring into `EndpointBuilder.Metadata`.
4. **`WebHostBuilder.Run()`** — resource-scoped middleware synthesis, spliced after `UseRouting()`.
5. **Integration tests** — combined app-wide providers, resource-scoped isolation between resources, resource-scoped + app-wide combined, empty-provider-list producing no header, 404/exception-survival for app-wide only.
6. **Migrate `Frank.JsonHome`** — delete `linkHeaderMiddleware`, wire `useJsonHome` through `LinkProviders`, migration-equivalence test.
7. **Migrate `Frank.OpenApi`** — delete `addServiceDescLinkHeader`, wire `useOpenApi` through `LinkProviders`, migration-equivalence test.
8. Existing `Frank.JsonHome`/`Frank.OpenApi` suites green, unchanged, across every targeted TFM.

## Testing

- **Pure functions, unit-tested without a server**: `WebLink.format` (multiple params, escaping, no-params case).
- **Integration** via the existing `WebApplicationFactory`-based suites:
  - Single app-wide provider → header present.
  - Two app-wide providers (JsonHome + OpenApi together) → both entries present in one combined header, neither overwritten.
  - Provider returning an empty sequence → no `Link` header at all.
  - Resource-scoped provider on resource A → present on A's responses, absent on resource B's.
  - Resource-scoped + app-wide together on the same resource → both present.
  - Unmatched route (404) → app-wide entries present, resource-scoped absent.
  - Exception-handling middleware regenerates the response → app-wide entries still present.
  - Migration equivalence: JsonHome's and OpenApi's exact header values unchanged before vs. after migration.
- **Regression**: existing `Frank.JsonHome`/`Frank.OpenApi` suites pass unchanged, across `net8.0`/`net9.0`/`net10.0`.

## Future work (separate)

- **`Frank.OpenApi` emitting `profile` (ALPS)** — once the deferred ALPS work lands, a one-liner on top of this mechanism.
- **Frank.Rdf adopting resource-scoped `link`** (#483) — this work only makes the mechanism available; whether and how `/games/{id}` uses it is that issue's own decision.
- **Per-response links** — pagination's `next`/`prev` are handler-time and need a different mechanism than this one.
