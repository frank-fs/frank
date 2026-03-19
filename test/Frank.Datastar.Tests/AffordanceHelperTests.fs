module AffordanceHelperTests

open Expecto
open Frank.Datastar.AffordanceHelper

let ticTacToeMap: AffordanceMap =
    { Version = "1.0"
      BaseUri = "https://example.com/alps"
      Entries =
          Map.ofList
              [ "/games/{gameId}|XTurn",
                { AllowedMethods = [ "GET"; "POST" ]
                  LinkRelations =
                      [ { Rel = "https://example.com/alps/games#makeMove"
                          Href = "/games/{gameId}"
                          Method = "POST"
                          Title = Some "Make Move" } ]
                  ProfileUrl = Some "https://example.com/alps/games" }

                "/games/{gameId}|OTurn",
                { AllowedMethods = [ "GET"; "POST" ]
                  LinkRelations =
                      [ { Rel = "https://example.com/alps/games#makeMove"
                          Href = "/games/{gameId}"
                          Method = "POST"
                          Title = Some "Make Move" } ]
                  ProfileUrl = Some "https://example.com/alps/games" }

                "/games/{gameId}|Won",
                { AllowedMethods = [ "GET" ]
                  LinkRelations = []
                  ProfileUrl = Some "https://example.com/alps/games" }

                "/games/{gameId}|Draw",
                { AllowedMethods = [ "GET" ]
                  LinkRelations = []
                  ProfileUrl = Some "https://example.com/alps/games" } ] }

[<Tests>]
let affordanceHelperTests =
    testList "AffordanceHelper" [

        testCase "XTurn state returns GET and POST" <| fun () ->
            let result = affordancesFor "/games/{gameId}" "XTurn" (Some ticTacToeMap)
            Expect.isTrue result.CanGet "XTurn should allow GET"
            Expect.isTrue result.CanPost "XTurn should allow POST"
            Expect.isFalse result.CanPut "XTurn should not allow PUT"
            Expect.isFalse result.CanDelete "XTurn should not allow DELETE"
            Expect.isFalse result.CanPatch "XTurn should not allow PATCH"
            Expect.equal result.AllowedMethods [ "GET"; "POST" ] "XTurn methods"

        testCase "Won state returns GET only" <| fun () ->
            let result = affordancesFor "/games/{gameId}" "Won" (Some ticTacToeMap)
            Expect.isTrue result.CanGet "Won should allow GET"
            Expect.isFalse result.CanPost "Won should not allow POST"
            Expect.isFalse result.CanPut "Won should not allow PUT"
            Expect.isFalse result.CanDelete "Won should not allow DELETE"
            Expect.equal result.LinkRelations [] "Won should have no transition links"

        testCase "OTurn state has link relations" <| fun () ->
            let result = affordancesFor "/games/{gameId}" "OTurn" (Some ticTacToeMap)
            Expect.equal result.LinkRelations.Length 1 "OTurn should have 1 link relation"
            Expect.equal result.LinkRelations.[0].Rel "https://example.com/alps/games#makeMove" "Link rel"
            Expect.equal result.LinkRelations.[0].Method "POST" "Link method"
            Expect.equal result.LinkRelations.[0].Title (Some "Make Move") "Link title"

        testCase "Missing state key returns permissive default" <| fun () ->
            let result = affordancesFor "/games/{gameId}" "UnknownState" (Some ticTacToeMap)
            Expect.isTrue result.CanGet "Unknown state should allow GET (permissive)"
            Expect.isTrue result.CanPost "Unknown state should allow POST (permissive)"
            Expect.isTrue result.CanPut "Unknown state should allow PUT (permissive)"
            Expect.isTrue result.CanDelete "Unknown state should allow DELETE (permissive)"
            Expect.isTrue result.CanPatch "Unknown state should allow PATCH (permissive)"
            Expect.equal result.LinkRelations [] "Permissive default has no link relations"

        testCase "No map (None) returns permissive default" <| fun () ->
            let result = affordancesFor "/games/{gameId}" "XTurn" None
            Expect.isTrue result.CanGet "No map should allow GET (permissive)"
            Expect.isTrue result.CanPost "No map should allow POST (permissive)"
            Expect.isTrue result.CanPut "No map should allow PUT (permissive)"
            Expect.isTrue result.CanDelete "No map should allow DELETE (permissive)"
            Expect.isTrue result.CanPatch "No map should allow PATCH (permissive)"

        testCase "Wildcard key for stateless resource" <| fun () ->
            let statelessMap =
                { ticTacToeMap with
                    Entries =
                        Map.ofList
                            [ "/health|*",
                              { AllowedMethods = [ "GET" ]
                                LinkRelations = []
                                ProfileUrl = None } ] }

            let result = affordancesFor "/health" "anything" (Some statelessMap)
            Expect.isTrue result.CanGet "Wildcard should resolve GET"
            Expect.isFalse result.CanPost "Wildcard should not allow POST"
            Expect.isFalse result.CanPut "Wildcard should not allow PUT"

        testCase "Empty string state key falls to permissive default" <| fun () ->
            let result = affordancesFor "/games/{gameId}" "" (Some ticTacToeMap)
            Expect.isTrue result.CanGet "Empty state key should be permissive (GET)"
            Expect.isTrue result.CanPost "Empty state key should be permissive (POST)"
            Expect.isTrue result.CanPut "Empty state key should be permissive (PUT)"
            Expect.isTrue result.CanDelete "Empty state key should be permissive (DELETE)"
            Expect.isTrue result.CanPatch "Empty state key should be permissive (PATCH)"

        testCase "Case-insensitive method matching" <| fun () ->
            let lowercaseMap =
                { ticTacToeMap with
                    Entries =
                        Map.ofList
                            [ "/items|*",
                              { AllowedMethods = [ "get"; "post" ]
                                LinkRelations = []
                                ProfileUrl = None } ] }

            let result = affordancesFor "/items" "*" (Some lowercaseMap)
            Expect.isTrue result.CanGet "Lowercase 'get' should set CanGet"
            Expect.isTrue result.CanPost "Lowercase 'post' should set CanPost"
            Expect.isFalse result.CanPut "Should not set CanPut"
            Expect.isFalse result.CanDelete "Should not set CanDelete"
            Expect.isFalse result.CanPatch "Should not set CanPatch"
    ]
