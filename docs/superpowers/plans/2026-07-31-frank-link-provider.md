# Shared Response Link Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `Frank.JsonHome`'s and `Frank.OpenApi`'s duplicate RFC 8288 `Link`-header middleware with one shared mechanism in Frank core, and add resource-scoped `Link` contribution support (unblocking `Frank.Rdf`, issue #483).

**Architecture:** One new file, `src/Frank/WebLink.fs` (+ `.fsi`), defines the `WebLink` type, its RFC 8288 formatter, and two middleware-installer functions: `useAppWideLinks` (reads a plain function list) and `useResourceScopedLinks` (reads endpoint metadata). `WebHostSpec` gains a `LinkProviders` field and a `link` CE operation, spliced into `WebHostBuilder.Run()`'s pipeline immediately before routing. `ResourceBuilder` gains a `link` CE operation that reuses the existing `ResourceSpec.Metadata` extensibility point (built for `Frank.Auth`) to attach resource-scoped providers to endpoint metadata — no new field needed there. `Frank.JsonHome` and `Frank.OpenApi` are then migrated onto `LinkProviders`, and their private duplicate implementations deleted.

**Tech Stack:** F# 8.0+, .NET 8.0/9.0/10.0 multi-targeting (`Frank`, `Frank.JsonHome`); `Frank.OpenApi` is `net10.0`-only. ASP.NET Core (`IApplicationBuilder`, `HttpContext`, `EndpointMetadataCollection`). Expecto (`testTask`/`testCase`/`Expect.*`), matching every other Frank test project.

**Design doc:** `docs/superpowers/specs/2026-07-31-frank-link-provider-design.md`

## Global Constraints

- Every `.fs` module under `src/Frank.*/` gets a matching `.fsi` placed directly above it in the project's `<Compile>` order (`CLAUDE.md`). Update both together in every task that touches a module.
- Verify with a **real build across every targeted TFM** before calling a task done: `Frank` and `Frank.JsonHome` are `net8.0;net9.0;net10.0`; `Frank.OpenApi` is `net10.0` only. `dotnet build` with no `-f` builds every TFM a multi-targeted project declares.
- No DI-based provider registration (`IServiceCollection`/`IServiceProvider`). Both `WebHostSpec.LinkProviders` and the resource-scoped path are plain function composition, matching how every other `WebHostSpec`/`ResourceSpec` field already works.
- No method restriction (GET/HEAD/OPTIONS-only) anywhere in this mechanism. A provider receives `HttpContext` and can filter by `ctx.Request.Method` itself if it wants to.
- This work is merged to `master` but not yet released as a package — no backward-compatibility shims, no preserved-but-deprecated old functions. Delete `linkHeaderMiddleware`/`addServiceDescLinkHeader` outright once their callers are migrated.
- Test framework is **Expecto** (`testTask` for `task {}`-based tests, `testCase` for synchronous ones, `Expect.equal`/`Expect.isTrue`/`Expect.contains`/etc.) — not xUnit/NUnit.
- Every test project is single-target `net10.0` even where the library under test multi-targets; `dotnet test` always runs against `net10.0`. TFM verification for `net8.0`/`net9.0` is a **build**, not a **test run**, of the library projects.
- Trunk-based workflow: commit directly after each task, no PR.

## Out of scope for this plan

- Whether/how `Frank.Rdf` (#483) actually uses the resource-scoped `link` operation — this plan only makes the mechanism available.
- The `profile` (ALPS) relation and per-response (handler-time) links like pagination's `next`/`prev` — both explicitly deferred in the design doc's Non-goals.

---

### Task 1: `WebLink` type and RFC 8288 formatter

**Files:**
- Create: `src/Frank/WebLink.fsi`
- Create: `src/Frank/WebLink.fs`
- Modify: `src/Frank/Frank.fsproj`
- Create: `test/Frank.Tests/WebLinkTests.fs`
- Modify: `test/Frank.Tests/Frank.Tests.fsproj`

**Interfaces:**
- Produces: `type WebLink = { Target: string; Rel: string; Params: (string * string) list }` and `val WebLink.format : WebLink -> string`, both public, in namespace `Frank.Builder`.

- [ ] **Step 1: Write the failing test**

Create `test/Frank.Tests/WebLinkTests.fs`:

```fsharp
module Frank.Tests.WebLinkTests

open Expecto
open Frank.Builder

[<Tests>]
let webLinkFormatTests =
    testList "WebLink.format" [
        testCase "a link with no params formats as target and rel only" (fun () ->
            let link = { Target = "/.well-known/home.json"; Rel = "home"; Params = [] }
            Expect.equal (WebLink.format link) "</.well-known/home.json>; rel=\"home\"" "No trailing params")

        testCase "a link with one param appends it as a quoted attribute" (fun () ->
            let link =
                { Target = "/.well-known/openapi.json"
                  Rel = "service-desc"
                  Params = [ "type", "application/json" ] }
            Expect.equal
                (WebLink.format link)
                "</.well-known/openapi.json>; rel=\"service-desc\"; type=\"application/json\""
                "One param appended")

        testCase "a link with multiple params appends them in order" (fun () ->
            let link =
                { Target = "/x"
                  Rel = "alternate"
                  Params = [ "type", "application/ld+json"; "title", "JSON-LD" ] }
            Expect.equal
                (WebLink.format link)
                "</x>; rel=\"alternate\"; type=\"application/ld+json\"; title=\"JSON-LD\""
                "Params appended in declaration order")

        testCase "a backslash in a param value is escaped" (fun () ->
            let link = { Target = "/x"; Rel = "alternate"; Params = [ "title", "back\\slash" ] }
            Expect.equal
                (WebLink.format link)
                "</x>; rel=\"alternate\"; title=\"back\\\\slash\""
                "Backslash doubled")

        testCase "a double quote in a param value is escaped" (fun () ->
            let link = { Target = "/x"; Rel = "alternate"; Params = [ "title", "say \"hi\"" ] }
            Expect.equal
                (WebLink.format link)
                "</x>; rel=\"alternate\"; title=\"say \\\"hi\\\"\""
                "Double quote escaped")

        testCase "a backslash or quote in rel itself is escaped" (fun () ->
            let link = { Target = "/x"; Rel = "weird\"rel"; Params = [] }
            Expect.equal (WebLink.format link) "</x>; rel=\"weird\\\"rel\"" "Rel escaped too")
    ]
```

Add it to `test/Frank.Tests/Frank.Tests.fsproj`, before `MiddlewareOrderingTests.fs` (alphabetical-ish grouping doesn't matter to Expecto's discovery, but keep new Link-related files together for readability):

```xml
  <ItemGroup>
    <Compile Include="WebLinkTests.fs" />
    <Compile Include="HandlerBuilderTests.fs" />
    <Compile Include="ResourceBuilderMetadataTests.fs" />
    <Compile Include="MiddlewareOrderingTests.fs" />
    <Compile Include="MetadataTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "WebLink.format"`
Expected: **compile error** — `Frank.Builder` has no `WebLink` type yet.

- [ ] **Step 3: Write minimal implementation**

Create `src/Frank/WebLink.fsi`:

```fsharp
namespace Frank.Builder

/// One RFC 8288 Link header entry.
type WebLink =
    { /// URI-Reference the link points at.
      Target: string
      /// The link relation type, e.g. "home", "service-desc".
      Rel: string
      /// Additional target attributes, e.g. "type", "title", "hreflang", in declaration order.
      Params: (string * string) list }

module WebLink =

    /// Formats one WebLink as an RFC 8288 field value, escaping backslashes
    /// and double quotes in quoted parameter values (rel and every param value).
    val format: link: WebLink -> string
```

Create `src/Frank/WebLink.fs`:

```fsharp
namespace Frank.Builder

type WebLink =
    { Target: string
      Rel: string
      Params: (string * string) list }

module WebLink =

    let private escapeParam (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let format (link: WebLink) : string =
        let paramStr =
            link.Params
            |> List.map (fun (name, value) -> "; " + name + "=\"" + escapeParam value + "\"")
            |> String.concat ""

        "<" + link.Target + ">; rel=\"" + escapeParam link.Rel + "\"" + paramStr
```

Add both to `src/Frank/Frank.fsproj`, right before `ResourceBuilder.fsi` (its first consumer will be Task 4's `ResourceBuilder.fs`; `WebHostBuilder.fs` needs it too, so it must compile before both):

```xml
  <ItemGroup>
    <Compile Include="ContentNegotiation.fsi" />
    <Compile Include="ContentNegotiation.fs" />
    <Compile Include="HandlerDefinition.fsi" />
    <Compile Include="HandlerDefinition.fs" />
    <Compile Include="HandlerBuilder.fsi" />
    <Compile Include="HandlerBuilder.fs" />
    <Compile Include="WebLink.fsi" />
    <Compile Include="WebLink.fs" />
    <Compile Include="ResourceBuilder.fsi" />
    <Compile Include="ResourceBuilder.fs" />
    <Compile Include="WebHostBuilder.fsi" />
    <Compile Include="WebHostBuilder.fs" />
  </ItemGroup>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "WebLink.format"`
Expected: PASS, all 6 cases.

- [ ] **Step 5: Verify multi-target build**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: succeeds for `net8.0`, `net9.0`, and `net10.0` (no `-f` builds all three).

- [ ] **Step 6: Commit**

```bash
git add src/Frank/WebLink.fsi src/Frank/WebLink.fs src/Frank/Frank.fsproj test/Frank.Tests/WebLinkTests.fs test/Frank.Tests/Frank.Tests.fsproj
git commit -m "feat(frank): add WebLink type and RFC 8288 formatter"
```

---

### Task 2: Response-link middleware installers (`useAppWideLinks`, `useResourceScopedLinks`)

**Files:**
- Modify: `src/Frank/WebLink.fsi`
- Modify: `src/Frank/WebLink.fs`
- Create: `test/Frank.Tests/ResponseLinkTests.fs`
- Modify: `test/Frank.Tests/Frank.Tests.fsproj`

**Interfaces:**
- Consumes: `WebLink`, `WebLink.format` (Task 1).
- Produces:
  - `val WebLink.useAppWideLinks : providers: (HttpContext -> WebLink seq) list -> app: IApplicationBuilder -> IApplicationBuilder` (public — consumed by `WebHostBuilder.fs` in Task 3).
  - `val WebLink.useResourceScopedLinks : app: IApplicationBuilder -> IApplicationBuilder` (public — consumed by `WebHostBuilder.fs` in Task 3).
  - `type internal ResourceLinkProvider = ResourceLinkProvider of (HttpContext -> WebLink seq)` (internal — consumed by `ResourceBuilder.fs` in Task 4, which is in the same `Frank` assembly).

This task can only exercise `useAppWideLinks` fully and `useResourceScopedLinks`'s no-match path — the "endpoint has resource-scoped metadata" path needs `ResourceBuilder.link` (Task 4) to attach real metadata, since `ResourceLinkProvider` is `internal` and this test project is a separate assembly (matching the existing pattern in this codebase of test projects not using `InternalsVisibleTo`, e.g. `ResourceEndpointDataSource` is duplicated as `TestEndpointDataSource` in every test project rather than exposed).

- [ ] **Step 1: Write the failing test**

Create `test/Frank.Tests/ResponseLinkTests.fs`:

```fsharp
module Frank.Tests.ResponseLinkTests

open System
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder

/// Wires WebLink.useAppWideLinks and WebLink.useResourceScopedLinks the same
/// way WebHostBuilder.Run will (Task 3) -- before and after UseRouting,
/// respectively -- without going through the webHost {} CE, since Run blocks.
let private createTestServer (providers: (HttpContext -> WebLink seq) list) =
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                    .Configure(fun app ->
                        app
                        |> WebLink.useAppWideLinks providers
                        |> fun app -> app.UseRouting()
                        |> WebLink.useResourceScopedLinks
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                endpoints.MapGet(
                                    "/test",
                                    Func<HttpContext, Task>(fun ctx -> ctx.Response.WriteAsync "OK"))
                                |> ignore)
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

let private createTestServerWithExceptionHandler (providers: (HttpContext -> WebLink seq) list) =
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                    .Configure(fun app ->
                        app.UseExceptionHandler(fun errApp ->
                            errApp.Run(fun ctx ->
                                ctx.Response.StatusCode <- 500
                                ctx.Response.WriteAsync "error"))
                        |> ignore

                        app
                        |> WebLink.useAppWideLinks providers
                        |> fun app -> app.Run(fun _ -> failwith "boom"))
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

[<Tests>]
let appWideLinkTests =
    testList "WebLink.useAppWideLinks" [
        testTask "no providers registered adds no Link header" {
            let client = createTestServer []
            let! (response: HttpResponseMessage) = client.GetAsync("/test")
            Expect.isFalse (response.Headers.Contains "Link") "No Link header"
        }

        testTask "a single provider's link appears on the response" {
            let providers = [ fun (_: HttpContext) -> Seq.singleton { Target = "/x"; Rel = "x"; Params = [] } ]
            let client = createTestServer providers
            let! (response: HttpResponseMessage) = client.GetAsync("/test")
            Expect.isTrue (response.Headers.Contains "Link") "Link header present"
            Expect.contains (response.Headers.GetValues "Link" |> List.ofSeq) "</x>; rel=\"x\"" "Correct value"
        }

        testTask "two providers combine into one Link header carrying both values" {
            let providers =
                [ fun (_: HttpContext) -> Seq.singleton { Target = "/a"; Rel = "a"; Params = [] }
                  fun (_: HttpContext) -> Seq.singleton { Target = "/b"; Rel = "b"; Params = [] } ]
            let client = createTestServer providers
            let! (response: HttpResponseMessage) = client.GetAsync("/test")
            let values = response.Headers.GetValues "Link" |> List.ofSeq
            Expect.contains values "</a>; rel=\"a\"" "First provider's value present"
            Expect.contains values "</b>; rel=\"b\"" "Second provider's value present"
        }

        testTask "a provider returning an empty sequence contributes nothing" {
            let providers = [ fun (_: HttpContext) -> Seq.empty ]
            let client = createTestServer providers
            let! (response: HttpResponseMessage) = client.GetAsync("/test")
            Expect.isFalse (response.Headers.Contains "Link") "No Link header from an empty contribution"
        }

        testTask "app-wide links appear on an unmatched route (404)" {
            let providers = [ fun (_: HttpContext) -> Seq.singleton { Target = "/x"; Rel = "x"; Params = [] } ]
            let client = createTestServer providers
            let! (response: HttpResponseMessage) = client.GetAsync("/nope")
            Expect.isTrue (response.Headers.Contains "Link") "Link header present on a 404"
        }

        testTask "app-wide links survive UseExceptionHandler regenerating the response" {
            let providers = [ fun (_: HttpContext) -> Seq.singleton { Target = "/x"; Rel = "x"; Params = [] } ]
            let client = createTestServerWithExceptionHandler providers
            let! (response: HttpResponseMessage) = client.GetAsync("/boom")
            Expect.equal (int response.StatusCode) 500 "Exception handler produced the response"
            Expect.isTrue (response.Headers.Contains "Link") "Link header survives Response.Clear()"
        }
    ]
```

Add to `test/Frank.Tests/Frank.Tests.fsproj`, right after `WebLinkTests.fs`:

```xml
    <Compile Include="WebLinkTests.fs" />
    <Compile Include="ResponseLinkTests.fs" />
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "WebLink.useAppWideLinks"`
Expected: **compile error** — `WebLink.useAppWideLinks`/`useResourceScopedLinks` don't exist yet.

- [ ] **Step 3: Write minimal implementation**

Update `src/Frank/WebLink.fsi`:

```fsharp
namespace Frank.Builder

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

/// One RFC 8288 Link header entry.
type WebLink =
    { /// URI-Reference the link points at.
      Target: string
      /// The link relation type, e.g. "home", "service-desc".
      Rel: string
      /// Additional target attributes, e.g. "type", "title", "hreflang", in declaration order.
      Params: (string * string) list }

/// Marks an endpoint-metadata entry as a resource-scoped Link contribution.
/// Internal: ResourceBuilder.fs attaches these to EndpointBuilder.Metadata;
/// WebLink.useResourceScopedLinks reads them back at request time. Not part
/// of the public authoring surface -- callers only see ResourceBuilder's
/// `link` operation.
type internal ResourceLinkProvider = ResourceLinkProvider of (HttpContext -> WebLink seq)

module WebLink =

    /// Formats one WebLink as an RFC 8288 field value, escaping backslashes
    /// and double quotes in quoted parameter values (rel and every param value).
    val format: link: WebLink -> string

    /// Installs app-wide Link contributions. On each request, calls every
    /// provider and, if any returned at least one WebLink, appends them all
    /// as a single Link header via Response.OnStarting -- surviving
    /// exception-handling middleware regenerating the response, and still
    /// applying to responses for unmatched routes. Splice this in before
    /// BeforeRoutingMiddleware runs.
    val useAppWideLinks: providers: (HttpContext -> WebLink seq) list -> app: IApplicationBuilder -> IApplicationBuilder

    /// Installs resource-scoped Link contributions. Reads the matched
    /// endpoint's metadata (populated by ResourceBuilder's `link` operation)
    /// and, if any resource-scoped providers are present, appends their
    /// links the same way useAppWideLinks does. A request matching no
    /// endpoint contributes nothing. Splice this in after UseRouting runs
    /// and before Middleware runs, since it needs the matched endpoint.
    val useResourceScopedLinks: app: IApplicationBuilder -> IApplicationBuilder
```

Update `src/Frank/WebLink.fs`:

```fsharp
namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives

type WebLink =
    { Target: string
      Rel: string
      Params: (string * string) list }

type internal ResourceLinkProvider = ResourceLinkProvider of (HttpContext -> WebLink seq)

module WebLink =

    let private escapeParam (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let format (link: WebLink) : string =
        let paramStr =
            link.Params
            |> List.map (fun (name, value) -> "; " + name + "=\"" + escapeParam value + "\"")
            |> String.concat ""

        "<" + link.Target + ">; rel=\"" + escapeParam link.Rel + "\"" + paramStr

    let private appendToResponse (ctx: HttpContext) (links: WebLink list) =
        if not (List.isEmpty links) then
            ctx.Response.OnStarting(fun () ->
                let values = links |> List.map format |> Array.ofList
                ctx.Response.Headers.Append("Link", StringValues values)
                Task.CompletedTask)
            |> ignore

    let useAppWideLinks
        (providers: (HttpContext -> WebLink seq) list)
        (app: IApplicationBuilder)
        : IApplicationBuilder =
        if List.isEmpty providers then
            app
        else
            app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                let links = [ for provider in providers do yield! provider ctx ]
                appendToResponse ctx links
                next.Invoke ctx)

    let useResourceScopedLinks (app: IApplicationBuilder) : IApplicationBuilder =
        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
            match ctx.GetEndpoint() with
            | null -> ()
            | endpoint ->
                let providers = endpoint.Metadata.GetOrderedMetadata<ResourceLinkProvider>()
                if providers.Count > 0 then
                    let links = [ for ResourceLinkProvider provider in providers do yield! provider ctx ]
                    appendToResponse ctx links

            next.Invoke ctx)
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "WebLink.useAppWideLinks"`
Expected: PASS, all 6 cases.

- [ ] **Step 5: Verify multi-target build**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: succeeds for `net8.0`, `net9.0`, and `net10.0`.

- [ ] **Step 6: Commit**

```bash
git add src/Frank/WebLink.fsi src/Frank/WebLink.fs test/Frank.Tests/ResponseLinkTests.fs test/Frank.Tests/Frank.Tests.fsproj
git commit -m "feat(frank): add app-wide and resource-scoped Link middleware installers"
```

---

### Task 3: `WebHostSpec.LinkProviders` + `link` CE operation + `Run()` wiring

**Files:**
- Modify: `src/Frank/WebHostBuilder.fsi`
- Modify: `src/Frank/WebHostBuilder.fs`
- Modify: `test/Frank.Tests/ResponseLinkTests.fs`

**Interfaces:**
- Consumes: `WebLink`, `WebLink.useAppWideLinks`, `WebLink.useResourceScopedLinks` (Task 1/2).
- Produces:
  - `WebHostSpec.LinkProviders: (HttpContext -> WebLink seq) list` (new field, default `[]`).
  - `WebHostBuilder.Link(spec, provider: HttpContext -> WebLink seq) : WebHostSpec` and `WebHostBuilder.Link(spec, target: string, rel: string) : WebHostSpec`, both under `[<CustomOperation("link")>]` — consumed by application code and, in Task 5/6, by `Frank.JsonHome`/`Frank.OpenApi`.
  - `WebHostBuilder.Run()`'s pipeline now includes `useAppWideLinks`/`useResourceScopedLinks` — consumed implicitly by every app using `webHost { }`.

- [ ] **Step 1: Write the failing test**

First, extend `test/Frank.Tests/ResponseLinkTests.fs`'s existing `open` list at the top of the file (`Microsoft.AspNetCore.Routing` for `Endpoint`/`RouteEndpointBuilder`/`Patterns.RoutePatternFactory`/`EndpointDataSource`; `Microsoft.Extensions.FileProviders` for `NullChangeToken`, matching the exact import `src/Frank/ResourceBuilder.fs`'s own `ResourceEndpointDataSource` relies on):

```fsharp
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.FileProviders
```

Then, still above the existing `[<Tests>]` blocks from Task 1/2 (order relative to those doesn't matter, but this type and function must precede their first use), add a test-local `TestEndpointDataSource` — `ResourceEndpointDataSource` is internal to `Frank`, so every Frank test project defines its own copy rather than reaching for it (the same pattern `test/Frank.OpenApi.Tests/OpenApiDocumentTests.fs` and `test/Frank.JsonHome.Tests/IntegrationTests.fs` already use):

```fsharp
type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _
```

Then add the harness function and the new test list, appended after `test/Frank.Tests/ResponseLinkTests.fs`'s existing content:

```fsharp
/// Mirrors WebHostBuilder.Run's pipeline shape exactly (Run blocks, so tests
/// wire it by hand), letting a test configure the spec via the real CE and
/// register extra resources the way an app would.
let private createFullPipelineTestServer (configureSpec: WebHostSpec -> WebHostSpec) (resources: Resource list) =
    let spec = WebHostSpec.Empty |> configureSpec
    let testEndpoint =
        RouteEndpointBuilder(
            RequestDelegate(fun ctx -> ctx.Response.WriteAsync "OK"),
            Patterns.RoutePatternFactory.Parse "/test",
            0)
            .Build()
    let allEndpoints =
        testEndpoint :: (resources |> List.collect (fun r -> List.ofArray r.Endpoints))
        |> Array.ofList

    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        spec.Services services |> ignore)
                    .Configure(fun app ->
                        app
                        |> WebLink.useAppWideLinks spec.LinkProviders
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> WebLink.useResourceScopedLinks
                        |> spec.Middleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

[<Tests>]
let webHostLinkOperationTests =
    testList "WebHostBuilder link operation" [
        testCase "link target rel appends a provider that always returns that link" (fun () ->
            let builder = WebHostBuilder([||])
            let spec = builder.Link(WebHostSpec.Empty, "/x", "x")
            Expect.equal (List.length spec.LinkProviders) 1 "One provider registered"
            let links = spec.LinkProviders.[0] null |> List.ofSeq
            Expect.equal links [ { Target = "/x"; Rel = "x"; Params = [] } ] "Static provider produces the configured link")

        testCase "link with a general provider appends it as-is" (fun () ->
            let builder = WebHostBuilder([||])
            let provider = fun (_: HttpContext) -> Seq.singleton { Target = "/y"; Rel = "y"; Params = [] }
            let spec = builder.Link(WebHostSpec.Empty, provider)
            Expect.equal (List.length spec.LinkProviders) 1 "One provider registered"
            Expect.equal (spec.LinkProviders.[0] null |> List.ofSeq) [ { Target = "/y"; Rel = "y"; Params = [] } ] "Provider unchanged")

        testCase "two link calls accumulate, not overwrite" (fun () ->
            let builder = WebHostBuilder([||])
            let spec =
                WebHostSpec.Empty
                |> fun s -> builder.Link(s, "/x", "x")
                |> fun s -> builder.Link(s, "/y", "y")
            Expect.equal (List.length spec.LinkProviders) 2 "Both providers registered")

        testTask "a response carries a link registered via the webHost CE's link operation" {
            let configure (spec: WebHostSpec) = (WebHostBuilder([||])).Link(spec, "/x", "x")
            let client = createFullPipelineTestServer configure []
            let! (response: HttpResponseMessage) = client.GetAsync("/test")
            Expect.contains (response.Headers.GetValues "Link" |> List.ofSeq) "</x>; rel=\"x\"" "Link header present with configured value"
        }
    ]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "WebHostBuilder link operation"`
Expected: **compile error** — `WebHostSpec` has no `LinkProviders` field and `WebHostBuilder` has no `Link` member yet.

- [ ] **Step 3: Write minimal implementation**

Update `src/Frank/WebHostBuilder.fsi` — add the field and two overloads:

```fsharp
type WebHostSpec =
    { Host: (IWebHostBuilder -> IWebHostBuilder)
      BeforeRoutingMiddleware: (IApplicationBuilder -> IApplicationBuilder)
      Middleware: (IApplicationBuilder -> IApplicationBuilder)
      Endpoints: Endpoint[]
      Services: (IServiceCollection -> IServiceCollection)
      LinkProviders: (HttpContext -> WebLink seq) list
      UseDefaults: bool }

    static member Empty: WebHostSpec
```

and, among the `[<CustomOperation>]` members (placement doesn't matter functionally; put it alphabetically-ish near `plug`):

```fsharp
    [<CustomOperation("link")>]
    member Link: spec: WebHostSpec * provider: (HttpContext -> WebLink seq) -> WebHostSpec

    member Link: spec: WebHostSpec * target: string * rel: string -> WebHostSpec
```

Update `src/Frank/WebHostBuilder.fs` — add the field to the record and `Empty`:

```fsharp
type WebHostSpec =
    { Host: (IWebHostBuilder -> IWebHostBuilder)
      BeforeRoutingMiddleware: (IApplicationBuilder -> IApplicationBuilder)
      Middleware: (IApplicationBuilder -> IApplicationBuilder)
      Endpoints: Endpoint[]
      Services: (IServiceCollection -> IServiceCollection)
      LinkProviders: (HttpContext -> WebLink seq) list
      UseDefaults: bool }

    static member Empty =
        { Host = id
          BeforeRoutingMiddleware = id
          Middleware = id
          Endpoints = [||]
          Services =
            (fun services ->
                services.AddMvcCore(fun options -> options.ReturnHttpNotAcceptable <- true)
                |> ignore

                services)
          LinkProviders = []
          UseDefaults = false }
```

add the CE members (near `Plug`):

```fsharp
    [<CustomOperation("link")>]
    member __.Link(spec: WebHostSpec, provider: HttpContext -> WebLink seq) : WebHostSpec =
        { spec with LinkProviders = spec.LinkProviders @ [ provider ] }

    member __.Link(spec: WebHostSpec, target: string, rel: string) : WebHostSpec =
        __.Link(spec, fun (_: HttpContext) -> Seq.singleton { Target = target; Rel = rel; Params = [] })
```

and splice the two middlewares into `Run()`:

```fsharp
    member __.Run(spec: WebHostSpec) =
        let builder = Host.CreateDefaultBuilder(args)

        let config =
            Action<_>(fun webBuilder ->
                spec
                    .Host(webBuilder)
                    .ConfigureServices(spec.Services >> ignore)
                    .Configure(fun app ->
                        app
                        |> WebLink.useAppWideLinks spec.LinkProviders
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> WebLink.useResourceScopedLinks
                        |> spec.Middleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                let dataSource = ResourceEndpointDataSource(spec.Endpoints)
                                endpoints.DataSources.Add(dataSource))
                        |> ignore)
                |> ignore)

        let configured =
            if spec.UseDefaults then
                builder.ConfigureWebHostDefaults(config)
            else
                builder.ConfigureWebHost(config)

        configured.Build().Run()
```

(Only the `.Configure(fun app -> ...)` body changes; everything else in `Run()` stays as-is.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "WebHostBuilder link operation"`
Expected: PASS, all 4 cases. Also re-run Task 1/2's filters to confirm no regression: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`.

- [ ] **Step 5: Verify multi-target build**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: succeeds for `net8.0`, `net9.0`, and `net10.0`.

- [ ] **Step 6: Commit**

```bash
git add src/Frank/WebHostBuilder.fsi src/Frank/WebHostBuilder.fs test/Frank.Tests/ResponseLinkTests.fs
git commit -m "feat(frank): add WebHostSpec.LinkProviders and the link CE operation"
```

---

### Task 4: `ResourceBuilder.link` operation (resource-scoped wiring)

**Files:**
- Modify: `src/Frank/ResourceBuilder.fsi`
- Modify: `src/Frank/ResourceBuilder.fs`
- Modify: `test/Frank.Tests/ResponseLinkTests.fs`

**Interfaces:**
- Consumes: `WebLink`, `ResourceLinkProvider` (internal, Task 1/2), `ResourceBuilder.AddMetadata` (existing).
- Produces: `ResourceBuilder.Link(spec, provider: HttpContext -> WebLink seq) : ResourceSpec` and `ResourceBuilder.Link(spec, target: string, rel: string) : ResourceSpec`, both under `[<CustomOperation("link")>]` — consumed by application code writing `resource { }` blocks.

- [ ] **Step 1: Write the failing test**

Append to `test/Frank.Tests/ResponseLinkTests.fs`:

```fsharp
let private ok: RequestDelegate = RequestDelegate(fun ctx -> ctx.Response.WriteAsync "OK")

[<Tests>]
let resourceScopedLinkTests =
    testList "ResourceBuilder link operation" [
        testTask "a resource-scoped link appears only on that resource's responses" {
            let a =
                resource "/a" {
                    link "/alt-a" "alternate"
                    get ok
                }
            let b =
                resource "/b" {
                    get ok
                }
            let client = createFullPipelineTestServer id [ a; b ]

            let! (respA: HttpResponseMessage) = client.GetAsync("/a")
            let! (respB: HttpResponseMessage) = client.GetAsync("/b")

            Expect.contains (respA.Headers.GetValues "Link" |> List.ofSeq) "</alt-a>; rel=\"alternate\"" "Resource A carries its own link"
            Expect.isFalse (respB.Headers.Contains "Link") "Resource B carries no link"
        }

        testTask "resource-scoped and app-wide links combine on the same response" {
            let a =
                resource "/a" {
                    link "/alt-a" "alternate"
                    get ok
                }
            let configure (spec: WebHostSpec) = (WebHostBuilder([||])).Link(spec, "/home.json", "home")
            let client = createFullPipelineTestServer configure [ a ]

            let! (resp: HttpResponseMessage) = client.GetAsync("/a")
            let values = resp.Headers.GetValues "Link" |> List.ofSeq

            Expect.contains values "</alt-a>; rel=\"alternate\"" "Resource-scoped entry present"
            Expect.contains values "</home.json>; rel=\"home\"" "App-wide entry present"
        }

        testTask "a resource-scoped link never appears on an unmatched route" {
            let a =
                resource "/a" {
                    link "/alt-a" "alternate"
                    get ok
                }
            let client = createFullPipelineTestServer id [ a ]

            let! (resp: HttpResponseMessage) = client.GetAsync("/nope")
            Expect.isFalse (resp.Headers.Contains "Link") "404 carries no resource-scoped link"
        }
    ]
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "ResourceBuilder link operation"`
Expected: **compile error** — `ResourceBuilder` has no `Link`/`link` member yet.

- [ ] **Step 3: Write minimal implementation**

Update `src/Frank/ResourceBuilder.fsi` — add near `AddMetadata`/the other `[<CustomOperation>]` members. **Both overloads must carry `[<CustomOperation("link")>]`** — Task 3 shipped this bug for `WebHostBuilder.Link` (only the `provider` overload attributed) and a review caught it: an unattributed overload of a *different arity* than the attributed one is not reachable through CE keyword syntax at all (verified empirically; `link "url" "rel"` failed to compile with `FS3099`), it's only callable as a plain method. The fix, confirmed against this codebase's own existing precedent (`useOpenApi` in `src/Frank.OpenApi/WebHostBuilderExtensions.fs:60,73` and `datastar` in `src/Frank.Datastar/Frank.Datastar.fs:38,60`, both of which attribute *every* overload of a multi-arity CE keyword), is to attribute both:

```fsharp
    [<CustomOperation("link")>]
    member Link: spec: ResourceSpec * target: string * rel: string -> ResourceSpec

    [<CustomOperation("link")>]
    member Link: spec: ResourceSpec * provider: (HttpContext -> WebLink seq) -> ResourceSpec
```

Update `src/Frank/ResourceBuilder.fs` — add near `AddMetadata`:

```fsharp
    [<CustomOperation("link")>]
    member __.Link(spec: ResourceSpec, target: string, rel: string) : ResourceSpec =
        __.Link(spec, fun (_: HttpContext) -> Seq.singleton { Target = target; Rel = rel; Params = [] })

    [<CustomOperation("link")>]
    member __.Link(spec: ResourceSpec, provider: HttpContext -> WebLink seq) : ResourceSpec =
        ResourceBuilder.AddMetadata(spec, fun builder -> builder.Metadata.Add(ResourceLinkProvider provider))
```

No changes needed to `ResourceSpec.Build()` — it already runs every `Metadata` convention against `builder` (line `for convention in metadata do convention builder`), which is exactly what adds the `ResourceLinkProvider` to the built endpoint's metadata.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "ResourceBuilder link operation"`
Expected: PASS, all 3 cases. Then run the whole project: `dotnet test test/Frank.Tests/Frank.Tests.fsproj` — expect all green, no regressions.

- [ ] **Step 5: Verify multi-target build**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: succeeds for `net8.0`, `net9.0`, and `net10.0`.

- [ ] **Step 6: Commit**

```bash
git add src/Frank/ResourceBuilder.fsi src/Frank/ResourceBuilder.fs test/Frank.Tests/ResponseLinkTests.fs
git commit -m "feat(frank): add ResourceBuilder link operation for resource-scoped Link contributions"
```

---

### Task 5: Migrate `Frank.JsonHome`

**Files:**
- Modify: `src/Frank.JsonHome/JsonHome.fsi`
- Modify: `src/Frank.JsonHome/JsonHome.fs`
- Modify: `src/Frank.JsonHome/WebHostBuilderExtensions.fs`
- Modify: `test/Frank.JsonHome.Tests/IntegrationTests.fs`

**Interfaces:**
- Consumes: `WebHostSpec.LinkProviders` (Task 3).
- Produces: `useJsonHome` (existing CE operation) now populates `LinkProviders` instead of `BeforeRoutingMiddleware`; `JsonHome.linkHeaderMiddleware` no longer exists.

This task has no new test *cases* — the existing `IntegrationTests.fs` suite (in particular "advertises the document with a Link header, including on 404s" and "the Link header survives an exception handler clearing the response") already asserts the exact header values this migration must keep producing. Making them compile and pass again against the migrated implementation **is** the migration-equivalence check.

- [ ] **Step 1: Confirm the current suite passes before touching anything**

Run: `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`
Expected: PASS (baseline, before migration).

- [ ] **Step 2: Delete `linkHeaderMiddleware` and its private `escapeParam`**

In `src/Frank.JsonHome/JsonHome.fs`, delete the `escapeParam` and `linkHeaderMiddleware` bindings (currently just above `documentHandler`):

```fsharp
    /// RFC 8288 parameter values are quoted strings, so a backslash or quote in
    /// the relation type has to be escaped.
    let private escapeParam (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let linkHeaderMiddleware (options: JsonHomeOptions) =
        // ... (entire body)
```

Remove the now-unused `open Microsoft.Extensions.Primitives` line at the top of the file (only used by the deleted code).

In `src/Frank.JsonHome/JsonHome.fsi`, delete the corresponding `val linkHeaderMiddleware : ...` entry and its doc comment.

- [ ] **Step 3: Rewire `install` in `WebHostBuilderExtensions.fs`**

In `src/Frank.JsonHome/WebHostBuilderExtensions.fs`, replace:

```fsharp
            BeforeRoutingMiddleware =
                spec.BeforeRoutingMiddleware
                >> fun app ->
                    // Both lambda parameters must be annotated: IApplicationBuilder.Use has
                    // Func<HttpContext, Func<Task>, Task> and Func<HttpContext, RequestDelegate, Task>
                    // overloads that F# cannot choose between otherwise.
                    app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                        runLinkHeader ctx (fun () -> next.Invoke ctx))
```

with:

```fsharp
            LinkProviders =
                spec.LinkProviders
                @ [ fun (_: HttpContext) -> Seq.singleton { Target = options.Path; Rel = options.Rel; Params = [] } ]
```

and delete the now-unused `let runLinkHeader = JsonHome.linkHeaderMiddleware options` line above it.

- [ ] **Step 4: Rewrite the two `IntegrationTests.fs` harnesses that called `linkHeaderMiddleware` directly**

In `test/Frank.JsonHome.Tests/IntegrationTests.fs`, replace `createServer`:

```fsharp
let private createServer (homeOptions: JsonHomeOptions) (resources: Resource list) =
    // Same composition useJsonHome performs: the document is one more
    // resource, dispatched through the same routing/UseEndpoints stage as
    // everything else -- after UseAuthentication/UseAuthorization, not before.
    let spec = (webHost [||]).UseJsonHome(WebHostSpec.Empty, fun _ -> homeOptions)
    let endpoints =
        (List.ofArray spec.Endpoints @ (resources |> List.collect (fun r -> List.ofArray r.Endpoints)))
        |> Array.ofList

    let host =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        spec.Services services |> ignore

                        services
                            .AddAuthentication(TestScheme)
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, fun _ -> ())
                        |> ignore

                        services.AddAuthorization() |> ignore

                        // ApiExplorer discovers endpoints through registered data sources.
                        services.AddSingleton<EndpointDataSource>(TestEndpointDataSource endpoints)
                        |> ignore)
                    .Configure(fun app ->
                        // The same middleware useJsonHome installs. WebHostBuilder.Run
                        // builds and blocks, so the pipeline is wired by hand, but the
                        // code under test is the real thing rather than a copy.
                        app
                        |> WebLink.useAppWideLinks spec.LinkProviders
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> WebLink.useResourceScopedLinks
                        |> fun app ->
                            app
                                .UseAuthentication()
                                .UseAuthorization()
                                .UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                        |> ignore)
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()
```

and `createFailingServer`:

```fsharp
/// A minimal pipeline with UseExceptionHandler ahead of the link-header
/// middleware, mirroring a standard production setup, and a handler that
/// always throws.
let private createFailingServer () =
    let spec = (webHost [||]).UseJsonHome(WebHostSpec.Empty)

    let host =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .Configure(fun app ->
                        app.UseExceptionHandler(fun errApp ->
                            errApp.Run(fun ctx ->
                                ctx.Response.StatusCode <- 500
                                ctx.Response.WriteAsync "error"))
                        |> ignore

                        app
                        |> WebLink.useAppWideLinks spec.LinkProviders
                        |> fun app -> app.Run(fun _ -> failwith "boom"))
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()
```

(`ok`/other test bodies are unchanged; only these two harness functions move off `JsonHome.linkHeaderMiddleware`.)

- [ ] **Step 5: Run the full JsonHome suite**

Run: `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`
Expected: PASS — same assertions as the Step 1 baseline, now exercising the migrated code path. In particular, confirm:
- "advertises the document with a Link header, including on 404s" — same `"</.well-known/home.json>; rel=\"home\""` value.
- "the Link header survives an exception handler clearing the response" — still 500 + `Link` present.
- "a configured path, rel, title, and links all take effect" — still `"</discovery.json>; rel=\"discovery\""`.

- [ ] **Step 6: Verify multi-target build**

Run: `dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj`
Expected: succeeds for `net8.0`, `net9.0`, and `net10.0`.

- [ ] **Step 7: Commit**

```bash
git add src/Frank.JsonHome/JsonHome.fsi src/Frank.JsonHome/JsonHome.fs src/Frank.JsonHome/WebHostBuilderExtensions.fs test/Frank.JsonHome.Tests/IntegrationTests.fs
git commit -m "refactor(json-home): migrate onto the shared WebLink mechanism, delete the private middleware"
```

---

### Task 6: Migrate `Frank.OpenApi`

**Files:**
- Modify: `src/Frank.OpenApi/WebHostBuilderExtensions.fs`
- Modify: `test/Frank.OpenApi.Tests/ServiceDescLinkTests.fs`

**Interfaces:**
- Consumes: `WebHostSpec.LinkProviders` (Task 3).
- Produces: `useOpenApi` (existing CE operation, both overloads) now populates `LinkProviders` instead of `BeforeRoutingMiddleware`; `addServiceDescLinkHeader`/`serviceDescLinkHeaderValue` no longer exist.

As with Task 5, the existing `ServiceDescLinkTests.fs` suite already asserts the exact header value (`expectedLinkValue`) this migration must keep producing — making it compile and pass again is the migration-equivalence check. `Frank.OpenApi` is `net10.0`-only, so there's no `net8.0`/`net9.0` build to verify here.

- [ ] **Step 1: Confirm the current suite passes before touching anything**

Run: `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`
Expected: PASS (baseline, before migration).

- [ ] **Step 2: Delete `addServiceDescLinkHeader` and `serviceDescLinkHeaderValue`, rewire both `UseOpenApi` overloads**

In `src/Frank.OpenApi/WebHostBuilderExtensions.fs`, delete:

```fsharp
    let private serviceDescLinkHeaderValue =
        StringValues(sprintf "<%s>; rel=\"service-desc\"; type=\"application/json\"" openApiRoutePattern)

    let addServiceDescLinkHeader (app: IApplicationBuilder) =
        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
            ctx.Response.OnStarting(fun () ->
                ctx.Response.Headers.Append("Link", serviceDescLinkHeaderValue)
                Task.CompletedTask)
            |> ignore
            next.Invoke ctx)
```

Remove the now-unused `open Microsoft.Extensions.Primitives` line (only used by the deleted `StringValues` reference; `Task`/`Threading.Tasks` is still used elsewhere in this file and stays).

Replace both occurrences of:

```fsharp
                BeforeRoutingMiddleware = spec.BeforeRoutingMiddleware >> addServiceDescLinkHeader
```

(one in each `UseOpenApi` overload) with:

```fsharp
                LinkProviders =
                    spec.LinkProviders
                    @ [ fun (_: HttpContext) ->
                            Seq.singleton { Target = openApiRoutePattern; Rel = "service-desc"; Params = [ "type", "application/json" ] } ]
```

- [ ] **Step 3: Update the three `ServiceDescLinkTests.fs` harnesses**

In `test/Frank.OpenApi.Tests/ServiceDescLinkTests.fs`, `createRealUseOpenApiTestServer` and `createRealUseOpenApiWithConfigureTestServer` both currently do:

```fsharp
                    .Configure(fun app ->
                        spec.BeforeRoutingMiddleware app |> ignore
                        app.UseRouting() |> ignore
                        spec.Middleware app |> ignore
                        app.UseEndpoints(fun endpoints ->
                            endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
```

Replace each with:

```fsharp
                    .Configure(fun app ->
                        app
                        |> WebLink.useAppWideLinks spec.LinkProviders
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> WebLink.useResourceScopedLinks
                        |> spec.Middleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
```

`createRealUseOpenApiTestServerWithExceptionHandler` currently does:

```fsharp
                    .Configure(fun app ->
                        app.UseExceptionHandler(fun errApp ->
                            errApp.Run(fun ctx ->
                                ctx.Response.StatusCode <- 500
                                ctx.Response.WriteAsync("error")))
                        |> ignore
                        spec.BeforeRoutingMiddleware app |> ignore
                        app.UseRouting() |> ignore
                        spec.Middleware app |> ignore
                        app.UseEndpoints(fun endpoints ->
                            endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
```

Replace with:

```fsharp
                    .Configure(fun app ->
                        app.UseExceptionHandler(fun errApp ->
                            errApp.Run(fun ctx ->
                                ctx.Response.StatusCode <- 500
                                ctx.Response.WriteAsync("error")))
                        |> ignore

                        app
                        |> WebLink.useAppWideLinks spec.LinkProviders
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> WebLink.useResourceScopedLinks
                        |> spec.Middleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
```

Add `open Frank.Builder`'s `WebLink` — already covered since the file already has `open Frank.Builder`.

- [ ] **Step 4: Run the full OpenApi suite**

Run: `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`
Expected: PASS — same `expectedLinkValue` assertions as the Step 1 baseline, now exercising the migrated code path, including "the header survives an unhandled exception regenerating the response via UseExceptionHandler".

- [ ] **Step 5: Commit**

```bash
git add src/Frank.OpenApi/WebHostBuilderExtensions.fs test/Frank.OpenApi.Tests/ServiceDescLinkTests.fs
git commit -m "refactor(openapi): migrate onto the shared WebLink mechanism, delete the private middleware"
```

---

### Task 7: Full solution verification

**Files:** none (verification only; fix forward in the relevant task's files if this surfaces a problem).

- [ ] **Step 1: Build every affected project across every targeted TFM**

Run:
```bash
dotnet build src/Frank/Frank.fsproj
dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj
dotnet build src/Frank.OpenApi/Frank.OpenApi.fsproj
```
Expected: all succeed, including `net8.0`/`net9.0` for the first two (the most likely place for a signature mismatch invisible from `net10.0`-only test runs — e.g. an inferred `Task` vs `Task<unit>`).

- [ ] **Step 2: Run every affected test project**

Run:
```bash
dotnet test test/Frank.Tests/Frank.Tests.fsproj
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj
dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj
```
Expected: all green.

- [ ] **Step 3: Build the full solution**

Run: `dotnet build`
Expected: succeeds — catches any other consumer of the now-deleted `JsonHome.linkHeaderMiddleware`/`Frank.OpenApi.addServiceDescLinkHeader` this plan's grep didn't find (e.g. samples).

- [ ] **Step 4: If Step 3 finds a stray consumer, fix it in place and re-run Steps 1-3**

No commit for this task unless Step 4 required a fix — in that case, commit that fix with a message describing what it was (e.g. `fix(samples): update to the shared WebLink mechanism`).
