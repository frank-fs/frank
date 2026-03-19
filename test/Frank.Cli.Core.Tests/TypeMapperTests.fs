module Frank.Cli.Core.Tests.TypeMapperTests

open System
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Statecharts.Unified
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Extraction.TypeMapper

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

let hasLiteralTriple (graph: IGraph) (s: Uri) (p: Uri) (value: string) =
    graph.Triples
    |> Seq.exists (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IUriNode as sn), (:? IUriNode as pn), (:? ILiteralNode as on) ->
            sn.Uri = s && pn.Uri = p && on.Value = value
        | _ -> false)

let baseUri = Uri "http://example.org/api"

let config = {
    BaseUri = baseUri
    Vocabularies = ["schema.org"; "hydra"]
}

[<Tests>]
let tests =
    testList "TypeMapper" [
        testCase "record maps to owl:Class with properties" <| fun _ ->
            let product = {
                FullName = "MyApp.Product"
                ShortName = "Product"
                Kind = Record [
                    { Name = "Id"; Kind = Primitive "xsd:integer"; IsRequired = true; IsScalar = true; Constraints = [] }
                    { Name = "Name"; Kind = Primitive "xsd:string"; IsRequired = true; IsScalar = true; Constraints = [] }
                    { Name = "Price"; Kind = Optional (Primitive "xsd:double"); IsRequired = false; IsScalar = true; Constraints = [] }
                ]
                GenericParameters = []
                SourceLocation = None
                IsClosed = true
            }

            let graph = mapTypes config [product]

            let classUri = Uri "http://example.org/api/types/Product"

            // Product is owl:Class
            Expect.isTrue
                (hasTriple graph classUri (Uri Rdf.Type) (Uri Owl.Class))
                "Product should be owl:Class"

            // rdfs:label
            Expect.isTrue
                (hasLiteralTriple graph classUri (Uri Rdfs.Label) "Product")
                "Product should have rdfs:label"

            // Id property
            let idPropUri = Uri "http://example.org/api/properties/Product/Id"
            Expect.isTrue
                (hasTriple graph idPropUri (Uri Rdf.Type) (Uri Owl.DatatypeProperty))
                "Id should be owl:DatatypeProperty"
            Expect.isTrue
                (hasTriple graph idPropUri (Uri Rdfs.Range) (Uri Xsd.Integer))
                "Id should have range xsd:integer"
            Expect.isTrue
                (hasTriple graph idPropUri (Uri Rdfs.Domain) classUri)
                "Id should have domain Product"

            // Name property
            let namePropUri = Uri "http://example.org/api/properties/Product/Name"
            Expect.isTrue
                (hasTriple graph namePropUri (Uri Rdf.Type) (Uri Owl.DatatypeProperty))
                "Name should be owl:DatatypeProperty"
            Expect.isTrue
                (hasTriple graph namePropUri (Uri Rdfs.Range) (Uri Xsd.String))
                "Name should have range xsd:string"

            // Price property (optional double)
            let pricePropUri = Uri "http://example.org/api/properties/Product/Price"
            Expect.isTrue
                (hasTriple graph pricePropUri (Uri Rdf.Type) (Uri Owl.DatatypeProperty))
                "Price should be owl:DatatypeProperty"
            Expect.isTrue
                (hasTriple graph pricePropUri (Uri Rdfs.Range) (Uri Xsd.Double))
                "Price should have range xsd:double (unwrapped from Optional)"

            assertValidTurtle graph

        testCase "DU maps to owl:Class with subclasses" <| fun _ ->
            let status = {
                FullName = "MyApp.Status"
                ShortName = "Status"
                Kind = DiscriminatedUnion [
                    { Name = "Active"; Fields = [] }
                    { Name = "Inactive"; Fields = [] }
                ]
                GenericParameters = []
                SourceLocation = None
                IsClosed = false
            }

            let graph = mapTypes config [status]

            let statusUri = Uri "http://example.org/api/types/Status"
            let activeUri = Uri "http://example.org/api/types/Active"
            let inactiveUri = Uri "http://example.org/api/types/Inactive"

            // Status is owl:Class
            Expect.isTrue
                (hasTriple graph statusUri (Uri Rdf.Type) (Uri Owl.Class))
                "Status should be owl:Class"

            // Active is owl:Class with subClassOf Status
            Expect.isTrue
                (hasTriple graph activeUri (Uri Rdf.Type) (Uri Owl.Class))
                "Active should be owl:Class"
            Expect.isTrue
                (hasTriple graph activeUri (Uri Rdfs.SubClassOf) statusUri)
                "Active should be subClassOf Status"

            // Inactive is owl:Class with subClassOf Status
            Expect.isTrue
                (hasTriple graph inactiveUri (Uri Rdf.Type) (Uri Owl.Class))
                "Inactive should be owl:Class"
            Expect.isTrue
                (hasTriple graph inactiveUri (Uri Rdfs.SubClassOf) statusUri)
                "Inactive should be subClassOf Status"

            assertValidTurtle graph

        testCase "DU case with fields generates scoped properties" <| fun _ ->
            let shape = {
                FullName = "MyApp.Shape"
                ShortName = "Shape"
                Kind = DiscriminatedUnion [
                    { Name = "Circle"; Fields = [{ Name = "Radius"; Kind = Primitive "xsd:double"; IsRequired = true; IsScalar = true; Constraints = [] }] }
                    { Name = "Rectangle"; Fields = [
                        { Name = "Width"; Kind = Primitive "xsd:double"; IsRequired = true; IsScalar = true; Constraints = [] }
                        { Name = "Height"; Kind = Primitive "xsd:double"; IsRequired = true; IsScalar = true; Constraints = [] }
                    ] }
                ]
                GenericParameters = []
                SourceLocation = None
                IsClosed = false
            }

            let graph = mapTypes config [shape]

            // Circle.Radius property
            let radiusPropUri = Uri "http://example.org/api/properties/Circle/Radius"
            Expect.isTrue
                (hasTriple graph radiusPropUri (Uri Rdf.Type) (Uri Owl.DatatypeProperty))
                "Radius should be owl:DatatypeProperty"
            Expect.isTrue
                (hasTriple graph radiusPropUri (Uri Rdfs.Domain) (Uri "http://example.org/api/types/Circle"))
                "Radius domain should be Circle"

            assertValidTurtle graph

        testCase "Reference field maps to owl:ObjectProperty" <| fun _ ->
            let order = {
                FullName = "MyApp.Order"
                ShortName = "Order"
                Kind = Record [
                    { Name = "Customer"; Kind = Reference "Customer"; IsRequired = true; IsScalar = true; Constraints = [] }
                ]
                GenericParameters = []
                SourceLocation = None
                IsClosed = true
            }

            let graph = mapTypes config [order]

            let customerPropUri = Uri "http://example.org/api/properties/Order/Customer"
            Expect.isTrue
                (hasTriple graph customerPropUri (Uri Rdf.Type) (Uri Owl.ObjectProperty))
                "Customer should be owl:ObjectProperty"
            Expect.isTrue
                (hasTriple graph customerPropUri (Uri Rdfs.Range) (Uri "http://example.org/api/types/Customer"))
                "Customer should have range Customer class"

            assertValidTurtle graph
    ]
