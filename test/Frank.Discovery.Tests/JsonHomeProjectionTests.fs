module Frank.Discovery.Tests.JsonHomeProjectionTests

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Microsoft.Extensions.FileProviders
open Expecto
open Frank.Builder
open Frank.Discovery
open Frank.Discovery.Tests.OptionsDiscoveryTests

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
            Expect.equal r.RelationType "http://example.com/alps/games#games" "ALPS-derived relation"
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
    ]
