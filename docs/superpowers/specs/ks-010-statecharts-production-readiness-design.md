---
source: kitty-specs/010-statecharts-production-readiness
status: complete
type: spec
---

# Feature Specification: Statecharts Production Readiness

**Feature Branch**: `010-statecharts-production-readiness`
**Created**: 2026-03-15
**Status**: Done
**GitHub Issue**: #95
**Predecessor**: `kitty-specs/004-frank-statecharts/` (spec 004, issue #87)
**Input**: Production readiness gaps identified during WP06 integration testing of #87. Four architectural concerns that must be addressed before Frank.Statecharts can be recommended for real applications.

## User Scenarios & Testing

### User Story 1 - Parameterized State Matching (Priority: P1)

A Frank developer defines a stateful resource whose state type includes parameterized discriminated union cases (e.g., `Won of winner: string`, `Error of code: int`). The developer registers a single `inState` handler block that matches all values of that DU case, regardless of the parameter value. For example, one handler block covers `Won "X"`, `Won "O"`, and any future `Won` variant without requiring separate registrations for each.

**Why this priority**: This is the most fundamental gap. Without it, any DU case carrying data is effectively unusable as a state key, severely limiting the state modeling patterns developers can employ. Every other gap builds on the assumption that state matching works correctly for real-world state types.

**Independent Test**: Define a state machine with a parameterized state (e.g., `Playing | Won of string | Draw`). Register a single `inState` handler for `Won`. Trigger transitions to `Won "X"` and `Won "O"`. Verify that both resolve to the same handler block and return the correct response. Verify that a request in `Playing` state does NOT match the `Won` handler.

**Acceptance Scenarios**:

1. **Given** a stateful resource with a `Won of string` state and a single `inState` handler registered for `Won`, **When** the resource is in state `Won "X"`, **Then** the handler is invoked and returns a successful response.
2. **Given** the same configuration, **When** the resource is in state `Won "O"`, **Then** the same handler is invoked (not a different registration).
3. **Given** a state machine with both parameterized (`Won of string`) and simple (`Draw`) states, **When** handlers are registered for each, **Then** routing dispatches to the correct handler based on the DU case name, not the full `ToString()` value.
4. **Given** a state type with nested parameters (e.g., `Playing of phase: Phase`), **When** a handler is registered for `Playing`, **Then** it matches all `Playing` variants regardless of the nested value.
5. **Given** a state type with a simple case (no parameters), **When** a handler is registered, **Then** matching behavior is unchanged from the current implementation (backward compatible).

---

### User Story 2 - Actor-Serialized Concurrency for State Changes (Priority: P1)

A Frank developer's stateful resource correctly handles concurrent requests from multiple users. All state access (reads and writes) is serialized through an actor (e.g., `MailboxProcessor`), so concurrent requests are processed sequentially. There are no lost updates because the actor is the single point of access — no optimistic concurrency tokens or version numbers are needed.

**Why this priority**: This is a correctness concern in any multi-user deployment. The current in-memory `MailboxProcessor` store already serializes access correctly. The architectural requirement is that the `IStateMachineStore` contract assumes actor-serialized access, so any store implementation (including durable stores) must go through an actor rather than allowing direct concurrent access to the backing store.

**Independent Test**: Create a stateful resource. Send two concurrent POST requests that both trigger transitions from the same state. Verify that they are processed sequentially (one completes before the other starts), and the final state reflects both transitions applied in order. No lost updates occur.

**Acceptance Scenarios**:

1. **Given** a stateful resource in state A with two simultaneous requests, **When** both attempt transitions, **Then** the actor processes them sequentially — the first transitions A→B, the second sees state B (not A).
2. **Given** a durable store (e.g., SQLite), **When** state is read or written, **Then** all access goes through the actor — no direct database access bypasses the actor's serialization.
3. **Given** the in-memory store, **When** concurrent requests arrive, **Then** the existing `MailboxProcessor` serialization behavior is unchanged (backward compatible).
4. **Given** a store implementation, **When** it is registered, **Then** it wraps its persistence backend in an actor that serializes all operations.
5. **Given** the actor-serialized model, **When** existing single-request flows run, **Then** they work without requiring callers to manage concurrency explicitly.

---

### User Story 3 - Guard Access to Event Context (Priority: P2)

A Frank developer defines guards that need to inspect the incoming event to make authorization decisions. Currently, guards run before the handler sets the event, so they receive `Unchecked.defaultof<'E>` — a null/default value that is a footgun. The fix replaces the single guard function with a discriminated union containing two cases: `AccessControl` (receives user, state, context — runs pre-handler, no event parameter) and `EventValidation` (receives user, state, event, context — runs post-handler with the actual event). The DU case determines both execution timing and type signature, so guards that don't need the event use `AccessControl` and guards that do use `EventValidation`. This is a breaking change, acceptable for pre-1.0.

**Why this priority**: This is an API correctness issue. The current `Unchecked.defaultof<'E>` behavior can cause runtime errors in guards that access the event. Since this is a pre-1.0 library, backward compatibility is not a constraint — the guard type can change freely.

**Independent Test**: Define an `AccessControl` guard that checks user/state and an `EventValidation` guard that inspects the event. Trigger a POST request that sets an event. Verify the `EventValidation` guard receives the actual event value. For a GET request (no event), verify only the `AccessControl` guard runs.

**Acceptance Scenarios**:

1. **Given** an `AccessControl` guard that checks only user and state, **When** the guard is evaluated pre-handler, **Then** it works correctly without any event parameter.
2. **Given** an `EventValidation` guard that inspects the event, **When** evaluated post-handler after the event is set, **Then** it receives the actual event value (not `Unchecked.defaultof`).
3. **Given** a GET request (no event), **When** guards are evaluated, **Then** only `AccessControl` guards run; `EventValidation` guards are skipped.
4. **Given** the guard type change from a single function to a DU with `AccessControl` and `EventValidation` cases, **When** existing guard code is updated, **Then** guards that only check user and state become `AccessControl` cases, and guards that need the event become `EventValidation` cases.

---

### User Story 4 - SQLite Durable State Persistence (Priority: P2)

A Frank developer deploys a stateful resource to production and needs state to survive application restarts. The developer configures a SQLite-backed store instead of the default in-memory store, and state machines resume from their persisted state after restart. The SQLite store wraps its persistence in an actor (per Story 2) so all reads and writes are serialized — persistence goes through the actor, never directly.

**Why this priority**: The in-memory store is only suitable for development and testing. A durable store is necessary for any production deployment. SQLite provides a low-dependency, file-based option that validates the store interface design without requiring external infrastructure. The actor-serialized model (Story 2) means the SQLite store doesn't need its own concurrency control — the actor handles it.

**Independent Test**: Configure a stateful resource with SQLite persistence. Create an instance, trigger state transitions, stop the application, restart, and verify the state is preserved. Also verify that concurrent access is serialized through the actor.

**Acceptance Scenarios**:

1. **Given** a stateful resource configured with SQLite persistence, **When** a state transition occurs, **Then** the actor persists the new state to the SQLite database.
2. **Given** a persisted state in SQLite, **When** the application restarts and a request arrives for the same instance, **Then** the actor rehydrates from SQLite and returns the previously persisted state (not the initial state).
3. **Given** two concurrent requests to the same instance with SQLite persistence, **When** both attempt state transitions, **Then** the actor serializes them — no concurrent database access occurs.
4. **Given** a SQLite store, **When** the store is registered via dependency injection, **Then** it replaces the default in-memory store transparently (same interface, no handler changes needed).
5. **Given** a SQLite store with subscriptions, **When** a state change is persisted, **Then** subscribers are notified of the new state (same observable semantics as the in-memory store).
6. **Given** a new application with no existing SQLite database, **When** the store is initialized, **Then** it creates the necessary schema automatically (no manual migration step).

---

### Edge Cases

- What happens when the SQLite database file is locked by another process? (Store should surface a clear error, not hang indefinitely.)
- What happens when a parameterized state has a `ToString()` override that collides with another case's key? (The new key extraction mechanism should be immune to custom `ToString()` implementations.)
- How does the SQLite store handle serialization of arbitrary `'State` and `'Context` types? (Likely JSON serialization with a configurable serializer.)
- What happens when a guard throws an exception (as opposed to returning `Blocked`)? (Propagate as 500, same as current behavior.)
- What happens when the SQLite store's `Subscribe` is called but the database has no entry for the instance? (Return no initial value, same as in-memory behavior.)
- What happens when the actor's mailbox overflows under extreme load? (MailboxProcessor has unbounded queues by default; document as a known limitation with guidance on backpressure strategies.)
- How does the SQLite store actor handle rehydration on startup — eager (load all instances) or lazy (load on first access)? (Lazy by default for scalability.)

## Requirements

### Functional Requirements

- **FR-001**: System MUST provide a state-key extraction mechanism that maps parameterized DU cases to a single key regardless of parameter values (e.g., all `Won _` variants share one key)
- **FR-002**: System MUST preserve backward compatibility for simple (non-parameterized) DU cases -- existing `inState` registrations must work without modification
- **FR-003**: System MUST require that all `IStateMachineStore` implementations serialize state access through an actor (e.g., `MailboxProcessor`), ensuring no concurrent reads or writes to the backing store
- **FR-004**: System MUST ensure that persistence (for durable stores) goes through the actor — external code never reads or writes the backing store directly
- **FR-005**: System MUST replace the current guard type with a discriminated union: `AccessControl` (no event parameter) and `EventValidation` (with event parameter). The DU case determines both execution phase and type signature. This is a breaking change (acceptable for pre-1.0).
- **FR-006**: System MUST eliminate the use of `Unchecked.defaultof<'E>` in guard evaluation — `AccessControl` guards have no event parameter, `EventValidation` guards receive the actual event
- **FR-007**: System MUST provide a SQLite-backed `IStateMachineStore` implementation that persists state durably across application restarts
- **FR-008**: System MUST auto-create the SQLite schema on first use (no manual migration step required)
- **FR-009**: System MUST serialize all SQLite store access through an actor, eliminating the need for database-level concurrency control
- **FR-010**: System MUST support the `Subscribe` (observable) interface on the SQLite store with the same behavioral semantics as the in-memory store
- **FR-011**: System MUST allow the SQLite store to be registered via dependency injection as a drop-in replacement for the in-memory store
- **FR-012**: System MUST handle the case where `ToString()` has been overridden on a state type without producing incorrect key collisions

### Key Entities

- **State Key**: The identifier derived from a DU state value used to look up handlers. Currently `state.ToString()`; will be replaced with a mechanism that groups parameterized cases by DU case name.
- **State Actor**: An actor (e.g., `MailboxProcessor`) that serializes all state reads and writes for a given store instance. The actor is the concurrency mechanism — no version tokens or compare-and-swap needed. How the actor persists state (SQLite, Akka.Persistence, event sourcing, etc.) is an internal implementation detail, not a public interface.
- **Guard**: A discriminated union with two cases: `AccessControl` (receives user, state, context — runs pre-handler, no event parameter) and `EventValidation` (receives user, state, event, context — runs post-handler with actual event). The DU case determines both execution timing and type signature. No `option`, no phase marker, no `Unchecked.defaultof`.
- **SQLite Store**: A durable `IStateMachineStore` implementation backed by an actor wrapping a SQLite database file, supporting persistent state and observable subscriptions.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Developers can register a single handler for a parameterized DU case and have it match all parameter values without additional registrations
- **SC-002**: Two concurrent requests to the same stateful resource instance are processed sequentially by the actor -- no lost updates occur
- **SC-003**: Existing stateful resource definitions (from spec 004) compile and pass all tests without modification after the changes (backward compatibility)
- **SC-004**: Guard DU eliminates all uses of `Unchecked.defaultof` — `AccessControl` guards have no event parameter, `EventValidation` guards receive the actual event
- **SC-005**: State persisted via the SQLite store survives application restart and is correctly restored on the next request
- **SC-006**: The SQLite store serializes all access through its actor -- no concurrent database operations occur
- **SC-007**: The SQLite store can be swapped in for the in-memory store with a single DI registration change and no handler modifications
- **SC-008**: All changes compile and pass tests across all supported target frameworks without regressions

## Assumptions

- The state-key extraction mechanism will use F# reflection to extract the DU case name (tag) at registration time, avoiding runtime `ToString()` calls. The exact API (e.g., a `stateKey` function parameter vs. automatic reflection) will be determined during design.
- SQLite serialization of `'State` and `'Context` types will use JSON. The serializer will be configurable but default to `System.Text.Json`.
- Concurrency is handled by actor serialization, not version tokens. The `IStateMachineStore` contract assumes all implementations serialize access through an actor. No compare-and-swap or optimistic concurrency tokens are needed at the interface level.
- Durable stores are actor implementations that happen to persist state. There is no public persistence interface — how an actor persists (SQLite, Akka.Persistence, event sourcing, etc.) is an internal implementation detail.
- The guard type changes from a single function to a DU with `AccessControl` and `EventValidation` cases. This is a breaking change, acceptable for pre-1.0. The DU case determines both execution phase and type signature — no `option`, no phase marker needed.
- The SQLite store will live in a separate project/package (`Frank.Statecharts.Sqlite` or similar) to avoid adding a SQLite dependency to the core library.


## Research

# Research: Statecharts Production Readiness

**Feature**: 010-statecharts-production-readiness
**Date**: 2026-03-15 (revised)
**Revision**: 3 -- replaces stale D-003 (two separate guard lists) with Guard DU per updated spec

## Decision 1: State Key Extraction Mechanism (FR-001, FR-002, FR-012)

**Status**: CARRIED FORWARD from revision 1 (unchanged)

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

4. **Immune to `ToString()` overrides** (FR-012): The tag reader bypasses `ToString()` entirely. Even if a developer overrides `ToString()` on their state type, the key extraction uses the compiler-generated tag discriminator.

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

## Decision 2: Actor-Serialized Concurrency Model (FR-003, FR-004)

**Status**: CARRIED FORWARD from revision 2 (unchanged)

### Problem

The spec requires that all state access (reads and writes) is serialized through an actor. The existing `MailboxProcessorStore` already does this correctly for the in-memory case. The architectural requirement is that the `IStateMachineStore` contract *assumes* actor-serialized access, so any store implementation (including durable stores) must go through an actor rather than allowing direct concurrent access to the backing store.

### Why NOT optimistic concurrency

The previous research (revision 1, D-002) proposed `VersionedState<'S,'C>` with version tokens and `UPDATE WHERE version = @expected`. The spec replaces this with actor serialization because:

1. **Simpler interface**: The `IStateMachineStore` interface does not need version parameters. `SetState` remains `Task<unit>` -- no `SetStateResult`, no `VersionConflict` case.
2. **No lost updates by design**: Since the actor serializes all reads and writes, concurrent requests are queued and processed one at a time. The second request always sees the state left by the first -- no compare-and-swap needed.
3. **Implementation consistency**: The in-memory store already uses a `MailboxProcessor`. Durable stores use the same pattern -- they wrap their backing store (SQLite, etc.) inside an actor. The concurrency model is uniform.
4. **No 409 Conflict responses**: The middleware does not need a `VersionConflict` case in `TransitionAttemptResult`. Concurrent requests are simply serialized -- the second request may see a different state than expected and the transition function handles that (returning `Invalid` or `Blocked` as appropriate).

### Decision: Actor-serialized access with UNCHANGED `IStateMachineStore` interface

The `IStateMachineStore<'State, 'Context>` interface remains exactly as it is today:

```fsharp
type IStateMachineStore<'State, 'Context when 'State: equality> =
    abstract GetState: instanceId: string -> Task<('State * 'Context) option>
    abstract SetState: instanceId: string -> state: 'State -> context: 'Context -> Task<unit>
    abstract Subscribe: instanceId: string -> observer: IObserver<'State * 'Context> -> IDisposable
```

**Contract assumption**: All implementations MUST serialize access through an actor (e.g., `MailboxProcessor`). This is a documented contract requirement, not an interface-level enforcement. The interface itself is simple; the serialization guarantee comes from the implementation.

### How the Existing MailboxProcessorStore Already Satisfies This

Looking at `src/Frank.Statecharts/Store.fs`:

- The `MailboxProcessorStore` wraps all state operations (`GetState`, `SetState`, `Subscribe`, `Unsubscribe`, `Stop`) in a `StoreMessage` DU and processes them sequentially in the agent loop.
- `instances` is a mutable `Map` accessed only inside the agent -- no external concurrent access.
- `PostAndAsyncReply` ensures the caller waits for the operation to complete before proceeding.

This is already the correct pattern. The key insight is: **no interface changes are needed**. Durable stores must simply follow the same pattern.

### Implications for Middleware and TransitionAttemptResult

- `TransitionAttemptResult` does NOT gain a `VersionConflict` case.
- `Middleware.fs` does NOT need a 409 Conflict handler for concurrency.
- The middleware's `executeTransition` closure calls `store.SetState` which is guaranteed to succeed (the actor serialized the operation).
- If two concurrent requests both want to transition from state A, the actor processes them sequentially: the first transitions A->B and succeeds; the second reads state B (not A), and the transition function decides whether B->C is valid.

### Impact Assessment

- **No breaking changes** to `IStateMachineStore`, `MailboxProcessorStore`, `StateMachineMetadata`, `TransitionAttemptResult`, or the middleware flow.
- The `MailboxProcessorStore` is already correct. No modifications needed.
- New store implementations (SQLite) must wrap their backing store in an actor -- this is the implementation pattern, not an interface change.

---

## Decision 3: Guard DU with AccessControl and EventValidation Cases (FR-005, FR-006)

**Status**: NEW in revision 3 -- replaces stale D-003 (two separate guard lists with `Guards` and `EventGuards` fields)

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

### Why the Previous Approach Was Wrong

Revision 1 and 2 of this research chose "Option A -- Two separate guard lists": adding `EventGuards: Guard<'S,'E,'C> list` as a new field on `StateMachine`, keeping the existing `Guards` field unchanged, and splitting guard evaluation into two closures. This was rejected by the spec update because:

1. **It doesn't fix the type safety problem**: Access-control guards still receive `GuardContext` with `Event: 'E` containing `Unchecked.defaultof<'E>`. The dangerous footgun remains in the type system -- nothing prevents a developer from accidentally reading `ctx.Event` in an access-control guard.
2. **Two separate lists is not a DU**: The spec explicitly requires "a discriminated union with two cases" (FR-005). A DU is semantically cleaner -- the case name tells you what the guard does and what parameters it receives.
3. **The DU case determines the type signature**: `AccessControl` guards have NO event parameter at all (not `Unchecked.defaultof`, not `option`, just absent). `EventValidation` guards receive the actual event value. This is only achievable with a DU where each case has a different function signature.

### Options Considered

| Option | Mechanism | Type Safety | Complexity |
|--------|-----------|-------------|------------|
| A. Two guard lists (stale revision 2) | `Guards` + `EventGuards` fields on `StateMachine` | Weak (Event still present as defaultof) | Medium |
| B. GuardContext with `Event option` | Single list, `Event: 'E option` | Medium (runtime option check) | Low |
| C. Guard DU with `AccessControl` and `EventValidation` | Single DU type, different function signatures per case | Strong (no event parameter exists for AccessControl) | Low |

### Decision: Option C -- Guard DU with `AccessControl` and `EventValidation` cases

The guard type becomes a discriminated union. Each case carries a named predicate with a DIFFERENT function signature:

```fsharp
/// Context for access-control guards (pre-handler). No event available.
type AccessControlContext<'State, 'Context> =
    { User: ClaimsPrincipal
      CurrentState: 'State
      Context: 'Context }

/// Context for event-validation guards (post-handler). Event is available.
type EventValidationContext<'State, 'Event, 'Context> =
    { User: ClaimsPrincipal
      CurrentState: 'State
      Event: 'Event
      Context: 'Context }

/// A guard that controls access to state transitions.
/// The DU case determines both execution phase and type signature.
type Guard<'State, 'Event, 'Context> =
    /// Runs pre-handler. No event parameter -- cannot access the event.
    | AccessControl of name: string * predicate: (AccessControlContext<'State, 'Context> -> GuardResult)
    /// Runs post-handler. Receives the actual event set by the handler.
    | EventValidation of name: string * predicate: (EventValidationContext<'State, 'Event, 'Context> -> GuardResult)
```

**Rationale**:

1. **Type-safe by construction**: `AccessControl` guards literally cannot access the event -- the `AccessControlContext` record has no `Event` field. There is no `Unchecked.defaultof`, no `option`, and no way to accidentally read a null event value.

2. **DU case determines execution phase**: The middleware/builder closures pattern-match on the DU case to decide WHEN the guard runs:
   - `AccessControl` guards are evaluated pre-handler (step 3 in middleware flow)
   - `EventValidation` guards are evaluated post-handler (new step 5, after the handler sets the event)

3. **Single `Guards` field on `StateMachine`**: The `StateMachine` record keeps a single `Guards: Guard<'S,'E,'C> list` field. No `EventGuards` field. The list contains both access-control and event-validation guards mixed together; the middleware separates them by pattern matching.

4. **Breaking change is acceptable**: This is pre-1.0. The old `Guard` record type, `GuardContext` record, and guard predicate signature all change. Every existing guard definition must be updated. The migration is mechanical: wrap existing guard predicates in `AccessControl(name, fun ctx -> ...)` and change `ctx.CurrentState` / `ctx.Context` field access (same names, just on a different record type).

### Eliminated Constructs

The following types from the current `Types.fs` are REMOVED:

- `GuardContext<'State, 'Event, 'Context>` -- replaced by `AccessControlContext` and `EventValidationContext`
- `Guard<'State, 'Event, 'Context>` (the record type) -- replaced by the `Guard` DU

### New StateMachineMetadata Fields

`StateMachineMetadata` gains a new closure:

```fsharp
/// Evaluate event-validation guards after the handler has set the event.
EvaluateEventGuards: HttpContext -> GuardResult
```

The existing `EvaluateGuards` closure is updated to only evaluate `AccessControl` guards (filtering the `Guards` list by DU case).

### Middleware Flow Change

Current flow:
```
1. GetState
2. Method check
3. Evaluate guards (all, pre-handler, with Unchecked.defaultof event)
4. Run handler
5. Execute transition
```

New flow:
```
1. GetState
2. Method check
3. Evaluate AccessControl guards (pre-handler, no event)
4. Run handler
5. Evaluate EventValidation guards (post-handler, with actual event)
6. Execute transition
```

If any `EventValidation` guard returns `Blocked`, the transition is not executed. However, the handler has already run and may have written to the response. If the response has already started, the middleware logs a warning (same pattern as existing `TransitionAttemptResult.Blocked` handling on line 97-107 of `Middleware.fs`).

### Builder Closures

The `StatefulResourceBuilder.Run` method splits guard evaluation into two closures:

```fsharp
// Separate guards by DU case at build time
let accessGuards =
    machineWithMetadata.Guards
    |> List.choose (function
        | AccessControl(name, pred) -> Some(name, pred)
        | _ -> None)

let eventGuards =
    machineWithMetadata.Guards
    |> List.choose (function
        | EventValidation(name, pred) -> Some(name, pred)
        | _ -> None)

// Closure: evaluate access-control guards (pre-handler)
let evaluateGuards (ctx: HttpContext) : GuardResult =
    let state = ctx.Items[StateMachineContext.stateKey] :?> 'S
    let context = ctx.Items[StateMachineContext.contextKey] :?> 'C

    let guardCtx: AccessControlContext<'S, 'C> =
        { User = ctx.User
          CurrentState = state
          Context = context }

    accessGuards
    |> List.tryPick (fun (_, pred) ->
        match pred guardCtx with
        | Allowed -> None
        | Blocked reason -> Some(Blocked reason))
    |> Option.defaultValue Allowed

// Closure: evaluate event-validation guards (post-handler)
let evaluateEventGuards (ctx: HttpContext) : GuardResult =
    let state = ctx.Items[StateMachineContext.stateKey] :?> 'S
    let context = ctx.Items[StateMachineContext.contextKey] :?> 'C

    match StateMachineContext.tryGetEvent<'E> ctx with
    | None -> Allowed  // No event set -- skip event guards
    | Some event ->
        let guardCtx: EventValidationContext<'S, 'E, 'C> =
            { User = ctx.User
              CurrentState = state
              Event = event
              Context = context }

        eventGuards
        |> List.tryPick (fun (_, pred) ->
            match pred guardCtx with
            | Allowed -> None
            | Blocked reason -> Some(Blocked reason))
        |> Option.defaultValue Allowed
```

### Impact on StateMachine Record

The `StateMachine` record's `Guards` field type changes from `Guard<'S,'E,'C> list` (where `Guard` was a record) to `Guard<'S,'E,'C> list` (where `Guard` is now a DU). The field name stays the same. No `EventGuards` field is added.

```fsharp
type StateMachine<'State, 'Event, 'Context when 'State: equality and 'State: comparison> =
    { Initial: 'State
      InitialContext: 'Context
      Transition: 'State -> 'Event -> 'Context -> TransitionResult<'State, 'Context>
      Guards: Guard<'State, 'Event, 'Context> list    // DU type, not record type
      StateMetadata: Map<'State, StateInfo> }
```

---

## Decision 4: SQLite Store as Actor-Wrapped Persistence (FR-007 through FR-011)

**Status**: CARRIED FORWARD from revision 2 (unchanged)

### Architectural Change from Revision 1

The previous research designed the SQLite store with direct database access protected by `UPDATE WHERE version = @expected` for optimistic concurrency. The updated spec replaces this: the SQLite store is an **actor implementation that wraps SQLite internally**. Persistence goes through the actor, never directly. No database-level concurrency control is needed because the actor serializes all access.

### Package and Dependency

The SQLite store lives in a separate project: `Frank.Statecharts.Sqlite`. This avoids adding a SQLite dependency to the core `Frank.Statecharts` package.

**NuGet dependency**: `Microsoft.Data.Sqlite` (10.0.x for net10.0, version-matched for net8.0/net9.0). This is the lightweight ADO.NET provider maintained by the .NET team.

### Actor Architecture

The SQLite store wraps a `MailboxProcessor` that serializes all reads, writes, and subscriptions -- exactly the same pattern as the in-memory `MailboxProcessorStore`, but with SQLite persistence inside the actor loop.

```fsharp
type SqliteStateMachineStore<'State, 'Context when 'State: equality>
    (connectionString: string, logger: ILogger, ?jsonOptions: JsonSerializerOptions) =

    // Internal actor -- all SQLite operations happen inside this loop
    let agent = MailboxProcessor<StoreMessage<'State, 'Context>>.Start(fun inbox ->
        // In-memory cache + subscriber list (same as MailboxProcessorStore)
        let mutable instances = Map.empty<string, 'State * 'Context>
        let mutable subscribers = Map.empty<string, IObserver<'State * 'Context> list>

        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | GetState(id, reply) ->
                // 1. Check in-memory cache
                // 2. If miss, load from SQLite
                // 3. Cache the result
                // 4. Reply
                ...
            | SetState(id, state, ctx, reply) ->
                // 1. Update in-memory cache
                // 2. Persist to SQLite (INSERT or UPDATE, no version check needed)
                // 3. Notify subscribers
                // 4. Reply
                ...
            | Subscribe(id, observer, reply) -> ...
            | Unsubscribe(id, observer) -> ...
            | Stop(reply) -> ...
        }
        loop ()
    )

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
```

### Why This Works Without Database-Level Concurrency Control

Because the actor serializes all operations:

1. **No concurrent reads/writes to SQLite**: The `MailboxProcessor` processes one message at a time. Two concurrent `SetState` requests are queued and executed sequentially.
2. **No `UPDATE WHERE version = @expected`**: The actor is the sole writer. There is no race between reading a version and writing with that version.
3. **No `VersionConflict` response**: Since operations are serialized, every write succeeds. The transition function (called in middleware *before* `SetState`) determines whether the transition is valid given the current state.
4. **Matches the in-memory store exactly**: The SQLite store is architecturally identical to `MailboxProcessorStore` -- it just adds `INSERT/UPDATE` calls inside the `SetState` handler.

### Rehydration Strategy: Lazy (Load on First Access)

**Decision**: The SQLite store uses lazy rehydration -- state is loaded from SQLite into the in-memory cache on first access (first `GetState` call for a given `instanceId`), not eagerly on startup.

**Rationale**:

1. **Scalability**: An application may have thousands of persisted instances. Loading all of them at startup wastes memory for instances that may never be accessed again.
2. **Startup time**: Eager loading adds startup latency proportional to the number of persisted instances. Lazy loading adds zero startup cost.
3. **Consistency with Akka patterns**: Akka DurableStateBehavior loads state when the actor is first activated (on first message), not when the actor system starts. This is the established pattern for actor-based persistence.
4. **Memory efficiency**: Only actively-accessed instances consume memory. The SQLite database serves as the authoritative store; the in-memory cache is a performance optimization.

**Implementation**: In the `GetState` handler inside the actor loop:

```fsharp
| GetState(id, reply) ->
    match Map.tryFind id instances with
    | Some state ->
        // Cache hit -- return immediately
        reply.Reply(Some state)
    | None ->
        // Cache miss -- load from SQLite
        let dbResult = loadFromSqlite id  // synchronous SQLite call inside actor
        match dbResult with
        | Some(state, ctx) ->
            instances <- Map.add id (state, ctx) instances
            reply.Reply(Some(state, ctx))
        | None ->
            reply.Reply(None)
    return! loop ()
```

### SQLite Schema Design

```sql
CREATE TABLE IF NOT EXISTS state_machine_instances (
    instance_id  TEXT NOT NULL,
    state_type   TEXT NOT NULL,
    state_json   TEXT NOT NULL,
    context_json TEXT NOT NULL,
    updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
    PRIMARY KEY (instance_id, state_type)
);
```

Key design decisions:

1. **Composite primary key** (`instance_id`, `state_type`): A single SQLite database can store instances for multiple state machine types. The `state_type` column holds the fully qualified type name of `'State` (e.g., `"MyApp.TicTacToeState"`). This prevents collisions when multiple stateful resources share the same database file.

2. **No version column**: Unlike the previous research, there is no `version` column. The actor serializes all access, so there is no need for database-level concurrency control. The database is just a persistence sink.

3. **JSON serialization**: `state_json` and `context_json` hold `System.Text.Json`-serialized representations of `'State` and `'Context`. The serializer is configurable via the store constructor (`JsonSerializerOptions`).

4. **Auto-schema creation** (FR-008): The store runs `CREATE TABLE IF NOT EXISTS` on initialization. No migration tooling required.

### SQLite Persistence Operations (Inside Actor)

Since all operations execute inside the actor loop (single-threaded), they use synchronous SQLite calls. This is correct because `Microsoft.Data.Sqlite`'s async methods are actually synchronous under the hood anyway (SQLite does not support true async I/O).

```sql
-- UPSERT (Insert or Replace) on SetState:
INSERT INTO state_machine_instances (instance_id, state_type, state_json, context_json, updated_at)
VALUES (@instanceId, @stateType, @stateJson, @contextJson, datetime('now'))
ON CONFLICT (instance_id, state_type)
DO UPDATE SET
    state_json = excluded.state_json,
    context_json = excluded.context_json,
    updated_at = excluded.updated_at;

-- Load on GetState (cache miss):
SELECT state_json, context_json
FROM state_machine_instances
WHERE instance_id = @instanceId AND state_type = @stateType;
```

### Microsoft.Data.Sqlite Async Limitations

An important finding: SQLite does not support asynchronous I/O. The `Microsoft.Data.Sqlite` async methods (`ExecuteNonQueryAsync`, `ExecuteReaderAsync`, etc.) execute synchronously under the hood. This is a well-documented limitation.

**Impact on our design**: Since all SQLite operations happen inside the `MailboxProcessor` agent loop (which runs on a thread pool thread via `async { }`), using synchronous SQLite calls is correct and avoids the false promise of async SQLite operations. The `MailboxProcessor` itself provides the asynchrony -- callers await the reply channel, not the SQLite call.

### Connection Management

```fsharp
// Single connection, opened once, kept alive for the lifetime of the store.
// This is safe because the actor serializes all access -- only one operation
// at a time ever uses the connection.
let connection = new SqliteConnection(connectionString)
connection.Open()

// Enable WAL mode for better behavior if external tools read the database.
use cmd = connection.CreateCommand()
cmd.CommandText <- "PRAGMA journal_mode=WAL;"
cmd.ExecuteNonQuery() |> ignore

// Set busy timeout for external lock contention.
cmd.CommandText <- "PRAGMA busy_timeout=5000;"
cmd.ExecuteNonQuery() |> ignore
```

A single connection is sufficient because the actor is single-threaded. This avoids connection pool overhead and ensures the SQLite database file is never accessed concurrently from the store. WAL mode is still useful if external tools (e.g., DB Browser for SQLite) read the database while the application is running.

### Subscribe/Observable Pattern (FR-010)

The SQLite store manages subscriptions identically to the in-memory store:
- In-memory subscriber list inside the actor (same `Map<string, IObserver list>` pattern)
- `SetState` notifies subscribers after successful persistence
- BehaviorSubject semantics: new subscribers receive current state immediately
- Cross-process notifications are out of scope (same as in-memory store)

### DI Registration (FR-011)

```fsharp
type IServiceCollection with
    member services.AddSqliteStateMachineStore<'State, 'Context
        when 'State: equality and 'State: comparison>
        (connectionString: string, ?options: JsonSerializerOptions) =
        services.AddSingleton<IStateMachineStore<'State, 'Context>>(fun sp ->
            let logger =
                sp.GetRequiredService<ILogger<SqliteStateMachineStore<'State, 'Context>>>()
            new SqliteStateMachineStore<'State, 'Context>(connectionString, logger, ?options = options)
            :> IStateMachineStore<'State, 'Context>)
```

This replaces the default in-memory store registration. The handler code does not need to change.

### F# DU Serialization with System.Text.Json

F# discriminated unions require special handling with `System.Text.Json`. By default, `System.Text.Json` does not know how to serialize/deserialize F# DUs. Options:

1. **`JsonFSharpConverter` from FSharp.SystemTextJson**: NuGet package `FSharp.SystemTextJson` provides `JsonFSharpConverter` that handles DUs, records, options, etc. This is the most robust option but adds a dependency.

2. **Custom `JsonConverter<'State>`**: Write a converter that uses `FSharpValue.GetUnionFields` / `FSharpValue.MakeUnion` for serialization/deserialization. More work but avoids the external dependency.

**Decision**: Accept `JsonSerializerOptions` as a parameter and document that users should configure `FSharp.SystemTextJson` if their state/context types use F# idioms. The store does not take a hard dependency on `FSharp.SystemTextJson` -- users add it to their application project. For the test suite, include `FSharp.SystemTextJson` as a test dependency.

---

## Decision 5: MailboxProcessor Backpressure and Queue Management

**Status**: CARRIED FORWARD from revision 2 (unchanged)

### Problem

F# `MailboxProcessor` has an unbounded internal message queue. Under extreme load (many concurrent state operations), the queue can grow without limit, potentially causing memory issues.

### Research Findings

1. **`CurrentQueueLength` property**: `MailboxProcessor` exposes `CurrentQueueLength: int` which returns the number of unprocessed messages. This can be used to implement backpressure.

2. **No built-in bounded queue**: Unlike Akka's bounded mailbox configurations, F# `MailboxProcessor` does not natively support queue limits. Backpressure must be implemented by the application.

3. **Bounded queue pattern**: A `BlockingQueueAgent<T>` can be implemented by checking `inbox.CurrentQueueLength > maxQueueSize` before accepting new messages. Tomas Petricek documented this pattern for F# agents.

### Decision: Document as known limitation; do not implement backpressure in V1

**Rationale**:

1. **Scope**: Backpressure is an operational concern, not a correctness concern. The spec lists it as an edge case: "MailboxProcessor has unbounded queues by default; document as a known limitation with guidance on backpressure strategies."

2. **Practical impact**: For a web application, the HTTP server itself provides implicit backpressure -- Kestrel limits concurrent connections and request queue depth. The MailboxProcessor queue would only grow if the application accepted more requests than the store can process, which is bounded by the HTTP pipeline.

3. **Complexity**: Adding configurable backpressure (e.g., rejecting operations with 503 Service Unavailable when queue exceeds a threshold) would add API complexity for a scenario that is unlikely in typical Frank deployments.

4. **Future work**: If monitoring reveals queue growth issues in production, backpressure can be added to the store interface as an opt-in feature (e.g., a `TrySetState` method that returns immediately if the queue is full). This does not require interface changes now.

**Guidance for documentation**: Note that `MailboxProcessor` queues are unbounded. Recommend monitoring `CurrentQueueLength` in production and scaling horizontally if queue depth grows. Suggest Kestrel connection limits as the primary backpressure mechanism.

---

## Decision 6: Version Bump and Breaking Changes

### Summary of Breaking Changes

| Change | Type | Affects |
|--------|------|---------|
| `Guard` type: record -> DU | Source-breaking | All guard definitions |
| `GuardContext` removed, replaced by `AccessControlContext` + `EventValidationContext` | Source-breaking | All guard predicates |
| `StateMachine.Guards` field type changed (record list -> DU list) | Source-breaking | All `StateMachine` record literals |
| `StateMachineMetadata.EvaluateEventGuards` new field | Binary-breaking | Internal (metadata is constructed by builder) |
| State key extraction (internal) | Behavioral change | State key strings change for parameterized DUs |

### Changes REMOVED (vs. revision 1)

| Change (removed) | Reason |
|-------------------|--------|
| `IStateMachineStore.GetState` return type -> `VersionedState` | Actor model eliminates need for versioning |
| `IStateMachineStore.SetState` signature -> with version param | Actor model eliminates need for versioning |
| `VersionedState<'S,'C>` new type | Not needed -- no version tokens |
| `SetStateResult` new DU | Not needed -- no version conflict |
| `TransitionAttemptResult.VersionConflict` new case | Not needed -- actor serialization prevents conflicts |
| `MailboxProcessorStore` version tracking | Not needed |
| `StateMachine.EventGuards` new field | Guard DU replaces separate field -- single `Guards` list |

### Version Strategy

The breaking changes are limited to:
- Guard type change from record to DU (source-breaking for guard definitions)
- Internal behavioral change to state key extraction (no API change)

Since Frank.Statecharts is pre-1.0 and the spec 004 implementation has not shipped a stable release, these breaking changes are acceptable.

---

## Open Questions

1. **State key for non-DU state types**: If `'State` is not a discriminated union (e.g., a string or record), `FSharpType.GetUnionCases` will fail. Should the key extraction fall back to `ToString()` for non-DU types? Or should the library require `'State` to be a DU? The spec focuses on DU cases, so a runtime check + fallback is reasonable.

2. **ETag interaction with state keys**: The `StatechartETagProvider` (spec 008) uses `string state` (which calls `ToString()`) for ETag computation (line 16 of `StatechartETagProvider.fs`). For ETags, the full state representation (including parameters) is correct -- different `Won "X"` and `Won "O"` should have different ETags. The ETag computation should NOT use the case-name key.

3. **Cross-process SQLite access**: The SQLite store wraps persistence in an actor within one process. If multiple application instances share a SQLite database file, they each have their own actor, and concurrent writes could conflict. This is a deployment concern, not a design concern. Document that SQLite stores should not be shared across processes -- use a proper database (PostgreSQL, etc.) for multi-process deployments.

4. **Guard evaluation when response has started**: If an event-validation guard blocks after the handler has already written to the response, the middleware cannot change the status code. The current approach (log a warning) is consistent with existing `TransitionAttemptResult.Blocked` handling. Deferred -- same trade-off as the existing design.

5. **Multi-targeting for Frank.Statecharts.Sqlite**: Should the SQLite package target `net8.0;net9.0;net10.0` like the core package? `Microsoft.Data.Sqlite` is available for all three. Recommendation: match the core package's target frameworks.

6. **Actor cache eviction for SQLite store**: The lazy rehydration model caches state in memory indefinitely. For applications with many instances accessed infrequently, this could consume memory. Consider an LRU eviction strategy in a future version. For V1, document that all accessed instances remain in memory.

7. **SQLite database file locking**: What happens when the SQLite database file is locked by another process? The `PRAGMA busy_timeout=5000` setting causes SQLite to retry for up to 5 seconds before returning SQLITE_BUSY. The store should catch this and surface a clear error rather than hanging indefinitely.
