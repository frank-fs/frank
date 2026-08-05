module Frank.Validation.Tests.ReportRoundTripTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open VDS.RDF
open VDS.RDF.Parsing

let private parseBackToGraph (json: string) : IGraph =
    let store = TripleStore()
    use reader = new System.IO.StringReader(json)
    JsonLdParser().Load(store, reader)
    store.Graphs |> Seq.head

[<Tests>]
let tests =
    testList
        "Shacl.reportToDoc"
        [ test "a conforming (empty) violation list produces sh:conforms true and no sh:result" {
              let doc = Shacl.reportToDoc []

              Expect.exists
                  doc.Statements
                  (fun (_, p, v) -> p = "sh:conforms" && v = Value.Literal(Literal.Bool true))
                  "sh:conforms true"

              Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:result") "no sh:result entries"
          }

          test "one violation produces sh:conforms false and one sh:result carrying every field" {
              let v: Violation =
                  { FocusNode = Value.Node(Node.Iri "https://example.org/move1")
                    ResultPath = Some(Uri "https://schema.org/position")
                    Severity = Severity.Violation
                    Message = "position is required"
                    ConstraintComponent = Uri "http://www.w3.org/ns/shacl#MinCountConstraintComponent"
                    SourceShape = Node.Iri "https://schema.org/MoveAction" }

              let doc = Shacl.reportToDoc [ v ]

              Expect.exists
                  doc.Statements
                  (fun (_, p, va) -> p = "sh:conforms" && va = Value.Literal(Literal.Bool false))
                  "sh:conforms false"

              Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:result") "sh:result present"

              Expect.exists
                  doc.Statements
                  (fun (_, p, va) -> p = "sh:focusNode" && va = Value.Node(Node.Iri "https://example.org/move1"))
                  "sh:focusNode"

              Expect.exists
                  doc.Statements
                  (fun (_, p, va) ->
                      p = "sh:resultMessage"
                      && va = Value.Literal(Literal.String "position is required"))
                  "sh:resultMessage"

              Expect.exists
                  doc.Statements
                  (fun (_, p, va) -> p = "sh:resultPath" && va = Value.Node(Node.Iri "https://schema.org/position"))
                  "sh:resultPath present when Some"
          }

          test "a violation with ResultPath=None omits sh:resultPath entirely" {
              let v: Violation =
                  { FocusNode = Value.Node(Node.Iri "https://example.org/move1")
                    ResultPath = None
                    Severity = Severity.Violation
                    Message = "complex-path violation"
                    ConstraintComponent = Uri "http://www.w3.org/ns/shacl#AndConstraintComponent"
                    SourceShape = Node.Iri "https://schema.org/MoveAction" }

              let doc = Shacl.reportToDoc [ v ]
              Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:resultPath") "no sh:resultPath when None"
          }

          test "round-trip: reportToDoc |> Doc.toJsonLd, reparsed via dotNetRDF's own JSON-LD reader, is isomorphic" {
              let v: Violation =
                  { FocusNode = Value.Node(Node.Iri "https://example.org/move1")
                    ResultPath = Some(Uri "https://schema.org/position")
                    Severity = Severity.Warning
                    Message = "check this"
                    ConstraintComponent = Uri "http://www.w3.org/ns/shacl#DatatypeConstraintComponent"
                    SourceShape = Node.Iri "https://schema.org/MoveAction" }

              let doc = Shacl.reportToDoc [ v ]
              let original = Doc.toGraph doc
              let json = Doc.toJsonLd doc
              let reparsed = parseBackToGraph json
              Expect.isTrue (original.Equals(reparsed)) "original and reparsed graphs are isomorphic"
          } ]
