namespace Frank.Provenance

open Frank.Statecharts.Dual

/// Why a provenance record violated dual conformance.
[<RequireQualifiedAccess>]
type DualViolationReason =
    /// Role had a MustSelect obligation in the given state but only polled (no advancing action).
    | ObligationNotFulfilled of role: string * state: string * requiredDescriptors: string list
    /// The trace included a transition not present in the role's dual FSM for that state.
    | TransitionNotInDual of role: string * state: string * descriptor: string
    /// The trace's state sequence does not follow a valid path through the dual FSM.
    | DualSequenceViolation of expected: string * actual: string
    /// Two participants' traces are not dual-compatible on a shared cut point:
    /// one's send does not match the other's receive.
    | CutInconsistency of
        descriptor: string *
        senderRole: string *
        receiverRole: string *
        senderState: string *
        receiverState: string

/// A single dual conformance violation tied to a specific provenance record.
type DualConformanceViolation =
    {
        /// The provenance record that violated dual conformance.
        Record: ProvenanceRecord
        /// Why the violation occurred.
        Reasons: DualViolationReason list
    }

/// Result of checking a sequence of provenance records against the client dual.
type DualConformanceReport =
    {
        /// Total records checked.
        TotalRecords: int
        /// Records that violated dual conformance.
        Violations: DualConformanceViolation list
        /// Obligation violations: states where MustSelect was required but not fulfilled.
        ObligationViolations: DualConformanceViolation list
        /// Cut consistency violations across participant pairs.
        CutViolations: DualConformanceViolation list
    }

    /// Records that passed all dual conformance checks.
    member this.ConformantCount = this.TotalRecords - this.Violations.Length

    /// Whether the trace is fully dual-conformant.
    member this.IsConformant =
        List.isEmpty this.Violations
        && List.isEmpty this.ObligationViolations
        && List.isEmpty this.CutViolations
