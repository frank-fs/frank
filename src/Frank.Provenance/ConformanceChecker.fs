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
            if Set.contains key ts then None
            else Some(ViolationReason.TransitionNotInProjection role)

    /// Check a single record against pre-built per-role transition sets.
    let private checkRecord
        (transitionSets: Map<string, Set<string * string * string>>)
        (record: ProvenanceRecord)
        : ConformanceViolation option =
        let key =
            (record.Activity.PreviousState, record.Activity.EventName, record.Activity.NewState)

        match record.ActingRoles with
        | [] -> Some { Record = record; Reasons = [ ViolationReason.NoActingRoles ] }
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
