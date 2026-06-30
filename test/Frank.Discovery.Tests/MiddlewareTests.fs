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
              Expect.stringContains body "https://schema.org/Game" "vocabulary rel present" ]

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
      DescribedByLinks = [] }

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

                          if d.TryGetProperty("href", &hEl) then Some(hEl.GetString()) else None
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
