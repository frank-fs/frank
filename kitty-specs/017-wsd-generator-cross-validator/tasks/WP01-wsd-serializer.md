---
work_package_id: WP01
title: WSD Serializer
lane: "doing"
dependencies: []
base_branch: master
base_commit: c25f06152554a9fee69211eb7ba68098b454cc7f
created_at: '2026-03-16T04:02:31.877115+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
- T006
phase: Phase 1a - Serializer
assignee: ''
agent: ''
shell_pid: "98343"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:06Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-002, FR-003, FR-004, FR-006, FR-008, FR-009]
---

# Work Package Prompt: WP01 -- WSD Serializer

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

No dependencies -- run from scratch:
```
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

- Create `src/Frank.Statecharts/Wsd/Serializer.fs` that serializes a `Diagram` AST to syntactically valid WSD text
- The serializer must be a **pure function** with no side effects (SC-008)
- Output must be parseable by the existing WSD parser (`parseWsd`) with zero errors (FR-008)
- Output uses Unix `\n` line endings throughout (spec edge case requirement)
- Output follows the formatting conventions from research.md R-02
- The module is `internal` to `Frank.Statecharts` assembly (DD-06)
- All tests pass via `dotnet test test/Frank.Statecharts.Tests/`

## Context & Constraints

- **Spec**: `kitty-specs/017-wsd-generator-cross-validator/spec.md` -- FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-008, FR-009
- **Plan**: `kitty-specs/017-wsd-generator-cross-validator/plan.md` -- DD-01 (two-phase pipeline), DD-03 (default arrow style), DD-06 (internal visibility)
- **Data Model**: `kitty-specs/017-wsd-generator-cross-validator/data-model.md` -- `Serializer.serialize`, `Serializer.needsQuoting`, `Serializer.quoteName` signatures
- **Research**: `kitty-specs/017-wsd-generator-cross-validator/research.md` -- R-02 (serialization format), R-04 (edge cases)
- **Quickstart**: `kitty-specs/017-wsd-generator-cross-validator/quickstart.md` -- example usage of `serialize`
- **Existing AST Types**: `src/Frank.Statecharts/Wsd/Types.fs` -- `Diagram`, `DiagramElement`, `Participant`, `Message`, `Note`, `GuardAnnotation`, `ArrowStyle`, `Direction`, `NotePosition`
- **Existing Parser**: `src/Frank.Statecharts/Wsd/Parser.fs` -- `parseWsd` function used for validation in tests
- **Existing Guard Parser**: `src/Frank.Statecharts/Wsd/GuardParser.fs` -- `[guard: key=value]` syntax the serializer must produce

**Key Constraints**:
- Module must be `internal` (matching all other `Wsd/` modules)
- No new NuGet dependencies
- Participant names with non-identifier characters must be quoted
- Guard annotations use `[guard: key=value, key2=value2]` format inside `note over` elements
- All transitions use `ArrowStyle.Solid` + `Direction.Forward` when serializing (though the serializer should handle all arrow styles correctly)

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `Wsd/Serializer.fs` module skeleton

**Purpose**: Establish the module file with correct namespace, module declaration, and function signatures.

**Steps**:
1. Create `src/Frank.Statecharts/Wsd/Serializer.fs`
2. Use `module internal Frank.Statecharts.Wsd.Serializer`
3. Open `Frank.Statecharts.Wsd.Types`
4. Add the three function signatures:
   - `val needsQuoting : name: string -> bool`
   - `val quoteName : name: string -> string`
   - `val serialize : diagram: Diagram -> string`
5. Use `System.Text.StringBuilder` for the serialize implementation

**Files**:
- `src/Frank.Statecharts/Wsd/Serializer.fs` (new, ~80-120 lines when complete)

**Notes**: The module declaration must be `internal` to match `Wsd/Types.fs`, `Wsd/Lexer.fs`, `Wsd/GuardParser.fs`, and `Wsd/Parser.fs`.

---

### Subtask T002 -- Implement `needsQuoting`

**Purpose**: Determine if a WSD participant name requires quoting (double quotes) because it contains non-identifier characters.

**Steps**:
1. Implement `needsQuoting` that returns `true` if the name contains any character that is NOT alphanumeric, underscore, or hyphen
2. Per the Lexer's escaping rules (research.md R-02): identifiers can contain alphanumeric, underscore, and hyphen characters
3. Empty strings should return `false` (edge case)

**Implementation**:
```fsharp
let needsQuoting (name: string) : bool =
    if System.String.IsNullOrEmpty(name) then false
    else name |> Seq.exists (fun c -> not (System.Char.IsLetterOrDigit(c) || c = '_' || c = '-'))
```

**Validation**:
- `needsQuoting "Locked"` -> `false`
- `needsQuoting "my state"` -> `true` (space)
- `needsQuoting "state-1"` -> `false` (hyphen is OK)
- `needsQuoting "state_1"` -> `false` (underscore is OK)
- `needsQuoting "state.name"` -> `true` (dot)

---

### Subtask T003 -- Implement `quoteName`

**Purpose**: Wrap a participant name in double quotes if it requires quoting, escaping any internal double quotes.

**Steps**:
1. If `needsQuoting name` is `false`, return the name unchanged
2. If quoting is needed, escape any `"` inside the name as `\"`, then wrap in `"..."`

**Implementation**:
```fsharp
let quoteName (name: string) : string =
    if needsQuoting name then
        sprintf "\"%s\"" (name.Replace("\"", "\\\""))
    else
        name
```

**Validation**:
- `quoteName "Locked"` -> `"Locked"` (no quoting needed)
- `quoteName "my state"` -> `"\"my state\""` (quoted)
- `quoteName "say \"hello\""` -> `"\"say \\\"hello\\\"\""` (escaped quotes)

---

### Subtask T004 -- Implement `serialize`

**Purpose**: Convert a `Diagram` AST to WSD text string following the formatting conventions from research.md R-02.

**Steps**:
1. Create a `StringBuilder`
2. **Title**: If `diagram.Title` is `Some title`, emit `title <title>\n\n`
3. **Participants**: Iterate `diagram.Elements`, for each `ParticipantDecl p`:
   - Emit `participant <quoteName p.Name>`
   - If `p.Alias` is `Some alias`, append ` as <quoteName alias>`
   - Append `\n`
4. After all participant declarations, emit a blank line (`\n`)
5. **Body elements**: Iterate `diagram.Elements` again, skipping `ParticipantDecl` and `TitleDirective` (already handled):
   - `MessageElement m`:
     - Determine arrow string: `Solid + Forward` = `"->"`, `Dashed + Forward` = `"-->"`, `Solid + Deactivating` = `"->-"`, `Dashed + Deactivating` = `"-->-"`
     - Emit `<quoteName m.Sender><arrow><quoteName m.Receiver>`
     - If `m.Label` is non-empty, append `: <label>`
     - If `m.Parameters` is non-empty, append `(<param1>, <param2>, ...)` to the label
     - Append `\n`
   - `NoteElement n`:
     - Determine position string: `Over` = `"over"`, `LeftOf` = `"left of"`, `RightOf` = `"right of"`
     - Emit `note <position> <quoteName n.Target>: `
     - If `n.Guard` is `Some guard` and `guard.Pairs` is non-empty:
       - Emit `[guard: <key1>=<value1>, <key2>=<value2>]`
       - If `n.Content` is also non-empty, append ` <content>` after the guard
     - Else emit `<n.Content>`
     - Append `\n`
   - `GroupElement g`:
     - Emit the group kind keyword (alt, opt, loop, par, break, critical, ref)
     - If first branch has a condition, append ` <condition>`
     - Emit `\n`
     - Recursively serialize branch elements (indented or flat -- keep flat for simplicity, parser does not require indentation)
     - For subsequent branches, emit `else` + optional condition + `\n` + branch elements
     - Emit `end\n`
   - `AutoNumberDirective`: Emit `autonumber\n`
   - Skip `TitleDirective` (already handled above)
6. Return `sb.ToString()`

**Important formatting rules**:
- Use explicit `\n` (not `Environment.NewLine` or `StringBuilder.AppendLine`) to ensure Unix line endings
- Blank line after participant declarations (before messages/notes)
- No trailing newline after the last element (or one trailing `\n` is fine)

**Files**:
- `src/Frank.Statecharts/Wsd/Serializer.fs` (main implementation)

**Edge Cases**:
- Empty diagram (no title, no elements): return empty string
- Diagram with only a title: `"title <title>\n\n"`
- Single participant, no messages: `"title ...\n\nparticipant X\n\n"`
- Self-message: sender and receiver are the same name -- valid WSD

---

### Subtask T005 -- Add `Wsd/Serializer.fs` to `.fsproj`

**Purpose**: Register the new file in the F# project's compile items in the correct order.

**Steps**:
1. Edit `src/Frank.Statecharts/Frank.Statecharts.fsproj`
2. Add `<Compile Include="Wsd/Serializer.fs" />` AFTER `<Compile Include="Wsd/Parser.fs" />` and BEFORE `<Compile Include="Types.fs" />`
3. The compile order must be: `Wsd/Types.fs` -> `Wsd/Lexer.fs` -> `Wsd/GuardParser.fs` -> `Wsd/Parser.fs` -> `Wsd/Serializer.fs` -> (later: `Wsd/Generator.fs`) -> `Types.fs` -> ...

**Files**:
- `src/Frank.Statecharts/Frank.Statecharts.fsproj`

**Expected `.fsproj` compile items after edit**:
```xml
<Compile Include="Wsd/Types.fs" />
<Compile Include="Wsd/Lexer.fs" />
<Compile Include="Wsd/GuardParser.fs" />
<Compile Include="Wsd/Parser.fs" />
<Compile Include="Wsd/Serializer.fs" />
<Compile Include="Types.fs" />
...
```

---

### Subtask T006 -- Create `Wsd/SerializerTests.fs`

**Purpose**: Comprehensive unit tests for the serializer covering all element types, formatting rules, and edge cases.

**Steps**:
1. Create `test/Frank.Statecharts.Tests/Wsd/SerializerTests.fs`
2. Module: `module Frank.Statecharts.Tests.Wsd.SerializerTests`
3. Open `Expecto`, `Frank.Statecharts.Wsd.Types`, `Frank.Statecharts.Wsd.Serializer`, `Frank.Statecharts.Wsd.Parser`
4. Define a synthetic position helper: `let pos = { Line = 0; Column = 0 }`
5. Add `[<Tests>] let serializerTests = testList "Serializer" [...]`

**Test cases to implement** (minimum):

| Test Name | What It Verifies |
|-----------|-----------------|
| `title emission` | `serialize { Title = Some "Test"; ... }` starts with `"title Test\n"` |
| `no title` | `serialize { Title = None; ... }` does not contain `"title"` |
| `participant declarations` | Each `ParticipantDecl` produces `"participant <name>\n"` |
| `participant ordering` | Participants appear in element order (initial state first) |
| `participant with alias` | `ParticipantDecl { Alias = Some "A" }` produces `"participant Name as A\n"` |
| `quoted participant name` | Name with spaces produces `"participant \"my state\"\n"` |
| `solid forward arrow` | Message with `Solid + Forward` produces `"A->B: label\n"` |
| `dashed forward arrow` | Message with `Dashed + Forward` produces `"A-->B: label\n"` |
| `solid deactivating arrow` | `Solid + Deactivating` produces `"A->-B: label\n"` |
| `dashed deactivating arrow` | `Dashed + Deactivating` produces `"A-->-B: label\n"` |
| `message with parameters` | Parameters produce `"A->B: method(p1, p2)\n"` |
| `message with empty label` | No label produces `"A->B\n"` (no colon) |
| `note over` | `NoteElement { NotePosition = Over }` produces `"note over X: text\n"` |
| `note with guard` | Guard annotation produces `"note over X: [guard: role=admin]\n"` |
| `note with multiple guards` | Multiple pairs produce `"note over X: [guard: role=admin, auth=bearer]\n"` |
| `guard plus content` | Guard + content produces `"note over X: [guard: role=admin] extra text\n"` |
| `empty diagram` | No elements produces empty string or minimal output |
| `single participant no messages` | Valid WSD with just participant declaration |
| `self-message` | Sender == receiver produces `"X->X: label\n"` |
| `autonumber directive` | `AutoNumberDirective` produces `"autonumber\n"` |
| `roundtrip validation` | Serialize a diagram, parse via `parseWsd`, verify zero errors |
| `Unix line endings` | Output contains `\n` and NOT `\r\n` |

6. Add `<Compile Include="Wsd/SerializerTests.fs" />` to test `.fsproj` BEFORE `<Compile Include="Wsd/RoundTripTests.fs" />` (or after existing Wsd test entries, before `Program.fs`)

**Files**:
- `test/Frank.Statecharts.Tests/Wsd/SerializerTests.fs` (new, ~150-200 lines)
- `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` (add compile item)

**Notes**: Tests use `[<Tests>]` attribute for Expecto auto-discovery. No changes to `Program.fs` needed.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Serializer output not parseable by existing parser | Every test serializes and re-parses to verify round-trip |
| Quoting logic misses edge cases | Test special characters: spaces, dots, colons, quotes |
| Group serialization is complex | Keep it simple (flat output, no indentation); parser accepts flat groups |

## Review Guidance

- Verify `module internal` declaration
- Verify file is added to `.fsproj` in correct order
- Verify all arrow styles produce the correct WSD syntax
- Verify `\n` line endings (not `Environment.NewLine`)
- Verify guard annotation format matches `GuardParser.tryParseGuard` expected input
- Confirm `parseWsd` succeeds on serializer output in at least one test

## Activity Log

- 2026-03-15T23:59:06Z -- system -- lane=planned -- Prompt created.
