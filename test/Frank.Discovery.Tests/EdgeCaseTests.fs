module Frank.Discovery.Tests.EdgeCaseTests

open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.Discovery

// Reuse helpers from OptionsDiscoveryTests
open Frank.Discovery.Tests.OptionsDiscoveryTests

[<Tests>]
let edgeCaseTests =
    testList "Edge Cases" [
        testTask "CORS preflight passes through - discovery middleware does not intercept" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    discoveryMediaType "application/ld+json" "describedby"
                }

            do! withDiscoveryServer [ itemsResource ] (fun client -> task {
                let request = new HttpRequestMessage(HttpMethod.Options, "/items")
                request.Headers.Add("Origin", "http://example.com")
                request.Headers.Add("Access-Control-Request-Method", "GET")
                let! (response: HttpResponseMessage) = client.SendAsync(request)

                // The discovery middleware should pass through for CORS preflights.
                // Our middleware always returns 200; if it passed through, ASP.NET Core
                // routing handles the request (typically 405 for method not allowed).
                Expect.notEqual response.StatusCode HttpStatusCode.OK
                    "CORS preflight should not be handled by discovery middleware (not 200)"

                // No Link header should be present from the discovery middleware
                let hasLink = response.Headers.Contains("Link")
                Expect.isFalse hasLink
                    "CORS preflight should not produce Link headers from discovery middleware"
            })
        }

        testTask "explicit OPTIONS handler takes precedence over discovery middleware" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("get")))
                    options (RequestDelegate(fun ctx ->
                        ctx.Response.StatusCode <- 200
                        ctx.Response.WriteAsync("custom OPTIONS")))
                    discoveryMediaType "application/ld+json" "describedby"
                }

            do! withDiscoveryServer [ itemsResource ] (fun client -> task {
                let request = new HttpRequestMessage(HttpMethod.Options, "/items")
                let! (response: HttpResponseMessage) = client.SendAsync(request)

                let! body = response.Content.ReadAsStringAsync()
                Expect.equal body "custom OPTIONS"
                    "Explicit OPTIONS handler should take precedence over discovery middleware"
            })
        }

        testTask "duplicate DiscoveryMediaType entries are deduplicated in OPTIONS response" {
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    discoveryMediaType "application/ld+json" "describedby"
                    discoveryMediaType "application/ld+json" "describedby"
                    discoveryMediaType "text/turtle" "describedby"
                }

            do! withDiscoveryServer [ itemsResource ] (fun client -> task {
                let request = new HttpRequestMessage(HttpMethod.Options, "/items")
                let! (response: HttpResponseMessage) = client.SendAsync(request)

                Expect.equal response.StatusCode HttpStatusCode.OK "OPTIONS should return 200"

                // Verify deduplication: application/ld+json should appear only once
                let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList
                let jsonLdLinks =
                    linkHeaders
                    |> List.filter (fun h -> h.Contains("application/ld+json"))
                Expect.equal (List.length jsonLdLinks) 1
                    "application/ld+json should appear only once after deduplication"

                // text/turtle should still be present
                let turtleLinks =
                    linkHeaders
                    |> List.filter (fun h -> h.Contains("text/turtle"))
                Expect.equal (List.length turtleLinks) 1
                    "text/turtle should be present"
            })
        }

        testTask "HEAD request does not trigger OPTIONS discovery middleware" {
            // The OPTIONS discovery middleware only intercepts OPTIONS requests.
            // HEAD requests are separate and should not trigger OPTIONS discovery.
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                    discoveryMediaType "application/ld+json" "describedby"
                }

            do! withDiscoveryServer [ itemsResource ] (fun client -> task {
                let request = new HttpRequestMessage(HttpMethod.Head, "/items")
                let! (response: HttpResponseMessage) = client.SendAsync(request)

                // HEAD request should not trigger the OPTIONS discovery middleware.
                // No Link header from the discovery middleware should be present.
                let hasLink = response.Headers.Contains("Link")
                Expect.isFalse hasLink
                    "HEAD request should not produce Link headers from OPTIONS discovery middleware"

                // Response body should be empty (HEAD semantics)
                let! body = response.Content.ReadAsStringAsync()
                Expect.equal body ""
                    "HEAD response body should be empty"
            })
        }

        testTask "no Link headers on error responses from OPTIONS discovery" {
            // When a resource returns an error (e.g. 404), the OPTIONS discovery
            // middleware should still work because OPTIONS is handled by the middleware
            // itself, not the handler. The handler returning 404 is irrelevant for OPTIONS.
            // This test verifies that a GET handler returning 404 does not affect OPTIONS.
            let itemsResource =
                resource "/items" {
                    name "Items"
                    get (RequestDelegate(fun ctx ->
                        ctx.Response.StatusCode <- 404
                        ctx.Response.WriteAsync("not found")))
                    discoveryMediaType "application/ld+json" "describedby"
                }

            do! withDiscoveryServer [ itemsResource ] (fun client -> task {
                // First, verify GET returns 404
                let getRequest = new HttpRequestMessage(HttpMethod.Get, "/items")
                let! (getResponse: HttpResponseMessage) = client.SendAsync(getRequest)
                Expect.equal getResponse.StatusCode HttpStatusCode.NotFound
                    "GET should return 404"

                // OPTIONS should still return 200 with Link headers (it's middleware-generated)
                let optionsRequest = new HttpRequestMessage(HttpMethod.Options, "/items")
                let! (optionsResponse: HttpResponseMessage) = client.SendAsync(optionsRequest)
                Expect.equal optionsResponse.StatusCode HttpStatusCode.OK
                    "OPTIONS should return 200 regardless of handler behavior"

                let linkHeaders = optionsResponse.Headers.GetValues("Link") |> Seq.toList
                Expect.isNonEmpty linkHeaders
                    "OPTIONS should include Link headers even when GET handler returns errors"
            })
        }

        testTask "multiple extensions contributing media types all appear in OPTIONS response" {
            // Simulate a resource with media types from both LinkedData and Statecharts
            let workflowResource =
                resource "/workflow" {
                    name "Workflow"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("workflow")))
                    discoveryMediaType "application/ld+json" "describedby"
                    discoveryMediaType "text/turtle" "describedby"
                    discoveryMediaType "application/rdf+xml" "describedby"
                    discoveryMediaType "application/scxml+xml" "describedby"
                }

            do! withDiscoveryServer [ workflowResource ] (fun client -> task {
                let request = new HttpRequestMessage(HttpMethod.Options, "/workflow")
                let! (response: HttpResponseMessage) = client.SendAsync(request)

                Expect.equal response.StatusCode HttpStatusCode.OK "OPTIONS should return 200"

                let linkHeaders = response.Headers.GetValues("Link") |> Seq.toList

                // All four media types should appear
                let linkValue = linkHeaders |> String.concat " "
                Expect.stringContains linkValue "application/ld+json"
                    "Should include LinkedData JSON-LD media type"
                Expect.stringContains linkValue "text/turtle"
                    "Should include LinkedData Turtle media type"
                Expect.stringContains linkValue "application/rdf+xml"
                    "Should include LinkedData RDF/XML media type"
                Expect.stringContains linkValue "application/scxml+xml"
                    "Should include Statecharts SCXML media type"

                // All should have rel=describedby
                for link in linkHeaders do
                    Expect.stringContains link "rel=\"describedby\""
                        "Each Link header should have rel=describedby"
            })
        }

        testTask "resource with no DiscoveryMediaType returns OPTIONS with Allow but no Link headers" {
            let plainResource =
                resource "/plain" {
                    name "Plain"
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("plain")))
                    post (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("created")))
                }

            do! withDiscoveryServer [ plainResource ] (fun client -> task {
                let request = new HttpRequestMessage(HttpMethod.Options, "/plain")
                let! (response: HttpResponseMessage) = client.SendAsync(request)

                Expect.equal response.StatusCode HttpStatusCode.OK "OPTIONS should return 200"

                let allowHeader = response.Content.Headers.Allow |> Set.ofSeq
                Expect.contains allowHeader "GET" "Allow should contain GET"
                Expect.contains allowHeader "POST" "Allow should contain POST"
                Expect.contains allowHeader "OPTIONS" "Allow should contain OPTIONS"

                // No Link headers since no DiscoveryMediaType metadata
                let hasLink = response.Headers.Contains("Link")
                Expect.isFalse hasLink "No Link headers without DiscoveryMediaType metadata"
            })
        }
    ]
