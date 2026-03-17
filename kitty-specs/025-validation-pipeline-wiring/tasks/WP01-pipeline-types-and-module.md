---
work_package_id: WP01
title: Pipeline Types & Module Foundation
lane: "done"
dependencies: []
base_branch: master
base_commit: b73e5455785e394d39d47b986125f459682e8eb3
created_at: '2026-03-17T22:52:51.732872+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
- T006
- T007
phase: Phase 0 - Foundation
assignee: ''
agent: "claude-opus-reviewer"
shell_pid: "80597"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-16T19:13:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-001
- FR-002
- FR-003
- FR-004
- FR-005
- FR-006
- FR-007
- FR-009
- FR-010
- FR-011
- FR-012
- FR-013
- FR-014
---

# Work Package Prompt: WP01 -- Pipeline Types & Module Foundation

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

No dependencies -- start from base branch:

```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

1. Add three new types (`PipelineError`, `FormatParseResult`, `PipelineResult`) to `Validation/Types.fs`.
2. Create `Validation/Pipeline.fs` with a public `Pipeline` module containing `parserFor` (private), `validateSourcesWithRules` (public), and `validateSources` (public).
3. Wire `Pipeline.fs` into the `.fsproj` compile order.
4. `dotnet build` succeeds for all three target frameworks (net8.0, net9.0, net10.0).
5. The `Pipeline` module is public (FR-013) -- accessible from external assemblies.

## Context & Constraints

- **Spec**: `kitty-specs/025-validation-pipeline-wiring/spec.md` -- FR-001 through FR-014
- **Plan**: `kitty-specs/025-validation-pipeline-wiring/plan.md` -- Phase 1 Design section
- **Data Model**: `kitty-specs/025-validation-pipeline-wiring/data-model.md` -- type definitions
- **Quickstart**: `kitty-specs/025-validation-pipeline-wiring/quickstart.md` -- API usage examples

**Key fact**: All parsers now return `Ast.ParseResult` directly (AST migrations 022/023/024 are complete). No mapper modules exist. The uniform interface is `string -> ParseResult`:
- **WSD**: `Wsd.Parser.parseWsd : string -> ParseResult`
- **smcat**: `Smcat.Parser.parseSmcat : string -> ParseResult`
- **SCXML**: `Scxml.Parser.parseString : string -> ParseResult`
- **ALPS**: `Alps.JsonParser.parseAlpsJson : string -> ParseResult`

All parser modules are `internal` but Pipeline lives in the same assembly, so access is fine.

**Compile order in `Frank.Statecharts.fsproj`**: Pipeline.fs must appear AFTER `Validation/Validator.fs` (which is after all parser and mapper files). Insert it as the last entry in the `<!-- Validation -->` section.

## Subtasks & Detailed Guidance

### Subtask T001 -- Add `PipelineError` discriminated union to Types.fs

- **Purpose**: Define the pipeline-level error type for input validation failures (FR-010, FR-012).
- **Steps**:
  1. Open `src/Frank.Statecharts/Validation/Types.fs`.
  2. After the existing `ValidationRule` type definition (end of file, approximately line 53), add the `PipelineError` type.
  3. Add XML doc comment explaining its purpose.
- **Code to add**:
  ```fsharp
  /// Pipeline-level error for invalid input (not parse errors).
  type PipelineError =
      | DuplicateFormat of FormatTag
      | UnsupportedFormat of FormatTag
  ```
- **Files**: `src/Frank.Statecharts/Validation/Types.fs`
- **Notes**: Two cases cover the spec's edge cases: duplicate format tags in input (FR-010) and format tags with no registered parser (FR-012, currently only `XState`).

### Subtask T002 -- Add `FormatParseResult` record type to Types.fs

- **Purpose**: Per-format parse outcome wrapper that attributes parse diagnostics to a specific format (FR-008).
- **Steps**:
  1. After the `PipelineError` type, add the `FormatParseResult` record.
  2. Include XML doc comment.
- **Code to add**:
  ```fsharp
  /// Per-format parse outcome from the pipeline.
  type FormatParseResult =
      { Format: FormatTag
        Errors: ParseFailure list
        Warnings: ParseWarning list
        Succeeded: bool }
  ```
- **Files**: `src/Frank.Statecharts/Validation/Types.fs`
- **Notes**: `ParseFailure` and `ParseWarning` are defined in `Frank.Statecharts.Ast` (already opened at top of file). `Succeeded` is a convenience field: `true` when `Errors` is empty.

### Subtask T003 -- Add `PipelineResult` record type to Types.fs

- **Purpose**: Top-level result type returned by `validateSources` (FR-003).
- **Steps**:
  1. After `FormatParseResult`, add the `PipelineResult` record.
  2. Include XML doc comment.
- **Code to add**:
  ```fsharp
  /// Top-level result from the validation pipeline.
  type PipelineResult =
      { ParseResults: FormatParseResult list
        Report: ValidationReport
        Errors: PipelineError list }
  ```
- **Files**: `src/Frank.Statecharts/Validation/Types.fs`
- **Notes**: `ValidationReport` is defined earlier in the same file. `Errors` contains pipeline-level errors (duplicate formats, unsupported formats), NOT parse errors.

### Subtask T004 -- Create Pipeline.fs with `parserFor` private function

- **Purpose**: Create the Pipeline module and implement format-tag-to-parser dispatch (FR-004, FR-005).
- **Steps**:
  1. Create new file `src/Frank.Statecharts/Validation/Pipeline.fs`.
  2. Use namespace `Frank.Statecharts.Validation`.
  3. Open required namespaces: `Frank.Statecharts.Ast`.
  4. Create module `Pipeline` (NOT `internal` -- must be public per FR-013).
  5. Implement `parserFor` as a private function.
- **Code structure**:
  ```fsharp
  namespace Frank.Statecharts.Validation

  open Frank.Statecharts.Ast

  /// End-to-end validation pipeline: parse format sources and validate.
  module Pipeline =

      /// Look up the parser function for a given format tag.
      /// Returns None for formats with no registered parser (e.g., XState).
      let private parserFor (tag: FormatTag) : (string -> ParseResult) option =
          match tag with
          | Wsd -> Some Wsd.Parser.parseWsd
          | Smcat -> Some Smcat.Parser.parseSmcat
          | Scxml -> Some Scxml.Parser.parseString
          | Alps -> Some Alps.JsonParser.parseAlpsJson
          | XState -> None
  ```
- **Files**: `src/Frank.Statecharts/Validation/Pipeline.fs` (NEW)
- **Notes**:
  - All four parsers return `Ast.ParseResult` directly (post-migration). Each case is simple delegation.
  - No mapper modules or adapter logic needed.

### Subtask T005 -- Implement `validateSourcesWithRules` public function

- **Purpose**: The main pipeline orchestration function that accepts custom rules (FR-014) and format source pairs (FR-002), validates input, parses, and runs validation (FR-006, FR-007).
- **Steps**:
  1. In `Pipeline.fs`, after `parserFor`, implement `validateSourcesWithRules`.
  2. Signature: `ValidationRule list -> (FormatTag * string) list -> PipelineResult`
  3. Implementation sequence:
     a. Handle empty input (FR-011): return empty `PipelineResult` immediately.
     b. Check for duplicate `FormatTag` values (FR-010): collect duplicates, return `DuplicateFormat` errors.
     c. For each `(tag, source)` pair with a parser: call the parser, collect `FormatParseResult` and `FormatArtifact`.
     d. For tags with no parser: add `UnsupportedFormat` to pipeline errors.
     e. Run `Validator.validate` with combined rules: `customRules @ SelfConsistencyRules.rules @ CrossFormatRules.rules`.
     f. Assemble and return `PipelineResult`.
- **Code structure**:
  ```fsharp
      /// Validate format sources with custom rules prepended to built-in rules.
      let validateSourcesWithRules
          (customRules: ValidationRule list)
          (sources: (FormatTag * string) list)
          : PipelineResult =
          // Empty input
          if List.isEmpty sources then
              { ParseResults = []
                Report =
                    { TotalChecks = 0; TotalSkipped = 0; TotalFailures = 0
                      Checks = []; Failures = [] }
                Errors = [] }
          else
              // Check for duplicate format tags
              let duplicates =
                  sources
                  |> List.map fst
                  |> List.groupBy id
                  |> List.filter (fun (_, group) -> group.Length > 1)
                  |> List.map (fun (tag, _) -> DuplicateFormat tag)

              if not (List.isEmpty duplicates) then
                  { ParseResults = []
                    Report =
                        { TotalChecks = 0; TotalSkipped = 0; TotalFailures = 0
                          Checks = []; Failures = [] }
                    Errors = duplicates }
              else
                  let mutable pipelineErrors = []
                  let parseResults = ResizeArray<FormatParseResult>()
                  let artifacts = ResizeArray<FormatArtifact>()

                  for (tag, source) in sources do
                      match parserFor tag with
                      | None ->
                          pipelineErrors <- UnsupportedFormat tag :: pipelineErrors
                      | Some parser ->
                          let result = parser source
                          parseResults.Add(
                              { Format = tag
                                Errors = result.Errors
                                Warnings = result.Warnings
                                Succeeded = List.isEmpty result.Errors })
                          artifacts.Add(
                              { Format = tag
                                Document = result.Document })

                  let allRules = customRules @ SelfConsistencyRules.rules @ CrossFormatRules.rules
                  let report = Validator.validate allRules (Seq.toList artifacts)

                  { ParseResults = Seq.toList parseResults
                    Report = report
                    Errors = List.rev pipelineErrors }
  ```
- **Files**: `src/Frank.Statecharts/Validation/Pipeline.fs`
- **Notes**:
  - The function uses `ResizeArray` for collecting results during iteration -- this is idiomatic F# for imperative loops.
  - Alternatively, use `List.fold` for a more functional style if preferred. Either approach is acceptable.
  - Custom rules are prepended so they run before built-in rules.
  - Parse errors do NOT prevent validation -- the best-effort `Document` is always used (FR-009).

### Subtask T006 -- Implement `validateSources` shorthand

- **Purpose**: Convenience function that calls `validateSourcesWithRules` with no custom rules (FR-001, FR-002).
- **Steps**:
  1. After `validateSourcesWithRules`, add `validateSources`.
- **Code**:
  ```fsharp
      /// Validate format sources using built-in self-consistency and cross-format rules.
      let validateSources (sources: (FormatTag * string) list) : PipelineResult =
          validateSourcesWithRules [] sources
  ```
- **Files**: `src/Frank.Statecharts/Validation/Pipeline.fs`
- **Notes**: This is a one-liner. It exists for API ergonomics so callers don't need to pass `[]` when they don't have custom rules.

### Subtask T007 -- Add Pipeline.fs to fsproj compile order

- **Purpose**: Ensure `Pipeline.fs` is compiled after `Validator.fs` and all parser/mapper files.
- **Steps**:
  1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`.
  2. Find the `<!-- Validation -->` section containing `Validation/Types.fs` and `Validation/Validator.fs`.
  3. Add `<Compile Include="Validation/Pipeline.fs" />` immediately after `Validation/Validator.fs`.
- **Before**:
  ```xml
  <!-- Validation -->
  <Compile Include="Validation/Types.fs" />
  <Compile Include="Validation/Validator.fs" />
  ```
- **After**:
  ```xml
  <!-- Validation -->
  <Compile Include="Validation/Types.fs" />
  <Compile Include="Validation/Validator.fs" />
  <Compile Include="Validation/Pipeline.fs" />
  ```
- **Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Validation**: Run `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` and verify success for all target frameworks.

## Risks & Mitigations

1. **Internal module access**: All parser modules are `internal`. Since `Pipeline.fs` is in the same assembly (`Frank.Statecharts`), it can access internal modules directly.

2. **F# compile order**: If `Pipeline.fs` cannot see parser modules, the build will fail immediately with "undefined" errors. The fsproj file order ensures parsers compile before the validation section.

## Review Guidance

- Verify all three new types match `data-model.md` exactly.
- Verify `Pipeline` module is NOT marked `internal`.
- Verify `parserFor` handles all 5 `FormatTag` cases (Wsd, Smcat, Scxml, Alps, XState).
- Verify SCXML and ALPS adapter logic correctly converts format-specific types to `Ast.ParseResult`.
- Verify `validateSourcesWithRules` handles all three input validation cases: empty input, duplicate formats, unsupported formats.
- Run `dotnet build` on all three target frameworks.

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-17T22:52:51Z – claude-opus – shell_pid=80000 – lane=doing – Assigned agent via workflow command
- 2026-03-17T22:54:20Z – claude-opus – shell_pid=80000 – lane=for_review – Ready for review: types + Pipeline module, build green
- 2026-03-17T22:54:26Z – claude-opus-reviewer – shell_pid=80597 – lane=doing – Started review via workflow command
- 2026-03-17T22:55:38Z – claude-opus-reviewer – shell_pid=80597 – lane=done – Review passed: types match data-model, Pipeline module public, all edge cases handled, build green
