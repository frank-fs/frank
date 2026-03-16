---
work_package_id: "WP02"
title: "Guard Discriminated Union"
phase: "Phase 1 - Foundation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs:
  - "FR-005"
  - "FR-006"
  - "FR-012"
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
history:
  - timestamp: "2026-03-15T23:59:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- Guard Discriminated Union

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

This WP depends on WP01:
```bash
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

Replace the `Guard` record type and `GuardContext` record with a Guard DU plus phase-specific context records that eliminate `Unchecked.defaultof<'E>` and provide type-safe two-phase guard evaluation:

1. `GuardContext` record is replaced by two phase-specific context records: `AccessControlContext<'S,'C>` and `EventValidationContext<'S,'E,'C>`
2. `Guard<'S,'E,'C>` becomes a DU with `AccessControl` (named, pre-handler, no event) and `EventValidation` (named, post-handler, with event) cases
3. `StateMachine` record keeps a single `Guards` field with the new DU type
4. Middleware evaluates `AccessControl` guards before the handler, `EventValidation` guards after the handler with the actual event
5. `Unchecked.defaultof<'E>` is completely eliminated from the codebase
6. All existing tests are updated to use the new Guard DU and context records
7. New tests validate both guard phases

**Success gate**: `dotnet build` succeeds across all targets. `dotnet test` passes all tests. No instance of `Unchecked.defaultof` remains in the statecharts source.

## Context & Constraints

- **Spec**: `kitty-specs/010-statecharts-production-readiness/spec.md` -- User Story 3 (Guard Access to Event Context, P2)
- **Plan**: `kitty-specs/010-statecharts-production-readiness/plan.md` -- Decision D-003
- **Research**: `kitty-specs/010-statecharts-production-readiness/research.md` -- Decision 3 (Guard DU with Phase-Typed Cases)
- **Breaking change**: Acceptable for pre-1.0 library. Guard type changes from record to DU.
- **Constraint**: Single `Guards` field on `StateMachine` -- NO separate `EventGuards` field. The plan explicitly says "No separate `EventGuards` field."
- **Constraint**: `StateMachineMetadata` gets a NEW `EvaluateEventGuards` closure field (this is a metadata field, not a `StateMachine` field)
- **Constraint**: If `EventValidation` guard blocks after response started, log warning (same pattern as existing `TransitionAttemptResult.Blocked` handling)

### Key Files
- `src/Frank.Statecharts/Types.fs` -- Guard DU definition, context records, GuardContext removal, StateMachine update
- `src/Frank.Statecharts/StatefulResourceBuilder.fs` -- evaluateGuards split, evaluateEventGuards closure, StateMachineMetadata update
- `src/Frank.Statecharts/Middleware.fs` -- two-phase guard evaluation flow
- `test/Frank.Statecharts.Tests/TypeTests.fs` -- update guard type references
- `test/Frank.Statecharts.Tests/MiddlewareTests.fs` -- update guard tests, add two-phase tests
- `test/Frank.Statecharts.Tests/StatefulResourceTests.fs` -- update guard references

## Subtasks & Detailed Guidance

### Subtask T008 -- Replace `Guard` record and `GuardContext` with Guard DU + context records in `Types.fs`

**Purpose**: Define the new Guard discriminated union with named cases and phase-specific context records that provide type-safe guard evaluation.

**Steps**:

1. In `src/Frank.Statecharts/Types.fs`, replace the current `GuardContext` and `Guard` types:

   **Current** (to be removed):
   ```fsharp
   type GuardContext<'State, 'Event, 'Context> =
       { User: ClaimsPrincipal
         CurrentState: 'State
         Event: 'Event
         Context: 'Context }

   type Guard<'State, 'Event, 'Context> =
       { Name: string
         Predicate: GuardContext<'State, 'Event, 'Context> -> GuardResult }
   ```

   **New** (replacement):
   ```fsharp
   /// Context passed to access-control guards (pre-handler, no event available).
   type AccessControlContext<'State, 'Context> =
       { User: ClaimsPrincipal
         CurrentState: 'State
         Context: 'Context }

   /// Context passed to event-validation guards (post-handler, with actual event).
   type EventValidationContext<'State, 'Event, 'Context> =
       { User: ClaimsPrincipal
         CurrentState: 'State
         Event: 'Event
         Context: 'Context }

   /// A guard predicate that controls state transition access.
   /// AccessControl runs pre-handler (no event available).
   /// EventValidation runs post-handler (with actual event value).
   type Guard<'State, 'Event, 'Context> =
       | AccessControl of name: string * predicate: (AccessControlContext<'State, 'Context> -> GuardResult)
       | EventValidation of name: string * predicate: (EventValidationContext<'State, 'Event, 'Context> -> GuardResult)
   ```

2. The `name` field is kept on both DU cases (replacing the `Name` field from the old record) to support diagnostics and logging.

3. Keep all other types in `Types.fs` unchanged (`BlockReason`, `GuardResult`, `StateInfo`, `TransitionResult`, `StateMachine`).

**Files**: `src/Frank.Statecharts/Types.fs`
**Parallel?**: No -- foundation for all subsequent subtasks
**Notes**: The context records are separate types rather than anonymous tuples because they provide named field access, which is more ergonomic for guard implementations. `AccessControlContext` intentionally omits the `Event` field -- it's not in the type at all, so guards cannot accidentally access it.

### Subtask T009 -- Update `StateMachine` record for Guard DU

**Purpose**: Ensure the `StateMachine` record uses the new `Guard` DU type in its single `Guards` field.

**Steps**:

1. In `src/Frank.Statecharts/Types.fs`, verify the `StateMachine` record's `Guards` field. It should already be typed as `Guard<'State, 'Event, 'Context> list`. Since we replaced the `Guard` type in T008, this field automatically uses the new DU type.

2. Verify no compile errors -- the `StateMachine` record should be:
   ```fsharp
   type StateMachine<'State, 'Event, 'Context when 'State: equality and 'State: comparison> =
       { Initial: 'State
         InitialContext: 'Context
         Transition: 'State -> 'Event -> 'Context -> TransitionResult<'State, 'Context>
         Guards: Guard<'State, 'Event, 'Context> list
         StateMetadata: Map<'State, StateInfo> }
   ```

3. Do NOT add a separate `EventGuards` field. The plan explicitly says "No separate `EventGuards` field."

**Files**: `src/Frank.Statecharts/Types.fs`
**Parallel?**: No -- depends on T008
**Notes**: The `Guards` field now contains a mixed list of `AccessControl` and `EventValidation` guards. The builder splits them by pattern matching at build time.

### Subtask T010 -- Update `evaluateGuards` closure (AccessControl only)

**Purpose**: Modify the `evaluateGuards` closure in `StatefulResourceBuilder.fs` to evaluate only `AccessControl` guards (pre-handler phase) using `AccessControlContext`.

**Steps**:

1. In `src/Frank.Statecharts/StatefulResourceBuilder.fs`, find the `evaluateGuards` closure (around line 197-212).

2. Replace the current implementation that uses `GuardContext` with:
   ```fsharp
   let evaluateGuards (ctx: HttpContext) : GuardResult =
       let state = ctx.Items[StateMachineContext.stateKey] :?> 'S
       let context = ctx.Items[StateMachineContext.contextKey] :?> 'C

       let guardCtx: AccessControlContext<'S, 'C> =
           { User = ctx.User
             CurrentState = state
             Context = context }

       machineWithMetadata.Guards
       |> List.tryPick (fun guard ->
           match guard with
           | AccessControl(_, predicate) ->
               match predicate guardCtx with
               | Allowed -> None
               | Blocked reason -> Some(Blocked reason)
           | EventValidation _ -> None)  // Skip event guards in pre-handler phase
       |> Option.defaultValue Allowed
   ```

3. This eliminates the old `GuardContext` construction and the `Unchecked.defaultof<'E>` usage entirely.

**Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
**Parallel?**: No -- depends on T008, T009
**Notes**: The key change is `| EventValidation _ -> None` which skips event-validation guards during the pre-handler phase. The `AccessControlContext` has no `Event` field, making it impossible to access the event.

### Subtask T011 -- Create `evaluateEventGuards` closure

**Purpose**: Add a new closure that evaluates `EventValidation` guards after the handler has run and the event is available.

**Steps**:

1. In `src/Frank.Statecharts/StatefulResourceBuilder.fs`, after the `evaluateGuards` closure, add:
   ```fsharp
   let evaluateEventGuards (ctx: HttpContext) : GuardResult =
       let state = ctx.Items[StateMachineContext.stateKey] :?> 'S
       let context = ctx.Items[StateMachineContext.contextKey] :?> 'C

       match StateMachineContext.tryGetEvent<'E> ctx with
       | None -> Allowed  // No event set -- nothing to validate
       | Some event ->
           let guardCtx: EventValidationContext<'S, 'E, 'C> =
               { User = ctx.User
                 CurrentState = state
                 Event = event
                 Context = context }

           machineWithMetadata.Guards
           |> List.tryPick (fun guard ->
               match guard with
               | EventValidation(_, predicate) ->
                   match predicate guardCtx with
                   | Allowed -> None
                   | Blocked reason -> Some(Blocked reason)
               | AccessControl _ -> None)  // Skip access guards in post-handler phase
           |> Option.defaultValue Allowed
   ```

2. This closure retrieves the event from `HttpContext.Items` (set by the handler via `StateMachineContext.setEvent`) and passes it to `EventValidation` guards via the `EventValidationContext`.

**Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
**Parallel?**: No -- depends on T010
**Notes**: If no event is set (e.g., GET request), the event guards are skipped entirely (return `Allowed`). This is correct -- GET requests don't trigger transitions.

### Subtask T012 -- Add `EvaluateEventGuards` to `StateMachineMetadata`

**Purpose**: Add the new closure field to `StateMachineMetadata` so the middleware can call it.

**Steps**:

1. In `src/Frank.Statecharts/StatefulResourceBuilder.fs`, update the `StateMachineMetadata` record definition:
   ```fsharp
   type StateMachineMetadata =
       { Machine: obj
         StateHandlerMap: Map<string, (string * RequestDelegate) list>
         ResolveInstanceId: HttpContext -> string
         TransitionObservers: (obj -> unit) list
         InitialStateKey: string
         GetCurrentStateKey: IServiceProvider -> HttpContext -> string -> Task<string>
         EvaluateGuards: HttpContext -> GuardResult
         EvaluateEventGuards: HttpContext -> GuardResult  // NEW
         ExecuteTransition: IServiceProvider -> HttpContext -> string -> Task<TransitionAttemptResult> }
   ```

2. Update the metadata construction in `Run` to include the new field:
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

**Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
**Parallel?**: No -- depends on T011
**Notes**: This is a breaking change to `StateMachineMetadata`, but it's an internal type (not part of the public API for end users).

### Subtask T013 -- Update Middleware.fs to two-phase guard evaluation

**Purpose**: Change the middleware flow from 5-step to 6-step, inserting event-validation guard evaluation between handler invocation and transition execution.

**Steps**:

1. In `src/Frank.Statecharts/Middleware.fs`, find the `HandleStateful` method.

2. The current flow is:
   ```
   Step 1: GetCurrentStateKey
   Step 2: Method check
   Step 3: Evaluate guards (all, pre-handler)
   Step 4: Invoke handler
   Step 5: Execute transition
   ```

3. Change to:
   ```
   Step 1: GetCurrentStateKey
   Step 2: Method check
   Step 3: Evaluate AccessControl guards (pre-handler)
   Step 4: Invoke handler
   Step 5: Evaluate EventValidation guards (post-handler)
   Step 6: Execute transition
   ```

4. After the handler invocation (`do! handler.Invoke(ctx)`) and BEFORE the transition execution, insert:
   ```fsharp
   // Step 5: Evaluate EventValidation guards (post-handler)
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
               "EventValidation guard blocked for instance {InstanceId} but response already started",
               instanceId
           )
   | Allowed ->
       // Step 6: Execute transition
       let! transResult = meta.ExecuteTransition ctx.RequestServices ctx instanceId
       // ... existing transition result handling
   ```

5. The existing transition result handling (lines 84-117) moves inside the `| Allowed ->` branch of the event guard check.

**Files**: `src/Frank.Statecharts/Middleware.fs`
**Parallel?**: No -- depends on T012
**Notes**: The "response already started" warning for event guard blocking follows the exact same pattern as the existing `TransitionAttemptResult.Blocked` handling (lines 96-107). This is consistent per the research.md decision.

### Subtask T014 -- Update existing tests for Guard DU

**Purpose**: Update all test files that reference the old `Guard` record and `GuardContext` type to use the new `Guard` DU and context records.

**Steps**:

1. **`test/Frank.Statecharts.Tests/TypeTests.fs`**: Find any tests that construct `Guard` records or `GuardContext` records. Update to use the DU:

   Old pattern:
   ```fsharp
   { Name = "test"; Predicate = fun ctx -> Allowed }
   ```
   New pattern:
   ```fsharp
   AccessControl("test", fun ctx -> Allowed)
   ```

2. **`test/Frank.Statecharts.Tests/MiddlewareTests.fs`**: Find tests that set up guards on `StateMachine` records. Update the guard construction. Old `GuardContext` field access (e.g., `ctx.CurrentState`) stays the same since `AccessControlContext` has the same field names (minus `Event`).

3. **`test/Frank.Statecharts.Tests/StatefulResourceTests.fs`**: Find tests that register guards. Update guard construction.

4. Search across all test files for `GuardContext` references -- all must be removed since the type no longer exists.

5. Search for `Unchecked.defaultof` -- ensure none remain in source or tests.

6. Compile and run tests after updating. Fix any remaining compilation errors.

**Files**: `test/Frank.Statecharts.Tests/TypeTests.fs`, `test/Frank.Statecharts.Tests/MiddlewareTests.fs`, `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
**Parallel?**: No -- must update all tests together for compilation to succeed
**Notes**: This is a mechanical refactor. The `Guards = []` pattern on `StateMachine` records stays the same since the list type is unchanged. Only tests that provide non-empty guard lists need updating. Guards that previously accessed `ctx.Event` were receiving `Unchecked.defaultof` -- those should become `EventValidation` guards that receive the actual event.

### Subtask T015 -- Add tests for `AccessControl` guards (pre-handler)

**Purpose**: Validate that `AccessControl` guards run before the handler and correctly block/allow without an event.

**Steps**:

1. Add tests in `test/Frank.Statecharts.Tests/MiddlewareTests.fs`:

   - **Test: AccessControl guard allows request**: Register an `AccessControl("allow", fun ctx -> Allowed)` guard. Make a request. Verify handler is invoked (200 OK).

   - **Test: AccessControl guard blocks request**: Register an `AccessControl("deny", fun ctx -> Blocked NotAllowed)` guard. Make a request. Verify 403 returned. Verify handler is NOT invoked.

   - **Test: AccessControl guard receives correct state**: Register an `AccessControl("check-state", fun ctx -> ...)` guard that checks `ctx.CurrentState`. Set store to a specific state. Make a request. Verify guard received the correct state.

   - **Test: AccessControl guard receives correct user**: Register an `AccessControl("check-user", fun ctx -> ...)` guard that checks `ctx.User`. Configure authentication. Verify guard receives the user.

**Files**: `test/Frank.Statecharts.Tests/MiddlewareTests.fs`
**Parallel?**: Yes -- can proceed alongside T016 after T014
**Notes**: These tests validate the new DU-based guard behavior specifically. The `AccessControlContext` has `User`, `CurrentState`, and `Context` fields but no `Event`.

### Subtask T016 -- Add tests for `EventValidation` guards (post-handler)

**Purpose**: Validate that `EventValidation` guards run after the handler and receive the actual event value via `EventValidationContext`.

**Steps**:

1. Add tests in `test/Frank.Statecharts.Tests/MiddlewareTests.fs`:

   - **Test: EventValidation guard receives actual event**: Register an `EventValidation("check-event", fun ctx -> ...)` guard that captures `ctx.Event`. Handler sets an event via `StateMachineContext.setEvent`. Make a POST request. Verify the guard received the actual event (not null/default).

   - **Test: EventValidation guard blocks transition**: Register an `EventValidation("block", fun ctx -> Blocked PreconditionFailed)` guard. Handler sets an event. Make a POST request. Verify the handler ran (response may have started) but the transition was NOT executed (state unchanged).

   - **Test: EventValidation guard skipped when no event**: Register an `EventValidation("skip", fun ctx -> ...)` guard. Make a GET request (handler does not set event). Verify the guard is NOT evaluated (or returns `Allowed` because no event).

   - **Test: EventValidation guard blocking after response started**: Register an `EventValidation("late-block", fun ctx -> Blocked ...)` guard. Handler writes to response before returning. Verify the middleware logs a warning instead of changing status code.

   - **Test: Mixed guards (AccessControl + EventValidation)**: Register both guard types. Verify `AccessControl` runs first (pre-handler), `EventValidation` runs after handler. Verify correct sequencing.

**Files**: `test/Frank.Statecharts.Tests/MiddlewareTests.fs`
**Parallel?**: Yes -- can proceed alongside T015 after T014
**Notes**: The `EventValidationContext` has `User`, `CurrentState`, `Event`, and `Context` fields. The `Event` field contains the actual event value set by the handler. The "blocking after response started" test validates the edge case from the spec.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Breaking change to `Guard` type breaks all guard constructions | T014 covers comprehensive test updates; this is expected for pre-1.0 |
| `EventValidation` guard evaluated when response already started | Middleware checks `ctx.Response.HasStarted` and logs warning (same as existing pattern) |
| `GuardContext` removal breaks code that imports it | Search all files for `GuardContext` references; it's internal to the statecharts project |
| `StateMachineMetadata` gains a field -- any code constructing it directly breaks | `StateMachineMetadata` is internal; only `StatefulResourceBuilder.Run` constructs it |
| Middleware logic becomes more complex with two guard phases | Clear step numbering in comments; each phase is a simple pattern match |

## Review Guidance

- Verify `GuardContext` type is completely removed from `Types.fs`
- Verify `AccessControlContext` and `EventValidationContext` records are defined
- Verify `Guard` DU cases include `name` field for diagnostics
- Verify `Unchecked.defaultof` is eliminated from all statecharts source files
- Verify `StateMachine` has a SINGLE `Guards` field (no `EventGuards`)
- Verify middleware flow: AccessControl -> handler -> EventValidation -> transition
- Verify "response already started" warning pattern for event guard blocking
- Run `dotnet build` across all targets
- Run `dotnet test` -- all tests must pass

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:00Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP02 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
