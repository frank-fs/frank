namespace Frank.Provenance

open Frank.Resources.Model
open Frank.Statecharts.Dual

/// Post-hoc dual conformance checking of provenance records against client dual annotations.
[<RequireQualifiedAccess>]
module DualConformanceChecker =

    /// Extract the set of descriptor names available to a role in a given state from dual annotations.
    let private descriptorsForRoleInState (dualResult: DeriveResult) (role: string) (state: string) : Set<string> =
        dualResult.Annotations
        |> Map.tryFind (role, state)
        |> Option.defaultValue []
        |> List.map (fun a -> a.Descriptor)
        |> Set.ofList

    /// Get the MustSelect descriptors for a role in a given state.
    let private mustSelectDescriptors (dualResult: DeriveResult) (role: string) (state: string) : string list =
        dualResult.Annotations
        |> Map.tryFind (role, state)
        |> Option.defaultValue []
        |> List.filter (fun a -> a.Obligation = MustSelect)
        |> List.map (fun a -> a.Descriptor)

    /// Identify all states where cut point annotations exist, keyed by descriptor name.
    let private cutPointStates (dualResult: DeriveResult) : Map<string, string> =
        dualResult.Annotations
        |> Map.toList
        |> List.collect (fun ((_role, state), anns) ->
            anns
            |> List.choose (fun a ->
                match a.CutPoint with
                | Some _ -> Some(a.Descriptor, state)
                | None -> None))

        |> Map.ofList

    /// A state epoch: a contiguous run of records while the system is in a given state.
    /// Tracks which roles acted and whether they performed advancing actions.
    type private StateEpoch =
        { State: string
          RolesWithAdvancingAction: Set<string>
          RolesPresent: Set<string> }

    /// Build state epochs from the trace. An epoch starts when we enter a state
    /// and ends when the state changes (or trace ends).
    let private buildStateEpochs (records: ProvenanceRecord list) : StateEpoch list =
        match records with
        | [] -> []
        | _ ->
            let folder
                (
                    currentState: string,
                    currentAdvancing: Set<string>,
                    currentPresent: Set<string>,
                    revEpochs: StateEpoch list
                )
                (record: ProvenanceRecord)
                =
                let recordState = record.Activity.PreviousState
                let isAdvancing = record.Activity.PreviousState <> record.Activity.NewState
                let roles = record.ActingRoles |> Set.ofList

                if recordState <> currentState then
                    // State changed — close the previous epoch, start a new one
                    let closedEpoch =
                        { State = currentState
                          RolesWithAdvancingAction = currentAdvancing
                          RolesPresent = currentPresent }

                    let newAdvancing = if isAdvancing then roles else Set.empty
                    let newPresent = roles
                    (recordState, newAdvancing, newPresent, closedEpoch :: revEpochs)
                else
                    // Same state — accumulate
                    let advancing' =
                        if isAdvancing then
                            Set.union currentAdvancing roles
                        else
                            currentAdvancing

                    let present' = Set.union currentPresent roles
                    (currentState, advancing', present', revEpochs)

            let firstRecord = List.head records
            let firstState = firstRecord.Activity.PreviousState

            let firstIsAdvancing =
                firstRecord.Activity.PreviousState <> firstRecord.Activity.NewState

            let firstRoles = firstRecord.ActingRoles |> Set.ofList

            let initAdvancing = if firstIsAdvancing then firstRoles else Set.empty

            let lastState, lastAdvancing, lastPresent, revEpochs =
                records
                |> List.tail
                |> List.fold folder (firstState, initAdvancing, firstRoles, [])

            // Close the last epoch
            let lastEpoch =
                { State = lastState
                  RolesWithAdvancingAction = lastAdvancing
                  RolesPresent = lastPresent }

            (lastEpoch :: revEpochs) |> List.rev

    /// Check that MustSelect obligations are fulfilled: for each state where a role
    /// has MustSelect obligations, the trace must contain at least one advancing action
    /// by that role before the state changes.
    ///
    /// Algorithm: partition the trace into "state epochs" (contiguous runs in the same state).
    /// For each epoch, check every role that was present: if the role has MustSelect obligations
    /// in that state, it must have performed at least one advancing action during the epoch.
    /// A role that was absent from an epoch is not checked (they may not have been active).
    let checkObligationFulfillment
        (dualResult: DeriveResult)
        (records: ProvenanceRecord list)
        : DualConformanceViolation list =
        match records with
        | [] -> []
        | _ ->
            // Build state epochs from the trace
            let epochs = buildStateEpochs records

            // For each epoch, check obligation fulfillment
            epochs
            |> List.choose (fun epoch ->
                // Find roles present in this epoch that have MustSelect obligations
                let violatingRoles =
                    epoch.RolesPresent
                    |> Set.toList
                    |> List.choose (fun role ->
                        let mustSelects = mustSelectDescriptors dualResult role epoch.State

                        if List.isEmpty mustSelects then
                            None // No obligation for this role in this state
                        elif Set.contains role epoch.RolesWithAdvancingAction then
                            None // Obligation fulfilled — role performed an advancing action
                        else
                            Some(role, epoch.State, mustSelects))

                if List.isEmpty violatingRoles then
                    None
                else
                    // Find a representative record from this epoch for the violation
                    let representativeRecord =
                        records
                        |> List.find (fun r ->
                            r.Activity.PreviousState = epoch.State
                            && r.ActingRoles
                               |> List.exists (fun role ->
                                   violatingRoles |> List.exists (fun (vr, _, _) -> vr = role)))

                    let reasons =
                        violatingRoles
                        |> List.map (fun (role, state, descriptors) ->
                            DualViolationReason.ObligationNotFulfilled(role, state, descriptors))

                    Some
                        { Record = representativeRecord
                          Reasons = reasons })

    /// Verify that the sequence of transitions follows a valid path through the
    /// role's dual state machine. Each transition must exist in the dual annotations
    /// for the acting role in the current state, and the state sequence must be continuous.
    let checkDualSequence
        (initialStateKey: string)
        (dualResult: DeriveResult)
        (records: ProvenanceRecord list)
        : DualConformanceViolation list =
        let folder (expectedState: string, revViolations: DualConformanceViolation list) (record: ProvenanceRecord) =
            let actualSource = record.Activity.PreviousState
            let event = record.Activity.EventName

            // Check 1: State sequence continuity
            let sequenceReasons =
                if actualSource <> expectedState then
                    [ DualViolationReason.DualSequenceViolation(expectedState, actualSource) ]
                else
                    []

            // Check 2: Transition exists in acting role's dual annotations
            let transitionReasons =
                match record.ActingRoles with
                | [] -> []
                | roles ->
                    roles
                    |> List.choose (fun role ->
                        let availableDescriptors = descriptorsForRoleInState dualResult role actualSource

                        if Set.contains event availableDescriptors then
                            None
                        else
                            Some(DualViolationReason.TransitionNotInDual(role, actualSource, event)))
                    |> fun reasons ->
                        // Only flag if ALL roles lack the transition (same logic as existing conformance checker)
                        if List.length reasons = List.length roles then
                            reasons
                        else
                            []

            let combinedReasons = sequenceReasons @ transitionReasons
            let nextExpected = record.Activity.NewState

            if List.isEmpty combinedReasons then
                (nextExpected, revViolations)
            else
                let violation =
                    { Record = record
                      Reasons = combinedReasons }

                (nextExpected, violation :: revViolations)

        let _, revViolations = records |> List.fold folder (initialStateKey, [])
        List.rev revViolations

    /// Check cut consistency: when a participant performs a cut point transition,
    /// it must happen from the state where the cut point is annotated in the dual.
    /// This ensures the cross-service boundary is consistent — the sender's state
    /// matches what the receiver expects.
    let checkCutConsistency
        (dualResult: DeriveResult)
        (records: ProvenanceRecord list)
        : DualConformanceViolation list =
        let expectedCutStates = cutPointStates dualResult

        if Map.isEmpty expectedCutStates then
            []
        else
            records
            |> List.choose (fun record ->
                let event = record.Activity.EventName
                let actualState = record.Activity.PreviousState

                match Map.tryFind event expectedCutStates with
                | None -> None // Not a cut point transition
                | Some expectedState ->
                    if actualState = expectedState then
                        None // Cut is consistent
                    else
                        let senderRole = record.ActingRoles |> List.tryHead |> Option.defaultValue "unknown"

                        let reason =
                            DualViolationReason.CutInconsistency(
                                event,
                                senderRole,
                                "external",
                                actualState,
                                expectedState
                            )

                        Some
                            { Record = record
                              Reasons = [ reason ] })

    /// Run all dual conformance checks and produce a combined report.
    let checkDualConformance
        (initialStateKey: string)
        (dualResult: DeriveResult)
        (records: ProvenanceRecord list)
        : DualConformanceReport =
        let sequenceViolations = checkDualSequence initialStateKey dualResult records
        let obligationViolations = checkObligationFulfillment dualResult records
        let cutViolations = checkCutConsistency dualResult records

        { TotalRecords = List.length records
          Violations = sequenceViolations
          ObligationViolations = obligationViolations
          CutViolations = cutViolations }
