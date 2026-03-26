module Frank.Statecharts.Tests.Affordances.LinkHeaderTests

open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Affordances
open Frank.Statecharts.Tests.Affordances.OptionsDiscoveryTests

/// Runs a test against a server with the Link header middleware enabled.
let withLinkServer resources f =
    withTestHost (fun app -> app.UseMiddleware<LinkHeaderMiddleware>() |> ignore) resources f

// ===== US2: Link Headers on GET Responses =====

[<Tests>]
let us2Tests =
    testList "US2 - Link Headers on GET Responses" [
        testTask "GET request to resource with DiscoveryMediaType metadata returns Link headers" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    discoveryMediaType "application/ld+json" "describedby"
                    discoveryMediaType "text/turtle" "describedby"
                    discoveryMediaType "application/rdf+xml" "describedby"
                }

            do! withLinkServer [ itemsResource ] (fun client -> task {
                let! (response: HttpResponseMessage) = client.GetAsync("/items")

                Expect.equal response.StatusCode HttpStatusCode.OK "GET should return 200"

                let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList
                Expect.hasLength linkHeaders 3 "Should have 3 Link headers"

                let allLinks = linkHeaders |> String.concat " "
                Expect.stringContains allLinks "</items>; rel=\"describedby\"; type=\"application/ld+json\""
                    "Link header should contain application/ld+json"
                Expect.stringContains allLinks "</items>; rel=\"describedby\"; type=\"text/turtle\""
                    "Link header should contain text/turtle"
                Expect.stringContains allLinks "</items>; rel=\"describedby\"; type=\"application/rdf+xml\""
                    "Link header should contain application/rdf+xml"

                let! body = response.Content.ReadAsStringAsync()
                Expect.equal body "items" "Normal response body should be present"
            })
        }

        testTask "GET request to resource with no semantic markers returns no Link headers" {
            let healthResource =
                resource "/health" {
                    name "Health"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("healthy")))
                }

            do! withLinkServer [ healthResource ] (fun client -> task {
                let! (response: HttpResponseMessage) = client.GetAsync("/health")

                Expect.equal response.StatusCode HttpStatusCode.OK "GET should return 200"
                Expect.isFalse (response.Headers.Contains("Link")) "No Link headers should be present"
            })
        }
    ]

// ===== US3: Per-Resource Opt-In =====

[<Tests>]
let us3Tests =
    testList "US3 - Per-Resource Opt-In" [
        testTask "two resources, one with metadata one without, Link headers only on marked" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    discoveryMediaType "application/ld+json" "describedby"
                }

            let healthResource =
                resource "/health" {
                    name "Health"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("healthy")))
                }

            do! withLinkServer [ itemsResource; healthResource ] (fun client -> task {
                // GET /items should have Link headers
                let! (itemsResponse: HttpResponseMessage) = client.GetAsync("/items")
                Expect.equal itemsResponse.StatusCode HttpStatusCode.OK "/items should return 200"
                Expect.isTrue (itemsResponse.Headers.Contains("Link")) "/items should have Link headers"

                // GET /health should NOT have Link headers
                let! (healthResponse: HttpResponseMessage) = client.GetAsync("/health")
                Expect.equal healthResponse.StatusCode HttpStatusCode.OK "/health should return 200"
                Expect.isFalse (healthResponse.Headers.Contains("Link")) "/health should not have Link headers"
            })
        }
    ]

// ===== US4: Global Enablement =====

[<Tests>]
let us4Tests =
    testList "US4 - Global Enablement" [
        testTask "global enablement with multiple resources, Link headers only on marked" {
            let linkedDataResource =
                resource "/data" {
                    name "LinkedData"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("data")))
                    discoveryMediaType "application/ld+json" "describedby"
                    discoveryMediaType "text/turtle" "describedby"
                }

            let statechartsResource =
                resource "/machines" {
                    name "Statecharts"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("machines")))
                    discoveryMediaType "application/scxml+xml" "describedby"
                }

            let plainResource =
                resource "/plain" {
                    name "Plain"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("plain")))
                }

            do! withLinkServer [ linkedDataResource; statechartsResource; plainResource ] (fun client -> task {
                // /data should have 2 Link headers
                let! (dataResponse: HttpResponseMessage) = client.GetAsync("/data")
                Expect.equal dataResponse.StatusCode HttpStatusCode.OK "/data should return 200"
                let dataLinks = dataResponse.Headers.GetValues("Link") |> Seq.toList
                Expect.hasLength dataLinks 2 "/data should have 2 Link headers"

                // /machines should have 1 Link header
                let! (machinesResponse: HttpResponseMessage) = client.GetAsync("/machines")
                Expect.equal machinesResponse.StatusCode HttpStatusCode.OK "/machines should return 200"
                let machinesLinks = machinesResponse.Headers.GetValues("Link") |> Seq.toList
                Expect.hasLength machinesLinks 1 "/machines should have 1 Link header"

                // /plain should have NO Link headers
                let! (plainResponse: HttpResponseMessage) = client.GetAsync("/plain")
                Expect.equal plainResponse.StatusCode HttpStatusCode.OK "/plain should return 200"
                Expect.isFalse (plainResponse.Headers.Contains("Link")) "/plain should not have Link headers"
            })
        }
    ]

// ===== Additional Behavioral Tests =====

[<Tests>]
let behavioralTests =
    testList "Link Header Behavioral Tests" [
        testTask "no Link headers on error responses" {
            let errorResource =
                resource "/error" {
                    name "Error"
                    get (RequestDelegate(fun ctx ->
                        ctx.Response.StatusCode <- 404
                        ctx.Response.WriteAsync("not found")))
                    discoveryMediaType "application/ld+json" "describedby"
                }

            do! withLinkServer [ errorResource ] (fun client -> task {
                let! (response: HttpResponseMessage) = client.GetAsync("/error")

                Expect.equal response.StatusCode HttpStatusCode.NotFound "Should return 404"
                Expect.isFalse (response.Headers.Contains("Link"))
                    "No Link headers should be present on error responses"
            })
        }

        testTask "no Link headers on POST requests" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    post (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("created")))
                    discoveryMediaType "application/ld+json" "describedby"
                }

            do! withLinkServer [ itemsResource ] (fun client -> task {
                let! (response: HttpResponseMessage) = client.PostAsync("/items", new StringContent("data"))

                Expect.isFalse (response.Headers.Contains("Link"))
                    "No Link headers should be present on POST responses"
            })
        }

        testTask "Link headers on HEAD requests" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    head (RequestDelegate(fun ctx ->
                        ctx.Response.StatusCode <- 200
                        Task.CompletedTask))
                    discoveryMediaType "application/ld+json" "describedby"
                }

            do! withLinkServer [ itemsResource ] (fun client -> task {
                let request = new HttpRequestMessage(HttpMethod.Head, "/items")
                let! (response: HttpResponseMessage) = client.SendAsync(request)

                // HEAD should get Link headers (same as GET, just no body)
                Expect.isTrue (response.Headers.Contains("Link"))
                    "Link headers should be present on HEAD responses"

                let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList
                Expect.hasLength linkHeaders 1 "Should have 1 Link header"
                Expect.stringContains (linkHeaders.[0]) "</items>; rel=\"describedby\"; type=\"application/ld+json\""
                    "Link header should match expected format"
            })
        }

        testTask "duplicate DiscoveryMediaType entries are deduplicated" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    discoveryMediaType "application/ld+json" "describedby"
                    discoveryMediaType "application/ld+json" "describedby"
                    discoveryMediaType "text/turtle" "describedby"
                }

            do! withLinkServer [ itemsResource ] (fun client -> task {
                let! (response: HttpResponseMessage) = client.GetAsync("/items")

                Expect.equal response.StatusCode HttpStatusCode.OK "GET should return 200"
                let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList
                Expect.hasLength linkHeaders 2 "Duplicate entries should be deduplicated to 2"
            })
        }
    ]
