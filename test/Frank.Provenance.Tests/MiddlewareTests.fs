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
                    DeclaredPrefixes = []
                    StoreConfig = Frank.Provenance.ProvenanceStoreConfig.defaults
                    MaxBodyBytes = Frank.Provenance.ProvenanceConfig.defaultMaxBodyBytes }

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
              "malformed Host header → provenance endpoint returns 400, no record appended (security: origin-at-edge)"
          <| async {
              // RED (before fix): middleware faults with UriFormatException when toJsonLd calls
              // UriFactory.Create on the malformed ResourceUri/Agent.Id — test throws and fails.
              // GREEN (after fix): edge validation catches the bad Host before any IRI construction,
              // returns 400, store.Append is never called.
              let config =
                  { orderProvConfig () with
                      PropertyClassRanges = Map.ofList [ "/square", "/tictactoe#" ] }

              let captureStore = CapturingStore()
              use app = startProvenanceServerWithStore config captureStore
              let server = app.GetTestServer()

              let bodyBytes =
                  System.Text.Encoding.UTF8.GetBytes """{"https://schema.org/square":"TopLeft"}"""

              // Inject malformed Host directly via TestServer.SendAsync, bypassing HTTP
              // parsing that would otherwise normalise or reject the host.
              let! ctx =
                  server.SendAsync(
                      Action<HttpContext>(fun ctx ->
                          ctx.Request.Method <- "POST"
                          ctx.Request.Scheme <- "http"
                          ctx.Request.Host <- HostString "ex ample.com"
                          ctx.Request.Path <- PathString "/orders"

                          ctx.Request.Headers.Add(
                              "Accept",
                              StringValues "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
                          )

                          ctx.Request.Headers.Add("Content-Type", StringValues "application/json")
                          ctx.Request.Body <- new MemoryStream(bodyBytes)
                          ctx.Request.ContentLength <- Nullable(int64 bodyBytes.Length))
                  )
                  |> Async.AwaitTask

              Expect.equal ctx.Response.StatusCode 400 "malformed Host → 400, not 500"
              Expect.isEmpty captureStore.Records "store.Append must not be called — origin rejected at edge"
          }

          testCaseAsync "PUT with IRI-keyed body captures provenance attributes (#14)"
          <| async {
              // RED before impl: BodyCapture.readAndResetAsync guards on Method<>"POST" →
              //   body attrs list is empty for PUT → test fails.
              // GREEN after impl: body-bearing check covers POST/PUT/PATCH → attrs captured.
              let captureStore = CapturingStore()
              use app = startProvenanceServerWithStore (orderProvConfig ()) captureStore
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Put, "/orders/1")

              req.Content <-
                  new System.Net.Http.StringContent(
                      """{"https://schema.org/agent":"Alice"}""",
                      System.Text.Encoding.UTF8,
                      "application/json"
                  )

              let! _ = client.SendAsync(req) |> Async.AwaitTask
              let records = captureStore.Records

              Expect.isNonEmpty records "PUT must produce a provenance record"

              let attrs = records.Head.BodyAttributes

              Expect.isNonEmpty attrs "PUT body IRI attrs must be captured"

              Expect.isTrue
                  (attrs |> List.exists (fun (iri, _) -> iri.Contains "schema.org/agent"))
                  "schema:agent IRI captured from PUT body"
          }

          testCaseAsync "PATCH with IRI-keyed body captures provenance attributes (#14)"
          <| async {
              // RED before impl: same guard — PATCH body not captured.
              // GREEN after impl: PATCH is in body-bearing set.
              let captureStore = CapturingStore()
              use app = startProvenanceServerWithStore (orderProvConfig ()) captureStore
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Patch, "/orders/1")

              req.Content <-
                  new System.Net.Http.StringContent(
                      """{"https://schema.org/agent":"Bob"}""",
                      System.Text.Encoding.UTF8,
                      "application/json"
                  )

              let! _ = client.SendAsync(req) |> Async.AwaitTask
              let records = captureStore.Records

              Expect.isNonEmpty records "PATCH must produce a provenance record"

              let attrs = records.Head.BodyAttributes

              Expect.isNonEmpty attrs "PATCH body IRI attrs must be captured"

              Expect.isTrue
                  (attrs |> List.exists (fun (iri, _) -> iri.Contains "schema.org/agent"))
                  "schema:agent IRI captured from PATCH body"
          }

          testCaseAsync "GET does not record in provenance store — safe methods are excluded (#390)"
          <| async {
              // Safe methods (GET/HEAD/OPTIONS) observe state without changing it.
              // Recording them would pollute move-history lineages when GET and POST
              // share a route (e.g. /games/{id}).
              let captureStore = CapturingStore()
              use app = startProvenanceServerWithStore (orderProvConfig ()) captureStore
              use client = app.GetTestClient()
              let! _ = client.GetAsync("/no-produces") |> Async.AwaitTask
              let records = captureStore.Records
              Expect.isEmpty records "GET must NOT produce a provenance record in the store"
          }

          testCaseAsync
              "prov-profile GET does not record in store — both InvokeWithProv and InvokeNonProv agree (#390 F1)"
          <| async {
              // RED (before fix): InvokeWithProv calls store.Append unconditionally — a GET
              //   with Accept prov-profile increments the store, violating HTTP safety.
              // GREEN (after fix): InvokeWithProv gates store.Append on isUnsafeMethod.
              //   JSON-LD snapshot is still returned to caller; the store is not written.
              let captureStore = CapturingStore()
              use app = startProvenanceServerWithStore (orderProvConfig ()) captureStore
              use client = app.GetTestClient()
              // First: non-prov GET — must not record (InvokeNonProv path, already fixed)
              let! _ = client.GetAsync("/no-produces") |> Async.AwaitTask
              Expect.isEmpty captureStore.Records "non-prov GET must not record"
              // Second: same GET with prov-profile Accept — must also not record (InvokeWithProv path)
              use req = new HttpRequestMessage(HttpMethod.Get, "/no-produces")

              req.Headers.TryAddWithoutValidation(
                  "Accept",
                  "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
              )
              |> ignore

              let! _ = client.SendAsync(req) |> Async.AwaitTask

              Expect.isEmpty
                  captureStore.Records
                  "prov-profile GET must NOT record in store (safe method — HTTP safety)"
          }

          testCaseAsync "lowercase method 'get' is classified as safe — not recorded (#390 F2)"
          <| async {
              // RED (before fix): isUnsafeMethod uses raw string '=' — "get" <> "GET" is
              //   true, so isUnsafeMethod "get" = true → store.Append is called.
              // GREEN (after fix): isUnsafeMethod uses HttpMethods.IsGet (case-insensitive) →
              //   isUnsafeMethod "get" = false → not recorded.
              let captureStore = CapturingStore()
              use app = startProvenanceServerWithStore (orderProvConfig ()) captureStore
              let server = app.GetTestServer()

              // Inject lowercase method directly, bypassing HTTP layer (which normalises casing).
              let! _ =
                  server.SendAsync(
                      Action<HttpContext>(fun ctx ->
                          ctx.Request.Method <- "get"
                          ctx.Request.Scheme <- "http"
                          ctx.Request.Host <- HostString "localhost"
                          ctx.Request.Path <- PathString "/no-produces")
                  )
                  |> Async.AwaitTask

              Expect.isEmpty captureStore.Records "lowercase 'get' must NOT produce a provenance record"
          }

          testCaseAsync "DELETE produces a prov:Activity record in the non-prov path — DELETE is state-changing (#390)"
          <| async {
              // RED (before fix): InvokeNonProv gates store.Append on isBodyBearing (POST/PUT/PATCH).
              //   DELETE has no body but IS state-changing — it must produce a provenance record.
              //   This test fails while the gate uses isBodyBearing alone.
              // GREEN (after fix): gate uses isUnsafeMethod (records everything except GET/HEAD/OPTIONS/TRACE).
              //   DELETE is unsafe → record is appended.
              let captureStore = CapturingStore()
              use app = startProvenanceServerWithStore (orderProvConfig ()) captureStore
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Delete, "/orders/1")
              let! _ = client.SendAsync(req) |> Async.AwaitTask
              let records = captureStore.Records
              Expect.isNonEmpty records "DELETE must produce a provenance record (state-changing, no body)"
              Expect.equal records.Head.HttpMethod "DELETE" "record HttpMethod must be DELETE"
          }

          testCaseAsync "over-limit POST body on provenance path returns 413 (#13)"
          <| async {
              // RED before impl: EnableBuffering() has no size limit → over-limit body is
              //   read successfully → returns prov ld+json (not 413).
              // GREEN after impl: EnableBuffering(MaxBodyBytes) + IOException catch → 413.
              let config =
                  { orderProvConfig () with
                      MaxBodyBytes = 64L }

              use app = startProvenanceServer config
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Post, "/orders")

              req.Headers.TryAddWithoutValidation(
                  "Accept",
                  "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
              )
              |> ignore

              req.Content <-
                  new System.Net.Http.StringContent(
                      System.String('x', 256),
                      System.Text.Encoding.UTF8,
                      "application/json"
                  )

              let! (resp: HttpResponseMessage) = client.SendAsync(req) |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 413 "over-limit body on provenance path returns 413"
          }

          testCaseAsync "downstream IOException propagates and is NOT converted to 413 (narrow-catch guard)"
          <| async {
              // RED before fix: broad IOException catch in InvokeCore wraps next.Invoke →
              //   handler IOException is caught and returns 413.
              // GREEN after fix: narrow catch covers only readAndResetAsync → handler
              //   IOException propagates out of the middleware (not 413).
              use app = startProvenanceServerWithThrowingEndpoint (orderProvConfig ())
              use client = app.GetTestClient()
              let mutable statusCode = 0

              try
                  let! (resp: HttpResponseMessage) = client.GetAsync("/throw-io") |> Async.AwaitTask
                  statusCode <- int resp.StatusCode
              with ex ->
                  statusCode <- 0

              Expect.notEqual statusCode 413 "downstream IOException must propagate, not become 413"
          }

          testCaseAsync "MINOR-2: 422 error response does NOT carry has_provenance Link"
          <| async {
              // RED before fix: InvokeNonProv appends Link before next.Invoke → 422 carries the header.
              // GREEN after fix: OnStarting callback gates on status ≥ 200 && < 400 → 422 skips the header.
              use app = startProvenanceServer (orderProvConfig ())
              use client = app.GetTestClient()
              let! (resp: HttpResponseMessage) = client.PostAsync("/reject", null) |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 422 "endpoint returns 422"

              let hasProvLink =
                  try
                      resp.Headers.GetValues("Link")
                      |> Seq.exists (fun v -> v.Contains "has_provenance")
                  with _ ->
                      false

              Expect.isFalse hasProvLink "422 response must NOT carry has_provenance Link (MINOR-2)"
          }

          testCaseAsync "MINOR-2: 2xx response carries has_provenance Link"
          <| async {
              // Confirms the OnStarting gate allows the header on successful responses.
              use app = startProvenanceServer (orderProvConfig ())
              use client = app.GetTestClient()
              let! (resp: HttpResponseMessage) = client.GetAsync("/no-produces") |> Async.AwaitTask
              Expect.equal (int resp.StatusCode) 200 "endpoint returns 200"

              let hasProvLink =
                  try
                      resp.Headers.GetValues("Link")
                      |> Seq.exists (fun v -> v.Contains "has_provenance")
                  with _ ->
                      false

              Expect.isTrue hasProvLink "200 response must carry has_provenance Link (MINOR-2)"
          } ]
