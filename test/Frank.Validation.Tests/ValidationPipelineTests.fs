/// Final-review finding I6: the full declarative chain
/// `resource { useValidation shapesGraph }` -> `webHost { useValidation }` -> interceptor was never
/// exercised end to end. ValidationMiddlewareTests wires the middleware by hand and attaches
/// ValidationMetadata via WithMetadata directly, which proves the interceptor works but proves
/// nothing about the two CE operations that are supposed to put it there. The only evidence the real
/// chain worked was the sample's manual curl run, outside the automated suite.
///
/// These tests build the resource through the REAL `resource { }` CE (so ResourceBuilderExtensions'
/// useValidation attaches the metadata for real) and compose the pipeline through the REAL
/// WebHostBuilder members the `webHost { }` CE desugars to -- `Resource` and `UseValidation`, called
/// exactly as a custom operation calls them. Only `Run` is bypassed, because it blocks on
/// Host.Run(); the app pipeline below mirrors Run's own composition order line for line
/// (BeforeRoutingMiddleware -> UseRouting -> Middleware -> UseEndpoints).
module Frank.Validation.Tests.ValidationPipelineTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Expecto
open Frank.Builder
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

/// Frank's own ResourceEndpointDataSource is internal to Frank and this assembly only has
/// InternalsVisibleTo from Frank.Validation, so the same two-member data source is restated here.
type private ResourceEndpoints(endpoints: Endpoint[]) =
    inherit EndpointDataSource()

    override _.Endpoints = endpoints :> IReadOnlyList<Endpoint>

    override _.GetChangeToken() =
        CancellationChangeToken(CancellationToken.None) :> IChangeToken

let private moveShapesGraph =
    Shacl.toShapesGraph
        [ recordShape
              (targetClass (Uri "https://schema.org/MoveAction"))
              [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                |> addConstraint (PropertyConstraint.MinCount 1)
                |> addConstraint (PropertyConstraint.Datatype XsdDatatype.Integer) ] ]

let private conformingBody =
    """[{"@id":"https://example.org/move1","@type":["https://schema.org/MoveAction"],"https://schema.org/position":[{"@value":3}]}]"""

let private violatingBody =
    """[{"@id":"https://example.org/move2","@type":["https://schema.org/MoveAction"]}]"""

/// The actual `resource { useValidation shapesGraph; post handler }` computation expression.
let private movesResource =
    resource "/games/{id}/moves" {
        useValidation moveShapesGraph

        post (
            RequestDelegate(fun (ctx: HttpContext) ->
                // Doubles as the I4 check on the real path: a handler reached through the
                // declarative chain can read the graph the interceptor already parsed.
                let triples =
                    match Validation.tryGetValidatedGraph ctx with
                    | Some graph -> graph.Triples.Count
                    | None -> -1

                ctx.Response.StatusCode <- 201
                ctx.Response.WriteAsync(sprintf "created:%d" triples))
        )
    }

/// A resource declared WITHOUT useValidation, to prove the interceptor is opt-in even when it is
/// registered app-wide by webHost { useValidation }.
let private unvalidatedResource =
    resource "/games/{id}/notes" { post (RequestDelegate(fun (ctx: HttpContext) -> ctx.Response.WriteAsync "note")) }

let private createTestServer () =
    let builder = WebHostBuilder([||])

    // Exactly what `webHost args { useValidation; resource movesResource; resource
    // unvalidatedResource }` desugars into, minus the blocking Run.
    let spec =
        WebHostSpec.Empty
        |> fun s -> builder.UseValidation(s)
        |> fun s -> builder.Resource(s, movesResource)
        |> fun s -> builder.Resource(s, unvalidatedResource)

    let host =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        spec.Services services |> ignore
                        services.AddRouting() |> ignore)
                    .Configure(fun app ->
                        // Mirrors WebHostBuilder.Run's own composition order.
                        app
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> spec.Middleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                endpoints.DataSources.Add(ResourceEndpoints(spec.Endpoints)))
                        |> ignore)
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()

let private post (client: HttpClient) (path: string) (body: string) =
    client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/ld+json"))

[<Tests>]
let tests =
    testList
        "resource { useValidation } + webHost { useValidation }, end to end"
        [ testTask "a conforming body reaches the handler through the real declarative chain" {
              let client = createTestServer ()
              let! (response: HttpResponseMessage) = post client "/games/1/moves" conformingBody

              Expect.equal response.StatusCode HttpStatusCode.Created "the handler ran and set 201"
              let! body = response.Content.ReadAsStringAsync()

              Expect.equal body "created:2" "and it read the already-parsed graph back (rdf:type + schema:position)"
          }

          testTask "a violating body 422s before the handler, through the real declarative chain" {
              let client = createTestServer ()
              let! (response: HttpResponseMessage) = post client "/games/1/moves" violatingBody

              Expect.equal response.StatusCode (enum 422) "422, not the handler's 201"
              let! (body: string) = response.Content.ReadAsStringAsync()
              Expect.isFalse (body.StartsWith "created") "the handler never ran"
              Expect.stringContains body "violations" "a real violation report"
          }

          testTask "a datatype violation (not just cardinality) is caught on the real chain too" {
              let client = createTestServer ()

              let wrongDatatype =
                  """[{"@id":"https://example.org/move3","@type":["https://schema.org/MoveAction"],"https://schema.org/position":[{"@value":"three"}]}]"""

              let! (response: HttpResponseMessage) = post client "/games/1/moves" wrongDatatype
              Expect.equal response.StatusCode (enum 422) "a string where xsd:integer is required"
          }

          testTask "a resource declared without useValidation is untouched, even with the interceptor registered" {
              let client = createTestServer ()
              let! (response: HttpResponseMessage) = post client "/games/1/notes" violatingBody

              Expect.equal response.StatusCode HttpStatusCode.OK "validation is per-resource opt-in"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "note" "the unvalidated handler ran"
          }

          testTask "a non-ld+json content type on a validated resource bypasses validation (documented behaviour)" {
              let client = createTestServer ()

              let! (response: HttpResponseMessage) =
                  client.PostAsync(
                      "/games/1/moves",
                      new StringContent(violatingBody, Encoding.UTF8, "application/json")
                  )

              // This is the bypass finding I8 makes explicit in the README: only an exact
              // application/ld+json Content-Type is intercepted, so the handler is on its own here.
              Expect.equal response.StatusCode HttpStatusCode.Created "reached the handler unvalidated"
              let! body = response.Content.ReadAsStringAsync()
              Expect.equal body "created:-1" "and there is no pre-parsed graph for it to read"
          } ]
