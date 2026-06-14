module Frank.Discovery.Tests.MiddlewareTests

open System.Net.Http
open Microsoft.AspNetCore.TestHost
open Expecto
open Frank.Discovery.Tests.TestHelpers

[<Tests>]
let tests =
    testList
        "DiscoveryMiddleware (TestServer)"
        [ testCase "OPTIONS yields Allow and Link rel=describedby"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Options, "/games/abc")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.isNonEmpty (allowValues resp) "Allow header present"
              let links = linkValues resp

              Expect.isTrue
                  (links |> List.exists (fun l -> l.Contains "rel=\"describedby\""))
                  "describedby Link present"

              Expect.isTrue (links |> List.exists (fun l -> l.Contains "/alps/test")) "profile URI in describedby Link"

          testCase "GET profile URI serves ALPS with schema.org IRIs"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/alps/test").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"
              Expect.equal (resp.Content.Headers.ContentType.MediaType) "application/alps+json" "alps+json content type"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "https://schema.org/Game" "schema IRI in ALPS"
              Expect.isFalse (body.Contains "urn:frank:") "no urn:frank: in ALPS"

          testCase "GET / with json-home Accept serves the resource directory"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.stringContains body "resources" "JSON Home resources key"
              Expect.stringContains body "https://schema.org/Game" "vocabulary rel present" ]
