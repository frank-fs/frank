module Frank.Provenance.Tests.SparqlHelpers

open System
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Query
open VDS.RDF.Query.Datasets

/// Copy all triples from a source graph into a named graph within a TripleStore.
/// In dotNetRdf 3.x, Graph.Name (IRefNode) determines graph identity in a store,
/// so we create a new Graph with a UriNode name and merge the source triples.
let addAsNamedGraph (store: TripleStore) (sourceGraph: IGraph) (graphUri: Uri) =
    let namedGraph = new Graph(new UriNode(graphUri))
    namedGraph.Merge(sourceGraph)
    store.Add(namedGraph) |> ignore

/// Execute a SPARQL query against a TripleStore with named graphs.
let executeSparqlOnDataset (store: TripleStore) (sparql: string) : SparqlResultSet =
    let dataset = new InMemoryDataset(store)
    let processor = new LeviathanQueryProcessor(dataset :> ISparqlDataset)
    let parser = new SparqlQueryParser()
    let query = parser.ParseFromString(sparql)

    match processor.ProcessQuery(query) with
    | :? SparqlResultSet as results -> results
    | _ -> failwith "Expected SparqlResultSet from SELECT/ASK query"
