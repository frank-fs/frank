module Frank.Validation.Tests.UriConventionTests

open System
open Expecto
open Frank.Validation

[<Tests>]
let resolveShapeUriTests =
    testList
        "UriConventions.resolveShapeUri"
        [
          // #174: HTTP-dereferenceable shape URIs with baseUri
          testCase "resolveShapeUri with baseUri produces HTTP URI with slug and fragment"
          <| fun _ ->
              let uri = UriConventions.resolveShapeUri (Some "http://example.com/api") "games" "MakeMove"
              Expect.equal (uri.ToString()) "http://example.com/api/shapes/games#MakeMove" "Should produce HTTP shape URI with fragment"

          testCase "resolveShapeUri with baseUri and trailing slash normalizes correctly"
          <| fun _ ->
              let uri = UriConventions.resolveShapeUri (Some "http://example.com/api/") "games" "MakeMove"
              Expect.equal (uri.ToString()) "http://example.com/api/shapes/games#MakeMove" "Should normalize trailing slash"

          testCase "resolveShapeUri without baseUri falls back to URN scheme"
          <| fun _ ->
              let uri = UriConventions.resolveShapeUri None "games" "MakeMove"
              let uriStr = uri.ToString()
              Expect.isTrue (uriStr.StartsWith("urn:frank:shape:")) "Should use URN scheme when no baseUri"
              Expect.isTrue (uriStr.Contains("games")) "Should contain slug"
              Expect.isTrue (uriStr.Contains("MakeMove")) "Should contain type name"

          testCase "resolveShapeUri with empty baseUri falls back to URN scheme"
          <| fun _ ->
              let uri = UriConventions.resolveShapeUri (Some "") "games" "MakeMove"
              let uriStr = uri.ToString()
              Expect.isTrue (uriStr.StartsWith("urn:frank:shape:")) "Should use URN scheme for empty baseUri"

          testCase "resolveShapeUri produces valid absolute URI"
          <| fun _ ->
              let uri = UriConventions.resolveShapeUri (Some "https://api.example.com") "games" "GameState"
              Expect.isTrue uri.IsAbsoluteUri "Should produce absolute URI"
              Expect.equal uri.Scheme "https" "Should preserve HTTPS scheme"
              Expect.equal uri.Fragment "#GameState" "Fragment should be the type name" ]
