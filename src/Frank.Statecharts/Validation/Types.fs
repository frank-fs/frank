namespace Frank.Statecharts.Validation

open Frank.Statecharts.Ast

/// Identifies which parser produced an artifact.
type FormatTag =
    | Wsd
    | Alps
    | Scxml
    | Smcat
    | XState

/// A format-tagged wrapper around a parsed StatechartDocument.
type FormatArtifact =
    { Format: FormatTag
      Document: StatechartDocument }

/// Status of a single validation check.
type CheckStatus =
    | Pass
    | Fail
    | Skip

/// A named invariant result from a validation rule.
type ValidationCheck =
    { Name: string
      Status: CheckStatus
      Reason: string option }

/// Detailed diagnostic information for a single validation failure.
type ValidationFailure =
    { Formats: FormatTag list
      EntityType: string
      Expected: string
      Actual: string
      Description: string }

/// Top-level result of a validation run.
type ValidationReport =
    { TotalChecks: int
      TotalSkipped: int
      TotalFailures: int
      Checks: ValidationCheck list
      Failures: ValidationFailure list }

/// A validation rule defined by a format module.
/// Check returns a tuple of (checks, failures) so rules can produce
/// rich diagnostic information for each failure.
type ValidationRule =
    { Name: string
      RequiredFormats: FormatTag Set
      Check: FormatArtifact list -> ValidationCheck list * ValidationFailure list }

/// Pipeline-level error for invalid input (not parse errors).
type PipelineError =
    | DuplicateFormat of FormatTag
    | UnsupportedFormat of FormatTag

/// Per-format parse outcome from the pipeline.
type FormatParseResult =
    { Format: FormatTag
      Errors: ParseFailure list
      Warnings: ParseWarning list
      Succeeded: bool }

/// Top-level result from the validation pipeline.
type PipelineResult =
    { ParseResults: FormatParseResult list
      Report: ValidationReport
      Errors: PipelineError list }
