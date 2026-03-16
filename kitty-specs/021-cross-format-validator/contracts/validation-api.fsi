// Contract: Cross-Format Statechart Validator API
// Spec: 021-cross-format-validator
// Date: 2026-03-15
// Status: Design contract (not compiled -- reference for implementation)
//
// This file defines the public API surface of the validation module.
// Implementation files must conform to these signatures.

namespace Frank.Statecharts.Validation

// ─────────────────────────────────────────────
// Types.fs
// ─────────────────────────────────────────────

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
      Document: Frank.Statecharts.Ast.StatechartDocument }

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
type ValidationRule =
    { Name: string
      RequiredFormats: FormatTag Set
      Check: FormatArtifact list -> ValidationCheck list }

// ─────────────────────────────────────────────
// Validator.fs — AstHelpers module
// ─────────────────────────────────────────────

module AstHelpers =
    open Frank.Statecharts.Ast

    /// Extract all StateNode values from a document, recursively including
    /// children and states nested in GroupBlock branches.
    val allStates: doc: StatechartDocument -> StateNode list

    /// Extract all TransitionEdge values from a document, including those
    /// nested in GroupBlock branches.
    val allTransitions: doc: StatechartDocument -> TransitionEdge list

    /// Extract the set of all state identifiers from a document.
    val stateIdentifiers: doc: StatechartDocument -> string Set

    /// Extract the set of all event names from transitions in a document.
    /// Filters out None events.
    val eventNames: doc: StatechartDocument -> string Set

    /// Extract the set of all transition target identifiers from a document.
    /// Filters out None (internal/completion) targets.
    val transitionTargets: doc: StatechartDocument -> string Set

// ─────────────────────────────────────────────
// Validator.fs — Validator module
// ─────────────────────────────────────────────

module Validator =

    /// Validate statechart artifacts against registered rules.
    ///
    /// For each rule:
    /// - If RequiredFormats is not a subset of available format tags, the rule is
    ///   skipped and a Skip check is recorded with the reason.
    /// - Otherwise, the rule's Check function is invoked with the full artifact list.
    /// - If Check throws an exception, a Fail check and a ValidationFailure are
    ///   recorded with the rule name and exception message.
    ///
    /// All results are aggregated into a single ValidationReport.
    /// The validator never aborts on the first failure.
    val validate: rules: ValidationRule list -> artifacts: FormatArtifact list -> ValidationReport
