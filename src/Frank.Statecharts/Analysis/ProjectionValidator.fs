module Frank.Statecharts.Analysis.ProjectionValidator

open System
open Frank.Resources.Model
open Frank.Validation

[<RequireQualifiedAccess>]
type Severity =
    | Error
    | Warning

type ProjectionCheckKind =
    | Connectedness
    | MixedChoice
    | Completeness
    | Deadlock
    | Livelock
    | GuardConsistency
    | ShapeReference
    | ClosedWorldTotality

module ProjectionCheckKind =
    let toString =
        function
        | Connectedness -> "connectedness"
        | MixedChoice -> "mixed-choice"
        | Completeness -> "completeness"
        | Deadlock -> "deadlock"
        | Livelock -> "livelock"
        | GuardConsistency -> "guard-consistency"
        | ShapeReference -> "shape-reference"
        | ClosedWorldTotality -> "closed-world-totality"

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

let private isFinal (statechart: ExtractedStatechart) (state: string) =
    statechart.StateMetadata
    |> Map.tryFind state
    |> Option.map _.IsFinal
    |> Option.defaultValue false

/// Post-projection: warn when a reachable non-final state has no outgoing transitions
/// in any role's projection. This checks whether *any* role can advance the state machine
/// (sufficient for HTTP where state is server-side), not per-role progress (which is #108).
let checkDeadlock (projections: Map<string, ExtractedStatechart>) (pruned: ExtractedStatechart) : ProjectionIssue list =
    let roleOutgoing =
        projections
        |> Map.values
        |> Seq.collect _.Transitions
        |> Seq.map _.Source
        |> Set.ofSeq

    pruned.StateNames
    |> List.choose (fun state ->
        if isFinal pruned state then
            None
        elif Set.contains state roleOutgoing then
            None
        else
            Some
                { Check = Deadlock
                  Severity = Severity.Warning
                  Message = $"State '%s{state}' is reachable but no role has outgoing transitions from it" })

/// Post-projection: warn when a reachable non-final state has outgoing transitions
/// but ALL of them are self-loops (source == target). The system is active but cannot
/// make progress — a livelock.
let checkLivelock (projections: Map<string, ExtractedStatechart>) (pruned: ExtractedStatechart) : ProjectionIssue list =
    let transitionsBySource =
        projections
        |> Map.values
        |> Seq.collect _.Transitions
        |> Seq.toList
        |> List.groupBy _.Source
        |> Map.ofList

    pruned.StateNames
    |> List.choose (fun state ->
        if isFinal pruned state then
            None
        else
            let outgoing = transitionsBySource |> Map.tryFind state |> Option.defaultValue []

            if outgoing.IsEmpty then
                None // No outgoing transitions = deadlock, not livelock
            elif outgoing |> List.forall (fun t -> t.Target = t.Source) then
                Some
                    { Check = Livelock
                      Severity = Severity.Warning
                      Message =
                        $"State '%s{state}' has only self-loop transitions across all role projections — no role can advance out of it" }
            else
                None)

/// Guard consistency: compare guards referenced by transitions against declared GuardNames.
/// Guards in transitions but not in GuardNames are errors (undeclared guard).
/// Guards in GuardNames but not referenced by any transition are warnings (unused guard).
let checkGuardConsistency (statechart: ExtractedStatechart) : ProjectionIssue list =
    let declaredGuards = statechart.GuardNames |> Set.ofList

    let referencedGuards = statechart.Transitions |> List.choose _.Guard |> Set.ofList

    let undeclared =
        Set.difference referencedGuards declaredGuards
        |> Set.toList
        |> List.map (fun guard ->
            { Check = GuardConsistency
              Severity = Severity.Error
              Message = $"Guard '%s{guard}' is referenced by a transition but not declared in GuardNames" })

    let unused =
        Set.difference declaredGuards referencedGuards
        |> Set.toList
        |> List.map (fun guard ->
            { Check = GuardConsistency
              Severity = Severity.Warning
              Message = $"Guard '%s{guard}' is declared in GuardNames but not referenced by any transition" })

    undeclared @ unused

/// SHACL shape cross-reference: compare ALPS def URIs against ShapeCache entries.
/// Orphaned shape refs (def URI with no shape) and unreferenced shapes (shape with no def) are warnings.
/// Uses string comparison on absolute URIs since System.Uri does not implement IComparable.
let checkShapeReference (alpsDefUris: Uri list) (shapeCache: ShapeCache) : ProjectionIssue list =
    let shapeKeys = shapeCache.Keys |> Seq.toList

    let defUriStrings =
        alpsDefUris |> List.map (fun (u: Uri) -> u.AbsoluteUri) |> Set.ofList

    let shapeUriStrings =
        shapeKeys |> List.map (fun (u: Uri) -> u.AbsoluteUri) |> Set.ofList

    let defUriByString =
        alpsDefUris |> List.map (fun u -> u.AbsoluteUri, u) |> Map.ofList

    let shapeUriByString =
        shapeKeys |> List.map (fun u -> u.AbsoluteUri, u) |> Map.ofList

    let orphaned =
        Set.difference defUriStrings shapeUriStrings
        |> Set.toList
        |> List.map (fun uriStr ->
            let uri = defUriByString[uriStr]

            { Check = ShapeReference
              Severity = Severity.Warning
              Message = $"ALPS def URI '%O{uri}' has no corresponding entry in ShapeCache" })

    let unreferenced =
        Set.difference shapeUriStrings defUriStrings
        |> Set.toList
        |> List.map (fun uriStr ->
            let uri = shapeUriByString[uriStr]

            { Check = ShapeReference
              Severity = Severity.Warning
              Message = $"ShapeCache entry '%O{uri}' is not referenced by any ALPS def URI" })

    orphaned @ unreferenced

/// Whether the statechart has any guard-related content worth checking.
let private hasGuards (statechart: ExtractedStatechart) =
    not statechart.GuardNames.IsEmpty
    || statechart.Transitions |> List.exists (fun t -> t.Guard.IsSome)

/// Closed-world totality: every transition must be explicitly assigned to at least one role.
/// Unrestricted transitions are errors (must use RestrictedTo with non-empty role list).
/// RestrictedTo [] (dead transitions) are also errors.
let checkClosedWorldTotality (statechart: ExtractedStatechart) : ProjectionIssue list =
    statechart.Transitions
    |> List.choose (fun t ->
        match t.Constraint with
        | Unrestricted ->
            Some
                { Check = ClosedWorldTotality
                  Severity = Severity.Error
                  Message =
                    $"Transition '%s{t.Event}' (%s{t.Source} -> %s{t.Target}) has no explicit role assignment (Unrestricted)" }
        | RestrictedTo [] ->
            Some
                { Check = ClosedWorldTotality
                  Severity = Severity.Error
                  Message =
                    $"Transition '%s{t.Event}' (%s{t.Source} -> %s{t.Target}) has empty role assignment (RestrictedTo [])" }
        | RestrictedTo _ -> None)

/// Run projection checks. Returns 0 checks if statechart has no roles and no guards.
/// GuardConsistency runs on all statecharts with guards; the 5 role-based checks require roles.
let validateProjection (resourceRoute: string) (statechart: ExtractedStatechart) : ProjectionCheckResult =
    let guardIssues, guardChecks =
        if hasGuards statechart then
            checkGuardConsistency statechart, 1
        else
            [], 0

    if statechart.Roles.IsEmpty then
        { Issues = guardIssues
          ChecksRun = guardChecks
          ResourceRoute = resourceRoute }
    else
        let projections = Projection.projectAll statechart
        let pruned = Projection.pruneUnreachableStates statechart

        let issues =
            [ yield! checkConnectedness statechart
              yield! checkMixedChoice statechart
              yield! checkCompleteness projections pruned
              yield! checkDeadlock projections pruned
              yield! checkLivelock projections pruned
              yield! guardIssues ]

        { Issues = issues
          ChecksRun = 5 + guardChecks
          ResourceRoute = resourceRoute }

/// Run projection checks in strict (closed-world) mode.
/// Includes all checks from validateProjection plus ClosedWorldTotality.
/// In strict mode, Unrestricted transitions and RestrictedTo [] are errors.
/// Returns 0 checks if statechart has no roles and no guards.
let validateProjectionStrict (resourceRoute: string) (statechart: ExtractedStatechart) : ProjectionCheckResult =
    let guardIssues, guardChecks =
        if hasGuards statechart then
            checkGuardConsistency statechart, 1
        else
            [], 0

    if statechart.Roles.IsEmpty then
        { Issues = guardIssues
          ChecksRun = guardChecks
          ResourceRoute = resourceRoute }
    else
        let projections = Projection.projectAll statechart
        let pruned = Projection.pruneUnreachableStates statechart

        let issues =
            [ yield! checkConnectedness statechart
              yield! checkMixedChoice statechart
              yield! checkCompleteness projections pruned
              yield! checkDeadlock projections pruned
              yield! checkClosedWorldTotality statechart
              yield! guardIssues ]

        { Issues = issues
          ChecksRun = 5 + guardChecks
          ResourceRoute = resourceRoute }
