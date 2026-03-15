# Research: Statecharts Production Readiness

**Feature**: 010-statecharts-production-readiness
**Date**: 2026-03-15

## Decision 1: State Key Extraction Mechanism (FR-001, FR-002, FR-013)

### Problem

The current implementation uses `state.ToString()` to derive string keys for the `StateHandlerMap` (mapping states to HTTP handlers). This occurs in three places in `src/Frank.Statecharts/StatefulResourceBuilder.fs`:

1. **Line 176**: `let initialStateKey = machineWithMetadata.Initial.ToString()` -- computing the initial state key
2. **Line 189**: `return state.ToString()` -- converting runtime state to a key for handler lookup
3. **Line 250**: `List.map (fun (s, h) -> (s.ToString(), h))` -- converting `Map<'State, handlers>` to `Map<string, handlers>`

For simple DU cases like `XTurn` or `Draw`, `ToString()` returns `"XTurn"` or `"Draw"`. But for parameterized cases like `Won "X"`, `ToString()` returns `"Won \"X\""`, and `Won "O"` returns `"Won \"O\""`. This means a handler registered via `inState (forState (Won "X") [...])` only matches `Won "X"`, not `Won "O"` -- the developer must register separate handlers for every possible parameter value, which is impractical.

### Options Considered

| Option | Mechanism | Performance | Robustness | Backward Compatible | Complexity |
|--------|-----------|-------------|------------|--------------------|----|
| A. `FSharpValue.PreComputeUnionTagReader` | Reflection at build time to precompute a tag reader; `FSharpType.GetUnionCases` for case name lookup | Fast (precomputed delegate) | Immune to `ToString()` overrides | Yes | Low |
| B. `FSharpValue.GetUnionFields` per call | Reflection on every state lookup | Slow (reflection per request) | Immune to `ToString()` overrides | Yes | Low |
| C. Explicit `stateKey: 'State -> string` parameter | User supplies key function | Fast (user-controlled) | Depends on user implementation | No (API change) | Medium |
| D. Custom `[<StateKey>]` attribute on DU cases | Attribute-based metadata | Fast (cached at startup) | Immune to `ToString()` overrides | No (requires annotations) | High |
| E. Pattern-match on `ToString()` output | Regex/string split to extract case name | Fast | Fragile (depends on `ToString()` format) | Yes | Medium |

### Decision: Option A -- `FSharpValue.PreComputeUnionTagReader` + `FSharpType.GetUnionCases`

**Rationale**:

1. **Precomputed performance**: `FSharpValue.PreComputeUnionTagReader(typeof<'State>)` returns a `obj -> int` function that reads the DU tag (an integer discriminator) without reflection overhead on each call. This function is created once at build time (in the `StatefulResourceBuilder.Run` method) and reused for every request.

2. **Case name mapping**: `FSharpType.GetUnionCases(typeof<'State>)` returns `UnionCaseInfo[]` where each element has a `.Tag` (int) and `.Name` (string) property. By building a `int -> string` lookup array at build time, converting a state value to its case name is O(1): read the tag integer, index into the array.

3. **Existing precedent in the codebase**: `src/Frank.LinkedData/Rdf/InstanceProjector.fs` (line 94) already uses `FSharpValue.PreComputeUnionTagReader` for option type handling. This is not a new pattern for the project.

4. **Immune to `ToString()` overrides** (FR-013): The tag reader bypasses `ToString()` entirely. Even if a developer overrides `ToString()` on their state type, the key extraction uses the compiler-generated tag discriminator.

5. **Backward compatible** (FR-002): Simple (non-parameterized) DU cases produce the same key as `ToString()` would -- the case name string. No existing code needs modification.

### Implementation Sketch

```fsharp
// At build time (in StatefulResourceBuilder.Run):
let stateType = typeof<'State>
let tagReader = FSharpValue.PreComputeUnionTagReader(stateType)
let cases = FSharpType.GetUnionCases(stateType)
let caseNames = cases |> Array.map (fun c -> c.Name)

// State-to-key function (replaces .ToString()):
let stateKey (state: 'State) : string =
    let tag = tagReader (box state)
    caseNames.[tag]
```

### Impact on Existing Code

Three call sites in `StatefulResourceBuilder.fs` change from `.ToString()` to the `stateKey` function:

- `initialStateKey` computation (line 176)
- `getCurrentStateKey` return value (line 189)
- `stateHandlerMap` key conversion (line 250)

The `inState` registration in the CE builder also changes: currently `Map.add stateHandlers.State (...)` uses `'State` equality (which is parameter-sensitive for DU cases). The map key should instead be the state key string (case name), so that `inState (forState (Won "X") [...])` and `inState (forState (Won "O") [...])` both map to key `"Won"`. This requires changing `StateHandlerMap` from `Map<'State, ...>` to `Map<string, ...>` or using the key extraction at `InState` time.

### Note on StateMachine.StateMetadata

The `StateMachine` type has `StateMetadata: Map<'State, StateInfo>` which also uses `'State` as a key. For parameterized states, this map currently requires exact state match. The key extraction should be applied here too -- `StateMetadata` should use string keys (case names) internally, or the builder should normalize the metadata map when constructing `StateMachineMetadata`.

---

## Decision 2: Optimistic Concurrency Model (FR-003, FR-004)

### Problem

The current `IStateMachineStore<'State, 'Context>` interface has no concurrency control:

```fsharp
type IStateMachineStore<'State, 'Context when 'State: equality> =
    abstract GetState: instanceId: string -> Task<('State * 'Context) option>
    abstract SetState: instanceId: string -> state: 'State -> context: 'Context -> Task<unit>
    abstract Subscribe: instanceId: string -> observer: IObserver<'State * 'Context> -> IDisposable
```

`SetState` unconditionally overwrites. In the in-memory `MailboxProcessorStore`, this is safe because the MailboxProcessor serializes all operations. But for any durable store (SQLite, PostgreSQL, etc.), concurrent requests could both read the same state, both pass guards, and both attempt `SetState` -- the second write silently overwrites the first (lost update).

### Options Considered

| Option | Mechanism | Interface Change | Breaking Change |
|--------|-----------|-----------------|----------------|
| A. Version integer on SetState | `SetState` takes and returns a version int | Yes -- signature change | Binary-breaking |
| B. Version in state tuple | `GetState` returns `('State * 'Context * int64)` | Yes -- return type change | Binary-breaking |
| C. Separate `VersionedState` record | New record wrapping state + version | Yes -- new type | Binary-breaking |
| D. `ConcurrencyToken` opaque string | `SetState` takes/returns token | Yes -- signature change | Binary-breaking |
| E. `TrySetState` new method | Add new method, keep old `SetState` | Yes -- new method | Source-compatible (additive) |

### Decision: Option C -- `VersionedState` record with `TrySetState` replacing `SetState`

**Rationale**:

1. **Clear semantics**: A `VersionedState<'State, 'Context>` record type bundles the state, context, and version together. This makes the version non-optional -- callers cannot accidentally forget to pass it.

2. **Version as int64**: An integer version (not an opaque ETag string) is simpler and matches the spec assumption. The version is incremented atomically on each successful `SetState`. The middleware does not expose this as an HTTP ETag (that is spec 008's concern).

3. **Explicit conflict signaling**: `SetState` currently returns `Task<unit>`. The new method should return `Task<Result<int64, ConcurrencyConflict>>` (or a similar DU) so the caller knows whether the write succeeded and what the new version is. Alternatively, returning `Task<bool>` (true = success, false = conflict) is simpler but less informative.

4. **Binary-breaking is acceptable**: The spec note says the `IStateMachineStore` interface is being changed. Since Frank.Statecharts is pre-1.0, binary-breaking changes are expected. The version bump from 6.x to 7.x already set this precedent.

### Proposed Interface

```fsharp
/// Versioned state snapshot returned by GetState.
type VersionedState<'State, 'Context> =
    { State: 'State
      Context: 'Context
      Version: int64 }

/// Result of a versioned SetState attempt.
[<RequireQualifiedAccess>]
type SetStateResult =
    | Success of newVersion: int64
    | VersionConflict of currentVersion: int64

type IStateMachineStore<'State, 'Context when 'State: equality> =
    abstract GetState: instanceId: string -> Task<VersionedState<'State, 'Context> option>
    abstract SetState: instanceId: string -> state: 'State -> context: 'Context -> expectedVersion: int64 -> Task<SetStateResult>
    abstract Subscribe: instanceId: string -> observer: IObserver<'State * 'Context> -> IDisposable
```

For new instances (no prior state), the caller passes `expectedVersion = 0L` (or a sentinel value like `-1L`). The store initializes with version `1L`.

### Impact on MailboxProcessorStore

The existing `MailboxProcessorStore` must be updated to:
- Store `int64` version alongside each instance's state
- Increment version on each successful `SetState`
- Check `expectedVersion` matches stored version before allowing the write
- Return `VersionConflict` if versions mismatch

Since the MailboxProcessor already serializes access, this adds no new concurrency primitives -- just a version check in the `SetState` handler.

### Impact on Middleware (StatefulResourceBuilder.fs)

The `executeTransition` closure (line 215) must:
1. Read the version from `GetState` (or from `HttpContext.Items` where it was cached)
2. Pass the version to `SetState`
3. Handle `VersionConflict` by returning a new `TransitionAttemptResult.VersionConflict` case
4. The middleware maps `VersionConflict` to HTTP 409 Conflict (FR-004)

### Open Question: Retry Logic

Should the middleware automatically retry on version conflict (re-read state, re-evaluate guards, re-run transition)? The spec says the second request "receives a conflict response" (409), implying no automatic retry. This keeps the middleware simple and lets the client decide whether to retry.

**Decision**: No automatic retry in V1. Return 409 and let the client retry.

---

## Decision 3: Two-Phase Guard Evaluation (FR-005, FR-006, FR-012)

### Problem

Guards currently receive `Unchecked.defaultof<'E>` for the event field (line 204 of `StatefulResourceBuilder.fs`):

```fsharp
let guardCtx =
    { User = ctx.User
      CurrentState = state
      Event = Unchecked.defaultof<'E>
      Context = context }
```

This is because guards are evaluated *before* the handler runs (step 3 in middleware), but the handler is what sets the event (step 4). Guards that try to inspect the event get a default/null value.

### Options Considered

| Option | Mechanism | Backward Compatible | Complexity |
|--------|-----------|--------------------|----|
| A. Two guard lists (access + validation) | Separate fields on `StateMachine` | Yes (existing guards default to access) | Medium |
| B. Guard phase marker attribute | Tag each guard with `Pre` or `Post` | Yes (default to `Pre`) | Low |
| C. GuardContext with `Event option` | Change `Event: 'E` to `Event: 'E option` | No (type change) | Low |
| D. Two-pass evaluation in middleware | Run all guards twice (pre and post), skip event checks in pre-pass | No (double evaluation) | High |

### Decision: Option A -- Two separate guard lists with phase discrimination

**Rationale**:

1. **Explicit phases**: The `StateMachine` type gets two guard lists:
   - `Guards: Guard<'State, 'Event, 'Context> list` -- access-control guards (pre-handler, no event context). Backward compatible with existing guards.
   - `EventGuards: Guard<'State, 'Event, 'Context> list` -- event-validation guards (post-handler, event available). New field, defaults to empty list.

2. **Backward compatible** (FR-012): Existing guards that only check `User` and `CurrentState` continue to work in the pre-handler phase. The `Event` field remains `Unchecked.defaultof<'E>` for pre-handler guards (same as current behavior). This is not ideal but preserves backward compatibility. Developers who need event access register event-validation guards instead.

3. **Skip for read-only operations** (FR-006): Event-validation guards are only evaluated when an event is set by the handler. If `StateMachineContext.tryGetEvent` returns `None`, event-validation guards are skipped entirely.

### Alternative Considered: GuardContext with Event option

Changing `Event: 'E` to `Event: 'E option` in `GuardContext` would be cleaner semantically but is source-breaking for all existing guard predicates. Every guard function would need to change from `ctx.Event` to pattern matching on `ctx.Event`. Since this is a pre-1.0 library, this is more acceptable than it would be for a stable API, but the two-list approach avoids this breakage entirely.

### Middleware Flow Change

Current flow:
```
1. GetState
2. Method check
3. Evaluate guards (all, pre-handler)
4. Run handler
5. Execute transition
```

New flow:
```
1. GetState
2. Method check
3. Evaluate access-control guards (pre-handler, no event)
4. Run handler
5. Evaluate event-validation guards (post-handler, with event)
6. Execute transition
```

If any event-validation guard returns `Blocked`, the transition is not executed. However, the handler has already run and may have written to the response. If the response has already started, the middleware logs a warning (same pattern as existing `TransitionAttemptResult.Blocked` handling on line 97-107 of `Middleware.fs`).

### Impact on StatefulResourceBuilder

The `InState` CE operation does not change. Guard registration uses the existing `machine` field plus a new `eventGuards` field on `StateMachine`. The builder's `evaluateGuards` closure splits into two closures:

```fsharp
let evaluateAccessGuards (ctx: HttpContext) : GuardResult = ...  // uses machine.Guards
let evaluateEventGuards (ctx: HttpContext) : GuardResult = ...   // uses machine.EventGuards
```

The `StateMachineMetadata` record gets a new field:
```fsharp
EvaluateEventGuards: HttpContext -> GuardResult
```

---

## Decision 4: SQLite Store Design (FR-007 through FR-011)

### Package and Dependency

The SQLite store lives in a separate project: `Frank.Statecharts.Sqlite`. This avoids adding a SQLite dependency to the core `Frank.Statecharts` package.

**NuGet dependency**: `Microsoft.Data.Sqlite` (10.0.x for net10.0, version-matched for net8.0/net9.0). This is the lightweight ADO.NET provider maintained by the .NET team, distinct from the heavier `System.Data.SQLite` or Entity Framework Core.

### Schema Design

```sql
CREATE TABLE IF NOT EXISTS state_machine_instances (
    instance_id TEXT NOT NULL,
    state_type  TEXT NOT NULL,
    state_json  TEXT NOT NULL,
    context_json TEXT NOT NULL,
    version     INTEGER NOT NULL DEFAULT 1,
    updated_at  TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (instance_id, state_type)
);
```

Key design decisions:

1. **Composite primary key** (`instance_id`, `state_type`): A single SQLite database can store instances for multiple state machine types. The `state_type` column holds the fully qualified type name of `'State` (e.g., `"MyApp.TicTacToeState"`). This prevents collisions when multiple stateful resources share the same database file.

2. **JSON serialization**: `state_json` and `context_json` hold `System.Text.Json`-serialized representations of `'State` and `'Context`. This is the simplest approach and aligns with the spec assumption. The serializer is configurable via the store constructor (`JsonSerializerOptions`).

3. **Version column**: `INTEGER NOT NULL DEFAULT 1`. Incremented on every successful update. Used for optimistic concurrency via `UPDATE ... WHERE version = @expectedVersion`.

4. **Auto-schema creation** (FR-008): The store runs `CREATE TABLE IF NOT EXISTS` on first use (in the constructor or on the first `GetState`/`SetState` call). No migration tooling required.

### Optimistic Concurrency in SQLite (FR-009)

```sql
-- SetState with optimistic concurrency:
UPDATE state_machine_instances
SET state_json = @stateJson,
    context_json = @contextJson,
    version = @newVersion,
    updated_at = datetime('now')
WHERE instance_id = @instanceId
  AND state_type = @stateType
  AND version = @expectedVersion;

-- Check rows affected:
-- 0 rows = version conflict
-- 1 row  = success
```

For new instances (no existing row):
```sql
INSERT INTO state_machine_instances (instance_id, state_type, state_json, context_json, version)
VALUES (@instanceId, @stateType, @stateJson, @contextJson, 1);
```

If the INSERT fails due to a PRIMARY KEY conflict (another request created the instance concurrently), it is treated as a version conflict.

### Subscribe/Observable Pattern (FR-010)

The SQLite store must implement `Subscribe` with the same behavioral semantics as the in-memory store:
- New subscribers immediately receive the current state (BehaviorSubject semantics)
- `SetState` calls notify all active subscribers

Since SQLite has no built-in change notification mechanism, the store manages subscriptions in-memory (using the same `IObserver` list pattern as `MailboxProcessorStore`). The `SetState` method notifies subscribers after a successful database write. This means subscribers are only notified when the write is performed by the same process -- cross-process notifications are out of scope.

### DI Registration (FR-011)

```fsharp
type IServiceCollection with
    member services.AddSqliteStateMachineStore<'State, 'Context
        when 'State: equality and 'State: comparison>
        (connectionString: string, ?options: JsonSerializerOptions) =
        services.AddSingleton<IStateMachineStore<'State, 'Context>>(fun sp ->
            new SqliteStateMachineStore<'State, 'Context>(connectionString, ?options = options)
            :> IStateMachineStore<'State, 'Context>)
```

This replaces the default in-memory store registration. The handler code does not need to change.

### Connection Management

The store should use a connection pool internally. For SQLite with WAL mode, a single writer connection and multiple reader connections is the standard pattern. However, since the store already serializes writes (optimistic concurrency means only one write succeeds per version), a simple approach is:

- Create a new `SqliteConnection` per operation (open, execute, close)
- Enable WAL mode on first connection: `PRAGMA journal_mode=WAL;`
- Use `PRAGMA busy_timeout=5000;` to handle lock contention gracefully (edge case: SQLite file locked by another process)

### F# DU Serialization with System.Text.Json

F# discriminated unions require special handling with `System.Text.Json`. By default, `System.Text.Json` does not know how to serialize/deserialize F# DUs. Options:

1. **`JsonFSharpConverter` from FSharp.SystemTextJson**: NuGet package `FSharp.SystemTextJson` provides `JsonFSharpConverter` that handles DUs, records, options, etc. This is the most robust option but adds a dependency.

2. **Custom `JsonConverter<'State>`**: Write a converter that uses `FSharpValue.GetUnionFields` / `FSharpValue.MakeUnion` for serialization/deserialization. More work but avoids the external dependency.

3. **.NET 9+ `JsonDerivedType` approach**: Not applicable to F# DUs.

**Decision**: Accept `JsonSerializerOptions` as a parameter and document that users should configure `FSharp.SystemTextJson` if their state/context types use F# idioms. The store does not take a hard dependency on `FSharp.SystemTextJson` -- users add it to their application project.

For the test suite and samples, include `FSharp.SystemTextJson` as a test/sample dependency.

---

## Decision 5: Version Bump and Breaking Changes

### Summary of Breaking Changes

| Change | Type | Affects |
|--------|------|---------|
| `IStateMachineStore.GetState` return type change | Binary-breaking | All store implementations |
| `IStateMachineStore.SetState` signature change | Binary-breaking | All store implementations |
| `StateMachine.EventGuards` new field | Source-breaking (record construction) | All `StateMachine` record literals |
| `VersionedState` new type | Additive | None |
| `SetStateResult` new type | Additive | None |
| `StateMachineMetadata.EvaluateEventGuards` new field | Binary-breaking | Internal (metadata is constructed by builder) |

### Version Strategy

Since Frank.Statecharts is pre-1.0 and the spec 004 implementation has not shipped a stable release, these breaking changes are acceptable. The version should advance to reflect the interface changes. If the current version is 7.x (post spec 004), the next version with these changes should be 8.0.0 (or maintain 7.x with a minor bump, depending on the project's versioning policy for pre-1.0 packages).

---

## Open Questions

1. **State key for non-DU state types**: If `'State` is not a discriminated union (e.g., a string or record), `FSharpType.GetUnionCases` will fail. Should the key extraction fall back to `ToString()` for non-DU types? Or should the library require `'State` to be a DU? The spec focuses on DU cases, so a runtime check + fallback is reasonable.

2. **ETag interaction with version**: The `StatechartETagProvider` (spec 008) currently hashes `(state, context)` to produce ETags. With versioning, should the ETag incorporate the version number? Probably not -- the version is an internal concurrency mechanism, while the ETag represents content equivalence. Two requests reading the same state but different versions should get the same ETag.

3. **Cross-process SQLite subscribers**: The current design only notifies in-process subscribers. If multiple application instances share a SQLite database, state changes from one process are not visible to subscribers in another process. This is documented as a known limitation.

4. **Guard evaluation when response has started**: If an event-validation guard blocks after the handler has already written to the response, the middleware cannot change the status code. The current approach (log a warning) is consistent with existing `TransitionAttemptResult.Blocked` handling. Should this be a stronger guarantee (e.g., buffering the response)? Deferred -- same trade-off as the existing design.

5. **Multi-targeting for Frank.Statecharts.Sqlite**: Should the SQLite package target `net8.0;net9.0;net10.0` like the core package? `Microsoft.Data.Sqlite` is available for all three. The answer depends on whether the project wants to maintain multi-target consistency. Recommendation: match the core package's target frameworks.
