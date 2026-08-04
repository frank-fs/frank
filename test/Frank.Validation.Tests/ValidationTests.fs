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

/// A one-triple graph whose object is a plain literal -- the data shape a TargetSpec.ObjectsOf shape
/// needs to produce a LITERAL focus node (final-review finding C1).
let private graphWithLiteralObject (predicate: string) (literal: string) : IGraph =
    let g = Graph() :> IGraph

    g.Assert(
        Triple(
            g.CreateUriNode(UriFactory.Create "https://example.org/s1"),
            g.CreateUriNode(UriFactory.Create predicate),
            g.CreateLiteralNode literal
        )
    )
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

                  Expect.equal
                      v.FocusNode
                      (Value.Node(Node.Iri "https://example.org/move2"))
                      "focus node is the instance"

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

          // Regression, final-review finding C1: SHACL focus nodes can be LITERALS, reachable via
          // TargetSpec.ObjectsOf. Violation.FocusNode used to be typed Frank.Rdf.Node (Iri | Blank
          // only), so Shacl.fs's nodeOf fell through to Node.Iri (n.ToString()) and fabricated a
          // garbage IRI, which then crashed Frank.Rdf's resolveIri with an unhandled exception the
          // moment reportToDoc/toJsonLd tried to serialize it for the 422 ld+json response.
          test "a literal focus node (TargetSpec.ObjectsOf) is reported as a literal, not a fabricated IRI" {
              let shape =
                  recordShape
                      [ TargetSpec.ObjectsOf(Uri "https://schema.org/name") ]
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]

              let sg = Shacl.toShapesGraph [ shape ]
              let dataGraph = graphWithLiteralObject "https://schema.org/name" "Alice"

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> failtest "expected Violates -- the literal has no schema:x"
              | ValidationOutcome.Violates violations ->
                  Expect.isNonEmpty violations "violation reported"

                  Expect.equal
                      violations.Head.FocusNode
                      (Value.Literal(Literal.String "Alice"))
                      "focus node is the literal itself, not Node.Iri \"Alice\""
          }

          test "a literal focus node round-trips through reportToDoc/toJsonLd without raising" {
              let shape =
                  recordShape
                      [ TargetSpec.ObjectsOf(Uri "https://schema.org/name") ]
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]

              let sg = Shacl.toShapesGraph [ shape ]
              let dataGraph = graphWithLiteralObject "https://schema.org/name" "Alice"

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> failtest "expected Violates"
              | ValidationOutcome.Violates violations ->
                  let doc = Shacl.reportToDoc violations

                  Expect.exists
                      doc.Statements
                      (fun (_, p, v) -> p = "sh:focusNode" && v = Value.Literal(Literal.String "Alice"))
                      "sh:focusNode carries a literal object, not an IRI"

                  // The actual C1 crash: Doc.toJsonLd -> Doc.toGraph -> resolveIri on the fabricated
                  // "Alice^^http://www.w3.org/2001/XMLSchema#string" IRI.
                  let json = Doc.toJsonLd doc
                  Expect.stringContains json "Alice" "the literal survives serialization"
          }

          test "a typed literal focus node keeps its datatype-mapped Frank.Rdf.Literal case" {
              let shape =
                  recordShape
                      [ TargetSpec.ObjectsOf(Uri "https://schema.org/position") ]
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]

              let sg = Shacl.toShapesGraph [ shape ]

              let dataGraph =
                  let g = Graph() :> IGraph

                  g.Assert(
                      Triple(
                          g.CreateUriNode(UriFactory.Create "https://example.org/move1"),
                          g.CreateUriNode(UriFactory.Create "https://schema.org/position"),
                          (3).ToLiteral g
                      )
                  )
                  |> ignore

                  g

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> failtest "expected Violates"
              | ValidationOutcome.Violates violations ->
                  Expect.equal
                      violations.Head.FocusNode
                      (Value.Literal(Literal.Int 3))
                      "xsd:integer literal maps back to Literal.Int"
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
