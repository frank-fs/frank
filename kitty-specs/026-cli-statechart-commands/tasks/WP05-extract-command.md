---
work_package_id: WP05
title: Extract Command
lane: "for_review"
dependencies: [WP02]
base_branch: 026-cli-statechart-commands-WP02
base_commit: 30a746dce8d843a01d8834b2b48bd8975dc4d1bc
created_at: '2026-03-18T02:39:20.616860+00:00'
subtasks:
- T026
- T027
- T028
- T029
- T030
- T031
assignee: ''
agent: "claude-opus"
shell_pid: "8834"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-16T19:12:54Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
---

# Work Package Prompt: WP05 -- Extract Command

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

Depends on WP02:

```bash
spec-kitty implement WP05 --base WP02
```

---

## Objectives & Success Criteria

1. Create `StatechartExtractCommand` module implementing the `frank statechart extract <assembly>` command logic.
2. Support `--output-format text|json` output option (default: text).
3. Produce structured output: state names, initial state, guard names, per-state metadata (methods, final flag, description).
4. Handle zero-resources gracefully (message, exit code 0).
5. Handle assembly load errors (non-zero exit, clear error message).

**Success**: The `execute` function takes an assembly path and format string, returns `Result<ExtractResult, string>` with all required metadata.

---

## Context & Constraints

- **Spec**: `kitty-specs/026-cli-statechart-commands/spec.md` -- User Story 1 (FR-007, FR-008, FR-009)
- **Plan**: `kitty-specs/026-cli-statechart-commands/plan.md` -- Extract Command section
- **Dependencies**:
  - `StatechartExtractor.extract` from WP02 -- loads assembly and returns `Result<ExtractedStatechart list, string>`
  - `ExtractedStatechart` type from WP02
- **Output conventions**: Follow `JsonOutput.fs` and `TextOutput.fs` patterns from existing frank-cli commands.

---

## Subtasks & Detailed Guidance

### Subtask T026 -- Create StatechartExtractCommand.fs

- **Purpose**: Implement the extract command logic as a module callable from `Program.fs`.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Commands/StatechartExtractCommand.fs`
  2. Module declaration: `module Frank.Cli.Core.Commands.StatechartExtractCommand`
  3. Open `Frank.Cli.Core.Statechart` for `StatechartExtractor` and `ExtractedStatechart`
  4. Define result type:
     ```fsharp
     type ExtractResult =
         { StateMachines: ExtractedStatechart list }
     ```
  5. Implement `execute (assemblyPath: string) : Result<ExtractResult, string>`:
     ```fsharp
     let execute (assemblyPath: string) : Result<ExtractResult, string> =
         StatechartExtractor.extract assemblyPath
         |> Result.map (fun machines -> { StateMachines = machines })
     ```

- **Files**: `src/Frank.Cli.Core/Commands/StatechartExtractCommand.fs` (NEW, ~30-50 lines)
- **Notes**: The command module is thin -- it calls the extractor and wraps the result. Output formatting is done by separate formatter functions.

### Subtask T027 -- Implement text output for extract

- **Purpose**: Format extract results as human-readable text (FR-008 with format=text).
- **Steps**:
  1. Add to `src/Frank.Cli.Core/Output/TextOutput.fs`:
     ```fsharp
     let formatStatechartExtractResult (result: StatechartExtractCommand.ExtractResult) : string =
     ```
  2. Format each `ExtractedStatechart` as a section:
     ```
     Statechart: /games/{id}
       Initial State: WaitingForPlayers
       States: WaitingForPlayers, InProgress, GameOver
       Guards: TurnGuard, PlayerCountGuard
       State Details:
         WaitingForPlayers:
           Methods: GET, POST
           Final: false
         InProgress:
           Methods: GET, PUT, DELETE
           Final: false
         GameOver:
           Methods: GET
           Final: true
     ```
  3. Handle zero-resources: return `"No state machines found in the assembly."`
  4. Use `bold` for headers, standard text for details.

- **Files**: `src/Frank.Cli.Core/Output/TextOutput.fs`
- **Notes**: Follow existing pattern in `formatExtractResult`. Use `StringBuilder`.

### Subtask T028 -- Implement JSON output for extract

- **Purpose**: Format extract results as structured JSON (FR-008 with format=json, FR-009).
- **Steps**:
  1. Add to `src/Frank.Cli.Core/Output/JsonOutput.fs`:
     ```fsharp
     let formatStatechartExtractResult (result: StatechartExtractCommand.ExtractResult) : string =
     ```
  2. JSON structure per FR-009:
     ```json
     {
       "status": "ok",
       "stateMachines": [
         {
           "routeTemplate": "/games/{id}",
           "initialState": "WaitingForPlayers",
           "states": ["WaitingForPlayers", "InProgress", "GameOver"],
           "guards": ["TurnGuard", "PlayerCountGuard"],
           "stateMetadata": {
             "WaitingForPlayers": {
               "allowedMethods": ["GET", "POST"],
               "isFinal": false,
               "description": null
             }
           }
         }
       ]
     }
     ```
  3. Use `Utf8JsonWriter` following existing `JsonOutput.fs` patterns.
  4. Handle zero-resources: write `"stateMachines": []` (empty array).

- **Files**: `src/Frank.Cli.Core/Output/JsonOutput.fs`
- **Notes**: `StateInfo` fields: `AllowedMethods: string list`, `IsFinal: bool`, `Description: string option`.

### Subtask T029 -- Handle zero-stateful-resources case

- **Purpose**: When the assembly contains no stateful resources, produce a clear message without error (FR-004).
- **Steps**:
  1. In text output: `"No state machines found in the assembly."`
  2. In JSON output: `{ "status": "ok", "stateMachines": [] }`
  3. Exit code: 0 (not an error).
  4. This is handled in the output formatters (T027, T028) -- the `ExtractResult.StateMachines` list will be empty.

- **Files**: `src/Frank.Cli.Core/Output/TextOutput.fs`, `src/Frank.Cli.Core/Output/JsonOutput.fs`

### Subtask T030 -- Handle assembly load errors

- **Purpose**: When the assembly cannot be loaded, produce a clear error (FR-005).
- **Steps**:
  1. The `StatechartExtractor.extract` function returns `Error msg` for load failures.
  2. In `Program.fs` (WP09), this will be handled by:
     ```fsharp
     | Error e ->
         Environment.ExitCode <- 1
         Console.Error.WriteLine(if format = "json" then JsonOutput.formatError e else TextOutput.formatError e)
     ```
  3. Ensure the error messages from WP02's extractor are descriptive enough for end users.

- **Files**: No changes needed here -- handled by extractor (WP02) and CLI wiring (WP09).

### Subtask T031 -- Add compile entry to Frank.Cli.Core.fsproj

- **Purpose**: Register the new command module in the project compile order.
- **Steps**:
  1. Add to `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`:
     ```xml
     <Compile Include="Commands/StatechartExtractCommand.fs" />
     ```
  2. Place after `Commands/StatusCommand.fs` and before `Output/JsonOutput.fs` to maintain correct compile order (command modules must be compiled before output formatters that reference their result types).

  **IMPORTANT**: Actually, since `JsonOutput.fs` and `TextOutput.fs` reference `StatechartExtractCommand.ExtractResult`, the command module must be compiled BEFORE the output modules. Check the current fsproj compile order:
  - Current order has Commands before Output
  - So add `StatechartExtractCommand.fs` after existing command entries (e.g., after `Commands/StatusCommand.fs`)

- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Output format inconsistency with existing commands | Follow exact patterns from `JsonOutput.fs` and `TextOutput.fs`. |
| Missing `StateInfo` data | `StateMetadataMap` may be empty for states not explicitly configured. Handle with defaults. |

---

## Review Guidance

- Verify text output is readable and includes all metadata fields.
- Verify JSON output matches the FR-009 schema exactly.
- Verify zero-resources case produces correct output (not an error).
- Verify command module is thin (delegates to extractor, doesn't duplicate logic).
- Verify `dotnet build` passes cleanly.

---

## Activity Log

- 2026-03-16T19:12:54Z -- system -- lane=planned -- Prompt created.
- 2026-03-18T02:39:20Z – claude-opus – shell_pid=8834 – lane=doing – Assigned agent via workflow command
- 2026-03-18T02:45:22Z – claude-opus – shell_pid=8834 – lane=for_review – Ready for review: StatechartExtractCommand with text/JSON output formatters. Build passes clean.
