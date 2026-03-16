---
work_package_id: WP05
title: SQLite Store Tests + DI Registration + Integration
lane: "done"
dependencies:
- WP04
- WP01
- WP02
- WP03
base_branch: 010-statecharts-production-readiness-WP04
base_commit: 6369cfaa9dca489be0aaf038af9989f1a14722c2
created_at: '2026-03-16T04:25:02.849964+00:00'
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
agent: "claude-opus-reviewer"
shell_pid: "16549"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-16T00:05:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-011
---

# Work Package Prompt: WP05 -- SQLite Store Tests + DI Registration + Integration

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

Depends on WP03 and WP04 -- branch from WP04 (WP03 will be merged via integration):

```bash
spec-kitty implement WP05 --base WP04
```

Note: WP05 depends on both WP03 and WP04. Since both depend on WP02, and WP04 contains the SQLite store that needs testing, branch from WP04. WP03 changes (documentation + tests on existing store) will be merged during integration.

---

## Objectives & Success Criteria

Create the SQLite store test project, add comprehensive tests, implement the DI registration extension method, and validate end-to-end integration across all work packages.

**Success Criteria**:
1. New test project `test/Frank.Statecharts.Sqlite.Tests/` builds and runs
2. CRUD tests pass (set, get, overwrite state)
3. Rehydration test passes (state survives simulated restart)
4. Subscription tests pass (BehaviorSubject semantics)
5. Concurrent access tests pass (actor serializes SQLite operations)
6. DI registration extension method (`AddSqliteStateMachineStore`) works
7. Full `dotnet build` succeeds across all targets (net8.0, net9.0, net10.0)
8. Full `dotnet test` passes across all test projects with no regressions

## Context & Constraints

- **Spec**: `/kitty-specs/010-statecharts-production-readiness/spec.md` -- User Story 4 (acceptance scenarios), Success Criteria SC-005 through SC-008
- **Plan**: `/kitty-specs/010-statecharts-production-readiness/plan.md` -- WP05 section
- **Research**: `/kitty-specs/010-statecharts-production-readiness/research.md` -- Decision 4 (SQLite store), Decision 6 (JsonSerializerOptions)
- **Data Model**: `/kitty-specs/010-statecharts-production-readiness/data-model.md` -- SqliteStateMachineStore, state_machine_instances table

**Test patterns**: Follow the existing test patterns in `test/Frank.Statecharts.Tests/StoreTests.fs` for structure and style.

**SQLite in-memory vs file**: Use in-memory SQLite (`Data Source=:memory:`) for fast tests. Use temp file databases for rehydration tests (in-memory databases are connection-scoped and cannot survive reconnection).

**FSharp.SystemTextJson dependency**: Include as a test dependency for F# DU serialization. The store itself has no hard dependency on it (users configure their own serializer).

## Subtasks & Detailed Guidance

### Subtask T028 -- Create test project structure

- **Purpose**: Set up the test project with correct dependencies and configuration.
- **Files**: `test/Frank.Statecharts.Sqlite.Tests/Frank.Statecharts.Sqlite.Tests.fsproj` (new), `test/Frank.Statecharts.Sqlite.Tests/Program.fs` (new)
- **Steps**:
  1. Create directory `test/Frank.Statecharts.Sqlite.Tests/`
  2. Create `.fsproj`:
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
     Note: Check exact versions of `FSharp.SystemTextJson` available on NuGet. Use the latest stable.

  3. Create `Program.fs`:
     ```fsharp
     module Program

     [<EntryPoint>]
     let main argv =
         Expecto.Tests.runTestsInAssemblyWithCLIArgs [] argv
     ```

  4. Add to solution:
     ```bash
     dotnet sln Frank.sln add test/Frank.Statecharts.Sqlite.Tests/Frank.Statecharts.Sqlite.Tests.fsproj
     ```

### Subtask T029 -- Implement DI registration extension method

- **Purpose**: Provide the `AddSqliteStateMachineStore` extension method for DI registration.
- **Files**: `src/Frank.Statecharts.Sqlite/SqliteStateMachineStore.fs` (add at the bottom of the file)
- **Steps**:
  1. Add an `[<AutoOpen>]` module with the DI extension:
     ```fsharp
     [<AutoOpen>]
     module SqliteStoreServiceCollectionExtensions =

         open Microsoft.Extensions.DependencyInjection

         type IServiceCollection with

             member services.AddSqliteStateMachineStore<'State, 'Context
                 when 'State: equality and 'State: comparison>
                 (connectionString: string, ?jsonOptions: JsonSerializerOptions) =
                 services.AddSingleton<IStateMachineStore<'State, 'Context>>(fun sp ->
                     let logger =
                         sp.GetRequiredService<ILogger<SqliteStateMachineStore<'State, 'Context>>>()
                     new SqliteStateMachineStore<'State, 'Context>(
                         connectionString, logger, ?jsonOptions = jsonOptions)
                     :> IStateMachineStore<'State, 'Context>)
     ```

  2. This replaces the default in-memory store registration. The handler code does not need to change.

### Subtask T030 -- Add CRUD tests

- **Purpose**: Verify basic state persistence operations work correctly with SQLite.
- **Files**: `test/Frank.Statecharts.Sqlite.Tests/SqliteStoreTests.fs` (new)
- **Parallel**: Yes (after T028-T029)
- **Steps**:
  1. Create test helpers:
     ```fsharp
     module SqliteStoreTests

     open System
     open System.Text.Json
     open System.Text.Json.Serialization
     open Expecto
     open Frank.Statecharts
     open Frank.Statecharts.Sqlite
     open Microsoft.Extensions.Logging

     // Test state type
     type TestState = Active | Completed | Error of string

     // JSON options with FSharp.SystemTextJson for DU serialization
     let jsonOptions =
         let opts = JsonSerializerOptions()
         opts.Converters.Add(JsonFSharpConverter())
         opts

     // Create a store with in-memory SQLite for fast tests
     let makeStore () =
         let logger = ... // Same TestLogger pattern as StoreTests.fs
         let store = new SqliteStateMachineStore<TestState, int>(
             "Data Source=:memory:", logger, jsonOptions)
         store, logger
     ```

  2. Add CRUD tests:
     ```fsharp
     [<Tests>]
     let crudTests =
         testList "SqliteStore.CRUD" [
             testAsync "GetState returns None for unknown instance" { ... }
             testAsync "SetState then GetState returns stored value" { ... }
             testAsync "SetState overwrites previous value" { ... }
             testAsync "Multiple instances are independent" { ... }
             testAsync "Parameterized state (Error of string) round-trips correctly" { ... }
         ]
     ```

### Subtask T031 -- Add rehydration test

- **Purpose**: Verify state survives simulated application restart (dispose store, create new one against same DB).
- **Files**: `test/Frank.Statecharts.Sqlite.Tests/SqliteStoreTests.fs`
- **Parallel**: Yes
- **Steps**:
  1. Use a temp file database (not in-memory):
     ```fsharp
     [<Tests>]
     let rehydrationTests =
         testList "SqliteStore.Rehydration" [
             testAsync "State survives store restart" {
                 let tempDb = System.IO.Path.GetTempFileName()
                 let connStr = sprintf "Data Source=%s" tempDb

                 try
                     // First store: write state
                     do
                         let logger = ...
                         use store = new SqliteStateMachineStore<TestState, int>(connStr, logger, jsonOptions)
                         let iface = store :> IStateMachineStore<TestState, int>
                         do! iface.SetState "game1" Active 42 |> Async.AwaitTask

                     // Second store: read state (simulates restart)
                     do
                         let logger = ...
                         use store = new SqliteStateMachineStore<TestState, int>(connStr, logger, jsonOptions)
                         let iface = store :> IStateMachineStore<TestState, int>
                         let! result = iface.GetState("game1") |> Async.AwaitTask
                         Expect.equal result (Some(Active, 42)) "state should survive restart"
                 finally
                     System.IO.File.Delete(tempDb)
             }

             testAsync "Parameterized state survives restart" {
                 // Test with Error "something" to verify DU serialization round-trips
                 ...
             }
         ]
     ```

  2. The rehydration test is the key validation for durable persistence (SC-005).

### Subtask T032 -- Add subscription notification tests

- **Purpose**: Verify observable subscription semantics match the in-memory store.
- **Files**: `test/Frank.Statecharts.Sqlite.Tests/SqliteStoreTests.fs`
- **Parallel**: Yes
- **Steps**:
  1. Add tests mirroring the in-memory store's subscription tests:
     ```fsharp
     [<Tests>]
     let subscriptionTests =
         testList "SqliteStore.Subscriptions" [
             testAsync "Subscribe to existing state emits current state immediately" { ... }
             testAsync "Subscribe to nonexistent instance emits nothing initially" { ... }
             testAsync "SetState notifies subscriber" { ... }
             testAsync "Multiple subscribers all receive notifications" { ... }
             testAsync "Unsubscribe stops notifications" { ... }
             testAsync "Failing subscriber does not break other subscribers" { ... }
         ]
     ```

  2. Follow the exact test patterns from `test/Frank.Statecharts.Tests/StoreTests.fs` but using `SqliteStateMachineStore`.

### Subtask T033 -- Add concurrent access serialization test

- **Purpose**: Verify the actor serializes all SQLite operations (no concurrent database access).
- **Files**: `test/Frank.Statecharts.Sqlite.Tests/SqliteStoreTests.fs`
- **Parallel**: Yes
- **Steps**:
  1. Add a concurrency test:
     ```fsharp
     [<Tests>]
     let concurrencyTests =
         testList "SqliteStore.Concurrency" [
             testAsync "Concurrent SetState operations are serialized" {
                 let store, _ = makeStore ()
                 use _s = store :> IDisposable
                 let iface = store :> IStateMachineStore<TestState, int>

                 let ops =
                     [| for i in 0..49 ->
                            async {
                                do! iface.SetState "same" Active i |> Async.AwaitTask
                            } |]

                 do! Async.Parallel ops |> Async.Ignore

                 let! result = iface.GetState("same") |> Async.AwaitTask
                 Expect.isSome result "should have state after concurrent writes"
             }

             testAsync "Concurrent GetState during SetState does not corrupt data" {
                 // Interleave reads and writes, verify no exceptions or torn reads
                 ...
             }
         ]
     ```

  2. These tests verify that the actor serialization works for SQLite just as it does for the in-memory store (SC-006).

### Subtask T034 -- Full integration build + test validation

- **Purpose**: Run the complete build and test suite across all targets to verify no regressions.
- **Files**: None (validation only)
- **Steps**:
  1. Run full build:
     ```bash
     dotnet build
     ```
     Must succeed for all target frameworks (net8.0, net9.0, net10.0).

  2. Run all tests:
     ```bash
     dotnet test
     ```
     Must pass for all test projects.

  3. Verify key integration scenarios:
     - State key extraction works with parameterized DUs (WP01)
     - Guard DU type change does not break existing guards (WP02)
     - Actor concurrency tests pass (WP03)
     - SQLite store CRUD, rehydration, and subscriptions work (WP04/WP05)

  4. Verify no `Unchecked.defaultof` remains in the codebase:
     ```bash
     grep -r "Unchecked.defaultof" src/Frank.Statecharts/
     ```
     Should return no results.

  5. Fix any regressions discovered during integration.

## Risks & Mitigations

1. **In-memory SQLite connection scope**: In-memory databases (`Data Source=:memory:`) are tied to the connection. When the connection closes, the database is destroyed. This is fine for CRUD and subscription tests but NOT for rehydration tests. Use temp files for rehydration.

2. **FSharp.SystemTextJson version**: Pin to a specific version to avoid compatibility issues. Check the latest stable version on NuGet.

3. **TestLogger reuse**: The `TestLogger` type is defined in `StoreTests.fs` (existing project). The SQLite test project cannot reference it directly. Either duplicate the type in the SQLite test project or extract it to a shared test utilities project. Duplication is acceptable for a small helper type.

4. **Integration regression**: T034 is the safety net. Any cross-cutting issues surfaced during integration should be fixed in this WP, not deferred.

## Review Guidance

- Verify test project follows existing patterns (Expecto, same structure as `Frank.Statecharts.Tests`)
- Verify rehydration test uses file-based SQLite (not in-memory)
- Verify DI extension method matches the pattern from `Store.fs` (`AddStateMachineStore`)
- Verify all tests use `FSharp.SystemTextJson` for DU serialization
- Verify `dotnet build` and `dotnet test` pass for all targets
- Verify `Unchecked.defaultof` is absent from the codebase
- Run `dotnet test` to confirm all tests pass

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T00:05:00Z -- system -- lane=planned -- Prompt generated via /spec-kitty.tasks

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP05 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-16T04:25:03Z – claude-opus – shell_pid=9464 – lane=doing – Assigned agent via workflow command
- 2026-03-16T04:32:48Z – claude-opus – shell_pid=9464 – lane=for_review – Ready for review: SQLite store test project with 25 tests covering CRUD, rehydration, subscriptions, concurrency, disposal, DI registration, and schema auto-creation. Full solution builds and all 888 tests pass.
- 2026-03-16T04:33:32Z – claude-opus-reviewer – shell_pid=16549 – lane=doing – Started review via workflow command
- 2026-03-16T04:37:49Z – claude-opus-reviewer – shell_pid=16549 – lane=done – Review passed: All 7 subtasks (T028-T034) complete. 25 tests across 7 test lists cover CRUD, rehydration, subscriptions, concurrency, disposal, DI registration, and schema auto-creation. Full solution builds (0 errors), all tests pass. Code mirrors MailboxProcessorStore patterns consistently. No regressions.
