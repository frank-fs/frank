namespace Frank.Provenance

open System
open Microsoft.Extensions.Logging
open VDS.RDF
open VDS.RDF.Query
open VDS.RDF.Query.Datasets
open Frank.Rdf

type private StoreMessage =
    | Append of ProvenanceRecord
    | Query of ProvenanceQuery * AsyncReplyChannel<SparqlQueryResult>

[<Sealed>]
type MailboxProcessorProvenanceStore(config: ProvenanceStoreConfig, logger: ILogger) =
    let store = new TripleStore()

    let graphNameFor (record: ProvenanceRecord) : Uri =
        match record.Activity with
        | Node.Iri s -> Uri s
        | Node.Blank id -> Uri(sprintf "urn:frank:provenance:%s" id)

    let runQuery (query: ProvenanceQuery) : SparqlQueryResult =
        let sparqlQuery = toSparqlQuery query
        let dataset = new InMemoryDataset(store, true)
        let processor = new LeviathanQueryProcessor(dataset)

        match processor.ProcessQuery(sparqlQuery) with
        | :? SparqlResultSet as rs -> SparqlQueryResult.Bindings rs
        | :? IGraph as g -> SparqlQueryResult.Graph g
        | other -> failwithf "Frank.Provenance: unexpected SPARQL result shape %A" other

    let agent =
        MailboxProcessor<StoreMessage>.Start(fun inbox ->
            let rec loop (entries: (IRefNode * Uri) list) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Append record ->
                        let graphName = graphNameFor record
                        // dotNetRDF names a graph via its constructor (IRefNode/Uri), not via a mutable
                        // property: setting Graph.BaseUri does NOT set Graph.Name, so a graph built that
                        // way is added as the store's unnamed default graph, not a named graph. Build the
                        // record's content unnamed (via Doc.toGraph), then merge it into a freshly
                        // constructed, properly named graph before adding it to the store.
                        let content = record |> ProvenanceRecord.toDoc |> Doc.toGraph
                        let namedGraph = new Graph(graphName)
                        namedGraph.Merge(content :> IGraph)
                        store.Add(namedGraph :> IGraph, true) |> ignore
                        logger.LogDebug("Appended provenance record for activity {GraphName}", graphName)

                        let updated = entries @ [ namedGraph.Name, graphName ]

                        let retained =
                            if updated.Length > config.MaxRecords then
                                let evictCount = min config.EvictionBatchSize updated.Length

                                for evictedName, evictedUri in updated |> List.truncate evictCount do
                                    store.Remove(evictedName) |> ignore
                                    logger.LogDebug("Evicted provenance record {GraphName}", evictedUri)

                                updated |> List.skip evictCount
                            else
                                updated

                        return! loop retained

                    | Query(query, reply) ->
                        reply.Reply(runQuery query)
                        return! loop entries
                }

            loop [])

    interface IProvenanceStore with
        member _.Append(record: ProvenanceRecord) = agent.Post(Append record)
        member _.Query(query: ProvenanceQuery) = agent.PostAndReply(fun reply -> Query(query, reply))

    interface IDisposable with
        member _.Dispose() = (agent :> IDisposable).Dispose()
