module Frank.Discovery.Tests.MiddlewareTests

open System
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
                  Rt = None
                  ClassIri = None
                  RequestClrTypeName = None } ]
            Rt = Some "https://schema.org/Game"
            ClassIri = None
            RequestClrTypeName = None } ]
      DescribedByLinks = []
      ResourceHrefVars = Map.empty }

[<Tests>]
let dereferenceTests =
    testList
        "DiscoveryMiddleware — relative IRI dereference (item #6)"
        [ testCase "ALPS href for square resolves against the live request origin (#398 AC1)"
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

              Expect.equal
                  squareHref.Value
                  "http://localhost/tictactoe#square"
                  "href is resolved against the live TestServer request origin, not host-relative"

              Expect.isTrue (Uri.IsWellFormedUriString(squareHref.Value, UriKind.Absolute)) "href is an absolute URI"

          testCase "ALPS href for external vocab class stays absolute unchanged (#398 AC1)"
          <| fun _ ->
              use app = startVocabServer tttVocabConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/alps/tictactoe").GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse(body)
              let alps = doc.RootElement.GetProperty("alps")
              let descriptors = alps.GetProperty("descriptor")

              let moveActionHref =
                  descriptors.EnumerateArray()
                  |> Seq.tryPick (fun d ->
                      let mutable idEl = Unchecked.defaultof<JsonElement>
                      let mutable hEl = Unchecked.defaultof<JsonElement>

                      if
                          d.TryGetProperty("id", &idEl)
                          && idEl.GetString() = "MoveAction"
                          && d.TryGetProperty("href", &hEl)
                      then
                          Some(hEl.GetString())
                      else
                          None)

              Expect.equal
                  moveActionHref
                  (Some "https://schema.org/MoveAction")
                  "external vocab href stays absolute, unaffected by origin resolution"

          testCase "GET /tictactoe (relative IRI dereference, strip fragment) → 200 (item #6)"
          <| fun _ ->
              use app = startVocabServer tttVocabConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/tictactoe").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200 — term definition served at /tictactoe" ]

// ── #397 AC1: served ALPS Type reflects the real registered HTTP method ──────
// startAlpsTypeServer (TestHelpers.fs) registers: GET+POST /games/{id} (relation=Game,
// POST also carries IAcceptsMetadata for MoveRequestFixture), PUT /widgets/{id}
// (relation=Widget), DELETE /gadgets/{id} (relation=Gadget). No live endpoint exists
// for ActionStatusType — its codegen default must survive untouched.

let private alpsTypeConfig =
    { ProfileUri = "/alps/test"
      HomeRoute = "/"
      AlpsDescriptors =
        [ { Id = "Game"
            // Codegen default is deliberately WRONG ("unsafe", as the old Rt-based
            // heuristic would emit) — reconciliation must override it to "safe" from
            // the live GET, proving Type is never left to the lock-file guess (#397).
            Type = "unsafe"
            Doc = None
            Href = Some "https://schema.org/Game"
            Descriptors = []
            Rt = None
            ClassIri = Some "https://schema.org/Game"
            RequestClrTypeName = None }
          { Id = "MoveAction"
            Type = "unsafe"
            Doc = None
            Href = Some "https://schema.org/MoveAction"
            Descriptors = []
            Rt = Some "https://schema.org/Game"
            // No live endpoint carries Relation="https://schema.org/MoveAction" — the
            // route-level correlation alone can't find this. Only the precise
            // RequestClrTypeName (IAcceptsMetadata) signal resolves it.
            ClassIri = None
            RequestClrTypeName = Some typeof<MoveRequestFixture>.FullName }
          { Id = "Widget"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/Widget"
            Descriptors = []
            Rt = None
            ClassIri = Some "https://schema.org/Widget"
            RequestClrTypeName = None }
          { Id = "Gadget"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/Gadget"
            Descriptors = []
            Rt = None
            ClassIri = Some "https://schema.org/Gadget"
            RequestClrTypeName = None }
          { Id = "ActionStatusType"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/ActionStatusType"
            Descriptors = []
            Rt = None
            // Never itself routed (a pure embedded outcome type) — no live endpoint
            // will ever match this ClassIri. Codegen default must survive untouched.
            ClassIri = Some "https://schema.org/ActionStatusType"
            RequestClrTypeName = None } ]
      DescribedByLinks = []
      ResourceHrefVars = Map.empty }

let private alpsTypeOf (descId: string) (alpsBody: string) : string =
    use doc = System.Text.Json.JsonDocument.Parse alpsBody
    let descriptors = doc.RootElement.GetProperty("alps").GetProperty("descriptor")

    descriptors.EnumerateArray()
    |> Seq.tryPick (fun d ->
        let mutable idEl = Unchecked.defaultof<System.Text.Json.JsonElement>
        let mutable typeEl = Unchecked.defaultof<System.Text.Json.JsonElement>

        if
            d.TryGetProperty("id", &idEl)
            && idEl.GetString() = descId
            && d.TryGetProperty("type", &typeEl)
        then
            Some(typeEl.GetString())
        else
            None)
    |> Option.defaultWith (fun () -> failwith $"descriptor '{descId}' not found in ALPS body")

[<Tests>]
let alpsTypeReconciliationTests =
    testList
        "DiscoveryMiddleware — #397 AC1: served ALPS Type from real HTTP methods"
        [ testCase "GET-only route (via relation) -> served Type is safe, overriding a wrong codegen default"
          <| fun _ ->
              use app = startAlpsTypeServer alpsTypeConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/alps/test").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

              Expect.equal
                  (alpsTypeOf "Game" body)
                  "safe"
                  "Game must be served as safe (real GET), not the wrong codegen unsafe default"

          testCase "POST route sharing the Game relation (via IAcceptsMetadata) -> served Type is unsafe"
          <| fun _ ->
              use app = startAlpsTypeServer alpsTypeConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/alps/test").GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

              Expect.equal
                  (alpsTypeOf "MoveAction" body)
                  "unsafe"
                  "MoveAction must be unsafe — resolved via the real POST endpoint's IAcceptsMetadata, not relation (no live relation exists for MoveAction)"

          testCase "PUT-only route -> served Type is idempotent"
          <| fun _ ->
              use app = startAlpsTypeServer alpsTypeConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/alps/test").GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.equal (alpsTypeOf "Widget" body) "idempotent" "Widget (PUT) must be idempotent"

          testCase "DELETE-only route -> served Type is idempotent"
          <| fun _ ->
              use app = startAlpsTypeServer alpsTypeConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/alps/test").GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              Expect.equal (alpsTypeOf "Gadget" body) "idempotent" "Gadget (DELETE) must be idempotent"

          testCase "class never itself routed -> codegen default Type survives untouched"
          <| fun _ ->
              use app = startAlpsTypeServer alpsTypeConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/alps/test").GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

              Expect.equal
                  (alpsTypeOf "ActionStatusType" body)
                  "semantic"
                  "ActionStatusType has no live endpoint — codegen default (semantic) is untouched"

          testCase
              "OPTIONS/Allow on the multi-verb Game route still reports both GET and POST (unaffected by ALPS reconciliation)"
          <| fun _ ->
              use app = startAlpsTypeServer alpsTypeConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Options, "/games/abc")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let allow = allowValues resp
              Expect.contains allow "GET" "Allow still includes GET"
              Expect.contains allow "POST" "Allow still includes POST" ]

// ── #398 AC2: rel="type" Link headers scoped to the matched resource's own relation ──

let private scopedRelationConfig =
    { ProfileUri = "/alps/test"
      HomeRoute = "/"
      AlpsDescriptors = []
      DescribedByLinks =
        [ { ClassIri = "https://schema.org/Game"
            Link = "<https://schema.org/Game>; rel=\"type\"" }
          { ClassIri = "https://schema.org/Widget"
            Link = "<https://schema.org/Widget>; rel=\"type\"" } ]
      ResourceHrefVars = Map.empty }

let private typeLinks (resp: HttpResponseMessage) =
    linkValues resp |> List.filter (fun l -> l.Contains "rel=\"type\"")

[<Tests>]
let describedByScopingTests =
    testList
        "DiscoveryMiddleware — #398 AC2: rel=\"type\" scoped to the matched resource"
        [ testCase "OPTIONS /games/{id} (declared relation=Game) carries only its own rel=\"type\" link"
          <| fun _ ->
              use app = startScopedRelationServer scopedRelationConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Options, "/games/abc")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let links = typeLinks resp
              Expect.equal links.Length 1 "exactly one rel=\"type\" link — the matched resource's own"
              Expect.stringContains links.[0] "https://schema.org/Game" "the Game link is present"

              Expect.isFalse
                  (links |> List.exists (fun l -> l.Contains "https://schema.org/Widget"))
                  "the unrelated Widget link must NOT be broadcast"

          testCase "OPTIONS /tictactoe (routed, no declared relation) carries zero rel=\"type\" links"
          <| fun _ ->
              use app = startScopedRelationServer scopedRelationConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Options, "/tictactoe")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.isEmpty (typeLinks resp) "no rel=\"type\" link for a route with no declared relation"

          testCase "OPTIONS / (unrouted home) carries zero rel=\"type\" links"
          <| fun _ ->
              use app = startScopedRelationServer scopedRelationConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Options, "/")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.isEmpty (typeLinks resp) "no rel=\"type\" link for the unrouted home path"

          testCase "OPTIONS still carries the unconditional rel=\"describedby\" profile Link regardless of scoping"
          <| fun _ ->
              use app = startScopedRelationServer scopedRelationConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Options, "/tictactoe")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let links = linkValues resp

              Expect.isTrue
                  (links
                   |> List.exists (fun l -> l.Contains "rel=\"describedby\"" && l.Contains "/alps/test"))
                  "profile describedby Link is unaffected by rel=\"type\" scoping" ]

// ── #398 AC3: Allow header is a single comma-joined wire-level value ─────────

/// Raw (non-comma-split) values for the given header on either general response
/// headers or content headers — .NET splits comma-joined header VALUES apart on
/// read via HttpClient's structured parsing, but NonValidated (added .NET 8)
/// exposes the raw header lines exactly as written on the wire, so this is the
/// only way to distinguish "1 line, comma-joined" from "N separate lines".
let private rawHeaderLines (resp: HttpResponseMessage) (name: string) : string list =
    let mutable values = Unchecked.defaultof<System.Net.Http.Headers.HeaderStringValues>

    if resp.Headers.NonValidated.TryGetValues(name, &values) then
        values |> List.ofSeq
    elif resp.Content.Headers.NonValidated.TryGetValues(name, &values) then
        values |> List.ofSeq
    else
        []

[<Tests>]
let allowHeaderJoiningTests =
    testList
        "DiscoveryMiddleware — #398 AC3: Allow header is one comma-joined wire value"
        [ testCase "OPTIONS /games/{id} (multi-verb) yields exactly one raw Allow header line, comma-joined"
          <| fun _ ->
              use app = startAlpsTypeServer alpsTypeConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Options, "/games/abc")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              let raw = rawHeaderLines resp "Allow"
              Expect.equal raw.Length 1 "Allow is exactly one wire-level header line, not one per method"
              let joined = raw.[0]
              Expect.stringContains joined "," "the single Allow value lists methods comma-joined"
              Expect.stringContains joined "GET" "Allow lists GET"
              Expect.stringContains joined "POST" "Allow lists POST"

              // Convention parity: this is the SAME serialization convention ASP.NET Core's own
              // built-in 405 path uses (HttpMethodMatcherPolicy) — single comma-joined value.
              Expect.isFalse (joined.Contains "\n") "single header value, no embedded newlines" ]
