# Data Model: Validation Pipeline End-to-End Wiring

## New Types (added to `src/Frank.Statecharts/Validation/Types.fs`)

All new types are added to the existing `Frank.Statecharts.Validation` namespace, below the existing `ValidationRule` type definition.

### PipelineError

Discriminated union for pipeline-level input validation errors. These are not parse errors (which come from format parsers) but errors in the pipeline's own input.

```fsharp
/// Pipeline-level error for invalid input (not parse errors).
type PipelineError =
    | DuplicateFormat of FormatTag
    | UnsupportedFormat of FormatTag
```

**Rationale**: Using a DU rather than strings ensures callers can pattern match on error types. Two cases cover the spec's edge cases: duplicate format tags in input (FR-010) and format tags with no registered parser (FR-012).

### FormatParseResult

Per-format parse outcome record. One is produced for each `(FormatTag * string)` pair that has a registered parser.

```fsharp
/// Per-format parse outcome from the pipeline.
type FormatParseResult =
    { Format: FormatTag
      Errors: ParseFailure list
      Warnings: ParseWarning list
      Succeeded: bool }
```

**Fields**:
- `Format`: The format tag that produced this result
- `Errors`: Parse errors from the format parser (from `Ast.ParseResult.Errors`)
- `Warnings`: Parse warnings from the format parser (from `Ast.ParseResult.Warnings`)
- `Succeeded`: Convenience field, `true` when `Errors` is empty

**Rationale**: Wrapping parse diagnostics per-format allows the caller (e.g., frank-cli) to attribute errors to the correct source file without needing to understand `Ast.ParseResult` internals.

### PipelineResult

Top-level result from `validateSources`. Contains everything the caller needs to present results.

```fsharp
/// Top-level result from the validation pipeline.
type PipelineResult =
    { ParseResults: FormatParseResult list
      Report: ValidationReport
      Errors: PipelineError list }
```

**Fields**:
- `ParseResults`: One `FormatParseResult` per input format that had a registered parser
- `Report`: The unified `ValidationReport` from running all applicable validation rules against all successfully parsed artifacts
- `Errors`: Pipeline-level errors (duplicate formats, unsupported formats). Empty list on valid input.

**Rationale**: Separating pipeline errors from parse results and validation report keeps concerns clean. The CLI can check `Errors` first for input problems, then `ParseResults` for per-format diagnostics, then `Report` for cross-format validation results.

## Existing Types (unchanged)

The following types from `Validation/Types.fs` are used but not modified:

- `FormatTag` -- DU: `Wsd | Alps | Scxml | Smcat | XState`
- `FormatArtifact` -- `{ Format: FormatTag; Document: StatechartDocument }`
- `ValidationReport` -- `{ TotalChecks; TotalSkipped; TotalFailures; Checks; Failures }`
- `ValidationRule` -- `{ Name; RequiredFormats; Check }`
- `ValidationCheck` -- `{ Name; Status; Reason }`
- `ValidationFailure` -- `{ Formats; EntityType; Expected; Actual; Description }`

The following types from `Ast/Types.fs` are used but not modified:

- `ParseResult` -- `{ Document: StatechartDocument; Errors: ParseFailure list; Warnings: ParseWarning list }`
- `ParseFailure` -- `{ Position; Description; Expected; Found; CorrectiveExample }`
- `ParseWarning` -- `{ Position; Description; Suggestion }`
- `StatechartDocument` -- `{ Title; InitialStateId; Elements; DataEntries; Annotations }`

## Entity Relationships

```
(FormatTag * string) list
        │
        ▼
   ┌─────────────────────┐
   │  Pipeline.validate   │
   │  Sources             │
   └──┬──────────┬───────┘
      │          │
      ▼          ▼
FormatParseResult   FormatArtifact
(per format)        (per format)
      │                  │
      │                  ▼
      │          ┌──────────────┐
      │          │  Validator   │
      │          │  .validate   │
      │          └──────┬───────┘
      │                 │
      │                 ▼
      │          ValidationReport
      │                 │
      ▼                 ▼
   ┌─────────────────────────┐
   │      PipelineResult      │
   │  .ParseResults           │
   │  .Report                 │
   │  .Errors                 │
   └─────────────────────────┘
```

## Validation Rules

No new validation rules are defined by the pipeline. It composes existing rules:

- `SelfConsistencyRules.rules` (5 rules): orphan targets, duplicate states, required fields, isolated states, empty statechart
- `CrossFormatRules.rules` (30 rules): pairwise state name, event name, and transition target agreement for all 10 format pairs

FR-014 allows callers to provide additional custom `ValidationRule` values via `validateSourcesWithRules`.
