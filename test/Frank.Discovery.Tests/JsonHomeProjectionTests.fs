module Frank.Discovery.Tests.JsonHomeProjectionTests

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Microsoft.Extensions.FileProviders
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Discovery.Tests.JsonHomeMiddlewareTests

[<Tests>]
let tests =
    testList "JsonHomeProjection" [
        testCase "projects resource with href (no variables)" <| fun _ ->
            let res = resource "/health" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            Expect.equal result.Title "TestApp" "should use assembly name"
            Expect.equal result.Resources.Length 1 "should have one resource"
            let r = result.Resources.[0]
            Expect.equal r.RouteTemplate "/health" "route template"
            Expect.isTrue (Map.isEmpty r.RouteVariables) "no route variables"
            Expect.equal r.RelationType "urn:frank:TestApp/health" "URN fallback relation"

        testCase "projects resource with template variables using RoutePattern.Parameters" <| fun _ ->
            let res = resource "/games/{gameId}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            let r = result.Resources.[0]
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
            let r = result.Resources.[0]
            Expect.equal r.RelationType "http://example.com/alps/games#games-gameId" "ALPS-derived relation"
            Expect.equal r.RouteVariables.["gameId"] "http://example.com/alps/games#gameId" "ALPS-enriched hrefVar"
            Expect.equal result.Title "My Game API" "should use metadata title"

        testCase "collects HTTP methods into hints.allow" <| fun _ ->
            let res = resource "/items" {
                get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("list")))
                post (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("create")))
            }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let result = JsonHomeProjection.project dataSource None "TestApp"
            let r = result.Resources.[0]
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
            Expect.equal result.Resources.Length 1 "should only have user resource"
            Expect.equal result.Resources.[0].RouteTemplate "/items" "should be items"

        testCase "empty data source produces empty resources" <| fun _ ->
            let dataSource = TestEndpointDataSource([||])
            let result = JsonHomeProjection.project dataSource None "TestApp"
            Expect.isEmpty result.Resources "should have no resources"

        testCase "docsUrl from metadata flows to hints" <| fun _ ->
            let res = resource "/items" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("ok"))) }
            let dataSource = TestEndpointDataSource(res.Endpoints)
            let metadata: JsonHomeMetadata =
                { JsonHomeMetadata.Empty with DocsUrl = Some "/scalar/v1" }
            let result = JsonHomeProjection.project dataSource (Some metadata) "TestApp"
            Expect.equal result.Resources.[0].Hints.DocsUrl (Some "/scalar/v1") "should have docs URL"

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
            Expect.equal result.DescribedByUrl (Some "/.well-known/frank-profiles") "should detect profiles endpoint"

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
            let rels = result.Resources |> List.map _.RelationType
            Expect.equal rels.Length 2 "should have two resources"
            Expect.notEqual rels.[0] rels.[1] "relation types must be distinct (no slug collision)"
            Expect.isTrue (rels |> List.exists (fun r -> r.Contains("#games-gameId"))) "item route should have games-gameId fragment"
            Expect.isTrue (rels |> List.exists (fun r -> r.Contains("#games") && not (r.Contains("#games-")))) "list route should have games fragment"

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
            let rels = result.Resources |> List.map _.RelationType
            Expect.equal rels.Length 4 "should have four resources"
            // All relation types must be distinct
            Expect.equal (rels |> List.distinct |> List.length) 4 "all relation types must be distinct"
            Expect.isTrue (rels |> List.exists (fun r -> r.EndsWith("#games"))) "/games should have fragment 'games'"
            Expect.isTrue (rels |> List.exists (fun r -> r.EndsWith("#games-gameId"))) "/games/{gameId} should have fragment 'games-gameId'"
            Expect.isTrue (rels |> List.exists (fun r -> r.EndsWith("#games-gameId-moves"))) "/games/{gameId}/moves should have fragment 'games-gameId-moves'"
            Expect.isTrue (rels |> List.exists (fun r -> r.EndsWith("#games-gameId-moves-moveId"))) "/games/{gameId}/moves/{moveId} should have fragment 'games-gameId-moves-moveId'"

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
            let r = result.Resources.[0]
            Expect.equal r.RelationType "http://example.com/alps/items#items" "single route should produce simple fragment"

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
            let r = result.Resources.[0]
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
            let r = result.Resources.[0]
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
            let r = result.Resources.[0]
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
            let r = result.Resources.[0]
            Expect.equal r.RelationType "https://secure.example.com/alps#items" "https:// URI should produce valid ALPS relation"
    ]
