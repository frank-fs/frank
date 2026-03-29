module Frank.Resources.Model.Tests.HierarchyProjectionTests

open Expecto
open FsCheck
open FsCheck.FSharp
open Frank.Resources.Model
open Frank.Resources.Model.Tests.TestHelpers

// -- Test fixtures --

/// Hierarchical chart where "Active" is a composite with children "Playing" and "Paused".
/// Only explicit transitions connect Active -> Done.
/// Children (Playing, Paused) are reachable via implicit initial-child entry of Active,
/// NOT via explicit transitions from the initial state.
let private hierarchicalChart: ExtractedStatechart =
    { RouteTemplate = "/games/{gameId}"
      StateNames = [ "Active"; "Playing"; "Paused"; "Done" ]
      InitialStateKey = "Active"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Active",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None }
              "Playing",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "Paused",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "Done",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None } ]
      Roles =
        [ { Name = "Player"; Description = None }
          { Name = "Spectator"
            Description = None } ]
      Transitions =
        [ // Active self-loop (observation)
          mkTransition "getGame" "Active" "Active" None Unrestricted
          // Active -> Done (exit composite)
          mkTransition "finish" "Active" "Done" None (RestrictedTo [ "Player" ])
          // Children progress: Playing <-> Paused
          mkTransition "pause" "Playing" "Paused" None (RestrictedTo [ "Player" ])
          mkTransition "resume" "Paused" "Playing" None (RestrictedTo [ "Player" ])
          mkTransition "getGame" "Done" "Done" None Unrestricted ] }

let private hierarchicalContainment: StateContainment =
    StateContainment.ofPairs [ ("Active", [ "Playing"; "Paused" ]) ]

/// Chart where child states have no explicit transitions TO them from outside the parent.
/// Without hierarchy awareness, Playing and Paused would be pruned as unreachable.
let private implicitEntryChart: ExtractedStatechart =
    { RouteTemplate = "/workflows/{id}"
      StateNames = [ "Running"; "StepA"; "StepB"; "Completed" ]
      InitialStateKey = "Running"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Running",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None }
              "StepA",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "StepB",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "Completed",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None } ]
      Roles = [ { Name = "Worker"; Description = None } ]
      Transitions =
        [ // Running is a composite, but only has an explicit transition to Completed
          mkTransition "complete" "Running" "Completed" None (RestrictedTo [ "Worker" ])
          mkTransition "view" "Running" "Running" None Unrestricted
          // Children transition internally
          mkTransition "advance" "StepA" "StepB" None (RestrictedTo [ "Worker" ])
          mkTransition "view" "Completed" "Completed" None Unrestricted ] }

let private implicitEntryContainment: StateContainment =
    StateContainment.ofPairs [ ("Running", [ "StepA"; "StepB" ]) ]

// -- Tests --

[<Tests>]
let hierarchyPruneTests =
    testList
        "Projection.pruneUnreachableStates hierarchy-aware"
        [ testCase "child states of reachable composite are retained (implicit entry)"
          <| fun _ ->
              let pruned =
                  Projection.pruneUnreachableStatesWithHierarchy hierarchicalContainment hierarchicalChart

              Expect.isTrue
                  (List.contains "Playing" pruned.StateNames)
                  "Playing retained as child of reachable Active"

              Expect.isTrue
                  (List.contains "Paused" pruned.StateNames)
                  "Paused retained as child of reachable Active"

          testCase "implicit-entry children not pruned when no explicit transition targets them"
          <| fun _ ->
              // Without hierarchy: StepA and StepB would be pruned (no transition targets them)
              let prunedFlat = Projection.pruneUnreachableStates implicitEntryChart

              Expect.isFalse
                  (List.contains "StepA" prunedFlat.StateNames)
                  "Without hierarchy, StepA is pruned"

              // With hierarchy: StepA and StepB are retained as children of Running
              let prunedHierarchical =
                  Projection.pruneUnreachableStatesWithHierarchy implicitEntryContainment implicitEntryChart

              Expect.isTrue
                  (List.contains "StepA" prunedHierarchical.StateNames)
                  "With hierarchy, StepA is retained"

              Expect.isTrue
                  (List.contains "StepB" prunedHierarchical.StateNames)
                  "With hierarchy, StepB is retained"

          testCase "flat chart with empty containment behaves same as original"
          <| fun _ ->
              let flatChart: ExtractedStatechart =
                  { RouteTemplate = "/docs"
                    StateNames = [ "Draft"; "Published"; "Orphan" ]
                    InitialStateKey = "Draft"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "Draft",
                            { AllowedMethods = [ "GET"; "PUT" ]
                              IsFinal = false
                              Description = None }
                            "Published",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None }
                            "Orphan",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None } ]
                    Roles = [ { Name = "Editor"; Description = None } ]
                    Transitions =
                      [ mkTransition "publish" "Draft" "Published" None Unrestricted
                        mkTransition "view" "Draft" "Draft" None Unrestricted
                        mkTransition "view" "Published" "Published" None Unrestricted
                        mkTransition "orphanAction" "Orphan" "Draft" None Unrestricted ] }

              let original = Projection.pruneUnreachableStates flatChart

              let withEmptyHierarchy =
                  Projection.pruneUnreachableStatesWithHierarchy StateContainment.empty flatChart

              Expect.equal withEmptyHierarchy.StateNames original.StateNames "Same behavior with empty containment"

          testCase "transitions referencing hierarchy-retained children are preserved"
          <| fun _ ->
              let pruned =
                  Projection.pruneUnreachableStatesWithHierarchy implicitEntryContainment implicitEntryChart

              let advanceTransitions =
                  pruned.Transitions |> List.filter (fun t -> t.Event = "advance")

              Expect.isNonEmpty advanceTransitions "advance (StepA -> StepB) is preserved"

          testCase "grandchildren of reachable composites are also retained"
          <| fun _ ->
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/nested"
                    StateNames = [ "Root"; "Parent"; "Child"; "GrandChild"; "Done" ]
                    InitialStateKey = "Root"
                    GuardNames = []
                    StateMetadata =
                      [ "Root"; "Parent"; "Child"; "GrandChild"; "Done" ]
                      |> List.map (fun s ->
                          s,
                          { AllowedMethods = [ "GET" ]
                            IsFinal = (s = "Done")
                            Description = None })
                      |> Map.ofList
                    Roles = [ { Name = "User"; Description = None } ]
                    Transitions =
                      [ mkTransition "view" "Root" "Root" None Unrestricted
                        mkTransition "exit" "Root" "Done" None (RestrictedTo [ "User" ])
                        mkTransition "view" "Done" "Done" None Unrestricted ] }

              let containment =
                  StateContainment.ofPairs [ ("Root", [ "Parent" ]); ("Parent", [ "Child"; "GrandChild" ]) ]

              let pruned =
                  Projection.pruneUnreachableStatesWithHierarchy containment chart

              Expect.isTrue (List.contains "Parent" pruned.StateNames) "Parent retained"
              Expect.isTrue (List.contains "Child" pruned.StateNames) "Child retained"
              Expect.isTrue (List.contains "GrandChild" pruned.StateNames) "GrandChild retained" ]

// -- FsCheck property tests --

/// Generator for a list of distinct child state names.
let private genChildNames: Gen<string list> =
    Gen.elements [ "Alpha"; "Beta"; "Gamma"; "Delta"; "Epsilon"; "Zeta"; "Eta"; "Theta" ]
    |> Gen.listOfLength 3
    |> Gen.map (List.distinct >> fun xs -> if xs.Length < 1 then [ "ChildA" ] else xs)

[<Tests>]
let hierarchyPrunePropertyTests =
    testList
        "Hierarchy pruning FsCheck properties"
        [ testCase "children of reachable composites are always in pruned result (property)"
          <| fun _ ->
              Prop.forAll
                  (Arb.fromGen genChildNames)
                  (fun childNames ->
                      let parentState = "Parent"
                      let finalState = "Final"
                      let allStates = parentState :: finalState :: childNames

                      let containment =
                          StateContainment.ofPairs [ (parentState, childNames) ]

                      let chart: ExtractedStatechart =
                          { RouteTemplate = "/test"
                            StateNames = allStates
                            InitialStateKey = parentState
                            GuardNames = []
                            StateMetadata =
                              allStates
                              |> List.map (fun s ->
                                  s,
                                  { AllowedMethods = [ "GET" ]
                                    IsFinal = (s = finalState)
                                    Description = None })
                              |> Map.ofList
                            Roles = [ { Name = "User"; Description = None } ]
                            Transitions =
                              [ mkTransition "view" parentState parentState None Unrestricted
                                mkTransition "exit" parentState finalState None (RestrictedTo [ "User" ])
                                mkTransition "view" finalState finalState None Unrestricted ] }

                      let pruned =
                          Projection.pruneUnreachableStatesWithHierarchy containment chart

                      childNames |> List.forall (fun child -> List.contains child pruned.StateNames))
              |> Check.QuickThrowOnFailure

          testCase "hierarchy pruning is superset of flat pruning (property)"
          <| fun _ ->
              Prop.forAll
                  (Arb.fromGen genChildNames)
                  (fun childNames ->
                      let parentState = "Parent"
                      let finalState = "Final"
                      let allStates = parentState :: finalState :: childNames

                      let containment =
                          StateContainment.ofPairs [ (parentState, childNames) ]

                      let chart: ExtractedStatechart =
                          { RouteTemplate = "/test"
                            StateNames = allStates
                            InitialStateKey = parentState
                            GuardNames = []
                            StateMetadata =
                              allStates
                              |> List.map (fun s ->
                                  s,
                                  { AllowedMethods = [ "GET" ]
                                    IsFinal = (s = finalState)
                                    Description = None })
                              |> Map.ofList
                            Roles = [ { Name = "User"; Description = None } ]
                            Transitions =
                              [ mkTransition "view" parentState parentState None Unrestricted
                                mkTransition "exit" parentState finalState None (RestrictedTo [ "User" ])
                                mkTransition "view" finalState finalState None Unrestricted ] }

                      let flatPruned = Projection.pruneUnreachableStates chart

                      let hierarchyPruned =
                          Projection.pruneUnreachableStatesWithHierarchy containment chart

                      let flatSet = flatPruned.StateNames |> Set.ofList
                      let hierarchySet = hierarchyPruned.StateNames |> Set.ofList
                      Set.isSubset flatSet hierarchySet)
              |> Check.QuickThrowOnFailure ]
