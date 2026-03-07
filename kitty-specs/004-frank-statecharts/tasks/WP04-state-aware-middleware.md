---
work_package_id: WP04
title: State-Aware Middleware
lane: "doing"
dependencies: [WP01, WP02, WP03]
base_branch: 004-frank-statecharts-WP04-merge-base
base_commit: 41d1bd76a6a81248e13e63ebf9f7f335ea2fcd1a
created_at: '2026-03-07T16:45:02.064454+00:00'
subtasks:
- T016
- T017
- T018
- T019
- T020
- T021
phase: Phase 2 - Core Pipeline
assignee: ''
agent: "claude-opus"
shell_pid: "55845"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-06T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-003, FR-004, FR-005, FR-008, FR-010, FR-014]
---

# Work Package Prompt: WP04 -- State-Aware Middleware

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP04 --base WP03
```

Depends on WP01 (types), WP02 (store), WP03 (CE produces metadata). Use `--base WP03` since WP03 is the last dependency in the chain (WP02 and WP03 both depend on WP01, and WP04 needs all three).

**Cross-WP dependency**: This WP consumes `StateMachineMetadata` as defined by WP03. See WP03's "Cross-WP Contract" section for the required record shape. The middleware accesses all machine behavior through closures stored in the metadata — no generic type parameters are needed at the middleware level. This WP will extend `StateMachineMetadata` with additional closure fields (`GetCurrentStateKey`, `SetStateAfterTransition`, `EvaluateGuards`, `TryGetEventAndTransition`) that are wired up during WP03's `Build` method.

---

## Objectives & Success Criteria

- Implement middleware that intercepts requests to stateful resources
- Retrieve current state from `IStateMachineStore` using resolved instance ID
- Check if HTTP method is allowed in current state, return 405 if not
- Evaluate guards in order, map `BlockReason` to HTTP status codes
- Execute handler on success, apply transition, fire `onTransition` hooks
- Return correct HTTP status codes: 200 (success), 405 (wrong method), 403/409/400/412 (guard blocked)
- Pass-through for non-stateful resources (no overhead)

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/003-statecharts-feasibility-research/plan.md` -- DD-02 (middleware pattern), DD-03 (guard evaluation order)
- `kitty-specs/003-statecharts-feasibility-research/data-model.md` -- `BlockReason` -> HTTP status code mapping

**Reference code**:
- `src/Frank.LinkedData/` -- `LinkedDataMarker` metadata + middleware interception pattern
- `src/Frank/Builder.fs:223-265` -- `WebHostSpec` with `BeforeRoutingMiddleware` and `Middleware` fields

**Key constraints (DD-02)**:
1. Check endpoint metadata for `StateMachineMetadata` marker
2. If present: retrieve state -> check method -> evaluate guards -> invoke handler -> transition -> hook
3. If absent: pass through (zero overhead for non-stateful resources)

**Guard evaluation (DD-03)**:
- Evaluate in registration order
- First `Blocked` result short-circuits
- All guards pass -> invoke handler

**BlockReason to HTTP status code mapping**:
- `NotAllowed` -> 403 Forbidden
- `NotYourTurn` -> 409 Conflict
- `InvalidTransition` -> 400 Bad Request
- `PreconditionFailed` -> 412 Precondition Failed
- `Custom(code, msg)` -> `code` with `msg` in response body

---

## Subtasks & Detailed Guidance

### Subtask T016 -- Create `Middleware.fs` with state-aware request interception

**Purpose**: Core middleware that checks for `StateMachineMetadata` and orchestrates the state-aware pipeline.

**Steps**:
1. Create `src/Frank.Statecharts/Middleware.fs`
2. Add to `.fsproj` `<Compile>` list after `StatefulResourceBuilder.fs`
3. Implement as an ASP.NET Core middleware class:

```fsharp
namespace Frank.Statecharts

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

type StateMachineMiddleware(next: RequestDelegate) =

    member _.InvokeAsync(ctx: HttpContext) : Task =
        let endpoint = ctx.GetEndpoint()
        match endpoint with
        | null -> next.Invoke(ctx)
        | ep ->
            let metadata = ep.Metadata.GetMetadata<StateMachineMetadata>()
            match metadata with
            | null -> next.Invoke(ctx)  // Not a stateful resource -- pass through
            | meta -> StateMachineMiddleware.HandleStateful(ctx, meta, next)

    static member private HandleStateful(ctx: HttpContext, meta: StateMachineMetadata, next: RequestDelegate) : Task =
        task {
            // 1. Resolve instance ID
            let instanceId = meta.ResolveInstanceId ctx
            let httpMethod = ctx.Request.Method

            // 2. Look up current state from store
            // (T017 implements this)

            // 3. Check if HTTP method is allowed in current state
            // (T018 implements this)

            // 4. Evaluate guards
            // (T019 implements this)

            // 5. Invoke handler, apply transition, fire hooks
            // (T020 implements this)
        } :> Task
```

**Files**: `src/Frank.Statecharts/Middleware.fs`
**Notes**:
- The middleware checks `ep.Metadata.GetMetadata<StateMachineMetadata>()` which returns `null` for non-stateful endpoints
- This is the exact same pattern as `Frank.LinkedData` (check for marker metadata, intercept or pass through)
- The middleware must run **after routing but before endpoint execution** (registered via `plug` not `plugBeforeRouting`)

**FR-014 Implementation**: The middleware exposes per-state allowed methods through `StateMachineMetadata` so that GET handlers (or any handler) can discover which HTTP methods are valid in the current state. This is inherent in `StateMachineMetadata.StateHandlerMap` — handlers access it via `HttpContext.GetEndpoint().Metadata.GetMetadata<StateMachineMetadata>()`. WP03's `Build` method populates this map; WP04's middleware uses it for 405 filtering; handlers read it directly for affordance generation.

### Subtask T017 -- Implement state lookup from store

**Purpose**: Retrieve current state from `IStateMachineStore` using the resolved instance ID.

**Steps**:
1. Resolve `IStateMachineStore` from DI within `HandleStateful`:

```fsharp
// The store type is determined by the machine's generic parameters
// Since metadata is boxed, we need a way to resolve the typed store
// Strategy: Store a factory function in metadata that creates the typed store lookup

// In StateMachineMetadata, add:
// StoreResolver: IServiceProvider -> instanceId:string -> Task<(string * string) option>
// where the tuple is (stateKey, contextJson) -- or use typed approach below

// Better approach: The middleware doesn't need to know the type parameters.
// Store a GetCurrentStateKey function in metadata:
```

2. **Practical approach**: Since middleware is untyped (works with `obj`), add helper functions to `StateMachineMetadata`:

```fsharp
// Add to StateMachineMetadata:
type StateMachineMetadata =
    { Machine: obj
      StateHandlerMap: Map<string, (string * RequestDelegate) list>
      ResolveInstanceId: HttpContext -> string
      TransitionObservers: (obj -> unit) list
      GetCurrentStateKey: IServiceProvider -> string -> Task<string option>
      // Returns state.ToString() for handler map lookup
      SetStateAfterTransition: IServiceProvider -> string -> obj -> Task<unit>
      // Accepts boxed (state, context) tuple
      InitialStateKey: string }
```

3. Use this in middleware:
```fsharp
let! stateKeyOpt = meta.GetCurrentStateKey ctx.RequestServices instanceId
let stateKey = stateKeyOpt |> Option.defaultValue meta.InitialStateKey
```

**Files**: `src/Frank.Statecharts/Middleware.fs`, `src/Frank.Statecharts/StatefulResourceBuilder.fs` (update metadata type)
**Notes**:
- State lookup must handle missing instance (new resource): default to `StateMachine.Initial`
- The `GetCurrentStateKey` function is wired up during `Build` in WP03 to close over the typed store

### Subtask T018 -- Implement method filtering (405)

**Purpose**: Check if the HTTP method is allowed in the current state; return 405 Method Not Allowed if not.

**Steps**:
```fsharp
// After state lookup:
let stateKey = ... // from T017
match Map.tryFind stateKey meta.StateHandlerMap with
| None ->
    // State has no handlers -- 405
    ctx.Response.StatusCode <- 405
| Some handlers ->
    let allowedMethods = handlers |> List.map fst |> List.distinct
    if allowedMethods |> List.contains httpMethod then
        // Method is allowed -- proceed to guard evaluation
        ...
    else
        // Method not allowed for this state
        ctx.Response.StatusCode <- 405
        ctx.Response.Headers["Allow"] <- String.Join(", ", allowedMethods)
```

**Files**: `src/Frank.Statecharts/Middleware.fs`
**Notes**:
- Set `Allow` header in 405 response per HTTP spec (RFC 9110)
- If the state itself is unknown (not in handler map), also return 405

### Subtask T019 -- Implement guard evaluation with HTTP status code mapping

**Purpose**: Evaluate guards in order, short-circuit on first `Blocked`, and map `BlockReason` to HTTP status codes.

**Steps**:
1. Extract the machine's guards and evaluate them:

```fsharp
// Guards are stored in the boxed machine -- need typed access
// Add to StateMachineMetadata:
// EvaluateGuards: HttpContext -> string -> GuardResult
// Typed guard evaluation closed over at Build time

let guardResult = meta.EvaluateGuards ctx stateKey
match guardResult with
| GuardResult.Allowed -> // proceed to handler
| GuardResult.Blocked reason ->
    match reason with
    | NotAllowed ->
        ctx.Response.StatusCode <- 403
    | NotYourTurn ->
        ctx.Response.StatusCode <- 409
    | InvalidTransition ->
        ctx.Response.StatusCode <- 400
    | PreconditionFailed ->
        ctx.Response.StatusCode <- 412
    | Custom(code, message) ->
        ctx.Response.StatusCode <- code
        do! ctx.Response.WriteAsync(message)
```

2. Add a helper function for `BlockReason` -> status code mapping in `Types.fs` or `Middleware.fs`:

```fsharp
module BlockReasonMapping =
    let toStatusCode (reason: BlockReason) =
        match reason with
        | NotAllowed -> 403
        | NotYourTurn -> 409
        | InvalidTransition -> 400
        | PreconditionFailed -> 412
        | Custom(code, _) -> code

    let toMessage (reason: BlockReason) =
        match reason with
        | Custom(_, message) -> Some message
        | _ -> None
```

**Files**: `src/Frank.Statecharts/Middleware.fs`
**Notes**:
- Guard evaluation order: registered order, first `Blocked` short-circuits (DD-03)
- `GuardContext` requires `ClaimsPrincipal` from `HttpContext.User`
- The `EvaluateGuards` function must be created at `Build` time to close over typed machine

**Distinction — BlockReason.InvalidTransition vs TransitionResult.Invalid**:
- `BlockReason.InvalidTransition` → returned by a **guard** that determines the requested event is structurally invalid for the current state (e.g., wrong event type). Maps to 400.
- `TransitionResult.Invalid` → returned by the **transition function** when it cannot produce a valid next state (e.g., illegal move in tic-tac-toe). Also maps to 400 but with the message from the `Invalid` case.
- Guards run first. If all guards pass, the handler executes, then the transition function runs. Either can produce a 400, but from different evaluation phases.

### Subtask T020 -- Implement transition execution and hooks

**Purpose**: After successful handler execution, apply the state transition and fire `onTransition` hooks.

**Steps**:
1. After guards pass, invoke the handler:

```fsharp
// Find the correct handler for this state + method
let handler =
    handlers |> List.find (fun (method, _) -> method = httpMethod) |> snd
do! handler.Invoke(ctx)

// After handler succeeds, apply transition
// The transition function needs the event -- how does the handler communicate the event?
```

2. **Event communication pattern**: The handler needs to signal what event occurred. Options:
   - **Option A**: Store the event in `HttpContext.Items` (simple, convention-based)
   - **Option B**: Wrap the handler to capture the return value

   Recommended: **Option A** with a helper function:

```fsharp
// In Types.fs or StatefulResourceBuilder.fs:
module StateMachineContext =
    let private eventKey = "Frank.Statecharts.Event"

    let setEvent (ctx: HttpContext) (event: 'Event) =
        ctx.Items[eventKey] <- box event

    let tryGetEvent<'Event> (ctx: HttpContext) : 'Event option =
        match ctx.Items.TryGetValue(eventKey) with
        | true, value -> Some (value :?> 'Event)
        | false, _ -> None
```

3. After handler, check for event and apply transition:

```fsharp
// After handler invocation:
match meta.TryGetEventAndTransition ctx instanceId with
| Some transitionResult ->
    match transitionResult with
    | Transitioned(newState, newCtx) ->
        do! meta.SetStateAfterTransition ctx.RequestServices instanceId (box (newState, newCtx))
        // Fire onTransition hooks
        let event = { PreviousState = ...; NewState = newState; ... }
        let logger = ctx.RequestServices.GetRequiredService<ILogger<StateMachineMiddleware>>()
        for observer in meta.TransitionObservers do
            try observer (box event)
            with ex -> logger.LogWarning(ex, "onTransition observer threw for instance {InstanceId}", instanceId)
    | TransitionResult.Blocked reason ->
        // This shouldn't happen after guards pass, but handle gracefully
        ctx.Response.StatusCode <- BlockReasonMapping.toStatusCode reason
    | Invalid msg ->
        ctx.Response.StatusCode <- 400
        do! ctx.Response.WriteAsync(msg)
| None ->
    // No event set by handler -- no transition (read-only operation like GET)
    ()
```

**Files**: `src/Frank.Statecharts/Middleware.fs`, `src/Frank.Statecharts/StatefulResourceBuilder.fs`
**Notes**:
- `onTransition` hooks fire AFTER successful state update, not before (DD-04)
- Read-only operations (GET) don't set an event, so no transition occurs
- Observer errors are caught and logged via `ILogger<StateMachineMiddleware>` resolved from `HttpContext.RequestServices` (constitution VII: no silent exception swallowing)
- The typed transition logic needs to be closed over at Build time (same pattern as guards)

### Subtask T021 -- Create `MiddlewareTests.fs`

**Purpose**: Integration tests for the complete middleware pipeline.

**Steps**:
1. Create `test/Frank.Statecharts.Tests/MiddlewareTests.fs`
2. Add to test `.fsproj` `<Compile>` list
3. Write tests using TestHost:

**a. Non-stateful pass-through**:
```fsharp
testTask "Non-stateful resource passes through middleware" {
    // Configure a plain resource (no statefulResource)
    // Verify middleware doesn't interfere
}
```

**b. Method filtering**:
```fsharp
testTask "Returns 405 for disallowed method in current state" {
    // statefulResource with POST allowed in XTurn
    // Send GET to resource in XTurn state
    // Verify 405 with Allow header
}
```

**c. Guard blocking**:
```fsharp
testTask "Returns 403 when guard blocks with NotAllowed" {
    // Guard that checks for admin role
    // Send request without admin claim
    // Verify 403
}

testTask "Returns 409 when guard blocks with NotYourTurn" {
    // Turn-based guard
    // Send request from wrong player
    // Verify 409
}
```

**d. Successful transition**:
```fsharp
testTask "Successful handler triggers state transition" {
    // POST to resource in XTurn state with valid player
    // Verify response is 200
    // Verify state has transitioned to OTurn
}
```

**e. Transition hook fires**:
```fsharp
testTask "onTransition hook fires after successful transition" {
    // Register onTransition observer
    // POST to trigger transition
    // Verify observer received TransitionEvent with correct before/after state
}
```

**f. Initial state for new instance**:
```fsharp
testTask "New instance uses Initial state from machine" {
    // GET resource with unknown instanceId
    // Verify middleware uses machine.Initial
}
```

**Files**: `test/Frank.Statecharts.Tests/MiddlewareTests.fs`
**Parallel?**: Can be scaffolded once T016 interface is defined.
**Validation**: `dotnet test test/Frank.Statecharts.Tests/` passes.

---

## Test Strategy

- All tests use ASP.NET Core TestHost
- Use `MailboxProcessorStore` as the concrete store in tests
- Mock `ClaimsPrincipal` for guard tests using standard `ClaimsIdentity`
- Verify HTTP status codes and response headers (especially `Allow` for 405)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Type erasure: middleware works with `obj` but machine is generic | Close over typed functions at Build time in StateMachineMetadata |
| Middleware ordering with LinkedData/Auth | Must run after routing but before endpoint execution; document in WP05 |
| Concurrent requests to same instance | MailboxProcessor serializes store access; middleware awaits store operations |
| Event communication from handler to middleware | Use `HttpContext.Items` convention; document pattern |

---

## Review Guidance

- Verify middleware checks `StateMachineMetadata` marker (null check, not exception)
- Verify 405 response includes `Allow` header
- Verify guard evaluation order (registration order, first Blocked short-circuits)
- Verify `BlockReason` -> HTTP status code mapping is correct
- Verify `onTransition` fires AFTER state update, not before
- Verify pass-through has zero overhead for non-stateful resources
- Verify event communication pattern via `HttpContext.Items`

---

## Activity Log

- 2026-03-06T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-07T16:45:02Z – claude-opus – shell_pid=55845 – lane=doing – Assigned agent via workflow command
