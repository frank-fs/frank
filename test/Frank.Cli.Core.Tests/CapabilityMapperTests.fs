module Frank.Cli.Core.Tests.CapabilityMapperTests

open System
open Expecto
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Extraction.TypeMapper
open Frank.Cli.Core.Extraction.CapabilityMapper

let hasBlankSubjectTriple (graph: IGraph) (p: Uri) (o: Uri) =
    graph.Triples
    |> Seq.exists (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IBlankNode), (:? IUriNode as pn), (:? IUriNode as on) ->
            pn.Uri = p && on.Uri = o
        | _ -> false)

let hasBlankSubjectLiteralTriple (graph: IGraph) (p: Uri) (value: string) =
    graph.Triples
    |> Seq.exists (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IBlankNode), (:? IUriNode as pn), (:? ILiteralNode as on) ->
            pn.Uri = p && on.Value = value
        | _ -> false)

let countBlankSubjectTriples (graph: IGraph) (p: Uri) (o: Uri) =
    graph.Triples
    |> Seq.filter (fun t ->
        match t.Subject, t.Predicate, t.Object with
        | (:? IBlankNode), (:? IUriNode as pn), (:? IUriNode as on) ->
            pn.Uri = p && on.Uri = o
        | _ -> false)
    |> Seq.length

let baseUri = Uri "http://example.org/api"

let config : MappingConfig = {
    BaseUri = baseUri
    Vocabularies = ["schema.org"; "hydra"]
}

[<Tests>]
let tests =
    testList "CapabilityMapper" [
        testCase "resource with multiple methods creates operations" <| fun _ ->
            let resource : AnalyzedResource = {
                RouteTemplate = "/products/{id}"
                Name = Some "products"
                HttpMethods = [Get; Post; Delete]
                HasLinkedData = false
                Location = { File = "test.fs"; Line = 1; Column = 1 }
            }

            let graph = mapCapabilities config [resource]

            // Should have 3 operations typed as hydra:Operation
            let opCount = countBlankSubjectTriples graph (Uri Rdf.Type) (Uri Hydra.Operation)
            Expect.equal opCount 3 "Should have 3 hydra:Operation blank nodes"

            // GET -> schema:ReadAction
            Expect.isTrue
                (hasBlankSubjectTriple graph (Uri Rdf.Type) (Uri SchemaOrg.ReadAction))
                "GET should map to schema:ReadAction"

            // POST -> schema:CreateAction
            Expect.isTrue
                (hasBlankSubjectTriple graph (Uri Rdf.Type) (Uri SchemaOrg.CreateAction))
                "POST should map to schema:CreateAction"

            // DELETE -> schema:DeleteAction
            Expect.isTrue
                (hasBlankSubjectTriple graph (Uri Rdf.Type) (Uri SchemaOrg.DeleteAction))
                "DELETE should map to schema:DeleteAction"

            // hydra:method literals
            Expect.isTrue
                (hasBlankSubjectLiteralTriple graph (Uri Hydra.Method) "GET")
                "Should have hydra:method GET"
            Expect.isTrue
                (hasBlankSubjectLiteralTriple graph (Uri Hydra.Method) "POST")
                "Should have hydra:method POST"
            Expect.isTrue
                (hasBlankSubjectLiteralTriple graph (Uri Hydra.Method) "DELETE")
                "Should have hydra:method DELETE"

        testCase "operations are linked to resource via hydra:supportedOperation" <| fun _ ->
            let resource : AnalyzedResource = {
                RouteTemplate = "/items"
                Name = None
                HttpMethods = [Get]
                HasLinkedData = false
                Location = { File = "test.fs"; Line = 1; Column = 1 }
            }

            let graph = mapCapabilities config [resource]

            let resUri = Uri "http://example.org/api/resources/items"

            // Resource should have hydra:supportedOperation to blank node
            let supportedOpTriples =
                graph.Triples
                |> Seq.filter (fun t ->
                    match t.Subject, t.Predicate, t.Object with
                    | (:? IUriNode as sn), (:? IUriNode as pn), (:? IBlankNode) ->
                        sn.Uri = resUri && pn.Uri = Uri Hydra.SupportedOperation
                    | _ -> false)
                |> Seq.length

            Expect.equal supportedOpTriples 1 "Should have 1 supportedOperation link"

        testCase "PATCH maps to schema:UpdateAction" <| fun _ ->
            let resource : AnalyzedResource = {
                RouteTemplate = "/items"
                Name = None
                HttpMethods = [Patch]
                HasLinkedData = false
                Location = { File = "test.fs"; Line = 1; Column = 1 }
            }

            let graph = mapCapabilities config [resource]

            Expect.isTrue
                (hasBlankSubjectTriple graph (Uri Rdf.Type) (Uri SchemaOrg.UpdateAction))
                "PATCH should map to schema:UpdateAction"
    ]
