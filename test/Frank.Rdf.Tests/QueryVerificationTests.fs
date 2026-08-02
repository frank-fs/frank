module Frank.Rdf.Tests.QueryVerificationTests

open Expecto
open VDS.RDF
open VDS.RDF.Query
open VDS.RDF.Query.Datasets
open VDS.RDF.Parsing
open Frank.Rdf

/// Runs a SPARQL SELECT query against a graph and returns the SparqlResultSet.
let private select (graph: Graph) (queryText: string) : SparqlResultSet =
    let dataset = new InMemoryDataset(graph :> IGraph)
    let processor = new LeviathanQueryProcessor(dataset)
    let parser = SparqlQueryParser()
    let query = parser.ParseFromString(queryText)

    match processor.ProcessQuery(query) with
    | :? SparqlResultSet as rs -> rs
    | other -> failwithf "Expected a SparqlResultSet, got %A" other

[<Tests>]
let tests =
    testList
        "Query verification"
        [ test "two-hop SPARQL query follows a blank-node reference and retrieves the correct value" {
              // A game whose schema:numberOfPlayers points at a separate, anonymous node that itself
              // carries a schema:value literal -- the "dereference and follow" story, but through a
              // blank node (the harder case: no IRI to write directly into the query).
              let players = Node.blank ()

              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"

                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              typ "schema:Game"
                              propertyNode "schema:numberOfPlayers" players
                          }
                      )

                      about (describe players { propertyInt "schema:value" 2 })
                  }

              let graph = Doc.toGraph doc

              let rs =
                  select
                      graph
                      """
                      PREFIX schema: <https://schema.org/>
                      SELECT ?value WHERE {
                          <https://example.org/g1> schema:numberOfPlayers ?p .
                          ?p schema:value ?value .
                      }
                      """

              Expect.equal rs.Count 1 "Exactly one result: the chained pattern matched through the blank node"
              let result = rs |> Seq.exactlyOne
              let value = result.["value"] :?> ILiteralNode
              Expect.equal value.Value "2" "The value retrieved by following the reference is the one asserted on the target node"
          }

          test "rdf:type is queryable via the SPARQL 'a' shorthand" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"
                      about (describe (Node.Iri "https://example.org/g1") { typ "schema:Game" })
                  }

              let graph = Doc.toGraph doc

              let rs =
                  select
                      graph
                      """
                      SELECT ?type WHERE {
                          <https://example.org/g1> a ?type .
                      }
                      """

              Expect.equal rs.Count 1 "One type asserted"
              let result = rs |> Seq.exactlyOne
              let typeNode = result.["type"] :?> IUriNode
              Expect.equal typeNode.Uri.AbsoluteUri "https://schema.org/Game" "rdf:type resolved to the expanded CURIE, found via 'a'"
          }

          test "multi-valued properties are all independently retrievable via SPARQL" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"

                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              propertyNode "schema:sameAs" (Node.Iri "http://www.wikidata.org/entity/Q210339")
                              propertyNode "schema:sameAs" (Node.Iri "http://dbpedia.org/resource/Tic-tac-toe")
                          }
                      )
                  }

              let graph = Doc.toGraph doc

              let rs =
                  select
                      graph
                      """
                      PREFIX schema: <https://schema.org/>
                      SELECT ?sameAs WHERE {
                          <https://example.org/g1> schema:sameAs ?sameAs .
                      }
                      """

              Expect.equal rs.Count 2 "Both sameAs assertions are separately bound"

              let values =
                  rs
                  |> Seq.map (fun r -> (r.["sameAs"] :?> IUriNode).Uri.AbsoluteUri)
                  |> Set.ofSeq

              Expect.equal
                  values
                  (Set.ofList [ "http://www.wikidata.org/entity/Q210339"; "http://dbpedia.org/resource/Tic-tac-toe" ])
                  "Both distinct object values are queryable, not just one"
          }

          test "plain IGraph triple-pattern matching retrieves the correct object without SPARQL" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"

                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              propertyString "schema:name" "Tic-tac-toe"
                              propertyInt "schema:numberOfPlayers" 2
                          }
                      )
                  }

              let graph = Doc.toGraph doc

              let subjectNode = graph.CreateUriNode(System.Uri "https://example.org/g1")
              let predicateNode = graph.CreateUriNode(System.Uri "https://schema.org/name")

              let matches = graph.GetTriplesWithSubjectPredicate(subjectNode, predicateNode) |> List.ofSeq

              Expect.equal matches.Length 1 "Exactly one triple matches this subject/predicate pair"
              let objectValue = (matches.Head.Object :?> ILiteralNode).Value
              Expect.equal objectValue "Tic-tac-toe" "Retrieved object value matches what was asserted, via the plain IGraph API"
          }

          test "a query using the CURIE-resolved absolute IRI finds triples authored via a declared prefix" {
              let doc =
                  rdf {
                      prefix "schema" "https://schema.org/"

                      about (
                          describe (Node.Iri "https://example.org/g1") {
                              propertyString "schema:name" "Tic-tac-toe"
                          }
                      )
                  }

              let graph = Doc.toGraph doc

              // The query below never mentions the "schema" prefix at all -- it uses the resolved
              // absolute IRI directly. If resolveIri had failed to expand the CURIE (leaving a literal
              // "schema:name" string in the graph, or something else entirely), this query would match
              // nothing.
              let rs =
                  select
                      graph
                      """
                      SELECT ?name WHERE {
                          <https://example.org/g1> <https://schema.org/name> ?name .
                      }
                      """

              Expect.equal rs.Count 1 "The resolved absolute IRI matches the triple asserted via the 'schema:' CURIE"
              let result = rs |> Seq.exactlyOne
              let nameValue = (result.["name"] :?> ILiteralNode).Value
              Expect.equal nameValue "Tic-tac-toe" "CURIE was expanded to the absolute IRI in the graph, not left as literal text"
          } ]
