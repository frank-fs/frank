Implement #108: Progress Analysis — Deadlock and Starvation Detection.

`gh issue view 108` has full details. Projection-only scope (no dual features — deferred to v7.4.0).

PR #172 just landed the projection operator. Key inputs:
- `Projection.projectAll : ExtractedStatechart -> Map<string, ExtractedStatechart>` in src/Frank.Resources.Model/Projection.fs
- `TransitionSpec` with `RoleConstraint` (Unrestricted | RestrictedTo) in src/Frank.Resources.Model/ResourceTypes.fs
- `ExtractedStatechart.Transitions` and `.States` for reachability analysis

What to build:
1. New module `src/Frank.Statecharts/Analysis/ProgressAnalysis.fs` — pure functions: `detectDeadlocks`, `detectStarvation`, `analyzeProgress`
2. Types: `ProgressDiagnostic` DU (Deadlock of state | Starvation of role * states | ReadOnlyRole of role) and `ProgressReport`
3. Deadlock: for each non-final state, check if ANY role has an unsafe/idempotent transition. No role can act = deadlock.
4. Starvation: for each role, build reachability graph. Find SCCs where role has no transitions. Find terminal paths where role is excluded.
5. CLI integration: add `--check-progress` flag to `src/Frank.Cli.Core/Commands/UnifiedValidateCommand.fs`
6. Tests in `test/Frank.Statecharts.Tests/Analysis/ProgressAnalysisTests.fs`

Wadler's guidance: completeness check already exists in #107 (`findOrphanedTransitions`). This issue adds deadlock + starvation. Connectedness and mixed choice go to #133.

Expert reviewers for this PR: Wadler (MPST liveness), Harel (statechart reachability).

Start with /brainstorm, then implement. Run `dotnet build Frank.sln` and `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` before claiming complete. PR must include `Closes #108`. Run /simplify then /expert-review before creating the PR.
