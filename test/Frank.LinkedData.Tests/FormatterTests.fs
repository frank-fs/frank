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

        testCase "Turtle round-trip preserves graph isomorphism" <| fun _ ->
            let g = createTestGraph ()
            use ms = new MemoryStream()
            TurtleFormatter.writeTurtle g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            let parsed = new Graph()
            let parser = TurtleParser()
            use reader = new StringReader(output)
            parser.Load(parsed, reader)
            Expect.equal parsed.Triples.Count g.Triples.Count "Triple count should match"
            let originalTripleSet = g.Triples |> Seq.map (fun t -> t.Subject.ToString(), t.Predicate.ToString(), t.Object.ToString()) |> Set.ofSeq
            let parsedTripleSet = parsed.Triples |> Seq.map (fun t -> t.Subject.ToString(), t.Predicate.ToString(), t.Object.ToString()) |> Set.ofSeq
            Expect.equal parsedTripleSet originalTripleSet "Triple sets should be identical"

        testCase "RdfXml round-trip preserves graph isomorphism" <| fun _ ->
            let g = createTestGraph ()
            use ms = new MemoryStream()
            RdfXmlFormatter.writeRdfXml g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            let parsed = new Graph()
            let parser = RdfXmlParser()
            use reader = new StringReader(output)
            parser.Load(parsed, reader)
            Expect.equal parsed.Triples.Count g.Triples.Count "Triple count should match"
            let originalTripleSet = g.Triples |> Seq.map (fun t -> t.Subject.ToString(), t.Predicate.ToString(), t.Object.ToString()) |> Set.ofSeq
            let parsedTripleSet = parsed.Triples |> Seq.map (fun t -> t.Subject.ToString(), t.Predicate.ToString(), t.Object.ToString()) |> Set.ofSeq
            Expect.equal parsedTripleSet originalTripleSet "Triple sets should be identical"

        testCase "JsonLdFormatter renders typed literals correctly" <| fun _ ->
            let g = createTestGraph ()
            use ms = new MemoryStream()
            JsonLdFormatter.writeJsonLd g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            let doc = System.Text.Json.JsonDocument.Parse(output)
            let age = doc.RootElement.GetProperty("Age")
            Expect.equal age.ValueKind System.Text.Json.JsonValueKind.Number "Age should be rendered as JSON number"

        testCase "JsonLdFormatter uses @graph for multiple subjects" <| fun _ ->
            let g = new Graph()
            let s1 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/a"))
            let s2 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/b"))
            let p = g.CreateUriNode(UriFactory.Root.Create("http://example.org/p"))
            g.Assert(Triple(s1, p, g.CreateLiteralNode("v1"))) |> ignore
            g.Assert(Triple(s2, p, g.CreateLiteralNode("v2"))) |> ignore
            use ms = new MemoryStream()
            JsonLdFormatter.writeJsonLd g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            let doc = System.Text.Json.JsonDocument.Parse(output)
            let hasGraph = doc.RootElement.TryGetProperty("@graph") |> fst
            Expect.isTrue hasGraph "Multiple subjects should use @graph array"

        testCase "Turtle handles graph with blank nodes" <| fun _ ->
            let g = new Graph()
            let s = g.CreateUriNode(UriFactory.Root.Create("http://example.org/x"))
            let p = g.CreateUriNode(UriFactory.Root.Create("http://example.org/rel"))
            let blank = g.CreateBlankNode()
            let p2 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/val"))
            g.Assert(Triple(s, p, blank)) |> ignore
            g.Assert(Triple(blank, p2, g.CreateLiteralNode("test"))) |> ignore
            use ms = new MemoryStream()
            TurtleFormatter.writeTurtle g ms
            ms.Seek(0L, SeekOrigin.Begin) |> ignore
            let output = (new StreamReader(ms)).ReadToEnd()
            Expect.isNotEmpty output "Should produce output with blank nodes"
            let parsed = new Graph()
            use reader = new StringReader(output)
            TurtleParser().Load(parsed, reader)
            Expect.equal parsed.Triples.Count 2 "Should preserve both triples with blank node"
    ]
