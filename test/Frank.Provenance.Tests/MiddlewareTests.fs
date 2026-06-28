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
              Expect.stringContains body "prov:Activity" "Activity type present as CURIE"
              Expect.stringContains body "https://schema.org/OrderAction" "domain IRI from provClass"
              Expect.stringContains body "prov:Agent" "Agent present as CURIE"
              Expect.isFalse (body.Contains "urn:frank:") "no hardcoded urn:frank: activity IRI"
              Expect.isFalse (body.Contains "urn:provenance:agent:") "no opaque urn:provenance:agent: IRI"
              Expect.stringContains body "/agents/anonymous" "agent IRI uses HTTP /agents/ path"
              Expect.stringContains body "http:methodName" "W3C HTTP methodName term as CURIE"
              Expect.stringContains body "http:statusCodeValue" "W3C HTTP statusCodeValue term as CURIE"
              Expect.stringContains body "prov:used" "prov:used asserted as CURIE"
              Expect.stringContains body "localhost" "entity @id is absolute (contains host)"
          }

          testCaseAsync "records untyped Activity when no produces metadata (AT3)"
          <| async {
              use app = startProvenanceServer (orderProvConfig ())
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/no-produces")

              req.Headers.TryAddWithoutValidation(
                  "Accept",
                  "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
              )
              |> ignore

              let! (resp: HttpResponseMessage) = client.SendAsync(req) |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "passes through as 200"
              Expect.stringContains body "prov:Activity" "Activity recorded as prov:Activity CURIE"
              Expect.isFalse (body.Contains "https://schema.org/") "no domain-type IRI — untyped activity"
          }

          testCaseAsync
              "empty ProvClasses config: prov-profile request returns untyped prov:Activity, no crash (GAP 2b)"
          <| async {
              let emptyConfig: Frank.Provenance.ProvenanceConfig =
                  { ProvClasses = Map.empty
                    KnownNamespaces = [||]
                    StoreConfig = Frank.Provenance.ProvenanceStoreConfig.defaults }

              use app = startProvenanceServer emptyConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/no-produces")

              req.Headers.TryAddWithoutValidation(
                  "Accept",
                  "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
              )
              |> ignore

              let! (resp: HttpResponseMessage) = client.SendAsync(req) |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "200 — no crash with empty ProvClasses"
              Expect.stringContains body "prov:Activity" "prov:Activity present in untyped record"
              Expect.isFalse (body.Contains "https://schema.org/") "no domain IRI when ProvClasses is empty"
          }

          testCaseAsync "non-prov response carries Vary: Accept and Link: has_provenance (fix #8/#9)"
          <| async {
              use app = startProvenanceServer (orderProvConfig ())
              use client = app.GetTestClient()
              let! (resp: HttpResponseMessage) = client.GetAsync("/no-produces") |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "passes through"

              let varyValues = resp.Headers.GetValues("Vary") |> Seq.toList

              Expect.isTrue
                  (varyValues |> List.exists (fun v -> v.Contains "Accept"))
                  "Vary: Accept present on pass-through"

              let linkValues = resp.Headers.GetValues("Link") |> Seq.toList

              Expect.isTrue
                  (linkValues
                   |> List.exists (fun v -> v.Contains "http://www.w3.org/ns/prov#has_provenance"))
                  "Link: has_provenance rel present on pass-through"
          } ]
