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

    /// Non-final states where no role has an advancing+live transition out.
    let detectDeadlocks (statechart: ExtractedStatechart) : ProgressDiagnostic list =
        let isFinal state =
            statechart.StateMetadata
            |> Map.tryFind state
            |> Option.map (fun si -> si.IsFinal)
            |> Option.defaultValue false

        let transitionsBySource =
            statechart.Transitions
            |> List.groupBy (fun t -> t.Source)
            |> Map.ofList

        statechart.StateNames
        |> List.choose (fun state ->
            if isFinal state then
                None
            else
                let transitions =
                    transitionsBySource
                    |> Map.tryFind state
                    |> Option.defaultValue []

                let advancingLive =
                    transitions |> List.filter (fun t -> isAdvancing t && isLive t)

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
                chart.Transitions
                |> List.exists (fun t -> isAdvancing t && isLive t)

            if hasAdvancing then None else Some role)
