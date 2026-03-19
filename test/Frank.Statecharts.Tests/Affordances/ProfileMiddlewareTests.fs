module Frank.Affordances.Tests.ProfileMiddlewareTests

open System
open System.Net
open System.Net.Http
open System.Text.Json
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Frank.Affordances
open Frank.Statecharts.Unified

// -- Test Fixtures --

let private alpsJsonFixture =
    """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      {
        "id": "gameState",
        "type": "semantic",
        "doc": { "value": "Current state of a game" }
      },
      {
        "id": "makeMove",
        "type": "unsafe",
        "rt": "#gameState",
        "doc": { "value": "Make a move in the game" }
      }
    ]
  }
}"""

let private owlTurtleFixture =
    """@prefix owl: <http://www.w3.org/2002/07/owl#> .
@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .
@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

<urn:frank:type:GameState> a owl:Class ;
    rdfs:label "GameState" .

<urn:frank:property:board> a owl:DatatypeProperty ;
    rdfs:domain <urn:frank:type:GameState> ;
    rdfs:range xsd:string .
"""

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

let private jsonSchemaFixture =
    """{
  "type": "object",
  "properties": {
    "board": {
      "type": "array",
      "items": { "type": "string" }
    },
    "currentPlayer": {
      "type": "string",
      "enum": ["X", "O"]
    }
  },
  "required": ["board", "currentPlayer"]
}"""

let private testProfiles: ProjectedProfiles =
    { AlpsProfiles = Map.ofList [ ("games", alpsJsonFixture) ]
      OwlOntologies = Map.ofList [ ("games", owlTurtleFixture) ]
      ShaclShapes = Map.ofList [ ("games", shaclTurtleFixture) ]
      JsonSchemas = Map.ofList [ ("games", jsonSchemaFixture) ] }

// -- Helpers --

let private buildTestServer (profiles: ProjectedProfiles) =
    let builder =
        WebHostBuilder()
            .Configure(fun app ->
                app.UseRouting() |> ignore

                app.UseEndpoints(fun endpoints ->
                    endpoints.MapProfiles(profiles)
                    endpoints.MapProfileDiscovery(profiles))
                |> ignore)
            .ConfigureServices(fun services -> services.AddRouting() |> ignore)

    new TestServer(builder)

// -- Tests --

[<Tests>]
let profileEndpointTests =
    testList
        "ProfileMiddleware"
        [
          // T051: ALPS JSON endpoint
          testCase "GET /alps/games returns ALPS JSON with correct content type"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response = client.GetAsync("/alps/games").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType =
                  response.Content.Headers.ContentType.MediaType

              Expect.equal contentType "application/alps+json" "Content-Type should be application/alps+json"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let alps = doc.RootElement.GetProperty("alps")
              let version = alps.GetProperty("version").GetString()
              Expect.equal version "1.0" "ALPS version should be 1.0"

              let descriptors = alps.GetProperty("descriptor")
              Expect.isTrue (descriptors.GetArrayLength() > 0) "Should have at least one descriptor"

          // T051: OWL Turtle endpoint
          testCase "GET /ontology/games returns OWL Turtle with correct content type"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/ontology/games").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType =
                  response.Content.Headers.ContentType.MediaType

              Expect.equal contentType "text/turtle" "Content-Type should be text/turtle"

              let body = response.Content.ReadAsStringAsync().Result
              Expect.isTrue (body.Contains("@prefix")) "Turtle should contain @prefix declarations"
              Expect.isTrue (body.Contains("owl:Class")) "Should contain OWL class declarations"

          // T051: SHACL Turtle endpoint
          testCase "GET /shapes/games returns SHACL Turtle with correct content type"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/shapes/games").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType =
                  response.Content.Headers.ContentType.MediaType

              Expect.equal contentType "text/turtle" "Content-Type should be text/turtle"

              let body = response.Content.ReadAsStringAsync().Result
              Expect.isTrue (body.Contains("@prefix")) "Turtle should contain @prefix declarations"
              Expect.isTrue (body.Contains("sh:NodeShape")) "Should contain SHACL shape declarations"

          // T051: JSON Schema endpoint
          testCase "GET /schemas/games returns JSON Schema with correct content type"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/schemas/games").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType =
                  response.Content.Headers.ContentType.MediaType

              Expect.equal contentType "application/schema+json" "Content-Type should be application/schema+json"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let typeProperty = doc.RootElement.GetProperty("type").GetString()
              Expect.equal typeProperty "object" "JSON Schema type should be 'object'"

          // T051: Unknown slug returns 404
          testCase "GET /alps/nonexistent returns 404"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/alps/nonexistent").Result

              Expect.equal response.StatusCode HttpStatusCode.NotFound "Should return 404 for unknown slug"

          // T051: Unknown slug for each format returns 404
          testCase "GET /ontology/nonexistent returns 404"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/ontology/nonexistent").Result

              Expect.equal response.StatusCode HttpStatusCode.NotFound "Should return 404 for unknown ontology slug"

          testCase "GET /shapes/nonexistent returns 404"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/shapes/nonexistent").Result

              Expect.equal response.StatusCode HttpStatusCode.NotFound "Should return 404 for unknown shapes slug"

          testCase "GET /schemas/nonexistent returns 404"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/schemas/nonexistent").Result

              Expect.equal response.StatusCode HttpStatusCode.NotFound "Should return 404 for unknown schema slug"

          // T051: Default Accept header works (serves content regardless)
          testCase "GET /alps/games with Accept */* returns ALPS JSON"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let request =
                  new HttpRequestMessage(HttpMethod.Get, "/alps/games")

              request.Headers.Add("Accept", "*/*")

              let response = client.SendAsync(request).Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200 with */* Accept"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              Expect.isTrue (doc.RootElement.TryGetProperty("alps") |> fst) "Should contain alps element"

          // T051: Vary header is set
          testCase "Profile responses include Vary: Accept header"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response = client.GetAsync("/alps/games").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let hasVary = response.Headers.Contains("Vary")
              Expect.isTrue hasVary "Should have Vary header"

              let varyValues = response.Headers.GetValues("Vary") |> Seq.toList

              Expect.isTrue
                  (varyValues |> List.exists (fun v -> v.Contains("Accept")))
                  "Vary header should include Accept"

          // T051: Empty profiles serve no endpoints (all return 404)
          testCase "empty profiles result in 404 for all profile paths"
          <| fun _ ->
              use server = buildTestServer ProjectedProfiles.empty
              use client = server.CreateClient()

              let alpsResult = client.GetAsync("/alps/games").Result
              let ontologyResult = client.GetAsync("/ontology/games").Result
              let shapesResult = client.GetAsync("/shapes/games").Result
              let schemasResult = client.GetAsync("/schemas/games").Result

              Expect.equal alpsResult.StatusCode HttpStatusCode.NotFound "ALPS should be 404"
              Expect.equal ontologyResult.StatusCode HttpStatusCode.NotFound "Ontology should be 404"
              Expect.equal shapesResult.StatusCode HttpStatusCode.NotFound "Shapes should be 404"
              Expect.equal schemasResult.StatusCode HttpStatusCode.NotFound "Schemas should be 404"

          // Discovery endpoint
          testCase "GET /.well-known/frank-profiles returns discovery document"
          <| fun _ ->
              use server = buildTestServer testProfiles
              use client = server.CreateClient()

              let response =
                  client.GetAsync("/.well-known/frank-profiles").Result

              Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

              let contentType =
                  response.Content.Headers.ContentType.MediaType

              Expect.equal contentType "application/json" "Content-Type should be application/json"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let profiles = doc.RootElement.GetProperty("profiles")
              Expect.isTrue (profiles.GetArrayLength() > 0) "Should have at least one profile entry" ]

[<Tests>]
let startupProjectionTests =
    testList
        "StartupProjection"
        [
          // Runtime state loading from assembly
          testCase "loadRuntimeStateFromAssembly returns None for assembly without embedded resource"
          <| fun _ ->
              // Use an assembly that doesn't have a unified-state.bin embedded
              let assembly = typeof<obj>.Assembly
              let result = StartupProjection.loadRuntimeStateFromAssembly assembly
              Expect.isNone result "Should return None when no embedded resource"

          testCase "ProjectedProfiles.isEmpty returns true for empty profiles"
          <| fun _ ->
              Expect.isTrue (ProjectedProfiles.isEmpty ProjectedProfiles.empty) "Empty profiles should be empty"

          testCase "ProjectedProfiles.isEmpty returns false for non-empty profiles"
          <| fun _ ->
              let profiles =
                  { ProjectedProfiles.empty with
                      AlpsProfiles = Map.ofList [ ("test", "{}") ] }

              Expect.isFalse (ProjectedProfiles.isEmpty profiles) "Non-empty profiles should not be empty" ]
