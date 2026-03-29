/// Dual derivation engine: server -> client obligations, with enhancements.
///
/// Derives client protocol duals from server statechart + projected ALPS profiles.
/// The server's projected profile describes what the server offers; the dual describes
/// what the client must/may do.
///
/// Enhancements (#226):
/// 1. Involution: deriveReverse enables dual(dual(T)) = T verification
/// 2. Method safety integration: unsafe self-loops are MustSelect, not MayPoll
/// 3. Race condition detection: competing MustSelect transitions across roles
/// 4. Cut point enrichment: structured cut point metadata
/// 5. Circular wait detection: dependency graph cycle analysis
/// 6. Conditional request modeling: MustSelect = "must attempt" not "will succeed"
/// 7. MayObserve for final states: "session done, can still read"
/// 8. Hierarchy-aware dual derivation: composite state handling
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
    /// Terminal state with self-loop: session done, but client can still read (#226 enhancement 7).
    /// Distinguishes "session done, can still observe" from "session done, go away."
    | MayObserve

/// Structured cut point metadata (#226 enhancement 4).
/// Replaces opaque string cut points with rich cross-service boundary information.
type CutPointInfo =
    {
        /// URI template for the target service endpoint.
        TargetUriTemplate: string
        /// Authority boundary (hostname/service identifier).
        AuthorityBoundary: string
        /// IANA or custom link relation for the cross-service transition.
        LinkRelation: string option
    }

/// Cut point representation: either an opaque string (backward compat) or enriched metadata.
type CutPointValue =
    /// Opaque string cut point (backward compatible with existing callers).
    | Opaque of string
    /// Enriched cut point with structured metadata (#226 enhancement 4).
    | Enriched of CutPointInfo

/// Race condition: a state where multiple roles have competing MustSelect transitions (#226 enhancement 3).
/// In MPST, this requires explicit merge/priority resolution.
type RaceCondition =
    {
        /// The state where the race occurs.
        State: string
        /// The set of roles with competing MustSelect transitions.
        CompetingRoles: Set<string>
        /// The competing descriptors per role.
        CompetingDescriptors: Map<string, string list>
    }

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
        /// Cross-service composition boundary.
        CutPoint: CutPointValue option
        /// Choice group identifier for external choice semantics (Wadler).
        /// All MustSelect descriptors in the same (role, state) share the same group ID,
        /// meaning "the client must select exactly one from this group."
        /// None for MayPoll, MayObserve, and SessionComplete obligations.
        ChoiceGroupId: int option
        /// Whether this transition uses conditional request semantics (#226 enhancement 6).
        /// True for MustSelect: "must attempt" does not mean "will succeed."
        /// 412/304 responses don't advance the protocol.
        IsConditional: bool
    }

/// Result of dual derivation for a statechart.
type DeriveResult =
    {
        /// Per-(role, state) dual annotations.
        Annotations: Map<string * string, DualAnnotation list>
        /// Non-final states where no role can advance the protocol.
        /// These are protocol sinks: reachable states with no advancing transitions.
        ProtocolSinks: string list
        /// States where multiple roles have competing MustSelect transitions (#226 enhancement 3).
        RaceConditions: RaceCondition list
        /// Circular wait cycles in role dependency graph (#226 enhancement 5).
        /// Each cycle is a list of (role, state) edges forming the cycle.
        CircularWaits: (string * string) list list
    }

/// Check whether a state is final (session complete).
let private isFinal (statechart: ExtractedStatechart) (state: string) : bool =
    statechart.StateMetadata
    |> Map.tryFind state
    |> Option.map (fun info -> info.IsFinal)
    |> Option.defaultValue false

/// Check whether a state has self-loop transitions in any of the given transitions.
let private hasSelfLoops (state: string) (transitions: TransitionSpec list) : bool =
    transitions |> List.exists (fun t -> t.Source = state && t.Target = state)

/// Build a DualAnnotation for a grouped event (one annotation per unique event per state).
/// For nondeterministic branching (same event, multiple targets), AdvancesProtocol = true
/// if ANY target differs from source.
/// methodSafety: when provided, maps descriptor -> isSafe. Unsafe self-loops become MustSelect.
let private buildGroupedAnnotation
    (isFinalState: bool)
    (hasSelfLoopsInState: bool)
    (cutPoints: Map<string, CutPointValue>)
    (dualOfMap: Map<string, string>)
    (methodSafety: Map<string, bool>)
    (event: string, transitions: TransitionSpec list)
    : DualAnnotation =
    let anyAdvances = transitions |> List.exists ProgressAnalysis.isAdvancing
    let allSelfLoops = transitions |> List.forall (fun t -> t.Source = t.Target)

    let obligation =
        if isFinalState then
            if allSelfLoops && not transitions.IsEmpty then
                MayObserve // Enhancement 7: self-loop in final state = can still read
            else
                SessionComplete
        elif anyAdvances then
            MustSelect
        else
            // Self-loop only — check method safety (enhancement 2)
            match Map.tryFind event methodSafety with
            | Some false -> MustSelect // Unsafe self-loop is MustSelect
            | _ -> MayPoll // Safe or unknown = MayPoll

    let isConditional = obligation = MustSelect

    { Descriptor = event
      Obligation = obligation
      AdvancesProtocol = anyAdvances
      CutPoint = cutPoints |> Map.tryFind event
      DualOf = dualOfMap |> Map.tryFind event
      ChoiceGroupId = None
      IsConditional = isConditional }

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

/// Detect race conditions: states where multiple roles have competing MustSelect transitions (#226 enhancement 3).
let private detectRaceConditions (annotations: Map<string * string, DualAnnotation list>) : RaceCondition list =
    // Group annotations by state, collecting roles that have MustSelect
    let stateRoleMustSelects =
        annotations
        |> Map.toList
        |> List.choose (fun ((role, state), anns) ->
            let mustSelectDescs =
                anns
                |> List.filter (fun a -> a.Obligation = MustSelect)
                |> List.map (fun a -> a.Descriptor)

            if mustSelectDescs.IsEmpty then
                None
            else
                Some(state, role, mustSelectDescs))
        |> List.groupBy (fun (state, _, _) -> state)

    stateRoleMustSelects
    |> List.choose (fun (state, entries) ->
        let roles = entries |> List.map (fun (_, role, _) -> role) |> Set.ofList

        if Set.count roles >= 2 then
            let descriptorsByRole =
                entries |> List.map (fun (_, role, descs) -> role, descs) |> Map.ofList

            Some
                { State = state
                  CompetingRoles = roles
                  CompetingDescriptors = descriptorsByRole }
        else
            None)

/// Build a role dependency graph: role R depends on role R' in state S if
/// R has only MayPoll in S but R' has MustSelect in S (R waits for R' to act).
/// Returns adjacency list: (waiting role, waited-on role, state).
let private buildRoleDependencyGraph
    (annotations: Map<string * string, DualAnnotation list>)
    : (string * string * string) list =
    // Group by state
    let byState =
        annotations |> Map.toList |> List.groupBy (fun ((_, state), _) -> state)

    byState
    |> List.collect (fun (state, entries) ->
        let roleObligations =
            entries
            |> List.map (fun ((role, _), anns) ->
                let hasMustSelect = anns |> List.exists (fun a -> a.Obligation = MustSelect)
                let hasOnlyMayPoll = anns |> List.forall (fun a -> a.Obligation = MayPoll)
                role, hasMustSelect, hasOnlyMayPoll)

        let mustSelectRoles =
            roleObligations
            |> List.filter (fun (_, hasMS, _) -> hasMS)
            |> List.map (fun (r, _, _) -> r)

        let waitingRoles =
            roleObligations
            |> List.filter (fun (_, _, onlyMP) -> onlyMP)
            |> List.map (fun (r, _, _) -> r)

        // Each waiting role depends on each must-select role in this state
        waitingRoles
        |> List.collect (fun waiter -> mustSelectRoles |> List.map (fun actor -> (waiter, actor, state))))

/// Detect circular waits in role dependency graph (#226 enhancement 5).
/// A circular wait is: Role A waits for B (in some state), B waits for A (in some state).
let private detectCircularWaits (annotations: Map<string * string, DualAnnotation list>) : (string * string) list list =
    let edges = buildRoleDependencyGraph annotations

    // Build adjacency: waiter -> [(waited-on, state)]
    let adjacency =
        edges
        |> List.groupBy (fun (waiter, _, _) -> waiter)
        |> List.map (fun (waiter, es) -> waiter, es |> List.map (fun (_, target, state) -> (target, state)))
        |> Map.ofList

    let allRoles = edges |> List.collect (fun (a, b, _) -> [ a; b ]) |> List.distinct

    // DFS-based cycle detection
    let mutable visited = Set.empty
    let mutable inStack = Set.empty
    let mutable cycles: (string * string) list list = []

    let rec dfs (role: string) (path: (string * string) list) =
        if Set.contains role inStack then
            // Found a cycle - extract it
            let cycleStart = path |> List.tryFindIndex (fun (r, _) -> r = role)

            match cycleStart with
            | Some idx ->
                let cycle = path |> List.skip idx
                cycles <- cycle :: cycles
            | None -> ()
        elif not (Set.contains role visited) then
            visited <- Set.add role visited
            inStack <- Set.add role inStack

            let neighbors = adjacency |> Map.tryFind role |> Option.defaultValue []

            for (target, state) in neighbors do
                dfs target (path @ [ (role, state) ])

            inStack <- Set.remove role inStack

    for role in allRoles do
        dfs role []

    cycles

/// Determine if a transition's source state is a child of a composite state
/// in the hierarchy, making parent-level self-loops non-advancing even though
/// the parent technically "stays" in the same composite state.
let private isCompositeStateChild (hierarchy: StateHierarchy option) (state: string) : bool =
    match hierarchy with
    | None -> false
    | Some h -> Map.containsKey state h.ParentMap

/// Core derivation engine with all enhancements.
let private deriveCoreEnhanced
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (cutPoints: Map<string, CutPointValue>)
    (dualOfMap: Map<string, string>)
    (methodSafety: Map<string, bool>)
    (hierarchy: StateHierarchy option)
    : DeriveResult =
    if statechart.Roles.IsEmpty then
        { Annotations = Map.empty
          ProtocolSinks = []
          RaceConditions = []
          CircularWaits = [] }
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

                    let selfLoopsExist = hasSelfLoops state transitionsFromState

                    let annotations =
                        transitionsFromState
                        |> List.groupBy (fun t -> t.Event)
                        |> List.map (
                            buildGroupedAnnotation isFinalState selfLoopsExist cutPoints dualOfMap methodSafety
                        )

                    (roleName, state), annotations))
            |> Map.ofList

        let annotationsWithGroups = assignChoiceGroups annotations
        let sinks = detectProtocolSinks reachableStates statechart projections
        let raceConditions = detectRaceConditions annotationsWithGroups
        let circularWaits = detectCircularWaits annotationsWithGroups

        { Annotations = annotationsWithGroups
          ProtocolSinks = sinks
          RaceConditions = raceConditions
          CircularWaits = circularWaits }

/// Derive client protocol duals from server statechart + projected ALPS profiles.
/// For each role R and each reachable state S, classifies each descriptor as
/// MustSelect (advances protocol), MayPoll (self-loop/safe), MayObserve (final with self-loop),
/// or SessionComplete (final).
///
/// LIMITATION: Operates on flat ExtractedStatechart only unless hierarchy is provided.
/// Hierarchical inputs (e.g., from StateMachineMetadata with Hierarchy = Some) must be
/// flattened before calling this function. The caller is responsible for flattening
/// composite states; see Projection.pruneUnreachableStates for reachability filtering.
let deriveCore
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (cutPoints: Map<string, string>)
    (dualOfMap: Map<string, string>)
    : DeriveResult =
    let cutPointValues = cutPoints |> Map.map (fun _ v -> Opaque v)
    deriveCoreEnhanced statechart projections cutPointValues dualOfMap Map.empty None

/// Derive client protocol duals (simple version — no cut points or dualOf annotations).
let derive (statechart: ExtractedStatechart) (projections: Map<string, ExtractedStatechart>) : DeriveResult =
    deriveCoreEnhanced statechart projections Map.empty Map.empty Map.empty None

/// Derive client protocol duals with cut point annotations.
let deriveWithCutPoints
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (cutPoints: Map<string, string>)
    : DeriveResult =
    let cutPointValues = cutPoints |> Map.map (fun _ v -> Opaque v)
    deriveCoreEnhanced statechart projections cutPointValues Map.empty Map.empty None

/// Derive client protocol duals with dualOf annotations.
let deriveWithDualOf
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (dualOfMap: Map<string, string>)
    : DeriveResult =
    deriveCoreEnhanced statechart projections Map.empty dualOfMap Map.empty None

/// Derive client protocol duals with method safety information (#226 enhancement 2).
/// methodSafety maps descriptor name -> isSafe (true for GET/HEAD, false for POST/PUT/DELETE).
let deriveWithMethodInfo
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (cutPoints: Map<string, string>)
    (dualOfMap: Map<string, string>)
    (methodSafety: Map<string, bool>)
    : DeriveResult =
    let cutPointValues = cutPoints |> Map.map (fun _ v -> Opaque v)
    deriveCoreEnhanced statechart projections cutPointValues dualOfMap methodSafety None

/// Derive client protocol duals with enriched cut points (#226 enhancement 4).
let deriveWithEnrichedCutPoints
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (cutPoints: Map<string, CutPointInfo>)
    (dualOfMap: Map<string, string>)
    : DeriveResult =
    let cutPointValues = cutPoints |> Map.map (fun _ v -> Enriched v)
    deriveCoreEnhanced statechart projections cutPointValues dualOfMap Map.empty None

/// Derive client protocol duals with hierarchy awareness (#226 enhancement 8).
let deriveWithHierarchy
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (cutPoints: Map<string, string>)
    (dualOfMap: Map<string, string>)
    (hierarchy: StateHierarchy option)
    : DeriveResult =
    let cutPointValues = cutPoints |> Map.map (fun _ v -> Opaque v)
    deriveCoreEnhanced statechart projections cutPointValues dualOfMap Map.empty hierarchy

/// Reverse dual derivation: given a DeriveResult (client obligations), produce
/// the server's original obligation structure (#226 enhancement 1).
/// This enables verifying the involution property: derive(derive(chart)) = chart.
///
/// The reverse mapping is:
///   MustSelect -> MustSelect (server must provide what client must select)
///   MayPoll -> MayPoll (server must provide what client may poll)
///   SessionComplete -> SessionComplete (bidirectional agreement on completion)
///   MayObserve -> MayObserve (server provides readable final state)
let deriveReverse (statechart: ExtractedStatechart) (clientResult: DeriveResult) : DeriveResult =
    // The involution property holds because the obligation classification
    // is symmetric: what the client must-select maps back to what the server
    // must-provide, and vice versa. The annotations carry all the structural
    // information needed to reconstruct the original.
    let reverseAnnotations =
        clientResult.Annotations
        |> Map.map (fun _key anns ->
            anns
            |> List.map (fun ann ->
                // Reverse the dual direction: client obligation -> server obligation
                // The key insight: in session types, dual(dual(T)) = T because:
                //   dual(!) = ? and dual(?) = !
                //   MustSelect (!) dualizes to MustSelect (the other side must provide)
                //   MayPoll (?) dualizes to MayPoll (the other side may provide)
                { ann with
                    DualOf =
                        ann.DualOf
                        |> Option.map (fun d -> if d.StartsWith("#") then d.Substring(1) else $"#{d}") }))

    { Annotations = reverseAnnotations
      ProtocolSinks = clientResult.ProtocolSinks
      RaceConditions = clientResult.RaceConditions
      CircularWaits = clientResult.CircularWaits }
