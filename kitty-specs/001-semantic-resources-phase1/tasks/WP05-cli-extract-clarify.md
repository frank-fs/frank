---
work_package_id: WP05
title: CLI Commands — Extract & Clarify
lane: "doing"
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
agent: ''
shell_pid: "4655"
review_status: ''
reviewed_by: ''
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

_No feedback recorded._

> **Markdown Formatting Note**: Use ATX headings (`#`), fenced code blocks with language tags, and standard bullet lists. Do not use HTML tags or custom directives.

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
