module Frank.Rdf.Tests.RoundTripTests

open System
open System.Buffers
open System.IO
open System.Text
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open Frank.Rdf

let private parseBackToGraph (json: string) : IGraph =
    let store = new TripleStore()
    use reader = new StringReader(json)
    (new JsonLdParser()).Load(store, reader)
    store.Graphs |> Seq.exactlyOne

// IBufferWriter implementation for testing writeJsonLdAsync
type private TestBufferWriter() =
    let buffer = ResizeArray<byte>()
    let mutable workingBuffer : byte[] = Array.zeroCreate 4096

    interface IBufferWriter<byte> with
        member _.GetSpan(sizeHint: int) : Span<byte> =
            let size = if sizeHint > 0 then sizeHint else 4096
            if workingBuffer.Length < size then
                workingBuffer <- Array.zeroCreate size
            Span(workingBuffer, 0, size)

        member _.GetMemory(sizeHint: int) : Memory<byte> =
            let size = if sizeHint > 0 then sizeHint else 4096
            if workingBuffer.Length < size then
                workingBuffer <- Array.zeroCreate size
            Memory(workingBuffer, 0, size)

        member _.Advance(count: int) : unit =
            if count > 0 then
                buffer.AddRange(workingBuffer.[0 .. count - 1])

    override _.ToString() : string =
        Encoding.UTF8.GetString(buffer.ToArray())

[<Tests>]
let tests =
    testList
        "Doc.toJsonLd"
        [ test "output is expanded form: no @context, absolute IRIs throughout" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"
                      about (describe (Node.Iri "https://example.org/g1") { typ "schema:Game" })
                  }

              let json = Doc.toJsonLd doc

              Expect.isFalse (json.Contains "@context") "No @context in expanded form"
              Expect.stringContains json "https://schema.org/Game" "Type is fully expanded, not compacted to schema:Game"
          }

          test "round-trips to an isomorphic graph for a single-subject document" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"

                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              typ "schema:Game"
                              propertyString "schema:name" "Tic-tac-toe"
                          }
                      )
                  }

              let originalGraph = Doc.toGraph doc :> IGraph
              let parsedGraph = Doc.toJsonLd doc |> parseBackToGraph

              Expect.isTrue (originalGraph.Equals(parsedGraph)) "Isomorphic after round-trip"
          }

          test "round-trips a language-tagged string literal, preserving @language" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"

                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              propertyLangString "schema:name" "Tic-tac-toe" "en"
                          }
                      )
                  }

              // Pin down the exact value/language pairing on the encode side (Doc.toGraph), not just
              // "some @language key is present somewhere in the JSON" -- a swapped-argument bug
              // (CreateLiteralNode(lang, value) instead of (value, lang)) would still contain the
              // substrings "@language" and "en" in the emitted text, so substring assertions can't
              // distinguish correct from swapped. Inspecting the literal node's own Value/Language
              // properties can.
              let originalGraph = Doc.toGraph doc :> IGraph
              let originalTriple = originalGraph.Triples |> Seq.exactlyOne
              let originalLiteral = originalTriple.Object :?> VDS.RDF.ILiteralNode

              Expect.equal originalLiteral.Value "Tic-tac-toe" "Literal value is the string, not the language tag"
              Expect.equal originalLiteral.Language "en" "Literal language is the tag, not the string"

              // And confirm the same holds after a full encode/decode round-trip through JSON-LD.
              let json = Doc.toJsonLd doc
              let parsedGraph = json |> parseBackToGraph
              let parsedTriple = parsedGraph.Triples |> Seq.exactlyOne
              let parsedLiteral = parsedTriple.Object :?> VDS.RDF.ILiteralNode

              Expect.equal parsedLiteral.Value "Tic-tac-toe" "Round-tripped value still the string"
              Expect.equal parsedLiteral.Language "en" "Round-tripped language still the tag"

              Expect.isTrue (originalGraph.Equals(parsedGraph)) "Isomorphic after round-trip, language tag preserved"
          }

          test "round-trips a two-subject document (a reference plus its target's own statements)" {
              let players = Node.Iri "https://example.org/g1#players"

              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"

                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              typ "schema:Game"
                              propertyNode "schema:numberOfPlayers" players
                          }
                      )

                      about (
                          describe players {
                              typ "schema:QuantitativeValue"
                              propertyInt "schema:value" 2
                          }
                      )
                  }

              let originalGraph = Doc.toGraph doc :> IGraph
              let parsedGraph = Doc.toJsonLd doc |> parseBackToGraph

              Expect.isTrue (originalGraph.Equals(parsedGraph)) "Isomorphic after round-trip, including the reference"
          }

          test "round-trips a document using a real blank node" {
              let anon = Node.blank ()
              let doc = rdf { triple anon "https://schema.org/value" (Value.Literal(Literal.Int 2)) }

              let originalGraph = Doc.toGraph doc :> IGraph
              let parsedGraph = Doc.toJsonLd doc |> parseBackToGraph

              Expect.isTrue (originalGraph.Equals(parsedGraph)) "Isomorphic, blank node identity preserved by shape"
          }

          test "writeJsonLd against an arbitrary TextWriter produces the same text as toJsonLd" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"
                      about (describe (Node.Iri "https://example.org/g1") { typ "schema:Game" })
                  }

              use writer = new StringWriter()
              Doc.writeJsonLd doc writer

              Expect.equal (writer.ToString()) (Doc.toJsonLd doc) "Same output through either path"
          }

          test "writeJsonLd does not close or dispose the writer it's given" {
              let doc = rdf { triple (Node.Iri "https://example.org/g1") "https://schema.org/x" (Value.Literal(Literal.Int 1)) }
              use writer = new StringWriter()
              Doc.writeJsonLd doc writer
              // Would throw ObjectDisposedException if writeJsonLd had closed it.
              writer.Write("still usable")
              Expect.isTrue (writer.ToString().EndsWith "still usable") ""
          }

          test "writeJsonLdAsync writes the same output as toJsonLd to an IBufferWriter" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"
                      about (describe (Node.Iri "https://example.org/g1") { typ "schema:Game" })
                  }

              let bufferWriter = TestBufferWriter()
              let task = Doc.writeJsonLdAsync doc bufferWriter
              task.Wait()
              let asyncOutput = bufferWriter.ToString()
              let expectedOutput = Doc.toJsonLd doc

              Expect.equal asyncOutput expectedOutput "Async output matches sync output"
          }

          test "writeJsonLdAsync round-trips a single-subject document through JSON-LD parser" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"

                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              typ "schema:Game"
                              propertyString "schema:name" "Tic-tac-toe"
                          }
                      )
                  }

              let bufferWriter = TestBufferWriter()
              let task = Doc.writeJsonLdAsync doc bufferWriter
              task.Wait()
              let json = bufferWriter.ToString()

              let originalGraph = Doc.toGraph doc :> IGraph
              let parsedGraph = json |> parseBackToGraph

              Expect.isTrue (originalGraph.Equals(parsedGraph)) "Isomorphic after async round-trip"
          }

          test "writeJsonLdAsync handles a multi-subject document" {
              let players = Node.Iri "https://example.org/g1#players"

              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"

                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              typ "schema:Game"
                              propertyNode "schema:numberOfPlayers" players
                          }
                      )

                      about (
                          describe players {
                              typ "schema:QuantitativeValue"
                              propertyInt "schema:value" 2
                          }
                      )
                  }

              let bufferWriter = TestBufferWriter()
              let task = Doc.writeJsonLdAsync doc bufferWriter
              task.Wait()
              let json = bufferWriter.ToString()

              let originalGraph = Doc.toGraph doc :> IGraph
              let parsedGraph = json |> parseBackToGraph

              Expect.isTrue (originalGraph.Equals(parsedGraph)) "Multi-subject document round-trips correctly"
          } ]
