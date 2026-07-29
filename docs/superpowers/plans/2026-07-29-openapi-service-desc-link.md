# Frank.OpenApi service-desc Link Header Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every response from an app that enables `useOpenApi` carries a `Link` response header advertising the OpenAPI document via the IANA-registered `service-desc` relation (RFC 8631), so any HTTP client can discover the machine-readable service description without prior knowledge of `/.well-known/openapi.json`.

**Architecture:** One new public function, `addServiceDescLinkHeader`, in `src/Frank.OpenApi/WebHostBuilderExtensions.fs`, wired into both `UseOpenApi` overloads' `BeforeRoutingMiddleware` composition (not `Middleware` — see Global Constraints). Fully self-contained in `Frank.OpenApi` — no `Frank` core changes (both `WebHostSpec` fields used already exist), no new NuGet dependency, no dependency on PR #473's `WebLink`/`IResponseLinkProvider`.

**Tech Stack:** F# 8.0+, .NET 10.0 (`Frank.OpenApi` is single-targeted), ASP.NET Core, Expecto for tests.

**Full design:** `docs/superpowers/specs/2026-07-29-openapi-service-desc-link-design.md`

## Global Constraints

- `src/Frank.OpenApi/Frank.OpenApi.fsproj` targets `net10.0` only (single-target, unlike `Frank` core's multi-targeting) — no cross-TFM build verification needed for this change.
- `rel` value is exactly `"service-desc"` (RFC 8631, verified against the IANA Link Relations registry).
- Target is exactly `openApiRoutePattern` (the existing private `"/.well-known/openapi.json"` constant already in `WebHostBuilderExtensions.fs`) — do not make this configurable, it isn't today and this feature doesn't need to change that.
- `type` parameter is exactly `"application/json"` (verified: `MapOpenApi` serves the document with this content type — confirmed via string constants in the installed `Microsoft.AspNetCore.OpenApi` assembly; no specialized `application/vnd.oai.openapi+json` media type is in use).
- The formatted header value MUST be computed once, at module-load time (a top-level `let`), not per-request inside the middleware closure — the middleware runs on every request in the app, including unrelated 404s.
- No `Response.OnStarting` wrapper. `BeforeRoutingMiddleware` composes before `UseRouting()` even runs, so nothing has touched the response yet — a direct `ctx.Response.Headers.Append(...)` call before invoking `next` is always safe.
- **Placement is load-bearing and verified empirically, not just by inspection: `addServiceDescLinkHeader` MUST be composed into `BeforeRoutingMiddleware`, not `Middleware`.** `EndpointMiddleware` is terminal for any endpoint that ASP.NET Core's routing has already matched, regardless of which specific `UseEndpoints()` call registered that endpoint — `UseRouting()` matches once, globally, against the union of all registered endpoints, and the *first* `EndpointMiddleware` instance encountered in the pipeline invokes whatever endpoint matched and does not call `next()`. A throwaway probe (two separate `UseEndpoints()` calls with a marker middleware sandwiched between them, run against a real `TestServer`) confirmed: middleware placed after a `UseEndpoints()` call never runs for *any* matched request from *either* call — only for unmatched (404) requests. This means ordering the header-append merely *before this module's own* `app.UseEndpoints(mapOpenApiEndpoints)` call, while still inside `Middleware`, is NOT sufficient — a *different* package's (or the app's own `plug`-registered) `UseEndpoints()` call composed earlier in the same `Middleware` chain would still bypass it for every matched request in the whole app. `BeforeRoutingMiddleware` runs before `UseRouting()` is even called, per `Frank.WebHostBuilder.Run`'s pipeline (`BeforeRoutingMiddleware -> UseRouting() -> Middleware -> UseEndpoints(resources)`), so no endpoint has been matched yet and nothing downstream can ever short-circuit it — structurally, not by convention. This is the same placement `Frank.JsonHome` uses for its own Link-header middleware, for the identical reason.
- `addServiceDescLinkHeader` MUST be `public` (listed in `WebHostBuilderExtensions.fsi`, not `private` in the `.fs`) — `Frank.WebHostBuilder.Run` calls the blocking `.Build().Run()` (real Kestrel), so it cannot be wired to a `TestServer`. The only way for a test to exercise the real code path (rather than a hand-copied duplicate of the wiring) is to call the real `UseOpenApi` member to get a `WebHostSpec`, then apply its `Services`/`BeforeRoutingMiddleware`/`Middleware` functions onto a `TestServer`-based host directly, in the same order `WebHostBuilder.Run` uses (`BeforeRoutingMiddleware` before `UseRouting()`, `Middleware` after).
- No new `WebHostBuilder` custom operation. The header is unconditional, automatic behavior of `useOpenApi` (both overloads) — there is nothing to separately opt into or configure.

---

### Task 1: Add the service-desc Link header middleware to useOpenApi

**Files:**
- Modify: `src/Frank.OpenApi/WebHostBuilderExtensions.fsi` (add one `open`, one public `val`)
- Modify: `src/Frank.OpenApi/WebHostBuilderExtensions.fs` (add the header value + middleware function, wire into both `UseOpenApi` overloads)
- Test: `test/Frank.OpenApi.Tests/ServiceDescLinkTests.fs` (create)
- Modify: `test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj` (register the new test file)

**Interfaces:**
- Consumes: nothing new — uses the existing private `openApiRoutePattern` constant, the existing `WebHostSpec`/`WebHostBuilder` types from `Frank.Builder` (`src/Frank/WebHostBuilder.fs`), and the existing `UseOpenApi` overloads.
- Produces: `WebHostBuilderExtensions.addServiceDescLinkHeader : app: IApplicationBuilder -> IApplicationBuilder` (public).

**Background you need:**

Current `src/Frank.OpenApi/WebHostBuilderExtensions.fs` (both overloads have this shape):

```fsharp
member _.UseOpenApi(spec: WebHostSpec) : WebHostSpec =
    { spec with
        Services = spec.Services >> fun services -> ...
        Middleware = spec.Middleware >> fun app ->
            app.UseEndpoints(mapOpenApiEndpoints) |> ignore
            app }
```

`test/Frank.OpenApi.Tests/OpenApiDocumentTests.fs` already defines (compiled before `SchemaTests.fs`, so visible to later files via `open`):
- `type TestEndpointDataSource(endpoints: Endpoint[])` — a test-only `EndpointDataSource`.
- `let openApiRoutePattern = "/.well-known/openapi.json"` — a copy of the private constant, for test use.
- `let simpleHandler : RequestDelegate` — a trivial `"OK"`-writing handler.

The new test file reuses all three via `open Frank.OpenApi.Tests.OpenApiDocumentTests` rather than duplicating them.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.OpenApi.Tests/ServiceDescLinkTests.fs`:

```fsharp
module Frank.OpenApi.Tests.ServiceDescLinkTests

open System.Net.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.OpenApi
open Frank.OpenApi.Tests.OpenApiDocumentTests

/// Creates a test server by calling the real WebHostBuilder.UseOpenApi member and
/// applying its Services/Middleware onto a TestServer-based host, so the behavior
/// under test is the actual production code path -- not a hand-copied duplicate of
/// its wiring. (Frank.WebHostBuilder.Run calls the blocking .Build().Run(), which
/// cannot be wired to a TestServer, hence not going through the `webHost { }` CE's
/// Run member directly.)
let createRealUseOpenApiTestServer (resources: Resource list) =
    let allEndpoints = resources |> List.collect (fun r -> r.Endpoints |> Array.toList) |> List.toArray
    let spec = (webHost [||]).UseOpenApi(WebHostSpec.Empty)
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        spec.Services services |> ignore)
                    .Configure(fun app ->
                        spec.BeforeRoutingMiddleware app |> ignore
                        app.UseRouting() |> ignore
                        spec.Middleware app |> ignore
                        app.UseEndpoints(fun endpoints ->
                            endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

/// Same as above but exercises the `configure: OpenApiOptions -> unit` overload.
let createRealUseOpenApiWithConfigureTestServer (resources: Resource list) =
    let allEndpoints = resources |> List.collect (fun r -> r.Endpoints |> Array.toList) |> List.toArray
    let spec = (webHost [||]).UseOpenApi(WebHostSpec.Empty, fun _options -> ())
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        spec.Services services |> ignore)
                    .Configure(fun app ->
                        spec.BeforeRoutingMiddleware app |> ignore
                        app.UseRouting() |> ignore
                        spec.Middleware app |> ignore
                        app.UseEndpoints(fun endpoints ->
                            endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

let private expectedLinkValue =
    "<" + openApiRoutePattern + ">; rel=\"service-desc\"; type=\"application/json\""

let private expectLinkHeader (response: HttpResponseMessage) (context: string) =
    Expect.isTrue (response.Headers.Contains("Link")) (context + ": response should carry a Link header")
    let values = response.Headers.GetValues("Link") |> List.ofSeq
    Expect.contains values expectedLinkValue (context + ": Link header should advertise the OpenAPI document as service-desc")

[<Tests>]
let tests =
    testList "Frank.OpenApi service-desc Link header" [
        testTask "response from an arbitrary resource carries the service-desc Link header" {
            let products =
                resource "/products" {
                    name "Products"
                    get simpleHandler
                }
            let client = createRealUseOpenApiTestServer [ products ]
            let! (response: HttpResponseMessage) = client.GetAsync("/products")
            expectLinkHeader response "GET /products"
        }

        testTask "response from the OpenAPI document's own route also carries the header" {
            let products =
                resource "/products" {
                    name "Products"
                    get simpleHandler
                }
            let client = createRealUseOpenApiTestServer [ products ]
            let! (response: HttpResponseMessage) = client.GetAsync(openApiRoutePattern)
            expectLinkHeader response (sprintf "GET %s" openApiRoutePattern)
        }

        testTask "the header is present with the configure-taking UseOpenApi overload too" {
            let products =
                resource "/products" {
                    name "Products"
                    get simpleHandler
                }
            let client = createRealUseOpenApiWithConfigureTestServer [ products ]
            let! (response: HttpResponseMessage) = client.GetAsync("/products")
            expectLinkHeader response "GET /products (configure overload)"
        }
    ]
```

Register it in `test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`, directly after `OpenApiDocumentTests.fs`:

```xml
    <Compile Include="OpenApiDocumentTests.fs" />
    <Compile Include="ServiceDescLinkTests.fs" />
    <Compile Include="SchemaTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj --filter "FullyQualifiedName~ServiceDescLink"
```

Expected: FAIL at compile time with `The type 'WebHostBuilderExtensions' does not define the field, constructor or member 'addServiceDescLinkHeader'` (referenced indirectly — the module doesn't expose it yet) or, if the file compiles because the test doesn't call it directly, FAIL at runtime because no `Link` header is present (`Expect.isTrue (response.Headers.Contains("Link")) ...` fails).

- [ ] **Step 3: Add the signature to `src/Frank.OpenApi/WebHostBuilderExtensions.fsi`**

Replace the entire file:

```fsharp
namespace Frank.OpenApi

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.OpenApi
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =

    /// Appends a `Link: <...>; rel="service-desc"; type="application/json"` header
    /// (RFC 8631) to every response, advertising the OpenAPI document. Composed into
    /// `WebHostSpec.BeforeRoutingMiddleware`, not `Middleware` -- `UseRouting()` matches
    /// endpoints globally, once, and the first `EndpointMiddleware` encountered in the
    /// pipeline dispatches whatever matched regardless of which `UseEndpoints()` call
    /// registered it, without calling `next()`. Middleware placed anywhere in `Middleware`
    /// (even before this module's own `UseEndpoints` call) can still be bypassed by an
    /// earlier `UseEndpoints()` call composed in by a different package or by `plug`.
    /// `BeforeRoutingMiddleware` runs before `UseRouting()` even executes, so nothing
    /// downstream can ever short-circuit it -- structurally, not just by convention.
    val addServiceDescLinkHeader : app:IApplicationBuilder -> IApplicationBuilder

    type WebHostBuilder with
        [<CustomOperation("useOpenApi")>]
        member UseOpenApi : spec:WebHostSpec -> WebHostSpec

        [<CustomOperation("useOpenApi")>]
        member UseOpenApi : spec:WebHostSpec * configure:(OpenApiOptions -> unit) -> WebHostSpec
```

- [ ] **Step 4: Add the implementation to `src/Frank.OpenApi/WebHostBuilderExtensions.fs`**

The file already has `open Microsoft.AspNetCore.Routing` followed immediately by `open Microsoft.Extensions.DependencyInjection`. Insert one new line, `open Microsoft.Extensions.Primitives`, between those two existing lines (do not duplicate the `DependencyInjection` line — it's already there):

```fsharp
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Primitives
open Microsoft.Extensions.DependencyInjection   // already present -- shown only for position
```

Add this directly after the existing `mapOpenApiEndpoints` function (before `configureOpenApiDefaults`):

```fsharp
    let private serviceDescLinkHeaderValue =
        StringValues(sprintf "<%s>; rel=\"service-desc\"; type=\"application/json\"" openApiRoutePattern)

    let addServiceDescLinkHeader (app: IApplicationBuilder) =
        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
            ctx.Response.Headers.Append("Link", serviceDescLinkHeaderValue)
            next.Invoke ctx)
```

Then update both `UseOpenApi` overloads to compose `addServiceDescLinkHeader` into `BeforeRoutingMiddleware` (not `Middleware`):

```fsharp
    type WebHostBuilder with
        [<CustomOperation("useOpenApi")>]
        member _.UseOpenApi(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Services = spec.Services >> fun services ->
                    services.AddOpenApi(fun options ->
                        configureOpenApiDefaults options
                    ) |> ignore
                    services
                BeforeRoutingMiddleware = spec.BeforeRoutingMiddleware >> addServiceDescLinkHeader
                Middleware = spec.Middleware >> fun app ->
                    app.UseEndpoints(mapOpenApiEndpoints) |> ignore
                    app }

        [<CustomOperation("useOpenApi")>]
        member _.UseOpenApi(spec: WebHostSpec, configure: OpenApiOptions -> unit) : WebHostSpec =
            { spec with
                Services = spec.Services >> fun services ->
                    services.AddOpenApi(fun options ->
                        configure options
                    ) |> ignore
                    services
                BeforeRoutingMiddleware = spec.BeforeRoutingMiddleware >> addServiceDescLinkHeader
                Middleware = spec.Middleware >> fun app ->
                    app.UseEndpoints(mapOpenApiEndpoints) |> ignore
                    app }
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj
```

Expected: PASS, all tests (including the 3 new ones and every pre-existing test in `OpenApiDocumentTests.fs`, `MetadataTests.fs`, and `SchemaTests.fs` — this change must not regress any of them).

- [ ] **Step 6: Commit**

```bash
git add src/Frank.OpenApi/WebHostBuilderExtensions.fsi src/Frank.OpenApi/WebHostBuilderExtensions.fs test/Frank.OpenApi.Tests/ServiceDescLinkTests.fs test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj
git commit -m "feat(openapi): advertise the document with a service-desc Link header

Every response from an app with useOpenApi enabled now carries
Link: <path>; rel=\"service-desc\"; type=\"application/json\" (RFC 8631),
so clients can discover the OpenAPI document without prior knowledge of
its route. Self-contained in Frank.OpenApi -- no Frank core changes, no
dependency on the WebLink/IResponseLinkProvider work in PR #473 (the
clobbering problem it solves doesn't apply here: ASP.NET Core's own
IHeaderDictionary.Append already supports multiple independent
contributors to the same header).

The header-appending middleware is composed into BeforeRoutingMiddleware,
not Middleware -- EndpointMiddleware is terminal for any endpoint
ASP.NET Core's routing has already matched, regardless of which
UseEndpoints() call registered it, so anything in Middleware (even
ordered before this module's own UseEndpoints call) can still be
bypassed by a different package's earlier UseEndpoints() call.
BeforeRoutingMiddleware runs before UseRouting() even executes, so
nothing downstream can ever short-circuit it (verified empirically).

Closes #477."
```

---

## Verification Checklist

Run after the task:

- [ ] `dotnet build src/Frank.OpenApi/Frank.OpenApi.fsproj` succeeds
- [ ] `dotnet build Frank.sln` succeeds
- [ ] `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj` passes (all files, not just the new one)
- [ ] The three new tests specifically cover: an arbitrary resource response, the OpenAPI document's own response, and the `configure`-taking `UseOpenApi` overload
