module Frank.Statecharts.Analysis.ProjectionValidator

open Frank.Resources.Model

[<RequireQualifiedAccess>]
type Severity =
    | Error
    | Warning

type ProjectionCheckKind =
    | Connectedness
    | MixedChoice
    | Completeness
    | Deadlock

module ProjectionCheckKind =
    let toString =
        function
        | Connectedness -> "connectedness"
        | MixedChoice -> "mixed-choice"
        | Completeness -> "completeness"
        | Deadlock -> "deadlock"

type ProjectionIssue =
    { Check: ProjectionCheckKind
      Severity: Severity
      Message: string }

type ProjectionCheckResult =
    { Issues: ProjectionIssue list
      ChecksRun: int
      ResourceRoute: string }

/// Pre-projection: every transition must be reachable by at least one role.
/// RestrictedTo [] means no role can trigger it; stale role names are flagged.
let checkConnectedness (statechart: ExtractedStatechart) : ProjectionIssue list =
    let knownRoles = statechart.Roles |> List.map _.Name |> Set.ofList

    statechart.Transitions
    |> List.collect (fun t ->
        match t.Constraint with
        | Unrestricted -> []
        | RestrictedTo [] ->
            [ { Check = Connectedness
                Severity = Severity.Error
                Message =
                  $"Transition '%s{t.Event}' (%s{t.Source} -> %s{t.Target}) has no role assignment (RestrictedTo [])" } ]
        | RestrictedTo roles ->
            roles
            |> List.choose (fun role ->
                if Set.contains role knownRoles then
                    None
                else
                    Some
                        { Check = Connectedness
                          Severity = Severity.Error
                          Message =
                            $"Transition '%s{t.Event}' (%s{t.Source} -> %s{t.Target}) references unknown role '%s{role}'" }))

/// Pre-projection: warn when different roles have exclusive transitions from the same state.
/// Only RestrictedTo transitions contribute; Unrestricted transitions are fine.
let checkMixedChoice (statechart: ExtractedStatechart) : ProjectionIssue list =
    statechart.Transitions
    |> List.choose (fun t ->
        match t.Constraint with
        | RestrictedTo roles when not roles.IsEmpty -> Some(t.Source, roles)
        | _ -> None)
    |> List.groupBy fst
    |> List.collect (fun (source, entries) ->
        let distinctRoles = entries |> List.collect snd |> List.distinct

        if distinctRoles.Length > 1 then
            let roleNames = String.concat ", " distinctRoles

            [ { Check = MixedChoice
                Severity = Severity.Warning
                Message = $"State '%s{source}' has restricted transitions from multiple roles: %s{roleNames}" } ]
        else
            [])

/// Post-projection: every pruned global transition must appear in at least one role's projection.
let checkCompleteness
    (projections: Map<string, ExtractedStatechart>)
    (pruned: ExtractedStatechart)
    : ProjectionIssue list =
    let orphans = Projection.findOrphanedTransitions pruned projections

    orphans
    |> List.map (fun t ->
        { Check = Completeness
          Severity = Severity.Error
          Message = $"Transition '%s{t.Event}' (%s{t.Source} -> %s{t.Target}) is not covered by any role's projection" })

/// Post-projection: warn when a reachable non-final state has no outgoing transitions
/// in any role's projection. This checks whether *any* role can advance the state machine
/// (sufficient for HTTP where state is server-side), not per-role progress (which is #108).
let checkDeadlock (projections: Map<string, ExtractedStatechart>) (pruned: ExtractedStatechart) : ProjectionIssue list =
    let isFinal state =
        pruned.StateMetadata
        |> Map.tryFind state
        |> Option.map _.IsFinal
        |> Option.defaultValue false

    let roleOutgoing =
        projections
        |> Map.values
        |> Seq.collect _.Transitions
        |> Seq.map _.Source
        |> Set.ofSeq

    pruned.StateNames
    |> List.choose (fun state ->
        if isFinal state then
            None
        elif Set.contains state roleOutgoing then
            None
        else
            Some
                { Check = Deadlock
                  Severity = Severity.Warning
                  Message = $"State '%s{state}' is reachable but no role has outgoing transitions from it" })

/// Run all 4 projection checks. Returns 0 checks if statechart has no roles.
let validateProjection (resourceRoute: string) (statechart: ExtractedStatechart) : ProjectionCheckResult =
    if statechart.Roles.IsEmpty then
        { Issues = []
          ChecksRun = 0
          ResourceRoute = resourceRoute }
    else
        let projections = Projection.projectAll statechart
        let pruned = Projection.pruneUnreachableStates statechart

        let issues =
            [ yield! checkConnectedness statechart
              yield! checkMixedChoice statechart
              yield! checkCompleteness projections pruned
              yield! checkDeadlock projections pruned ]

        { Issues = issues
          ChecksRun = 4
          ResourceRoute = resourceRoute }
