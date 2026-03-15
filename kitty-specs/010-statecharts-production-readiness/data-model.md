# Data Model: Statecharts Production Readiness

**Feature**: 010-statecharts-production-readiness
**Date**: 2026-03-15

## Entity Relationship Overview

```
StateMachine<'S,'E,'C> (modified)
       |
       |-- Guards (access-control, pre-handler)
       |-- EventGuards (event-validation, post-handler) [NEW]
       |-- StateMetadata (keyed by case name, not 'State equality)
       |
       v
IStateMachineStore<'S,'C> (modified)
       |
       |-- GetState -> VersionedState<'S,'C> [CHANGED]
       |-- SetState (with expectedVersion) -> SetStateResult [CHANGED]
       |-- Subscribe -> IObserver<'S * 'C>
       |
       |-- implemented by
       |
       +-------> MailboxProcessorStore<'S,'C> (modified for versioning)
       |
       +-------> SqliteStateMachineStore<'S,'C> [NEW]
                    |
                    |-- reads/writes
                    v
                 SQLite DB (state_machine_instances table) [NEW]

StateKeyExtractor [NEW]
       |
       |-- uses FSharpValue.PreComputeUnionTagReader
       |-- uses FSharpType.GetUnionCases
       |-- replaces .ToString() for state-to-key mapping
       v
StateMachineMetadata (modified)
       |
       |-- StateHandlerMap: Map<string, handlers> (keyed by case name)
       |-- EvaluateGuards (access-control only)
       |-- EvaluateEventGuards [NEW]
       |-- GetCurrentStateKey (uses StateKeyExtractor)
       v
StateMachineMiddleware (modified flow)
       |
       1. GetState (with version)
       2. Method check
       3. Access-control guards (pre-handler)
       4. Run handler
       5. Event-validation guards (post-handler) [NEW]
       6. Execute transition (with version check) [MODIFIED]
       7. Handle VersionConflict -> 409 [NEW]
```

## New Entities

### VersionedState<'State, 'Context>

Versioned snapshot of a state machine instance, returned by `GetState`. Bundles the state value, extended context, and an integer concurrency version.

```fsharp
type VersionedState<'State, 'Context> =
    { State: 'State
      Context: 'Context
      Version: int64 }
```

| Attribute | Type | Description |
|-----------|------|-------------|
| State | `'State` | Current state value |
| Context | `'Context` | Extended state / context data |
| Version | `int64` | Monotonically increasing concurrency token |

**Identity**: One per state machine instance per store.
**Lifecycle**: Created on `GetState`, consumed by `SetState` for optimistic concurrency.
**Key constraint**: `Version` starts at `1L` for new instances. Incremented by `1L` on each successful `SetState`. Value `0L` is reserved to signal "new instance, no prior state."

### SetStateResult

Discriminated union representing the outcome of a versioned `SetState` attempt.

```fsharp
[<RequireQualifiedAccess>]
type SetStateResult =
    | Success of newVersion: int64
    | VersionConflict of currentVersion: int64
```

| Variant | Fields | Description |
|---------|--------|-------------|
| Success | `newVersion: int64` | Write succeeded; carries the new version number |
| VersionConflict | `currentVersion: int64` | Write rejected; carries the version currently in the store |

**HTTP mapping**: `VersionConflict` maps to `409 Conflict` in the middleware (FR-004).

### SqliteStateMachineStore<'State, 'Context>

Durable `IStateMachineStore` implementation backed by a SQLite database file. Implements optimistic concurrency via `UPDATE ... WHERE version = @expected`.

```fsharp
type SqliteStateMachineStore<'State, 'Context when 'State: equality>
    (connectionString: string, ?jsonOptions: JsonSerializerOptions) =

    interface IStateMachineStore<'State, 'Context>
    interface IDisposable
```

| Attribute | Type | Description |
|-----------|------|-------------|
| connectionString | `string` | SQLite connection string (e.g., `"Data Source=statecharts.db"`) |
| jsonOptions | `JsonSerializerOptions option` | Serializer options for state/context (default: `JsonSerializerOptions.Default`) |

**Identity**: One per stateful resource type registration in DI.
**Lifecycle**: Singleton, disposed on application shutdown.
**Key behaviors**:
- Auto-creates schema on first use (FR-008)
- Enforces optimistic concurrency via version column (FR-009)
- Manages in-memory subscriber list for observable semantics (FR-010)
- Enables WAL mode for concurrent read performance

### SQLite Table: state_machine_instances

Physical storage schema for durable state persistence.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| instance_id | TEXT | NOT NULL, PK (composite) | State machine instance identifier |
| state_type | TEXT | NOT NULL, PK (composite) | Fully qualified .NET type name of `'State` |
| state_json | TEXT | NOT NULL | JSON-serialized state value |
| context_json | TEXT | NOT NULL | JSON-serialized context value |
| version | INTEGER | NOT NULL, DEFAULT 1 | Concurrency version token |
| updated_at | TEXT | NOT NULL | ISO 8601 timestamp of last update |

**Primary key**: `(instance_id, state_type)` -- allows multiple state machine types to coexist in one database.
**Indexes**: Primary key provides lookup by instance. No additional indexes needed for typical access patterns.

## Modified Entities

### IStateMachineStore<'State, 'Context> (modified)

Updated interface with versioned operations.

| Method | Old Signature | New Signature |
|--------|--------------|---------------|
| GetState | `string -> Task<('State * 'Context) option>` | `string -> Task<VersionedState<'State, 'Context> option>` |
| SetState | `string -> 'State -> 'Context -> Task<unit>` | `string -> 'State -> 'Context -> int64 -> Task<SetStateResult>` |
| Subscribe | (unchanged) | `string -> IObserver<'State * 'Context> -> IDisposable` |

**Breaking change**: Both `GetState` and `SetState` signatures change. All store implementations must update.

### StateMachine<'State, 'Event, 'Context> (modified)

Added `EventGuards` field for two-phase guard evaluation.

| Attribute | Type | Change | Description |
|-----------|------|--------|-------------|
| Initial | `'State` | Unchanged | Starting state |
| InitialContext | `'Context` | Unchanged | Starting context |
| Transition | `'State -> 'Event -> 'Context -> TransitionResult` | Unchanged | Pure transition function |
| Guards | `Guard list` | Unchanged | Access-control guards (pre-handler) |
| EventGuards | `Guard list` | **NEW** | Event-validation guards (post-handler) |
| StateMetadata | `Map<'State, StateInfo>` | Unchanged at type level | Per-state HTTP configuration |

**Backward compatibility**: Existing code that constructs `StateMachine` records will need to add `EventGuards = []` to compile. This is source-breaking but the fix is trivial.

### StateMachineMetadata (modified)

Added event guard evaluation closure.

| Attribute | Type | Change | Description |
|-----------|------|--------|-------------|
| Machine | `obj` | Unchanged | Boxed StateMachine |
| StateHandlerMap | `Map<string, (string * RequestDelegate) list>` | Unchanged (key semantics change) | State key -> handlers. Keys now use DU case names instead of `ToString()` |
| ResolveInstanceId | `HttpContext -> string` | Unchanged | Instance key extractor |
| TransitionObservers | `(obj -> unit) list` | Unchanged | Boxed transition event handlers |
| InitialStateKey | `string` | Unchanged (value semantics change) | Initial state key. Now uses DU case name |
| GetCurrentStateKey | `IServiceProvider -> HttpContext -> string -> Task<string>` | Modified | Now caches version in HttpContext.Items |
| EvaluateGuards | `HttpContext -> GuardResult` | Unchanged | Access-control guards (pre-handler) |
| EvaluateEventGuards | `HttpContext -> GuardResult` | **NEW** | Event-validation guards (post-handler) |
| ExecuteTransition | `IServiceProvider -> HttpContext -> string -> Task<TransitionAttemptResult>` | Modified | Now passes version for optimistic concurrency |

### TransitionAttemptResult (modified)

Added `VersionConflict` case.

| Variant | Fields | Change | HTTP Mapping |
|---------|--------|--------|-------------|
| NoEvent | - | Unchanged | (no action) |
| Succeeded | `transitionEvent: obj` | Unchanged | 200 (handler-determined) |
| Blocked | `BlockReason` | Unchanged | Per BlockReason mapping |
| Invalid | `message: string` | Unchanged | 400 Bad Request |
| VersionConflict | - | **NEW** | 409 Conflict |

### MailboxProcessorStore<'State, 'Context> (modified)

Updated internal state to include version tracking.

| Internal State | Old Type | New Type |
|---------------|----------|----------|
| instances | `Map<string, 'State * 'Context>` | `Map<string, 'State * 'Context * int64>` |

Version is incremented on each successful `SetState`. Version mismatch returns `SetStateResult.VersionConflict`.

## Relationships

1. **VersionedState -> IStateMachineStore**: `GetState` now returns `VersionedState` wrapping state + version. The version is threaded through the middleware for use in `SetState`.

2. **SetStateResult -> Middleware**: The middleware pattern-matches on `SetStateResult` to determine whether to proceed (Success) or return 409 (VersionConflict).

3. **SqliteStateMachineStore -> SQLite DB**: One-to-one relationship between store instance and database file. Multiple state machine types can share one database via the composite primary key.

4. **StateMachine.Guards -> Middleware (pre-handler)**: Access-control guards evaluated at step 3, before the handler runs. No event context available.

5. **StateMachine.EventGuards -> Middleware (post-handler)**: Event-validation guards evaluated at step 5, after the handler sets the event. Full `GuardContext` including the actual event value.

6. **StateKeyExtractor -> StateMachineMetadata**: The key extraction function (built from `FSharpValue.PreComputeUnionTagReader`) is captured in closures used by `StateMachineMetadata` fields. It replaces all `ToString()` calls for state-to-key conversion.

7. **SqliteStateMachineStore -> FSharp.SystemTextJson**: Soft dependency via `JsonSerializerOptions`. Users configure their serializer to handle F# types; the store does not mandate a specific serializer package.

## Data Flow: Versioned State Transition

```
Request arrives
    |
    v
Middleware reads version from store
    GetState("game-42") -> Some { State=XTurn; Context=3; Version=5 }
    |
    v
Cache (state, context, version) in HttpContext.Items
    |
    v
Evaluate access-control guards (no event)
    |
    v
Run handler (handler may call setEvent)
    |
    v
Evaluate event-validation guards (with event, if set)
    |
    v
Execute transition
    transition XTurn (MakeMove 4) 3 -> Transitioned(OTurn, 4)
    |
    v
SetState("game-42", OTurn, 4, expectedVersion=5)
    |
    +---> Success(6) -> notify subscribers, return Succeeded
    |
    +---> VersionConflict(6) -> return 409 Conflict
```
