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

    let namePred =
        g.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Name"))

    let agePred =
        g.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Age"))

    let nameObj = g.CreateLiteralNode("Alice")

    let ageObj =
        g.CreateLiteralNode("30", UriFactory.Root.Create("http://www.w3.org/2001/XMLSchema#integer"))

    g.Assert(Triple(subject, namePred, nameObj)) |> ignore
    g.Assert(Triple(subject, agePred, ageObj)) |> ignore
    g

/// Creates a richer graph with 3 subjects and 7 triples for round-trip testing.
let private createRichTestGraph () =
    let g = new Graph()

    let rdfType =
        g.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))

    let person1 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/person/1"))
    let person2 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/person/2"))
    let org1 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/org/1"))

    let namePred =
        g.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Name"))

    let agePred =
        g.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Person/Age"))

    let memberPred =
        g.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Org/member"))

    let personType =
        g.CreateUriNode(UriFactory.Root.Create("http://example.org/types/Person"))

    let orgType =
        g.CreateUriNode(UriFactory.Root.Create("http://example.org/types/Organization"))
    // person1: Name, Age, rdf:type
    g.Assert(Triple(person1, namePred, g.CreateLiteralNode("Alice"))) |> ignore

    g.Assert(
        Triple(
            person1,
            agePred,
            g.CreateLiteralNode("30", UriFactory.Root.Create("http://www.w3.org/2001/XMLSchema#integer"))
        )
    )
    |> ignore

    g.Assert(Triple(person1, rdfType, personType)) |> ignore
    // person2: Name
    g.Assert(Triple(person2, namePred, g.CreateLiteralNode("Bob"))) |> ignore
    g.Assert(Triple(person2, rdfType, personType)) |> ignore
    // org1: rdf:type, member->person1, member->person2
    g.Assert(Triple(org1, rdfType, orgType)) |> ignore
    g.Assert(Triple(org1, memberPred, person1)) |> ignore
    g

[<Tests>]
let tests =
    testList
        "Formatters"
        [ testCase "TurtleFormatter writes valid Turtle with 2 triples"
          <| fun _ ->
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

          testCase "RdfXmlFormatter writes valid RDF/XML with 2 triples"
          <| fun _ ->
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

          testCase "JsonLdFormatter writes valid JSON-LD with expected properties"
          <| fun _ ->
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

          testCase "JsonLdFormatter @id matches subject URI"
          <| fun _ ->
              let g = createTestGraph ()
              use ms = new MemoryStream()
              JsonLdFormatter.writeJsonLd g ms
              ms.Seek(0L, SeekOrigin.Begin) |> ignore
              let output = (new StreamReader(ms)).ReadToEnd()
              let doc = System.Text.Json.JsonDocument.Parse(output)
              let id = doc.RootElement.GetProperty("@id").GetString()
              Expect.equal id "http://example.org/person/1" "@id should match subject URI"

          testCase "TurtleFormatter handles empty graph"
          <| fun _ ->
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

          testCase "RdfXmlFormatter handles empty graph"
          <| fun _ ->
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

          testCase "Turtle round-trip preserves graph isomorphism"
          <| fun _ ->
              let g = createRichTestGraph ()
              use ms = new MemoryStream()
              TurtleFormatter.writeTurtle g ms
              ms.Seek(0L, SeekOrigin.Begin) |> ignore
              let output = (new StreamReader(ms)).ReadToEnd()
              let parsed = new Graph()
              let parser = TurtleParser()
              use reader = new StringReader(output)
              parser.Load(parsed, reader)
              Expect.equal parsed.Triples.Count g.Triples.Count "Triple count should match (7)"
              Expect.isTrue (parsed.Difference(g).AreEqual) "Round-tripped Turtle graph must be isomorphic to original"

          testCase "RdfXml round-trip preserves graph isomorphism"
          <| fun _ ->
              let g = createRichTestGraph ()
              use ms = new MemoryStream()
              RdfXmlFormatter.writeRdfXml g ms
              ms.Seek(0L, SeekOrigin.Begin) |> ignore
              let output = (new StreamReader(ms)).ReadToEnd()
              let parsed = new Graph()
              let parser = RdfXmlParser()
              use reader = new StringReader(output)
              parser.Load(parsed, reader)
              Expect.equal parsed.Triples.Count g.Triples.Count "Triple count should match (7)"
              Expect.isTrue (parsed.Difference(g).AreEqual) "Round-tripped RDF/XML graph must be isomorphic to original"

          testCase "Turtle round-trip with Unicode characters"
          <| fun _ ->
              let g = new Graph()
              let s = g.CreateUriNode(UriFactory.Root.Create("http://example.org/item/1"))
              let p = g.CreateUriNode(UriFactory.Root.Create("http://example.org/name"))
              g.Assert(Triple(s, p, g.CreateLiteralNode("Ångström"))) |> ignore
              let p2 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/label"))
              g.Assert(Triple(s, p2, g.CreateLiteralNode("日本語"))) |> ignore
              use ms = new MemoryStream()
              TurtleFormatter.writeTurtle g ms
              ms.Seek(0L, SeekOrigin.Begin) |> ignore
              let output = (new StreamReader(ms)).ReadToEnd()
              let parsed = new Graph()
              use reader = new StringReader(output)
              TurtleParser().Load(parsed, reader)
              Expect.isTrue (parsed.Difference(g).AreEqual) "Unicode literals must survive Turtle round-trip"

          testCase "RdfXml round-trip with Unicode characters"
          <| fun _ ->
              let g = new Graph()
              let s = g.CreateUriNode(UriFactory.Root.Create("http://example.org/item/2"))
              let p = g.CreateUriNode(UriFactory.Root.Create("http://example.org/name"))
              g.Assert(Triple(s, p, g.CreateLiteralNode("Ångström"))) |> ignore
              let p2 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/label"))
              g.Assert(Triple(s, p2, g.CreateLiteralNode("日本語"))) |> ignore
              use ms = new MemoryStream()
              RdfXmlFormatter.writeRdfXml g ms
              ms.Seek(0L, SeekOrigin.Begin) |> ignore
              let output = (new StreamReader(ms)).ReadToEnd()
              let parsed = new Graph()
              use reader = new StringReader(output)
              RdfXmlParser().Load(parsed, reader)
              Expect.isTrue (parsed.Difference(g).AreEqual) "Unicode literals must survive RDF/XML round-trip"

          testCase "JsonLdFormatter renders typed literals correctly"
          <| fun _ ->
              let g = createTestGraph ()
              use ms = new MemoryStream()
              JsonLdFormatter.writeJsonLd g ms
              ms.Seek(0L, SeekOrigin.Begin) |> ignore
              let output = (new StreamReader(ms)).ReadToEnd()
              let doc = System.Text.Json.JsonDocument.Parse(output)
              let age = doc.RootElement.GetProperty("Age")
              Expect.equal age.ValueKind System.Text.Json.JsonValueKind.Number "Age should be rendered as JSON number"

          testCase "JsonLdFormatter uses @graph for multiple subjects"
          <| fun _ ->
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

          testCase "JsonLdFormatter @graph path preserves typed literals"
          <| fun _ ->
              let g = new Graph()
              let s1 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/a"))
              let s2 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/b"))
              let intPred = g.CreateUriNode(UriFactory.Root.Create("http://example.org/count"))
              let boolPred = g.CreateUriNode(UriFactory.Root.Create("http://example.org/active"))
              let doublePred = g.CreateUriNode(UriFactory.Root.Create("http://example.org/score"))
              let stringPred = g.CreateUriNode(UriFactory.Root.Create("http://example.org/label"))

              let xsd suffix =
                  UriFactory.Root.Create(sprintf "http://www.w3.org/2001/XMLSchema#%s" suffix)
              // Subject 1: integer + boolean
              g.Assert(Triple(s1, intPred, g.CreateLiteralNode("42", xsd "integer")))
              |> ignore

              g.Assert(Triple(s1, boolPred, g.CreateLiteralNode("true", xsd "boolean")))
              |> ignore
              // Subject 2: double + plain string
              g.Assert(Triple(s2, doublePred, g.CreateLiteralNode("3.14", xsd "double")))
              |> ignore

              g.Assert(Triple(s2, stringPred, g.CreateLiteralNode("hello"))) |> ignore
              use ms = new MemoryStream()
              JsonLdFormatter.writeJsonLd g ms
              ms.Seek(0L, SeekOrigin.Begin) |> ignore
              let output = (new StreamReader(ms)).ReadToEnd()
              let doc = System.Text.Json.JsonDocument.Parse(output)
              let root = doc.RootElement
              let hasGraph = root.TryGetProperty("@graph") |> fst
              Expect.isTrue hasGraph "Multi-subject should use @graph"
              let graphArr = root.GetProperty("@graph")
              Expect.equal (graphArr.GetArrayLength()) 2 "Should have 2 subjects in @graph"
              // Find subject a and b by @id
              let items = [ for i in 0 .. graphArr.GetArrayLength() - 1 -> graphArr.[i] ]

              let subjA =
                  items
                  |> List.find (fun e -> e.GetProperty("@id").GetString() = "http://example.org/a")

              let subjB =
                  items
                  |> List.find (fun e -> e.GetProperty("@id").GetString() = "http://example.org/b")
              // Subject A: integer rendered as JSON number
              let countVal = subjA.GetProperty("count")

              Expect.equal
                  countVal.ValueKind
                  System.Text.Json.JsonValueKind.Number
                  "integer should be JSON number in @graph"

              Expect.equal (countVal.GetInt64()) 42L "integer value should be 42"
              // Subject A: boolean rendered as JSON true/false
              let activeVal = subjA.GetProperty("active")

              Expect.equal
                  activeVal.ValueKind
                  System.Text.Json.JsonValueKind.True
                  "boolean true should be JSON true in @graph"
              // Subject B: double rendered as JSON number
              let scoreVal = subjB.GetProperty("score")

              Expect.equal
                  scoreVal.ValueKind
                  System.Text.Json.JsonValueKind.Number
                  "double should be JSON number in @graph"

              Expect.floatClose Accuracy.medium (scoreVal.GetDouble()) 3.14 "double value should be 3.14"
              // Subject B: plain string rendered as JSON string
              let labelVal = subjB.GetProperty("label")
              Expect.equal labelVal.ValueKind System.Text.Json.JsonValueKind.String "plain string should be JSON string"
              Expect.equal (labelVal.GetString()) "hello" "string value should be 'hello'"

          testCase "JsonLdFormatter @graph path handles decimal as string"
          <| fun _ ->
              // Decimal datatype is not in the special-cased list, so it falls through to WriteString
              let g = new Graph()
              let s1 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/x"))
              let s2 = g.CreateUriNode(UriFactory.Root.Create("http://example.org/y"))
              let pricePred = g.CreateUriNode(UriFactory.Root.Create("http://example.org/price"))
              let namePred = g.CreateUriNode(UriFactory.Root.Create("http://example.org/name"))

              let xsd suffix =
                  UriFactory.Root.Create(sprintf "http://www.w3.org/2001/XMLSchema#%s" suffix)

              g.Assert(Triple(s1, pricePred, g.CreateLiteralNode("9.99", xsd "decimal")))
              |> ignore

              g.Assert(Triple(s2, namePred, g.CreateLiteralNode("item"))) |> ignore
              use ms = new MemoryStream()
              JsonLdFormatter.writeJsonLd g ms
              ms.Seek(0L, SeekOrigin.Begin) |> ignore
              let output = (new StreamReader(ms)).ReadToEnd()
              let doc = System.Text.Json.JsonDocument.Parse(output)
              let graphArr = doc.RootElement.GetProperty("@graph")
              let items = [ for i in 0 .. graphArr.GetArrayLength() - 1 -> graphArr.[i] ]

              let subjX =
                  items
                  |> List.find (fun e -> e.GetProperty("@id").GetString() = "http://example.org/x")
              // Decimal falls through to string in JSON-LD formatter
              let priceVal = subjX.GetProperty("price")

              Expect.equal
                  priceVal.ValueKind
                  System.Text.Json.JsonValueKind.String
                  "decimal should be written as string"

              Expect.equal (priceVal.GetString()) "9.99" "decimal value should be '9.99'"

          testCase "Turtle handles graph with blank nodes"
          <| fun _ ->
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
              Expect.equal parsed.Triples.Count 2 "Should preserve both triples with blank node" ]
