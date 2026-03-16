---
work_package_id: WP03
title: Roundtrip Tests and Integration
lane: "doing"
dependencies:
- WP01
- WP02
base_branch: 017-wsd-generator-cross-validator-WP03-merge-base
base_commit: 8755d025fe79eba4740de371b6404b9438371827
created_at: '2026-03-16T04:26:02.994072+00:00'
subtasks:
- T013
- T014
- T015
- T016
- T017
phase: Phase 2 - Integration
assignee: ''
agent: ''
shell_pid: "9914"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:06Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-002, FR-008]
---

# Work Package Prompt: WP03 -- Roundtrip Tests and Integration

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

Depends on WP01 and WP02 -- use the latest completed WP as base:
```
spec-kitty implement WP03 --base WP02
```

If WP01 was completed after WP02, use `--base WP01` instead. The merge of both predecessors into the feature branch is required before this WP can begin.

---

## Objectives & Success Criteria

- Create `Wsd/GeneratorRoundTripTests.fs` that validates the full pipeline: `StateMachineMetadata -> Generator.generate -> Serializer.serialize -> Parser.parseWsd`
- Roundtrip tests verify **semantic equivalence**: same participant names, same message labels, same guard annotations (SC-001, SC-002)
- Cosmetic differences (whitespace, ordering of non-participant elements) are acceptable per spec
- All new test files are registered in the test `.fsproj`
- `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds for ALL target frameworks: net8.0, net9.0, net10.0
- `dotnet test test/Frank.Statecharts.Tests/` passes ALL tests (existing + new)
- Success criteria SC-001 (tic-tac-toe roundtrip), SC-003 (1 to 20+ states), SC-007 (multi-TFM), SC-008 (pure function) are met

## Context & Constraints

- **Spec**: `kitty-specs/017-wsd-generator-cross-validator/spec.md` -- User Story 2 (roundtrip fidelity), acceptance scenarios 2.1-2.3, edge cases
- **Plan**: `kitty-specs/017-wsd-generator-cross-validator/plan.md` -- Phase 2 (roundtrip tests depend on both Generator and Serializer)
- **Data Model**: `kitty-specs/017-wsd-generator-cross-validator/data-model.md` -- data flow diagram (metadata -> Diagram -> text -> ParseResult)
- **Research**: `kitty-specs/017-wsd-generator-cross-validator/research.md` -- R-03 (guard wildcard `*` value), R-04 (edge cases)

**Key Source Files** (from WP01 and WP02):
- `src/Frank.Statecharts/Wsd/Serializer.fs` -- `serialize`, `needsQuoting`, `quoteName`
- `src/Frank.Statecharts/Wsd/Generator.fs` -- `generate`, `GenerateOptions`, `GeneratorError`
- `src/Frank.Statecharts/Wsd/Parser.fs` -- `parseWsd` function (returns `ParseResult`)
- `src/Frank.Statecharts/Wsd/Types.fs` -- AST types for structural comparison
- `src/Frank.Statecharts/Types.fs` -- `StateMachine<'S,'E,'C>`, `Guard<'S,'E,'C>`
- `src/Frank.Statecharts/StatefulResourceBuilder.fs` -- `StateMachineMetadata`

**Existing test patterns** (reference):
- `test/Frank.Statecharts.Tests/Wsd/RoundTripTests.fs` -- existing parser roundtrip tests (parse WSD, verify AST)
- `test/Frank.Statecharts.Tests/Wsd/GeneratorTests.fs` -- generator unit tests (from WP02), including `makeMetadata` helper

**Key Constraints**:
- Roundtrip comparison must be **semantic**, not textual (cosmetic differences allowed)
- Guard roundtrip: generator emits `[guard: name=*]`, parser reads back `("name", "*")` pairs
- All generated participants must be `Explicit = true` to avoid parser implicit-participant warnings
- Tests target net10.0 (test project is single-target)

## Subtasks & Detailed Guidance

### Subtask T013 -- Create `Wsd/GeneratorRoundTripTests.fs` with helper infrastructure

**Purpose**: Establish the roundtrip test module with helper functions for semantic AST comparison.

**Steps**:
1. Create `test/Frank.Statecharts.Tests/Wsd/GeneratorRoundTripTests.fs`
2. Module: `module Frank.Statecharts.Tests.Wsd.GeneratorRoundTripTests`
3. Open: `Expecto`, `Frank.Statecharts`, `Frank.Statecharts.Wsd.Types`, `Frank.Statecharts.Wsd.Generator`, `Frank.Statecharts.Wsd.Serializer`, `Frank.Statecharts.Wsd.Parser`

**Helper functions to create**:

```fsharp
/// Extract participant names from a ParseResult.
let private participantNames (r: ParseResult) =
    r.Diagram.Participants |> List.map (fun p -> p.Name)

/// Extract (sender, receiver, label) triples from messages in a ParseResult.
let private messageTriples (r: ParseResult) =
    r.Diagram.Elements
    |> List.choose (function
        | MessageElement m -> Some (m.Sender, m.Receiver, m.Label)
        | _ -> None)

/// Extract guard pairs from notes in a ParseResult.
let private guardPairs (r: ParseResult) =
    r.Diagram.Elements
    |> List.choose (function
        | NoteElement n when n.Guard.IsSome -> Some n.Guard.Value.Pairs
        | _ -> None)
    |> List.concat

/// Run the full roundtrip pipeline and return the ParseResult.
let private roundtrip (options: GenerateOptions) (metadata: StateMachineMetadata) =
    match generate options metadata with
    | Error e -> failwithf "Generator failed: %A" e
    | Ok diagram ->
        let wsdText = serialize diagram
        let parseResult = parseWsd wsdText
        (diagram, wsdText, parseResult)
```

Also reuse or adapt the `makeMetadata` helper from `GeneratorTests.fs` (WP02). If code sharing across test files is needed, duplicate the helper (test code duplication is acceptable).

**Files**:
- `test/Frank.Statecharts.Tests/Wsd/GeneratorRoundTripTests.fs` (new, ~200-250 lines when complete)

---

### Subtask T014 -- Implement turnstile roundtrip test

**Purpose**: Validate the primary roundtrip scenario from spec User Story 2 -- generate WSD from a 3-state turnstile, parse back, verify structural equivalence.

**Steps**:
1. Define a turnstile state machine and handler map (reuse from WP02 tests or define fresh):
   ```fsharp
   type TurnstileState = Locked | Unlocked | Broken
   type TurnstileEvent = Coin | Push | Break

   let turnstileMachine : StateMachine<TurnstileState, TurnstileEvent, unit> =
       { Initial = Locked
         InitialContext = ()
         Transition = fun _ _ _ -> TransitionResult.Invalid "stub"
         Guards = []
         StateMetadata = Map.empty }
   ```
2. Create handler map with realistic HTTP methods:
   ```fsharp
   let turnstileHandlers = Map.ofList [
       "Locked", [ ("GET", stubDelegate); ("POST", stubDelegate) ]
       "Unlocked", [ ("GET", stubDelegate); ("POST", stubDelegate) ]
       "Broken", [ ("GET", stubDelegate) ]
   ]
   ```
3. Construct metadata via `makeMetadata`
4. Run roundtrip pipeline
5. Assert:

| Assertion | Details |
|-----------|---------|
| Parse succeeds with zero errors | `Expect.isEmpty parseResult.Errors "no parse errors"` |
| Title matches | `Expect.equal parseResult.Diagram.Title (Some "turnstile") "title"` |
| Three participants | `participantNames parseResult` has length 3 |
| Initial state first | First participant is `"Locked"` |
| All state names present | Set of participant names = `{"Locked"; "Unlocked"; "Broken"}` |
| Correct number of messages | 5 messages total (2 for Locked, 2 for Unlocked, 1 for Broken) |
| Message labels are HTTP methods | Labels include "GET" and "POST" |
| No guard annotations | No notes with guards (turnstile has no guards) |

**Acceptance criteria addressed**: SC-001, spec User Story 1 scenarios 1-4, User Story 2 scenario 1.

---

### Subtask T015 -- Implement edge case roundtrip tests

**Purpose**: Validate roundtrip fidelity for edge cases defined in the spec.

**Test cases to implement**:

**1. Single state, no transitions**:
```fsharp
test "single state no transitions roundtrips" {
    let machine = { Initial = "Terminal"; InitialContext = (); ... }
    let handlers = Map.ofList [ "Terminal", [] ]
    let metadata = makeMetadata machine handlers
    let (_, _, result) = roundtrip { ResourceName = "terminal" } metadata
    Expect.isEmpty result.Errors "no errors"
    Expect.equal (participantNames result) ["Terminal"] "one participant"
    Expect.isEmpty (messageTriples result) "no messages"
}
```

**2. Self-transition** (state -> same state):
```fsharp
test "self-transition roundtrips" {
    // Handler map with single state having POST handler
    // Generator emits self-message: state->state: POST
    // Parser should parse self-message correctly
    let (_, _, result) = roundtrip opts metadata
    let msgs = messageTriples result
    Expect.isTrue (msgs |> List.forall (fun (s, r, _) -> s = r)) "all self-messages"
}
```

**3. State names with special characters**:
```fsharp
test "quoted state names roundtrip" {
    // State name with space: "In Progress"
    // Serializer should quote it: participant "In Progress"
    // Parser should read it back correctly
    let machine = { Initial = "In Progress"; ... }
    let handlers = Map.ofList [ "In Progress", [("GET", stubDelegate)] ]
    let (_, _, result) = roundtrip { ResourceName = "workflow" } metadata
    Expect.isEmpty result.Errors "no errors"
    Expect.isTrue (participantNames result |> List.contains "In Progress") "quoted name survived"
}
```

**4. Metadata with guards**:
```fsharp
test "guards roundtrip as wildcard annotations" {
    let guards = [
        { Name = "role"; Predicate = fun _ -> Allowed }
        { Name = "auth"; Predicate = fun _ -> Allowed }
    ]
    let machine = { ...; Guards = guards; ... }
    let metadata = makeMetadata machine handlers
    let (_, _, result) = roundtrip opts metadata
    let pairs = guardPairs result
    Expect.isTrue (pairs |> List.exists (fun (k, v) -> k = "role" && v = "*")) "role guard"
    Expect.isTrue (pairs |> List.exists (fun (k, v) -> k = "auth" && v = "*")) "auth guard"
}
```

**5. Large state machine (performance / correctness)**:
```fsharp
test "20+ states roundtrip without error" {
    let states = [ for i in 1..25 -> sprintf "State%d" i ]
    let handlers = states |> List.map (fun s -> s, [("GET", stubDelegate)]) |> Map.ofList
    let machine = { Initial = "State1"; ... }
    let metadata = makeMetadata machine handlers
    let (_, _, result) = roundtrip { ResourceName = "large" } metadata
    Expect.isEmpty result.Errors "no errors"
    Expect.equal (participantNames result).Length 25 "25 participants"
}
```

**Acceptance criteria addressed**: SC-002 (roundtrip preserves participants, messages, guards), SC-003 (20+ states), spec User Story 2 scenarios 2.1-2.3, edge cases.

---

### Subtask T016 -- Add test files to test `.fsproj`

**Purpose**: Register all new test files (from WP01, WP02, and WP03) in the test project compile items.

**Steps**:
1. Edit `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
2. Add the following `<Compile>` entries in the `<ItemGroup>` with other Wsd test files, AFTER `<Compile Include="Wsd/RoundTripTests.fs" />` and BEFORE `<Compile Include="StatechartETagProviderTests.fs" />`:
   ```xml
   <Compile Include="Wsd/SerializerTests.fs" />
   <Compile Include="Wsd/GeneratorTests.fs" />
   <Compile Include="Wsd/GeneratorRoundTripTests.fs" />
   ```
3. **CHECK FIRST**: Some of these may already be added by WP01 or WP02. Only add entries that are missing.

**Expected `.fsproj` compile items after edit**:
```xml
<Compile Include="TypeTests.fs" />
<Compile Include="StoreTests.fs" />
<Compile Include="MiddlewareTests.fs" />
<Compile Include="StatefulResourceTests.fs" />
<Compile Include="Wsd/GuardParserTests.fs" />
<Compile Include="Wsd/ParserTests.fs" />
<Compile Include="Wsd/GroupingTests.fs" />
<Compile Include="Wsd/ErrorTests.fs" />
<Compile Include="Wsd/RoundTripTests.fs" />
<Compile Include="Wsd/SerializerTests.fs" />
<Compile Include="Wsd/GeneratorTests.fs" />
<Compile Include="Wsd/GeneratorRoundTripTests.fs" />
<Compile Include="StatechartETagProviderTests.fs" />
<Compile Include="Program.fs" />
```

**Files**:
- `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`

---

### Subtask T017 -- Full TFM build and test verification

**Purpose**: Verify the complete implementation compiles across all target frameworks and all tests pass.

**Steps**:
1. Build the source project for all TFMs:
   ```bash
   dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj
   ```
   This builds net8.0, net9.0, and net10.0. All three must succeed.

2. Run the test project:
   ```bash
   dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
   ```
   All existing tests plus new Serializer, Generator, and Roundtrip tests must pass.

3. Verify specific test counts:
   - `Serializer` test list should have ~15-20 tests
   - `Generator` test list should have ~10-15 tests
   - `GeneratorRoundTrip` test list should have ~6-8 tests

4. If any test fails, diagnose and fix before marking this WP complete.

**Validation checklist**:
- [ ] `dotnet build` succeeds for net8.0
- [ ] `dotnet build` succeeds for net9.0
- [ ] `dotnet build` succeeds for net10.0
- [ ] `dotnet test` passes all tests
- [ ] No warnings related to new files (aside from existing warnings if any)
- [ ] No `InternalsVisibleTo` issues (already configured in `.fsproj`)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| WP01 or WP02 merge conflicts | Check `.fsproj` for already-added entries before adding duplicates |
| Guard roundtrip fails (parser rejects wildcard format) | The `[guard: role=*]` format is syntactically valid per `GuardParser.tryParseGuard`; test explicitly |
| Quoted participant names fail roundtrip (parser expects different quoting) | Test special character names explicitly; verify `Lexer.tokenize` handles quoted strings |
| Test metadata construction is complex | Reuse `makeMetadata` helper; keep closure stubs minimal |
| Reflection-based guard extraction may fail on certain TFMs | `dotnet build` for all 3 TFMs catches compile issues; runtime behavior consistent across TFMs for F# reflection |

## Review Guidance

- Verify all spec acceptance scenarios are covered by at least one test
- Verify roundtrip comparison is semantic (participant names, message labels, guard pairs) not textual
- Verify `parseWsd` returns zero errors for all generated WSD text
- Confirm all generated participants are `Explicit = true` (no implicit-participant warnings)
- Check that test `.fsproj` has correct file order (new entries after existing Wsd tests, before `Program.fs`)
- Verify `dotnet build` and `dotnet test` commands were actually run and outputs reported

## Activity Log

- 2026-03-15T23:59:06Z -- system -- lane=planned -- Prompt created.
