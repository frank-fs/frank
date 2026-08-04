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
let private createTestServer (validated: bool) =
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

                                if validated then
                                    mapping.WithMetadata(ValidationMetadata moveShapesGraph) |> ignore
                                else
                                    ())
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
          } ]
