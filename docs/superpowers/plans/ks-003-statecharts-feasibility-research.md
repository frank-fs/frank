---
source: kitty-specs/003-statecharts-feasibility-research
type: plan
---

# Implementation Plan: Frank.Statecharts Core Runtime Library

**Branch**: `003-statecharts-feasibility-research` | **Date**: 2026-03-06 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `kitty-specs/003-statecharts-feasibility-research/spec.md`
**Research**: [research.md](research.md) | **Data Model**: [data-model.md](data-model.md)

## Summary

Implement `Frank.Statecharts`, a core extension library that makes implicit resource state machines explicit. Provides a `statefulResource` computation expression that auto-generates allowed HTTP methods/responses per state, with guards mapping to HTTP status codes, `IStateMachineStore` abstraction with `MailboxProcessor` default, and transition event hooks for Provenance observability. This is the critical-path deliverable for the v7.3.0 milestone, unblocking #76 (Validation) and #77 (Provenance). CLI spec generation (WSD, ALPS, XState, SCXML, smcat) is deferred to #57.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core)
**Primary Dependencies**: Frank 7.x (project reference), Microsoft.AspNetCore.App (framework reference)
**Storage**: N/A (stateless library; `IStateMachineStore` abstraction with in-memory default)
**Testing**: Expecto + ASP.NET Core TestHost (matching existing Frank test patterns)
**Target Platform**: .NET multi-target library (net8.0;net9.0;net10.0)
**Project Type**: Library extension for Frank
**Performance Goals**: MailboxProcessor per-message overhead ~1-5us; no additional allocation in hot paths beyond state lookup
**Constraints**: No external NuGet dependencies beyond ASP.NET Core built-ins; must follow Frank's extension conventions
**Scale/Scope**: Simple-to-moderate statecharts (3-8 states); complex multi-entity coordination deferred

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Resource-Oriented Design -- PASS

The `statefulResource` CE extends the `resource` CE concept. State-dependent behavior is modeled as filtered affordances on resources, not as URL patterns. The `inState` blocks define which HTTP methods are available per state, maintaining the uniform interface semantics. Filtered affordances (omitting unavailable transitions) follow the Amundsen/REST approach.

### II. Idiomatic F# -- PASS

- `statefulResource` is a computation expression (CE pattern)
- `StateMachine<'State, 'Event, 'Context>` uses generic type parameters
- `TransitionResult` and `BlockReason` are discriminated unions
- `Guard` uses function types, not interfaces
- Pipeline-friendly: pure transition functions `'State -> 'Event -> 'Context -> TransitionResult`

### III. Library, Not Framework -- PASS

Frank.Statecharts is a library extension. It does not impose state machine usage on all resources. Users can use `statefulResource` for resources that need state-dependent behavior and plain `resource` for everything else. No opinions on view engines, data access, or authentication beyond guard context providing `ClaimsPrincipal`.

### IV. ASP.NET Core Native -- PASS

- Exposes `HttpContext` in state-specific handlers
- Uses ASP.NET Core middleware pipeline for state interception
- `IStateMachineStore` pluggable via DI (`IServiceCollection`)
- Endpoint metadata pattern (same as LinkedData, Auth, OpenApi)

### V. Performance Parity -- PASS

- MailboxProcessor per-message overhead: ~1-5us (negligible vs network latency)
- State lookup is one async operation per request
- No allocations in hot path beyond state retrieval
- Struct types used where appropriate (matching Frank core patterns)

### VI. Resource Disposal Discipline -- PASS

- `IStateMachineStore.Subscribe` returns `IDisposable` -- callers MUST use `use`
- `MailboxProcessorStore` implements `IDisposable` for cleanup
- No `StreamReader`/`StreamWriter` usage in core library

### VII. No Silent Exception Swallowing -- PASS

- Guard evaluation failures propagate (no catch-and-ignore)
- `TransitionResult.Invalid` is explicit, not swallowed
- Store operation failures propagate to caller

### VIII. No Duplicated Logic -- PASS

- Single `StateMachineMetadata` type shared between CE, middleware, and tests
- Guard evaluation logic in one place (middleware), not duplicated per handler
- `BlockReason` to HTTP status code mapping defined once

## Project Structure

### Documentation (this feature)

```
kitty-specs/003-statecharts-feasibility-research/
├── spec.md              # Research specification
├── plan.md              # This file
├── research.md          # Phase 0 research (complete)
├── data-model.md        # Entity model (complete)
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── research/
    ├── evidence-log.csv
    └── source-register.csv
```

### Source Code (repository root)

```
src/
├── Frank/                          # Existing core library
│   └── Builder.fs                  # ResourceBuilder, ResourceSpec, WebHostBuilder
│
├── Frank.Statecharts/              # NEW: Core statecharts library
│   ├── Frank.Statecharts.fsproj    # Multi-target: net8.0;net9.0;net10.0
│   ├── Types.fs                    # StateMachine, TransitionResult, BlockReason, Guard, StateInfo
│   ├── Store.fs                    # IStateMachineStore, MailboxProcessorStore
│   ├── StatefulResourceBuilder.fs  # statefulResource CE with inState blocks
│   ├── Middleware.fs               # State-aware request interception
│   ├── ResourceBuilderExtensions.fs # [<AutoOpen>] extensions for ResourceBuilder
│   └── WebHostBuilderExtensions.fs  # [<AutoOpen>] useStatecharts extension
│
└── Frank.Auth/                     # Existing (guard context uses ClaimsPrincipal)

test/
└── Frank.Statecharts.Tests/        # NEW: Test project
    ├── Frank.Statecharts.Tests.fsproj  # Target: net10.0
    ├── TypeTests.fs                # StateMachine, TransitionResult, Guard unit tests
    ├── StoreTests.fs               # MailboxProcessorStore tests
    ├── MiddlewareTests.fs          # State-aware routing integration tests
    ├── StatefulResourceTests.fs    # CE integration tests (TestHost)
    └── Program.fs                  # Expecto entry point
```

**Structure Decision**: Single library project + single test project. Follows the same pattern as Frank.LinkedData, Frank.Auth, Frank.OpenApi, and Frank.Datastar. No additional projects needed.

## Design Decisions

### DD-01: statefulResource CE Architecture

The `statefulResource` CE wraps `ResourceBuilder` rather than extending it. This is because state-specific handler registration (`inState` blocks) requires a different compilation model than the flat handler list in `ResourceSpec`. The CE:

1. Collects a `StateMachine<'S,'E,'C>` definition (initial state, transition function, guards)
2. Collects per-state handler registrations (`inState` blocks with HTTP method handlers)
3. At build time, generates one `Resource` with all handlers registered, plus `StateMachineMetadata` endpoint metadata
4. At runtime, middleware reads metadata and filters handlers based on current state

### DD-02: Middleware Interception Pattern

Following Frank.LinkedData's pattern:
1. `StateMachineMetadata` marker added to endpoint metadata during CE build
2. `useStatecharts` registers middleware that checks for this marker
3. If present: retrieve state from store -> check if HTTP method is allowed in current state -> evaluate guards -> invoke handler or return appropriate status code
4. If absent: pass through to next middleware (no overhead for non-stateful resources)

### DD-03: Guard Evaluation Order

Guards are evaluated in registration order. First `Blocked` result short-circuits. If all guards pass, the handler is invoked. This matches Frank.Auth's `AuthRequirement` evaluation pattern.

### DD-04: Transition Hooks for Provenance

`onTransition` fires an observable event after a successful state transition. The event contains:
- Previous state and context
- New state and context
- The event that triggered the transition
- Timestamp
- User identity (if available)

Frank.Provenance (#77) subscribes to this observable to generate PROV-O annotations.

### DD-05: State Instance Resolution

Each `statefulResource` instance is identified by a key derived from route parameters. For example, `/games/{gameId}` uses the `gameId` route value as the instance key. The CE provides a `resolveInstanceId` custom operation for configuring this.

## Complexity Tracking

No constitution violations requiring justification. The design adds one new library project following established patterns.

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| CE compilation complexity (nested `inState` blocks) | Medium | Prototype the CE first; fall back to builder pattern if CE limitations hit |
| MailboxProcessor lifecycle management (disposal, timeout) | Low | Follow tic-tac-toe's GameSupervisor pattern for instance lifecycle |
| Middleware ordering conflicts with LinkedData/Auth | Low | Document recommended middleware order; test all combinations |
| Generic type constraints on `'State` DU | Low | Require `'State : equality` for Map lookup; document constraint |
