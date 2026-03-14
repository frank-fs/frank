namespace Frank

open System
open System.Collections.Generic
open Microsoft.Extensions.Logging

/// A cached ETag entry with access tracking for LRU eviction.
[<Struct>]
type CacheEntry =
    { ETag: string
      LastAccessed: DateTimeOffset
      ComputedAt: DateTimeOffset }

/// Statistics for the ETag cache.
[<Struct>]
type CacheStats =
    { EntryCount: int
      HitCount: int64
      MissCount: int64 }

type internal ETagCacheMessage =
    | GetETag of resourceKey: string * AsyncReplyChannel<string option>
    | SetETag of resourceKey: string * etag: string
    | InvalidateETag of resourceKey: string
    | InvalidateAll
    | GetStats of AsyncReplyChannel<CacheStats>
    | Stop of AsyncReplyChannel<unit>

/// A MailboxProcessor-backed concurrent cache for ETag values with LRU eviction.
type ETagCache(maxEntries: int, logger: ILogger<ETagCache>) =
    let mutable disposed = false
    let cache = Dictionary<string, CacheEntry>()
    let mutable hitCount = 0L
    let mutable missCount = 0L

    let agent =
        MailboxProcessor<ETagCacheMessage>.Start(fun inbox ->
            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | GetETag(resourceKey, reply) ->
                        match cache.TryGetValue(resourceKey) with
                        | true, entry ->
                            hitCount <- hitCount + 1L

                            cache.[resourceKey] <-
                                { entry with
                                    LastAccessed = DateTimeOffset.UtcNow }

                            reply.Reply(Some entry.ETag)
                        | false, _ ->
                            missCount <- missCount + 1L
                            reply.Reply(None)

                        return! loop ()
                    | SetETag(resourceKey, etag) ->
                        let now = DateTimeOffset.UtcNow

                        cache.[resourceKey] <-
                            { ETag = etag
                              LastAccessed = now
                              ComputedAt = now }

                        if cache.Count > maxEntries then
                            let mutable oldestKey = ""
                            let mutable oldestTime = DateTimeOffset.MaxValue

                            for kvp in cache do
                                if kvp.Value.LastAccessed < oldestTime then
                                    oldestKey <- kvp.Key
                                    oldestTime <- kvp.Value.LastAccessed

                            if oldestKey <> "" then
                                cache.Remove(oldestKey) |> ignore

                        return! loop ()
                    | InvalidateETag resourceKey ->
                        cache.Remove(resourceKey) |> ignore
                        return! loop ()
                    | InvalidateAll ->
                        cache.Clear()
                        hitCount <- 0L
                        missCount <- 0L
                        return! loop ()
                    | GetStats reply ->
                        reply.Reply(
                            { EntryCount = cache.Count
                              HitCount = hitCount
                              MissCount = missCount }
                        )

                        return! loop ()
                    | Stop reply ->
                        reply.Reply()
                        return ()
                }

            loop ())

    do agent.Error.Add(fun exn -> logger.LogError(exn, "ETagCache MailboxProcessor error"))

    let ensureNotDisposed () =
        if disposed then
            raise (ObjectDisposedException(nameof ETagCache))

    /// Retrieves the cached ETag for the given resource key, updating its LastAccessed time.
    member _.GetETag(resourceKey: string) : Async<string option> =
        ensureNotDisposed ()
        agent.PostAndAsyncReply(fun reply -> GetETag(resourceKey, reply))

    /// Stores an ETag value, evicting the least-recently-used entry if capacity is exceeded.
    member _.SetETag(resourceKey: string, etag: string) : unit =
        ensureNotDisposed ()
        agent.Post(SetETag(resourceKey, etag))

    /// Removes the cached entry for the given resource key.
    member _.Invalidate(resourceKey: string) : unit =
        ensureNotDisposed ()
        agent.Post(InvalidateETag resourceKey)

    /// Clears all cached entries and resets statistics.
    member _.InvalidateAll() : unit =
        ensureNotDisposed ()
        agent.Post(InvalidateAll)

    /// Returns current cache statistics.
    member _.GetStats() : Async<CacheStats> =
        ensureNotDisposed ()
        agent.PostAndAsyncReply(fun reply -> GetStats reply)

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true

                try
                    agent.PostAndReply((fun reply -> Stop reply), timeout = 5000)
                with :? TimeoutException ->
                    logger.LogWarning("ETagCache disposal timed out after 5 seconds")

                (agent :> IDisposable).Dispose()
