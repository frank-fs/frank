# Progress Analysis — Deadlock and Starvation Detection

**Issue:** #108
**Milestone:** v7.3.0
**Expert reviewers:** Wadler (MPST liveness), Harel (statechart reachability)

## Context

Safety (no invalid actions) is enforced at runtime by Frank's middleware. Progress (no stuck states) is a property of the protocol design that can only be verified statically — middleware handling one request at a time cannot detect that no future request will ever advance the protocol.

PR #172 landed the projection operator (`Projection.projectAll`). PR #107 added completeness checking (`findOrphanedTransitions`). This issue adds the remaining liveness properties: deadlock detection and starvation detection.

Scope is projection-only — no dual/complementary features (deferred to v7.4.0). Connectedness and mixed choice deferred to #133. Livelock detection (non-terminating cycles) deferred — non-terminating protocols are valid designs (long-running services); requires SCC which changes algorithm complexity.

## Types

New file: `src/Frank.Resources.Model/ProgressAnalysis.fs`
Namespace: `Frank.Resources.Model`

### Predicates

```fsharp
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
```

### Diagnostics

```fsharp
type ProgressDiagnostic =
    /// Non-final state where no role has an advancing+live transition. Error severity.
    | Deadlock of state: string * selfLoopEvents: string list
    /// Role permanently excluded on ALL forward paths from a reachable state. Warning severity.
    | Starvation of role: string * excludedAfter: string * excludedStates: string list
    /// Role with zero advancing transitions in any state. Info severity (expected for observers).
    | ReadOnlyRole of role: string

type ProgressReport =
    { /// Route template identifying which statechart this report covers.
      Route: string
      Diagnostics: ProgressDiagnostic list
      /// True if any Deadlock diagnostic exists.
      HasErrors: bool
      /// True if any Starvation diagnostic exists.
      HasWarnings: bool
      /// Number of states examined.
      StatesAnalyzed: int
      /// Role names examined.
      RolesAnalyzed: string list }
```

No separate `ProgressSeverity` type — severity is implicit in the DU case. If needed downstream:

```fsharp
module ProgressDiagnostic =
    let severity = function
        | Deadlock _ -> "error"
        | Starvation _ -> "warning"
        | ReadOnlyRole _ -> "info"
```

## Algorithms

### Execution Order

1. `identifyReadOnlyRoles` — classify first to exclude from starvation analysis
2. `detectDeadlocks` — global chart only, no projections needed
3. `detectStarvation` — projections for classification, global graph for reachability
4. `analyzeProgress` — orchestrator composing 1-3

### identifyReadOnlyRoles

```
identifyReadOnlyRoles : Map<string, ExtractedStatechart> -> string list

For each role R in projections:
    If R's projection has zero transitions where (isAdvancing && isLive):
        Include R in result
```

### detectDeadlocks

```
detectDeadlocks : ExtractedStatechart -> ProgressDiagnostic list

For each state S where StateMetadata[S].IsFinal = false:
    advancingLive = transitions from S where (isAdvancing && isLive)
    If advancingLive is empty:
        selfLoops = transitions from S where Source = Target, map to distinct Event names
        Emit Deadlock(S, selfLoops)
```

Operates on global chart. A non-final state with no advancing+live transitions is a deadlock regardless of role distribution. States not in `StateMetadata` are treated as non-final (conservative — errs toward reporting).

### detectStarvation

```
detectStarvation :
    ExtractedStatechart -> Map<string, ExtractedStatechart> -> string list -> ProgressDiagnostic list

Input: global chart, per-role projections, read-only role names (to exclude)

For each non-read-only role R:
    activeStates(R) = { S | R's projection has any transition from S where isAdvancing }
    For each non-final state S where S not in activeStates(R):
        forwardReachable = BFS from S using ALL global live transitions (isLive only)
        If forwardReachable intersect activeStates(R) is empty:
            excludedStates = forwardReachable states that are non-final and not in activeStates(R)
            Emit Starvation(R, S, excludedStates)
```

**Critical design decisions:**
1. **Uses ALL roles' transitions for reachability, not excluding R's.** Harel's correction — excluding R's transitions severs legitimate recovery paths where R acts at intermediate states.
2. **Excludes dead transitions (`RestrictedTo []`) from BFS adjacency.** Harel's soundness fix — dead transitions are edges no participant can fire. Including them inflates the reachable set, producing false negatives (claiming a role isn't starved because BFS reached a recovery state through an unfirable transition). Note: `Projection.pruneUnreachableStates` includes dead transitions in its adjacency, which is defensible there (retains states for deadlock reporting). The asymmetry is intentional.

**Strong vs weak starvation:** Only strong starvation (excluded on ALL forward paths) is reported. Weak starvation (excluded on some paths but not all) is normal turn-taking — e.g., PlayerX can't act in OTurn but recovers when PlayerO advances to XTurn. No warning emitted.

### analyzeProgress

```
analyzeProgress : ExtractedStatechart -> ProgressReport

1. pruned = Projection.pruneUnreachableStates(statechart)
2. projections = Projection.projectAll(pruned)
3. readOnlyRoles = identifyReadOnlyRoles(projections)
4. deadlocks = detectDeadlocks(pruned)
5. starvation = detectStarvation(pruned, projections, readOnlyRoles)
6. readOnlyDiagnostics = readOnlyRoles |> map ReadOnlyRole
7. all = deadlocks @ starvation @ readOnlyDiagnostics
8. Return ProgressReport with Route, HasErrors, HasWarnings, StatesAnalyzed, RolesAnalyzed
```

Single entry point. Caller passes `ExtractedStatechart`; projections computed internally.

### Reachability Helper

Private `forwardReachable` function within `ProgressAnalysis`. Builds `Map<string, string list>` adjacency from **live transitions only** (`isLive`), recursive BFS from a start state. Same pattern as `Projection.pruneUnreachableStates` but filters dead transitions — intentional asymmetry (see detectStarvation notes).

## Module Placement

`src/Frank.Resources.Model/ProgressAnalysis.fs` — expert consensus (Syme, Seemann, Wlaschin, Wadler, Harel: 5/7).

Rationale: all input types and helper functions (`Projection.*`) are in this assembly. Zero dependency on parsers, ASTs, or `Frank.Statecharts`. Consistent with `findOrphanedTransitions` precedent. Keeps the leaf assembly self-contained — any consumer gets progress analysis without pulling in parsers.

### fsproj Compile Order

```xml
<Compile Include="TypeAnalysis.fs" />
<Compile Include="ResourceTypes.fs" />
<Compile Include="Projection.fs" />
<Compile Include="ProgressAnalysis.fs" />   <!-- NEW -->
<Compile Include="RuntimeTypes.fs" />
<Compile Include="AffordanceTypes.fs" />
```

## CLI Integration

### New Command

`src/Frank.Cli.Core/Commands/UnifiedValidateCommand.fs`

Top-level `validate` command registered alongside `extract` and `generate` in `Program.fs`.

```
frank validate --project game.fsproj --check-progress [--output-format text|json]
```

Follows `UnifiedExtractCommand` pattern:
- `--project` option (required)
- `--check-progress` flag (bool, default false)
- `--output-format` option (text|json, default text)
- Loads extraction state via `UnifiedCache.tryLoadFresh`
- For each `UnifiedResource` with `Statechart`, calls `analyzeProgress`
- Exit code 1 if any report has `HasErrors` (deadlocks)
- Warnings and info print but don't fail the process

### Output Formatting

Add `formatProgressReport` to `TextOutput.fs` and `JsonOutput.fs`.

Text output example:
```
Progress analysis for /games/{gameId} (5 states, 3 roles)
  [error] Deadlock: state Stuck has no advancing transitions (self-loops: getGame)
  [warn]  Starvation: role Admin excluded after state Phase2 (excluded from: Phase3, Phase4)
  [info]  Read-only role: Spectator (expected for observers)
```

### Merge Strategy with #133

Both #108 and #133 create `UnifiedValidateCommand.fs` with different flags (`--check-progress` and `--check-projection`). Different code paths, no logic overlap. Whichever merges first wins the file structure; second rebases and adds its flag. Analysis modules in separate assemblies — zero conflict on analysis code.

## Tests

New file: `test/Frank.Resources.Model.Tests/ProgressAnalysisTests.fs`

### fsproj Compile Order

After `ProjectionTests.fs`, before `Program.fs`.

### Fixtures

| Fixture | Purpose |
|---------|---------|
| TicTacToe chart | No deadlocks, no starvation, Spectator is read-only. Turn-taking (weak starvation) produces no warnings. |
| Deadlock chart | Non-final state with only self-loops for all roles |
| Dead transition deadlock | Non-final state where only advancing transition is `RestrictedTo []` |
| Starvation chart | Role permanently excluded after a state on all forward paths |
| Dead transition in forward path | Starvation regression: recovery path only via dead transition — must still report starved |
| All-unrestricted chart | No starvation possible (fast path) |
| Single final state | Empty report |
| Empty transitions chart | Initial state only, no transitions — deadlock |
| Cycle without final states | Non-terminating cycle, all roles active — no deadlock, no starvation (valid design) |
| Diamond topology | Two paths reconverge at active state — BFS finds recovery through inactive intermediates |
| Reachable dead-end | Non-initial non-final state with no outgoing transitions — deadlock |
| Multiple starvation entry points | Same role excluded from S1 and S2 independently — two diagnostics emitted |
| Disconnected chart via analyzeProgress | Pruning prevents false deadlocks from unreachable non-final states |

### Test Structure

```fsharp
module Frank.Resources.Model.Tests.ProgressAnalysisTests

[<Tests>]
let readOnlyRoleTests = testList "identifyReadOnlyRoles" [
    // Spectator in TicTacToe = read-only
    // PlayerX in TicTacToe = not read-only
    // All-unrestricted = no read-only roles
    // Role with only dead transitions (RestrictedTo []) = read-only
]

[<Tests>]
let deadlockTests = testList "detectDeadlocks" [
    // TicTacToe = no deadlocks
    // Self-loop-only state = deadlock with selfLoopEvents
    // Dead transition state = deadlock (RestrictedTo [] not live)
    // Final state with no transitions = not deadlock
    // Empty transitions from initial = deadlock
    // Reachable dead-end (non-initial, non-final, no outgoing) = deadlock
    // Cycle without final states = NOT deadlock (advancing transitions exist)
]

[<Tests>]
let starvationTests = testList "detectStarvation" [
    // TicTacToe PlayerX in OTurn = NOT starvation (weak, recovers via PlayerO)
    // Synthetic permanent exclusion = starvation with excludedStates
    // Read-only roles excluded from analysis
    // All-unrestricted = no starvation
    // Dead transition in forward path = starvation (BFS excludes dead edges)
    // Diamond topology = NOT starvation (BFS finds recovery through inactive intermediates)
    // Multiple entry points for same role = two distinct diagnostics
]

[<Tests>]
let analyzeProgressTests = testList "analyzeProgress" [
    // TicTacToe: HasErrors=false, HasWarnings=false, 1 ReadOnlyRole diagnostic
    // Deadlock chart: HasErrors=true
    // Starvation chart: HasWarnings=true
    // Disconnected chart: pruning prevents false deadlocks from unreachable states
    // Integration: correct Route, StatesAnalyzed, RolesAnalyzed
]
```

## Verification

1. `dotnet build Frank.sln` — compiles all projects
2. `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` — all tests pass
3. `dotnet test test/Frank.Resources.Model.Tests/` — progress analysis tests specifically
4. Manual: verify TicTacToe produces clean report (no deadlocks, no starvation, Spectator=read-only)
5. Manual: verify synthetic deadlock/starvation fixtures produce correct diagnostics
