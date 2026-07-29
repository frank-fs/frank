# Frank.OpenApi: advertise the document with a service-desc Link header

**Tracks:** GitHub issue [#477](https://github.com/frank-fs/frank/issues/477), deferred from PR [#473](https://github.com/frank-fs/frank/pull/473) (`Frank.JsonHome`).

**Goal:** Every response from an app that enables `useOpenApi` carries a `Link` response header pointing at the OpenAPI document, using the IANA-registered `service-desc` relation (RFC 8631) so any HTTP client — not just ones that already know to look at `/.well-known/openapi.json` — can discover the machine-readable description of the service.

## Background

Issue #477 originally sketched this as a consumer of PR #473's `WebLink`/`IResponseLinkProvider` — a DI-resolved provider abstraction in `Frank` core that lets multiple packages contribute to the response `Link` header through one shared middleware, avoiding one contributor's header write clobbering another's.

That shared abstraction turned out to be unnecessary for this: ASP.NET Core's own `HeaderDictionaryExtensions.Append(IHeaderDictionary, string, StringValues)` ("Add new values. Each item remains a separate array entry") already lets independent middleware add to the same header key without clobbering each other. Verified directly against the installed ASP.NET Core 10.0 reference assemblies — there is no built-in equivalent of a multi-provider `Link` aggregator, but there doesn't need to be one for a single, fixed, always-the-same link value.

**Decision: this feature is self-contained in `Frank.OpenApi` and does not depend on PR #473 or introduce any change to `Frank` core.** If `Frank.JsonHome` is also present in the same app, its own middleware (which independently appends to the same header) coexists correctly with this one — verified via the same `Append` semantics.

## Verified facts this design depends on

- `rel="service-desc"` is registered in the [IANA Link Relations registry](https://www.iana.org/assignments/link-relations/link-relations.xhtml) under RFC 8631: *"service description for the context that is primarily intended for consumption by machines."* Confirmed correct for this use case.
- `src/Frank.OpenApi/WebHostBuilderExtensions.fs` (current `master`, post PR #472) hardcodes the document route as a private `openApiRoutePattern = "/.well-known/openapi.json"` constant — not currently configurable. Nothing new needs to be parameterized for this feature.
- ASP.NET Core's `MapOpenApi` serves the document as `application/json` (confirmed via string constants in the installed `Microsoft.AspNetCore.OpenApi` assembly — no specialized `application/vnd.oai.openapi+json` media type is in use).
- `useOpenApi` currently only registers endpoints (`app.UseEndpoints(mapOpenApiEndpoints)`); it installs no per-request middleware today.

## Architecture

One new function in `src/Frank.OpenApi/WebHostBuilderExtensions.fs`: `addServiceDescLinkHeader`, wired into both existing `UseOpenApi` overloads' `BeforeRoutingMiddleware` composition (not `Middleware` — see Data flow for why). No new files. No new public types. No `Frank` core changes (both `WebHostSpec` fields used already exist). No new NuGet dependency.

```fsharp
let private serviceDescLinkHeaderValue =
    StringValues(sprintf "<%s>; rel=\"service-desc\"; type=\"application/json\"" openApiRoutePattern)

let addServiceDescLinkHeader (app: IApplicationBuilder) =
    app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
        ctx.Response.Headers.Append("Link", serviceDescLinkHeaderValue)
        next.Invoke ctx)
```

`addServiceDescLinkHeader` is **public** (not `private`) and listed in `WebHostBuilderExtensions.fsi` — see Testing below for why.

Both `UseOpenApi` overloads gain this:

```fsharp
BeforeRoutingMiddleware = spec.BeforeRoutingMiddleware >> addServiceDescLinkHeader
Middleware = spec.Middleware >> fun app ->
    app.UseEndpoints(mapOpenApiEndpoints) |> ignore
    app
```

## Data flow

The header value is formatted exactly once, at module-load time (`let serviceDescLinkHeaderValue = ...`, not computed inside the middleware closure) — not per-request. This matters because the middleware runs for every request in the app, including ones that don't touch OpenAPI at all (e.g. a 404 for an unrelated path), so avoiding repeated string-building on every request is a real, not premature, optimization.

**Placement: `BeforeRoutingMiddleware`, not `Middleware` — this was gotten wrong once during this feature's own implementation and corrected after further investigation.** The first draft placed `addServiceDescLinkHeader` inside `Middleware`, ordered before this module's own `app.UseEndpoints(mapOpenApiEndpoints)` call. That's necessary but not sufficient. Verified empirically (a throwaway probe: two separate `UseEndpoints()` calls in one pipeline with a marker middleware sandwiched between them) that `UseRouting()` matches endpoints globally, once, against the union of *all* registered endpoints regardless of which `UseEndpoints()` call registered them — and the *first* `EndpointMiddleware` instance encountered in the pipeline dispatches whatever matched, without calling `next()`, regardless of origin. So middleware placed anywhere in `Middleware` — even before this module's own `UseEndpoints` call — can still be silently bypassed by a *different* package's (or the app's own `plug`-registered) `UseEndpoints()` call, if that call happens to be composed earlier in the same `Middleware` chain. Only for unmatched (404) requests would our middleware still run.

`BeforeRoutingMiddleware` runs before `UseRouting()` is even called (per `Frank`'s `WebHostBuilder.Run`: `BeforeRoutingMiddleware -> UseRouting() -> Middleware -> UseEndpoints(resources)`), so no endpoint has been matched yet and nothing downstream — no matter how many `UseEndpoints()` calls or in what order — can ever short-circuit it. This is the same placement `Frank.JsonHome` already uses for its own Link-header-and-document-serving middleware, for exactly this reason (advertising on every response, including 404s).

Because the header is appended unconditionally before calling `next()`, and nothing has run yet at this point in the pipeline (not even routing), `Response.Headers.Append` is always safe here — no `Response.OnStarting` callback is needed. (An even earlier draft of this design used `OnStarting` defensively; that turned out to be unnecessary complexity, and has been dropped.)

`Append`, not header-index assignment, is what makes this safe to coexist with anything else — including `Frank.JsonHome`'s own independent `Link` header contribution — without either clobbering the other.

## API surface

No new `WebHostBuilder` custom operation. The header is automatic, unconditional behavior of the existing `useOpenApi` operation (both overloads) — there is nothing to separately opt into or configure, since the header only ever makes sense (and only ever has one value) when OpenAPI generation is enabled at all.

## Testing

`test/Frank.OpenApi.Tests/OpenApiDocumentTests.fs` does not currently exercise `WebHostBuilderExtensions.UseOpenApi` — it hand-rolls an equivalent `Host.CreateDefaultBuilder().UseTestServer()...` setup, because `Frank.WebHostBuilder.Run` calls the blocking `.Build().Run()` (real Kestrel), which cannot be wired to a `TestServer`. To test the real code path rather than a hand-copied duplicate, the new test constructs a `WebHostSpec` by calling the actual `UseOpenApi` member, then applies its `Services`/`BeforeRoutingMiddleware`/`Middleware` functions onto a `TestServer`-based host in the same order `WebHostBuilder.Run` does (`BeforeRoutingMiddleware` before `UseRouting()`, `Middleware` after) — same harness shape as the existing file, but driving the real function. This mirrors the same fix already applied to `Frank.JsonHome`, where the equivalent middleware was made public for exactly this reason.

At minimum:
- A request to an arbitrary registered resource (not the OpenAPI document itself) returns a `Link` response header containing `</.well-known/openapi.json>; rel="service-desc"; type="application/json"`.
- A request to the OpenAPI document's own route also carries the header (no special-casing excludes it).
- The header value is present and correctly formed regardless of which `UseOpenApi` overload is used.

## Out of scope

- Making the OpenAPI document route configurable. It's already a hardcoded constant today; this feature doesn't change that.
- Any change to `Frank` core, `PR #473`'s `WebLink`/`IResponseLinkProvider`, or `Frank.JsonHome`. Those were evaluated during this design's brainstorming (see Background) but are separate, already-in-flight work with their own review process.
- A general-purpose `link` WebHostBuilder operation. Considered and rejected — see Background/API surface.
