# Progress Analysis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add deadlock and starvation detection to the Frank spec pipeline, providing the progress guarantee from MPST theory.

**Architecture:** Pure analysis functions in `Frank.Resources.Model` operate on `ExtractedStatechart`. Hybrid approach: projections classify per-state role activity, global graph BFS determines starvation reachability. CLI exposes via top-level `validate --check-progress` command.

**Tech Stack:** F#, Expecto tests, System.CommandLine CLI, System.Text.Json output

**Spec:** `docs/superpowers/specs/2026-03-24-progress-analysis-design.md`

---

### Task 1: Scaffold types and predicates

**Files:**
- Create: `src/Frank.Resources.Model/ProgressAnalysis.fs`
- Modify: `src/Frank.Resources.Model/Frank.Resources.Model.fsproj`

- [ ] **Step 1: Create ProgressAnalysis.fs with types only**

```fsharp
namespace Frank.Resources.Model

/// Progress analysis for statechart protocols.
/// Detects deadlocks (no role can advance) and starvation (role permanently excluded).
/// All functions are pure, total, and format-agnostic.
module ProgressAnalysis =

    /// A transition that advances the protocol (Source <> Target).
    /// Self-loops (getGame: XTurn -> XTurn) are observations, not progress.
    /// Assumption: operates on flat extracted statechart where hierarchy is resolved.
    let isAdvancing (t: TransitionSpec) : bool = t.Source <> t.Target

    /// A transition that at least one role can trigger.
    /// RestrictedTo [] = dead transition (no role can fire it).
    let isLive (t: TransitionSpec) : bool =
        match t.Constraint with
        | RestrictedTo [] -> false
        | _ -> true

    type ProgressDiagnostic =
        /// Non-final state where no role has an advancing+live transition. Error severity.
        | Deadlock of state: string * selfLoopEvents: string list
        /// Role permanently excluded on ALL forward paths from a reachable state. Warning severity.
        | Starvation of role: string * excludedAfter: string * excludedStates: string list
        /// Role with zero advancing transitions in any state. Info severity (expected for observers).
        | ReadOnlyRole of role: string

    module ProgressDiagnostic =
        let severity =
            function
            | Deadlock _ -> "error"
            | Starvation _ -> "warning"
            | ReadOnlyRole _ -> "info"

    type ProgressReport =
        { Route: string
          Diagnostics: ProgressDiagnostic list
          HasErrors: bool
          HasWarnings: bool
          StatesAnalyzed: int
          RolesAnalyzed: string list }
```

- [ ] **Step 2: Add to fsproj compile order**

In `src/Frank.Resources.Model/Frank.Resources.Model.fsproj`, add after the `Projection.fs` line:

```xml
    <Compile Include="ProgressAnalysis.fs" />
```

So the ItemGroup reads:
```xml
    <Compile Include="TypeAnalysis.fs" />
    <Compile Include="ResourceTypes.fs" />
    <Compile Include="Projection.fs" />
    <Compile Include="ProgressAnalysis.fs" />
    <Compile Include="RuntimeTypes.fs" />
    <Compile Include="AffordanceTypes.fs" />
```

- [ ] **Step 3: Verify it compiles**

Run: `dotnet build src/Frank.Resources.Model/Frank.Resources.Model.fsproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/Frank.Resources.Model/ProgressAnalysis.fs src/Frank.Resources.Model/Frank.Resources.Model.fsproj
git commit -m "feat(progress): add ProgressAnalysis types and predicates

Scaffold for #108 — types only, no logic yet.
- isAdvancing/isLive predicates
- ProgressDiagnostic DU (Deadlock|Starvation|ReadOnlyRole)
- ProgressReport record"
```

---

### Task 2: Test scaffolding with fixtures

**Files:**
- Create: `test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs`
- Modify: `test/Frank.Resources.Model.Tests/Frank.Resources.Model.Tests.fsproj`

- [ ] **Step 1: Create test file with all fixtures**

```fsharp
module Frank.Resources.Model.Tests.ProgressAnalysisTests

open Expecto
open Frank.Resources.Model

// -- Helpers --

let private mkTransition event source target guard roleConstraint =
    { Event = event
      Source = source
      Target = target
      Guard = guard
      Constraint = roleConstraint }

let private mkState isFinal =
    { AllowedMethods = [ "GET" ]
      IsFinal = isFinal
      Description = None }

// -- Fixtures --

/// TicTacToe: 2 players + spectator. No deadlocks, no starvation.
/// Spectator is read-only. Turn-taking is weak starvation (no warning).
let private ticTacToeChart: ExtractedStatechart =
    { RouteTemplate = "/games/{gameId}"
      StateNames = [ "XTurn"; "OTurn"; "XWins"; "OWins"; "Draw" ]
      InitialStateKey = "XTurn"
      GuardNames = [ "TurnGuard" ]
      StateMetadata =
        Map.ofList
            [ "XTurn", mkState false
              "OTurn", mkState false
              "XWins", mkState true
              "OWins", mkState true
              "Draw", mkState true ]
      Roles =
        [ { Name = "PlayerX"; Description = Some "Player X" }
          { Name = "PlayerO"; Description = Some "Player O" }
          { Name = "Spectator"; Description = Some "Observer" } ]
      Transitions =
        [ mkTransition "getGame" "XTurn" "XTurn" None Unrestricted
          mkTransition "getGame" "OTurn" "OTurn" None Unrestricted
          mkTransition "getGame" "XWins" "XWins" None Unrestricted
          mkTransition "getGame" "OWins" "OWins" None Unrestricted
          mkTransition "getGame" "Draw" "Draw" None Unrestricted
          mkTransition "makeMove" "XTurn" "OTurn" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "XWins" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "Draw" (Some "TurnGuard") (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "OTurn" "XTurn" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "OWins" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "Draw" (Some "TurnGuard") (RestrictedTo [ "PlayerO" ]) ] }

/// Non-final state with only self-loops for all roles.
let private deadlockSelfLoopChart: ExtractedStatechart =
    { RouteTemplate = "/stuck"
      StateNames = [ "Stuck"; "Done" ]
      InitialStateKey = "Stuck"
      GuardNames = []
      StateMetadata =
        Map.ofList [ "Stuck", mkState false; "Done", mkState true ]
      Roles = [ { Name = "Admin"; Description = None } ]
      Transitions =
        [ mkTransition "refresh" "Stuck" "Stuck" None Unrestricted ] }

/// Only advancing transition is RestrictedTo [] (dead).
let private deadTransitionDeadlockChart: ExtractedStatechart =
    { RouteTemplate = "/dead"
      StateNames = [ "Active"; "Archived" ]
      InitialStateKey = "Active"
      GuardNames = []
      StateMetadata =
        Map.ofList [ "Active", mkState false; "Archived", mkState true ]
      Roles = [ { Name = "Admin"; Description = None } ]
      Transitions =
        [ mkTransition "view" "Active" "Active" None Unrestricted
          mkTransition "archive" "Active" "Archived" None (RestrictedTo []) ] }

/// Role permanently excluded after a state on all forward paths.
/// Admin acts in Phase1, transitions to Phase2. Worker never has advancing transitions.
let private starvationChart: ExtractedStatechart =
    { RouteTemplate = "/workflow"
      StateNames = [ "Phase1"; "Phase2"; "Phase3"; "Done" ]
      InitialStateKey = "Phase1"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Phase1", mkState false
              "Phase2", mkState false
              "Phase3", mkState false
              "Done", mkState true ]
      Roles =
        [ { Name = "Admin"; Description = None }
          { Name = "Worker"; Description = None } ]
      Transitions =
        [ mkTransition "start" "Phase1" "Phase2" None (RestrictedTo [ "Admin" ])
          mkTransition "advance" "Phase2" "Phase3" None (RestrictedTo [ "Admin" ])
          mkTransition "complete" "Phase3" "Done" None (RestrictedTo [ "Admin" ])
          mkTransition "view" "Phase1" "Phase1" None Unrestricted
          mkTransition "view" "Phase2" "Phase2" None Unrestricted
          mkTransition "view" "Phase3" "Phase3" None Unrestricted ] }

/// Recovery path only via dead transition — must still report starved.
let private deadTransitionForwardPathChart: ExtractedStatechart =
    { RouteTemplate = "/dead-path"
      StateNames = [ "Start"; "Middle"; "Recovery"; "End" ]
      InitialStateKey = "Start"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Start", mkState false
              "Middle", mkState false
              "Recovery", mkState false
              "End", mkState true ]
      Roles =
        [ { Name = "RoleA"; Description = None }
          { Name = "RoleB"; Description = None } ]
      Transitions =
        [ mkTransition "go" "Start" "Middle" None (RestrictedTo [ "RoleA" ])
          // Only path from Middle to Recovery is dead — no one can fire it
          mkTransition "recover" "Middle" "Recovery" None (RestrictedTo [])
          mkTransition "act" "Recovery" "End" None (RestrictedTo [ "RoleB" ])
          mkTransition "finish" "Middle" "End" None (RestrictedTo [ "RoleA" ]) ] }

/// All transitions unrestricted — no starvation possible.
let private allUnrestrictedChart: ExtractedStatechart =
    { RouteTemplate = "/open"
      StateNames = [ "A"; "B"; "Done" ]
      InitialStateKey = "A"
      GuardNames = []
      StateMetadata =
        Map.ofList [ "A", mkState false; "B", mkState false; "Done", mkState true ]
      Roles =
        [ { Name = "User"; Description = None }
          { Name = "Admin"; Description = None } ]
      Transitions =
        [ mkTransition "step1" "A" "B" None Unrestricted
          mkTransition "step2" "B" "Done" None Unrestricted ] }

/// Single final state — empty report.
let private singleFinalChart: ExtractedStatechart =
    { RouteTemplate = "/final"
      StateNames = [ "Done" ]
      InitialStateKey = "Done"
      GuardNames = []
      StateMetadata = Map.ofList [ "Done", mkState true ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions = [] }

/// Initial state only, no transitions — deadlock.
let private emptyTransitionsChart: ExtractedStatechart =
    { RouteTemplate = "/empty"
      StateNames = [ "Idle" ]
      InitialStateKey = "Idle"
      GuardNames = []
      StateMetadata = Map.ofList [ "Idle", mkState false ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions = [] }

/// Non-terminating cycle, all roles active — no deadlock, no starvation.
let private cycleNoFinalChart: ExtractedStatechart =
    { RouteTemplate = "/cycle"
      StateNames = [ "A"; "B" ]
      InitialStateKey = "A"
      GuardNames = []
      StateMetadata = Map.ofList [ "A", mkState false; "B", mkState false ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions =
        [ mkTransition "forward" "A" "B" None Unrestricted
          mkTransition "back" "B" "A" None Unrestricted ] }

/// Diamond: two paths reconverge at active state.
/// RoleB inactive at B and C, but both paths reach D where RoleB can act.
let private diamondChart: ExtractedStatechart =
    { RouteTemplate = "/diamond"
      StateNames = [ "A"; "B"; "C"; "D"; "Done" ]
      InitialStateKey = "A"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "A", mkState false
              "B", mkState false
              "C", mkState false
              "D", mkState false
              "Done", mkState true ]
      Roles =
        [ { Name = "RoleA"; Description = None }
          { Name = "RoleB"; Description = None } ]
      Transitions =
        [ mkTransition "left" "A" "B" None (RestrictedTo [ "RoleA" ])
          mkTransition "right" "A" "C" None (RestrictedTo [ "RoleA" ])
          mkTransition "converge1" "B" "D" None (RestrictedTo [ "RoleA" ])
          mkTransition "converge2" "C" "D" None (RestrictedTo [ "RoleA" ])
          mkTransition "finish" "D" "Done" None (RestrictedTo [ "RoleB" ])
          mkTransition "act" "A" "A" None (RestrictedTo [ "RoleB" ]) ] }

/// Non-initial non-final state with no outgoing transitions — deadlock.
let private reachableDeadEndChart: ExtractedStatechart =
    { RouteTemplate = "/deadend"
      StateNames = [ "Start"; "DeadEnd"; "Done" ]
      InitialStateKey = "Start"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Start", mkState false
              "DeadEnd", mkState false
              "Done", mkState true ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions =
        [ mkTransition "enter" "Start" "DeadEnd" None Unrestricted
          mkTransition "skip" "Start" "Done" None Unrestricted ] }

/// Same role excluded from two independent states — two diagnostics.
let private multipleStarvationChart: ExtractedStatechart =
    { RouteTemplate = "/multi-starve"
      StateNames = [ "Start"; "Branch1"; "Branch2"; "End1"; "End2" ]
      InitialStateKey = "Start"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Start", mkState false
              "Branch1", mkState false
              "Branch2", mkState false
              "End1", mkState true
              "End2", mkState true ]
      Roles =
        [ { Name = "RoleA"; Description = None }
          { Name = "RoleB"; Description = None } ]
      Transitions =
        [ mkTransition "go1" "Start" "Branch1" None (RestrictedTo [ "RoleA" ])
          mkTransition "go2" "Start" "Branch2" None (RestrictedTo [ "RoleA" ])
          mkTransition "end1" "Branch1" "End1" None (RestrictedTo [ "RoleA" ])
          mkTransition "end2" "Branch2" "End2" None (RestrictedTo [ "RoleA" ]) ] }

/// Disconnected chart — unreachable non-final state should not produce false deadlock.
let private disconnectedChart: ExtractedStatechart =
    { RouteTemplate = "/disconnected"
      StateNames = [ "Start"; "Done"; "Orphan" ]
      InitialStateKey = "Start"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "Start", mkState false
              "Done", mkState true
              "Orphan", mkState false ]
      Roles = [ { Name = "User"; Description = None } ]
      Transitions =
        [ mkTransition "finish" "Start" "Done" None Unrestricted
          mkTransition "orphanAct" "Orphan" "Start" None Unrestricted ] }

// -- Placeholder test lists (to be filled in subsequent tasks) --

[<Tests>]
let predicateTests =
    testList
        "predicates"
        [ testCase "isAdvancing returns true for state-changing transition"
          <| fun _ ->
              let t = mkTransition "go" "A" "B" None Unrestricted
              Expect.isTrue (ProgressAnalysis.isAdvancing t) "A->B is advancing"

          testCase "isAdvancing returns false for self-loop"
          <| fun _ ->
              let t = mkTransition "view" "A" "A" None Unrestricted
              Expect.isFalse (ProgressAnalysis.isAdvancing t) "A->A is not advancing"

          testCase "isLive returns true for unrestricted"
          <| fun _ ->
              let t = mkTransition "go" "A" "B" None Unrestricted
              Expect.isTrue (ProgressAnalysis.isLive t) "Unrestricted is live"

          testCase "isLive returns true for non-empty RestrictedTo"
          <| fun _ ->
              let t = mkTransition "go" "A" "B" None (RestrictedTo [ "R" ])
              Expect.isTrue (ProgressAnalysis.isLive t) "RestrictedTo [R] is live"

          testCase "isLive returns false for empty RestrictedTo"
          <| fun _ ->
              let t = mkTransition "go" "A" "B" None (RestrictedTo [])
              Expect.isFalse (ProgressAnalysis.isLive t) "RestrictedTo [] is dead" ]
```

- [ ] **Step 2: Add to test fsproj compile order**

In `test/Frank.Resources.Model.Tests/Frank.Resources.Model.Tests.fsproj`, add after `ProjectionTests.fs`:

```xml
    <Compile Include="ProgressAnalysisTests.fs" />
```

So the ItemGroup reads:
```xml
    <Compile Include="ResourceSlugTests.fs" />
    <Compile Include="AffordanceMapTests.fs" />
    <Compile Include="ProjectionTests.fs" />
    <Compile Include="ProgressAnalysisTests.fs" />
    <Compile Include="Program.fs" />
```

- [ ] **Step 3: Verify tests compile and predicate tests pass**

Run: `dotnet test test/Frank.Resources.Model.Tests/ --filter "predicates"`
Expected: 5 tests pass.

- [ ] **Step 4: Commit**

```bash
git add test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs test/Frank.Resources.Model.Tests/Frank.Resources.Model.Tests.fsproj
git commit -m "test(progress): add test fixtures and predicate tests

13 test fixtures covering all scenarios from spec.
5 predicate tests for isAdvancing and isLive."
```

---

### Task 3: TDD identifyReadOnlyRoles

**Files:**
- Modify: `test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs`
- Modify: `src/Frank.Resources.Model/ProgressAnalysis.fs`

- [ ] **Step 1: Write failing tests**

Add to `ProgressAnalysisTests.fs`:

```fsharp
[<Tests>]
let readOnlyRoleTests =
    testList
        "identifyReadOnlyRoles"
        [ testCase "Spectator in TicTacToe is read-only"
          <| fun _ ->
              let projections = Projection.projectAll ticTacToeChart
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              Expect.contains readOnly "Spectator" "Spectator is read-only"

          testCase "PlayerX in TicTacToe is not read-only"
          <| fun _ ->
              let projections = Projection.projectAll ticTacToeChart
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              Expect.isFalse (List.contains "PlayerX" readOnly) "PlayerX is not read-only"

          testCase "all-unrestricted chart has no read-only roles"
          <| fun _ ->
              let projections = Projection.projectAll allUnrestrictedChart
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              Expect.isEmpty readOnly "No read-only roles when all unrestricted"

          testCase "role with only dead transitions is read-only"
          <| fun _ ->
              let projections = Projection.projectAll deadTransitionDeadlockChart
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              Expect.contains readOnly "Admin" "Admin with only dead advancing transition is read-only" ]
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Frank.Resources.Model.Tests/ --filter "identifyReadOnlyRoles"`
Expected: FAIL — `identifyReadOnlyRoles` not defined.

- [ ] **Step 3: Implement identifyReadOnlyRoles**

Add to `ProgressAnalysis` module in `src/Frank.Resources.Model/ProgressAnalysis.fs`, after the types:

```fsharp
    /// Identify roles with zero advancing+live transitions in any state.
    /// These are read-only observers (e.g., Spectator) — info, not a problem.
    let identifyReadOnlyRoles (projections: Map<string, ExtractedStatechart>) : string list =
        projections
        |> Map.toList
        |> List.choose (fun (role, chart) ->
            let hasAdvancing =
                chart.Transitions
                |> List.exists (fun t -> isAdvancing t && isLive t)

            if hasAdvancing then None else Some role)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Frank.Resources.Model.Tests/ --filter "identifyReadOnlyRoles"`
Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Frank.Resources.Model/ProgressAnalysis.fs test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs
git commit -m "feat(progress): implement identifyReadOnlyRoles

Classifies roles with zero advancing+live transitions as read-only.
Used to exclude observers from starvation analysis."
```

---

### Task 4: TDD detectDeadlocks

**Files:**
- Modify: `test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs`
- Modify: `src/Frank.Resources.Model/ProgressAnalysis.fs`

- [ ] **Step 1: Write failing tests**

Add to `ProgressAnalysisTests.fs`:

```fsharp
[<Tests>]
let deadlockTests =
    testList
        "detectDeadlocks"
        [ testCase "TicTacToe has no deadlocks"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates ticTacToeChart
              let deadlocks = ProgressAnalysis.detectDeadlocks pruned
              Expect.isEmpty deadlocks "TicTacToe has no deadlocks"

          testCase "self-loop-only state is deadlock with selfLoopEvents"
          <| fun _ ->
              let deadlocks = ProgressAnalysis.detectDeadlocks deadlockSelfLoopChart

              Expect.hasLength deadlocks 1 "One deadlock"

              match deadlocks.[0] with
              | ProgressAnalysis.Deadlock(state, selfLoops) ->
                  Expect.equal state "Stuck" "Deadlock at Stuck"
                  Expect.contains selfLoops "refresh" "Self-loop event reported"
              | _ -> failtest "Expected Deadlock diagnostic"

          testCase "dead transition state is deadlock"
          <| fun _ ->
              let deadlocks = ProgressAnalysis.detectDeadlocks deadTransitionDeadlockChart

              Expect.hasLength deadlocks 1 "One deadlock"

              match deadlocks.[0] with
              | ProgressAnalysis.Deadlock(state, selfLoops) ->
                  Expect.equal state "Active" "Deadlock at Active"
                  Expect.contains selfLoops "view" "Self-loop reported"
              | _ -> failtest "Expected Deadlock diagnostic"

          testCase "final state with no transitions is not deadlock"
          <| fun _ ->
              let deadlocks = ProgressAnalysis.detectDeadlocks singleFinalChart
              Expect.isEmpty deadlocks "Final state is not a deadlock"

          testCase "empty transitions from initial is deadlock"
          <| fun _ ->
              let deadlocks = ProgressAnalysis.detectDeadlocks emptyTransitionsChart

              Expect.hasLength deadlocks 1 "One deadlock"

              match deadlocks.[0] with
              | ProgressAnalysis.Deadlock(state, selfLoops) ->
                  Expect.equal state "Idle" "Deadlock at Idle"
                  Expect.isEmpty selfLoops "No self-loops"
              | _ -> failtest "Expected Deadlock diagnostic"

          testCase "reachable dead-end is deadlock"
          <| fun _ ->
              let deadlocks = ProgressAnalysis.detectDeadlocks reachableDeadEndChart

              let deadEndDiags =
                  deadlocks
                  |> List.choose (fun d ->
                      match d with
                      | ProgressAnalysis.Deadlock(s, _) when s = "DeadEnd" -> Some d
                      | _ -> None)

              Expect.hasLength deadEndDiags 1 "DeadEnd is a deadlock"

          testCase "cycle without final states is NOT deadlock"
          <| fun _ ->
              let deadlocks = ProgressAnalysis.detectDeadlocks cycleNoFinalChart
              Expect.isEmpty deadlocks "Cycle with advancing transitions is not deadlock" ]
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Frank.Resources.Model.Tests/ --filter "detectDeadlocks"`
Expected: FAIL — `detectDeadlocks` not defined.

- [ ] **Step 3: Implement detectDeadlocks**

Add to `ProgressAnalysis` module:

```fsharp
    /// Detect non-final states where no role has an advancing+live transition.
    /// Operates on the global chart (not projections).
    /// States not in StateMetadata are treated as non-final (conservative).
    let detectDeadlocks (statechart: ExtractedStatechart) : ProgressDiagnostic list =
        let isFinal state =
            statechart.StateMetadata
            |> Map.tryFind state
            |> Option.map (fun si -> si.IsFinal)
            |> Option.defaultValue false

        let transitionsBySource =
            statechart.Transitions
            |> List.groupBy (fun t -> t.Source)
            |> Map.ofList

        statechart.StateNames
        |> List.choose (fun state ->
            if isFinal state then
                None
            else
                let transitions =
                    transitionsBySource
                    |> Map.tryFind state
                    |> Option.defaultValue []

                let advancingLive =
                    transitions |> List.filter (fun t -> isAdvancing t && isLive t)

                if List.isEmpty advancingLive then
                    let selfLoopEvents =
                        transitions
                        |> List.filter (fun t -> t.Source = t.Target)
                        |> List.map (fun t -> t.Event)
                        |> List.distinct

                    Some(Deadlock(state, selfLoopEvents))
                else
                    None)
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Frank.Resources.Model.Tests/ --filter "detectDeadlocks"`
Expected: 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Frank.Resources.Model/ProgressAnalysis.fs test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs
git commit -m "feat(progress): implement detectDeadlocks

Non-final state with no advancing+live transitions = deadlock.
Reports self-loop events for diagnostics."
```

---

### Task 5: TDD detectStarvation

**Files:**
- Modify: `test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs`
- Modify: `src/Frank.Resources.Model/ProgressAnalysis.fs`

- [ ] **Step 1: Write failing tests**

Add to `ProgressAnalysisTests.fs`:

```fsharp
[<Tests>]
let starvationTests =
    testList
        "detectStarvation"
        [ testCase "TicTacToe PlayerX in OTurn is NOT starvation"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates ticTacToeChart
              let projections = Projection.projectAll pruned
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              let starvation = ProgressAnalysis.detectStarvation pruned projections readOnly
              Expect.isEmpty starvation "Turn-taking is not starvation"

          testCase "Worker permanently excluded is starvation"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates starvationChart
              let projections = Projection.projectAll pruned
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              let starvation = ProgressAnalysis.detectStarvation pruned projections readOnly

              let workerDiags =
                  starvation
                  |> List.choose (fun d ->
                      match d with
                      | ProgressAnalysis.Starvation(r, _, _) when r = "Worker" -> Some d
                      | _ -> None)

              Expect.isNonEmpty workerDiags "Worker is starved"

          testCase "read-only roles excluded from analysis"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates ticTacToeChart
              let projections = Projection.projectAll pruned
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              let starvation = ProgressAnalysis.detectStarvation pruned projections readOnly

              let spectatorDiags =
                  starvation
                  |> List.choose (fun d ->
                      match d with
                      | ProgressAnalysis.Starvation(r, _, _) when r = "Spectator" -> Some d
                      | _ -> None)

              Expect.isEmpty spectatorDiags "Spectator excluded from starvation analysis"

          testCase "all-unrestricted has no starvation"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates allUnrestrictedChart
              let projections = Projection.projectAll pruned
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              let starvation = ProgressAnalysis.detectStarvation pruned projections readOnly
              Expect.isEmpty starvation "All-unrestricted has no starvation"

          testCase "dead transition in forward path still reports starvation"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates deadTransitionForwardPathChart
              let projections = Projection.projectAll pruned
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              let starvation = ProgressAnalysis.detectStarvation pruned projections readOnly

              let roleBDiags =
                  starvation
                  |> List.choose (fun d ->
                      match d with
                      | ProgressAnalysis.Starvation(r, _, _) when r = "RoleB" -> Some d
                      | _ -> None)

              Expect.isNonEmpty roleBDiags "RoleB starved — recovery only via dead transition"

          testCase "diamond topology is NOT starvation"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates diamondChart
              let projections = Projection.projectAll pruned
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              let starvation = ProgressAnalysis.detectStarvation pruned projections readOnly

              let roleBDiags =
                  starvation
                  |> List.choose (fun d ->
                      match d with
                      | ProgressAnalysis.Starvation(r, _, _) when r = "RoleB" -> Some d
                      | _ -> None)

              Expect.isEmpty roleBDiags "RoleB recovers at D via diamond paths"

          testCase "multiple starvation entry points emit two diagnostics"
          <| fun _ ->
              let pruned = Projection.pruneUnreachableStates multipleStarvationChart
              let projections = Projection.projectAll pruned
              let readOnly = ProgressAnalysis.identifyReadOnlyRoles projections
              let starvation = ProgressAnalysis.detectStarvation pruned projections readOnly

              let roleBDiags =
                  starvation
                  |> List.choose (fun d ->
                      match d with
                      | ProgressAnalysis.Starvation(r, _, _) when r = "RoleB" -> Some d
                      | _ -> None)

              Expect.isGreaterThanOrEqual
                  (List.length roleBDiags)
                  2
                  "RoleB starved from both Branch1 and Branch2" ]
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Frank.Resources.Model.Tests/ --filter "detectStarvation"`
Expected: FAIL — `detectStarvation` not defined.

- [ ] **Step 3: Implement forwardReachable helper and detectStarvation**

Add to `ProgressAnalysis` module (before `detectDeadlocks`):

```fsharp
    /// Build adjacency map from live transitions only.
    /// Dead transitions (RestrictedTo []) excluded — they inflate reachability
    /// and produce false negatives in starvation detection.
    let private buildLiveAdjacency (transitions: TransitionSpec list) : Map<string, string list> =
        transitions
        |> List.filter isLive
        |> List.groupBy (fun t -> t.Source)
        |> List.map (fun (k, ts) -> k, ts |> List.map (fun t -> t.Target) |> List.distinct)
        |> Map.ofList

    /// Forward reachability via BFS from a start state.
    let private forwardReachable (adjacency: Map<string, string list>) (start: string) : Set<string> =
        let rec bfs (visited: Set<string>) (frontier: string list) =
            match frontier with
            | [] -> visited
            | state :: rest ->
                if Set.contains state visited then
                    bfs visited rest
                else
                    let visited' = Set.add state visited

                    let neighbors =
                        adjacency
                        |> Map.tryFind state
                        |> Option.defaultValue []
                        |> List.filter (fun s -> not (Set.contains s visited'))

                    bfs visited' (neighbors @ rest)

        bfs Set.empty [ start ]
```

Then add `detectStarvation`:

```fsharp
    /// Detect roles permanently excluded on ALL forward paths from reachable states.
    /// Uses ALL live global transitions for reachability (Harel correction).
    /// Excludes dead transitions from BFS (Harel soundness fix).
    let detectStarvation
        (statechart: ExtractedStatechart)
        (projections: Map<string, ExtractedStatechart>)
        (readOnlyRoles: string list)
        : ProgressDiagnostic list =
        let isFinal state =
            statechart.StateMetadata
            |> Map.tryFind state
            |> Option.map (fun si -> si.IsFinal)
            |> Option.defaultValue false

        let adjacency = buildLiveAdjacency statechart.Transitions

        let roleNames =
            projections
            |> Map.keys
            |> Seq.filter (fun r -> not (List.contains r readOnlyRoles))
            |> Seq.toList

        roleNames
        |> List.collect (fun role ->
            let roleChart =
                projections
                |> Map.tryFind role
                |> Option.defaultValue statechart

            let activeStates =
                roleChart.Transitions
                |> List.filter isAdvancing
                |> List.map (fun t -> t.Source)
                |> Set.ofList

            let nonFinalStates =
                statechart.StateNames
                |> List.filter (fun s -> not (isFinal s))

            nonFinalStates
            |> List.choose (fun state ->
                if Set.contains state activeStates then
                    None
                else
                    let reachable = forwardReachable adjacency state

                    if Set.intersect reachable activeStates |> Set.isEmpty then
                        let excludedStates =
                            reachable
                            |> Set.toList
                            |> List.filter (fun s -> s <> state && not (isFinal s) && not (Set.contains s activeStates))

                        Some(Starvation(role, state, excludedStates))
                    else
                        None))
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Frank.Resources.Model.Tests/ --filter "detectStarvation"`
Expected: 7 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Frank.Resources.Model/ProgressAnalysis.fs test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs
git commit -m "feat(progress): implement detectStarvation

Forward BFS on live-only global graph (Harel soundness fix).
Uses ALL roles' transitions for reachability (Harel correction).
Only strong starvation reported — weak (turn-taking) excluded."
```

---

### Task 6: TDD analyzeProgress

**Files:**
- Modify: `test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs`
- Modify: `src/Frank.Resources.Model/ProgressAnalysis.fs`

- [ ] **Step 1: Write failing tests**

Add to `ProgressAnalysisTests.fs`:

```fsharp
[<Tests>]
let analyzeProgressTests =
    testList
        "analyzeProgress"
        [ testCase "TicTacToe: no errors, no warnings, 1 read-only"
          <| fun _ ->
              let report = ProgressAnalysis.analyzeProgress ticTacToeChart
              Expect.isFalse report.HasErrors "No errors"
              Expect.isFalse report.HasWarnings "No warnings"
              Expect.equal report.StatesAnalyzed 5 "5 states analyzed"
              Expect.equal report.Route "/games/{gameId}" "Route preserved"

              let readOnlyDiags =
                  report.Diagnostics
                  |> List.choose (fun d ->
                      match d with
                      | ProgressAnalysis.ReadOnlyRole r -> Some r
                      | _ -> None)

              Expect.equal readOnlyDiags [ "Spectator" ] "Spectator is read-only"

          testCase "deadlock chart has errors"
          <| fun _ ->
              let report = ProgressAnalysis.analyzeProgress deadlockSelfLoopChart
              Expect.isTrue report.HasErrors "Has errors"

          testCase "starvation chart has warnings"
          <| fun _ ->
              let report = ProgressAnalysis.analyzeProgress starvationChart
              Expect.isTrue report.HasWarnings "Has warnings"

          testCase "disconnected chart: pruning prevents false deadlock"
          <| fun _ ->
              let report = ProgressAnalysis.analyzeProgress disconnectedChart

              let orphanDeadlocks =
                  report.Diagnostics
                  |> List.choose (fun d ->
                      match d with
                      | ProgressAnalysis.Deadlock(s, _) when s = "Orphan" -> Some s
                      | _ -> None)

              Expect.isEmpty orphanDeadlocks "Orphan state pruned, no false deadlock"

          testCase "RolesAnalyzed populated correctly"
          <| fun _ ->
              let report = ProgressAnalysis.analyzeProgress ticTacToeChart
              Expect.hasLength report.RolesAnalyzed 3 "3 roles"
              Expect.contains report.RolesAnalyzed "PlayerX" "PlayerX in list"
              Expect.contains report.RolesAnalyzed "Spectator" "Spectator in list" ]
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Frank.Resources.Model.Tests/ --filter "analyzeProgress"`
Expected: FAIL — `analyzeProgress` not defined.

- [ ] **Step 3: Implement analyzeProgress**

Add to `ProgressAnalysis` module:

```fsharp
    /// Orchestrator: prune, project, classify, detect, assemble report.
    /// Single entry point — caller passes ExtractedStatechart; projections computed internally.
    let analyzeProgress (statechart: ExtractedStatechart) : ProgressReport =
        let pruned = Projection.pruneUnreachableStates statechart
        let projections = Projection.projectAll pruned

        let readOnlyRoles = identifyReadOnlyRoles projections
        let deadlocks = detectDeadlocks pruned
        let starvation = detectStarvation pruned projections readOnlyRoles

        let readOnlyDiagnostics =
            readOnlyRoles |> List.map ReadOnlyRole

        let allDiagnostics =
            deadlocks @ starvation @ readOnlyDiagnostics

        { Route = statechart.RouteTemplate
          Diagnostics = allDiagnostics
          HasErrors = deadlocks |> List.isEmpty |> not
          HasWarnings = starvation |> List.isEmpty |> not
          StatesAnalyzed = pruned.StateNames.Length
          RolesAnalyzed = statechart.Roles |> List.map (fun r -> r.Name) }
```

- [ ] **Step 4: Run ALL tests to verify everything passes**

Run: `dotnet test test/Frank.Resources.Model.Tests/`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Frank.Resources.Model/ProgressAnalysis.fs test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs
git commit -m "feat(progress): implement analyzeProgress orchestrator

Composes prune → projectAll → identifyReadOnlyRoles →
detectDeadlocks → detectStarvation → ProgressReport.
All analysis tests passing."
```

---

### Task 7: CLI integration

**Files:**
- Create: `src/Frank.Cli.Core/Commands/UnifiedValidateCommand.fs`
- Modify: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
- Modify: `src/Frank.Cli.Core/Output/TextOutput.fs`
- Modify: `src/Frank.Cli.Core/Output/JsonOutput.fs`
- Modify: `src/Frank.Cli/Program.fs`

- [ ] **Step 1: Create UnifiedValidateCommand.fs**

```fsharp
module Frank.Cli.Core.Commands.UnifiedValidateCommand

open System.IO
open Frank.Resources.Model
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Statechart.StatechartError

type UnifiedValidateResult =
    { Reports: ProgressAnalysis.ProgressReport list
      HasErrors: bool
      FromCache: bool }

let execute (projectPath: string) : Async<Result<UnifiedValidateResult, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error(FileNotFound projectPath)
        else
            let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))
            let cacheResult = UnifiedCache.tryLoadFresh projectDir false

            match cacheResult with
            | Error _ ->
                return
                    Error(
                        CodeTruthExtractionFailed
                            "No extraction cache found. Run 'frank extract --project ...' first."
                    )
            | Ok cachedState ->
                let reports =
                    cachedState.Resources
                    |> List.choose (fun r ->
                        r.Statechart
                        |> Option.map ProgressAnalysis.analyzeProgress)

                let hasErrors =
                    reports |> List.exists (fun r -> r.HasErrors)

                return
                    Ok
                        { Reports = reports
                          HasErrors = hasErrors
                          FromCache = true }
    }
```

- [ ] **Step 2: Add to Frank.Cli.Core.fsproj**

Add after `Commands/UnifiedGenerateCommand.fs`:

```xml
    <Compile Include="Commands/UnifiedValidateCommand.fs" />
```

- [ ] **Step 3: Add formatProgressReport to TextOutput.fs**

Add at the end of the `TextOutput` module (before the closing of the module):

```fsharp
    let formatProgressReport (report: ProgressAnalysis.ProgressReport) : string =
        let sb = System.Text.StringBuilder()

        sb.AppendLine(
            bold (
                sprintf
                    "Progress analysis for %s (%d states, %d roles)"
                    report.Route
                    report.StatesAnalyzed
                    (List.length report.RolesAnalyzed)
            )
        )
        |> ignore

        if List.isEmpty report.Diagnostics then
            sb.AppendLine(green "  No progress issues detected") |> ignore
        else
            for diag in report.Diagnostics do
                match diag with
                | ProgressAnalysis.Deadlock(state, selfLoops) ->
                    let loopInfo =
                        if List.isEmpty selfLoops then
                            ""
                        else
                            sprintf " (self-loops: %s)" (String.concat ", " selfLoops)

                    sb.AppendLine(red (sprintf "  [error] Deadlock: state %s has no advancing transitions%s" state loopInfo))
                    |> ignore
                | ProgressAnalysis.Starvation(role, excludedAfter, excludedStates) ->
                    let statesInfo =
                        if List.isEmpty excludedStates then
                            ""
                        else
                            sprintf " (excluded from: %s)" (String.concat ", " excludedStates)

                    sb.AppendLine(
                        yellow (sprintf "  [warn]  Starvation: role %s excluded after state %s%s" role excludedAfter statesInfo)
                    )
                    |> ignore
                | ProgressAnalysis.ReadOnlyRole role ->
                    sb.AppendLine(sprintf "  [info]  Read-only role: %s (expected for observers)" role)
                    |> ignore

        sb.ToString()

    let formatUnifiedValidateResult (result: UnifiedValidateCommand.UnifiedValidateResult) : string =
        let sb = System.Text.StringBuilder()

        for report in result.Reports do
            sb.Append(formatProgressReport report) |> ignore
            sb.AppendLine() |> ignore

        if List.isEmpty result.Reports then
            sb.AppendLine("No stateful resources found.") |> ignore

        sb.ToString()
```

- [ ] **Step 4: Add formatProgressReport to JsonOutput.fs**

Add at the end of the `JsonOutput` module:

```fsharp
    let formatUnifiedValidateResult (result: UnifiedValidateCommand.UnifiedValidateResult) : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writeString writer "status" (if result.HasErrors then "error" else "ok")
        writer.WriteBoolean("fromCache", result.FromCache)
        writer.WriteBoolean("hasErrors", result.HasErrors)

        writer.WriteStartArray("reports")

        for report in result.Reports do
            writer.WriteStartObject()
            writeString writer "route" report.Route
            writeNumber writer "statesAnalyzed" report.StatesAnalyzed
            writer.WriteBoolean("hasErrors", report.HasErrors)
            writer.WriteBoolean("hasWarnings", report.HasWarnings)

            writer.WriteStartArray("rolesAnalyzed")

            for role in report.RolesAnalyzed do
                writer.WriteStringValue(role)

            writer.WriteEndArray()

            writer.WriteStartArray("diagnostics")

            for diag in report.Diagnostics do
                writer.WriteStartObject()
                writeString writer "severity" (ProgressAnalysis.ProgressDiagnostic.severity diag)

                match diag with
                | ProgressAnalysis.Deadlock(state, selfLoops) ->
                    writeString writer "kind" "deadlock"
                    writeString writer "state" state

                    writer.WriteStartArray("selfLoopEvents")

                    for ev in selfLoops do
                        writer.WriteStringValue(ev)

                    writer.WriteEndArray()
                | ProgressAnalysis.Starvation(role, excludedAfter, excludedStates) ->
                    writeString writer "kind" "starvation"
                    writeString writer "role" role
                    writeString writer "excludedAfter" excludedAfter

                    writer.WriteStartArray("excludedStates")

                    for s in excludedStates do
                        writer.WriteStringValue(s)

                    writer.WriteEndArray()
                | ProgressAnalysis.ReadOnlyRole role ->
                    writeString writer "kind" "readOnlyRole"
                    writeString writer "role" role

                writer.WriteEndObject()

            writer.WriteEndArray()
            writer.WriteEndObject()

        writer.WriteEndArray()
        writer.WriteEndObject()
        writer.Flush()

        Encoding.UTF8.GetString(stream.ToArray())
```

- [ ] **Step 5: Register validate command in Program.fs**

Add before the `// ── status (top-level) ──` section (after the `generate` block around line 653):

```fsharp
    // ── validate (top-level, unified) ──
    let uniValidateCmd = Command("validate")

    uniValidateCmd.Description <-
        "Validate stateful resources for progress properties (deadlock, starvation)"

    let uniValProjectOpt = Option<string>("--project")
    uniValProjectOpt.Description <- "Path to .fsproj file"
    uniValProjectOpt.Required <- true
    let uniValCheckProgressOpt = Option<bool>("--check-progress")

    uniValCheckProgressOpt.Description <-
        "Run deadlock and starvation analysis on per-role projections"

    uniValCheckProgressOpt.DefaultValueFactory <- (fun _ -> false)
    let uniValFormatOpt = Option<string>("--output-format")
    uniValFormatOpt.Description <- "Output format (text|json)"
    uniValFormatOpt.DefaultValueFactory <- (fun _ -> "text")
    uniValidateCmd.Options.Add(uniValProjectOpt)
    uniValidateCmd.Options.Add(uniValCheckProgressOpt)
    uniValidateCmd.Options.Add(uniValFormatOpt)

    uniValidateCmd.SetAction(fun parseResult ->
        let project = parseResult.GetValue(uniValProjectOpt)
        let checkProgress = parseResult.GetValue(uniValCheckProgressOpt)
        let format = parseResult.GetValue(uniValFormatOpt)

        if checkProgress then
            let result =
                UnifiedValidateCommand.execute project
                |> Async.RunSynchronously

            match result with
            | Ok r ->
                let output =
                    if format = "json" then
                        JsonOutput.formatUnifiedValidateResult r
                    else
                        TextOutput.formatUnifiedValidateResult r

                Console.WriteLine(output)

                if r.HasErrors then
                    Environment.ExitCode <- 1
            | Error e ->
                Environment.ExitCode <- 1
                Console.Error.WriteLine(StatechartError.formatError e)
        else
            Console.WriteLine("No validation flags specified. Use --check-progress to run progress analysis."))

    root.Subcommands.Add(uniValidateCmd)
```

- [ ] **Step 6: Verify build**

Run: `dotnet build Frank.sln`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add src/Frank.Cli.Core/Commands/UnifiedValidateCommand.fs src/Frank.Cli.Core/Frank.Cli.Core.fsproj src/Frank.Cli.Core/Output/TextOutput.fs src/Frank.Cli.Core/Output/JsonOutput.fs src/Frank.Cli/Program.fs
git commit -m "feat(progress): add validate --check-progress CLI command

Top-level 'validate' command with --check-progress flag.
Reads from extraction cache (requires prior 'frank extract').
Text and JSON output formatting for ProgressReport.
Exit code 1 on deadlocks (errors), 0 on warnings/info."
```

---

### Task 8: Full verification

- [ ] **Step 1: Build everything**

Run: `dotnet build Frank.sln`
Expected: Build succeeded.

- [ ] **Step 2: Run all tests**

Run: `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"`
Expected: All tests pass.

- [ ] **Step 3: Run progress analysis tests specifically**

Run: `dotnet test test/Frank.Resources.Model.Tests/ -v normal`
Expected: All progress analysis tests pass with names visible.

- [ ] **Step 4: Verify no Fantomas issues**

Run: `dotnet fantomas --check src/Frank.Resources.Model/ProgressAnalysis.fs`
Expected: No formatting issues (or fix any that arise).

- [ ] **Step 5: Final commit if any formatting fixes needed**

Only if Fantomas required changes:
```bash
git add -A
git commit -m "style: apply Fantomas formatting to ProgressAnalysis"
```
