namespace Frank.Resources.Model

/// Progress analysis for statechart protocols.
/// Detects deadlocks (no role can advance) and starvation (role permanently excluded).
/// All functions are pure, total, and format-agnostic.
module ProgressAnalysis =

    /// A transition that advances the protocol (Source <> Target).
    /// Self-loops (getGame: XTurn -> XTurn) are observations, not progress.
    /// Assumption: operates on flat extracted statechart where hierarchy is resolved.
    let isAdvancing (t: TransitionSpec) : bool = t.Source <> t.Target

    /// A transition that at least one role can trigger.
    /// RestrictedTo [] = dead transition (no role can fire it).
    let isLive (t: TransitionSpec) : bool =
        match t.Constraint with
        | RestrictedTo [] -> false
        | _ -> true

    type ProgressDiagnostic =
        /// Non-final state where no role has an advancing+live transition. Error severity.
        | Deadlock of state: string * selfLoopEvents: string list
        /// Role permanently excluded on ALL forward paths from a reachable state. Warning severity.
        | Starvation of role: string * excludedAfter: string * excludedStates: string list
        /// Role with zero advancing transitions in any state. Info severity (expected for observers).
        | ReadOnlyRole of role: string

    module ProgressDiagnostic =
        let severity =
            function
            | Deadlock _ -> "error"
            | Starvation _ -> "warning"
            | ReadOnlyRole _ -> "info"

    type ProgressReport =
        { Route: string
          Diagnostics: ProgressDiagnostic list
          HasErrors: bool
          HasWarnings: bool
          StatesAnalyzed: int
          RolesAnalyzed: string list }

    let private isFinal (statechart: ExtractedStatechart) (state: string) : bool =
        statechart.StateMetadata
        |> Map.tryFind state
        |> Option.map (fun si -> si.IsFinal)
        |> Option.defaultValue false

    /// Non-final states where no role has an advancing+live transition out.
    /// Caller is responsible for pruning unreachable states first; see analyzeProgress.
    let detectDeadlocks (statechart: ExtractedStatechart) : ProgressDiagnostic list =
        let transitionsBySource =
            statechart.Transitions |> List.groupBy (fun t -> t.Source) |> Map.ofList

        statechart.StateNames
        |> List.choose (fun state ->
            if isFinal statechart state then
                None
            else
                let transitions = transitionsBySource |> Map.tryFind state |> Option.defaultValue []

                let advancingLive = transitions |> List.filter (fun t -> isAdvancing t && isLive t)

                if List.isEmpty advancingLive then
                    let selfLoopEvents =
                        transitions
                        |> List.filter (fun t -> t.Source = t.Target)
                        |> List.map (fun t -> t.Event)
                        |> List.distinct

                    Some(Deadlock(state, selfLoopEvents))
                else
                    None)

    /// Roles with zero advancing+live transitions — they can only observe, not advance.
    let identifyReadOnlyRoles (projections: Map<string, ExtractedStatechart>) : string list =
        projections
        |> Map.toList
        |> List.choose (fun (role, chart) ->
            let hasAdvancing =
                chart.Transitions |> List.exists (fun t -> isAdvancing t && isLive t)

            if hasAdvancing then None else Some role)

    /// Build adjacency map using only live transitions (dead transitions excluded from BFS).
    /// Harel soundness fix: dead transitions can't be traversed, so they don't count as escape paths.
    let private buildLiveAdjacency (transitions: TransitionSpec list) : Map<string, string list> =
        transitions
        |> List.filter isLive
        |> List.groupBy (fun t -> t.Source)
        |> List.map (fun (k, ts) -> k, ts |> List.map (fun t -> t.Target) |> List.distinct)
        |> Map.ofList

    /// BFS forward reachability from a start state using the given adjacency map.
    let private forwardReachable (adjacency: Map<string, string list>) (start: string) : Set<string> =
        let rec bfs (visited: Set<string>) (frontier: string list) =
            match frontier with
            | [] -> visited
            | state :: rest ->
                if Set.contains state visited then
                    bfs visited rest
                else
                    let visited' = Set.add state visited

                    let neighbors =
                        adjacency
                        |> Map.tryFind state
                        |> Option.defaultValue []
                        |> List.filter (fun s -> not (Set.contains s visited'))

                    bfs visited' (neighbors @ rest)

        bfs Set.empty [ start ]

    /// Roles permanently excluded on ALL forward paths from a non-final reachable state.
    /// Only strong starvation reported: excluded on every path, not just some paths.
    /// Read-only roles are excluded from analysis (they're expected observers).
    let detectStarvation
        (statechart: ExtractedStatechart)
        (projections: Map<string, ExtractedStatechart>)
        (readOnlyRoles: string list)
        : ProgressDiagnostic list =
        let adjacency = buildLiveAdjacency statechart.Transitions
        let readOnlySet = Set.ofList readOnlyRoles

        let nonFinalStates =
            statechart.StateNames |> List.filter (fun s -> not (isFinal statechart s))

        let roleNames =
            projections
            |> Map.keys
            |> Seq.filter (fun r -> not (Set.contains r readOnlySet))
            |> Seq.toList

        roleNames
        |> List.collect (fun role ->
            let roleChart = projections |> Map.tryFind role |> Option.defaultValue statechart

            let activeStates =
                roleChart.Transitions
                |> List.filter (fun t -> isAdvancing t && isLive t)
                |> List.map (fun t -> t.Source)
                |> Set.ofList

            nonFinalStates
            |> List.choose (fun state ->
                if Set.contains state activeStates then
                    None
                else
                    let reachable = forwardReachable adjacency state

                    if Set.intersect reachable activeStates |> Set.isEmpty then
                        let excludedStates =
                            reachable
                            |> Set.toList
                            |> List.filter (fun s ->
                                s <> state && not (isFinal statechart s) && not (Set.contains s activeStates))

                        Some(Starvation(role, state, excludedStates))
                    else
                        None))

    /// Run all progress checks on a statechart.
    /// Prunes unreachable states first to avoid false positives from disconnected fragments.
    let analyzeProgress (statechart: ExtractedStatechart) : ProgressReport =
        let pruned = Projection.pruneUnreachableStates statechart

        let projections =
            statechart.Roles
            |> List.map (fun r -> r.Name, Projection.filterTransitionsByRole r.Name pruned)
            |> Map.ofList

        let readOnlyRoles = identifyReadOnlyRoles projections
        let deadlocks = detectDeadlocks pruned
        let starvation = detectStarvation pruned projections readOnlyRoles

        let readOnlyDiagnostics = readOnlyRoles |> List.map ReadOnlyRole

        let allDiagnostics = deadlocks @ starvation @ readOnlyDiagnostics

        { Route = statechart.RouteTemplate
          Diagnostics = allDiagnostics
          HasErrors = deadlocks |> List.isEmpty |> not
          HasWarnings = starvation |> List.isEmpty |> not
          StatesAnalyzed = pruned.StateNames.Length
          RolesAnalyzed = statechart.Roles |> List.map (fun r -> r.Name) }
