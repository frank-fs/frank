module Frank.Provenance.Tests.MiddlewareTests

open System
open System.IO
open System.Net.Http
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Primitives
open Expecto
open Frank.Provenance
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
                    PropertyClassRanges = Map.empty
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

          testCaseAsync "POST with IRI-keyed JSON body emits attributes on prov:Activity node"
          <| async {
              use app = startProvenanceServer (orderProvConfig ())
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Post, "/orders")

              req.Headers.TryAddWithoutValidation(
                  "Accept",
                  "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
              )
              |> ignore

              req.Content <-
                  new System.Net.Http.StringContent(
                      """{"https://schema.org/agent":"Alice","https://schema.org/object":"order-1"}""",
                      System.Text.Encoding.UTF8,
                      "application/json"
                  )

              let! (resp: HttpResponseMessage) = client.SendAsync(req) |> Async.AwaitTask
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.stringContains body "schema.org/agent" "schema:agent IRI from body attrs"
              Expect.stringContains body "Alice" "schema:agent value from body attrs"
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
          }

          testCaseAsync "Link has_provenance target is provenance doc with anchor= resource (AC1 / PROV-AQ)"
          <| async {
              use app = startProvenanceServer (orderProvConfig ())
              use client = app.GetTestClient()
              let! (resp: HttpResponseMessage) = client.GetAsync("/no-produces") |> Async.AwaitTask
              let linkValues = resp.Headers.GetValues("Link") |> Seq.toList
              let provLink = linkValues |> List.tryFind (fun v -> v.Contains "has_provenance")
              Expect.isSome provLink "has_provenance Link header must be present"
              let link = provLink.Value
              Expect.isTrue (link.Contains "/provenance?resource=") "Link target must point to provenance doc"
              Expect.isTrue (link.Contains "anchor=") "Link must carry anchor= param (PROV-AQ §4.1)"
              Expect.isFalse (link.Contains "; type=") "Link must not carry spurious type= param"
          }

          testCaseAsync "malformed IRI key in POST body is dropped — no 500 (AC3 / security)"
          <| async {
              use app = startProvenanceServer (orderProvConfig ())
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Post, "/orders")

              req.Headers.TryAddWithoutValidation(
                  "Accept",
                  "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
              )
              |> ignore

              req.Content <-
                  new System.Net.Http.StringContent(
                      """{"http://[invalid":"bad-value","https://schema.org/agent":"Alice"}""",
                      System.Text.Encoding.UTF8,
                      "application/json"
                  )

              let! (resp: HttpResponseMessage) = client.SendAsync(req) |> Async.AwaitTask
              Expect.isTrue (int resp.StatusCode < 500) "malformed IRI key must not cause 500"
              let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
              Expect.stringContains body "Alice" "valid attribute is still captured after dropping malformed key"
              Expect.isFalse (body.Contains "invalid") "malformed key must be dropped from output"
          }

          testCaseAsync
              "malformed Host with class-ranged value degrades to Literal in store — value-IRI sink guarded (security: Host vector)"
          <| async {
              // PropertyClassRanges maps "/square" → "/tictactoe#", so body value "TopLeft"
              // becomes IriNode(originStr + "/tictactoe#" + "TopLeft").  When Host is
              // "ex ample.com" (space), originStr is "http://ex ample.com" — an invalid URI —
              // and the unguarded path would pass IriNode("http://ex ample.com/tictactoe#TopLeft")
              // to UriFactory.Create which throws UriFormatException.
              let config =
                  { orderProvConfig() with
                      PropertyClassRanges = Map.ofList [ "/square", "/tictactoe#" ] }

              let captureStore = CapturingStore()
              use app = startProvenanceServerWithStore config captureStore
              let server = app.GetTestServer()

              let bodyBytes =
                  System.Text.Encoding.UTF8.GetBytes """{"https://schema.org/square":"TopLeft"}"""

              // Inject malformed Host directly via TestServer.SendAsync, bypassing HTTP
              // parsing that would otherwise normalise or reject the host.
              // The store.Append call inside the middleware happens before toJsonLd runs, so
              // the record IS captured even when the downstream entity-URI path (issue #17)
              // also throws with a malformed Host.
              let! _ =
                  server.SendAsync(
                      Action<HttpContext>(fun ctx ->
                          ctx.Request.Method <- "POST"
                          ctx.Request.Scheme <- "http"
                          ctx.Request.Host <- HostString "ex ample.com"
                          ctx.Request.Path <- PathString "/orders"
                          ctx.Request.Headers.Add("Accept", StringValues "application/ld+json; profile=\"http://www.w3.org/ns/prov\"")
                          ctx.Request.Headers.Add("Content-Type", StringValues "application/json")
                          ctx.Request.Body <- new MemoryStream(bodyBytes)
                          ctx.Request.ContentLength <- Nullable(int64 bodyBytes.Length))
                  )
                  |> Async.AwaitTask
                  |> Async.Catch

              let records = captureStore.Records

              Expect.hasLength
                  records
                  1
                  "store.Append is called before toJsonLd — record captured even when entity URI also throws"

              let bodyAttrs = records[0].BodyAttributes

              let squareAttr =
                  bodyAttrs |> List.tryFind (fun (iri, _) -> iri.Contains "square")

              Expect.isSome squareAttr "class-ranged body attribute must be present in captured record"

              let (_, attrValue) = squareAttr.Value

              match attrValue with
              | Literal v -> Expect.equal v "TopLeft" "value degraded to Literal — value-IRI sink is guarded"
              | IriNode iri ->
                  failtest
                      $"expected Literal but store has IriNode '{iri}' — value-IRI sink is unguarded"
          } ]
