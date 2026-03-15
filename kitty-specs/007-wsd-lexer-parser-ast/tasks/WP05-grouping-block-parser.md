---
work_package_id: "WP05"
title: "Grouping block parser"
lane: "done"
dependencies: ["WP03"]
requirement_refs: ["FR-006"]
subtasks: ["T028", "T029", "T030", "T031"]
agent: "claude-opus-reviewer"
shell_pid: "42640"
reviewed_by: "Ryan Riley"
review_status: "approved"
history:
  - timestamp: "2026-03-07T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# WP05: Grouping Block Parser

## Implementation Command

```
spec-kitty implement WP05 --base WP03
```

## Objectives

Implement parsing of all seven WSD grouping block types (`alt`, `opt`, `loop`, `par`, `break`, `critical`, `ref`) with full support for `else` branches, `end` terminators, and arbitrary nesting depth. This extends the core parser (WP03) by replacing the group-parsing stub with a recursive descent implementation.

**Modified file**: `src/Frank.Statecharts/Wsd/Parser.fs` (extend existing parser)
**Test file**: `test/Frank.Statecharts.Tests/Wsd/GroupingTests.fs`

## Success Criteria

- All seven grouping block kinds are recognized and produce correct `GroupElement` AST nodes
- `else` creates additional branches within a group
- `end` terminates the current group
- Nesting works to at least 5 levels deep without error or performance degradation (SC-004)
- Each branch preserves its condition text (or `None` for bare `else`)
- Child elements within branches are parsed recursively (messages, notes, nested groups)
- Unclosed blocks at EOF produce an error referencing the opening line (basic recovery)
- Mixed element types within group branches work correctly

## Context & Constraints

- **Depends on**: WP03 (Parser.fs with core infrastructure and element parsing)
- **Modifies**: `src/Frank.Statecharts/Wsd/Parser.fs` — the `parseGroup` function stub from WP03 is replaced with the full implementation
- **Recursive descent**: Group body parsing calls back into the main `parseElements` loop (or a variant of it), enabling groups to contain any diagram element including other groups
- **Condition text**: The text after the group keyword (e.g., `alt success case`) is the condition. It's everything after the keyword up to the newline. Use `TextContent` token if the lexer emits it, or collect remaining tokens on the line.
- **Else branches**: `else` within a group starts a new branch. `else` may optionally have condition text (e.g., `else not found`).
- **End terminator**: `end` closes the innermost open group. If `end` appears with no open group, it's an error.
- **Par semantics**: `par` uses `else` to separate parallel branches (not conditional branches). The parser treats them identically — semantic interpretation is downstream.

## Subtasks & Detailed Guidance

### T028: All Seven Block Kinds

Implement `parseGroup` to handle all seven grouping block keywords.

**Token sequence for a basic block**:
```
<GroupKeyword> [TextContent(condition)] Newline
  <child elements>
End Newline
```

**Implementation**:
```fsharp
let parseGroup (state: ParserState) =
    let startToken = advance state  // consume group keyword (Alt, Opt, etc.)
    let kind =
        match startToken.Kind with
        | Alt -> GroupKind.Alt
        | Opt -> GroupKind.Opt
        | Loop -> GroupKind.Loop
        | Par -> GroupKind.Par
        | Break -> GroupKind.Break
        | Critical -> GroupKind.Critical
        | Ref -> GroupKind.Ref
        | _ -> failwith "parseGroup called with non-group token"

    // Parse condition text (optional, rest of line after keyword)
    let condition = parseConditionText state
    skipToNewline state

    // Parse branches (initial + else branches)
    let branches = parseBranches state kind startToken.Position

    let group = {
        Kind = kind
        Branches = branches
        Position = startToken.Position
    }
    state.Elements <- GroupElement group :: state.Elements
```

**Condition text parsing**: After the group keyword, if the next token on the same line is `TextContent` or `Identifier` or `Colon` + `TextContent`, collect it as the condition string. If the next token is `Newline`, condition is `None`.

For each block kind:
- `alt <condition>` — required condition text (but don't error if missing — emit warning)
- `opt <condition>` — required condition text
- `loop <condition>` — required condition text (e.g., "3 times", "for each item")
- `par` — optional condition text (usually none)
- `break <condition>` — required condition text
- `critical <condition>` — optional condition text
- `ref <text>` — required text (reference label)

**Tests**:
- Each of the seven block kinds with condition text
- `par` with no condition
- Block with empty body (just keyword + end)
- `ref Reference Name` — ref block with label

### T029: Else Branch Handling

Parse `else` keywords within a group to create multiple branches.

**Implementation approach**:

The branch parsing function collects elements until it hits `else`, `end`, or `Eof`:

```fsharp
let rec parseBranches (state: ParserState) (kind: GroupKind) (openPos: SourcePosition) : GroupBranch list =
    let firstBranch = parseBranchBody state kind openPos (Some firstCondition)
    // firstBranch includes elements until else/end/eof

    let rec collectElseBranches acc =
        skipNewlines state
        match (peek state).Kind with
        | Else ->
            let elseToken = advance state
            let elseCond = parseConditionText state
            skipToNewline state
            let branch = parseBranchBody state kind openPos elseCond
            collectElseBranches (branch :: acc)
        | End ->
            advance state |> ignore  // consume End
            skipToNewline state
            List.rev acc
        | Eof ->
            // Unclosed block
            addError state openPos
                (sprintf "Unclosed '%A' block" kind)
                "'end' keyword"
                "end of file"
                (sprintf "%s condition\n  ...\nend" (groupKindKeyword kind))
            List.rev acc
        | _ ->
            // Unexpected — should not happen if parseBranchBody stopped correctly
            List.rev acc

    collectElseBranches [firstBranch]
```

**Branch body parsing**: Parse elements within a branch until encountering `else`, `end`, or `Eof`. This is similar to the main parse loop but with additional stop conditions.

```fsharp
let parseBranchBody (state: ParserState) (kind: GroupKind) (openPos: SourcePosition) (condition: string option) : GroupBranch =
    let savedElements = state.Elements
    state.Elements <- []  // Start fresh for this branch

    let rec loop () =
        skipNewlines state
        let token = peek state
        match token.Kind with
        | Else | End | Eof -> ()  // Stop — caller handles these
        | _ ->
            parseOneElement state  // Parse one element (message, note, nested group, etc.)
            loop ()

    loop ()
    let branchElements = List.rev state.Elements
    state.Elements <- savedElements  // Restore outer context

    { Condition = condition; Elements = branchElements }
```

**Key design**: Save and restore the parent's element list when entering a branch, so branch elements are collected separately.

**Tests**:
- `alt` with two branches (initial + one else)
- `alt` with three branches (initial + two else)
- `else` with condition text: `else not found`
- `else` without condition text (bare else)
- `par` with three parallel branches (each separated by `else`)
- `opt` with no else (single branch)

### T030: Arbitrary Nesting Support

Ensure grouping blocks can nest to arbitrary depth by making group body parsing recursive.

**Implementation**: The branch body parsing function (`parseBranchBody` or equivalent) calls the same element parsing logic as the top level. When it encounters a group keyword (alt, opt, loop, etc.), it recursively calls `parseGroup`. This naturally supports nesting because `parseGroup` calls `parseBranchBody` which calls `parseOneElement` which can call `parseGroup` again.

**Stack depth consideration**: Recursive descent uses the call stack for nesting. 5 levels of nesting produces roughly 15-20 stack frames (3-4 functions per nesting level). This is well within any reasonable stack limit. No explicit depth tracking is needed for correctness, but adding a depth counter with a warning at extreme depths (e.g., 50+) is a reasonable safety measure.

**Implementation sketch for nested parsing**:
```fsharp
// In parseOneElement (or the main loop):
match token.Kind with
| Alt | Opt | Loop | Par | Break | Critical | Ref ->
    parseGroup state  // Recursive: parseGroup -> parseBranches -> parseBranchBody -> parseOneElement -> parseGroup
| ...
```

**Tests**:
- 2 levels: `alt` containing `opt`
- 3 levels: `alt` containing `loop` containing `opt`
- 5 levels: verify correct nesting structure (SC-004)
- Nested groups in different branches of the same parent
- Messages and notes interleaved with nested groups

**5-level nesting test example**:
```
alt level 1
  loop level 2
    opt level 3
      par level 4
        critical level 5
          Client->Server: deep message
        end
      end
    end
  end
end
```
Verify: The resulting AST has 5 levels of `GroupElement` nesting, each with the correct `GroupKind`, and the message is a child of the innermost group's branch.

### T031: Grouping Block Tests

Comprehensive test suite for grouping blocks. Use Expecto.

**Test file**: `test/Frank.Statecharts.Tests/Wsd/GroupingTests.fs`

**Test categories**:

1. **Basic blocks**: each of the seven kinds with a simple body
2. **Condition text**: blocks with and without condition text
3. **Else branches**: single else, multiple else, bare else, else with condition
4. **Nesting**: 2-level, 3-level, 5-level, nested in different branches
5. **Mixed content**: messages, notes, and groups within branches
6. **Empty branches**: branch with no elements between else/end
7. **Unclosed blocks**: block without `end` — error with opening line reference
8. **Mismatched end**: extra `end` with no open block — error
9. **Acceptance scenarios from spec.md US3**:
   - US3-S1: `alt` with two branches, condition text preserved
   - US3-S2: `loop` containing nested `opt`, conditions correct
   - US3-S3: `par` with three parallel branches
   - US3-S4: unclosed `alt` produces error with opening line number

**Example test**:
```fsharp
testCase "US3-S1: alt with two branches" <| fun _ ->
    let result = parseWsd """
participant Client
participant API

alt success
    Client->API: getResource()
    API->-Client: 200 OK
else not found
    API->-Client: 404 Not Found
end
"""
    Expect.isEmpty result.Errors "no errors"
    let groups = result.Diagram.Elements |> List.choose (function GroupElement g -> Some g | _ -> None)
    Expect.equal groups.Length 1 "one group"
    let g = groups.[0]
    Expect.equal g.Kind GroupKind.Alt "alt group"
    Expect.equal g.Branches.Length 2 "two branches"
    Expect.equal g.Branches.[0].Condition (Some "success") "first condition"
    Expect.equal g.Branches.[1].Condition (Some "not found") "else condition"
    Expect.equal g.Branches.[0].Elements.Length 2 "first branch: 2 elements"
    Expect.equal g.Branches.[1].Elements.Length 1 "else branch: 1 element"
```

Write at least 20 test cases covering all categories.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Recursive parsing blows the stack on pathological input | Add optional depth counter; warn at 50+ levels. Real WSD never nests this deep. |
| Element list save/restore in branch parsing introduces bugs | Unit test each branch in isolation; verify parent elements are not corrupted after group parsing. |
| `else` vs `Else` keyword — lexer must tokenize correctly | Depends on WP02 lexer tests passing. Verify `else` is `TokenKind.Else` not `Identifier "else"`. |
| Condition text parsing ambiguity (when does condition end?) | Condition is everything after the keyword until end of line. Use TextContent token or collect remaining tokens. |

## Review Guidance

- Verify all seven GroupKind values are handled in `parseGroup`
- Verify nesting works by inspecting the AST tree structure (not just element count)
- Verify condition text is captured on both initial block and else branches
- Verify unclosed blocks produce errors with the opening line number
- Verify element list save/restore: parent elements not lost after group parsing
- Run `dotnet build` and `dotnet test`

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-07T00:00:00Z | system | Prompt generated via /spec-kitty.tasks |
- 2026-03-15T19:34:21Z – unknown – lane=for_review – Moved to for_review
- 2026-03-15T19:45:29Z – claude-opus-reviewer – shell_pid=42640 – lane=doing – Started review via workflow command
- 2026-03-15T19:46:25Z – claude-opus-reviewer – shell_pid=42640 – lane=done – Review passed: All 4 subtasks (T028-T031) implemented. All 7 group kinds parsed with recursive descent. Else branch handling with condition text. Arbitrary nesting via mutual recursion with depth warning at 50+. Branch body isolation via save/restore of state.Elements. 26 grouping tests covering all kinds, nesting 2-5 levels, multiple branches, empty branches, unclosed blocks, stray end. Builds clean, 170 tests pass.
