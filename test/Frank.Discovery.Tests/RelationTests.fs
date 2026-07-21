module Frank.Discovery.Tests.RelationTests

open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Discovery.Tests.TestHelpers

/// Build a resource using the `relation` CE op and inspect the built endpoint.
let private buildGameEndpoint () =
    let res =
        resource "/games/{id}" {
            relation "https://schema.org/Game"
            get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("game")))
        }

    res.Endpoints.[0]

/// Retrieve ResourceRelationMetadata via box/unbox to satisfy F# null constraint.
let private tryGetRelationMeta (ep: Microsoft.AspNetCore.Http.Endpoint) =
    let boxed = ep.Metadata.GetMetadata<ResourceRelationMetadata>() |> box

    if boxed = null then
        None
    else
        Some(boxed |> unbox<ResourceRelationMetadata>)

/// Spin a minimal TestServer seeded with two Frank resources, each carrying `relation`.
/// Reuses TestHelpers.buildDiscoveryApp's ResourceEndpointDataSource wiring (#411 — the
/// SAME concrete type DiscoveryMiddleware's production constructor receives via
/// WebHostBuilder.Run) — the real, Frank-built Endpoint[] (already carrying
/// ResourceRelationMetadata via the `relation` CE op) is wrapped directly, not re-declared
/// via a second, redundant MapMethods/.WithMetadata pass.
let private startRelationServer () =
    let gameResource =
        resource "/games/{id}" {
            relation "https://schema.org/Game"
            get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("game")))
        }

    let lobbyResource =
        resource "/" {
            relation "https://schema.org/WebPage"
            get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("lobby")))
        }

    let config =
        { DiscoveryConfig.Empty with
            ProfileUri = "/alps/test"
            HomeRoute = "/"
            ResourceHrefVars =
                Map.ofList [ "https://schema.org/Game", Map.ofList [ "id", "https://schema.org/identifier" ] ] }

    let endpoints: Endpoint[] =
        Array.append gameResource.Endpoints lobbyResource.Endpoints

    let app = buildDiscoveryApp None config endpoints
    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let relationOpTests =
    testList
        "relation CE op stamps ResourceRelationMetadata"
        [ testCase "endpoint carries ResourceRelationMetadata with correct IRI"
          <| fun _ ->
              let ep = buildGameEndpoint ()
              let meta = tryGetRelationMeta ep
              Expect.isSome meta "ResourceRelationMetadata present"
              Expect.equal meta.Value.Relation "https://schema.org/Game" "IRI matches"

          testCase "relation IRI survives Build() round-trip"
          <| fun _ ->
              let ep = buildGameEndpoint ()
              let meta = tryGetRelationMeta ep
              Expect.isSome meta "ResourceRelationMetadata present"
              Expect.stringStarts meta.Value.Relation "http" "IRI is absolute" ]

[<Tests>]
let runtimeJsonHomeTests =
    testList
        "runtime JSON Home from endpoint relation metadata"
        [ testCase "GET / with json-home Accept → 200 with non-empty resources keyed by IRI"
          <| fun _ ->
              use app = startRelationServer ()
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 OK"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse body
              let resources = doc.RootElement.GetProperty("resources")
              Expect.isTrue (resources.EnumerateObject() |> Seq.length > 0) "resources non-empty"

          testCase "resource keys start with http (absolute vocabulary IRIs)"
          <| fun _ ->
              use app = startRelationServer ()
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse body
              let resources = doc.RootElement.GetProperty("resources")
              let keys = resources.EnumerateObject() |> Seq.map (fun p -> p.Name) |> Seq.toList
              Expect.isTrue (keys |> List.forall (fun k -> k.StartsWith "http")) "all keys are absolute IRIs"

          testCase "body contains no urn:frank:"
          <| fun _ ->
              use app = startRelationServer ()
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.isFalse (body.Contains "urn:frank:") "no urn:frank: in JSON Home"

          testCase "both registered relation IRIs appear as resource keys"
          <| fun _ ->
              use app = startRelationServer ()
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "https://schema.org/Game" "Game IRI present"
              Expect.stringContains body "https://schema.org/WebPage" "WebPage IRI present" ]

// ── #415/#wave1c: JSON Home resource key relativized for declared-only prefixes, then
// resolved against the live request origin ─────────────────────────────────────────
// ResourceRelationMetadata.Relation stays the full, un-relativized class IRI (the
// correlation key matched against AlpsDescriptor.ClassIri — #397/#398/#411's
// invariant, never itself relativized). But when SERVED as a JSON Home resource key
// (JsonHomeSerializer.WritePropertyName), a declared-only/owned prefix's identity must
// not leak a placeholder domain nobody serves (#415 thesis) — the SAME host-relative
// href DiscoveryEmitter already computed for that class's own AlpsDescriptor.Href is
// used instead, mirroring how `href`/`href-template` are already served host-relative.
// That host-relative form is itself only an intermediate value, though: RFC 8288 §2.1
// requires the served resources-object key to be a genuine link-relation-type IRI, so
// DiscoveryMiddleware resolves it (and every HrefVars meaning IRI) against the live
// TestServer request origin before writing the wire body — mirroring handleAlpsProfile's
// existing per-request Href/Rt resolution (#398).

let private declaredOnlyConfig: DiscoveryConfig =
    { ProfileUri = "/alps/test"
      HomeRoute = "/"
      AlpsDescriptors =
        [ { Id = "Game"
            Type = "semantic"
            Doc = None
            Href = Some "/ex#Game"
            Descriptors = []
            Rt = None
            ClassIri = Some "https://tictactoe.invalid/ex#Game"
            RequestClrTypeName = None } ]
      DescribedByLinks = []
      ResourceHrefVars = Map.ofList [ "https://tictactoe.invalid/ex#Game", Map.ofList [ "id", "/ex#identifier" ] ] }

let private startDeclaredOnlyRelationServer () =
    let gameResource =
        resource "/games/{id}" {
            relation "https://tictactoe.invalid/ex#Game"
            get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("game")))
        }

    let app = buildDiscoveryApp None declaredOnlyConfig gameResource.Endpoints
    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let declaredOnlyJsonHomeKeyTests =
    testList
        "runtime JSON Home — #415 declared-only relation resolved to its own AlpsDescriptor.Href"
        [ testCase
              "resource key is resolved absolute against the live origin, not the host-relative href nor the un-relativized placeholder domain"
          <| fun _ ->
              use app = startDeclaredOnlyRelationServer ()
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 OK"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse body
              let resources = doc.RootElement.GetProperty("resources")
              let keys = resources.EnumerateObject() |> Seq.map (fun p -> p.Name) |> Seq.toList

              Expect.contains
                  keys
                  "http://localhost/ex#Game"
                  "resource key is the host-relative href resolved against the live TestServer request origin"

              Expect.isFalse (keys |> List.contains "/ex#Game") "the un-resolved, still-relative form never appears"

              Expect.isFalse
                  (keys |> List.exists (fun k -> k.Contains "tictactoe.invalid"))
                  "the un-relativized placeholder-domain identity key never appears as a served resource key"

          testCase "HrefVars meaning IRIs are resolved against the live origin, not served relative"
          <| fun _ ->
              use app = startDeclaredOnlyRelationServer ()
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse body

              let resource =
                  doc.RootElement.GetProperty("resources").GetProperty("http://localhost/ex#Game")

              let hrefVars = resource.GetProperty("href-vars")

              Expect.equal
                  (hrefVars.GetProperty("id").GetString())
                  "http://localhost/ex#identifier"
                  "href-vars 'id' meaning IRI is resolved against the live TestServer request origin"

              Expect.isFalse
                  (body.Contains "\"/ex#identifier\"")
                  "the un-resolved, still-relative meaning IRI never appears" ]

// ── 2nd expert finding: classIriHrefMap fallback (no matching ALPS descriptor) ─────
// homeResourcesFromEndpoints's servedRelation falls back to the RAW relation IRI when no
// top-level ALPS descriptor's ClassIri matches it (line ~116). By the correlation-key
// contract (#397/#398/#411), ResourceRelationMetadata.Relation is always the class's full,
// un-relativized absolute IRI — so in the documented/expected case, this fallback's raw
// value is already absolute, and resolveJsonHomeResourceAgainst's resolveHrefAgainst is a
// no-op on it (RFC 3986 §5.3), so it stays absolute unchanged: safe by construction.
// The `relation` CE op only validates non-empty (Frank.Discovery.fs), not absoluteness —
// an app author could call it with a relative string directly, bypassing the documented
// invariant. Because the resolution fix above applies resolveJsonHomeResourceAgainst to
// EVERY served resource regardless of which classIriHrefMap branch produced it, that
// hypothetical relative relation is resolved against the live origin too — closing the gap
// even for input that violates the documented contract, not just the expected case.

let private fallbackRelationConfig: DiscoveryConfig =
    { ProfileUri = "/alps/test"
      HomeRoute = "/"
      // No descriptor's ClassIri matches either registered relation below — both fall
      // through classIriHrefMap's Map.tryFind to the RAW relation.
      AlpsDescriptors = []
      DescribedByLinks = []
      ResourceHrefVars = Map.empty }

let private startFallbackRelationServer () =
    // Fixed (non-templated) routes deliberately — this fixture investigates Relation
    // resolution specifically, and a templated href would additionally require every
    // template variable to have a derived meaning IRI (JsonHomeSerializer.writeHrefVar),
    // an orthogonal concern already covered by the href-vars-resolution test above.
    let absoluteRelationResource =
        resource "/widgets" {
            relation "https://tictactoe.invalid/ex#Widget"
            get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("widget")))
        }

    // Deliberately violates the documented "always absolute" contract — the `relation` CE
    // op only validates non-empty, not absoluteness (Frank.Discovery.fs `Relation` member).
    let relativeRelationResource =
        resource "/gadgets" {
            relation "/ex#Gadget"
            get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("gadget")))
        }

    let endpoints: Endpoint[] =
        Array.append absoluteRelationResource.Endpoints relativeRelationResource.Endpoints

    let app = buildDiscoveryApp None fallbackRelationConfig endpoints
    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let fallbackRelationTests =
    testList
        "runtime JSON Home — classIriHrefMap fallback (no matching ALPS descriptor, 2nd expert finding)"
        [ testCase "an absolute relation with no matching descriptor stays absolute unchanged (safe by construction)"
          <| fun _ ->
              use app = startFallbackRelationServer ()
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse body

              let keys =
                  doc.RootElement.GetProperty("resources").EnumerateObject()
                  |> Seq.map (fun p -> p.Name)
                  |> Seq.toList

              Expect.contains
                  keys
                  "https://tictactoe.invalid/ex#Widget"
                  "already-absolute fallback relation is unchanged, still absolute"

          testCase
              "a relative relation with no matching descriptor is still resolved against the live origin, never leaked relative"
          <| fun _ ->
              use app = startFallbackRelationServer ()
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse body

              let keys =
                  doc.RootElement.GetProperty("resources").EnumerateObject()
                  |> Seq.map (fun p -> p.Name)
                  |> Seq.toList

              Expect.contains
                  keys
                  "http://localhost/ex#Gadget"
                  "even a relative fallback relation (bypassing the documented always-absolute contract) is resolved against origin"

              Expect.isFalse
                  (keys |> List.contains "/ex#Gadget")
                  "the un-resolved, still-relative fallback relation never appears" ]

[<Tests>]
let jsonHomeFromSampleConfigTests =
    testList
        "JSON Home uses sampleConfig (existing test compat)"
        [ testCase "GET / with json-home Accept serves schema.org/Game from endpoint metadata"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "resources" "resources key"
              Expect.stringContains body "https://schema.org/Game" "Game IRI from endpoint metadata" ]
