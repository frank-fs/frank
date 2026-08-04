module Frank.Validation.Tests.ValidationTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions
open VDS.RDF

let private dataGraphWithType (classIri: string) (instanceIri: string) (extraTriples: (string * string) list) : IGraph =
    let g = Graph() :> IGraph
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
    let inst = g.CreateUriNode(UriFactory.Create instanceIri)
    let rdfType = g.CreateUriNode(g.ResolveQName "rdf:type")

    g.Assert(Triple(inst, rdfType, g.CreateUriNode(UriFactory.Create classIri)))
    |> ignore

    for predicate, value in extraTriples do
        g.Assert(Triple(inst, g.CreateUriNode(UriFactory.Create predicate), g.CreateLiteralNode value))
        |> ignore

    g

[<Tests>]
let tests =
    testList
        "Shacl.validate"
        [ test "a conforming instance validates as Conforms" {
              let shape =
                  recordShape
                      (targetClass (Uri "https://schema.org/MoveAction"))
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]

              let sg = Shacl.toShapesGraph [ shape ]

              let dataGraph =
                  dataGraphWithType
                      "https://schema.org/MoveAction"
                      "https://example.org/move1"
                      [ "https://schema.org/position", "3" ]

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> ()
              | ValidationOutcome.Violates vs -> failtestf "expected Conforms, got %d violation(s): %A" vs.Length vs
          }

          test "a missing required property violates with a non-empty Violation list" {
              let shape =
                  recordShape
                      (targetClass (Uri "https://schema.org/MoveAction"))
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]

              let sg = Shacl.toShapesGraph [ shape ]

              let dataGraph =
                  dataGraphWithType "https://schema.org/MoveAction" "https://example.org/move2" []

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> failtest "expected Violates -- required position is missing"
              | ValidationOutcome.Violates violations ->
                  Expect.isNonEmpty violations "at least one violation"
                  let v = violations.Head
                  Expect.equal v.FocusNode (Node.Iri "https://example.org/move2") "focus node is the instance"
                  Expect.equal v.Severity Severity.Violation "default severity"
          }

          test "an enum (sh:in) violation reports the offending focus node" {
              let shape =
                  enumShape
                      (Uri "https://schema.org/GameStatusType")
                      (Uri "https://schema.org/Active")
                      [ Uri "https://schema.org/Completed" ]

              let sg = Shacl.toShapesGraph [ shape ]

              let dataGraph =
                  dataGraphWithType "https://schema.org/GameStatusType" "https://schema.org/Unknown" []

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> failtest "expected Violates -- Unknown is not in the sh:in list"
              | ValidationOutcome.Violates violations -> Expect.isNonEmpty violations "violation reported"
          }

          test "an empty data graph conforms trivially against a targetClass shape (nothing to target)" {
              let shape =
                  recordShape
                      (targetClass (Uri "https://schema.org/MoveAction"))
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]

              let sg = Shacl.toShapesGraph [ shape ]
              let dataGraph = Graph() :> IGraph

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> ()
              | ValidationOutcome.Violates vs -> failtestf "expected Conforms on an empty graph, got %A" vs
          } ]
