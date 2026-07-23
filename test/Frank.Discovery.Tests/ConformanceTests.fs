module Frank.Discovery.Tests.ConformanceTests

open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.TestHost
open Expecto
open Frank.Discovery.Tests.TestHelpers

/// #432 F-CONF: the advertised⟹served conformance seam. Every method OPTIONS advertises
/// in Allow for a real, resource/get-CE-registered route must actually be servable (not
/// 405), and every rel="profile" ALPS Link OPTIONS advertises for a resource must also
/// appear on every status that resource's GET (and, RFC 7231 §4.3.2, HEAD) can return (200
/// AND 304) — not just on OPTIONS itself. Built via TestHelpers.buildFConfApp, the REAL
/// `resource`/`get`/`useDiscoveryWith` composition (not a hand-built RouteEndpoint) — HEAD
/// registration (#431) and DiscoveryMiddleware's HEAD-parity emission (#432 review fix 2)
/// both only reproduce through that real path.
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

          // Proven invariant (HEAD folded into the GET endpoint's registered methods by
          // Builder.fs's ResourceSpec.Build, #431): HEAD is both advertised (Allow includes
          // HEAD whenever GET is present) AND served by the SAME endpoint GET is, so it
          // never 405s.
          testCase "HEAD does not 405 (registered and served, #431)"
          <| fun _ ->
              use app = buildFConfApp sampleConfig
              use client = app.GetTestClient()
              use req = new HttpRequestMessage(HttpMethod.Head, "/games/1")
              let resp = client.SendAsync(req).GetAwaiter().GetResult()
              Expect.notEqual (int resp.StatusCode) 405 "HEAD is advertised in Allow and is actually served"

          testCase "profile Link present on GET 200"
          <| fun _ ->
              use app = buildFConfApp sampleConfig
              use client = app.GetTestClient()
              let resp = client.GetAsync("/games/1").GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "200"
              let links = linkValues resp

              Expect.isTrue
                  (links
                   |> List.exists (fun l -> l.Contains "rel=\"profile\"" && l.Contains "/alps/test"))
                  "profile Link (RFC 6906, pointing at the ALPS profile) present on GET 200"

          testCase "profile Link present on GET 304 (conditional, matching If-None-Match)"
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
                   |> List.exists (fun l -> l.Contains "rel=\"profile\"" && l.Contains "/alps/test"))
                  "profile Link (RFC 6906, pointing at the ALPS profile) present on GET 304"

          // #432 review fix 2 (RFC 7231 §4.3.2): a HEAD response MUST carry the same
          // headers the equivalent GET would, including the discovery Link set — this
          // fixture runs the SAME DiscoveryMiddleware+resource/get CE pipeline
          // HeadRegistrationIntegrationTests deliberately excludes DiscoveryMiddleware from,
          // so it is the one place this parity is actually exercised.
          testCase "HEAD /games/{id} carries the same Link set as GET"
          <| fun _ ->
              use app = buildFConfApp sampleConfig
              use client = app.GetTestClient()
              let getResp = client.GetAsync("/games/1").GetAwaiter().GetResult()
              Expect.equal (int getResp.StatusCode) 200 "GET 200"
              let getLinks = linkValues getResp |> List.sort

              use headReq = new HttpRequestMessage(HttpMethod.Head, "/games/1")
              let headResp = client.SendAsync(headReq).GetAwaiter().GetResult()
              Expect.equal (int headResp.StatusCode) 200 "HEAD 200"
              let headLinks = linkValues headResp |> List.sort

              Expect.equal headLinks getLinks "HEAD's Link header set must equal GET's (RFC 7231 §4.3.2)"

          // #432 review fix 6: JSON Home and OPTIONS derive Allow from the SAME
          // advertisedMethods computation (DiscoveryMiddleware.homeResourcesFromEndpoints /
          // handleOptions) — this proves the "both channels" claim, not merely asserts it.
          testCase "JSON Home allow for /games/{id} equals OPTIONS Allow"
          <| fun _ ->
              use app = buildFConfApp sampleConfig
              use client = app.GetTestClient()

              use optsReq = new HttpRequestMessage(HttpMethod.Options, "/games/1")
              let optsResp = client.SendAsync(optsReq).GetAwaiter().GetResult()
              let optsAllow = allowValues optsResp |> List.sort

              use homeReq = new HttpRequestMessage(HttpMethod.Get, "/")
              homeReq.Headers.Add("Accept", "application/json-home")
              let homeResp = client.SendAsync(homeReq).GetAwaiter().GetResult()
              Expect.equal (int homeResp.StatusCode) 200 "JSON Home 200"
              let homeBody = homeResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
              use doc = JsonDocument.Parse homeBody
              let resources = doc.RootElement.GetProperty "resources"

              let gameEntry =
                  resources.EnumerateObject()
                  |> Seq.tryFind (fun r -> r.Name = "https://schema.org/Game")

              Expect.isSome gameEntry "JSON Home missing the /games/{id} resource entry"

              let homeAllow =
                  gameEntry.Value.Value.GetProperty("hints").GetProperty("allow").EnumerateArray()
                  |> Seq.map (fun el -> el.GetString())
                  |> Seq.toList
                  |> List.sort

              Expect.equal
                  homeAllow
                  optsAllow
                  "JSON Home 'allow' and OPTIONS 'Allow' must advertise the identical method set" ]
