---
work_package_id: WP06
title: Roundtrip Tests & Golden Files
lane: "planned"
dependencies:
- WP03
subtasks:
- T030
- T031
- T032
- T033
- T034
phase: Phase 3 - Validation
assignee: ''
agent: ''
shell_pid: ''
review_status: "has_feedback"
reviewed_by: "Ryan Riley"
review_feedback_file: "/private/tmp/fix-lane.md"
history:
- timestamp: '2026-03-15T23:59:14Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-002, FR-009]
---

# Work Package Prompt: WP06 -- Roundtrip Tests & Golden Files

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

**Reviewed by**: Ryan Riley
**Status**: ❌ Changes Requested
**Date**: 2026-03-16
**Feedback file**: `/private/tmp/fix-lane.md`

**Issue**: Manually correcting lane status to done


## Implementation Command

```bash
spec-kitty implement WP06 --base WP05
```

Depends on WP03 (Parser) and WP05 (Generator) -- both must be complete for the roundtrip cycle. Use `--base WP05` since WP05 is the later dependency (WP03 is already merged into the chain via WP05's own base).

**Note**: If WP05 was implemented independently from WP03 (parallel branches), you may need to merge both branches first. Check with `git log --oneline` to verify both WP03 and WP05 changes are present.

---

## Objectives & Success Criteria

- Create at least 3 golden file smcat examples of varying complexity (SC-009)
- Implement roundtrip consistency tests: parse -> extract info -> generate -> re-parse -> compare (User Story 4)
- Implement semantic equivalence comparison for SmcatDocument ASTs
- Validate SC-005 (roundtrip consistency) and SC-009 (golden file tests)

**Done when**: `dotnet test --filter "Smcat.RoundTrip"` passes. At least 3 golden file examples roundtrip successfully. State topology and transition labels survive the cycle.

## Context & Constraints

- **Spec**: User Story 4 (roundtrip consistency), SC-005, SC-009
- **Data Model**: SmcatDocument, SmcatState, SmcatTransition structures
- **Research**: R-008 (generator output format)
- **Quickstart**: `quickstart.md` -- onboarding example with expected AST

**Key constraints**:
- Roundtrip cycle: parse smcat -> extract state/event/transition info -> generate smcat -> re-parse generated text -> compare ASTs
- Semantic equivalence means: same state names/types, same transition source/target/labels. Ordering may differ.
- Some smcat features may NOT survive the roundtrip because StateMachineMetadata doesn't carry them: state activities (`entry/`, `exit/`, `...`), state attributes (`[color="red"]`), transition attributes, comments. Document which features roundtrip and which don't.
- Golden files must cover: (1) simple linear, (2) branching with guards, (3) composite states

## Subtasks & Detailed Guidance

### Subtask T030 -- Create golden file smcat examples

**Purpose**: Provide at least 3 representative smcat examples for roundtrip testing, covering increasing complexity.

**Steps**:

1. **Golden File 1: Simple linear flow** (3 states, 3 transitions):
   ```smcat
   # Simple order process
   initial => pending: submit;
   pending => approved: approve;
   approved => final: complete;
   ```
   - Tests: basic transitions, initial/final pseudo-states, event-only labels

2. **Golden File 2: Branching with guards and actions** (5+ states, multiple transition paths):
   ```smcat
   # Onboarding workflow with validation
   initial => home: start;
   home => WIP: begin;
   WIP => customerData: collectCustomerData [isValid] / logAction;
   WIP => home: reset [notValid] / logReset;
   customerData => review: submitForReview;
   review => customerData: reject [needsChanges] / notifyUser;
   review => final: approve [allGood];
   ```
   - Tests: guards, actions, multiple transitions from same state, all label combinations

3. **Golden File 3: Composite states** (nested state machines):
   ```smcat
   # Traffic light with walk signal
   initial => operating: powerOn;
   operating {
     initial => red: start;
     red => green: timer;
     green => yellow: timer;
     yellow => red: timer;
   };
   operating => final: powerOff;
   ```
   - Tests: composite states with nested transitions, transitions at multiple levels

4. **Storage**: Define golden files as string constants in `RoundTripTests.fs` (following WSD pattern where test data is inline). Alternatively, create a `test/Frank.Statecharts.Tests/Smcat/TestData/` directory with `.smcat` files, but inline is simpler and matches existing patterns.

**Files**: `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs` (golden file string constants)

**Parallel?**: Yes -- can be written immediately since it's just smcat text content.

---

### Subtask T031 -- Create `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs`

**Purpose**: Implement the roundtrip test harness that runs the parse-generate-reparse cycle.

**Steps**:

1. Replace the stub with the full test module:
   ```fsharp
   module Smcat.RoundTripTests

   open Expecto
   open Frank.Statecharts.Smcat.Types
   open Frank.Statecharts.Smcat.Parser
   open Frank.Statecharts.Smcat.Generator
   ```

2. **Implement the roundtrip cycle**:
   ```fsharp
   let roundtrip (smcatText: string) =
       // Step 1: Parse original text
       let result1 = parseSmcat smcatText
       Expect.isEmpty result1.Errors "Original parse should have no errors"

       // Step 2: Extract state/event/transition info from AST
       let (states, transitions) = extractTopology result1.Document

       // Step 3: Build a minimal metadata-like representation
       //         OR: generate directly from AST info (see notes below)

       // Step 4: Generate smcat text
       let generatedText = generateFromAst result1.Document

       // Step 5: Re-parse generated text
       let result2 = parseSmcat generatedText
       Expect.isEmpty result2.Errors "Re-parsed output should have no errors"

       // Step 6: Compare ASTs for semantic equivalence
       assertSemanticEquivalence result1.Document result2.Document
   ```

   **Important design decision**: The generator API (`generate`) takes `StateMachineMetadata`, but for roundtrip testing, the source is a parsed AST. Two approaches:

   **Option A**: Create a helper `generateFromDocument` that takes an `SmcatDocument` directly and produces smcat text. This is simpler for testing and avoids constructing `StateMachineMetadata` from an AST.

   **Option B**: Extract topology from the AST, build `StateMachineMetadata`, and use the full `generate` function. This tests the actual production API but requires complex metadata construction.

   **Recommendation**: Use Option A for this WP. Create a test-only helper function that generates smcat text from the parsed AST. This validates that the AST types can fully represent smcat semantics. The full `generate` from metadata API is already tested in WP05's GeneratorTests.

3. **Test-only generator helper** (defined within the test file):
   ```fsharp
   let generateFromDocument (doc: SmcatDocument) : string =
       let lines = ResizeArray<string>()
       for element in doc.Elements do
           match element with
           | TransitionElement t ->
               let label = t.Label |> Option.map formatLabelText
               let line =
                   match label with
                   | Some l -> sprintf "%s => %s: %s;" t.Source t.Target l
                   | None -> sprintf "%s => %s;" t.Source t.Target
               lines.Add(line)
           | StateDeclaration s ->
               // Only emit explicit state declarations if they have attributes or activities
               if s.Attributes.Length > 0 || s.Activities.IsSome then
                   lines.Add(sprintf "%s;" s.Name)
           | CommentElement _ -> () // Comments don't roundtrip
       lines |> String.concat "\n"
   ```

4. **Write roundtrip test for each golden file**:
   ```fsharp
   [<Tests>]
   let roundTripTests = testList "Smcat.RoundTrip" [
       testCase "golden file 1 - simple linear" <| fun _ ->
           roundtrip goldenSimpleLinear

       testCase "golden file 2 - branching with guards" <| fun _ ->
           roundtrip goldenBranchingGuards

       testCase "golden file 3 - composite states" <| fun _ ->
           roundtrip goldenCompositeStates
   ]
   ```

**Files**: `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs` (~200-250 lines)

---

### Subtask T032 -- Implement semantic equivalence comparison

**Purpose**: Compare two `SmcatDocument` ASTs for semantic equivalence, ignoring ordering differences.

**Steps**:

1. **Define comparison functions**:

   ```fsharp
   let extractStateSet (doc: SmcatDocument) : Set<string * StateType> =
       doc.Elements
       |> List.collect (fun el ->
           match el with
           | TransitionElement t ->
               [ (t.Source, inferStateType t.Source [])
                 (t.Target, inferStateType t.Target []) ]
           | StateDeclaration s -> [ (s.Name, s.StateType) ]
           | CommentElement _ -> [])
       |> Set.ofList

   let extractTransitionSet (doc: SmcatDocument) : Set<string * string * string option * string option * string option> =
       doc.Elements
       |> List.choose (fun el ->
           match el with
           | TransitionElement t ->
               let (ev, gd, ac) =
                   match t.Label with
                   | Some l -> (l.Event, l.Guard, l.Action)
                   | None -> (None, None, None)
               Some (t.Source, t.Target, ev, gd, ac)
           | _ -> None)
       |> Set.ofList
   ```

2. **Semantic equivalence assertion**:
   ```fsharp
   let assertSemanticEquivalence (doc1: SmcatDocument) (doc2: SmcatDocument) =
       let states1 = extractStateSet doc1
       let states2 = extractStateSet doc2
       Expect.equal states1 states2 "State sets should be equivalent"

       let transitions1 = extractTransitionSet doc1
       let transitions2 = extractTransitionSet doc2
       Expect.equal transitions1 transitions2 "Transition sets should be equivalent"
   ```

3. **What is NOT compared** (by design):
   - Element ordering (states/transitions may appear in different order)
   - Comments (discarded during generation)
   - State activities (not carried through `StateMachineMetadata`)
   - State/transition attributes (not carried through `StateMachineMetadata`)
   - Whitespace and formatting

**Files**: `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs`

---

### Subtask T033 -- Add roundtrip edge case tests

**Purpose**: Test roundtrip consistency for edge cases and partial feature combinations.

**Steps**:

1. **Guard-only transitions**:
   ```smcat
   a => b: [isReady];
   ```
   Verify guard survives roundtrip.

2. **Action-only transitions**:
   ```smcat
   a => b: / doSomething;
   ```
   Verify action survives roundtrip.

3. **No-label transitions**:
   ```smcat
   a => b;
   ```
   Verify transition roundtrips without label.

4. **Pseudo-states**:
   ```smcat
   initial => start;
   start => final;
   ```
   Verify initial/final pseudo-state semantics survive.

5. **Multiple transitions from same source**:
   ```smcat
   a => b: go;
   a => c: stay;
   ```
   Both transitions should survive.

6. **Empty input**: `""` -> empty document roundtrips (trivially).

7. **Single state declaration** (if the generator emits it):
   ```smcat
   idle;
   ```
   Verify state survives roundtrip (if state declarations are emitted by the test helper generator).

**Files**: `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs`

---

### Subtask T034 -- Validate SC-005 and SC-009 success criteria

**Purpose**: Explicit validation that the success criteria from the spec are met.

**Steps**:

1. **SC-005 (Roundtrip consistency)**: Verify all golden file tests pass and the assertion output shows:
   - State topology matches (same state names and types in both ASTs)
   - Transition labels match (same event/guard/action strings)
   - This is validated by the roundtrip tests in T031-T033

2. **SC-009 (Golden file tests)**: Verify at least 3 smcat examples pass:
   - Golden File 1: simple linear (3 states, 3 transitions) -- PASS
   - Golden File 2: branching with guards (5+ states, guards and actions) -- PASS
   - Golden File 3: composite states (nested state machines) -- PASS

3. **Run the full test suite** and verify:
   ```bash
   dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat.RoundTrip"
   ```

4. **Document which features survive roundtrip**:
   - Survives: state names, state types, transition source/target, event labels, guard labels, action labels
   - Does NOT survive: comments, state activities, state/transition attributes, element ordering, whitespace

**Files**: No new files -- validation step. Document results in the activity log.

---

## Test Strategy

Run roundtrip tests:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat.RoundTrip"
```

Run all smcat tests:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat"
```

Run full test suite (no regressions):
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
```

## Risks & Mitigations

- **Composite state roundtrip complexity**: The test-only generator helper needs to handle nested documents recursively. If this is too complex, start with flat (non-composite) roundtrip tests and add composite support incrementally.
- **Ordering sensitivity in assertions**: Use `Set` comparison, not list comparison. If sets aren't sufficient (e.g., duplicate transitions are valid), use sorted list comparison.
- **Generator output that doesn't re-parse**: If the generated smcat text has syntax issues, the re-parse step will fail with errors. Debug by examining the generated text string in the test output.
- **Test helper generator vs production generator**: The test-only `generateFromDocument` is separate from the production `Generator.generate`. This is intentional -- the test helper validates AST completeness, while the production generator validates the metadata-to-smcat path.

## Review Guidance

- Verify at least 3 golden file examples exist with varying complexity
- Verify semantic equivalence comparison uses Set-based comparison (not order-dependent)
- Verify roundtrip tests assert no parse errors on both parse and re-parse
- Verify edge cases cover: guards only, actions only, no label, pseudo-states, multiple transitions from same source
- Verify SC-005 and SC-009 are explicitly validated
- Check that `dotnet test --filter "Smcat.RoundTrip"` passes
- Check that `dotnet test --filter "Smcat"` passes (no regressions from other WPs)

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:14Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:
1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP06 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-16T14:34:43Z – unknown – lane=planned – Moved to planned
