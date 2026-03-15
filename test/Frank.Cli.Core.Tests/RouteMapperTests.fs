module Frank.Cli.Core.Tests.RouteMapperTests

open System
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Extraction.TypeMapper
open Frank.Cli.Core.Extraction.RouteMapper

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

let config : MappingConfig = {
    BaseUri = baseUri
    Vocabularies = ["schema.org"; "hydra"]
}

[<Tests>]
let tests =
    testList "RouteMapper" [
        testCase "route maps to hydra:Resource with template" <| fun _ ->
            let resource : AnalyzedResource = {
                RouteTemplate = "/products/{id}"
                Name = Some "products"
                HttpMethods = [Get]
                HasLinkedData = false
                Location = { File = "test.fs"; Line = 1; Column = 1 }
            }

            let graph = mapRoutes config [resource] []

            let resUri = Uri "http://example.org/api/resources/products/{id}"

            // rdf:type hydra:Resource
            Expect.isTrue
                (hasTriple graph resUri (Uri Rdf.Type) (Uri Hydra.Resource))
                "Resource should be hydra:Resource"

            // rdfs:label
            Expect.isTrue
                (hasLiteralTriple graph resUri (Uri Rdfs.Label) "products")
                "Resource should have rdfs:label from Name"

            // hydra:template
            Expect.isTrue
                (hasLiteralTriple graph resUri (Uri Hydra.Template) "http://example.org/api/resources/products/{id}")
                "Resource should have hydra:template"

            assertValidTurtle graph

        testCase "route without name uses route template as label" <| fun _ ->
            let resource : AnalyzedResource = {
                RouteTemplate = "/items"
                Name = None
                HttpMethods = [Get]
                HasLinkedData = false
                Location = { File = "test.fs"; Line = 1; Column = 1 }
            }

            let graph = mapRoutes config [resource] []

            let resUri = Uri "http://example.org/api/resources/items"

            Expect.isTrue
                (hasLiteralTriple graph resUri (Uri Rdfs.Label) "/items")
                "Resource without name should use route template as label"

            assertValidTurtle graph

        testCase "route with HasLinkedData links to matching type" <| fun _ ->
            let resource : AnalyzedResource = {
                RouteTemplate = "/products/{id}"
                Name = Some "products"
                HttpMethods = [Get]
                HasLinkedData = true
                Location = { File = "test.fs"; Line = 1; Column = 1 }
            }

            let productType : AnalyzedType = {
                FullName = "MyApp.Product"
                ShortName = "Product"
                Kind = Record [{ Name = "Id"; Kind = Primitive "xsd:integer"; IsRequired = true; IsScalar = true; Constraints = [] }]
                GenericParameters = []
                SourceLocation = None
                IsClosed = true
            }

            let graph = mapRoutes config [resource] [productType]

            let resUri = Uri "http://example.org/api/resources/products/{id}"
            let classUri = Uri "http://example.org/api/types/Product"

            Expect.isTrue
                (hasTriple graph resUri (Uri Hydra.SupportedClass) classUri)
                "Resource with HasLinkedData should link to Product class"

            assertValidTurtle graph
    ]
