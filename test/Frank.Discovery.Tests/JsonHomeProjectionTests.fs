module Frank.Discovery.Tests.JsonHomeProjectionTests

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Tests.Shared.TestEndpointDataSource

[<Tests>]
let tests =
    testList "JsonHomeProjection" [
        testCase "projects resource with href (no variables)" <| fun _ ->
            let res = resource "/health" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            Expect.equal result.Input.Title "TestApp" "should use assembly name"
            Expect.equal result.Input.Resources.Length 1 "should have one resource"
            let r = result.Input.Resources.[0]
            Expect.equal r.RouteTemplate "/health" "route template"
            Expect.isTrue (Map.isEmpty r.RouteVariables) "no route variables"
            Expect.equal r.RelationType "urn:frank:TestApp/health" "URN fallback relation"

        testCase "projects resource with template variables using RoutePattern.Parameters" <| fun _ ->
            let res = resource "/games/{gameId}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            let r = result.Input.Resources.[0]
            Expect.equal r.RouteVariables.Count 1 "should have one variable"
            Expect.isTrue (r.RouteVariables.ContainsKey("gameId")) "should have gameId"
            Expect.equal r.RouteVariables.["gameId"] "urn:frank:TestApp/param/gameId" "URN fallback var"

        testCase "ALPS enrichment uses AlpsBaseUri for link relation" <| fun _ ->
            let res = resource "/games/{gameId}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let metadata: JsonHomeMetadata =
                { Title = Some "My Game API"
                  DocsUrl = Some "/scalar/v1"
                  AlpsBaseUri = Some "http://example.com/alps/games"
                  AlpsDescriptors = Some (Map.ofList [ "games", Map.ofList [ "gameId", "http://example.com/alps/games#gameId" ] ]) }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let r = result.Input.Resources.[0]
            Expect.equal r.RelationType "http://example.com/alps/games#games~gameId" "ALPS-derived relation"
            Expect.equal r.RouteVariables.["gameId"] "http://example.com/alps/games#gameId" "ALPS-enriched hrefVar"
            Expect.equal result.Input.Title "My Game API" "should use metadata title"

        testCase "collects HTTP methods into hints.allow" <| fun _ ->
            let res = resource "/items" {
                get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("list")))
                post (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("create")))
            }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            let r = result.Input.Resources.[0]
            Expect.contains r.Hints.Allow "GET" "should have GET"
            Expect.contains r.Hints.Allow "POST" "should have POST"

        testCase "filters out framework-internal endpoints" <| fun _ ->
            let userRes = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let internalEndpoint =
                RouteEndpointBuilder(
                    RequestDelegate(fun ctx -> ctx.Response.WriteAsync("profiles")),
                    RoutePatternFactory.Parse("/.well-known/frank-profiles"),
                    0)
                    .Build()
            let allEndpoints = Array.append userRes.Endpoints [| internalEndpoint |]
            let dataSource = TestEndpointDataSource(allEndpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            Expect.equal result.Input.Resources.Length 1 "should only have user resource"
            Expect.equal result.Input.Resources.[0].RouteTemplate "/items" "should be items"

        testCase "empty data source produces empty resources" <| fun _ ->
            let dataSource = TestEndpointDataSource([||])
            let result = JsonHomeProjection.project dataSource None "TestApp"
            Expect.isEmpty result.Input.Resources "should have no resources"

        testCase "docsUrl from metadata flows to hints" <| fun _ ->
            let res = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let metadata: JsonHomeMetadata =
                { JsonHomeMetadata.Empty with DocsUrl = Some "/scalar/v1" }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            Expect.equal result.Input.Resources.[0].Hints.DocsUrl (Some "/scalar/v1") "should have docs URL"

        testCase "describedByUrl detected from well-known endpoint" <| fun _ ->
            let userRes = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let profilesEndpoint =
                RouteEndpointBuilder(
                    RequestDelegate(fun ctx -> ctx.Response.WriteAsync("profiles")),
                    RoutePatternFactory.Parse("/.well-known/frank-profiles"),
                    0)
                    .Build()
            let allEndpoints = Array.append userRes.Endpoints [| profilesEndpoint |]
            let dataSource = TestEndpointDataSource(allEndpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            Expect.equal result.Input.DescribedByUrl (Some "/.well-known/frank-profiles") "should detect profiles endpoint"

        // M-1: route constraints stripped from hrefTemplate
        testCase "route constraints stripped from template" <| fun _ ->
            let res = resource "/items/{id:int}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            let r = result.Input.Resources.[0]
            Expect.equal r.RouteTemplate "/items/{id}" "route constraint should be stripped"
            Expect.isFalse (r.RouteTemplate.Contains(":")) "should not contain constraint colon"

        // #200: ALPS slug collision
        testCase "distinct routes with same slug produce distinct relation types" <| fun _ ->
            let listRes = resource "/games" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("list"))) }
            let itemRes = resource "/games/{gameId}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("item"))) }
            let allEndpoints = Array.append listRes.Endpoints itemRes.Endpoints
            let dataSource = TestEndpointDataSource(allEndpoints)
            let metadata: JsonHomeMetadata =
                { Title = None
                  DocsUrl = None
                  AlpsBaseUri = Some "http://example.com/alps/games"
                  AlpsDescriptors = Some (Map.ofList [ "games", Map.ofList [ "gameId", "http://example.com/alps/games#gameId" ] ]) }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let rels = result.Input.Resources |> List.map _.RelationType
            Expect.equal rels.Length 2 "should have two resources"
            Expect.notEqual rels.[0] rels.[1] "relation types must be distinct (no slug collision)"
            Expect.isTrue (rels |> List.exists (fun r -> r.Contains("#games~gameId"))) "item route should have games~gameId fragment"
            Expect.isTrue (rels |> List.exists (fun r -> r.Contains("#games") && not (r.Contains("#games~")))) "list route should have games fragment"

        // #200: nested routes produce unique fragments
        testCase "nested routes produce unique collision-free fragments" <| fun _ ->
            let gamesRes = resource "/games" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("list"))) }
            let gameRes = resource "/games/{gameId}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("game"))) }
            let movesRes = resource "/games/{gameId}/moves" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("moves"))) }
            let moveRes = resource "/games/{gameId}/moves/{moveId}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("move"))) }
            let allEndpoints = Array.concat [| gamesRes.Endpoints; gameRes.Endpoints; movesRes.Endpoints; moveRes.Endpoints |]
            let dataSource = TestEndpointDataSource(allEndpoints)
            let metadata: JsonHomeMetadata =
                { Title = None
                  DocsUrl = None
                  AlpsBaseUri = Some "http://example.com/alps/games"
                  AlpsDescriptors = Some (Map.ofList [ "games", Map.ofList [ "gameId", "http://example.com/alps/games#gameId" ] ]) }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let rels = result.Input.Resources |> List.map _.RelationType
            Expect.equal rels.Length 4 "should have four resources"
            // All relation types must be distinct
            Expect.equal (rels |> List.distinct |> List.length) 4 "all relation types must be distinct"
            Expect.isTrue (rels |> List.exists (fun r -> r.EndsWith("#games"))) "/games should have fragment 'games'"
            Expect.isTrue (rels |> List.exists (fun r -> r.EndsWith("#games~gameId"))) "/games/{gameId} should have fragment 'games~gameId'"
            Expect.isTrue (rels |> List.exists (fun r -> r.EndsWith("#games~gameId~moves"))) "/games/{gameId}/moves should have fragment 'games~gameId~moves'"
            Expect.isTrue (rels |> List.exists (fun r -> r.EndsWith("#games~gameId~moves~moveId"))) "/games/{gameId}/moves/{moveId} should have fragment 'games~gameId~moves~moveId'"

        // #200: backward compatibility — single route still works
        testCase "single route without collision still produces correct fragment" <| fun _ ->
            let res = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let metadata: JsonHomeMetadata =
                { Title = None
                  DocsUrl = None
                  AlpsBaseUri = Some "http://example.com/alps/items"
                  AlpsDescriptors = Some (Map.ofList [ "items", Map.empty ]) }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let r = result.Input.Resources.[0]
            Expect.equal r.RelationType "http://example.com/alps/items#items" "single route should produce simple fragment"

        // M-2: AlpsBaseUri alone is sufficient for ALPS relation (no descriptor required)
        testCase "AlpsBaseUri without AlpsDescriptors produces ALPS relation" <| fun _ ->
            let res = resource "/widgets" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let metadata: JsonHomeMetadata =
                { Title = None
                  DocsUrl = None
                  AlpsBaseUri = Some "http://example.com/alps/widgets"
                  AlpsDescriptors = None }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let r = result.Input.Resources.[0]
            Expect.isTrue (r.RelationType.StartsWith("http://example.com/alps/widgets#")) "AlpsBaseUri alone should produce ALPS relation, not URN"

        // M-6: AlpsBaseUri with existing fragment strips fragment before appending
        testCase "AlpsBaseUri with existing fragment strips fragment before appending" <| fun _ ->
            let res = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let metadata: JsonHomeMetadata =
                { Title = None
                  DocsUrl = None
                  AlpsBaseUri = Some "http://example.com/alps#existing"
                  AlpsDescriptors = Some (Map.ofList [ "items", Map.empty ]) }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let r = result.Input.Resources.[0]
            Expect.isFalse (r.RelationType.Contains("#existing#")) "should not produce double-fragment URI"
            Expect.equal r.RelationType "http://example.com/alps#items" "should strip existing fragment and append new one"

        // F-6: collision-free separator (~)
        testCase "routes with hyphens in segments don't collide with multi-segment routes" <| fun _ ->
            let res1 = resource "/a-b/c" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let res2 = resource "/a/b-c" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let allEndpoints = Array.append res1.Endpoints res2.Endpoints
            let dataSource = TestEndpointDataSource(allEndpoints)
            let metadata: JsonHomeMetadata =
                { Title = None
                  DocsUrl = None
                  AlpsBaseUri = Some "http://example.com/alps/test"
                  AlpsDescriptors = Some (Map.ofList [ "a-b", Map.empty; "a", Map.empty ]) }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let rels = result.Input.Resources |> List.map _.RelationType
            Expect.equal rels.Length 2 "should have two resources"
            Expect.notEqual rels.[0] rels.[1] "relation types must be distinct (no collision)"

        // #201: AlpsBaseUri must be absolute
        testCase "relative AlpsBaseUri falls back to URN" <| fun _ ->
            let res = resource "/games/{gameId}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let metadata: JsonHomeMetadata =
                { Title = None
                  DocsUrl = None
                  AlpsBaseUri = Some "/alps/tictactoe"
                  AlpsDescriptors = Some (Map.ofList [ "games", Map.empty ]) }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let r = result.Input.Resources.[0]
            Expect.isTrue (r.RelationType.StartsWith("urn:frank:")) "relative AlpsBaseUri should fall back to URN"

        // #201: empty string AlpsBaseUri falls back to URN
        testCase "empty string AlpsBaseUri falls back to URN" <| fun _ ->
            let res = resource "/games/{gameId}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let metadata: JsonHomeMetadata =
                { Title = None
                  DocsUrl = None
                  AlpsBaseUri = Some ""
                  AlpsDescriptors = Some (Map.ofList [ "games", Map.empty ]) }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let r = result.Input.Resources.[0]
            Expect.isTrue (r.RelationType.StartsWith("urn:frank:")) "empty AlpsBaseUri should fall back to URN"

        // #201: absolute http:// URI works correctly
        testCase "absolute http URI produces ALPS relation type" <| fun _ ->
            let res = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let metadata: JsonHomeMetadata =
                { Title = None
                  DocsUrl = None
                  AlpsBaseUri = Some "http://api.example.com/alps"
                  AlpsDescriptors = Some (Map.ofList [ "items", Map.empty ]) }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let r = result.Input.Resources.[0]
            Expect.equal r.RelationType "http://api.example.com/alps#items" "http:// URI should produce valid ALPS relation"

        // #201: absolute https:// URI works correctly
        testCase "absolute https URI produces ALPS relation type" <| fun _ ->
            let res = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let metadata: JsonHomeMetadata =
                { Title = None
                  DocsUrl = None
                  AlpsBaseUri = Some "https://secure.example.com/alps"
                  AlpsDescriptors = Some (Map.ofList [ "items", Map.empty ]) }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            let r = result.Input.Resources.[0]
            Expect.equal r.RelationType "https://secure.example.com/alps#items" "https:// URI should produce valid ALPS relation"
    ]

[<Tests>]
let entryPointTests =
    testList "JsonHomeProjection entry-point filtering" [
        testCase "resource with entryPoint appears in JSON Home; resource without it does not" <| fun _ ->
            let entryRes =
                resource "/games" {
                    entryPoint
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("games")))
                }
            let hiddenRes =
                resource "/internal/metrics" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("metrics")))
                }
            let allEndpoints = Array.append entryRes.Endpoints hiddenRes.Endpoints
            let dataSource = TestEndpointDataSource(allEndpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            Expect.equal result.Input.Resources.Length 1 "should have only entry-point resource"
            Expect.equal result.Input.Resources.[0].RouteTemplate "/games" "should be the entry-point resource"
            Expect.isFalse result.UsedFallback "should not use fallback when entry points exist"

        testCase "when no resources are marked, all appear (fallback)" <| fun _ ->
            let res1 = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items"))) }
            let res2 = resource "/orders" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("orders"))) }
            let allEndpoints = Array.append res1.Endpoints res2.Endpoints
            let dataSource = TestEndpointDataSource(allEndpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            Expect.equal result.Input.Resources.Length 2 "should have both resources in fallback"
            Expect.isTrue result.UsedFallback "should indicate fallback was used"

        testCase "multiple entry points all appear" <| fun _ ->
            let res1 =
                resource "/games" {
                    entryPoint
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("games")))
                }
            let res2 =
                resource "/players" {
                    entryPoint
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("players")))
                }
            let res3 =
                resource "/admin/stats" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("stats")))
                }
            let allEndpoints = Array.concat [| res1.Endpoints; res2.Endpoints; res3.Endpoints |]
            let dataSource = TestEndpointDataSource(allEndpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            Expect.equal result.Input.Resources.Length 2 "should have two entry-point resources"
            let templates = result.Input.Resources |> List.map _.RouteTemplate |> List.sort
            Expect.equal templates [ "/games"; "/players" ] "should contain both entry-point resources"
            Expect.isFalse result.UsedFallback "should not use fallback"

        testCase "entryPoint metadata survives endpoint build and is visible on Endpoint.Metadata" <| fun _ ->
            let res =
                resource "/test" {
                    entryPoint
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("test")))
                }
            let ep = res.Endpoints.[0]
            let marker = ep.Metadata.GetMetadata<EntryPointMetadata>()
            Expect.isNotNull (box marker) "should have EntryPointMetadata on endpoint"
            Expect.isTrue marker.IsEntryPoint "should be marked as entry point"
    ]
