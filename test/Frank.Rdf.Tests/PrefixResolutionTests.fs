module Frank.Rdf.Tests.PrefixResolutionTests

open Expecto
open Frank.Rdf

[<Tests>]
let tests =
    testList
        "Prefix resolution"
        [ test "resolves a CURIE against a declared prefix" {
              let prefixes = [ "schema", "https://schema.org/" ]
              Expect.equal (resolveIri prefixes "schema:Game") "https://schema.org/Game" ""
          }

          test "a declared prefix takes priority even when the raw CURIE also parses as a well-formed URI" {
              // "schema:Game" is itself syntactically a valid absolute URI (scheme "schema", opaque
              // part "Game") under System.Uri's loose rules. The declared prefix must win regardless --
              // this is the exact bug this function exists to avoid.
              let prefixes = [ "schema", "https://schema.org/" ]
              Expect.equal (resolveIri prefixes "schema:name") "https://schema.org/name" ""
          }

          test "passes an absolute IRI through unchanged when its scheme isn't a declared prefix" {
              let prefixes = [ "schema", "https://schema.org/" ]

              Expect.equal
                  (resolveIri prefixes "http://www.wikidata.org/entity/Q210339")
                  "http://www.wikidata.org/entity/Q210339"
                  ""

              Expect.equal
                  (resolveIri prefixes "https://tictactoe.example/games/1#players")
                  "https://tictactoe.example/games/1#players"
                  ""
          }

          test "raises for an undeclared prefix that isn't an absolute IRI either" {
              Expect.throws (fun () -> resolveIri [] "schema:Game" |> ignore) "No declared prefixes at all"
          }

          test "raises for a string with no colon" {
              Expect.throws (fun () -> resolveIri [] "Game" |> ignore) ""
          }

          test "validatePrefixes accepts the same prefix declared twice with the same URI" {
              validatePrefixes [ "schema", "https://schema.org/"; "schema", "https://schema.org/" ]
          }

          test "validatePrefixes raises when the same prefix is declared with two different URIs" {
              Expect.throws
                  (fun () -> validatePrefixes [ "schema", "https://schema.org/"; "schema", "https://example.org/" ])
                  ""
          } ]
