---
work_package_id: "WP04"
title: "StatechartDocumentJson & ValidationReportFormatter"
lane: "planned"
dependencies: ["WP01"]
subtasks:
  - "T020"
  - "T021"
  - "T022"
  - "T023"
  - "T024"
  - "T025"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
history:
  - timestamp: "2026-03-16T19:12:54Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP04 -- StatechartDocumentJson & ValidationReportFormatter

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

Depends on WP01:

```bash
spec-kitty implement WP04 --base WP01
```

---

## Objectives & Success Criteria

1. Create `StatechartDocumentJson` module that serializes `StatechartDocument` (and `ParseResult` with errors/warnings) to JSON.
2. Create `ValidationReportFormatter` module that formats `ValidationReport` as both text and JSON.
3. Both modules follow existing output conventions (`Utf8JsonWriter` for JSON, `StringBuilder` with color helpers for text).
4. Both modules compile cleanly with `dotnet build`.

**Success**: `StatechartDocumentJson.serialize` produces valid JSON from a `StatechartDocument`. `ValidationReportFormatter.formatText/formatJson` produce correct output from a `ValidationReport`.

---

## Context & Constraints

- **Spec**: `kitty-specs/026-cli-statechart-commands/spec.md` (FR-023, FR-024, FR-032, FR-033, FR-034)
- **Plan**: `kitty-specs/026-cli-statechart-commands/plan.md` -- ValidationReportFormatter entity description
- **Existing patterns**:
  - `src/Frank.Cli.Core/Output/JsonOutput.fs` -- `Utf8JsonWriter` pattern for JSON output
  - `src/Frank.Cli.Core/Output/TextOutput.fs` -- `StringBuilder` with color helpers for text output, `NO_COLOR` env var
- **Key types**:
  - `StatechartDocument` in `src/Frank.Statecharts/Ast/Types.fs` -- root AST node with states, transitions, title, etc.
  - `ParseResult` in `src/Frank.Statecharts/Ast/Types.fs` -- contains `Document`, `Errors`, `Warnings`
  - `ValidationReport` in `src/Frank.Statecharts/Validation/Types.fs` -- contains `TotalChecks`, `TotalFailures`, `Checks`, `Failures`
  - `ValidationCheck`, `ValidationFailure`, `CheckStatus` in `src/Frank.Statecharts/Validation/Types.fs`

---

## Subtasks & Detailed Guidance

### Subtask T020 -- Create StatechartDocumentJson.fs

- **Purpose**: Serialize `StatechartDocument` and `ParseResult` to JSON for the import and extract commands.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Statechart/StatechartDocumentJson.fs`
  2. Module declaration: `module Frank.Cli.Core.Statechart.StatechartDocumentJson`
  3. Open: `Frank.Statecharts.Ast`, `System.IO`, `System.Text`, `System.Text.Json`

  4. Implement `serializeDocument (doc: StatechartDocument) : string`:
     ```json
     {
       "title": "<title or null>",
       "initialStateId": "<id or null>",
       "states": [
         {
           "identifier": "<name>",
           "kind": "<Regular|Initial|Final|...>",
           "label": "<label or null>"
         }
       ],
       "transitions": [
         {
           "source": "<source>",
           "target": "<target or null>",
           "event": "<event or null>",
           "guard": "<guard or null>",
           "action": "<action or null>"
         }
       ]
     }
     ```

  5. Implement `serializeParseResult (result: ParseResult) : string`:
     ```json
     {
       "document": { ... },
       "errors": [
         {
           "line": 5,
           "column": 12,
           "description": "...",
           "expected": "...",
           "found": "..."
         }
       ],
       "warnings": [
         {
           "line": 3,
           "column": 1,
           "description": "...",
           "suggestion": "..."
         }
       ]
     }
     ```

  6. Use `Utf8JsonWriter` with `JsonWriterOptions(Indented = true)` following `JsonOutput.fs` conventions.
  7. Extract states and transitions from `doc.Elements` using pattern matching on `StateDecl` and `TransitionElement`.

- **Files**: `src/Frank.Cli.Core/Statechart/StatechartDocumentJson.fs` (NEW, ~120-180 lines)
- **Parallel?**: Yes -- can proceed alongside T021.
- **Notes**: Handle `option` types by writing `null` for `None`. Include all `StateKind` values as strings. Position info is optional (write only if present).

### Subtask T021 -- Create ValidationReportFormatter.fs

- **Purpose**: Format `ValidationReport` for display as text or JSON (FR-023, FR-024).
- **Steps**:
  1. Create `src/Frank.Cli.Core/Statechart/ValidationReportFormatter.fs`
  2. Module declaration: `module Frank.Cli.Core.Statechart.ValidationReportFormatter`
  3. Open: `Frank.Statecharts.Validation`, `System.IO`, `System.Text`, `System.Text.Json`

  4. Implement `formatText (report: ValidationReport) : string`:
     - Use `StringBuilder` with color helpers (copy pattern from `TextOutput.fs`)
     - Header: `"Validation Report"` (bold)
     - Summary line: `"Checks: {total} | Failures: {failures} | Skipped: {skipped}"`
     - Status: green "PASSED" if `TotalFailures = 0`, red "FAILED" if > 0
     - Passed checks: list each with green checkmark or "PASS" prefix
     - Failed checks: list each with red "FAIL" prefix, include `Reason`
     - Skipped checks: list each with yellow "SKIP" prefix, include `Reason`
     - Failure details section: for each `ValidationFailure`, show:
       - Description (bold)
       - Formats involved
       - Expected vs Actual
       - Entity type

  5. Implement `formatJson (report: ValidationReport) : string`:
     ```json
     {
       "status": "passed|failed",
       "totalChecks": 10,
       "totalFailures": 2,
       "totalSkipped": 1,
       "checks": [
         {
           "name": "...",
           "status": "pass|fail|skip",
           "reason": "..."
         }
       ],
       "failures": [
         {
           "formats": ["Wsd", "Alps"],
           "entityType": "state name",
           "expected": "...",
           "actual": "...",
           "description": "..."
         }
       ]
     }
     ```

  6. Color helpers: define private `isColorEnabled`, `bold`, `green`, `red`, `yellow` following `TextOutput.fs` pattern exactly. Or import from `TextOutput` if they are accessible.
     - `isColorEnabled()`: checks `NO_COLOR` env var and `Console.IsOutputRedirected`
     - The functions are `private` in `TextOutput`, so you'll need to define local copies.

- **Files**: `src/Frank.Cli.Core/Statechart/ValidationReportFormatter.fs` (NEW, ~150-200 lines)
- **Parallel?**: Yes -- can proceed alongside T020.
- **Notes**: Respect `NO_COLOR` environment variable (FR-034). Follow JSON conventions from `JsonOutput.fs`. Map `CheckStatus` DU cases to lowercase strings: `Pass -> "pass"`, `Fail -> "fail"`, `Skip -> "skip"`.

### Subtask T022 -- Implement StatechartDocument JSON serialization

- **Purpose**: Covered by T020. This subtask tracks the implementation detail of extracting states and transitions from the element list.
- **Steps**:
  1. Traverse `doc.Elements` to collect `StateNode` and `TransitionEdge` values:
     ```fsharp
     let states =
         doc.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)
     let transitions =
         doc.Elements |> List.choose (function TransitionElement t -> Some t | _ -> None)
     ```
  2. Consider using `Validation.AstHelpers.allStates` and `allTransitions` for recursive extraction (includes nested states and group elements).
  3. Serialize `StateKind` as string representation (e.g., `"Regular"`, `"Final"`, `"Initial"`).
  4. Serialize `Annotation` list if non-empty (optional -- may skip for simplicity, since import consumers primarily need states/transitions).

- **Files**: `src/Frank.Cli.Core/Statechart/StatechartDocumentJson.fs`

### Subtask T023 -- Implement ValidationReport text/JSON formatting

- **Purpose**: Covered by T021. This subtask tracks formatting of `FormatTag` values and `ValidationFailure` details.
- **Steps**:
  1. Format `FormatTag` as string: use `sprintf "%A"` or explicit match (`Wsd -> "WSD"`, etc.)
  2. Group checks by status for text output: passed first, then failed, then skipped
  3. Ensure failure details section is only shown when `TotalFailures > 0`

- **Files**: `src/Frank.Cli.Core/Statechart/ValidationReportFormatter.fs`

### Subtask T024 -- Add compile entries to Frank.Cli.Core.fsproj

- **Purpose**: Register the new source files in the project compile order.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
  2. Add compile entries after the `FormatPipeline.fs` entry (or after `StatechartExtractor.fs` if WP03 is not yet merged):
     ```xml
     <Compile Include="Statechart/StatechartDocumentJson.fs" />
     <Compile Include="Statechart/ValidationReportFormatter.fs" />
     ```
  3. Both must come before command files that depend on them.

- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
- **Notes**: If WP03 is being merged in the same batch, ensure these entries come after FormatDetector and FormatPipeline.

### Subtask T025 -- Verify modules compile

- **Purpose**: Confirm all changes compile cleanly.
- **Steps**:
  1. Run `dotnet build` from the repository root
  2. Fix any compilation errors
  3. Verify `StatechartDocumentJson` can access `StatechartDocument` and `ParseResult` types
  4. Verify `ValidationReportFormatter` can access `ValidationReport`, `ValidationCheck`, `ValidationFailure` types
- **Files**: N/A

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Large AST serialization (many states) | Use `Utf8JsonWriter` (streaming) not `JsonSerializer` (string-based). |
| Color helper duplication | Accept duplication for now. `TextOutput` helpers are `private`. |
| `StateKind` DU may change | Use `sprintf "%A"` for forward-compatible serialization. |

---

## Review Guidance

- Verify `StatechartDocumentJson` produces valid JSON for edge cases: empty document, document with only states, document with only transitions.
- Verify `ValidationReportFormatter.formatText` respects `NO_COLOR` environment variable.
- Verify `ValidationReportFormatter.formatJson` produces valid JSON with correct field names.
- Verify both modules follow existing output conventions from `JsonOutput.fs` and `TextOutput.fs`.
- Verify `dotnet build` passes cleanly.

---

## Activity Log

- 2026-03-16T19:12:54Z -- system -- lane=planned -- Prompt created.
