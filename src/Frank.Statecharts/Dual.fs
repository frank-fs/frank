/// One-directional dual derivation engine: server -> client obligations.
///
/// Derives client protocol duals from server statechart + projected ALPS profiles.
/// The server's projected profile describes what the server offers; the dual describes
/// what the client must/may do.
///
/// IMPORTANT: This is NOT a full session-type duality with involution (dual(dual(T)) = T).
/// It is a one-directional derivation: given a server statechart, produce the client's
/// obligation classification per (role, state, descriptor). The naming "Dual" is domain
/// shorthand following Wadler's structural duality vocabulary, but the implementation
/// only computes server -> client obligations, not bidirectional session types.
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
        /// Choice group identifier for external choice semantics (Wadler).
        /// All MustSelect descriptors in the same (role, state) share the same group ID,
        /// meaning "the client must select exactly one from this group."
        /// None for MayPoll and SessionComplete obligations.
        ChoiceGroupId: int option
    }

/// Result of dual derivation for a statechart.
type DeriveResult =
    {
        /// Per-(role, state) dual annotations.
        Annotations: Map<string * string, DualAnnotation list>
        /// Non-final states where no role can advance the protocol.
        /// These are protocol sinks: reachable states with no advancing transitions.
        ProtocolSinks: string list
    }

/// Check whether a state is final (session complete).
let private isFinal (statechart: ExtractedStatechart) (state: string) : bool =
    statechart.StateMetadata
    |> Map.tryFind state
    |> Option.map (fun info -> info.IsFinal)
    |> Option.defaultValue false

/// Build a DualAnnotation for a grouped event (one annotation per unique event per state).
/// For nondeterministic branching (same event, multiple targets), AdvancesProtocol = true
/// if ANY target differs from source.
let private buildGroupedAnnotation
    (isFinalState: bool)
    (cutPoints: Map<string, string>)
    (dualOfMap: Map<string, string>)
    (event: string, transitions: TransitionSpec list)
    : DualAnnotation =
    let anyAdvances = transitions |> List.exists ProgressAnalysis.isAdvancing

    let obligation =
        if isFinalState then SessionComplete
        elif anyAdvances then MustSelect
        else MayPoll

    { Descriptor = event
      Obligation = obligation
      AdvancesProtocol = anyAdvances
      CutPoint = cutPoints |> Map.tryFind event
      DualOf = dualOfMap |> Map.tryFind event
      ChoiceGroupId = None }

/// Assign ChoiceGroupId to MustSelect annotations that share the same (role, state).
/// Uses a mutable counter to assign globally unique group IDs.
let private assignChoiceGroups (annotations: Map<string * string, DualAnnotation list>) =
    let mutable nextGroupId = 0

    annotations
    |> Map.map (fun _key anns ->
        let hasMustSelect = anns |> List.exists (fun a -> a.Obligation = MustSelect)

        if hasMustSelect then
            let groupId = nextGroupId
            nextGroupId <- nextGroupId + 1

            anns
            |> List.map (fun a ->
                if a.Obligation = MustSelect then
                    { a with ChoiceGroupId = Some groupId }
                else
                    a)
        else
            anns)

/// Detect protocol sink states: non-final states where no role's projection
/// has any advancing (source != target) transition.
let private detectProtocolSinks
    (reachableStates: Set<string>)
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    : string list =
    let advancingSourceStates =
        projections
        |> Map.values
        |> Seq.collect (fun sc -> sc.Transitions)
        |> Seq.filter ProgressAnalysis.isAdvancing
        |> Seq.map (fun t -> t.Source)
        |> Set.ofSeq

    statechart.StateNames
    |> List.filter (fun state ->
        Set.contains state reachableStates
        && not (isFinal statechart state)
        && not (Set.contains state advancingSourceStates))

/// Derive client protocol duals from server statechart + projected ALPS profiles.
/// For each role R and each reachable state S, classifies each descriptor as
/// MustSelect (advances protocol), MayPoll (self-loop/safe), or SessionComplete (final).
///
/// LIMITATION: Operates on flat ExtractedStatechart only. Hierarchical inputs
/// (e.g., from StateMachineMetadata with Hierarchy = Some) must be flattened before
/// calling this function. The caller is responsible for flattening composite states;
/// see Projection.pruneUnreachableStates for reachability filtering.
let deriveCore
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (cutPoints: Map<string, string>)
    (dualOfMap: Map<string, string>)
    : DeriveResult =
    if statechart.Roles.IsEmpty then
        { Annotations = Map.empty
          ProtocolSinks = [] }
    else
        let reachableStates =
            (Projection.pruneUnreachableStates statechart).StateNames |> Set.ofList

        let annotations =
            projections
            |> Map.toList
            |> List.collect (fun (roleName, roleProjection) ->
                statechart.StateNames
                |> List.filter (fun s -> Set.contains s reachableStates)
                |> List.map (fun state ->
                    let isFinalState = isFinal statechart state

                    let transitionsFromState =
                        roleProjection.Transitions |> List.filter (fun t -> t.Source = state)

                    let annotations =
                        if isFinalState then
                            // For final states, group by event and annotate as SessionComplete.
                            // If no transitions exist, return empty list — consumers check isFinal.
                            transitionsFromState
                            |> List.groupBy (fun t -> t.Event)
                            |> List.map (buildGroupedAnnotation true cutPoints dualOfMap)
                        else
                            transitionsFromState
                            |> List.groupBy (fun t -> t.Event)
                            |> List.map (buildGroupedAnnotation false cutPoints dualOfMap)

                    (roleName, state), annotations))
            |> Map.ofList

        let annotationsWithGroups = assignChoiceGroups annotations
        let sinks = detectProtocolSinks reachableStates statechart projections

        { Annotations = annotationsWithGroups
          ProtocolSinks = sinks }

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
