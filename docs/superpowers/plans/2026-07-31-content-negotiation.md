# Content Negotiation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Frank a real, tested, Frank-native content negotiation mechanism (`negotiate { }`) that supports independently-produced representations per media type, bridges to ASP.NET Core MVC's `IOutputFormatter` registry as an optional per-representation producer, and replaces the fake sample and untested `IOutputFormatter`-only mechanism from issue #482.

**Architecture:** A new `NegotiateBuilder` computation expression in `Frank.Builder` (alongside `HandlerBuilder`/`ResourceBuilder`) does `Accept`-header parsing and representation selection using `Microsoft.Net.Http.Headers` (no MVC dependency), producing a plain `HandlerDefinition` that plugs into `ResourceBuilder.Get`/`Post`/etc.'s existing `HandlerDefinition` overload with zero changes to `ResourceBuilder`. The existing `ContentNegotiation.fs` (`negotiate`/`ctx.Negotiate`, `IOutputFormatter`-based) is kept unchanged and gains one new function, `viaOutputFormatter`, usable as an ordinary producer inside `negotiate { }` for apps that want MVC's formatter registry for specific representations (JSON, XML) while using independent producers for others (JSON-LD, images).

**Tech Stack:** F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core), `Microsoft.Net.Http.Headers` (shared framework, not MVC) for dispatch, `Microsoft.AspNetCore.Mvc.Formatters`/`Infrastructure` (already used by the existing `ContentNegotiation.fs`) for the bridge, Expecto for tests, `FSharp.Analyzers.SDK` for the `Frank.Analyzers` extension.

## Global Constraints

- Every `.fs` file under `src/Frank.*/` gets a matching `.fsi` signature file, placed directly above it in the `.fsproj`'s `<Compile>` order. Internal members needed by another file in the same assembly are marked `internal` (not `private`) in both files; anything not needed elsewhere is simply omitted from the `.fsi`.
- Verify every change to `src/Frank/` and `src/Frank.Analyzers/` with a real build across all three target frameworks (`dotnet build <project>.fsproj` builds all TFMs in `<TargetFrameworks>` by default) — signature mismatches only surface at compile time, not by reading the code.
- `ResourceBuilder.fs`, `ResourceBuilder.fsi`, `WebHostBuilder.fs`, and `WebHostBuilder.fsi` are never modified by this plan — the whole mechanism plugs into `ResourceBuilder`'s existing `HandlerDefinition` overload.
- The existing `negotiate`/`ctx.Negotiate(statusCode, body)` functions in `ContentNegotiation.fs` keep their exact current signature and behavior — this plan only adds to that file, never edits their bodies.
- Design reference: `docs/superpowers/specs/2026-07-31-content-negotiation-design.md`. Read it before starting if anything below is unclear about *why* — this plan covers *what* and *how*.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `src/Frank/NegotiateBuilder.fsi` / `.fs` | New | `NegotiateSpec`, `NegotiateBuilder` CE, dispatch algorithm |
| `src/Frank/ContentNegotiation.fsi` / `.fs` | Modified | Adds `viaOutputFormatter`; existing `negotiate`/`ctx.Negotiate` untouched |
| `src/Frank/Frank.fsproj` | Modified | Insert `NegotiateBuilder.fsi`/`.fs` compile entries |
| `test/Frank.Tests/NegotiateBuilderTests.fs` | New | Dispatch, wildcard, auto-format, metadata-merge tests |
| `test/Frank.Tests/ContentNegotiationTests.fs` | New | `viaOutputFormatter` tests + first-ever tests for existing `negotiate`/`ctx.Negotiate` |
| `test/Frank.Tests/Frank.Tests.fsproj` | Modified | Add the two new test file compile entries + `Microsoft.Extensions.DependencyInjection`/MVC-formatter package references if needed |
| `test/Frank.OpenApi.Tests/NegotiateMetadataTests.fs` | New | Confirms `negotiate { }` metadata reaches the generated OpenAPI document |
| `test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj` | Modified | Add the new test file compile entry |
| `sample/Frank.OpenApi.Sample/Handlers.fs` | Modified | Fix `getProductNegotiated`; add `getProductBridged` |
| `sample/Frank.OpenApi.Sample/Program.fs` | Modified | Wire up `getProductBridged` |
| `src/Frank.Analyzers/DuplicateHandlerAnalyzer.fsi` / `.fs` | Modified | Add duplicate-`accepts`-media-type detection (`FRANK002`) |
| `test/Frank.Analyzers.Tests/fixtures/*.fs` | New | Fixtures for the new analyzer check |
| `test/Frank.Analyzers.Tests/Frank.Analyzers.Tests.fsproj` | Modified | Add new fixture compile entries |
| `test/Frank.Analyzers.Tests/run-analyzer-tests.sh` | Modified | Add `check_test` calls for the new fixtures |
| `src/Frank/NegotiateBuilder.fsi` / `.fs` | Modified (Task 8) | `Run` merges same-`(StatusCode, Type)` `IProducesResponseTypeMetadata` entries into one before building the final `HandlerDefinition` |

**Task 8 addendum (added after Task 5 ran):** Task 5's own verification found that its central claim doesn't hold for the common case — see the addendum after Task 7 below for the full story and the fix.

---

## Task 1: `NegotiateBuilder` core — direct producers, dispatch, wildcard matching

**Files:**
- Create: `src/Frank/NegotiateBuilder.fsi`
- Create: `src/Frank/NegotiateBuilder.fs`
- Modify: `src/Frank/Frank.fsproj`
- Create: `test/Frank.Tests/NegotiateBuilderTests.fs`
- Modify: `test/Frank.Tests/Frank.Tests.fsproj`

**Interfaces:**
- Consumes: `Frank.Builder.HandlerDefinition` (`{ Handler: RequestDelegate; Metadata: obj list }`, from `HandlerDefinition.fs`, already compiled before this file).
- Produces: `Frank.Builder.NegotiateSpec` (`{ Representations: (string * RequestDelegate) list; Metadata: obj list }`), `Frank.Builder.NegotiateBuilder` (the `negotiate { }` CE with an `accepts` custom operation), `Frank.Builder.NegotiateFunctions.negotiate: NegotiateBuilder`. Task 3 adds two more `Accepts` overloads to this same type; Task 4 adds two more after that.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Tests/NegotiateBuilderTests.fs`:

```fsharp
module Frank.Tests.NegotiateBuilderTests

open System.IO
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Builder

let createMockContext () =
    let context = DefaultHttpContext()
    let responseStream = new MemoryStream()
    context.Response.Body <- responseStream
    context

let setAccept (ctx: HttpContext) (value: string) =
    ctx.Request.Headers.Accept <- Microsoft.Extensions.Primitives.StringValues(value)

let getResponseBody (ctx: HttpContext) =
    ctx.Response.Body.Position <- 0L
    use reader = new StreamReader(ctx.Response.Body)
    reader.ReadToEnd()

let writeText (text: string) (ctx: HttpContext) : Task =
    task { do! ctx.Response.WriteAsync(text) }

[<Tests>]
let tests =
    testList
        "NegotiateBuilder"
        [ testCase "selects the representation matching an exact Accept header"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.ContentType "application/json" "Content-Type should match the winning representation"
              Expect.equal (getResponseBody ctx) "json" "Body should come from the JSON representation"

          testCase "quality values pick the higher-preference representation"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "text/html;q=0.3, application/json;q=0.8"

              let def =
                  negotiate {
                      accepts "text/html" (writeText "html")
                      accepts "application/json" (writeText "json")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal (getResponseBody ctx) "json" "Higher quality value should win regardless of registration order"

          testCase "responds 406 with no body when nothing matches"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/xml"

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.StatusCode 406 "Should be Not Acceptable"
              Expect.equal (getResponseBody ctx) "" "No body should be written"

          testCase "absent Accept header selects the first-registered representation"
          <| fun () ->
              let ctx = createMockContext ()

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal (getResponseBody ctx) "json" "First-registered representation is the default"

          testCase "Accept: */* selects the first-registered representation"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "*/*"

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal (getResponseBody ctx) "json" "Wildcard Accept resolves the same way as absent"

          testCase "a malformed Accept header falls back to the first-registered representation"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "not a media type at all;;;"

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.StatusCode 200 "Should not be a 500"
              Expect.equal (getResponseBody ctx) "json" "Falls back to the default representation"

          testCase "only the selected representation's producer runs"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"
              let mutable htmlRan = false
              let mutable jsonRan = false

              let def =
                  negotiate {
                      accepts "application/json" (fun (ctx: HttpContext) -> jsonRan <- true; writeText "json" ctx)
                      accepts "text/html" (fun (ctx: HttpContext) -> htmlRan <- true; writeText "html" ctx)
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.isTrue jsonRan "Selected representation's producer should run"
              Expect.isFalse htmlRan "Non-selected representation's producer should never run"

          testCase "a wildcard representation catches an Accept that matches nothing more specific"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "image/png"

              let def =
                  negotiate {
                      accepts "application/json" (writeText "json")
                      accepts "*/*" (fun (ctx: HttpContext) -> task {
                          ctx.Response.ContentType <- "image/png"
                          do! ctx.Response.WriteAsync("image-bytes")
                      })
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.ContentType "image/png" "Wildcard representation must set its own Content-Type"
              Expect.equal (getResponseBody ctx) "image-bytes" "Wildcard representation's own producer ran"

          testCase "a wildcard representation registered first shadows a later, more specific one"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"

              let def =
                  negotiate {
                      accepts "*/*" (writeText "wildcard")
                      accepts "application/json" (writeText "json")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal (getResponseBody ctx) "wildcard" "A wildcard registered first always wins -- documented footgun"

          testCase "a representation registered via handler{} contributes its metadata"
          <| fun () ->
              let def =
                  negotiate {
                      accepts "application/json" (handler {
                          producesEmpty 200
                          handle (writeText "json")
                      })
                      accepts "text/html" (writeText "html")
                  }

              Expect.hasLength def.Metadata 1 "Only the handler{}-based representation contributes metadata"

          testCase "negotiate {} with no accepts calls throws"
          <| fun () ->
              let buildEmpty () = negotiate { accepts "unused" (writeText "unused") } |> ignore |> ignore
              // (kept non-empty above to prove the builder compiles; the real empty-block case:)
              let buildTrulyEmpty () =
                  (NegotiateBuilder()).Run(NegotiateSpec.Empty) |> ignore

              Expect.throws buildTrulyEmpty "Should throw when no representations are registered" ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`
Expected: FAIL to compile — `Frank.Builder.NegotiateBuilder`/`negotiate` don't exist yet.

- [ ] **Step 3: Add `NegotiateBuilder.fsi`**

Create `src/Frank/NegotiateBuilder.fsi`:

```fsharp
namespace Frank.Builder

open Microsoft.AspNetCore.Http

/// One representation: a media type (an exact type, or a "*/*"/"type/*" wildcard
/// catch-all) paired with the RequestDelegate that produces it. Representations are
/// independent of each other -- there is no shared object serialized differently per
/// entry, unlike IOutputFormatter's model.
type NegotiateSpec =
    { Representations: (string * RequestDelegate) list
      Metadata: obj list }

    static member Empty: NegotiateSpec

[<Sealed>]
type NegotiateBuilder =
    new: unit -> NegotiateBuilder

    member Yield: 'T -> NegotiateSpec
    member Run: spec: NegotiateSpec -> HandlerDefinition

    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: RequestDelegate -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> unit) -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handlerDef: HandlerDefinition -> NegotiateSpec

[<AutoOpen>]
module NegotiateFunctions =
    val negotiate: NegotiateBuilder
```

- [ ] **Step 4: Add `NegotiateBuilder.fs`**

Create `src/Frank/NegotiateBuilder.fs`:

```fsharp
namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Net.Http.Headers

type NegotiateSpec =
    { Representations: (string * RequestDelegate) list
      Metadata: obj list }

    static member Empty =
        { Representations = []
          Metadata = [] }

module internal Negotiation =

    let isWildcard (mediaType: string) =
        mediaType = "*/*" || mediaType.EndsWith("/*")

    /// True if `candidate` (one entry from the client's Accept header) and
    /// `registered` (one representation's declared media type) match, honoring a
    /// wildcard on either side -- a wildcard client entry matching a concrete
    /// representation is the common case; a wildcard *registered* representation
    /// matching a concrete client entry is what makes a catch-all `accepts "*/*"`
    /// work.
    let matches (candidate: MediaTypeHeaderValue) (registered: string) : bool =
        let registeredValue = MediaTypeHeaderValue.Parse(registered)
        candidate.MatchesMediaType(registeredValue) || registeredValue.MatchesMediaType(candidate)

    /// Selects the index of the representation that should serve this request, given
    /// the raw Accept header values and the registered media types, in registration
    /// order. An absent, empty, or entirely unparseable Accept is treated as an
    /// implicit "*/*" -- there is no separate "default representation" concept, it
    /// falls out of ordinary wildcard matching. Per RFC 9110 12.5.1, a q=0 entry is an
    /// explicit rejection of that media type, not merely a low-priority one -- it is
    /// excluded before matching, and if every entry the client sent is q=0 (nothing
    /// left to match against), the result is None (406), not a fall-through to the
    /// "*/*" absent-header default. Returns None when nothing registered matches any
    /// (non-rejected) entry.
    let selectRepresentation (acceptValues: string seq) (mediaTypes: string list) : int option =
        if List.isEmpty mediaTypes then
            None
        else
            // ctx.Request.Headers.Accept can deliver a single comma-joined value
            // (e.g. "text/html;q=0.3, application/json;q=0.8") rather than one value
            // per media range -- TryParseList (not per-value TryParse) is required to
            // split and parse each range correctly; TryParse on the whole joined
            // string fails and silently degrades every multi-entry Accept header to
            // "nothing parsed", which is indistinguishable from an absent header.
            let acceptedButNotRejected =
                match MediaTypeHeaderValue.TryParseList(ResizeArray(acceptValues)) with
                | true, parsed -> parsed |> Seq.filter (fun v -> v.Quality.GetValueOrDefault(1.0) > 0.0) |> List.ofSeq
                | false, _ -> []

            let allEntriesWereExplicitlyRejected =
                not (Seq.isEmpty acceptValues)
                && (match MediaTypeHeaderValue.TryParseList(ResizeArray(acceptValues)) with
                    | true, parsed -> not (Seq.isEmpty parsed) && List.isEmpty acceptedButNotRejected
                    | false, _ -> false)

            if allEntriesWereExplicitlyRejected then
                None
            else
                let entries =
                    if List.isEmpty acceptedButNotRejected then
                        [ MediaTypeHeaderValue.Parse("*/*") ]
                    else
                        acceptedButNotRejected
                        |> List.sortWith (fun a b -> MediaTypeHeaderValueComparer.QualityComparer.Compare(b, a))

                entries |> List.tryPick (fun entry -> mediaTypes |> List.tryFindIndex (matches entry))

    let dispatch (representations: (string * RequestDelegate) list) : RequestDelegate =
        RequestDelegate(fun ctx ->
            let mediaTypes = representations |> List.map fst

            match selectRepresentation ctx.Request.Headers.Accept mediaTypes with
            | Some idx ->
                let mediaType, handler = representations.[idx]

                if not (isWildcard mediaType) then
                    ctx.Response.ContentType <- mediaType

                handler.Invoke(ctx)
            | None ->
                ctx.Response.StatusCode <- StatusCodes.Status406NotAcceptable
                Task.CompletedTask)

[<Sealed>]
type NegotiateBuilder() =

    member _.Yield(_) = NegotiateSpec.Empty

    member _.Run(spec: NegotiateSpec) : HandlerDefinition =
        if List.isEmpty spec.Representations then
            failwith "At least one representation must be registered using the 'accepts' operation"

        { Handler = Negotiation.dispatch spec.Representations
          Metadata = spec.Metadata }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: RequestDelegate) =
        { spec with Representations = spec.Representations @ [ mediaType, handler ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> unit) =
        let producer =
            RequestDelegate(fun ctx ->
                handler ctx
                Task.CompletedTask)

        { spec with Representations = spec.Representations @ [ mediaType, producer ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handlerDef: HandlerDefinition) =
        { spec with
            Representations = spec.Representations @ [ mediaType, handlerDef.Handler ]
            Metadata = spec.Metadata @ handlerDef.Metadata }

[<AutoOpen>]
module NegotiateFunctions =
    let negotiate = NegotiateBuilder()
```

**If the compiler rejects `MediaTypeHeaderValueComparer.QualityComparer.Compare(b, a)` or `MatchesMediaType`'s parameter type:** these are the two calls flagged as "verify against the real API" in the design doc. Check the actual overloads with `dotnet fsi` or IDE tooltips on `Microsoft.Net.Http.Headers.MediaTypeHeaderValueComparer` / `Microsoft.Net.Http.Headers.MediaTypeHeaderValue` and adjust the call shape to match — the *behavior* each call needs to produce (descending quality sort; symmetric wildcard match) is fixed by the tests in Step 1, not by this exact code.

- [ ] **Step 5: Wire `NegotiateBuilder` into `Frank.fsproj`**

Modify `src/Frank/Frank.fsproj`, inserting two lines after the `HandlerBuilder.fs` entry:

```xml
    <Compile Include="ContentNegotiation.fsi" />
    <Compile Include="ContentNegotiation.fs" />
    <Compile Include="HandlerDefinition.fsi" />
    <Compile Include="HandlerDefinition.fs" />
    <Compile Include="HandlerBuilder.fsi" />
    <Compile Include="HandlerBuilder.fs" />
    <Compile Include="NegotiateBuilder.fsi" />
    <Compile Include="NegotiateBuilder.fs" />
    <Compile Include="ResourceBuilder.fsi" />
    <Compile Include="ResourceBuilder.fs" />
    <Compile Include="WebHostBuilder.fsi" />
    <Compile Include="WebHostBuilder.fs" />
```

- [ ] **Step 6: Wire the new test file into `Frank.Tests.fsproj`**

Modify `test/Frank.Tests/Frank.Tests.fsproj`, adding the new file before `Program.fs`:

```xml
    <Compile Include="HandlerBuilderTests.fs" />
    <Compile Include="ResourceBuilderMetadataTests.fs" />
    <Compile Include="MiddlewareOrderingTests.fs" />
    <Compile Include="MetadataTests.fs" />
    <Compile Include="NegotiateBuilderTests.fs" />
    <Compile Include="Program.fs" />
```

- [ ] **Step 7: Build across all target frameworks**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: Builds `net8.0`, `net9.0`, and `net10.0` (all three listed in `<TargetFrameworks>`) without errors or warnings.

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`
Expected: All `NegotiateBuilder` tests PASS, and every pre-existing test in the project still passes.

- [ ] **Step 9: Commit**

```bash
git add src/Frank/NegotiateBuilder.fsi src/Frank/NegotiateBuilder.fs src/Frank/Frank.fsproj test/Frank.Tests/NegotiateBuilderTests.fs test/Frank.Tests/Frank.Tests.fsproj
git commit -m "feat(frank): add negotiate { } content negotiation CE

Frank-native Accept-header dispatch built on Microsoft.Net.Http.Headers,
no AddMvcCore() dependency. Supports independent producers per media
type (not one object reformatted), quality-value precedence, 406 on no
match, and wildcard/catch-all *//* representations. Plugs into
ResourceBuilder's existing HandlerDefinition overload -- zero changes
to ResourceBuilder.

Part of #482."
```

---

## Task 2: `viaOutputFormatter` bridge + tests for both mechanisms

**Files:**
- Modify: `src/Frank/ContentNegotiation.fsi`
- Modify: `src/Frank/ContentNegotiation.fs`
- Create: `test/Frank.Tests/ContentNegotiationTests.fs`
- Modify: `test/Frank.Tests/Frank.Tests.fsproj`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `Frank.ContentNegotiation.viaOutputFormatter: mediaType: string -> body: 'a -> ctx: HttpContext -> Task`. Task 3 depends on this exact signature.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Tests/ContentNegotiationTests.fs`:

```fsharp
module Frank.Tests.ContentNegotiationTests

open System.IO
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Expecto
open Frank

let createMockContext (services: System.IServiceProvider) =
    let context = DefaultHttpContext()
    let responseStream = new MemoryStream()
    context.Response.Body <- responseStream
    context.RequestServices <- services
    context

let getResponseBody (ctx: HttpContext) =
    ctx.Response.Body.Position <- 0L
    use reader = new StreamReader(ctx.Response.Body)
    reader.ReadToEnd()

type Product = { Name: string; Price: decimal }

let servicesWithJsonOnly () =
    let services = ServiceCollection()
    services.AddMvcCore() |> ignore
    services.BuildServiceProvider()

let servicesWithJsonAndXml () =
    let services = ServiceCollection()
    services.AddMvcCore().AddXmlSerializerFormatters() |> ignore
    services.BuildServiceProvider()

[<Tests>]
let tests =
    testList
        "ContentNegotiation"
        [ testCase "viaOutputFormatter writes JSON when a JSON formatter is registered"
          <| fun () ->
              let ctx = createMockContext (servicesWithJsonOnly ())
              let product = { Name = "Widget"; Price = 9.99m }

              ContentNegotiation.viaOutputFormatter "application/json" product ctx
              |> Async.AwaitTask
              |> Async.RunSynchronously

              Expect.equal ctx.Response.ContentType "application/json" "Content-Type should be set"
              Expect.stringContains (getResponseBody ctx) "Widget" "Body should contain the serialized product"

          testCase "viaOutputFormatter throws when no formatter supports the requested media type"
          <| fun () ->
              let ctx = createMockContext (servicesWithJsonOnly ())
              let product = { Name = "Widget"; Price = 9.99m }

              let callIt () =
                  ContentNegotiation.viaOutputFormatter "application/xml" product ctx
                  |> Async.AwaitTask
                  |> Async.RunSynchronously

              Expect.throws callIt "No XML formatter is registered -- this is a server misconfiguration, not a 406"

          testCase "viaOutputFormatter writes XML once AddXmlSerializerFormatters is registered"
          <| fun () ->
              let ctx = createMockContext (servicesWithJsonAndXml ())
              let product = { Name = "Widget"; Price = 9.99m }

              ContentNegotiation.viaOutputFormatter "application/xml" product ctx
              |> Async.AwaitTask
              |> Async.RunSynchronously

              Expect.equal ctx.Response.ContentType "application/xml" "Content-Type should be set"
              Expect.stringContains (getResponseBody ctx) "Widget" "Body should contain the serialized product"

          testCase "negotiate (the existing IOutputFormatter mechanism) selects by Accept across formatters"
          <| fun () ->
              let ctx = createMockContext (servicesWithJsonAndXml ())
              ctx.Request.Headers.Accept <- Microsoft.Extensions.Primitives.StringValues("application/xml")
              let product = { Name = "Widget"; Price = 9.99m }

              ctx.Negotiate(200, product) |> Async.AwaitTask |> Async.RunSynchronously

              Expect.equal ctx.Response.StatusCode 200 "Status code should be as requested"
              Expect.stringContains (getResponseBody ctx) "Widget" "Body should contain the serialized product"

          testCase "negotiate responds 406 when Accept matches no registered formatter"
          <| fun () ->
              let ctx = createMockContext (servicesWithJsonOnly ())
              ctx.Request.Headers.Accept <- Microsoft.Extensions.Primitives.StringValues("application/xml")
              let product = { Name = "Widget"; Price = 9.99m }

              ctx.Negotiate(200, product) |> Async.AwaitTask |> Async.RunSynchronously

              Expect.equal ctx.Response.StatusCode 406 "No XML formatter registered -- should be Not Acceptable" ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`
Expected: FAIL to compile — `ContentNegotiation.viaOutputFormatter` doesn't exist yet.

- [ ] **Step 3: Add `viaOutputFormatter` to `ContentNegotiation.fsi`**

Modify `src/Frank/ContentNegotiation.fsi` to:

```fsharp
namespace Frank

module ContentNegotiation =

    open System.Threading.Tasks
    open Microsoft.AspNetCore.Http

    val notAcceptable: ctx: HttpContext -> Task

    val negotiate: statusCode: int -> body: 'a -> ctx: HttpContext -> Task

    /// Delegates to ASP.NET Core MVC's registered IOutputFormatters to write `body` as
    /// exactly `mediaType`, for representations that want to reuse an app's existing
    /// formatter registry (AddMvcCore(), AddXmlSerializerFormatters(), etc.) instead of
    /// a hand-written producer. Unlike `negotiate`, this does not parse Accept itself --
    /// it asks for a formatter constrained to this one already-decided media type.
    /// Throws if no formatter supports it (a server misconfiguration, not a client
    /// error, by the time this is called).
    val viaOutputFormatter: mediaType: string -> body: 'a -> ctx: HttpContext -> Task

    type HttpContext with
        member Negotiate: statusCode: int * body: 'a -> Task
```

- [ ] **Step 4: Add `viaOutputFormatter` to `ContentNegotiation.fs`**

Modify `src/Frank/ContentNegotiation.fs` to:

```fsharp
namespace Frank

/// Lightweight content negotiation from AspNetCore.Mvc.Core.
/// Based on https://www.strathweb.com/2018/09/running-asp-net-core-content-negotiation-by-hand/
module ContentNegotiation =

    open System.Threading.Tasks
    open Microsoft.AspNetCore.Http
    open Microsoft.AspNetCore.Mvc.Formatters
    open Microsoft.AspNetCore.Mvc.Infrastructure
    open Microsoft.Extensions.DependencyInjection

    let notAcceptable (ctx: HttpContext) : Task =
        ctx.Response.StatusCode <- 406
        upcast Task.FromResult()

    let negotiate statusCode (body: 'a) (ctx: HttpContext) =
        let selector = ctx.RequestServices.GetRequiredService<OutputFormatterSelector>()

        let writerFactory =
            ctx.RequestServices.GetRequiredService<IHttpResponseStreamWriterFactory>()

        let formatterContext =
            OutputFormatterWriteContext(
                ctx,
                (fun stream encoding -> writerFactory.CreateWriter(stream, encoding)),
                typeof<'a>,
                body
            )

        let formatter =
            selector.SelectFormatter(formatterContext, [||], MediaTypeCollection())

        if isNull formatter then
            notAcceptable ctx
        else
            ctx.Response.StatusCode <- statusCode
            formatter.WriteAsync(formatterContext)

    let viaOutputFormatter (mediaType: string) (body: 'a) (ctx: HttpContext) : Task =
        let selector = ctx.RequestServices.GetRequiredService<OutputFormatterSelector>()

        let writerFactory =
            ctx.RequestServices.GetRequiredService<IHttpResponseStreamWriterFactory>()

        let formatterContext =
            OutputFormatterWriteContext(
                ctx,
                (fun stream encoding -> writerFactory.CreateWriter(stream, encoding)),
                typeof<'a>,
                body
            )

        let requestedTypes = MediaTypeCollection()
        requestedTypes.Add(mediaType)

        let formatter =
            selector.SelectFormatter(formatterContext, [||], requestedTypes)

        if isNull formatter then
            failwithf
                "No IOutputFormatter is registered for media type '%s'. Ensure AddMvcCore() (and any extra formatter package, e.g. AddXmlSerializerFormatters()) is registered for this media type."
                mediaType
        else
            ctx.Response.ContentType <- mediaType
            formatter.WriteAsync(formatterContext)

    type HttpContext with
        member ctx.Negotiate(statusCode, body) = negotiate statusCode body ctx
```

**If `selector.SelectFormatter(formatterContext, [||], requestedTypes)` returns a formatter for the wrong type, or `MediaTypeCollection.Add` doesn't have this signature:** check the actual `OutputFormatterSelector.SelectFormatter` and `MediaTypeCollection` members — the tests in Step 1 pin the required behavior (found formatter writes the body and sets the exact `Content-Type`; missing formatter throws).

- [ ] **Step 5: Wire the new test file into `Frank.Tests.fsproj`**

Modify `test/Frank.Tests/Frank.Tests.fsproj`:

```xml
    <Compile Include="HandlerBuilderTests.fs" />
    <Compile Include="ResourceBuilderMetadataTests.fs" />
    <Compile Include="MiddlewareOrderingTests.fs" />
    <Compile Include="MetadataTests.fs" />
    <Compile Include="NegotiateBuilderTests.fs" />
    <Compile Include="ContentNegotiationTests.fs" />
    <Compile Include="Program.fs" />
```

- [ ] **Step 6: Build across all target frameworks**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: Builds `net8.0`, `net9.0`, `net10.0` without errors.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`
Expected: All tests PASS, including the previously-existing ones.

- [ ] **Step 8: Commit**

```bash
git add src/Frank/ContentNegotiation.fsi src/Frank/ContentNegotiation.fs test/Frank.Tests/ContentNegotiationTests.fs test/Frank.Tests/Frank.Tests.fsproj
git commit -m "feat(frank): add viaOutputFormatter bridge, test both conneg mechanisms

viaOutputFormatter delegates to the existing IOutputFormatter registry
for one already-known media type (not full Accept parsing), for use as
an ordinary producer inside negotiate { }. Also adds the first-ever
test coverage for the existing negotiate/ctx.Negotiate functions,
closing the gap #482 originally raised.

Part of #482."
```

---

## Task 3: `Task<'a>`/`Async<'a>` `accepts` overloads auto-format via `viaOutputFormatter`

**Files:**
- Modify: `src/Frank/NegotiateBuilder.fsi`
- Modify: `src/Frank/NegotiateBuilder.fs`
- Modify: `test/Frank.Tests/NegotiateBuilderTests.fs`

**Interfaces:**
- Consumes: `Frank.ContentNegotiation.viaOutputFormatter` (Task 2).
- Produces: two more `NegotiateBuilder.Accepts` overloads (`HttpContext -> Task<'a>`, `HttpContext -> Async<'a>`). Task 4's list-batch overloads call these by name.

- [ ] **Step 1: Write the failing tests**

Add to `test/Frank.Tests/NegotiateBuilderTests.fs`'s test list (inside the `testList "NegotiateBuilder" [ ... ]`, before the closing `]`):

```fsharp
          testCase "a Task<'a>-returning accepts handler has its value auto-formatted, not discarded"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"
              let services = Microsoft.Extensions.DependencyInjection.ServiceCollection()
              services.AddMvcCore() |> ignore
              ctx.RequestServices <- services.BuildServiceProvider()

              let def =
                  negotiate {
                      accepts "application/json" (fun (_: HttpContext) -> task { return {| Name = "Widget" |} })
                      accepts "text/html" (writeText "html")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.equal ctx.Response.ContentType "application/json" "Value should be written via viaOutputFormatter"
              Expect.stringContains (getResponseBody ctx) "Widget" "Serialized value should appear in the body"

          testCase "an Async<'a>-returning accepts handler has its value auto-formatted"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/json"
              let services = Microsoft.Extensions.DependencyInjection.ServiceCollection()
              services.AddMvcCore() |> ignore
              ctx.RequestServices <- services.BuildServiceProvider()

              let def =
                  negotiate {
                      accepts "application/json" (fun (_: HttpContext) -> async { return {| Name = "Widget" |} })
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.stringContains (getResponseBody ctx) "Widget" "Serialized value should appear in the body"

          testCase "a value-returning accepts entry composes with an independent-producer entry"
          <| fun () ->
              let ctx = createMockContext ()
              setAccept ctx "application/ld+json"
              let services = Microsoft.Extensions.DependencyInjection.ServiceCollection()
              services.AddMvcCore() |> ignore
              ctx.RequestServices <- services.BuildServiceProvider()
              let mutable jsonRan = false

              let def =
                  negotiate {
                      accepts "application/json" (fun (_: HttpContext) -> jsonRan <- true; task { return {| Name = "Widget" |} })
                      accepts "application/ld+json" (writeText "jsonld")
                  }

              def.Handler.Invoke(ctx).Wait()

              Expect.isFalse jsonRan "The value-returning representation should not run when a different one is selected"
              Expect.equal (getResponseBody ctx) "jsonld" "The independent producer should have run instead"
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`
Expected: FAIL to compile — no `Accepts` overload matches `HttpContext -> Task<'a>`/`HttpContext -> Async<'a>` yet.

- [ ] **Step 3: Add the two overloads to `NegotiateBuilder.fsi`**

Modify `src/Frank/NegotiateBuilder.fsi`, adding two lines to the `Accepts` group:

```fsharp
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: RequestDelegate -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> unit) -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handlerDef: HandlerDefinition -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> Task<'a>) -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> Async<'a>) -> NegotiateSpec
```

Also add `open System.Threading.Tasks` at the top of the file (needed for `Task<'a>`).

**Note:** the `.fsi` for Task 1 was originally written with the `[<CustomOperation>]` attribute on only the first overload (matching `ResourceBuilder.fsi`'s convention). Task 1's review found this contradicts `HandlerBuilder.fsi`'s convention (repeats the attribute on every overload), which is the correct one to follow here since `NegotiateBuilder` is the same kind of accumulating CE as `HandlerBuilder`. If Task 1 has already been fixed by the time you read this, your starting file will already have the attribute repeated on the first three lines below — just add it to your two new lines too, consistent with the rest.

- [ ] **Step 4: Add the two overloads to `NegotiateBuilder.fs`**

Modify `src/Frank/NegotiateBuilder.fs`, adding two more `Accepts` members to the `NegotiateBuilder` type (after the `HandlerDefinition` overload):

```fsharp
    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Task<'a>) =
        let producer =
            RequestDelegate(fun ctx ->
                task {
                    let! value = handler ctx
                    return! Frank.ContentNegotiation.viaOutputFormatter mediaType value ctx
                })

        { spec with Representations = spec.Representations @ [ mediaType, producer ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Async<'a>) =
        let producer =
            RequestDelegate(fun ctx ->
                task {
                    let! value = Async.StartAsTask(handler ctx)
                    return! Frank.ContentNegotiation.viaOutputFormatter mediaType value ctx
                })

        { spec with Representations = spec.Representations @ [ mediaType, producer ] }
```

- [ ] **Step 5: Build across all target frameworks**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: Builds `net8.0`, `net9.0`, `net10.0` without errors. If F# reports the new overloads as ambiguous with the `HttpContext -> unit` or `RequestDelegate` overloads, check `HandlerBuilder.fs`'s equivalent `Handle` overloads (Task 1's `Accepts` overload set already mirrors that proven pattern) for the exact ordering/shape that resolves cleanly.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`
Expected: All tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Frank/NegotiateBuilder.fsi src/Frank/NegotiateBuilder.fs test/Frank.Tests/NegotiateBuilderTests.fs
git commit -m "feat(frank): auto-format Task<'a>/Async<'a> accepts handlers

Unlike HandlerBuilder.Handle's same-shaped overloads (which discard a
returned value), negotiate { }'s accepts pipes a value-returning
handler's result through viaOutputFormatter using the accepts call's
own media type -- the value would otherwise silently vanish, which
would have made these overloads useless for content negotiation.

Part of #482."
```

---

## Task 4: Batch `accepts [mediaTypes] handler` sugar

**Files:**
- Modify: `src/Frank/NegotiateBuilder.fsi`
- Modify: `src/Frank/NegotiateBuilder.fs`
- Modify: `test/Frank.Tests/NegotiateBuilderTests.fs`

**Interfaces:**
- Consumes: the `Task<'a>`/`Async<'a>` `Accepts` overloads from Task 3 (called internally, once per media type).
- Produces: two more `Accepts` overloads taking `mediaTypes: string list`.

- [ ] **Step 1: Write the failing test**

Add to `test/Frank.Tests/NegotiateBuilderTests.fs`'s test list:

```fsharp
          testCase "accepts [mediaTypes] handler registers one representation per media type"
          <| fun () ->
              let services = Microsoft.Extensions.DependencyInjection.ServiceCollection()
              services.AddMvcCore().AddXmlSerializerFormatters() |> ignore
              let provider = services.BuildServiceProvider()

              let def =
                  negotiate {
                      accepts [ "application/json"; "application/xml" ] (fun (_: HttpContext) -> task { return {| Name = "Widget" |} })
                  }

              Expect.hasLength def.Representations 2 "Should expand to two representations"

              let jsonCtx = createMockContext ()
              jsonCtx.RequestServices <- provider
              setAccept jsonCtx "application/json"
              def.Handler.Invoke(jsonCtx).Wait()
              Expect.equal jsonCtx.Response.ContentType "application/json" "JSON entry should format as JSON"

              let xmlCtx = createMockContext ()
              xmlCtx.RequestServices <- provider
              setAccept xmlCtx "application/xml"
              def.Handler.Invoke(xmlCtx).Wait()
              Expect.equal xmlCtx.Response.ContentType "application/xml" "XML entry should format as XML, not the whole list"
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`
Expected: FAIL to compile — no `Accepts` overload takes a `string list` yet.

- [ ] **Step 3: Add the two overloads to `NegotiateBuilder.fsi`**

Modify `src/Frank/NegotiateBuilder.fsi`, adding two more lines to the `Accepts` group:

```fsharp
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaTypes: string list * handler: (HttpContext -> Task<'a>) -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaTypes: string list * handler: (HttpContext -> Async<'a>) -> NegotiateSpec
```

- [ ] **Step 4: Add the two overloads to `NegotiateBuilder.fs`**

Modify `src/Frank/NegotiateBuilder.fs`, adding after the single-`mediaType` `Task<'a>`/`Async<'a>` overloads:

```fsharp
    [<CustomOperation("accepts")>]
    member this.Accepts(spec: NegotiateSpec, mediaTypes: string list, handler: HttpContext -> Task<'a>) =
        mediaTypes |> List.fold (fun s mt -> this.Accepts(s, mt, handler)) spec

    [<CustomOperation("accepts")>]
    member this.Accepts(spec: NegotiateSpec, mediaTypes: string list, handler: HttpContext -> Async<'a>) =
        mediaTypes |> List.fold (fun s mt -> this.Accepts(s, mt, handler)) spec
```

Note these use `this.Accepts(...)` (not `_.Accepts`) since they call back into the type's own single-`mediaType` overloads by name.

- [ ] **Step 5: Build across all target frameworks**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: Builds `net8.0`, `net9.0`, `net10.0` without errors.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`
Expected: All tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/Frank/NegotiateBuilder.fsi src/Frank/NegotiateBuilder.fs test/Frank.Tests/NegotiateBuilderTests.fs
git commit -m "feat(frank): accepts [mediaTypes] handler batch-registration sugar

Reduces the two-line 'same handler under several standard formats'
case to one line; each expansion still resolves and formats using its
own specific matched media type, not the whole list.

Part of #482."
```

---

## Task 5: OpenAPI metadata integration check

**Files:**
- Create: `test/Frank.OpenApi.Tests/NegotiateMetadataTests.fs`
- Modify: `test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`

**Interfaces:**
- Consumes: `Frank.Builder.negotiate`/`accepts` (Tasks 1–4), `Frank.Builder.handler`/`produces`/`producesEmpty` (existing), `Frank.OpenApi.Tests.OpenApiDocumentTests.createOpenApiTestServer: Resource list -> HttpClient` (existing helper in `test/Frank.OpenApi.Tests/OpenApiDocumentTests.fs`).
- Produces: nothing new for later tasks — this is a verification-only task.

- [ ] **Step 1: Write the failing test**

Create `test/Frank.OpenApi.Tests/NegotiateMetadataTests.fs`:

```fsharp
module Frank.OpenApi.Tests.NegotiateMetadataTests

open System.Net.Http.Json
open System.Text.Json
open Expecto
open Frank.Builder
open Frank.OpenApi.Tests.OpenApiDocumentTests

type Product = { Name: string; Price: decimal }

[<Tests>]
let tests =
    testList
        "Negotiate metadata reaches the OpenAPI document"
        [ testCaseAsync "a resource using negotiate { } with handler{}-declared representations lists both media types"
          <| async {
              let resourceSpec =
                  resource "/negotiated-products/{id}" {
                      get (negotiate {
                          accepts "application/json" (handler { produces typeof<Product> 200 })
                          accepts "text/html" (handler { producesEmpty 200 })
                      })
                  }

              let client = createOpenApiTestServer [ resourceSpec ]
              let! json = client.GetStringAsync(openApiRoutePattern) |> Async.AwaitTask
              use doc = JsonDocument.Parse(json)

              let responses =
                  doc.RootElement
                      .GetProperty("paths")
                      .GetProperty("/negotiated-products/{id}")
                      .GetProperty("get")
                      .GetProperty("responses")
                      .GetProperty("200")
                      .GetProperty("content")

              Expect.isTrue (responses.TryGetProperty("application/json") |> fst) "JSON representation's metadata should appear"
              Expect.isTrue (responses.TryGetProperty("text/html") |> fst) "HTML representation's metadata should appear"
          } ]
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`
Expected: FAIL to compile (new file not yet in the `.fsproj`) or FAIL at runtime if the metadata doesn't merge as expected — either failure mode is informative here, since this task adds no new production code, only a check on Tasks 1–4's existing behavior.

- [ ] **Step 3: Wire the new test file into the project**

Modify `test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`:

```xml
    <Compile Include="MetadataTests.fs" />
    <Compile Include="OpenApiDocumentTests.fs" />
    <Compile Include="NegotiateMetadataTests.fs" />
    <Compile Include="ServiceDescLinkTests.fs" />
    <Compile Include="SchemaTests.fs" />
    <Compile Include="Program.fs" />
```

`NegotiateMetadataTests.fs` must come after `OpenApiDocumentTests.fs` in the compile order since it uses `createOpenApiTestServer` and `openApiRoutePattern` from that file.

- [ ] **Step 4: Run the test again**

Run: `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`

If it fails because the generated document's JSON shape differs from what Step 1 assumes (e.g. a different path for `content` under a response, or the media types aren't both present), inspect the raw `json` string in a debugger or `printfn` and adjust the property-path assertions to match what `Microsoft.AspNetCore.OpenApi` actually emits — the design commitment being verified is "both representations' metadata reach the document," not this exact JSON path.

Expected once correct: PASS.

- [ ] **Step 5: Commit**

```bash
git add test/Frank.OpenApi.Tests/NegotiateMetadataTests.fs test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj
git commit -m "test(openapi): verify negotiate { } metadata reaches the generated document

Confirms the design's central OpenAPI-integration claim: a
handler{}-declared representation's metadata flows through the
existing HandlerDefinition.Metadata -> EndpointBuilder conventions ->
Microsoft.AspNetCore.OpenApi pipeline with zero Frank.OpenApi changes.

Part of #482."
```

---

## Task 6: Fix the fake sample, add the `viaOutputFormatter` bridge sample

**Files:**
- Modify: `sample/Frank.OpenApi.Sample/Handlers.fs:148-170`
- Modify: `sample/Frank.OpenApi.Sample/Program.fs`

**Interfaces:**
- Consumes: `Frank.Builder.negotiate`/`accepts` (Tasks 1–4), `Frank.ContentNegotiation.viaOutputFormatter` (Task 2).
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Replace the fake `getProductNegotiated`**

Modify `sample/Frank.OpenApi.Sample/Handlers.fs`, replacing lines 147–170 (the `getProductNegotiated` definition and its preceding comment):

```fsharp
/// Content negotiation example -- genuinely returns a different body for JSON vs. HTML
let getProductNegotiated =
    negotiate {
        accepts "application/json" (handler {
            name "getProductNegotiatedJson"
            produces typeof<Product> 200
            produces typeof<ErrorResponse> 404
            handle (fun (ctx: HttpContext) -> task {
                let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
                match ProductStore.getById id with
                | Some product -> do! ctx.Response.WriteAsJsonAsync(product)
                | None ->
                    ctx.Response.StatusCode <- 404
                    do! ctx.Response.WriteAsJsonAsync({
                        Code = "NOT_FOUND"
                        Message = $"Product with ID {id} not found"
                        Details = None
                    })
            })
        })
        accepts "text/html" (fun (ctx: HttpContext) -> task {
            let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
            match ProductStore.getById id with
            | Some product ->
                do! ctx.Response.WriteAsync(
                    $"<html><body><h1>{product.Name}</h1><p>${product.Price}</p></body></html>")
            | None ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsync($"<html><body><h1>Not found</h1><p>{id}</p></body></html>")
        })
    }
```

This registers a `HandlerDefinition` (via `handler { }`) for JSON and a bare function for HTML — matching Task 1–4's `Accepts` overloads directly.

- [ ] **Step 2: Add a second sample demonstrating the `viaOutputFormatter` bridge**

Modify `sample/Frank.OpenApi.Sample/Handlers.fs`, adding after `getProductNegotiated`:

```fsharp
/// Content negotiation with the IOutputFormatter bridge -- JSON and XML reuse MVC's
/// formatter registry (requires AddMvcCore().AddXmlSerializerFormatters(), wired up in
/// Program.fs), while an independent producer still handles HTML.
let getProductBridged =
    negotiate {
        accepts [ "application/json"; "application/xml" ] (fun (ctx: HttpContext) -> task {
            let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
            return ProductStore.getById id
        })
        accepts "text/html" (fun (ctx: HttpContext) -> task {
            let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
            match ProductStore.getById id with
            | Some product ->
                do! ctx.Response.WriteAsync(
                    $"<html><body><h1>{product.Name}</h1><p>${product.Price}</p></body></html>")
            | None ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsync($"<html><body><h1>Not found</h1><p>{id}</p></body></html>")
        })
    }
```

- [ ] **Step 3: Wire `getProductBridged` into `Program.fs`**

Modify `sample/Frank.OpenApi.Sample/Program.fs`, adding a new resource near the existing `/api/products/{id}/negotiate` one (around line 37-40):

```fsharp
    resource "/api/products/{id}/negotiate" {
        name "getProductNegotiated"
        get getProductNegotiated
    }

    resource "/api/products/{id}/negotiate-bridged" {
        name "getProductBridged"
        get getProductBridged
    }
```

Check that `Program.fs`'s host setup calls `services.AddMvcCore().AddXmlSerializerFormatters()` (or equivalent) somewhere in `ConfigureServices` — if it doesn't yet, add it, since `getProductBridged`'s JSON/XML representations need it at runtime (the HTML representation and `getProductNegotiated` do not).

- [ ] **Step 4: Manually verify the sample**

Run: `dotnet run --project sample/Frank.OpenApi.Sample/`

Then, in another terminal:
```bash
curl -H "Accept: application/json" http://localhost:<port>/api/products/<real-id>/negotiate
curl -H "Accept: text/html" http://localhost:<port>/api/products/<real-id>/negotiate
curl -H "Accept: application/xml" http://localhost:<port>/api/products/<real-id>/negotiate-bridged
```

Expected: the first two return genuinely different bodies (JSON object vs. an HTML page) for the same `id`; the third returns an XML-serialized product. Stop the sample server afterward.

- [ ] **Step 5: Commit**

```bash
git add sample/Frank.OpenApi.Sample/Handlers.fs sample/Frank.OpenApi.Sample/Program.fs
git commit -m "fix(sample): make getProductNegotiated genuinely negotiate, add bridge sample

getProductNegotiated previously always returned JSON regardless of
Accept (its own comment admitted this). Now uses negotiate { } and
actually varies its response. Also adds getProductBridged
demonstrating the viaOutputFormatter bridge (JSON/XML via MVC
formatters, HTML as an independent producer) on the same resource.

Fixes the sample referenced in #482."
```

---

## Task 7: Extend `DuplicateHandlerAnalyzer` for duplicate `accepts` media types

**Files:**
- Modify: `src/Frank.Analyzers/DuplicateHandlerAnalyzer.fsi`
- Modify: `src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs`
- Create: `test/Frank.Analyzers.Tests/fixtures/DuplicateAccepts.fs`
- Create: `test/Frank.Analyzers.Tests/fixtures/DistinctAccepts.fs`
- Modify: `test/Frank.Analyzers.Tests/Frank.Analyzers.Tests.fsproj`
- Modify: `test/Frank.Analyzers.Tests/run-analyzer-tests.sh`

**Interfaces:**
- Consumes: nothing from earlier tasks (this is a static-analysis change, independent of runtime behavior).
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the failing fixtures**

Create `test/Frank.Analyzers.Tests/fixtures/DuplicateAccepts.fs`:

```fsharp
module TestFixtures.DuplicateAccepts

open Frank.Builder

let jsonHandler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let anotherJsonHandler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK002 -- "application/json" registered twice
let duplicateAcceptsResource =
    resource "/test" {
        get (negotiate {
            accepts "application/json" jsonHandler
            accepts "application/json" anotherJsonHandler // Duplicate media type -- should warn
        })
    }
```

Create `test/Frank.Analyzers.Tests/fixtures/DistinctAccepts.fs`:

```fsharp
module TestFixtures.DistinctAccepts

open Frank.Builder

let jsonHandler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let htmlHandler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should NOT trigger any warnings -- different media types
let distinctAcceptsResource =
    resource "/test" {
        get (negotiate {
            accepts "application/json" jsonHandler
            accepts "text/html" htmlHandler
        })
    }
```

- [ ] **Step 2: Wire the fixtures into `Frank.Analyzers.Tests.fsproj`**

Modify `test/Frank.Analyzers.Tests/Frank.Analyzers.Tests.fsproj`, adding two lines to the `<Compile>` list:

```xml
    <Compile Include="fixtures/DatastarNoConflict.fs" />
    <Compile Include="fixtures/DuplicateAccepts.fs" />
    <Compile Include="fixtures/DistinctAccepts.fs" />
  </ItemGroup>
```

- [ ] **Step 3: Add `check_test` calls to `run-analyzer-tests.sh`**

Modify `test/Frank.Analyzers.Tests/run-analyzer-tests.sh`, adding after the existing `DatastarNoConflict` line (before the closing `echo ""` / summary block):

```bash
# Duplicate accepts media-type detection
check_test "DuplicateAccepts" true "Duplicate accepts media type detection"
check_test "DistinctAccepts" false "Distinct accepts media types (no warning)"
```

Note the script's `check_test` function currently only greps for `FRANK001` — it needs a warning-code parameter. Modify the function itself:

```bash
check_test() {
    local fixture=$1
    local expect_warning=$2
    local description=$3
    local code=${4:-FRANK001}

    if [[ "$expect_warning" == "true" ]]; then
        if echo "$ANALYZER_OUTPUT" | grep -q "$fixture.fs.*$code"; then
            echo -e "${GREEN}PASS${NC}: $fixture - $description"
            PASSED=$((PASSED + 1))
        else
            echo -e "${RED}FAIL${NC}: $fixture - Expected $code warning ($description)"
            FAILED=$((FAILED + 1))
        fi
    else
        if echo "$ANALYZER_OUTPUT" | grep -q "$fixture.fs.*$code"; then
            echo -e "${RED}FAIL${NC}: $fixture - Unexpected $code warning ($description)"
            FAILED=$((FAILED + 1))
        else
            echo -e "${GREEN}PASS${NC}: $fixture - $description"
            PASSED=$((PASSED + 1))
        fi
    fi
}
```

And call the new checks with the `FRANK002` code explicitly:

```bash
check_test "DuplicateAccepts" true "Duplicate accepts media type detection" "FRANK002"
check_test "DistinctAccepts" false "Distinct accepts media types (no warning)" "FRANK002"
```

- [ ] **Step 4: Run the script to verify the new checks fail**

Run: `bash test/Frank.Analyzers.Tests/run-analyzer-tests.sh`
Expected: `DuplicateAccepts` FAILs (no `FRANK002` emitted yet, since the analyzer doesn't check `accepts` at all).

- [ ] **Step 5: Add the `FRANK002` check to `DuplicateHandlerAnalyzer.fsi`**

Modify `src/Frank.Analyzers/DuplicateHandlerAnalyzer.fsi`, adding after `createDuplicateMessage`:

```fsharp
/// Create a message for a duplicate `accepts` media-type registration inside one
/// `negotiate { }` block
val createDuplicateMediaTypeMessage: mediaType: string -> duplicateRange: range -> firstRange: range -> Message
```

- [ ] **Step 6: Extend `DuplicateHandlerAnalyzer.fs`**

Modify `src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs`. First, change the per-CE-block tracking to carry two dictionaries instead of one -- replace:

```fsharp
    // Use a mutable stack to track context per CE block
    let contextStack =
        ResizeArray<System.Collections.Generic.Dictionary<string, range>>()

    let pushContext () =
        contextStack.Add(System.Collections.Generic.Dictionary<string, range>())

    let popContext () =
        if contextStack.Count > 0 then
            contextStack.RemoveAt(contextStack.Count - 1)

    let tryRegisterMethod (methodName: string) (r: range) =
        if contextStack.Count > 0 then
            let current = contextStack.[contextStack.Count - 1]

            if current.ContainsKey(methodName) then
                // Duplicate found
                messages.Add(createDuplicateMessage methodName r current.[methodName])
            else
                // Register this method
                current.[methodName] <- r
```

with:

```fsharp
    // Use a mutable stack to track context per CE block. Each level tracks HTTP
    // method operations (get/post/...) and negotiate { } accepts media types
    // separately, since they're unrelated checks that both key on "the same name
    // registered twice inside one CE block".
    let contextStack =
        ResizeArray<
            {| Methods: System.Collections.Generic.Dictionary<string, range>
               MediaTypes: System.Collections.Generic.Dictionary<string, range> |}
         >()

    let pushContext () =
        contextStack.Add(
            {| Methods = System.Collections.Generic.Dictionary<string, range>()
               MediaTypes = System.Collections.Generic.Dictionary<string, range>() |}
        )

    let popContext () =
        if contextStack.Count > 0 then
            contextStack.RemoveAt(contextStack.Count - 1)

    let tryRegisterMethod (methodName: string) (r: range) =
        if contextStack.Count > 0 then
            let current = contextStack.[contextStack.Count - 1].Methods

            if current.ContainsKey(methodName) then
                // Duplicate found
                messages.Add(createDuplicateMessage methodName r current.[methodName])
            else
                // Register this method
                current.[methodName] <- r

    let tryRegisterMediaType (mediaType: string) (r: range) =
        if contextStack.Count > 0 then
            let current = contextStack.[contextStack.Count - 1].MediaTypes

            if current.ContainsKey(mediaType) then
                // Duplicate found
                messages.Add(createDuplicateMediaTypeMessage mediaType r current.[mediaType])
            else
                // Register this media type
                current.[mediaType] <- r
```

Next, add the message constructor after `createDuplicateMessage`:

```fsharp
/// Create a message for a duplicate `accepts` media-type registration inside one
/// `negotiate { }` block
let createDuplicateMediaTypeMessage (mediaType: string) (duplicateRange: range) (firstRange: range) : Message =
    { Type = "Duplicate accepts media type"
      Message =
        sprintf
            "Media type '%s' is already registered in this negotiate block at line %d. The earlier registration always wins, making this one dead code."
            mediaType
            firstRange.StartLine
      Code = "FRANK002"
      Severity = Severity.Warning
      Range = duplicateRange
      Fixes = [] }
```

Finally, extend the `SynExpr.App` case in `walkExprForCE` to detect `accepts "<string literal>" ...`. Replace the `if contextStack.Count > 0 then` block's `match funcExpr with` to add a new case (keep the existing `SynExpr.Ident`/`SynExpr.App`(datastar)/`_` cases, add one more `SynExpr.App` case before the datastar one):

```fsharp
            if contextStack.Count > 0 then
                match funcExpr with
                | SynExpr.Ident ident ->
                    let name = ident.idText.ToLowerInvariant()

                    if httpMethodOperations.Contains name then
                        tryRegisterMethod (name.ToUpperInvariant()) r
                    elif name = "datastar" then
                        match tryGetDatastarMethodFromArg argExpr with
                        | Some explicitMethod -> tryRegisterMethod explicitMethod r
                        | None -> tryRegisterMethod "GET" r

                | SynExpr.App(funcExpr = SynExpr.Ident acceptsIdent; argExpr = mediaTypeArg) when
                    acceptsIdent.idText.ToLowerInvariant() = "accepts"
                    ->
                    match mediaTypeArg with
                    | SynExpr.Const(constant = SynConst.String(text = mediaType)) -> tryRegisterMediaType mediaType r
                    | _ -> () // e.g. HandlerBuilder's `accepts typeof<X>` -- not a media-type literal, not our concern

                | SynExpr.App(funcExpr = innerFunc; argExpr = methodArg) ->
                    match innerFunc with
                    | SynExpr.Ident ident when ident.idText.ToLowerInvariant() = "datastar" ->
                        match tryGetDatastarMethodFromArg methodArg with
                        | Some explicitMethod -> tryRegisterMethod explicitMethod r
                        | None -> tryRegisterMethod "GET" r

                        handledDatastarCurried <- true // Mark that we handled this
                    | _ -> ()

                | _ -> ()
```

The new `SynExpr.App(funcExpr = SynExpr.Ident acceptsIdent; ...)` case must come **before** the existing generic `SynExpr.App(funcExpr = innerFunc; argExpr = methodArg)` case, since both match the same curried-application shape and F# tries patterns top-to-bottom — otherwise `accepts "application/json" handler` would fall into the datastar-shaped branch and be silently ignored.

This relies on `negotiate { }` itself being a `SynExpr.ComputationExpr`, which already pushes its own context via the existing `SynExpr.ComputationExpr(expr = bodyExpr) -> pushContext (); ...; popContext ()` case — no change needed there, since that case doesn't care which CE it is.

- [ ] **Step 7: Build across all target frameworks**

Run: `dotnet build src/Frank.Analyzers/Frank.Analyzers.fsproj`
Expected: Builds `net8.0`, `net9.0`, `net10.0` without errors.

- [ ] **Step 8: Run the analyzer test script to verify it passes**

Run: `bash test/Frank.Analyzers.Tests/run-analyzer-tests.sh`
Expected: All checks PASS, including the two new ones and every pre-existing one (confirming `get`/`post` duplicate detection still works unaffected).

- [ ] **Step 9: Commit**

```bash
git add src/Frank.Analyzers/DuplicateHandlerAnalyzer.fsi src/Frank.Analyzers/DuplicateHandlerAnalyzer.fs test/Frank.Analyzers.Tests/fixtures/DuplicateAccepts.fs test/Frank.Analyzers.Tests/fixtures/DistinctAccepts.fs test/Frank.Analyzers.Tests/Frank.Analyzers.Tests.fsproj test/Frank.Analyzers.Tests/run-analyzer-tests.sh
git commit -m "feat(analyzers): detect duplicate accepts media types (FRANK002)

DuplicateHandlerAnalyzer previously only deduped by operation name
against a fixed httpMethodOperations set, so it couldn't catch
'accepts \"application/json\"' registered twice in one negotiate { }
block -- today that silently makes the second registration dead code.
Keys the new check on the accepts call's own media-type string literal
instead, scoped per negotiate { } block via the existing per-CE
context stack.

Part of #482."
```

---

## Task 8: Merge same-status-code metadata so it survives OpenAPI generation

**Added after Task 5 ran.** Task 5 was written as a verification-only task — confirm that a `negotiate { }` representation's `handler{}`-declared metadata reaches the generated OpenAPI document via the existing pipeline, with zero `Frank.OpenApi` changes. It did its job: it found that claim is **false** for the common case where two representations share a status code (e.g. both `200`).

Root cause, isolated by Task 5's implementer with a bare ASP.NET Core minimal-API sanity test (zero Frank code involved — `.Produces<T>(200, "text/html").Produces<T>(200, "application/json")` on a plain `MapGet`, generated document keeps only `application/json`): `Microsoft.AspNetCore.OpenApi`'s document generator does **last-registered-wins**, not merge, when it sees multiple *separate* `IProducesResponseTypeMetadata` objects targeting the same status code. Frank's own pipeline (`NegotiateBuilder`'s metadata merge from Tasks 1–4, `HandlerDefinition`, `ResourceBuilder`) was verified correct all the way to `Endpoint.Metadata` — both representations' metadata genuinely arrive there. The loss happens strictly inside `Microsoft.AspNetCore.OpenApi`'s own generation step, which Task 5 was correctly scoped not to touch.

There is already a *proven-working* shape for this: the existing `OpenApiDocumentTests.fs` test "HandlerDefinition with custom content types for content negotiation" uses **one** `produces typeof<Product> 200 [ "application/xml"; "application/json" ]` call — one metadata object, multiple content types in its `ContentTypes` array — and that merges into the document correctly. The fix is to make `negotiate { }` itself never hand `Microsoft.AspNetCore.OpenApi` two separate metadata objects for the same `(status code, response type)` in the first place — merge them into that already-working one-object-many-content-types shape before they ever leave `NegotiateBuilder`. This keeps the fix inside `Frank` core, touches **no** `Frank.OpenApi` file, and doesn't need custom OpenAPI schema synthesis.

**Scope note:** this fixes the case where multiple representations share a status code **and** a response type (e.g. `Product` returned as both `application/json` and `application/xml` — the common shape when several `accepts` entries wrap the same underlying object). It does **not** fix the narrower case of representations sharing a status code with **different** response types (e.g. JSON returns `Product`, a `handler{}`-wrapped alternate representation returns some other type) — `Microsoft.AspNetCore.OpenApi`'s last-wins behavior still applies there. That's a documented, accepted remaining limitation, not something this task chases (see *Future work* in the design doc addendum below).

**Files:**
- Modify: `src/Frank/NegotiateBuilder.fs`
- Modify: `test/Frank.OpenApi.Tests/NegotiateMetadataTests.fs` (already exists from Task 5 — currently a genuinely-red test; no changes needed to it, it's the acceptance test for this fix)

**Interfaces:**
- Consumes: `Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata`/`ProducesResponseTypeMetadata` — the same interface/class `HandlerBuilder.fs` already constructs (`StatusCode: int`, `Type: System.Type`, `ContentTypes: IEnumerable<string>`).
- Produces: nothing new consumed by later tasks — `NegotiateBuilder.Run`'s signature (`NegotiateSpec -> HandlerDefinition`) is unchanged; this only changes what `Metadata` list it builds internally. No `.fsi` change needed.

- [ ] **Step 1: Confirm the existing test still fails, for the documented reason**

Run: `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`
Expected: FAIL — the `NegotiateMetadataTests` test (added in Task 5) fails because only one of the two content types appears in the generated document's `200.content`. This is the RED state this task turns GREEN — do not modify the test itself, it already asserts the correct thing.

- [ ] **Step 2: Add the merge function to `NegotiateBuilder.fs`**

Modify `src/Frank/NegotiateBuilder.fs`. Add `open Microsoft.AspNetCore.Http.Metadata` to the top-of-file opens (alongside the existing `open System.Threading.Tasks`, `open Microsoft.AspNetCore.Http`, `open Microsoft.Net.Http.Headers`), then add this function above the `NegotiateBuilder` type definition (after the `Negotiation` module, before `[<Sealed>] type NegotiateBuilder`):

```fsharp
/// Merges `IProducesResponseTypeMetadata` entries that share both the same status code
/// and the same response `Type` into one, unioning their content types -- the exact
/// shape (one metadata object, several content types) that already reaches the
/// generated OpenAPI document correctly (see `OpenApiDocumentTests.fs`'s "HandlerDefinition
/// with custom content types for content negotiation" test). Without this, two
/// `handler { produces ... }` representations sharing a status code (the common
/// `negotiate { }` case: the same response type serialized as e.g. both
/// `application/json` and `application/xml`) would emit two SEPARATE metadata objects
/// for that status code -- and Microsoft.AspNetCore.OpenApi's own document generator
/// keeps only the last-registered one, silently dropping the other from the generated
/// document (verified: this reproduces with a bare ASP.NET Core minimal API, zero Frank
/// code involved -- it's inherent framework behavior, not something Frank broke).
///
/// Representations sharing a status code but declaring DIFFERENT response types are
/// left as separate metadata objects -- Microsoft.AspNetCore.OpenApi's last-wins
/// behavior still applies to that narrower case. A documented, accepted limitation, not
/// fixed here.
let private mergeProducesMetadata (metadata: obj list) : obj list =
    let produces, other =
        metadata |> List.partition (fun m -> m :? IProducesResponseTypeMetadata)

    let merged =
        produces
        |> List.map (fun m -> m :?> IProducesResponseTypeMetadata)
        |> List.groupBy (fun m -> m.StatusCode, m.Type)
        |> List.map (fun ((statusCode, responseType), group) ->
            let contentTypes =
                group
                |> List.collect (fun m -> m.ContentTypes |> List.ofSeq)
                |> List.distinct
                |> Array.ofList

            ProducesResponseTypeMetadata(statusCode, responseType, contentTypes) :> obj)

    other @ merged
```

- [ ] **Step 3: Use it in `Run`**

Modify `NegotiateBuilder.fs`'s `NegotiateBuilder.Run` member:

```fsharp
    member _.Run(spec: NegotiateSpec) : HandlerDefinition =
        if List.isEmpty spec.Representations then
            failwith "At least one representation must be registered using the 'accepts' operation"

        { Handler = Negotiation.dispatch spec.Representations
          Metadata = mergeProducesMetadata spec.Metadata }
```

- [ ] **Step 4: Build across all target frameworks**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: Builds `net8.0`, `net9.0`, `net10.0` without errors.

- [ ] **Step 5: Run the OpenAPI test to verify it now passes**

Run: `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`
Expected: PASS — the `NegotiateMetadataTests` test from Task 5 now shows both `application/json` and `text/html` under the `200` response's `content`.

- [ ] **Step 6: Run the full `Frank.Tests` suite to confirm no regression**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`
Expected: PASS — in particular, `NegotiateBuilderTests.fs`'s "a representation registered via `handler { produces ... }` contributes its metadata" test (a single-representation case, `mergeProducesMetadata` should be a no-op for it: one entry in, one entry out) and any other `def.Metadata` assertions from Tasks 1–4 must still hold. If any test asserted an EXACT metadata list order that this merge changes (produces-metadata entries move to the end of the list, after non-produces metadata like name/tags — see the doc comment above), update that assertion to check membership/count rather than exact position; do not weaken it to check nothing.

- [ ] **Step 7: Add a regression test for the no-merge (single representation) and different-types (not merged) cases**

Add to `test/Frank.Tests/NegotiateBuilderTests.fs`'s test list:

```fsharp
          testCase "produces metadata from a single representation is unaffected by the merge"
          <| fun () ->
              let def =
                  negotiate {
                      accepts "application/json" (handler {
                          produces typeof<Product> 200
                          handle (writeText "json")
                      })
                      accepts "text/html" (writeText "html")
                  }

              let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
              Expect.hasLength produces 1 "One representation's metadata should pass through unmerged"
              Expect.sequenceEqual produces.[0].ContentTypes [ "application/json" ] "Content types unchanged for a single representation"

          testCase "produces metadata from two representations sharing status code and type is merged into one"
          <| fun () ->
              let def =
                  negotiate {
                      accepts "text/html" (handler {
                          produces typeof<Product> 200 [ "text/html" ]
                          handle (writeText "html")
                      })
                      accepts "application/json" (handler {
                          produces typeof<Product> 200 [ "application/json" ]
                          handle (writeText "json")
                      })
                  }

              let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
              Expect.hasLength produces 1 "Same status code + same type should merge into one metadata object"
              Expect.containsAll produces.[0].ContentTypes [ "text/html"; "application/json" ] "Merged entry should carry both content types"

          testCase "produces metadata from two representations sharing status code but different types is NOT merged"
          <| fun () ->
              let def =
                  negotiate {
                      accepts "application/json" (handler {
                          produces typeof<Product> 200
                          handle (writeText "json")
                      })
                      accepts "application/xml" (handler {
                          produces typeof<string> 200 [ "application/xml" ]
                          handle (writeText "xml")
                      })
                  }

              let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
              Expect.hasLength produces 2 "Different response types sharing a status code must stay separate -- documented remaining limitation"
```

Re-run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj` — expect PASS, full suite.

- [ ] **Step 8: Commit**

```bash
git add src/Frank/NegotiateBuilder.fs test/Frank.Tests/NegotiateBuilderTests.fs
git commit -m "fix(frank): merge negotiate { } metadata sharing status code and type

Task 5 found that Microsoft.AspNetCore.OpenApi's document generator
keeps only the last-registered IProducesResponseTypeMetadata per
status code, silently dropping sibling representations' content
types from the generated document -- confirmed as inherent framework
behavior (reproduces with a bare minimal API, zero Frank code
involved), not a Frank defect. Fixes it by merging negotiate { }
representations that share both status code and response type into
one metadata object with a unioned content-types list before they
ever leave NegotiateBuilder -- the exact one-object-many-content-types
shape already proven to reach the document correctly. Representations
sharing a status code with genuinely different response types are a
documented remaining limitation, not fixed here.

Fixes the gap found in and closes out #482."
```

---

## Self-Review

**1. Spec coverage** (against `docs/superpowers/specs/2026-07-31-content-negotiation-design.md`):
- Goal 1 (lean, Frank-native primitive, no `AddMvcCore()`) → Task 1.
- Goal 2 (independent producers per media type) → Task 1 (direct producers), Task 3 (value-returning producers still independent per representation).
- Goal 3 (tested `Accept` selection incl. quality, 406) → Task 1's tests.
- Goal 4 (working sample) → Task 6.
- Goal 5 (free OpenAPI integration) → Task 5 verified this claim was only partially true; Task 8 (added after Task 5 ran) closes the gap for the common same-status-code-same-type case, with a documented remaining limitation for the different-types case.
- Goal 6 (keep `IOutputFormatter`, real-tested) → Task 2.
- Goal 7 (analyzer gap) → Task 7.
- *Registering a wildcard/catch-all representation* (shadowing caveat, Content-Type not auto-set, composes with `ctx.Negotiate`) → Task 1's tests.
- *Bridging to `IOutputFormatter`* (`viaOutputFormatter`, auto-format overloads, list sugar) → Tasks 2–4.
- *Analyzer coverage for duplicate `accepts`* → Task 7.
- *Sample fix* → Task 6.

**2. Placeholder scan:** No "TBD"/"handle appropriately"/deferred-detail steps remain — every code block is complete and every test asserts a concrete value. Two spots explicitly flag *which specific API call* to verify against the compiler (`MediaTypeHeaderValueComparer`/`MatchesMediaType` in Task 1, `OutputFormatterSelector.SelectFormatter`/`MediaTypeCollection.Add` in Task 2) rather than leaving the whole step vague — this mirrors the design doc's own explicit "verify against real API" notes, not a gap in the plan.

**3. Type consistency:** `viaOutputFormatter: mediaType: string -> body: 'a -> ctx: HttpContext -> Task` (Task 2) is called identically in Task 3's two new `Accepts` overloads. `NegotiateSpec.Representations: (string * RequestDelegate) list` and `NegotiateSpec.Metadata: obj list` are used consistently across Tasks 1, 3, and 4. `HandlerDefinition.{Handler; Metadata}` (pre-existing) is read the same way in Task 1's `HandlerDefinition`-accepting overload as everywhere else in the codebase.

**4. Task Right-Sizing:** each task ends with its own passing test run and its own commit; Task 5 (metadata check) and Task 6 (sample fix) intentionally add no reusable production surface, since they exist to *validate* claims made by earlier tasks, not to be depended on by later ones.

---

**Plan complete and saved to `docs/superpowers/plans/2026-07-31-content-negotiation.md`. Two execution options:**

**1. Subagent-Driven (recommended)** - I dispatch a fresh subagent per task, review between tasks, fast iteration

**2. Inline Execution** - Execute tasks in this session using executing-plans, batch execution with checkpoints

**Which approach?**
