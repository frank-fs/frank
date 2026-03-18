# Feature Specification: Frank.Statecharts Core Runtime Library

**Feature Branch**: `004-frank-statecharts`
**Created**: 2026-03-07
**Status**: Done
**GitHub Issue**: #87
**Research**: `kitty-specs/003-statecharts-feasibility-research/`
**Input**: Phase 2 of #80 (Semantic Metadata-Augmented Resources). Core runtime library unblocking #76 (Validation) and #77 (Provenance). CLI spec generation deferred to #57.

## User Scenarios & Testing

### User Story 1 - Define State-Dependent Resource Behavior (Priority: P1)

A Frank developer defines a `statefulResource` where different HTTP methods are available depending on the resource's current state. For example, a game resource allows POST (make move) during play states but only GET (view result) in terminal states.

**Why this priority**: This is the core value proposition -- making implicit resource state machines explicit with auto-generated method availability per state.

**Independent Test**: Define a simple 2-state machine (Locked/Unlocked turnstile), register different handlers per state, verify that requests to disallowed methods return 405 Method Not Allowed with correct `Allow` header.

**Acceptance Scenarios**:

1. **Given** a `statefulResource` with POST registered in `XTurn` state, **When** a POST request arrives while the resource is in `XTurn` state, **Then** the handler executes and returns 200.
2. **Given** a `statefulResource` with only GET registered in `Won` state, **When** a POST request arrives while the resource is in `Won` state, **Then** the middleware returns 405 with `Allow: GET` header.
3. **Given** a `statefulResource` with no prior state for an instance, **When** a request arrives, **Then** the middleware uses the machine's `Initial` state.

---

### User Story 2 - Guard-Based Access Control (Priority: P1)

A Frank developer attaches named guards to a state machine that evaluate `ClaimsPrincipal` to determine whether a transition is allowed. Guards map to specific HTTP status codes (403 Forbidden, 409 Conflict, etc.) providing meaningful error responses.

**Why this priority**: Guards are the mechanism for per-user discrimination (e.g., turn-based games) and are essential for any multi-user stateful resource.

**Independent Test**: Define guards that check claims, send requests with different principals, verify correct HTTP status codes (403 for NotAllowed, 409 for NotYourTurn).

**Acceptance Scenarios**:

1. **Given** a guard checking for "player=X" claim in `XTurn` state, **When** a user with "player=X" claim sends POST, **Then** the guard allows and the handler executes.
2. **Given** the same guard, **When** a user with "player=O" claim sends POST in `XTurn` state, **Then** the guard blocks with 409 Conflict.
3. **Given** a guard checking for authentication, **When** an unauthenticated user sends POST, **Then** the guard blocks with 403 Forbidden.
4. **Given** multiple guards, **When** evaluated, **Then** they execute in registration order and the first `Blocked` result short-circuits.

---

### User Story 3 - State Persistence and Transition (Priority: P1)

A Frank developer's stateful resource persists state across requests via `IStateMachineStore`. After a handler signals an event, the middleware applies the transition function and persists the new state.

**Why this priority**: Without persistence, state machines reset on every request. This is fundamental to multi-step workflows.

**Independent Test**: POST to create a game, POST a move (triggers transition from XTurn to OTurn), GET to verify state is now OTurn.

**Acceptance Scenarios**:

1. **Given** a new instance with no stored state, **When** state is requested, **Then** the store returns the machine's `Initial` state.
2. **Given** a handler that sets an event via `StateMachineContext.setEvent`, **When** the handler completes, **Then** the middleware applies the transition and persists the new state.
3. **Given** a successful transition, **When** a subsequent GET arrives, **Then** the store returns the updated state.

---

### User Story 4 - Transition Event Hooks for Observability (Priority: P2)

A Frank developer registers `onTransition` observers that fire after successful state transitions, receiving before/after state, the event, timestamp, and user identity. This enables Frank.Provenance (#77) to generate PROV-O annotations.

**Why this priority**: Hooks are the integration point for Provenance. Important but not blocking core functionality.

**Independent Test**: Register an `onTransition` callback, trigger a transition, verify the callback received correct `TransitionEvent` data.

**Acceptance Scenarios**:

1. **Given** an `onTransition` observer registered, **When** a transition succeeds, **Then** the observer receives a `TransitionEvent` with previous state, new state, event, and timestamp.
2. **Given** an `onTransition` observer, **When** a guard blocks a request, **Then** the observer does NOT fire.
3. **Given** multiple observers, **When** one throws an exception, **Then** the others still fire (error isolation).

---

### User Story 5 - WebHost Extension Integration (Priority: P2)

A Frank developer registers Frank.Statecharts via `useStatecharts` in the `webHost` CE, which sets up the middleware and DI services. The extension composes with existing Frank extensions (Auth, LinkedData).

**Why this priority**: Extension sugar for ergonomic setup. The core library works without it (manual middleware registration), but this follows established Frank conventions.

**Independent Test**: Use `useStatecharts` in a `webHost` CE, verify the middleware intercepts stateful resources and passes through non-stateful ones.

**Acceptance Scenarios**:

1. **Given** a `webHost` with `useStatecharts`, **When** a request hits a `statefulResource`, **Then** the middleware intercepts and applies state logic.
2. **Given** the same `webHost`, **When** a request hits a plain `resource`, **Then** the middleware passes through with zero overhead.
3. **Given** `useAuthentication` and `useStatecharts` in the same `webHost`, **Then** auth runs before statecharts middleware.

---

### User Story 6 - End-to-End Tic-Tac-Toe Validation (Priority: P2)

A simplified tic-tac-toe game validates the complete stack: state-dependent methods, guard evaluation (turn-based), transitions through multiple states, and transition hooks.

**Why this priority**: Integration validation ensures all components work together. Uses the tic-tac-toe prior art as the reference scenario.

**Independent Test**: Full game lifecycle via TestHost: create game, X moves, O moves, verify state transitions, verify guards block wrong player, verify Won state disallows moves.

**Acceptance Scenarios**:

1. **Given** a new game (XTurn), **When** player X POSTs a move, **Then** state transitions to OTurn.
2. **Given** OTurn state, **When** player X POSTs, **Then** 409 Conflict (not your turn).
3. **Given** Won state, **When** any player POSTs, **Then** 405 Method Not Allowed.
4. **Given** a GET handler in any state, **When** it queries available methods, **Then** it can discover which actions are valid for the current state.

---

### Edge Cases

- What happens when the store is disposed while requests are in flight? (`ObjectDisposedException`)
- How does the middleware handle a missing `resolveInstanceId`? (Default: first route value)
- What happens when a handler completes but does not set an event? (No transition -- read-only operation like GET)
- What happens when the transition function returns `Invalid`? (400 Bad Request with message)
- What happens with concurrent requests to the same instance? (MailboxProcessor serializes access)

## Requirements

### Functional Requirements

- **FR-001**: System MUST provide a `statefulResource` computation expression that registers state-specific HTTP method handlers via `inState` blocks
- **FR-002**: System MUST provide a `StateMachine<'State, 'Event, 'Context>` record type with DU-based states, pure transition functions, and named guards
- **FR-003**: System MUST evaluate guards in registration order, short-circuiting on first `Blocked` result
- **FR-004**: System MUST map `BlockReason` variants to HTTP status codes: `NotAllowed`->403, `NotYourTurn`->409, `InvalidTransition`->400, `PreconditionFailed`->412, `Custom(code,msg)`->code
- **FR-005**: System MUST return 405 Method Not Allowed with `Allow` header when an HTTP method is not registered for the current state
- **FR-006**: System MUST provide `IStateMachineStore<'S,'C>` abstraction with `GetState`, `SetState`, and `Subscribe` operations
- **FR-007**: System MUST provide `MailboxProcessorStore` as the default in-memory `IStateMachineStore` implementation with serialized access
- **FR-008**: System MUST support `onTransition` observable hooks that fire after successful state transitions (not before, not on blocked requests)
- **FR-009**: System MUST add `StateMachineMetadata` to endpoint metadata following the marker metadata + middleware interception pattern
- **FR-010**: System MUST pass through non-stateful resources with zero overhead (null check on metadata, not exception)
- **FR-011**: System MUST provide `useStatecharts` custom operation on `WebHostBuilder` for middleware and DI registration
- **FR-012**: System MUST implement `IDisposable` on `MailboxProcessorStore` with proper cleanup (drain pending operations, notify subscribers with `OnCompleted`)
- **FR-013**: System MUST provide BehaviorSubject semantics on store subscriptions (new subscribers immediately receive current state)
- **FR-014**: Handlers MUST be able to discover per-state allowed methods at runtime via `StateMachineMetadata` for affordance generation

### Key Entities

- **StateMachine<'S,'E,'C>**: Compile-time definition with initial state, transition function, guards, and state metadata
- **TransitionResult<'S,'C>**: Outcome DU -- `Transitioned`, `Blocked`, or `Invalid`
- **BlockReason**: Why a guard blocked -- maps to HTTP status codes (`[<Struct>]`)
- **Guard<'S,'E,'C>**: Named predicate evaluating `GuardContext` to `GuardResult`
- **IStateMachineStore<'S,'C>**: Persistence abstraction with `GetState`/`SetState`/`Subscribe`
- **StateMachineMetadata**: Endpoint metadata marker read by middleware
- **TransitionEvent<'S,'E,'C>**: Observable event with before/after state, event, timestamp, user

## Success Criteria

### Measurable Outcomes

- **SC-001**: All core types compile under multi-target (net8.0/net9.0/net10.0) with `dotnet build`
- **SC-002**: A simplified tic-tac-toe game works end-to-end via TestHost: state-dependent methods, guard evaluation, transitions, and hooks all function correctly
- **SC-003**: Non-stateful resources experience zero measurable overhead from the middleware (null metadata check only)
- **SC-004**: `MailboxProcessorStore` handles 100+ concurrent operations without deadlocks or data races
- **SC-005**: All public API types follow Frank conventions: `[<Struct>]` where appropriate, `'State : equality` constraint, `IDisposable` on store and subscriptions
- **SC-006**: Extension pattern matches Frank.Auth exactly: `[<AutoOpen>] module` + `[<CustomOperation>]` on `WebHostBuilder` and `ResourceBuilder`
