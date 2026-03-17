---
work_package_id: WP04
title: Type Cleanup and Build Verification
lane: done
dependencies:
- WP01
- WP02
- WP03
subtasks:
- T028
- T029
- T030
- T031
- T032
- T033
- T034
phase: Phase 3 - Cleanup & Verification
assignee: ''
agent: ''
shell_pid: ''
review_status: approved
reviewed_by: claude-opus
history:
- timestamp: '2026-03-16T19:26:17Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-025, FR-026, FR-027, FR-030, FR-031]
---

# Work Package Prompt: WP04 -- Type Cleanup and Build Verification

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: Update `review_status: acknowledged` when you begin addressing feedback.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

This WP depends on WP01, WP02, and WP03:
```bash
spec-kitty implement WP04 --base WP03
```

---

## Objectives & Success Criteria

- Delete all format-specific types that are superseded by the shared AST: `ScxmlDocument`, `ScxmlState`, `ScxmlTransition`, `ScxmlParseResult`, `ScxmlStateKind`, `DataEntry`, `ParseError`, `ParseWarning` from `Scxml/Types.fs`.
- Verify `Scxml/Mapper.fs` was deleted in WP01 (confirmation only).
- Verify zero remaining references to deleted types in the entire codebase.
- Verify `dotnet build` succeeds across net8.0, net9.0, net10.0 with zero errors.
- Verify `dotnet test` passes with zero failures, including cross-format validator tests.
- This WP completes the migration (spec success criteria SC-004 through SC-008).

## Context & Constraints

- **Spec**: `kitty-specs/024-scxml-shared-ast-migration/spec.md` (FR-025, FR-026, FR-027, FR-030, FR-031)
- **Plan**: `kitty-specs/024-scxml-shared-ast-migration/plan.md` (Project Structure -- files to delete)
- **Prerequisite**: WP01, WP02, and WP03 must all be complete. The parser and generator no longer use format-specific types. All tests use shared AST types.
- **Retained types** (per FR-027): `ScxmlTransitionType` (Internal/External), `ScxmlHistoryKind` (Shallow/Deep), `SourcePosition` (if still referenced by parser internals).

---

## Subtasks & Detailed Guidance

### Subtask T028 -- Delete format-specific types from `Scxml/Types.fs`

- **Purpose**: Remove the types that have been superseded by shared AST types, keeping only those needed for parser internals.
- **Steps**:
  1. Open `src/Frank.Statecharts/Scxml/Types.fs`
  2. **Delete** the following type definitions:
     - `ScxmlStateKind` (Simple/Compound/Parallel/Final) -- replaced by `Ast.StateKind`
     - `DataEntry` (with `Id` field) -- replaced by `Ast.DataEntry` (with `Name` field)
     - `ScxmlTransition` -- replaced by `Ast.TransitionEdge`
     - `ScxmlHistory` -- replaced by `StateNode` with `Kind = ShallowHistory/DeepHistory` + `ScxmlAnnotation(ScxmlHistory(...))`
     - `ScxmlInvoke` -- replaced by `ScxmlAnnotation(ScxmlInvoke(...))` on `StateNode`
     - `ScxmlState` -- replaced by `Ast.StateNode`
     - `ScxmlDocument` -- replaced by `Ast.StatechartDocument`
     - `ParseError` -- replaced by `Ast.ParseFailure`
     - `ParseWarning` -- replaced by `Ast.ParseWarning`
     - `ScxmlParseResult` -- replaced by `Ast.ParseResult`
  3. **Retain** the following type definitions:
     - `SourcePosition` (struct) -- used by parser for XML line info. Check if parser still references it after WP01. If the parser was changed to use `Ast.SourcePosition` directly, this can be deleted too.
     - `ScxmlTransitionType` (Internal/External) -- may be used by parser internals for transition type parsing. Check references.
     - `ScxmlHistoryKind` (Shallow/Deep) -- may be used by parser internals for history kind parsing. Check references.
  4. If `SourcePosition`, `ScxmlTransitionType`, or `ScxmlHistoryKind` are no longer referenced by the parser (WP01 may have eliminated the references), they can be deleted too. Check with:
     ```bash
     grep -rn "ScxmlTransitionType\|ScxmlHistoryKind\|Scxml\.Types\.SourcePosition" src/Frank.Statecharts/Scxml/Parser.fs
     ```
  5. If ALL types are deleted, `Scxml/Types.fs` itself can be deleted and removed from the fsproj. Otherwise, keep it with only the retained types.
- **Files**: `src/Frank.Statecharts/Scxml/Types.fs`
- **Notes**: The module is declared `module internal Frank.Statecharts.Scxml.Types`, so all types are internal. No external API impact.

### Subtask T029 -- (Moved to WP01 as T016)

**Note**: Mapper.fs deletion was moved to WP01 (subtask T016) as part of the merged ScxmlMeta extension + parser migration WP. By the time WP04 runs, Mapper.fs is already deleted.

### Subtask T030 -- Verify Mapper.fs removed from project file

- **Purpose**: Verify that the `<Compile Include="Scxml/Mapper.fs" />` entry was removed from the project file (done in WP01 T016).
- **Steps**:
  1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`
  2. Confirm no `<Compile Include="Scxml/Mapper.fs" />` line exists.
  3. If `Scxml/Types.fs` was completely emptied and deleted in T027, also remove:
     ```xml
     <Compile Include="Scxml/Types.fs" />
     ```
  4. The SCXML section should look like:
     ```xml
     <!-- SCXML parser -->
     <Compile Include="Scxml/Types.fs" />   <!-- only if retained -->
     <Compile Include="Scxml/Parser.fs" />
     <Compile Include="Scxml/Generator.fs" />
     ```
- **Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`

### Subtask T031 -- Verify no remaining references to deleted types

- **Purpose**: Confirm that no code in `src/` or `test/` still references the deleted type names.
- **Steps**:
  1. Search for deleted type references:
     ```bash
     grep -rn "ScxmlDocument\b" src/ test/ --include="*.fs"
     grep -rn "ScxmlState\b" src/ test/ --include="*.fs"
     grep -rn "ScxmlTransition\b" src/ test/ --include="*.fs"
     grep -rn "ScxmlParseResult\b" src/ test/ --include="*.fs"
     grep -rn "ScxmlStateKind\b" src/ test/ --include="*.fs"
     ```
  2. Search for mapper references:
     ```bash
     grep -rn "Mapper\.\(toStatechartDocument\|fromStatechartDocument\)" src/ test/ --include="*.fs"
     grep -rn "Scxml\.Mapper" src/ test/ --include="*.fs"
     ```
  3. Each grep should return zero results. If any results are found, fix them before proceeding.
  4. Note: `ScxmlMeta` cases like `ScxmlTransitionType` (in `Ast/Types.fs`) and `ScxmlTransitionType` (in `Scxml/Types.fs`) may share the same name. Ensure the Scxml.Types version is not referenced if it was deleted. The Ast version is the one that should be used.
- **Files**: All `.fs` files in `src/` and `test/`
- **Notes**: Be careful with word boundaries. `ScxmlState` should not match `ScxmlStatechart...` etc. Use `\b` word boundary in grep patterns.

### Subtask T032 -- Verify build across all TFMs

- **Purpose**: Confirm clean build with zero errors.
- **Steps**:
  1. Clean build:
     ```bash
     dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj -c Release
     ```
     This builds all 3 TFMs (net8.0, net9.0, net10.0) due to multi-targeting.
  2. Verify zero errors and zero warnings (or only pre-existing warnings).
  3. Also build the test project:
     ```bash
     dotnet build test/Frank.Statecharts.Tests/
     ```
- **Files**: N/A (verification step)

### Subtask T033 -- Verify all tests pass

- **Purpose**: Confirm all SCXML tests pass after the full migration.
- **Steps**:
  1. Run the full test suite:
     ```bash
     dotnet test test/Frank.Statecharts.Tests/ --verbosity normal
     ```
  2. Verify zero failures.
  3. Pay special attention to:
     - Round-trip tests (strongest migration validation)
     - Error tests (Document-is-always-present change)
     - Type tests (deleted type tests removed, new case tests added)
- **Files**: N/A (verification step)

### Subtask T034 -- Verify cross-format validator tests

- **Purpose**: Confirm that validation tests involving SCXML artifacts work correctly without the mapper.
- **Steps**:
  1. Run validation-specific tests:
     ```bash
     dotnet test test/Frank.Statecharts.Tests/ --filter "FullyQualifiedName~Validation" --verbosity normal
     ```
  2. Verify zero failures.
  3. Check that cross-format tests (comparing SCXML with WSD, smcat, ALPS, XState artifacts) produce correct results.
  4. The validation code (`Validation/Validator.fs`, `Validation/Types.fs`) references `Scxml` only as a `FormatTag` enum case. This is unaffected by the migration. But if any validation test constructs SCXML parse results via the mapper and then validates them, those tests will need updating. Check:
     ```bash
     grep -rn "Mapper\|parseString\|ScxmlDocument" test/Frank.Statecharts.Tests/Validation/ --include="*.fs"
     ```
  5. If validation tests call `Parser.parseString` (which now returns `Ast.ParseResult`), they should work without changes since they already expect `Ast.ParseResult` or `StatechartDocument`.
- **Files**: `test/Frank.Statecharts.Tests/Validation/` (verification)
- **Notes**: The validation tests may not directly reference SCXML types at all (they work with `StatechartDocument` values). In that case, this is a simple verification step confirming no regressions.

---

## Risks & Mitigations

- **Premature deletion**: Deleting types that are still referenced will cause build failures. Mitigation: T031 searches for all references before T032 builds. Run T031 first, fix any remaining references, then proceed to T032.
- **Scxml.Types.SourcePosition vs Ast.SourcePosition**: If the parser still uses `Scxml.Types.SourcePosition` internally (for `IXmlLineInfo` extraction before converting to `Ast.SourcePosition`), we must retain it. Check parser source carefully. If the parser was changed in WP01 to produce `Ast.SourcePosition` directly from `IXmlLineInfo`, the SCXML `SourcePosition` can be deleted.
- **ScxmlTransitionType naming conflict**: Both `Scxml.Types.ScxmlTransitionType` (DU: Internal/External) and `Ast.ScxmlMeta.ScxmlTransitionType` (case: `internal: bool`) exist. If the `Scxml.Types` version is deleted, any parser code using `Internal`/`External` cases must switch to using `bool` values or the `Ast.HistoryKind` equivalents. Verify parser references in WP01.

---

## Review Guidance

- **SC-004**: Zero references to `ScxmlDocument`, `ScxmlState`, `ScxmlTransition`, `ScxmlParseResult`, or `ScxmlStateKind` in codebase (grep verification).
- **SC-005**: `Mapper.fs` does not exist in filesystem or project file.
- **SC-006**: All SCXML tests pass (zero failures).
- **SC-007**: Build succeeds across all three TFMs (zero errors).
- **SC-008**: Cross-format validator tests pass.
- Verify `Scxml/Types.fs` retains ONLY types needed for parser internals (if any).
- Verify the `Frank.Statecharts.fsproj` has correct `<Compile>` entries (no deleted files).

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T19:26:17Z -- system -- lane=planned -- Prompt created.
- 2026-03-17T05:00:00Z -- claude-opus -- lane=done -- Review APPROVED. All 14 checklist items passed. Types.fs fully deleted (not emptied), fsproj updated, zero references to deleted types, build clean (0 warnings, 0 errors, all 3 TFMs), 834/834 tests pass, 104/104 validation tests pass. SC-004 through SC-008 all met. Commit db5ffb7.

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP04 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
