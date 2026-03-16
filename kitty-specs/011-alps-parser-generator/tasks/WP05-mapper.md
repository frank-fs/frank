---
work_package_id: "WP05"
subtasks:
  - "T028"
  - "T029"
  - "T030"
  - "T031"
title: "ALPS Mapper -- Bidirectional Shared AST Mapping"
phase: "Phase 3 - Shared AST Integration"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01", "WP02"]
requirement_refs: ["FR-009", "FR-010", "FR-011", "FR-012"]
history:
  - timestamp: "2026-03-16T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP05 -- ALPS Mapper -- Bidirectional Shared AST Mapping

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<descriptor>` ``, `` `<ext>` ``
Use language identifiers in code blocks: ````fsharp`

---

## BLOCKED: Spec 020 Dependency

**This work package is BLOCKED on spec 020 (shared statechart AST)**. Do not begin implementation until the shared AST types (`StatechartDocument`, `StateNode`, `TransitionEdge`, `AlpsMeta`, `Annotation`) exist in `src/Frank.Statecharts/Ast/Types.fs`.

Once spec 020 has landed, verify the following types exist:
- `StatechartDocument` (root AST node)
- `StateNode` with `Identifier`, `Kind`, `Children` fields
- `TransitionEdge` with `Event`, `Source`, `Target`, `Guard`, `Annotations`, `Parameters` fields
- `AlpsMeta` DU with `AlpsTransitionType`, `AlpsDescriptorHref`, `AlpsExtension` cases
- `Annotation` DU with `AlpsAnnotation of AlpsMeta` case
- `AlpsTransitionKind` enum/DU for safe/unsafe/idempotent

If any of these types are missing or structured differently than expected, consult the spec 020 data model and adapt the mapper accordingly (per R-007).

---

## Implementation Command

```bash
spec-kitty implement WP05 --base WP04
```

If WP04 is not yet complete, use `--base WP02` instead (WP05 only strictly needs WP01 types and WP02 parser for tests).

---

## Objectives & Success Criteria

1. `toStatechartDocument` maps ALPS AST to shared statechart AST correctly.
2. `fromStatechartDocument` maps shared statechart AST back to ALPS AST correctly.
3. States (XTurn, OTurn, Won, Draw) extracted from tic-tac-toe ALPS AST.
4. Transitions (makeMove, viewGame) with correct source/target/event names.
5. Guard labels extracted from `ext` elements with `id = "guard"`.
6. HTTP method hints derived from descriptor types (safe->GET, unsafe->POST, idempotent->PUT).
7. Information that ALPS cannot express (workflow ordering, initial state) is left empty/default.
8. `dotnet build` and `dotnet test` pass.

## Context & Constraints

- **Spec references**: User Story 3 in spec.md. FR-009 through FR-012.
- **Architecture decisions**: AD-001 (only mapper depends on spec 020).
- **Research decisions**: R-006 (mapper is only spec 020 dependency), R-007 (accept AlpsMeta stub as-is).
- **Data model**: See data-model.md "Mapping Rules" section for complete field-by-field mapping.
- **Known limitations (D-006)**:
  - ALPS cannot distinguish PUT from DELETE (both `type="idempotent"`)
  - ALPS has no workflow ordering
  - `rt` is single-valued (one descriptor per target state for conditional transitions)
  - Guard semantics from `ext` elements are best-effort
  - No concept of initial state in ALPS

## Subtasks & Detailed Guidance

### Subtask T028 -- Implement toStatechartDocument

- **Purpose**: Convert an ALPS-specific `AlpsDocument` to the shared `StatechartDocument` AST, extracting states, transitions, guards, and HTTP method annotations.
- **File**: `src/Frank.Statecharts/Alps/Mapper.fs` (new file)
- **Module**: `module internal Frank.Statecharts.Alps.Mapper`

**Function signature (from data-model.md):**
```fsharp
val toStatechartDocument : doc: AlpsDocument -> StatechartDocument
```

**Mapping algorithm:**

1. **Identify state descriptors**: Top-level semantic descriptors that contain nested transition descriptors (via `href` references or inline descriptors).

   ```fsharp
   let isTransitionType dt =
       match dt with
       | Safe | Unsafe | Idempotent -> true
       | Semantic -> false

   // A semantic descriptor is a "state" if it contains transition-type children
   // or if it is referenced as an rt target by other descriptors
   ```

2. **Extract states**: Each qualifying semantic descriptor becomes a `StateNode`:
   - `StateNode.Identifier` = descriptor `id`
   - `StateNode.Kind` = `Regular` (ALPS has no state type classification)
   - Child states from nested semantic descriptors (if any)

3. **Extract transitions**: Each transition-type descriptor (safe/unsafe/idempotent) becomes a `TransitionEdge`:
   - `TransitionEdge.Event` = descriptor `id`
   - `TransitionEdge.Source` = parent semantic descriptor `id`
   - `TransitionEdge.Target` = `rt` value (strip `#` prefix if present)
   - `TransitionEdge.Annotations` = `[AlpsAnnotation(AlpsTransitionType(...))]`
   - `TransitionEdge.Guard` = value from `ext` element with `id = "guard"` (if present)
   - `TransitionEdge.Parameters` = nested parameter descriptor ids

4. **Map descriptor type to annotation:**
   ```fsharp
   let toTransitionKind (dt: DescriptorType) : AlpsTransitionKind =
       match dt with
       | Safe -> AlpsTransitionKind.Safe
       | Unsafe -> AlpsTransitionKind.Unsafe
       | Idempotent -> AlpsTransitionKind.Idempotent
       | Semantic -> failwith "Semantic is not a transition type"
   ```

5. **Extract guard from ext elements:**
   ```fsharp
   let extractGuard (exts: AlpsExtension list) : string option =
       exts
       |> List.tryFind (fun e -> e.Id = "guard")
       |> Option.bind (fun e -> e.Value)
   ```

6. **Handle rt references**: Strip `#` prefix from local references:
   ```fsharp
   let resolveRt (rt: string option) : string option =
       rt |> Option.map (fun r -> if r.StartsWith("#") then r.Substring(1) else r)
   ```

7. **Assemble StatechartDocument**:
   - States: collected from semantic descriptors
   - Transitions: collected from transition-type descriptors
   - No initial state (ALPS limitation)
   - No ordering (ALPS limitation)

**Edge cases:**
- Descriptor with `href` to non-existent id: preserve in annotations, no target state resolution
- External URL in `rt` or `href`: preserve as-is in annotations (not a local state)
- Semantic descriptor with no transition children: still a valid state (just no outgoing transitions)
- Multiple `ext` elements with `id = "guard"`: use the first one

### Subtask T029 -- Implement fromStatechartDocument

- **Purpose**: Convert a `StatechartDocument` back to an ALPS-specific `AlpsDocument`, producing one semantic descriptor per state and one transition descriptor per state-transition-target triple.
- **File**: Same file: `src/Frank.Statecharts/Alps/Mapper.fs`

**Function signature:**
```fsharp
val fromStatechartDocument : doc: StatechartDocument -> AlpsDocument
```

**Mapping algorithm:**

1. **States to semantic descriptors**: Each `StateNode` becomes a semantic `Descriptor`:
   - `Id` = `StateNode.Identifier`
   - `Type` = `Semantic`
   - `Descriptors` = href references to available transitions (nested children)

2. **Transitions to transition descriptors**: Each `TransitionEdge` becomes a transition `Descriptor`:
   - `Id` = `TransitionEdge.Event`
   - `Type` = derived from `AlpsAnnotation` (Safe/Unsafe/Idempotent)
   - `ReturnType` = `Some ("#" + TransitionEdge.Target)` (add `#` prefix for local reference)
   - `Extensions` = guard label as `ext` element with `id = "guard"` (if guard present)
   - `Descriptors` = parameter descriptors (from `TransitionEdge.Parameters`)

3. **Handle conditional transitions (FR-013)**: If the same event name has multiple target states, produce one descriptor per target:
   - Same `Id` (event name), different `ReturnType` per target
   - Guard from each individual transition

4. **Derive ALPS type from annotations:**
   ```fsharp
   let fromTransitionKind (kind: AlpsTransitionKind) : DescriptorType =
       match kind with
       | AlpsTransitionKind.Safe -> Safe
       | AlpsTransitionKind.Unsafe -> Unsafe
       | AlpsTransitionKind.Idempotent -> Idempotent
   ```
   Default to `Unsafe` if no ALPS annotation is present (POST is the safest default for state-modifying actions).

5. **Assemble AlpsDocument**:
   - `Version` = `Some "1.0"`
   - `Descriptors` = state descriptors (each containing transition descriptor references)
   - `Links`, `Extensions` = empty (or from annotations if available)

### Subtask T030 -- Add Mapper.fs to fsproj

- **Purpose**: Wire the mapper into the compile order. Must be last in the Alps group since it depends on both Alps types and shared AST types.
- **File**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Steps**: Add `<Compile Include="Alps/Mapper.fs" />` after `<Compile Include="Alps/JsonGenerator.fs" />`.

**Expected compile order:**
```xml
<Compile Include="Alps/Types.fs" />
<Compile Include="Alps/JsonParser.fs" />
<Compile Include="Alps/XmlParser.fs" />
<Compile Include="Alps/JsonGenerator.fs" />
<Compile Include="Alps/Mapper.fs" />
<Compile Include="Types.fs" />
```

**Note**: The mapper depends on shared AST types (spec 020). Those types must be compiled before Mapper.fs. Verify the fsproj compile order includes the shared AST types before the Alps group, or adjust accordingly.

- **Validation**: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` must succeed.

### Subtask T031 -- Mapper Tests

- **Purpose**: Validate all mapping rules from data-model.md. Tests use the golden files to verify real-world mapping correctness.
- **File**: `test/Frank.Statecharts.Tests/Alps/MapperTests.fs` (new file)
- **Module**: `module Frank.Statecharts.Tests.Alps.MapperTests`

**Test categories:**

1. **State extraction tests:**
   ```fsharp
   testCase "tic-tac-toe states are extracted" <| fun _ ->
       let alpsDoc = parseAlpsJson GoldenFiles.ticTacToeAlpsJson |> Result.defaultWith (fun _ -> failwith "parse failed")
       let statechart = toStatechartDocument alpsDoc
       let stateIds = statechart.States |> List.map (fun s -> s.Identifier) |> Set.ofList
       Expect.containsAll stateIds (Set.ofList ["XTurn"; "OTurn"; "Won"; "Draw"]) "all states extracted"
   ```

2. **Transition mapping tests:**
   ```fsharp
   testCase "makeMove transitions have correct source and target" <| fun _ ->
       let alpsDoc = parseAlpsJson GoldenFiles.ticTacToeAlpsJson |> Result.defaultWith (fun _ -> failwith "parse failed")
       let statechart = toStatechartDocument alpsDoc
       let makeMoves = statechart.Transitions |> List.filter (fun t -> t.Event = "makeMove")
       Expect.isNonEmpty makeMoves "should have makeMove transitions"
       // Verify at least one has source=XTurn, target=OTurn
       let xToO = makeMoves |> List.tryFind (fun t -> t.Source = "XTurn" && t.Target = "OTurn")
       Expect.isSome xToO "XTurn -> OTurn transition"
   ```

3. **Guard extraction tests:**
   ```fsharp
   testCase "guard labels extracted from ext elements" <| fun _ ->
       let alpsDoc = parseAlpsJson GoldenFiles.ticTacToeAlpsJson |> Result.defaultWith (fun _ -> failwith "parse failed")
       let statechart = toStatechartDocument alpsDoc
       let guarded = statechart.Transitions |> List.filter (fun t -> t.Guard.IsSome)
       Expect.isNonEmpty guarded "should have guarded transitions"
   ```

4. **HTTP method hint tests:**
   ```fsharp
   testCase "safe descriptor maps to GET annotation" <| fun _ ->
       let desc = { Id = Some "viewGame"; Type = Safe; Href = None; ReturnType = None;
                    Documentation = None; Descriptors = []; Extensions = []; Links = [] }
       // Map and verify annotation contains AlpsTransitionType Safe
       ...

   testCase "unsafe descriptor maps to POST annotation" <| fun _ ->
       ...

   testCase "idempotent descriptor maps to PUT annotation" <| fun _ ->
       ...
   ```

5. **Roundtrip mapper test:**
   ```fsharp
   testCase "toStatechartDocument -> fromStatechartDocument preserves states and transitions" <| fun _ ->
       let original = parseAlpsJson GoldenFiles.ticTacToeAlpsJson |> Result.defaultWith (fun _ -> failwith "parse failed")
       let statechart = toStatechartDocument original
       let roundTripped = fromStatechartDocument statechart
       // Compare key structural elements (ids, types, rt values)
       let originalIds = original.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList
       let roundTrippedIds = roundTripped.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList
       Expect.equal roundTrippedIds originalIds "descriptor ids preserved through mapper roundtrip"
   ```

6. **Edge case tests:**
   ```fsharp
   testCase "empty ALPS document maps to empty statechart" <| fun _ ->
       let doc = { Version = None; Documentation = None; Descriptors = []; Links = []; Extensions = [] }
       let statechart = toStatechartDocument doc
       Expect.isEmpty statechart.States "no states"
       Expect.isEmpty statechart.Transitions "no transitions"

   testCase "descriptor with external URL href is preserved as annotation" <| fun _ ->
       ...

   testCase "missing workflow ordering leaves ordering empty" <| fun _ ->
       // ALPS limitation: no ordering info
       ...
   ```

**Add test file to fsproj**: Add `<Compile Include="Alps/MapperTests.fs" />` after `<Compile Include="Alps/RoundTripTests.fs" />` (or after other Alps test files if RoundTripTests.fs hasn't been added yet) and before `<Compile Include="Program.fs" />`.

- **Validation**: `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` must pass all tests.

## Risks & Mitigations

- **Spec 020 not landed**: This WP is fully blocked on spec 020. Do not attempt to implement before the shared AST types exist. If spec 020 types differ from what's expected, adapt the mapper (R-007).
- **AlpsMeta stub insufficiency**: If the `AlpsMeta` DU from spec 020 doesn't cover all ALPS concepts (e.g., missing `AlpsExtension` case), propose additions via the spec 020 amendment process before implementing workarounds.
- **Lossy roundtrip**: The ALPS -> StatechartDocument -> ALPS roundtrip will lose information that StatechartDocument adds (e.g., ordering, initial state annotations from other formats). The mapper roundtrip test should verify that ALPS-expressible information is preserved, not that the full StatechartDocument roundtrips.
- **Conditional transitions**: One ALPS descriptor per target state means the mapper must handle multiple `TransitionEdge` instances with the same event name but different targets. Ensure the `fromStatechartDocument` correctly produces multiple descriptors.

## Review Guidance

- Verify mapper depends ONLY on spec 020 types (not on any other format's types).
- Verify guard extraction uses `id = "guard"` specifically (FR-012).
- Verify HTTP method mapping matches FR-010: safe->GET, unsafe->POST, idempotent->PUT.
- Verify `#` prefix handling on rt values (stripped when mapping to statechart, re-added when mapping back).
- Verify empty/minimal document handling (no crash on empty input).
- Verify ALPS limitations are documented in mapper output (no ordering, no initial state).
- Run `dotnet build` and `dotnet test`.

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

- 2026-03-16T00:00:00Z - system - lane=planned - Prompt created.
