---
work_package_id: "WP02"
title: "Guard Discriminated Union"
phase: "Phase 1 - Foundation"
lane: "done"
assignee: ""
agent: ""
shell_pid: ""
review_status: "has_feedback"
reviewed_by: "Ryan Riley"
dependencies: ["WP01"]
requirement_refs:
  - "FR-005"
  - "FR-006"
subtasks:
  - "T008"
  - "T009"
  - "T010"
  - "T011"
  - "T012"
  - "T013"
  - "T014"
  - "T015"
  - "T016"
review_feedback_file: "/private/tmp/fix-lane.md"
history:
  - timestamp: "2026-03-16T00:05:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- Guard Discriminated Union

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

**Reviewed by**: Ryan Riley
**Status**: ❌ Changes Requested
**Date**: 2026-03-16
**Feedback file**: `/private/tmp/fix-lane.md`

**Issue**: Manually correcting lane status to done


## Implementation Command

Depends on WP01 -- branch from WP01:

```bash
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

Replace the current `Guard` record type and `GuardContext` record with a `Guard` discriminated union having `AccessControl` and `EventValidation` cases. Introduce `AccessControlContext` (no event field) and `EventValidationContext` (with event field) records. Update middleware to two-phase guard evaluation. Eliminate all uses of `Unchecked.defaultof<'E>`.

**Success Criteria**:
1. `Guard` is a DU with `AccessControl` and `EventValidation` cases, each carrying a `name: string` and a predicate function
2. `AccessControlContext` has no `Event` field -- guards cannot access the event at compile time
3. `EventValidationContext` has an `Event` field with the actual event value (never null/defaultof)
4. `GuardContext` record is removed entirely
5. `StateMachine.Guards` stays as a single `Guard<'S,'E,'C> list` field -- no `EventGuards` field
6. Middleware evaluates `AccessControl` guards pre-handler (step 3) and `EventValidation` guards post-handler (new step 5)
7. No occurrences of `Unchecked.defaultof<'E>` remain in the codebase
8. All existing tests compile and pass after guard type updates
9. Build succeeds across all targets (net8.0, net9.0, net10.0)

## Context & Constraints

- **Spec**: `/kitty-specs/010-statecharts-production-readiness/spec.md` -- User Story 3 (Guard Access to Event Context), FR-005, FR-006
- **Plan**: `/kitty-specs/010-statecharts-production-readiness/plan.md` -- Decision D-003
- **Research**: `/kitty-specs/010-statecharts-production-readiness/research.md` -- Decision 3 (full rationale, builder closure sketches)
- **Data Model**: `/kitty-specs/010-statecharts-production-readiness/data-model.md` -- Guard DU entity, AccessControlContext, EventValidationContext, StateMachineMetadata changes, middleware flow
- **Constitution**: Principle II (Idiomatic F#) -- DU is the idiomatic modeling choice; Principle VIII (No Duplicated Logic) -- single Guards list with DU dispatch

**Breaking Change**: This is a source-breaking change. The old `Guard` record (`{ Name; Predicate }`) and `GuardContext` record (`{ User; CurrentState; Event; Context }`) are both replaced. All code constructing guards must be updated. This is acceptable for pre-1.0.

**Depends on WP01**: WP01 must land first because both WPs modify `Types.fs` and `StatefulResourceBuilder.fs`. WP01 stabilizes the `StateMachine` record shape and the builder's closure structure before WP02 modifies them further.

## Subtasks & Detailed Guidance

### Subtask T008 -- Replace Guard record and GuardContext with Guard DU + new context records in Types.fs

- **Purpose**: Define the core type changes that replace the footgun `Unchecked.defaultof<'E>` with type-safe DU cases.
- **Files**: `src/Frank.Statecharts/Types.fs`
- **Steps**:
  1. **Remove** the `GuardContext` record type:
     ```fsharp
     // DELETE THIS:
     type GuardContext<'State, 'Event, 'Context> =
         { User: ClaimsPrincipal
           CurrentState: 'State
           Event: 'Event
           Context: 'Context }
     ```

  2. **Remove** the `Guard` record type:
     ```fsharp
     // DELETE THIS:
     type Guard<'State, 'Event, 'Context> =
         { Name: string
           Predicate: GuardContext<'State, 'Event, 'Context> -> GuardResult }
     ```

  3. **Add** new context record types and Guard DU in their place:
     ```fsharp
     /// Context for access-control guards (pre-handler). No event available.
     type AccessControlContext<'State, 'Context> =
         { User: ClaimsPrincipal
           CurrentState: 'State
           Context: 'Context }

     /// Context for event-validation guards (post-handler). Event is the actual value set by the handler.
     type EventValidationContext<'State, 'Event, 'Context> =
         { User: ClaimsPrincipal
           CurrentState: 'State
           Event: 'Event
           Context: 'Context }

     /// A guard that controls access to state transitions.
     /// The DU case determines both execution phase and type signature.
     type Guard<'State, 'Event, 'Context> =
         /// Runs pre-handler. Cannot access the event (AccessControlContext has no Event field).
         | AccessControl of name: string * predicate: (AccessControlContext<'State, 'Context> -> GuardResult)
         /// Runs post-handler. Receives the actual event set by the handler.
         | EventValidation of name: string * predicate: (EventValidationContext<'State, 'Event, 'Context> -> GuardResult)
     ```

  4. Place these types after `GuardResult` and before `StateInfo` in the file to maintain logical ordering.

- **Notes**: The DU field labels (`name:`, `predicate:`) are named fields for documentation/readability, not for positional extraction. Pattern matching uses positional deconstruction: `AccessControl(name, pred)`.

### Subtask T009 -- Update StateMachine record for Guard DU type

- **Purpose**: The `StateMachine` record's `Guards` field keeps the same name but its element type changes from the old `Guard` record to the new `Guard` DU.
- **Files**: `src/Frank.Statecharts/Types.fs`
- **Steps**:
  1. The `StateMachine` record definition does NOT need any code change -- the `Guards` field is typed as `Guard<'State, 'Event, 'Context> list`, and since we replaced the `Guard` type definition in T008 (same name, same type parameters), this field automatically uses the new DU type.
  2. Verify the record definition still compiles:
     ```fsharp
     type StateMachine<'State, 'Event, 'Context when 'State: equality and 'State: comparison> =
         { Initial: 'State
           InitialContext: 'Context
           Transition: 'State -> 'Event -> 'Context -> TransitionResult<'State, 'Context>
           Guards: Guard<'State, 'Event, 'Context> list  // Now DU type
           StateMetadata: Map<'State, StateInfo> }
     ```
- **Notes**: This subtask is minimal -- the type name hasn't changed. The breaking change manifests at call sites where guards are constructed.

### Subtask T010 -- Update `evaluateGuards` closure to use AccessControl only

- **Purpose**: The existing `evaluateGuards` closure passes `Unchecked.defaultof<'E>` as the event. Replace it with a closure that only evaluates `AccessControl` guards using `AccessControlContext` (no event field).
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Steps**:
  1. At the top of `Run`, after the machine is available, partition guards by DU case:
     ```fsharp
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
     ```

  2. Replace the existing `evaluateGuards` closure (currently around lines 197-212):
     ```fsharp
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
     ```

  3. This eliminates the `Unchecked.defaultof<'E>` line entirely.

### Subtask T011 -- Create `evaluateEventGuards` closure

- **Purpose**: Add a new closure for evaluating `EventValidation` guards post-handler, receiving the actual event value.
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Steps**:
  1. Add a new closure after `evaluateGuards`:
     ```fsharp
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

  2. When no event is set (GET requests, or handlers that don't call `setEvent`), event guards are skipped by returning `Allowed`.

### Subtask T012 -- Add `EvaluateEventGuards` field to `StateMachineMetadata`

- **Purpose**: Expose the event-guard evaluation closure to the middleware via endpoint metadata.
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Steps**:
  1. Add a new field to the `StateMachineMetadata` record:
     ```fsharp
     type StateMachineMetadata =
         {
             Machine: obj
             StateHandlerMap: Map<string, (string * RequestDelegate) list>
             ResolveInstanceId: HttpContext -> string
             TransitionObservers: (obj -> unit) list
             InitialStateKey: string
             GetCurrentStateKey: IServiceProvider -> HttpContext -> string -> Task<string>
             EvaluateGuards: HttpContext -> GuardResult
             /// Evaluate event-validation guards after the handler has set the event.
             EvaluateEventGuards: HttpContext -> GuardResult  // NEW
             ExecuteTransition: IServiceProvider -> HttpContext -> string -> Task<TransitionAttemptResult>
         }
     ```

  2. Wire the closure into the metadata construction:
     ```fsharp
     let metadata: StateMachineMetadata =
         { Machine = box machineWithMetadata
           StateHandlerMap = stateHandlerMap
           ResolveInstanceId = resolveId
           TransitionObservers = ...
           InitialStateKey = initialStateKey
           GetCurrentStateKey = getCurrentStateKey
           EvaluateGuards = evaluateGuards
           EvaluateEventGuards = evaluateEventGuards  // NEW
           ExecuteTransition = executeTransition }
     ```

### Subtask T013 -- Update Middleware.fs to two-phase guard evaluation

- **Purpose**: Change the middleware flow from 5-step to 6-step, inserting event-guard evaluation between handler invocation and transition execution.
- **Files**: `src/Frank.Statecharts/Middleware.fs`
- **Steps**:
  1. After the handler invocation (`do! handler.Invoke(ctx)`) and before the transition execution (`let! transResult = meta.ExecuteTransition ...`), insert event-guard evaluation:
     ```fsharp
     | Allowed ->
         // Step 4: Invoke the state-specific handler
         do! handler.Invoke(ctx)

         // Step 5: Evaluate event-validation guards (NEW)
         let eventGuardResult = meta.EvaluateEventGuards ctx

         match eventGuardResult with
         | Blocked reason ->
             if not ctx.Response.HasStarted then
                 ctx.Response.StatusCode <- BlockReasonMapping.toStatusCode reason
                 match BlockReasonMapping.toMessage reason with
                 | Some msg -> do! ctx.Response.WriteAsync(msg)
                 | None -> ()
             else
                 logger.LogWarning(
                     "Event guard blocked for instance {InstanceId} but response already started",
                     instanceId
                 )
         | Allowed ->
             // Step 6: Try transition (event set by handler in HttpContext.Items)
             let! transResult = meta.ExecuteTransition ctx.RequestServices ctx instanceId
             // ... existing transition result handling ...
     ```

  2. The full updated flow becomes:
     - Step 1: Look up current state from store
     - Step 2: Check HTTP method allowed
     - Step 3: Evaluate `AccessControl` guards (pre-handler)
     - Step 4: Invoke handler
     - Step 5: Evaluate `EventValidation` guards (post-handler) -- **NEW**
     - Step 6: Execute transition

  3. The existing transition result handling (lines 84-117) stays the same but is now nested inside the `Allowed` branch of the event guard check.

- **Notes**: If an `EventValidation` guard blocks, the handler has already run. If the response has already started, log a warning (same pattern as the existing `TransitionAttemptResult.Blocked` handling). The transition is NOT executed when an event guard blocks.

### Subtask T014 -- Update existing tests referencing Guard record / GuardContext types

- **Purpose**: All existing test code that constructs `Guard` records or `GuardContext` records must be updated to use the new DU.
- **Files**: `test/Frank.Statecharts.Tests/TypeTests.fs`, `test/Frank.Statecharts.Tests/MiddlewareTests.fs`, `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
- **Steps**:
  1. **TypeTests.fs** -- `guardTests`: Update guard construction from record syntax to DU syntax:
     ```fsharp
     // OLD:
     let adminGuard: Guard<TurnstileState, TurnstileEvent, unit> =
         { Name = "isAdmin"
           Predicate = fun ctx -> if ctx.User.IsInRole("Admin") then Allowed else Blocked(NotAllowed) }
     let ctx = { User = adminPrincipal; CurrentState = Locked; Event = Coin; Context = () }
     Expect.equal (adminGuard.Predicate ctx) Allowed "admin should be allowed"

     // NEW:
     let adminGuard: Guard<TurnstileState, TurnstileEvent, unit> =
         AccessControl("isAdmin", fun ctx -> if ctx.User.IsInRole("Admin") then Allowed else Blocked(NotAllowed))
     let ctx: AccessControlContext<TurnstileState, unit> =
         { User = adminPrincipal; CurrentState = Locked; Context = () }
     match adminGuard with
     | AccessControl(_, pred) -> Expect.equal (pred ctx) Allowed "admin should be allowed"
     | _ -> failtest "Expected AccessControl"
     ```

  2. **MiddlewareTests.fs** -- `guardedMachine` and `notYourTurnMachine`: Update guard construction:
     ```fsharp
     // OLD:
     Guards = [ { Name = "RequireAdmin"; Predicate = fun ctx -> ... } ]
     // NEW:
     Guards = [ AccessControl("RequireAdmin", fun ctx ->
         if ctx.User.IsInRole("admin") then Allowed else Blocked NotAllowed) ]
     ```
     Note: These guards only check `ctx.User`, so they become `AccessControl` guards with `AccessControlContext`.

  3. **StatefulResourceTests.fs** -- `turnGuard`: Update from record to DU:
     ```fsharp
     let turnGuard: Guard<TicTacToeState, TicTacToeEvent, int> =
         AccessControl("TurnGuard", fun ctx ->
             match ctx.CurrentState with
             | XTurn ->
                 if ctx.User.HasClaim("player", "X") then Allowed
                 elif ctx.User.HasClaim("player", "O") then Blocked NotYourTurn
                 else Blocked NotAllowed
             | OTurn ->
                 if ctx.User.HasClaim("player", "O") then Allowed
                 elif ctx.User.HasClaim("player", "X") then Blocked NotYourTurn
                 else Blocked NotAllowed
             | Won _ | Draw -> Allowed)
     ```

  4. **Search for all `GuardContext` occurrences** across the codebase and replace.

### Subtask T015 -- Add tests for AccessControl guards (pre-handler)

- **Purpose**: Verify that `AccessControl` guards run before the handler and block requests appropriately.
- **Files**: `test/Frank.Statecharts.Tests/MiddlewareTests.fs`
- **Parallel**: Yes (after T008-T013)
- **Steps**:
  1. Add test that `AccessControl` guard blocks before handler invocation (handler must NOT run).
  2. Add test that `AccessControl` guard passes allow handler to proceed normally.

### Subtask T016 -- Add tests for EventValidation guards (post-handler)

- **Purpose**: Verify that `EventValidation` guards run after the handler and receive the actual event value.
- **Files**: `test/Frank.Statecharts.Tests/MiddlewareTests.fs`
- **Parallel**: Yes (after T008-T013)
- **Steps**:
  1. Add test that `EventValidation` guard receives actual event value via `ctx.Event`.
  2. Add test that `EventValidation` guard blocking suppresses transition (state does not change).
  3. Add test with both `AccessControl` and `EventValidation` guards mixed in one `Guards` list.
  4. Add test that `EventValidation` guards are skipped on GET (no event set).

## Risks & Mitigations

1. **Breaking change scope**: All guard construction code changes. The migration is mechanical: wrap existing predicates in `AccessControl(name, fun ctx -> ...)` and adjust context field access. All affected test files are enumerated in T014.

2. **Event guard ordering**: If the handler writes to the response and an `EventValidation` guard then blocks, the response may be partially written. The middleware handles this with `ctx.Response.HasStarted` check and logs a warning.

3. **WSD Guard Parser**: Check `src/Frank.Statecharts/Wsd/GuardParser.fs` for references to the old `Guard` type and update if needed.

## Review Guidance

- Verify `Unchecked.defaultof` does not appear anywhere in the codebase after this WP
- Verify `AccessControlContext` has exactly 3 fields: `User`, `CurrentState`, `Context` (no `Event`)
- Verify `EventValidationContext` has exactly 4 fields: `User`, `CurrentState`, `Event`, `Context`
- Verify middleware flow is 6-step (state lookup, method check, access guard, handler, event guard, transition)
- Verify `EventValidation` guards are skipped when no event is set
- Verify the `StateMachine` record has a single `Guards` field (no `EventGuards` field)
- Run `dotnet build` for all targets and `dotnet test` to confirm no regressions

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T00:05:00Z -- system -- lane=planned -- Prompt generated via /spec-kitty.tasks

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP02 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-16T14:34:43Z – unknown – lane=planned – Moved to planned
- 2026-03-16T14:35:33Z – unknown – lane=done – Moved to done
