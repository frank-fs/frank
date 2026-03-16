---
work_package_id: WP01
title: State Key Extraction
lane: "for_review"
dependencies: []
base_branch: master
base_commit: 7d5b7cdd35e1b13fb514c7646148835ae04c087f
created_at: '2026-03-16T04:02:08.636514+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
- T006
- T007
phase: Phase 1 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "97668"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-16T00:05:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-001
- FR-002
- FR-012
---

# Work Package Prompt: WP01 -- State Key Extraction

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

No dependencies -- start from target branch:

```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

Replace the fragile `state.ToString()` key derivation with `FSharpValue.PreComputeUnionTagReader` + `FSharpType.GetUnionCases` so parameterized DU cases map to a single handler key by case name.

**Success Criteria**:
1. A single `inState` handler registered for a parameterized case (e.g., `Won "X"`) matches all parameter variants (`Won "O"`, `Won "Z"`, etc.)
2. Simple (non-parameterized) DU cases work identically to before (backward compatible)
3. Non-DU state types fall back to `ToString()` gracefully
4. `StatechartETagProvider` is NOT affected -- it correctly uses `string state` for parameter-sensitive hashing
5. All existing tests pass without modification (except where state keys are asserted)
6. Build succeeds across all targets (net8.0, net9.0, net10.0)

## Context & Constraints

- **Spec**: `/kitty-specs/010-statecharts-production-readiness/spec.md` -- User Story 1 (Parameterized State Matching)
- **Plan**: `/kitty-specs/010-statecharts-production-readiness/plan.md` -- Decision D-001
- **Research**: `/kitty-specs/010-statecharts-production-readiness/research.md` -- Decision 1 (full rationale)
- **Data Model**: `/kitty-specs/010-statecharts-production-readiness/data-model.md` -- StateKeyExtractor entity
- **Constitution**: Principle V (Performance Parity) -- precomputed delegate ensures O(1) per-request key extraction
- **Existing precedent**: `src/Frank.LinkedData/Rdf/InstanceProjector.fs` line 94 uses `PreComputeUnionTagReader`

**Key Constraint**: The `StatechartETagProvider` uses `string state` (which calls `ToString()`) for ETag computation. This is CORRECT behavior -- different `Won "X"` and `Won "O"` should produce different ETags. Do NOT change the ETag provider to use case-name keys.

**Key Constraint**: The `StateMachine` record's `StateMetadata: Map<'State, StateInfo>` field keeps its `'State` keys at the type level. The normalization to string keys happens in `StatefulResourceBuilder.Run` when constructing `StateMachineMetadata`, not by changing the `StateMachine` record type.

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `stateKey` function using `FSharpValue.PreComputeUnionTagReader`

- **Purpose**: Build the core key extraction mechanism that replaces `ToString()` for state-to-handler-key mapping.
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Steps**:
  1. Add `open FSharp.Reflection` at the top of the file
  2. Inside `StatefulResourceBuilder.Run`, before the `initialStateKey` computation, add:
     ```fsharp
     // Precompute DU tag reader for O(1) state key extraction
     let stateType = typeof<'S>
     let tagReader = FSharpValue.PreComputeUnionTagReader(stateType)
     let cases = FSharpType.GetUnionCases(stateType)
     let caseNames = cases |> Array.map (fun c -> c.Name)

     let stateKey (state: 'S) : string =
         let tag = tagReader (box state)
         caseNames.[tag]
     ```
  3. This function is captured by closures created later in `Run`
- **Notes**: The `PreComputeUnionTagReader` returns a fast `obj -> int` delegate. Combined with the `caseNames` array, this gives O(1) key extraction without per-request reflection.

### Subtask T002 -- Add non-DU type fallback with `FSharpType.IsUnion` guard

- **Purpose**: Handle edge case where `'State` is not a discriminated union (e.g., a string or record type).
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Steps**:
  1. Wrap the T001 code with an `FSharpType.IsUnion` check:
     ```fsharp
     let stateKey: 'S -> string =
         if FSharpType.IsUnion(typeof<'S>, true) then
             let tagReader = FSharpValue.PreComputeUnionTagReader(typeof<'S>)
             let cases = FSharpType.GetUnionCases(typeof<'S>, true)
             let caseNames = cases |> Array.map (fun c -> c.Name)
             fun (state: 'S) -> caseNames.[tagReader (box state)]
         else
             fun (state: 'S) -> state.ToString()
     ```
  2. The `true` parameter to `IsUnion` and `GetUnionCases` allows non-public union types
- **Notes**: This fallback ensures the library doesn't crash if someone uses a non-DU state type. The `ToString()` fallback preserves existing behavior for non-DU types.

### Subtask T003 -- Replace `state.ToString()` at three call sites

- **Purpose**: Wire the new `stateKey` function into all places that derive string keys from state values.
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Steps**:
  1. **Line 176** (`initialStateKey`): Change from:
     ```fsharp
     let initialStateKey = machineWithMetadata.Initial.ToString()
     ```
     To:
     ```fsharp
     let initialStateKey = stateKey machineWithMetadata.Initial
     ```
  2. **Line 189** (`getCurrentStateKey` return): Change from:
     ```fsharp
     return state.ToString()
     ```
     To:
     ```fsharp
     return stateKey state
     ```
  3. **Line 250** (`stateHandlerMap` construction): Change from:
     ```fsharp
     |> List.map (fun (s, h) -> (s.ToString(), h))
     ```
     To:
     ```fsharp
     |> List.map (fun (s, h) -> (stateKey s, h))
     ```
     **BUT**: After T004, this map construction will already use string keys from the CE accumulator, so this line may change further. See T004.
- **Validation**: After this change, `Won "X"` and `Won "O"` both produce key `"Won"`. `XTurn` still produces `"XTurn"`.

### Subtask T004 -- Change `InState` CE operation to use string keys

- **Purpose**: The `StateHandlerMap` in the CE accumulator (`StatefulResourceSpec`) currently uses `'State` as the map key. For parameterized DUs, `Won "X"` and `Won "O"` are different keys. Change to string keys (case names) so handlers accumulate correctly.
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Steps**:
  1. Change `StatefulResourceSpec.StateHandlerMap` type from `Map<'State, (string * RequestDelegate) list>` to `Map<string, (string * RequestDelegate) list>`:
     ```fsharp
     type StatefulResourceSpec<'State, 'Event, 'Context when 'State: equality and 'State: comparison> =
         { RouteTemplate: string
           Machine: StateMachine<'State, 'Event, 'Context> option
           StateHandlerMap: Map<string, (string * RequestDelegate) list>  // Changed from Map<'State, ...>
           TransitionObservers: (TransitionEvent<'State, 'Event, 'Context> -> unit) list
           ResolveInstanceId: (HttpContext -> string) option
           Metadata: (EndpointBuilder -> unit) list }
     ```
  2. Update the `InState` member to convert the state to a string key. Since `InState` runs during CE evaluation (before `Run`), it needs its own key extraction. However, there is a problem: the `stateKey` function requires type reflection which is heavy for a CE operation.

     **Solution**: The `InState` member receives `StateHandlers<'S>` which contains `.State: 'S`. Since `InState` is called during CE evaluation, use the same `FSharpType.IsUnion` / `PreComputeUnionTagReader` approach, but cached statically. Alternatively, extract the key in `Run` by re-keying. The simplest approach is:
     - Keep `StateHandlers<'S>` passing in `'State` values
     - In `InState`, compute the key using a local helper (or defer keying to `Run`)
     - **Recommended approach**: Create a static helper function `StateKeyExtractor.keyOf` that can be called from `InState`:
       ```fsharp
       [<RequireQualifiedAccess>]
       module internal StateKeyExtractor =
           let private cache = System.Collections.Concurrent.ConcurrentDictionary<System.Type, obj -> string>()

           let keyOf<'S> (state: 'S) : string =
               let extractor =
                   cache.GetOrAdd(typeof<'S>, fun t ->
                       if FSharpType.IsUnion(t, true) then
                           let tagReader = FSharpValue.PreComputeUnionTagReader(t)
                           let cases = FSharpType.GetUnionCases(t, true)
                           let caseNames = cases |> Array.map (fun c -> c.Name)
                           fun (o: obj) -> caseNames.[tagReader o]
                       else
                           fun (o: obj) -> o.ToString())
               extractor (box state)
       ```
     - Use `StateKeyExtractor.keyOf` in `InState`:
       ```fsharp
       member _.InState(spec: StatefulResourceSpec<'S, 'E, 'C>, stateHandlers: StateHandlers<'S>) =
           let key = StateKeyExtractor.keyOf stateHandlers.State
           let existing = Map.tryFind key spec.StateHandlerMap |> Option.defaultValue []
           { spec with StateHandlerMap = Map.add key (existing @ stateHandlers.Handlers) spec.StateHandlerMap }
       ```
     - Also use `StateKeyExtractor.keyOf` in `Run` for the `stateKey` function (replaces T001/T002 inline code):
       ```fsharp
       let stateKey (state: 'S) = StateKeyExtractor.keyOf state
       ```
  3. Update `stateMetadata` computation in `Run`. Currently it uses `spec.StateHandlerMap |> Map.map (fun _state handlers -> ...)` where `_state` was `'State`. Now it's `string`. This should work fine since the map iteration doesn't use the key value for anything except creating `StateInfo`.
  4. The `stateHandlerMap` variable in `Run` (around line 247-251) simplifies since the CE accumulator already uses string keys:
     ```fsharp
     let stateHandlerMap = spec.StateHandlerMap  // Already Map<string, handlers>
     ```
     The old `Map.toList |> List.map (fun (s, h) -> (s.ToString(), h)) |> Map.ofList` is no longer needed.
- **Edge Case**: If a user registers `inState (forState (Won "X") [...])` and `inState (forState (Won "O") [...])`, both map to key `"Won"` and their handlers merge. This is the desired behavior -- a single `inState` for `Won` with any parameter value covers all variants.

### Subtask T005 -- Add tests for parameterized DU state matching

- **Purpose**: Verify the core value proposition -- parameterized DU cases match a single handler.
- **Files**: `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
- **Parallel**: Yes (after T001-T004)
- **Steps**:
  1. The existing `TicTacToeState` type already has `Won of winner: string` which is perfect for testing.
  2. Add tests that:
     - Register a single handler for `Won "X"` (or any `Won` variant)
     - Trigger a transition to `Won "X"` -- verify the handler matches
     - Trigger a transition to `Won "O"` -- verify the SAME handler matches
     - Verify that `XTurn` does NOT match the `Won` handler
  3. Add a test with the full game flow: play moves until `Won "X"`, then GET to verify the `Won` handler returns the correct response.
  4. Add a test where both `Won "X"` and `Won "O"` handlers are registered separately via `inState` -- verify they merge into a single handler set for key `"Won"`.
- **Notes**: The existing test `buildGameResource` registers `inState (forState (Won "X") [...])`. After the change, this will match any `Won` variant. Existing tests may need the assertion strings updated if they checked for `Won "X"` as a key string.

### Subtask T006 -- Add tests for simple DU backward compatibility

- **Purpose**: Ensure non-parameterized DU cases work exactly as before.
- **Files**: `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
- **Parallel**: Yes (after T001-T004)
- **Steps**:
  1. Verify existing tests for `XTurn`, `OTurn`, `Draw` (simple cases) still pass
  2. Verify `Active`/`Completed` in `MiddlewareTests.fs` still work
  3. The `Locked`/`Unlocked` turnstile in `TypeTests.fs` should be unaffected
  4. Add explicit assertions that simple case key extraction produces the case name (e.g., `stateKey XTurn = "XTurn"`)
- **Notes**: Most existing tests should pass without modification. If any test asserts a specific key string from `ToString()`, verify it still matches the case name.

### Subtask T007 -- Verify `StatechartETagProvider` is NOT affected

- **Purpose**: Confirm the ETag provider still uses `string state` (full representation including parameters) for ETags, not the case-name key.
- **Files**: `test/Frank.Statecharts.Tests/StatechartETagProviderTests.fs`
- **Parallel**: Yes (after T001-T004)
- **Steps**:
  1. Review `src/Frank.Statecharts/StatechartETagProvider.fs` line 16: `let stateBytes = Encoding.UTF8.GetBytes(string state)` -- this uses `string state` which calls `ToString()`. This is CORRECT and should NOT change.
  2. Add a test that sets state to `Won "X"` and `Won "O"`, computes ETags for both, and verifies they are DIFFERENT (proving ETags are parameter-sensitive even though handler keys are not).
  3. Verify existing ETag tests still pass.
- **Notes**: The `StatechartETagProvider` does NOT use `stateKey` -- it uses `string state` directly. This is by design: ETags must be sensitive to the full state value.

## Risks & Mitigations

1. **`StateMetadata` map normalization**: The `StateMachine.StateMetadata: Map<'State, StateInfo>` field still uses `'State` keys at the type level. When building `StateMachineMetadata` in `Run`, the `stateMetadata` computation iterates this map. Since the map keys are now processed through `stateKey` in the CE accumulator, ensure the `stateMetadata` computation also uses string keys or is refactored to work with the string-keyed `StateHandlerMap` instead.

2. **Thread safety of `StateKeyExtractor` cache**: The `ConcurrentDictionary` is thread-safe. The `GetOrAdd` factory may be called multiple times concurrently for the same type (by design -- `ConcurrentDictionary.GetOrAdd` is not serialized), but the result is deterministic and idempotent.

3. **`getWithAffordances` test helper**: The `StatefulResourceTests.fs` helper `getWithAffordances` uses `myState.ToString()` to look up handlers in the `StateHandlerMap`. After this change, the map keys are case names, not `ToString()` values. The helper must be updated to use case-name keys.

## Review Guidance

- Verify that `PreComputeUnionTagReader` and `GetUnionCases` are called only once per state type (not per request)
- Verify the `stateKey` function is captured in closures correctly
- Check that `StateHandlerMap` key collisions for parameterized cases are handled (handlers merge, not overwrite)
- Confirm `StatechartETagProvider` is completely untouched
- Run `dotnet build` for all targets and `dotnet test` to confirm no regressions

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T00:05:00Z -- system -- lane=planned -- Prompt generated via /spec-kitty.tasks

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP01 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-16T04:02:08Z – claude-opus – shell_pid=97668 – lane=doing – Assigned agent via workflow command
- 2026-03-16T04:15:43Z – claude-opus – shell_pid=97668 – lane=for_review – Ready for review: StateKeyExtractor with PreComputeUnionTagReader replaces ToString(). All 262 tests pass across net8.0/net9.0/net10.0.
