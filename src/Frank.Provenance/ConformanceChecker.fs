namespace Frank.Provenance

open Frank.Resources.Model

/// Post-hoc conformance checking of provenance records against per-role projected profiles.
[<RequireQualifiedAccess>]
module ConformanceChecker =

    /// Classify why a role failed conformance for a given transition.
    let private classifyRoleFailure
        (transitionSets: Map<string, Set<string * string * string>>)
        (key: string * string * string)
        (role: string)
        : ViolationReason option =
        match Map.tryFind role transitionSets with
        | None -> Some(ViolationReason.RoleNotInProjection role)
        | Some ts ->
            if Set.contains key ts then
                None
            else
                Some(ViolationReason.TransitionNotInProjection role)

    /// Check a single record against pre-built per-role transition sets.
    let private checkRecord
        (transitionSets: Map<string, Set<string * string * string>>)
        (record: ProvenanceRecord)
        : ConformanceViolation option =
        let key =
            (record.Activity.PreviousState, record.Activity.EventName, record.Activity.NewState)

        match record.ActingRoles with
        | [] ->
            Some
                { Record = record
                  Reasons = [ ViolationReason.NoActingRoles ] }
        | roles ->
            let reasons = roles |> List.choose (classifyRoleFailure transitionSets key)

            if List.length reasons = List.length roles then
                Some { Record = record; Reasons = reasons }
            else
                None

    /// Verify provenance records against per-role projected profiles.
    /// A transition is conformant if at least one acting role's projection includes it.
    let checkConformance
        (projections: Map<string, ExtractedStatechart>)
        (records: ProvenanceRecord list)
        : ConformanceReport =
        let transitionSets =
            projections
            |> Map.map (fun _ proj ->
                proj.Transitions
                |> List.map (fun t -> (t.Source, t.Event, t.Target))
                |> Set.ofList)

        let violations = records |> List.choose (checkRecord transitionSets)

        { TotalRecords = List.length records
          Violations = violations }

    /// Verify provenance records against per-role projected profiles AND validate
    /// that the sequence of transitions follows a valid path from the initial state.
    /// Combines per-transition role conformance with sequence continuity checking:
    /// the first transition's source must match initialStateKey, and each subsequent
    /// transition's source must match the previous transition's target.
    let checkSequenceConformance
        (initialStateKey: string)
        (projections: Map<string, ExtractedStatechart>)
        (records: ProvenanceRecord list)
        : ConformanceReport =
        let transitionSets =
            projections
            |> Map.map (fun _ proj ->
                proj.Transitions
                |> List.map (fun t -> (t.Source, t.Event, t.Target))
                |> Set.ofList)

        let folder (expectedState: string, violations: ConformanceViolation list) (record: ProvenanceRecord) =
            let actualSource = record.Activity.PreviousState

            let sequenceReason =
                if actualSource <> expectedState then
                    [ ViolationReason.StateSequenceViolation(expectedState, actualSource) ]
                else
                    []

            let roleViolation = checkRecord transitionSets record

            let combinedReasons =
                match roleViolation with
                | Some rv -> sequenceReason @ rv.Reasons
                | None -> sequenceReason

            let nextExpected = record.Activity.NewState

            if List.isEmpty combinedReasons then
                (nextExpected, violations)
            else
                let violation =
                    { Record = record
                      Reasons = combinedReasons }

                (nextExpected, violations @ [ violation ])

        let _, violations = records |> List.fold folder (initialStateKey, [])

        { TotalRecords = List.length records
          Violations = violations }
