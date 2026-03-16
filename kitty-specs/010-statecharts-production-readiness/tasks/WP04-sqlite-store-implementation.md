---
work_package_id: "WP04"
title: "SQLite Durable Store Implementation"
phase: "Phase 2 - Wave 1"
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
  - timestamp: "2026-03-15T23:59:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP04 -- SQLite Durable Store Implementation

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

This WP depends on WP02:
```bash
spec-kitty implement WP04 --base WP02
```

---

## Objectives & Success Criteria

Create the `Frank.Statecharts.Sqlite` package with a durable `IStateMachineStore` implementation backed by SQLite, wrapped in an actor for serialized access:

1. New project `src/Frank.Statecharts.Sqlite/` with multi-target `net8.0;net9.0;net10.0`
2. `SqliteStateMachineStore` implements `IStateMachineStore<'State, 'Context>` with actor-wrapped SQLite persistence
3. Schema auto-creation on first use (no manual migration)
4. Lazy rehydration (load from SQLite on first `GetState` cache miss)
5. Subscribe/observable pattern matching in-memory store semantics
6. `IDisposable` for proper connection and actor cleanup
7. Configurable JSON serialization via `JsonSerializerOptions`
8. Project builds across all targets

**Success gate**: `dotnet build src/Frank.Statecharts.Sqlite/` succeeds across all targets. The store can be instantiated and basic operations (set/get) work.

## Context & Constraints

- **Spec**: `kitty-specs/010-statecharts-production-readiness/spec.md` -- User Story 4 (SQLite Durable State Persistence, P2)
- **Plan**: `kitty-specs/010-statecharts-production-readiness/plan.md` -- Decision D-004 (SQLite store as actor-wrapped persistence)
- **Research**: `kitty-specs/010-statecharts-production-readiness/research.md` -- Decision 4 (full architecture), Decision 6 (serialization)
- **Data Model**: `kitty-specs/010-statecharts-production-readiness/data-model.md` -- `SqliteStateMachineStore` entity, SQLite schema
- **Constitution**: Principle III (Library, Not Framework) -- SQLite is a separate optional package
- **Constitution**: Principle VI (Resource Disposal Discipline) -- `IDisposable`, `use` semantics
- **Constitution**: Principle VII (No Silent Exception Swallowing) -- `ILogger` for errors
- **Constraint**: Single `SqliteConnection` opened once, kept alive for store lifetime (safe because actor is single-threaded)
- **Constraint**: All SQLite operations are synchronous inside the actor (Microsoft.Data.Sqlite async is synchronous under the hood)
- **Constraint**: Composite primary key `(instance_id, state_type)` -- multiple state machine types can share one database
- **Constraint**: JSON serialization of `'State` and `'Context` via `System.Text.Json` with configurable `JsonSerializerOptions`

### Key Files
- `src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj` -- NEW project file
- `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs` -- NEW implementation
- `src/Frank.Statecharts/Store.fs` -- reference for `MailboxProcessorStore` pattern to follow
- `Frank.sln` -- add new project

### Reference Implementation
The existing `MailboxProcessorStore` in `src/Frank.Statecharts/Store.fs` is the reference pattern. The SQLite store mirrors its structure exactly, adding SQLite persistence inside the actor loop.

## Subtasks & Detailed Guidance

### Subtask T021 -- Create `Frank.Statecharts.Sqlite` project structure

**Purpose**: Set up the new project with correct multi-targeting, dependencies, and packaging metadata.

**Steps**:

1. Create directory: `src/Frank.Statecharts.Sqlite/`

2. Create `src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

     <PropertyGroup>
       <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
       <PackageTags>statecharts;state-machine;sqlite;persistence</PackageTags>
       <Description>SQLite-backed durable state persistence for Frank.Statecharts</Description>
     </PropertyGroup>

     <ItemGroup>
       <Compile Include="SqliteStateMachineStore.fs" />
     </ItemGroup>

     <ItemGroup>
       <FrameworkReference Include="Microsoft.AspNetCore.App" />
     </ItemGroup>

     <ItemGroup>
       <ProjectReference Include="../Frank.Statecharts/Frank.Statecharts.fsproj" />
     </ItemGroup>

     <ItemGroup>
       <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.*" />
     </ItemGroup>

   </Project>
   ```

3. **NuGet version note**: Use `Microsoft.Data.Sqlite` version `9.0.*` which supports all three target frameworks. Check the latest available version and pin appropriately. The `9.0.x` line is the latest stable that works across net8.0/net9.0/net10.0.

4. The `FrameworkReference Include="Microsoft.AspNetCore.App"` is needed for `ILogger` and `IServiceCollection` (or use the appropriate `Microsoft.Extensions.*` NuGet packages instead if preferred -- check which approach the core `Frank.Statecharts.fsproj` uses).

**Files**: `src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj`
**Parallel?**: No -- project must exist before other subtasks
**Notes**: The project inherits packaging metadata from `src/Directory.Build.props` (VersionPrefix, Authors, Copyright, etc.).

### Subtask T022 -- Add project to solution and set up references

**Purpose**: Integrate the new project into the build system.

**Steps**:

1. Add the project to `Frank.sln`:
   ```bash
   dotnet sln Frank.sln add src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj
   ```

2. Verify the project builds:
   ```bash
   dotnet build src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj
   ```

3. Verify it builds for all targets:
   ```bash
   dotnet build src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj -f net8.0
   dotnet build src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj -f net9.0
   dotnet build src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj -f net10.0
   ```

**Files**: `Frank.sln`, `src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj`
**Parallel?**: No -- depends on T021
**Notes**: After adding to the solution, `dotnet build` from the repo root should include this project.

### Subtask T023 -- Implement `SqliteStateMachineStore` actor core

**Purpose**: Create the actor-based store with `MailboxProcessor`, `StoreMessage` DU, in-memory cache, and subscriber list -- mirroring the `MailboxProcessorStore` pattern exactly.

**Steps**:

1. Create `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`.

2. Define the `StoreMessage` DU (private, matching the pattern in `Store.fs`):
   ```fsharp
   namespace Frank.Statecharts.Sqlite

   open System
   open System.Text.Json
   open System.Threading.Tasks
   open Microsoft.Data.Sqlite
   open Microsoft.Extensions.DependencyInjection
   open Microsoft.Extensions.Logging
   open Frank.Statecharts

   type private StoreMessage<'State, 'Context when 'State: equality> =
       | GetState of instanceId: string * replyChannel: AsyncReplyChannel<('State * 'Context) option>
       | SetState of instanceId: string * state: 'State * context: 'Context * replyChannel: AsyncReplyChannel<unit>
       | Subscribe of
           instanceId: string *
           observer: IObserver<'State * 'Context> *
           replyChannel: AsyncReplyChannel<IDisposable>
       | Unsubscribe of instanceId: string * observer: IObserver<'State * 'Context>
       | Stop of replyChannel: AsyncReplyChannel<unit>
   ```

3. Define the store class:
   ```fsharp
   type SqliteStateMachineStore<'State, 'Context when 'State: equality>
       (connectionString: string, logger: ILogger, ?jsonOptions: JsonSerializerOptions) =

       let options = defaultArg jsonOptions JsonSerializerOptions.Default
       let stateType = typeof<'State>.FullName
       let mutable disposed = false
   ```

4. Inside the class, create the `MailboxProcessor` agent with:
   - `instances: Map<string, 'State * 'Context>` (in-memory cache)
   - `subscribers: Map<string, IObserver<'State * 'Context> list>` (subscriber list)
   - Message processing loop matching the `MailboxProcessorStore` pattern

5. For the initial skeleton, implement `GetState` as cache-only (SQLite loading comes in T025) and `SetState` as cache-only (SQLite persistence comes in T024). This lets T023 focus on the actor structure.

**Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
**Parallel?**: No -- foundation for T024-T027
**Notes**: The `StoreMessage` DU is intentionally duplicated from `Store.fs` (it's `private` in both projects). Per the plan: "keeping them separate avoids a cross-project internal dependency."

### Subtask T024 -- Implement SQLite persistence

**Purpose**: Add SQLite schema creation, connection management, and persistence operations inside the actor loop.

**Steps**:

1. **Connection setup** (in the class constructor, before the agent):
   ```fsharp
   let connection = new SqliteConnection(connectionString)
   do
       connection.Open()
       // Enable WAL mode for external read compatibility
       use pragmaCmd = connection.CreateCommand()
       pragmaCmd.CommandText <- "PRAGMA journal_mode=WAL;"
       pragmaCmd.ExecuteNonQuery() |> ignore
       // Set busy timeout for external lock contention
       pragmaCmd.CommandText <- "PRAGMA busy_timeout=5000;"
       pragmaCmd.ExecuteNonQuery() |> ignore
   ```

2. **Schema auto-creation** (FR-008):
   ```fsharp
   do
       use schemaCmd = connection.CreateCommand()
       schemaCmd.CommandText <- """
           CREATE TABLE IF NOT EXISTS state_machine_instances (
               instance_id  TEXT NOT NULL,
               state_type   TEXT NOT NULL,
               state_json   TEXT NOT NULL,
               context_json TEXT NOT NULL,
               updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
               PRIMARY KEY (instance_id, state_type)
           );"""
       schemaCmd.ExecuteNonQuery() |> ignore
   ```

3. **Helper functions** (inside the class, before the agent):
   ```fsharp
   let loadFromSqlite (instanceId: string) : ('State * 'Context) option =
       use cmd = connection.CreateCommand()
       cmd.CommandText <- """
           SELECT state_json, context_json
           FROM state_machine_instances
           WHERE instance_id = @instanceId AND state_type = @stateType;"""
       cmd.Parameters.AddWithValue("@instanceId", instanceId) |> ignore
       cmd.Parameters.AddWithValue("@stateType", stateType) |> ignore
       use reader = cmd.ExecuteReader()
       if reader.Read() then
           let stateJson = reader.GetString(0)
           let contextJson = reader.GetString(1)
           let state = JsonSerializer.Deserialize<'State>(stateJson, options)
           let context = JsonSerializer.Deserialize<'Context>(contextJson, options)
           Some(state, context)
       else
           None

   let persistToSqlite (instanceId: string) (state: 'State) (context: 'Context) : unit =
       use cmd = connection.CreateCommand()
       cmd.CommandText <- """
           INSERT INTO state_machine_instances (instance_id, state_type, state_json, context_json, updated_at)
           VALUES (@instanceId, @stateType, @stateJson, @contextJson, datetime('now'))
           ON CONFLICT (instance_id, state_type)
           DO UPDATE SET
               state_json = excluded.state_json,
               context_json = excluded.context_json,
               updated_at = excluded.updated_at;"""
       cmd.Parameters.AddWithValue("@instanceId", instanceId) |> ignore
       cmd.Parameters.AddWithValue("@stateType", stateType) |> ignore
       cmd.Parameters.AddWithValue("@stateJson", JsonSerializer.Serialize(state, options)) |> ignore
       cmd.Parameters.AddWithValue("@contextJson", JsonSerializer.Serialize(context, options)) |> ignore
       cmd.ExecuteNonQuery() |> ignore
   ```

4. **Wire into actor's SetState handler**:
   ```fsharp
   | SetState(id, state, ctx, reply) ->
       instances <- Map.add id (state, ctx) instances
       try
           persistToSqlite id state ctx
       with ex ->
           logger.LogError(ex, "Failed to persist state for instance {InstanceId}", id)
           reraise ()
       // Notify subscribers (same as MailboxProcessorStore)
       ...
       reply.Reply(())
   ```

5. **Error handling**: Catch `SqliteException` from `persistToSqlite` and log via `ILogger`. Re-raise so the caller's `Task` faults (don't silently swallow per Constitution VII).

**Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
**Parallel?**: No -- depends on T023
**Notes**: All SQLite operations are synchronous inside the actor. This is correct per research.md: "Microsoft.Data.Sqlite's async methods are actually synchronous under the hood."

### Subtask T025 -- Implement lazy rehydration

**Purpose**: On `GetState`, check the in-memory cache first. On cache miss, load from SQLite, cache the result, and return.

**Steps**:

1. Update the `GetState` handler in the actor loop:
   ```fsharp
   | GetState(id, reply) ->
       match Map.tryFind id instances with
       | Some state ->
           // Cache hit
           reply.Reply(Some state)
       | None ->
           // Cache miss -- try loading from SQLite
           try
               match loadFromSqlite id with
               | Some(state, ctx) ->
                   instances <- Map.add id (state, ctx) instances
                   reply.Reply(Some(state, ctx))
               | None ->
                   reply.Reply(None)
           with ex ->
               logger.LogError(ex, "Failed to load state from SQLite for instance {InstanceId}", id)
               reply.Reply(None)
       return! loop ()
   ```

2. The key behavior: the first `GetState` for a given instance triggers a SQLite query. Subsequent calls use the in-memory cache. The cache is updated on `SetState` as well, so after a state change, the next `GetState` hits the cache.

3. **Error handling on load**: If SQLite fails to load (e.g., deserialization error), log the error and return `None` (treat as "instance doesn't exist"). This is safer than crashing the actor.

**Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
**Parallel?**: No -- depends on T024
**Notes**: Per research.md: "Only actively-accessed instances consume memory. The SQLite database serves as the authoritative store; the in-memory cache is a performance optimization."

### Subtask T026 -- Implement Subscribe/observable pattern

**Purpose**: Manage subscriptions with the same semantics as `MailboxProcessorStore`.

**Steps**:

1. The Subscribe, Unsubscribe, and subscriber notification logic should mirror `MailboxProcessorStore` exactly.

2. **Subscribe handler**:
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
           // Try loading from SQLite for initial emit
           try
               match loadFromSqlite id with
               | Some(state, ctx) ->
                   instances <- Map.add id (state, ctx) instances
                   try observer.OnNext(state, ctx)
                   with ex ->
                       logger.LogWarning(ex, "Store subscriber OnNext threw during initial emit for instance {InstanceId}", id)
               | None -> ()
           with ex ->
               logger.LogWarning(ex, "Failed to load state from SQLite during subscribe for instance {InstanceId}", id)
       let disposable =
           { new IDisposable with
               member _.Dispose() = inbox.Post(Unsubscribe(id, observer)) }
       reply.Reply(disposable)
       return! loop ()
   ```

3. **Subscriber notification in SetState**: After persisting to SQLite, notify subscribers:
   ```fsharp
   match Map.tryFind id subscribers with
   | Some observers ->
       for obs in observers do
           try obs.OnNext(state, ctx)
           with ex ->
               logger.LogWarning(ex, "Store subscriber OnNext threw for instance {InstanceId}", id)
   | None -> ()
   ```

4. **Stop handler**: Notify all subscribers with `OnCompleted`, clear state, close connection.

**Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
**Parallel?**: No -- depends on T023
**Notes**: The Subscribe handler has one difference from the in-memory store: if the instance is not in cache, it tries loading from SQLite before emitting to the new subscriber. This is needed because the SQLite store may have persisted state that hasn't been accessed yet.

### Subtask T027 -- Implement `IDisposable` for cleanup

**Purpose**: Properly dispose the SQLite connection and stop the actor on cleanup.

**Steps**:

1. Implement the `IStateMachineStore` interface methods (delegating to the agent):
   ```fsharp
   let ensureNotDisposed () =
       if disposed then
           raise (ObjectDisposedException(nameof SqliteStateMachineStore))

   interface IStateMachineStore<'State, 'Context> with
       member _.GetState(instanceId) =
           ensureNotDisposed ()
           agent.PostAndAsyncReply(fun reply -> GetState(instanceId, reply))
           |> Async.StartAsTask

       member _.SetState instanceId state context =
           ensureNotDisposed ()
           agent.PostAndAsyncReply(fun reply -> SetState(instanceId, state, context, reply))
           |> Async.StartAsTask

       member _.Subscribe instanceId observer =
           ensureNotDisposed ()
           agent.PostAndAsyncReply(fun reply -> Subscribe(instanceId, observer, reply))
           |> Async.RunSynchronously
   ```

2. Implement `IDisposable`:
   ```fsharp
   interface IDisposable with
       member _.Dispose() =
           if not disposed then
               disposed <- true
               agent.PostAndReply(Stop)
               connection.Dispose()
   ```

3. The `Stop` message handler in the agent should:
   - Notify all subscribers with `OnCompleted`
   - Clear the `instances` and `subscribers` maps
   - Reply to unblock the `PostAndReply` call
   - The `connection.Dispose()` happens after the agent stops

**Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
**Parallel?**: No -- depends on T023, T024
**Notes**: The disposal order is important: stop the agent first (which flushes pending operations), then dispose the connection. Per Constitution Principle VI.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| F# DU serialization with System.Text.Json fails without `FSharp.SystemTextJson` | Document as soft dependency; accept `JsonSerializerOptions` parameter for user configuration |
| SQLite file locked by external process | `PRAGMA busy_timeout=5000` retries for 5 seconds; `SqliteException` surfaced via logger |
| Connection disposed while agent has pending messages | `ensureNotDisposed()` check on all public methods; `Stop` message flushes before dispose |
| `typeof<'State>.FullName` returns null for generic types | Use `typeof<'State>.AssemblyQualifiedName` as fallback, or format manually |
| Schema creation fails on read-only filesystem | `SqliteException` surfaced via logger; fail fast on construction |

## Review Guidance

- Verify the actor pattern mirrors `MailboxProcessorStore` structurally
- Verify single connection, opened once, WAL mode, busy_timeout
- Verify UPSERT SQL (ON CONFLICT DO UPDATE)
- Verify lazy rehydration (cache miss -> SQLite load -> cache update)
- Verify subscriber notification happens AFTER SQLite persistence
- Verify `IDisposable` stops agent before disposing connection
- Verify `ensureNotDisposed` on all public methods
- Run `dotnet build src/Frank.Statecharts.Sqlite/` across all targets
- Verify no hard dependency on `FSharp.SystemTextJson`

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:00Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP04 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
