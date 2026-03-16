---
work_package_id: WP05
title: Generator
lane: "doing"
dependencies:
- WP01
- WP02
base_branch: 013-smcat-parser-generator-WP05-merge-base
base_commit: 8129dd5d9159bf6609e884a7f65a3050b2f1c4b6
created_at: '2026-03-16T04:25:25.875918+00:00'
subtasks:
- T025
- T026
- T027
- T028
- T029
phase: Phase 2 - Generation
assignee: ''
agent: "claude-opus-4-6"
shell_pid: "9648"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:14Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-009]
---

# Work Package Prompt: WP05 -- Generator

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP05 --base WP01
```

Depends on WP01 (Types.fs for TransitionLabel types and StateMachineMetadata access). Does NOT depend on WP02/WP03 -- the generator works from `StateMachineMetadata`, not from the parser.

---

## Objectives & Success Criteria

- Implement `src/Frank.Statecharts/Smcat/Generator.fs` that produces valid smcat text from `StateMachineMetadata`
- Handle initial state transitions (emitted first), regular transitions, and final state transitions (emitted last)
- Format transition labels correctly: `event [guard] / action` with optional components omitted
- Create generator tests covering all User Story 3 acceptance scenarios

**Done when**: `dotnet test --filter "Smcat.Generator"` passes. Generated smcat text for known metadata matches expected output and is valid smcat syntax.

## Context & Constraints

- **Spec**: User Story 3 (generate smcat from metadata), FR-009
- **Research**: R-008 (generator output format)
- **API Signatures**: `contracts/api-signatures.md` -- `generate` function signature with generic type parameters
- **Data Model**: `data-model.md` -- Generator Output Model section

**Key constraints**:
- Module declaration: `module internal Frank.Statecharts.Smcat.Generator`
- `StateMachineMetadata` stores transitions as closures -- caller must provide explicit `transitions` list, `stateNames` function, and `eventNames` function
- Output: one statement per line, semicolon terminators
- Ordering: initial transition first, regular transitions in provided order, final transitions last
- Access to `StateMachineMetadata<'State, 'Event, 'Context>` from `Frank.Statecharts` (the runtime Types.fs)

**Important type reference**: The `StateMachineMetadata` type is defined in `src/Frank.Statecharts/Types.fs` (NOT Smcat/Types.fs). It includes:
- `Guards: Guard<'State, 'Event, 'Context> list` (where `Guard` has a `Name` field)
- `InitialState: 'State`
- `StateMetadata: StateInfo<'State> list` (where `StateInfo` has `State` and `IsFinal` fields)

You will need to read `src/Frank.Statecharts/Types.fs` to understand the exact field names and types.

## Subtasks & Detailed Guidance

### Subtask T025 -- Create `src/Frank.Statecharts/Smcat/Generator.fs`

**Purpose**: Implement the generator module with the API specified in contracts/api-signatures.md.

**Steps**:

1. Replace the stub with the full module:
   ```fsharp
   module internal Frank.Statecharts.Smcat.Generator

   open Frank.Statecharts.Smcat.Types
   ```

2. Implement the `generate` function:
   ```fsharp
   let generate<'State, 'Event, 'Context when 'State : equality and 'State : comparison>
       (metadata: StateMachineMetadata<'State, 'Event, 'Context>)
       (stateNames: 'State -> string)
       (eventNames: 'Event -> string)
       (transitions: ('State * 'Event * 'State) list)
       : string =
   ```

   **Note**: You may need to open `Frank.Statecharts` (or the specific namespace where `StateMachineMetadata` is defined) to access the metadata type. Check `src/Frank.Statecharts/Types.fs` for the exact module/namespace.

3. **Build output using `StringBuilder`** (or `ResizeArray<string>` joined at the end) for performance (SC-008 -- no intermediate string concatenation):
   ```fsharp
   let lines = ResizeArray<string>()
   // ... add lines ...
   lines |> String.concat "\n"
   ```

4. **Processing steps**:
   a. Identify initial state from `metadata.InitialState`
   b. Identify final states from `metadata.StateMetadata` where `IsFinal = true`
   c. Build guard lookup: for each transition, find matching guard by checking `metadata.Guards`
   d. Emit initial transition line
   e. Emit regular transition lines
   f. Emit final state transition lines

**Files**: `src/Frank.Statecharts/Smcat/Generator.fs` (~120-150 lines)

---

### Subtask T026 -- Implement initial state handling

**Purpose**: Emit `initial => <first_state>;` as the first line when an initial state exists, per R-008 conventions.

**Steps**:

1. Get the initial state name: `stateNames metadata.InitialState`

2. Emit the initial transition:
   ```fsharp
   lines.Add(sprintf "initial => %s;" (stateNames metadata.InitialState))
   ```

3. **Edge case**: If the initial state is also a target of a transition in the `transitions` list, do NOT duplicate it. The `initial => X;` line is always emitted separately from regular transitions.

4. **Edge case**: If the initial state name itself contains special characters or spaces, it should be quoted: `initial => "complex name";`. Use a helper to determine if quoting is needed:
   ```fsharp
   let needsQuoting (name: string) =
       name |> Seq.exists (fun c -> not (System.Char.IsLetterOrDigit c || c = '_' || c = '.' || c = '-'))

   let quoteName (name: string) =
       if needsQuoting name then sprintf "\"%s\"" name else name
   ```

**Files**: `src/Frank.Statecharts/Smcat/Generator.fs`

---

### Subtask T027 -- Implement transition label formatting

**Purpose**: Format transition labels as `event [guard] / action` with correct omission of absent components.

**Steps**:

1. **Create a label formatting function**:
   ```fsharp
   let formatLabel (eventName: string option) (guardName: string option) (actionName: string option) : string option =
       // Returns None if all components are absent (no label needed)
       // Returns Some "formatted label" if any component is present
   ```

2. **Formatting rules** (each component independently optional):
   | Event | Guard | Action | Output |
   |-------|-------|--------|--------|
   | Some e | None | None | `"e"` |
   | Some e | Some g | None | `"e [g]"` |
   | Some e | None | Some a | `"e / a"` |
   | Some e | Some g | Some a | `"e [g] / a"` |
   | None | Some g | None | `"[g]"` |
   | None | Some g | Some a | `"[g] / a"` |
   | None | None | Some a | `"/ a"` |
   | None | None | None | No label |

3. **Build full transition line**:
   ```fsharp
   let formatTransition (source: string) (target: string) (label: string option) : string =
       match label with
       | Some l -> sprintf "%s => %s: %s;" (quoteName source) (quoteName target) l
       | None -> sprintf "%s => %s;" (quoteName source) (quoteName target)
   ```

4. **Guard name lookup**: For each `(source, event, target)` transition triple, find the matching guard in `metadata.Guards`:
   - Guards have a `Name` property
   - The guard may be associated with the transition via the event/state pair
   - **Check the actual `Guard` type definition** in `src/Frank.Statecharts/Types.fs` to determine how guards are associated with specific transitions

5. **Action names**: smcat actions are informational annotations. In the generator context, actions may not be directly available from `StateMachineMetadata`. If guard/action information is not available for a transition, omit those components from the label.

**Files**: `src/Frank.Statecharts/Smcat/Generator.fs`

---

### Subtask T028 -- Implement final state handling

**Purpose**: Emit `<source> => final;` transitions for states that are marked as final in the metadata.

**Steps**:

1. **Identify final states**:
   ```fsharp
   let finalStates =
       metadata.StateMetadata
       |> List.filter (fun si -> si.IsFinal)
       |> List.map (fun si -> stateNames si.State)
   ```

2. **Find transitions leading to final states**: Look through the `transitions` list for any transition whose target is a final state. For those transitions, the target in the smcat output should be `"final"` (the pseudo-state name).

3. **Handle states that ARE final but have no incoming transitions**: If a state is marked `IsFinal` but nothing transitions INTO it (unusual but possible), emit a bare `<state> => final;` line.

4. **Ordering**: Final transitions are emitted after all regular transitions, per R-008.

5. **Example**: If `completed` is a final state with a transition `running => completed: finish;`, the output should include both:
   ```
   running => completed: finish;
   completed => final;
   ```

**Files**: `src/Frank.Statecharts/Smcat/Generator.fs`

**Notes**: The `final` pseudo-state is a synthetic construct in smcat. It does not exist as a real state in the metadata. The generator creates the `=> final;` transition to represent the `IsFinal` flag.

---

### Subtask T029 -- Create `test/Frank.Statecharts.Tests/Smcat/GeneratorTests.fs`

**Purpose**: Tests covering all User Story 3 acceptance scenarios and label formatting edge cases.

**Steps**:

1. Replace the stub with test cases.

2. **Setup**: Create test helper to build simple `StateMachineMetadata` instances. You'll need to reference the types from `Frank.Statecharts` (the runtime module). Study existing tests in `test/Frank.Statecharts.Tests/` for how they construct metadata.

   **Important**: `StateMachineMetadata` likely requires constructing guards, transition functions, and state info. Look at existing test files like `StatefulResourceTests.fs` for patterns.

3. **Acceptance scenario tests from spec**:

   a. States `idle`, `running`, `stopped` with initial state `idle`:
      - Verify output contains `"initial => idle;"`

   b. Transition from `idle` to `running` with event `start`, guard `isReady`, action `logStart`:
      - Verify output contains `"idle => running: start [isReady] / logStart;"`

   c. Final state `completed`:
      - Verify output contains a transition to `"final"`

   d. Transitions with no guards or actions:
      - Verify labels contain only the event name

4. **Label formatting tests**:
   - Event only: verify `"event"` (no brackets, no slash)
   - Event + guard: verify `"event [guard]"` (no slash)
   - Event + action: verify `"event / action"` (no brackets)
   - All three: verify `"event [guard] / action"`
   - No label: verify just `"source => target;"` (no colon)

5. **Edge cases**:
   - No transitions in list: output is just the initial state line
   - State names needing quoting: `"a state"` -> quoted in output
   - No initial state: initial transition line is omitted (if applicable)
   - No final states: no final transition lines

6. **Define test state/event types**:
   ```fsharp
   type TestState = Idle | Running | Stopped | Completed
   type TestEvent = Start | Stop | Finish
   ```

7. Use Expecto pattern:
   ```fsharp
   module Smcat.GeneratorTests

   open Expecto
   open Frank.Statecharts.Smcat.Generator

   [<Tests>]
   let generatorTests = testList "Smcat.Generator" [ ... ]
   ```

**Files**: `test/Frank.Statecharts.Tests/Smcat/GeneratorTests.fs` (~200-250 lines)

**Parallel?**: Yes -- can be written alongside T025-T028 implementation.

---

## Test Strategy

Run generator tests:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat.Generator"
```

Verify no regressions:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
```

## Risks & Mitigations

- **StateMachineMetadata construction in tests**: Building metadata objects may be complex if the type requires closures for transition functions. Study existing test patterns. If construction is too complex, consider creating a test helper module.
- **Guard association model**: The way guards are linked to specific transitions depends on the `Guard<'State, 'Event, 'Context>` type definition. Read `src/Frank.Statecharts/Types.fs` carefully to understand the association model.
- **Generated output must be re-parseable**: While roundtrip tests are in WP06, keep this in mind during generation -- use valid smcat syntax at all times.
- **String quoting**: State/event names containing spaces or special characters must be quoted in the output. The `needsQuoting`/`quoteName` helper ensures this.

## Review Guidance

- Verify all 4 acceptance scenarios from User Story 3 have corresponding tests
- Verify label formatting handles all 7 combinations of event/guard/action presence
- Verify initial transition is emitted first and final transitions last
- Verify state names with special characters are properly quoted
- Check that output uses semicolon terminators and one statement per line
- Verify `dotnet test --filter "Smcat.Generator"` passes

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:14Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:
1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP05 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-16T04:25:26Z – claude-opus-4-6 – shell_pid=9648 – lane=doing – Assigned agent via workflow command
