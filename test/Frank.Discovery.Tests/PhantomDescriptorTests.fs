module Frank.Discovery.Tests.PhantomDescriptorTests

open System.Net.Http
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Expecto
open Frank.Discovery
open Frank.Discovery.Tests.TestHelpers

/// #418: a served ALPS descriptor must correspond to something a client can actually
/// reach — either backing a registered route (methodsByRel, via ResourceRelationMetadata),
/// a registered accepted request type (methodsByType, via IAcceptsMetadata — the SAME two
/// live-correlation maps #397/#398's reconcileAlpsTypes already established), a registered
/// DECLARED RESPONSE type (producedTypes, via IProducesResponseTypeMetadata — e.g.
/// `produces typeof<MoveResult> 200`), or being the `rt` target of a descriptor that IS
/// live. A descriptor satisfying none of these is a phantom affordance (e.g.
/// sample/TicTacToe-v732's MoveLog/ItemList, which backs zero route and is never embedded
/// anywhere) and must be dropped at serve time, since codegen (DiscoveryEmitter, MSBuild
/// time) cannot see which types end up routed/embedded — only the running app knows.
let private mkDescriptor id classIri requestType rt =
    { Id = id
      Type = "semantic"
      Doc = None
      Href = Some $"https://example.org/{id}"
      Descriptors = []
      Rt = rt
      ClassIri = classIri
      RequestClrTypeName = requestType }

[<Tests>]
let filterReachableDescriptorsTests =
    testList
        "DiscoveryMiddleware — #418 filterReachableDescriptors (pure)"
        [ test "descriptor live via methodsByRel (ClassIri) is kept" {
              let liveKeys = Set.ofList [ "https://schema.org/Game" ]
              let d = mkDescriptor "Game" (Some "https://schema.org/Game") None None

              let result = DiscoveryMiddleware.filterReachableDescriptors liveKeys [ d ]

              Expect.equal result [ d ] "live-by-relation descriptor is kept"
          }

          test "descriptor live via methodsByType (RequestClrTypeName, accepted request body) is kept" {
              let liveKeys = Set.ofList [ "App.MoveRequest" ]
              let d = mkDescriptor "MoveRequest" None (Some "App.MoveRequest") None

              let result = DiscoveryMiddleware.filterReachableDescriptors liveKeys [ d ]

              Expect.equal result [ d ] "live-by-request-type descriptor is kept"
          }

          test "descriptor live via producedTypes (RequestClrTypeName, declared response type, `produces`) is kept" {
              // Mirrors the real regression: MoveResult (`produces typeof<MoveResult> 200`)
              // is never accepted as a request body and never backs its own route by
              // relation — only IProducesResponseTypeMetadata correlates it.
              let liveKeys = Set.ofList [ "App.MoveResult" ]

              let d =
                  mkDescriptor "MoveResult" (Some "https://schema.org/MoveResult") (Some "App.MoveResult") None

              let result = DiscoveryMiddleware.filterReachableDescriptors liveKeys [ d ]

              Expect.equal result [ d ] "live-by-declared-response-type descriptor is kept"
          }

          test "descriptor with no live signal, and not an rt target of a live descriptor, is DROPPED (#418 core case)" {
              // Mirrors MoveLog/ItemList: class-mapped (has a ClassIri) but backs zero route
              // and is never referenced by anything.
              let orphan = mkDescriptor "MoveLog" (Some "https://schema.org/ItemList") None None

              let result = DiscoveryMiddleware.filterReachableDescriptors Set.empty [ orphan ]

              Expect.isEmpty result "orphaned descriptor (no live signal, no rt reference) is dropped"
          }

          test
              "descriptor with no live signal, but IS the rt target of a live descriptor, is kept (embedded-child regression guard)" {
              let liveKeys = Set.ofList [ "App.MoveRequest" ]

              let moveRequest =
                  mkDescriptor "MoveRequest" None (Some "App.MoveRequest") (Some "https://example.org/Outcome")

              // ClassIri Some — a genuinely class-mapped descriptor (like MoveResult) with NO
              // live signal of its own, reachable ONLY via being MoveRequest's `rt` target.
              // ClassIri=None would make this test vacuous (auto-kept regardless of rt-linkage).
              let outcome = mkDescriptor "Outcome" (Some "https://example.org/Outcome") None None

              let result =
                  DiscoveryMiddleware.filterReachableDescriptors liveKeys [ moveRequest; outcome ]

              Expect.contains result moveRequest "the live descriptor itself is kept"

              Expect.contains
                  result
                  outcome
                  "the rt TARGET of a live descriptor is kept even though it has no live signal of its own"
          }

          test
              "rt chain of depth 2 (A live --rt--> B --rt--> C): ALL THREE survive, not just the one-hop target B (#422 expert-review finding 1)" {
              // A is live; B is reachable only as A's rt target; C is reachable only as B's
              // rt target. Neither B nor C has any live signal of its own. A one-hop closure
              // keeps B (one hop from A) but drops C (two hops from A) even though B — now
              // served, live — publishes an `rt` link to C. A client following A -> B -> C's
              // rt would hit a dead reference the server itself just served.
              let liveKeys = Set.ofList [ "https://schema.org/A" ]

              let a =
                  mkDescriptor "A" (Some "https://schema.org/A") None (Some "https://example.org/B")

              let b =
                  mkDescriptor "B" (Some "https://schema.org/B") None (Some "https://example.org/C")

              let c = mkDescriptor "C" (Some "https://schema.org/C") None None

              let result = DiscoveryMiddleware.filterReachableDescriptors liveKeys [ a; b; c ]

              Expect.contains result a "the live descriptor itself is kept"
              Expect.contains result b "one-hop rt target (B) is kept"

              Expect.contains
                  result
                  c
                  "two-hop rt target (C, reachable via B's rt) must ALSO be kept — a fixed-point closure, not a one-hop check"
          }

          test
              "unrelated non-live descriptor is still dropped even when SOME OTHER live descriptor exists (no false-positive keep-everything)" {
              let liveKeys = Set.ofList [ "https://schema.org/Game" ]
              let game = mkDescriptor "Game" (Some "https://schema.org/Game") None None
              let orphan = mkDescriptor "MoveLog" (Some "https://schema.org/ItemList") None None

              let result =
                  DiscoveryMiddleware.filterReachableDescriptors liveKeys [ game; orphan ]

              Expect.contains result game "live descriptor kept"

              Expect.isFalse
                  (result |> List.contains orphan)
                  "unrelated orphan still dropped, not swept in by an unrelated live descriptor"
          } ]

// ── #418 AC1/AC2/AC4: serve-time filtering over a live EndpointDataSource ─────────

let private phantomConfig: DiscoveryConfig =
    { ProfileUri = "/alps/test"
      HomeRoute = "/"
      AlpsDescriptors =
        [ { Id = "Game"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/Game"
            Descriptors = []
            Rt = None
            ClassIri = Some "https://schema.org/Game"
            RequestClrTypeName = None }
          { Id = "MoveRequest"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/MoveAction"
            Descriptors = []
            Rt = Some "https://schema.org/Game"
            ClassIri = Some "https://schema.org/MoveAction"
            // FCS-style dotted form (matching Frank.ClrTypeName.normalizeFullName's output for
            // a module-nested type), NOT typeof<MoveRequestFixture>.FullName's raw CLR '+' form
            // — see AlpsTypeReconciliationTests.fs's moveRequestFixtureFcsStyleName for why the
            // raw CLR form would mask the very correlation this fixture exists to exercise.
            RequestClrTypeName = Some "Frank.Discovery.Tests.TestHelpers.MoveRequestFixture" }
          // Real regression (#418): never accepted as a request body, never backs its own
          // route by relation — reachable ONLY as the POST endpoint's DECLARED RESPONSE
          // type (IProducesResponseTypeMetadata, `produces typeof<MoveResult> 200` in the
          // real sample). Mirrors sample/TicTacToe-v732's MoveResult/"Won" case exactly.
          { Id = "MoveResult"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/MoveResult"
            Descriptors = []
            Rt = None
            ClassIri = Some "https://schema.org/MoveResult"
            RequestClrTypeName = Some "Frank.Discovery.Tests.PhantomDescriptorTests.MoveResultFixture" }
          { Id = "MoveLog"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/ItemList"
            Descriptors = []
            Rt = None
            ClassIri = Some "https://schema.org/ItemList"
            RequestClrTypeName = None } ]
      DescribedByLinks = []
      ResourceHrefVars = Map.empty }

/// Fixture response type for IProducesResponseTypeMetadata — stands in for MoveResult.
type private MoveResultFixture = { Status: string }

// ── #422 Finding C: correlationExtractors is a genuine fold over a list ──────────────

/// #422 Finding C: filterReachableDescriptors/isLiveDescriptor used to hand-enumerate three
/// separately-named signals (relation / accepted-request-type / produced-response-type),
/// each requiring its own extraction function, its own map/set, and its own threaded
/// parameter — already had to grow a 3rd signal mid-#418 to fix the MoveResult regression.
/// correlationExtractors/liveCorrelationKeysWith replace that with a single pluggable list
/// of `RouteEndpoint -> string list` extractors folded into ONE liveCorrelationKeys set —
/// adding a future signal is exactly one function appended to the list, and
/// isLiveDescriptor/filterReachableDescriptors never change. These tests prove the fold is
/// real (not just renamed plumbing) by removing an extractor from the list — via
/// liveCorrelationKeysWith's own `extractors` parameter, from OUTSIDE the module, with zero
/// changes to isLiveDescriptor/filterReachableDescriptors — and observing the live set
/// shrink accordingly.
let private endpointDataSourceOf (endpoints: RouteEndpoint list) : EndpointDataSource =
    { new EndpointDataSource() with
        member _.Endpoints =
            endpoints |> List.map (fun re -> re :> Endpoint) |> ResizeArray :> _

        member _.GetChangeToken() =
            Microsoft.Extensions.FileProviders.NullChangeToken.Singleton :> Microsoft.Extensions.Primitives.IChangeToken }

[<Tests>]
let correlationExtractorsTests =
    testList
        "DiscoveryMiddleware — #422 correlationExtractors is a fold over a list"
        [ test "liveCorrelationKeys (the full extractor list) unions all three signals across endpoints" {
              let dataSource =
                  endpointDataSourceOf
                      [ routeEndpoint
                            "/games/{id}"
                            [| "GET" |]
                            [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ]
                        routeEndpoint
                            "/games/{id}"
                            [| "POST" |]
                            [ box (
                                  AcceptsMetadata([| "application/json" |], typeof<MoveRequestFixture>, false)
                                  :> IAcceptsMetadata
                              )
                              box (
                                  ProducesResponseTypeMetadata(200, typeof<MoveResultFixture>, [| "application/json" |])
                                  :> obj
                              ) ] ]

              let fullKeys = DiscoveryMiddleware.liveCorrelationKeys dataSource

              Expect.contains fullKeys "https://schema.org/Game" "relation-IRI signal present"

              Expect.contains
                  fullKeys
                  (Frank.ClrTypeName.normalizeFullName typeof<MoveRequestFixture>.FullName)
                  "accepted-request-type signal present"

              Expect.contains
                  fullKeys
                  (Frank.ClrTypeName.normalizeFullName typeof<MoveResultFixture>.FullName)
                  "produced-response-type signal present"
          }

          test
              "removing ONE extractor from the list (outside isLiveDescriptor/filterReachableDescriptors) shrinks the live set accordingly — proves a genuine fold, not hand-wired signals" {
              let dataSource =
                  endpointDataSourceOf
                      [ routeEndpoint
                            "/games/{id}"
                            [| "POST" |]
                            [ box (
                                  AcceptsMetadata([| "application/json" |], typeof<MoveRequestFixture>, false)
                                  :> IAcceptsMetadata
                              )
                              box (
                                  ProducesResponseTypeMetadata(200, typeof<MoveResultFixture>, [| "application/json" |])
                                  :> obj
                              ) ] ]

              // Only the produced-response-type extractor — dropping the accepted-request-type
              // extractor from the list, with zero code changes anywhere else.
              let onlyProducedTypeExtractor = [ DiscoveryMiddleware.producedResponseTypesOf ]

              let narrowedKeys =
                  DiscoveryMiddleware.liveCorrelationKeysWith onlyProducedTypeExtractor dataSource

              Expect.contains
                  narrowedKeys
                  (Frank.ClrTypeName.normalizeFullName typeof<MoveResultFixture>.FullName)
                  "the remaining (produced-response-type) extractor's signal is still present"

              Expect.isFalse
                  (Set.contains (Frank.ClrTypeName.normalizeFullName typeof<MoveRequestFixture>.FullName) narrowedKeys)
                  "the REMOVED (accepted-request-type) extractor's signal is gone from the live set — proves the set is a genuine fold over the extractor list, not independently hand-wired"
          } ]

let private startPhantomServer () =
    let endpoints: Microsoft.AspNetCore.Http.Endpoint[] =
        [| routeEndpoint
               "/games/{id}"
               [| "GET" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ]
           routeEndpoint
               "/games/{id}"
               [| "POST" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
                 box (AcceptsMetadata([| "application/json" |], typeof<MoveRequestFixture>, false) :> IAcceptsMetadata)
                 box (ProducesResponseTypeMetadata(200, typeof<MoveResultFixture>, [| "application/json" |]) :> obj) ] |]

    let app = buildDiscoveryApp None phantomConfig endpoints
    app.StartAsync().GetAwaiter().GetResult()
    app

[<Tests>]
let phantomServeTimeTests =
    testList
        "DiscoveryMiddleware — #418 phantom descriptor dropped at serve time"
        [ testCase "GET ALPS profile: MoveLog/ItemList (zero backing route, unreferenced) is NOT served"
          <| fun _ ->
              use app = startPhantomServer ()
              use client = app.GetTestClient()
              let resp = client.GetAsync(phantomConfig.ProfileUri).GetAwaiter().GetResult()
              Expect.equal (int resp.StatusCode) 200 "ALPS profile served"
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

              use doc = JsonDocument.Parse body
              let descriptors = doc.RootElement.GetProperty("alps").GetProperty("descriptor")

              let ids =
                  descriptors.EnumerateArray()
                  |> Seq.map (fun d -> d.GetProperty("id").GetString())
                  |> Seq.toList

              Expect.isFalse
                  (ids |> List.contains "MoveLog")
                  "MoveLog descriptor must not be served — zero backing route, never embedded"

              Expect.isFalse
                  (body.Contains "ItemList")
                  "ItemList class IRI must not appear anywhere in the served ALPS document"

          testCase "GET ALPS profile: Game (live-routed) still served"
          <| fun _ ->
              use app = startPhantomServer ()
              use client = app.GetTestClient()
              let resp = client.GetAsync(phantomConfig.ProfileUri).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

              use doc = JsonDocument.Parse body
              let descriptors = doc.RootElement.GetProperty("alps").GetProperty("descriptor")

              let ids =
                  descriptors.EnumerateArray()
                  |> Seq.map (fun d -> d.GetProperty("id").GetString())
                  |> Seq.toList

              Expect.contains ids "Game" "Game (live via ResourceRelationMetadata) is still served"

          testCase
              "GET ALPS profile: MoveRequest (embedded action, live via IAcceptsMetadata) still served — regression guard"
          <| fun _ ->
              use app = startPhantomServer ()
              use client = app.GetTestClient()
              let resp = client.GetAsync(phantomConfig.ProfileUri).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

              use doc = JsonDocument.Parse body
              let descriptors = doc.RootElement.GetProperty("alps").GetProperty("descriptor")

              let ids =
                  descriptors.EnumerateArray()
                  |> Seq.map (fun d -> d.GetProperty("id").GetString())
                  |> Seq.toList

              Expect.contains
                  ids
                  "MoveRequest"
                  "MoveRequest (live via IAcceptsMetadata — a route's request body type, not itself a top-level GET route) is still served: the filter must not drop legitimately-reachable-but-not-top-level-routed types"

          testCase
              "GET ALPS profile: MoveResult (declared response type, live via IProducesResponseTypeMetadata) still served — regression guard"
          <| fun _ ->
              use app = startPhantomServer ()
              use client = app.GetTestClient()
              let resp = client.GetAsync(phantomConfig.ProfileUri).GetAwaiter().GetResult()
              let body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

              use doc = JsonDocument.Parse body
              let descriptors = doc.RootElement.GetProperty("alps").GetProperty("descriptor")

              let ids =
                  descriptors.EnumerateArray()
                  |> Seq.map (fun d -> d.GetProperty("id").GetString())
                  |> Seq.toList

              Expect.contains
                  ids
                  "MoveResult"
                  "MoveResult (never accepted as a request body, never backs its own route — reachable ONLY via `produces`, IProducesResponseTypeMetadata) is still served: the filter must recognize the response-type live signal too" ]
