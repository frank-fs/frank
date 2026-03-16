# Data Model: Statecharts Production Readiness

**Feature**: 010-statecharts-production-readiness
**Date**: 2026-03-15 (revised)
**Revision**: 3 -- replaces stale Guard model (two separate fields) with Guard DU per updated spec

## Entity Relationship Overview

```
StateMachine<'S,'E,'C> (modified)
       |
       |-- Guards: Guard<'S,'E,'C> list  (DU type, mixed AccessControl + EventValidation)
       |-- StateMetadata (keyed by case name, not 'State equality)
       |
       v
Guard<'S,'E,'C> (DU -- replaces Guard record)
       |
       +-- AccessControl of name * predicate:(AccessControlContext -> GuardResult)
       |     runs pre-handler, no event parameter
       |
       +-- EventValidation of name * predicate:(EventValidationContext -> GuardResult)
             runs post-handler, receives actual event
       |
       v
AccessControlContext<'S,'C>    (NEW -- replaces GuardContext for pre-handler guards)
EventValidationContext<'S,'E,'C> (NEW -- replaces GuardContext for post-handler guards)

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
       |-- EvaluateGuards (AccessControl guards only, pre-handler)
       |-- EvaluateEventGuards [NEW] (EventValidation guards only, post-handler)
       |-- GetCurrentStateKey (uses StateKeyExtractor)
       v
StateMachineMiddleware (modified flow)
       |
       1. GetState
       2. Method check
       3. AccessControl guards (pre-handler, no event)
       4. Run handler
       5. EventValidation guards (post-handler, with actual event) [NEW]
       6. Execute transition [UNCHANGED -- no version check]
```

## Key Differences from Revision 2

Revision 2 added `EventGuards: Guard<'S,'E,'C> list` as a SEPARATE field on `StateMachine`, keeping the old `Guard` record type with `GuardContext` containing `Event: 'E` (populated with `Unchecked.defaultof`). This has been **REPLACED** with:

- `Guard` is now a **discriminated union**, not a record
- `GuardContext` is **removed**, replaced by `AccessControlContext` (no event) and `EventValidationContext` (with event)
- `StateMachine` keeps a **single** `Guards` field (DU list), no `EventGuards` field
- The DU case determines both execution phase and type signature

This eliminates `Unchecked.defaultof<'E>` entirely -- `AccessControl` guards cannot access the event because their context type has no `Event` field.

## New Entities

### Guard<'State, 'Event, 'Context> (DU -- replaces record)

Discriminated union with two cases. Each case determines both the execution phase and the predicate's type signature.

```fsharp
type Guard<'State, 'Event, 'Context> =
    | AccessControl of name: string * predicate: (AccessControlContext<'State, 'Context> -> GuardResult)
    | EventValidation of name: string * predicate: (EventValidationContext<'State, 'Event, 'Context> -> GuardResult)
```

| Case | Name Field | Predicate Input | Execution Phase | Event Available |
|------|-----------|-----------------|-----------------|-----------------|
| `AccessControl` | `string` | `AccessControlContext<'S,'C>` | Pre-handler (step 3) | No |
| `EventValidation` | `string` | `EventValidationContext<'S,'E,'C>` | Post-handler (step 5) | Yes (actual event) |

**Identity**: Guards are identified by name (used for logging/diagnostics).
**Lifecycle**: Defined at compile time in `StateMachine` record, separated into two lists at build time in `StatefulResourceBuilder.Run`.

### AccessControlContext<'State, 'Context> (NEW)

Context record for pre-handler guards. Has NO event field.

```fsharp
type AccessControlContext<'State, 'Context> =
    { User: ClaimsPrincipal
      CurrentState: 'State
      Context: 'Context }
```

| Attribute | Type | Description |
|-----------|------|-------------|
| User | `ClaimsPrincipal` | Current authenticated user |
| CurrentState | `'State` | Current state of the instance |
| Context | `'Context` | Current context of the instance |

### EventValidationContext<'State, 'Event, 'Context> (NEW)

Context record for post-handler guards. Has the actual event value.

```fsharp
type EventValidationContext<'State, 'Event, 'Context> =
    { User: ClaimsPrincipal
      CurrentState: 'State
      Event: 'Event
      Context: 'Context }
```

| Attribute | Type | Description |
|-----------|------|-------------|
| User | `ClaimsPrincipal` | Current authenticated user |
| CurrentState | `'State` | Current state of the instance |
| Event | `'Event` | Actual event set by the handler (never null/defaultof) |
| Context | `'Context` | Current context of the instance |

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

`Guards` field type changes from `Guard` record list to `Guard` DU list. No new fields added.

| Attribute | Type | Change | Description |
|-----------|------|--------|-------------|
| Initial | `'State` | Unchanged | Starting state |
| InitialContext | `'Context` | Unchanged | Starting context |
| Transition | `'State -> 'Event -> 'Context -> TransitionResult` | Unchanged | Pure transition function |
| Guards | `Guard<'S,'E,'C> list` | **TYPE CHANGED** (record -> DU) | Mixed list of AccessControl and EventValidation guards |
| StateMetadata | `Map<'State, StateInfo>` | Unchanged at type level | Per-state HTTP configuration |

**Breaking change**: The `Guards` field's element type changes from the `Guard` record (`{ Name; Predicate }`) to the `Guard` DU (`AccessControl | EventValidation`). Existing guard definitions must be updated.

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
| EvaluateGuards | `HttpContext -> GuardResult` | Unchanged (implementation change) | AccessControl guards only (pre-handler). No longer passes `Unchecked.defaultof` event. |
| EvaluateEventGuards | `HttpContext -> GuardResult` | **NEW** | EventValidation guards (post-handler, with actual event) |
| ExecuteTransition | `IServiceProvider -> HttpContext -> string -> Task<TransitionAttemptResult>` | Unchanged | Post-handler transition execution |

### Removed Entities

| Entity | Reason |
|--------|--------|
| `GuardContext<'State, 'Event, 'Context>` | Replaced by `AccessControlContext` (no event) and `EventValidationContext` (with event) |
| `Guard<'State, 'Event, 'Context>` (record type) | Replaced by `Guard` DU with `AccessControl` and `EventValidation` cases |

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

1. **Guard DU -> AccessControlContext / EventValidationContext**: Each DU case carries a predicate function that receives a different context type. The `AccessControl` case receives `AccessControlContext` (no event); the `EventValidation` case receives `EventValidationContext` (with event). The DU case is the discriminator for both the context type and the execution phase.

2. **SqliteStateMachineStore -> MailboxProcessor**: The SQLite store contains a `MailboxProcessor` that serializes all operations. The actor owns both the in-memory cache and the SQLite connection.

3. **SqliteStateMachineStore -> SQLite DB**: One-to-one relationship between store instance and database connection. Multiple state machine types can share one database via the composite primary key. The actor is the sole accessor.

4. **StateMachine.Guards -> Middleware**: The single `Guards` list is partitioned by DU case at build time:
   - `AccessControl` guards -> `evaluateGuards` closure -> step 3 (pre-handler)
   - `EventValidation` guards -> `evaluateEventGuards` closure -> step 5 (post-handler)

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
A evaluates AccessControl       |
  guards (pre-handler)          |
A runs handler                  |
A evaluates EventValidation     |
  guards (post-handler)         |
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
A returns 200               B evaluates AccessControl guards with OTurn
                            B runs handler
                            B evaluates EventValidation guards
                            B calls SetState(...)
                                |
                                v
                            Actor processes B's SetState
                            -> persists to SQLite
                            -> reply to B
```

Note: Both requests succeed because they are serialized by the actor. No version conflict, no 409. B always sees the state left by A.

## Data Flow: Guard DU Evaluation

```
Request arrives
    |
    v
Middleware resolves state from store
    |
    v
Partition Guards list by DU case (done at build time):
  AccessControl guards: [("isAuthenticated", pred1), ("isAdmin", pred2)]
  EventValidation guards: [("validateMove", pred3)]
    |
    v
Step 3: Evaluate AccessControl guards
  Build AccessControlContext { User; CurrentState; Context }
  (no Event field -- cannot access event)
  If any returns Blocked -> return 403/409/etc
    |
    v
Step 4: Run handler
  Handler calls StateMachineContext.setEvent(ctx, event)
    |
    v
Step 5: Evaluate EventValidation guards
  Build EventValidationContext { User; CurrentState; Event; Context }
  (Event is actual value from handler, never null/defaultof)
  If any returns Blocked -> suppress transition, log warning if response started
    |
    v
Step 6: Execute transition
```
