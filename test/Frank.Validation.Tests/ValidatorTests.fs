module Frank.Validation.Tests.ValidatorTests

open System
open Expecto
open VDS.RDF
open Frank.Semantic
open Frank.Validation

let private orderShape () =
    Shapes.toShapesGraph
        [ RecordShape(
              Uri "https://schema.org/Order",
              [ { Path = Uri "https://schema.org/totalPaymentDue"
                  Datatype = Some XsdDecimal
                  MinCount = 1
                  MaxCount = None
                  Pattern = None } ]
          ) ]

let private buildDataGraph (triples: (string * string * string) list) : IGraph =
    let g = new Graph() :> IGraph
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
    g.NamespaceMap.AddNamespace("xsd", UriFactory.Create "http://www.w3.org/2001/XMLSchema#")

    for (s, p, o) in triples do
        let sNode = g.CreateUriNode(UriFactory.Create s)
        let pNode = g.CreateUriNode(UriFactory.Create p)
        let oNode = g.CreateUriNode(UriFactory.Create o)
        g.Assert(Triple(sNode, pNode, oNode)) |> ignore

    g

let private buildDataGraphWithLiteral (s: string) (p: string) (value: string) (datatype: string) : IGraph =
    let g = new Graph() :> IGraph
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
    g.NamespaceMap.AddNamespace("xsd", UriFactory.Create "http://www.w3.org/2001/XMLSchema#")
    let sNode = g.CreateUriNode(UriFactory.Create s)
    let pNode = g.CreateUriNode(UriFactory.Create p)
    let oNode = g.CreateLiteralNode(value, UriFactory.Create datatype)
    g.Assert(Triple(sNode, pNode, oNode)) |> ignore
    let rdfType = g.CreateUriNode(UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
    let orderType = g.CreateUriNode(UriFactory.Create "https://schema.org/Order")
    g.Assert(Triple(sNode, rdfType, orderType)) |> ignore
    g

let private conformingGraph () =
    buildDataGraphWithLiteral
        "https://example.org/order/1"
        "https://schema.org/totalPaymentDue"
        "100"
        "http://www.w3.org/2001/XMLSchema#decimal"

let private wrongDatatypeGraph () =
    buildDataGraphWithLiteral
        "https://example.org/order/1"
        "https://schema.org/totalPaymentDue"
        "not-a-number"
        "http://www.w3.org/2001/XMLSchema#string"

let private missingPropertyGraph () =
    let g = new Graph() :> IGraph
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
    let inst = g.CreateUriNode(UriFactory.Create "https://example.org/order/1")
    let rdfType = g.CreateUriNode(UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
    let orderType = g.CreateUriNode(UriFactory.Create "https://schema.org/Order")
    g.Assert(Triple(inst, rdfType, orderType)) |> ignore
    g

[<Tests>]
let tests =
    testList
        "Validator.validate"
        [ test "conforming data graph (decimal totalPaymentDue) → Conforms = true" {
              use sg = orderShape ()
              use data = conformingGraph ()
              let report = Validator.validate sg data
              Expect.isTrue report.Conforms "conforming graph should pass SHACL validation"
          }

          test "wrong datatype (string where decimal required) → Conforms = false" {
              use sg = orderShape ()
              use data = wrongDatatypeGraph ()
              let report = Validator.validate sg data
              Expect.isFalse report.Conforms "wrong datatype should fail SHACL validation"
          }

          test "missing required property → Conforms = false" {
              use sg = orderShape ()
              use data = missingPropertyGraph ()
              let report = Validator.validate sg data
              Expect.isFalse report.Conforms "missing totalPaymentDue should fail SHACL validation"
          }

          test "non-conforming report graph contains schema.org/totalPaymentDue and NOT urn:frank:" {
              use sg = orderShape ()
              use data = missingPropertyGraph ()
              let report = Validator.validate sg data

              let reportTriples =
                  report.Graph.Triples |> Seq.map (fun t -> t.ToString()) |> String.concat "\n"

              Expect.stringContains reportTriples "schema.org/totalPaymentDue" "report references property IRI"
              Expect.isFalse (reportTriples.Contains("urn:frank:")) "report must not contain urn:frank: IRIs"
          } ]
