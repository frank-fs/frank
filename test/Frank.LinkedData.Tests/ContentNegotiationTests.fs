module Frank.LinkedData.Tests.ContentNegotiationTests

open System
open System.IO
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open Frank.Builder
open Frank.LinkedData
open Frank.LinkedData.Rdf

/// Helper to create a TestServer with the LinkedData middleware and an endpoint.
let private createTestHost (config: LinkedDataConfig) (useMarker: bool) =
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
                            Func<HttpContext, RequestDelegate, Task>(
                                WebHostBuilderExtensions.linkedDataMiddleware NullLogger.Instance
                            )
                        )
                        |> ignore

                        app.UseEndpoints(fun endpoints ->
                            let ep =
                                endpoints.MapGet(
                                    "/person/1",
                                    RequestDelegate(fun ctx ->
                                        ctx.Response.ContentType <- "application/json"
                                        ctx.Response.WriteAsync("""{"Name":"Alice","Age":30}"""))
                                )

                            if useMarker then
                                ep.WithMetadata(LinkedDataMarker) |> ignore
                            else
                                ())
                        |> ignore)
                |> ignore)

    let host = hostBuilder.Build()
    host.Start()
    host

let private makeConfig (ontology: IGraph) =
    let manifest: SemanticManifest =
        { Version = "1.0.0"
          BaseUri = "http://example.org/api"
          SourceHash = "abc"
          Vocabularies = []
          GeneratedAt = DateTimeOffset.UtcNow }

    { OntologyGraph = ontology
      ShapesGraph = new Graph()
      BaseUri = "http://example.org/api"
      Manifest = manifest }
    : LinkedDataConfig

let private makeOntologyWithNameAndAge () =
    let ontology = new Graph()

    let nameNode =
        ontology.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Name"))

    let ageNode =
        ontology.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Age"))

    let rdfType =
        ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))

    let owlProp =
        ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/2002/07/owl#DatatypeProperty"))

    ontology.Assert(Triple(nameNode, rdfType, owlProp)) |> ignore
    ontology.Assert(Triple(ageNode, rdfType, owlProp)) |> ignore
    ontology

[<Tests>]
let tests =
    testList
        "ContentNegotiation"
        [ testCase "LinkedDataMarker is added to endpoint metadata"
          <| fun _ ->
              let spec =
                  ResourceSpec.Empty
                  |> fun s -> ResourceBuilder.AddMetadata(s, fun b -> b.Metadata.Add(LinkedDataMarker))

              let builtSpec =
                  { spec with
                      Handlers = [ "GET", RequestDelegate(fun _ -> Task.CompletedTask) ] }

              let resource = builtSpec.Build("/test")
              Expect.isNonEmpty resource.Endpoints "Should have endpoints"
              let endpoint = resource.Endpoints.[0]
              let marker = endpoint.Metadata.GetMetadata<LinkedDataMarker>()
              Expect.isFalse (obj.ReferenceEquals(marker, null)) "Endpoint should have LinkedDataMarker metadata"

          testCase "negotiateRdfType returns text/turtle for Accept: text/turtle"
          <| fun _ ->
              let result = negotiateRdfType "text/turtle"
              Expect.equal result (Some "text/turtle") "Should negotiate text/turtle"

          testCase "negotiateRdfType returns application/ld+json for Accept: application/ld+json"
          <| fun _ ->
              let result = negotiateRdfType "application/ld+json"
              Expect.equal result (Some "application/ld+json") "Should negotiate application/ld+json"

          testCase "negotiateRdfType returns application/rdf+xml for Accept: application/rdf+xml"
          <| fun _ ->
              let result = negotiateRdfType "application/rdf+xml"
              Expect.equal result (Some "application/rdf+xml") "Should negotiate application/rdf+xml"

          testCase "negotiateRdfType returns None for Accept: application/json"
          <| fun _ ->
              let result = negotiateRdfType "application/json"
              Expect.equal result None "Should not negotiate application/json as RDF"

          testCase "negotiateRdfType returns None for empty Accept"
          <| fun _ ->
              let result = negotiateRdfType ""
              Expect.equal result None "Should not negotiate empty Accept"

          testCase "projectJsonToRdf creates triples from JSON matching ontology"
          <| fun _ ->
              let ontology = makeOntologyWithNameAndAge ()
              let json = System.Text.Json.JsonDocument.Parse("""{"Name":"Bob","Age":25}""")
              let uri = Uri("http://example.org/person/2")
              let result = projectJsonToRdf ontology uri json.RootElement
              Expect.equal result.Triples.Count 2 "Should produce 2 triples from JSON"

          testCase "projectJsonToRdf ignores properties not in ontology"
          <| fun _ ->
              let ontology = new Graph()

              let nameNode =
                  ontology.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Name"))

              let rdfType =
                  ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))

              let owlProp =
                  ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/2002/07/owl#DatatypeProperty"))

              ontology.Assert(Triple(nameNode, rdfType, owlProp)) |> ignore

              let json =
                  System.Text.Json.JsonDocument.Parse("""{"Name":"Bob","Unknown":"value"}""")

              let uri = Uri("http://example.org/person/3")
              let result = projectJsonToRdf ontology uri json.RootElement
              Expect.equal result.Triples.Count 1 "Should only produce 1 triple (Unknown not in ontology)"

          testCase "writeRdf dispatches to correct formatter"
          <| fun _ ->
              let g = new Graph()
              let s = g.CreateUriNode(UriFactory.Root.Create("http://example.org/x"))
              let p = g.CreateUriNode(UriFactory.Root.Create("http://example.org/p"))
              let o = g.CreateLiteralNode("v")
              g.Assert(Triple(s, p, o)) |> ignore

              use ms1 = new MemoryStream()
              writeRdf "text/turtle" g ms1
              Expect.isGreaterThan (int ms1.Length) 0 "Turtle output should have content"

              use ms2 = new MemoryStream()
              writeRdf "application/rdf+xml" g ms2
              Expect.isGreaterThan (int ms2.Length) 0 "RDF/XML output should have content"

              use ms3 = new MemoryStream()
              writeRdf "application/ld+json" g ms3
              Expect.isGreaterThan (int ms3.Length) 0 "JSON-LD output should have content"

              use ms4 = new MemoryStream()
              writeRdf "text/plain" g ms4
              Expect.equal (int ms4.Length) 0 "Unknown type should produce no output"

          testCase "middleware passes through when no LinkedDataMarker"
          <| fun _ ->
              let ontology = makeOntologyWithNameAndAge ()
              let config = makeConfig ontology
              use host = createTestHost config false
              let server = host.GetTestServer()
              use client = server.CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/person/1")
              request.Headers.Add("Accept", "text/turtle")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result
              Expect.stringContains body "Alice" "Response should contain original JSON when no marker"

          testCase "middleware intercepts when LinkedDataMarker is present and Accept is text/turtle"
          <| fun _ ->
              let ontology = makeOntologyWithNameAndAge ()
              let config = makeConfig ontology
              use host = createTestHost config true
              let server = host.GetTestServer()
              use client = server.CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/person/1")
              request.Headers.Add("Accept", "text/turtle")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result

              // The middleware should have intercepted and produced Turtle
              Expect.isFalse
                  (body.StartsWith("{"))
                  (sprintf "Body should not be JSON. Got: %s" (body.Substring(0, min body.Length 500)))

              Expect.equal
                  (response.Content.Headers.ContentType.MediaType)
                  "text/turtle"
                  "Content-Type should be text/turtle"

              let parsed = new Graph()
              let parser = TurtleParser()
              use reader = new StringReader(body)
              parser.Load(parsed, reader)
              Expect.isGreaterThan parsed.Triples.Count 0 "Turtle response should have triples"

          testCase "middleware passes through when Accept is not RDF"
          <| fun _ ->
              let ontology = makeOntologyWithNameAndAge ()
              let config = makeConfig ontology
              use host = createTestHost config true
              let server = host.GetTestServer()
              use client = server.CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/person/1")
              request.Headers.Add("Accept", "application/json")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result
              Expect.stringContains body "Alice" "Response should contain original JSON for non-RDF Accept"

          testCase "middleware returns JSON-LD when Accept is application/ld+json"
          <| fun _ ->
              let ontology = makeOntologyWithNameAndAge ()
              let config = makeConfig ontology
              use host = createTestHost config true
              let server = host.GetTestServer()
              use client = server.CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/person/1")
              request.Headers.Add("Accept", "application/ld+json")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result

              Expect.equal
                  (response.Content.Headers.ContentType.MediaType)
                  "application/ld+json"
                  "Content-Type should be application/ld+json"

              let doc = System.Text.Json.JsonDocument.Parse(body)
              let hasContext = doc.RootElement.TryGetProperty("@context") |> fst
              Expect.isTrue hasContext "JSON-LD response should have @context"

          testCase "middleware returns RDF/XML when Accept is application/rdf+xml"
          <| fun _ ->
              let ontology = makeOntologyWithNameAndAge ()
              let config = makeConfig ontology
              use host = createTestHost config true
              let server = host.GetTestServer()
              use client = server.CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/person/1")
              request.Headers.Add("Accept", "application/rdf+xml")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result

              Expect.equal
                  (response.Content.Headers.ContentType.MediaType)
                  "application/rdf+xml"
                  "Content-Type should be application/rdf+xml"

              Expect.isTrue (body.Contains("rdf:RDF") || body.Contains("<rdf:")) "Should contain RDF/XML markup"

          testCase "middleware passes through when Accept is text/html"
          <| fun _ ->
              let ontology = makeOntologyWithNameAndAge ()
              let config = makeConfig ontology
              use host = createTestHost config true
              let server = host.GetTestServer()
              use client = server.CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/person/1")
              request.Headers.Add("Accept", "text/html")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result
              Expect.stringContains body "Alice" "text/html should pass through to standard handler"

          testCase "middleware passes through original response when JSON projection fails"
          <| fun _ ->
              let ontology = makeOntologyWithNameAndAge ()
              let config = makeConfig ontology

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
                                      Func<HttpContext, RequestDelegate, Task>(
                                          WebHostBuilderExtensions.linkedDataMiddleware NullLogger.Instance
                                      )
                                  )
                                  |> ignore

                                  app.UseEndpoints(fun endpoints ->
                                      let ep =
                                          endpoints.MapGet(
                                              "/bad",
                                              RequestDelegate(fun ctx ->
                                                  ctx.Response.ContentType <- "application/json"
                                                  ctx.Response.WriteAsync("not valid json {{"))
                                          )

                                      ep.WithMetadata(LinkedDataMarker) |> ignore)
                                  |> ignore)
                          |> ignore)

              use host = hostBuilder.Build()
              host.Start()
              let server = host.GetTestServer()
              use client = server.CreateClient()

              let request = new HttpRequestMessage(HttpMethod.Get, "/bad")
              request.Headers.Add("Accept", "text/turtle")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              let body = response.Content.ReadAsStringAsync().Result
              Expect.stringContains body "not valid json" "Should pass through original response on projection failure" ]
