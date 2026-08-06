module Frank.Tests.MediaTypeNegotiationTests

open Expecto
open Frank.Builder

[<Tests>]
let tests =
    testList
        "MediaTypeNegotiation"
        [ testCase "selectRepresentation picks the exact match"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [ "application/json" ] [ "application/json"; "text/html" ]
              Expect.equal result (Some 0) "application/json is index 0"

          testCase "selectRepresentation honors quality values over registration order"
          <| fun () ->
              let result =
                  MediaTypeNegotiation.selectRepresentation
                      [ "text/html;q=0.3, application/json;q=0.8" ]
                      [ "text/html"; "application/json" ]
              Expect.equal result (Some 1) "application/json (index 1) has higher quality"

          testCase "selectRepresentation returns None when nothing matches"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [ "application/xml" ] [ "application/json" ]
              Expect.equal result None "No registered type matches application/xml"

          testCase "selectRepresentation treats an absent Accept as */* -- first registered wins"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [] [ "application/json"; "text/html" ]
              Expect.equal result (Some 0) "Empty Accept defaults to the first representation"

          testCase "selectRepresentation treats a malformed Accept as */* -- first registered wins"
          <| fun () ->
              let result =
                  MediaTypeNegotiation.selectRepresentation [ "not a media type at all;;;" ] [ "application/json"; "text/html" ]
              Expect.equal result (Some 0) "Malformed Accept defaults to the first representation"

          testCase "selectRepresentation rejects q=0 outright, not merely deprioritizes"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [ "application/json;q=0" ] [ "application/json" ]
              Expect.equal result None "q=0 must exclude the representation entirely"

          testCase "a wildcard registered representation matches any concrete Accept entry"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [ "image/png" ] [ "application/json"; "*/*" ]
              Expect.equal result (Some 1) "Only the wildcard entry matches image/png"

          testCase "application/ld+json Accept never matches a registered application/json via suffix leniency"
          <| fun () ->
              let result = MediaTypeNegotiation.selectRepresentation [ "application/ld+json" ] [ "application/json" ]
              Expect.equal result None "Concrete-vs-concrete comparison must be exact, not suffix-lenient"

          testCase "ProducesMediaTypeMetadata exposes MediaType and Ordinal"
          <| fun () ->
              let m = ProducesMediaTypeMetadata("application/json", 0)
              Expect.equal m.MediaType "application/json" "MediaType round-trips"
              Expect.equal m.Ordinal 0 "Ordinal round-trips" ]
