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
///
/// ==========================================================================
/// MPST FORMALISM BOUNDS (issue #244)
/// ==========================================================================
///
/// This module implements dual derivation as a documented approximation of the
/// full Multiparty Session Types (MPST) formalism. The following limitations
/// are explicit design choices, not implementation bugs:
///
/// 1. AND-STATE DUAL DERIVATION GAP
///    AND-state (parallel composition) is modeled in the runtime hierarchy
///    (see Hierarchy.fs, CompositeKind.AND) but is NOT handled by dual derivation.
///    Synchronization barriers — the requirement that the client must engage ALL
///    parallel branches before an AND-state may exit — are silently dropped.
///    When deriveWithHierarchy receives a hierarchy containing AND-state composites,
///    it emits a DeriveResult.Warnings entry and proceeds with flat-FSM semantics.
///    Full AND-state dual derivation would require a tensor product T1 ⊗ T2 over
///    the parallel regions, which is not implemented. See item 3 (no composition
///    operator) below.
///
/// 2. SEQUENTIAL DUALITY APPROXIMATION
///    Per-(role, state) snapshot dualization is correct for flat FSMs but is an
///    approximation for sequenced session types with continuations (T.S notation
///    from Wadler's "Propositions as Sessions"). The dual of a full protocol tree
///    is not computed: this engine takes a per-state snapshot and classifies each
///    descriptor as MustSelect/MayPoll/SessionComplete. The full continuation
///    structure (what comes AFTER each selection) is not reflected in the dual.
///    This is sufficient for ALPS projection and HTTP affordance generation — the
///    use cases Frank targets — but is not a complete session-type dual in the
///    Wadler/Honda sense.
///
/// 3. NO PROTOCOL COMPOSITION OPERATOR
///    There is no tensor product (T1 ⊗ T2), parallel composition, or cut rule for
///    merging DeriveResult values. CutPointInfo annotates service boundaries but
///    does not compose protocol types. This means that cross-service protocols
///    (e.g., order → payment → fulfillment) cannot be composed into a single typed
///    session. Future work: implement a `compose` function that takes two DeriveResult
///    values and a cut point map and returns a merged DeriveResult with protocol
///    continuations linked across service boundaries.
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
        /// Mutual obligation cycles in the role dependency graph (#226 enhancement 5,
        /// #253 cross-state edges).
        ///
        /// Each cycle is a list of (role, state) nodes forming a closed dependency chain.
        /// A cycle means: role A depends on role B in one state, and role B depends on
        /// role A in another state (or transitively through other roles/states).
        ///
        /// Formalism caveat (Harel): In a flat FSM, the system occupies one state at a
        /// time — these cycles represent mutual obligation chains, not Coffman deadlocks
        /// (which require simultaneous waiting via AND-state parallel composition).
        /// Treat as a liveness diagnostic: cycles indicate tightly coupled role obligations
        /// that could indicate problematic protocol design.
        ///
        /// Duality note (Wadler): CircularWaits does NOT satisfy the involution property.
        /// AdvancesProtocol is structural (not flipped by deriveReverse), so the waiter
        /// classification differs between forward and reversed derivations. The analysis
        /// assumes cooperative liveness — roles eventually exercise all advancing capabilities.
        CircularWaits: (string * string) list list
        /// Formalism-bound warnings: conditions where the derivation is a known approximation
        /// rather than the full MPST dual. See the module-level MPST FORMALISM BOUNDS comment
        /// for the complete list of documented limitations.
        ///
        /// Current warning sources:
        /// - AND-state parallel composition: hierarchy contains AND-state composites whose
        ///   synchronization barriers are not modeled (see formalism bound 1 above).
        Warnings: string list
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

            // Check if any two DIFFERENT roles share a descriptor
            let rolePairs =
                let roleDescs = descriptorsByRole |> Map.toList

                [ for i in 0 .. roleDescs.Length - 2 do
                      for j in i + 1 .. roleDescs.Length - 1 do
                          yield snd roleDescs.[i], snd roleDescs.[j] ]

            let hasOverlap =
                rolePairs
                |> List.exists (fun (descs1, descs2) ->
                    not (Set.isEmpty (Set.intersect (Set.ofList descs1) (Set.ofList descs2))))

            if hasOverlap then
                Some
                    { State = state
                      CompetingRoles = roles
                      CompetingDescriptors = descriptorsByRole }
            else
                None
        else
            None)

/// Build a map from (role, sourceState) to the target states of that role's advancing
/// transitions. Uses role projections (not the raw statechart) so the transition target
/// map is consistent with the obligation classification that drives waiter/actor roles.
///
/// Formalism note: guarded transitions are treated as always-fireable. This is a sound
/// over-approximation (may report cycles that guards prevent) but cannot detect stuck
/// states where guards block the only exit. Guard satisfiability requires runtime
/// evaluation, which is outside the scope of static dual analysis.
let private buildRoleTransitionTargets
    (projections: Map<string, ExtractedStatechart>)
    : Map<string * string, string list> =
    projections
    |> Map.toList
    |> List.collect (fun (roleName, proj) ->
        proj.Transitions
        |> List.filter (fun t -> t.Source <> t.Target)
        |> List.groupBy (fun t -> t.Source)
        |> List.map (fun (source, ts) ->
            (roleName, source), ts |> List.map (fun t -> t.Target) |> List.distinct))
    |> Map.ofList

/// Classify roles in each state as waiters (MayPoll-only, no advancing) or actors (MustSelect).
/// Returns (state, waitingRoles, mustSelectRoles) triples.
let private classifyRolesByState
    (annotations: Map<string * string, DualAnnotation list>)
    : (string * string list * string list) list =
    let byState =
        annotations |> Map.toList |> List.groupBy (fun ((_, state), _) -> state)

    byState
    |> List.map (fun (state, entries) ->
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
            |> List.filter (fun (_, _, onlyMPOrComplete, hasAdvancing) -> onlyMPOrComplete && not hasAdvancing)
            |> List.map (fun (r, _, _, _) -> r)

        state, waitingRoles, mustSelectRoles)

/// Build intra-state dependency edges: (waiter, S) → (actor, S).
/// Each waiting role depends on each must-select role in the same state.
/// These edges alone form a DAG (a role cannot be both waiter and actor in one state).
let private buildIntraStateEdges
    (classifications: (string * string list * string list) list)
    : ((string * string) * (string * string)) list =
    classifications
    |> List.collect (fun (state, waitingRoles, mustSelectRoles) ->
        waitingRoles
        |> List.collect (fun waiter ->
            mustSelectRoles |> List.map (fun actor -> ((waiter, state), (actor, state)))))

/// Build cross-state dependency edges: (waiter, S) → (actor, T) where actor transitions
/// from S to T. These edges capture the obligation chain across states — "waiter is
/// blocked in S until actor advances to T, where obligations may reverse." Without
/// cross-state edges, the dependency graph is a DAG by construction and no cycles can
/// ever be detected (issue #253).
let private buildCrossStateEdges
    (classifications: (string * string list * string list) list)
    (roleTransitionTargets: Map<string * string, string list>)
    : ((string * string) * (string * string)) list =
    classifications
    |> List.collect (fun (state, waitingRoles, mustSelectRoles) ->
        waitingRoles
        |> List.collect (fun waiter ->
            mustSelectRoles
            |> List.collect (fun actor ->
                let targets =
                    roleTransitionTargets
                    |> Map.tryFind (actor, state)
                    |> Option.defaultValue []

                targets |> List.map (fun target -> ((waiter, state), (actor, target))))))

/// Build the full role dependency graph by composing intra-state and cross-state edges.
/// Returns adjacency list: (waiting (role, state), waited-on (role, state)).
let private buildRoleDependencyGraph
    (annotations: Map<string * string, DualAnnotation list>)
    (roleTransitionTargets: Map<string * string, string list>)
    : ((string * string) * (string * string)) list =
    let classifications = classifyRolesByState annotations
    buildIntraStateEdges classifications @ buildCrossStateEdges classifications roleTransitionTargets

/// Detect circular waits in role dependency graph (#226 enhancement 5, #253 cross-state).
/// Uses (role, state) pairs as graph nodes, not just role names, so that cycles
/// accurately reflect the state context in which the dependency occurs.
/// A circular wait is: (RoleA, S1) waits for (RoleB, S2), (RoleB, S2) waits for (RoleA, S1).
let private detectCircularWaits
    (annotations: Map<string * string, DualAnnotation list>)
    (roleTransitionTargets: Map<string * string, string list>)
    : (string * string) list list =
    let edges = buildRoleDependencyGraph annotations roleTransitionTargets

    // Build adjacency: (role, state) -> [(target role, target state)]
    let adjacency =
        edges
        |> List.groupBy fst
        |> List.map (fun (node, es) -> node, es |> List.map snd |> List.distinct)
        |> Map.ofList

    let allNodes = edges |> List.collect (fun (a, b) -> [ a; b ]) |> List.distinct

    // DFS-based cycle detection using (role, state) pairs
    let mutable visited: Set<string * string> = Set.empty
    let mutable inStack: Set<string * string> = Set.empty
    let mutable cycles: (string * string) list list = []

    let rec dfs (node: string * string) (path: (string * string) list) =
        if Set.contains node inStack then
            // Found a cycle - extract it from the path (stored in reverse order).
            // Mark the cycle endpoint as visited so it is not re-entered from
            // a different DFS root, preventing duplicate cycle reporting.
            visited <- Set.add node visited

            let forwardPath = List.rev path
            let cycleStart = forwardPath |> List.tryFindIndex (fun n -> n = node)

            match cycleStart with
            | Some idx ->
                let cycle = forwardPath |> List.skip idx
                cycles <- cycle :: cycles
            | None -> ()
        elif not (Set.contains node visited) then
            visited <- Set.add node visited
            inStack <- Set.add node inStack

            let neighbors = adjacency |> Map.tryFind node |> Option.defaultValue []

            for target in neighbors do
                dfs target (node :: path)

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

/// Detect AND-state composites in the hierarchy and produce formalism-bound warnings.
///
/// AND-state dual derivation gap (issue #244, formalism bound 1):
/// AND-state parallel composition is not handled by dual derivation.
/// Synchronization barriers — the requirement that the client must engage ALL parallel
/// branches before the AND-state exits — are silently dropped when the hierarchy
/// contains AND-state composites. This function produces a warning for each AND-state
/// found so that callers can surface the approximation rather than assuming full MPST
/// duality has been computed.
///
/// The derivation proceeds with flat-FSM semantics regardless; this function only
/// produces warnings, it does not halt or change the derivation output.
let private detectAndStateWarnings (hierarchy: StateHierarchy option) : string list =
    match hierarchy with
    | None -> []
    | Some h ->
        h.StateKind
        |> Map.toList
        |> List.choose (fun (stateId, kind) ->
            match kind with
            | CompositeKind.AND ->
                Some
                    $"AND-state dual derivation gap: composite state '{stateId}' uses AND (parallel) composition. \
Synchronization barriers across parallel regions are not modeled by dual derivation. \
The dual for '{stateId}' is computed using flat-FSM approximation only (formalism bound 1, issue #244). \
Full tensor-product dual is not supported."
            | _ -> None)

/// Core derivation engine with all enhancements.
let private deriveCoreEnhanced
    (statechart: ExtractedStatechart)
    (projections: Map<string, ExtractedStatechart>)
    (cutPoints: Map<string, CutPointValue>)
    (dualOfMap: Map<string, string>)
    (methodSafety: Map<string, bool>)
    (hierarchy: StateHierarchy option)
    : DeriveResult =
    // AND-state warning check (formalism bound 1): surface approximation before deriving.
    let warnings = detectAndStateWarnings hierarchy

    if statechart.Roles.IsEmpty then
        { Annotations = Map.empty
          ProtocolSinks = []
          RaceConditions = []
          CircularWaits = []
          Warnings = warnings }
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

        let roleTransitionTargets = buildRoleTransitionTargets projections
        let circularWaits = detectCircularWaits annotationsWithGroups roleTransitionTargets

        { Annotations = annotationsWithGroups
          ProtocolSinks = sinks
          RaceConditions = raceConditions
          CircularWaits = circularWaits
          Warnings = warnings }

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
///
/// AND-state formalism bound (issue #244):
/// If the hierarchy contains AND-state (parallel) composites, the returned DeriveResult
/// will include Warnings entries describing the approximation. Synchronization barriers
/// across AND-state parallel regions are NOT modeled — the derivation proceeds with
/// flat-FSM semantics. Callers SHOULD inspect DeriveResult.Warnings when passing a
/// hierarchy that may contain AND composites.
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
///
/// RaceConditions and CircularWaits are re-derived from the reversed annotations
/// rather than passed through unchanged — obligations change under reversal, so
/// any race or wait structure may change too. ProtocolSinks is re-derived from
/// the statechart's structural projections, which are obligation-independent.
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

    // Re-derive RaceConditions, CircularWaits, and ProtocolSinks from reversed annotations.
    // These depend on obligations (which role has MustSelect), so they must be
    // re-computed rather than carried from the forward result.
    let reverseRaceConditions = detectRaceConditions reverseAnnotations

    // Projections are needed for both circular wait targets and protocol sink detection.
    // Structural targets don't change under obligation reversal — only who's a waiter/actor changes.
    let projections =
        if statechart.Roles.IsEmpty then Map.empty
        else Projection.projectAll statechart

    let roleTransitionTargets = buildRoleTransitionTargets projections
    let reverseCircularWaits = detectCircularWaits reverseAnnotations roleTransitionTargets

    // Re-derive ProtocolSinks from the statechart's structural projections.
    // ProtocolSinks is obligation-independent (determined by transition graph structure),
    // so the result is the same as the forward derivation, but we re-derive for correctness.
    let reachableStates =
        if statechart.Roles.IsEmpty then
            Set.empty
        else
            (Projection.pruneUnreachableStates statechart).StateNames |> Set.ofList

    let reverseSinks =
        if statechart.Roles.IsEmpty then
            []
        else
            detectProtocolSinks reachableStates statechart projections

    { Annotations = reverseAnnotations
      ProtocolSinks = reverseSinks
      RaceConditions = reverseRaceConditions
      CircularWaits = reverseCircularWaits
      Warnings = clientResult.Warnings }
