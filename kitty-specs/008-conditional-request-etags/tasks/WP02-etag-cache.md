---
work_package_id: "WP02"
title: "MailboxProcessor ETag Cache"
lane: "done"
dependencies: ["WP01"]
requirement_refs: ["FR-009", "FR-015"]
subtasks: ["T006", "T007", "T008", "T009", "T010"]
agent: "claude-opus-reviewer"
shell_pid: "40917"
reviewed_by: "Ryan Riley"
review_status: "approved"
history:
  - timestamp: "2026-03-07T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- MailboxProcessor ETag Cache

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP02 --base WP01
```

Depends on WP01 (core ETag types in `ETag.fs`).

---

## Objectives & Success Criteria

- Implement `ETagCache` type wrapping a `MailboxProcessor<ETagCacheMessage>` with serialized access
- Support operations: GetETag (reply channel), SetETag (fire-and-forget), Invalidate, InvalidateAll, GetStats
- Implement LRU eviction when cache exceeds configurable `maxEntries` (default 10,000)
- Implement `IDisposable` for clean MailboxProcessor shutdown
- Wire `MailboxProcessor.Error` event to `ILogger` (Constitution principle VII)
- Provide `AddETagCache` DI extension for `IServiceCollection`
- All operations serialized via MailboxProcessor -- no data races under concurrent access

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/008-conditional-request-etags/research.md` -- Decision 4: MailboxProcessor Cache Design
- `kitty-specs/008-conditional-request-etags/data-model.md` -- ETagCache entity definition
- `src/Frank.Statecharts/Store.fs` -- Reference for MailboxProcessor patterns in Frank

**Key constraints**:
- All new code lives in `src/Frank/ETagCache.fs` -- added to Frank.fsproj after `ETag.fs`
- No external NuGet dependencies
- MailboxProcessor pattern consistent with Frank.Statecharts' `MailboxProcessorStore`
- `GetETag` is the only blocking operation (uses `AsyncReplyChannel`); all others are fire-and-forget
- Cache is a singleton in DI; created at middleware registration, disposed on app shutdown
- `ILogger<ETagCache>` required for error logging
- LRU eviction on `SetETag` when at capacity -- O(n) scan is acceptable for default 10k entries

---

## Subtasks & Detailed Guidance

### Subtask T006 -- Create `ETagCache.fs` with message DU and CacheEntry record

**Purpose**: Define the types that the MailboxProcessor operates on.

**Steps**:
1. Create `src/Frank/ETagCache.fs`
2. Use namespace `Frank`
3. Define the following types:

```fsharp
namespace Frank

open System

/// A single entry in the ETag cache.
type CacheEntry =
    { ETag: string
      LastAccessed: DateTimeOffset
      ComputedAt: DateTimeOffset }

/// Diagnostic statistics for the ETag cache.
type CacheStats =
    { EntryCount: int
      HitCount: int64
      MissCount: int64 }

/// Messages processed by the ETagCache MailboxProcessor.
type internal ETagCacheMessage =
    | GetETag of resourceKey: string * AsyncReplyChannel<string option>
    | SetETag of resourceKey: string * etag: string
    | InvalidateETag of resourceKey: string
    | InvalidateAll
    | GetStats of AsyncReplyChannel<CacheStats>
    | Stop of AsyncReplyChannel<unit>
```

4. Add `ETagCache.fs` to `Frank.fsproj` after `ETag.fs` and before `Builder.fs`

**Files**: `src/Frank/ETagCache.fs`, `src/Frank/Frank.fsproj` (modified)
**Notes**:
- `ETagCacheMessage` is `internal` -- only the `ETagCache` type consumes it
- `Stop` message enables graceful shutdown (reply channel signals completion)
- `CacheStats` is public for diagnostics/monitoring
- `CacheEntry` tracks `LastAccessed` for LRU eviction and `ComputedAt` for freshness

### Subtask T007 -- Implement `ETagCache` type with MailboxProcessor

**Purpose**: Core cache implementation with serialized access via MailboxProcessor.

**Steps**:
1. In `ETagCache.fs`, implement the `ETagCache` class:

```fsharp
open System.Collections.Generic
open Microsoft.Extensions.Logging

type ETagCache(maxEntries: int, logger: ILogger<ETagCache>) =
    let mutable hitCount = 0L
    let mutable missCount = 0L

    let agent = MailboxProcessor<ETagCacheMessage>.Start(fun inbox ->
        let cache = Dictionary<string, CacheEntry>()
        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | GetETag(key, reply) ->
                match cache.TryGetValue(key) with
                | true, entry ->
                    cache.[key] <- { entry with LastAccessed = DateTimeOffset.UtcNow }
                    hitCount <- hitCount + 1L
                    reply.Reply(Some entry.ETag)
                | false, _ ->
                    missCount <- missCount + 1L
                    reply.Reply(None)
                return! loop ()

            | SetETag(key, etag) ->
                let now = DateTimeOffset.UtcNow
                cache.[key] <- { ETag = etag; LastAccessed = now; ComputedAt = now }
                // LRU eviction handled in T008
                return! loop ()

            | InvalidateETag key ->
                cache.Remove(key) |> ignore
                return! loop ()

            | InvalidateAll ->
                cache.Clear()
                return! loop ()

            | GetStats reply ->
                reply.Reply({ EntryCount = cache.Count
                              HitCount = hitCount
                              MissCount = missCount })
                return! loop ()

            | Stop reply ->
                reply.Reply(())
                // Do not recurse -- exits the loop
        }
        loop ()
    )

    do agent.Error.Add(fun exn ->
        logger.LogError(exn, "ETagCache MailboxProcessor error"))
```

2. Add public methods that post messages to the agent:

```fsharp
    /// Retrieve cached ETag for a resource instance. Returns None on cache miss.
    member _.GetETag(resourceKey: string) : Async<string option> =
        agent.PostAndAsyncReply(fun reply -> GetETag(resourceKey, reply))

    /// Store or update the cached ETag for a resource instance.
    member _.SetETag(resourceKey: string, etag: string) : unit =
        agent.Post(SetETag(resourceKey, etag))

    /// Remove a specific cache entry (e.g., after state transition).
    member _.Invalidate(resourceKey: string) : unit =
        agent.Post(InvalidateETag resourceKey)

    /// Clear the entire cache.
    member _.InvalidateAll() : unit =
        agent.Post(InvalidateAll)

    /// Get diagnostic cache statistics.
    member _.GetStats() : Async<CacheStats> =
        agent.PostAndAsyncReply(fun reply -> GetStats reply)
```

**Files**: `src/Frank/ETagCache.fs`
**Notes**:
- `hitCount`/`missCount` are mutable fields updated inside the MailboxProcessor loop (safe because MailboxProcessor serializes access)
- `GetETag` returns `Async<string option>` -- the middleware will convert to `Task` at the call site
- `SetETag` and `Invalidate` are fire-and-forget (no reply channel) for performance
- The `Error` event handler logs exceptions but does not swallow them -- MailboxProcessor will still terminate on unhandled exceptions

### Subtask T008 -- Implement LRU eviction

**Purpose**: Prevent unbounded memory growth by evicting least-recently-accessed entries when at capacity.

**Steps**:
1. In the `SetETag` handler within the MailboxProcessor loop, add eviction logic:

```fsharp
            | SetETag(key, etag) ->
                let now = DateTimeOffset.UtcNow
                cache.[key] <- { ETag = etag; LastAccessed = now; ComputedAt = now }

                // LRU eviction: if over capacity, remove the least recently accessed entry
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
```

**Files**: `src/Frank/ETagCache.fs`
**Notes**:
- O(n) scan on eviction is acceptable for the default 10,000 entry limit
- Eviction only triggers when `cache.Count > maxEntries` (after the new entry is added)
- Only one entry is evicted per `SetETag` call -- this keeps the cache at most `maxEntries + 1` briefly
- For production workloads needing higher capacity, the `maxEntries` parameter is configurable
- Consider: if the same key is being updated (not a new entry), no eviction is needed. The `cache.Count > maxEntries` check handles this implicitly since the count doesn't change on update.

### Subtask T009 -- Implement IDisposable on ETagCache

**Purpose**: Clean MailboxProcessor lifecycle with graceful shutdown.

**Steps**:
1. Add `IDisposable` implementation to `ETagCache`:

```fsharp
    interface IDisposable with
        member _.Dispose() =
            try
                agent.PostAndReply(fun reply -> Stop reply, timeout = 5000)
            with
            | :? TimeoutException ->
                logger.LogWarning("ETagCache disposal timed out after 5 seconds")
            (agent :> IDisposable).Dispose()
```

**Files**: `src/Frank/ETagCache.fs`
**Notes**:
- The `Stop` message causes the MailboxProcessor loop to exit cleanly (no more recursion)
- A 5-second timeout prevents hanging on shutdown if the agent is stuck
- After the agent stops, `Dispose()` is called on the MailboxProcessor itself
- The `try/with` ensures disposal proceeds even if the Stop message times out
- When registered as a singleton in DI, ASP.NET Core disposes it on application shutdown

### Subtask T010 -- Create `ETagCacheTests.fs` with tests

**Purpose**: Validate cache operations, LRU eviction, and concurrent access.

**Steps**:
1. Create `test/Frank.Tests/ETagCacheTests.fs`
2. Add to `Frank.Tests.fsproj` before `Program.fs`
3. Write Expecto tests covering:

**a. Basic operations**:
- `GetETag` returns `None` for unknown key (cache miss)
- `SetETag` followed by `GetETag` returns the stored value
- `Invalidate` removes the entry; subsequent `GetETag` returns `None`
- `InvalidateAll` clears all entries
- `GetStats` reflects hit/miss counts accurately

**b. LRU eviction**:
- Set entries up to `maxEntries` capacity; verify all are retrievable
- Set one more entry beyond capacity; verify the least-recently-accessed entry was evicted
- Access an entry (GetETag) to update its `LastAccessed`; verify it survives eviction over an older untouched entry

**c. Lifecycle**:
- Dispose the cache; verify it can be disposed without error
- Operations after disposal should not hang (MailboxProcessor is stopped)

**d. Concurrent access**:
- Multiple async tasks concurrently calling `GetETag`/`SetETag` on the same key
- Verify no data corruption or deadlocks

**Example test structure**:

```fsharp
module ETagCacheTests

open System
open Expecto
open Frank
open Microsoft.Extensions.Logging.Abstractions

let makeCache maxEntries =
    new ETagCache(maxEntries, NullLogger<ETagCache>.Instance)

[<Tests>]
let cacheTests =
    testList "ETagCache" [
        testAsync "GetETag returns None for unknown key" {
            use cache = makeCache 100
            let! result = cache.GetETag("unknown")
            Expect.isNone result "should be None"
        }

        testAsync "SetETag then GetETag returns stored value" {
            use cache = makeCache 100
            cache.SetETag("key1", "\"abc\"")
            // Small delay to let fire-and-forget message process
            do! Async.Sleep 10
            let! result = cache.GetETag("key1")
            Expect.equal result (Some "\"abc\"") "should return stored ETag"
        }

        testAsync "LRU eviction removes oldest entry" {
            use cache = makeCache 3
            cache.SetETag("a", "\"1\"")
            do! Async.Sleep 10
            cache.SetETag("b", "\"2\"")
            do! Async.Sleep 10
            cache.SetETag("c", "\"3\"")
            do! Async.Sleep 10
            // Cache is at capacity (3). Adding one more should evict "a".
            cache.SetETag("d", "\"4\"")
            do! Async.Sleep 10
            let! a = cache.GetETag("a")
            Expect.isNone a "oldest entry should be evicted"
            let! d = cache.GetETag("d")
            Expect.equal d (Some "\"4\"") "newest entry should be present"
        }
    ]
```

**Files**: `test/Frank.Tests/ETagCacheTests.fs`, `test/Frank.Tests/Frank.Tests.fsproj` (modified)
**Notes**:
- Use `NullLogger<ETagCache>.Instance` for tests (no actual logging)
- Fire-and-forget operations (`SetETag`, `Invalidate`) need a small `Async.Sleep` before asserting, since the message may not have been processed yet
- For the LRU test, use a very small `maxEntries` (e.g., 3) to make eviction deterministic
- Concurrent tests can use `Async.Parallel` with multiple operations

**Validation**: `dotnet test test/Frank.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build src/Frank/` to verify compilation on all 3 targets
- Run `dotnet test test/Frank.Tests/` to verify all cache tests pass
- Run `dotnet build Frank.sln` to verify solution-level build succeeds

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| MailboxProcessor `Stop` hangs on disposal | 5-second timeout with fallback Dispose |
| Fire-and-forget message ordering in tests | Small `Async.Sleep` delays; accept minor non-determinism in test timing |
| LRU O(n) scan overhead at high capacity | Acceptable for 10k default; document that higher capacity may need a different eviction strategy |
| Hit/miss counters as mutable fields | Safe because MailboxProcessor serializes all access; only updated inside the loop |

---

## Review Guidance

- Verify `ETagCache.fs` is in `Frank.fsproj` after `ETag.fs` and before `Builder.fs`
- Verify MailboxProcessor `Error` event is wired to `ILogger.LogError`
- Verify `Stop` message causes clean loop exit (no recursion after Stop)
- Verify `IDisposable` uses timeout on `PostAndReply` to prevent hang
- Verify LRU eviction scans by `LastAccessed` and removes the oldest entry
- Verify `GetETag` updates `LastAccessed` (not just `SetETag`)
- Verify tests cover cache miss, cache hit, invalidation, eviction, and disposal
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-15T19:34:33Z – unknown – lane=for_review – Moved to for_review
- 2026-03-15T19:42:29Z – claude-opus-reviewer – shell_pid=40917 – lane=doing – Started review via workflow command
- 2026-03-15T19:43:19Z – claude-opus-reviewer – shell_pid=40917 – lane=done – Review passed: ETagCache with MailboxProcessor, LRU eviction, IDisposable, and error logging all correctly implemented. 9 new tests all pass (45 total). Struct records for CacheEntry/CacheStats reduce allocations. ensureNotDisposed guards on all public methods. Dictionary and counters as class fields is safe since MailboxProcessor serializes access. InvalidateAll resets stats (minor deviation, reasonable).
