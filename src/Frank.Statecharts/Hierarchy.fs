namespace Frank.Statecharts

// ==========================================================================
// Hierarchical statechart runtime (issue #136)
//
// Pure-function implementation of hierarchical state semantics:
// - XOR (exclusive) and AND (parallel) composite states
// - LCA-based entry/exit ordering (SCXML-compliant)
// - Shallow and deep history pseudo-states
// - HTTP method resolution with parent fallback
//
// Opt-in: flat FSMs remain unaffected. Hierarchical dispatch activates
// only when StateMachineMetadata.Hierarchy = Some _. Use the `useHierarchyWith`
// CE operation on statefulResource to set this field.
// ==========================================================================

/// Composite state kind: XOR (exclusive, one child active) or AND (parallel, all children active).
///
/// AND-state dual derivation gap (issue #244):
/// AND-state parallel composition is modeled in this runtime (HierarchicalRuntime.enterState,
/// HierarchicalRuntime.transition) but is NOT handled by dual derivation in Frank.Statecharts.Dual.
/// Synchronization barriers — the requirement that the client must engage ALL parallel regions
/// before the AND-state exits — are silently dropped by the dual derivation engine.
/// When Dual.deriveWithHierarchy is called with a hierarchy containing AND composites, it emits
/// DeriveResult.Warnings entries and proceeds with flat-FSM approximation semantics.
/// Full AND-state dual derivation would require tensor-product composition (T1 ⊗ T2) across
/// parallel regions, which is not implemented (see Dual.fs module comment, formalism bound 1).
type CompositeKind =
    | XOR
    | AND

/// Specification for a single composite state, used as input to StateHierarchy.build.
type CompositeStateSpec =
    {
        Id: string
        Kind: CompositeKind
        Children: string list
        InitialChild: string option
        /// For AND composites: the target state key to transition to when all regions complete.
        /// None for XOR composites and AND composites without auto-completion.
        CompletionTarget: string option
    }

/// Input specification for building a StateHierarchy.
type HierarchySpec = { States: CompositeStateSpec list }

/// Pre-computed immutable data structure for hierarchical state operations.
/// Built once from the hierarchy spec; used for all runtime queries.
type StateHierarchy =
    {
        /// child -> parent mapping
        ParentMap: Map<string, string>
        /// parent -> ordered children mapping
        ChildrenMap: Map<string, string list>
        /// composite state -> initial child mapping
        InitialChild: Map<string, string>
        /// composite state -> XOR or AND
        StateKind: Map<string, CompositeKind>
        /// Pre-computed LCA for all (source, target) pairs in the statechart
        LcaCache: Map<string * string, string>
        /// Pre-computed depth (distance from root) for every state
        DepthMap: Map<string, int>
        /// Pre-computed all descendants (recursive) for every composite state
        DescendantMap: Map<string, string list>
        /// AND composite state -> completion target state key
        /// Populated from CompositeStateSpec.CompletionTarget for AND-kind specs.
        CompletionTargets: Map<string, string>
    }

/// The set of currently active state IDs in a hierarchical statechart.
/// Wraps Set<string> for type safety and discoverability.
type ActiveStateConfiguration = private { ActiveStates: Set<string> }

/// History of active state configurations, keyed by composite state ID.
/// Used to restore previous configurations when re-entering via history pseudo-states.
type HistoryRecord =
    private
        { Entries: Map<string, ActiveStateConfiguration> }

/// Result of a hierarchical state transition, including the new configuration
/// and the ordered lists of exited/entered states for entry/exit actions.
type HierarchicalTransitionResult =
    {
        /// The resulting active state configuration after the transition.
        Configuration: ActiveStateConfiguration
        /// States exited in order (source up to but not including LCA).
        ExitedStates: string list
        /// States entered in order (LCA down to target).
        EnteredStates: string list
        /// Updated history record (records exited composite state configurations).
        HistoryRecord: HistoryRecord
    }

/// Hierarchy-level operations routed through the statechart middleware.
/// Allows handlers to trigger AND-state region completion and history recovery
/// without bypassing middleware guard evaluation.
type HierarchyOp =
    /// Complete an AND-state region by transitioning from activeState to doneState.
    /// The middleware will auto-detect completion and fire the completion target transition.
    | CompleteRegion of activeState: string * doneState: string
    /// Re-enter a composite state using shallow or deep history pseudo-state semantics.
    | RecoverHistory of compositeId: string * kind: Frank.Statecharts.Ast.HistoryKind

/// The RuntimeInterpreter's representation type — a state transformer
/// that threads configuration and history through a sequence of operations,
/// accumulating exited and entered state lists.
type RuntimeStep =
    ActiveStateConfiguration * HistoryRecord
        -> ActiveStateConfiguration * HistoryRecord * string list * string list

// ==========================================================================
// ActiveStateConfiguration module
// ==========================================================================

[<RequireQualifiedAccess>]
module ActiveStateConfiguration =

    let empty: ActiveStateConfiguration = { ActiveStates = Set.empty }

    let add (stateId: string) (config: ActiveStateConfiguration) : ActiveStateConfiguration =
        { ActiveStates = Set.add stateId config.ActiveStates }

    let remove (stateId: string) (config: ActiveStateConfiguration) : ActiveStateConfiguration =
        { ActiveStates = Set.remove stateId config.ActiveStates }

    let isActive (stateId: string) (config: ActiveStateConfiguration) : bool =
        Set.contains stateId config.ActiveStates

    let toSet (config: ActiveStateConfiguration) : Set<string> = config.ActiveStates

    let isEmpty (config: ActiveStateConfiguration) : bool = Set.isEmpty config.ActiveStates

    let ofSet (states: Set<string>) : ActiveStateConfiguration = { ActiveStates = states }

// ==========================================================================
// HistoryRecord module
// ==========================================================================

[<RequireQualifiedAccess>]
module HistoryRecord =

    let empty: HistoryRecord = { Entries = Map.empty }

    let record (compositeStateId: string) (config: ActiveStateConfiguration) (history: HistoryRecord) : HistoryRecord =
        { Entries = Map.add compositeStateId config history.Entries }

    let tryGet (compositeStateId: string) (history: HistoryRecord) : ActiveStateConfiguration option =
        Map.tryFind compositeStateId history.Entries

    let toMap (history: HistoryRecord) : Map<string, ActiveStateConfiguration> = history.Entries

    let ofMap (entries: Map<string, ActiveStateConfiguration>) : HistoryRecord = { Entries = entries }

// ==========================================================================
// StateHierarchy module
// ==========================================================================

[<RequireQualifiedAccess>]
module StateHierarchy =

    /// Build a StateHierarchy from a HierarchySpec.
    /// Pre-computes LCA cache for all (source, target) state pairs and depth map.
    let build (spec: HierarchySpec) : StateHierarchy =
        let parentMap =
            spec.States
            |> List.collect (fun s -> s.Children |> List.map (fun child -> (child, s.Id)))
            |> Map.ofList

        let childrenMap =
            spec.States |> List.map (fun s -> (s.Id, s.Children)) |> Map.ofList

        let initialChild =
            spec.States
            |> List.choose (fun s ->
                match s.InitialChild with
                | Some initial -> Some(s.Id, initial)
                | None -> None)
            |> Map.ofList

        let stateKind = spec.States |> List.map (fun s -> (s.Id, s.Kind)) |> Map.ofList

        // Collect all known state IDs (composite parents + their children)
        let allStateIds =
            spec.States |> List.collect (fun s -> s.Id :: s.Children) |> List.distinct

        // Pre-compute depth for every state
        let computeDepthFromParentMap stateId =
            let rec loop current d =
                match Map.tryFind current parentMap with
                | Some parent -> loop parent (d + 1)
                | None -> d

            loop stateId 0

        let depthMap =
            allStateIds
            |> List.map (fun s -> (s, computeDepthFromParentMap s))
            |> Map.ofList

        // Pre-compute ancestry (root-to-leaf) for LCA computation
        let ancestryFromParentMap stateId =
            let rec loop current acc =
                let acc = current :: acc

                match Map.tryFind current parentMap with
                | Some parent -> loop parent acc
                | None -> acc

            loop stateId []

        // Pre-compute LCA for all (source, target) pairs
        let lcaCache =
            allStateIds
            |> List.collect (fun a ->
                allStateIds
                |> List.choose (fun b ->
                    let ancestryA = ancestryFromParentMap a
                    let ancestryBSet = ancestryFromParentMap b |> Set.ofList

                    let rec findLCA candidates lastCommon =
                        match candidates with
                        | [] -> lastCommon
                        | x :: rest ->
                            if Set.contains x ancestryBSet then
                                findLCA rest (Some x)
                            else
                                lastCommon

                    match findLCA ancestryA None with
                    | Some lca -> Some((a, b), lca)
                    | None -> None))
            |> Map.ofList

        // Pre-compute all descendants for every composite state
        let descendantMap =
            spec.States
            |> List.map (fun s ->
                let rec collectDescendants stateId =
                    match Map.tryFind stateId childrenMap with
                    | Some children -> children |> List.collect (fun c -> c :: collectDescendants c)
                    | None -> []

                (s.Id, collectDescendants s.Id))
            |> Map.ofList

        let completionTargets =
            spec.States
            |> List.choose (fun s ->
                match s.Kind, s.CompletionTarget with
                | CompositeKind.AND, Some target -> Some(s.Id, target)
                | _ -> None)
            |> Map.ofList

        { ParentMap = parentMap
          ChildrenMap = childrenMap
          InitialChild = initialChild
          StateKind = stateKind
          LcaCache = lcaCache
          DepthMap = depthMap
          DescendantMap = descendantMap
          CompletionTargets = completionTargets }

    /// Compute the ancestry path from a state up to the root (inclusive).
    /// Returns root-to-leaf order: root first, then its child, down to the state itself.
    /// Example: ancestry("Red") = ["Root"; "Active"; "Red"]
    let private ancestry (hierarchy: StateHierarchy) (stateId: string) : string list =
        let rec loop current acc =
            let acc = current :: acc

            match Map.tryFind current hierarchy.ParentMap with
            | Some parent -> loop parent acc
            | None -> acc

        loop stateId []

    /// Compute the Least Common Ancestor of two states.
    /// Uses the pre-computed LCA cache when available, falls back to ancestry traversal.
    /// Returns None if the states share no common ancestor (disconnected hierarchies).
    let computeLCA (hierarchy: StateHierarchy) (stateA: string) (stateB: string) : string option =
        match Map.tryFind (stateA, stateB) hierarchy.LcaCache with
        | Some lca -> Some lca
        | None ->
            // Fallback for states not in the pre-computed cache
            let ancestryA = ancestry hierarchy stateA
            let ancestryB = ancestry hierarchy stateB |> Set.ofList

            let rec findLCA candidates lastCommon =
                match candidates with
                | [] -> lastCommon
                | x :: rest ->
                    if Set.contains x ancestryB then
                        findLCA rest (Some x)
                    else
                        lastCommon

            findLCA ancestryA None

    /// Walk from source up to (but not including) lca via ParentMap.
    /// Returns source-first order: [source; parent; ...; child-of-lca].
    /// Internal: used by HierarchicalRuntime and TransitionProgram.
    /// Promote to public if interpreters move to separate assemblies.
    let internal exitPath (hierarchy: StateHierarchy) (source: string) (lca: string) : string list =
        let maxDepth = hierarchy.DepthMap.Count

        let rec loop current acc depth =
            if current = lca then
                acc
            elif depth <= 0 then
                invalidOp (sprintf "exitPath: exceeded max depth walking from '%s' toward '%s'" source lca)
            else
                match Map.tryFind current hierarchy.ParentMap with
                | Some parent -> loop parent (current :: acc) (depth - 1)
                | None -> current :: acc

        loop source [] maxDepth |> List.rev

    /// Walk from target up to (but not including) lca via ParentMap.
    /// Returns lca-first order: [child-of-lca; ...; parent-of-target; target].
    let internal entryPath (hierarchy: StateHierarchy) (target: string) (lca: string) : string list =
        let maxDepth = hierarchy.DepthMap.Count

        let rec loop current acc depth =
            if current = lca then
                acc
            elif depth <= 0 then
                invalidOp (sprintf "entryPath: exceeded max depth walking from '%s' toward '%s'" target lca)
            else
                match Map.tryFind current hierarchy.ParentMap with
                | Some parent -> loop parent (current :: acc) (depth - 1)
                | None -> current :: acc

        loop target [] maxDepth

    /// Convert a StateHierarchy to a lightweight StateContainment for use
    /// in hierarchy-aware projection and validation functions in Frank.Resources.Model.
    let toContainment (hierarchy: StateHierarchy) : Frank.Resources.Model.StateContainment =
        let containment =
            Frank.Resources.Model.StateContainment.ofPairs (Map.toList hierarchy.ChildrenMap)

        let compositeKinds =
            hierarchy.StateKind
            |> Map.map (fun _ kind ->
                match kind with
                | CompositeKind.XOR -> Frank.Resources.Model.CompositeKind.XOR
                | CompositeKind.AND -> Frank.Resources.Model.CompositeKind.AND)

        { containment with
            CompositeKinds = compositeKinds }

// ==========================================================================
// HierarchicalRuntime module
// ==========================================================================

[<RequireQualifiedAccess>]
module HierarchicalRuntime =

    /// Collect all descendant state IDs of a composite state (recursive).
    /// Uses the pre-computed DescendantMap for O(1) lookup.
    let private allDescendants (hierarchy: StateHierarchy) (stateId: string) : string list =
        Map.tryFind stateId hierarchy.DescendantMap |> Option.defaultValue []

    /// Add all ancestors of a state up to the root to the active configuration.
    /// Harel semantics: a state is active iff it or any descendant is the current atomic state;
    /// all ancestors must be in the active configuration.
    let private addAncestors (hierarchy: StateHierarchy) (stateId: string) (config: ActiveStateConfiguration) =
        let rec loop current config =
            match Map.tryFind current hierarchy.ParentMap with
            | Some parent -> loop parent (ActiveStateConfiguration.add parent config)
            | None -> config

        loop stateId config

    /// Enter a state, recursively activating initial children for composite states.
    /// For AND composites, all children are entered. For XOR, only the initial child.
    /// Enforces XOR exclusivity: entering a child of an XOR composite deactivates siblings.
    /// Adds all ancestors up to root per Harel semantics (issue #265).
    let rec enterState
        (hierarchy: StateHierarchy)
        (stateId: string)
        (config: ActiveStateConfiguration)
        : ActiveStateConfiguration =
        // Harel semantics: all ancestors must be in the active configuration
        let config = addAncestors hierarchy stateId config

        // Enforce XOR exclusivity: if parent is XOR, deactivate sibling children and their descendants
        let config =
            match Map.tryFind stateId hierarchy.ParentMap with
            | Some parentId ->
                match Map.tryFind parentId hierarchy.StateKind with
                | Some CompositeKind.XOR ->
                    match Map.tryFind parentId hierarchy.ChildrenMap with
                    | Some siblings ->
                        siblings
                        |> List.fold
                            (fun c sibling ->
                                if sibling = stateId then
                                    c
                                else
                                    let c = ActiveStateConfiguration.remove sibling c

                                    allDescendants hierarchy sibling
                                    |> List.fold (fun c' s -> ActiveStateConfiguration.remove s c') c)
                            config
                    | None -> config
                | _ -> config
            | None -> config

        let config = ActiveStateConfiguration.add stateId config

        match Map.tryFind stateId hierarchy.StateKind with
        | Some CompositeKind.XOR ->
            match Map.tryFind stateId hierarchy.InitialChild with
            | Some initial -> enterState hierarchy initial config
            | None -> config
        | Some CompositeKind.AND ->
            match Map.tryFind stateId hierarchy.ChildrenMap with
            | Some children -> children |> List.fold (fun c child -> enterState hierarchy child c) config
            | None -> config
        | None ->
            // Atomic state - nothing else to enter
            config

    /// Record a composite state's active descendant configuration in history.
    /// No-op for atomic (non-composite) states.
    let private recordCompositeHistory
        (hierarchy: StateHierarchy)
        (stateId: string)
        (config: ActiveStateConfiguration)
        (history: HistoryRecord)
        : HistoryRecord =
        match Map.tryFind stateId hierarchy.StateKind with
        | Some _ ->
            let childStates = allDescendants hierarchy stateId

            let activeChildren =
                childStates |> List.filter (fun s -> ActiveStateConfiguration.isActive s config)

            let childConfig =
                activeChildren
                |> List.fold (fun c s -> ActiveStateConfiguration.add s c) ActiveStateConfiguration.empty

            HistoryRecord.record stateId childConfig history
        | None -> history

    /// Diff ActiveStateConfiguration before/after, sorted parent-before-child
    /// for SCXML-compliant entry ordering.
    let private enteredStatesSorted
        (hierarchy: StateHierarchy)
        (before: Set<string>)
        (after: Set<string>)
        : string list =
        Set.difference after before
        |> Set.toList
        |> List.sortBy (fun s -> Map.tryFind s hierarchy.DepthMap |> Option.defaultValue 0)

    /// Perform a hierarchical state transition from source to target.
    /// Computes LCA, exits states from source up to LCA, enters states from LCA down to target.
    /// Accepts a HistoryRecord to accumulate history across transitions.
    let transition
        (hierarchy: StateHierarchy)
        (config: ActiveStateConfiguration)
        (source: string)
        (target: string)
        (history: HistoryRecord)
        : HierarchicalTransitionResult =
        // For self-transitions (source = target), use external semantics:
        // exit the state and re-enter it, resetting composite children to initial.
        let exitStates, entryStates, lcaOpt =
            if source = target then
                ([ source ], [ target ], None)
            else
                let lca =
                    StateHierarchy.computeLCA hierarchy source target
                    |> Option.defaultWith (fun () ->
                        failwithf
                            "No LCA found for (%s, %s) — disconnected hierarchy. Ensure all states share a common ancestor."
                            source
                            target)

                let exits = StateHierarchy.exitPath hierarchy source lca
                let entries = StateHierarchy.entryPath hierarchy target lca
                (exits, entries, Some lca)

        // Record history for the LCA if it's a composite. The LCA is NOT being exited,
        // but its active child is changing. Record what was active before so shallow/deep
        // history pseudo-states can restore it later (e.g., Capture→Retry within Payment).
        let historyWithLca =
            match lcaOpt with
            | Some lca -> recordCompositeHistory hierarchy lca config history
            | None -> history

        // Exit phase: record history for exited composite states, folding into LCA history
        let updatedHistory =
            exitStates
            |> List.fold (fun h exitState -> recordCompositeHistory hierarchy exitState config h) historyWithLca

        // Remove exited states and all descendants of exited composite states.
        // For AND composites, this deactivates all regions; for XOR, any active child subtree.
        let configAfterExit =
            exitStates
            |> List.fold
                (fun c exitState ->
                    let c = ActiveStateConfiguration.remove exitState c

                    match Map.tryFind exitState hierarchy.StateKind with
                    | Some _ ->
                        allDescendants hierarchy exitState
                        |> List.fold (fun c' s -> ActiveStateConfiguration.remove s c') c
                    | None -> c)
                config

        // Entry phase: add states to configuration, recursively entering composites.
        // Accumulate entered states in reverse using cons-based accumulation
        // to avoid O(n^2) list concatenation from List.rev + append.
        let configAfterEntry, enteredStatesRev =
            entryStates
            |> List.fold
                (fun (currentConfig, accRev) entryState ->
                    let before = ActiveStateConfiguration.toSet currentConfig
                    let nextConfig = enterState hierarchy entryState currentConfig
                    let after = ActiveStateConfiguration.toSet nextConfig
                    let sorted = enteredStatesSorted hierarchy before after
                    let nextAccRev = sorted |> List.fold (fun acc s -> s :: acc) accRev

                    (nextConfig, nextAccRev))
                (configAfterExit, [])

        { Configuration = configAfterEntry
          ExitedStates = exitStates
          EnteredStates = List.rev enteredStatesRev
          HistoryRecord = updatedHistory }

    /// Enter a composite's initial child, or return config unchanged if none defined.
    let private enterInitialChild
        (hierarchy: StateHierarchy)
        (compositeStateId: string)
        (config: ActiveStateConfiguration)
        : ActiveStateConfiguration =
        match Map.tryFind compositeStateId hierarchy.InitialChild with
        | Some initial -> enterState hierarchy initial config
        | None -> config

    /// Restore shallow history: re-enter the direct child that was last active.
    let private restoreShallowHistory
        (hierarchy: StateHierarchy)
        (compositeStateId: string)
        (previousConfig: ActiveStateConfiguration)
        (config: ActiveStateConfiguration)
        : ActiveStateConfiguration =
        let children = Map.tryFind compositeStateId hierarchy.ChildrenMap |> Option.defaultValue []

        let lastActiveChild =
            children |> List.tryFind (fun child -> ActiveStateConfiguration.isActive child previousConfig)

        match lastActiveChild with
        | Some child -> enterState hierarchy child config
        | None -> enterInitialChild hierarchy compositeStateId config

    /// Restore deep history: walk down the composite hierarchy, re-entering
    /// each previously active child via enterState (enforces XOR exclusivity).
    let private restoreDeepHistory
        (hierarchy: StateHierarchy)
        (compositeStateId: string)
        (previousConfig: ActiveStateConfiguration)
        (config: ActiveStateConfiguration)
        : ActiveStateConfiguration =
        let previousStates = ActiveStateConfiguration.toSet previousConfig

        let rec restoreSubtree (parentId: string) (c: ActiveStateConfiguration) : ActiveStateConfiguration =
            let children = Map.tryFind parentId hierarchy.ChildrenMap |> Option.defaultValue []
            let activeChildren = children |> List.filter (fun child -> Set.contains child previousStates)

            activeChildren
            |> List.fold
                (fun acc child ->
                    let acc = enterState hierarchy child acc

                    match Map.tryFind child hierarchy.StateKind with
                    | Some _ -> restoreSubtree child acc
                    | None -> acc)
                c

        restoreSubtree compositeStateId config

    /// Enter a composite state using history (shallow or deep).
    /// Falls back to initial child when no history is recorded.
    let enterWithHistory
        (hierarchy: StateHierarchy)
        (historyKind: Frank.Statecharts.Ast.HistoryKind)
        (compositeStateId: string)
        (config: ActiveStateConfiguration)
        (history: HistoryRecord)
        : ActiveStateConfiguration =
        // Harel semantics: all ancestors must be in the active configuration (issue #265).
        let config = addAncestors hierarchy compositeStateId config
        let config = ActiveStateConfiguration.add compositeStateId config

        match HistoryRecord.tryGet compositeStateId history with
        | Some prev ->
            match historyKind with
            | Frank.Statecharts.Ast.HistoryKind.Shallow -> restoreShallowHistory hierarchy compositeStateId prev config
            | Frank.Statecharts.Ast.HistoryKind.Deep -> restoreDeepHistory hierarchy compositeStateId prev config
        | None -> enterInitialChild hierarchy compositeStateId config

    /// Construct a RuntimeInterpreter for the given hierarchy.
    /// Internal — consumers should use runProgram, not the raw interpreter.
    let internal createInterpreter
        (hierarchy: StateHierarchy)
        : Frank.Statecharts.Ast.TransitionAlgebra<RuntimeStep> =
        { Exit = fun stateId (config, history) ->
              // Pure state removal — programs must RecordHistory explicitly
              // before Exit to capture history from the original config.
              let config =
                  let c = ActiveStateConfiguration.remove stateId config
                  match Map.tryFind stateId hierarchy.StateKind with
                  | Some _ ->
                      allDescendants hierarchy stateId
                      |> List.fold (fun c' s -> ActiveStateConfiguration.remove s c') c
                  | None -> c
              (config, history, [ stateId ], [])

          Enter = fun stateId (config, history) ->
              let before = ActiveStateConfiguration.toSet config
              let config = enterState hierarchy stateId config
              let after = ActiveStateConfiguration.toSet config
              let entered = enteredStatesSorted hierarchy before after
              (config, history, [], entered)

          // Protocol marker — actual region entry is performed by the prior Enter call.
          Fork = fun _regions (config, history) ->
              (config, history, [], [])

          RecordHistory = fun compositeId (config, history) ->
              let history = recordCompositeHistory hierarchy compositeId config history
              (config, history, [], [])

          RestoreHistory = fun (compositeId, kind) (config, history) ->
              let before = ActiveStateConfiguration.toSet config
              let config =
                  enterWithHistory hierarchy kind compositeId config history
              let after = ActiveStateConfiguration.toSet config
              let entered = enteredStatesSorted hierarchy before after
              (config, history, [], entered)

          // TODO: exited1 @ exited2 is O(n) in left list; left-associative fold
          // in sequenceOps makes this O(n²) for deep programs. N=3-8 today.
          // If programs grow, switch to reversed accumulators + single List.rev in runProgram.
          Bind = fun step1 step2 (config, history) ->
              let (c1, h1, exited1, entered1) = step1 (config, history)
              let (c2, h2, exited2, entered2) = (step2 ()) (c1, h1)
              (c2, h2, exited1 @ exited2, entered1 @ entered2)

          Return = fun () (config, history) ->
              (config, history, [], [])

          Zero = fun () (config, history) ->
              (config, history, [], []) }

    /// Run a program through the RuntimeInterpreter, producing a
    /// HierarchicalTransitionResult from the initial state.
    let runProgram
        (hierarchy: StateHierarchy)
        (config: ActiveStateConfiguration)
        (history: HistoryRecord)
        (program: Frank.Statecharts.Ast.TransitionAlgebra<RuntimeStep> -> RuntimeStep)
        : HierarchicalTransitionResult =
        let activeStates = ActiveStateConfiguration.toSet config

        if not (Set.isEmpty activeStates)
           && not (activeStates |> Set.forall (fun s -> Map.containsKey s hierarchy.DepthMap)) then
            invalidArg (nameof config) "config contains states not present in hierarchy"

        let interpreter = createInterpreter hierarchy
        let step = program interpreter
        let (finalConfig, finalHistory, exited, entered) = step (config, history)
        { Configuration = finalConfig
          ExitedStates = exited
          EnteredStates = entered
          HistoryRecord = finalHistory }

    /// Check whether all regions of an AND composite have a leaf that is a final state.
    /// Returns true when every direct child region of compositeId has at least one active
    /// descendant (or itself) in finalStates.
    let isCompositeComplete
        (hierarchy: StateHierarchy)
        (config: ActiveStateConfiguration)
        (compositeId: string)
        (finalStates: Set<string>)
        : bool =
        match Map.tryFind compositeId hierarchy.ChildrenMap with
        | None -> false
        | Some children ->
            children
            |> List.forall (fun child ->
                let descendants = child :: (allDescendants hierarchy child)

                descendants
                |> List.exists (fun d -> ActiveStateConfiguration.isActive d config && Set.contains d finalStates))

    /// Walk up the parent chain from stateId to find the nearest AND composite that has a
    /// CompletionTarget configured. Returns (compositeId, targetKey) or None.
    let findCompletionTarget (hierarchy: StateHierarchy) (stateId: string) : (string * string) option =
        let rec loop current =
            match Map.tryFind current hierarchy.ParentMap with
            | None -> None
            | Some parent ->
                match Map.tryFind parent hierarchy.StateKind with
                | Some CompositeKind.AND ->
                    match Map.tryFind parent hierarchy.CompletionTargets with
                    | Some target -> Some(parent, target)
                    | None -> loop parent
                | _ -> loop parent

        loop stateId

    /// Find the deepest active leaf state (non-composite) in the active configuration.
    /// For XOR-only configurations, returns the single active leaf.
    /// For AND-states with multiple active leaves, returns the deepest by hierarchy depth;
    /// ties are broken lexicographically (deterministic but arbitrary).
    /// Callers operating on AND-state configurations should use ActiveStateConfiguration.toSet instead.
    let leafState (hierarchy: StateHierarchy) (config: ActiveStateConfiguration) : string option =
        let activeStates = ActiveStateConfiguration.toSet config

        let leaves =
            activeStates
            |> Set.filter (fun s -> not (Map.containsKey s hierarchy.StateKind))

        if Set.isEmpty leaves then
            None
        else
            leaves
            |> Set.toList
            |> List.sortByDescending (fun s -> Map.tryFind s hierarchy.DepthMap |> Option.defaultValue 0)
            |> List.tryHead

    /// Collect all ancestor state IDs from a given state up to the root (exclusive of stateId itself).
    let private ancestorStates (hierarchy: StateHierarchy) (stateId: string) : string list =
        let rec loop current acc =
            match Map.tryFind current hierarchy.ParentMap with
            | Some parent -> loop parent (parent :: acc)
            | None -> acc

        loop stateId []

    /// Resolve allowed HTTP methods for the current active configuration.
    /// Returns the union of methods from all active states and their ancestors.
    let resolveAllowedMethods
        (hierarchy: StateHierarchy)
        (stateHandlerMap: Map<string, string list>)
        (config: ActiveStateConfiguration)
        : Set<string> =
        let activeStates = ActiveStateConfiguration.toSet config

        // Collect active states + their ancestors (same traversal as resolveHandlers)
        let allStates =
            activeStates
            |> Set.toList
            |> List.collect (fun stateId -> stateId :: ancestorStates hierarchy stateId)
            |> List.distinct

        allStates
        |> List.collect (fun stateId ->
            match Map.tryFind stateId stateHandlerMap with
            | Some methods -> methods
            | None -> [])
        |> Set.ofList

    /// Resolve handlers for the current active configuration.
    /// Child handlers override parent handlers for the same HTTP method.
    let resolveHandlers<'T>
        (hierarchy: StateHierarchy)
        (stateHandlerMap: Map<string, (string * 'T) list>)
        (config: ActiveStateConfiguration)
        : (string * 'T) list =
        let activeStates = ActiveStateConfiguration.toSet config

        // Collect all active states ordered by depth (deepest first for override precedence),
        // using the pre-computed DepthMap
        let statesByDepth =
            activeStates
            |> Set.toList
            |> List.map (fun stateId ->
                let d = Map.tryFind stateId hierarchy.DepthMap |> Option.defaultValue 0
                (stateId, d))
            |> List.sortByDescending snd
            |> List.map fst

        // Collect all states to consider: active states + their ancestors (for fallback)
        let allStatesWithDepth =
            statesByDepth
            |> List.collect (fun stateId ->
                let ancestors = ancestorStates hierarchy stateId

                let ancestorsWithDepth =
                    ancestors
                    |> List.map (fun a -> (a, Map.tryFind a hierarchy.DepthMap |> Option.defaultValue 0))

                let d = Map.tryFind stateId hierarchy.DepthMap |> Option.defaultValue 0
                (stateId, d) :: ancestorsWithDepth)
            |> List.sortByDescending snd
            |> List.distinctBy fst

        // Build handler map via fold: deepest state wins for each method
        let methodMap =
            allStatesWithDepth
            |> List.fold
                (fun (acc: Map<string, 'T>) (stateId, _depth) ->
                    match Map.tryFind stateId stateHandlerMap with
                    | Some handlers ->
                        handlers
                        |> List.fold
                            (fun m (method, handler) ->
                                if Map.containsKey method m then
                                    m
                                else
                                    Map.add method handler m)
                            acc
                    | None -> acc)
                Map.empty

        methodMap |> Map.toList

// ==========================================================================
// TransitionProgram builder — the safety boundary for program construction
// ==========================================================================

[<RequireQualifiedAccess>]
module TransitionProgram =

    /// Build thunked ops for a self-transition (external semantics: exit + re-enter).
    let private selfTransitionOps
        (hierarchy: StateHierarchy)
        (source: string)
        (alg: Frank.Statecharts.Ast.TransitionAlgebra<'r>)
        : (unit -> 'r) list =
        let recordOps =
            if Map.containsKey source hierarchy.StateKind then
                [ fun () -> alg.RecordHistory source ]
            else
                []

        recordOps @ [ (fun () -> alg.Exit source); (fun () -> alg.Enter source) ]

    /// Build thunked ops for a non-self transition: RecordHistory, Exit, Enter (+Fork).
    let private transitionOps
        (hierarchy: StateHierarchy)
        (source: string)
        (target: string)
        (alg: Frank.Statecharts.Ast.TransitionAlgebra<'r>)
        : (unit -> 'r) list =
        let lca =
            StateHierarchy.computeLCA hierarchy source target
            |> Option.defaultWith (fun () ->
                failwithf
                    "No LCA for (%s, %s) — disconnected hierarchy. Ensure all states share a common ancestor."
                    source
                    target)

        let exits = StateHierarchy.exitPath hierarchy source lca
        let entries = StateHierarchy.entryPath hierarchy target lca

        let recordOps =
            lca :: exits
            |> List.filter (fun s -> Map.containsKey s hierarchy.StateKind)
            |> List.map (fun s () -> alg.RecordHistory s)

        let exitOps = exits |> List.map (fun s () -> alg.Exit s)

        let enterOps =
            entries
            |> List.collect (fun s ->
                let enter () = alg.Enter s

                match Map.tryFind s hierarchy.StateKind with
                | Some CompositeKind.AND ->
                    let children =
                        Map.tryFind s hierarchy.ChildrenMap |> Option.defaultValue []

                    [ enter; fun () -> alg.Fork children ]
                | _ -> [ enter ])

        recordOps @ exitOps @ enterOps

    /// Sequence thunked ops via Bind, returning Return for empty programs.
    let private sequenceOps (alg: Frank.Statecharts.Ast.TransitionAlgebra<'r>) (ops: (unit -> 'r) list) : 'r =
        match ops with
        | [] -> alg.Zero()
        | first :: rest -> rest |> List.fold (fun acc op -> alg.Bind acc op) (first ())

    /// Build a transition program equivalent to HierarchicalRuntime.transition.
    /// This is the safety boundary for program construction — it ensures
    /// RecordHistory is emitted for all composites before any Exit, so
    /// history sees the original config. The algebra permits hand-written
    /// programs but does not enforce correct ordering; this builder does.
    /// Self-transitions use external semantics: exit and re-enter.
    let fromTransition
        (hierarchy: StateHierarchy)
        (source: string)
        (target: string)
        : Frank.Statecharts.Ast.TransitionAlgebra<'r> -> 'r =
        if System.String.IsNullOrWhiteSpace source then
            invalidArg (nameof source) "source must be a non-empty state ID"

        if System.String.IsNullOrWhiteSpace target then
            invalidArg (nameof target) "target must be a non-empty state ID"

        fun alg ->
            let ops =
                if source = target then
                    selfTransitionOps hierarchy source alg
                else
                    transitionOps hierarchy source target alg

            sequenceOps alg ops
