# Lift NegotiateBuilder to Routing-Layer Dispatch — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `NegotiateBuilder`'s internal per-request dispatch function with real ASP.NET Core routing-layer dispatch, so `Accept`-header representation selection happens in the matcher/DFA — via a custom `MatcherPolicy` — before any handler code runs, the same class of mechanism the framework already uses for `Consumes`/request `Content-Type` dispatch (`AcceptsMatcherPolicy`).

**Architecture:** `NegotiateBuilder.Run` changes from producing one `HandlerDefinition` (dispatch happens inside its `RequestDelegate`) to producing a `HandlerDefinition list` — one per representation, each becoming its own `RouteEndpoint` at the same route+verb, tagged with a new `ProducesMediaTypeMetadata` marker. A new `FrankProducesMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy`, auto-registered by `webHost { }`, selects among those endpoints per-request using the same RFC 9110 §12.5.1 matching logic `NegotiateBuilder` already has (extracted, unchanged, into a shared `MediaTypeNegotiation` module both files depend on). `ResourceSpec.Handlers` gains a per-entry metadata slot so each generated `RouteEndpoint` carries its own metadata directly, instead of today's method-scoped-convention trick (which only worked because at most one handler existed per HTTP method).

**Tech Stack:** F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core), `Microsoft.AspNetCore.Routing.Matching` (`MatcherPolicy`, `IEndpointSelectorPolicy`, `CandidateSet`), `Microsoft.Net.Http.Headers` (unchanged, already used), Expecto + `Microsoft.AspNetCore.TestHost` for integration tests (already referenced in `Frank.Tests.fsproj`), BenchmarkDotNet in a new `benchmarks/Frank.Benchmarks/` project.

## Global Constraints

- Every `.fs` file under `src/Frank/` gets a matching `.fsi` directly above it in `Frank.fsproj`'s `<Compile>` order. Members needed by another file in the same assembly are `internal` (not `private`) in both files.
- Verify every `src/Frank/` change with a real build across all three target frameworks (`dotnet build src/Frank/Frank.fsproj` builds `net8.0;net9.0;net10.0` by default) — signature mismatches only surface at compile time.
- This is a binary-breaking, source-compatible change: every existing `negotiate { accepts "..." handler }` call site keeps compiling unchanged, because `NegotiateBuilder.Run`'s new `HandlerDefinition list` return type flows straight into the new `ResourceBuilder.Get`/`Post`/etc. `HandlerDefinition list` overload — no call-site edits required in `Frank.Alps.Sample`/`Frank.Rdf.Sample`/`Frank.OpenApi.Sample`. Verify this by building the samples, don't assume it.
- Design reference: `docs/superpowers/specs/2026-07-31-content-negotiation-design.md` (the mechanism this supersedes) and the brainstorming transcript this plan was drafted from — read them if anything below is unclear about *why*.
- Do not resurrect `Negotiation.dispatch` after deleting it, even for the benchmark comparison — the benchmark project builds its own minimal baseline reusing the shared `MediaTypeNegotiation` functions instead (Task 8). Keeping dead production code alive only for a benchmark is exactly the kind of thing CLAUDE.md's "don't add code beyond what's needed" rule forbids.

---

## File Structure

| File | Status | Responsibility |
|---|---|---|
| `src/Frank/MediaTypeNegotiation.fsi` / `.fs` | New | `isWildcard`, `matches`, `specificity`, `effectiveQuality`, `selectRepresentation` (ported unchanged from today's `NegotiateBuilder.fs`'s `Negotiation` module) + new `ProducesMediaTypeMetadata` type. Exists as its own file specifically so both `NegotiateBuilder.fs` (writes the metadata) and the new `ProducesMatcherPolicy.fs` (reads it, reuses the matching functions) can depend on it without a circular reference between those two. |
| `src/Frank/NegotiateBuilder.fsi` / `.fs` | Modified | `NegotiateSpec.Representations` gains per-entry metadata; `Run` returns `HandlerDefinition list`; `dispatch` deleted (routing does this now); `mergeProducesMetadata` stays, now called once and broadcast to every representation. |
| `src/Frank/ProducesMatcherPolicy.fsi` / `.fs` | New | `FrankProducesMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy` — the real routing-layer dispatch. |
| `src/Frank/HandlerDefinition.fsi` / `.fs` | Modified | Delete `HandlerDefinitionMetadata.toConventions` (dead once `ResourceBuilder` attaches metadata per-entry directly). |
| `src/Frank/ResourceBuilder.fsi` / `.fs` | Modified | `ResourceSpec.Handlers` gains per-entry metadata; `Build` attaches it directly; `AddMethodMetadata` deleted; new `Get`/`Post`/etc. `HandlerDefinition list` overloads. |
| `src/Frank/WebHostBuilder.fsi` / `.fs` | Modified | Default `Services` registers `FrankProducesMatcherPolicy` as a `MatcherPolicy` singleton, unconditionally. |
| `src/Frank/Frank.fsproj` | Modified | Insert `MediaTypeNegotiation.fsi/.fs` before `NegotiateBuilder`, `ProducesMatcherPolicy.fsi/.fs` after it. |
| `test/Frank.Tests/NegotiateBuilderTests.fs` | Rewritten | Every existing scenario, now driven through a real `TestServer` + HTTP request instead of `def.Handler.Invoke(ctx)` — there is no longer a single handler to invoke directly. |
| `test/Frank.Tests/MediaTypeNegotiationTests.fs` | New | Pure unit tests for the extracted matching functions (no HTTP context needed) — the fast-running counterpart to the integration tests above. |
| `test/Frank.OpenApi.Tests/NegotiateMetadataTests.fs` | Modified | Extend to prove the broadcast-merge mitigation: N separate `RouteEndpoint`s still produce one correct, complete OpenAPI operation despite the framework's known last-write-wins bug (`dotnet/aspnetcore#58329`). |
| `benchmarks/Frank.Benchmarks/Frank.Benchmarks.fsproj`, `Program.fs`, `NegotiationBenchmarks.fs` | New | BenchmarkDotNet comparison: baseline linear-scan dispatch (reusing `MediaTypeNegotiation.selectRepresentation` directly) vs. `FrankProducesMatcherPolicy` through a real `TestServer`. |
| `Frank.sln` | Modified | Add `benchmarks/Frank.Benchmarks/Frank.Benchmarks.fsproj`. |

---

## Task 1: Extract `MediaTypeNegotiation` — pure port, no behavior change

**Files:**
- Create: `src/Frank/MediaTypeNegotiation.fsi`
- Create: `src/Frank/MediaTypeNegotiation.fs`
- Modify: `src/Frank/Frank.fsproj` (insert both, positioned directly after `HandlerBuilder.fsi`/`.fs` and before `NegotiateBuilder.fsi`/`.fs`)
- Create: `test/Frank.Tests/MediaTypeNegotiationTests.fs`
- Modify: `test/Frank.Tests/Frank.Tests.fsproj` (add the new test file, positioned before `NegotiateBuilderTests.fs`)

**Interfaces:**
- Consumes: nothing Frank-specific — only `Microsoft.Net.Http.Headers.MediaTypeHeaderValue` (already a Frank dependency via the shared framework).
- Produces: `Frank.Builder.MediaTypeNegotiation.isWildcard/matches/specificity/effectiveQuality/selectRepresentation` (signatures unchanged from today's `internal module Negotiation` in `NegotiateBuilder.fs`) and `Frank.Builder.ProducesMediaTypeMetadata` (`MediaType: string`, `Ordinal: int` — a plain public class, no interface). Task 3 consumes `selectRepresentation`/`effectiveQuality` from here; Task 2 consumes them too.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Tests/MediaTypeNegotiationTests.fs` — direct unit tests against the pure functions, no `HttpContext` needed:

```fsharp
module Frank.Tests.MediaTypeNegotiationTests

open Expecto
open Frank.Builder

[<Tests>]
let tests =
    testList
        "MediaTypeNegotiation"
        [ testCase "selectRepresentation picks the exact match"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [ "application/json" ] [ "application/json"; "text/html" ]
              Expect.equal result (Some 0) "application/json is index 0"

          testCase "selectRepresentation honors quality values over registration order"
          <| fun () ->
              let result =
                  MediaTypeNegotiation.selectRepresentation
                      [ "text/html;q=0.3, application/json;q=0.8" ]
                      [ "text/html"; "application/json" ]
              Expect.equal result (Some 1) "application/json (index 1) has higher quality"

          testCase "selectRepresentation returns None when nothing matches"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [ "application/xml" ] [ "application/json" ]
              Expect.equal result None "No registered type matches application/xml"

          testCase "selectRepresentation treats an absent Accept as */* -- first registered wins"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [] [ "application/json"; "text/html" ]
              Expect.equal result (Some 0) "Empty Accept defaults to the first representation"

          testCase "selectRepresentation treats a malformed Accept as */* -- first registered wins"
          <| fun () ->
              let result =
                  MediaTypeNegotiation.selectRepresentation [ "not a media type at all;;;" ] [ "application/json"; "text/html" ]
              Expect.equal result (Some 0) "Malformed Accept defaults to the first representation"

          testCase "selectRepresentation rejects q=0 outright, not merely deprioritizes"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [ "application/json;q=0" ] [ "application/json" ]
              Expect.equal result None "q=0 must exclude the representation entirely"

          testCase "a wildcard registered representation matches any concrete Accept entry"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [ "image/png" ] [ "application/json"; "*/*" ]
              Expect.equal result (Some 1) "Only the wildcard entry matches image/png"

          testCase "application/ld+json Accept never matches a registered application/json via suffix leniency"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [ "application/ld+json" ] [ "application/json" ]
              Expect.equal result None "Concrete-vs-concrete comparison must be exact, not suffix-lenient"

          testCase "ProducesMediaTypeMetadata exposes MediaType and Ordinal"
          <| fun () ->
              let m = ProducesMediaTypeMetadata("application/json", 0)
              Expect.equal m.MediaType "application/json" "MediaType round-trips"
              Expect.equal m.Ordinal 0 "Ordinal round-trips" ]
```

- [ ] **Step 2: Run the tests to verify they fail (module doesn't exist yet)**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~MediaTypeNegotiationTests"`
Expected: FAIL to compile — `MediaTypeNegotiation`/`ProducesMediaTypeMetadata` not defined.

- [ ] **Step 3: Create `MediaTypeNegotiation.fsi`**

```fsharp
namespace Frank.Builder

open Microsoft.Net.Http.Headers

/// One representation's declared media type, paired with its position among
/// its siblings for tie-breaking. Read by `FrankProducesMatcherPolicy`, written by
/// `NegotiateBuilder.Run` -- one instance per representation's own `RouteEndpoint`.
[<Sealed>]
type ProducesMediaTypeMetadata =
    new: mediaType: string * ordinal: int -> ProducesMediaTypeMetadata
    member MediaType: string
    member Ordinal: int

/// RFC 9110 §12.5.1 media-type matching and quality-value selection, shared
/// between `NegotiateBuilder` (today's dispatch, pending removal) and
/// `FrankProducesMatcherPolicy` (routing-layer dispatch). Pure functions --
/// no `HttpContext` dependency -- so both a request-time policy and a
/// unit test can call them directly.
module internal MediaTypeNegotiation =

    val isWildcard: mediaType: string -> bool

    val matches: candidate: MediaTypeHeaderValue -> registered: string -> bool

    val specificity: entry: MediaTypeHeaderValue -> int

    val effectiveQuality: parsed: MediaTypeHeaderValue list -> mt: string -> float option

    /// Selects the index of the representation that should serve this request,
    /// given the raw Accept header values and the registered media types, in
    /// registration order. See `NegotiateBuilder.fs`'s original doc comment
    /// (git history) for the full RFC 9110 rationale -- behavior is unchanged
    /// from the pre-extraction implementation.
    val selectRepresentation: acceptValues: string seq -> mediaTypes: string list -> int option
```

- [ ] **Step 4: Create `MediaTypeNegotiation.fs`**

Port `isWildcard`, `matches`, `specificity`, `effectiveQuality`, `selectRepresentation` verbatim from the current `internal module Negotiation` in `src/Frank/NegotiateBuilder.fs` (lines 22–144) — same bodies, same doc comments, only the module name changes (`Negotiation` → `MediaTypeNegotiation`). Do not port `rejectWildcardAutoFormat`, `dispatch`, or `mergeProducesMetadata` — those stay in `NegotiateBuilder.fs` (the first is CE-specific validation, the second is being deleted in Task 3, the third is `HandlerDefinition`-list-specific bookkeeping that belongs with the CE, not the raw matching logic). Add the new type above the module:

```fsharp
namespace Frank.Builder

open Microsoft.Net.Http.Headers

[<Sealed>]
type ProducesMediaTypeMetadata(mediaType: string, ordinal: int) =
    member _.MediaType = mediaType
    member _.Ordinal = ordinal

module internal MediaTypeNegotiation =
    // ... isWildcard, matches, specificity, effectiveQuality, selectRepresentation
    // exactly as in today's NegotiateBuilder.fs's Negotiation module ...
```

- [ ] **Step 5: Add both files to `Frank.fsproj`, positioned before `NegotiateBuilder`**

```xml
<Compile Include="HandlerBuilder.fsi" />
<Compile Include="HandlerBuilder.fs" />
<Compile Include="MediaTypeNegotiation.fsi" />
<Compile Include="MediaTypeNegotiation.fs" />
<Compile Include="WebLink.fsi" />
<Compile Include="WebLink.fs" />
<Compile Include="NegotiateBuilder.fsi" />
<Compile Include="NegotiateBuilder.fs" />
```

- [ ] **Step 6: Run tests, verify they pass**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~MediaTypeNegotiationTests"`
Expected: PASS, all 9 cases.

- [ ] **Step 7: Build all three target frameworks**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: 0 errors across `net8.0;net9.0;net10.0`.

- [ ] **Step 8: Commit**

```bash
git add src/Frank/MediaTypeNegotiation.fsi src/Frank/MediaTypeNegotiation.fs src/Frank/Frank.fsproj test/Frank.Tests/MediaTypeNegotiationTests.fs test/Frank.Tests/Frank.Tests.fsproj
git commit -m "feat(frank): extract MediaTypeNegotiation, add ProducesMediaTypeMetadata"
```

---

## Task 2: `FrankProducesMatcherPolicy` — real routing-layer dispatch

**Files:**
- Create: `src/Frank/ProducesMatcherPolicy.fsi`
- Create: `src/Frank/ProducesMatcherPolicy.fs`
- Modify: `src/Frank/Frank.fsproj` (insert after `NegotiateBuilder.fsi`/`.fs`, before `ResourceBuilder.fsi`/`.fs`)
- Create: `test/Frank.Tests/ProducesMatcherPolicyTests.fs`
- Modify: `test/Frank.Tests/Frank.Tests.fsproj`

**Interfaces:**
- Consumes: `Frank.Builder.MediaTypeNegotiation.effectiveQuality` (Task 1), `Frank.Builder.ProducesMediaTypeMetadata` (Task 1), `Microsoft.AspNetCore.Routing.Matching.{MatcherPolicy, IEndpointSelectorPolicy, CandidateSet}`.
- Produces: `Frank.Builder.FrankProducesMatcherPolicy` (a public, parameterless-constructible `MatcherPolicy`). Task 5 (`WebHostBuilder`) registers this type in DI: `services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>()`.

This task is tested through a real `TestServer`, not a hand-built `CandidateSet` — matching the harness pattern already established in `test/Frank.Alps.Tests/AlpsDocumentIntegrationTests.fs`.

- [ ] **Step 1: Write the failing test**

Create `test/Frank.Tests/ProducesMatcherPolicyTests.fs`:

```fsharp
module Frank.Tests.ProducesMatcherPolicyTests

open System.Net
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Matching
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder

/// Same pattern as `Frank.Alps.Tests.AlpsDocumentIntegrationTests`'s `TestEndpointDataSource` --
/// `ResourceEndpointDataSource` is `internal` to `Frank.dll` with no `InternalsVisibleTo`.
type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = Microsoft.Extensions.Primitives.NullChangeToken.Singleton :> _

/// Builds two RouteEndpoints at the identical path+verb, tagged with
/// ProducesMediaTypeMetadata directly -- bypassing NegotiateBuilder entirely, since
/// this task tests only the routing policy, not the CE that will produce these
/// endpoints starting in Task 3.
let private buildTaggedEndpoint (path: string) (mediaType: string) (ordinal: int) (body: string) : Endpoint =
    let pattern = Patterns.RoutePatternFactory.Parse path
    let handler =
        RequestDelegate(fun ctx ->
            ctx.Response.ContentType <- mediaType
            ctx.Response.WriteAsync(body))
    let builder = RouteEndpointBuilder(handler, pattern, 0)
    builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
    builder.Metadata.Add(ProducesMediaTypeMetadata(mediaType, ordinal))
    builder.Build()

let private buildHost (endpoints: Endpoint[]) : IHost =
    Host
        .CreateDefaultBuilder([||])
        .ConfigureWebHost(fun webBuilder ->
            webBuilder
                .UseTestServer()
                .ConfigureServices(fun services ->
                    services.AddRouting() |> ignore
                    services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore)
                .Configure(fun app ->
                    app.UseRouting() |> ignore
                    app.UseEndpoints(fun endpoints' ->
                        endpoints'.DataSources.Add(TestEndpointDataSource endpoints))
                    |> ignore)
            |> ignore)
        .Build()

[<Tests>]
let tests =
    testList
        "FrankProducesMatcherPolicy"
        [ testCaseTask "selects the endpoint matching an exact Accept header"
          <| task {
              let endpoints =
                  [| buildTaggedEndpoint "/x" "application/json" 0 "json"
                     buildTaggedEndpoint "/x" "text/html" 1 "html" |]
              use host = buildHost endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              use request = new HttpRequestMessage(HttpMethod.Get, "/x")
              request.Headers.Accept.ParseAdd("text/html")
              let! response = client.SendAsync(request)
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "html" "The text/html-tagged endpoint should have been selected"
              Expect.equal (response.Content.Headers.ContentType.MediaType) "text/html" "Content-Type set to the winner"
          }

          testCaseTask "sets Vary: Accept on a successful dispatch"
          <| task {
              let endpoints = [| buildTaggedEndpoint "/x" "application/json" 0 "json" |]
              use host = buildHost endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              use request = new HttpRequestMessage(HttpMethod.Get, "/x")
              request.Headers.Accept.ParseAdd("application/json")
              let! response = client.SendAsync(request)
              Expect.contains (response.Headers.Vary |> List.ofSeq) "Accept" "Vary: Accept must be present"
          }

          testCaseTask "responds 406 with no body when nothing matches"
          <| task {
              let endpoints = [| buildTaggedEndpoint "/x" "application/json" 0 "json" |]
              use host = buildHost endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              use request = new HttpRequestMessage(HttpMethod.Get, "/x")
              request.Headers.Accept.ParseAdd("application/xml")
              let! response = client.SendAsync(request)
              Expect.equal response.StatusCode HttpStatusCode.NotAcceptable "Should be 406"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "No body on 406"
          }

          testCaseTask "absent Accept selects the lowest-Ordinal (first-registered) endpoint"
          <| task {
              let endpoints =
                  [| buildTaggedEndpoint "/x" "application/json" 0 "json"
                     buildTaggedEndpoint "/x" "text/html" 1 "html" |]
              use host = buildHost endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              let! response = client.GetAsync("/x")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "json" "Ordinal 0 wins on an absent Accept header"
          }

          testCaseTask "an unrelated endpoint at a different path is untouched"
          <| task {
              let endpoints =
                  [| buildTaggedEndpoint "/x" "application/json" 0 "json"
                     buildTaggedEndpoint "/y" "text/plain" 0 "plain" |]
              use host = buildHost endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              let! response = client.GetAsync("/y")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "plain" "/y has only one representation, unaffected by /x's negotiation"
          } ]
```

- [ ] **Step 2: Run the test to verify it fails (type doesn't exist yet)**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~ProducesMatcherPolicyTests"`
Expected: FAIL to compile — `FrankProducesMatcherPolicy` not defined.

- [ ] **Step 3: Create `ProducesMatcherPolicy.fsi`**

```fsharp
namespace Frank.Builder

open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Matching

/// Routing-layer counterpart to the framework's own `AcceptsMatcherPolicy`
/// (request Content-Type / Consumes) -- this one dispatches by response
/// representation, keyed on the `Accept` request header and each candidate
/// endpoint's `ProducesMediaTypeMetadata`. Registered as a `MatcherPolicy`
/// singleton by `webHost { }` (`WebHostBuilder.fs`) unconditionally; a no-op
/// for any app with no `ProducesMediaTypeMetadata`-tagged endpoints.
[<Sealed>]
type FrankProducesMatcherPolicy =
    inherit MatcherPolicy
    new: unit -> FrankProducesMatcherPolicy
    override Order: int
    interface IEndpointSelectorPolicy
```

- [ ] **Step 4: Create `ProducesMatcherPolicy.fs`**

```fsharp
namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Matching
open Microsoft.Net.Http.Headers

[<Sealed>]
type FrankProducesMatcherPolicy() =
    inherit MatcherPolicy()

    // Matches the framework's own NegotiationMatcherPolicy<T> (Accept-Encoding):
    // run very late, after any other policy has already invalidated candidates on
    // other grounds (auth, etc.), so this only negotiates among what's left.
    static let http406Endpoint =
        lazy
            Endpoint(
                (fun ctx ->
                    ctx.Response.StatusCode <- StatusCodes.Status406NotAcceptable
                    Task.CompletedTask),
                EndpointMetadataCollection.Empty,
                "406 HTTP Not Acceptable (Frank negotiate { })"
            )

    override _.Order = 10_000

    interface IEndpointSelectorPolicy with
        member _.AppliesToEndpoints(endpoints) =
            endpoints
            |> Seq.exists (fun e -> not (isNull (e.Metadata.GetMetadata<ProducesMediaTypeMetadata>())))

        member _.ApplyAsync(httpContext, candidates) =
            let raw: System.Collections.Generic.IList<string> = httpContext.Request.Headers.Accept |> Array.ofSeq :> _

            let parsed =
                match MediaTypeHeaderValue.TryParseList(raw) with
                | true, values -> values |> List.ofSeq
                | false, _ -> []

            let parsed =
                if List.isEmpty parsed then
                    [ MediaTypeHeaderValue.Parse("*/*") ]
                else
                    parsed

            let mutable sawTaggedCandidate = false
            let mutable bestIndex = -1
            let mutable bestQuality = 0.0
            let mutable bestOrdinal = System.Int32.MaxValue

            for i in 0 .. candidates.Count - 1 do
                if candidates.IsValidCandidate(i) then
                    let metadata = candidates.[i].Endpoint.Metadata.GetMetadata<ProducesMediaTypeMetadata>()

                    if not (isNull metadata) then
                        sawTaggedCandidate <- true

                        match MediaTypeNegotiation.effectiveQuality parsed metadata.MediaType with
                        | Some quality when quality > 0.0 ->
                            if
                                bestIndex < 0
                                || quality > bestQuality
                                || (quality = bestQuality && metadata.Ordinal < bestOrdinal)
                            then
                                bestIndex <- i
                                bestQuality <- quality
                                bestOrdinal <- metadata.Ordinal
                        | _ -> ()

            if sawTaggedCandidate then
                httpContext.Response.Headers.Append("Vary", "Accept")

                if bestIndex < 0 then
                    httpContext.SetEndpoint(http406Endpoint.Value)
                    httpContext.Request.RouteValues <- null
                else
                    for i in 0 .. candidates.Count - 1 do
                        if i <> bestIndex && candidates.IsValidCandidate(i) then
                            let metadata = candidates.[i].Endpoint.Metadata.GetMetadata<ProducesMediaTypeMetadata>()

                            if not (isNull metadata) then
                                candidates.SetValidity(i, false)

                    let winner = candidates.[bestIndex].Endpoint.Metadata.GetMetadata<ProducesMediaTypeMetadata>()

                    if not (MediaTypeNegotiation.isWildcard winner.MediaType) then
                        httpContext.Response.ContentType <- winner.MediaType

            Task.CompletedTask
```

- [ ] **Step 5: Add both files to `Frank.fsproj`, after `NegotiateBuilder`**

```xml
<Compile Include="NegotiateBuilder.fsi" />
<Compile Include="NegotiateBuilder.fs" />
<Compile Include="ProducesMatcherPolicy.fsi" />
<Compile Include="ProducesMatcherPolicy.fs" />
<Compile Include="ResourceBuilder.fsi" />
<Compile Include="ResourceBuilder.fs" />
```

- [ ] **Step 6: Add `Microsoft.AspNetCore.TestHost` usage confirmed available, run tests**

`Frank.Tests.fsproj` already references `Microsoft.AspNetCore.TestHost` (verified). Add `ProducesMatcherPolicyTests.fs` to its `<Compile>` list, positioned after `MediaTypeNegotiationTests.fs` and before `NegotiateBuilderTests.fs`.

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~ProducesMatcherPolicyTests"`
Expected: PASS, all 5 cases. This is the empirical proof (flagged as needed during brainstorming) that setting `Response.ContentType`/`Vary` from inside `ApplyAsync` survives to the actual HTTP response.

- [ ] **Step 7: Build all three target frameworks**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: 0 errors across `net8.0;net9.0;net10.0`.

- [ ] **Step 8: Commit**

```bash
git add src/Frank/ProducesMatcherPolicy.fsi src/Frank/ProducesMatcherPolicy.fs src/Frank/Frank.fsproj test/Frank.Tests/ProducesMatcherPolicyTests.fs test/Frank.Tests/Frank.Tests.fsproj
git commit -m "feat(frank): add FrankProducesMatcherPolicy for routing-layer Accept dispatch"
```

---

## Task 3: `ResourceSpec.Handlers` gains per-entry metadata; delete dead conventions code

Do this before Task 4 (`NegotiateBuilder` rewire) because Task 4's new `Get`/`Post`/etc. overload needs `ResourceBuilder.AddHandlerDefinition` already simplified.

**Files:**
- Modify: `src/Frank/HandlerDefinition.fsi`, `src/Frank/HandlerDefinition.fs` (delete `HandlerDefinitionMetadata.toConventions`)
- Modify: `src/Frank/ResourceBuilder.fsi`, `src/Frank/ResourceBuilder.fs`
- Create: `test/Frank.Tests/ResourceBuilderMultiHandlerTests.fs`
- Modify: `test/Frank.Tests/Frank.Tests.fsproj`

**Interfaces:**
- Consumes: nothing new.
- Produces: `Frank.Builder.ResourceSpec.Handlers: (string * RequestDelegate * obj list) list` (was `(string * RequestDelegate) list`); new `ResourceBuilder.Get/Post/Put/Patch/Delete/Head/Options(spec: ResourceSpec, handlerDefs: HandlerDefinition list) : ResourceSpec` overloads, one per HTTP method already carrying a `HandlerDefinition` overload today. Task 4 (`NegotiateBuilder`) relies on these existing by the time it lands.

Existing behavior this must NOT change: a resource with exactly one handler per method still produces exactly the same `RouteEndpoint` metadata as today (this is the regression risk — verify with the existing `ResourceBuilderMetadataTests.fs`, unchanged, still passing).

- [ ] **Step 1: Write the failing test**

Create `test/Frank.Tests/ResourceBuilderMultiHandlerTests.fs`:

```fsharp
module Frank.Tests.ResourceBuilderMultiHandlerTests

open System.Net
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder

type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = Microsoft.Extensions.Primitives.NullChangeToken.Singleton :> _

let private buildHost (endpoints: Endpoint[]) : IHost =
    Host
        .CreateDefaultBuilder([||])
        .ConfigureWebHost(fun webBuilder ->
            webBuilder
                .UseTestServer()
                .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                .Configure(fun app ->
                    app.UseRouting() |> ignore
                    app.UseEndpoints(fun endpoints' -> endpoints'.DataSources.Add(TestEndpointDataSource endpoints))
                    |> ignore)
            |> ignore)
        .Build()

[<Tests>]
let tests =
    testList
        "ResourceBuilder multi-handler-per-method"
        [ testCaseTask "Get with a HandlerDefinition list expands to N RouteEndpoints, each with its own metadata"
          <| task {
              let defA =
                  { Handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("a"))
                    Metadata = [ box "marker-a" ] }
              let defB =
                  { Handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("b"))
                    Metadata = [ box "marker-b" ] }

              let built = (resource "/x") { get [ defA; defB ] }

              Expect.equal built.Endpoints.Length 2 "Two representations become two RouteEndpoints"

              let metadataOf (e: Endpoint) = e.Metadata |> Seq.filter (fun m -> m :? string) |> List.ofSeq

              Expect.contains (metadataOf built.Endpoints.[0] @ metadataOf built.Endpoints.[1]) (box "marker-a") "First endpoint's own metadata is attached"
              Expect.contains (metadataOf built.Endpoints.[0] @ metadataOf built.Endpoints.[1]) (box "marker-b") "Second endpoint's own metadata is attached"

              // Each endpoint carries ONLY its own metadata, not the other's -- this is
              // exactly the bug the method-scoped-convention trick had once multiple
              // handlers share one method.
              Expect.equal (metadataOf built.Endpoints.[0]) [ box "marker-a" ] "Endpoint 0 has only its own metadata"
              Expect.equal (metadataOf built.Endpoints.[1]) [ box "marker-b" ] "Endpoint 1 has only its own metadata"
          }

          testCaseTask "both endpoints are independently reachable through real routing"
          <| task {
              let defA =
                  { Handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("a"))
                    Metadata = [] }
              let defB =
                  { Handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("b"))
                    Metadata = [] }

              let built = (resource "/x") { get [ defA; defB ] }
              use host = buildHost built.Endpoints
              do! host.StartAsync()
              use client = host.GetTestClient()
              let! response = client.GetAsync("/x")
              // With no policy to disambiguate (FrankProducesMatcherPolicy lands in Task 2/5,
              // not wired into this bare test host), this proves only that both endpoints
              // reach the DFA -- an AmbiguousMatchException here would be the real 500 to
              // catch if the per-entry Handlers shape were wrong.
              Expect.equal response.StatusCode HttpStatusCode.InternalServerError "Ambiguous without a disambiguating policy -- expected here, proves both endpoints registered"
          } ]
```

- [ ] **Step 2: Run to verify it fails (no `get: HandlerDefinition list` overload yet)**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~ResourceBuilderMultiHandlerTests"`
Expected: FAIL to compile.

- [ ] **Step 3: Modify `HandlerDefinition.fsi`/`.fs` — delete `toConventions`**

`HandlerDefinition.fsi`, remove:
```fsharp
module HandlerDefinitionMetadata =
    val toConventions : def:HandlerDefinition -> (EndpointBuilder -> unit) list
```
and the now-unused `open Microsoft.AspNetCore.Builder` if nothing else in the file needs it.

`HandlerDefinition.fs`, remove the corresponding `module HandlerDefinitionMetadata = let toConventions ...` block.

- [ ] **Step 4: Modify `ResourceBuilder.fsi`**

```fsharp
type ResourceSpec =
    { Name: string
      Handlers: (string * RequestDelegate * obj list) list
      Metadata: (EndpointBuilder -> unit) list }

    static member Empty: ResourceSpec
    member Build: routeTemplate: string -> Resource

[<Sealed>]
type ResourceBuilder =
    // ... unchanged members ...

    static member AddMetadata: spec: ResourceSpec * convention: (EndpointBuilder -> unit) -> ResourceSpec

    // AddMethodMetadata: REMOVED (dead once AddHandlerDefinition attaches metadata directly)

    static member AddHandlerDefinition:
        httpMethod: string * spec: ResourceSpec * def: HandlerDefinition -> ResourceSpec

    static member AddHandlerDefinitions:
        httpMethod: string * spec: ResourceSpec * defs: HandlerDefinition list -> ResourceSpec

    static member AddHandler: httpMethod: string * spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec
    // ... other AddHandler overloads unchanged in signature (bodies change internally to add `[]` as the third tuple element) ...

    // Existing per-method HandlerDefinition overloads unchanged, e.g.:
    member Get: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    // New: one per method that already has the HandlerDefinition overload
    member Get: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec
    member Post: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec
    member Put: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec
    member Patch: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec
    member Delete: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec
    member Head: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec
    member Options: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec
```

- [ ] **Step 5: Modify `ResourceBuilder.fs`**

`ResourceSpec.Build`, change the endpoint-building loop to unpack and attach the third tuple element directly:

```fsharp
member spec.Build(routeTemplate) =
    let { Name = name
          Handlers = handlers
          Metadata = metadata } =
        spec

    let routePattern = Patterns.RoutePatternFactory.Parse routeTemplate

    let endpoints =
        [| for httpMethod, handler, ownMetadata in handlers ->
               let displayName =
                   httpMethod + " " + (if String.IsNullOrEmpty name then routeTemplate else name)

               let builder = RouteEndpointBuilder(handler, routePattern, 0)
               builder.DisplayName <- displayName
               builder.Metadata.Add(HttpMethodMetadata [| httpMethod |])
               builder.Metadata.Add(handler.Method)

               for m in ownMetadata do
                   builder.Metadata.Add m

               for convention in metadata do
                   convention builder

               builder.Build() |]

    { Endpoints = endpoints }
```

Replace `AddMethodMetadata` (delete it) and simplify `AddHandlerDefinition`:

```fsharp
static member AddHandlerDefinition(httpMethod: string, spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec =
    { spec with
        Handlers = (httpMethod, def.Handler, def.Metadata) :: spec.Handlers }

static member AddHandlerDefinitions(httpMethod: string, spec: ResourceSpec, defs: HandlerDefinition list) : ResourceSpec =
    defs
    |> List.fold (fun s def -> ResourceBuilder.AddHandlerDefinition(httpMethod, s, def)) spec
```

Update every `AddHandler` overload to add `[]` as the third tuple element, e.g.:

```fsharp
static member AddHandler(httpMethod, spec, handler) =
    { spec with
        Handlers = (httpMethod, handler, []) :: spec.Handlers }

static member AddHandler(httpMethod, spec, handler: HttpContext -> Task<'a>) =
    { spec with
        Handlers = (httpMethod, RequestDelegate(fun ctx -> handler ctx :> Task), []) :: spec.Handlers }
```
(same pattern for the remaining three `AddHandler` overloads — `HttpContext -> Task<HttpContext option>) -> ...`, `HttpContext -> Async<'a>`, `HttpContext -> unit`).

Add the new `HandlerDefinition list` members, one per method, immediately after each method's existing `HandlerDefinition` member, e.g.:

```fsharp
member _.Get(spec: ResourceSpec, handlerDef: HandlerDefinition) =
    ResourceBuilder.AddHandlerDefinition(HttpMethods.Get, spec, handlerDef)

member _.Get(spec: ResourceSpec, handlerDefs: HandlerDefinition list) =
    ResourceBuilder.AddHandlerDefinitions(HttpMethods.Get, spec, handlerDefs)
```
(repeat for `Post`, `Put`, `Patch`, `Delete`, `Head`, `Options` — every method that has today's `HandlerDefinition` overload; `Connect`/`Trace` don't have one today per the current source, so skip those two).

- [ ] **Step 6: Add the new test file to `Frank.Tests.fsproj`**, run everything

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~ResourceBuilderMultiHandlerTests"`
Expected: PASS, both cases.

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~ResourceBuilderMetadataTests"`
Expected: PASS unchanged — proves the single-handler-per-method path is unaffected.

- [ ] **Step 7: Build all three target frameworks**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: 0 errors across `net8.0;net9.0;net10.0`.

- [ ] **Step 8: Commit**

```bash
git add src/Frank/HandlerDefinition.fsi src/Frank/HandlerDefinition.fs src/Frank/ResourceBuilder.fsi src/Frank/ResourceBuilder.fs test/Frank.Tests/ResourceBuilderMultiHandlerTests.fs test/Frank.Tests/Frank.Tests.fsproj
git commit -m "feat(frank): ResourceSpec.Handlers carries per-entry metadata, add HandlerDefinition list overloads"
```

---

## Task 4: Rewire `NegotiateBuilder` — `Run` returns `HandlerDefinition list`, broadcast-merged metadata

**Files:**
- Modify: `src/Frank/NegotiateBuilder.fsi`, `src/Frank/NegotiateBuilder.fs`
- Rewrite: `test/Frank.Tests/NegotiateBuilderTests.fs` (every scenario preserved, now driven through a real `TestServer` + `FrankProducesMatcherPolicy` instead of `def.Handler.Invoke(ctx)`)

**Interfaces:**
- Consumes: `Frank.Builder.MediaTypeNegotiation.*` (Task 1), `Frank.Builder.ProducesMediaTypeMetadata` (Task 1), `Frank.Builder.ResourceBuilder.Get/Post/etc(spec, handlerDefs: HandlerDefinition list)` (Task 3).
- Produces: `Frank.Builder.NegotiateSpec.Representations: (string * RequestDelegate * obj list) list` (was `(string * RequestDelegate) list`); `NegotiateSpec.Metadata` field removed; `NegotiateBuilder.Run(spec) : HandlerDefinition list` (was `HandlerDefinition`).

- [ ] **Step 1: Write the failing tests (full rewrite)**

Rewrite `test/Frank.Tests/NegotiateBuilderTests.fs`. Keep every existing `testCase` scenario name and assertion intent from the current file, converted to the `TestServer` harness pattern from Task 2's `ProducesMatcherPolicyTests.fs`. Shared setup:

```fsharp
module Frank.Tests.NegotiateBuilderTests

open System.Net
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Matching
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder

type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = Microsoft.Extensions.Primitives.NullChangeToken.Singleton :> _

let private buildHost (resource: Resource) (configureServices: IServiceCollection -> unit) : IHost =
    Host
        .CreateDefaultBuilder([||])
        .ConfigureWebHost(fun webBuilder ->
            webBuilder
                .UseTestServer()
                .ConfigureServices(fun services ->
                    services.AddRouting() |> ignore
                    services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore
                    configureServices services)
                .Configure(fun app ->
                    app.UseRouting() |> ignore
                    app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource resource.Endpoints))
                    |> ignore)
            |> ignore)
        .Build()

let private noServices (_: IServiceCollection) = ()

let private getWithAccept (host: IHost) (accept: string option) : Task<HttpResponseMessage> =
    task {
        use client = host.GetTestClient()
        use request = new HttpRequestMessage(HttpMethod.Get, "/x")
        accept |> Option.iter (fun a -> request.Headers.Accept.ParseAdd(a))
        return! client.SendAsync(request)
    }

let writeText (text: string) (ctx: HttpContext) : Task =
    task { do! ctx.Response.WriteAsync(text) }
```

Then, e.g. the exact-match and quality-value cases:

```fsharp
[<Tests>]
let tests =
    testList
        "NegotiateBuilder (routed through FrankProducesMatcherPolicy)"
        [ testCaseTask "selects the representation matching an exact Accept header"
          <| task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/json")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "json" "Body should come from the JSON representation"
              Expect.equal response.Content.Headers.ContentType.MediaType "application/json" "Content-Type should match the winning representation"
          }

          testCaseTask "quality values pick the higher-preference representation"
          <| task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "text/html" (writeText "html")
                              accepts "application/json" (writeText "json")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "text/html;q=0.3, application/json;q=0.8")
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "json" "Higher quality value should win regardless of registration order"
          }

          testCaseTask "responds 406 with no body when nothing matches"
          <| task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host (Some "application/xml")
              Expect.equal response.StatusCode HttpStatusCode.NotAcceptable "Should be Not Acceptable"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "" "No body should be written"
          }

          testCaseTask "absent Accept header selects the first-registered representation"
          <| task {
              let built =
                  (resource "/x") {
                      get (
                          negotiate {
                              accepts "application/json" (writeText "json")
                              accepts "text/html" (writeText "html")
                          }
                      )
                  }
              use host = buildHost built noServices
              do! host.StartAsync()
              let! response = getWithAccept host None
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "json" "First-registered representation is the default"
          } ]
```

Convert every remaining scenario from the CURRENT (pre-rewrite) `test/Frank.Tests/NegotiateBuilderTests.fs` — read that file directly (it is still fully intact in the working tree at the point this task starts; do not reconstruct it from memory) and, for each remaining `testCase`, apply exactly one of the three conversion rules below. No scenario, assertion, or closure-captured mutable flag from the original file may be dropped.

**Rule A — needs a real request (convert to `testCaseTask`, build a host, send an HTTP request):** every scenario that currently calls `def.Handler.Invoke(ctx).Wait()` and asserts on `ctx.Response.*` or on a closure-captured flag proving which producer ran. This is the majority of the remaining cases: `*/*` default, malformed Accept, only-selected-representation's-producer-runs (keep the `mutable jsonRan`/`htmlRan` flags exactly as today, just read them after `client.SendAsync` instead of after `def.Handler.Invoke`), wildcard catch-all, wildcard-registered-first footgun, bare `HttpContext -> unit` handler, the three q=0/quality-precedence RFC 9110 cases, value-returning `Task<'a>`/`Async<'a>` auto-format via `viaOutputFormatter` (these need `services.AddLogging(); services.AddMvcCore()` in `configureServices`, exactly as the current file's `services`/`ctx.RequestServices` setup does), value-returning composed with an independent producer, both `application/ld+json` vs `application/json` non-collision cases, `Vary: Accept` on success and on 406, and the wildcard-delegates-to-`ctx.Negotiate` case (needs `services.AddMvcCore().AddXmlSerializerFormatters()`, matching the current file exactly, and TWO requests against the SAME built resource — one Accept matching the concrete entry, one matching only the wildcard — exactly as the current file's two-context test does).

**Rule B — throws during construction, before any request (stays synchronous, no host):** the empty-block-throws case (`NegotiateBuilder().Run(NegotiateSpec.Empty)`) and the three wildcard-with-value-returning-handler-throws cases (`accepts "*/*"`/`"application/*"` with a `Task<'a>`/`Async<'a>` handler) — these fail inside `negotiate { }` itself, never reach routing. Keep them exactly as written today, using `messageOfThrow`.

**Rule C — inspects `NegotiateBuilder.Run`'s output directly, no host needed:** `handler{}` metadata contribution, `accepts [mediaTypes]` list sugar (assert `NegotiateBuilder().Accepts(NegotiateSpec.Empty, [...], handler).Representations` has length 2, exactly as today), and the three `produces`-metadata-merge cases. For the merge cases specifically: `NegotiateBuilder.Run` now returns a `HandlerDefinition list`, not one `HandlerDefinition` — assert `HandlerDefinition.findAll<IProducesResponseTypeMetadata>` against **every** entry in that list, confirming each carries the identical broadcast-merged set (see the worked example in Step 4 of this task's own description), not just the one representation that happened to declare `produces`.

Every converted case keeps the original file's exact assertion messages (the string passed to `Expect.*`) unless the mechanism changed makes the message inaccurate (e.g. "Content-Type should match the winning representation" is still accurate; a message that says "the handler" where it now must say "the response" needs updating, not dropping).

For the "produces metadata... merged" cases specifically, call the builder directly rather than through a host, since this task is testing `NegotiateBuilder.Run`'s own output, not routing:

```fsharp
testCase "produces metadata from two representations sharing status code and type is merged and broadcast to every representation"
<| fun () ->
    let defs =
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

    Expect.hasLength defs 2 "Two representations"

    for def in defs do
        let produces = HandlerDefinition.findAll<Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata> def
        Expect.hasLength produces 1 "Same status code + type merge into one metadata object"
        Expect.containsAll produces.[0].ContentTypes [ "text/html"; "application/json" ] "Every representation's endpoint carries the full merged content-type union"
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~NegotiateBuilderTests"`
Expected: FAIL to compile — `Run` still returns a single `HandlerDefinition`, `Representations` still lacks the third tuple element.

- [ ] **Step 3: Modify `NegotiateBuilder.fsi`**

```fsharp
namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type NegotiateSpec =
    { Representations: (string * RequestDelegate * obj list) list }

    static member Empty: NegotiateSpec

[<Sealed>]
type NegotiateBuilder =
    new: unit -> NegotiateBuilder

    member Yield: 'T -> NegotiateSpec
    member Run: spec: NegotiateSpec -> HandlerDefinition list

    // Every `Accepts` overload's signature is otherwise unchanged from today --
    // only NegotiateSpec's field and Run's return type change.
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: RequestDelegate -> NegotiateSpec
    // ... remaining Accepts overloads, unchanged signatures ...

[<AutoOpen>]
module NegotiateFunctions =
    val negotiate: NegotiateBuilder
```

- [ ] **Step 4: Modify `NegotiateBuilder.fs`**

Delete `Negotiation.dispatch` entirely (dead — `FrankProducesMatcherPolicy` does this now). Delete `Negotiation.rejectWildcardAutoFormat`'s home only if it's the sole remaining member of the `Negotiation` module after `isWildcard`/`matches`/`specificity`/`effectiveQuality`/`selectRepresentation` move to `MediaTypeNegotiation` — keep `rejectWildcardAutoFormat` and `mergeProducesMetadata` here, they're CE-specific, not raw matching logic. Update every reference from `Negotiation.X` to `MediaTypeNegotiation.X` for the functions that moved.

```fsharp
namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata

type NegotiateSpec =
    { Representations: (string * RequestDelegate * obj list) list }

    static member Empty = { Representations = [] }

module internal Negotiation =

    // rejectWildcardAutoFormat: UNCHANGED, moved verbatim from today's file.

    // mergeProducesMetadata: UNCHANGED BODY, moved verbatim -- still takes an
    // obj list and returns an obj list. What changes is WHO calls it and how
    // many times: today Run calls it once against NegotiateSpec.Metadata; now
    // Run calls it once against the concatenation of every representation's
    // own metadata, then broadcasts the single result to every entry.

[<Sealed>]
type NegotiateBuilder() =

    member _.Yield(_) = NegotiateSpec.Empty

    member _.Run(spec: NegotiateSpec) : HandlerDefinition list =
        if List.isEmpty spec.Representations then
            failwith "At least one representation must be registered using the 'accepts' operation"

        let allOwnMetadata =
            spec.Representations |> List.collect (fun (_, _, m) -> m)

        let mergedMetadata = Negotiation.mergeProducesMetadata allOwnMetadata

        spec.Representations
        |> List.mapi (fun ordinal (mediaType, handler, _) ->
            { Handler = handler
              Metadata = (ProducesMediaTypeMetadata(mediaType, ordinal) :> obj) :: mergedMetadata })

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: RequestDelegate) =
        { spec with Representations = spec.Representations @ [ mediaType, handler, [] ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> unit) =
        let producer =
            RequestDelegate(fun ctx ->
                handler ctx
                Task.CompletedTask)

        { spec with Representations = spec.Representations @ [ mediaType, producer, [] ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handlerDef: HandlerDefinition) =
        { spec with
            Representations = spec.Representations @ [ mediaType, handlerDef.Handler, handlerDef.Metadata ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Task<unit>) =
        let producer = RequestDelegate(fun ctx -> handler ctx :> Task)
        { spec with Representations = spec.Representations @ [ mediaType, producer, [] ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Task<'a>) =
        Negotiation.rejectWildcardAutoFormat mediaType

        let producer =
            RequestDelegate(fun ctx ->
                task {
                    let! value = handler ctx
                    return! Frank.ContentNegotiation.viaOutputFormatter mediaType value ctx
                })

        { spec with Representations = spec.Representations @ [ mediaType, producer, [] ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(spec: NegotiateSpec, mediaType: string, handler: HttpContext -> Async<'a>) =
        Negotiation.rejectWildcardAutoFormat mediaType

        let producer =
            RequestDelegate(fun ctx ->
                task {
                    let! value = Async.StartAsTask(handler ctx)
                    return! Frank.ContentNegotiation.viaOutputFormatter mediaType value ctx
                })

        { spec with Representations = spec.Representations @ [ mediaType, producer, [] ] }

    [<CustomOperation("accepts")>]
    member this.Accepts(spec: NegotiateSpec, mediaTypes: string list, handler: HttpContext -> Task<'a>) =
        mediaTypes |> List.fold (fun s mt -> this.Accepts(s, mt, handler)) spec

    [<CustomOperation("accepts")>]
    member this.Accepts(spec: NegotiateSpec, mediaTypes: string list, handler: HttpContext -> Async<'a>) =
        mediaTypes |> List.fold (fun s mt -> this.Accepts(s, mt, handler)) spec

[<AutoOpen>]
module NegotiateFunctions =
    let negotiate = NegotiateBuilder()
```

- [ ] **Step 5: Run tests**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~NegotiateBuilderTests"`
Expected: PASS, every scenario from the original 26-case file.

- [ ] **Step 6: Build all three target frameworks**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: 0 errors across `net8.0;net9.0;net10.0`.

- [ ] **Step 7: Commit**

```bash
git add src/Frank/NegotiateBuilder.fsi src/Frank/NegotiateBuilder.fs test/Frank.Tests/NegotiateBuilderTests.fs
git commit -m "feat(frank): NegotiateBuilder.Run returns HandlerDefinition list, dispatch moves to routing layer"
```

---

## Task 5: Auto-register `FrankProducesMatcherPolicy` in `webHost { }`

**Files:**
- Modify: `src/Frank/WebHostBuilder.fs` (no `.fsi` change — `WebHostSpec.Empty`'s value doesn't change its type)
- Modify: `test/Frank.Tests/NegotiateBuilderTests.fs` (remove the now-redundant explicit `services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>()` from `buildHost`'s `configureServices`, since production code no longer requires the caller to do this — but the test's own `buildHost` helper builds a bare `TestServer` pipeline, not `WebHostBuilder.Run`, so it still needs the registration explicitly; this step instead adds ONE new test proving `webHost { }` registers it automatically)
- Create test coverage: extend `test/Frank.Tests/NegotiateBuilderTests.fs` with one case built through `webHost { }`+`resource { }` directly, not the bare `buildHost` test helper

**Interfaces:**
- Consumes: `Frank.Builder.FrankProducesMatcherPolicy` (Task 2).
- Produces: nothing new — `WebHostSpec.Empty.Services` behavior changes, no type changes.

- [ ] **Step 1: Write the failing test**

Add to `test/Frank.Tests/NegotiateBuilderTests.fs`:

```fsharp
testCaseTask "webHost { } auto-registers FrankProducesMatcherPolicy -- negotiate { } works with zero explicit DI setup"
<| task {
    let resourceSpec =
        (resource "/x") {
            get (
                negotiate {
                    accepts "application/json" (writeText "json")
                    accepts "text/html" (writeText "html")
                }
            )
        }

    // Build directly off WebHostSpec.Empty (production defaults), substituting
    // only UseTestServer() for the real listener -- same pattern as
    // AlpsDocumentIntegrationTests.buildHost, but starting from the actual
    // WebHostSpec.Empty.Services this task modifies, not a hand-rolled one.
    let spec =
        { WebHostSpec.Empty with
            Endpoints = resourceSpec.Endpoints }

    use host =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        spec.Services services |> ignore)
                    .Configure(fun app ->
                        app.UseRouting() |> ignore
                        app.UseEndpoints(fun endpoints ->
                            endpoints.DataSources.Add(TestEndpointDataSource spec.Endpoints))
                        |> ignore)
                |> ignore)
            .Build()

    do! host.StartAsync()
    use client = host.GetTestClient()
    use request = new HttpRequestMessage(HttpMethod.Get, "/x")
    request.Headers.Accept.ParseAdd("text/html")
    let! response = client.SendAsync(request)
    let! body = response.Content.ReadAsStringAsync()
    Expect.equal body "html" "Negotiation worked without the test explicitly registering FrankProducesMatcherPolicy"
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~NegotiateBuilderTests"`
Expected: This new case FAILs (500 `AmbiguousMatchException`, not 200) — `FrankProducesMatcherPolicy` isn't registered by `WebHostSpec.Empty.Services` yet.

- [ ] **Step 3: Modify `WebHostBuilder.fs`**

```fsharp
open Microsoft.AspNetCore.Routing.Matching

type WebHostSpec =
    { // ... unchanged fields ...
    }

    static member Empty =
        { Host = id
          BeforeRoutingMiddleware = id
          Middleware = id
          Endpoints = [||]
          Services =
            (fun services ->
                services.AddMvcCore(fun options -> options.ReturnHttpNotAcceptable <- true)
                |> ignore

                services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore

                services)
          LinkProviders = []
          UseDefaults = false }
```

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~NegotiateBuilderTests"`
Expected: PASS, including the new case.

- [ ] **Step 5: Build all three target frameworks**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/Frank/WebHostBuilder.fs test/Frank.Tests/NegotiateBuilderTests.fs
git commit -m "feat(frank): webHost {} auto-registers FrankProducesMatcherPolicy"
```

---

## Task 6: Verify samples still compile unchanged; OpenAPI broadcast-merge regression test

**Files:**
- Verify only (no source edits expected): `sample/Frank.Alps.Sample/Program.fs`, `sample/Frank.Rdf.Sample/Program.fs`, `sample/Frank.OpenApi.Sample/Handlers.fs`, `sample/Frank.OpenApi.Sample/Program.fs`
- Modify: `test/Frank.OpenApi.Tests/NegotiateMetadataTests.fs`

**Interfaces:**
- Consumes: everything from Tasks 1–5.
- Produces: nothing new — this task is verification plus one new regression test proving the OpenAPI mitigation from the brainstorming session actually works end-to-end.

- [ ] **Step 1: Build every sample, confirm zero source changes needed**

Run: `dotnet build sample/Frank.Alps.Sample/Frank.Alps.Sample.fsproj`
Run: `dotnet build sample/Frank.Rdf.Sample/Frank.Rdf.Sample.fsproj`
Run: `dotnet build sample/Frank.OpenApi.Sample/Frank.OpenApi.Sample.fsproj`
Expected: 0 errors, 0 source edits. If any sample fails to compile, that means a `negotiate { }` call site relies on something this plan assumed was source-compatible but isn't — stop and reconcile before continuing; do not silently patch the sample to work around a hole in the plan.

- [ ] **Step 2: Write the failing OpenAPI regression test**

Read the existing `test/Frank.OpenApi.Tests/NegotiateMetadataTests.fs` first to match its existing harness pattern exactly (it already builds a real `Microsoft.AspNetCore.OpenApi` document against a `negotiate { }`-based resource, per the design doc's Goal 5 coverage). Add:

```fsharp
testCaseTask "N separate RouteEndpoints from negotiate { } still produce ONE OpenAPI operation listing every content type -- mitigates dotnet/aspnetcore#58329"
<| task {
    let resourceSpec =
        (resource "/products/{id}") {
            get (
                negotiate {
                    accepts "application/json" (handler {
                        produces typeof<Product> 200 [ "application/json" ]
                        handle someJsonHandler
                    })
                    accepts "text/html" (handler {
                        produces typeof<Product> 200 [ "text/html" ]
                        handle someHtmlHandler
                    })
                }
            )
        }

    // ... build the OpenAPI document the same way the existing tests in this
    // file do (reuse that harness, don't hand-roll a new one) ...

    let operation = document.Paths.["/products/{id}"].Operations.[HttpMethod.Get]
    let contentTypes = operation.Responses.["200"].Content.Keys |> Set.ofSeq

    Expect.equal contentTypes (Set.ofList [ "application/json"; "text/html" ]) "Both content types must appear despite two separate RouteEndpoints at the same path+verb -- proves the broadcast-merge in NegotiateBuilder.Run works around the framework's last-write-wins collapse"
}
```

- [ ] **Step 3: Run to verify current behavior, then confirm it already passes**

Run: `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj --filter "FullyQualifiedName~NegotiateMetadataTests"`
Expected: PASS — Task 4's broadcast-merge should already make this correct; this step exists to prove it, not to drive new implementation. If it FAILS, the broadcast-merge in Task 4 has a bug — fix `NegotiateBuilder.Run` there, don't patch around it here.

- [ ] **Step 4: Commit**

```bash
git add test/Frank.OpenApi.Tests/NegotiateMetadataTests.fs
git commit -m "test(frank.openapi): prove negotiate {} sidesteps dotnet/aspnetcore#58329's operation-collapse bug"
```

---

## Task 7: `benchmarks/Frank.Benchmarks` — BenchmarkDotNet harness and scenarios

**Files:**
- Create: `benchmarks/Frank.Benchmarks/Frank.Benchmarks.fsproj`
- Create: `benchmarks/Frank.Benchmarks/Program.fs`
- Create: `benchmarks/Frank.Benchmarks/NegotiationBenchmarks.fs`
- Modify: `Frank.sln` (add the new project)

**Interfaces:**
- Consumes: `Frank.Builder.MediaTypeNegotiation.selectRepresentation` (Task 1, for the baseline), `Frank.Builder.FrankProducesMatcherPolicy` + `resource`/`negotiate`/`webHost` (all prior tasks, for the routed comparison).
- Produces: nothing consumed elsewhere — this is a standalone measurement project, not part of `Frank.fsproj`'s dependency graph.

This is a measurement task, not a TDD task — there's no "failing test" step. Each step below is still independently runnable and verifiable.

- [ ] **Step 1: Create the project file**

`benchmarks/Frank.Benchmarks/Frank.Benchmarks.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="NegotiationBenchmarks.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.*" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0-preview.1.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank/Frank.fsproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write `NegotiationBenchmarks.fs`**

Baseline reuses `MediaTypeNegotiation.selectRepresentation` directly (the exact function `Negotiation.dispatch` used to call before deletion in Task 4 — so the baseline measures precisely "what the old code path did," without resurrecting the deleted `dispatch`/single-`HandlerDefinition` production code). The routed variant goes through a real `TestServer` with `FrankProducesMatcherPolicy` registered:

```fsharp
namespace Frank.Benchmarks

open System.Net.Http
open BenchmarkDotNet.Attributes
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Matching
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Frank.Builder

type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = Microsoft.Extensions.Primitives.NullChangeToken.Singleton :> _

/// One BenchmarkDotNet class per scenario shape (single / N=3-first / N=3-last /
/// wildcard / 406 / default), each with [<Params>] selecting Baseline vs Routed,
/// so BenchmarkDotNet's own summary table does the side-by-side comparison.
[<MemoryDiagnoser>]
type SingleRepresentationBenchmarks() =

    let mediaTypes = [ "application/json" ]
    let acceptValues = [ "application/json" ]

    let mutable host: IHost = Unchecked.defaultof<_>
    let mutable client: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        let handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("json"))
        let pattern = Patterns.RoutePatternFactory.Parse "/x"
        let builder = RouteEndpointBuilder(handler, pattern, 0)
        builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
        builder.Metadata.Add(ProducesMediaTypeMetadata("application/json", 0))
        let endpoint = builder.Build()

        host <-
            Host
                .CreateDefaultBuilder([||])
                .ConfigureWebHost(fun wb ->
                    wb
                        .UseTestServer()
                        .ConfigureServices(fun services ->
                            services.AddRouting() |> ignore
                            services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore)
                        .Configure(fun app ->
                            app.UseRouting() |> ignore
                            app.UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource [| endpoint |]))
                            |> ignore)
                    |> ignore)
                .Build()

        host.StartAsync().GetAwaiter().GetResult()
        client <- host.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        client.Dispose()
        host.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() =
        MediaTypeNegotiation.selectRepresentation acceptValues mediaTypes

    [<Benchmark>]
    member _.RoutingLayerDispatch() =
        client.GetAsync("/x").GetAwaiter().GetResult()

/// Exact parameters for the five remaining benchmark classes -- each has IDENTICAL
/// structure to `SingleRepresentationBenchmarks` above (same [<GlobalSetup>]/
/// [<GlobalCleanup>]/[<Benchmark(Baseline = true)>] DirectFunctionDispatch/
/// [<Benchmark>] RoutingLayerDispatch shape, same TestEndpointDataSource/host/client
/// wiring), differing ONLY in the `mediaTypes`/`acceptValues` values and how many
/// endpoints `Setup` registers. Write all five as separate types in this file,
/// copy-pasting `SingleRepresentationBenchmarks`'s full body for each and changing
/// only the two `let` bindings and the `Setup` endpoint-registration block per the
/// table below -- do not abbreviate any of the five, BenchmarkDotNet needs one
/// complete type per scenario to produce a comparable summary row.
///
/// | Type name | mediaTypes registered (in order) | acceptValues sent | Setup registers |
/// |---|---|---|---|
/// | `ThreeRepresentationsAcceptFirstBenchmarks` | `["application/json"; "text/html"; "application/xml"]` | `["application/json"]` | 3 endpoints, ordinals 0/1/2, bodies "json"/"html"/"xml" |
/// | `ThreeRepresentationsAcceptLastBenchmarks` | `["application/json"; "text/html"; "application/xml"]` | `["application/xml"]` | same 3 endpoints as above |
/// | `WildcardFallbackBenchmarks` | `["application/json"; "*/*"]` | `["image/png"]` | 2 endpoints, ordinals 0/1, bodies "json"/"fallback" |
/// | `NotAcceptableBenchmarks` | `["application/json"]` | `["application/xml"]` | 1 endpoint, ordinal 0, body "json" -- baseline measures `selectRepresentation` returning `None`; routed measures the full 406 round-trip through `client.GetAsync` |
/// | `DefaultRepresentationBenchmarks` | `["application/json"; "text/html"]` | `[]` (no `Accept` header set on either the baseline call or the `HttpRequestMessage`) | 2 endpoints, ordinals 0/1, bodies "json"/"html" |
```

- [ ] **Step 3: Write `Program.fs`**

```fsharp
module Frank.Benchmarks.Program

open BenchmarkDotNet.Running

[<EntryPoint>]
let main argv =
    BenchmarkSwitcher.FromAssembly(typeof<SingleRepresentationBenchmarks>.Assembly).Run(argv) |> ignore
    0
```

- [ ] **Step 4: Add the project to `Frank.sln`**

Run: `dotnet sln Frank.sln add benchmarks/Frank.Benchmarks/Frank.Benchmarks.fsproj`

- [ ] **Step 5: Build and do a short smoke run**

Run: `dotnet build benchmarks/Frank.Benchmarks/Frank.Benchmarks.fsproj`
Expected: 0 errors.

Run: `dotnet run -c Release --project benchmarks/Frank.Benchmarks -- --filter "*SingleRepresentationBenchmarks*" --job short`
Expected: Completes, prints a summary table with both `DirectFunctionDispatch` and `RoutingLayerDispatch` rows. This is a smoke test of the harness itself, not a performance verdict — full runs (all six scenario classes, default job) are a separate, manual step outside this plan's automated task-by-task flow, since BenchmarkDotNet's real timing runs take much longer than a TDD step should.

- [ ] **Step 6: Commit**

```bash
git add benchmarks/Frank.Benchmarks Frank.sln
git commit -m "feat(benchmarks): add Frank.Benchmarks comparing direct dispatch vs FrankProducesMatcherPolicy"
```

---

## Task 8: Full-suite verification

**Files:** none created or modified — this task only runs commands.

- [ ] **Step 1: Full build, all three TFMs**

Run: `dotnet build src/Frank/Frank.fsproj`
Expected: 0 errors, 0 new warnings across `net8.0;net9.0;net10.0`.

- [ ] **Step 2: Full `Frank.Tests` suite**

Run: `dotnet test test/Frank.Tests/Frank.Tests.fsproj`
Expected: PASS, including every pre-existing test file untouched by this plan (`WebLinkTests.fs`, `ResponseLinkTests.fs`, `HandlerBuilderTests.fs`, `ResourceBuilderMetadataTests.fs`, `MiddlewareOrderingTests.fs`, `MetadataTests.fs`, `ContentNegotiationTests.fs`).

- [ ] **Step 3: Full `Frank.OpenApi.Tests` suite**

Run: `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`
Expected: PASS.

- [ ] **Step 4: Full `Frank.Alps.Tests` and `Frank.Rdf.Tests` suites**

Run: `dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
Run: `dotnet test test/Frank.Rdf.Tests/Frank.Rdf.Tests.fsproj`
Expected: PASS — these exercise `negotiate { }` through the real samples' resource definitions, the strongest end-to-end proof the refactor is behavior-preserving.

- [ ] **Step 5: Solution-wide build**

Run: `dotnet build Frank.sln`
Expected: 0 errors.

- [ ] **Step 6: Run `Frank.Analyzers.Tests`, confirm FRANK001/FRANK002 still pass unchanged**

Run: `dotnet test test/Frank.Analyzers.Tests/Frank.Analyzers.Tests.fsproj`
Expected: PASS — per the brainstorming session's verified finding, `DuplicateHandlerAnalyzer` is a source-AST walker unaffected by this refactor; this step confirms that holds, it isn't expected to require any analyzer changes.

- [ ] **Step 7: Commit any remaining cleanup**

If every prior task's commit already left the tree clean, this step is a no-op — confirm with `git status` before committing anything.
