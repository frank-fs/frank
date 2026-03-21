module Frank.Discovery.Tests.JsonHomeDocumentTests

open Expecto
open Frank.Discovery

[<Tests>]
let tests =
    testList "JsonHomeDocument" [
        testCase "empty resources produces valid document" <| fun _ ->
            let input: JsonHomeInput =
                { Title = "TestApp"
                  DescribedByUrl = None
                  Resources = [] }

            let json = JsonHomeDocument.build input
            Expect.isTrue (json.Contains("\"resources\":{}")) "should have empty resources object"
            Expect.isTrue (json.Contains("\"title\":\"TestApp\"")) "should have title"

        testCase "resource with href (no template variables)" <| fun _ ->
            let input: JsonHomeInput =
                { Title = "TestApp"
                  DescribedByUrl = None
                  Resources =
                    [ { RelationType = "urn:frank:TestApp/health"
                        RouteTemplate = "/health"
                        RouteVariables = Map.empty
                        Hints =
                          { Allow = [ "GET" ]
                            Formats = [ "text/plain" ]
                            AcceptPost = None
                            AcceptPut = None
                            AcceptPatch = None
                            DocsUrl = None } } ] }

            let json = JsonHomeDocument.build input
            Expect.isTrue (json.Contains("\"href\":\"/health\"")) "should use href not hrefTemplate"
            Expect.isFalse (json.Contains("hrefTemplate")) "should not have hrefTemplate"
            Expect.isFalse (json.Contains("hrefVars")) "should not have hrefVars"

        testCase "resource with hrefTemplate and hrefVars" <| fun _ ->
            let input: JsonHomeInput =
                { Title = "TestApp"
                  DescribedByUrl = None
                  Resources =
                    [ { RelationType = "http://example.com/alps/games#game"
                        RouteTemplate = "/games/{gameId}"
                        RouteVariables = Map.ofList [ "gameId", "http://example.com/alps/games#gameId" ]
                        Hints =
                          { Allow = [ "GET"; "POST" ]
                            Formats = [ "application/json" ]
                            AcceptPost = Some [ "application/json" ]
                            AcceptPut = None
                            AcceptPatch = None
                            DocsUrl = None } } ] }

            let json = JsonHomeDocument.build input
            Expect.isTrue (json.Contains("\"hrefTemplate\":\"/games/{gameId}\"")) "should use hrefTemplate"
            Expect.isTrue (json.Contains("\"hrefVars\"")) "should have hrefVars"
            Expect.isFalse (json.Contains("\"href\"")) "should not have bare href"

        testCase "formats serialized as object, accept-post as array" <| fun _ ->
            let input: JsonHomeInput =
                { Title = "TestApp"
                  DescribedByUrl = None
                  Resources =
                    [ { RelationType = "urn:frank:TestApp/items"
                        RouteTemplate = "/items"
                        RouteVariables = Map.empty
                        Hints =
                          { Allow = [ "GET"; "POST" ]
                            Formats = [ "application/json"; "text/html" ]
                            AcceptPost = Some [ "application/json" ]
                            AcceptPut = None
                            AcceptPatch = None
                            DocsUrl = None } } ] }

            let json = JsonHomeDocument.build input
            // formats is an object: {"application/json":{},"text/html":{}}
            Expect.isTrue (json.Contains("\"application/json\":{}")) "formats should be object with empty value"
            // accept-post is an array: ["application/json"]
            Expect.isTrue (json.Contains("\"accept-post\":[\"application/json\"]")) "accept-post should be array"

        testCase "describedByUrl present adds api.links" <| fun _ ->
            let input: JsonHomeInput =
                { Title = "TestApp"
                  DescribedByUrl = Some "/.well-known/frank-profiles"
                  Resources = [] }

            let json = JsonHomeDocument.build input
            Expect.isTrue (json.Contains("\"describedBy\":\"/.well-known/frank-profiles\"")) "should have describedBy link"

        testCase "describedByUrl None omits api.links" <| fun _ ->
            let input: JsonHomeInput =
                { Title = "TestApp"
                  DescribedByUrl = None
                  Resources = [] }

            let json = JsonHomeDocument.build input
            Expect.isFalse (json.Contains("\"links\"")) "should not have links object"

        testCase "docsUrl present adds hints.docs" <| fun _ ->
            let input: JsonHomeInput =
                { Title = "TestApp"
                  DescribedByUrl = None
                  Resources =
                    [ { RelationType = "urn:frank:TestApp/items"
                        RouteTemplate = "/items"
                        RouteVariables = Map.empty
                        Hints =
                          { Allow = [ "GET" ]
                            Formats = [ "application/json" ]
                            AcceptPost = None
                            AcceptPut = None
                            AcceptPatch = None
                            DocsUrl = Some "/scalar/v1" } } ] }

            let json = JsonHomeDocument.build input
            Expect.isTrue (json.Contains("\"docs\":\"/scalar/v1\"")) "should have docs hint"

        testCase "docsUrl None omits hints.docs" <| fun _ ->
            let input: JsonHomeInput =
                { Title = "TestApp"
                  DescribedByUrl = None
                  Resources =
                    [ { RelationType = "urn:frank:TestApp/items"
                        RouteTemplate = "/items"
                        RouteVariables = Map.empty
                        Hints =
                          { Allow = [ "GET" ]
                            Formats = []
                            AcceptPost = None
                            AcceptPut = None
                            AcceptPatch = None
                            DocsUrl = None } } ] }

            let json = JsonHomeDocument.build input
            Expect.isFalse (json.Contains("\"docs\"")) "should not have docs hint"

        testCase "mixed ALPS and URN resources in same document" <| fun _ ->
            let input: JsonHomeInput =
                { Title = "TestApp"
                  DescribedByUrl = None
                  Resources =
                    [ { RelationType = "http://example.com/alps/games#game"
                        RouteTemplate = "/games/{gameId}"
                        RouteVariables = Map.ofList [ "gameId", "http://example.com/alps/games#gameId" ]
                        Hints =
                          { Allow = [ "GET" ]; Formats = []; AcceptPost = None
                            AcceptPut = None; AcceptPatch = None; DocsUrl = None } }
                      { RelationType = "urn:frank:TestApp/health"
                        RouteTemplate = "/health"
                        RouteVariables = Map.empty
                        Hints =
                          { Allow = [ "GET" ]; Formats = [ "text/plain" ]; AcceptPost = None
                            AcceptPut = None; AcceptPatch = None; DocsUrl = None } } ] }

            let json = JsonHomeDocument.build input
            Expect.isTrue (json.Contains("http://example.com/alps/games#game")) "should have ALPS relation"
            Expect.isTrue (json.Contains("urn:frank:TestApp/health")) "should have URN relation"
    ]
