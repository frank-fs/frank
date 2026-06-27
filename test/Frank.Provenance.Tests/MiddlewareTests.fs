module Frank.Provenance.Tests.MiddlewareTests

open System.Net.Http
open Microsoft.AspNetCore.TestHost
open Expecto
open Frank.Provenance.Tests.MiddlewareTestHelpers

[<Tests>]
let tests =
    testList
        "ProvenanceMiddleware E2E"
        [ testCaseAsync "POST with prov profile returns typed prov:Activity (AC #3)"
          <| async {
              use app = startProvenanceServer (orderProvConfig ())
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Post, "/orders")

              req.Headers.TryAddWithoutValidation(
                  "Accept",
                  "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
              )
              |> ignore

              let! (resp: HttpResponseMessage) = client.SendAsync(req) |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.stringContains body "http://www.w3.org/ns/prov#Activity" "Activity type present"
              Expect.stringContains body "https://schema.org/OrderAction" "domain IRI from provClass"
              Expect.stringContains body "http://www.w3.org/ns/prov#Agent" "Agent present"
              Expect.isFalse (body.Contains "urn:frank:") "no hardcoded urn:frank: activity IRI"
              Expect.isFalse (body.Contains "urn:provenance:agent:") "no opaque urn:provenance:agent: IRI"
              Expect.stringContains body "/agents/anonymous" "agent IRI uses HTTP /agents/ path"
              Expect.stringContains body "http://www.w3.org/2011/http#methodName" "W3C HTTP methodName term"
              Expect.stringContains body "http://www.w3.org/2011/http#statusCodeValue" "W3C HTTP statusCodeValue term"
              Expect.stringContains body "http://www.w3.org/ns/prov#used" "prov:used asserted"
              Expect.stringContains body "localhost" "entity @id is absolute (contains host)"
          }

          testCaseAsync "records untyped Activity when no produces metadata"
          <| async {
              use app = startProvenanceServer (orderProvConfig ())
              use client = app.GetTestClient()
              let! (resp: HttpResponseMessage) = client.GetAsync("/no-produces") |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "passes through"
          } ]
