---
work_package_id: WP05
title: SQLite Store Tests + DI Registration + Integration
lane: planned
dependencies:
- WP04
- WP01
- WP02
- WP03
subtasks:
- T028
- T029
- T030
- T031
- T032
- T033
- T034
phase: Phase 3 - Integration
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-007
- FR-008
- FR-009
- FR-010
- FR-011
---

# Work Package Prompt: WP05 -- SQLite Store Tests + DI Registration + Integration

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

This WP depends on WP03 and WP04:
```bash
spec-kitty implement WP05 --base WP04
```

Note: WP03 and WP04 are parallel tracks that both depend on WP02. WP05 integrates both. Use `--base WP04` and ensure WP03's changes are also merged into the branch.

---

## Objectives & Success Criteria

Create the SQLite store test project, add comprehensive tests, implement DI registration, and validate end-to-end integration:

1. New test project `test/Frank.Statecharts.Sqlite.Tests/` targets `net10.0`
2. DI registration extension method `AddSqliteStateMachineStore` works as drop-in replacement
3. CRUD tests pass (set, get, overwrite)
4. Rehydration test passes (state survives "restart")
5. Subscription tests pass (notify on state change, BehaviorSubject semantics)
6. Concurrent access serialization test passes
7. Full `dotnet build` and `dotnet test` across all projects and targets with no regressions

**Success gate**: `dotnet build` from repo root succeeds. `dotnet test` from repo root passes all tests in all test projects.

## Context & Constraints

- **Spec**: `kitty-specs/010-statecharts-production-readiness/spec.md` -- User Story 4 acceptance scenarios
- **Plan**: `kitty-specs/010-statecharts-production-readiness/plan.md` -- WP05 integration
- **Research**: `kitty-specs/010-statecharts-production-readiness/research.md` -- Decision 4 (DI registration), Decision 6 (serialization)
- **Constraint**: Test project targets `net10.0` only (matching existing `Frank.Statecharts.Tests`)
- **Constraint**: Uses Expecto test framework (matching existing tests)
- **Constraint**: Include `FSharp.SystemTextJson` as a TEST dependency for DU serialization
- **Constraint**: Rehydration tests must use file-based SQLite (not `:memory:`) to test persistence across store instances
- **Constraint**: This is the final WP -- must validate that ALL changes from WP01-WP04 work together

### Key Files
- `test/Frank.Statecharts.Sqlite.Tests/Frank.Statecharts.Sqlite.Tests.fsproj` -- NEW
- `test/Frank.Statecharts.Sqlite.Tests/SqliteStoreTests.fs` -- NEW
- `test/Frank.Statecharts.Sqlite.Tests/Program.fs` -- NEW (Expecto entry point)
- `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs` -- DI extension added
- `Frank.sln` -- add test project

## Subtasks & Detailed Guidance

### Subtask T028 -- Create `Frank.Statecharts.Sqlite.Tests` project structure

**Purpose**: Set up the test project with correct dependencies and framework targeting.

**Steps**:

1. Create directory: `test/Frank.Statecharts.Sqlite.Tests/`

2. Create `test/Frank.Statecharts.Sqlite.Tests/Frank.Statecharts.Sqlite.Tests.fsproj`:
   ```xml
   <Project Sdk="Microsoft.NET.Sdk">

     <PropertyGroup>
       <OutputType>Exe</OutputType>
       <TargetFramework>net10.0</TargetFramework>
       <IsPackable>false</IsPackable>
       <IsTestProject>true</IsTestProject>
       <GenerateProgramFile>false</GenerateProgramFile>
     </PropertyGroup>

     <ItemGroup>
       <Compile Include="SqliteStoreTests.fs" />
       <Compile Include="Program.fs" />
     </ItemGroup>

     <ItemGroup>
       <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
       <PackageReference Include="Expecto" Version="10.2.3" />
       <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.3" />
       <PackageReference Include="FSharp.SystemTextJson" Version="1.3.13" />
     </ItemGroup>

     <ItemGroup>
       <ProjectReference Include="../../src/Frank.Statecharts.Sqlite/Frank.Statecharts.Sqlite.fsproj" />
     </ItemGroup>

   </Project>
   ```

3. Create `test/Frank.Statecharts.Sqlite.Tests/Program.fs`:
   ```fsharp
   module Program

   open Expecto

   [<EntryPoint>]
   let main argv = runTestsInAssemblyWithCLIArgs [] argv
   ```

4. Add the test project to the solution:
   ```bash
   dotnet sln Frank.sln add test/Frank.Statecharts.Sqlite.Tests/Frank.Statecharts.Sqlite.Tests.fsproj
   ```

5. Check `FSharp.SystemTextJson` version -- use the latest stable version available. The version `1.3.13` is a placeholder.

**Files**: `test/Frank.Statecharts.Sqlite.Tests/Frank.Statecharts.Sqlite.Tests.fsproj`, `test/Frank.Statecharts.Sqlite.Tests/Program.fs`
**Parallel?**: No -- project must exist before tests
**Notes**: The `FSharp.SystemTextJson` package is a TEST dependency only -- it's not referenced by the SQLite store itself (per Decision D-006).

### Subtask T029 -- Implement DI registration extension method

**Purpose**: Provide a convenient `AddSqliteStateMachineStore` extension method for `IServiceCollection`.

**Steps**:

1. In `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs` (at the bottom of the file, after the store class), add:
   ```fsharp
   [<AutoOpen>]
   module SqliteStoreServiceCollectionExtensions =

       open Microsoft.Extensions.DependencyInjection

       type IServiceCollection with

           /// Registers a SQLite-backed IStateMachineStore as a singleton.
           /// Replaces the default in-memory store. No handler changes needed.
           /// <param name="connectionString">SQLite connection string (e.g., "Data Source=statecharts.db")</param>
           /// <param name="jsonOptions">Optional JsonSerializerOptions for state/context serialization.
           /// Configure with FSharp.SystemTextJson's JsonFSharpConverter for F# DU support.</param>
           member services.AddSqliteStateMachineStore<'State, 'Context
               when 'State: equality and 'State: comparison>
               (connectionString: string, ?jsonOptions: JsonSerializerOptions) =
               services.AddSingleton<IStateMachineStore<'State, 'Context>>(fun sp ->
                   let logger =
                       sp.GetRequiredService<ILogger<SqliteStateMachineStore<'State, 'Context>>>()
                   SqliteStateMachineStore<'State, 'Context>(
                       connectionString, logger, ?jsonOptions = jsonOptions)
                   :> IStateMachineStore<'State, 'Context>)
   ```

2. Verify it follows the same pattern as `AddStateMachineStore` in `src/Frank.Statecharts/Store.fs`.

3. The extension method should return `IServiceCollection` for chaining (check the existing pattern).

**Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs`
**Parallel?**: No -- depends on T021 (project exists)
**Notes**: This is a drop-in replacement per FR-011. Users switch from `services.AddStateMachineStore<S,C>()` to `services.AddSqliteStateMachineStore<S,C>("Data Source=app.db")` with no handler changes.

### Subtask T030 -- Add CRUD tests

**Purpose**: Verify basic set/get/overwrite operations work correctly with the SQLite store.

**Steps**:

1. Create `test/Frank.Statecharts.Sqlite.Tests/SqliteStoreTests.fs`:

2. Define test state types:
   ```fsharp
   module SqliteStoreTests

   open System
   open System.IO
   open System.Text.Json
   open System.Text.Json.Serialization
   open Expecto
   open Frank.Statecharts
   open Frank.Statecharts.Sqlite
   open Microsoft.Extensions.Logging
   open Microsoft.Extensions.Logging.Abstractions

   type TestState =
       | Active
       | Paused
       | Completed of result: string

   type TestContext = { Counter: int; LastUpdated: string }
   ```

3. Create a helper to set up the JSON serializer with `FSharp.SystemTextJson`:
   ```fsharp
   let jsonOptions =
       let options = JsonSerializerOptions()
       options.Converters.Add(JsonFSharpConverter())
       options

   let createStore (dbPath: string) =
       let connStr = sprintf "Data Source=%s" dbPath
       let logger = NullLogger<SqliteStateMachineStore<TestState, TestContext>>.Instance :> ILogger
       new SqliteStateMachineStore<TestState, TestContext>(connStr, logger, jsonOptions)
   ```

4. Add tests:

   - **Test: GetState returns None for non-existent instance**:
     ```fsharp
     testAsync "GetState returns None for non-existent instance" {
         use store = createStore ":memory:"
         let iface = store :> IStateMachineStore<TestState, TestContext>
         let! result = iface.GetState "nonexistent" |> Async.AwaitTask
         Expect.isNone result "Should be None"
     }
     ```

   - **Test: SetState then GetState returns the value**:
     ```fsharp
     testAsync "SetState then GetState roundtrips" {
         use store = createStore ":memory:"
         let iface = store :> IStateMachineStore<TestState, TestContext>
         let ctx = { Counter = 1; LastUpdated = "2026-01-01" }
         do! iface.SetState "inst1" Active ctx |> Async.AwaitTask
         let! result = iface.GetState "inst1" |> Async.AwaitTask
         let state, context = Expect.wantSome result "Should have state"
         Expect.equal state Active "State should match"
         Expect.equal context ctx "Context should match"
     }
     ```

   - **Test: SetState overwrites previous state**:
     ```fsharp
     testAsync "SetState overwrites previous state" {
         use store = createStore ":memory:"
         let iface = store :> IStateMachineStore<TestState, TestContext>
         do! iface.SetState "inst1" Active { Counter = 1; LastUpdated = "v1" } |> Async.AwaitTask
         do! iface.SetState "inst1" Paused { Counter = 2; LastUpdated = "v2" } |> Async.AwaitTask
         let! result = iface.GetState "inst1" |> Async.AwaitTask
         let state, ctx = Expect.wantSome result "Should have state"
         Expect.equal state Paused "Should be updated state"
         Expect.equal ctx.Counter 2 "Counter should be updated"
     }
     ```

   - **Test: Parameterized DU state serializes correctly**:
     ```fsharp
     testAsync "parameterized DU state roundtrips" {
         use store = createStore ":memory:"
         let iface = store :> IStateMachineStore<TestState, TestContext>
         do! iface.SetState "inst1" (Completed "success") { Counter = 1; LastUpdated = "now" } |> Async.AwaitTask
         let! result = iface.GetState "inst1" |> Async.AwaitTask
         let state, _ = Expect.wantSome result "Should have state"
         Expect.equal state (Completed "success") "Parameterized state should roundtrip"
     }
     ```

**Files**: `test/Frank.Statecharts.Sqlite.Tests/SqliteStoreTests.fs`
**Parallel?**: Yes -- can proceed alongside T031-T033 after T028
**Notes**: Use `:memory:` for tests that don't need persistence across store instances. Use temp file for rehydration tests.

### Subtask T031 -- Add rehydration test

**Purpose**: Verify that state survives application restart by creating a new store instance against the same database file.

**Steps**:

1. Add test:
   ```fsharp
   testAsync "state survives store restart (rehydration)" {
       let dbPath = Path.Combine(Path.GetTempPath(), sprintf "frank_test_%s.db" (Guid.NewGuid().ToString("N")))
       try
           // First store instance: set state
           do
               use store1 = createStore dbPath
               let iface1 = store1 :> IStateMachineStore<TestState, TestContext>
               do! iface1.SetState "game1" (Completed "winner") { Counter = 42; LastUpdated = "before-restart" }
                   |> Async.AwaitTask

           // Second store instance: verify state persists
           do
               use store2 = createStore dbPath
               let iface2 = store2 :> IStateMachineStore<TestState, TestContext>
               let! result = iface2.GetState "game1" |> Async.AwaitTask
               let state, ctx = Expect.wantSome result "State should survive restart"
               Expect.equal state (Completed "winner") "State should match"
               Expect.equal ctx.Counter 42 "Context should match"
       finally
           // Cleanup
           if File.Exists(dbPath) then File.Delete(dbPath)
           let walPath = dbPath + "-wal"
           let shmPath = dbPath + "-shm"
           if File.Exists(walPath) then File.Delete(walPath)
           if File.Exists(shmPath) then File.Delete(shmPath)
   }
   ```

2. Key details:
   - Use a unique temp file path (GUID-based) to avoid test interference
   - Fully dispose the first store before creating the second (simulates restart)
   - Clean up all SQLite files (`.db`, `-wal`, `-shm`) in the `finally` block

**Files**: `test/Frank.Statecharts.Sqlite.Tests/SqliteStoreTests.fs`
**Parallel?**: Yes -- independent test
**Notes**: This is the core acceptance test for User Story 4. The lazy rehydration means the second store loads from SQLite on first `GetState`.

### Subtask T032 -- Add subscription notification tests

**Purpose**: Verify that the SQLite store's Subscribe/observable pattern works with the same semantics as the in-memory store.

**Steps**:

1. Add tests:

   - **Test: Subscriber receives notification on state change**:
     ```fsharp
     testAsync "subscriber notified on state change" {
         use store = createStore ":memory:"
         let iface = store :> IStateMachineStore<TestState, TestContext>
         let mutable receivedState = None
         let observer =
             { new IObserver<TestState * TestContext> with
                 member _.OnNext(value) = receivedState <- Some value
                 member _.OnError(ex) = ()
                 member _.OnCompleted() = () }
         use _sub = iface.Subscribe "inst1" observer
         do! iface.SetState "inst1" Active { Counter = 1; LastUpdated = "now" } |> Async.AwaitTask
         Expect.isSome receivedState "Should have received notification"
         let state, ctx = receivedState.Value
         Expect.equal state Active "Notification state should match"
     }
     ```

   - **Test: BehaviorSubject semantics (new subscriber receives current state)**:
     ```fsharp
     testAsync "new subscriber receives current state immediately" {
         use store = createStore ":memory:"
         let iface = store :> IStateMachineStore<TestState, TestContext>
         do! iface.SetState "inst1" Active { Counter = 1; LastUpdated = "now" } |> Async.AwaitTask
         let mutable receivedState = None
         let observer =
             { new IObserver<TestState * TestContext> with
                 member _.OnNext(value) = receivedState <- Some value
                 member _.OnError(ex) = ()
                 member _.OnCompleted() = () }
         use _sub = iface.Subscribe "inst1" observer
         Expect.isSome receivedState "Should receive current state on subscribe"
     }
     ```

   - **Test: Unsubscribe stops notifications**:
     ```fsharp
     testAsync "unsubscribe stops notifications" {
         use store = createStore ":memory:"
         let iface = store :> IStateMachineStore<TestState, TestContext>
         let mutable notifyCount = 0
         let observer =
             { new IObserver<TestState * TestContext> with
                 member _.OnNext(_) = notifyCount <- notifyCount + 1
                 member _.OnError(_) = ()
                 member _.OnCompleted() = () }
         let sub = iface.Subscribe "inst1" observer
         do! iface.SetState "inst1" Active { Counter = 1; LastUpdated = "now" } |> Async.AwaitTask
         sub.Dispose()
         do! iface.SetState "inst1" Paused { Counter = 2; LastUpdated = "now" } |> Async.AwaitTask
         Expect.equal notifyCount 1 "Should only receive one notification (before unsubscribe)"
     }
     ```

**Files**: `test/Frank.Statecharts.Sqlite.Tests/SqliteStoreTests.fs`
**Parallel?**: Yes -- independent test category
**Notes**: Subscription tests use `:memory:` database since persistence is not the concern here.

### Subtask T033 -- Add concurrent access serialization test

**Purpose**: Verify that the SQLite store's actor serializes concurrent operations correctly.

**Steps**:

1. Add test:
   ```fsharp
   testAsync "concurrent SetState calls are serialized" {
       use store = createStore ":memory:"
       let iface = store :> IStateMachineStore<TestState, TestContext>
       let barrier = new System.Threading.Barrier(10)

       let tasks =
           [| for i in 0..9 ->
               task {
                   barrier.SignalAndWait()
                   do! iface.SetState "inst1" Active { Counter = i; LastUpdated = sprintf "t%d" i }
               } |]

       do! Task.WhenAll(tasks) |> Async.AwaitTask

       let! result = iface.GetState "inst1" |> Async.AwaitTask
       let _, ctx = Expect.wantSome result "Should have state"
       // Final counter should be one of the values (the last one processed by actor)
       Expect.isTrue (ctx.Counter >= 0 && ctx.Counter <= 9) "Counter should be valid"
   }
   ```

2. This test validates that no exceptions occur under concurrent access and the final state is valid.

**Files**: `test/Frank.Statecharts.Sqlite.Tests/SqliteStoreTests.fs`
**Parallel?**: Yes -- independent test
**Notes**: Same pattern as the concurrency test in WP03, but against the SQLite store.

### Subtask T034 -- Full integration build and test validation

**Purpose**: Final validation that all changes from WP01-WP05 work together across all targets and test projects.

**Steps**:

1. Build everything from repo root:
   ```bash
   dotnet build
   ```

2. Run all tests:
   ```bash
   dotnet test
   ```

3. Verify builds for all individual targets:
   ```bash
   dotnet build src/Frank.Statecharts/ -f net8.0
   dotnet build src/Frank.Statecharts/ -f net9.0
   dotnet build src/Frank.Statecharts/ -f net10.0
   dotnet build src/Frank.Statecharts.Sqlite/ -f net8.0
   dotnet build src/Frank.Statecharts.Sqlite/ -f net9.0
   dotnet build src/Frank.Statecharts.Sqlite/ -f net10.0
   ```

4. Fix any regressions found. Common issues:
   - Missing `open` statements
   - Framework-specific API differences
   - Test assertion failures from WP01/WP02 changes
   - NuGet version conflicts

5. Verify backward compatibility:
   - All pre-existing tests pass (no regressions)
   - Simple DU states work as before
   - `StatechartETagProvider` produces same ETags as before for non-parameterized states

6. Verify no `Unchecked.defaultof` remains in any statecharts source file.

**Files**: All source and test files across the statecharts projects
**Parallel?**: No -- must run after all other subtasks
**Notes**: This is the safety net. Any integration issues surface here. Budget time for debugging.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| In-memory SQLite databases are connection-scoped (lost when connection closes) | Rehydration tests use file-based temp databases |
| `FSharp.SystemTextJson` version incompatibility | Pin version in test project; check NuGet for latest stable |
| Test interference when running in parallel | Each test uses its own store instance; file-based tests use GUID paths |
| Integration regressions from WP01/WP02 changes | T034 runs full build+test suite; fix regressions immediately |
| `NullLogger` may hide errors in tests | Review test output for swallowed exceptions; use capturing logger if debugging |

## Review Guidance

- Verify test project structure matches existing `Frank.Statecharts.Tests` pattern
- Verify DI extension follows same pattern as `AddStateMachineStore`
- Verify rehydration test uses file-based SQLite (not `:memory:`)
- Verify cleanup of temp files in test `finally` blocks
- Verify `FSharp.SystemTextJson` is TEST dependency only
- Run `dotnet build` from repo root -- must succeed
- Run `dotnet test` from repo root -- all tests must pass
- Check for `Unchecked.defaultof` -- must be eliminated

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:00Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP05 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
