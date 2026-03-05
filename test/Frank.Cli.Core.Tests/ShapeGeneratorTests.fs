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
        | (:? IUriNode as sn), (:? IUriNode as pn), (:? IUriNode as on) ->
            sn.Uri = s && pn.Uri = p && on.Uri = o
        | _ -> false)

let hasBlankObjectForSubject (graph: IGraph) (s: Uri) (p: Uri) =
    graph.Triples
    |> Seq.exists (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IUriNode as sn), (:? IUriNode as pn), (:? IBlankNode) ->
            sn.Uri = s && pn.Uri = p
        | _ -> false)

/// Find blank nodes linked from a subject via a predicate, then check if blank node has a triple
let blankNodeHasTriple (graph: IGraph) (s: Uri) (linkPred: Uri) (blankPred: Uri) (blankObj: Uri) =
    graph.Triples
    |> Seq.filter (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IUriNode as sn), (:? IUriNode as pn), (:? IBlankNode) ->
            sn.Uri = s && pn.Uri = linkPred
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
        | (:? IUriNode as sn), (:? IUriNode as pn), (:? IBlankNode) ->
            sn.Uri = s && pn.Uri = linkPred
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

let config : MappingConfig = {
    BaseUri = baseUri
    Vocabularies = ["schema.org"; "hydra"]
}

[<Tests>]
let tests =
    testList "ShapeGenerator" [
        testCase "record generates sh:NodeShape with targetClass" <| fun _ ->
            let person = {
                FullName = "MyApp.Person"
                ShortName = "Person"
                Kind = Record [
                    { Name = "Name"; Kind = Primitive "xsd:string"; IsRequired = true }
                    { Name = "Email"; Kind = Optional (Primitive "xsd:string"); IsRequired = false }
                ]
                GenericParameters = []
                SourceLocation = None
            }

            let graph = generateShapes config [person]

            let shapeUri = Uri "http://example.org/api/shapes/PersonShape"
            let classUri = Uri "http://example.org/api/types/Person"

            // rdf:type sh:NodeShape
            Expect.isTrue
                (hasTriple graph shapeUri (Uri Rdf.Type) (Uri Shacl.NodeShape))
                "Shape should be sh:NodeShape"

            // sh:targetClass
            Expect.isTrue
                (hasTriple graph shapeUri (Uri Shacl.TargetClass) classUri)
                "Shape should target Person class"

            assertValidTurtle graph

        testCase "required field has sh:minCount 1" <| fun _ ->
            let person = {
                FullName = "MyApp.Person"
                ShortName = "Person"
                Kind = Record [
                    { Name = "Name"; Kind = Primitive "xsd:string"; IsRequired = true }
                ]
                GenericParameters = []
                SourceLocation = None
            }

            let graph = generateShapes config [person]

            let shapeUri = Uri "http://example.org/api/shapes/PersonShape"
            let namePropUri = Uri "http://example.org/api/properties/Person/Name"

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

        testCase "optional field has sh:minCount 0" <| fun _ ->
            let person = {
                FullName = "MyApp.Person"
                ShortName = "Person"
                Kind = Record [
                    { Name = "Email"; Kind = Optional (Primitive "xsd:string"); IsRequired = false }
                ]
                GenericParameters = []
                SourceLocation = None
            }

            let graph = generateShapes config [person]

            let shapeUri = Uri "http://example.org/api/shapes/PersonShape"

            // sh:minCount 0
            Expect.isTrue
                (blankNodeHasLiteralTriple graph shapeUri (Uri Shacl.Property) (Uri Shacl.MinCount) "0")
                "Optional field should have sh:minCount 0"

            assertValidTurtle graph

        testCase "reference field uses sh:class instead of sh:datatype" <| fun _ ->
            let order = {
                FullName = "MyApp.Order"
                ShortName = "Order"
                Kind = Record [
                    { Name = "Customer"; Kind = Reference "Customer"; IsRequired = true }
                ]
                GenericParameters = []
                SourceLocation = None
            }

            let graph = generateShapes config [order]

            let shapeUri = Uri "http://example.org/api/shapes/OrderShape"
            let customerClassUri = Uri "http://example.org/api/types/Customer"

            // Property shape has sh:class pointing to Customer
            Expect.isTrue
                (blankNodeHasTriple graph shapeUri (Uri Shacl.Property) (Uri Shacl.Class) customerClassUri)
                "Reference field should use sh:class"

            assertValidTurtle graph
    ]
