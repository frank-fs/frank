namespace Frank.Provenance

/// Why a provenance record violated role conformance.
[<RequireQualifiedAccess>]
type ViolationReason =
    /// Role's projected profile has no matching transition for this event in this state.
    | TransitionNotInProjection of role: string
    /// Role is not recognized by the protocol (not in the projection map).
    | RoleNotInProjection of role: string
    /// No acting roles were recorded — conformance cannot be verified.
    | NoActingRoles

/// A single conformance violation tied to a specific provenance record.
type ConformanceViolation =
    {
        /// The provenance record that violated conformance.
        Record: ProvenanceRecord
        /// Why the violation occurred, one entry per failing role or structural issue.
        Reasons: ViolationReason list
    }

/// Result of checking a sequence of provenance records against projected profiles.
type ConformanceReport =
    {
        /// Total records checked.
        TotalRecords: int
        /// Records that violated conformance.
        Violations: ConformanceViolation list
    }

    /// Records that passed conformance (at least one acting role had the transition).
    member this.ConformantCount = this.TotalRecords - this.Violations.Length
