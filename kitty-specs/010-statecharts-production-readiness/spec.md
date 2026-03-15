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

### User Story 2 - Optimistic Concurrency for State Changes (Priority: P1)

A Frank developer's stateful resource correctly handles concurrent requests from multiple users. When two requests simultaneously read the same state, both pass guards, and both attempt transitions, only one succeeds. The second request receives a conflict response rather than silently overwriting the first request's state change.

**Why this priority**: This is a correctness bug in any multi-user deployment. Without concurrency control, lost updates corrupt state. The current in-memory store serializes access within a single process, but the store interface itself has no compare-and-swap semantics, meaning any durable store implementation would be vulnerable to lost updates.

**Independent Test**: Create a stateful resource. Send two concurrent POST requests that both trigger transitions from the same state. Verify that exactly one succeeds with a state transition and the other either retries successfully or returns a conflict response (409). Verify that the final persisted state is consistent (no lost updates).

**Acceptance Scenarios**:

1. **Given** a stateful resource in state A with two simultaneous requests that both read state A, **When** both attempt to transition to state B, **Then** the first to persist succeeds and the second detects the conflict.
2. **Given** a conflict is detected during state persistence, **When** the middleware handles the conflict, **Then** it returns 409 Conflict to the client (unless retry is configured).
3. **Given** a store that supports versioning, **When** `SetState` is called with a stale version, **Then** the store rejects the update and signals a version mismatch.
4. **Given** a store that supports versioning, **When** `SetState` is called with the current version, **Then** the update succeeds and the version is incremented.
5. **Given** the in-memory store, **When** concurrency control is added, **Then** existing single-request flows continue to work without requiring callers to manage versions explicitly.

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

A Frank developer deploys a stateful resource to production and needs state to survive application restarts. The developer configures a SQLite-backed store instead of the default in-memory store, and state machines resume from their persisted state after restart. The SQLite store implements the same `IStateMachineStore` interface (including concurrency control from Story 2) and can be registered via dependency injection.

**Why this priority**: The in-memory store is only suitable for development and testing. A durable store is necessary for any production deployment. SQLite provides a low-dependency, file-based option that validates the store interface design without requiring external infrastructure. However, this depends on the interface changes from Story 2 (concurrency control).

**Independent Test**: Configure a stateful resource with SQLite persistence. Create an instance, trigger state transitions, stop the application, restart, and verify the state is preserved. Also verify that concurrent access from multiple requests uses optimistic concurrency correctly.

**Acceptance Scenarios**:

1. **Given** a stateful resource configured with SQLite persistence, **When** a state transition occurs, **Then** the new state is persisted to the SQLite database.
2. **Given** a persisted state in SQLite, **When** the application restarts and a request arrives for the same instance, **Then** the store returns the previously persisted state (not the initial state).
3. **Given** two concurrent requests to the same instance with SQLite persistence, **When** both attempt state transitions, **Then** optimistic concurrency is enforced (one succeeds, one receives a conflict).
4. **Given** a SQLite store, **When** the store is registered via dependency injection, **Then** it replaces the default in-memory store transparently (same interface, no handler changes needed).
5. **Given** a SQLite store with subscriptions, **When** a state change is persisted, **Then** subscribers are notified of the new state (same observable semantics as the in-memory store).
6. **Given** a new application with no existing SQLite database, **When** the store is initialized, **Then** it creates the necessary schema automatically (no manual migration step).

---

### Edge Cases

- What happens when the SQLite database file is locked by another process? (Store should surface a clear error, not hang indefinitely.)
- What happens when a parameterized state has a `ToString()` override that collides with another case's key? (The new key extraction mechanism should be immune to custom `ToString()` implementations.)
- What happens when a version conflict occurs but the response has already started streaming? (Log a warning, same as existing `TransitionAttemptResult.Blocked` handling.)
- How does the SQLite store handle serialization of arbitrary `'State` and `'Context` types? (Likely JSON serialization with a configurable serializer.)
- What happens when a guard throws an exception (as opposed to returning `Blocked`)? (Propagate as 500, same as current behavior.)
- What happens when the SQLite store's `Subscribe` is called but the database has no entry for the instance? (Return no initial value, same as in-memory behavior.)
- How does the middleware distinguish between "version conflict, please retry" and "guard blocked, do not retry"? (Different HTTP status codes: 409 for conflict, 403/412 for guard blocks.)

## Requirements

### Functional Requirements

- **FR-001**: System MUST provide a state-key extraction mechanism that maps parameterized DU cases to a single key regardless of parameter values (e.g., all `Won _` variants share one key)
- **FR-002**: System MUST preserve backward compatibility for simple (non-parameterized) DU cases -- existing `inState` registrations must work without modification
- **FR-003**: System MUST add a version or concurrency token to the store interface so that `SetState` can detect and reject lost updates
- **FR-004**: System MUST return 409 Conflict when a state transition fails due to a version mismatch (concurrent modification)
- **FR-005**: System MUST separate guard evaluation into two phases: access-control guards (pre-handler, no event context) and event-validation guards (post-handler, with event context)
- **FR-006**: System MUST not evaluate event-validation guards when no event is set by the handler (read-only operations)
- **FR-007**: System MUST provide a SQLite-backed `IStateMachineStore` implementation that persists state durably across application restarts
- **FR-008**: System MUST auto-create the SQLite schema on first use (no manual migration step required)
- **FR-009**: System MUST enforce optimistic concurrency in the SQLite store implementation
- **FR-010**: System MUST support the `Subscribe` (observable) interface on the SQLite store with the same behavioral semantics as the in-memory store
- **FR-011**: System MUST allow the SQLite store to be registered via dependency injection as a drop-in replacement for the in-memory store
- **FR-012**: System MUST preserve backward compatibility for existing guards that only inspect `User` and `CurrentState` (no code changes required for existing guard predicates)
- **FR-013**: System MUST handle the case where `ToString()` has been overridden on a state type without producing incorrect key collisions

### Key Entities

- **State Key**: The identifier derived from a DU state value used to look up handlers. Currently `state.ToString()`; will be replaced with a mechanism that groups parameterized cases by DU case name.
- **Store Version**: A concurrency token (integer or opaque string) associated with each persisted state instance, incremented on every successful `SetState`. Used for optimistic concurrency.
- **Access-Control Guard**: A guard predicate that evaluates before the handler runs, using only `User`, `CurrentState`, and `Context` (no event). Used for authentication and authorization checks.
- **Event-Validation Guard**: A guard predicate that evaluates after the handler sets an event, using the full `GuardContext` including the actual event. Used for transition-specific validation.
- **SQLite Store**: A durable `IStateMachineStore` implementation backed by a SQLite database file, supporting versioned state persistence, optimistic concurrency, and observable subscriptions.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Developers can register a single handler for a parameterized DU case and have it match all parameter values without additional registrations
- **SC-002**: Two concurrent requests to the same stateful resource instance never produce a lost update -- exactly one succeeds and the other receives a conflict response
- **SC-003**: Existing stateful resource definitions (from spec 004) compile and pass all tests without modification after the changes (backward compatibility)
- **SC-004**: Guards that check only user identity and current state continue to function identically without code changes
- **SC-005**: State persisted via the SQLite store survives application restart and is correctly restored on the next request
- **SC-006**: The SQLite store handles concurrent access from multiple requests without data corruption
- **SC-007**: The SQLite store can be swapped in for the in-memory store with a single DI registration change and no handler modifications
- **SC-008**: All changes compile and pass tests across all supported target frameworks without regressions

## Assumptions

- The state-key extraction mechanism will use F# reflection to extract the DU case name (tag) at registration time, avoiding runtime `ToString()` calls. The exact API (e.g., a `stateKey` function parameter vs. automatic reflection) will be determined during design.
- SQLite serialization of `'State` and `'Context` types will use JSON. The serializer will be configurable but default to `System.Text.Json`.
- The concurrency token will be an integer version number, not an opaque ETag string. The middleware will not expose this as an HTTP ETag header (that concern belongs to spec 008, conditional requests).
- The two-phase guard model will be opt-in: guards default to access-control (pre-handler) phase. Developers explicitly mark guards as event-validation guards. This ensures backward compatibility.
- The SQLite store will live in a separate project/package (`Frank.Statecharts.Sqlite` or similar) to avoid adding a SQLite dependency to the core library.
