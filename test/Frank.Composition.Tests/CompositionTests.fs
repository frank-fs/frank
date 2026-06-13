module Frank.Composition.Tests.CompositionTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Expecto
open VDS.RDF
open VDS.RDF.Shacl
open Frank.Discovery
open Frank.Discovery.DiscoveryMiddleware
open Frank.LinkedData.LinkedDataMiddleware
open Frank.Validation.ValidationMiddleware
open Frank.Provenance.ProvenanceMiddleware

let private makeShapesGraph (fieldIri: string) (typeIri: string) : ShapesGraph =
    let sh = "http://www.w3.org/ns/shacl#"
    let xsd = "http://www.w3.org/2001/XMLSchema#"
    let rdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"
    let g = new Graph()
    let uri s = g.CreateUriNode(UriFactory.Create(s))
    let litInt s = g.CreateLiteralNode(s, UriFactory.Create(xsd + "integer"))
    let shapeNode = uri "urn:test:Shape"
    let propNode = uri "urn:test:PropShape"
    g.Assert(Triple(shapeNode, uri rdfType, uri (sh + "NodeShape"))) |> ignore
    g.Assert(Triple(shapeNode, uri (sh + "targetClass"), uri typeIri)) |> ignore
    g.Assert(Triple(shapeNode, uri (sh + "property"), propNode)) |> ignore
    g.Assert(Triple(propNode, uri rdfType, uri (sh + "PropertyShape"))) |> ignore
    g.Assert(Triple(propNode, uri (sh + "path"), uri fieldIri)) |> ignore
    g.Assert(Triple(propNode, uri (sh + "datatype"), uri (xsd + "integer"))) |> ignore
    g.Assert(Triple(propNode, uri (sh + "minCount"), litInt "1")) |> ignore
    new ShapesGraph(g)

let private makeGraph (typeIri: string) (fieldIri: string) : IGraph =
    let g = new Graph()
    let uri s = g.CreateUriNode(UriFactory.Create(s))
    let owl = "http://www.w3.org/2002/07/owl#"
    let rdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"
    let frankType = "urn:frank:type:Order"
    g.Assert(Triple(uri frankType, uri rdfType, uri (owl + "Class"))) |> ignore
    g.Assert(Triple(uri frankType, uri (owl + "equivalentClass"), uri typeIri)) |> ignore
    g :> IGraph

/// Build a test server with all four packages configured with a consistent vocabulary IRI.
let private createComposedServer (fieldIri: string) (typeIri: string) : HttpClient =
    let shapesGraph = makeShapesGraph fieldIri typeIri

    let graph = makeGraph typeIri fieldIri
    let context = """{"@context": ["https://schema.org/"]}"""

    let discoveryConfig: DiscoveryConfig =
        { ProfileBaseUri = "/alps"
          HomeRoute = "/"
          AlpsDescriptors =
            Map.ofList
              [ "MyApp.Order",
                [ { Id = "Order"; Type = "semantic"; Doc = None; Href = Some typeIri }
                  { Id = "totalPaymentDue"; Type = "semantic"; Doc = None; Href = Some fieldIri } ] ]
          DescribedByLinks =
            Map.ofList [ "MyApp.Order", [ $"<{typeIri}>; rel=\"describedby\"" ] ] }

    let provenanceConfig = ProvenanceConfig.Default

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddSingleton<ShapesGraph>(shapesGraph) |> ignore
    builder.Services.AddSingleton<LinkedDataConfig>({ Graph = graph; JsonLdContext = context }) |> ignore
    builder.Services.AddSingleton<DiscoveryConfig>(discoveryConfig) |> ignore
    builder.Services.AddSingleton<ProvenanceConfig>(provenanceConfig) |> ignore
    builder.Services.AddSingleton<Frank.Provenance.ProvenanceStore.ProvenanceStore>(provenanceConfig.Store) |> ignore

    let app = builder.Build()
    (app :> IApplicationBuilder).UseRouting() |> ignore
    app.UseMiddleware<ValidationMiddleware>() |> ignore
    app.UseMiddleware<LinkedDataMiddleware>() |> ignore
    app.UseMiddleware<ProvenanceMiddleware>() |> ignore
    app.UseMiddleware<OptionsEnricherMiddleware>() |> ignore

    app.MapGet(
        "/alps/{slug}",
        Func<HttpContext, Task>(fun ctx ->
            task {
                ctx.Response.ContentType <- "application/alps+json"
                do! ctx.Response.WriteAsync(AlpsSerializer.serialize discoveryConfig.AlpsDescriptors)
            }
            :> Task)
    )
    |> ignore

    app.MapGet(
        "/",
        Func<HttpContext, Task>(fun ctx ->
            task {
                let acceptHeader =
                    if ctx.Request.Headers.ContainsKey("Accept") then
                        ctx.Request.Headers["Accept"].ToString()
                    else
                        ""

                if acceptHeader.Contains("application/json-home") then
                    ctx.Response.ContentType <- "application/json-home+json"
                    let resources =
                        discoveryConfig.DescribedByLinks
                        |> Map.toList
                        |> List.map (fun (typeName, links) ->
                            let rel =
                                match links with
                                | link :: _ ->
                                    let s = link.IndexOf('<') + 1
                                    let e = link.IndexOf('>')
                                    if s > 0 && e > s then link.[s..e-1] else typeName
                                | [] -> typeName
                            { JsonHomeSerializer.Relation = rel
                              JsonHomeSerializer.Href = "/orders/{orderId}"
                              JsonHomeSerializer.Allow = [ "GET"; "HEAD"; "POST" ] })
                    do! ctx.Response.WriteAsync(JsonHomeSerializer.serialize resources)
                else
                    do! ctx.Response.WriteAsync("Frank")
            }
            :> Task)
    )
    |> ignore

    app.MapGet(
        "/orders/{id}",
        Func<HttpContext, Task>(fun ctx ->
            task {
                ctx.Response.StatusCode <- 200
                do! ctx.Response.WriteAsync("Order")
            }
            :> Task)
    )
    |> ignore

    app.MapPost(
        "/orders/{id}",
        Func<HttpContext, Task>(fun ctx ->
            task {
                ctx.Response.StatusCode <- 200
                do! ctx.Response.WriteAsync("Created")
            }
            :> Task)
    )
    |> ignore

    app.StartAsync() |> Async.AwaitTask |> Async.RunSynchronously
    app.GetTestClient()

[<Tests>]
let compositionTests =
    testList "Frank.Composition" [
        testTask "AT1: all four packages use the same IRI for schema:totalPaymentDue" {
            let fieldIri = "https://schema.org/totalPaymentDue"
            let typeIri = "https://schema.org/Order"
            let client = createComposedServer fieldIri typeIri

            // 1. Validation: POST invalid body → 422, report must not contain urn:frank:
            let invalidBody = sprintf """{"@type": "%s", "%s": "not-a-number"}""" typeIri fieldIri
            let content = new StringContent(invalidBody, Encoding.UTF8, "application/ld+json")
            let! (validationResponse: HttpResponseMessage) = client.PostAsync("/orders/o1", content)
            Expect.equal validationResponse.StatusCode HttpStatusCode.UnprocessableEntity "Validation: should return 422"
            let! (validationBody: string) = validationResponse.Content.ReadAsStringAsync()
            Expect.isFalse (validationBody.Contains("urn:frank:")) "Validation: report must not contain urn:frank:"

            // 2. LinkedData: GET ld+json → body references vocabulary
            let ldReq = new HttpRequestMessage(HttpMethod.Get, "/orders/o1")
            ldReq.Headers.Add("Accept", "application/ld+json")
            let! (ldResponse: HttpResponseMessage) = client.SendAsync(ldReq)
            Expect.equal ldResponse.StatusCode HttpStatusCode.OK "LinkedData: should return 200"
            let! (ldBody: string) = ldResponse.Content.ReadAsStringAsync()
            Expect.isTrue
                (ldBody.Contains("schema.org") || ldBody.Contains("equivalentClass"))
                "LinkedData: body should reference vocabulary"

            // 3. Discovery: GET /alps/orders → ALPS has fieldIri in href (no Accept header — avoid LinkedData 406)
            let! (alpsResponse: HttpResponseMessage) = client.GetAsync("/alps/orders")
            Expect.equal alpsResponse.StatusCode HttpStatusCode.OK "Discovery: ALPS should return 200"
            let! (alpsBody: string) = alpsResponse.Content.ReadAsStringAsync()
            Expect.isTrue (alpsBody.Contains(fieldIri)) $"Discovery: ALPS must contain {fieldIri}"

            // 4. OPTIONS → Link header has typeIri
            let optReq = new HttpRequestMessage(HttpMethod.Options, "/orders/o1")
            let! (optResponse: HttpResponseMessage) = client.SendAsync(optReq)
            let linkHeaders =
                try optResponse.Headers.GetValues("Link") |> Seq.toList
                with _ ->
                    try optResponse.Content.Headers.GetValues("Link") |> Seq.toList
                    with _ -> []
            Expect.isTrue
                (linkHeaders |> List.exists (fun h -> h.Contains(typeIri)))
                $"Discovery: OPTIONS Link header must contain {typeIri}"
        }

        testTask "AT2: IRI change propagates consistently — schema:price" {
            let fieldIri = "https://schema.org/price"
            let typeIri = "https://schema.org/Offer"
            let client = createComposedServer fieldIri typeIri

            // ALPS should reference schema:price (no Accept header — avoid LinkedData 406)
            let! (alpsResponse: HttpResponseMessage) = client.GetAsync("/alps/orders")
            let! (alpsBody: string) = alpsResponse.Content.ReadAsStringAsync()
            Expect.isTrue (alpsBody.Contains(fieldIri)) $"ALPS must contain {fieldIri}"
            Expect.isFalse (alpsBody.Contains("schema.org/totalPaymentDue")) "ALPS must NOT contain old IRI href"
        }

        testTask "AT3: packages compose without middleware conflicts" {
            let client = createComposedServer "https://schema.org/totalPaymentDue" "https://schema.org/Order"

            // Valid POST passes through Validation
            let validBody = """{"@type": "https://schema.org/Order", "https://schema.org/totalPaymentDue": 100}"""
            let content = new StringContent(validBody, Encoding.UTF8, "application/ld+json")
            let! (postResponse: HttpResponseMessage) = client.PostAsync("/orders/o1", content)
            Expect.equal postResponse.StatusCode HttpStatusCode.OK "Valid POST should return 200"

            // Normal GET passes through LinkedData
            let! (getResponse: HttpResponseMessage) = client.GetAsync("/orders/o1")
            Expect.equal getResponse.StatusCode HttpStatusCode.OK "GET without RDF Accept should return 200"

            // GET ld+json served by LinkedData
            let ldReq = new HttpRequestMessage(HttpMethod.Get, "/orders/o1")
            ldReq.Headers.Add("Accept", "application/ld+json")
            let! (ldResponse: HttpResponseMessage) = client.SendAsync(ldReq)
            Expect.equal ldResponse.StatusCode HttpStatusCode.OK "GET ld+json should return 200"
        }
    ]
