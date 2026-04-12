---
source: kitty-specs/025-validation-pipeline-wiring
type: plan
---

# Implementation Plan: Validation Pipeline End-to-End Wiring

**Branch**: `025-validation-pipeline-wiring` | **Date**: 2026-03-16 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `kitty-specs/025-validation-pipeline-wiring/spec.md`

## Summary

Add a thin orchestration module (`Pipeline.fs`) to `Frank.Statecharts.Validation` that accepts raw format source text as `(FormatTag * string)` pairs, dispatches to the correct parser per format, wraps results as `FormatArtifact` records, runs both self-consistency and cross-format validation rules, and returns a unified `PipelineResult`. This closes the gap between individual parsers and the existing cross-format validator engine (spec 021), enabling external consumers like frank-cli (#94) to validate statechart sources with a single function call. End-to-end integration tests verify the pipeline using real tic-tac-toe format text in all four formats.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank.Statecharts)
**Primary Dependencies**: Frank.Statecharts (same project -- Pipeline is a new module in the existing assembly), Frank.Statecharts.Ast (shared AST types), Frank.Statecharts.Validation (existing Types.fs and Validator.fs)
**Storage**: N/A (stateless -- pure functions, no persistence)
**Testing**: Expecto 10.2.3 + YoloDev.Expecto.TestSdk 0.14.3 + Microsoft.NET.Test.Sdk 17.14.1 (matching existing `Frank.Statecharts.Tests`)
**Target Platform**: .NET 8.0/9.0/10.0 (multi-targeting)
**Project Type**: Single library project with co-located test project
**Performance Goals**: Parse-and-validate of 4 format sources (~50 lines each) in under 2 seconds (SC-004)
**Constraints**: Pipeline must be public (FR-013) for frank-cli consumption. BLOCKED on #113, #114, #115 completing parser migrations to uniform `string -> Ast.ParseResult` interface.
**Scale/Scope**: One new source file (~100-150 lines), three new types added to existing Types.fs, one new test file (~300-400 lines)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | N/A | Statechart tooling, not HTTP resource definition |
| II. Idiomatic F# | PASS | Pipeline uses discriminated unions (`PipelineError`, `FormatTag`), option types, pure functions, pipeline-friendly signature `(FormatTag * string) list -> PipelineResult` |
| III. Library, Not Framework | PASS | Adds a composable function, no opinions beyond orchestration. Callers handle I/O and presentation. |
| IV. ASP.NET Core Native | N/A | No ASP.NET Core interaction -- pure library function |
| V. Performance Parity | PASS | Delegates to existing parsers and validator; no new allocations in hot paths. Performance test (SC-004) validates <2s for 4-format validation. |
| VI. Resource Disposal Discipline | PASS | No IDisposable resources created. All parsers are `string -> ParseResult` (post-migration), no streams or readers. |
| VII. No Silent Exception Swallowing | PASS | Parse errors are surfaced in `FormatParseResult.Errors`. Pipeline-level errors (duplicate format, unsupported format) returned as `PipelineError` in the result, not swallowed. Existing `Validator.validate` already catches rule exceptions and reports them as failures. |
| VIII. No Duplicated Logic | PASS | Reuses existing `SelfConsistencyRules.rules`, `CrossFormatRules.rules`, and `Validator.validate`. No logic duplication. Parser dispatch is a simple match expression with one case per format -- not duplicable. |

**Post-design re-check**: No new concerns. The Pipeline module is a thin dispatcher that composes existing pieces.

## Project Structure

### Documentation (this feature)

```
kitty-specs/025-validation-pipeline-wiring/
├── plan.md              # This file
├── spec.md              # Feature specification
├── data-model.md        # Phase 1: type definitions
└── quickstart.md        # Phase 1: usage examples
```

### Source Code (repository root)

```
src/Frank.Statecharts/
├── Validation/
│   ├── Types.fs              # MODIFY: Add PipelineResult, FormatParseResult, PipelineError types
│   ├── Validator.fs           # UNCHANGED: Existing validator engine
│   └── Pipeline.fs            # NEW: Pipeline module with validateSources function
├── Frank.Statecharts.fsproj   # MODIFY: Add Compile Include for Pipeline.fs

test/Frank.Statecharts.Tests/
├── Validation/
│   ├── PipelineTests.fs       # NEW: Unit tests for Pipeline module (dispatch, edge cases)
│   └── PipelineIntegrationTests.fs  # NEW: End-to-end tests with real format text
├── Frank.Statecharts.Tests.fsproj   # MODIFY: Add Compile Includes for new test files
```

**Structure Decision**: Pipeline module is added to the existing `Frank.Statecharts` project alongside existing `Validation/` files. No new projects needed. The module lives in the same assembly as the parsers, so it can call internal parser functions directly. Tests go in the existing test project under the `Validation/` subdirectory, matching the established pattern.

## Complexity Tracking

No constitution violations. No additional complexity beyond what the spec requires.

---

## Phase 0: Research

No research required. The user confirmed this is a low-risk thin orchestration layer. All dependencies are internal to the project:

- **Parser interfaces**: WSD already returns `Ast.ParseResult` directly via `Wsd.Parser.parseWsd : string -> ParseResult`. The other three parsers (smcat, SCXML, ALPS) currently return format-specific types with separate Mapper modules. The spec is BLOCKED on #113/#114/#115 completing the migration to uniform `string -> Ast.ParseResult`. The Pipeline assumes the post-migration interface.
- **Validator engine**: `Validator.validate : ValidationRule list -> FormatArtifact list -> ValidationReport` is the existing entry point (spec 021, complete).
- **Validation rules**: `SelfConsistencyRules.rules` and `CrossFormatRules.rules` are the built-in rule sets. FR-014 adds optional custom rules via a parameter.
- **Types**: `FormatTag`, `FormatArtifact`, `ValidationReport`, `ValidationRule` are all defined in `Validation/Types.fs`.

**Decision**: No `research.md` needed -- all unknowns are resolved.

---

## Phase 1: Design

### Data Model

See `kitty-specs/025-validation-pipeline-wiring/data-model.md` for full type definitions.

**New types added to `Validation/Types.fs`**:

1. **`PipelineError`** -- Discriminated union for input validation errors:
   - `DuplicateFormat of FormatTag` -- same format tag appears twice in input
   - `UnsupportedFormat of FormatTag` -- no parser registered for this format tag

2. **`FormatParseResult`** -- Per-format parse outcome:
   - `Format: FormatTag` -- which format this result is for
   - `Errors: ParseFailure list` -- parse errors from the format parser
   - `Warnings: ParseWarning list` -- parse warnings from the format parser
   - `Succeeded: bool` -- convenience: true when `Errors` is empty

3. **`PipelineResult`** -- Top-level result from `validateSources`:
   - `ParseResults: FormatParseResult list` -- one per input format
   - `Report: ValidationReport` -- unified validation report from all rules
   - `Errors: PipelineError list` -- pipeline-level errors (empty on success)

**No changes to existing types.**

### Pipeline Module Design

`Pipeline.fs` -- a public module `Frank.Statecharts.Validation.Pipeline` containing:

1. **`parserFor`** (private): `FormatTag -> (string -> Ast.ParseResult) option`
   - Match on `FormatTag` and return a function that parses text to `Ast.ParseResult`
   - `Wsd -> Some Wsd.Parser.parseWsd` (already returns `Ast.ParseResult` directly)
   - `XState -> None` (no parser exists yet)
   - For smcat, SCXML, and ALPS: these parsers currently return format-specific types and require a mapper step to produce `StatechartDocument`. The `parserFor` function must compose the parser and mapper calls into a single `string -> Ast.ParseResult` function. **Post-migration** (after specs 022/023/024 complete), these become direct calls. **Pre-migration** (current state), the composed calls are:
     - `Smcat -> Some (fun s -> let r = Smcat.Parser.parseSmcat s in Smcat.Mapper.toStatechartDocument r)` -- compose parser + mapper
     - `Scxml -> Some (fun s -> let r = Scxml.Parser.parseString s in Scxml.Mapper.toStatechartDocument r)` -- compose parser + mapper
     - `Alps -> Some (fun s -> let r = Alps.JsonParser.parseAlpsJson s in Alps.Mapper.toStatechartDocument r)` -- compose parser + mapper (note: `parseAlpsJson` returns `Result<AlpsDocument, ...>`, so error handling is needed)
   - The tasks (WP01) already describe this correctly. When specs 022/023/024 land, these simplify to direct parser calls returning `Ast.ParseResult`.

2. **`validateSources`** (public): `(FormatTag * string) list -> PipelineResult`
   - Validate input: check for empty list (return empty result), duplicate formats (return error)
   - For each `(tag, source)` pair:
     - Look up parser via `parserFor`
     - If no parser: add `UnsupportedFormat tag` to pipeline errors, skip
     - If parser found: call `parser source`, collect `FormatParseResult`, wrap `Document` as `FormatArtifact`
   - Assemble all `FormatArtifact` records
   - Run `Validator.validate (SelfConsistencyRules.rules @ CrossFormatRules.rules) artifacts`
   - Return `PipelineResult`

3. **`validateSourcesWithRules`** (public): `ValidationRule list -> (FormatTag * string) list -> PipelineResult`
   - Same as `validateSources` but prepends custom rules to the built-in rules (FR-014)
   - `validateSources` is implemented as `validateSourcesWithRules []`

### File Ordering in .fsproj

`Pipeline.fs` must appear after `Validation/Validator.fs` in the compile order (it depends on `Validator`, `SelfConsistencyRules`, `CrossFormatRules`). It must also appear after all parser files since it dispatches to them.

Insert after the existing `<!-- Validation -->` section:
```xml
<Compile Include="Validation/Types.fs" />
<Compile Include="Validation/Validator.fs" />
<Compile Include="Validation/Pipeline.fs" />    <!-- NEW -->
```

### Test Design

**`PipelineTests.fs`** (unit tests):
- Empty input returns empty result (FR-011)
- Duplicate format tags return `DuplicateFormat` error (FR-010)
- Unsupported format tag returns `UnsupportedFormat` error (FR-012)
- Single format runs self-consistency only, cross-format skipped (User Story 3)
- Parse errors included in `FormatParseResult.Errors` (FR-008)
- Custom rules via `validateSourcesWithRules` (FR-014)

**`PipelineIntegrationTests.fs`** (end-to-end with real format text):
- Consistent tic-tac-toe in all 4 formats produces zero failures (SC-001, User Story 5 scenario 1)
- State name mismatch detected across formats (SC-002, User Story 5 scenario 2)
- Missing event detected across formats (User Story 5 scenario 3)
- Parse error in one format still validates others (User Story 2)
- Performance: 4 formats in under 2 seconds (SC-004)

### Quickstart

See `kitty-specs/025-validation-pipeline-wiring/quickstart.md` for usage examples.
