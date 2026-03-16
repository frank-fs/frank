---
work_package_id: WP03
title: LabelParser & Parser
lane: "doing"
dependencies: [WP02]
base_branch: 013-smcat-parser-generator-WP02
base_commit: 60c578d302419ad1e2329f103e71ed6539970372
created_at: '2026-03-16T04:31:54.360856+00:00'
subtasks:
- T012
- T013
- T014
- T015
- T016
- T017
- T018
- T019
phase: Phase 1 - Core Parsing
assignee: ''
agent: "claude-opus"
shell_pid: "15237"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:14Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-002, FR-003, FR-005, FR-006, FR-007, FR-008]
---

# Work Package Prompt: WP03 -- LabelParser & Parser

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP03 --base WP02
```

Depends on WP02 (Lexer must exist to produce tokens).

---

## Objectives & Success Criteria

- Implement `src/Frank.Statecharts/Smcat/LabelParser.fs` for transition label parsing (`event [guard] / action`)
- Implement `src/Frank.Statecharts/Smcat/Parser.fs` -- full recursive-descent parser producing `SmcatDocument` from tokens
- Parse state declarations (explicit, with activities and attributes), transitions (with labels), and composite states (recursive nesting)
- Provide `parseSmcat` convenience function (tokenize + parse in one call)
- Create comprehensive tests for both modules

**Done when**: `dotnet test --filter "Smcat.LabelParser or Smcat.Parser"` passes. The onboarding example from quickstart.md parses correctly to the expected AST.

## Context & Constraints

- **Spec**: FR-002 through FR-007 (AST production, label parsing, pseudo-states, composites, activities, attributes), FR-012 (comments), FR-014 (terminators)
- **Research**: R-001 (hand-written recursive-descent), R-003 (label grammar), R-004 (composite recursion), R-005 (attributes)
- **Data Model**: `data-model.md` -- SmcatDocument, SmcatElement, SmcatState, SmcatTransition, TransitionLabel structures
- **API Signatures**: `contracts/api-signatures.md` -- `parseLabel`, `parse`, `parseSmcat` signatures
- **Quickstart**: `quickstart.md` -- onboarding example with expected AST output
- **WSD Parser Pattern**: `src/Frank.Statecharts/Wsd/Parser.fs` and `src/Frank.Statecharts/Wsd/GuardParser.fs`

**Key constraints**:
- `module internal Frank.Statecharts.Smcat.LabelParser` and `module internal Frank.Statecharts.Smcat.Parser`
- Parser uses mutable `ParserState` (matching WSD pattern)
- Basic error handling (report first error and stop, or skip on error) -- comprehensive error recovery is in WP04
- Empty/whitespace-only/comment-only input produces valid empty `SmcatDocument`

## Subtasks & Detailed Guidance

### Subtask T012 -- Create `src/Frank.Statecharts/Smcat/LabelParser.fs`

**Purpose**: Parse transition label strings in the format `event [guard] / action` where each component is optional. This is a self-contained grammar analogous to `Wsd/GuardParser.fs`.

**Steps**:

1. Replace the stub with the full module:
   ```fsharp
   module internal Frank.Statecharts.Smcat.LabelParser

   open Frank.Statecharts.Smcat.Types
   ```

2. Implement `parseLabel`:
   ```fsharp
   let parseLabel (label: string) (position: SourcePosition) : TransitionLabel * ParseWarning list =
   ```

3. **Grammar**:
   ```
   label   ::= event? guard? action?
   event   ::= text until '[' or '/' or end
   guard   ::= '[' text_until_']' ']'
   action  ::= '/' text_to_end
   ```

4. **Parsing algorithm** (character-by-character on the label string):
   - Initialize: `eventText = None`, `guardText = None`, `actionText = None`, `warnings = []`
   - Scan forward through the label string:
     a. If `[` found: everything before it (trimmed) is the event; scan for matching `]` to get guard text
     b. If `/` found (and not inside `[...]`): everything before it (trimmed, excluding any guard already parsed) is the event; everything after is the action
     c. If end reached: everything is the event (trimmed)
   - If `[` is found but no `]`: record a `ParseWarning` for unclosed bracket, treat remaining text as guard
   - Trim whitespace from all extracted strings
   - If a component is empty after trimming, set it to `None`

5. **Edge cases to handle**:
   - Event only: `"start"` -> `{ Event = Some "start"; Guard = None; Action = None }`
   - Guard only: `"[isReady]"` -> `{ Event = None; Guard = Some "isReady"; Action = None }`
   - Action only: `"/ doSomething"` -> `{ Event = None; Guard = None; Action = Some "doSomething" }`
   - Event + guard: `"start [isReady]"` -> `{ Event = Some "start"; Guard = Some "isReady"; Action = None }`
   - Event + action: `"start / doSomething"` -> `{ Event = Some "start"; Guard = None; Action = Some "doSomething" }`
   - All three: `"start [isReady] / doSomething"` -> `{ Event = Some "start"; Guard = Some "isReady"; Action = Some "doSomething" }`
   - Empty label: `""` -> `{ Event = None; Guard = None; Action = None }`
   - Whitespace-only label: `"   "` -> `{ Event = None; Guard = None; Action = None }`

**Files**: `src/Frank.Statecharts/Smcat/LabelParser.fs` (~80-100 lines)

**Parallel?**: Yes -- independent of Parser infrastructure (T013).

---

### Subtask T013 -- Create `src/Frank.Statecharts/Smcat/Parser.fs` with parsing infrastructure

**Purpose**: Set up the parser module with state management, token consumption, and lookahead -- the foundation for all parsing logic.

**Steps**:

1. Replace the stub with the full module:
   ```fsharp
   module internal Frank.Statecharts.Smcat.Parser

   open Frank.Statecharts.Smcat.Types
   open Frank.Statecharts.Smcat.LabelParser
   open Frank.Statecharts.Smcat.Lexer
   ```

2. Define `ParserState`:
   ```fsharp
   type ParserState =
       { Tokens: Token array
         mutable Position: int
         mutable Elements: SmcatElement list
         mutable Errors: ParseFailure list
         mutable Warnings: ParseWarning list
         mutable ErrorLimitReached: bool
         MaxErrors: int }
   ```

3. Implement helper functions:
   ```fsharp
   let inline peek (state: ParserState) =
       if state.Position < state.Tokens.Length then state.Tokens[state.Position]
       else { Kind = Eof; Position = { Line = 0; Column = 0 } }

   let inline advance (state: ParserState) =
       if state.Position < state.Tokens.Length then
           state.Position <- state.Position + 1

   let inline expect (state: ParserState) (kind: TokenKind) =
       let tok = peek state
       if tok.Kind = kind then
           advance state
           Some tok
       else
           None

   let skipNewlines (state: ParserState) =
       while (peek state).Kind = Newline do
           advance state
   ```

4. Implement error recording:
   ```fsharp
   let addError (state: ParserState) (pos: SourcePosition) (desc: string) (expected: string) (found: string) (example: string) =
       if not state.ErrorLimitReached then
           state.Errors <- state.Errors @ [{ Position = pos; Description = desc; Expected = expected; Found = found; CorrectiveExample = example }]
           if state.Errors.Length >= state.MaxErrors then
               state.ErrorLimitReached <- true
   ```

5. Create `parseState` factory:
   ```fsharp
   let createState (tokens: Token list) (maxErrors: int) : ParserState =
       { Tokens = tokens |> Array.ofList
         Position = 0
         Elements = []
         Errors = []
         Warnings = []
         ErrorLimitReached = false
         MaxErrors = maxErrors }
   ```

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs` (~500-600 lines total when complete with T014-T017)

---

### Subtask T014 -- Implement state declaration parsing

**Purpose**: Parse explicit state declarations with optional activities and attributes.

**Steps**:

1. **State declaration forms**:
   - Single state: `idle;`
   - State list: `idle, running, stopped;`
   - State with activities: `doing: entry/ start exit/ stop ...;`
   - State with attributes: `on [label="Lamp on" color="#008800"];`
   - State with children (composite): `parent { child1 => child2; };` (handled in T016)

2. **Parsing logic** -- `parseStateDeclaration`:
   - Called when an identifier is seen and lookahead confirms it's NOT a transition (no `=>` following)
   - Read the state name (identifier or quoted string)
   - Check for `Colon` -> if present, parse state body (activities, nested content)
   - Check for `LeftBracket` -> parse attributes (key=value pairs until `RightBracket`)
   - Check for `LeftBrace` -> parse composite children (T016)
   - Check for `Comma` -> more states in this declaration
   - Check for `Semicolon` or `Newline` -> end of declaration
   - Create `SmcatState` record with `inferStateType` applied to the name

3. **Activity parsing** -- when `EntrySlash`, `ExitSlash`, or `Ellipsis` token follows the colon:
   - `EntrySlash` -> read text until next activity keyword or statement terminator -> `Entry = Some text`
   - `ExitSlash` -> read text until next activity keyword or statement terminator -> `Exit = Some text`
   - `Ellipsis` -> read text until next activity keyword or statement terminator -> `Do = Some text`
   - Activities can appear in any order and multiple can be present

4. **Attribute parsing** -- `parseAttributes`:
   - Called when `LeftBracket` is encountered
   - Loop: read `Identifier` (key), optional `Equals`, value (`Identifier` or `QuotedString`), until `RightBracket`
   - Return `SmcatAttribute list`
   - If `RightBracket` not found, record error and return what was parsed

5. Create `SmcatState` with:
   ```fsharp
   { Name = name
     Label = labelFromAttributes   // from [label="..."] attribute if present
     StateType = inferStateType name attributes
     Activities = if hasActivities then Some activities else None
     Attributes = attributes
     Children = None   // unless composite (T016)
     Position = startPosition }
   ```

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

---

### Subtask T015 -- Implement transition parsing

**Purpose**: Parse transitions in the format `source => target: event [guard] / action;` with optional label and attributes.

**Steps**:

1. **Transition detection**: When the parser sees an identifier and lookahead shows `TransitionArrow` (possibly after consuming some whitespace/newlines), enter transition parsing mode.

2. **Parsing logic** -- `parseTransition`:
   - Read source name (already consumed by the caller as the identifier)
   - Expect `TransitionArrow` (`=>`)
   - Read target name (identifier or quoted string)
   - Check for `Colon` -> if present, collect label tokens until `Semicolon`, `Comma`, `Newline`, or `LeftBracket` (for attributes)
   - If label tokens present, extract the label text and call `LabelParser.parseLabel`
   - Check for `LeftBracket` -> parse transition attributes (same `parseAttributes` as state declarations)
   - Expect statement terminator (`Semicolon`, `Comma`, or `Newline`)

3. **Label extraction**: After the `Colon`, collect all tokens up to the statement terminator. Reconstruct the label text from tokens, then pass to `LabelParser.parseLabel`.

   Alternative approach: After `Colon`, collect all remaining text on the logical line (until `;`, `,`, `Newline`, or `[` for attributes). This text string is the raw label that `parseLabel` processes.

   **Recommended approach**: Collect tokens between `:` and terminator. Map them back to text:
   - `Identifier s` -> s
   - `LeftBracket` -> `[`
   - `RightBracket` -> `]`
   - `ForwardSlash` -> `/`
   - other -> their textual representation
   Then pass reconstructed text to `parseLabel`.

4. Create `SmcatTransition`:
   ```fsharp
   { Source = sourceName
     Target = targetName
     Label = parsedLabel   // None if no colon
     Attributes = transitionAttributes
     Position = sourcePosition }
   ```

5. Emit `TransitionElement transition` to the elements list.

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

**Notes**: The disambiguation between state declaration and transition is the key challenge. After reading an identifier, check if the next non-whitespace token is `TransitionArrow`. If yes -> transition. If no -> state declaration.

---

### Subtask T016 -- Implement composite state parsing

**Purpose**: Parse nested state machines within `{ ... }` blocks for composite states, supporting arbitrary nesting depth.

**Steps**:

1. **When to enter composite parsing**: After reading a state name and seeing `LeftBrace`, the state is composite.

2. **Recursive parsing**:
   ```fsharp
   let rec parseDocument (state: ParserState) (depth: int) : SmcatDocument =
       if depth > 50 then
           // Add warning about excessive nesting
           state.Warnings <- state.Warnings @ [{ Position = (peek state).Position; Description = "Nesting depth exceeds 50 levels"; Suggestion = Some "Consider flattening the state hierarchy" }]
       let elements = ResizeArray<SmcatElement>()
       // Parse elements until RightBrace (if nested) or Eof (if top-level)
       while not (atEndOfDocument state depth) do
           skipNewlines state
           match (peek state).Kind with
           | Eof -> () // done
           | RightBrace when depth > 0 -> () // end of nested document
           | _ -> parseElement state depth elements
       { Elements = elements |> Seq.toList }
   ```

3. **Composite state construction**:
   - When `LeftBrace` is encountered after a state name:
     - Advance past `{`
     - Recursively call `parseDocument` with `depth + 1`
     - Expect `RightBrace` (advance past `}`)
     - Set `Children = Some nestedDocument` on the SmcatState

4. **Depth safety**: Per R-004 and SC-004, support at least 5 levels of nesting without error. Use a depth counter with a warning at 50+ levels (no hard failure).

5. **Example**:
   ```
   parent {
     child1 => child2: event;
     child2 {
       grandchild1 => grandchild2;
     };
   };
   ```
   Produces: `SmcatState { Name = "parent"; Children = Some { Elements = [transition child1->child2; SmcatState { Name = "child2"; Children = Some { Elements = [transition grandchild1->grandchild2] } }] } }`

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

**Notes**: The top-level `parseDocument` call uses `depth = 0`. Nested calls increment depth. `RightBrace` terminates a nested document but is an error at the top level.

---

### Subtask T017 -- Implement `parseSmcat` convenience function

**Purpose**: Provide a one-call API that tokenizes and parses smcat text, as specified in the API signatures.

**Steps**:

1. Implement the public API functions:
   ```fsharp
   let parse (tokens: Token list) (maxErrors: int) : ParseResult =
       let state = createState tokens maxErrors
       let doc = parseDocument state 0
       { Document = doc
         Errors = state.Errors
         Warnings = state.Warnings }

   let parseSmcat (source: string) : ParseResult =
       let tokens = Lexer.tokenize source
       parse tokens 50
   ```

2. **Empty input handling**: If `tokens` is `[{ Kind = Eof; ... }]` (only EOF), return:
   ```fsharp
   { Document = { Elements = [] }; Errors = []; Warnings = [] }
   ```

3. **Comment-only input**: After lexer strips comments, the token list may contain only `Newline` and `Eof` tokens. The parser should skip newlines and produce an empty document.

**Files**: `src/Frank.Statecharts/Smcat/Parser.fs`

---

### Subtask T018 -- Create `test/Frank.Statecharts.Tests/Smcat/LabelParserTests.fs`

**Purpose**: Comprehensive tests for the LabelParser covering all combinations of event/guard/action presence.

**Steps**:

1. Replace the stub with test cases:

2. **Test cases** (from R-003 edge cases):
   - Event only: `"start"` -> Event = Some "start", Guard = None, Action = None
   - Guard only: `"[isReady]"` -> Event = None, Guard = Some "isReady", Action = None
   - Action only: `"/ doSomething"` -> Event = None, Guard = None, Action = Some "doSomething"
   - Event + guard: `"start [isReady]"` -> Event = Some "start", Guard = Some "isReady"
   - Event + action: `"start / doSomething"` -> Event = Some "start", Action = Some "doSomething"
   - All three: `"start [isReady] / doSomething"` -> all Some
   - Empty label: `""` -> all None
   - Whitespace-only: `"   "` -> all None
   - Guard with spaces: `"event [has spaces inside]"` -> Guard = Some "has spaces inside"
   - Action with spaces: `"event / do the thing"` -> Action = Some "do the thing"
   - Unclosed bracket: `"event [guard"` -> Guard = Some "guard", with ParseWarning
   - Only colon (parsed by Parser before reaching LabelParser): label text would be empty

3. Use Expecto pattern:
   ```fsharp
   module Smcat.LabelParserTests

   open Expecto
   open Frank.Statecharts.Smcat.Types
   open Frank.Statecharts.Smcat.LabelParser

   [<Tests>]
   let labelTests = testList "Smcat.LabelParser" [ ... ]
   ```

**Files**: `test/Frank.Statecharts.Tests/Smcat/LabelParserTests.fs` (~120-150 lines)

**Parallel?**: Yes -- can be written alongside Parser implementation.

---

### Subtask T019 -- Create `test/Frank.Statecharts.Tests/Smcat/ParserTests.fs`

**Purpose**: Full integration tests for the parser covering state declarations, transitions, composites, and pseudo-states.

**Steps**:

1. Replace the stub with test cases:

2. **Test the onboarding example from quickstart.md**:
   ```
   # Simple onboarding state machine
   initial => home: start;
   home => WIP: begin;
   WIP => customerData: collectCustomerData [isValid] / logAction;
   customerData => final: complete;
   ```
   Expected: 4 TransitionElement nodes with correct source/target/label/stateType values.

3. **Test state declarations**:
   - `"idle, running, stopped;"` -> 3 StateDeclaration nodes
   - `"idle;"` -> 1 StateDeclaration with StateType.Regular
   - `"initial;"` -> 1 StateDeclaration with StateType.Initial

4. **Test transitions**:
   - `"a => b;"` -> TransitionElement with no label
   - `"a => b: event;"` -> TransitionElement with Event = Some "event"
   - `"a => b: event [guard] / action;"` -> full label

5. **Test pseudo-states**:
   - Parse `"initial => home: start;"` -> source state type is Initial
   - Parse `"home => final;"` -> target state type is Final
   - Parse `"^choice => a;"` -> source state type is Choice
   - Parse `"deep.history => a;"` -> source state type is DeepHistory

6. **Test composite states**:
   - ```
     parent {
       child1 => child2;
     };
     ```
     -> StateDeclaration with Children = Some { Elements = [one transition] }
   - Nested composite (2 levels deep)

7. **Test state activities**:
   - `"active: entry/ start exit/ stop ...;"` -> StateDeclaration with Activities

8. **Test attributes**:
   - `"on [label=\"Lamp on\" color=\"#008800\"];"` -> StateDeclaration with 2 attributes

9. **Test edge cases**:
   - Empty input `""` -> empty document, no errors
   - Comment-only `"# just a comment"` -> empty document
   - Whitespace-only `"   \n  "` -> empty document
   - Comma as terminator: `"a => b, c => d;"` (two transitions)

10. Use Expecto pattern:
    ```fsharp
    module Smcat.ParserTests

    open Expecto
    open Frank.Statecharts.Smcat.Types
    open Frank.Statecharts.Smcat.Parser

    [<Tests>]
    let parserTests = testList "Smcat.Parser" [ ... ]
    ```

**Files**: `test/Frank.Statecharts.Tests/Smcat/ParserTests.fs` (~250-300 lines)

**Parallel?**: Yes -- can be written alongside Parser implementation if the ParseResult structure is stable.

---

## Test Strategy

Run label parser tests:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat.LabelParser"
```

Run parser tests:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat.Parser"
```

Run all smcat tests:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat"
```

Verify no regressions:
```bash
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
```

## Risks & Mitigations

- **State vs transition disambiguation**: Key challenge. After reading an identifier, lookahead for `TransitionArrow`. If found -> transition. Otherwise -> state declaration. Must handle newlines between identifier and arrow carefully.
- **Label reconstruction from tokens**: Converting tokens back to text for `parseLabel` input may lose original whitespace. Alternative: have the parser extract label text directly from the source string using token positions. Choose the simpler approach that works correctly.
- **Recursive nesting performance**: For 5+ levels (SC-004), the recursive approach is fine. The 50-level warning prevents stack overflow in pathological cases.
- **Mutually recursive parse functions**: `parseDocument` calls `parseElement` which may call `parseStateDeclaration` which may call `parseDocument` (for composites). Use `let rec ... and ...` if needed, or define functions in dependency order.

## Review Guidance

- Verify the onboarding example from quickstart.md parses to the exact expected AST
- Verify all 6 label combinations (event/guard/action presence) produce correct TransitionLabel
- Verify composite states parse recursively to at least 2 levels
- Verify pseudo-state inference: initial, final, history, deep.history, choice (^), forkjoin (])
- Verify empty/whitespace/comment-only input produces valid empty document
- Verify both `;` and `,` are accepted as statement terminators
- Check that `dotnet test --filter "Smcat.LabelParser or Smcat.Parser"` passes

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:14Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:
1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP03 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-16T04:31:54Z – claude-opus – shell_pid=15237 – lane=doing – Assigned agent via workflow command
