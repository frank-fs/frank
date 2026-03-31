/// Tests for MPST formalism bounds documentation (issue #244).
///
/// These tests verify the observable behavior of formalism-bound guards and
/// the corrected semantics documented by issue #244:
///
///   1. AND-state dual derivation gap: DeriveResult.Warnings surfaces when
///      deriveWithHierarchy is called with AND-state composites in the hierarchy.
///   2. XOR-only hierarchies produce no AND-state warnings.
///   3. Flat (no-hierarchy) derivations produce no AND-state warnings.
///   4. Unrestricted RoleConstraint semantics: any role may trigger independently
///      (shared-input), NOT sent to all roles simultaneously (broadcast).
module Frank.Statecharts.Tests.DualFormalismBoundsTests

open Expecto
open Frank.Resources.Model
open Frank.Statecharts
open Frank.Statecharts.Dual

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private mkTransition event source target guard roleConstraint =
    { Event = event
      Source = source
      Target = target
      Guard = guard
      Constraint = roleConstraint }

// ---------------------------------------------------------------------------
// Minimal flat statechart fixture (XTurn -> OTurn -> XWins, 2 roles)
// ---------------------------------------------------------------------------

let private minimalChart: ExtractedStatechart =
    { RouteTemplate = "/games/{gameId}"
      StateNames = [ "XTurn"; "OTurn"; "XWins" ]
      InitialStateKey = "XTurn"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "XTurn",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "OTurn",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "XWins",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None } ]
      Roles =
        [ { Name = "PlayerX"; Description = None }
          { Name = "PlayerO"; Description = None } ]
      Transitions =
        [ mkTransition "getGame" "XTurn" "XTurn" None Unrestricted
          mkTransition "getGame" "OTurn" "OTurn" None Unrestricted
          mkTransition "getGame" "XWins" "XWins" None Unrestricted
          mkTransition "makeMove" "XTurn" "OTurn" None (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "OTurn" "XWins" None (RestrictedTo [ "PlayerO" ]) ] }

// ---------------------------------------------------------------------------
// XOR-only hierarchy fixture
// ---------------------------------------------------------------------------

let private xorHierarchySpec: HierarchySpec =
    { States =
        [ { Id = "Active"
            Kind = CompositeKind.XOR
            Children = [ "XTurn"; "OTurn" ]
            InitialChild = Some "XTurn" } ] }

let private xorHierarchy: StateHierarchy = StateHierarchy.build xorHierarchySpec

// ---------------------------------------------------------------------------
// AND-state hierarchy fixture: "Voting" composite with parallel regions
// ---------------------------------------------------------------------------

let private andHierarchySpec: HierarchySpec =
    { States =
        [ { Id = "Voting"
            Kind = CompositeKind.AND
            Children = [ "RegionA"; "RegionB" ]
            InitialChild = None } ] }

let private andHierarchy: StateHierarchy = StateHierarchy.build andHierarchySpec

// ---------------------------------------------------------------------------
// Mixed AND+XOR hierarchy fixture
// ---------------------------------------------------------------------------

let private mixedHierarchySpec: HierarchySpec =
    { States =
        [ { Id = "Root"
            Kind = CompositeKind.XOR
            Children = [ "Voting"; "Done" ]
            InitialChild = Some "Voting" }
          { Id = "Voting"
            Kind = CompositeKind.AND
            Children = [ "RegionA"; "RegionB" ]
            InitialChild = None } ] }

let private mixedHierarchy: StateHierarchy = StateHierarchy.build mixedHierarchySpec

// ---------------------------------------------------------------------------
// Test: AND-state hierarchy emits a warning in DeriveResult.Warnings
// ---------------------------------------------------------------------------

[<Tests>]
let andStateWarningTests =
    let projections = Projection.projectAll minimalChart

    testList
        "AND-state dual derivation gap: warnings surfaced"
        [ testCase "deriveWithHierarchy with AND-state emits a warning"
          <| fun _ ->
              let result =
                  deriveWithHierarchy minimalChart projections Map.empty Map.empty (Some andHierarchy)

              Expect.isNonEmpty result.Warnings "AND-state hierarchy should produce at least one warning"

          testCase "AND-state warning mentions AND or parallel"
          <| fun _ ->
              let result =
                  deriveWithHierarchy minimalChart projections Map.empty Map.empty (Some andHierarchy)

              let hasAndWarning =
                  result.Warnings
                  |> List.exists (fun w ->
                      w.ToLowerInvariant().Contains("and")
                      || w.ToLowerInvariant().Contains("parallel"))

              Expect.isTrue hasAndWarning "warning should reference AND-state or parallel composition"

          testCase "mixed AND+XOR hierarchy emits a warning"
          <| fun _ ->
              let result =
                  deriveWithHierarchy minimalChart projections Map.empty Map.empty (Some mixedHierarchy)

              Expect.isNonEmpty result.Warnings "hierarchy containing AND-states should produce a warning"

          testCase "AND-state warning mentions synchronization barrier or dual gap"
          <| fun _ ->
              let result =
                  deriveWithHierarchy minimalChart projections Map.empty Map.empty (Some andHierarchy)

              let hasSyncWarning =
                  result.Warnings
                  |> List.exists (fun w ->
                      w.ToLowerInvariant().Contains("synchronization")
                      || w.ToLowerInvariant().Contains("dual")
                      || w.ToLowerInvariant().Contains("not supported"))

              Expect.isTrue hasSyncWarning "warning should mention the derivation gap" ]

// ---------------------------------------------------------------------------
// Test: XOR-only and no-hierarchy derivations produce no AND-state warnings
// ---------------------------------------------------------------------------

[<Tests>]
let noAndStateWarningTests =
    let projections = Projection.projectAll minimalChart

    testList
        "No AND-state warnings for XOR-only or flat derivations"
        [ testCase "derive (no hierarchy) produces no warnings"
          <| fun _ ->
              let result = derive minimalChart projections
              Expect.isEmpty result.Warnings "flat derive should produce no warnings"

          testCase "deriveWithHierarchy with None produces no warnings"
          <| fun _ ->
              let result = deriveWithHierarchy minimalChart projections Map.empty Map.empty None
              Expect.isEmpty result.Warnings "None hierarchy should produce no warnings"

          testCase "deriveWithHierarchy with XOR-only hierarchy produces no AND-state warnings"
          <| fun _ ->
              let result =
                  deriveWithHierarchy minimalChart projections Map.empty Map.empty (Some xorHierarchy)

              let andWarnings =
                  result.Warnings
                  |> List.filter (fun w ->
                      w.ToLowerInvariant().Contains("and")
                      || w.ToLowerInvariant().Contains("parallel"))

              Expect.isEmpty andWarnings "XOR-only hierarchy should not produce AND-state warnings"

          testCase "deriveCore produces no warnings"
          <| fun _ ->
              let result = deriveCore minimalChart projections Map.empty Map.empty
              Expect.isEmpty result.Warnings "deriveCore should produce no warnings"

          testCase "deriveWithMethodInfo produces no warnings for flat chart"
          <| fun _ ->
              let result =
                  deriveWithMethodInfo minimalChart projections Map.empty Map.empty Map.empty

              Expect.isEmpty result.Warnings "flat deriveWithMethodInfo should produce no warnings" ]

// ---------------------------------------------------------------------------
// Test: DeriveResult.Warnings is always present (never missing field)
// ---------------------------------------------------------------------------

[<Tests>]
let warningsFieldPresentTests =
    let projections = Projection.projectAll minimalChart

    testList
        "DeriveResult.Warnings field is always present"
        [ testCase "derive result has Warnings field"
          <| fun _ ->
              let result = derive minimalChart projections
              // Access the field — this fails to compile if the field doesn't exist
              let _ = result.Warnings
              Expect.isTrue true "Warnings field accessible"

          testCase "deriveReverse result preserves Warnings field"
          <| fun _ ->
              let clientResult = derive minimalChart projections
              let serverResult = deriveReverse minimalChart clientResult
              let _ = serverResult.Warnings
              Expect.isTrue true "deriveReverse result has Warnings field" ]

// ---------------------------------------------------------------------------
// Test: Unrestricted RoleConstraint = shared-input, NOT broadcast
//
// Shared-input: any single role may trigger the transition independently.
// Broadcast: send to all roles simultaneously (which is NOT the semantics here).
//
// This test documents the correct semantic interpretation by verifying that
// an Unrestricted transition from StateA to StateA' results in a single
// MustSelect annotation for the triggering role — not parallel annotations
// for all roles simultaneously.
// ---------------------------------------------------------------------------

[<Tests>]
let unrestrictedConstraintSemanticsTests =
    // A chart where a single Unrestricted "observe" transition is available
    let sharedInputChart: ExtractedStatechart =
        { RouteTemplate = "/resources/{id}"
          StateNames = [ "Open"; "Closed" ]
          InitialStateKey = "Open"
          GuardNames = []
          StateMetadata =
            Map.ofList
                [ "Open",
                  { AllowedMethods = [ "GET"; "DELETE" ]
                    IsFinal = false
                    Description = None }
                  "Closed",
                  { AllowedMethods = [ "GET" ]
                    IsFinal = true
                    Description = None } ]
          Roles =
            [ { Name = "Admin"; Description = None }
              { Name = "User"; Description = None } ]
          Transitions =
            [ mkTransition "getResource" "Open" "Open" None Unrestricted
              mkTransition "getResource" "Closed" "Closed" None Unrestricted
              mkTransition "deleteResource" "Open" "Closed" None Unrestricted ] }

    let projections = Projection.projectAll sharedInputChart
    let result = derive sharedInputChart projections

    testList
        "Unrestricted constraint = shared-input (any role may trigger independently)"
        [ testCase "Unrestricted MustSelect appears per-role, not as a single broadcast entry"
          <| fun _ ->
              // In shared-input semantics: each role independently gets the annotation.
              // If it were broadcast, only a single combined entry would exist.
              let adminAnnotations =
                  result.Annotations |> Map.tryFind ("Admin", "Open") |> Option.defaultValue []

              let userAnnotations =
                  result.Annotations |> Map.tryFind ("User", "Open") |> Option.defaultValue []

              Expect.isNonEmpty adminAnnotations "Admin has annotations for Open state"
              Expect.isNonEmpty userAnnotations "User has annotations for Open state"

              // Both roles independently see deleteResource (MustSelect)
              let adminDeleteObligation =
                  adminAnnotations
                  |> List.tryFind (fun a -> a.Descriptor = "deleteResource")
                  |> Option.map (fun a -> a.Obligation)

              let userDeleteObligation =
                  userAnnotations
                  |> List.tryFind (fun a -> a.Descriptor = "deleteResource")
                  |> Option.map (fun a -> a.Obligation)

              Expect.equal adminDeleteObligation (Some MustSelect) "Admin sees deleteResource as MustSelect"
              Expect.equal userDeleteObligation (Some MustSelect) "User sees deleteResource as MustSelect"

          testCase "Unrestricted self-loop appears for each role independently (shared-input)"
          <| fun _ ->
              // Both Admin and User independently see getResource as MayPoll (self-loop)
              let adminAnnotations =
                  result.Annotations |> Map.tryFind ("Admin", "Open") |> Option.defaultValue []

              let userAnnotations =
                  result.Annotations |> Map.tryFind ("User", "Open") |> Option.defaultValue []

              let adminGet =
                  adminAnnotations
                  |> List.tryFind (fun a -> a.Descriptor = "getResource")
                  |> Option.map (fun a -> a.Obligation)

              let userGet =
                  userAnnotations
                  |> List.tryFind (fun a -> a.Descriptor = "getResource")
                  |> Option.map (fun a -> a.Obligation)

              Expect.equal adminGet (Some MayPoll) "Admin sees getResource as MayPoll (self-loop, shared-input)"
              Expect.equal userGet (Some MayPoll) "User sees getResource as MayPoll (self-loop, shared-input)" ]
