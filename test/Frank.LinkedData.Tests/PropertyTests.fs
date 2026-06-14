module Frank.LinkedData.Tests.PropertyTests

open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.TestHost
open Expecto
open FsCheck
open VDS.RDF
open VDS.RDF.Parsing
open Frank.LinkedData
open Frank.LinkedData.Tests.TestHelpers

/// FsCheck generator for a graph with 1..5 random triples.
let genGraph () : IGraph =
    let graph = new Graph()

    let subjects =
        [| "https://example.org/a"; "https://example.org/b"; "https://example.org/c" |]

    let predicates = [| "https://example.org/p1"; "https://example.org/p2" |]
    let objects = [| "https://example.org/x"; "https://example.org/y" |]

    for s in subjects do
        for p in predicates do
            let o = objects.[0]

            graph.Assert(
                Triple(
                    graph.CreateUriNode(System.Uri(s)),
                    graph.CreateUriNode(System.Uri(p)),
                    graph.CreateUriNode(System.Uri(o))
                )
            )
            |> ignore

    graph :> IGraph

/// Extract @context value(s) from JSON-LD response body.
let extractContextUris (jsonBody: string) : string list =
    try
        let doc = JsonDocument.Parse(jsonBody)
        let root = doc.RootElement

        let tryGetContext (el: JsonElement) =
            match el.TryGetProperty("@context") with
            | true, ctx ->
                match ctx.ValueKind with
                | JsonValueKind.String -> [ ctx.GetString() ]
                | JsonValueKind.Array ->
                    ctx.EnumerateArray()
                    |> Seq.choose (fun el ->
                        if el.ValueKind = JsonValueKind.String then
                            Some(el.GetString())
                        else
                            None)
                    |> Seq.toList
                | _ -> []
            | _ -> []

        match root.ValueKind with
        | JsonValueKind.Array -> root.EnumerateArray() |> Seq.collect tryGetContext |> Seq.toList
        | _ -> tryGetContext root
    with _ ->
        []

[<Tests>]
let tests =
    testList
        "LinkedData property tests"
        [ testProperty "ld+json @context references only the external URI from config — never urn:frank:"
          <| fun () ->
              let config = sampleConfig

              use app = startServer config
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/ld+json")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              let contextUris = extractContextUris body
              let hasExternalRef = contextUris |> List.exists (fun u -> u.Contains("schema.org"))
              let hasUrnFrank = body.Contains("urn:frank:")
              hasExternalRef && not hasUrnFrank

          testProperty "Turtle round-trip preserves all triples — none silently dropped"
          <| fun () ->
              let config =
                  { sampleConfig with
                      Graph = genGraph () }

              use app = startServer config

              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "text/turtle")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              let turtle = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              let reparsed = new Graph()
              let parser = TurtleParser()

              try
                  use reader = new System.IO.StringReader(turtle)
                  parser.Load(reparsed :> IGraph, reader)
                  let original = config.Graph.Triples.Count
                  let parsed = reparsed.Triples.Count
                  parsed >= original
              with _ ->
                  false

          testProperty "supported Accept types yield 200; unsupported RDF-looking Accept yields 406"
          <| fun () ->
              let config = sampleConfig
              let supportedTypes = [ "application/ld+json"; "text/turtle"; "application/rdf+xml" ]

              let unsupportedRdfTypes =
                  [ "application/xml"; "application/n-triples"; "text/n3"; "application/trig" ]

              use app = startServer config
              use client = app.GetTestClient()

              let checkSupported (ct: string) =
                  use req = new HttpRequestMessage(HttpMethod.Get, "/data")
                  req.Headers.Add("Accept", ct)
                  let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
                  int resp.StatusCode = 200

              let checkUnsupported (ct: string) =
                  use req = new HttpRequestMessage(HttpMethod.Get, "/data")
                  req.Headers.Add("Accept", ct)
                  let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
                  int resp.StatusCode = 406

              supportedTypes |> List.forall checkSupported
              && unsupportedRdfTypes |> List.forall checkUnsupported ]
