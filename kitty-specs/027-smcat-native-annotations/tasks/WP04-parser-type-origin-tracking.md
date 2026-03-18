---
work_package_id: WP04
title: Parser Type Origin Tracking
lane: "doing"
dependencies: [WP01]
base_branch: 027-smcat-native-annotations-WP01
base_commit: 1a657d3121f0d1d274c9aa8cae2459565738e29a
created_at: '2026-03-18T06:13:45.168634+00:00'
subtasks:
- T014
- T015
- T016
- T017
- T018
phase: Phase 1 - Implementation
assignee: ''
agent: "claude-opus"
shell_pid: "37896"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T05:39:36Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-012, FR-013, FR-014]
---

# Work Package Prompt: WP04 – Parser Type Origin Tracking

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
spec-kitty implement WP04 --base WP01
```

---

## Objectives & Success Criteria

- Parser stores `SmcatAnnotation(SmcatStateType(kind, Explicit))` when `[type="..."]` attribute is in source
- Parser stores `SmcatAnnotation(SmcatStateType(kind, Inferred))` when type inferred from naming convention AND kind is not Regular
- Parser omits `SmcatStateType` annotation for Regular/Inferred states (default)
- Parser stores `SmcatAnnotation(SmcatTransition ...)` on every parsed transition
- All existing parser tests pass (updated with new annotation assertions)
- FR-012, FR-013, FR-014 from spec are satisfied

## Context & Constraints

- **Spec**: FR-012, FR-013, FR-014
- **Research**: R-002 (annotation density rule), R-006 (parser transition inference)
- **Data model**: Annotation placement rules — parser sections
- **File**: `src/Frank.Statecharts/Smcat/Parser.fs` (894 lines) — surgical changes only
- **Key decision**: The `type` attribute is BOTH consumed by `inferStateType` for `StateKind` (existing behavior preserved) AND stored as a `SmcatStateType` annotation (new behavior)

### Parser Architecture (relevant sections)

- **Lines 370-377**: Attribute-to-annotation conversion. Currently:
  - `color` → `SmcatAnnotation(SmcatColor value)`
  - `label` → `SmcatAnnotation(SmcatStateLabel value)` + sets `Label`
  - `type` → consumed by `inferStateType`, NOT stored as annotation ← CHANGE THIS
  - other → `SmcatAnnotation(SmcatCustomAttribute(key, value))`

- **Lines 50-77** (`Smcat/Types.fs`): `inferStateType` function. Returns `StateKind` based on naming convention or `[type="..."]` attribute. This function is NOT modified — it continues to work as before.

- **Lines ~380-400**: State construction. After parsing attributes and inferring type, constructs `StateNode` with `Kind` and `Annotations`.

- **Lines ~490-520, ~640-660**: Transition construction points.

### Transition Kind Inference Rules (from research R-006)

| Condition | SmcatTransitionKind |
|-----------|---------------------|
| `Source = "initial"` | `InitialTransition` |
| `Source = Target` (self-loop) | `SelfTransition` |
| `Target = Some "final"` | `FinalTransition` |
| Inside composite `{...}` block | `InternalTransition` |
| All other cases | `ExternalTransition` |

## Subtasks & Detailed Guidance

### Subtask T014 – Store `SmcatStateType(kind, Explicit)` for explicit type attributes

- **Purpose**: When the source text has `[type="..."]`, the parser should store this as an annotation so the serializer can round-trip it.
- **File**: `src/Frank.Statecharts/Smcat/Parser.fs`
- **Steps**:
  1. Locate the attribute-to-annotation conversion (around line 370-377). The current logic skips the `type` key:
     ```fsharp
     // Current: type attribute is consumed by inferStateType, not stored
     | "type" -> ()  // or it's filtered out before annotation construction
     ```
  2. Change to: store `type` as annotation AND continue to consume it for `Kind` inference:
     ```fsharp
     // After inferring StateKind from type attribute:
     // If type attribute was present, add SmcatAnnotation(SmcatStateType(inferredKind, Explicit))
     ```
  3. The implementation depends on how the parser structures the attribute processing. The key requirement is:
     - Track whether a `type` attribute was found in the parsed attributes
     - After `inferStateType` resolves the `StateKind`, if `type` was present: add `SmcatAnnotation(SmcatStateType(kind, Explicit))`
  4. One approach: add a `hasExplicitType` boolean tracked during attribute processing, then use it after `inferStateType`:
     ```fsharp
     let hasExplicitType =
         attributes |> List.exists (fun a -> a.Key.Equals("type", StringComparison.OrdinalIgnoreCase))
     let stateKind = inferStateType name attributes
     let typeAnnotation =
         if hasExplicitType then
             [ SmcatAnnotation(SmcatStateType(stateKind, Explicit)) ]
         elif stateKind <> Regular then
             [ SmcatAnnotation(SmcatStateType(stateKind, Inferred)) ]
         else
             []  // Regular, Inferred — omit (default)
     ```
     Then prepend `typeAnnotation` to the existing annotations list.
- **Parallel?**: No.
- **Notes**: This subtask and T015 are closely related — the `elif` branch in the code above is T015. They're separated for clarity but will likely be implemented in the same code block.

### Subtask T015 – Store `SmcatStateType(kind, Inferred)` for non-Regular naming convention

- **Purpose**: When a state's type is inferred from naming convention (not from an attribute), store the annotation so consumers know the origin.
- **File**: `src/Frank.Statecharts/Smcat/Parser.fs`
- **Steps**:
  1. This is the `elif` branch from T014's code:
     ```fsharp
     elif stateKind <> Regular then
         [ SmcatAnnotation(SmcatStateType(stateKind, Inferred)) ]
     ```
  2. States whose kind is `Regular` and type is inferred (the vast majority) get NO annotation — this is the universal default per clarification Q3.
  3. Examples of Inferred annotations:
     - State named `"initial"` with no `[type=...]` → `SmcatStateType(Initial, Inferred)`
     - State named `"^myChoice"` → `SmcatStateType(Choice, Inferred)`
     - State named `"]forkJoin"` → `SmcatStateType(ForkJoin, Inferred)`
     - State named `"myFinal"` containing "final" → `SmcatStateType(Final, Inferred)`
- **Parallel?**: No (implemented together with T014).

### Subtask T016 – Add `SmcatTransition` annotations to parsed transitions

- **Purpose**: Every parsed transition should carry a `SmcatTransitionKind` annotation describing its semantic role.
- **File**: `src/Frank.Statecharts/Smcat/Parser.fs`
- **Steps**:
  1. Locate transition construction points in the parser. Transitions are created in multiple places:
     - After parsing `source => target: label;` syntax
     - The transition is constructed as a `TransitionEdge` record
  2. At each transition construction point, infer the `SmcatTransitionKind`:
     ```fsharp
     let transitionKind =
         if source = "initial" then InitialTransition
         elif target = Some "final" then FinalTransition
         elif target = Some source then SelfTransition
         elif isInsideComposite then InternalTransition  // need to track this
         else ExternalTransition
     ```
  3. Add the annotation:
     ```fsharp
     Annotations = existingAnnotations @ [ SmcatAnnotation(SmcatTransition transitionKind) ]
     ```
  4. For `isInsideComposite`: the parser tracks nesting depth during composite state `{...}` parsing. When parsing transitions inside a composite block, use depth > 0 as the signal. Check how the parser tracks this — look at the recursive `parseElements` or composite state handling around lines 788-823.
  5. Be careful with edge cases:
     - `initial => final;` → `InitialTransition` (initial takes precedence)
     - Self-loop `a => a;` → `SelfTransition`
     - `a => final;` where `a` is not `"initial"` → `FinalTransition`
- **Parallel?**: Yes (independent from T014/T015 — transitions vs states).
- **Notes**: The parser may construct transitions in multiple code paths (normal transitions, error recovery transitions). Ensure ALL construction sites get the annotation. Search for `TransitionElement` construction in Parser.fs.

### Subtask T017 – Update ParserTests.fs

- **Purpose**: Verify the parser produces correct `SmcatStateType` and `SmcatTransition` annotations.
- **File**: `test/Frank.Statecharts.Tests/Smcat/ParserTests.fs`
- **Steps**:
  1. Update `"state with type attribute overrides naming"` test (line 337-341): add assertion that annotations contain `SmcatAnnotation(SmcatStateType(Initial, Explicit))`.
  2. Update `"state label and color"` test (line 220-232): verify the state has `SmcatStateType(Regular, Inferred)` absent (no SmcatStateType annotation for regular states).
  3. Add NEW test: `"explicit type attribute stored as annotation"`:
     ```fsharp
     testCase "explicit type attribute stored as annotation"
     <| fun _ ->
         let result = parseSmcat "myState [type=\"initial\"];"
         let ss = states result
         let typeAnnotation =
             ss[0].Annotations |> List.tryPick (function
                 | SmcatAnnotation(SmcatStateType(k, o)) -> Some(k, o)
                 | _ -> None)
         Expect.equal typeAnnotation (Some(Initial, Explicit)) "explicit type annotation"
     ```
  4. Add NEW test: `"inferred type from naming convention"`:
     ```fsharp
     testCase "inferred type from naming convention"
     <| fun _ ->
         let result = parseSmcat "initial;"
         let ss = states result
         let typeAnnotation =
             ss[0].Annotations |> List.tryPick (function
                 | SmcatAnnotation(SmcatStateType(k, o)) -> Some(k, o)
                 | _ -> None)
         Expect.equal typeAnnotation (Some(Initial, Inferred)) "inferred type annotation"
     ```
  5. Add NEW test: `"regular state has no SmcatStateType annotation"`:
     ```fsharp
     testCase "regular state has no SmcatStateType annotation"
     <| fun _ ->
         let result = parseSmcat "idle;"
         let ss = states result
         let hasStateType =
             ss[0].Annotations |> List.exists (function
                 | SmcatAnnotation(SmcatStateType _) -> true
                 | _ -> false)
         Expect.isFalse hasStateType "regular state has no SmcatStateType"
     ```
  6. Add NEW test: `"transition has SmcatTransition annotation"`:
     ```fsharp
     testCase "transition has SmcatTransition annotation"
     <| fun _ ->
         let result = parseSmcat "a => b: go;"
         let ts = transitions result
         let transKind =
             ts[0].Annotations |> List.tryPick (function
                 | SmcatAnnotation(SmcatTransition k) -> Some k
                 | _ -> None)
         Expect.equal transKind (Some ExternalTransition) "external transition"
     ```
  7. Add NEW test: `"initial transition annotation"`:
     ```fsharp
     testCase "initial transition annotation"
     <| fun _ ->
         let result = parseSmcat "initial => start;"
         let ts = transitions result
         let transKind =
             ts[0].Annotations |> List.tryPick (function
                 | SmcatAnnotation(SmcatTransition k) -> Some k
                 | _ -> None)
         Expect.equal transKind (Some InitialTransition) "initial transition"
     ```
- **Parallel?**: No (depends on T014-T016).

### Subtask T018 – Verify build and tests

- **Purpose**: Full build and test validation.
- **Steps**:
  1. `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`
  2. `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
  3. All tests must pass, including new parser annotation tests.

## Risks & Mitigations

- **Parser complexity**: Parser.fs is 894 lines with multiple code paths. Changes must be surgical — only add annotation construction, don't restructure parsing logic.
- **Multiple transition construction sites**: Search for ALL `TransitionElement` constructors in Parser.fs. Missing one means some transitions lack annotations.
- **Composite state nesting**: The `InternalTransition` inference depends on detecting composite block context. If the parser doesn't expose nesting depth cleanly, use a simpler heuristic: transitions whose source AND target are both children of the same composite state get `InternalTransition`.

## Review Guidance

- Verify `[type="initial"]` → `SmcatStateType(Initial, Explicit)` annotation present
- Verify `initial;` (no attribute) → `SmcatStateType(Initial, Inferred)` annotation present
- Verify `idle;` → NO SmcatStateType annotation
- Verify `myState [type="regular"];` → `SmcatStateType(Regular, Explicit)` annotation present
- Verify every `TransitionElement` in parser output has a `SmcatTransition` annotation
- Verify `initial => start;` → `InitialTransition`
- Verify `a => a: GET;` → `SelfTransition`
- Verify `a => final;` → `FinalTransition`
- Run `dotnet test` — all green

## Activity Log

- 2026-03-18T05:39:36Z – system – lane=planned – Prompt created.
- 2026-03-18T06:13:45Z – claude-opus – shell_pid=37896 – lane=doing – Assigned agent via workflow command
