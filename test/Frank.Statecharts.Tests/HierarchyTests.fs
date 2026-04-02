module HierarchyTests

open Expecto
open Frank.Statecharts
open Frank.Statecharts.Ast

// ==========================================================================
// Test scenario: Traffic light with hierarchical states
//
// Root
//   Active (composite, XOR)
//     Red (initial child of Active)
//     Yellow
//     Green
//   Off (regular)
//
// This models a traffic light that can be turned on/off.
// When on ("Active"), it cycles through Red -> Green -> Yellow -> Red.
// Turning off from any Active child returns to Off.
// Turning on from Off enters Active -> Red (initial child).
// ==========================================================================

/// State identifiers for the traffic light hierarchy.
[<RequireQualifiedAccess>]
module TrafficLight =
    let root = "Root"
    let active = "Active"
    let red = "Red"
    let yellow = "Yellow"
    let green = "Green"
    let off = "Off"

// ==========================================================================
// Sub-task A: StateHierarchy data structure
// ==========================================================================

[<Tests>]
let stateHierarchyBuildTests =
    testList
        "StateHierarchy.build"
        [ testCase "parentMap contains child-to-parent mappings"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              Expect.equal
                  (Map.tryFind TrafficLight.active hierarchy.ParentMap)
                  (Some TrafficLight.root)
                  "Active's parent is Root"

              Expect.equal
                  (Map.tryFind TrafficLight.off hierarchy.ParentMap)
                  (Some TrafficLight.root)
                  "Off's parent is Root"

              Expect.equal
                  (Map.tryFind TrafficLight.red hierarchy.ParentMap)
                  (Some TrafficLight.active)
                  "Red's parent is Active"

              Expect.equal
                  (Map.tryFind TrafficLight.yellow hierarchy.ParentMap)
                  (Some TrafficLight.active)
                  "Yellow's parent is Active"

              Expect.equal
                  (Map.tryFind TrafficLight.green hierarchy.ParentMap)
                  (Some TrafficLight.active)
                  "Green's parent is Active"

              Expect.isNone (Map.tryFind TrafficLight.root hierarchy.ParentMap) "Root has no parent"

          testCase "childrenMap contains parent-to-children mappings"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              Expect.equal
                  (Map.tryFind TrafficLight.root hierarchy.ChildrenMap)
                  (Some [ TrafficLight.active; TrafficLight.off ])
                  "Root's children"

              Expect.equal
                  (Map.tryFind TrafficLight.active hierarchy.ChildrenMap)
                  (Some [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ])
                  "Active's children"

          testCase "initialChild maps composite states to their initial child"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              Expect.equal
                  (Map.tryFind TrafficLight.root hierarchy.InitialChild)
                  (Some TrafficLight.active)
                  "Root's initial"

              Expect.equal
                  (Map.tryFind TrafficLight.active hierarchy.InitialChild)
                  (Some TrafficLight.red)
                  "Active's initial"

          testCase "stateKind maps state IDs to their composite kind"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              Expect.equal (Map.tryFind TrafficLight.root hierarchy.StateKind) (Some CompositeKind.XOR) "Root is XOR"

              Expect.equal
                  (Map.tryFind TrafficLight.active hierarchy.StateKind)
                  (Some CompositeKind.XOR)
                  "Active is XOR"

              Expect.isNone (Map.tryFind TrafficLight.red hierarchy.StateKind) "Red is atomic (not in stateKind)"

          testCase "empty hierarchy spec produces empty maps"
          <| fun () ->
              let hierarchy = StateHierarchy.build { States = [] }
              Expect.isEmpty (Map.toList hierarchy.ParentMap) "no parents"
              Expect.isEmpty (Map.toList hierarchy.ChildrenMap) "no children"
              Expect.isEmpty (Map.toList hierarchy.InitialChild) "no initials"
              Expect.isEmpty (Map.toList hierarchy.StateKind) "no kinds"

          testCase "toContainment produces correct StateContainment"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let containment = StateHierarchy.toContainment hierarchy

              // ParentOf
              Expect.equal
                  (Frank.Resources.Model.StateContainment.children TrafficLight.root containment)
                  [ TrafficLight.active; TrafficLight.off ]
                  "Root children"

              Expect.equal
                  (Frank.Resources.Model.StateContainment.children TrafficLight.active containment)
                  [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                  "Active children"

              // ChildOf
              Expect.equal
                  (Frank.Resources.Model.StateContainment.parent TrafficLight.red containment)
                  (Some TrafficLight.active)
                  "Red's parent is Active"

              // isComposite
              Expect.isTrue
                  (Frank.Resources.Model.StateContainment.isComposite TrafficLight.root containment)
                  "Root is composite"

              Expect.isFalse
                  (Frank.Resources.Model.StateContainment.isComposite TrafficLight.red containment)
                  "Red is atomic"

              // allDescendants
              let rootDescendants =
                  Frank.Resources.Model.StateContainment.allDescendants TrafficLight.root containment
                  |> Set.ofList

              Expect.contains rootDescendants TrafficLight.active "Root descendants include Active"
              Expect.contains rootDescendants TrafficLight.red "Root descendants include Red (grandchild)" ]

// ==========================================================================
// Sub-task A: LCA computation
// ==========================================================================

[<Tests>]
let lcaTests =
    testList
        "StateHierarchy.computeLCA"
        [ testCase "LCA of siblings is their parent"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let lca = StateHierarchy.computeLCA hierarchy TrafficLight.red TrafficLight.green
              Expect.equal lca (Some TrafficLight.active) "LCA of Red and Green is Active"

          testCase "LCA of parent and child is the parent"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let lca = StateHierarchy.computeLCA hierarchy TrafficLight.active TrafficLight.red
              Expect.equal lca (Some TrafficLight.active) "LCA of Active and Red is Active"

          testCase "LCA of states in different subtrees is the common ancestor"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let lca = StateHierarchy.computeLCA hierarchy TrafficLight.red TrafficLight.off
              Expect.equal lca (Some TrafficLight.root) "LCA of Red and Off is Root"

          testCase "LCA of state with itself is itself"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None } ] }

              let lca =
                  StateHierarchy.computeLCA hierarchy TrafficLight.active TrafficLight.active

              Expect.equal lca (Some TrafficLight.active) "LCA of Active and Active is Active" ]

// ==========================================================================
// Sub-task B: XOR composite states
// ==========================================================================

[<Tests>]
let xorCompositeTests =
    testList
        "XOR composite states"
        [ testCase "entering composite state activates initial child"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let config =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              Expect.isTrue
                  (ActiveStateConfiguration.isActive TrafficLight.red config)
                  "Red is active after entering Active"

              Expect.isTrue (ActiveStateConfiguration.isActive TrafficLight.active config) "Active is also active"

          testCase "entering atomic state activates only that state"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None } ] }

              let config =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.off ActiveStateConfiguration.empty

              Expect.isTrue (ActiveStateConfiguration.isActive TrafficLight.off config) "Off is active"
              Expect.isFalse (ActiveStateConfiguration.isActive TrafficLight.active config) "Active is not active"

          testCase "transition within composite changes only child"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              // Start in Red (inside Active)
              let initial =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              // Transition from Red to Green
              let result =
                  HierarchicalRuntime.transition
                      hierarchy
                      initial
                      TrafficLight.red
                      TrafficLight.green
                      HistoryRecord.empty

              Expect.isTrue
                  (ActiveStateConfiguration.isActive TrafficLight.green result.Configuration)
                  "Green is active"

              Expect.isFalse
                  (ActiveStateConfiguration.isActive TrafficLight.red result.Configuration)
                  "Red is no longer active"

              Expect.isTrue
                  (ActiveStateConfiguration.isActive TrafficLight.active result.Configuration)
                  "Active remains active" ]

// ==========================================================================
// Sub-task C: LCA-based entry/exit ordering
// ==========================================================================

[<Tests>]
let entryExitOrderingTests =
    testList
        "LCA-based entry/exit ordering"
        [ testCase "transition within same parent: exit source, enter target"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let initial =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              let result =
                  HierarchicalRuntime.transition
                      hierarchy
                      initial
                      TrafficLight.red
                      TrafficLight.green
                      HistoryRecord.empty

              // Exit Red, then Enter Green (within Active, so Active is not exited/entered)
              Expect.equal result.ExitedStates [ TrafficLight.red ] "Exited Red"
              Expect.equal result.EnteredStates [ TrafficLight.green ] "Entered Green"

          testCase "transition across composite boundary: exit up to LCA, enter down to target"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let initial =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              // Transition from Red (inside Active) to Off (sibling of Active)
              let result =
                  HierarchicalRuntime.transition hierarchy initial TrafficLight.red TrafficLight.off HistoryRecord.empty

              // Should exit Red, then Active, then enter Off (LCA is Root)
              Expect.equal result.ExitedStates [ TrafficLight.red; TrafficLight.active ] "Exited Red then Active"
              Expect.equal result.EnteredStates [ TrafficLight.off ] "Entered Off"

          testCase "transition from atomic to composite: enters composite and initial child"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              // Start in Off
              let initial =
                  ActiveStateConfiguration.empty
                  |> ActiveStateConfiguration.add TrafficLight.off
                  |> ActiveStateConfiguration.add TrafficLight.root

              let result =
                  HierarchicalRuntime.transition
                      hierarchy
                      initial
                      TrafficLight.off
                      TrafficLight.active
                      HistoryRecord.empty

              // Should exit Off, enter Active then Red (initial child of Active)
              Expect.equal result.ExitedStates [ TrafficLight.off ] "Exited Off"
              Expect.equal result.EnteredStates [ TrafficLight.active; TrafficLight.red ] "Entered Active then Red"
              Expect.isTrue (ActiveStateConfiguration.isActive TrafficLight.red result.Configuration) "Red is active"

              Expect.isTrue
                  (ActiveStateConfiguration.isActive TrafficLight.active result.Configuration)
                  "Active is active" ]

// ==========================================================================
// Sub-task D: AND (parallel/orthogonal) composite states
//
// Scenario: a device with parallel regions
//   Device (AND composite)
//     Display (XOR)
//       ScreenOn (initial)
//       ScreenOff
//     Network (XOR)
//       Connected (initial)
//       Disconnected
// ==========================================================================

[<RequireQualifiedAccess>]
module Device =
    let device = "Device"
    let display = "Display"
    let screenOn = "ScreenOn"
    let screenOff = "ScreenOff"
    let network = "Network"
    let connected = "Connected"
    let disconnected = "Disconnected"

[<Tests>]
let andCompositeTests =
    testList
        "AND (parallel) composite states"
        [ testCase "entering AND state activates all children's initial states"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = Device.device
                              Kind = CompositeKind.AND
                              Children = [ Device.display; Device.network ]
                              InitialChild = None
                              CompletionTarget = None }
                            { Id = Device.display
                              Kind = CompositeKind.XOR
                              Children = [ Device.screenOn; Device.screenOff ]
                              InitialChild = Some Device.screenOn
                              CompletionTarget = None }
                            { Id = Device.network
                              Kind = CompositeKind.XOR
                              Children = [ Device.connected; Device.disconnected ]
                              InitialChild = Some Device.connected
                              CompletionTarget = None } ] }

              let config =
                  HierarchicalRuntime.enterState hierarchy Device.device ActiveStateConfiguration.empty

              Expect.isTrue (ActiveStateConfiguration.isActive Device.device config) "Device is active"
              Expect.isTrue (ActiveStateConfiguration.isActive Device.display config) "Display is active"
              Expect.isTrue (ActiveStateConfiguration.isActive Device.screenOn config) "ScreenOn is active"
              Expect.isTrue (ActiveStateConfiguration.isActive Device.network config) "Network is active"
              Expect.isTrue (ActiveStateConfiguration.isActive Device.connected config) "Connected is active"

          testCase "transition in one region does not affect the other"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = Device.device
                              Kind = CompositeKind.AND
                              Children = [ Device.display; Device.network ]
                              InitialChild = None
                              CompletionTarget = None }
                            { Id = Device.display
                              Kind = CompositeKind.XOR
                              Children = [ Device.screenOn; Device.screenOff ]
                              InitialChild = Some Device.screenOn
                              CompletionTarget = None }
                            { Id = Device.network
                              Kind = CompositeKind.XOR
                              Children = [ Device.connected; Device.disconnected ]
                              InitialChild = Some Device.connected
                              CompletionTarget = None } ] }

              let initial =
                  HierarchicalRuntime.enterState hierarchy Device.device ActiveStateConfiguration.empty

              // Transition ScreenOn -> ScreenOff (Display region only)
              let result =
                  HierarchicalRuntime.transition hierarchy initial Device.screenOn Device.screenOff HistoryRecord.empty

              Expect.isTrue
                  (ActiveStateConfiguration.isActive Device.screenOff result.Configuration)
                  "ScreenOff is active"

              Expect.isFalse
                  (ActiveStateConfiguration.isActive Device.screenOn result.Configuration)
                  "ScreenOn is not active"
              // Network region unchanged
              Expect.isTrue
                  (ActiveStateConfiguration.isActive Device.connected result.Configuration)
                  "Connected still active"

              Expect.isTrue
                  (ActiveStateConfiguration.isActive Device.network result.Configuration)
                  "Network still active" ]

// ==========================================================================
// Sub-task E: History pseudo-states
// ==========================================================================

[<Tests>]
let historyTests =
    testList
        "History pseudo-states"
        [ testCase "shallow history restores last active child"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              // Start in Green (inside Active)
              let initial =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              let inGreen =
                  HierarchicalRuntime.transition
                      hierarchy
                      initial
                      TrafficLight.red
                      TrafficLight.green
                      HistoryRecord.empty

              // Exit Active -> Off
              let inOff =
                  HierarchicalRuntime.transition
                      hierarchy
                      inGreen.Configuration
                      TrafficLight.green
                      TrafficLight.off
                      HistoryRecord.empty

              // Re-enter Active via shallow history: should restore Green (last active child of Active)
              let restored =
                  HierarchicalRuntime.enterWithHistory
                      hierarchy
                      HistoryKind.Shallow
                      TrafficLight.active
                      inOff.Configuration
                      inOff.HistoryRecord

              Expect.isTrue
                  (ActiveStateConfiguration.isActive TrafficLight.green restored)
                  "Green restored via shallow history"

              Expect.isTrue (ActiveStateConfiguration.isActive TrafficLight.active restored) "Active is active"

          testCase "shallow history uses initial child when no history recorded"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              // No prior history - should default to initial child
              let config =
                  ActiveStateConfiguration.empty
                  |> ActiveStateConfiguration.add TrafficLight.off
                  |> ActiveStateConfiguration.add TrafficLight.root

              let restored =
                  HierarchicalRuntime.enterWithHistory
                      hierarchy
                      HistoryKind.Shallow
                      TrafficLight.active
                      config
                      HistoryRecord.empty

              Expect.isTrue
                  (ActiveStateConfiguration.isActive TrafficLight.red restored)
                  "Red (initial) used when no history"

          testCase "deep history restores full configuration recursively"
          <| fun () ->
              // Three-level hierarchy: Machine -> Active -> SubMode -> SubChild
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = "Machine"
                              Kind = CompositeKind.XOR
                              Children = [ "Active"; "Idle" ]
                              InitialChild = Some "Active"
                              CompletionTarget = None }
                            { Id = "Active"
                              Kind = CompositeKind.XOR
                              Children = [ "SubModeA"; "SubModeB" ]
                              InitialChild = Some "SubModeA"
                              CompletionTarget = None }
                            { Id = "SubModeA"
                              Kind = CompositeKind.XOR
                              Children = [ "ChildX"; "ChildY" ]
                              InitialChild = Some "ChildX"
                              CompletionTarget = None } ] }

              // Build up state: Machine -> Active -> SubModeA -> ChildY
              let initial =
                  HierarchicalRuntime.enterState hierarchy "Machine" ActiveStateConfiguration.empty

              let moved =
                  HierarchicalRuntime.transition hierarchy initial "ChildX" "ChildY" HistoryRecord.empty

              // Exit to Idle
              let exited =
                  HierarchicalRuntime.transition hierarchy moved.Configuration "ChildY" "Idle" HistoryRecord.empty

              // Re-enter Active via deep history: should fully restore ChildY
              let restored =
                  HierarchicalRuntime.enterWithHistory
                      hierarchy
                      HistoryKind.Deep
                      "Active"
                      exited.Configuration
                      exited.HistoryRecord

              Expect.isTrue (ActiveStateConfiguration.isActive "ChildY" restored) "ChildY restored via deep history"
              Expect.isTrue (ActiveStateConfiguration.isActive "SubModeA" restored) "SubModeA restored"
              Expect.isTrue (ActiveStateConfiguration.isActive "Active" restored) "Active restored" ]

// ==========================================================================
// Sub-task F: HTTP mapping
// ==========================================================================

[<Tests>]
let httpMappingTests =
    testList
        "HTTP mapping for composite states"
        [ testCase "allowed methods for composite state = union of parent and active child"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let parentMethods = [ "GET"; "OPTIONS" ]
              let childMethods = [ "POST"; "DELETE" ]

              let stateHandlerMap =
                  Map.ofList [ TrafficLight.active, parentMethods; TrafficLight.red, childMethods ]

              let result =
                  HierarchicalRuntime.resolveAllowedMethods
                      hierarchy
                      stateHandlerMap
                      (ActiveStateConfiguration.empty
                       |> ActiveStateConfiguration.add TrafficLight.active
                       |> ActiveStateConfiguration.add TrafficLight.red)

              // Union of parent and child methods
              Expect.containsAll result (Set.ofList [ "GET"; "OPTIONS"; "POST"; "DELETE" ]) "Union of methods"

          testCase "child handler overrides parent handler for same method"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let parentHandlers =
                  [ ("GET", "parent-get-handler"); ("POST", "parent-post-handler") ]

              let childHandlers = [ ("GET", "child-get-handler") ]

              let stateHandlerMap =
                  Map.ofList [ TrafficLight.active, parentHandlers; TrafficLight.red, childHandlers ]

              let resolved =
                  HierarchicalRuntime.resolveHandlers
                      hierarchy
                      stateHandlerMap
                      (ActiveStateConfiguration.empty
                       |> ActiveStateConfiguration.add TrafficLight.active
                       |> ActiveStateConfiguration.add TrafficLight.red)

              // GET should use child handler, POST should fall back to parent
              let getHandler = resolved |> List.tryFind (fun (m, _) -> m = "GET")
              let postHandler = resolved |> List.tryFind (fun (m, _) -> m = "POST")

              Expect.isSome getHandler "GET handler exists"
              Expect.equal (getHandler |> Option.map snd) (Some "child-get-handler") "GET uses child handler"
              Expect.isSome postHandler "POST handler exists"
              Expect.equal (postHandler |> Option.map snd) (Some "parent-post-handler") "POST falls back to parent" ]

// ==========================================================================
// Sub-task G: Opt-in integration (regression: flat FSM unaffected)
// ==========================================================================

[<Tests>]
let optInTests =
    testList
        "Opt-in integration"
        [ testCase "hierarchical dispatch is a pure function"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              // Same input should produce same output (pure function)
              let config1 =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              let config2 =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              Expect.equal config1 config2 "Same inputs produce same outputs"

          testCase "ActiveStateConfiguration is a Set-based immutable type"
          <| fun () ->
              let config1 = ActiveStateConfiguration.empty |> ActiveStateConfiguration.add "A"
              let config2 = ActiveStateConfiguration.empty |> ActiveStateConfiguration.add "A"
              // Set equality
              Expect.equal config1 config2 "Equal configurations"
              // Adding does not mutate
              let config3 = config1 |> ActiveStateConfiguration.add "B"
              Expect.isFalse (ActiveStateConfiguration.isActive "B" config1) "Original unchanged"
              Expect.isTrue (ActiveStateConfiguration.isActive "B" config3) "New has B" ]

// ==========================================================================
// ActiveStateConfiguration basic tests
// ==========================================================================

[<Tests>]
let activeStateConfigTests =
    testList
        "ActiveStateConfiguration"
        [ testCase "empty has no active states"
          <| fun () ->
              Expect.isFalse
                  (ActiveStateConfiguration.isActive "anything" ActiveStateConfiguration.empty)
                  "nothing active"

          testCase "add and isActive work"
          <| fun () ->
              let config = ActiveStateConfiguration.empty |> ActiveStateConfiguration.add "A"
              Expect.isTrue (ActiveStateConfiguration.isActive "A" config) "A is active"
              Expect.isFalse (ActiveStateConfiguration.isActive "B" config) "B is not active"

          testCase "remove removes a state"
          <| fun () ->
              let config =
                  ActiveStateConfiguration.empty
                  |> ActiveStateConfiguration.add "A"
                  |> ActiveStateConfiguration.add "B"
                  |> ActiveStateConfiguration.remove "A"

              Expect.isFalse (ActiveStateConfiguration.isActive "A" config) "A removed"
              Expect.isTrue (ActiveStateConfiguration.isActive "B" config) "B still active"

          testCase "toSet returns all active state IDs"
          <| fun () ->
              let config =
                  ActiveStateConfiguration.empty
                  |> ActiveStateConfiguration.add "A"
                  |> ActiveStateConfiguration.add "B"
                  |> ActiveStateConfiguration.add "C"

              Expect.equal (ActiveStateConfiguration.toSet config) (Set.ofList [ "A"; "B"; "C" ]) "all three" ]

// ==========================================================================
// HistoryRecord basic tests
// ==========================================================================

[<Tests>]
let historyRecordTests =
    testList
        "HistoryRecord"
        [ testCase "empty has no entries"
          <| fun () ->
              let entry = HistoryRecord.tryGet "someState" HistoryRecord.empty
              Expect.isNone entry "no history for any state"

          testCase "record and retrieve"
          <| fun () ->
              let config =
                  ActiveStateConfiguration.empty
                  |> ActiveStateConfiguration.add "Child1"
                  |> ActiveStateConfiguration.add "Child2"

              let record = HistoryRecord.empty |> HistoryRecord.record "Parent" config
              let retrieved = HistoryRecord.tryGet "Parent" record
              Expect.equal retrieved (Some config) "config stored and retrieved" ]

// ==========================================================================
// Bug 1: resolveAllowedMethods must traverse ancestors (#224)
// ==========================================================================

[<Tests>]
let resolveAllowedMethodsAncestryTests =
    testList
        "resolveAllowedMethods ancestor traversal (#224)"
        [ testCase "methods from non-active ancestor are included"
          <| fun () ->
              // Root (XOR) -> Active (XOR) -> Red
              // Only Red is in the active config (not Active itself),
              // but Active defines methods that should be discovered via ancestry.
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.root
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.active; TrafficLight.off ]
                              InitialChild = Some TrafficLight.active
                              CompletionTarget = None }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let stateHandlerMap =
                  Map.ofList
                      [ TrafficLight.root, [ "OPTIONS" ]
                        TrafficLight.active, [ "GET" ]
                        TrafficLight.red, [ "POST" ] ]

              // Only Red is active (leaf) — ancestors Active and Root are NOT in config
              let config =
                  ActiveStateConfiguration.empty |> ActiveStateConfiguration.add TrafficLight.red

              let result =
                  HierarchicalRuntime.resolveAllowedMethods hierarchy stateHandlerMap config

              Expect.contains result "POST" "Red's own method"
              Expect.contains result "GET" "Active's method via ancestor traversal"
              Expect.contains result "OPTIONS" "Root's method via ancestor traversal" ]

// ==========================================================================
// Bug 2: HistoryRecord must accumulate across transitions (#224)
// ==========================================================================

[<Tests>]
let historyAccumulationTests =
    testList
        "HistoryRecord accumulation across transitions (#224)"
        [ testCase "transition preserves prior history for unrelated composite states"
          <| fun () ->
              // Two sibling composite states under Root.
              // Transition within one composite should preserve history recorded for the other.
              // Root (XOR)
              //   GroupA (XOR): A1 (initial), A2
              //   GroupB (XOR): B1 (initial), B2
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = "Root"
                              Kind = CompositeKind.XOR
                              Children = [ "GroupA"; "GroupB" ]
                              InitialChild = Some "GroupA"
                              CompletionTarget = None }
                            { Id = "GroupA"
                              Kind = CompositeKind.XOR
                              Children = [ "A1"; "A2" ]
                              InitialChild = Some "A1"
                              CompletionTarget = None }
                            { Id = "GroupB"
                              Kind = CompositeKind.XOR
                              Children = [ "B1"; "B2" ]
                              InitialChild = Some "B1"
                              CompletionTarget = None } ] }

              // Enter GroupA -> A1, then move to A2
              let initial =
                  HierarchicalRuntime.enterState hierarchy "GroupA" ActiveStateConfiguration.empty
                  |> ActiveStateConfiguration.add "Root"

              let inA2 =
                  HierarchicalRuntime.transition hierarchy initial "A1" "A2" HistoryRecord.empty

              // Transition from A2 to GroupB (crosses Root, exits GroupA — records GroupA history)
              let inB =
                  HierarchicalRuntime.transition hierarchy inA2.Configuration "A2" "GroupB" HistoryRecord.empty

              // GroupA history should be recorded
              Expect.isSome (HistoryRecord.tryGet "GroupA" inB.HistoryRecord) "GroupA history recorded on exit"

              // Now transition B1 -> B2, PASSING the accumulated history
              let inB2 =
                  HierarchicalRuntime.transition hierarchy inB.Configuration "B1" "B2" inB.HistoryRecord

              // BUG: if transition starts from HistoryRecord.empty, GroupA's history is lost
              Expect.isSome
                  (HistoryRecord.tryGet "GroupA" inB2.HistoryRecord)
                  "GroupA history preserved during unrelated transition" ]

// ==========================================================================
// Bug 3: AND composite exit must deactivate all regions (#224)
// ==========================================================================

[<Tests>]
let andCompositeExitTests =
    testList
        "AND composite exit deactivates all regions (#224)"
        [ testCase "transitioning out of AND composite deactivates all region states"
          <| fun () ->
              // Device (AND) has Display and Network regions.
              // We add an "Outer" parent so we can transition out of Device.
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = "Outer"
                              Kind = CompositeKind.XOR
                              Children = [ Device.device; "Standby" ]
                              InitialChild = Some Device.device
                              CompletionTarget = None }
                            { Id = Device.device
                              Kind = CompositeKind.AND
                              Children = [ Device.display; Device.network ]
                              InitialChild = None
                              CompletionTarget = None }
                            { Id = Device.display
                              Kind = CompositeKind.XOR
                              Children = [ Device.screenOn; Device.screenOff ]
                              InitialChild = Some Device.screenOn
                              CompletionTarget = None }
                            { Id = Device.network
                              Kind = CompositeKind.XOR
                              Children = [ Device.connected; Device.disconnected ]
                              InitialChild = Some Device.connected
                              CompletionTarget = None } ] }

              // Enter Device -> all regions active
              let initial =
                  ActiveStateConfiguration.empty
                  |> ActiveStateConfiguration.add "Outer"
                  |> fun c -> HierarchicalRuntime.enterState hierarchy Device.device c

              // Verify all regions are active
              Expect.isTrue (ActiveStateConfiguration.isActive Device.screenOn initial) "ScreenOn active"
              Expect.isTrue (ActiveStateConfiguration.isActive Device.connected initial) "Connected active"

              // Transition from ScreenOn (Display region) to Standby (outside Device)
              let result =
                  HierarchicalRuntime.transition hierarchy initial Device.screenOn "Standby" HistoryRecord.empty

              // ALL Device descendant states should be deactivated
              Expect.isFalse
                  (ActiveStateConfiguration.isActive Device.screenOn result.Configuration)
                  "ScreenOn deactivated"

              Expect.isFalse
                  (ActiveStateConfiguration.isActive Device.display result.Configuration)
                  "Display deactivated"

              Expect.isFalse
                  (ActiveStateConfiguration.isActive Device.connected result.Configuration)
                  "Connected deactivated (sibling region)"

              Expect.isFalse
                  (ActiveStateConfiguration.isActive Device.network result.Configuration)
                  "Network deactivated (sibling region)"

              Expect.isFalse
                  (ActiveStateConfiguration.isActive Device.device result.Configuration)
                  "Device itself deactivated"

              Expect.isTrue (ActiveStateConfiguration.isActive "Standby" result.Configuration) "Standby is active" ]

// ==========================================================================
// Improvement 4: Internal vs external self-transitions (#224)
// ==========================================================================

[<Tests>]
let selfTransitionTests =
    testList
        "Internal vs external self-transitions (#224)"
        [ testCase "external self-transition exits and re-enters the state"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              let initial =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              // Move to Green first
              let inGreen =
                  HierarchicalRuntime.transition
                      hierarchy
                      initial
                      TrafficLight.red
                      TrafficLight.green
                      HistoryRecord.empty

              // Self-transition on Active (external): should exit Active+Green, re-enter Active+Red
              let result =
                  HierarchicalRuntime.transition
                      hierarchy
                      inGreen.Configuration
                      TrafficLight.active
                      TrafficLight.active
                      HistoryRecord.empty

              // External self-transition: exits the state and re-enters it (resets to initial child)
              Expect.isTrue
                  (ActiveStateConfiguration.isActive TrafficLight.red result.Configuration)
                  "Re-entered to initial child Red"

              Expect.isFalse
                  (ActiveStateConfiguration.isActive TrafficLight.green result.Configuration)
                  "Green no longer active after self-transition"

              Expect.isTrue
                  (ActiveStateConfiguration.isActive TrafficLight.active result.Configuration)
                  "Active still active" ]

// ==========================================================================
// Improvement 5: XOR exclusivity enforcement (#224)
// ==========================================================================

[<Tests>]
let xorExclusivityTests =
    testList
        "XOR exclusivity enforcement (#224)"
        [ testCase "enterState on XOR composite deactivates previous sibling"
          <| fun () ->
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red
                              CompletionTarget = None } ] }

              // Manually create invalid config: both Red and Green active in XOR composite
              let invalidConfig =
                  ActiveStateConfiguration.empty
                  |> ActiveStateConfiguration.add TrafficLight.active
                  |> ActiveStateConfiguration.add TrafficLight.red
                  |> ActiveStateConfiguration.add TrafficLight.green

              // enterState should enforce XOR by deactivating other children
              let result =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.yellow invalidConfig

              Expect.isTrue (ActiveStateConfiguration.isActive TrafficLight.yellow result) "Yellow is active"

              // XOR: only one child should be active
              let activeChildren =
                  [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                  |> List.filter (fun c -> ActiveStateConfiguration.isActive c result)

              Expect.equal activeChildren.Length 1 "Only one child active in XOR composite"
              Expect.equal activeChildren.Head TrafficLight.yellow "That child is Yellow" ]

// ==========================================================================
// Issue #265: enterState must include all ancestors up to root
//
// Hierarchy: Order > Processing > Payment > Authorize
//            Order > Pending (initial)
//
// Harel semantics: a state is active iff it or any descendant is the
// current atomic state. All ancestors must be in the active configuration.
// ==========================================================================

/// State identifiers for the order fulfillment hierarchy (issue #265).
[<RequireQualifiedAccess>]
module OrderFulfillment =
    let order = "Order"
    let pending = "Pending"
    let processing = "Processing"
    let payment = "Payment"
    let authorize = "Authorize"

/// Build the order fulfillment hierarchy used by issue #265 tests.
let private orderHierarchy =
    StateHierarchy.build
        { States =
            [ { Id = OrderFulfillment.order
                Kind = CompositeKind.XOR
                Children = [ OrderFulfillment.pending; OrderFulfillment.processing ]
                InitialChild = Some OrderFulfillment.pending
                CompletionTarget = None }
              { Id = OrderFulfillment.processing
                Kind = CompositeKind.XOR
                Children = [ OrderFulfillment.payment ]
                InitialChild = Some OrderFulfillment.payment
                CompletionTarget = None }
              { Id = OrderFulfillment.payment
                Kind = CompositeKind.XOR
                Children = [ OrderFulfillment.authorize ]
                InitialChild = Some OrderFulfillment.authorize
                CompletionTarget = None } ] }

[<Tests>]
let enterStateAncestorTests =
    testList
        "Issue #265: enterState includes all ancestors"
        [
          // Acceptance test 1: Initial entry includes root
          testCase "initial entry into Pending includes Order (root)"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState orderHierarchy OrderFulfillment.pending ActiveStateConfiguration.empty

              let active = ActiveStateConfiguration.toSet config

              Expect.isTrue
                  (Set.contains OrderFulfillment.order active)
                  "Order (root) must be in active config when entering Pending"

              Expect.isTrue
                  (Set.contains OrderFulfillment.pending active)
                  "Pending must be in active config"

              Expect.equal active (Set.ofList [ OrderFulfillment.order; OrderFulfillment.pending ]) "Active config = {Order, Pending}"

          // Acceptance test 2: Transition preserves full ancestor chain
          testCase "transition Pending → Authorize includes all four ancestors"
          <| fun () ->
              // Start with initial entry into Pending
              let initialConfig =
                  HierarchicalRuntime.enterState orderHierarchy OrderFulfillment.pending ActiveStateConfiguration.empty

              // Transition Pending → Authorize
              let result =
                  HierarchicalRuntime.transition
                      orderHierarchy
                      initialConfig
                      OrderFulfillment.pending
                      OrderFulfillment.authorize
                      HistoryRecord.empty

              let active = ActiveStateConfiguration.toSet result.Configuration

              Expect.equal
                  active
                  (Set.ofList
                      [ OrderFulfillment.order
                        OrderFulfillment.processing
                        OrderFulfillment.payment
                        OrderFulfillment.authorize ])
                  "Active config = {Order, Processing, Payment, Authorize}"

          // Acceptance test 3: Root never absent from active config
          testCase "Order is always in active config for any reachable state"
          <| fun () ->
              // Test every reachable atomic state
              let allAtomicStates =
                  [ OrderFulfillment.pending; OrderFulfillment.authorize ]

              for atomicState in allAtomicStates do
                  let config =
                      HierarchicalRuntime.enterState orderHierarchy atomicState ActiveStateConfiguration.empty

                  Expect.isTrue
                      (ActiveStateConfiguration.isActive OrderFulfillment.order config)
                      (sprintf "Order must be active when in state %s" atomicState)

          // Harel finding 5: enterWithHistory must include ancestors even on
          // degenerate path where no child enterState call occurs.
          // Shallow history on a composite with no matching child AND no initial child
          // returns without calling enterState — ancestors would be missing.
          testCase "enterWithHistory includes ancestors on degenerate shallow path"
          <| fun () ->
              // Root (XOR) > Inner (XOR, no initial child, no children in history)
              let hierarchy =
                  StateHierarchy.build
                      { States =
                          [ { Id = "Root"
                              Kind = CompositeKind.XOR
                              Children = [ "Inner"; "Other" ]
                              InitialChild = Some "Inner"
                              CompletionTarget = None }
                            { Id = "Inner"
                              Kind = CompositeKind.XOR
                              Children = [ "A"; "B" ]
                              // No initial child — degenerate
                              InitialChild = None
                              CompletionTarget = None } ] }

              // Fabricate a history record where Inner's recorded config has a state
              // that is NOT a current child of Inner (simulates stale/empty match)
              let staleHistory =
                  HistoryRecord.record "Inner" ActiveStateConfiguration.empty HistoryRecord.empty

              // enterWithHistory on Inner from empty config with stale history:
              // shallow history finds no matching child, no initial child → returns early
              let restored =
                  HierarchicalRuntime.enterWithHistory
                      hierarchy
                      HistoryKind.Shallow
                      "Inner"
                      ActiveStateConfiguration.empty
                      staleHistory

              // Root must still be in config — enterWithHistory must add ancestors
              Expect.isTrue
                  (ActiveStateConfiguration.isActive "Root" restored)
                  "Root must be in config even on degenerate enterWithHistory path"

              Expect.isTrue
                  (ActiveStateConfiguration.isActive "Inner" restored)
                  "Inner must be in config" ]

// ==========================================================================
// Composite StateKind in AST
// ==========================================================================

[<Tests>]
let astCompositeKindTests =
    testList
        "AST StateKind.Composite"
        [ testCase "StateKind.Composite can be constructed"
          <| fun () ->
              let kind = Frank.Statecharts.Ast.StateKind.Composite

              match kind with
              | Frank.Statecharts.Ast.StateKind.Composite -> ()
              | _ -> failtest "Expected Composite" ]
