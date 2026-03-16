---
work_package_id: "WP04"
title: "Test Suite Migration"
phase: "Phase 3 - Test Migration"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01", "WP02", "WP03"]
requirement_refs: ["FR-021", "FR-024"]
subtasks:
  - "T014"
  - "T015"
  - "T016"
  - "T017"
  - "T018"
history:
  - timestamp: "2026-03-16T19:13:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP04 -- Test Suite Migration

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Objectives & Success Criteria

- All smcat test files compile with shared AST types
- All existing test scenarios continue to pass (same assertions, updated type references)
- `dotnet test` passes for the `Frank.Statecharts.Tests` project
- Round-trip tests use `Serializer.serialize` instead of test-only `generateFromDocument` helper
- Generator tests assert against `StatechartDocument` instead of raw text

## Context & Constraints

- **Spec**: User Story 5 (FR-021) -- all existing tests must pass
- **Plan**: DD-006 (test migration strategy)
- **Test files**: `test/Frank.Statecharts.Tests/Smcat/` -- 6 test files
- **Key pattern replacements** (from plan DD-006):
  - `StateDeclaration s` -> `StateDecl s`
  - `TransitionElement t` -> `Ast.TransitionElement t` (or `TransitionElement t` if Ast opened last)
  - `s.Name` -> `s.Identifier`
  - `s.StateType` -> `s.Kind`
  - `t.Label.Value.Event` -> `t.Event`
  - `t.Target` (string) -> `t.Target.Value` (string option, unwrap)
  - `s.Children.Value.Elements` -> navigate via `StateNode.Children` list
  - `err.Position.Line` -> `err.Position.Value.Line` (SourcePosition option)
  - `SmcatDocument` -> `StatechartDocument`
  - `StateType` -> `StateKind`
- **Prerequisite**: WP01, WP02, WP03 must be complete

## Implementation Command

```bash
spec-kitty implement WP04 --base WP03
```

## Subtasks & Detailed Guidance

### Subtask T014 -- Update ParserTests.fs

**Purpose**: Migrate all parser test assertions from smcat-specific types to shared AST types.

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Smcat/ParserTests.fs`

2. **Update imports**:
   ```fsharp
   open Frank.Statecharts.Ast
   open Frank.Statecharts.Smcat.Types  // still needed for inferStateType, SmcatAttribute
   open Frank.Statecharts.Smcat.Parser
   ```

3. **Update helper functions**:
   ```fsharp
   /// Helper to extract transitions from a ParseResult.
   let private transitions (result: ParseResult) =
       result.Document.Elements
       |> List.choose (fun e ->
           match e with
           | TransitionElement t -> Some t
           | _ -> None)

   /// Helper to extract state declarations from a ParseResult.
   let private states (result: ParseResult) =
       result.Document.Elements
       |> List.choose (fun e ->
           match e with
           | StateDecl s -> Some s
           | _ -> None)
   ```
   Note: `ParseResult` is now `Ast.ParseResult`. `TransitionElement` and `StateDecl` are `Ast.StatechartElement` cases.

4. **Update transition assertions** -- label components are now directly on `TransitionEdge`:
   - `ts[0].Label None` -> Check `ts[0].Event = None && ts[0].Guard = None && ts[0].Action = None`
   - `ts[0].Label.Value.Event` -> `ts[0].Event`
   - `ts[0].Label.Value.Guard` -> `ts[0].Guard`
   - `ts[0].Label.Value.Action` -> `ts[0].Action`
   - For "has label" checks: `Expect.isSome ts[0].Label` -> check that at least one of Event/Guard/Action is Some
   - `ts[0].Target` (was `string`) -> `ts[0].Target` (now `string option`, so use `.Value` or pattern match)

   **Examples of specific test updates**:

   ```fsharp
   // OLD: Expect.equal ts[0].Label None "no label"
   // NEW:
   Expect.equal ts[0].Event None "no event"
   Expect.equal ts[0].Guard None "no guard"
   Expect.equal ts[0].Action None "no action"

   // OLD: Expect.isSome ts[0].Label "has label"
   //      Expect.equal ts[0].Label.Value.Event (Some "event") "event"
   // NEW:
   Expect.equal ts[0].Event (Some "event") "event"

   // OLD: Expect.equal ts[0].Target "b" "target"
   // NEW:
   Expect.equal ts[0].Target (Some "b") "target"
   ```

5. **Update state assertions**:
   ```fsharp
   // OLD: Expect.equal ss[0].Name "idle" "state name"
   // NEW:
   Expect.equal ss[0].Identifier "idle" "state name"

   // OLD: Expect.equal ss[0].StateType Regular "regular type"
   // NEW:
   Expect.equal ss[0].Kind StateKind.Regular "regular type"

   // OLD: Expect.equal ss[0].StateType Initial "initial state type"
   // NEW:
   Expect.equal ss[0].Kind StateKind.Initial "initial state type"
   ```

6. **Update attribute assertions** -- attributes are now annotations:
   ```fsharp
   // OLD: Expect.equal ss[0].Attributes.Length 2 "two attributes"
   //      Expect.equal ss[0].Attributes[0].Key "label" "first attr key"
   // NEW: Check annotations
   Expect.equal ss[0].Annotations.Length 2 "two annotations"
   // Annotations are Annotation DU values, not SmcatAttribute records
   // Use pattern matching or check specific annotation types
   ```

   **Note**: The exact assertion pattern depends on how annotations are ordered. The parser should produce annotations in the same order as the original attributes.

7. **Update activity assertions** -- activities are now `StateActivities` (lists, not options):
   ```fsharp
   // OLD: Expect.equal ss[0].Activities.Value.Entry (Some "start") "entry activity"
   // NEW:
   Expect.equal ss[0].Activities.Value.Entry ["start"] "entry activity"

   // OLD: Expect.equal ss[0].Activities.Value.Exit (Some "stop") "exit activity"
   // NEW:
   Expect.equal ss[0].Activities.Value.Exit ["stop"] "exit activity"
   ```

8. **Update composite state assertions**:
   ```fsharp
   // OLD: Expect.isSome ss[0].Children "has children"
   //      let children = ss[0].Children.Value
   //      let childTs = children.Elements |> List.choose (...)
   // NEW:
   Expect.isNonEmpty ss[0].Children "has children"
   // Child transitions are now in the parent document's elements list
   // or need to be found differently

   // For child state assertions:
   // OLD: ss[0].Children.Value.Elements |> List.choose (fun e -> match e with StateDeclaration s -> Some s | _ -> None)
   // NEW: ss[0].Children  -- this IS the child state list
   ```

   The composite state tests will need the most attention because the structure changed from `SmcatDocument option` to `StateNode list`. Transitions within composites may need to be found in the parent document's elements list.

**Files**: `test/Frank.Statecharts.Tests/Smcat/ParserTests.fs`
**Parallel?**: Yes, can proceed alongside T015-T018.

**Validation**:
- [ ] All parser tests compile
- [ ] All parser tests pass
- [ ] No references to `SmcatDocument`, `SmcatState`, `SmcatTransition`, `StateDeclaration`, `SmcatElement`

### Subtask T015 -- Update ErrorTests.fs

**Purpose**: Migrate error test assertions from smcat-specific types to shared AST types.

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Smcat/ErrorTests.fs`

2. **Update imports**: Add `open Frank.Statecharts.Ast`

3. **Update helper functions**: Same as ParserTests.fs (T014 step 3)

4. **Update error position assertions**:
   ```fsharp
   // OLD: Expect.isGreaterThan err.Position.Line 0 "has line"
   // NEW: (Position is now SourcePosition option)
   Expect.isSome err.Position "has position"
   Expect.isGreaterThan err.Position.Value.Line 0 "has line"
   Expect.isGreaterThan err.Position.Value.Column 0 "has column"
   ```

5. **Update transition/state extraction helpers** to match T014 patterns.

6. **Update the `parse` call in error limit tests**:
   ```fsharp
   // This should work unchanged since `parse` still takes Token list and maxErrors
   let result = parse tokens 5
   ```

**Files**: `test/Frank.Statecharts.Tests/Smcat/ErrorTests.fs`
**Parallel?**: Yes.

**Validation**:
- [ ] All error tests compile and pass
- [ ] Error position checks use `.Value` to unwrap option

### Subtask T016 -- Update LabelParserTests.fs

**Purpose**: Minor update to use `Ast.SourcePosition` for the test helper `pos`.

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Smcat/LabelParserTests.fs`

2. **Update imports**: Add `open Frank.Statecharts.Ast` (or adjust existing opens)

3. **Update `pos` helper**:
   ```fsharp
   // This should work unchanged since SourcePosition from Types.fs is deleted
   // and Ast.SourcePosition has the same shape {Line: int; Column: int}
   let private pos : SourcePosition = { Line = 1; Column = 1 }
   ```
   If `SourcePosition` is ambiguous, qualify with `Ast.SourcePosition`.

4. **Update warning assertions** if `ParseWarning` changed:
   ```fsharp
   // OLD: warnings[0].Description
   // NEW: Same -- field names haven't changed, just the Position type
   // Position is now SourcePosition option, but label parser tests may not check position
   ```

5. The `TransitionLabel` type is unchanged (retained in Types.fs), so label field assertions remain the same.

**Files**: `test/Frank.Statecharts.Tests/Smcat/LabelParserTests.fs`
**Parallel?**: Yes.

**Validation**:
- [ ] All label parser tests compile and pass
- [ ] `pos` uses `Ast.SourcePosition` (or unqualified since local is deleted)

### Subtask T017 -- Update GeneratorTests.fs

**Purpose**: The generator now returns `Result<StatechartDocument, GeneratorError>` instead of `string`. Tests must unwrap the Result and assert against the `StatechartDocument` structure (or use `Serializer.serialize` to get text for text-based assertions).

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Smcat/GeneratorTests.fs`

2. **Update imports**:
   ```fsharp
   open Frank.Statecharts.Ast
   open Frank.Statecharts.Smcat.Generator
   open Frank.Statecharts.Smcat.Serializer  // for serialize function
   ```

3. **Update `formatLabel` and `formatTransition` tests**:
   These helper functions have been **moved to Serializer.fs** and are now **private**. Options:
   - a) Delete these tests (the functions are internal implementation details)
   - b) Make them `internal` in Serializer.fs and test them there
   - c) Keep similar tests but exercise them through the public `serialize` API

   **Recommended**: Delete the `labelFormattingTests` and `transitionFormattingTests` test sections since `formatLabel` and `formatTransition` are private in Serializer.fs. The label formatting logic is thoroughly tested by the round-trip tests (WP04 T018). If you want to keep unit tests for label formatting, create them as Serializer-level tests that use `serialize` on a constructed `StatechartDocument`.

4. **Update full generator tests**:
   The `generate` function now returns `Result<StatechartDocument, GeneratorError>`. For text assertions, chain with `Serializer.serialize`:

   ```fsharp
   // OLD:
   let result = generate options (makeMetadata machine handlers)
   let lines = result.Split('\n')
   Expect.equal lines.[0] "initial => Idle;" "first line is initial transition"

   // NEW (Option A: assert against AST structure):
   let result = generate options (makeMetadata machine handlers)
   match result with
   | Error e -> failwithf "Generator failed: %A" e
   | Ok doc ->
       // Check elements
       let transitions =
           doc.Elements
           |> List.choose (function TransitionElement t -> Some t | _ -> None)
       // ... assert against transitions

   // NEW (Option B: serialize to text and assert as before):
   let result = generate options (makeMetadata machine handlers)
   match result with
   | Error e -> failwithf "Generator failed: %A" e
   | Ok doc ->
       let text = serialize doc
       let lines = text.Split('\n')
       Expect.equal lines.[0] "initial => Idle;" "first line is initial transition"
   ```

   **Recommended**: Use Option B (serialize to text) to minimize test changes and maintain the existing assertion logic. This also validates the Serializer as a bonus.

   **However**, note that the old generator emitted `initial => Idle;` as the first line. The new generator produces a `StatechartDocument` with `StateDecl` elements first, then transitions. When serialized, the output will be different:
   ```
   Idle;
   Running;
   initial => Idle;
   Idle => Idle: GET;
   ...
   ```
   vs. the old output:
   ```
   initial => Idle;
   Idle => Idle: GET;
   ...
   ```

   The old generator produced ONLY transitions (no state declarations). The new generator produces state declarations + transitions. So the text output will differ. **You have two options**:
   - a) Accept the new output format and update assertions accordingly
   - b) Modify the Serializer to skip standalone state declarations when they are also referenced in transitions (like the old generator behavior)

   **Recommended**: Option A -- update assertions. The new output is more complete (includes state declarations). The serializer should emit what the AST contains.

   **Alternatively**: Since the old tests tested specific text output, and the new generator produces AST, test the AST directly:

   ```fsharp
   test "emits initial transition first" {
       let machine = simpleMachine [] (Map.ofList [...])
       let handlers = Map.ofList [...]
       match generate options (makeMetadata machine handlers) with
       | Error e -> failwithf "Generator failed: %A" e
       | Ok doc ->
           // Check that initial state ID is set
           Expect.equal doc.InitialStateId (Some "Idle") "initial state"
           // Check state declarations exist
           let stateDecls =
               doc.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)
           Expect.isTrue (stateDecls |> List.exists (fun s -> s.Identifier = "Idle")) "has Idle state"
   }

   test "emits self-messages for each HTTP method" {
       // ...
       match generate options (makeMetadata machine handlers) with
       | Error e -> failwithf "Generator failed: %A" e
       | Ok doc ->
           let transitions =
               doc.Elements |> List.choose (function TransitionElement t -> Some t | _ -> None)
           let selfTransitions =
               transitions |> List.filter (fun t -> t.Source = "Idle" && t.Target = Some "Idle")
           Expect.equal selfTransitions.Length 2 "two self-transitions"
           Expect.isTrue (selfTransitions |> List.exists (fun t -> t.Event = Some "GET")) "has GET"
           Expect.isTrue (selfTransitions |> List.exists (fun t -> t.Event = Some "POST")) "has POST"
   }
   ```

5. **Update the "single state, no handlers" test**: The old assertion was `Expect.equal result "initial => Idle;"`. The new assertion should check the AST has no transitions (except possibly none, since there are no handlers).

**Files**: `test/Frank.Statecharts.Tests/Smcat/GeneratorTests.fs`
**Parallel?**: Yes.

**Validation**:
- [ ] All generator tests compile and pass
- [ ] Tests exercise the new `Result<StatechartDocument, GeneratorError>` return type
- [ ] `formatLabel` and `formatTransition` tests are removed or adapted
- [ ] No references to old `generate: ... -> string` signature

### Subtask T018 -- Update RoundTripTests.fs

**Purpose**: The round-trip tests use a test-only `generateFromDocument` helper that produces smcat text from `SmcatDocument`. This must be replaced by `Serializer.serialize` (which takes `StatechartDocument`). All type references must be updated.

**Steps**:

1. Open `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs`

2. **Update imports**:
   ```fsharp
   open Frank.Statecharts.Ast
   open Frank.Statecharts.Smcat.Types
   open Frank.Statecharts.Smcat.Parser
   open Frank.Statecharts.Smcat.Serializer  // replaces Generator import for formatLabel
   ```

3. **Delete test-only helpers**: Remove `formatLabelText`, `generateFromDocument`, `generateSmcatFromDocument` functions. Replace all calls with `Serializer.serialize`.

4. **Update semantic equivalence helpers**:
   ```fsharp
   // OLD: extractStateSet (doc: SmcatDocument) : Set<string * StateType>
   // NEW:
   let rec private extractStateSet (doc: StatechartDocument) : Set<string * StateKind> =
       doc.Elements
       |> List.collect (fun el ->
           match el with
           | TransitionElement t ->
               let targetStates =
                   match t.Target with
                   | Some target -> [(target, inferStateType target [])]
                   | None -> []
               (t.Source, inferStateType t.Source []) :: targetStates
           | StateDecl s ->
               let childStates =
                   s.Children
                   |> List.collect (fun child ->
                       // Create a temporary doc from children for recursive call
                       let childDoc = { Title = None; InitialStateId = None
                                        Elements = [StateDecl child]; DataEntries = []; Annotations = [] }
                       extractStateSet childDoc |> Set.toList)
               (s.Identifier, s.Kind) :: childStates
           | _ -> [])
       |> Set.ofList

   // OLD: extractTransitionSet (doc: SmcatDocument) : Set<...>
   // NEW:
   let rec private extractTransitionSet
       (doc: StatechartDocument)
       : Set<string * string * string option * string option * string option> =
       doc.Elements
       |> List.collect (fun el ->
           match el with
           | TransitionElement t ->
               let target = t.Target |> Option.defaultValue ""
               [(t.Source, target, t.Event, t.Guard, t.Action)]
           | StateDecl s ->
               s.Children
               |> List.collect (fun child ->
                   let childDoc = { Title = None; InitialStateId = None
                                    Elements = [StateDecl child]; DataEntries = []; Annotations = [] }
                   extractTransitionSet childDoc |> Set.toList)
           | _ -> [])
       |> Set.ofList
   ```

   **Note**: The exact implementation of `extractStateSet` and `extractTransitionSet` depends on how the parser stores composite-internal transitions. If transitions within composites are hoisted to the parent document's elements list, the recursive traversal through `StateNode.Children` won't find transitions -- they'll be at the parent level. Test this carefully.

5. **Update `assertSemanticEquivalence`**:
   ```fsharp
   let private assertSemanticEquivalence (doc1: StatechartDocument) (doc2: StatechartDocument) =
       // ... uses updated extractStateSet and extractTransitionSet
   ```

6. **Update `roundtrip` helper**:
   ```fsharp
   let private roundtrip (smcatText: string) =
       let result1 = parseSmcat smcatText
       Expect.isEmpty result1.Errors (sprintf "Original parse should have no errors, got: %A" result1.Errors)
       let generatedText = serialize result1.Document   // was generateSmcatFromDocument
       let result2 = parseSmcat generatedText
       Expect.isEmpty result2.Errors
           (sprintf "Re-parsed output should have no errors, got: %A\nGenerated text:\n%s" result2.Errors generatedText)
       assertSemanticEquivalence result1.Document result2.Document
   ```

7. **Update the "empty input roundtrips trivially" test**:
   ```fsharp
   // Use serialize instead of generateSmcatFromDocument
   let generatedText = serialize result1.Document
   ```

8. **Update "deterministic roundtrip" test**:
   ```fsharp
   let gen1 = serialize result1.Document
   let gen2 = serialize result2.Document
   ```

**Files**: `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs`
**Parallel?**: Yes.

**Validation**:
- [ ] All round-trip tests compile and pass
- [ ] `Serializer.serialize` is used instead of test-only `generateFromDocument`
- [ ] Golden file round-trips succeed (simple linear, branching, composite)
- [ ] No references to `SmcatDocument`, `StateType`, `StateDeclaration`

## Risks & Mitigations

- **Risk**: Composite state test assertions may be complex due to `Children` type change. **Mitigation**: Focus on getting the state list assertions correct first, then handle transition assertions.
- **Risk**: Generator test text assertions may not match new Serializer output format. **Mitigation**: Use AST-level assertions (check `StatechartDocument` structure) instead of text comparison.
- **Risk**: Round-trip tests may fail if Serializer output doesn't exactly re-parse to the same structure. **Mitigation**: Use semantic equivalence (state sets, transition sets) not structural equality.

## Review Guidance

- Run `dotnet test` and verify all smcat tests pass
- Check that no smcat-specific types (`SmcatDocument`, `SmcatState`, `SmcatTransition`, etc.) are referenced in test files
- Verify round-trip golden files still work end-to-end
- Verify generator tests exercise the new Result-based API

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
