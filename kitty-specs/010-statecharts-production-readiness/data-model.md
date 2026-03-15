# Data Model: Statecharts Production Readiness

**Feature**: 010-statecharts-production-readiness
**Date**: 2026-03-15 (revised)
**Revision**: 2 -- replaces stale data model based on updated spec (actor-serialized model)

## Entity Relationship Overview

```
StateMachine<'S,'E,'C> (modified)
       |
       |-- Guards (access-control, pre-handler)
       |-- EventGuards (event-validation, post-handler) [NEW]
       |-- StateMetadata (keyed by case name, not 'State equality)
       |
       v
IStateMachineStore<'S,'C> (UNCHANGED)
       |
       |-- GetState -> ('S * 'C) option        [UNCHANGED]
       |-- SetState -> Task<unit>               [UNCHANGED]
       |-- Subscribe -> IObserver<'S * 'C>      [UNCHANGED]
       |
       |-- implemented by
       |
       +-------> MailboxProcessorStore<'S,'C> (UNCHANGED)
       |
       +-------> SqliteStateMachineStore<'S,'C> [NEW]
                    |
                    |-- actor wraps SQLite access internally
                    |-- same MailboxProcessor pattern as in-memory store
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
       1. GetState
       2. Method check
       3. Access-control guards (pre-handler)
       4. Run handler
       5. Event-validation guards (post-handler) [NEW]
       6. Execute transition [UNCHANGED -- no version check]
```

## Key Difference from Revision 1

Revision 1 introduced `VersionedState<'S,'C>`, `SetStateResult`, and `TransitionAttemptResult.VersionConflict` for optimistic concurrency. All of these have been **REMOVED**. The actor-serialized model means:

- `IStateMachineStore` interface is **UNCHANGED** from spec 004
- `MailboxProcessorStore` is **UNCHANGED** -- it already serializes access
- No version tokens, no compare-and-swap, no 409 Conflict responses
- SQLite store uses the same actor pattern, wrapping SQLite internally

## New Entities

### SqliteStateMachineStore<'State, 'Context>

Durable `IStateMachineStore` implementation. Architecturally identical to `MailboxProcessorStore` but adds SQLite persistence inside the actor loop.

```fsharp
type SqliteStateMachineStore<'State, 'Context when 'State: equality>
    (connectionString: string, logger: ILogger, ?jsonOptions: JsonSerializerOptions) =

    interface IStateMachineStore<'State, 'Context>
    interface IDisposable
```

| Attribute | Type | Description |
|-----------|------|-------------|
| connectionString | `string` | SQLite connection string (e.g., `"Data Source=statecharts.db"`) |
| logger | `ILogger` | Logger for error/warning reporting |
| jsonOptions | `JsonSerializerOptions option` | Serializer options for state/context (default: `JsonSerializerOptions.Default`) |

**Identity**: One per stateful resource type registration in DI.
**Lifecycle**: Singleton, disposed on application shutdown.
**Internal architecture**:
- `MailboxProcessor` agent serializes all operations (same as in-memory store)
- In-memory cache (`Map<string, 'State * 'Context>`) for fast reads
- SQLite database for durable persistence
- Subscriber list (`Map<string, IObserver list>`) for observable notifications
- Single `SqliteConnection` opened once, used for lifetime of store

**Key behaviors**:
- Auto-creates schema on first use (FR-008)
- All access serialized through actor -- no concurrent database operations (FR-009)
- Manages in-memory subscriber list for observable semantics (FR-010)
- Enables WAL mode for external read compatibility
- Lazy rehydration: loads from SQLite on first `GetState` cache miss

### SQLite Table: state_machine_instances

Physical storage schema for durable state persistence.

| Column | Type | Constraints | Description |
|--------|------|-------------|-------------|
| instance_id | TEXT | NOT NULL, PK (composite) | State machine instance identifier |
| state_type | TEXT | NOT NULL, PK (composite) | Fully qualified .NET type name of `'State` |
| state_json | TEXT | NOT NULL | JSON-serialized state value |
| context_json | TEXT | NOT NULL | JSON-serialized context value |
| updated_at | TEXT | NOT NULL | ISO 8601 timestamp of last update |

**Primary key**: `(instance_id, state_type)` -- allows multiple state machine types to coexist in one database.
**Indexes**: Primary key provides lookup by instance. No additional indexes needed.
**Note**: No `version` column -- the actor serializes all access, so database-level concurrency control is not needed.

### StateKeyExtractor (internal)

Not a standalone type -- a set of precomputed functions captured in closures at build time.

| Component | Type | Description |
|-----------|------|-------------|
| tagReader | `obj -> int` | Precomputed DU tag reader from `FSharpValue.PreComputeUnionTagReader` |
| caseNames | `string[]` | Array of DU case names from `FSharpType.GetUnionCases` |
| stateKey | `'State -> string` | Composed function: `tagReader >> caseNames.[_]` |

**Lifecycle**: Created once in `StatefulResourceBuilder.Run`, captured in closures used by `StateMachineMetadata`.

## Modified Entities

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
| GetCurrentStateKey | `IServiceProvider -> HttpContext -> string -> Task<string>` | Unchanged | Resolve state from store, cache in Items, return key |
| EvaluateGuards | `HttpContext -> GuardResult` | Unchanged | Access-control guards (pre-handler) |
| EvaluateEventGuards | `HttpContext -> GuardResult` | **NEW** | Event-validation guards (post-handler) |
| ExecuteTransition | `IServiceProvider -> HttpContext -> string -> Task<TransitionAttemptResult>` | Unchanged | Post-handler transition execution |

### IStateMachineStore<'State, 'Context> (UNCHANGED)

The store interface is **not modified** in this spec. The actor-serialized model is a documented contract requirement, not an interface-level change.

| Method | Signature | Change |
|--------|-----------|--------|
| GetState | `string -> Task<('State * 'Context) option>` | UNCHANGED |
| SetState | `string -> 'State -> 'Context -> Task<unit>` | UNCHANGED |
| Subscribe | `string -> IObserver<'State * 'Context> -> IDisposable` | UNCHANGED |

### MailboxProcessorStore<'State, 'Context> (UNCHANGED)

No modifications needed. Already implements actor-serialized access correctly.

### TransitionAttemptResult (UNCHANGED)

No new cases added. The `VersionConflict` case proposed in revision 1 has been removed.

| Variant | Fields | HTTP Mapping |
|---------|--------|-------------|
| NoEvent | - | (no action) |
| Succeeded | `transitionEvent: obj` | 200 (handler-determined) |
| Blocked | `BlockReason` | Per BlockReason mapping |
| Invalid | `message: string` | 400 Bad Request |

## Relationships

1. **SqliteStateMachineStore -> MailboxProcessor**: The SQLite store contains a `MailboxProcessor` that serializes all operations. The actor owns both the in-memory cache and the SQLite connection.

2. **SqliteStateMachineStore -> SQLite DB**: One-to-one relationship between store instance and database connection. Multiple state machine types can share one database via the composite primary key. The actor is the sole accessor.

3. **StateMachine.Guards -> Middleware (pre-handler)**: Access-control guards evaluated at step 3, before the handler runs. No event context available.

4. **StateMachine.EventGuards -> Middleware (post-handler)**: Event-validation guards evaluated at step 5, after the handler sets the event. Full `GuardContext` including the actual event value.

5. **StateKeyExtractor -> StateMachineMetadata**: The key extraction function (built from `FSharpValue.PreComputeUnionTagReader`) is captured in closures used by `StateMachineMetadata` fields. It replaces all `ToString()` calls for state-to-key conversion.

6. **SqliteStateMachineStore -> FSharp.SystemTextJson**: Soft dependency via `JsonSerializerOptions`. Users configure their serializer to handle F# types; the store does not mandate a specific serializer package.

## Data Flow: Actor-Serialized State Transition

```
Request A arrives         Request B arrives (concurrent)
    |                         |
    v                         v
Middleware A                Middleware B
reads state from store      reads state from store
    |                         |
    v                         |
Actor processes             Actor queues B's GetState
GetState("game-42")             |
-> returns (XTurn, 3)           |
    |                           |
    v                           |
A evaluates guards              |
A runs handler                  |
A executes transition           |
A calls SetState("game-42", OTurn, 4)
    |                           |
    v                           |
Actor processes A's SetState    |
-> persists to SQLite           |
-> notifies subscribers         |
-> reply to A                   |
    |                           |
    |                           v
    |                     Actor processes B's GetState
    |                     -> returns (OTurn, 4)  <-- sees A's result
    |                           |
    v                           v
A returns 200               B evaluates guards with OTurn
                            B runs handler
                            B calls SetState(...)
                                |
                                v
                            Actor processes B's SetState
                            -> persists to SQLite
                            -> reply to B
```

Note: Both requests succeed because they are serialized by the actor. No version conflict, no 409. B always sees the state left by A.
