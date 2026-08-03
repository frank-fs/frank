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
              // Deliberately not "schema:Game" -- that string is itself a well-formed absolute URI
              // under System.Uri's loose rules (see the test above), so with no declared prefixes it
              // would pass through unchanged rather than raise. The unescaped space here is what makes
              // this string genuinely neither a resolvable CURIE nor a well-formed absolute IRI.
              Expect.throws (fun () -> resolveIri [] "schema:Game Object" |> ignore) "No declared prefixes at all"
          }

          test "raises for a typo'd, undeclared CURIE that looks well-formed under System.Uri's loose rules" {
              // "foaf:name" and "schema:Game" are both syntactically well-formed absolute URIs under
              // System.Uri's loose rules (scheme + opaque part), which is exactly the gap this behavior
              // change closes: neither contains "://" nor uses an allow-listed non-hierarchical scheme,
              // so they must now raise instead of silently becoming the literal IRI <foaf:name>.
              Expect.throws (fun () -> resolveIri [] "foaf:name" |> ignore) "Undeclared prefix, no declared prefixes"
              Expect.throws (fun () -> resolveIri [] "schema:Game" |> ignore) "Undeclared prefix, no declared prefixes"
          }

          test "passes allow-listed non-hierarchical schemes through unchanged" {
              Expect.equal (resolveIri [] "urn:isbn:0451450523") "urn:isbn:0451450523" ""
              Expect.equal (resolveIri [] "mailto:someone@example.org") "mailto:someone@example.org" ""
          }

          test "still raises for an allow-listed scheme prefix that isn't actually well-formed" {
              // The allow-list only loosens the "looks absolute" gate -- Uri.IsWellFormedUriString still
              // has to agree, so a malformed string starting with an allow-listed scheme still raises.
              Expect.throws (fun () -> resolveIri [] "mailto:not an email" |> ignore) "Allow-listed scheme, still malformed"
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
