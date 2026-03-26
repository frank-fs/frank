module Frank.Statecharts.Tests.Affordances.DiscoveryCombinationTests

open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Affordances
open Frank.Statecharts.Tests.Affordances.OptionsDiscoveryTests

/// Build a test server from a WebHostSpec, replicating the CE Run pipeline with TestServer.
let withCeServer (spec: WebHostSpec) (f: HttpClient -> Task) =
    task {
        let builder = WebApplication.CreateBuilder([||])
        builder.WebHost.UseTestServer() |> ignore
        spec.Services(builder.Services) |> ignore
        let app = builder.Build()
        let dataSource = TestEndpointDataSource(spec.Endpoints)
        (app :> IApplicationBuilder)
        |> spec.BeforeRoutingMiddleware
        |> fun app -> app.UseRouting()
        |> spec.Middleware
        |> ignore
        (app :> IEndpointRouteBuilder).DataSources.Add(dataSource)
        app.Start()

        let client = app.GetTestClient()

        try
            do! f client
        finally
            client.Dispose()
            (app :> System.IDisposable).Dispose()
    }
    :> Task

// -- Helpers --

/// Build a resource at /items with GET handler and discoveryMediaType metadata.
let private itemsResourceWithMedia () =
    resource "/items" {
        get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items list")))
        discoveryMediaType "application/json" "self"
    }

/// Build a resource at /items with GET handler but no discoveryMediaType metadata.
let private itemsResourcePlain () =
    resource "/items" {
        get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items list")))
    }

/// Build a resource at / with GET handler.
let private rootResource () =
    resource "/" {
        get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("root handler")))
    }

/// Build a spec with specific CE operations applied, then run a test against that server.
let private withSpec
    (configure: WebHostBuilder -> WebHostSpec -> WebHostSpec)
    (res: Resource)
    (f: HttpClient -> Task)
    =
    let ceBuilder = WebHostBuilder([||])
    let spec =
        ceBuilder.Yield()
        |> configure ceBuilder
        |> fun s -> ceBuilder.Resource(s, res)
    withCeServer spec f

// -- A. Individual isolation --

[<Tests>]
let isolationTests =
    testList "A. Individual isolation" [
        testTask "useOptionsDiscovery alone handles OPTIONS" {
            do! withSpec (fun b s -> b.UseOptionsDiscovery(s)) (itemsResourceWithMedia ()) (fun client -> task {
                let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/items"))
                Expect.equal resp.StatusCode HttpStatusCode.NoContent "OPTIONS should return 204"
                Expect.isGreaterThan (resp.Content.Headers.Allow.Count) 0 "should have Allow header"
            })
        }

        testTask "useOptionsDiscovery alone does not add Link headers" {
            do! withSpec (fun b s -> b.UseOptionsDiscovery(s)) (itemsResourceWithMedia ()) (fun client -> task {
                let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                Expect.isFalse (resp.Headers.Contains("Link")) "should not have Link header from OPTIONS-only middleware"
            })
        }

        testTask "useLinkHeaders alone adds Link headers to GET" {
            do! withSpec (fun b s -> b.UseLinkHeaders(s)) (itemsResourceWithMedia ()) (fun client -> task {
                let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                Expect.equal resp.StatusCode HttpStatusCode.OK "GET should return 200"
                Expect.isTrue (resp.Headers.Contains("Link")) "should have Link header"
            })
        }

        testTask "useLinkHeaders alone does not handle OPTIONS" {
            do! withSpec (fun b s -> b.UseLinkHeaders(s)) (itemsResourceWithMedia ()) (fun client -> task {
                let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/items"))
                // Without OptionsDiscoveryMiddleware, OPTIONS goes to the routing layer
                Expect.notEqual resp.StatusCode HttpStatusCode.OK "should not get 200 from discovery"
            })
        }

        testTask "useJsonHome alone serves home document" {
            do! withSpec (fun b s -> b.UseJsonHome(s)) (itemsResourcePlain ()) (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp.StatusCode HttpStatusCode.OK "should serve home document"
                Expect.equal (resp.Content.Headers.ContentType.MediaType) "application/json-home" "content type"
                Expect.isTrue (resp.Headers.Contains("Vary")) "should have Vary header"
            })
        }
    ]

// -- B. Pairwise combinations --

[<Tests>]
let pairwiseTests =
    testList "B. Pairwise combinations" [
        testTask "useOptionsDiscovery + useLinkHeaders = useDiscoveryHeaders behavior" {
            let res = itemsResourceWithMedia ()

            // Axis 1: Allow on OPTIONS
            do! withSpec (fun b s -> s |> b.UseOptionsDiscovery |> b.UseLinkHeaders) res (fun clientIndiv -> task {
                do! withSpec (fun b s -> b.UseDiscoveryHeaders(s)) res (fun clientBundle -> task {
                    let! (r1: HttpResponseMessage) = clientIndiv.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/items"))
                    let! (r2: HttpResponseMessage) = clientBundle.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/items"))
                    let allow1 = r1.Content.Headers.Allow |> Set.ofSeq
                    let allow2 = r2.Content.Headers.Allow |> Set.ofSeq
                    Expect.equal allow1 allow2 "Allow headers should match"

                    // Axis 2: Link on GET
                    let! (r3: HttpResponseMessage) = clientIndiv.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                    let! (r4: HttpResponseMessage) = clientBundle.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                    Expect.equal (r3.Headers.Contains("Link")) (r4.Headers.Contains("Link")) "Link header presence should match"

                    // Axis 3: pass-through on unmatched
                    let! (r5: HttpResponseMessage) = clientIndiv.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/nonexistent"))
                    let! (r6: HttpResponseMessage) = clientBundle.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/nonexistent"))
                    Expect.equal r5.StatusCode r6.StatusCode "unmatched status should match"
                })
            })
        }

        testTask "useOptionsDiscovery + useJsonHome work together" {
            do! withSpec (fun b s -> s |> b.UseJsonHome |> b.UseOptionsDiscovery) (itemsResourceWithMedia ()) (fun client -> task {
                // OPTIONS works
                let! (resp1: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/items"))
                Expect.equal resp1.StatusCode HttpStatusCode.NoContent "OPTIONS should work"
                Expect.isGreaterThan (resp1.Content.Headers.Allow.Count) 0 "should have Allow"

                // JSON Home works
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp2: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp2.StatusCode HttpStatusCode.OK "JSON Home should work"

                // Link headers absent (no LinkHeaderMiddleware)
                let! (resp3: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                Expect.isFalse (resp3.Headers.Contains("Link")) "Link headers should be absent"
            })
        }

        testTask "useLinkHeaders + useJsonHome work together" {
            do! withSpec (fun b s -> s |> b.UseJsonHome |> b.UseLinkHeaders) (itemsResourceWithMedia ()) (fun client -> task {
                // Link headers on GET
                let! (resp1: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                Expect.isTrue (resp1.Headers.Contains("Link")) "should have Link header"

                // JSON Home works
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp2: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp2.StatusCode HttpStatusCode.OK "JSON Home should work"
            })
        }
    ]

// -- C. Full bundle --

[<Tests>]
let bundleTests =
    testList "C. Full bundle" [
        testTask "useDiscoveryHeaders provides OPTIONS and Link but not JSON Home" {
            do! withSpec (fun b s -> b.UseDiscoveryHeaders(s)) (itemsResourceWithMedia ()) (fun client -> task {
                // OPTIONS works
                let! (resp1: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/items"))
                Expect.equal resp1.StatusCode HttpStatusCode.NoContent "OPTIONS should work"

                // Link works
                let! (resp2: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                Expect.isTrue (resp2.Headers.Contains("Link")) "should have Link header"

                // JSON Home NOT available
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp3: HttpResponseMessage) = client.SendAsync(req)
                Expect.notEqual resp3.StatusCode HttpStatusCode.OK "should not serve JSON Home"
            })
        }

        testTask "useDiscovery provides OPTIONS, Link headers, and JSON Home" {
            do! withSpec (fun b s -> b.UseDiscovery(s)) (itemsResourceWithMedia ()) (fun client -> task {
                // OPTIONS works
                let! (resp1: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/items"))
                Expect.equal resp1.StatusCode HttpStatusCode.NoContent "OPTIONS should work"
                Expect.isGreaterThan (resp1.Content.Headers.Allow.Count) 0 "should have Allow"

                // Link headers work
                let! (resp2: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                Expect.isTrue (resp2.Headers.Contains("Link")) "should have Link header"

                // JSON Home works
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp3: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp3.StatusCode HttpStatusCode.OK "JSON Home should work"
                Expect.isTrue (resp3.Headers.Contains("Vary")) "should have Vary header"
            })
        }
    ]

// -- D. Ordering independence --

[<Tests>]
let orderingTests =
    testList "D. Ordering independence" [
        testTask "useLinkHeaders before useOptionsDiscovery same as after" {
            let res = itemsResourceWithMedia ()

            // Order 1: Link then Options; Order 2: Options then Link — test both in nested servers
            do! withSpec (fun b s -> s |> b.UseLinkHeaders |> b.UseOptionsDiscovery) res (fun client1 -> task {
                do! withSpec (fun b s -> s |> b.UseOptionsDiscovery |> b.UseLinkHeaders) res (fun client2 -> task {
                    // Axis 1: Allow on OPTIONS
                    let! (r1: HttpResponseMessage) = client1.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/items"))
                    let! (r2: HttpResponseMessage) = client2.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/items"))
                    Expect.equal (r1.Content.Headers.Allow |> Set.ofSeq) (r2.Content.Headers.Allow |> Set.ofSeq) "Allow headers should match"

                    // Axis 2: Link on GET
                    let! (r3: HttpResponseMessage) = client1.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                    let! (r4: HttpResponseMessage) = client2.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                    Expect.equal (r3.Headers.Contains("Link")) (r4.Headers.Contains("Link")) "Link header presence should match"
                })
            })
        }
    ]

// -- E. Graceful degradation --

[<Tests>]
let degradationTests =
    testList "E. Graceful degradation" [
        testTask "OPTIONS with no resources passes through" {
            // Build server with no resources, just the middleware
            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseOptionsDiscovery(s)

            do! withCeServer spec (fun client -> task {
                let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/anything"))
                Expect.notEqual resp.StatusCode HttpStatusCode.OK "should pass through (404)"
            })
        }

        testTask "Link headers with no DiscoveryMediaType metadata adds nothing" {
            do! withSpec (fun b s -> b.UseLinkHeaders(s)) (itemsResourcePlain ()) (fun client -> task {
                let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                Expect.equal resp.StatusCode HttpStatusCode.OK "GET should return 200"
                Expect.isFalse (resp.Headers.Contains("Link")) "should not have Link header without metadata"
            })
        }

        testTask "JSON Home with no resources returns valid empty document" {
            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseJsonHome(s)

            do! withCeServer spec (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("application/json-home"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                Expect.equal resp.StatusCode HttpStatusCode.OK "should return 200"
                Expect.equal (resp.Content.Headers.ContentType.MediaType) "application/json-home" "content type"
                Expect.isTrue (resp.Headers.Contains("Vary")) "should have Vary header"
                let! (body: string) = resp.Content.ReadAsStringAsync()
                Expect.isTrue (body.Contains("\"resources\"")) "should have resources key"
            })
        }
    ]

// -- F. Accept header / cross-mechanism interaction --

[<Tests>]
let interactionTests =
    testList "F. Accept header interaction" [
        testTask "JSON Home at root with wrong Accept passes through" {
            do! withSpec (fun b s -> b.UseDiscovery(s)) (itemsResourcePlain ()) (fun client -> task {
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("text/html"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                // Middleware should not intercept — wrong Accept type
                let contentType =
                    if isNull resp.Content.Headers.ContentType then ""
                    else resp.Content.Headers.ContentType.MediaType
                Expect.notEqual contentType "application/json-home" "should not be json-home"
                Expect.isFalse (resp.Headers.Contains("Vary")) "should not have Vary from json-home middleware"
            })
        }

        testTask "OPTIONS at root passes through when no user endpoint at root" {
            do! withSpec (fun b s -> b.UseDiscovery(s)) (itemsResourceWithMedia ()) (fun client -> task {
                // No user-defined resource at /, JSON Home only intercepts GET/HEAD
                let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Options, "/"))
                // OptionsDiscoveryMiddleware scans EndpointDataSource — no endpoint at /
                Expect.notEqual resp.StatusCode HttpStatusCode.NoContent "OPTIONS at root should pass through"
            })
        }

        testTask "user-defined GET / handler responds normally under useDiscovery" {
            // Build server with useDiscovery AND a user resource at /
            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseDiscovery(s)
                |> fun s -> ceBuilder.Resource(s, rootResource ())

            do! withCeServer spec (fun client -> task {
                // GET / without json-home Accept should get user handler, not JSON Home
                let req = new HttpRequestMessage(HttpMethod.Get, "/")
                req.Headers.Accept.Add(MediaTypeWithQualityHeaderValue("text/html"))
                let! (resp: HttpResponseMessage) = client.SendAsync(req)
                let! (body: string) = resp.Content.ReadAsStringAsync()
                Expect.equal body "root handler" "should get user handler response"
            })
        }
    ]
