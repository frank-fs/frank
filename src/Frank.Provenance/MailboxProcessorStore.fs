namespace Frank.Provenance

open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.Extensions.Logging

type private StoreMessage =
    | Append of ProvenanceRecord
    | QueryByResource of string * AsyncReplyChannel<ProvenanceRecord list>
    | QueryByAgent of string * AsyncReplyChannel<ProvenanceRecord list>
    | QueryByActivityId of string * AsyncReplyChannel<ProvenanceRecord option>
    | Dispose of AsyncReplyChannel<unit>

type MailboxProcessorProvenanceStore(config: ProvenanceStoreConfig, logger: ILogger) =

    let mutable disposed = false

    let ensureNotDisposed () =
        if disposed then
            raise (System.ObjectDisposedException(nameof MailboxProcessorProvenanceStore))

    let rebuildIndexes (records: ResizeArray<ProvenanceRecord>) =
        let resourceIndex = Dictionary<string, ResizeArray<int>>()
        let agentIndex = Dictionary<string, ResizeArray<int>>()
        let activityIndex = Dictionary<string, int>()

        for i = 0 to records.Count - 1 do
            let r = records.[i]

            match resourceIndex.TryGetValue(r.ResourceUri) with
            | true, indices -> indices.Add(i)
            | false, _ ->
                let indices = ResizeArray<int>()
                indices.Add(i)
                resourceIndex.[r.ResourceUri] <- indices

            match agentIndex.TryGetValue(r.Agent.Id) with
            | true, indices -> indices.Add(i)
            | false, _ ->
                let indices = ResizeArray<int>()
                indices.Add(i)
                agentIndex.[r.Agent.Id] <- indices

            activityIndex.[r.Id] <- i

        resourceIndex, agentIndex, activityIndex

    let addToIndex (index: Dictionary<string, ResizeArray<int>>) key position =
        match index.TryGetValue(key) with
        | true, indices -> indices.Add(position)
        | false, _ ->
            let indices = ResizeArray<int>()
            indices.Add(position)
            index.[key] <- indices

    // Removes oldest records when MaxRecords is exceeded and rebuilds indexes.
    // Returns the (possibly rebuilt) index triple; caller reassigns its mutable variables.
    let evictIfNeeded
        (records: ResizeArray<ProvenanceRecord>)
        (resourceIndex: Dictionary<string, ResizeArray<int>>)
        (agentIndex: Dictionary<string, ResizeArray<int>>)
        (activityIndex: Dictionary<string, int>)
        =
        if records.Count > config.MaxRecords then
            let evictCount = min config.EvictionBatchSize records.Count

            logger.LogInformation(
                "Evicting {EvictCount} oldest records (store has {Count}, max {Max})",
                evictCount,
                records.Count,
                config.MaxRecords
            )

            records.RemoveRange(0, evictCount)
            rebuildIndexes records
        else
            resourceIndex, agentIndex, activityIndex

    let lookupByIndex (records: ResizeArray<ProvenanceRecord>) (index: Dictionary<string, ResizeArray<int>>) key =
        match index.TryGetValue(key) with
        | true, indices -> List.init indices.Count (fun j -> records.[indices.[j]])
        | false, _ -> []

    let agent =
        MailboxProcessor<StoreMessage>.Start(fun inbox ->
            let records = ResizeArray<ProvenanceRecord>()
            let mutable resourceIndex = Dictionary<string, ResizeArray<int>>()
            let mutable agentIndex = Dictionary<string, ResizeArray<int>>()
            let mutable activityIndex = Dictionary<string, int>()

            // Rule 10: loop is bounded by the Dispose message. IDisposable.Dispose posts
            // Dispose to the mailbox; the Dispose handler clears state and returns without
            // recursing, ending the agent. No other exit path exists during normal operation.
            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Append record ->
                        let position = records.Count
                        records.Add(record)
                        addToIndex resourceIndex record.ResourceUri position
                        addToIndex agentIndex record.Agent.Id position
                        activityIndex.[record.Id] <- position
                        let ri, ai, acti = evictIfNeeded records resourceIndex agentIndex activityIndex
                        resourceIndex <- ri
                        agentIndex <- ai
                        activityIndex <- acti
                        return! loop ()
                    | QueryByResource(uri, reply) ->
                        reply.Reply(lookupByIndex records resourceIndex uri)
                        return! loop ()
                    | QueryByAgent(agentId, reply) ->
                        reply.Reply(lookupByIndex records agentIndex agentId)
                        return! loop ()
                    | QueryByActivityId(activityId, reply) ->
                        let result =
                            match activityIndex.TryGetValue activityId with
                            | true, pos -> Some records.[pos]
                            | false, _ -> None

                        reply.Reply result
                        return! loop ()
                    | Dispose reply ->
                        logger.LogInformation("Disposing provenance store, draining {Count} records", records.Count)

                        records.Clear()
                        resourceIndex.Clear()
                        agentIndex.Clear()
                        activityIndex.Clear()
                        reply.Reply(())
                }

            loop ())

    do
        agent.Error.Add(fun ex -> logger.LogError(ex, "MailboxProcessor error in provenance store"))

        logger.LogInformation(
            "MailboxProcessorProvenanceStore created (MaxRecords={MaxRecords}, EvictionBatchSize={EvictionBatchSize})",
            config.MaxRecords,
            config.EvictionBatchSize
        )

    interface IProvenanceStore with
        member _.Append(record) =
            ensureNotDisposed ()
            agent.Post(Append record)

        member _.QueryByResource(resourceUri) =
            ensureNotDisposed ()

            agent.PostAndAsyncReply(fun reply -> QueryByResource(resourceUri, reply))
            |> Async.StartImmediateAsTask

        member _.QueryByAgent(agentId) =
            ensureNotDisposed ()

            agent.PostAndAsyncReply(fun reply -> QueryByAgent(agentId, reply))
            |> Async.StartImmediateAsTask

        member _.QueryByActivityId(activityId) =
            ensureNotDisposed ()

            agent.PostAndAsyncReply(fun reply -> QueryByActivityId(activityId, reply))
            |> Async.StartImmediateAsTask

    interface System.IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                try
                    agent.PostAndReply(fun reply -> Dispose reply)
                with :? System.ObjectDisposedException ->
                    logger.LogWarning("Provenance store MailboxProcessor was already disposed")
