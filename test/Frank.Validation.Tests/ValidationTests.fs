module Frank.Validation.Tests.ValidationTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions
open VDS.RDF

let private dataGraphWithType (classIri: string) (instanceIri: string) (extraTriples: (string * string) list) : IGraph =
    let g = new Graph() :> IGraph
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
    let g = new Graph() :> IGraph

    g.Assert(
        Triple(
            g.CreateUriNode(UriFactory.Create "https://example.org/s1"),
            g.CreateUriNode(UriFactory.Create predicate),
            g.CreateLiteralNode literal
        )
    )
    |> ignore

    g

/// An instance of `classIri` carrying one xsd:integer-typed property -- needed wherever a numeric
/// SPARQL FILTER or range constraint has to see a real integer, not a plain string literal.
let private graphWithIntProperty (classIri: string) (instanceIri: string) (position: int) : IGraph =
    let g = new Graph() :> IGraph
    let inst = g.CreateUriNode(UriFactory.Create instanceIri)

    g.Assert(Triple(inst, g.CreateUriNode(UriFactory.Create RdfTypeIri), g.CreateUriNode(UriFactory.Create classIri)))
    |> ignore

    g.Assert(Triple(inst, g.CreateUriNode(UriFactory.Create "https://schema.org/position"), position.ToLiteral g))
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
                  let g = new Graph() :> IGraph

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

          // Final-review finding C2, the half that IS a behavioural gap: sh:sparql was emission-
          // tested but never behaviour-tested, so nothing proved a SPARQL constraint fires at all.
          // (The ASK half is rejected at build time -- see ShaclToDocTests -- because SHACL's
          // sh:sparql is SELECT-based by definition and dotNetRDF maps it to its Select validator
          // unconditionally.)
          test "a SELECT-form sh:sparql constraint conforms when the query returns no rows" {
              let sc =
                  { Query = "SELECT $this WHERE { $this <https://schema.org/position> ?p . FILTER (?p <= 0) }"
                    Message = Some "position must be positive"
                    Prefixes = [] }

              let shape =
                  recordShape
                      (targetClass (Uri "https://schema.org/MoveAction"))
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.Sparql sc) ]

              let sg = Shacl.toShapesGraph [ shape ]

              let dataGraph =
                  graphWithIntProperty "https://schema.org/MoveAction" "https://example.org/move1" 3

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> ()
              | ValidationOutcome.Violates vs -> failtestf "expected Conforms, got %A" vs
          }

          test "a SELECT-form sh:sparql constraint violates once per row the query returns" {
              let sc =
                  { Query = "SELECT $this WHERE { $this <https://schema.org/position> ?p . FILTER (?p <= 0) }"
                    Message = Some "position must be positive"
                    Prefixes = [] }

              let shape =
                  recordShape
                      (targetClass (Uri "https://schema.org/MoveAction"))
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.Sparql sc) ]

              let sg = Shacl.toShapesGraph [ shape ]

              let dataGraph =
                  graphWithIntProperty "https://schema.org/MoveAction" "https://example.org/move2" -1

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> failtest "expected Violates -- the SELECT returns a row for position = -1"
              | ValidationOutcome.Violates violations ->
                  Expect.isNonEmpty violations "violation reported"

                  Expect.equal
                      violations.Head.ConstraintComponent
                      (Uri "http://www.w3.org/ns/shacl#SPARQLConstraintComponent")
                      "reported as a SPARQL constraint component"
          }

          // Final-review finding I2, the dangerous half: a CLOSED shape merging with an OPEN shape
          // over the same class silently expanded the closed shape's allowed-property set with the
          // open shape's paths -- data that must be rejected was accepted, with no diagnostic.
          test "a closed shape is NOT widened by a second, independent shape over the same class" {
              let closedToName =
                  recordShape
                      (targetClass (Uri "https://schema.org/Person"))
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]
                  |> function
                      | ShapeDecl.RecordShape spec ->
                          ShapeDecl.RecordShape
                              { spec with
                                  Closed = true
                                  IgnoredProperties = [ Uri RdfTypeIri ] }
                      | other -> other

              let requiresEmail =
                  recordShape
                      (targetClass (Uri "https://schema.org/Person"))
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/email"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]

              let sg = Shacl.toShapesGraph [ closedToName; requiresEmail ]

              let dataGraph =
                  let g = new Graph() :> IGraph
                  let inst = g.CreateUriNode(UriFactory.Create "https://example.org/p1")

                  g.Assert(
                      Triple(
                          inst,
                          g.CreateUriNode(UriFactory.Create RdfTypeIri),
                          g.CreateUriNode(UriFactory.Create "https://schema.org/Person")
                      )
                  )
                  |> ignore

                  g.Assert(
                      Triple(
                          inst,
                          g.CreateUriNode(UriFactory.Create "https://schema.org/name"),
                          g.CreateLiteralNode "Alice"
                      )
                  )
                  |> ignore

                  g.Assert(
                      Triple(
                          inst,
                          g.CreateUriNode(UriFactory.Create "https://schema.org/email"),
                          g.CreateLiteralNode "alice@example.org"
                      )
                  )
                  |> ignore

                  g

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms ->
                  failtest
                      "expected Violates -- schema:email is outside the closed shape's allowed set; conforming here means the two shapes merged"
              | ValidationOutcome.Violates violations ->
                  Expect.isNonEmpty violations "the closed shape still rejects schema:email"

                  Expect.isTrue
                      (violations
                       |> List.exists (fun v ->
                           v.ConstraintComponent = Uri "http://www.w3.org/ns/shacl#ClosedConstraintComponent"))
                      "and it is the closedness constraint that fires"
          }

          test "two independent shapes over one class are BOTH enforced" {
              let requiresName =
                  recordShape
                      (targetClass (Uri "https://schema.org/Person"))
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]

              let requiresEmail =
                  recordShape
                      (targetClass (Uri "https://schema.org/Person"))
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/email"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]

              let sg = Shacl.toShapesGraph [ requiresName; requiresEmail ]

              let dataGraph =
                  dataGraphWithType "https://schema.org/Person" "https://example.org/p2" []

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> failtest "expected Violates -- both name and email are missing"
              | ValidationOutcome.Violates violations ->
                  Expect.hasLength violations 2 "one violation per shape, neither swallowed by the other"

                  Expect.isTrue
                      (violations |> List.map (fun v -> v.SourceShape) |> List.distinct |> List.length = 2)
                      "reported against two DISTINCT source shapes"
          }

          test "an empty data graph conforms trivially against a targetClass shape (nothing to target)" {
              let shape =
                  recordShape
                      (targetClass (Uri "https://schema.org/MoveAction"))
                      [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                        |> addConstraint (PropertyConstraint.MinCount 1) ]

              let sg = Shacl.toShapesGraph [ shape ]
              let dataGraph = new Graph() :> IGraph

              match Shacl.validate sg dataGraph with
              | ValidationOutcome.Conforms -> ()
              | ValidationOutcome.Violates vs -> failtestf "expected Conforms on an empty graph, got %A" vs
          } ]
