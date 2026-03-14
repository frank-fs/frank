namespace Frank.Provenance

open System
open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.Extensions.Logging

type private StoreMessage =
    | Append of ProvenanceRecord
    | QueryByResource of string * AsyncReplyChannel<ProvenanceRecord list>
    | QueryByAgent of string * AsyncReplyChannel<ProvenanceRecord list>
    | QueryByTimeRange of DateTimeOffset * DateTimeOffset * AsyncReplyChannel<ProvenanceRecord list>
    | Dispose of AsyncReplyChannel<unit>

/// A MailboxProcessor-based implementation of IProvenanceStore.
/// Uses an internal agent for thread-safe, serialized access to the store.
type MailboxProcessorProvenanceStore(config: ProvenanceStoreConfig, logger: ILogger) =

    let mutable disposed = false

    let rebuildIndexes (records: ResizeArray<ProvenanceRecord>) =
        let resourceIndex = Dictionary<string, ResizeArray<int>>()
        let agentIndex = Dictionary<string, ResizeArray<int>>()

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

        resourceIndex, agentIndex

    let agent =
        MailboxProcessor<StoreMessage>.Start(fun inbox ->
            let records = ResizeArray<ProvenanceRecord>()
            let mutable resourceIndex = Dictionary<string, ResizeArray<int>>()
            let mutable agentIndex = Dictionary<string, ResizeArray<int>>()

            let addToIndex (index: Dictionary<string, ResizeArray<int>>) key position =
                match index.TryGetValue(key) with
                | true, indices -> indices.Add(position)
                | false, _ ->
                    let indices = ResizeArray<int>()
                    indices.Add(position)
                    index.[key] <- indices

            let evictIfNeeded () =
                if records.Count > config.MaxRecords then
                    let evictCount = min config.EvictionBatchSize records.Count

                    logger.LogInformation(
                        "Evicting {EvictCount} oldest records (store has {Count}, max {Max})",
                        evictCount,
                        records.Count,
                        config.MaxRecords
                    )

                    records.RemoveRange(0, evictCount)
                    let ri, ai = rebuildIndexes records
                    resourceIndex <- ri
                    agentIndex <- ai

            let lookupByIndex (index: Dictionary<string, ResizeArray<int>>) key =
                match index.TryGetValue(key) with
                | true, indices -> indices |> Seq.map (fun i -> records.[i]) |> Seq.toList
                | false, _ -> []

            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Append record ->
                        let position = records.Count
                        records.Add(record)
                        addToIndex resourceIndex record.ResourceUri position
                        addToIndex agentIndex record.Agent.Id position
                        evictIfNeeded ()
                        return! loop ()

                    | QueryByResource(uri, reply) ->
                        reply.Reply(lookupByIndex resourceIndex uri)
                        return! loop ()

                    | QueryByAgent(agentId, reply) ->
                        reply.Reply(lookupByIndex agentIndex agentId)
                        return! loop ()

                    | QueryByTimeRange(startTime, endTime, reply) ->
                        let results =
                            records
                            |> Seq.filter (fun r -> r.RecordedAt >= startTime && r.RecordedAt <= endTime)
                            |> Seq.toList

                        reply.Reply(results)
                        return! loop ()

                    | Dispose reply ->
                        logger.LogInformation("Disposing provenance store, draining {Count} records", records.Count)

                        records.Clear()
                        resourceIndex.Clear()
                        agentIndex.Clear()
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
            if disposed then
                logger.LogWarning("Attempted to append to disposed provenance store")
            else
                agent.Post(Append record)

        member _.QueryByResource(resourceUri) =
            if disposed then
                Task.FromResult(List.empty<ProvenanceRecord>)
            else
                agent.PostAndAsyncReply(fun reply -> QueryByResource(resourceUri, reply))
                |> Async.StartAsTask

        member _.QueryByAgent(agentId) =
            if disposed then
                Task.FromResult(List.empty<ProvenanceRecord>)
            else
                agent.PostAndAsyncReply(fun reply -> QueryByAgent(agentId, reply))
                |> Async.StartAsTask

        member _.QueryByTimeRange(start, end_) =
            if disposed then
                Task.FromResult(List.empty<ProvenanceRecord>)
            else
                agent.PostAndAsyncReply(fun reply -> QueryByTimeRange(start, end_, reply))
                |> Async.StartAsTask

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                agent.PostAndAsyncReply(fun reply -> Dispose reply) |> Async.RunSynchronously
