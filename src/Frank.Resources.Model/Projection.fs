namespace Frank.Resources.Model

/// Per-role projection of statecharts.
/// All functions are pure, total (no Result/Option), and format-agnostic.
/// Closed-world strict mode returns Result with explicit error types.
module Projection =

    /// Error types for closed-world (strict) projection.
    /// In closed-world mode, every transition must be explicitly assigned to roles.
    type ProjectionError =
        /// Transition uses Unrestricted constraint — no explicit role assignment.
        | UnmappedTransition of TransitionSpec
        /// Transition uses RestrictedTo [] — no role can trigger it.
        | DeadTransition of TransitionSpec

    module ProjectionError =

        /// Human-readable description of a projection error.
        let describe (error: ProjectionError) : string =
            match error with
            | UnmappedTransition t ->
                $"Unmapped transition '%s{t.Event}' (%s{t.Source} -> %s{t.Target}): no explicit role assignment (Unrestricted)"
            | DeadTransition t ->
                $"Dead transition '%s{t.Event}' (%s{t.Source} -> %s{t.Target}): RestrictedTo [] means no role can trigger it"

    /// Filter transitions to those available to the given role.
    /// Unrestricted transitions survive in all projections.
    /// RestrictedTo transitions survive only if roleName is in the list.
    let filterTransitionsByRole (roleName: string) (statechart: ExtractedStatechart) : ExtractedStatechart =
        let filtered =
            statechart.Transitions
            |> List.filter (fun t ->
                match t.Constraint with
                | Unrestricted -> true
                | RestrictedTo roles -> List.contains roleName roles)

        { statechart with
            Transitions = filtered }

    /// Remove states unreachable from the initial state via surviving transitions.
    /// Initial state is always retained. Updates StateNames, StateMetadata,
    /// and removes transitions referencing pruned states.
    /// Uses a pre-built adjacency map for O(V+E) traversal.
    let pruneUnreachableStates (statechart: ExtractedStatechart) : ExtractedStatechart =
        let adjacency =
            statechart.Transitions
            |> List.groupBy (fun t -> t.Source)
            |> List.map (fun (k, ts) -> k, ts |> List.map (fun t -> t.Target))
            |> Map.ofList

        let rec reachable (visited: Set<string>) (frontier: string list) =
            match frontier with
            | [] -> visited
            | state :: rest ->
                if Set.contains state visited then
                    reachable visited rest
                else
                    let visited' = Set.add state visited

                    let neighbors =
                        adjacency
                        |> Map.tryFind state
                        |> Option.defaultValue []
                        |> List.filter (fun t -> not (Set.contains t visited'))

                    reachable visited' (neighbors @ rest)

        let reachableStates = reachable Set.empty [ statechart.InitialStateKey ]

        { statechart with
            StateNames = statechart.StateNames |> List.filter (fun s -> Set.contains s reachableStates)
            StateMetadata =
                statechart.StateMetadata
                |> Map.filter (fun k _ -> Set.contains k reachableStates)
            Transitions =
                statechart.Transitions
                |> List.filter (fun t -> Set.contains t.Source reachableStates && Set.contains t.Target reachableStates) }

    /// Project a statechart for a single role.
    /// Prune globally first (remove truly disconnected states using all transitions),
    /// then filter transitions by role. In multi-party protocols, state reachability
    /// is global — all participants encounter all globally-reachable states because
    /// other roles advance the state machine.
    let projectForRole (roleName: string) : ExtractedStatechart -> ExtractedStatechart =
        pruneUnreachableStates >> filterTransitionsByRole roleName

    /// Project for all roles defined in the statechart.
    /// Returns empty map if statechart has no roles (no-op).
    /// Prunes unreachable states once, then filters per role.
    let projectAll (statechart: ExtractedStatechart) : Map<string, ExtractedStatechart> =
        let pruned = pruneUnreachableStates statechart

        statechart.Roles
        |> List.map (fun role -> role.Name, filterTransitionsByRole role.Name pruned)
        |> Map.ofList

    /// Closed-world (strict) projection: every transition must be explicitly
    /// assigned to at least one role. Unrestricted transitions are errors
    /// (they must use RestrictedTo with a non-empty role list).
    /// RestrictedTo [] (dead transitions) are also errors.
    /// Returns Ok with projections if all transitions are assigned,
    /// or Error with a list of projection errors.
    /// No-role statecharts return Ok with empty map (nothing to project).
    let projectAllStrict
        (statechart: ExtractedStatechart)
        : Result<Map<string, ExtractedStatechart>, ProjectionError list> =
        if statechart.Roles.IsEmpty then
            Ok Map.empty
        else
            let errors =
                statechart.Transitions
                |> List.choose (fun t ->
                    match t.Constraint with
                    | Unrestricted -> Some(UnmappedTransition t)
                    | RestrictedTo [] -> Some(DeadTransition t)
                    | RestrictedTo _ -> None)

            if errors.IsEmpty then
                Ok(projectAll statechart)
            else
                Error errors

    /// Post-projection completeness check: every global transition must appear
    /// in at least one role's projection. Returns orphaned transitions.
    let findOrphanedTransitions
        (globalChart: ExtractedStatechart)
        (projections: Map<string, ExtractedStatechart>)
        : TransitionSpec list =
        let coveredTransitions =
            projections |> Map.values |> Seq.collect (fun sc -> sc.Transitions) |> Set.ofSeq

        globalChart.Transitions
        |> List.filter (fun t -> not (Set.contains t coveredTransitions))
