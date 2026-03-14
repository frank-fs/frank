---
work_package_id: "WP02"
title: "IProvenanceStore + MailboxProcessorStore"
lane: done
dependencies: ["WP01"]
requirement_refs: ["FR-007", "FR-008", "FR-009", "FR-015"]
subtasks: ["T007", "T008", "T009", "T010", "T011"]
history:
  - timestamp: "2026-03-07T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- IProvenanceStore + MailboxProcessorStore

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

Depends on WP01 for core types (`ProvenanceRecord`, `ProvenanceStoreConfig`).

---

## Objectives & Success Criteria

- Define `IProvenanceStore` interface with `Append`, `QueryByResource`, `QueryByAgent`, `QueryByTimeRange`, and `IDisposable`
- Implement `MailboxProcessorProvenanceStore` with MailboxProcessor-backed serialized writes and concurrent-safe reads
- Implement configurable retention policy (max record count, oldest-first eviction in batches)
- Implement `IDisposable` with proper drain of pending appends
- Sub-millisecond append and query at 10,000 records (FR-008, SC-003)
- Post-disposal messages are no-ops (logged, not thrown)
- All store tests pass

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/006-prov-o-state-change-tracking/research.md` -- Decision 3 (MailboxProcessor store design), Decision 6 (retention policy)
- `kitty-specs/006-prov-o-state-change-tracking/data-model.md` -- IProvenanceStore interface, MailboxProcessorProvenanceStore, StoreState, StoreMessage

**Key constraints**:
- `Append` uses `MailboxProcessor.Post` (fire-and-forget) for minimal request-path overhead
- Query methods use `PostAndAsyncReply` returning `Task<ProvenanceRecord list>`
- `ResizeArray` for records (not immutable list) -- performance requirement at 10K records
- `Dictionary<string, ResizeArray<int>>` indexes for resource URI and agent ID
- Eviction batch size default: 100. Eviction rebuilds indexes (simple, not index-aware).
- `ILogger` for all observability (store lifecycle, eviction events, disposal, post-disposal access)
- Constitution VI: `IDisposable` with proper cleanup
- Constitution VII: no silent exception swallowing

---

## Subtasks & Detailed Guidance

### Subtask T007 -- Create `Store.fs` with `IProvenanceStore` interface

**Purpose**: Define the persistence abstraction that all store implementations must satisfy.

**Steps**:
1. Create `src/Frank.Provenance/Store.fs`
2. Define the interface:

```fsharp
namespace Frank.Provenance

open System
open System.Threading.Tasks

/// Persistence abstraction for provenance records. Pluggable via DI.
type IProvenanceStore =
    inherit IDisposable

    /// Fire-and-forget append of a provenance record.
    /// The default MailboxProcessor store uses Post (no blocking).
    abstract Append: record: ProvenanceRecord -> unit

    /// Query all provenance records for a resource URI.
    abstract QueryByResource: resourceUri: string -> Task<ProvenanceRecord list>

    /// Query all provenance records for an agent ID.
    abstract QueryByAgent: agentId: string -> Task<ProvenanceRecord list>

    /// Query all provenance records within a time range.
    abstract QueryByTimeRange: start: DateTimeOffset * end_: DateTimeOffset -> Task<ProvenanceRecord list>
```

3. Add `Store.fs` to `Frank.Provenance.fsproj` after `Types.fs`:
   ```xml
   <Compile Include="Store.fs" />
   ```

**Files**: `src/Frank.Provenance/Store.fs`
**Notes**:
- `Append` returns `unit` (fire-and-forget). External store implementations may internally buffer.
- Query methods return `Task` for async compatibility with external stores (not `Async` -- interop with C# DI consumers).
- `IDisposable` inheritance is mandatory per Constitution VI.
- The interface is in its own file (not in Types.fs) because it depends on types but will be referenced by both the store implementation and the middleware.

### Subtask T008 -- Create `MailboxProcessorStore.fs`

**Purpose**: Implement the default in-memory store using F#'s MailboxProcessor for lock-free serialized writes.

**Steps**:
1. Create `src/Frank.Provenance/MailboxProcessorStore.fs`
2. Define internal message type and state:

```fsharp
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

type private StoreState = {
    Records: ResizeArray<ProvenanceRecord>
    ResourceIndex: Dictionary<string, ResizeArray<int>>
    AgentIndex: Dictionary<string, ResizeArray<int>>
}
```

3. Implement `MailboxProcessorProvenanceStore`:
   - Constructor takes `ProvenanceStoreConfig` and `ILogger`
   - Start MailboxProcessor in constructor with recursive message loop
   - `Append`: Post message, update Records + indexes, check retention
   - `QueryByResource`: PostAndAsyncReply, look up in ResourceIndex, return records at those indices
   - `QueryByAgent`: PostAndAsyncReply, look up in AgentIndex, return records at those indices
   - `QueryByTimeRange`: PostAndAsyncReply, linear scan filtering by `RecordedAt`
   - Track disposed state with mutable boolean flag

**Key implementation detail for indexing on Append**:
```fsharp
let addToIndex (index: Dictionary<string, ResizeArray<int>>) (key: string) (idx: int) =
    match index.TryGetValue(key) with
    | true, indices -> indices.Add(idx)
    | false, _ ->
        let indices = ResizeArray()
        indices.Add(idx)
        index.[key] <- indices
```

**Key implementation detail for query**:
```fsharp
// QueryByResource handler
let indices =
    match state.ResourceIndex.TryGetValue(uri) with
    | true, idxs -> idxs |> Seq.map (fun i -> state.Records.[i]) |> Seq.toList
    | false, _ -> []
replyChannel.Reply(indices)
```

4. Add `MailboxProcessorStore.fs` to `.fsproj` after `Store.fs`

**Files**: `src/Frank.Provenance/MailboxProcessorStore.fs`
**Notes**:
- The MailboxProcessor loop is `async { ... }` with `let! msg = inbox.Receive()` and pattern matching on `StoreMessage`.
- After disposal, all messages should be handled gracefully (log warning, reply with empty list for queries, ignore appends).
- `Task.FromResult` to convert synchronous results to Task for the interface.
- Use `Async.StartAsTask` for `PostAndAsyncReply` to bridge to Task.

### Subtask T009 -- Implement retention policy

**Purpose**: Prevent unbounded memory growth by evicting oldest records when capacity is exceeded.

**Steps**:
1. In the `Append` handler, after adding the record to state:

```fsharp
if state.Records.Count > config.MaxRecords then
    let evictCount = min config.EvictionBatchSize state.Records.Count
    state.Records.RemoveRange(0, evictCount)
    // Rebuild indexes from scratch
    state.ResourceIndex.Clear()
    state.AgentIndex.Clear()
    for i in 0 .. state.Records.Count - 1 do
        let r = state.Records.[i]
        addToIndex state.ResourceIndex r.ResourceUri i
        addToIndex state.AgentIndex r.Agent.Id i
    logger.LogInformation("Provenance store evicted {Count} records, {Remaining} remaining", evictCount, state.Records.Count)
```

2. Eviction happens inside the MailboxProcessor loop (serialized, no concurrent access issues)
3. Batch eviction amortizes O(n) index rebuild cost

**Files**: `src/Frank.Provenance/MailboxProcessorStore.fs` (same file as T008)
**Notes**:
- Eviction batch size default is 100 (from `ProvenanceStoreConfig.defaults`)
- At 10K records with batch 100, eviction happens once every 100 appends after capacity
- Index rebuild is O(n) but infrequent -- acceptable for in-memory store
- Log at Information level (not Debug) since eviction is operationally significant

### Subtask T010 -- Implement IDisposable with drain

**Purpose**: Ensure proper cleanup when the store is disposed (Constitution VI).

**Steps**:
1. Implement disposal via the MailboxProcessor message:

```fsharp
member _.Dispose() =
    if not disposed then
        disposed <- true
        try
            agent.PostAndReply(fun ch -> Dispose ch)
        with
        | :? ObjectDisposedException ->
            logger.LogWarning("Provenance store was already disposed")
```

2. In the `Dispose` message handler:
   - Clear `Records`, `ResourceIndex`, `AgentIndex`
   - Log disposal at Information level
   - Reply to unblock the caller

3. After disposal, subsequent messages:
   - `Append`: log warning, ignore
   - `Query*`: reply with empty list
   - `Dispose`: reply immediately (idempotent)

**Files**: `src/Frank.Provenance/MailboxProcessorStore.fs` (same file as T008)
**Notes**:
- The `disposed` flag is checked in all public interface methods BEFORE posting to the MailboxProcessor
- For `Append`, check `disposed` and return early (log warning) -- do not post
- For queries, check `disposed` and return `Task.FromResult([])` immediately
- This avoids `ObjectDisposedException` from the MailboxProcessor itself after disposal

### Subtask T011 -- Create `StoreTests.fs`

**Purpose**: Comprehensive tests for the MailboxProcessor store covering all operations and edge cases.

**Steps**:
1. Create `test/Frank.Provenance.Tests/StoreTests.fs`
2. Add a test helper to create sample `ProvenanceRecord` instances:

```fsharp
let makeRecord resourceUri agentId stateBefore stateAfter =
    let now = DateTimeOffset.UtcNow
    { Id = Guid.NewGuid().ToString()
      ResourceUri = resourceUri
      Agent = { Id = agentId; AgentType = SoftwareAgent agentId }
      Activity = { Id = Guid.NewGuid().ToString(); HttpMethod = "POST"; ResourceUri = resourceUri
                   EventName = "TestEvent"; PreviousState = stateBefore; NewState = stateAfter
                   StartedAt = now; EndedAt = now.AddMilliseconds(1.) }
      UsedEntity = { Id = Guid.NewGuid().ToString(); ResourceUri = resourceUri
                     StateName = stateBefore; CapturedAt = now }
      GeneratedEntity = { Id = Guid.NewGuid().ToString(); ResourceUri = resourceUri
                          StateName = stateAfter; CapturedAt = now.AddMilliseconds(1.) }
      RecordedAt = now }
```

3. Write tests covering:

**a. Append and QueryByResource**: Append 3 records for resource A, 2 for resource B. Query A returns 3, query B returns 2, query C returns 0.

**b. QueryByAgent**: Append records with different agent IDs. Query returns correct subsets.

**c. QueryByTimeRange**: Append records at different times (use artificial timestamps). Verify range query filters correctly.

**d. Retention policy**: Create store with `MaxRecords = 10; EvictionBatchSize = 5`. Append 15 records. Verify store contains 10 records (5 evicted). Verify oldest 5 are gone.

**e. Disposal drain**: Append records, dispose, verify queries return empty lists. Verify no exceptions on post-disposal append.

**f. Concurrent appends**: Append 1000 records from multiple threads using `Task.WhenAll`. Verify final count is correct (up to retention limit).

**g. Empty store queries**: Query on fresh store returns empty list (not null, not error).

4. Add `StoreTests.fs` to test `.fsproj` before `Program.fs`

**Files**: `test/Frank.Provenance.Tests/StoreTests.fs`
**Validation**: `dotnet test test/Frank.Provenance.Tests/` passes with all store tests green. Pay attention to timing-sensitive tests -- use explicit timestamps rather than `DateTimeOffset.UtcNow` where ordering matters.

---

## Test Strategy

- Run `dotnet build` to verify compilation on all targets
- Run `dotnet test test/Frank.Provenance.Tests/` to verify all store tests pass
- Manually verify no `[<Literal>]` or `mutable` fields leak into the public API beyond `disposed` flag

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| MailboxProcessor disposal race | Check `disposed` flag before posting; handle `ObjectDisposedException` in Dispose method |
| Index corruption under eviction | Eviction happens inside serialized MailboxProcessor loop; no concurrent access possible |
| `ResizeArray.RemoveRange` index shift | After eviction, rebuild indexes from scratch (indices refer to new positions) |
| Sub-millisecond performance at 10K | Use Dictionary indexes for O(1) lookup; only TimeRange requires linear scan |

---

## Review Guidance

- Verify `IProvenanceStore` inherits `IDisposable`
- Verify `Append` uses `Post` (fire-and-forget), not `PostAndAsyncReply`
- Verify all query methods return `Task<ProvenanceRecord list>` (not Async)
- Verify retention eviction is batch-based (not per-append)
- Verify index rebuild happens after eviction
- Verify post-disposal behavior: no exceptions, logged warnings, empty results
- Verify ILogger usage on all significant lifecycle events (creation, eviction, disposal)
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
