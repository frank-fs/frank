module Frank.Rdf.Tests.DescribeBuilderTests

open System
open Expecto
open Frank.Rdf

[<Tests>]
let tests =
    testList
        "describe { }"
        [ test "typ asserts rdf:type with the given CURIE, unresolved" {
              let d = describe (Node.Iri "https://example.org/g1") { typ "schema:Game" }

              Expect.equal
                  d.Statements
                  [ "http://www.w3.org/1999/02/22-rdf-syntax-ns#type", Value.Node(Node.Iri "schema:Game") ]
                  ""
          }

          test "typ can be called more than once, asserting multiple types" {
              let d =
                  describe (Node.Iri "https://example.org/g1") {
                      typ "schema:Game"
                      typ "schema:CreativeWork"
                  }

              Expect.equal d.Statements.Length 2 "Two rdf:type statements"
          }

          test "propertyString/Int/Bool/DateTime/Node operations wrap plain values into the right Value/Literal case" {
              let d =
                  describe (Node.Iri "https://example.org/g1") {
                      propertyString "schema:name" "Tic-tac-toe"
                      propertyInt "schema:numberOfPlayers" 2
                      propertyBool "schema:isFree" true
                      propertyDateTime "schema:datePublished" (DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
                      propertyNode "schema:sameAs" (Node.Iri "http://www.wikidata.org/entity/Q210339")
                  }

              Expect.equal
                  d.Statements
                  [ "schema:name", Value.Literal(Literal.String "Tic-tac-toe")
                    "schema:numberOfPlayers", Value.Literal(Literal.Int 2)
                    "schema:isFree", Value.Literal(Literal.Bool true)
                    "schema:datePublished",
                    Value.Literal(Literal.DateTime(DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)))
                    "schema:sameAs", Value.Node(Node.Iri "http://www.wikidata.org/entity/Q210339") ]
                  ""
          }

          test "propertyLangString wraps a value and BCP47 language tag into Literal.LangString" {
              let d =
                  describe (Node.Iri "https://example.org/g1") { propertyLangString "schema:name" "Tic-tac-toe" "en" }

              Expect.equal
                  d.Statements
                  [ "schema:name", Value.Literal(Literal.LangString("Tic-tac-toe", "en")) ]
                  ""
          }

          test "property can be called more than once for the same predicate (multi-valued property)" {
              let d =
                  describe (Node.Iri "https://example.org/g1") {
                      propertyNode "schema:sameAs" (Node.Iri "http://www.wikidata.org/entity/Q210339")
                      propertyNode "schema:sameAs" (Node.Iri "http://dbpedia.org/resource/Tic-tac-toe")
                  }

              Expect.equal d.Statements.Length 2 "Two separate sameAs statements, not overwritten"
          }

          test "an empty describe block produces a Description with no statements" {
              let d = describe (Node.Iri "https://example.org/g1") { () }
              Expect.equal d.Statements [] ""
          }

          test "Subject is the value passed to describe" {
              let subject = Node.Iri "https://example.org/g1"
              let d = describe subject { typ "schema:Game" }
              Expect.equal d.Subject subject ""
          } ]
