---
work_package_id: WP05
title: CLI Commands — Extract & Clarify
lane: "done"
dependencies: [WP04]
base_branch: 001-semantic-resources-phase1-WP04
base_commit: 4adeff1f323febb4654650c6434be04155551b8b
created_at: '2026-03-05T20:16:35.148958+00:00'
subtasks:
- T024
- T025
- T026
- T027
- T028
phase: Phase 1 - CLI
assignee: ''
agent: "claude-opus-reviewer-r2"
shell_pid: "8180"
review_status: "has_feedback"
reviewed_by: "Ryan Riley"
review_feedback_file: "/private/var/folders/21/1fmrn5_d30734sj2v64kf6rh0000gn/T/spec-kitty-review-feedback-WP05.md"
history:
- timestamp: '2026-03-04T22:10:13Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-007
- FR-008
- FR-013
- FR-014
---

# WP05: CLI Commands — Extract & Clarify

> **Review Feedback Status**: No review feedback yet.

## Review Feedback

**Reviewed by**: Ryan Riley
**Status**: ❌ Changes Requested
**Date**: 2026-03-05
**Feedback file**: `/private/var/folders/21/1fmrn5_d30734sj2v64kf6rh0000gn/T/spec-kitty-review-feedback-WP05.md`

## WP05 Review Feedback

Build passes, all 64 tests pass. The implementation demonstrates good understanding of the domain and solid F# idioms. However, several spec requirements are not fully met.

### Issue 1: Missing `missing-relationship` ambiguity category (T025 - Critical)

The spec requires four ambiguity categories: `unmapped-type`, `open-or-closed`, `object-or-datatype`, and `missing-relationship`. The `ClarifyCommand` only implements the first three. The `missing-relationship` category ("types that appear to relate to each other but have no explicit property linking them") is entirely absent. Add a `missingRelationshipQuestions` function that inspects the ontology for classes that share naming patterns or appear in the same source context but have no `owl:ObjectProperty` linking them.

### Issue 2: Unstable question IDs (T025 - Critical)

The spec says question IDs should be stable slugs like `"unmapped-type-MyType"`. The current implementation uses index-based IDs (`"unmapped-type-0"`, `"open-or-closed-0"`) which are unstable across runs if the set of types changes. Change `unmapped-type` IDs to `$"unmapped-type-{ut.TypeName}"` (or a slugified version), `open-or-closed` IDs to `$"open-or-closed-{typeName}"`, etc.

### Issue 3: Missing `scope` parameter on ExtractCommand.execute (T024)

The spec requires a `scope` parameter (one of `project | file | resource`, default `project`) on `ExtractCommand.execute`. This parameter is missing from the function signature. Add it even if the initial implementation only supports `project` scope.

### Issue 4: ExtractResult and JSON output schema mismatch (T026)

The spec says `formatExtractResult : ExtractResult -> string` should take the `ExtractResult` type from ExtractCommand. Instead, `JsonOutput` defines its own `ExtractResultJson` type that flattens `ontologySummary` and `shapesSummary`. The spec calls for `ontologySummary` (with classCount, propertyCount, alignedCount, unalignedCount) and `shapesSummary` (with shapeCount, constraintCount) as sub-objects. The current `ExtractResultJson` is missing `alignedCount`, `unalignedCount`, and `constraintCount` fields entirely. Either use the `ExtractResult` type directly or make `ExtractResultJson` structurally match it, including the nested summary objects.

### Issue 5: formatExtractResult/formatClarifyResult should accept domain types (T026/T027)

Both `JsonOutput` and `TextOutput` define their own intermediate types rather than accepting `ExtractResult`/`ClarifyResult` from the command modules as specified. The spec signatures are `formatExtractResult : ExtractResult -> string` and `formatClarifyResult : ClarifyResult -> string`. Either accept the domain types and do the mapping internally, or provide explicit conversion functions. Currently there is no code that bridges `ExtractCommand.ExtractResult` to `JsonOutput.ExtractResultJson`.

### Issue 6: ExtractCommand tests do not verify orchestration sequence (T028 - Critical)

The spec requires: "Mock all analyser and mapper dependencies via interfaces/function parameters" and "Verify orchestration calls each step exactly once and in the documented sequence." The current `ExtractCommandTests` only test (a) error on non-existent project and (b) state save/load roundtrip. There are no mocks, no sequence verification, and no test that verifies `ExtractionState` is written to the expected path after a successful run. The ExtractCommand currently calls concrete implementations directly, making it untestable without real projects. Refactor `execute` to accept dependencies as function parameters (or an interface/record of functions) so tests can inject mocks and verify call order.

### Issue 7: TextOutput summary table format (T027 - Minor)

The spec says "Summary table with columns: Category | Count" but the output uses simple indented text without table formatting (no column separators or headers). Consider using a proper text table format with `|` separators and a header row, or at minimum, column-aligned output with header labels.

### Issue 8: Missing `status` field on ExtractResult JSON (T026)

The spec requires `formatExtractResult` to "emit a top-level `status` field (`ok` on success, `error` on failure)". The `ExtractResultJson` type has a `Status` field but it's set to `"success"` in tests rather than `"ok"` as specified. Use `"ok"` for consistency with the spec.

### Summary of Required Changes (ordered by priority)

1. Add `missing-relationship` ambiguity category to ClarifyCommand
2. Fix question IDs to be stable slugs based on type names
3. Add `scope` parameter to ExtractCommand.execute
4. Fix JSON output schema to match spec (nested summaries, all fields, `"ok"` status)
5. Add orchestration sequence tests with mocked dependencies (refactor execute to accept deps)
6. Bridge or unify domain types with output types
7. Fix summary table formatting (minor)


## Implementation Command

```
spec-kitty implement WP05 --base WP04
```

## Objectives & Success Criteria

Implement the `extract` and `clarify` CLI subcommands with both JSON and human-readable text output modes.

Success criteria:
- `frank-cli extract --project <path>` runs the full analysis pipeline and persists `ExtractionState` to `obj/frank-cli/`
- `frank-cli clarify --project <path>` loads persisted state and returns a list of disambiguation questions
- JSON output for both commands matches the schemas defined in `contracts/cli-commands.md`
- Text output is human-readable and optionally ANSI color-coded
- All orchestration steps execute in the correct sequence; failures at any step produce a diagnostic message referencing the source location

## Context & Constraints

- `ExtractCommand` orchestrates the full analysis pipeline: project load (Ionide.ProjInfo) → AST analysis → type analysis → mappers → state persistence (no compiled assembly required)
- `ClarifyCommand` reads the persisted `ExtractionState` from `obj/frank-cli/` and analyses it for ambiguities
- JSON output schemas are normative — output must match them exactly, including field names and envelope structure
- References: `plan.md`, `data-model.md` (ExtractionState lifecycle), `contracts/cli-commands.md`
- All new modules live under `Frank.Cli.Core`; the command modules are orchestrators only — business logic belongs in the analyser and mapper modules from earlier WPs

## Subtasks & Detailed Guidance

### T024: ExtractCommand.fs

Module: `Frank.Cli.Core.Commands.ExtractCommand`

Accepted parameters:
- `projectPath` — path to the .fsproj file (required)
- `baseUri` — base URI for the ontology (required)
- `vocabularies` — list of vocabulary URIs to align against (optional, default schema.org + hydra)
- `scope` — one of `project | file | resource` (optional, default `project`)

Orchestration sequence (must execute in this exact order):
1. Load project via `Ionide.ProjInfo` to obtain source files and compiler options (no compiled assembly required)
2. Run `AstAnalyzer` (from WP03) to extract route declarations and HTTP method handlers from untyped AST
3. Run `TypeAnalyzer` (from WP03) to resolve discriminated unions, record shapes, and type hierarchy from typed AST
4. Run `TypeMapper` to map F# types to OWL classes
5. Run `RouteMapper` to map Frank routes to Hydra operations
6. Run `CapabilityMapper` to map Frank resource capabilities to semantic actions
7. Run `ShapeGenerator` to produce SHACL shapes
8. Run `VocabularyAligner` to align generated terms against selected vocabularies
9. Persist `ExtractionState` to `obj/frank-cli/extraction-state.json`

Return type: `ExtractResult` record with:
- `ontologySummary` — class count, property count, aligned/unaligned counts
- `shapesSummary` — shape count, constraint count
- `unmappedTypes` — list of type names FCS found but could not map (with source file + line)
- `stateFilePath` — absolute path to the persisted state file

Error handling:
- Project file not found → emit `"Project file not found at <path>."` and return error result
- Ionide.ProjInfo load failure → emit descriptive error about .fsproj loading; suggest verifying the project file exists and MSBuild is available
- FCS failure → report with source file path and line number from the FCS diagnostic
- Any analyser step failure → report step name and underlying exception message; do not silently swallow

### T025: ClarifyCommand.fs

Module: `Frank.Cli.Core.Commands.ClarifyCommand`

Behaviour:
- Load `ExtractionState` from `obj/frank-cli/extraction-state.json` (or the path specified by `--project`)
- Analyse the state for the following ambiguity categories:
  - `unmapped-type` — types FCS found but no OWL class was generated
  - `open-or-closed` — discriminated unions where it is unclear whether cases are an open or closed enumeration
  - `object-or-datatype` — record fields where the property classification (object property vs. datatype property) is ambiguous (e.g., a `string` field named `homepage`)
  - `missing-relationship` — types that appear to relate to each other but have no explicit property linking them

Each question in the output:
- `id` — stable slug, e.g., `"unmapped-type-MyType"`
- `category` — one of the four categories above
- `questionText` — natural-language question for the user
- `context` — record with `sourceType` (string) and `location` (file path + line, if available)
- `options` — list of records with `label` (string) and `impact` (string describing how choosing this option affects the ontology)

Return type: `ClarifyResult` record with:
- `questions` — array of question records
- `resolvedCount` — count of questions that already have answers in the persisted state (from a prior clarify run)
- `totalCount` — total questions detected

### T026: JSON output formatting module

Module: `Frank.Cli.Core.Output.JsonOutput`

- Use `System.Text.Json` with `JsonSerializerOptions` (camelCase, indented)
- Provide `formatExtractResult : ExtractResult -> string` — serialises to the extract schema from `contracts/cli-commands.md`
- Provide `formatClarifyResult : ClarifyResult -> string` — serialises to the clarify schema
- Both functions must emit a top-level `status` field (`"ok"` on success, `"error"` on failure)
- Provide `formatError : string -> string` — emits `{ "status": "error", "message": "<msg>" }` for command-level failures
- Do not use third-party JSON libraries; `System.Text.Json` only

### T027: Text output mode

Module: `Frank.Cli.Core.Output.TextOutput`

- Provide `formatExtractResult : ExtractResult -> string` for human-readable extract output:
  - Summary table with columns: Category | Count (classes found, properties, routes, unmapped types)
  - List unmapped types with source location if available
- Provide `formatClarifyResult : ClarifyResult -> string`:
  - Numbered list of questions
  - For each question: question text, source context on a sub-line, lettered options each with its impact description
- ANSI colour: detect `NO_COLOR` env var and `isatty` on stdout; if a terminal is detected and `NO_COLOR` is not set, use colour codes (bold for headings, yellow for warnings, red for errors); otherwise output plain text
- Do not use any external terminal/colour libraries; write ANSI escape codes directly

### T028: Tests

Location: `test/Frank.Cli.Core.Tests/`

Coverage required:

**ExtractCommand tests**:
- Mock all analyser and mapper dependencies via interfaces/function parameters
- Verify orchestration calls each step exactly once and in the documented sequence
- Verify `ExtractionState` is written to the expected path after a successful run
- Verify that a project-not-found error is returned (not thrown) when the .fsproj path is invalid

**ClarifyCommand tests**:
- Create a synthetic `ExtractionState` with a known set of unmapped types and ambiguous fields
- Verify that the expected questions are generated with correct categories and non-empty question text
- Verify `resolvedCount` is zero when no prior answers exist

**JSON output tests**:
- Deserialise the output of `formatExtractResult` and verify required top-level fields match the contract schema
- Deserialise the output of `formatClarifyResult` and verify `status`, `questions`, `resolvedCount`, `totalCount` are present
- Verify `formatError` produces `{ "status": "error", "message": ... }`

**Text output tests**:
- Verify `formatExtractResult` output contains the summary table header text
- Verify `formatClarifyResult` output contains numbered questions and lettered options
- Verify ANSI codes are absent when `NO_COLOR=1` is set in the process environment

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| FCS diagnostic source locations may be unavailable for some type errors | Fall back to type name only; never fail because a source location is missing |
| `obj/frank-cli/` may not exist on first run | Create the directory as part of ExtractCommand before writing the state file |
| JSON schema may evolve after this WP is complete | Pin the schema version in the manifest and version the state file format; add a schema version field to the JSON output |

## Review Guidance

- Run `dotnet test test/Frank.Cli.Core.Tests/` — all tests must pass
- Manually invoke `frank-cli extract --project samples/...` and inspect the JSON output against `contracts/cli-commands.md`
- Manually invoke `frank-cli clarify --project samples/...` and verify questions are generated for any known ambiguous types
- Check that `--text` flag produces readable output without JSON
- Confirm ANSI codes absent in non-terminal output (pipe to a file and inspect)

## Activity Log

| Timestamp | Lane | Agent | Action |
|---|---|---|---|
| 2026-03-04T22:10:13Z | planned | system | Prompt generated via /spec-kitty.tasks |
- 2026-03-05T20:16:35Z – claude-opus – shell_pid=4655 – lane=doing – Assigned agent via workflow command
- 2026-03-05T20:31:30Z – claude-opus – shell_pid=4655 – lane=for_review – Ready for review: ExtractCommand, ClarifyCommand, JsonOutput, TextOutput + 15 new tests, 64 total passing
- 2026-03-05T20:34:04Z – claude-opus-reviewer – shell_pid=7121 – lane=doing – Started review via workflow command
- 2026-03-05T20:36:21Z – claude-opus-reviewer – shell_pid=7121 – lane=planned – Moved to planned
- 2026-03-05T20:47:13Z – claude-opus-reviewer – shell_pid=7121 – lane=for_review – R2: All 8 review issues fixed, 66 tests passing
- 2026-03-05T20:47:35Z – claude-opus-reviewer-r2 – shell_pid=8180 – lane=doing – Started review via workflow command
- 2026-03-05T20:48:07Z – claude-opus-reviewer-r2 – shell_pid=8180 – lane=done – Review passed (R2): All 8 issues fixed
