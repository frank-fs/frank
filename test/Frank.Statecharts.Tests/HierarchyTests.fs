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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

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
              Expect.isEmpty (Map.toList hierarchy.StateKind) "no kinds" ]

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

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
                              InitialChild = Some TrafficLight.active } ] }

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

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
                              InitialChild = Some TrafficLight.active } ] }

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

              // Start in Red (inside Active)
              let initial =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              // Transition from Red to Green
              let result =
                  HierarchicalRuntime.transition hierarchy initial TrafficLight.red TrafficLight.green

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

              let initial =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              let result =
                  HierarchicalRuntime.transition hierarchy initial TrafficLight.red TrafficLight.green

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

              let initial =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              // Transition from Red (inside Active) to Off (sibling of Active)
              let result =
                  HierarchicalRuntime.transition hierarchy initial TrafficLight.red TrafficLight.off

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

              // Start in Off
              let initial =
                  ActiveStateConfiguration.empty
                  |> ActiveStateConfiguration.add TrafficLight.off
                  |> ActiveStateConfiguration.add TrafficLight.root

              let result =
                  HierarchicalRuntime.transition hierarchy initial TrafficLight.off TrafficLight.active

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
                              InitialChild = None }
                            { Id = Device.display
                              Kind = CompositeKind.XOR
                              Children = [ Device.screenOn; Device.screenOff ]
                              InitialChild = Some Device.screenOn }
                            { Id = Device.network
                              Kind = CompositeKind.XOR
                              Children = [ Device.connected; Device.disconnected ]
                              InitialChild = Some Device.connected } ] }

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
                              InitialChild = None }
                            { Id = Device.display
                              Kind = CompositeKind.XOR
                              Children = [ Device.screenOn; Device.screenOff ]
                              InitialChild = Some Device.screenOn }
                            { Id = Device.network
                              Kind = CompositeKind.XOR
                              Children = [ Device.connected; Device.disconnected ]
                              InitialChild = Some Device.connected } ] }

              let initial =
                  HierarchicalRuntime.enterState hierarchy Device.device ActiveStateConfiguration.empty

              // Transition ScreenOn -> ScreenOff (Display region only)
              let result =
                  HierarchicalRuntime.transition hierarchy initial Device.screenOn Device.screenOff

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

              // Start in Green (inside Active)
              let initial =
                  HierarchicalRuntime.enterState hierarchy TrafficLight.active ActiveStateConfiguration.empty

              let inGreen =
                  HierarchicalRuntime.transition hierarchy initial TrafficLight.red TrafficLight.green

              // Exit Active -> Off
              let inOff =
                  HierarchicalRuntime.transition hierarchy inGreen.Configuration TrafficLight.green TrafficLight.off

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

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
                              InitialChild = Some "Active" }
                            { Id = "Active"
                              Kind = CompositeKind.XOR
                              Children = [ "SubModeA"; "SubModeB" ]
                              InitialChild = Some "SubModeA" }
                            { Id = "SubModeA"
                              Kind = CompositeKind.XOR
                              Children = [ "ChildX"; "ChildY" ]
                              InitialChild = Some "ChildX" } ] }

              // Build up state: Machine -> Active -> SubModeA -> ChildY
              let initial =
                  HierarchicalRuntime.enterState hierarchy "Machine" ActiveStateConfiguration.empty

              let moved = HierarchicalRuntime.transition hierarchy initial "ChildX" "ChildY"

              // Exit to Idle
              let exited =
                  HierarchicalRuntime.transition hierarchy moved.Configuration "ChildY" "Idle"

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
                              InitialChild = Some TrafficLight.red } ] }

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
                              InitialChild = Some TrafficLight.red } ] }

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
                              InitialChild = Some TrafficLight.active }
                            { Id = TrafficLight.active
                              Kind = CompositeKind.XOR
                              Children = [ TrafficLight.red; TrafficLight.yellow; TrafficLight.green ]
                              InitialChild = Some TrafficLight.red } ] }

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
