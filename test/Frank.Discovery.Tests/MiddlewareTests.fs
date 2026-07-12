module Frank.Discovery.Tests.MiddlewareTests

open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.TestHost
open Expecto
open Frank.Discovery
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
              Expect.stringContains body "https://schema.org/Game" "vocabulary rel present"

          testCase "GET / Accept:application/json-home → Content-Type: application/json-home (#10)"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"

              Expect.equal
                  (resp.Content.Headers.ContentType.MediaType)
                  "application/json-home"
                  "Content-Type must be application/json-home"

          testCase "GET / Accept:application/json-home → Vary: Accept (#8)"
          <| fun _ ->
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"
              let vary = resp.Headers.Vary |> Seq.toList
              Expect.contains vary "Accept" "Vary: Accept must be present on JSON Home conneg response"

          testCase
              "GET and POST on same route produce single JSON Home entry with allow ⊇ {GET,HEAD,OPTIONS,POST} (#390)"
          <| fun _ ->
              use app = startMultiVerbServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse body
              let resources = doc.RootElement.GetProperty "resources"
              let entries = resources.EnumerateObject() |> Seq.toList
              Expect.equal entries.Length 1 "exactly one JSON Home entry for same-route multi-verb resource"
              let gameEntry = entries.[0]
              Expect.equal gameEntry.Name "https://schema.org/Game" "entry key is vocab IRI"

              let allow =
                  gameEntry.Value.GetProperty("hints").GetProperty("allow").EnumerateArray()
                  |> Seq.map (fun el -> el.GetString())
                  |> Seq.toList

              Expect.contains allow "GET" "allow includes GET"
              Expect.contains allow "HEAD" "allow includes HEAD"
              Expect.contains allow "OPTIONS" "allow includes OPTIONS (RFC 7231 §7.4.1 — #390 F5)"
              Expect.contains allow "POST" "allow includes POST"

          testCase "same relation, different hrefs: JSON Home must not emit duplicate relation keys (#390 F4)"
          <| fun _ ->
              use app = startDuplicateRelationServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse body
              let resources = doc.RootElement.GetProperty "resources"
              // EnumerateObject returns ALL properties including duplicates — asserts dedup ran.
              let entries = resources.EnumerateObject() |> Seq.toList
              Expect.equal entries.Length 1 "JSON Home must have exactly one entry per relation (no duplicate keys)"

          testCase "duplicate relation IRI emits a LogWarning naming the relation and colliding hrefs (#390 F4 warn)"
          <| fun _ ->
              // RED (before fix): dedup was silent inside the pure homeResourcesFromEndpoints —
              //   no logger threaded in, no LogWarning emitted. CapturingLoggerProvider records
              //   nothing relevant → warnings list is empty → Expect.isNonEmpty FAILS.
              // GREEN (after fix): DiscoveryMiddleware ctor lazy deduplicates via the injected
              //   ILogger<DiscoveryMiddleware>, calling LogWarning on each dropped href, naming
              //   the relation IRI, kept href, and dropped href.
              let provider, app = startDuplicateRelationServerWithLogCapture sampleConfig
              use _ = app
              use client = app.GetTestClient()
              // Trigger JSON Home to force the lazy evaluation (warning fires on first access).
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              client.SendAsync(req).GetAwaiter().GetResult() |> ignore

              let warnings =
                  provider.Messages |> List.filter (fun m -> m.Contains "https://schema.org/Game")

              Expect.isNonEmpty warnings "LogWarning must be emitted for the duplicate relation IRI"
              Expect.stringContains warnings.[0] "https://schema.org/Game" "warning names the relation IRI"
              // Existing no-duplicate-keys assertion still holds.
              use req2 = new HttpRequestMessage(HttpMethod.Get, "/")
              req2.Headers.Add("Accept", "application/json-home")
              let resp2 = client.SendAsync(req2).GetAwaiter().GetResult()
              let body = resp2.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse body

              let entries =
                  doc.RootElement.GetProperty("resources").EnumerateObject() |> Seq.toList

              Expect.equal entries.Length 1 "JSON Home still has exactly one entry per relation after warning"

          testCase "OPTIONS handler includes OPTIONS in Allow header (RFC 7231 §7.4.1 — #390 F5)"
          <| fun _ ->
              // RED (before fix): handleOptions builds methods from endpoint metadata only.
              //   OPTIONS is not registered as an endpoint method → not in Allow.
              // GREEN (after fix): handleOptions always appends OPTIONS to the Allow set.
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Options, "/games/abc")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let allow = allowValues resp
              Expect.contains allow "OPTIONS" "Allow must include OPTIONS (RFC 7231 §7.4.1)"

          testCase "JSON Home hints.allow includes OPTIONS (#390 F5)"
          <| fun _ ->
              // RED (before fix): homeResourcesFromEndpoints collects only endpoint-declared methods.
              //   OPTIONS is not declared → absent from hints.allow.
              // GREEN (after fix): addOptions step always adds OPTIONS to the allow set.
              use app = startServer sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/")
              req.Headers.Add("Accept", "application/json-home")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse body
              let resources = doc.RootElement.GetProperty "resources"

              let gameEntry =
                  resources.EnumerateObject()
                  |> Seq.tryFind (fun r -> r.Name = "https://schema.org/Game")

              Expect.isSome gameEntry "game resource entry present"

              let allow =
                  gameEntry.Value.Value.GetProperty("hints").GetProperty("allow").EnumerateArray()
                  |> Seq.map (fun el -> el.GetString())
                  |> Seq.toList

              Expect.contains allow "OPTIONS" "JSON Home hints.allow must include OPTIONS (RFC 7231 §7.4.1)" ]

let private tttVocabConfig =
    { ProfileUri = "/alps/tictactoe"
      HomeRoute = "/"
      AlpsDescriptors =
        [ { Id = "MoveAction"
            Type = "unsafe"
            Doc = None
            Href = Some "https://schema.org/MoveAction"
            Descriptors =
              [ { Id = "square"
                  Type = "semantic"
                  Doc = None
                  Href = Some "/tictactoe#square"
                  Descriptors = []
                  Rt = None } ]
            Rt = Some "https://schema.org/Game" } ]
      DescribedByLinks = []
      ResourceHrefVars = Map.empty }

[<Tests>]
let dereferenceTests =
    testList
        "DiscoveryMiddleware — relative IRI dereference (item #6)"
        [ testCase "ALPS href for square is host-relative /tictactoe#square (not example.org)"
          <| fun _ ->
              use app = startVocabServer tttVocabConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/alps/tictactoe").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse(body)
              let alps = doc.RootElement.GetProperty("alps")
              let descriptors = alps.GetProperty("descriptor")

              // After AC1 nesting, square is inside MoveAction.descriptor — not top-level.
              let moveActionEl =
                  descriptors.EnumerateArray()
                  |> Seq.tryPick (fun d ->
                      let mutable idEl = Unchecked.defaultof<JsonElement>

                      if d.TryGetProperty("id", &idEl) && idEl.GetString() = "MoveAction" then
                          Some d
                      else
                          None)

              Expect.isSome moveActionEl "MoveAction descriptor present at top level"
              let mutable nestedDescEl = Unchecked.defaultof<JsonElement>

              Expect.isTrue
                  (moveActionEl.Value.TryGetProperty("descriptor", &nestedDescEl))
                  "MoveAction has nested descriptor array"

              let squareHref =
                  nestedDescEl.EnumerateArray()
                  |> Seq.tryPick (fun d ->
                      let mutable idEl = Unchecked.defaultof<JsonElement>

                      if d.TryGetProperty("id", &idEl) && idEl.GetString() = "square" then
                          let mutable hEl = Unchecked.defaultof<JsonElement>

                          if d.TryGetProperty("href", &hEl) then
                              Some(hEl.GetString())
                          else
                              None
                      else
                          None)

              Expect.isSome squareHref "square nested descriptor has href"
              Expect.equal squareHref.Value "/tictactoe#square" "href is host-relative"

          testCase "GET /tictactoe (relative IRI dereference, strip fragment) → 200 (item #6)"
          <| fun _ ->
              use app = startVocabServer tttVocabConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/tictactoe").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 — term definition served at /tictactoe" ]
