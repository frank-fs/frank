module Frank.LinkedData.Tests.EndpointMetadataTests

open System
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open VDS.RDF
open Expecto
open Frank.LinkedData
open Frank.LinkedData.Tests.TestHelpers

/// Build a second fixture graph with a distinct triple (owl:Class) so test assertions
/// can distinguish endpoint graph from global graph unambiguously.
let private buildEndpointGraph () : IGraph =
    let g = new Graph()

    let subject =
        g.CreateUriNode(Uri "https://example.org/vocab/TttSquare")

    let rdfType =
        g.CreateUriNode(Uri "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

    let owlClass =
        g.CreateUriNode(Uri "http://www.w3.org/2002/07/owl#Class")

    g.Assert(Triple(subject, rdfType, owlClass)) |> ignore
    g :> IGraph

let private endpointContext =
    """{"@context":{"owl":"http://www.w3.org/2002/07/owl#"}}"""

/// Start a TestServer that mirrors the production middleware ordering:
///   UseRouting → LinkedDataMiddleware → endpoint pipeline
/// /vocab carries LinkedDataConfig metadata; /data does not.
let private startServerWithMetadata (globalConfig: LinkedDataConfig) (endpointConfig: LinkedDataConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(globalConfig) |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<LinkedDataMiddleware>() |> ignore

    app
        .MapGet("/vocab", Func<string>(fun () -> "vocab-fallback"))
        .WithMetadata(endpointConfig)
    |> ignore

    app.MapGet("/data", Func<string>(fun () -> "downstream")) |> ignore
    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let endpointMetadataTests =
    let endpointGraph = buildEndpointGraph ()
    let endpointConfig = { Graph = endpointGraph; JsonLdContext = endpointContext }

    testList
        "LinkedDataMiddleware endpoint-metadata override"
        [ testCase "GET /vocab Accept:text/turtle → serves endpoint graph, not global graph"
          <| fun _ ->
              use app = startServerWithMetadata sampleConfig endpointConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/vocab")
              req.Headers.Add("Accept", "text/turtle")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 OK"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "owl" "endpoint graph owl:Class triple present"
              Expect.isFalse (body.Contains("schema.org/Game")) "global graph NOT served when endpoint config present"

          testCase "GET /data Accept:text/turtle → falls back to global config graph"
          <| fun _ ->
              use app = startServerWithMetadata sampleConfig endpointConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "text/turtle")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 OK"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "schema.org" "global config graph (seeAlso triple) served as fallback"
              Expect.isFalse (body.Contains("owl#Class")) "endpoint graph NOT served when no endpoint config"

          testCase "GET /vocab Accept:application/ld+json → 200 ld+json, endpoint graph in body"
          <| fun _ ->
              use app = startServerWithMetadata sampleConfig endpointConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/vocab")
              req.Headers.Add("Accept", "application/ld+json")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 OK"
              Expect.equal resp.Content.Headers.ContentType.MediaType "application/ld+json" "ld+json content-type"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "@context" "@context key present"
              Expect.stringContains body "owl" "endpoint graph owl context present" ]
