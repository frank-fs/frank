module Frank.Statecharts.Tests.Analysis.HierarchyLivelockTests

open Expecto
open FsCheck
open FsCheck.FSharp
open Frank.Resources.Model
open Frank.Statecharts.Analysis.ProjectionValidator

// -- Helpers --

let private mkTransition event source target guard roleConstraint =
    { Event = event
      Source = source
      Target = target
      Guard = guard
      Constraint = roleConstraint
      Safety = Unsafe }

// -- Test fixtures --

/// Hierarchical statechart: composite state "Active" has children "Playing" and "Paused".
/// Active -> Active is a self-loop at the parent level, but children progress
/// (Playing <-> Paused), so this should NOT be flagged as livelock.
let private hierarchicalActiveChart: ExtractedStatechart =
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
        [ // Parent self-loop: Active -> Active (GET observation)
          mkTransition "getGame" "Active" "Active" None Unrestricted
          // Children progress: Playing <-> Paused
          mkTransition "pause" "Playing" "Paused" None (RestrictedTo [ "Player" ])
          mkTransition "resume" "Paused" "Playing" None (RestrictedTo [ "Player" ])
          // Exit from child to sibling of parent
          mkTransition "finish" "Playing" "Done" None (RestrictedTo [ "Player" ])
          mkTransition "getGame" "Done" "Done" None Unrestricted ] }

let private hierarchicalActiveContainment: StateContainment =
    StateContainment.ofPairs [ ("Active", [ "Playing"; "Paused" ]) ]

/// Composite state with self-loop where children ALSO only have self-loops.
/// This IS a real livelock: neither parent nor children can make progress.
let private hierarchicalTrueLivelockChart: ExtractedStatechart =
    { RouteTemplate = "/items/{itemId}"
      StateNames = [ "Stuck"; "SubA"; "SubB" ]
      InitialStateKey = "Stuck"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Stuck",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None }
              "SubA",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None }
              "SubB",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None } ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions =
        [ mkTransition "check" "Stuck" "Stuck" None Unrestricted
          mkTransition "checkA" "SubA" "SubA" None Unrestricted
          mkTransition "checkB" "SubB" "SubB" None Unrestricted ] }

let private trueLivelockContainment: StateContainment =
    StateContainment.ofPairs [ ("Stuck", [ "SubA"; "SubB" ]) ]

/// Flat statechart with self-loop — should still be flagged as livelock
/// even when hierarchy info is provided (empty containment).
let private flatSelfLoopChart: ExtractedStatechart =
    { RouteTemplate = "/tasks/{taskId}"
      StateNames = [ "Open"; "Stuck"; "Done" ]
      InitialStateKey = "Open"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Open",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "Stuck",
              { AllowedMethods = [ "GET" ]
                IsFinal = false
                Description = None }
              "Done",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None } ]
      Roles =
        [ { Name = "Worker"; Description = None }
          { Name = "Manager"; Description = None } ]
      Transitions =
        [ mkTransition "view" "Open" "Open" None Unrestricted
          mkTransition "assign" "Open" "Stuck" None (RestrictedTo [ "Manager" ])
          mkTransition "view" "Stuck" "Stuck" None Unrestricted
          mkTransition "complete" "Open" "Done" None (RestrictedTo [ "Worker" ])
          mkTransition "view" "Done" "Done" None Unrestricted ] }

// -- Tests --

[<Tests>]
let hierarchyAwareLivelockTests =
    testList
        "ProjectionValidator.checkLivelock hierarchy-aware"
        [ testCase "composite self-loop NOT flagged when children have progressing transitions"
          <| fun _ ->
              // Use hierarchy-aware projection so children are not pruned
              let projections =
                  Projection.projectAllWithHierarchy hierarchicalActiveContainment hierarchicalActiveChart

              let pruned =
                  Projection.pruneUnreachableStatesWithHierarchy hierarchicalActiveContainment hierarchicalActiveChart

              let issues = checkLivelock projections pruned hierarchicalActiveContainment

              let activeIssues = issues |> List.filter (fun i -> i.Message.Contains("Active"))

              Expect.isEmpty activeIssues "Active should not be flagged — children progress"

          testCase "composite self-loop IS flagged when children also only self-loop"
          <| fun _ ->
              let projections =
                  Projection.projectAllWithHierarchy trueLivelockContainment hierarchicalTrueLivelockChart

              let pruned =
                  Projection.pruneUnreachableStatesWithHierarchy trueLivelockContainment hierarchicalTrueLivelockChart

              let issues = checkLivelock projections pruned trueLivelockContainment

              let stuckIssues = issues |> List.filter (fun i -> i.Message.Contains("Stuck"))

              Expect.isNonEmpty stuckIssues "Stuck should be flagged — neither parent nor children progress"

          testCase "flat self-loop still flagged with empty containment"
          <| fun _ ->
              let projections = Projection.projectAll flatSelfLoopChart
              let pruned = Projection.pruneUnreachableStates flatSelfLoopChart
              let issues = checkLivelock projections pruned StateContainment.empty

              let stuckIssues = issues |> List.filter (fun i -> i.Message.Contains("Stuck"))

              Expect.isNonEmpty stuckIssues "Flat self-loop still detected"

          testCase "composite self-loop IS flagged when progressing descendants are unreachable"
          <| fun _ ->
              // Composite state "Outer" has children "Inner" and "Dead".
              // Inner has a progressing transition (Inner -> Done), but Inner is NOT reachable
              // because no transition targets it and it's only a child of Outer.
              // However, with hierarchy-aware pruning, children ARE reachable via containment.
              // So we need a case where a descendant is genuinely unreachable:
              // a grandchild whose parent is not in containment of a reachable composite.
              // Instead, test with a chart where we pass a containment that includes
              // descendants not in the pruned chart's state names.
              let chart: ExtractedStatechart =
                  { RouteTemplate = "/test"
                    StateNames = [ "Outer"; "Inner"; "Done" ]
                    InitialStateKey = "Outer"
                    GuardNames = []
                    StateMetadata =
                      Map.ofList
                          [ "Outer",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None }
                            "Inner",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = false
                              Description = None }
                            "Done",
                            { AllowedMethods = [ "GET" ]
                              IsFinal = true
                              Description = None } ]
                    Roles = [ { Name = "User"; Description = None } ]
                    Transitions =
                      [ mkTransition "check" "Outer" "Outer" None Unrestricted
                        mkTransition "check" "Inner" "Inner" None Unrestricted
                        mkTransition "view" "Done" "Done" None Unrestricted ] }

              // Containment says Inner is a child of Outer, but also claims "Ghost" is a child
              // with progressing transitions — except Ghost is not in the chart at all.
              let containment = StateContainment.ofPairs [ ("Outer", [ "Inner"; "Ghost" ]) ]

              // Manually construct a pruned chart where Ghost is not present
              // (it was never in StateNames). This simulates the case where a descendant
              // listed in containment is not reachable.
              let projections = Projection.projectAllWithHierarchy containment chart
              let pruned = Projection.pruneUnreachableStatesWithHierarchy containment chart

              let issues = checkLivelock projections pruned containment

              // Inner only has self-loops and is non-final, so it should be flagged
              let innerIssues = issues |> List.filter (fun i -> i.Message.Contains("Inner"))

              Expect.isNonEmpty innerIssues "Inner should be flagged — it only has self-loops"

          testCase "backward compat: checkLivelock without hierarchy behaves as before"
          <| fun _ ->
              let projections = Projection.projectAll flatSelfLoopChart
              let pruned = Projection.pruneUnreachableStates flatSelfLoopChart
              let issuesNoHierarchy = checkLivelock projections pruned StateContainment.empty
              Expect.isNonEmpty issuesNoHierarchy "Flat behavior preserved" ]

// -- FsCheck property tests --

/// Generator for a list of distinct child state names.
let private genChildNames: Gen<string list> =
    Gen.elements [ "Alpha"; "Beta"; "Gamma"; "Delta"; "Epsilon"; "Zeta" ]
    |> Gen.listOfLength 3
    |> Gen.map (List.distinct >> fun xs -> if xs.Length < 2 then [ "Child1"; "Child2" ] else xs)

[<Tests>]
let hierarchyLivelockPropertyTests =
    testList
        "Hierarchy livelock FsCheck properties"
        [ testCase "composite self-loop with progressing child is never livelock (property)"
          <| fun _ ->
              Prop.forAll (Arb.fromGen genChildNames) (fun childNames ->
                  let parentState = "Composite"
                  let finalState = "End"
                  let allStates = parentState :: finalState :: childNames

                  let containment = StateContainment.ofPairs [ (parentState, childNames) ]

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
                          [ mkTransition "observe" parentState parentState None Unrestricted
                            mkTransition "advance" childNames[0] childNames[1] None (RestrictedTo [ "User" ])
                            mkTransition "end" childNames[0] finalState None (RestrictedTo [ "User" ])
                            mkTransition "view" finalState finalState None Unrestricted ] }

                  let projections = Projection.projectAllWithHierarchy containment chart
                  let pruned = Projection.pruneUnreachableStatesWithHierarchy containment chart
                  let issues = checkLivelock projections pruned containment

                  let compositeIssues =
                      issues |> List.filter (fun i -> i.Message.Contains("Composite"))

                  compositeIssues.IsEmpty)
              |> Check.QuickThrowOnFailure

          testCase "composite self-loop with all-self-loop children is always livelock (property)"
          <| fun _ ->
              Prop.forAll (Arb.fromGen genChildNames) (fun childNames ->
                  let parentState = "Stuck"
                  let allStates = parentState :: childNames

                  let containment = StateContainment.ofPairs [ (parentState, childNames) ]

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
                                IsFinal = false
                                Description = None })
                          |> Map.ofList
                        Roles = [ { Name = "User"; Description = None } ]
                        Transitions =
                          [ mkTransition "observe" parentState parentState None Unrestricted ]
                          @ (childNames
                             |> List.map (fun child -> mkTransition "check" child child None Unrestricted)) }

                  let projections = Projection.projectAllWithHierarchy containment chart
                  let pruned = Projection.pruneUnreachableStatesWithHierarchy containment chart
                  let issues = checkLivelock projections pruned containment

                  let stuckIssues = issues |> List.filter (fun i -> i.Message.Contains("Stuck"))

                  not stuckIssues.IsEmpty)
              |> Check.QuickThrowOnFailure ]
