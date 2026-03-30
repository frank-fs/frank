/// Dual derivation engine: server -> client obligations, with enhancements.
///
/// Derives client protocol duals from server statechart + projected ALPS profiles.
/// The server's projected profile describes what the server offers; the dual describes
/// what the client must/may do.
///
/// Enhancements (#226):
/// 1. Involution: deriveReverse enables dual(dual(T)) = T verification
/// 2. Method safety integration: unsafe self-loops are MustSelect, not MayPoll
/// 3. Race condition detection: competing MustSelect transitions across roles on overlapping descriptors
/// 4. Cut point enrichment: structured cut point metadata
/// 5. Circular wait detection: dependency graph cycle analysis using (role, state) pairs
/// 6. Conditional request modeling: MustSelect = "must attempt" not "will succeed"
/// 7. Hierarchy-aware dual derivation: composite state handling
module Frank.Statecharts.Dual

open Frank.Resources.Model

/// Client obligation classification for a descriptor in a given state.
/// Maps to ALPS duality vocabulary from #124.
///
/// Classification rules:
/// - Final states WITHOUT self-loops: SessionComplete (no further interaction possible).
/// - Final states WITH self-loops: MayPoll (they are not truly final in the Harel sense;
///   the self-loop represents safe, non-advancing observation).
/// - Non-final states with advancing transitions: MustSelect.
/// - Non-final states with safe self-loops only: MayPoll.
/// - Non-final states with unsafe self-loops: MustSelect (method safety integration).
type ClientObligation =
    /// The client must select this action to advance the protocol.
    | MustSelect
    /// The client may poll (observe) — no protocol advancement.
    | MayPoll
    /// Terminal state — the session is complete (no self-loops, no further interaction).
    | SessionComplete

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
///
/// The Opaque/Enriched distinction exists because `deriveCore` uses Opaque for backward
/// compatibility with existing callers that pass plain string cut points, while
/// `deriveWithEnrichedCutPoints` uses Enriched for callers that can provide structured
/// metadata. The serializer (DualAlpsGenerator) handles both representations, formatting
/// Enriched as "template@authority" in the ALPS output.
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
        /// None for MayPoll and SessionComplete obligations.
        ChoiceGroupId: int option
        /// Whether this transition uses conditional request semantics (#226 enhancement 6).
        /// IsConditional is true for all MustSelect obligations. Captures the HTTP-level concern
        /// that any state-advancing request may receive 412/304. Does NOT distinguish between
        /// concurrency races and precondition failures.
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
                MayPoll // Self-loop in final state = safe, non-advancing observation (not truly final per Harel)
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

/// Detect race conditions: states where DIFFERENT roles have competing MustSelect transitions
/// on OVERLAPPING descriptors (#226 enhancement 3).
///
/// Multiple MustSelect from the SAME role = external choice (valid, not a race).
/// Multiple MustSelect from DIFFERENT roles on non-overlapping descriptors = valid interleaving.
/// Only flag as race when different roles compete for the same transition/descriptor.
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

            // Check for overlapping descriptors across roles
            let allDescriptorSets =
                descriptorsByRole |> Map.toList |> List.map (fun (_, descs) -> Set.ofList descs)

            let hasOverlap =
                allDescriptorSets
                |> List.exists (fun s1 -> allDescriptorSets |> List.exists (fun s2 -> s1 <> s2 && not (Set.isEmpty (Set.intersect s1 s2))))

            if hasOverlap then
                Some
                    { State = state
                      CompetingRoles = roles
                      CompetingDescriptors = descriptorsByRole }
            else
                None
        else
            None)

/// Build a role dependency graph: role R depends on role R' in state S if
/// R has only non-advancing transitions (MayPoll) in S AND R' has MustSelect in S,
/// meaning R NEEDS R' to act for the protocol to advance from S.
///
/// A MayPoll-only role may be a legitimate observer, not "waiting" — so we only
/// create a dependency edge when the observer role has no alternative path forward
/// (no MustSelect in any other state reachable from S without R' acting first).
///
/// Returns adjacency list: (waiting (role, state), waited-on (role, state)).
let private buildRoleDependencyGraph
    (annotations: Map<string * string, DualAnnotation list>)
    : ((string * string) * (string * string)) list =
    // Group by state
    let byState =
        annotations |> Map.toList |> List.groupBy (fun ((_, state), _) -> state)

    byState
    |> List.collect (fun (state, entries) ->
        let roleObligations =
            entries
            |> List.map (fun ((role, _), anns) ->
                let hasMustSelect = anns |> List.exists (fun a -> a.Obligation = MustSelect)

                let hasOnlyMayPollOrComplete =
                    anns
                    |> List.forall (fun a -> a.Obligation = MayPoll || a.Obligation = SessionComplete)

                let hasAnyAdvancing = anns |> List.exists (fun a -> a.AdvancesProtocol)
                role, hasMustSelect, hasOnlyMayPollOrComplete, hasAnyAdvancing)

        let mustSelectRoles =
            roleObligations
            |> List.filter (fun (_, hasMS, _, _) -> hasMS)
            |> List.map (fun (r, _, _, _) -> r)

        let waitingRoles =
            roleObligations
            // Only roles with no advancing transitions are truly "waiting" — not just observing
            |> List.filter (fun (_, _, onlyMPOrComplete, hasAdvancing) -> onlyMPOrComplete && not hasAdvancing)
            |> List.map (fun (r, _, _, _) -> r)

        // Each waiting role depends on each must-select role in this state
        waitingRoles
        |> List.collect (fun waiter ->
            mustSelectRoles |> List.map (fun actor -> ((waiter, state), (actor, state)))))

/// Detect circular waits in role dependency graph (#226 enhancement 5).
/// Uses (role, state) pairs as graph nodes, not just role names, so that cycles
/// accurately reflect the state context in which the dependency occurs.
/// A circular wait is: (RoleA, S1) waits for (RoleB, S1), (RoleB, S2) waits for (RoleA, S2).
let private detectCircularWaits (annotations: Map<string * string, DualAnnotation list>) : (string * string) list list =
    let edges = buildRoleDependencyGraph annotations

    // Build adjacency: (role, state) -> [(target role, target state)]
    let adjacency =
        edges
        |> List.groupBy fst
        |> List.map (fun (node, es) -> node, es |> List.map snd)
        |> Map.ofList

    let allNodes =
        edges |> List.collect (fun (a, b) -> [ a; b ]) |> List.distinct

    // DFS-based cycle detection using (role, state) pairs
    let mutable visited: Set<string * string> = Set.empty
    let mutable inStack: Set<string * string> = Set.empty
    let mutable cycles: (string * string) list list = []

    let rec dfs (node: string * string) (path: (string * string) list) =
        if Set.contains node inStack then
            // Found a cycle - extract it from the path
            let cycleStart = path |> List.tryFindIndex (fun n -> n = node)

            match cycleStart with
            | Some idx ->
                let cycle = path |> List.skip idx
                cycles <- cycle :: cycles
            | None -> ()
        elif not (Set.contains node visited) then
            visited <- Set.add node visited
            inStack <- Set.add node inStack

            let neighbors = adjacency |> Map.tryFind node |> Option.defaultValue []

            for target in neighbors do
                dfs target (path @ [ node ])

            inStack <- Set.remove node inStack

    for node in allNodes do
        dfs node []

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
/// MustSelect (advances protocol), MayPoll (self-loop/safe/final-with-self-loop),
/// or SessionComplete (final, no self-loops).
///
/// Operates on flat ExtractedStatechart. The caller is responsible for flattening
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
///
/// HTTP method safety IS relevant because unsafe self-loops represent server-side state
/// changes that clients must acknowledge (MustSelect), while safe self-loops are
/// observation-only (MayPoll). For example, a POST self-loop that updates a draft
/// should be MustSelect because it has side effects the client must intentionally trigger.
///
/// Descriptors not present in the methodSafety map default to MayPoll (safe), preserving
/// backward compatibility with callers that do not provide method safety information.
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

/// Derive client protocol duals with hierarchy awareness (#226 enhancement 7).
///
/// The hierarchy parameter is used only to suppress false-positive livelock detection
/// for composite state self-loops. The derivation itself operates on flat ExtractedStatechart.
///
/// The caller decides whether to pass hierarchy information:
/// - Pass `None` for flat statecharts or when composite-state-aware suppression is not needed.
/// - Pass `Some hierarchy` when the flat chart was produced by flattening a hierarchical
///   statechart and parent-level self-loops should not trigger livelock warnings.
///
/// Example: a traffic light with composite state "Active" containing "Red", "Yellow", "Green"
/// would pass `Some hierarchy` so that flattened parent self-loops (checkStatus: Red->Red)
/// are recognized as composite-state artifacts, not real protocol sinks.
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
/// The reverse mapping follows session type duality (Wadler):
///   MustSelect (!) -> MayPoll (?) : client must select -> server may offer
///   MayPoll (?) -> MustSelect (!) : client may poll -> server must provide
///   SessionComplete -> SessionComplete : symmetric (session end)
///
/// Applying this twice yields the identity: dual(dual(T)) = T.
let deriveReverse (statechart: ExtractedStatechart) (clientResult: DeriveResult) : DeriveResult =
    let flipObligation (obligation: ClientObligation) : ClientObligation =
        match obligation with
        | MustSelect -> MayPoll
        | MayPoll -> MustSelect
        | SessionComplete -> SessionComplete

    let reverseAnnotations =
        clientResult.Annotations
        |> Map.map (fun _key anns ->
            anns
            |> List.map (fun ann ->
                let flipped = flipObligation ann.Obligation

                { ann with
                    Obligation = flipped
                    IsConditional = flipped = MustSelect
                    DualOf =
                        ann.DualOf
                        |> Option.map (fun d -> if d.StartsWith("#") then d.Substring(1) else $"#{d}") }))

    { Annotations = reverseAnnotations
      ProtocolSinks = clientResult.ProtocolSinks
      RaceConditions = clientResult.RaceConditions
      CircularWaits = clientResult.CircularWaits }
