module Frank.Validation.Tests.ValidationTests

open System.Net
open System.Net.Http
open System.Text
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open VDS.RDF
open VDS.RDF.Shacl
open Frank.Validation.ValidationMiddleware

let private makeTestShapesGraph () : ShapesGraph =
    let sh = "http://www.w3.org/ns/shacl#"
    let xsd = "http://www.w3.org/2001/XMLSchema#"
    let rdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"
    let g = new Graph()

    let uri s = g.CreateUriNode(UriFactory.Create(s))
    let lit s (dt: string) = g.CreateLiteralNode(s, UriFactory.Create(dt))

    let shapeNode = uri "urn:test:OrderShape"
    let propNode = uri "urn:test:TotalPropShape"

    g.Assert(Triple(shapeNode, uri rdfType, uri (sh + "NodeShape"))) |> ignore
    g.Assert(Triple(shapeNode, uri (sh + "targetClass"), uri "https://schema.org/Order")) |> ignore
    g.Assert(Triple(shapeNode, uri (sh + "property"), propNode)) |> ignore
    g.Assert(Triple(propNode, uri rdfType, uri (sh + "PropertyShape"))) |> ignore
    g.Assert(Triple(propNode, uri (sh + "path"), uri "https://schema.org/totalPaymentDue")) |> ignore
    g.Assert(Triple(propNode, uri (sh + "datatype"), uri (xsd + "integer"))) |> ignore
    g.Assert(Triple(propNode, uri (sh + "minCount"), lit "1" (xsd + "integer"))) |> ignore

    new ShapesGraph(g)

let private createServer () =
    let shapesGraph = makeTestShapesGraph ()

    let host =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        services.AddSingleton<ShapesGraph>(shapesGraph) |> ignore)
                    .Configure(fun app ->
                        app.UseRouting() |> ignore
                        app.UseMiddleware<ValidationMiddleware>() |> ignore

                        app.UseEndpoints(fun endpoints ->
                            endpoints.MapPost(
                                "/orders",
                                RequestDelegate(fun ctx ->
                                    ctx.Response.StatusCode <- 200
                                    ctx.Response.WriteAsync("ok")))
                            |> ignore)
                        |> ignore)
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()

[<Tests>]
let validationTests =
    testList "Frank.Validation" [
        testTask "AT1: valid JSON-LD body passes through" {
            let client = createServer ()
            let body = """{"@type": "https://schema.org/Order", "https://schema.org/totalPaymentDue": 100}"""
            let content = new StringContent(body, Encoding.UTF8, "application/ld+json")
            let! (response: HttpResponseMessage) = client.PostAsync("/orders", content)
            Expect.equal response.StatusCode HttpStatusCode.OK "should pass validation"
        }

        testTask "AT2: invalid datatype returns 422" {
            let client = createServer ()
            let body = """{"@type": "https://schema.org/Order", "https://schema.org/totalPaymentDue": "not-a-number"}"""
            let content = new StringContent(body, Encoding.UTF8, "application/ld+json")
            let! (response: HttpResponseMessage) = client.PostAsync("/orders", content)
            Expect.equal response.StatusCode HttpStatusCode.UnprocessableEntity "should return 422"
        }

        testTask "AT3: missing required field returns 422" {
            let client = createServer ()
            let body = """{"@type": "https://schema.org/Order"}"""
            let content = new StringContent(body, Encoding.UTF8, "application/ld+json")
            let! (response: HttpResponseMessage) = client.PostAsync("/orders", content)
            Expect.equal response.StatusCode HttpStatusCode.UnprocessableEntity "should return 422 for missing field"
        }

        testTask "AT2b: response body cites vocabulary IRI not urn:frank:" {
            let client = createServer ()
            let body = """{"@type": "https://schema.org/Order", "https://schema.org/totalPaymentDue": "not-a-number"}"""
            let content = new StringContent(body, Encoding.UTF8, "application/ld+json")
            let! (response: HttpResponseMessage) = client.PostAsync("/orders", content)
            let! (responseBody: string) = response.Content.ReadAsStringAsync()
            Expect.isFalse (responseBody.Contains("urn:frank:")) "report must not contain urn:frank: URIs"
        }

        testTask "AT4: non-JSON-LD request passes through without validation" {
            let client = createServer ()
            let content = new StringContent("some text", Encoding.UTF8, "text/plain")
            let! (response: HttpResponseMessage) = client.PostAsync("/orders", content)
            Expect.equal response.StatusCode HttpStatusCode.OK "text/plain should pass through"
        }

        testTask "AT5: empty ShapesGraph passes all JSON-LD (useValidation fallback behavior)" {
            // useValidation with no GeneratedValidation present falls back to empty ShapesGraph
            // Empty ShapesGraph has no constraints → report.Conforms = true → all inputs pass
            let shapesGraph = new ShapesGraph(new VDS.RDF.Graph())
            let builder = WebApplication.CreateBuilder([||])
            builder.WebHost.UseTestServer() |> ignore
            builder.Services.AddRouting() |> ignore
            builder.Services.AddSingleton<ShapesGraph>(shapesGraph) |> ignore
            let app = builder.Build()
            (app :> IApplicationBuilder).UseRouting() |> ignore
            app.UseMiddleware<ValidationMiddleware>() |> ignore

            app.UseEndpoints(fun endpoints ->
                endpoints.MapPost(
                    "/orders",
                    RequestDelegate(fun ctx ->
                        ctx.Response.StatusCode <- 200
                        ctx.Response.WriteAsync("ok")))
                |> ignore)
            |> ignore

            app.Start()
            let client = app.GetTestClient()
            let body = """{"@type": "https://schema.org/AnyType", "https://schema.org/anyProp": "anyValue"}"""
            let content = new StringContent(body, Encoding.UTF8, "application/ld+json")
            let! (response: HttpResponseMessage) = client.PostAsync("/orders", content)
            Expect.equal response.StatusCode HttpStatusCode.OK "empty ShapesGraph should pass all JSON-LD (useValidation fallback)"
        }
    ]
