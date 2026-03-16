---
work_package_id: WP03
title: Help Subcommand Logic
lane: planned
dependencies:
- WP01
subtasks:
- T013
- T014
- T015
- T016
- T017
phase: Phase 2 - Content and Logic
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:04Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-011
- FR-012
- FR-013
- FR-014
- FR-015
- FR-016
- FR-017
---

# Work Package Prompt: WP03 -- Help Subcommand Logic

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

This WP depends on WP01 and WP02:

```bash
spec-kitty implement WP03 --base WP02
```

---

## Objectives & Success Criteria

1. `HelpSubcommand.resolve` correctly returns `CommandMatch` for valid command names.
2. `HelpSubcommand.resolve` correctly returns `TopicMatch` for valid topic names.
3. `HelpSubcommand.resolve` returns `NoMatch` with fuzzy suggestions for unknown arguments.
4. Commands take priority over topics when a name matches both (edge case from spec).
5. `HelpSubcommand.listAll` returns all commands and topics for the no-argument index case.
6. `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` succeeds.

## Context & Constraints

- **Spec**: `kitty-specs/016-frank-cli-help-system/spec.md` (FR-011 through FR-017)
- **Data Model**: `kitty-specs/016-frank-cli-help-system/data-model.md` (HelpLookupResult type)
- **Contracts**: `kitty-specs/016-frank-cli-help-system/contracts/cli-outputs.md` (help subcommand output formats)
- **Research**: `kitty-specs/016-frank-cli-help-system/research.md` (R4: fuzzy matching approach)
- **Edge Case**: From spec -- if a name matches both a command and a topic, commands take priority.

## Subtasks & Detailed Guidance

### Subtask T013 -- Create HelpSubcommand.fs

**Purpose**: Create the module that resolves help arguments to their corresponding content (command help, topic, or "did you mean?" suggestions).

**Steps**:

1. Create `src/Frank.Cli.Core/Help/HelpSubcommand.fs`.

2. Use namespace `Frank.Cli.Core.Help`.

3. Open `Frank.Cli.Core.Help` (for types and HelpContent).

4. Module structure:

```fsharp
module HelpSubcommand =

    /// Maximum Levenshtein distance for fuzzy suggestions.
    let private maxSuggestionDistance = 3

    // resolve, listAll functions defined in subsequent subtasks
```

**Files**: `src/Frank.Cli.Core/Help/HelpSubcommand.fs` (new, ~60 lines total)
**Parallel?**: No -- T014-T016 add to this file sequentially.

---

### Subtask T014 -- Implement Resolve Function

**Purpose**: Given a string argument from `frank-cli help <arg>`, determine whether it matches a command, a topic, or nothing (with suggestions).

**Steps**:

1. Implement the `resolve` function in HelpSubcommand:

```fsharp
    /// Resolve a help argument to a command, topic, or suggestion list.
    let resolve (argument: string) : HelpLookupResult =
        // 1. Check commands first (commands take priority per spec edge case)
        match HelpContent.findCommand argument with
        | Some cmd -> CommandMatch cmd
        | None ->
            // 2. Check topics
            match HelpContent.findTopic argument with
            | Some topic -> TopicMatch topic
            | None ->
                // 3. No match -- provide fuzzy suggestions
                let suggestions =
                    FuzzyMatch.suggest argument HelpContent.allNames maxSuggestionDistance
                    |> List.map fst  // Extract just the names, drop distances
                NoMatch suggestions
```

2. The priority order is: exact command match > exact topic match > fuzzy suggestions.

3. Matching is case-insensitive (handled by `HelpContent.findCommand` / `findTopic`).

**Files**: `src/Frank.Cli.Core/Help/HelpSubcommand.fs` (extend)
**Parallel?**: No -- must follow T013.

**Edge Cases**:
- `"extract"` -> `CommandMatch extractHelp`
- `"workflows"` -> `TopicMatch workflowsTopic`
- `"comiple"` -> `NoMatch ["compile"]` (fuzzy match)
- `"xyz"` -> `NoMatch []` (no suggestions within threshold)
- If a topic were named the same as a command, command wins.

---

### Subtask T015 -- Implement "Did You Mean?" Suggestion Logic

**Purpose**: Connect FuzzyMatch to HelpSubcommand for producing helpful suggestions on unknown input.

**Steps**:

1. The suggestion logic is already integrated in T014 via `FuzzyMatch.suggest`. This subtask ensures the threshold is appropriate:
   - `maxSuggestionDistance = 3` means up to 3 edits are tolerated.
   - Prefix matching is also included (e.g., "ext" matches "extract").

2. Test the threshold mentally with common typos:
   - "comiple" -> "compile" (distance 2: transposition + missing letter) -- within threshold
   - "extact" -> "extract" (distance 1: missing 'r') -- within threshold
   - "helpme" -> "help" (distance 2: extra 'me') -- within threshold
   - "validate" -> "validate" (distance 0: exact) -- direct match, not fuzzy
   - "zzzzzz" -> nothing within distance 3 -- empty suggestions

3. If the threshold seems too broad or narrow for the 9-candidate set, adjust `maxSuggestionDistance`.

**Files**: `src/Frank.Cli.Core/Help/HelpSubcommand.fs` (verify/adjust)
**Parallel?**: No -- depends on T014.

---

### Subtask T016 -- Implement listAll Function

**Purpose**: Provide the data for the `frank-cli help` (no arguments) index view that lists all commands and topics.

**Steps**:

1. Add to HelpSubcommand:

```fsharp
    /// Result type for the no-argument help index.
    type HelpIndex =
        { Commands: (string * string) list  // (name, summary) pairs
          Topics: (string * string) list }   // (name, summary) pairs

    /// List all commands and topics for the help index display.
    let listAll () : HelpIndex =
        { Commands =
            HelpContent.allCommands
            |> List.map (fun c -> (c.Name, c.Summary))
          Topics =
            HelpContent.allTopics
            |> List.map (fun t -> (t.Name, t.Summary)) }
```

2. This function is called when `frank-cli help` is invoked with no argument.

**Files**: `src/Frank.Cli.Core/Help/HelpSubcommand.fs` (extend, ~15 lines)
**Parallel?**: No -- must follow T013.

---

### Subtask T017 -- Update Frank.Cli.Core.fsproj

**Purpose**: Add HelpSubcommand.fs to the compile list.

**Steps**:

1. Add to `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`:

```xml
<!-- After Help/HelpContent.fs -->
<Compile Include="Help/HelpSubcommand.fs" />
```

2. The Help compile order after WP03:

```xml
<Compile Include="Help/HelpTypes.fs" />
<Compile Include="Help/FuzzyMatch.fs" />
<Compile Include="Help/HelpContent.fs" />
<Compile Include="Help/HelpSubcommand.fs" />
```

3. Run `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` to verify.

**Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (modify)
**Parallel?**: No -- must be done after T013-T016 create the file.

**Validation**:
- [ ] `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` succeeds
- [ ] `resolve "extract"` returns `CommandMatch`
- [ ] `resolve "workflows"` returns `TopicMatch`
- [ ] `resolve "comiple"` returns `NoMatch` with "compile" in suggestions
- [ ] `listAll()` returns all 7 commands and 2 topics

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Fuzzy threshold too broad (spurious suggestions) | With only 9 candidates, threshold 3 is conservative; test in WP06 |
| Missing edge case: name matches both command and topic | Commands always checked first; topic check only if command not found |

## Review Guidance

- Verify the resolve function checks commands before topics.
- Verify fuzzy suggestions use the FuzzyMatch module (not reimplemented).
- Verify listAll returns all 7 commands and 2 topics.
- Verify case-insensitive matching works (e.g., "Extract" resolves to "extract").
- Run `dotnet build` to confirm compilation.

## Activity Log

- 2026-03-15T23:59:04Z -- system -- lane=planned -- Prompt created.
