module Frank.Validation.Tests.ShapesTests

open System
open Expecto
open Frank.Semantic
open Frank.Validation
open VDS.RDF

let private dataGraph (classIri: string) (instanceIri: string) : IGraph =
    let g = new Graph() :> IGraph
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
    let inst = g.CreateUriNode(UriFactory.Create instanceIri)
    let rdfType = g.CreateUriNode(g.ResolveQName "rdf:type")

    g.Assert(Triple(inst, rdfType, g.CreateUriNode(UriFactory.Create classIri)))
    |> ignore

    g

[<Tests>]
let tests =
    testList
        "Shapes.toShapesGraph"
        [ test "nullary-union sh:in: focus node in list conforms (well-formed list, no RdfException)" {
              let shapes =
                  [ EnumShape(
                        Uri "https://schema.org/GameStatusType",
                        { Head = Uri "https://schema.org/ActiveActionStatus"
                          Tail = [ Uri "https://schema.org/CompletedActionStatus" ] }
                    ) ]

              use sg = Shapes.toShapesGraph shapes

              let report =
                  sg.Validate(dataGraph "https://schema.org/GameStatusType" "https://schema.org/ActiveActionStatus")

              Expect.isTrue report.Conforms "focus node present in sh:in list conforms"
          }
          test "nullary-union sh:in rejects focus node absent from the list" {
              let shapes =
                  [ EnumShape(
                        Uri "https://schema.org/GameStatusType",
                        { Head = Uri "https://schema.org/ActiveActionStatus"
                          Tail = [] }
                    ) ]

              use sg = Shapes.toShapesGraph shapes

              let report =
                  sg.Validate(dataGraph "https://schema.org/GameStatusType" "https://schema.org/UnknownStatus")

              Expect.isFalse report.Conforms "focus node absent from sh:in does not conform"
          }
          test "record shape with required int property: missing property does not conform" {
              let shapes =
                  [ RecordShape(
                        Uri "https://schema.org/MoveAction",
                        [ { Path = Uri "https://schema.org/position"
                            Datatype = Some XsdInteger
                            MinCount = 1
                            MaxCount = Some 1
                            Pattern = None } ]
                    ) ]

              use sg = Shapes.toShapesGraph shapes

              let report =
                  sg.Validate(dataGraph "https://schema.org/MoveAction" "https://example.org/move1")

              Expect.isFalse report.Conforms "missing required position → non-conforming (no RdfException)"
          } ]
