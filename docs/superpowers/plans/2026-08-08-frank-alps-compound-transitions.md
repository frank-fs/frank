# Frank.Alps Compound Transitions — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `ProtocolTransition`'s shape with a compound-transition-capable one (`StateGuard` for conjunctive/disjunctive/negated guards, `TransitionTarget` for fan-out and history pseudostates), ship guard-side enforcement in `Excerpt.fs` now, and prove the mechanism with a light traffic-light-with-pedestrian-crossing sample before the real (cross-role) order-fulfillment build.

**Architecture:** `Descriptor` gains two new fields (`Guard: StateGuard option`, `Targets: TransitionTarget list`), mutually recursive with two new types (`DescriptorTypes.fs`). Two new combinators, `guardedBy`/`entersRegions` (`Descriptor.fs`), sit alongside unchanged `from`/`rt`. `ProtocolGraph.ofProfile` derives the same-named `ProtocolTransition`, now `{ FromGuard: StateGuard option; Transition: Descriptor; ToTargets: TransitionTarget list }`, folding `Guard`-over-`From` and `Targets`-over-`Rt`. `Excerpt.fs`'s state-filtering predicate becomes a recursive fold over `StateGuard` (`satisfiesGuard`) instead of a flat existential match. A new sample project proves both derivation and filtering end to end.

**Design doc:** `docs/superpowers/specs/2026-08-08-frank-alps-compound-transitions-design.md`

## Global Constraints

- Every `.fs` file gets a matching `.fsi` immediately above it in `<Compile>` order (`CLAUDE.md`). Update both together in every task.
- Test framework is **Expecto**, matching `Frank.Alps.Tests`.
- `Descriptor`, `StateGuard`, `TransitionTarget`, `ProtocolTransition` are plain reference types — do not add `[<Struct>]` to any of these (unchanged posture from the parent plan; rides on #485, not decided here).
- Commit after every task (this repo is trunk-based — commit directly, no PR).
- **Wire format is out of scope for this plan.** `StateGuard`/`TransitionTarget` ship as pure in-process/derived types only — no `ext`-marker serialization. **Decided** (user, 2026-08-08): option 4, "don't serialize at all" — matches `ProtocolGraph`'s existing "read-only, nothing executes" posture, and ALPS is already a per-request/per-role projection of current state, not a write-side model. Other options recorded but not pursued (see design doc, *Wire format*).

## Out of scope for this plan

- **Fan-out (write) enforcement.** `ToTargets` is authored and derived; nothing commits a multi-region state change. Actor-model work, tracked separately.
- **Wire-format encoding** of `StateGuard`/`ToTargets` — decided against (option 4), see Global Constraints.
- **Cross-role fan-out targets**, **timed transitions**, **transition actions/side-effects** — unchanged non-goals from the design doc.
- **The order-fulfillment sample.** Built after this plan lands and the traffic-light sample proves the mechanism — separate follow-on, not a task here.
- **Full multi-document profile hosting/discovery.** Untouched — `def`-based cross-role matching (used by the future order-fulfillment sample) needs no change to this plan's scope.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/Frank.Alps/DescriptorTypes.fsi`/`.fs` | Modify | Add `StateGuard`, `TransitionTarget`; add `Guard`/`Targets` fields to `Descriptor` |
| `src/Frank.Alps/Descriptor.fsi`/`.fs` | Modify | Add `guardedBy`, `entersRegions` |
| `src/Frank.Alps/ProtocolGraph.fsi`/`.fs` | Modify | Replace `ProtocolTransition` shape; rewrite `ofProfile`'s derivation/gating rule |
| `src/Frank.Alps/Excerpt.fsi`/`.fs` | Modify | Replace flat existential filter with `satisfiesGuard` fold |
| `test/Frank.Alps.Tests/DescriptorTypesTests.fs` | Modify | `emptyDescriptor` helper gains `Guard = None; Targets = []` |
| `test/Frank.Alps.Tests/ProtocolGraphTests.fs` | Rewrite | New gating rule, `Guard`/`Targets` precedence, collapsed multi-`from` edge |
| `test/Frank.Alps.Tests/ExcerptTests.fs` (or existing equivalent) | Modify | `satisfiesGuard` coverage |
| `test/Frank.Alps.Tests/CompoundTransitionTests.fs` | Create | `guardedBy`/`entersRegions` combinator tests |
| `sample/Frank.Alps.TrafficLightSample/*` | Create | Traffic light + pedestrian crossing — proves `StateGuard`/`TransitionTarget` end to end |
| `src/Frank.Alps/README.md` | Modify | `ProtocolGraph.ofProfile` section — document new shape and the two behavior changes |
| `Frank.sln` | Modify | Register the new sample project |

---

### Task 1: `StateGuard`, `TransitionTarget`, `Descriptor.Guard`/`Descriptor.Targets`

**Files:**
- Modify: `src/Frank.Alps/DescriptorTypes.fsi`, `src/Frank.Alps/DescriptorTypes.fs`
- Modify: `test/Frank.Alps.Tests/DescriptorTypesTests.fs`

**Interfaces:**
- Consumes: `Descriptor` (existing).
- Produces: `StateGuard`, `TransitionTarget`; `Descriptor.Guard: StateGuard option`, `Descriptor.Targets: TransitionTarget list`.

- [ ] **Step 1: Write the failing tests**

Update `emptyDescriptor` in `DescriptorTypesTests.fs` to include `Guard = None; Targets = []`. Add:

```fsharp
test "a Descriptor can hold a StateGuard via Guard without a compiler error" {
    let a = emptyDescriptor "a"
    let d = { emptyDescriptor "t" with Guard = Some(StateGuard.State a) }
    match d.Guard with
    | Some(StateGuard.State s) -> Expect.equal s.Id "a" ""
    | _ -> failwith "expected State"
}

test "StateGuard nests: All/Any/Not wrap StateGuard, not Descriptor" {
    let a, b = emptyDescriptor "a", emptyDescriptor "b"
    let g = StateGuard.All [ StateGuard.State a; StateGuard.Any [ StateGuard.State b; StateGuard.Not(StateGuard.State a) ] ]
    match g with
    | StateGuard.All [ StateGuard.State _; StateGuard.Any [ StateGuard.State _; StateGuard.Not(StateGuard.State _) ] ] -> ()
    | _ -> failwith "expected nested All/Any/Not"
}

test "a Descriptor can hold TransitionTarget list via Targets" {
    let a = emptyDescriptor "region"
    let d = { emptyDescriptor "t" with Targets = [ TransitionTarget.EnterState a; TransitionTarget.History a; TransitionTarget.DeepHistory a ] }
    Expect.equal d.Targets.Length 3 ""
}
```

Expected build failure: `StateGuard`/`TransitionTarget` not defined, `Guard`/`Targets` not fields of `Descriptor`.

- [ ] **Step 2: Update `DescriptorTypes.fsi`**

Add fields to `Descriptor` (after `From`, before `Rel` — matches design doc field order) and the two new co-recursive types after `DescriptorRef`:

```fsharp
type Descriptor =
    { // ...existing fields...
      From: Descriptor list
      Guard: StateGuard option
      Rt: Descriptor option
      Targets: TransitionTarget list
      // ...remaining existing fields...
      Descriptors: Descriptor list }

and DescriptorRef =
    | Local of Descriptor
    | External of Uri

/// A structural guard tree over descriptor state, evaluated against a resolver's active-state set.
/// `State`/`Predicate` hold a plain, local Descriptor -- cross-role composition connects via a shared
/// `Def` URI on independently-authored descriptors, not via a cross-document reference (design doc,
/// 2026-08-08-frank-alps-compound-transitions-design.md).
and StateGuard =
    | State of Descriptor
    | Not of StateGuard
    | All of StateGuard list
    | Any of StateGuard list
    | Predicate of Descriptor

/// Where a fan-out transition enters. Always local to the same document as the transition -- a
/// transition can only enter its own document's orthogonal regions.
and TransitionTarget =
    | EnterState of Descriptor
    | History of Descriptor
    | DeepHistory of Descriptor
```

- [ ] **Step 3: Update `DescriptorTypes.fs`** — mirror the `.fsi` exactly (plain record/DU definitions, no logic).

- [ ] **Step 4: Update every other `emptyDescriptor`-style test helper** in `test/Frank.Alps.Tests/*` that constructs a `Descriptor` record literal — grep for `Descriptors = []` to find them all; each needs `Guard = None; Targets = []` added or the whole test project fails to build.

```bash
grep -rn "Descriptors = \[\]" test/Frank.Alps.Tests/*.fs
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps/DescriptorTypes.fsi src/Frank.Alps/DescriptorTypes.fs test/Frank.Alps.Tests
git commit -m "feat(alps): add StateGuard, TransitionTarget, Descriptor.Guard/Targets"
```

---

### Task 2: `guardedBy`, `entersRegions`

**Files:**
- Modify: `src/Frank.Alps/Descriptor.fsi`, `src/Frank.Alps/Descriptor.fs`
- Create: `test/Frank.Alps.Tests/CompoundTransitionTests.fs`
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj` (add the new file before `Program.fs`)

**Interfaces:**
- Consumes: `Descriptor`, `StateGuard`, `TransitionTarget` (Task 1).
- Produces: `guardedBy: StateGuard -> Descriptor -> Descriptor`, `entersRegions: TransitionTarget list -> Descriptor -> Descriptor`.

- [ ] **Step 1: Write the failing tests**

```fsharp
module Frank.Alps.Tests.CompoundTransitionTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "guardedBy, entersRegions"
        [ test "guardedBy sets Guard, leaving From untouched" {
              let vehicleRed = semantic "vehicleRed"
              let pedWaiting = semantic "pedWaiting"
              let d = unsafe "walk" |> guardedBy (StateGuard.All [ StateGuard.State vehicleRed; StateGuard.State pedWaiting ])
              Expect.isTrue d.Guard.IsSome ""
              Expect.equal d.From [] ""
          }

          test "entersRegions sets Targets, leaving Rt untouched" {
              let vehicleFlashing = semantic "vehicleFlashing"
              let pedFlashing = semantic "pedFlashing"
              let d = unsafe "emergencyOverride" |> entersRegions [ TransitionTarget.EnterState vehicleFlashing; TransitionTarget.EnterState pedFlashing ]
              Expect.equal d.Targets.Length 2 ""
              Expect.equal d.Rt None ""
          }

          test "guardedBy and entersRegions compose with each other and with from/rt" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let d =
                  unsafe "x"
                  |> from [ a ]
                  |> rt b
                  |> guardedBy (StateGuard.State a)
                  |> entersRegions [ TransitionTarget.EnterState c ]
              Expect.isTrue (d.From <> [] && d.Guard.IsSome && d.Rt.IsSome && d.Targets <> []) ""
          } ]
```

- [ ] **Step 2: Run to verify failure**, then append to `Descriptor.fsi`:

```fsharp

/// Sets an explicit guard tree, independent of `from`. `ProtocolGraph.ofProfile` prefers `Guard` over
/// deriving one from `From` when both are present.
val guardedBy: guard: StateGuard -> Descriptor -> Descriptor

/// Sets explicit fan-out targets, independent of `rt`. `ProtocolGraph.ofProfile` prefers `Targets` over
/// deriving one from `Rt` when both are present.
val entersRegions: targets: TransitionTarget list -> Descriptor -> Descriptor
```

Append to `Descriptor.fs`:

```fsharp

let guardedBy (guard: StateGuard) (d: Descriptor) : Descriptor = { d with Guard = Some guard }
let entersRegions (targets: TransitionTarget list) (d: Descriptor) : Descriptor = { d with Targets = targets }
```

- [ ] **Step 3: Run tests, verify pass. Commit.**

```bash
git add src/Frank.Alps/Descriptor.fsi src/Frank.Alps/Descriptor.fs test/Frank.Alps.Tests
git commit -m "feat(alps): guardedBy, entersRegions combinators"
```

---

### Task 3: Replace `ProtocolTransition`, rewrite `ofProfile`

**Files:**
- Modify: `src/Frank.Alps/ProtocolGraph.fsi`, `src/Frank.Alps/ProtocolGraph.fs`
- Rewrite: `test/Frank.Alps.Tests/ProtocolGraphTests.fs`

**Interfaces:**
- Consumes: `Descriptor`, `StateGuard`, `TransitionTarget` (Tasks 1–2).
- Produces: `ProtocolTransition` (new shape), `ProtocolGraph.ofProfile` (new gating/derivation rule).

**Background:** two deliberate behavior changes from today, both recorded in the design doc:
1. A transition using plain `from [A; B]` now collapses into **one** edge with `FromGuard = Some (Any [State A; State B])`, not two edges (today's behavior).
2. `ToTargets` non-empty is now the *only* requirement to emit an edge — `FromGuard` is independently optional (`None` = unconditional). A transition with `rt` but no `from`/`guardedBy` now yields an edge, where today it's excluded.

- [ ] **Step 1: Rewrite `ProtocolGraphTests.fs`** — replace the existing five tests (multi-`from` expansion, missing-`from`-or-`rt` exclusion) with:

```fsharp
module Frank.Alps.Tests.ProtocolGraphTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "ProtocolGraph.ofProfile"
        [ test "from [A] |> rt B yields one edge, FromGuard = Some (State A)" {
              let a, t, b = semantic "a", unsafe "t" |> from [ semantic "a" ], semantic "b"
              // (author with matching identity, not two unrelated `a` values)
              let aState = semantic "a"
              let tt = unsafe "t" |> from [ aState ] |> rt b
              match ProtocolGraph.ofProfile [ aState; tt; b ] with
              | [ { FromGuard = Some(StateGuard.State s); ToTargets = [ TransitionTarget.EnterState target ] } ] ->
                  Expect.equal s.Id "a" ""
                  Expect.equal target.Id "b" ""
              | other -> failwithf "expected one State-guarded edge, got %A" other
          }

          test "from [A; B] |> rt C collapses into one edge, FromGuard = Some (Any [State A; State B])" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let t = unsafe "t" |> from [ a; b ] |> rt c
              match ProtocolGraph.ofProfile [ a; b; t; c ] with
              | [ { FromGuard = Some(StateGuard.Any [ StateGuard.State s1; StateGuard.State s2 ]) } ] ->
                  Expect.equal (s1.Id, s2.Id) ("a", "b") ""
              | other -> failwithf "expected one Any-guarded edge, got %A" other
          }

          test "rt alone (no from, no guardedBy) now yields one unconditional edge" {
              let c = semantic "c"
              let t = unsafe "t" |> rt c
              match ProtocolGraph.ofProfile [ t; c ] with
              | [ { FromGuard = None } ] -> ()
              | other -> failwithf "expected one unconditional edge, got %A" other
          }

          test "entersRegions alone (no from/guardedBy/rt) yields one unconditional fan-out edge" {
              let x, y = semantic "x", semantic "y"
              let t = unsafe "t" |> entersRegions [ TransitionTarget.EnterState x; TransitionTarget.EnterState y ]
              match ProtocolGraph.ofProfile [ t; x; y ] with
              | [ { FromGuard = None; ToTargets = [ TransitionTarget.EnterState _; TransitionTarget.EnterState _ ] } ] -> ()
              | other -> failwithf "expected one unconditional 2-target edge, got %A" other
          }

          test "guardedBy wins over from when both are present" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let t = unsafe "t" |> from [ a ] |> guardedBy (StateGuard.State b) |> rt c
              match ProtocolGraph.ofProfile [ a; b; t; c ] with
              | [ { FromGuard = Some(StateGuard.State s) } ] -> Expect.equal s.Id "b" ""
              | other -> failwithf "expected guardedBy's State b to win, got %A" other
          }

          test "entersRegions wins over rt when both are present" {
              let c, x = semantic "c", semantic "x"
              let t = unsafe "t" |> rt c |> entersRegions [ TransitionTarget.EnterState x ]
              match ProtocolGraph.ofProfile [ t; c; x ] with
              | [ { ToTargets = [ TransitionTarget.EnterState target ] } ] -> Expect.equal target.Id "x" ""
              | other -> failwithf "expected entersRegions's x to win, got %A" other
          }

          test "no rt, no entersRegions -- no edge, same as today" {
              let a = semantic "a"
              let t = unsafe "t" |> from [ a ]
              Expect.equal (ProtocolGraph.ofProfile [ a; t ]) [] ""
          }

          test "a semantic (non-transition) descriptor never yields an edge" {
              Expect.equal (ProtocolGraph.ofProfile [ semantic "x" ]) [] ""
          }

          test "an empty profile yields no edges" { Expect.equal (ProtocolGraph.ofProfile []) [] "" } ]
```

- [ ] **Step 2: Run, verify failure (old `ProtocolTransition` shape/`ofProfile` still in place).**

- [ ] **Step 3: Update `ProtocolGraph.fsi`**

```fsharp
namespace Frank.Alps

/// A derived protocol edge. `FromGuard = None` means the transition is unconditional -- it fires
/// regardless of prior state. `ToTargets` non-empty is the only requirement for an edge to exist.
type ProtocolTransition =
    { FromGuard: StateGuard option
      Transition: Descriptor
      ToTargets: TransitionTarget list }

module ProtocolGraph =
    /// Derives the read-only edge set from an authored profile. `FromGuard` comes from `Guard` if set,
    /// else from `From` (empty -> None, one -> State, many -> Any -- collapses today's per-alternative
    /// expansion into one edge). `ToTargets` comes from `Targets` if non-empty, else from `Rt` (Some ->
    /// one EnterState, None -> empty). An edge is emitted iff the resulting `ToTargets` is non-empty.
    val ofProfile: Descriptor list -> ProtocolTransition list
```

- [ ] **Step 4: Update `ProtocolGraph.fs`**

```fsharp
namespace Frank.Alps

type ProtocolTransition =
    { FromGuard: StateGuard option
      Transition: Descriptor
      ToTargets: TransitionTarget list }

module ProtocolGraph =
    let private deriveGuard (d: Descriptor) : StateGuard option =
        match d.Guard with
        | Some g -> Some g
        | None ->
            match d.From with
            | [] -> None
            | [ x ] -> Some(StateGuard.State x)
            | xs -> Some(StateGuard.Any(xs |> List.map StateGuard.State))

    let private deriveTargets (d: Descriptor) : TransitionTarget list =
        match d.Targets with
        | [] ->
            match d.Rt with
            | Some t -> [ TransitionTarget.EnterState t ]
            | None -> []
        | ts -> ts

    let ofProfile (profile: Descriptor list) : ProtocolTransition list =
        DescriptorTree.flattenAll profile
        |> List.choose (fun d ->
            match deriveTargets d with
            | [] -> None
            | targets ->
                Some
                    { FromGuard = deriveGuard d
                      Transition = d
                      ToTargets = targets })
```

- [ ] **Step 5: Run tests, verify pass. Commit.**

```bash
git add src/Frank.Alps/ProtocolGraph.fsi src/Frank.Alps/ProtocolGraph.fs test/Frank.Alps.Tests/ProtocolGraphTests.fs
git commit -m "feat(alps): replace ProtocolTransition with StateGuard/TransitionTarget shape"
```

Update `src/Frank.Alps/README.md`'s `ProtocolGraph.ofProfile` paragraph (line 117 as of this plan) to describe the new shape and both behavior changes, referencing the new design doc.

---

### Task 4: Guard-side enforcement in `Excerpt.fs`

**Files:**
- Modify: `src/Frank.Alps/Excerpt.fsi`, `src/Frank.Alps/Excerpt.fs`
- Modify or create: the test file covering `Excerpt.satisfiesState`/state filtering (find via `grep -rln satisfiesState test/Frank.Alps.Tests`)

**Interfaces:**
- Consumes: `StateGuard`, `ProtocolGraph.ofProfile`'s derivation logic conceptually (not a direct dependency — `Excerpt.fs` derives its own guard from `d.Guard`/`d.From` inline, same rule as `ProtocolGraph.deriveGuard`, since it filters `Descriptor`s directly, not `ProtocolTransition`s).
- Produces: `Excerpt.satisfiesGuard: Uri list -> StateGuard -> bool`, replacing the inline flat existential filter in `Alps.excerpt`.

- [ ] **Step 1: Write the failing tests** (new cases alongside existing `satisfiesState` coverage):

```fsharp
test "satisfiesGuard: State is existential match against a single leaf" {
    let target = System.Uri "https://example.org/states/a"
    let a = semantic "a" |> def "https://example.org/states/a"
    Expect.isTrue (Excerpt.satisfiesGuard [ target ] (StateGuard.State a)) ""
    Expect.isFalse (Excerpt.satisfiesGuard [] (StateGuard.State a)) ""
}

test "satisfiesGuard: All requires every element satisfied" {
    let ua, ub = System.Uri "https://example.org/a", System.Uri "https://example.org/b"
    let a = semantic "a" |> def "https://example.org/a"
    let b = semantic "b" |> def "https://example.org/b"
    let guard = StateGuard.All [ StateGuard.State a; StateGuard.State b ]
    Expect.isTrue (Excerpt.satisfiesGuard [ ua; ub ] guard) ""
    Expect.isFalse (Excerpt.satisfiesGuard [ ua ] guard) ""
}

test "satisfiesGuard: Any requires at least one element satisfied" {
    let ua = System.Uri "https://example.org/a"
    let a = semantic "a" |> def "https://example.org/a"
    let b = semantic "b" |> def "https://example.org/b"
    let guard = StateGuard.Any [ StateGuard.State a; StateGuard.State b ]
    Expect.isTrue (Excerpt.satisfiesGuard [ ua ] guard) ""
}

test "satisfiesGuard: Not negates" {
    let ua = System.Uri "https://example.org/a"
    let a = semantic "a" |> def "https://example.org/a"
    Expect.isFalse (Excerpt.satisfiesGuard [ ua ] (StateGuard.Not(StateGuard.State a))) ""
    Expect.isTrue (Excerpt.satisfiesGuard [] (StateGuard.Not(StateGuard.State a))) ""
}

test "satisfiesGuard: nested All/Any" {
    let ua, ub, uc = System.Uri "https://example.org/a", System.Uri "https://example.org/b", System.Uri "https://example.org/c"
    let a = semantic "a" |> def "https://example.org/a"
    let b = semantic "b" |> def "https://example.org/b"
    let c = semantic "c" |> def "https://example.org/c"
    let guard = StateGuard.All [ StateGuard.State a; StateGuard.Any [ StateGuard.State b; StateGuard.State c ] ]
    Expect.isTrue (Excerpt.satisfiesGuard [ ua; uc ] guard) ""
    Expect.isFalse (Excerpt.satisfiesGuard [ ua ] guard) ""
}
```

- [ ] **Step 2: Run, verify failure.**

- [ ] **Step 3: Append to `Excerpt.fsi`**

```fsharp
/// Evaluates a StateGuard against a resolver's active-state Uri list. `State`/`Predicate` use the
/// existing contains-ancestry match (`satisfiesState`); `All`/`Any`/`Not` fold structurally.
val satisfiesGuard: activeStates: Uri list -> guard: StateGuard -> bool
```

- [ ] **Step 4: Append to `Excerpt.fs`, module `Excerpt`**

```fsharp
let rec satisfiesGuard (activeStates: Uri list) (guard: StateGuard) : bool =
    match guard with
    | StateGuard.State d
    | StateGuard.Predicate d -> activeStates |> List.exists (fun s -> satisfiesState s d)
    | StateGuard.Not g -> not (satisfiesGuard activeStates g)
    | StateGuard.All gs -> gs |> List.forall (satisfiesGuard activeStates)
    | StateGuard.Any gs -> gs |> List.exists (satisfiesGuard activeStates)

/// Same derivation `ProtocolGraph.deriveGuard` uses -- kept independent (no cross-module dependency
/// for a five-line rule) rather than shared, since ofProfile derives from a Descriptor list and this
/// filters Descriptors directly from a different entry point (descriptorsForRoute).
let deriveGuard (d: Descriptor) : StateGuard option =
    match d.Guard with
    | Some g -> Some g
    | None ->
        match d.From with
        | [] -> None
        | [ x ] -> Some(StateGuard.State x)
        | xs -> Some(StateGuard.Any(xs |> List.map StateGuard.State))
```

- [ ] **Step 5: Replace the inline filter in `Alps.excerpt`** (module `Alps`, function `excerpt`):

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
                match Excerpt.deriveGuard d with
                | None -> true
                | Some guard -> Excerpt.satisfiesGuard activeStates guard)
```

- [ ] **Step 6: Run tests, verify pass — including the existing `satisfiesState`/filtering tests, which must still pass unchanged (this is a superset, not a behavior change for plain single-`from` transitions).**

- [ ] **Step 7: Commit**

```bash
git add src/Frank.Alps/Excerpt.fsi src/Frank.Alps/Excerpt.fs test/Frank.Alps.Tests
git commit -m "feat(alps): guard-side enforcement -- satisfiesGuard fold replaces flat existential filter"
```

---

### Task 5: Traffic-light + pedestrian crossing sample

**Files:**
- Create: `sample/Frank.Alps.TrafficLightSample/Frank.Alps.TrafficLightSample.fsproj`
- Create: `sample/Frank.Alps.TrafficLightSample/Program.fs`
- Modify: `Frank.sln`

**Interfaces:**
- Consumes: `guardedBy`, `entersRegions`, `regions`, `initial`, `ProtocolGraph.ofProfile`, `Excerpt.satisfiesGuard` (Tasks 1–4).
- Produces: a runnable sample proving the whole mechanism, not just unit tests — per this repo's package-deliverables convention (`hooks/check-new-package-deliverables.sh` flags packages missing a sample; this is a sample for existing `Frank.Alps`, added because the feature needs proof, not because the hook requires it here).

- [ ] **Step 1: Author the profile** (mirrors the design doc's *Sketch: traffic light* verbatim):

```fsharp
module Frank.Alps.TrafficLightSample.Program

open Frank.Alps

let vehicleGreen = semantic "vehicleGreen" |> initial
let vehicleRed = semantic "vehicleRed"
let vehicleSignal = semantic "vehicleSignal" |> contains [ vehicleGreen; vehicleRed ]

let pedWaiting = semantic "pedWaiting" |> initial
let pedWalk = semantic "pedWalk"
let pedestrianSignal = semantic "pedestrianSignal" |> contains [ pedWaiting; pedWalk ]

let intersection = semantic "intersection" |> regions [ vehicleSignal; pedestrianSignal ]

let walk =
    unsafe "walk"
    |> guardedBy (StateGuard.All [ StateGuard.State vehicleRed; StateGuard.State pedWaiting ])
    |> rt pedWalk

let vehicleFlashing = semantic "vehicleFlashing"
let pedFlashing = semantic "pedFlashing"

let emergencyOverride =
    unsafe "emergencyOverride"
    |> entersRegions [ TransitionTarget.EnterState vehicleFlashing; TransitionTarget.EnterState pedFlashing ]

let emergencyClear =
    unsafe "emergencyClear"
    |> entersRegions [ TransitionTarget.History vehicleSignal; TransitionTarget.History pedestrianSignal ]

let profile =
    [ intersection; walk; emergencyOverride; emergencyClear ]
```

- [ ] **Step 2: Exercise `ProtocolGraph.ofProfile` and assert on the derived edges**, printing a human-readable summary (mirrors how the ping/pong sample proves `Excerpt` filtering end to end, not just documents it):

```fsharp
let edges = ProtocolGraph.ofProfile profile

let describe (e: ProtocolTransition) =
    let guard =
        match e.FromGuard with
        | None -> "unconditional"
        | Some g -> sprintf "%A" g
    let targets = e.ToTargets |> List.map (sprintf "%A") |> String.concat ", "
    sprintf "%s -- guard: %s -- targets: %s" e.Transition.Id guard targets

edges |> List.iter (describe >> printfn "%s")

assert (edges |> List.exists (fun e -> e.Transition.Id = "walk" && e.FromGuard.IsSome))
assert (edges |> List.exists (fun e -> e.Transition.Id = "emergencyOverride" && e.FromGuard.IsNone && e.ToTargets.Length = 2))
assert (edges |> List.exists (fun e -> e.Transition.Id = "emergencyClear" && (e.ToTargets |> List.forall (function TransitionTarget.History _ -> true | _ -> false))))
```

- [ ] **Step 3: Exercise `Excerpt.satisfiesGuard` directly against a canned/pre-determined active-state sequence** (no real timer — a fixed `Uri list` per "step" is enough to prove enforcement):

```fsharp
let step1ActiveStates = [ vehicleRed.Def.Value; pedWaiting.Def.Value ]  // requires `def` on each leaf state -- add during authoring
let walkGuard = walk.Guard |> Option.get
printfn "walk enabled at step1: %b" (Excerpt.satisfiesGuard step1ActiveStates walkGuard)
```

(Adjust: `vehicleRed`/`pedWaiting` need `def` set for this to have a `Uri` to compare — add `|> def "..."` to each leaf state during authoring, matching the traffic-light sketch's spirit even though the design doc's sketch didn't need `def` for the single-document case; here it's needed only because `satisfiesGuard`'s test harness compares by `Uri`, not because cross-role sharing is involved.)

- [ ] **Step 4: `Frank.Alps.TrafficLightSample.fsproj`** — mirror `sample/Frank.Alps.Sample/Frank.Alps.Sample.fsproj`'s shape (`OutputType Exe`, `TargetFramework net10.0`, `ProjectReference` to `Frank.Alps`).

- [ ] **Step 5: Register in `Frank.sln`**

```bash
dotnet sln Frank.sln add sample/Frank.Alps.TrafficLightSample/Frank.Alps.TrafficLightSample.fsproj
```

- [ ] **Step 6: Run it, confirm the printed edges and assertions match expectations**

```bash
dotnet run --project sample/Frank.Alps.TrafficLightSample/Frank.Alps.TrafficLightSample.fsproj
```

- [ ] **Step 7: Commit**

```bash
git add Frank.sln sample/Frank.Alps.TrafficLightSample
git commit -m "sample(frank-alps): traffic light + pedestrian crossing proves compound transitions"
```

---

### Task 6: Full build + test verification

- [ ] **Step 1: Build every targeted TFM**

```bash
dotnet build Frank.sln -c Debug
```

- [ ] **Step 2: Run the full `Frank.Alps.Tests` suite**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

- [ ] **Step 3: Run the sample**

```bash
dotnet run --project sample/Frank.Alps.TrafficLightSample/Frank.Alps.TrafficLightSample.fsproj
```

- [ ] **Step 4: Update `RELEASE_NOTES.md`** with the `ProtocolTransition` shape change and the two behavior changes (multi-`from` collapse, guard-optional gating) — breaking, pre-NuGet, no deprecation path needed.

---

## After this plan

- Build the order-fulfillment sample (cross-role, `def`-shared guard leaves) — separate follow-on.
- Fan-out (write) enforcement — actor-model work, tracked separately.
