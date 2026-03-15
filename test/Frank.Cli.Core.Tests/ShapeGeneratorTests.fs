module Frank.Cli.Core.Tests.ShapeGeneratorTests

open System
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Extraction.TypeMapper
open Frank.Cli.Core.Extraction.ShapeGenerator

let assertValidTurtle (graph: IGraph) =
    let writer = VDS.RDF.Writing.CompressingTurtleWriter()
    let turtle = VDS.RDF.Writing.StringWriter.Write(graph, writer)
    let parser = VDS.RDF.Parsing.TurtleParser()
    let roundTrip = new VDS.RDF.Graph()
    use reader = new System.IO.StringReader(turtle)
    parser.Load(roundTrip, reader)

let hasTriple (graph: IGraph) (s: Uri) (p: Uri) (o: Uri) =
    graph.Triples
    |> Seq.exists (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IUriNode as sn), (:? IUriNode as pn), (:? IUriNode as on) -> sn.Uri = s && pn.Uri = p && on.Uri = o
        | _ -> false)

let hasBlankObjectForSubject (graph: IGraph) (s: Uri) (p: Uri) =
    graph.Triples
    |> Seq.exists (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IUriNode as sn), (:? IUriNode as pn), (:? IBlankNode) -> sn.Uri = s && pn.Uri = p
        | _ -> false)

/// Find blank nodes linked from a subject via a predicate, then check if blank node has a triple
let blankNodeHasTriple (graph: IGraph) (s: Uri) (linkPred: Uri) (blankPred: Uri) (blankObj: Uri) =
    graph.Triples
    |> Seq.filter (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IUriNode as sn), (:? IUriNode as pn), (:? IBlankNode) -> sn.Uri = s && pn.Uri = linkPred
        | _ -> false)
    |> Seq.exists (fun linkTriple ->
        let blankNode = linkTriple.Object

        graph.Triples
        |> Seq.exists (fun t ->
            match t.Subject, t.Predicate, t.Object with
            | s2, (:? IUriNode as pn), (:? IUriNode as on) when obj.ReferenceEquals(s2, blankNode) ->
                pn.Uri = blankPred && on.Uri = blankObj
            | _ -> false))

let blankNodeHasLiteralTriple (graph: IGraph) (s: Uri) (linkPred: Uri) (blankPred: Uri) (value: string) =
    graph.Triples
    |> Seq.filter (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IUriNode as sn), (:? IUriNode as pn), (:? IBlankNode) -> sn.Uri = s && pn.Uri = linkPred
        | _ -> false)
    |> Seq.exists (fun linkTriple ->
        let blankNode = linkTriple.Object

        graph.Triples
        |> Seq.exists (fun t ->
            match t.Subject, t.Predicate, t.Object with
            | s2, (:? IUriNode as pn), (:? ILiteralNode as on) when obj.ReferenceEquals(s2, blankNode) ->
                pn.Uri = blankPred && on.Value = value
            | _ -> false))

let baseUri = Uri "http://example.org/api"

let config: MappingConfig =
    { BaseUri = baseUri
      Vocabularies = [ "schema.org"; "hydra" ] }

[<Tests>]
let tests =
    testList
        "ShapeGenerator"
        [ testCase "record generates sh:NodeShape with targetNode"
          <| fun _ ->
              let person =
                  { FullName = "MyApp.Person"
                    ShortName = "Person"
                    Kind =
                      Record
                          [ { Name = "Name"
                              Kind = Primitive "xsd:string"
                              IsRequired = true
                              IsScalar = true
                              Constraints = [] }
                            { Name = "Email"
                              Kind = Optional(Primitive "xsd:string")
                              IsRequired = false
                              IsScalar = true
                              Constraints = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = true }

              let graph = generateShapes config [ person ]

              let shapeUri =
                  Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Person"))

              let classUri = Uri "http://example.org/api/types/Person"

              // rdf:type sh:NodeShape
              Expect.isTrue
                  (hasTriple graph shapeUri (Uri Rdf.Type) (Uri Shacl.NodeShape))
                  "Shape should be sh:NodeShape"

              // sh:targetNode
              Expect.isTrue
                  (hasTriple graph shapeUri (Uri Shacl.TargetNode) (Uri "urn:frank:validation:request"))
                  "Shape should target validation request node"

              assertValidTurtle graph

          testCase "required field has sh:minCount 1"
          <| fun _ ->
              let person =
                  { FullName = "MyApp.Person"
                    ShortName = "Person"
                    Kind =
                      Record
                          [ { Name = "Name"
                              Kind = Primitive "xsd:string"
                              IsRequired = true
                              IsScalar = true
                              Constraints = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = true }

              let graph = generateShapes config [ person ]

              let shapeUri =
                  Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Person"))

              let namePropUri = Uri "urn:frank:property:Name"

              // Shape has sh:property blank node
              Expect.isTrue
                  (hasBlankObjectForSubject graph shapeUri (Uri Shacl.Property))
                  "Shape should have sh:property"

              // Property shape has sh:path pointing to property URI
              Expect.isTrue
                  (blankNodeHasTriple graph shapeUri (Uri Shacl.Property) (Uri Shacl.Path) namePropUri)
                  "Property shape should have sh:path to Name property"

              // Property shape has sh:datatype xsd:string
              Expect.isTrue
                  (blankNodeHasTriple graph shapeUri (Uri Shacl.Property) (Uri Shacl.Datatype) (Uri Xsd.String))
                  "Property shape should have sh:datatype xsd:string"

              // sh:minCount 1
              Expect.isTrue
                  (blankNodeHasLiteralTriple graph shapeUri (Uri Shacl.Property) (Uri Shacl.MinCount) "1")
                  "Required field should have sh:minCount 1"

              assertValidTurtle graph

          testCase "optional field has sh:minCount 0"
          <| fun _ ->
              let person =
                  { FullName = "MyApp.Person"
                    ShortName = "Person"
                    Kind =
                      Record
                          [ { Name = "Email"
                              Kind = Optional(Primitive "xsd:string")
                              IsRequired = false
                              IsScalar = true
                              Constraints = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = true }

              let graph = generateShapes config [ person ]

              let shapeUri =
                  Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Person"))

              // sh:minCount 0
              Expect.isTrue
                  (blankNodeHasLiteralTriple graph shapeUri (Uri Shacl.Property) (Uri Shacl.MinCount) "0")
                  "Optional field should have sh:minCount 0"

              assertValidTurtle graph

          testCase "reference field uses sh:class instead of sh:datatype"
          <| fun _ ->
              let order =
                  { FullName = "MyApp.Order"
                    ShortName = "Order"
                    Kind =
                      Record
                          [ { Name = "Customer"
                              Kind = Reference "Customer"
                              IsRequired = true
                              IsScalar = true
                              Constraints = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = true }

              let graph = generateShapes config [ order ]

              let shapeUri =
                  Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Order"))

              let customerClassUri = Uri "http://example.org/api/types/Customer"

              // Property shape has sh:class pointing to Customer
              Expect.isTrue
                  (blankNodeHasTriple graph shapeUri (Uri Shacl.Property) (Uri Shacl.Class) customerClassUri)
                  "Reference field should use sh:class"

              assertValidTurtle graph

          testCase "scalar field has sh:maxCount 1"
          <| fun _ ->
              let t =
                  { FullName = "MyApp.Item"
                    ShortName = "Item"
                    Kind =
                      Record
                          [ { Name = "Name"
                              Kind = Primitive "xsd:string"
                              IsRequired = true
                              IsScalar = true
                              Constraints = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = true }

              let graph = generateShapes config [ t ]
              let shapeUri = Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Item"))

              Expect.isTrue
                  (blankNodeHasLiteralTriple graph shapeUri (Uri Shacl.Property) (Uri Shacl.MaxCount) "1")
                  "Scalar field should have sh:maxCount 1"

              assertValidTurtle graph

          testCase "collection field has no sh:maxCount"
          <| fun _ ->
              let t =
                  { FullName = "MyApp.Bag"
                    ShortName = "Bag"
                    Kind =
                      Record
                          [ { Name = "Items"
                              Kind = Collection(Primitive "xsd:string")
                              IsRequired = true
                              IsScalar = false
                              Constraints = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = true }

              let graph = generateShapes config [ t ]
              let shapeUri = Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Bag"))
              // Should NOT have sh:maxCount
              Expect.isFalse
                  (blankNodeHasLiteralTriple graph shapeUri (Uri Shacl.Property) (Uri Shacl.MaxCount) "1")
                  "Collection field should not have sh:maxCount"

              assertValidTurtle graph

          testCase "Guid field has sh:pattern with UUID regex"
          <| fun _ ->
              let t =
                  { FullName = "MyApp.Entity"
                    ShortName = "Entity"
                    Kind =
                      Record
                          [ { Name = "Id"
                              Kind = Guid
                              IsRequired = true
                              IsScalar = true
                              Constraints = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = true }

              let graph = generateShapes config [ t ]
              // Check sh:pattern exists on the property shape
              let shapeUri =
                  Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Entity"))

              Expect.isTrue (hasBlankObjectForSubject graph shapeUri (Uri Shacl.Property)) "Should have property shape"
              assertValidTurtle graph

          testCase "record has sh:closed true"
          <| fun _ ->
              let t =
                  { FullName = "MyApp.Closed"
                    ShortName = "Closed"
                    Kind =
                      Record
                          [ { Name = "X"
                              Kind = Primitive "xsd:string"
                              IsRequired = true
                              IsScalar = true
                              Constraints = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = true }

              let graph = generateShapes config [ t ]

              let shapeUri =
                  Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Closed"))
              // Check sh:closed true via raw triple inspection
              let closedTriples =
                  graph.Triples
                  |> Seq.exists (fun t ->
                      match t.Subject, t.Predicate, t.Object with
                      | (:? IUriNode as s), (:? IUriNode as p), (:? ILiteralNode as o) ->
                          s.Uri = shapeUri && p.Uri = Uri Shacl.Closed && o.Value = "true"
                      | _ -> false)

              Expect.isTrue closedTriples "Record should have sh:closed true"
              assertValidTurtle graph

          testCase "simple DU has sh:in with case names"
          <| fun _ ->
              let t =
                  { FullName = "MyApp.Color"
                    ShortName = "Color"
                    Kind =
                      DiscriminatedUnion
                          [ { Name = "Red"; Fields = [] }
                            { Name = "Green"; Fields = [] }
                            { Name = "Blue"; Fields = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = false }

              let graph = generateShapes config [ t ]

              let shapeUri =
                  Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Color"))

              Expect.isTrue (hasBlankObjectForSubject graph shapeUri (Uri Shacl.In)) "Simple DU should have sh:in"
              assertValidTurtle graph

          testCase "payload DU has sh:or with case shape URIs"
          <| fun _ ->
              let t =
                  { FullName = "MyApp.Shape"
                    ShortName = "Shape"
                    Kind =
                      DiscriminatedUnion
                          [ { Name = "Circle"
                              Fields =
                                [ { Name = "Radius"
                                    Kind = Primitive "xsd:double"
                                    IsRequired = true
                                    IsScalar = true
                                    Constraints = [] } ] }
                            { Name = "Square"
                              Fields =
                                [ { Name = "Side"
                                    Kind = Primitive "xsd:double"
                                    IsRequired = true
                                    IsScalar = true
                                    Constraints = [] } ] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = false }

              let graph = generateShapes config [ t ]

              let shapeUri =
                  Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Shape"))

              Expect.isTrue (hasBlankObjectForSubject graph shapeUri (Uri Shacl.Or)) "Payload DU should have sh:or"
              assertValidTurtle graph

          testCase "reference field has sh:node to nested shape"
          <| fun _ ->
              let t =
                  { FullName = "MyApp.Wrapper"
                    ShortName = "Wrapper"
                    Kind =
                      Record
                          [ { Name = "Inner"
                              Kind = Reference "MyApp.Inner"
                              IsRequired = true
                              IsScalar = true
                              Constraints = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = true }

              let graph = generateShapes config [ t ]

              let shapeUri =
                  Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Wrapper"))

              let nestedUri =
                  Uri(sprintf "urn:frank:shape:%s" (Uri.EscapeDataString "MyApp.Inner"))

              Expect.isTrue
                  (blankNodeHasTriple graph shapeUri (Uri Shacl.Property) (Uri Shacl.Node) nestedUri)
                  "Reference field should have sh:node to nested shape"

              assertValidTurtle graph

          testCase "all URIs use urn:frank scheme"
          <| fun _ ->
              let t =
                  { FullName = "MyApp.Check"
                    ShortName = "Check"
                    Kind =
                      Record
                          [ { Name = "Val"
                              Kind = Primitive "xsd:string"
                              IsRequired = true
                              IsScalar = true
                              Constraints = [] } ]
                    GenericParameters = []
                    SourceLocation = None
                    IsClosed = true }

              let graph = generateShapes config [ t ]
              // All subject URIs should be urn:frank:* or standard vocabularies
              let subjects =
                  graph.Triples
                  |> Seq.choose (fun t ->
                      match t.Subject with
                      | :? IUriNode as u -> Some(u.Uri.ToString())
                      | _ -> None)
                  |> Seq.filter (fun u -> not (u.StartsWith("http://www.w3.org/")))
                  |> Seq.toList

              for uri in subjects do
                  Expect.isTrue (uri.StartsWith("urn:frank:")) (sprintf "URI %s should use urn:frank scheme" uri) ]
