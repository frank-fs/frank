# Frank.Alps: CurrentStateResolver returns a set of active states

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `regions`/`StateComposition` already ship as authoring-only (you can author orthogonal/AND composite states today). `CurrentStateResolver` and the excerpt filtering predicate stay single-state-scoped, so a `regions` composite isn't yet something the resolver can report as "these N regions are concurrently active" for filtering. Change `CurrentStateResolver` from `string -> Uri option` to `string -> Uri list` (the concurrently-active state URIs, one per active orthogonal region; empty list means "no opinion for this resource," same as today's `None`), and change the filtering predicate from "does the resolved state satisfy this edge's `FromState`" to an existential match: "does *any* element of the resolved active-state set satisfy this edge's `FromState`."

**Tracks:** frank-fs/frank#490

**Explicitly out of scope** (per the issue): conjunctive AND-guards / multi-region fan-out targets — that's a separate wrapper type, frank-fs/frank#489, not touched here.

**Design doc:** `docs/superpowers/specs/2026-08-02-frank-alps-protocol-design.md`

## Global Constraints

- `CurrentStateResolver` is a public type (`src/Frank.Alps/Excerpt.fsi:10`) — this is a breaking signature change for any caller supplying `Some resolver`. Update every caller in this repo (tests, sample) as part of this task; there is no deprecation path needed (trunk-based, pre-1.0-in-spirit package).
- `Excerpt.satisfiesState`'s own signature (`current: Uri -> candidate: Descriptor -> bool`) does **not** need to change — only its call site in `Alps.excerpt` needs to iterate the active-state list existentially. Do not add a second overload of `satisfiesState`; one Uri at a time is still the right shape for that function.
- Every `.fs` file has a matching `.fsi` (`CLAUDE.md`). Update `Excerpt.fsi`/`Excerpt.fs` together.
- Test framework is Expecto.
- Commit directly to this task's branch when done (trunk-based repo — no PR needed once merged back to master by the coordinator).

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/Frank.Alps/Excerpt.fsi` | Modify | `CurrentStateResolver` type signature + doc comment |
| `src/Frank.Alps/Excerpt.fs` | Modify | `CurrentStateResolver` type; `Alps.excerpt`'s state-filtering call site becomes an existential match over the active-state list |
| `test/Frank.Alps.Tests/FilteringIntegrationTests.fs` | Modify | Update the one resolver constructed in this file (`Uri option` → `Uri list`); add a new test demonstrating multi-region existential OR matching |
| `src/Frank.Alps/README.md` | Modify (if it shows the old signature) | Keep in sync |
| `RELEASE_NOTES.md` | Modify | Note the signature change, mirroring this repo's existing release-notes convention |

---

### Task 1: `CurrentStateResolver` → `string -> Uri list`, existential filtering

**Files:** see File Structure above.

**Interfaces:**
- Consumes: existing `Excerpt.satisfiesState: current: Uri -> candidate: Descriptor -> bool` (unchanged), existing `Alps.excerpt: resolver: CurrentStateResolver option -> RequestDelegate`.
- Produces: `CurrentStateResolver = string -> Uri list` (was `string -> Uri option`). `Alps.excerpt`'s outer `resolver: CurrentStateResolver option` is unchanged — the *outer* option still means "no resolver configured at all, don't filter"; the resolver's own return value collapses "couldn't determine a state" and "determined it, but there's exactly one" into the same list shape (empty vs. one-or-more elements), replacing the old inner `Uri option`.

**Exact change** to `src/Frank.Alps/Excerpt.fsi`:

Current:
```fsharp
/// Answers "what state is this specific resource in", if the application supplies one -- a plain
/// function wired at composition time, no dependency on `Frank.Provenance` or any other package. The
/// natural implementation queries a provenance/event store; absent, or returning `None`, means state
/// filtering simply does not apply (design doc, *State-based filtering*).
type CurrentStateResolver = string -> Uri option
```

Required:
```fsharp
/// Answers "what states is this specific resource concurrently in", if the application supplies one --
/// a plain function wired at composition time, no dependency on `Frank.Provenance` or any other package.
/// One element per active orthogonal region (design doc, *State-based filtering*); an empty list means
/// state filtering simply does not apply for this resource, the same as the old `None`. A `from`-state
/// candidate is satisfied if it is satisfied by *any* element of the returned list (existential/OR
/// match across regions) -- this reaches independent-region OR filtering correctly but does not reach
/// conjunctive AND-guards or multi-region fan-out targets; see frank-fs/frank#489 for that.
type CurrentStateResolver = string -> Uri list
```

**Exact change** to `src/Frank.Alps/Excerpt.fs`:

1. The type alias itself: `type CurrentStateResolver = string -> Uri list` (mirror the `.fsi` doc comment).

2. `Alps.excerpt`'s state-filtering call site — current code:

```fsharp
let stateFiltered =
    match resolver with
    | None -> authAllowed
    | Some resolve ->
        match resolve ctx.Request.Path.Value with
        | None -> authAllowed
        | Some current ->
            authAllowed
            |> List.filter (fun d ->
                List.isEmpty d.From || d.From |> List.exists (Excerpt.satisfiesState current))
```

Required:

```fsharp
let stateFiltered =
    match resolver with
    | None -> authAllowed
    | Some resolve ->
        match resolve ctx.Request.Path.Value with
        | [] -> authAllowed
        | activeStates ->
            authAllowed
            |> List.filter (fun d ->
                List.isEmpty d.From
                || d.From
                   |> List.exists (fun candidate -> activeStates |> List.exists (fun s -> Excerpt.satisfiesState s candidate)))
```

**Tests to update/add in `FilteringIntegrationTests.fs`:**

- The existing resolver at line ~281 (`testTask "Alps.excerpt (Some resolver) excludes a from-declared transition whose state is unsatisfied"`) constructs `let resolver: CurrentStateResolver = fun path -> match path with | "/games/1" -> Some(Uri "...open") | "/games/2" -> Some(Uri "...closed") | _ -> None`. Change each `Some x` to `[ x ]` and the `None` fallback to `[]`. No other change to that test's assertions is needed — the existing single-state behavior must be preserved exactly when the resolver only ever returns zero-or-one states.
- **New test:** a resolver that returns *two* active states for one path (simulating two concurrently-active orthogonal regions), where a `from`-declared transition's candidate matches only the *second* of the two returned states. Assert the transition is still included in the excerpt (proving the existential/OR match works across a multi-element list, not just a singleton). Mirror the existing test's structure (`createServer`, `request`, `topLevelIds`) exactly.

**Verification:** `dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj` must pass on all three TFMs (`net8.0`, `net9.0`, `net10.0`). Also run `test/Frank.Alps.Tests/ExcerptTests.fs` and `test/Frank.Alps.Tests/SampleIntegrationTests.fs` to confirm the unrelated `satisfiesState` unit tests and the `Alps.excerpt None` sample path are unaffected. `grep -rn "CurrentStateResolver" src/ test/ sample/` before finishing to confirm every caller was updated.
