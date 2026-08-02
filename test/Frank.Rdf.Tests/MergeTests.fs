module Frank.Rdf.Tests.MergeTests

open Expecto
open Frank.Rdf

[<Tests>]
let tests =
    testList
        "Doc.merge / include"
        [ test "merges two documents' prefixes and statements" {
              let a = rdf { prefix "schema" "https://schema.org/" }
              let b = rdf { triple (Node.Iri "https://example.org/g1") "schema:name" (Value.Literal(Literal.String "x")) }

              let merged = Doc.merge a b

              Expect.equal merged.Prefixes a.Prefixes ""
              Expect.equal merged.Statements b.Statements ""
          }

          test "include inside rdf { } does the same thing as Doc.merge" {
              let other =
                  rdf { triple (Node.Iri "https://example.org/g1") "schema:name" (Value.Literal(Literal.String "x")) }

              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"
                      include other
                  }

              Expect.equal doc.Statements other.Statements ""
          }

          test "merging docs that declare the same prefix with the same URI is a no-op, not a conflict" {
              let a = rdf { prefix "schema" "https://schema.org/" }
              let b = rdf { prefix "schema" "https://schema.org/" }
              let merged = Doc.merge a b
              // Doesn't throw when built into a graph -- validatePrefixes tolerates exact duplicates.
              Doc.toGraph merged |> ignore
          }

          test "merging docs that declare the same prefix with different URIs raises when built" {
              let a = rdf { prefix "schema" "https://schema.org/" }
              let b = rdf { prefix "schema" "https://example.org/" }
              let merged = Doc.merge a b
              Expect.throws (fun () -> Doc.toGraph merged |> ignore) ""
          }

          test "two independently-built docs, each minting their own blank node, never collide when merged" {
              let docA =
                  let anon = Node.blank ()
                  rdf { triple anon "https://schema.org/value" (Value.Literal(Literal.Int 1)) }

              let docB =
                  let anon = Node.blank ()
                  rdf { triple anon "https://schema.org/value" (Value.Literal(Literal.Int 2)) }

              let merged = Doc.merge docA docB
              let graph = Doc.toGraph merged

              Expect.equal graph.Triples.Count 2 "Both statements present"
              let subjects = graph.Triples |> Seq.map (fun t -> t.Subject) |> Seq.distinct |> Seq.length
              Expect.equal subjects 2 "The two independently-minted blank nodes remain distinct after merge"
          } ]
