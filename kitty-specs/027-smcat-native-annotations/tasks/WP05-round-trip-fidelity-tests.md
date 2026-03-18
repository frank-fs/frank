---
work_package_id: "WP05"
subtasks:
  - "T019"
  - "T020"
  - "T021"
  - "T022"
  - "T023"
title: "Round-Trip Fidelity Tests"
phase: "Phase 2 - Validation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP02", "WP03", "WP04"]
requirement_refs: ["FR-015", "FR-016", "FR-017"]
history:
  - timestamp: "2026-03-18T05:39:36Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP05 – Round-Trip Fidelity Tests

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
spec-kitty implement WP05 --base WP02 --base WP03 --base WP04
```

Note: If the implement command only supports a single `--base`, merge WP02-04 first or implement WP05 after all three are merged to the target branch.

---

## Objectives & Success Criteria

- New golden files exercise explicit types, inferred types, colors, custom attributes, activities, composite states
- `assertStructuralEquivalence` function compares full AST including annotations
- Round-trip helper runs both semantic and structural comparison
- All round-trip tests pass: parse → serialize → parse produces structurally equivalent ASTs
- FR-015 and SC-003 from spec are satisfied

## Context & Constraints

- **Spec**: FR-015 (round-trip fidelity), SC-003 (structural equivalence)
- **File**: `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs` (301 lines) — extend, don't replace
- **Existing infrastructure**: `roundtrip` helper, `assertSemanticEquivalence`, golden files
- **Key decision (D-005)**: Extend existing file with structural comparison. No new test files.

### Existing Round-Trip Test Structure

The file has:
- 3 golden files (simple linear, branching with guards, composite states)
- `extractStateSet` / `extractTransitionSet` for semantic comparison
- `assertSemanticEquivalence` (state sets + transition sets, ignores annotations)
- `roundtrip` helper (parse → serialize → parse → semantic compare)
- Edge case tests, semantic equivalence tests, success criteria tests

### What's Needed

- New golden files with annotation-exercising smcat
- `assertStructuralEquivalence` that includes annotation comparison
- Updated `roundtrip` to run both semantic and structural checks
- New test cases focused on annotation preservation

## Subtasks & Detailed Guidance

### Subtask T019 – Add new golden files

- **Purpose**: Create representative smcat text that exercises the full annotation system.
- **File**: `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs`
- **Steps**:
  1. Add golden file for explicit type attributes:
     ```fsharp
     /// Golden File 4: Explicit type attributes and colors.
     /// Tests: SmcatStateType Explicit round-trip, SmcatColor, multiple attributes.
     let goldenExplicitTypes =
         """myStart [type="initial"];
     idle [color="green"];
     processing;
     done [type="final"];
     myStart => idle: begin;
     idle => processing: submit;
     processing => done: complete;"""
     ```
  2. Add golden file for naming convention (inferred types):
     ```fsharp
     /// Golden File 5: Naming convention types (inferred).
     /// Tests: SmcatStateType Inferred round-trip, pseudo-state names.
     let goldenInferredTypes =
         """initial => active: start;
     active => active: refresh;
     active => final: shutdown;"""
     ```
  3. Add golden file for activities and custom attributes:
     ```fsharp
     /// Golden File 6: Activities and custom attributes.
     /// Tests: StateActivities, SmcatCustomAttribute round-trip.
     let goldenActivitiesAndAttributes =
         """idle: entry/ initialize exit/ cleanup;
     working [priority="high"];
     idle => working: begin;
     working => idle: finish;"""
     ```
  4. Add golden file with composite states and internal transitions:
     ```fsharp
     /// Golden File 7: Composite states with internal transitions.
     /// Tests: SmcatTransitionKind InternalTransition, nested states.
     let goldenCompositeAnnotations =
         """initial => machine: start;
     machine {
       idle;
       running;
       idle => running: go;
       running => idle: stop;
     };
     machine => final: shutdown;"""
     ```
- **Parallel?**: Yes (independent from T020).
- **Notes**: Golden files should be valid smcat that parses without errors. Use the existing golden files as format examples.

### Subtask T020 – Implement `assertStructuralEquivalence`

- **Purpose**: Compare two `StatechartDocument` ASTs including annotations, not just state/transition topology.
- **File**: `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs`
- **Steps**:
  1. Create a helper to normalize annotations for comparison (set-based, not list-order-sensitive):
     ```fsharp
     /// Normalize annotations to a set for order-independent comparison.
     let private normalizeAnnotations (annotations: Annotation list) : Set<Annotation> =
         Set.ofList annotations
     ```
     Note: This requires `Annotation` to support structural comparison. F# DUs support this by default if all payload types do. `StateKind`, `SmcatTypeOrigin`, `SmcatTransitionKind` are all simple DUs — structural comparison works.
  2. Create a structural state extractor that includes annotations:
     ```fsharp
     /// Extract (name, kind, annotations) tuples from document states.
     let rec private extractAnnotatedStateSet (doc: StatechartDocument)
         : Set<string * StateKind * Set<Annotation>> =
         doc.Elements
         |> List.collect (fun el ->
             match el with
             | StateDecl s ->
                 let childStates = extractAnnotatedStateSetFromChildren s.Children
                 (s.Identifier |> Option.defaultValue "", s.Kind, normalizeAnnotations s.Annotations)
                 :: childStates
             | _ -> [])
         |> Set.ofList
     ```
  3. Create a structural transition extractor that includes annotations:
     ```fsharp
     /// Extract (source, target, event, guard, action, annotations) tuples.
     let rec private extractAnnotatedTransitionSet (doc: StatechartDocument)
         : Set<string * string option * string option * string option * string option * Set<Annotation>> =
         doc.Elements
         |> List.collect (fun el ->
             match el with
             | TransitionElement t ->
                 [ (t.Source, t.Target, t.Event, t.Guard, t.Action, normalizeAnnotations t.Annotations) ]
             | StateDecl s ->
                 extractAnnotatedTransitionSetFromChildren s.Children
             | _ -> [])
         |> Set.ofList
     ```
  4. Implement `assertStructuralEquivalence`:
     ```fsharp
     let private assertStructuralEquivalence (doc1: StatechartDocument) (doc2: StatechartDocument) =
         let states1 = extractAnnotatedStateSet doc1
         let states2 = extractAnnotatedStateSet doc2
         Expect.equal states1 states2 "Annotated state sets should be structurally equivalent"

         let transitions1 = extractAnnotatedTransitionSet doc1
         let transitions2 = extractAnnotatedTransitionSet doc2
         Expect.equal transitions1 transitions2 "Annotated transition sets should be structurally equivalent"
     ```
  5. Don't forget the `...FromChildren` recursive helpers (mirror the existing pattern).
- **Parallel?**: Yes (independent from T019).
- **Notes**: Structural comparison is strictly stronger than semantic comparison. If structural passes, semantic also passes. We keep both because semantic comparison produces clearer error messages when debugging failures (you can see whether you lost a state or just an annotation).

### Subtask T021 – Update `roundtrip` helper

- **Purpose**: Run both semantic and structural comparison in the round-trip cycle.
- **File**: `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs`
- **Steps**:
  1. Update the existing `roundtrip` function (line 110) to include structural comparison:
     ```fsharp
     let private roundtrip (smcatText: string) =
         let result1 = parseSmcat smcatText
         Expect.isEmpty result1.Errors (sprintf "Original parse should have no errors, got: %A" result1.Errors)

         let generatedText = serialize result1.Document

         let result2 = parseSmcat generatedText
         Expect.isEmpty result2.Errors
             (sprintf "Re-parsed output should have no errors, got: %A\nGenerated text:\n%s" result2.Errors generatedText)

         // Semantic comparison (topology)
         assertSemanticEquivalence result1.Document result2.Document
         // Structural comparison (includes annotations)
         assertStructuralEquivalence result1.Document result2.Document
     ```
  2. This means ALL existing round-trip tests now also verify annotation preservation. Some may fail if the parser/serializer don't perfectly round-trip annotations for the existing golden files. If so, investigate — this is the round-trip fidelity test catching real issues.
- **Parallel?**: No (depends on T019 and T020).

### Subtask T022 – Add annotation-focused round-trip test cases

- **Purpose**: Test cases specifically designed to exercise annotation preservation.
- **File**: `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs`
- **Steps**:
  1. Add a new test list for annotation round-trips:
     ```fsharp
     [<Tests>]
     let annotationRoundTripTests =
         testList
             "Smcat.RoundTrip.Annotations"
             [ testCase "golden file 4 - explicit types and colors"
               <| fun _ -> roundtrip goldenExplicitTypes

               testCase "golden file 5 - inferred types"
               <| fun _ -> roundtrip goldenInferredTypes

               testCase "golden file 6 - activities and custom attributes"
               <| fun _ -> roundtrip goldenActivitiesAndAttributes

               testCase "golden file 7 - composite annotations"
               <| fun _ -> roundtrip goldenCompositeAnnotations

               testCase "explicit type survives round-trip"
               <| fun _ ->
                   let result1 = parseSmcat "myState [type=\"initial\"];"
                   let generated = serialize result1.Document
                   Expect.stringContains generated "type=\"initial\"" "explicit type preserved in output"
                   let result2 = parseSmcat generated
                   assertStructuralEquivalence result1.Document result2.Document

               testCase "inferred type does not gain explicit attribute"
               <| fun _ ->
                   let result1 = parseSmcat "initial => idle;"
                   let generated = serialize result1.Document
                   // "initial" is inferred — should NOT have [type="initial"] in output
                   let hasExplicitType = generated.Contains("type=\"initial\"")
                   Expect.isFalse hasExplicitType "inferred initial should not have type attribute"

               testCase "color annotation survives round-trip"
               <| fun _ -> roundtrip "myState [color=\"red\"];"

               testCase "self-transition annotation round-trip"
               <| fun _ ->
                   let result1 = parseSmcat "a => a: refresh;"
                   let ts1 =
                       result1.Document.Elements
                       |> List.choose (function TransitionElement t -> Some t | _ -> None)
                   let hasSelf =
                       ts1[0].Annotations |> List.exists (function
                           | SmcatAnnotation(SmcatTransition SelfTransition) -> true
                           | _ -> false)
                   Expect.isTrue hasSelf "self-transition annotated"
                   roundtrip "a => a: refresh;" ]
     ```
  2. These tests exercise specific annotation scenarios that the golden file tests cover broadly.
- **Parallel?**: No (depends on T021).

### Subtask T023 – Verify build and tests

- **Purpose**: Final validation — all tests pass, all round-trip scenarios succeed.
- **Steps**:
  1. `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`
  2. `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
  3. Specifically verify: `dotnet test --filter "Smcat.RoundTrip"` — all round-trip tests pass
  4. If any existing golden file tests fail with the structural comparison, investigate the specific annotation that doesn't round-trip and fix the parser or serializer accordingly.

## Risks & Mitigations

- **Structural comparison too strict**: If `Annotation` DU cases don't support structural equality for `Set` operations, use custom comparison. F# DUs support this by default for simple payloads.
- **Existing golden files may fail**: Adding structural comparison to the `roundtrip` helper means golden files 1-3 now also check annotations. If the parser wasn't producing `SmcatTransition` annotations before WP04, these will fail. This is expected and correct — WP05 depends on WP02-04 being complete.
- **Whitespace sensitivity**: The structural comparison compares ASTs, not text. Whitespace differences in serialized output don't affect structural comparison.

## Review Guidance

- Verify `assertStructuralEquivalence` compares annotations as sets (not ordered lists)
- Verify ALL existing round-trip tests still pass (golden files 1-3)
- Verify new golden files (4-7) round-trip successfully
- Verify explicit type attributes survive round-trip (appear in serialized output)
- Verify inferred types do NOT gain explicit attributes during round-trip
- Run full test suite: `dotnet test` — all green

## Activity Log

- 2026-03-18T05:39:36Z – system – lane=planned – Prompt created.
