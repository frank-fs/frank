---
work_package_id: "WP02"
subtasks:
  - "T005"
  - "T006"
  - "T007"
  - "T008"
  - "T009"
title: "Generator Native Annotations"
phase: "Phase 1 - Implementation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs: ["FR-005", "FR-006", "FR-007", "FR-008"]
history:
  - timestamp: "2026-03-18T05:39:36Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 – Generator Native Annotations

## ⚠️ IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

- Initial pseudo-state node has `Kind = Initial` (not `Regular`) with `SmcatAnnotation(SmcatStateType(Initial, Explicit))`
- Final pseudo-state nodes have `Kind = Final` with `SmcatAnnotation(SmcatStateType(Final, Explicit))`
- Regular states have NO `SmcatStateType` annotation (absence = `Regular, Inferred` default)
- Every transition carries `SmcatAnnotation(SmcatTransition ...)` with correct semantic role
- All existing generator tests pass (updated for new assertions)
- SC-001, SC-002, SC-004 from spec are satisfied

## Context & Constraints

- **Spec**: FR-005, FR-006, FR-007, FR-008
- **Research**: R-003 (pseudo-state identity), R-005 (generator transition inference)
- **Data model**: Annotation placement rules — generator section
- **Key decision (D-003)**: Identifiers `"initial"` and `"final"` are RETAINED. Only `Kind` and `Annotations` change.
- **Key decision (D-002)**: Regular states get NO `SmcatStateType` annotation.

### Current Generator State (pre-change)

The generator (`src/Frank.Statecharts/Smcat/Generator.fs`, 111 lines) currently:
- Creates all states with `Kind = Regular` and `Annotations = []`
- Creates initial/final as string-named states (`Identifier = Some "initial"`, `Kind = Regular`)
- Creates transitions with `Annotations = []`

### Target Generator State (post-change)

- Initial state: `Kind = Initial`, `Annotations = [SmcatAnnotation(SmcatStateType(Initial, Explicit))]`
- Final state: `Kind = Final`, `Annotations = [SmcatAnnotation(SmcatStateType(Final, Explicit))]`
- Regular states: `Kind = Regular`, `Annotations = []` (no SmcatStateType — default)
- Initial transition: `Annotations = [SmcatAnnotation(SmcatTransition InitialTransition)]`
- Self-transitions: `Annotations = [SmcatAnnotation(SmcatTransition SelfTransition)]`
- Final transitions: `Annotations = [SmcatAnnotation(SmcatTransition FinalTransition)]`

## Subtasks & Detailed Guidance

### Subtask T005 – Update initial pseudo-state node

- **Purpose**: The initial pseudo-state should declare its nature through `Kind` and annotations, not just its string name.
- **File**: `src/Frank.Statecharts/Smcat/Generator.fs`
- **Steps**:
  1. The generator does not currently create a `StateDecl` for the initial pseudo-state — it only creates a `TransitionElement` with `Source = "initial"`. A `StateDecl` is required (the serializer walks `StateDecl` entries for attribute emission).
  2. Add a dedicated initial state declaration BEFORE the ordered states:
     ```fsharp
     // Initial pseudo-state declaration
     let initialStateDecl =
         [ StateDecl
               { Identifier = Some "initial"
                 Label = None
                 Kind = Initial
                 Children = []
                 Activities = None
                 Position = Some syntheticPos
                 Annotations = [ SmcatAnnotation(SmcatStateType(Initial, Explicit)) ] } ]
     ```
  3. Ensure it appears first in `allElements`.
  4. Add `open Frank.Statecharts.Ast` imports if not already present (should be).
- **Parallel?**: No (sequential within Generator.fs).
- **Notes**: The current `stateElements` list (line 43-53) creates declarations for states from `orderedStates` — these are domain states, not pseudo-states. The initial pseudo-state is only represented by the `initialTransition` currently. You may need to add a new `StateDecl` for it.

### Subtask T006 – Update final pseudo-state nodes

- **Purpose**: Final state transitions currently target `Some "final"` but no `StateDecl` exists for the final pseudo-state. Add one with correct Kind and annotation.
- **File**: `src/Frank.Statecharts/Smcat/Generator.fs`
- **Steps**:
  1. Check if any domain states are marked final (`info.IsFinal`). For each, update the domain state's `StateDecl` — it should NOT become `Kind = Final` (the domain state is still a regular state that happens to be final in the machine). The `Kind = Final` belongs on the smcat `"final"` pseudo-state.
  2. Add a final pseudo-state `StateDecl` when any final transitions exist:
     ```fsharp
     let finalStateDecl =
         if finalTransitions.IsEmpty then []
         else
             [ StateDecl
                   { Identifier = Some "final"
                     Label = None
                     Kind = Final
                     Children = []
                     Activities = None
                     Position = Some syntheticPos
                     Annotations = [ SmcatAnnotation(SmcatStateType(Final, Explicit)) ] } ]
     ```
  3. Include in `allElements` after domain states and transitions.
- **Parallel?**: No.
- **Notes**: Domain states that are final in the machine (e.g., `Completed`) should remain `Kind = Regular` — they're regular states that happen to have a transition to the `"final"` pseudo-state. Only the pseudo-state itself is `Kind = Final`.

### Subtask T007 – Add `SmcatTransition` annotations to all transitions

- **Purpose**: Every generated transition should carry a `SmcatTransitionKind` annotation describing its semantic role.
- **File**: `src/Frank.Statecharts/Smcat/Generator.fs`
- **Steps**:
  1. Update `initialTransition` (lines 56-65):
     ```fsharp
     Annotations = [ SmcatAnnotation(SmcatTransition InitialTransition) ]
     ```
  2. Update `transitionElements` self-transitions (lines 69-84):
     ```fsharp
     Annotations = [ SmcatAnnotation(SmcatTransition SelfTransition) ]
     ```
  3. Update `finalTransitions` (lines 87-101):
     ```fsharp
     Annotations = [ SmcatAnnotation(SmcatTransition FinalTransition) ]
     ```
  4. If the generator ever produces external transitions (state → different state), annotate with `ExternalTransition`. Currently the generator only produces self-transitions for HTTP methods, so this may not apply yet.
- **Parallel?**: No.

### Subtask T008 – Update GeneratorTests.fs

- **Purpose**: Verify the generator produces correct Kind values and annotations.
- **File**: `test/Frank.Statecharts.Tests/Smcat/GeneratorTests.fs`
- **Steps**:
  1. Update `"emits initial transition"` test: add assertions for the initial state declaration (if added in T005) — `Kind = Initial`, annotations contain `SmcatStateType(Initial, Explicit)`. Also verify the initial transition has `SmcatTransition InitialTransition`.
  2. Update `"emits self-transitions for each HTTP method"` test: verify self-transitions have `SmcatTransition SelfTransition`.
  3. Update `"emits final state transitions"` test: verify final transition has `SmcatTransition FinalTransition`. Verify final pseudo-state declaration (if added in T006) has `Kind = Final`.
  4. Update `"single state, no handlers"` test: verify initial transition annotation.
  5. Update `"serialized output contains all elements"` test: this uses `generateText` which serializes — output assertions may change if the serializer now emits type attributes (but serializer changes are in WP03, so this test should still pass with current serializer behavior).
  6. Add a NEW test: `"regular states have no SmcatStateType annotation"` — verify domain states (e.g., "Idle", "Running") have `Annotations = []` (no SmcatStateType, since they're Regular/Inferred default).
- **Parallel?**: No (depends on T005-T007).
- **Notes**: Use pattern matching to extract annotations:
  ```fsharp
  let hasSmcatStateType kind origin (annotations: Annotation list) =
      annotations |> List.exists (function
          | SmcatAnnotation(SmcatStateType(k, o)) -> k = kind && o = origin
          | _ -> false)

  let hasSmcatTransition expected (annotations: Annotation list) =
      annotations |> List.exists (function
          | SmcatAnnotation(SmcatTransition tk) -> tk = expected
          | _ -> false)
  ```

### Subtask T009 – Verify build and tests

- **Purpose**: Full build and test validation.
- **Steps**:
  1. `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`
  2. `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
  3. All tests must pass, including updated generator tests.

## Risks & Mitigations

- **Initial pseudo-state StateDecl**: The current generator may not create a `StateDecl` for initial — only a `TransitionElement`. If smcat serializer requires a `StateDecl` for every state referenced by a transition, you need to add one. Check by examining the serializer's behavior with missing state declarations.
- **Test ordering**: Generator tests reference `ts[0]`, `ts[1]` by index. Adding new elements may shift indices. Use `List.tryFind` with predicates instead of index access where possible.

## Review Guidance

- Verify `Kind = Initial` on initial pseudo-state (not Regular)
- Verify `Kind = Final` on final pseudo-state (not Regular)
- Verify `Kind = Regular` on domain states (even final-in-machine states like `Completed`)
- Verify every transition has exactly one `SmcatTransition` annotation
- Verify regular states have NO `SmcatStateType` annotation
- Run `dotnet test` — all green

## Activity Log

- 2026-03-18T05:39:36Z – system – lane=planned – Prompt created.
