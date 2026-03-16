---
work_package_id: "WP04"
title: "SQLite Durable Store Implementation"
phase: "Phase 2 - Parallel Streams"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP02"]
requirement_refs:
  - "FR-007"
  - "FR-008"
  - "FR-009"
  - "FR-010"
subtasks:
  - "T021"
  - "T022"
  - "T023"
  - "T024"
  - "T025"
  - "T026"
  - "T027"
history:
  - timestamp: "2026-03-16T00:05:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP04 -- SQLite Durable Store Implementation

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

Depends on WP02 -- branch from WP02:

```bash
spec-kitty implement WP04 --base WP02
```

---

## Objectives & Success Criteria

Create the `Frank.Statecharts.Sqlite` package with `SqliteStateMachineStore` -- an actor-wrapped SQLite implementation of `IStateMachineStore` supporting durable persistence, lazy rehydration, subscriptions, and proper disposal.

**Success Criteria**:
1. New project `src/Frank.Statecharts.Sqlite/` builds across all targets (net8.0, net9.0, net10.0)
2. `SqliteStateMachineStore` implements `IStateMachineStore<'State, 'Context>` and `IDisposable`
3. All SQLite access is serialized through a `MailboxProcessor` actor
4. Schema is auto-created on first use (no manual migration)
5. State is persisted durably (UPSERT on `SetState`)
6. Lazy rehydration: `GetState` loads from SQLite on cache miss
7. Subscriptions work with same BehaviorSubject semantics as in-memory store
8. Single connection with WAL mode and busy_timeout
9. Project added to `Frank.sln`

## Context & Constraints

- **Spec**: `/kitty-specs/010-statecharts-production-readiness/spec.md` -- User Story 4 (SQLite Durable State Persistence)
- **Plan**: `/kitty-specs/010-statecharts-production-readiness/plan.md` -- Decision D-004
- **Research**: `/kitty-specs/010-statecharts-production-readiness/research.md` -- Decision 4 (Actor-Wrapped Persistence), Decision 6 (JsonSerializerOptions)
- **Data Model**: `/kitty-specs/010-statecharts-production-readiness/data-model.md` -- SqliteStateMachineStore entity, state_machine_instances table
- **Constitution**: Principle III (Library, Not Framework) -- separate package; Principle VI (Disposal Discipline) -- IDisposable; Principle VII (No Silent Exception Swallowing) -- log via ILogger

**Parallel with WP03**: This WP can run in parallel with WP03 since they are independent concerns. WP04 creates a new project; WP03 modifies existing test files.

**Key Architecture**: The SQLite store is architecturally identical to `MailboxProcessorStore` but with SQLite persistence inside the actor loop. Study `src/Frank.Statecharts/Store.fs` as the reference implementation.

## Subtasks & Detailed Guidance

### Subtask T021 -- Create project structure and .fsproj

- **Purpose**: Set up the new `Frank.Statecharts.Sqlite` project with correct multi-targeting and dependencies.
- **Files**: `src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj` (new), `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs` (new)
- **Steps**:
  1. Create directory `src/Frank.Statecharts.Sqlite/`
  2. Create `.fsproj`:
     ```xml
     <Project Sdk="Microsoft.NET.Sdk">

       <PropertyGroup>
         <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
         <PackageTags>statecharts;state-machine;sqlite;persistence</PackageTags>
         <Description>SQLite durable persistence for Frank.Statecharts state machines</Description>
       </PropertyGroup>

       <ItemGroup>
         <Compile Include="SqliteStateMachineStore.fs" />
       </ItemGroup>

       <ItemGroup>
         <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.*" Condition="'$(TargetFramework)' == 'net8.0' OR '$(TargetFramework)' == 'net9.0'" />
         <PackageReference Include="Microsoft.Data.Sqlite" Version="10.0.*" Condition="'$(TargetFramework)' == 'net10.0'" />
       </ItemGroup>

       <ItemGroup>
         <ProjectReference Include="../Frank.Statecharts/Frank.Statecharts.fsproj" />
       </ItemGroup>

     </Project>
     ```

  3. **Note on `Microsoft.Data.Sqlite` versioning**: The package version should match the target framework. For net8.0/net9.0, use 9.0.x (latest stable for those TFMs). For net10.0, use 10.0.x. If 10.0.x is not yet available on NuGet, use the latest preview or the same 9.0.x version. Check `dotnet list package` output from the existing solution to see what version pattern the project uses.

  4. The project inherits shared packaging properties from `src/Directory.Build.props` (VersionPrefix, etc.).

### Subtask T022 -- Add project to Frank.sln and set up references

- **Purpose**: Integrate the new project into the solution so it builds with the rest of the codebase.
- **Files**: `Frank.sln`
- **Steps**:
  1. Add the project to the solution:
     ```bash
     dotnet sln Frank.sln add src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj
     ```
  2. Verify the project builds:
     ```bash
     dotnet build src/Frank.Statecharts.Sqlite/
     ```
  3. The project references `Frank.Statecharts` (project reference, not NuGet). This gives access to `IStateMachineStore`, `StoreMessage`, etc.

- **Notes**: The `Frank.Statecharts` project has `InternalsVisibleTo` for the test project. The SQLite store should NOT need internal access -- it only uses the public `IStateMachineStore` interface. If the `StoreMessage` DU is private (it is), the SQLite store defines its own identical DU (acceptable per Constitution VIII -- it is a 5-line type definition).

### Subtask T023 -- Implement SqliteStateMachineStore actor core

- **Purpose**: Build the MailboxProcessor-based actor that serializes all operations.
- **Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
- **Steps**:
  1. Define a private `StoreMessage` DU (identical pattern to `MailboxProcessorStore`):
     ```fsharp
     type private StoreMessage<'State, 'Context when 'State: equality> =
         | GetState of instanceId: string * replyChannel: AsyncReplyChannel<('State * 'Context) option>
         | SetState of instanceId: string * state: 'State * context: 'Context * replyChannel: AsyncReplyChannel<unit>
         | Subscribe of instanceId: string * observer: IObserver<'State * 'Context> * replyChannel: AsyncReplyChannel<IDisposable>
         | Unsubscribe of instanceId: string * observer: IObserver<'State * 'Context>
         | Stop of replyChannel: AsyncReplyChannel<unit>
     ```

  2. Define the store class:
     ```fsharp
     type SqliteStateMachineStore<'State, 'Context when 'State: equality>
         (connectionString: string, logger: ILogger, ?jsonOptions: JsonSerializerOptions) =

         let options = defaultArg jsonOptions JsonSerializerOptions.Default

         // ... connection setup (T024)
         // ... agent loop (T024, T025, T026)

         interface IStateMachineStore<'State, 'Context> with
             member _.GetState(instanceId) =
                 agent.PostAndAsyncReply(fun reply -> GetState(instanceId, reply))
                 |> Async.StartAsTask
             member _.SetState instanceId state context =
                 agent.PostAndAsyncReply(fun reply -> SetState(instanceId, state, context, reply))
                 |> Async.StartAsTask
             member _.Subscribe instanceId observer =
                 agent.PostAndAsyncReply(fun reply -> Subscribe(instanceId, observer, reply))
                 |> Async.RunSynchronously

         interface IDisposable with
             // ... (T027)
     ```

  3. The actor maintains mutable state inside the loop:
     ```fsharp
     let mutable instances = Map.empty<string, 'State * 'Context>
     let mutable subscribers = Map.empty<string, IObserver<'State * 'Context> list>
     ```

### Subtask T024 -- Implement SQLite persistence

- **Purpose**: Set up the SQLite connection, auto-create schema, and implement UPSERT/SELECT operations inside the actor loop.
- **Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
- **Steps**:
  1. **Connection setup** (in the constructor, before the agent):
     ```fsharp
     let connection = new SqliteConnection(connectionString)
     do connection.Open()

     // Enable WAL mode for better external read compatibility
     do
         use cmd = connection.CreateCommand()
         cmd.CommandText <- "PRAGMA journal_mode=WAL;"
         cmd.ExecuteNonQuery() |> ignore

     // Set busy timeout for external lock contention
     do
         use cmd = connection.CreateCommand()
         cmd.CommandText <- "PRAGMA busy_timeout=5000;"
         cmd.ExecuteNonQuery() |> ignore

     // Auto-create schema (FR-008)
     do
         use cmd = connection.CreateCommand()
         cmd.CommandText <- """
             CREATE TABLE IF NOT EXISTS state_machine_instances (
                 instance_id  TEXT NOT NULL,
                 state_type   TEXT NOT NULL,
                 state_json   TEXT NOT NULL,
                 context_json TEXT NOT NULL,
                 updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
                 PRIMARY KEY (instance_id, state_type)
             );"""
         cmd.ExecuteNonQuery() |> ignore
     ```

  2. **State type key** (for the composite primary key):
     ```fsharp
     let stateTypeName = typeof<'State>.FullName
     ```

  3. **UPSERT on SetState** (inside actor loop):
     ```fsharp
     | SetState(id, state, ctx, reply) ->
         // Update in-memory cache
         instances <- Map.add id (state, ctx) instances

         // Persist to SQLite
         try
             use cmd = connection.CreateCommand()
             cmd.CommandText <- """
                 INSERT INTO state_machine_instances (instance_id, state_type, state_json, context_json, updated_at)
                 VALUES (@id, @type, @stateJson, @ctxJson, datetime('now'))
                 ON CONFLICT (instance_id, state_type)
                 DO UPDATE SET
                     state_json = excluded.state_json,
                     context_json = excluded.context_json,
                     updated_at = excluded.updated_at;"""
             cmd.Parameters.AddWithValue("@id", id) |> ignore
             cmd.Parameters.AddWithValue("@type", stateTypeName) |> ignore
             cmd.Parameters.AddWithValue("@stateJson", JsonSerializer.Serialize(state, options)) |> ignore
             cmd.Parameters.AddWithValue("@ctxJson", JsonSerializer.Serialize(ctx, options)) |> ignore
             cmd.ExecuteNonQuery() |> ignore
         with ex ->
             logger.LogError(ex, "Failed to persist state for instance {InstanceId}", id)
             reraise ()

         // Notify subscribers (same pattern as MailboxProcessorStore)
         // ... (T026)

         reply.Reply(())
         return! loop ()
     ```

  4. **SELECT on cache miss** (inside actor loop, `GetState` handler):
     ```fsharp
     | GetState(id, reply) ->
         match Map.tryFind id instances with
         | Some state ->
             reply.Reply(Some state)
         | None ->
             // Lazy rehydration from SQLite (T025)
             // ...
         return! loop ()
     ```

- **Notes**:
  - SQLite operations are synchronous (`ExecuteNonQuery`, not `ExecuteNonQueryAsync`) because Microsoft.Data.Sqlite async is synchronous under the hood. This is correct because the operations run inside the actor loop (single-threaded).
  - The `connection` is a single instance used for the lifetime of the store. Safe because the actor serializes all access.
  - `SqliteException` should be caught and logged via `ILogger` (Constitution Principle VII).

### Subtask T025 -- Implement lazy rehydration

- **Purpose**: Load state from SQLite into in-memory cache on first access (cache miss in `GetState`).
- **Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
- **Steps**:
  1. In the `GetState` handler, when the in-memory cache has no entry:
     ```fsharp
     | GetState(id, reply) ->
         match Map.tryFind id instances with
         | Some state ->
             reply.Reply(Some state)
         | None ->
             // Cache miss -- load from SQLite
             try
                 use cmd = connection.CreateCommand()
                 cmd.CommandText <- """
                     SELECT state_json, context_json
                     FROM state_machine_instances
                     WHERE instance_id = @id AND state_type = @type;"""
                 cmd.Parameters.AddWithValue("@id", id) |> ignore
                 cmd.Parameters.AddWithValue("@type", stateTypeName) |> ignore
                 use reader = cmd.ExecuteReader()
                 if reader.Read() then
                     let stateJson = reader.GetString(0)
                     let ctxJson = reader.GetString(1)
                     let state = JsonSerializer.Deserialize<'State>(stateJson, options)
                     let ctx = JsonSerializer.Deserialize<'Context>(ctxJson, options)
                     instances <- Map.add id (state, ctx) instances
                     reply.Reply(Some(state, ctx))
                 else
                     reply.Reply(None)
             with ex ->
                 logger.LogError(ex, "Failed to load state for instance {InstanceId}", id)
                 reply.Reply(None)
         return! loop ()
     ```

  2. Once loaded, the state stays in the in-memory cache. Subsequent `GetState` calls for the same instance are cache hits.
  3. No eager loading on startup -- only accessed instances are loaded.

- **Notes**: Cache eviction is documented as a known limitation. All accessed instances remain in memory for the lifetime of the store. LRU eviction is deferred to a future version.

### Subtask T026 -- Implement Subscribe/observable pattern

- **Purpose**: Support the same observable subscription semantics as the in-memory store.
- **Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
- **Steps**:
  1. **Subscribe handler** (inside actor loop):
     ```fsharp
     | Subscribe(id, observer, reply) ->
         let current = Map.tryFind id subscribers |> Option.defaultValue []
         subscribers <- Map.add id (observer :: current) subscribers

         // BehaviorSubject: emit current state immediately
         match Map.tryFind id instances with
         | Some state ->
             try observer.OnNext(state)
             with ex ->
                 logger.LogWarning(ex, "Store subscriber OnNext threw during initial emit for instance {InstanceId}", id)
         | None ->
             // If not in cache, try loading from SQLite for initial emit
             try
                 use cmd = connection.CreateCommand()
                 cmd.CommandText <- "SELECT state_json, context_json FROM state_machine_instances WHERE instance_id = @id AND state_type = @type;"
                 cmd.Parameters.AddWithValue("@id", id) |> ignore
                 cmd.Parameters.AddWithValue("@type", stateTypeName) |> ignore
                 use reader = cmd.ExecuteReader()
                 if reader.Read() then
                     let stateJson = reader.GetString(0)
                     let ctxJson = reader.GetString(1)
                     let state = JsonSerializer.Deserialize<'State>(stateJson, options)
                     let ctx = JsonSerializer.Deserialize<'Context>(ctxJson, options)
                     instances <- Map.add id (state, ctx) instances
                     try observer.OnNext((state, ctx))
                     with ex ->
                         logger.LogWarning(ex, "Store subscriber OnNext threw during initial emit for instance {InstanceId}", id)
             with ex ->
                 logger.LogWarning(ex, "Failed to load state for subscriber initial emit for instance {InstanceId}", id)

         let disposable =
             { new IDisposable with
                 member _.Dispose() = agent.Post(Unsubscribe(id, observer)) }
         reply.Reply(disposable)
         return! loop ()
     ```

  2. **Unsubscribe handler** (same pattern as `MailboxProcessorStore`):
     ```fsharp
     | Unsubscribe(id, observer) ->
         match Map.tryFind id subscribers with
         | Some observers ->
             let filtered = observers |> List.filter (fun o -> not (obj.ReferenceEquals(o, observer)))
             subscribers <- Map.add id filtered subscribers
         | None -> ()
         return! loop ()
     ```

  3. **Notify subscribers in SetState** (add after the SQLite UPSERT):
     ```fsharp
     // After persisting to SQLite:
     match Map.tryFind id subscribers with
     | Some observers ->
         for obs in observers do
             try obs.OnNext(state, ctx)
             with ex ->
                 logger.LogWarning(ex, "Store subscriber OnNext threw for instance {InstanceId}", id)
     | None -> ()
     ```

### Subtask T027 -- Implement IDisposable for cleanup

- **Purpose**: Clean up the SQLite connection and actor on disposal.
- **Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
- **Steps**:
  1. Add disposal tracking and `IDisposable` implementation:
     ```fsharp
     let mutable disposed = false

     let ensureNotDisposed () =
         if disposed then
             raise (ObjectDisposedException(nameof SqliteStateMachineStore))

     // In each IStateMachineStore member, call ensureNotDisposed() first

     interface IDisposable with
         member _.Dispose() =
             if not disposed then
                 disposed <- true
                 // Stop the actor (notifies subscribers with OnCompleted)
                 agent.PostAndReply(Stop)
                 // Close the SQLite connection
                 connection.Close()
                 connection.Dispose()
     ```

  2. The `Stop` message handler (inside actor loop):
     ```fsharp
     | Stop reply ->
         for KeyValue(id, observers) in subscribers do
             for obs in observers do
                 try obs.OnCompleted()
                 with ex ->
                     logger.LogWarning(ex, "Store subscriber OnCompleted threw for instance {InstanceId}", id)
         subscribers <- Map.empty
         instances <- Map.empty
         reply.Reply(())
     ```

  3. Add `ensureNotDisposed()` call at the start of each `IStateMachineStore` member (before posting to agent).

## Risks & Mitigations

1. **F# DU serialization**: `System.Text.Json` does not handle F# DUs by default. Users must configure `JsonSerializerOptions` with `FSharp.SystemTextJson` or custom converters. Document this in the class XML docs. Tests (WP05) will include `FSharp.SystemTextJson` as a test dependency.

2. **SQLite file locking**: If another process holds the database file, `PRAGMA busy_timeout=5000` causes SQLite to retry for 5 seconds. If it still fails, `SqliteException` is thrown. The actor catches this and logs via `ILogger`. The store does NOT hang indefinitely.

3. **Cross-process access**: Documented as unsupported. The SQLite store is designed for single-process use. The actor model assumes it is the sole accessor of the database. For multi-process deployments, use PostgreSQL or similar.

4. **Memory growth**: Lazy rehydration caches state in memory indefinitely. Document as a known limitation. LRU eviction deferred to future version.

5. **Namespace**: Use `namespace Frank.Statecharts.Sqlite` for the new file. The DI extension method goes in the same file with an `[<AutoOpen>]` module.

## Review Guidance

- Verify the actor pattern matches `MailboxProcessorStore` structurally (same message DU, same loop pattern)
- Verify all SQLite operations are synchronous (not async) inside the actor loop
- Verify connection is opened once and kept alive for the store lifetime
- Verify WAL mode and busy_timeout PRAGMAs are set
- Verify schema auto-creation uses `CREATE TABLE IF NOT EXISTS`
- Verify lazy rehydration only loads on cache miss
- Verify subscriber notifications happen AFTER SQLite persistence
- Verify `IDisposable` closes the connection and stops the actor
- Run `dotnet build src/Frank.Statecharts.Sqlite/` to verify it compiles

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T00:05:00Z -- system -- lane=planned -- Prompt generated via /spec-kitty.tasks

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP04 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
