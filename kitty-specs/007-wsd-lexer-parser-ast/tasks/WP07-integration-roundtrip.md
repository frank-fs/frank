---
work_package_id: "WP07"
title: "Integration, round-trip tests + build verification"
lane: "planned"
dependencies: ["WP05", "WP06"]
requirement_refs: ["FR-002", "FR-003", "FR-004", "FR-005", "FR-006", "FR-007", "FR-008", "FR-008a", "FR-009", "FR-010", "FR-011", "FR-012", "FR-013"]
subtasks: ["T039", "T040", "T041", "T042", "T043", "T044"]
history:
  - timestamp: "2026-03-07T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# WP07: Integration, Round-Trip Tests + Build Verification

## Implementation Command

```
spec-kitty implement WP07 --base WP05
```

## Objectives

Final integration work package that ties all parser components together and validates them against real-world WSD examples. This includes:

1. The `parseWsd` convenience function (end-to-end: string -> ParseResult)
2. Round-trip tests against Amundsen's published examples (onboarding, tic-tac-toe)
3. Edge case tests covering the full spec
4. `.fsproj` file updates with correct compile ordering
5. Multi-target build verification (net8.0/net9.0/net10.0)

**Test file**: `test/Frank.Statecharts.Tests/Wsd/RoundTripTests.fs`
**Modified files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`, `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`

## Success Criteria

- `parseWsd` correctly parses the Amundsen onboarding WSD example end-to-end (SC-001)
- `parseWsd` correctly parses the tic-tac-toe WSD example with guard extensions (SC-002)
- All four arrow types produce correct AST nodes (SC-003)
- 5+ level nesting works without error (SC-004)
- All invalid inputs produce failures with position and corrective example (SC-005)
- Library compiles under multi-target net8.0/net9.0/net10.0 (SC-006)
- All edge cases from spec.md are covered by tests
- `dotnet build` and `dotnet test` pass on all targets

## Context & Constraints

- **Depends on**: WP05 (grouping blocks complete), WP06 (error recovery + guard integration complete)
- **Integration scope**: This WP does not write new parser logic. It validates that all pieces work together and adds the remaining edge case coverage.
- **Amundsen examples**: The onboarding and tic-tac-toe WSD examples should be representative of real-world usage. If exact examples are not available, create realistic approximations based on the spec's description.
- **Build configuration**: The `.fsproj` files must list Wsd/ files in dependency order. If Frank.Statecharts project doesn't exist yet (#87), document exactly what's needed.

## Subtasks & Detailed Guidance

### T039: Convenience parseWsd Function

Verify and finalize the `parseWsd` function that provides the one-call entry point. `parseWsd` internally calls `parse` with `maxErrors=50` (the FR-008 default).

**Signature**: `val parseWsd: source: string -> ParseResult`

**Implementation**:
```fsharp
let parseWsd (source: string) : ParseResult =
    let tokens = Lexer.tokenize source
    parse tokens 50
```

This should already exist from WP03. Verify:
- It calls `Lexer.tokenize` then `parse`
- Default error limit is 50 (FR-008 default)
- It handles null/empty input gracefully (empty string → empty diagram, null → either empty diagram or ArgumentNullException — decide and document)

**Additional convenience functions to consider**:
- `parseWsdWithLimit: source: string -> maxErrors: int -> ParseResult` — explicit error limit
- These are optional and should only be added if the test suite or downstream consumers need them

**Tests**:
- `parseWsd ""` → empty diagram, no errors
- `parseWsd null` → decide behavior, test it
- `parseWsd "participant Client\nClient->Client: self"` → self-message works
- Verify the convenience function produces identical results to `Lexer.tokenize >> parse`

### T040: Round-Trip Tests - Amundsen Onboarding Example (SC-001)

Create a comprehensive end-to-end test using a realistic WSD onboarding example.

**Example WSD** (based on spec description — create a realistic Amundsen-style onboarding flow):
```
title Onboarding Flow
autonumber

participant Client
participant API
participant DB

Client->API: createAccount(name, email)
note over API: [guard: auth=none]
API->DB: insertUser(name, email)
DB->-API: userId
API->-Client: 201 Created

Client-->API: getProfile()
note over API: [guard: auth=bearer]
API-->DB: selectUser(userId)
DB-->-API: userData
API-->-Client: 200 OK
```

**Test verifications** (walk the entire AST):
1. Title is "Onboarding Flow"
2. AutoNumber is true
3. Three explicit participants: Client, API, DB
4. Elements in source order: 2 participant decls (after title/autonumber), then messages and notes interleaved
5. First message: Client->API, Solid/Forward, label "createAccount", params ["name"; "email"]
6. Guard on first note: `[guard: auth=none]` → pairs = [("auth", "none")]
7. Return messages (`->-` and `-->-`) have correct ArrowStyle and Direction
8. Dashed messages (`-->`) have ArrowStyle.Dashed
9. All participant names in messages match declared participants
10. No errors, no warnings (all participants declared, all syntax valid)

**Round-trip concept**: Parse the WSD, walk the AST, and verify every element maps back to the expected syntax. This doesn't require literally regenerating WSD text — just verify the AST captures all information needed to reconstruct it.

Write at least 10 assertions covering the full AST structure.

### T041: Round-Trip Tests - Tic-Tac-Toe with Guards (SC-002)

Create an end-to-end test using a tic-tac-toe game flow with guard extensions.

**Example WSD**:
```
title Tic-Tac-Toe Game

participant PlayerX
participant PlayerO
participant Board
participant GameEngine

note over PlayerX: [guard: role=PlayerX, state=XTurn]
PlayerX->Board: makeMove(position)
Board->GameEngine: validateMove(position)
GameEngine->-Board: valid

alt win condition
    GameEngine->-Board: gameOver(winner=X)
    Board->-PlayerX: youWin
else continue
    note over PlayerO: [guard: role=PlayerO, state=OTurn]
    PlayerO->Board: makeMove(position)
    Board->GameEngine: validateMove(position)
    GameEngine->-Board: valid
end
```

**Test verifications**:
1. Title is "Tic-Tac-Toe Game"
2. Four participants: PlayerX, PlayerO, Board, GameEngine
3. First note has guard: role=PlayerX, state=XTurn (two pairs)
4. Alt block with two branches: "win condition" and "continue"
5. Second branch contains a note with guard: role=PlayerO, state=OTurn
6. Messages have correct arrow styles and parameters
7. Nested group structure is correct
8. No errors (valid WSD with guards)
9. Warnings: none (all participants declared)

Write at least 10 assertions covering guards, groups, and message structure.

### T042: Edge Case Tests

Cover all edge cases listed in the spec.

**Edge cases from spec.md**:

1. **Deeply nested grouping blocks (5+ levels)**: already tested in WP05, but include in round-trip suite for completeness
2. **Mixed arrow styles within the same group block**: messages with `->`, `-->`, `->-`, `-->-` all inside one `alt`
3. **Malformed guard syntax**: unclosed brackets, missing equals, empty key/value — verify in note context
4. **Unicode characters**: participant names with Unicode (`participant Utilisateur`), message text with Unicode, note content with Unicode
5. **Empty diagrams**: zero elements after directives → valid empty AST
6. **Comment lines**: `# this is a comment` is ignored
7. **Whitespace-only lines**: ignored, produce only Newline tokens
8. **Duplicate participant declarations**: second `participant Client` is a no-op
9. **Implicit participants by first message appearance**: register on first use
10. **Title with special characters**: `title My API: v2.0 [beta]`
11. **Messages with no parameters vs. empty parens vs. multiple params**:
    - `Client->Server: getData` → params = []
    - `Client->Server: getData()` → params = []
    - `Client->Server: getData(a, b, c)` → params = ["a"; "b"; "c"]
12. **Tabs vs. spaces**: both accepted, indentation not significant
13. **Windows and Unix line endings**: mixed `\r\n` and `\n` in same file

**Tests**: One test case per edge case above. Group related cases into test lists.

```fsharp
testCase "unicode participant names" <| fun _ ->
    let result = parseWsd """
participant Utilisateur
participant Serveur
Utilisateur->Serveur: requete
"""
    Expect.isEmpty result.Errors "no errors"
    Expect.equal result.Diagram.Participants.[0].Name "Utilisateur" "unicode name"

testCase "title with special characters" <| fun _ ->
    let result = parseWsd "title My API: v2.0 [beta]"
    Expect.equal result.Diagram.Title (Some "My API: v2.0 [beta]") "special chars in title"

testCase "empty input" <| fun _ ->
    let result = parseWsd ""
    Expect.isEmpty result.Errors "no errors"
    Expect.isEmpty result.Diagram.Elements "no elements"

testCase "comments ignored" <| fun _ ->
    let result = parseWsd """
# this is a comment
participant Client
# another comment
Client->Client: self
"""
    Expect.isEmpty result.Errors "no errors"
    Expect.equal result.Diagram.Participants.Length 1 "one participant"
```

Write at least 15 test cases covering all edge cases.

### T043: .fsproj Compile Item Ordering

Ensure the `Frank.Statecharts.fsproj` file includes all Wsd/ source files in the correct dependency order.

**Required order in `<ItemGroup>` (Compile items)**:
```xml
<!-- Existing files... -->
<Compile Include="Wsd/Types.fs" />
<Compile Include="Wsd/Lexer.fs" />
<Compile Include="Wsd/GuardParser.fs" />
<Compile Include="Wsd/Parser.fs" />
<!-- Other existing files... -->
```

**Key constraint**: F# compile order matters. Types.fs must precede all others. Lexer.fs and GuardParser.fs must precede Parser.fs. Lexer.fs and GuardParser.fs can be in either order relative to each other (they don't depend on each other).

**Test project**: Ensure `Frank.Statecharts.Tests.fsproj` includes all Wsd/ test files:
```xml
<Compile Include="Wsd/LexerTests.fs" />
<Compile Include="Wsd/GuardParserTests.fs" />
<Compile Include="Wsd/ParserTests.fs" />
<Compile Include="Wsd/GroupingTests.fs" />
<Compile Include="Wsd/ErrorTests.fs" />
<Compile Include="Wsd/RoundTripTests.fs" />
```

**InternalsVisibleTo**: Verify the Frank.Statecharts project has `[<assembly: InternalsVisibleTo("Frank.Statecharts.Tests")>]` (or the equivalent MSBuild property). Without this, the test project cannot access the `internal` WSD parser types.

**Verification**: `dotnet build` succeeds for both projects.

### T044: Multi-Target Build Verification

Verify the library compiles under all three target frameworks (SC-006).

**Steps**:
1. Run `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` — verify it builds for net8.0, net9.0, and net10.0
2. Run `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` — verify all tests pass
3. Check for any conditional compilation issues (`#if NET8_0` etc.) — the WSD parser should have NONE (pure F#, no platform-specific code)
4. Verify no warnings during build (treat warnings as errors if the project is configured that way)

**If Frank.Statecharts project doesn't exist**: Document exactly what's needed:
- Project file with `<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>`
- AssemblyInfo with `InternalsVisibleTo`
- Compile items in correct order
- Create a minimal project shell if appropriate

**Tests**: The build itself is the test. No additional test code needed — if `dotnet build` and `dotnet test` pass on all targets, this subtask is complete.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Amundsen example WSD may not match exact published format | Create realistic approximations based on spec descriptions. The parser should handle any valid WSD, not just one specific example. |
| WSD files integration into Frank.Statecharts.fsproj | Frank.Statecharts exists on master. Add WSD source files to the existing Compile ItemGroup in the .fsproj. |
| Unicode edge cases in lexer | Test with representative Unicode (accented Latin, CJK if ambitious). The lexer's identifier scanner must handle Unicode letters. |
| .fsproj merge conflicts when multiple WPs modify it | Each WP adds its files; this WP verifies final order. Resolve conflicts by ensuring dependency order. |

## Review Guidance

- Verify Amundsen onboarding example test walks the FULL AST (not just element count)
- Verify tic-tac-toe test verifies guard annotations on notes
- Verify all 13 edge cases from spec.md have at least one test
- Verify `.fsproj` compile order is correct (Types before Lexer before GuardParser before Parser)
- Verify InternalsVisibleTo is configured
- Run `dotnet build` on all three target frameworks
- Run `dotnet test` and verify all tests pass
- Verify no build warnings

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-07T00:00:00Z | system | Prompt generated via /spec-kitty.tasks |
