module Frank.LinkedData.Tests.FormatterTests

open System
open System.IO
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open Frank.LinkedData.Negotiation

/// Creates a minimal graph with 2 triples for testing formatters.
let private createTestGraph () =
    let g = new Graph()
    let subject = g.CreateUriNode(UriFactory.Root.Create("http://example.org/person/1"))
    let namePred = g.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Name"))
    let agePred = g.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Age"))
    let nameObj = g.CreateLiteralNode("Alice")
    let ageObj = g.CreateLiteralNode("30", UriFactory.Root.Create("http://www.w3.org/2001/XMLSchema#integer"))
    g.Assert(Triple(subject, namePred, nameObj)) |> ignore
    g.Assert(Triple(subject, agePred, ageObj)) |> ignore
    g

[<Tests>]
let tests =
    testList "Formatters" [
        testCase "TurtleFormatter writes valid Turtle with 2 triples" <| fun _ ->
            let g = createTestGraph ()
            use ms = new MemoryStream()
            TurtleFormatter.writeTurtle g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            Expect.isNotEmpty output "Turtle output should not be empty"

            // Parse it back and verify 2 triples
            let parsed = new Graph()
            let parser = TurtleParser()
            use reader = new StringReader(output)
            parser.Load(parsed, reader)
            Expect.equal parsed.Triples.Count 2 "Parsed Turtle graph should have 2 triples"

        testCase "RdfXmlFormatter writes valid RDF/XML with 2 triples" <| fun _ ->
            let g = createTestGraph ()
            use ms = new MemoryStream()
            RdfXmlFormatter.writeRdfXml g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            Expect.isNotEmpty output "RDF/XML output should not be empty"

            // Parse it back and verify 2 triples
            let parsed = new Graph()
            let parser = RdfXmlParser()
            use reader = new StringReader(output)
            parser.Load(parsed, reader)
            Expect.equal parsed.Triples.Count 2 "Parsed RDF/XML graph should have 2 triples"

        testCase "JsonLdFormatter writes valid JSON-LD with expected properties" <| fun _ ->
            let g = createTestGraph ()
            use ms = new MemoryStream()
            JsonLdFormatter.writeJsonLd g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            Expect.isNotEmpty output "JSON-LD output should not be empty"

            // Verify it's valid JSON and contains expected fields
            let doc = System.Text.Json.JsonDocument.Parse(output)
            let root = doc.RootElement

            // Should have @context
            let hasContext = root.TryGetProperty("@context") |> fst
            Expect.isTrue hasContext "JSON-LD should have @context"

            // Should have @id
            let hasId = root.TryGetProperty("@id") |> fst
            Expect.isTrue hasId "JSON-LD should have @id"

            // Should have Name property
            let hasName = root.TryGetProperty("Name") |> fst
            Expect.isTrue hasName "JSON-LD should have Name property"

            // Should have Age property
            let hasAge = root.TryGetProperty("Age") |> fst
            Expect.isTrue hasAge "JSON-LD should have Age property"

        testCase "JsonLdFormatter @id matches subject URI" <| fun _ ->
            let g = createTestGraph ()
            use ms = new MemoryStream()
            JsonLdFormatter.writeJsonLd g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            let doc = System.Text.Json.JsonDocument.Parse(output)
            let id = doc.RootElement.GetProperty("@id").GetString()
            Expect.equal id "http://example.org/person/1" "@id should match subject URI"

        testCase "TurtleFormatter handles empty graph" <| fun _ ->
            let g = new Graph()
            use ms = new MemoryStream()
            TurtleFormatter.writeTurtle g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            // Should not throw; output may be empty or contain only prefixes
            let parsed = new Graph()
            let parser = TurtleParser()
            use reader = new StringReader(output)
            parser.Load(parsed, reader)
            Expect.equal parsed.Triples.Count 0 "Empty graph should produce 0 triples"

        testCase "RdfXmlFormatter handles empty graph" <| fun _ ->
            let g = new Graph()
            use ms = new MemoryStream()
            RdfXmlFormatter.writeRdfXml g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            let parsed = new Graph()
            let parser = RdfXmlParser()
            use reader = new StringReader(output)
            parser.Load(parsed, reader)
            Expect.equal parsed.Triples.Count 0 "Empty graph should produce 0 triples"
    ]
