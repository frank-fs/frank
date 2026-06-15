module Frank.LinkedData.Tests.MiddlewareTests

open System.Net.Http
open Microsoft.AspNetCore.TestHost
open Expecto
open Frank.LinkedData.Tests.TestHelpers

[<Tests>]
let tests =
    testList
        "LinkedDataMiddleware (TestServer)"
        [ testCase "GET /data Accept:application/ld+json → 200, ld+json content-type"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/ld+json")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "status 200"
              Expect.equal resp.Content.Headers.ContentType.MediaType "application/ld+json" "ld+json content-type"

          testCase "GET /data Accept:application/ld+json body contains @context"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/ld+json")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "@context" "@context key present in JSON-LD"

          testCase "GET /data Accept:application/ld+json body references external vocab URI"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/ld+json")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "https://schema.org" "external vocab URI from config.JsonLdContext present"

          testCase "GET /data Accept:application/ld+json body surfaces seeAlso triple object"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/ld+json")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "schema.org/Game" "seeAlso triple object URI present in ld+json body"

          testCase "GET /data Accept:text/turtle → 200, text/turtle content-type"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "text/turtle")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "status 200"
              Expect.equal resp.Content.Headers.ContentType.MediaType "text/turtle" "text/turtle content-type"

          testCase "GET /data Accept:text/turtle body contains fixture triple IRI"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "text/turtle")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "schema.org" "vocabulary IRI present in Turtle"
              Expect.stringContains body "seeAlso" "seeAlso predicate present in Turtle"

          testCase "GET /data Accept:application/rdf+xml → 200, application/rdf+xml content-type"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/rdf+xml")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "status 200"
              Expect.equal resp.Content.Headers.ContentType.MediaType "application/rdf+xml" "rdf+xml content-type"

          testCase "GET /data Accept:application/rdf+xml body is valid RDF/XML"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/rdf+xml")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "rdf:RDF" "rdf:RDF root element present"
              Expect.stringContains body "schema.org" "schema.org IRI present in RDF/XML"

          testCase "GET /data Accept:application/xml (unsupported RDF type) → 406"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/xml")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 406 "406 Not Acceptable"

          testCase "GET /data Accept:application/xml 406 body lists available representations"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/xml")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "application/ld+json" "ld+json listed in 406 body"
              Expect.stringContains body "text/turtle" "turtle listed in 406 body"
              Expect.stringContains body "application/rdf+xml" "rdf+xml listed in 406 body"

          testCase "GET /data Accept:application/json (non-RDF) → passes through to downstream"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/json")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 from downstream"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "downstream" "downstream handler response"

          testCase "GET /data no Accept header → passes through to downstream"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              let (resp: HttpResponseMessage) = client.GetAsync("/data").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 from downstream" ]

[<Tests>]
let qvalueTests =
    testList
        "LinkedDataMiddleware q-value content negotiation"
        [ testCase "turtle;q=0.9 + ld+json;q=0.3 (turtle first) → Serve text/turtle (higher q wins)"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "text/turtle;q=0.9, application/ld+json;q=0.3")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "status 200"
              Expect.equal resp.Content.Headers.ContentType.MediaType "text/turtle" "turtle wins by q"

          testCase "ld+json;q=0.3 + turtle;q=0.9 (ld+json first) → Serve text/turtle (higher q wins)"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/ld+json;q=0.3, text/turtle;q=0.9")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "status 200"

              Expect.equal
                  resp.Content.Headers.ContentType.MediaType
                  "text/turtle"
                  "turtle wins by q regardless of header order"

          testCase "ld+json;q=0 alone → 406 (q=0 excludes; only RDF-scope type → NotAcceptable)"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/ld+json;q=0")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 406 "q=0 means not acceptable; RDF-scope with no match → 406"

          testCase "ld+json;q=0 + turtle → Serve text/turtle (q=0 excludes ld+json; turtle has implicit q=1)"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/ld+json;q=0, text/turtle")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "status 200"

              Expect.equal
                  resp.Content.Headers.ContentType.MediaType
                  "text/turtle"
                  "turtle served; ld+json excluded by q=0"

          testCase "*/* alone → PassThrough (wildcard does not serve RDF)"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "*/*")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "downstream 200"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "downstream" "*/* passes through to downstream handler"

          testCase "application/* alone → PassThrough (wildcard subtype does not serve RDF)"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/*")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "downstream 200"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "downstream" "application/* passes through to downstream handler"

          testCase "application/*;q=0 + turtle → Serve text/turtle (wildcard exclusion)"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/data")
              req.Headers.Add("Accept", "application/*;q=0, text/turtle")
              let (resp: HttpResponseMessage) = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "status 200"

              Expect.equal
                  resp.Content.Headers.ContentType.MediaType
                  "text/turtle"
                  "application/* q=0 excludes ld+json and rdf+xml; turtle served" ]
