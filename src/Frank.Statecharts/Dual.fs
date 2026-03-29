/// Session type dual derivation engine.
/// Derives client protocol duals from server statechart + projected ALPS profiles.
/// Implements Wadler's structural duality: the server's projected profile describes
/// what the server offers; the dual describes what the client must/may do.
module Frank.Statecharts.Dual

open Frank.Resources.Model

/// Client obligation classification for a descriptor in a given state.
/// Maps to ALPS duality vocabulary from #124.
type ClientObligation =
    /// The client must select this action to advance the protocol.
    | MustSelect
    /// The client may poll (observe) — no protocol advancement.
    | MayPoll
    /// Terminal state — the session is complete.
    | SessionComplete

/// Duality annotation for a single descriptor in a (role, state) pair.
type DualAnnotation =
    {
        /// Semantic descriptor name (e.g., "makeMove", "viewOrder").
        Descriptor: string
        /// Client obligation classification.
        Obligation: ClientObligation
        /// Whether this transition advances the protocol (source != target).
        AdvancesProtocol: bool
        /// The dual counterpart descriptor (e.g., "#confirmOrder" for submitPayment).
        DualOf: string option
        /// Cross-service composition boundary (e.g., "service-b#acceptOrder").
        CutPoint: string option
    }

/// Result of dual derivation for a statechart.
type DeriveResult =
    {
        /// Per-(role, state) dual annotations.
        Annotations: Map<string * string, DualAnnotation list>
        /// States where no role can advance the protocol (potential deadlocks).
        PotentialDeadlocks: string list
    }

/// Classify a transition's obligation for a specific role in a specific state.
/// Safe self-loop = MayPoll. Unsafe/idempotent with source != target = MustSelect.
let private classifyObligation (isFinalState: bool) (transition: TransitionSpec) : ClientObligation =
    if isFinalState then
        SessionComplete
    else
        let advancesProtocol = transition.Source <> transition.Target

        if advancesProtocol then MustSelect else MayPoll

/// Check whether a transition advances the protocol (source != target).
let private advancesProtocol (transition: TransitionSpec) : bool = transition.Source <> transition.Target

/// Build a DualAnnotation for a transition.
let private buildAnnotation
    (isFinalState: bool)
    (cutPoints: Map<string, string>)
    (dualOfMap: Map<string, string>)
    (transition: TransitionSpec)
    : DualAnnotation =
    { Descriptor = transition.Event
      Obligation = classifyObligation isFinalState transition
      AdvancesProtocol = advancesProtocol transition
      CutPoint = cutPoints |> Map.tryFind transition.Event
      DualOf = dualOfMap |> Map.tryFind transition.Event }

/// Detect potential deadlock states: non-final states where no role's projection
/// has any advancing (source != target) transition.
let private detectDeadlocks
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    : string list =
    let advancingSourceStates =
        projections
        |> Map.values
        |> Seq.collect (fun sc -> sc.Transitions)
        |> Seq.filter (fun t -> t.Source <> t.Target)
        |> Seq.map (fun t -> t.Source)
        |> Set.ofSeq

    statechart.StateNames
    |> List.filter (fun state ->
        let isFinal =
            statechart.StateMetadata
            |> Map.tryFind state
            |> Option.map (fun info -> info.IsFinal)
            |> Option.defaultValue false

        not isFinal && not (Set.contains state advancingSourceStates))

/// Derive client protocol duals from server statechart + projected ALPS profiles.
/// For each role R and each reachable state S, classifies each descriptor as
/// MustSelect (advances protocol), MayPoll (self-loop/safe), or SessionComplete (final).
let deriveCore
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (cutPoints: Map<string, string>)
    (dualOfMap: Map<string, string>)
    : DeriveResult =
    if statechart.Roles.IsEmpty then
        { Annotations = Map.empty
          PotentialDeadlocks = [] }
    else
        let annotations =
            projections
            |> Map.toList
            |> List.collect (fun (roleName, roleProjection) ->
                statechart.StateNames
                |> List.map (fun state ->
                    let isFinal =
                        statechart.StateMetadata
                        |> Map.tryFind state
                        |> Option.map (fun info -> info.IsFinal)
                        |> Option.defaultValue false

                    let transitionsFromState =
                        roleProjection.Transitions |> List.filter (fun t -> t.Source = state)

                    let annotations =
                        if isFinal then
                            // For final states, annotate all transitions as SessionComplete
                            // and if no transitions exist, add a synthetic one
                            let transAnnotations =
                                transitionsFromState |> List.map (buildAnnotation true cutPoints dualOfMap)

                            if transAnnotations.IsEmpty then
                                // Final state with no visible transitions: emit SessionComplete marker
                                [ { Descriptor = ""
                                    Obligation = SessionComplete
                                    AdvancesProtocol = false
                                    CutPoint = None
                                    DualOf = None } ]
                            else
                                transAnnotations
                        else
                            transitionsFromState |> List.map (buildAnnotation false cutPoints dualOfMap)

                    (roleName, state), annotations))
            |> Map.ofList

        let deadlocks = detectDeadlocks statechart projections

        { Annotations = annotations
          PotentialDeadlocks = deadlocks }

/// Derive client protocol duals (simple version — no cut points or dualOf annotations).
let derive (statechart: ExtractedStatechart) (projections: Map<string, ExtractedStatechart>) : DeriveResult =
    deriveCore statechart projections Map.empty Map.empty

/// Derive client protocol duals with cut point annotations.
let deriveWithCutPoints
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (cutPoints: Map<string, string>)
    : DeriveResult =
    deriveCore statechart projections cutPoints Map.empty

/// Derive client protocol duals with dualOf annotations.
let deriveWithDualOf
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (dualOfMap: Map<string, string>)
    : DeriveResult =
    deriveCore statechart projections Map.empty dualOfMap
