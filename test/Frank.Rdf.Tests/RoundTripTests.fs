module Frank.Rdf.Tests.RoundTripTests

open System.IO
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open Frank.Rdf

let private parseBackToGraph (json: string) : IGraph =
    let store = new TripleStore()
    use reader = new StringReader(json)
    (new JsonLdParser()).Load(store, reader)
    store.Graphs |> Seq.exactlyOne

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
          } ]
