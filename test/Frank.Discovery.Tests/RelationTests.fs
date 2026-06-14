module Frank.Discovery.Tests.RelationTests

open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
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
            HomeRoute = "/" }

    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    builder.Services.AddRouting() |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<DiscoveryMiddleware.DiscoveryMiddleware>() |> ignore

    for ep in gameResource.Endpoints do
        let re = ep :?> RouteEndpoint
        let methods = ep.Metadata.GetMetadata<HttpMethodMetadata>().HttpMethods

        app
            .MapMethods(re.RoutePattern.RawText, methods, System.Func<string>(fun () -> ""))
            .WithMetadata({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
        |> ignore

    for ep in lobbyResource.Endpoints do
        let re = ep :?> RouteEndpoint
        let methods = ep.Metadata.GetMetadata<HttpMethodMetadata>().HttpMethods

        app
            .MapMethods(re.RoutePattern.RawText, methods, System.Func<string>(fun () -> ""))
            .WithMetadata({ Relation = "https://schema.org/WebPage" }: ResourceRelationMetadata)
        |> ignore

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
