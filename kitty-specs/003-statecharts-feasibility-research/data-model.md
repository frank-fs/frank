# Data Model: Frank.Statecharts

**Feature**: 003-statecharts-feasibility-research
**Date**: 2026-03-06

## Entity Relationship Overview

```
StateMachine<'S,'E,'C> ──1:N──> StateInfo
       │                            │
       │ 1:N                        │ 1:N
       v                            v
  Guard<'S,'E,'C>            AllowedMethod (HTTP verb)
       │
       │ evaluates to
       v
  GuardResult ──> BlockReason ──> HTTP Status Code

StateMachineInstance
       │
       │ persisted by
       v
IStateMachineStore<'S,'C>
       │
       │ default impl
       v
MailboxProcessorStore<'S,'C>

statefulResource CE
       │
       │ wraps
       v
ResourceBuilder (existing Frank CE)
       │
       │ produces
       v
Resource { Endpoints }
       │
       │ with metadata
       v
StateMachineMetadata ──> used by middleware
```

## Core Entities

### StateMachine<'State, 'Event, 'Context>

The compile-time definition of a state machine. Generic over three type parameters.

| Attribute | Type | Description |
|-----------|------|-------------|
| Initial | 'State | The starting state |
| Transition | 'State -> 'Event -> 'Context -> TransitionResult | Pure transition function |
| Guards | Guard list | Named guard predicates |
| StateMetadata | Map<'State, StateInfo> | Per-state HTTP configuration |

**Identity**: Singleton per `statefulResource` CE instance. Identified by resource route template.
**Lifecycle**: Created at application startup, immutable thereafter.

### StateInfo

Metadata about a single state, used for affordance generation and HTTP method filtering.

| Attribute | Type | Description |
|-----------|------|-------------|
| AllowedMethods | string list | HTTP methods available in this state |
| IsFinal | bool | Terminal state (no outgoing transitions) |
| Description | string option | Human-readable state description |

**Derived from**: The `inState` blocks in the `statefulResource` CE. Each `inState` block registers handlers for specific HTTP methods, which populates `AllowedMethods`.

### Guard<'State, 'Event, 'Context>

A named predicate that evaluates whether a transition is allowed.

| Attribute | Type | Description |
|-----------|------|-------------|
| Name | string | Guard identifier (used in spec generation) |
| Predicate | GuardContext -> GuardResult | Evaluation function |

**Key relationship**: Guards reference `ClaimsPrincipal` for per-user discrimination. This is the bridge between Frank.Auth and Frank.Statecharts.

### TransitionResult<'State, 'Context>

The outcome of a transition attempt.

| Variant | Fields | HTTP Mapping |
|---------|--------|-------------|
| Transitioned | state, context | 200/202 (handler determines) |
| Blocked | BlockReason | See BlockReason mapping |
| Invalid | message | 400 Bad Request |

### BlockReason

Why a guard blocked a transition.

| Variant | HTTP Status | Use Case |
|---------|-------------|----------|
| NotAllowed | 403 Forbidden | User lacks permission |
| NotYourTurn | 409 Conflict | Turn-based guard failure |
| InvalidTransition | 400 Bad Request | Transition not defined for current state |
| PreconditionFailed | 412 | State precondition not met |
| Custom | configurable | Domain-specific |

### StateMachineInstance

A runtime instance of a state machine with current state and context.

| Attribute | Type | Description |
|-----------|------|-------------|
| InstanceId | string | Unique identifier (typically from route parameter) |
| CurrentState | 'State | Current state |
| Context | 'Context | Current extended state |

**Persisted by**: `IStateMachineStore<'State, 'Context>`
**Observable**: Subscribers notified on state changes (for Provenance)

### IStateMachineStore<'State, 'Context>

Abstraction for state machine instance persistence.

| Method | Signature | Description |
|--------|-----------|-------------|
| GetState | instanceId -> Task<('State * 'Context) option> | Retrieve current state |
| SetState | instanceId -> state -> context -> Task<unit> | Persist state change |
| Subscribe | instanceId -> IObserver -> IDisposable | Observe state changes |

**Default implementation**: `MailboxProcessorStore` -- in-memory, single-node, serialized access via F# MailboxProcessor.

### StateMachineMetadata

Endpoint metadata marker (like `LinkedDataMarker` in Frank.LinkedData).

| Attribute | Type | Description |
|-----------|------|-------------|
| Machine | obj (boxed StateMachine) | The state machine definition |
| StoreFactory | IServiceProvider -> obj | Factory for creating store instances |

**Used by**: Statecharts middleware to intercept requests and apply state-aware routing.

## Relationships

1. **StateMachine -> StateInfo**: One machine defines metadata for each of its states (1:N). The set of states is determined by the 'State DU cases.

2. **StateMachine -> Guard**: One machine has zero or more guards (1:N). Guards are evaluated in order; first `Blocked` result short-circuits.

3. **StateMachineInstance -> IStateMachineStore**: Each instance is persisted by exactly one store (N:1 -- multiple instances share a store).

4. **statefulResource -> ResourceBuilder**: The `statefulResource` CE wraps the existing `resource` CE, adding statechart-specific custom operations while delegating HTTP handler registration to the existing builder.

5. **StateMachineMetadata -> Middleware**: The middleware reads `StateMachineMetadata` from endpoint metadata (same pattern as LinkedData reads `LinkedDataMarker`) to determine if state-aware routing applies.

6. **Guard -> Frank.Auth**: Guards can access `ClaimsPrincipal` via `GuardContext.User`. This enables per-user discrimination without coupling to Frank.Auth's `AuthRequirement` type.

7. **onTransition -> Frank.Provenance**: The transition event hook is an `IObservable<TransitionEvent>` that Frank.Provenance (#77) subscribes to for PROV-O annotation generation.

## Spec Format Entities

### Spec Projection Model

Each spec format projects a subset of the runtime model:

| Runtime Entity | WSD | ALPS | XState | SCXML | smcat |
|---------------|-----|------|--------|-------|-------|
| State | participant | semantic descriptor | state | state | node |
| Transition | arrow | safe/unsafe/idempotent | event.target | transition | edge |
| Guard | note annotation | ext element | guard (named) | cond (expression) | bracket label |
| Context | (not represented) | (not represented) | context | datamodel | (not represented) |
| HTTP Method | arrow type | descriptor type | (not represented) | (not represented) | (not represented) |
| Parameters | message params | nested descriptors | (in context) | (in datamodel) | (not represented) |
