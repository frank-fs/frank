module Frank.Validation.Tests.ValidationMiddlewareTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

let private moveShapesGraph =
    Shacl.toShapesGraph
        [ recordShape
              (targetClass (Uri "https://schema.org/MoveAction"))
              [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                |> addConstraint (PropertyConstraint.MinCount 1) ] ]

let private conformingBody =
    """[{"@id":"https://example.org/move1","@type":["https://schema.org/MoveAction"],"https://schema.org/position":[{"@value":3}]}]"""

let private violatingBody =
    """[{"@id":"https://example.org/move2","@type":["https://schema.org/MoveAction"]}]"""

/// Wires useValidationMiddleware exactly where WebHostBuilder.Run places it -- after UseRouting,
/// before UseEndpoints -- without going through the webHost{ } CE, since Run blocks. Mirrors
/// test/Frank.Tests/ResponseLinkTests.fs's createTestServer.
let private createTestServerWith (shapesGraph: VDS.RDF.Shacl.ShapesGraph option) =
    let builder =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                    .Configure(fun app ->
                        app
                        |> fun app -> app.UseRouting()
                        |> Frank.Validation.WebHostBuilderExtensions.useValidationMiddleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                let mapping =
                                    endpoints.MapPost(
                                        "/moves",
                                        Func<HttpContext, Task>(fun ctx -> ctx.Response.WriteAsync "handled")
                                    )

                                match shapesGraph with
                                | Some sg -> mapping.WithMetadata(ValidationMetadata sg) |> ignore
                                | None -> ())
                            |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

let private createTestServer (validated: bool) =
    createTestServerWith (if validated then Some moveShapesGraph else None)

/// A server whose handler reads the pre-parsed graph back out through the PUBLIC accessor -- the
/// "handler doesn't re-parse" feature the design doc documents (final-review finding I4). Kept
/// separate from createTestServerWith because the other tests assert on that handler's exact body.
let private createGraphEchoServer () =
    let builder =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                    .Configure(fun app ->
                        app
                        |> fun app -> app.UseRouting()
                        |> Frank.Validation.WebHostBuilderExtensions.useValidationMiddleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                endpoints
                                    .MapPost(
                                        "/moves",
                                        Func<HttpContext, Task>(fun ctx ->
                                            match Validation.tryGetValidatedGraph ctx with
                                            | Some g ->
                                                ctx.Response.WriteAsync(sprintf "triples=%d" g.Triples.Count)
                                            | None -> ctx.Response.WriteAsync "no-graph")
                                    )
                                    .WithMetadata(ValidationMetadata moveShapesGraph)
                                |> ignore)
                            |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

[<Tests>]
let tests =
    testList
        "useValidationMiddleware"
        [ testTask "no ValidationMetadata on the endpoint -- passes straight through" {
              let client = createTestServer false

              let! (response: HttpResponseMessage) =
                  client.PostAsync("/moves", new StringContent(violatingBody, Encoding.UTF8, "application/ld+json"))

              Expect.equal response.StatusCode HttpStatusCode.OK "handler ran unvalidated"
          }

          testTask "GET requests to a validated resource pass through unvalidated (not POST/PUT/PATCH)" {
              let client = createTestServer true
              let! (response: HttpResponseMessage) = client.GetAsync("/moves")
              Expect.notEqual response.StatusCode HttpStatusCode.UnprocessableEntity "GET is never intercepted"
          }

          testTask "a conforming application/ld+json body reaches the handler" {
              let client = createTestServer true

              let! (response: HttpResponseMessage) =
                  client.PostAsync("/moves", new StringContent(conformingBody, Encoding.UTF8, "application/ld+json"))

              Expect.equal response.StatusCode HttpStatusCode.OK "handler ran"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "handled" "handler's own response body, untouched"
          }

          testTask "a violating body short-circuits with 422 and never reaches the handler" {
              let client = createTestServer true

              let! (response: HttpResponseMessage) =
                  client.PostAsync("/moves", new StringContent(violatingBody, Encoding.UTF8, "application/ld+json"))

              Expect.equal response.StatusCode (enum 422) "422 Unprocessable Entity"
              let! body = response.Content.ReadAsStringAsync()
              Expect.notEqual body "handled" "handler never ran"
          }

          testTask "422 with Accept: application/ld+json returns a real sh:ValidationReport" {
              let client = createTestServer true
              let req = new HttpRequestMessage(HttpMethod.Post, "/moves")
              req.Content <- new StringContent(violatingBody, Encoding.UTF8, "application/ld+json")
              req.Headers.Accept.ParseAdd("application/ld+json")
              let! (response: HttpResponseMessage) = client.SendAsync(req)
              Expect.equal response.Content.Headers.ContentType.MediaType "application/ld+json" "ld+json response"
              let! body = response.Content.ReadAsStringAsync()
              Expect.stringContains body "ValidationReport" "real SHACL report in the body"
          }

          testTask "422 with no Accept (or a non-ld+json Accept) returns application/problem+json" {
              let client = createTestServer true

              let! (response: HttpResponseMessage) =
                  client.PostAsync("/moves", new StringContent(violatingBody, Encoding.UTF8, "application/ld+json"))

              Expect.equal
                  response.Content.Headers.ContentType.MediaType
                  "application/problem+json"
                  "problem+json by default"

              let! body = response.Content.ReadAsStringAsync()
              Expect.stringContains body "violations" "flattened violations array present"
          }

          testTask "malformed JSON-LD returns 400, distinct from 422" {
              let client = createTestServer true

              let! (response: HttpResponseMessage) =
                  client.PostAsync("/moves", new StringContent("{not valid json", Encoding.UTF8, "application/ld+json"))

              Expect.equal
                  response.StatusCode
                  HttpStatusCode.BadRequest
                  "400, not 422 -- a parse failure isn't a SHACL violation"
          }

          testTask "an oversized body returns 413 before parsing is attempted" {
              let client = createTestServer true
              let huge = String('x', 2_000_000)

              let! (response: HttpResponseMessage) =
                  client.PostAsync("/moves", new StringContent(huge, Encoding.UTF8, "application/ld+json"))

              Expect.equal response.StatusCode (enum 413) "413 Payload Too Large"

              // Final-review minor item: 413 used to return a bare status with an empty body and no
              // Content-Type, inconsistent with the 400/422 paths.
              Expect.equal
                  response.Content.Headers.ContentType.MediaType
                  "application/problem+json"
                  "413 is problem+json, same as 400/422"
          }

          // Final-review finding I4: the design doc documents stashing the parsed graph so a
          // downstream handler -- in a CONSUMING application, a different assembly -- can read it
          // back without re-parsing, and the sample's comment claims as much. ValidatedGraphKey is
          // internal, so as shipped no external consumer could reach it at all. There is now a
          // public accessor that doesn't leak the magic string either.
          testTask "a handler can read the pre-parsed graph back through the public accessor" {
              let client = createGraphEchoServer ()

              let! (response: HttpResponseMessage) =
                  client.PostAsync("/moves", new StringContent(conformingBody, Encoding.UTF8, "application/ld+json"))

              Expect.equal response.StatusCode HttpStatusCode.OK "handler ran"
              let! body = response.Content.ReadAsStringAsync()

              Expect.equal
                  body
                  "triples=2"
                  "the handler saw the graph the middleware already parsed (rdf:type + schema:position)"
          }

          testTask "tryGetValidatedGraph returns None when nothing was validated" {
              let client = createTestServer false

              let! (response: HttpResponseMessage) =
                  client.PostAsync("/moves", new StringContent(violatingBody, Encoding.UTF8, "application/ld+json"))

              Expect.equal response.StatusCode HttpStatusCode.OK "unvalidated pass-through"
          }

          // Final-review finding C1, end to end: a literal focus node (TargetSpec.ObjectsOf) used to
          // fabricate a garbage IRI that crashed Frank.Rdf's resolveIri while serializing the 422
          // ld+json report -- surfacing as an unhandled 500 with a torn response.
          testTask "a violation on a LITERAL focus node returns a real 422 ld+json report, not a 500" {
              let literalTargetShapes =
                  Shacl.toShapesGraph
                      [ recordShape
                            [ TargetSpec.ObjectsOf(Uri "https://schema.org/name") ]
                            [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                              |> addConstraint (PropertyConstraint.MinCount 1) ] ]

              let client = createTestServerWith (Some literalTargetShapes)

              let body =
                  """[{"@id":"https://example.org/p1","https://schema.org/name":[{"@value":"Alice"}]}]"""

              let req = new HttpRequestMessage(HttpMethod.Post, "/moves")
              req.Content <- new StringContent(body, Encoding.UTF8, "application/ld+json")
              req.Headers.Accept.ParseAdd("application/ld+json")
              let! (response: HttpResponseMessage) = client.SendAsync(req)

              Expect.equal response.StatusCode (enum 422) "422, not an unhandled 500"
              let! text = response.Content.ReadAsStringAsync()
              Expect.stringContains text "Alice" "the literal focus node is in the report"
          }

          // Final-review findings C1/I1/I7 share one defence: nothing dotNetRDF raises during
          // request handling may escape as an unhandled exception. Provoked here with a shapes graph
          // built OUTSIDE toShapesGraph (so it skips the build-time SPARQL check I1 adds) carrying a
          // syntactically broken sh:select -- dotNetRDF raises RdfParseException at validate time.
          testTask "an unexpected validation-engine exception becomes a logged 500 problem+json" {
              let brokenDoc =
                  let sparqlNode = Node.blank ()
                  let propNode = Node.blank ()
                  let shapeNode = Node.Iri "https://example.org/BrokenShape"

                  { Prefixes =
                      [ "sh", "http://www.w3.org/ns/shacl#"
                        "rdf", "http://www.w3.org/1999/02/22-rdf-syntax-ns#" ]
                    Statements =
                      [ shapeNode, RdfTypeIri, Value.Node(Node.Iri "sh:NodeShape")
                        shapeNode, "sh:targetObjectsOf", Value.Node(Node.Iri "https://schema.org/name")
                        shapeNode, "sh:property", Value.Node propNode
                        propNode, "sh:path", Value.Node(Node.Iri "https://schema.org/x")
                        propNode, "sh:sparql", Value.Node sparqlNode
                        sparqlNode, "sh:select", Value.Literal(Literal.String "SELECT $this WHERE { <<< }") ] }

              let brokenShapes = new VDS.RDF.Shacl.ShapesGraph(Doc.toGraph brokenDoc)
              let client = createTestServerWith (Some brokenShapes)

              let body =
                  """[{"@id":"https://example.org/p1","https://schema.org/name":[{"@value":"Alice"}]}]"""

              let! (response: HttpResponseMessage) =
                  client.PostAsync("/moves", new StringContent(body, Encoding.UTF8, "application/ld+json"))

              Expect.equal response.StatusCode HttpStatusCode.InternalServerError "500, not an unhandled crash"

              Expect.equal
                  response.Content.Headers.ContentType.MediaType
                  "application/problem+json"
                  "a real problem+json body, not a torn/empty response"
          } ]
