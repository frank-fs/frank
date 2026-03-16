---
work_package_id: "WP01"
subtasks:
  - "T001"
  - "T002"
  - "T003"
  - "T004"
  - "T005"
  - "T006"
  - "T007"
title: "Foundation -- ALPS Types, Golden Files, and Project Wiring"
phase: "Phase 0 - Setup & Foundation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: []
requirement_refs: ["FR-001", "FR-002", "FR-003", "FR-004", "FR-005", "FR-006", "FR-007", "FR-008", "FR-018"]
history:
  - timestamp: "2026-03-16T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP01 -- Foundation -- ALPS Types, Golden Files, and Project Wiring

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

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<descriptor>` ``
Use language identifiers in code blocks: ````fsharp`, ````xml`, ````json`

---

## Implementation Command

```bash
spec-kitty implement WP01
```

No `--base` flag needed (this is the first work package).

---

## Objectives & Success Criteria

1. All ALPS-specific AST types defined in `src/Frank.Statecharts/Alps/Types.fs` with structural equality.
2. Golden file string constants for tic-tac-toe (JSON + XML) and onboarding (JSON + XML) in `test/Frank.Statecharts.Tests/Alps/GoldenFiles.fs`.
3. Type construction and equality tests passing in `test/Frank.Statecharts.Tests/Alps/TypeTests.fs`.
4. `dotnet build` succeeds for `src/Frank.Statecharts/Frank.Statecharts.fsproj` (multi-target net8.0;net9.0;net10.0).
5. `dotnet test` succeeds for `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` (net10.0).

## Context & Constraints

- **Architecture**: ALPS types are self-contained (AD-001). No dependency on spec 020 shared AST.
- **Module style**: Follow the WSD pattern (`module internal Frank.Statecharts.Wsd.Types`). ALPS module: `module internal Frank.Statecharts.Alps.Types`.
- **Data model**: See `kitty-specs/011-alps-parser-generator/data-model.md` for complete entity definitions.
- **Research**: See `kitty-specs/011-alps-parser-generator/research.md` for ALPS JSON/XML format reference and golden file guidance.
- **Existing pattern**: Reference `src/Frank.Statecharts/Wsd/Types.fs` for discriminated union and struct patterns.
- **Test pattern**: Reference `test/Frank.Statecharts.Tests/Wsd/ParserTests.fs` for Expecto test list structure.
- **Constitution**: Principle II (Idiomatic F#) -- use discriminated unions, option types, record types with structural equality.

## Subtasks & Detailed Guidance

### Subtask T001 -- Define ALPS AST Types in Alps/Types.fs

- **Purpose**: Create the typed F# AST that both parsers produce and the generator consumes. This is the core data model for all ALPS operations.
- **File**: `src/Frank.Statecharts/Alps/Types.fs` (new file, create `Alps/` directory)
- **Module**: `module internal Frank.Statecharts.Alps.Types`

**Types to define (from data-model.md):**

```fsharp
// Source position for XML parse errors (1-based)
[<Struct>]
type AlpsSourcePosition = { Line: int; Column: int }

// Parse error with optional position
type AlpsParseError =
    { Description: string
      Position: AlpsSourcePosition option }

// ALPS documentation element (doc)
type AlpsDocumentation =
    { Format: string option  // defaults to "text" per ALPS spec
      Value: string }

// ALPS extension element (ext)
type AlpsExtension =
    { Id: string
      Href: string option
      Value: string option }

// ALPS link element
type AlpsLink =
    { Rel: string
      Href: string }

// ALPS descriptor type discriminated union (FR-003)
type DescriptorType =
    | Semantic
    | Safe
    | Unsafe
    | Idempotent

// ALPS descriptor -- the core element (self-referential for nesting)
type Descriptor =
    { Id: string option            // required for inline, absent for href-only
      Type: DescriptorType         // defaults to Semantic if omitted (FR-006)
      Href: string option          // local fragment (#id) or external URL
      ReturnType: string option    // rt value
      Documentation: AlpsDocumentation option
      Descriptors: Descriptor list // nested children (self-referential)
      Extensions: AlpsExtension list
      Links: AlpsLink list }

// Root ALPS document
type AlpsDocument =
    { Version: string option
      Documentation: AlpsDocumentation option
      Descriptors: Descriptor list
      Links: AlpsLink list
      Extensions: AlpsExtension list }
```

**Key requirements**:
- `AlpsSourcePosition` must be `[<Struct>]` (value type, matches WSD pattern).
- All types use F# structural equality (default for records and DUs).
- `Descriptor` is self-referential via `Descriptors: Descriptor list`.
- `DescriptorType` defaults to `Semantic` -- this is enforced by the parsers, not the type itself.
- Lists are used for ordered collections (not arrays).

**Validation**: After creating, verify `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds (requires T002 first).

### Subtask T002 -- Update Frank.Statecharts.fsproj

- **Purpose**: Wire Alps/Types.fs into the F# compile order so it compiles with the main project.
- **File**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Steps**:
  1. Add `<Compile Include="Alps/Types.fs" />` after `<Compile Include="Wsd/Parser.fs" />` and before `<Compile Include="Types.fs" />`.
  2. This follows the pattern established by WSD: format-specific files grouped before the main Types.fs.

**Expected compile order after change:**
```xml
<Compile Include="Wsd/Types.fs" />
<Compile Include="Wsd/Lexer.fs" />
<Compile Include="Wsd/GuardParser.fs" />
<Compile Include="Wsd/Parser.fs" />
<Compile Include="Alps/Types.fs" />
<Compile Include="Types.fs" />
...
```

**Note**: Only add Alps/Types.fs now. Later WPs (WP02-WP05) will add JsonParser.fs, XmlParser.fs, JsonGenerator.fs, and Mapper.fs.

- **Validation**: Run `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` -- must succeed on all three targets.

### Subtask T003 -- Tic-Tac-Toe ALPS JSON Golden File

- **Purpose**: Provide the primary test golden file. This JSON represents the tic-tac-toe state machine as an ALPS document with all states, transitions, guard extensions, and documentation.
- **File**: `test/Frank.Statecharts.Tests/Alps/GoldenFiles.fs` (new file, create `Alps/` directory)
- **Module**: `module internal Frank.Statecharts.Tests.Alps.GoldenFiles`

**Content**: A `[<Literal>]` string constant `ticTacToeAlpsJson` containing the full ALPS JSON document.

**Structure to include:**
- Root `alps` object with `version: "1.0"`
- Top-level `doc` element describing the tic-tac-toe game
- Data element descriptors (semantic): `gameState`, `position`, `player`
- State descriptors (semantic, containing transition hrefs): `XTurn`, `OTurn`, `Won`, `Draw`
- Transition descriptors: `makeMove` (type `unsafe`), `viewGame` (type `safe`)
- For `makeMove`: One descriptor per target state due to single-valued `rt`:
  - `makeMove` from XTurn to OTurn (rt: `#OTurn`), with ext guard `role=PlayerX`
  - `makeMove` from XTurn to Won (rt: `#Won`), with ext guard `wins`
  - `makeMove` from XTurn to Draw (rt: `#Draw`), with ext guard `boardFull`
  - `makeMove` from OTurn to XTurn (rt: `#XTurn`), with ext guard `role=PlayerO`
  - `makeMove` from OTurn to Won (rt: `#Won`), with ext guard `wins`
  - `makeMove` from OTurn to Draw (rt: `#Draw`), with ext guard `boardFull`
- `viewGame` (type `safe`) available from all states
- Nested parameter descriptors under transition descriptors (e.g., `position` under `makeMove`)
- `link` element: `{ "rel": "self", "href": "http://example.com/alps/tic-tac-toe" }`

**Reference**: See research.md Research Area 1 for ALPS JSON structure and Research Area 4 for golden file content guidance.

**Important**: The JSON must be valid and parseable by `System.Text.Json.JsonDocument`.

### Subtask T004 -- Tic-Tac-Toe ALPS XML Golden File

- **Purpose**: Provide the XML equivalent of T003 for cross-format testing (User Story 2 requires identical AST from both formats).
- **File**: Same file as T003: `test/Frank.Statecharts.Tests/Alps/GoldenFiles.fs`

**Content**: A `[<Literal>]` string constant `ticTacToeAlpsXml` containing the equivalent ALPS XML document.

**Key XML mapping rules (from research.md):**
- Root element: `<alps version="1.0">`
- `<doc format="text">Description text</doc>` (element with text content)
- `<descriptor id="..." type="..." href="..." rt="...">` (attributes)
- Nested `<descriptor>` elements for children
- `<ext id="..." value="..."/>` (attributes)
- `<link rel="..." href="..."/>` (attributes)

**The XML must produce the exact same AlpsDocument AST as the JSON when parsed.**

### Subtask T005 -- Onboarding ALPS Golden Files (JSON + XML)

- **Purpose**: Provide a second, simpler golden file for testing. The onboarding example represents a linear state machine, contrasting with tic-tac-toe's cyclic graph.
- **File**: Same file: `test/Frank.Statecharts.Tests/Alps/GoldenFiles.fs`

**Content**: Two `[<Literal>]` string constants: `onboardingAlpsJson` and `onboardingAlpsXml`.

**Structure:**
- States: `Welcome`, `CollectEmail`, `CollectProfile`, `Review`, `Complete`
- Transitions: `start` (safe, Welcome -> CollectEmail), `submitEmail` (unsafe, CollectEmail -> CollectProfile), `submitProfile` (unsafe, CollectProfile -> Review), `confirmReview` (unsafe, Review -> Complete), `editEmail` (safe, Review -> CollectEmail), `editProfile` (safe, Review -> CollectProfile)
- Data elements: `email`, `name`, `bio`
- Top-level doc describing the onboarding flow
- No guard extensions (simpler example)

### Subtask T006 -- TypeTests.fs

- **Purpose**: Validate that ALPS AST types support correct construction and structural equality, and that discriminated union cases are exhaustive.
- **File**: `test/Frank.Statecharts.Tests/Alps/TypeTests.fs` (new file)
- **Module**: `module Frank.Statecharts.Tests.Alps.TypeTests`

**Test cases to implement (Expecto test list):**

```fsharp
[<Tests>]
let typeTests = testList "Alps.Types" [
    // Construction tests
    testCase "empty AlpsDocument can be constructed" <| fun _ -> ...
    testCase "Descriptor with all fields populated" <| fun _ -> ...
    testCase "Descriptor with minimal fields (id and default type)" <| fun _ -> ...
    testCase "nested Descriptors preserve hierarchy" <| fun _ -> ...

    // Structural equality tests
    testCase "two identical AlpsDocuments are equal" <| fun _ -> ...
    testCase "two different AlpsDocuments are not equal" <| fun _ -> ...
    testCase "Descriptor equality includes nested children" <| fun _ -> ...

    // DescriptorType tests
    testCase "all four DescriptorType cases exist" <| fun _ -> ...

    // AlpsParseError tests
    testCase "AlpsParseError with position" <| fun _ -> ...
    testCase "AlpsParseError without position" <| fun _ -> ...
]
```

**Pattern**: Follow `test/Frank.Statecharts.Tests/Wsd/ParserTests.fs` for Expecto test structure. Use `Expect.equal`, `Expect.isNone`, `Expect.isSome`, etc.

### Subtask T007 -- Update Test Project fsproj

- **Purpose**: Wire all Alps test files into the test project compile order.
- **File**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
- **Steps**:
  1. Add Alps test files BEFORE `<Compile Include="Program.fs" />` (Program.fs must be last).
  2. Add in this order (GoldenFiles first so other test modules can reference them):

```xml
<Compile Include="Alps/GoldenFiles.fs" />
<Compile Include="Alps/TypeTests.fs" />
```

**Note**: Only add files created in this WP. Later WPs will add JsonParserTests.fs, XmlParserTests.fs, JsonGeneratorTests.fs, MapperTests.fs, RoundTripTests.fs.

- **Validation**: Run `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` -- all existing tests plus new TypeTests must pass.

## Risks & Mitigations

- **Golden file accuracy**: The golden files anchor all downstream testing. Cross-reference ALPS JSON structure from research.md Research Area 1 carefully. If a golden file has an error, all parser and generator tests will produce confusing failures.
- **fsproj ordering**: F# is order-sensitive. If Alps/Types.fs is placed incorrectly, the build will fail with unresolved type errors. Verify with `dotnet build` immediately after modifying the fsproj.
- **Self-referential Descriptor type**: F# handles recursive record types natively. No special annotation needed for `Descriptors: Descriptor list`.

## Review Guidance

- Verify all types match data-model.md entity definitions exactly.
- Verify golden file JSON is valid (can be parsed by `JsonDocument.Parse`).
- Verify golden file XML is valid (can be parsed by `XDocument.Parse`).
- Verify golden file JSON and XML represent the same semantic content.
- Verify fsproj compile orders are correct (build succeeds on all targets).
- Verify TypeTests cover construction, equality, and all DescriptorType cases.

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

### How to Add Activity Log Entries

**When adding an entry**:
1. Scroll to the bottom of this Activity Log section
2. **APPEND the new entry at the END** (do NOT prepend or insert in middle)
3. Use exact format: `- YYYY-MM-DDTHH:MM:SSZ - agent_id - lane=<lane> - <action>`
4. Timestamp MUST be current time in UTC

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

- 2026-03-16T00:00:00Z - system - lane=planned - Prompt created.
