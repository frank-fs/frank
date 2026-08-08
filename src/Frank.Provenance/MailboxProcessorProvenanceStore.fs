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
type MailboxProcessorProvenanceStore(config: ProvenanceStoreConfig, logger: ILogger, ?journal: IProvenanceJournal) =
    let store = new TripleStore()
    let snapshotEvery = max 1 config.SnapshotEvery

    let graphNameFor (record: ProvenanceRecord) : Uri =
        match record.Activity with
        | Node.Iri s -> Uri s
        | Node.Blank id -> Uri(sprintf "urn:frank:provenance:%s" id)

    // A recovered graph's Name came from graphNameFor's own output at the time it was first
    // appended (see graphNameFor above), so it's always an IUriNode -- the urn:frank:provenance:...
    // fallback in graphNameFor still produces a URI, never a blank node. The Guid fallback below only
    // guards a journal implementation that hands back a graph built some other way.
    let graphUriOf (g: IGraph) : Uri =
        match g.Name with
        | :? IUriNode as un -> un.Uri
        | _ -> Uri(sprintf "urn:frank:provenance:recovered:%s" (Guid.NewGuid().ToString()))

    let runQuery (query: ProvenanceQuery) : SparqlQueryResult =
        let sparqlQuery = toSparqlQuery query
        let dataset = new InMemoryDataset(store, true)
        let processor = new LeviathanQueryProcessor(dataset)

        match processor.ProcessQuery(sparqlQuery) with
        | :? SparqlResultSet as rs -> SparqlQueryResult.Bindings rs
        | :? IGraph as g -> SparqlQueryResult.Graph g
        | other -> failwithf "Frank.Provenance: unexpected SPARQL result shape %A" other

    let recoveredEntries =
        let recovered =
            journal |> Option.map (fun j -> j.Recover() |> List.ofSeq) |> Option.defaultValue []

        for g in recovered do
            store.Add(g, true) |> ignore

        recovered |> List.map (fun g -> g.Name, graphUriOf g)

    let agent =
        MailboxProcessor<StoreMessage>.Start(fun inbox ->
            let rec loop (entries: (IRefNode * Uri) list) (appendCount: int) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Append record ->
                        // A malformed record (a relative-IRI Activity, an invalid prefix/IRI Doc.toGraph
                        // rejects, ...) must not kill the mailbox loop: an unhandled exception here stops
                        // MailboxProcessor's Receive loop for good, so every subsequent Append silently
                        // vanishes into a dead mailbox and every subsequent Query (PostAndReply, infinite
                        // timeout) blocks its caller forever. Catch, log, and keep the previous, known-good
                        // entries state -- the bad record is dropped, not retried.
                        try
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

                            let appendCount = appendCount + 1

                            // Journal failures are isolated from the record's own bookkeeping. The
                            // record is already in `store` by this point, so letting a throwing
                            // journal fall through to the outer handler would return the OLD entries
                            // list -- leaving that graph in the store but absent from the eviction
                            // list, so MaxRecords could never reclaim it. Durability is best-effort;
                            // the store's bound is not.
                            match journal with
                            | Some j ->
                                try
                                    j.Append(namedGraph :> IGraph)

                                    if appendCount % snapshotEvery = 0 then
                                        j.Snapshot(store.Graphs |> List.ofSeq)
                                with ex ->
                                    logger.LogError(ex, "Journal write failed for {GraphName}; the record is still in the store but is not durable", graphName)
                            | None -> ()

                            let updated = entries @ [ namedGraph.Name, graphName ]

                            let retained =
                                if updated.Length > config.MaxRecords then
                                    // Clamp so eviction can never remove the record just appended above,
                                    // regardless of how config.MaxRecords/EvictionBatchSize are configured
                                    // (e.g. MaxRecords <= 0, or EvictionBatchSize >= MaxRecords): always
                                    // leave at least the newest entry behind.
                                    let evictCount =
                                        [ config.EvictionBatchSize; updated.Length - 1 ] |> List.min |> max 0

                                    for evictedName, evictedUri in updated |> List.truncate evictCount do
                                        store.Remove(evictedName) |> ignore
                                        logger.LogDebug("Evicted provenance record {GraphName}", evictedUri)

                                    updated |> List.skip evictCount
                                else
                                    updated

                            return! loop retained appendCount
                        with ex ->
                            logger.LogError(ex, "Failed to append a provenance record; dropping it and continuing")
                            return! loop entries appendCount

                    | Query(query, reply) ->
                        // Same story as Append, but the caller is blocked in PostAndReply waiting on
                        // `reply` -- so on failure we must still Reply (with an empty graph) rather than
                        // let the exception propagate, or that caller hangs forever with no log line to
                        // explain it.
                        let result =
                            try
                                runQuery query
                            with ex ->
                                logger.LogError(ex, "Failed to run provenance query {Query}; returning an empty graph", query)
                                SparqlQueryResult.Graph(new Graph() :> IGraph)

                        reply.Reply(result)
                        return! loop entries appendCount
                }

            loop recoveredEntries 0)

    interface IProvenanceStore with
        member _.Append(record: ProvenanceRecord) = agent.Post(Append record)
        member _.Query(query: ProvenanceQuery) = agent.PostAndReply(fun reply -> Query(query, reply))

    interface IDisposable with
        member _.Dispose() = (agent :> IDisposable).Dispose()
