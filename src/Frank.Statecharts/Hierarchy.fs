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
// only when StateMachineMetadata.Hierarchy = Some _.
// ==========================================================================

/// Composite state kind: XOR (exclusive, one child active) or AND (parallel, all children active).
type CompositeKind =
    | XOR
    | AND

/// Specification for a single composite state, used as input to StateHierarchy.build.
type CompositeStateSpec =
    { Id: string
      Kind: CompositeKind
      Children: string list
      InitialChild: string option }

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

// ==========================================================================
// StateHierarchy module
// ==========================================================================

[<RequireQualifiedAccess>]
module StateHierarchy =

    /// Build a StateHierarchy from a HierarchySpec.
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

        { ParentMap = parentMap
          ChildrenMap = childrenMap
          InitialChild = initialChild
          StateKind = stateKind }

    /// Compute the ancestry path from a state up to the root (inclusive).
    /// Returns the state itself first, then its parent, then grandparent, etc.
    /// Example: ancestry("Red") = ["Red"; "Active"; "Root"]
    let private ancestry (hierarchy: StateHierarchy) (stateId: string) : string list =
        let rec loop current acc =
            let acc = current :: acc

            match Map.tryFind current hierarchy.ParentMap with
            | Some parent -> loop parent acc
            | None -> List.rev acc

        loop stateId []

    /// Compute the Least Common Ancestor of two states.
    /// Returns None if the states share no common ancestor (disconnected hierarchies).
    let computeLCA (hierarchy: StateHierarchy) (stateA: string) (stateB: string) : string option =
        let ancestryA = ancestry hierarchy stateA
        let ancestryB = ancestry hierarchy stateB |> Set.ofList

        // Walk ancestry of A from root toward leaf; last one in both sets is LCA
        let ancestryAFromRoot = List.rev ancestryA

        let rec findLCA candidates lastCommon =
            match candidates with
            | [] -> lastCommon
            | x :: rest ->
                if Set.contains x ancestryB then
                    findLCA rest (Some x)
                else
                    lastCommon

        findLCA ancestryAFromRoot None

// ==========================================================================
// HierarchicalRuntime module
// ==========================================================================

[<RequireQualifiedAccess>]
module HierarchicalRuntime =

    /// Enter a state, recursively activating initial children for composite states.
    /// For AND composites, all children are entered. For XOR, only the initial child.
    let rec enterState
        (hierarchy: StateHierarchy)
        (stateId: string)
        (config: ActiveStateConfiguration)
        : ActiveStateConfiguration =
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

    /// Collect the list of states to exit from source up to (but not including) LCA.
    /// Order: source first, then parent, up to but not including LCA.
    let private exitPath (hierarchy: StateHierarchy) (source: string) (lca: string) : string list =
        let rec loop current acc =
            if current = lca then
                acc
            else
                match Map.tryFind current hierarchy.ParentMap with
                | Some parent -> loop parent (current :: acc)
                | None -> current :: acc

        loop source [] |> List.rev

    /// Collect the list of states to enter from LCA down to target.
    /// Order: first child of LCA on the path to target, then its child, etc.
    let private entryPath (hierarchy: StateHierarchy) (target: string) (lca: string) : string list =
        let rec ancestryToLca current acc =
            if current = lca then
                acc
            else
                match Map.tryFind current hierarchy.ParentMap with
                | Some parent -> ancestryToLca parent (current :: acc)
                | None -> current :: acc

        ancestryToLca target []

    /// Exit a composite state, recording its active configuration in history.
    let private exitCompositeState
        (hierarchy: StateHierarchy)
        (stateId: string)
        (config: ActiveStateConfiguration)
        (history: HistoryRecord)
        : HistoryRecord =
        match Map.tryFind stateId hierarchy.StateKind with
        | Some _ ->
            // Record the current configuration for this composite state
            // (all active children within this composite)
            let childStates =
                match Map.tryFind stateId hierarchy.ChildrenMap with
                | Some children ->
                    let rec allDescendants states =
                        states
                        |> List.collect (fun s ->
                            let descendants =
                                match Map.tryFind s hierarchy.ChildrenMap with
                                | Some kids -> allDescendants kids
                                | None -> []

                            s :: descendants)

                    allDescendants children
                | None -> []

            let activeChildren =
                childStates |> List.filter (fun s -> ActiveStateConfiguration.isActive s config)

            let childConfig =
                activeChildren
                |> List.fold (fun c s -> ActiveStateConfiguration.add s c) ActiveStateConfiguration.empty

            HistoryRecord.record stateId childConfig history
        | None -> history

    /// Perform a hierarchical state transition from source to target.
    /// Computes LCA, exits states from source up to LCA, enters states from LCA down to target.
    let transition
        (hierarchy: StateHierarchy)
        (config: ActiveStateConfiguration)
        (source: string)
        (target: string)
        : HierarchicalTransitionResult =
        let lca =
            StateHierarchy.computeLCA hierarchy source target |> Option.defaultValue source

        // Compute exit path (source up to but not including LCA)
        let exitStates = exitPath hierarchy source lca

        // Compute entry path (LCA down to target, not including LCA)
        let entryStates = entryPath hierarchy target lca

        // Record history for exited composite states
        let mutable history = HistoryRecord.empty

        for exitState in exitStates do
            history <- exitCompositeState hierarchy exitState config history

        // Exit states: remove from configuration
        let mutable currentConfig = config

        for exitState in exitStates do
            currentConfig <- ActiveStateConfiguration.remove exitState currentConfig

        // Enter states: add to configuration, recursively entering composites
        let mutable enteredStates = []

        for entryState in entryStates do
            // If this is a composite state, enterState handles the recursion
            let before = ActiveStateConfiguration.toSet currentConfig
            currentConfig <- enterState hierarchy entryState currentConfig
            let after = ActiveStateConfiguration.toSet currentConfig
            let newlyEntered = Set.difference after before |> Set.toList
            enteredStates <- enteredStates @ newlyEntered

        { Configuration = currentConfig
          ExitedStates = exitStates
          EnteredStates = enteredStates
          HistoryRecord = history }

    /// Enter a composite state using history (shallow or deep).
    /// Falls back to initial child when no history is recorded.
    let enterWithHistory
        (hierarchy: StateHierarchy)
        (historyKind: Frank.Statecharts.Ast.HistoryKind)
        (compositeStateId: string)
        (config: ActiveStateConfiguration)
        (history: HistoryRecord)
        : ActiveStateConfiguration =
        let config = ActiveStateConfiguration.add compositeStateId config

        match HistoryRecord.tryGet compositeStateId history with
        | Some previousConfig ->
            match historyKind with
            | Frank.Statecharts.Ast.HistoryKind.Shallow ->
                // Shallow: restore only the direct child that was active
                match Map.tryFind compositeStateId hierarchy.ChildrenMap with
                | Some children ->
                    let lastActiveChild =
                        children
                        |> List.tryFind (fun child -> ActiveStateConfiguration.isActive child previousConfig)

                    match lastActiveChild with
                    | Some child -> enterState hierarchy child config
                    | None ->
                        // Fallback to initial
                        match Map.tryFind compositeStateId hierarchy.InitialChild with
                        | Some initial -> enterState hierarchy initial config
                        | None -> config
                | None -> config
            | Frank.Statecharts.Ast.HistoryKind.Deep ->
                // Deep: restore the full configuration recursively
                let previousStates = ActiveStateConfiguration.toSet previousConfig

                previousStates |> Set.fold (fun c s -> ActiveStateConfiguration.add s c) config
        | None ->
            // No history: fall back to initial child
            match Map.tryFind compositeStateId hierarchy.InitialChild with
            | Some initial -> enterState hierarchy initial config
            | None -> config

    /// Resolve allowed HTTP methods for the current active configuration.
    /// Returns the union of methods from all active states and their ancestors.
    let resolveAllowedMethods
        (hierarchy: StateHierarchy)
        (stateHandlerMap: Map<string, string list>)
        (config: ActiveStateConfiguration)
        : Set<string> =
        let activeStates = ActiveStateConfiguration.toSet config

        activeStates
        |> Set.toList
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

        // Collect all active states ordered by depth (deepest first for override precedence)
        let statesByDepth =
            activeStates
            |> Set.toList
            |> List.map (fun stateId ->
                let rec depth id d =
                    match Map.tryFind id hierarchy.ParentMap with
                    | Some parent -> depth parent (d + 1)
                    | None -> d

                (stateId, depth stateId 0))
            |> List.sortByDescending snd
            |> List.map fst

        // Build handler map: deepest state wins for each method
        let mutable methodMap = Map.empty<string, 'T>

        for stateId in statesByDepth do
            match Map.tryFind stateId stateHandlerMap with
            | Some handlers ->
                for (method, handler) in handlers do
                    if not (Map.containsKey method methodMap) then
                        methodMap <- Map.add method handler methodMap
            | None -> ()

        // Also collect from parents not in active set but in ancestry
        // Walk back up from deepest active states to find parent handlers
        for stateId in statesByDepth do
            let rec walkParents current =
                match Map.tryFind current hierarchy.ParentMap with
                | Some parent ->
                    match Map.tryFind parent stateHandlerMap with
                    | Some handlers ->
                        for (method, handler) in handlers do
                            if not (Map.containsKey method methodMap) then
                                methodMap <- Map.add method handler methodMap
                    | None -> ()

                    walkParents parent
                | None -> ()

            walkParents stateId

        methodMap |> Map.toList
