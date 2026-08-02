module Frank.Rdf.Tests.ToGraphTests

open Expecto
open VDS.RDF
open Frank.Rdf

[<Tests>]
let tests =
    testList
        "Doc.toGraph"
        [ test "asserts one triple per statement, IRIs resolved" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"
                      about (describe (Node.Iri "https://example.org/g1") { typ "schema:Game" })
                  }

              let graph = Doc.toGraph doc

              Expect.equal graph.Triples.Count 1 "One triple"
              let t = graph.Triples |> Seq.exactlyOne
              Expect.equal (t.Subject :?> IUriNode).Uri.AbsoluteUri "https://example.org/g1" "Subject resolved"
              Expect.equal (t.Predicate :?> IUriNode).Uri.AbsoluteUri RdfTypeIri "rdf:type"
              Expect.equal (t.Object :?> IUriNode).Uri.AbsoluteUri "https://schema.org/Game" "CURIE object resolved"
          }

          test "multi-valued properties become multiple distinct triples with the same subject/predicate" {
              let doc =
                  rdf {
                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              propertyNode "schema:sameAs" (Node.Iri "http://www.wikidata.org/entity/Q210339")
                              propertyNode "schema:sameAs" (Node.Iri "http://dbpedia.org/resource/Tic-tac-toe")
                          }
                      )
                  }

              let graph = Doc.toGraph doc
              Expect.equal graph.Triples.Count 2 "Two separate triples, not overwritten"
          }

          test "the same Node.Blank label always resolves to the same graph node" {
              let players = Node.blank ()

              let doc =
                  rdf {
                      about (describe (Node.Iri "https://example.org/g1") { propertyNode "schema:numberOfPlayers" players })
                      about (describe players { propertyInt "schema:value" 2 })
                  }

              let graph = Doc.toGraph doc
              let objectOfFirst = (graph.Triples |> Seq.item 0).Object
              let subjectOfSecond = (graph.Triples |> Seq.item 1).Subject
              Expect.equal objectOfFirst subjectOfSecond "Same blank node object identity"
          }

          test "two different Node.blank values never resolve to the same graph node" {
              let a, b = Node.blank (), Node.blank ()
              let doc = rdf { triple a "schema:x" (Value.Node b) }
              let graph = Doc.toGraph doc
              let t = graph.Triples |> Seq.exactlyOne
              Expect.notEqual t.Subject t.Object "Distinct blank nodes"
          }

          test "typed literals round-trip their CLR values" {
              let doc =
                  rdf {
                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              propertyString "schema:name" "Tic-tac-toe"
                              propertyInt "schema:numberOfPlayers" 2
                              propertyBool "schema:isFree" true
                          }
                      )
                  }

              let graph = Doc.toGraph doc
              let literalValues = graph.Triples |> Seq.map (fun t -> (t.Object :?> ILiteralNode).Value) |> Set.ofSeq
              Expect.isTrue (literalValues.Contains "Tic-tac-toe") ""
              Expect.isTrue (literalValues.Contains "2") ""
              Expect.isTrue (literalValues.Contains "true") ""
          }

          test "conflicting prefix declarations throw before any graph is built" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"
                      prefix "schema" "https://example.org/"
                  }

              Expect.throws (fun () -> Doc.toGraph doc |> ignore) ""
          }

          test "undeclared prefix throws with the CURIE named in the message" {
              // Deliberately not "schema:Game" -- that string is itself a well-formed absolute URI
              // under System.Uri's loose rules (see Task 2's PrefixResolutionTests.fs), so with no
              // declared prefix it would pass through unchanged rather than raise. The unescaped space
              // here is what makes this string genuinely neither a resolvable CURIE nor a well-formed
              // absolute IRI, so it actually exercises the raise path this test is named for.
              let doc = rdf { about (describe (Node.Iri "https://example.org/g1") { typ "schema:Game Object" }) }
              // No `prefix "schema" ...` declared above.
              Expect.throwsC
                  (fun () -> Doc.toGraph doc |> ignore)
                  (fun ex -> Expect.stringContains ex.Message "schema:Game Object" "Names the offending CURIE")
          } ]
