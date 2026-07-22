module Frank.Discovery.Tests.ConformanceTests

open System.Net.Http
open Microsoft.AspNetCore.TestHost
open Expecto
open Frank.Discovery.Tests.TestHelpers

/// #432 F-CONF: the advertised⟹served conformance seam. Every method OPTIONS advertises
/// in Allow for a real, resource/get-CE-registered route must actually be servable (not
/// 405), and every describedby Link OPTIONS advertises for a resource must also appear on
/// every status that resource's GET can return (200 AND 304) — not just on OPTIONS itself.
/// Built via TestHelpers.buildFConfApp, the REAL `resource`/`get`/`useDiscoveryWith`
/// composition (not a hand-built RouteEndpoint) — the GET-only registration gap this test
/// documents (HEAD → 405) only reproduces through that real path.
[<Tests>]
let tests =
    testList
        "F-CONF: advertised⟹served conformance (#432)"
        [ testCase "OPTIONS advertises exactly GET, HEAD, OPTIONS for /games/{id}"
          <| fun _ ->
              use app = buildFConfApp sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Options, "/games/1")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()

              Expect.equal
                  (allowValues resp |> List.sort)
                  [ "GET"; "HEAD"; "OPTIONS" ]
                  "Allow header advertises GET, HEAD, OPTIONS"

          testList
              "advertised methods are actually served (do not 405)"
              [ for m in [ "GET"; "OPTIONS" ] do
                    testCase $"{m} does not 405"
                    <| fun _ ->
                        use app = buildFConfApp sampleConfig
                        use client = app.GetTestClient()
                        use req = new HttpRequestMessage(HttpMethod(m), "/games/1")
                        let resp = client.SendAsync(req).GetAwaiter().GetResult()
                        Expect.notEqual (int resp.StatusCode) 405 $"{m} is advertised and must not 405" ]

          // EXPECTED RED until #431 lands: HEAD is advertised (Allow includes HEAD, added
          // by the shared advertised-method computation whenever GET is present) but the
          // `get` CE registers GET-only today — #432's scope is the dedup seam + the
          // describedby-on-GET emission, not the HEAD registration itself. Do NOT fake this
          // green by registering `head` on the fixture in buildFConfApp; that would hide
          // the real gap #431 exists to close.
          testCase "HEAD does not 405 — EXPECTED RED pre-#431 (get CE is GET-only today)"
          <| fun _ ->
              use app = buildFConfApp sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Head, "/games/1")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()

              Expect.notEqual
                  (int resp.StatusCode)
                  405
                  "HEAD is advertised in Allow but not yet served — closed by #431, not #432"

          testCase "describedby Link present on GET 200"
          <| fun _ ->
              use app = buildFConfApp sampleConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/games/1").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"
              let links = linkValues resp

              Expect.isTrue
                  (links
                   |> List.exists (fun l -> l.Contains "rel=\"describedby\"" && l.Contains "/alps/test"))
                  "describedby Link (pointing at the ALPS profile) present on GET 200"

          testCase "describedby Link present on GET 304 (conditional, matching If-None-Match)"
          <| fun _ ->
              use app = buildFConfApp sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Get, "/games/1")
              req.Headers.TryAddWithoutValidation("If-None-Match", "\"v1\"") |> ignore
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 304 "304"
              let links = linkValues resp

              Expect.isTrue
                  (links
                   |> List.exists (fun l -> l.Contains "rel=\"describedby\"" && l.Contains "/alps/test"))
                  "describedby Link (pointing at the ALPS profile) present on GET 304" ]
