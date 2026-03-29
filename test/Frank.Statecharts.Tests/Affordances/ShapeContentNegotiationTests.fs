module Frank.Affordances.Tests.ShapeContentNegotiationTests

open System
open System.Net
open System.Net.Http
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Frank.Affordances
open Frank.Resources.Model

// -- Test Fixtures --

let private shaclTurtleFixture =
    """@prefix sh: <http://www.w3.org/ns/shacl#> .
@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

<urn:frank:shape:GameState> a sh:NodeShape ;
    sh:targetClass <urn:frank:type:GameState> ;
    sh:property [
        sh:path <urn:frank:property:board> ;
        sh:datatype xsd:string ;
        sh:minCount 1
    ] .
"""

let private testProfiles: ProjectedProfiles =
    { AlpsProfiles = Map.empty
      RoleAlpsProfiles = Map.empty
      OwlOntologies = Map.empty
      ShaclShapes = Map.ofList [ ("games", shaclTurtleFixture) ]
      JsonSchemas = Map.empty }

// -- Helpers --

let private buildTestServer (profiles: ProjectedProfiles) =
    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    let app = builder.Build()

    app.UseRouting() |> ignore

    app.UseEndpoints(fun endpoints -> endpoints.MapProfiles(profiles))
    |> ignore

    app.Start()
    app.GetTestServer()

// -- Tests --

[<Tests>]
let shapeContentNegotiationTests =
    testList
        "ShapeContentNegotiation"
        [
          // #174: Content negotiation on /shapes/{slug}
          testCase "GET /shapes/games with Accept: text/turtle returns text/turtle"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let request =
                  new HttpRequestMessage(HttpMethod.Get, "/shapes/games")

              request.Headers.Add("Accept", "text/turtle")

              let response = client.SendAsync(request).Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType =
                  response.Content.Headers.ContentType.MediaType

              Expect.equal contentType "text/turtle" "Content-Type should be text/turtle"

              let body = response.Content.ReadAsStringAsync().Result
              Expect.isTrue (body.Contains("sh:NodeShape")) "Body should contain SHACL shape content"

          testCase "GET /shapes/games with Accept: application/ld+json returns JSON-LD"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let request =
                  new HttpRequestMessage(HttpMethod.Get, "/shapes/games")

              request.Headers.Add("Accept", "application/ld+json")

              let response = client.SendAsync(request).Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType =
                  response.Content.Headers.ContentType.MediaType

              Expect.equal contentType "application/ld+json" "Content-Type should be application/ld+json"

              let body = response.Content.ReadAsStringAsync().Result
              Expect.isTrue (body.Contains("@context")) "JSON-LD should contain @context"

          testCase "GET /shapes/games with Accept: application/rdf+xml returns RDF/XML"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let request =
                  new HttpRequestMessage(HttpMethod.Get, "/shapes/games")

              request.Headers.Add("Accept", "application/rdf+xml")

              let response = client.SendAsync(request).Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType =
                  response.Content.Headers.ContentType.MediaType

              Expect.equal contentType "application/rdf+xml" "Content-Type should be application/rdf+xml"

              let body = response.Content.ReadAsStringAsync().Result
              Expect.isTrue (body.Contains("rdf:RDF") || body.Contains("RDF")) "RDF/XML should contain RDF elements"

          testCase "GET /shapes/games with default Accept returns text/turtle"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response = client.GetAsync("/shapes/games").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType =
                  response.Content.Headers.ContentType.MediaType

              Expect.equal contentType "text/turtle" "Default should be text/turtle"

          testCase "GET /shapes/games includes Vary: Accept header"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response = client.GetAsync("/shapes/games").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let hasVary = response.Headers.Contains("Vary")
              Expect.isTrue hasVary "Should have Vary header"

              let varyValues = response.Headers.GetValues("Vary") |> Seq.toList

              Expect.isTrue
                  (varyValues |> List.exists (fun v -> v.Contains("Accept")))
                  "Vary header should include Accept"

          testCase "GET /shapes/nonexistent returns 404"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/shapes/nonexistent").Result

              Expect.equal response.StatusCode HttpStatusCode.NotFound "Should return 404 for unknown slug" ]
