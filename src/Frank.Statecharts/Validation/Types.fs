namespace Frank.Statecharts.Validation

open Frank.Statecharts.Ast

// ============================================================================
// Validation Domain Types (Spec 021 - WP01)
//
// All types match the contract in contracts/validation-api.fsi exactly.
// Namespace: Frank.Statecharts.Validation
// ============================================================================

/// Identifies which parser produced an artifact (FR-002, FR-014).
type FormatTag =
    | Wsd
    | Alps
    | Scxml
    | Smcat
    | XState

/// A format-tagged wrapper around a parsed StatechartDocument (FR-002).
type FormatArtifact =
    { Format: FormatTag
      Document: StatechartDocument }

/// Status of a single validation check (FR-004).
type CheckStatus =
    | Pass
    | Fail
    | Skip

/// A named invariant result from a validation rule (FR-004).
type ValidationCheck =
    { Name: string
      Status: CheckStatus
      Reason: string option }

/// Detailed diagnostic information for a single validation failure (FR-005, FR-015).
type ValidationFailure =
    { Formats: FormatTag list
      EntityType: string
      Expected: string
      Actual: string
      Description: string }

/// Top-level result of a validation run (FR-003, FR-009).
/// TotalChecks counts only Pass + Fail (not Skip).
/// TotalFailures equals Failures.Length.
type ValidationReport =
    { TotalChecks: int
      TotalSkipped: int
      TotalFailures: int
      Checks: ValidationCheck list
      Failures: ValidationFailure list }

/// A validation rule defined by a format module (FR-001, FR-017).
/// Note: structural equality is not supported due to function field.
type ValidationRule =
    { Name: string
      RequiredFormats: FormatTag Set
      Check: FormatArtifact list -> ValidationCheck list }
