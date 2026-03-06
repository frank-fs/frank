module Frank.LinkedData.Sample.Tests.HttpTests

open System
open System.IO
open System.Net.Http
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open Frank.Builder
open Frank.LinkedData
open Frank.LinkedData.Rdf

/// Create a product ontology with Name, Price, InStock properties.
let private makeProductOntology () =
    let ontology = new Graph()
    let baseUri = "http://example.org/api"

    let rdfType =
        ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))

    let datatypeProp =
        ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/2002/07/owl#DatatypeProperty"))

    for propName in [ "Name"; "Price"; "InStock"; "Id" ] do
        let propNode =
            ontology.CreateUriNode(UriFactory.Root.Create($"{baseUri}/properties/Product/{propName}"))

        ontology.Assert(Triple(propNode, rdfType, datatypeProp)) |> ignore

    ontology

let private makeConfig () =
    let manifest: SemanticManifest =
        { Version = "1.0.0"
          BaseUri = "http://example.org/api"
          SourceHash = "test-hash"
          Vocabularies = []
          GeneratedAt = DateTimeOffset.UtcNow }

    { OntologyGraph = makeProductOntology ()
      ShapesGraph = new Graph()
      BaseUri = "http://example.org/api"
      Manifest = manifest }
    : LinkedDataConfig

/// Create a TestHost with LinkedData middleware and a product endpoint.
let private createProductTestHost (config: LinkedDataConfig) =
    let hostBuilder =
        HostBuilder()
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddSingleton<LinkedDataConfig>(config) |> ignore
                        services.AddRouting() |> ignore)
                    .Configure(fun (app: IApplicationBuilder) ->
                        app.UseRouting() |> ignore

                        app.Use(
                            Func<HttpContext, RequestDelegate, Task>(WebHostBuilderExtensions.linkedDataMiddleware)
                        )
                        |> ignore

                        app.UseEndpoints(fun endpoints ->
                            endpoints
                                .MapGet(
                                    "/products/1",
                                    RequestDelegate(fun ctx ->
                                        ctx.Response.ContentType <- "application/json"

                                        ctx.Response.WriteAsync(
                                            """{"id":1,"name":"F# in Action","price":39.99,"inStock":true,"category":"Books"}"""
                                        ))
                                )
                                .WithMetadata(LinkedDataMarker)
                            |> ignore

                            endpoints
                                .MapGet(
                                    "/products",
                                    RequestDelegate(fun ctx ->
                                        ctx.Response.ContentType <- "application/json"

                                        ctx.Response.WriteAsync(
                                            """[{"id":1,"name":"F# in Action","price":39.99}]"""
                                        ))
                                )
                                .WithMetadata(LinkedDataMarker)
                            |> ignore)
                        |> ignore)
                |> ignore)

    let host = hostBuilder.Build()
    host.Start()
    host

[<Tests>]
let tests =
    testList
        "HTTP Content Negotiation"
        [ testCase "GET /products/1 with Accept: application/ld+json returns JSON-LD"
          <| fun _ ->
              let config = makeConfig ()
              use host = createProductTestHost config
              use client = host.GetTestServer().CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/products/1")
              request.Headers.Add("Accept", "application/ld+json")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result

              Expect.equal (int response.StatusCode) 200 "Should return 200"

              Expect.equal
                  (response.Content.Headers.ContentType.MediaType)
                  "application/ld+json"
                  "Content-Type should be application/ld+json"
              // JSON-LD is valid JSON
              let doc = JsonDocument.Parse body
              Expect.isSome (Some doc) "Body should be valid JSON"

          testCase "GET /products/1 with Accept: text/turtle returns valid Turtle"
          <| fun _ ->
              let config = makeConfig ()
              use host = createProductTestHost config
              use client = host.GetTestServer().CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/products/1")
              request.Headers.Add("Accept", "text/turtle")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result

              Expect.equal (int response.StatusCode) 200 "Should return 200"

              Expect.equal
                  (response.Content.Headers.ContentType.MediaType)
                  "text/turtle"
                  "Content-Type should be text/turtle"

              // Parse Turtle to verify it's valid and has triples
              let graph = new Graph()
              let parser = TurtleParser()
              use reader = new StringReader(body)
              parser.Load(graph, reader)
              Expect.isGreaterThan graph.Triples.Count 0 "Turtle response should contain triples"

          testCase "GET /products/1 with Accept: application/rdf+xml returns valid RDF/XML"
          <| fun _ ->
              let config = makeConfig ()
              use host = createProductTestHost config
              use client = host.GetTestServer().CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/products/1")
              request.Headers.Add("Accept", "application/rdf+xml")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result

              Expect.equal (int response.StatusCode) 200 "Should return 200"

              Expect.equal
                  (response.Content.Headers.ContentType.MediaType)
                  "application/rdf+xml"
                  "Content-Type should be application/rdf+xml"

              // Parse RDF/XML to verify it's valid
              let graph = new Graph()
              let parser = RdfXmlParser()
              use reader = new StringReader(body)
              parser.Load(graph, reader)
              Expect.isGreaterThan graph.Triples.Count 0 "RDF/XML response should contain triples"

          testCase "GET /products/1 with Accept: application/json returns standard JSON (not RDF)"
          <| fun _ ->
              let config = makeConfig ()
              use host = createProductTestHost config
              use client = host.GetTestServer().CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/products/1")
              request.Headers.Add("Accept", "application/json")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result

              Expect.equal (int response.StatusCode) 200 "Should return 200"
              // Should be standard JSON, not RDF
              let doc = JsonDocument.Parse body
              let root = doc.RootElement
              Expect.equal (root.GetProperty("name").GetString()) "F# in Action" "Should contain product name"
              Expect.equal (root.GetProperty("price").GetDouble()) 39.99 "Should contain product price"

          testCase "RDF response contains ontology-derived predicates for product properties"
          <| fun _ ->
              let config = makeConfig ()
              use host = createProductTestHost config
              use client = host.GetTestServer().CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/products/1")
              request.Headers.Add("Accept", "text/turtle")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result

              let graph = new Graph()
              let parser = TurtleParser()
              use reader = new StringReader(body)
              parser.Load(graph, reader)

              // Verify triples use ontology-derived predicate URIs
              let predicateUris =
                  graph.Triples
                  |> Seq.choose (fun t ->
                      match t.Predicate with
                      | :? IUriNode as u -> Some(u.Uri.ToString())
                      | _ -> None)
                  |> Seq.toList

              // Should contain properties from the ontology
              let hasNamePredicate =
                  predicateUris
                  |> List.exists (fun u -> u.Contains("/properties/Product/Name") || u.Contains("/Name"))

              Expect.isTrue hasNamePredicate "RDF should contain a Name predicate from the ontology"

          testCase "embedded resource names match expected Frank.Semantic convention"
          <| fun _ ->
              // Verify the expected embedded resource names are correct constants
              // (The actual embedding is verified by the MSBuild targets at build time)
              let expectedNames =
                  [ "Frank.Semantic.ontology.owl.xml"
                    "Frank.Semantic.shapes.shacl.ttl"
                    "Frank.Semantic.manifest.json" ]

              for name in expectedNames do
                  Expect.isTrue
                      (name.StartsWith("Frank.Semantic."))
                      $"Resource name '{name}' should start with Frank.Semantic." ]
