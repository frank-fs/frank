module Frank.Rdf.Tests.RdfTypesTests

open Expecto
open Frank.Rdf

[<Tests>]
let tests =
    testList
        "RdfTypes"
        [ test "Doc.Empty has no prefixes or statements" {
              Expect.equal Doc.Empty.Prefixes [] "No prefixes"
              Expect.equal Doc.Empty.Statements [] "No statements"
          }

          test "Node.blank mints a fresh globally-unique label each call" {
              let a = Node.blank ()
              let b = Node.blank ()

              match a, b with
              | Node.Blank idA, Node.Blank idB -> Expect.notEqual idA idB "Two calls never share a label"
              | _ -> failwith "Node.blank must return Node.Blank"
          }

          test "Node.Iri and Node.Blank are structurally distinguishable" {
              Expect.notEqual (Node.Iri "https://example.org/x") (Node.Blank "x") "Different cases"
          } ]
