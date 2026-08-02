module Frank.Rdf.Tests.RdfBuilderTests

open Expecto
open Frank.Rdf

[<Tests>]
let tests =
    testList
        "rdf { }"
        [ test "prefix accumulates declared prefixes in order" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"
                      prefix "foaf" "http://xmlns.com/foaf/0.1/"
                  }

              Expect.equal doc.Prefixes [ "schema", "https://schema.org/"; "foaf", "http://xmlns.com/foaf/0.1/" ] ""
          }

          test "about attaches a Description's statements, with the subject filled in" {
              let doc =
                  rdf {
                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              typ "schema:Game"
                              propertyString "schema:name" "Tic-tac-toe"
                          }
                      )
                  }

              Expect.equal
                  doc.Statements
                  [ Node.Iri "https://example.org/g1", RdfTypeIri, Value.Node(Node.Iri "schema:Game")
                    Node.Iri "https://example.org/g1", "schema:name", Value.Literal(Literal.String "Tic-tac-toe") ]
                  ""
          }

          test "two about calls for different subjects both land in Statements" {
              let doc =
                  rdf {
                      about (describe (Node.Iri "https://example.org/g1") { typ "schema:Game" })
                      about (describe (Node.Iri "https://example.org/g1#players") { typ "schema:QuantitativeValue" })
                  }

              Expect.equal doc.Statements.Length 2 ""
          }

          test "an empty describe block passed to about contributes nothing" {
              let doc = rdf { about (describe (Node.Iri "https://example.org/g1") { () }) }
              Expect.equal doc.Statements [] ""
          }

          test "triple asserts one raw statement directly, without a describe/about pair" {
              let doc =
                  rdf {
                      triple (Node.Iri "https://example.org/g1") "schema:name" (Value.Literal(Literal.String "X"))
                  }

              Expect.equal
                  doc.Statements
                  [ Node.Iri "https://example.org/g1", "schema:name", Value.Literal(Literal.String "X") ]
                  ""
          }

          test "prefix, about, and triple compose freely in one document" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"
                      about (describe (Node.Iri "https://example.org/g1") { typ "schema:Game" })
                      triple (Node.Iri "https://example.org/g1") "schema:extra" (Value.Literal(Literal.String "x"))
                  }

              Expect.equal doc.Prefixes.Length 1 "One prefix"
              Expect.equal doc.Statements.Length 2 "One from about, one from triple"
          } ]
