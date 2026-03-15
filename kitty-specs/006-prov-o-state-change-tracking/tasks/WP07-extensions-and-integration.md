---
work_package_id: WP07
title: WebHostBuilderExtensions + Integration Tests
lane: done
dependencies:
- WP04
subtasks: [T031, T032, T033, T034, T035, T036]
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-011]
---

# Work Package Prompt: WP07 -- WebHostBuilderExtensions + Integration Tests

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
spec-kitty implement WP07 --base WP06
```

Depends on WP04 (middleware), WP05 (observer), WP06 (agent types). This is the convergence point.

---

## Objectives & Success Criteria

- `useProvenance` custom operation on `WebHostBuilder` registers all DI services and middleware
- `ProvenanceSubscriptionManager` (IHostedService) subscribes to `onTransition` on start, disposes on stop
- Optional `ProvenanceStoreConfig` passthrough via `useProvenance { maxRecords 50_000 }`
- Custom `IProvenanceStore` registered in DI overrides default `MailboxProcessorProvenanceStore`
- Full end-to-end pipeline: state transition -> provenance record -> content-negotiated response
- All acceptance scenarios from User Stories 1-4 are covered by integration tests
- `dotnet build Frank.sln` and `dotnet test` both pass

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/006-prov-o-state-change-tracking/plan.md` -- Project structure, dependency graph
- `kitty-specs/006-prov-o-state-change-tracking/research.md` -- Decision 4 (subscription lifecycle), Decision 5 (middleware registration)
- `kitty-specs/006-prov-o-state-change-tracking/quickstart.md` -- `useProvenance` usage examples
- `src/Frank.LinkedData/WebHostBuilderExtensions.fs` -- Pattern reference for middleware + CE extension
- `src/Frank.Auth/` -- Pattern reference for DI registration + CE extension (if available)

**Key constraints**:
- `useProvenance` follows the same `[<AutoOpen>]` pattern as `useLinkedData` and `useStatecharts`
- `WebHostBuilder` CE is in `Frank.Builder` namespace
- Registration order: `useStatecharts` before `useProvenance` (provenance depends on statecharts hooks)
- Middleware ordering: provenance middleware runs BEFORE LinkedData middleware
- `IHostedService` lifecycle: subscriptions happen after endpoint routing is built
- Default store is `MailboxProcessorProvenanceStore` with `ProvenanceStoreConfig.defaults`
- If user registers custom `IProvenanceStore` in DI (via `services`), default is NOT created
- Constitution VI: all subscriptions stored and disposed on shutdown

**Final .fsproj compilation order**:
```xml
<Compile Include="ProvVocabulary.fs" />
<Compile Include="Types.fs" />
<Compile Include="Store.fs" />
<Compile Include="MailboxProcessorStore.fs" />
<Compile Include="GraphBuilder.fs" />
<Compile Include="TransitionObserver.fs" />
<Compile Include="Middleware.fs" />
<Compile Include="WebHostBuilderExtensions.fs" />
```

---

## Subtasks & Detailed Guidance

### Subtask T031 -- Create `WebHostBuilderExtensions.fs` with `useProvenance` custom operation

**Purpose**: Provide the developer-facing API for enabling provenance on a Frank web host.

**Steps**:
1. Create `src/Frank.Provenance/WebHostBuilderExtensions.fs`
2. Study `src/Frank.LinkedData/WebHostBuilderExtensions.fs` for the exact pattern:
   - `[<AutoOpen>]` module
   - `type WebHostBuilder with` extension
   - `[<CustomOperation("useProvenance")>]` member
   - Registration of services and middleware in the `WebHostSpec`

3. Implement the extension:

```fsharp
namespace Frank.Provenance

open Frank.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting

[<AutoOpen>]
module WebHostBuilderProvenanceExtensions =

    type WebHostBuilder with

        /// Enable PROV-O provenance tracking for all stateful resources.
        /// Registers IProvenanceStore (default MailboxProcessorProvenanceStore),
        /// TransitionObserver, ProvenanceSubscriptionManager, and provenance middleware.
        [<CustomOperation("useProvenance")>]
        member _.UseProvenance(state: WebHostSpec) =
            // Add DI registrations and middleware to the spec
            ...

        /// Enable PROV-O provenance tracking with custom configuration.
        [<CustomOperation("useProvenance")>]
        member _.UseProvenance(state: WebHostSpec, config: ProvenanceStoreConfig) =
            // Same as above but with custom config
            ...
```

4. Add `WebHostBuilderExtensions.fs` to `.fsproj` as the LAST file:
   ```xml
   <Compile Include="WebHostBuilderExtensions.fs" />
   ```

**Files**: `src/Frank.Provenance/WebHostBuilderExtensions.fs`
**Notes**:
- The `WebHostSpec` is Frank's internal representation accumulated by the CE. Study how `useLinkedData` and `useStatecharts` modify it.
- If `WebHostBuilder` uses a different pattern (e.g., accumulating a list of setup actions), adapt accordingly.
- The overload with `ProvenanceStoreConfig` enables `useProvenance { maxRecords 50_000 }` syntax. If CE overloads are not supported, use a builder pattern instead.
- Verify the exact CE pattern by reading `src/Frank/Builder.fs` for `WebHostBuilder` and `WebHostSpec` types.

### Subtask T032 -- Implement DI registration

**Purpose**: Register all provenance services in the ASP.NET Core DI container.

**Steps**:
1. In the `useProvenance` implementation, add service registrations:

```fsharp
let registerProvenanceServices (services: IServiceCollection) (config: ProvenanceStoreConfig) =
    // Register default store only if no custom store is already registered
    services.TryAddSingleton<IProvenanceStore>(fun sp ->
        let logger = sp.GetRequiredService<ILogger<MailboxProcessorProvenanceStore>>()
        new MailboxProcessorProvenanceStore(config, logger) :> IProvenanceStore)

    // Register observer
    services.AddSingleton<TransitionObserver>() |> ignore

    // Register subscription manager (IHostedService)
    services.AddHostedService<ProvenanceSubscriptionManager>() |> ignore
```

2. Use `TryAddSingleton` for `IProvenanceStore` so custom registrations via `services` take precedence
3. `TransitionObserver` is singleton (one observer for all resources)
4. `ProvenanceSubscriptionManager` is registered as `IHostedService` for lifecycle management

**Files**: `src/Frank.Provenance/WebHostBuilderExtensions.fs`
**Notes**:
- `TryAddSingleton` is critical: if the user registered their own `IProvenanceStore` via the `services` custom operation on `WebHostBuilder`, the default `MailboxProcessorProvenanceStore` must NOT be created. This satisfies User Story 4 (External Provenance Store) and SC-008.
- The factory lambda for `IProvenanceStore` resolves `ILogger` from DI, ensuring proper logging infrastructure.
- `services.AddHostedService<T>()` requires `T` to implement `IHostedService`.

### Subtask T033 -- Implement ProvenanceSubscriptionManager (IHostedService)

**Purpose**: Manage the lifecycle of onTransition subscriptions: subscribe after endpoints are built, dispose on shutdown.

**Steps**:
1. Add `ProvenanceSubscriptionManager` class (can be in `WebHostBuilderExtensions.fs` or a separate file):

```fsharp
open Microsoft.Extensions.Hosting

/// Manages provenance observer subscriptions to statechart transition observables.
/// Subscribes on application start, disposes subscriptions on stop.
type ProvenanceSubscriptionManager(
    observer: TransitionObserver,
    store: IProvenanceStore,
    logger: ILogger<ProvenanceSubscriptionManager>) =

    let subscriptions = ResizeArray<IDisposable>()

    interface IHostedService with
        member _.StartAsync(cancellationToken) = task {
            // Enumerate all StateMachineMetadata endpoints
            // Subscribe observer to each store's onTransition observable
            // Store IDisposable subscriptions for cleanup
            logger.LogInformation("Provenance subscription manager started, {Count} subscriptions active",
                                  subscriptions.Count)
        }

        member _.StopAsync(cancellationToken) = task {
            for sub in subscriptions do
                try sub.Dispose()
                with ex -> logger.LogWarning(ex, "Error disposing provenance subscription")
            subscriptions.Clear()
            logger.LogInformation("Provenance subscription manager stopped")
        }
```

2. The `StartAsync` implementation depends on how Frank.Statecharts exposes its transition observables:
   - Option A: Enumerate `IEndpointRouteBuilder` metadata for `StateMachineMetadata` endpoints
   - Option B: Resolve `IStateMachineStore` instances from DI and subscribe to their observables
   - Option C: If Frank.Statecharts provides a central `ITransitionEventBus`, subscribe to that

3. Since Frank.Statecharts may not be available, implement a placeholder that:
   - Logs that no statechart endpoints were found (if metadata enumeration yields nothing)
   - Is ready to wire up when the Statecharts API is finalized

**Files**: `src/Frank.Provenance/WebHostBuilderExtensions.fs` (or separate `SubscriptionManager.fs`)
**Notes**:
- The subscription lifecycle is critical for Constitution VI (disposal discipline)
- Each `IDisposable` from `observable.Subscribe(observer)` must be stored and disposed on stop
- `StopAsync` disposes subscriptions before the store is disposed (ordering matters)
- If a subscription Dispose throws, log and continue (do not let one failure prevent others from disposing)

### Subtask T034 -- Implement optional ProvenanceStoreConfig passthrough

**Purpose**: Enable `useProvenance { maxRecords 50_000 }` syntax for custom configuration.

**Steps**:
1. If the CE overload pattern supports it, implement:

```fsharp
// Option A: CE overload with ProvenanceStoreConfig
[<CustomOperation("useProvenance")>]
member _.UseProvenance(state: WebHostSpec, config: ProvenanceStoreConfig) =
    registerProvenanceServices config state

// Option B: Separate maxRecords operation (if CE nesting is required)
// This depends on how WebHostBuilder CE is structured
```

2. If `WebHostBuilder` does not support parameterized custom operations easily, use a simpler approach:
   - `useProvenance` always uses defaults
   - Provide a standalone `configureProvenance` function or custom operation that accepts `ProvenanceStoreConfig`

3. Test that both forms work:
```fsharp
// Default
webHost { useProvenance; ... }

// Custom config
webHost { useProvenance { maxRecords 50_000 }; ... }
// OR
webHost { useProvenance; configureProvenance { maxRecords 50_000 }; ... }
```

**Files**: `src/Frank.Provenance/WebHostBuilderExtensions.fs`
**Notes**:
- Study how other Frank extensions handle configuration (e.g., `useLinkedData` with config options)
- The quickstart.md shows `useProvenance { maxRecords 50_000 }` syntax -- try to match this
- If CE nesting is too complex, a separate `configureProvenance` operation is acceptable

**Implementation note**: The `useProvenance` CE supports optional configuration:
```fsharp
webHost {
    useProvenance {
        maxRecords 50_000
    }
    useStatecharts
}
```
Default configuration uses 10,000 max records if no `maxRecords` is specified.

### Subtask T035 -- Create `IntegrationTests.fs` with full-pipeline TestHost tests

**Purpose**: End-to-end tests verifying the complete provenance pipeline: transition -> record -> query -> response.

**Steps**:
1. Create `test/Frank.Provenance.Tests/IntegrationTests.fs`
2. Set up TestHost with `useProvenance` enabled and a stateful resource:

```fsharp
// Since Frank.Statecharts may not be available, simulate the pipeline:
// 1. Register IProvenanceStore via DI
// 2. Register provenance middleware
// 3. Manually trigger TransitionObserver.OnNext with test events
// 4. Query provenance via middleware with Accept headers
```

3. Write tests covering User Story 1 acceptance scenarios:

**a. US1-SC1: POST triggers transition, provenance record created**
- Trigger `TransitionObserver.OnNext` with a test event
- Query store via `QueryByResource`, verify record has Agent, Activity, Entity

**b. US1-SC2: Authenticated user, agent references ClaimsPrincipal**
- Create event with authenticated principal
- Verify record agent is `Person` with correct name/identifier

**c. US1-SC3: Pre/post transition entities captured**
- Verify `UsedEntity.StateName` = previous state
- Verify `GeneratedEntity.StateName` = new state

**d. US1-SC4: Guard-blocked request, no provenance record**
- Do NOT call `TransitionObserver.OnNext` (guard-blocked transitions never fire onTransition)
- Verify store has no records for the resource

4. Write tests covering User Story 2 acceptance scenarios:

**e. US2-SC1: GET with Turtle Accept -> valid Turtle**
- Seed store with records
- GET resource URI with `Accept: application/vnd.frank.provenance+turtle`
- Verify 200, Content-Type, body contains `prov:Activity`

**f. US2-SC2: GET with JSON-LD Accept -> valid JSON-LD**
- Same setup, verify JSON-LD output with `@context`

**g. US2-SC3: No provenance records -> empty graph (200)**
- GET with provenance Accept on empty resource
- Verify 200 (not 404)

**h. US2-SC4: Standard Accept -> normal response**
- GET with `Accept: application/json`
- Verify normal response, not provenance

5. Write test covering User Story 4:

**i. US4-SC1: Custom store receives all records**
- Register mock store in DI
- Trigger observer
- Verify mock received the record

6. Add `IntegrationTests.fs` to test `.fsproj`

**Files**: `test/Frank.Provenance.Tests/IntegrationTests.fs`
**Validation**: `dotnet test test/Frank.Provenance.Tests/` passes with all integration tests green.

### Subtask T036 -- Create `CustomStoreTests.fs` with DI replacement tests

**Purpose**: Verify that custom `IProvenanceStore` implementations can replace the default store via DI.

**Steps**:
1. Create `test/Frank.Provenance.Tests/CustomStoreTests.fs`
2. Implement a test custom store:

```fsharp
type TestCustomStore() =
    let records = ResizeArray<ProvenanceRecord>()
    member _.AppendedRecords = records |> Seq.toList
    member _.QueryResults = ResizeArray<string>()
    interface IProvenanceStore with
        member _.Append(r) = records.Add(r)
        member _.QueryByResource(uri) =
            // Track queries for verification
            ...
            Task.FromResult(records |> Seq.filter (fun r -> r.ResourceUri = uri) |> Seq.toList)
        member _.QueryByAgent(id) = Task.FromResult(records |> Seq.filter (fun r -> r.Agent.Id = id) |> Seq.toList)
        member _.QueryByTimeRange(s, e) = Task.FromResult(records |> Seq.filter (fun r -> r.RecordedAt >= s && r.RecordedAt <= e) |> Seq.toList)
    interface IDisposable with
        member _.Dispose() = ()
```

3. Write tests covering User Story 4 acceptance scenarios:

**a. US4-SC1: Custom store receives records instead of default**
- Register `TestCustomStore` in DI BEFORE `useProvenance`
- Trigger transition observer
- Verify `TestCustomStore.AppendedRecords` contains the record
- Verify default `MailboxProcessorProvenanceStore` was NOT created

**b. US4-SC2: Default store is queryable**
- Use default configuration (no custom store)
- Append records, query by resource/agent/time
- Verify correct subsets returned

4. Add `CustomStoreTests.fs` to test `.fsproj`

**Files**: `test/Frank.Provenance.Tests/CustomStoreTests.fs`
**Validation**: `dotnet test test/Frank.Provenance.Tests/` passes with all custom store tests green.

---

## Test Strategy

- Run `dotnet build Frank.sln` to verify solution-level compilation (all targets)
- Run `dotnet test test/Frank.Provenance.Tests/` -- all tests pass
- Verify `useProvenance` compiles within a `webHost` CE with Frank.Statecharts project reference
- Verify total test count covers all acceptance scenarios from spec

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Frank.Statecharts API surface drift | Frank.Statecharts is fully available on master. Use real `onTransition` and `IStateMachineStore.Subscribe` for integration tests. |
| WebHostBuilder CE pattern unclear | Read `src/Frank/Builder.fs` for exact pattern; follow LinkedData/Auth extensions |
| IHostedService startup ordering | Use `IHostApplicationLifetime.ApplicationStarted` if endpoints are not yet built during `StartAsync` |
| Middleware ordering conflicts | Document: useProvenance MUST come after useStatecharts, ideally before useLinkedData |
| TryAddSingleton behavior with generic interface | Test explicitly that custom registration takes precedence |

---

## Review Guidance

- Verify `useProvenance` follows the same CE pattern as `useLinkedData` / `useStatecharts`
- Verify `TryAddSingleton` is used for `IProvenanceStore` (not `AddSingleton`)
- Verify `ProvenanceSubscriptionManager` implements `IHostedService`
- Verify all subscription `IDisposable` instances are stored and disposed on stop
- Verify `StopAsync` continues disposing even if one subscription throws
- Verify `.fsproj` compilation order is correct (WebHostBuilderExtensions.fs is last)
- Verify integration tests cover all 4 User Stories from spec
- Verify custom store DI test proves default is NOT created when custom is registered
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
