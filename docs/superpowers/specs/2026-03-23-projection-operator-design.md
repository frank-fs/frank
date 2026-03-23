# Projection Operator — Per-Role ALPS Profiles from Global Protocol

**Issue:** #107 — Projection Operator — Per-Role ALPS Profiles from Global Protocol
**Date:** 2026-03-23
**Status:** Design approved

## Overview

Build a projection operator that takes a global statechart with role definitions and derives per-role ALPS profiles — one per role, containing only the descriptors that role can trigger or observe. This is the central mechanism for making Frank's implicit per-role protocol views explicit and verifiable.

The projection operator is a pure function in `Frank.Resources.Model` (zero-dep assembly). It operates on enriched `ExtractedStatechart` types and produces filtered statecharts. The caller swaps the projected statechart into a `UnifiedResource` copy, then passes it to the existing `UnifiedAlpsGenerator.generate` unchanged.

**Scope:** Build-time implementation (CLI pipeline). Request-time middleware (content negotiation) is designed here but implemented in #130.

## Design Decisions

### One ALPS profile per role (not per state)

ALPS profiles are vocabulary documents describing what a role can encounter across its entire interaction. Per-state profiles would create file explosion, break cacheability (invalidated on every state transition), and fight the ALPS spec's intent. State scoping is handled by `availableInStates` extensions on each descriptor.

Output structure:
- Global profile: `/alps/games` — complete vocabulary, all roles/states
- Per-role profiles: `/alps/games-playerx`, `/alps/games-playero` — filtered subsets

Slugs use lowercase role names appended with a hyphen separator.

### Pre-resolved role constraints on transitions (not a separate guard map)

Each `TransitionSpec` carries a `RoleConstraint` DU indicating which roles can trigger it. The extraction step eagerly resolves guard + state → permitted roles. The projection operator receives fully-resolved data.

This matches MPST global types where each interaction `p -> q: <label>` already names its participants. Projection is a syntactic filter, not a lookup-dependent computation.

A separate `GuardRoleMap: Map<string, Map<string, string list>>` was rejected — it introduces indirection with no session-type analogue, creates dangling reference risks, and makes the projection operator dependent on external state.

### Uniform projection — no safe descriptor special case

Guards are the sole arbiter of descriptor visibility. No special treatment for safe (GET) descriptors. In MPST, projection inspects participant names, not message types. HTTP method safety is about side effects, not access scope.

Unguarded transitions (no guard) are available to all roles — open-world default matching HTTP conventions. The cross-validator emits a warning for unmapped transitions so teams can confirm intent. Closed-world with totality check deferred to #171.

### Pipeline: enrich → project → generate (Approach C)

Unidirectional pipeline where projection operates on enriched portable types, not on generated ALPS output.

```
ExtractedStatechart (with Transitions + RoleConstraint)
  |> Projection.projectForRole roleName  -- pure filter + prune
  → ExtractedStatechart (projected)

Caller then:
  → swap projected statechart into UnifiedResource copy
  → UnifiedAlpsGenerator.generate(projectedResource, baseUri)
  → per-role ALPS JSON
```

The projection operator lives in `Frank.Resources.Model` (zero-dep) and knows nothing about `UnifiedResource` or ALPS. The callsite in `Frank.Cli.Core` handles the `UnifiedResource` assembly. `HttpCapabilities` on the `UnifiedResource` are not filtered — the ALPS generator already scopes transition descriptors by the states present in the statechart.

### Same type in, same type out

`projectForRole : string -> ExtractedStatechart -> ExtractedStatechart`. The projected statechart has the same structure as the unprojected one — it's a subset, not a different kind. This gives free composability: any function consuming `ExtractedStatechart` works on both global and projected views.

### Decomposed into composable endomorphisms

Following Seemann's recommendation:

```fsharp
let filterTransitionsByRole (roleName: string) : ExtractedStatechart -> ExtractedStatechart
let pruneUnreachableStates : ExtractedStatechart -> ExtractedStatechart
let projectForRole roleName = filterTransitionsByRole roleName >> pruneUnreachableStates
```

Each function is independently reusable and testable. `pruneUnreachableStates` is useful after other transformations too.

### Completeness check (post-projection validation)

After projecting all roles, verify that every transition in the global statechart appears in at least one role's projection. This is a simple set-coverage check — the union of all projected transition sets must equal the global transition set.

Other verification steps have homes elsewhere:
- Deadlock detection → #108
- Connectedness + mixed choice → #133

## New Types in `Frank.Resources.Model`

### `RoleConstraint` DU

```fsharp
/// Whether a transition is available to all roles or restricted to specific roles.
type RoleConstraint =
    | Unrestricted                    // no guard — all roles (broadcast)
    | RestrictedTo of string list     // guard resolved — these roles only (directed)
```

Maps to session type semantics: `Unrestricted` = broadcast message, `RestrictedTo` = directed message.

### `TransitionSpec` record

```fsharp
/// A single transition in the extracted statechart.
/// Domain-neutral: no ALPS/SCXML/format-specific concepts.
type TransitionSpec =
    { /// The event name (semantic transition name, e.g., "makeMove", "getGame")
      Event: string
      /// Source state key (e.g., "XTurn")
      Source: string
      /// Target state key (e.g., "OTurn")
      Target: string
      /// Guard name controlling this transition (None = unguarded).
      /// Retained for diagnostics and cross-validation, not used by projection.
      Guard: string option
      /// Pre-resolved role constraint. Extraction resolves guard + state → roles.
      Constraint: RoleConstraint }
```

Note: not `[<Struct>]` — 5 fields with reference types (`string option`, `RoleConstraint` DU) means no meaningful benefit from struct layout.

### `ExtractedStatechart` addition

One new field:

```fsharp
type ExtractedStatechart =
    { RouteTemplate: string
      StateNames: string list
      InitialStateKey: string
      GuardNames: string list
      StateMetadata: Map<string, StateInfo>
      Roles: RoleInfo list
      /// All transitions in the statechart (NEW).
      Transitions: TransitionSpec list }
```

`GuardNames` is retained for backward compatibility and cross-validation. The `Guard` field on `TransitionSpec` is the per-transition version.

### `ProjectedProfiles` addition

Flat map for per-role profiles (slug already encodes the role):

```fsharp
type ProjectedProfiles =
    { AlpsProfiles: Map<string, string>
      /// Per-role ALPS profiles keyed by slug (e.g., "games-playerx" → JSON) (NEW).
      RoleAlpsProfiles: Map<string, string>
      OwlOntologies: Map<string, string>
      ShaclShapes: Map<string, string>
      JsonSchemas: Map<string, string> }
```

`ProjectedProfiles.empty` and `ProjectedProfiles.isEmpty` must be updated to include `RoleAlpsProfiles`.

## Projection Operator (`Frank.Resources.Model.Projection` module)

```fsharp
module Projection =

    /// Filter transitions to those available to the given role.
    /// Unrestricted transitions survive in all projections.
    /// RestrictedTo transitions survive only if roleName is in the list.
    let filterTransitionsByRole (roleName: string) (statechart: ExtractedStatechart) : ExtractedStatechart

    /// Remove states unreachable from the initial state via surviving transitions.
    /// Initial state is always retained. Updates StateNames, StateMetadata,
    /// and removes transitions referencing pruned states.
    let pruneUnreachableStates (statechart: ExtractedStatechart) : ExtractedStatechart

    /// Project a statechart for a single role: filter then prune.
    let projectForRole (roleName: string) : ExtractedStatechart -> ExtractedStatechart

    /// Project for all roles defined in the statechart.
    /// Returns empty map if statechart has no roles (no-op).
    let projectAll (statechart: ExtractedStatechart) : Map<string, ExtractedStatechart>

    /// Post-projection completeness check: every global transition must appear
    /// in at least one role's projection. Returns orphaned transitions.
    let findOrphanedTransitions
        (global: ExtractedStatechart)
        (projections: Map<string, ExtractedStatechart>)
        : TransitionSpec list
```

The projection operator is total — every input produces a valid output. No `Result`, no `Option`. A projection with one state and no transitions is valid (role can observe but not act).

Role matching is case-sensitive — role names from `RoleInfo.Name` must match `RestrictedTo` entries exactly. The extraction step is responsible for consistent casing.

## Build-Time Integration (CLI Pipeline)

### Pipeline flow

```
UnifiedExtractor.extract → UnifiedResource list
  → for each resource with a Statechart that has Roles:
      Projection.projectAll → Map<string, ExtractedStatechart>
      → for each (roleName, projectedChart):
          copy UnifiedResource with Statechart = Some projectedChart
          → UnifiedAlpsGenerator.generate(projectedResource, baseUri) → per-role ALPS JSON
      → also generate global (unprojected) ALPS as today
      → run findOrphanedTransitions, emit warnings for orphaned transitions
  → store global profiles in ProjectedProfiles.AlpsProfiles (existing)
  → store per-role profiles in ProjectedProfiles.RoleAlpsProfiles (new)
```

Resources with no `Roles` (empty list) skip projection — no per-role profiles generated. This is the common case for most resources today.

### Extraction: populating `Transitions`

The `UnifiedExtractor` currently captures `StateNames`, `GuardNames`, and `StateMetadata` from the statechart builder. It must additionally extract per-state transitions with their guard assignments and resolve `RoleConstraint` values.

Transition data comes from `StateMachine<'State, 'Event, 'Context>` at extraction time:
- `Transition` function provides state → event → state mappings
- `Guards` list provides guard names and their runtime predicates
- `RoleDefinition` list (from `StatefulResourceBuilder`) provides role names

The extraction step resolves: for each (state, transition), which guards apply, and for each guard, which roles are permitted. This resolution uses the `RoleDefinition.ClaimsPredicate` at extraction time (not at projection time). The extracted `RoleConstraint` is the pre-resolved result.

### CLI output

Generated via the existing `frank generate --format alps` command:

```
projected/games.alps.json           ← global
projected/games-playerx.alps.json   ← PlayerX projection
projected/games-playero.alps.json   ← PlayerO projection
```

### ALPS document annotations

The ALPS generator (not the projection operator) adds annotations to projected profiles:
- `projectedRole` ext element identifying the role (uses `Classification.ProjectedRoleExtId`)
- `availableInStates` ext on each transition descriptor (NEW — must be built in this issue; the ALPS generator does not currently emit this)
- No `protocolState` at build-time (runtime-only "you are here" pin)

### ALPS cross-linking

Added by `UnifiedAlpsGenerator.fs` (not `GeneratorCommon.fs`):
- Global profile includes `link` elements to per-role profiles with `rel: "related"`
- Per-role profiles include a `link` element back to the global profile with `rel: "profile"`

## Request-Time Integration (Design Only — Implemented in #130)

### Flow

1. Startup: load `ProjectedProfiles.RoleAlpsProfiles` from cached extraction state
2. Middleware resolves authenticated user's claims → role name (via `RoleDefinition.ClaimsPredicate`)
3. Dictionary lookup: role slug → pre-computed profile string
4. Serve with `Link: </alps/games-playerx>; rel="profile"` header
5. Unauthenticated/unknown role → global profile with `Link: </alps/games>; rel="profile"`

### What this issue provides for #130

- `ProjectedProfiles.RoleAlpsProfiles` field populated at build-time
- `Projection.projectForRole` and `projectAll` functions usable at startup
- Per-role ALPS documents with `projectedRole` ext and cross-links

### What #130 adds

- Claims → role resolution in middleware
- `Vary: Authorization` on responses where `Link: rel="profile"` varies by role
- `ReadOnlyMemory<byte>` optimization for zero-copy response writing
- `protocolState` ext injection based on current resource state

## Files Modified

| File | Change |
|------|--------|
| `src/Frank.Resources.Model/ResourceTypes.fs` | Add `RoleConstraint`, `TransitionSpec`, `Transitions` field on `ExtractedStatechart`, `RoleAlpsProfiles` field on `ProjectedProfiles`, update `empty` and `isEmpty` |
| `src/Frank.Resources.Model/Projection.fs` | **New file.** `filterTransitionsByRole`, `pruneUnreachableStates`, `projectForRole`, `projectAll`, `findOrphanedTransitions` |
| `src/Frank.Resources.Model/Frank.Resources.Model.fsproj` | Add `Projection.fs` to compile order (after `ResourceTypes.fs`) |
| `src/Frank.Cli.Core/Unified/UnifiedAlpsGenerator.fs` | Emit `projectedRole` ext, `availableInStates` ext, and cross-link `link` elements on projected profiles |
| `src/Frank.Cli.Core/Commands/UnifiedGenerateCommand.fs` | Integrate projection into generation pipeline; emit orphaned transition warnings |
| `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs` | Populate `Transitions` on `ExtractedStatechart` during extraction; resolve `RoleConstraint` from guards + roles |
| `test/Frank.Resources.Model.Tests/` | **New test project or tests.** Projection operator unit tests |

### Binary compatibility

Adding `Transitions` to `ExtractedStatechart` and `RoleAlpsProfiles` to `ProjectedProfiles` are binary-breaking changes. `UnifiedExtractionState` is serialized to `model.bin` via `UnifiedCache`. The `ToolVersion` in `UnifiedCache` must be bumped so stale caches are invalidated.

## Verification

1. `dotnet build Frank.sln` — all projects compile
2. `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` — all existing tests pass
3. Projection unit tests verify:
   - `filterTransitionsByRole` keeps correct transitions per role
   - `pruneUnreachableStates` removes unreachable states, keeps initial, updates StateNames/StateMetadata/Transitions
   - `projectAll` produces one projection per role; returns empty map for no-role statecharts
   - `findOrphanedTransitions` detects uncovered transitions as `TransitionSpec list`
   - Unrestricted transitions appear in all projections
   - RestrictedTo transitions appear only in permitted role projections
   - Role matching is case-sensitive
4. TicTacToe sample: `frank generate --format alps` generates correct per-role ALPS files
   - PlayerX profile: `makeMove` with `availableInStates: "XTurn"` only
   - PlayerO profile: `makeMove` with `availableInStates: "OTurn"` only
   - Both profiles: `getGame` available in all states (unguarded)
   - Global profile links to per-role profiles; per-role profiles link back

## Related Issues

- #130 — Request-time content negotiation (uses types/functions from this issue)
- #133 — Cross-validator projection consistency (connectedness, mixed choice checks)
- #108 — Progress analysis (deadlock, starvation detection)
- #171 — Closed-world projection with totality check (future exploration)
- #91 — Cross-validator (completeness check extends this)

## Expert Consensus

Design validated by 6 expert perspectives:

| Expert | Key contribution |
|--------|-----------------|
| Amundsen | ALPS is vocabulary, not runtime snapshot. One profile per role. Cross-linking with `rel: "related"` / `rel: "profile"`. |
| Darrel Miller | HTTP safety ≠ access scope. Cacheability requires stable profile URLs. `Vary: Authorization` for role-dependent `Link` headers. |
| Wadler | MPST projection correspondence. `RoleConstraint` DU = broadcast vs directed. Completeness check = projectability verification. Uniform projection (no safe special case). |
| Syme | `TransitionSpec` completes the type. Pre-resolved roles eliminate nested map smell. Pipeline-friendly argument order. |
| Wlaschin | "Parse, don't validate" — resolve at extraction. `RoleConstraint` DU makes semantics explicit. Projection = what you can *do*. |
| Seemann | Decompose into composable endomorphisms. Same type in/out. Total function (no Result). Dependency rejection pattern. |
