# Feature Specification: Statecharts Production Readiness

**Feature Branch**: `010-statecharts-production-readiness`
**Created**: 2026-03-15
**Status**: Draft
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

A Frank developer defines guards that need to inspect the incoming event to make authorization decisions. Currently, guards run before the handler sets the event, so they receive a default/null event value. The developer needs a way to write guards that can evaluate both access-control concerns (pre-handler, no event needed) and event-specific validation (post-event, event available).

**Why this priority**: This is an API correctness issue that affects guard expressiveness, but most common guards (authentication, turn checking) only need user identity and current state, which are already available. Event-specific guards are a secondary use case. However, the current behavior of passing a default/null event value is a footgun that could cause runtime errors in guards that naively try to use the event.

**Independent Test**: Define a guard that inspects the event value. Trigger a request that sets an event. Verify the guard can see the actual event (not a default value). Separately, verify that access-control guards (which only check `User` and `CurrentState`) continue to work in the pre-handler phase.

**Acceptance Scenarios**:

1. **Given** a guard that checks only `User` and `CurrentState` (access-control guard), **When** the guard is evaluated before the handler runs, **Then** it correctly allows or blocks based on claims and state, without needing event context.
2. **Given** a guard that needs to inspect the event for validation, **When** the guard is evaluated after the handler sets the event, **Then** it receives the actual event value (not a default/null).
3. **Given** a stateful resource with both access-control guards and event-validation guards, **When** a request arrives, **Then** access-control guards run first (pre-handler) and event-validation guards run after the handler sets the event.
4. **Given** a guard that inspects the event, **When** no event is set by the handler (e.g., a GET request), **Then** the event-validation guard is not evaluated (it is skipped for read-only operations).
5. **Given** the current guard API, **When** upgrading to the new two-phase model, **Then** existing guards that only check `User` and `CurrentState` continue to work without modification (backward compatible).

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
- **FR-005**: System MUST separate guard evaluation into two phases: access-control guards (pre-handler, no event context) and event-validation guards (post-handler, with event context)
- **FR-006**: System MUST not evaluate event-validation guards when no event is set by the handler (read-only operations)
- **FR-007**: System MUST provide a SQLite-backed `IStateMachineStore` implementation that persists state durably across application restarts
- **FR-008**: System MUST auto-create the SQLite schema on first use (no manual migration step required)
- **FR-009**: System MUST serialize all SQLite store access through an actor, eliminating the need for database-level concurrency control
- **FR-010**: System MUST support the `Subscribe` (observable) interface on the SQLite store with the same behavioral semantics as the in-memory store
- **FR-011**: System MUST allow the SQLite store to be registered via dependency injection as a drop-in replacement for the in-memory store
- **FR-012**: System MUST preserve backward compatibility for existing guards that only inspect `User` and `CurrentState` (no code changes required for existing guard predicates)
- **FR-013**: System MUST handle the case where `ToString()` has been overridden on a state type without producing incorrect key collisions

### Key Entities

- **State Key**: The identifier derived from a DU state value used to look up handlers. Currently `state.ToString()`; will be replaced with a mechanism that groups parameterized cases by DU case name.
- **State Actor**: An actor (e.g., `MailboxProcessor`) that serializes all state reads and writes for a given store instance. The actor is the concurrency mechanism — no version tokens or compare-and-swap needed. How the actor persists state (SQLite, Akka.Persistence, event sourcing, etc.) is an internal implementation detail, not a public interface.
- **Access-Control Guard**: A guard predicate that evaluates before the handler runs, using only `User`, `CurrentState`, and `Context` (no event). Used for authentication and authorization checks.
- **Event-Validation Guard**: A guard predicate that evaluates after the handler sets an event, using the full `GuardContext` including the actual event. Used for transition-specific validation.
- **SQLite Store**: A durable `IStateMachineStore` implementation backed by an actor wrapping a SQLite database file, supporting persistent state and observable subscriptions.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Developers can register a single handler for a parameterized DU case and have it match all parameter values without additional registrations
- **SC-002**: Two concurrent requests to the same stateful resource instance are processed sequentially by the actor -- no lost updates occur
- **SC-003**: Existing stateful resource definitions (from spec 004) compile and pass all tests without modification after the changes (backward compatibility)
- **SC-004**: Guards that check only user identity and current state continue to function identically without code changes
- **SC-005**: State persisted via the SQLite store survives application restart and is correctly restored on the next request
- **SC-006**: The SQLite store serializes all access through its actor -- no concurrent database operations occur
- **SC-007**: The SQLite store can be swapped in for the in-memory store with a single DI registration change and no handler modifications
- **SC-008**: All changes compile and pass tests across all supported target frameworks without regressions

## Assumptions

- The state-key extraction mechanism will use F# reflection to extract the DU case name (tag) at registration time, avoiding runtime `ToString()` calls. The exact API (e.g., a `stateKey` function parameter vs. automatic reflection) will be determined during design.
- SQLite serialization of `'State` and `'Context` types will use JSON. The serializer will be configurable but default to `System.Text.Json`.
- Concurrency is handled by actor serialization, not version tokens. The `IStateMachineStore` contract assumes all implementations serialize access through an actor. No compare-and-swap or optimistic concurrency tokens are needed at the interface level.
- Durable stores are actor implementations that happen to persist state. There is no public persistence interface — how an actor persists (SQLite, Akka.Persistence, event sourcing, etc.) is an internal implementation detail.
- The two-phase guard model will be opt-in: guards default to access-control (pre-handler) phase. Developers explicitly mark guards as event-validation guards. This ensures backward compatibility.
- The SQLite store will live in a separate project/package (`Frank.Statecharts.Sqlite` or similar) to avoid adding a SQLite dependency to the core library.
