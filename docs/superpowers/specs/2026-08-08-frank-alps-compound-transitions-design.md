# Frank.Alps — compound protocol transitions (AND-guards, fan-out, history)

**Date**: 2026-08-08
**Branch**: `worktree-compound-protocol`
**Status**: Draft — awaiting review (Wire format decided, see below)

## Context

Closes the scope boundary [2026-08-02-frank-alps-protocol-design.md](2026-08-02-frank-alps-protocol-design.md) recorded and deliberately did not design: *Reviewed against Harel's formalism* found the deferred `CurrentStateResolver`-returning-a-set plan (frank-fs/frank#490, shipped) reaches independent-region OR filtering but not genuine Harel compound transitions — a conjunctive AND-guard across regions, or a transition fanning out to enter several regions at once (frank-fs/frank#489, this doc).

Driven by a concrete target, not doc-completeness: order-fulfillment, where payment/inventory/shipping/billing are genuinely separate roles/services, each with its own ALPS document. Design goal restated mid-session: full Harel statechart support, not just enough surface for one demo — so the type built here has to be the real shape, not a placeholder replaced later. Proven first with a light, single-document sample (traffic light + pedestrian crossing) before the cross-role order-fulfillment build, per this repo's outside-in-before-codegen posture.

`ProtocolTransition`/`ProtocolGraph.ofProfile` are not yet published to NuGet and have exactly one consumer in the repo outside this doc and `sample/` — `test/Frank.Alps.Tests/ProtocolGraphTests.fs` (62 lines). No production or sample code touches them. That removes the back-compat constraint the parent design's *layered alongside, not a change to it* posture was written under — see *Replacing `ProtocolTransition`* below for why this design reverses that call.

## Goals

1. Author conjunctive (AND) and disjunctive (OR) guards across regions, arbitrarily nested — not just a flat list, so `(A and B) or C` is expressible on one edge.
2. Author transitions that fan out to enter multiple regions at once, including history/deep-history pseudostate targets (H/H\*).
3. Guard-side enforcement ships now, not deferred: `Excerpt.fs`'s state-based filtering evaluates the full guard tree against `CurrentStateResolver`'s existing `Uri list`, not just a flat existential match. Cheap — a pure predicate change, no new state-store or write concern.
4. Cross-role composition (order-fulfillment's actual shape) without new reference machinery: a guard leaf naming a state owned by another role's document connects via a *shared `def` URI* — the identity `Excerpt.satisfiesState` already matches on — not via a cross-document descriptor reference.
5. Replace `ProtocolTransition`'s shape in place, keeping the name, rather than adding a disconnected parallel type — see *Replacing `ProtocolTransition`*.
6. Prove the design with a light, single-document sample (traffic light + pedestrian crossing, canned resolver, no real timer) before the cross-role order-fulfillment sample.

## Non-goals

- **Fan-out (write) enforcement.** Entering N regions atomically on one transition fire is a state-mutation/orchestration problem — `CurrentStateResolver` is caller-supplied and read-only, and Frank.Alps owns no state store or commit path. This is exactly where the actor-model work already anticipated ([[project_actor_model_trajectory]]: MailboxProcessor proof-of-concept now, Akka.NET/Orleans/Proto.Actor later) buys atomic, serialized region updates. `ToTargets`/`entersRegions` are fully authorable and derivable via `ofProfile` now; nothing executes them.
- **Timed transitions and transition actions/side-effects.** Different axis — temporal/behavioral, not structural composition. Nothing in Frank.Alps executes anything; encoding a timer or action here would author semantics this codebase has no engine to run.
- **Cross-role fan-out targets.** `TransitionTarget` stays same-document only. A transition can only enter *its own* document's orthogonal regions (Harel: the regions a transition enters belong to the same statechart as the transition itself). Reaching into another role's state on fan-out would be an event/notification to that role, not a state entry — a different mechanism, not designed here.
- **Full multi-document profile hosting, discovery, or rendering.** Unchanged from the parent design's existing non-goal. Only guard-leaf `def`-identity matching is enabled by this doc (already-existing mechanism, already-existing field) — not general multi-document profiles.
- **`DescriptorRef`-based external guard references.** Considered and rejected during design: `href`/`hrefExternal`/`InheritsFrom` are a pure wire-serialization/attribute-inheritance concern (confirmed against `Serialization.fs`) that `Excerpt.satisfiesState` never reads — only `Descriptor.Def` drives matching. A new reference type would have solved nothing that shared `def` doesn't already solve.

## The design

### `Descriptor` — two new fields, mutually recursive with the new guard/target types

```fsharp
type Descriptor =
    { // ...all existing fields, unchanged...
      From: Descriptor list             // unchanged — still the plain multi-alternative source, still drives
                                         // protocolState/availableInStates ext at serialization time
      Guard: StateGuard option          // NEW — set by `guardedBy`; None means "derive from From" (see ofProfile)
      Rt: Descriptor option             // unchanged — still the single spec-required `rt` wire property (draft-07 §2.2.13)
      Targets: TransitionTarget list    // NEW — set by `entersRegions`; empty means "derive from Rt" (see ofProfile)
      Descriptors: Descriptor list }

and StateGuard =
    | State of Descriptor        // is this state/region currently active (contains-ancestry match, unchanged)
    | Not of StateGuard          // negated guard
    | All of StateGuard list     // AND — every element must be satisfied
    | Any of StateGuard list     // OR — any element satisfies (today's #490 existential match, now one case, not the whole story)
    | Predicate of Descriptor    // opaque named condition beyond "state active" — Frank.Alps doesn't evaluate it,
                                  // just carries and Def-matches it the same as State

and TransitionTarget =
    | EnterState of Descriptor
    | History of Descriptor       // H — shallow: re-enter region's last active direct substate
    | DeepHistory of Descriptor   // H* — deep: re-enter last active state at any depth
```

Same pattern already established for `DescriptorRef` co-recursing with `Descriptor` — no new idiom introduced.

`State`/`Predicate` hold a plain, local, compile-checked `Descriptor` — not `DescriptorRef`. Cross-role connection (order-fulfillment's payment/inventory regions, owned by other documents) happens by two independently-authored local descriptors declaring the *same* `def` URI, matched by the existing `Excerpt.satisfiesState`/`CurrentStateResolver` mechanism — no cross-document reference needed at all. See *Sketch: order-fulfillment* below.

### New authoring combinators

```fsharp
val guardedBy: StateGuard -> Descriptor -> Descriptor       // sets Guard explicitly
val entersRegions: TransitionTarget list -> Descriptor -> Descriptor   // sets Targets explicitly
```

Additive alongside unchanged `from`/`rt` — the common single-source/single-target edge keeps using `from [x] |> rt y` exactly as today; `guardedBy`/`entersRegions` are only needed for the compound cases.

### Replacing `ProtocolTransition`

```fsharp
type ProtocolTransition =
    { FromGuard: StateGuard option        // None = unconditional (fires regardless of prior state)
      Transition: Descriptor
      ToTargets: TransitionTarget list }  // non-empty required to emit an edge — the one true requirement

module ProtocolGraph =
    val ofProfile: Descriptor list -> ProtocolTransition list
```

Same name as the parent design's type, new shape — no separate `CompoundProtocolTransition`. Justified by verified near-zero blast radius (one 62-line test file; `sample/` and the runtime request-serving pipeline never call `ofProfile`, confirmed by grep) and by the type-level fact that a plain edge is exactly the degenerate case (`FromGuard = Some (State f)`, `ToTargets = [ EnterState t ]`) — forking a parallel type would mean every future consumer (this graph's only real job) has to know about two shapes representing the same thing.

**Derivation rule** (`ofProfile`, per transition descriptor `d`):

```
FromGuard =
    match d.Guard with
    | Some g -> Some g
    | None ->
        match d.From with
        | []  -> None
        | [x] -> Some (State x)
        | xs  -> Some (Any (xs |> List.map State))   // collapses today's N-alternative-edges into one Any-guarded edge

ToTargets =
    match d.Targets with
    | [] -> (match d.Rt with Some t -> [ EnterState t ] | None -> [])
    | ts -> ts

emit an edge for d iff ToTargets is non-empty
```

**Two behavior changes from the parent design, both deliberate, confirmed during this session:**

1. **Multi-`from` collapse.** Today, `t |> from [A; B] |> rt C` yields two separate `ProtocolTransition` edges (one per alternative). Under this design it yields one edge, `FromGuard = Some (Any [State A; State B])` — matches what "reachable from A or B" actually means (one transition, not two), and is the only representation consistent with `guardedBy`/`Any` existing at all.
2. **Guard becomes optional, gating relaxes.** Today, an edge requires *both* `From` non-empty and `Rt` present — a structural consequence of `ProtocolTransition`'s old record requiring both fields. Under this design, only `ToTargets` non-empty is required; `FromGuard` is independently optional. Net effect: a transition with `rt` but no `from`/`guardedBy` now yields an *unconditional* edge, where today it's silently excluded from `ofProfile`'s output entirely. Necessary for `emergencyOverride`-style guard-less fan-out to exist in the graph at all (see *Sketch: traffic light*).

### Guard-side enforcement — ships now (`Excerpt.fs`)

Today's flat existential match:

```fsharp
authAllowed
|> List.filter (fun d ->
    List.isEmpty d.From
    || d.From |> List.exists (fun candidate -> activeStates |> List.exists (fun s -> Excerpt.satisfiesState s candidate)))
```

Replaced by a fold over the derived `StateGuard` (via `d.Guard`, falling back to the same `From`-derivation as `ofProfile` above, for descriptors not yet migrated to `guardedBy`):

```fsharp
let rec satisfiesGuard (activeStates: Uri list) (guard: StateGuard) : bool =
    match guard with
    | State d | Predicate d -> activeStates |> List.exists (fun s -> Excerpt.satisfiesState s d)
    | Not g -> not (satisfiesGuard activeStates g)
    | All gs -> gs |> List.forall (satisfiesGuard activeStates)
    | Any gs -> gs |> List.exists (satisfiesGuard activeStates)
```

`None` (unconditional) is always satisfied — never filtered, same graceful-degradation instinct as an absent resolver today.

### Wire format — **decided: option 4**

Verified against the actual spec text (draft-07, fetched during this design): `rt` (§2.2.13) is singular — "MUST point to the id of an existing descriptor," one reference, not an array. `ext` (§2.2.6) explicitly MAY be an array. `rt` therefore keeps pointing at one target (the first/primary `ToTargets` element when present); it cannot carry fan-out on its own.

Four options were surfaced for representing the rest of `StateGuard`/`ToTargets` on the wire, not narrowed to one:

1. **Ride `ext`** — full guard/target tree serialized into new markers under the existing `https://frank-fs.github.io/alps-ext/` namespace, same tolerant-reader posture as `protocolState`/`orthogonal`. Con: a naive ALPS reader sees one plain edge and has no idea more is happening.
2. **Flat correlation tag** — N ordinary, fully-plain transitions sharing an `ext "compoundGroup" "x"` marker. Con: can't express nesting (`Not`/`Any` inside `All`), loses atomicity signal beyond convention.
3. **Reuse `contains`** — wrap member transitions under one parent, mark AND/OR via the existing `regions`/`StateComposition` mechanism. Con: overloads `contains` with a third meaning (representation grouping, composite-state hierarchy, now compound-transition membership).
4. **Don't serialize at all** — `StateGuard`/`ToTargets` stay purely in-process/derived, matching the package's already-stated posture ("nothing in this package executes a transition... stays external"). Wire format stays exactly `from`/`rt` per plain edge, unchanged. Con: an external tool reading the raw JSON can never see the compound structure — only an in-process consumer (this assembly) can.

**Confirmed: option 4.** `StateGuard`/`ToTargets` stay purely in-process/derived — no wire serialization. Rationale (user, 2026-08-08): ALPS is already projected to the current resource state by role — the served excerpt is a per-request, per-role view, not a write-side model. Serializing guard/fan-out logic onto the wire would leak that write-side model into a document meant to say "what's true from here, now." Options 1-3 above stay recorded for context; not pursued.

### Sketch: traffic light + pedestrian crossing (single-document — the light proof sample)

```fsharp
let vehicleGreen = semantic "vehicleGreen" |> initial
let vehicleRed   = semantic "vehicleRed"
let vehicleSignal = semantic "vehicleSignal" |> contains [ vehicleGreen; vehicleRed ]

let pedWaiting = semantic "pedWaiting" |> initial
let pedWalk    = semantic "pedWalk"
let pedestrianSignal = semantic "pedestrianSignal" |> contains [ pedWaiting; pedWalk ]

let intersection = semantic "intersection" |> regions [ vehicleSignal; pedestrianSignal ]

// AND-guard: walk only when vehicle is Red AND pedestrian is Waiting
let walk =
    unsafe "walk"
    |> guardedBy (All [ State vehicleRed; State pedWaiting ])
    |> rt pedWalk

// fan-out, unconditional: one event enters both regions' flashing state at once
let vehicleFlashing = semantic "vehicleFlashing"
let pedFlashing     = semantic "pedFlashing"
let emergencyOverride =
    unsafe "emergencyOverride"
    |> entersRegions [ EnterState vehicleFlashing; EnterState pedFlashing ]

// history: resume whatever each region was doing before the override
let emergencyClear =
    unsafe "emergencyClear"
    |> entersRegions [ History vehicleSignal; History pedestrianSignal ]
```

No `def`-sharing needed — everything local to one document, same as any `from`/`rt` reference today. The sample calls `ProtocolGraph.ofProfile` explicitly and asserts the derived edges — the first place in the codebase exercising this type via anything other than a unit test, mirroring how the ping/pong sample proves `Excerpt` filtering end-to-end rather than just documenting it. A canned/fixed `CurrentStateResolver` (pre-determined sequence, no real timer) drives it.

### Sketch: order-fulfillment (cross-role — the real target, built after)

```fsharp
// payment.alps.json (Payment role, owns this state)
let paymentAuthorized =
    semantic "paymentAuthorized" |> def "https://example.org/order-states/paymentAuthorized"
let authorize = unsafe "authorize" |> from [ paymentPending ] |> rt paymentAuthorized

// inventory.alps.json (Inventory role, owns this state)
let inventoryReserved =
    semantic "inventoryReserved" |> def "https://example.org/order-states/inventoryReserved"
let reserve = unsafe "reserve" |> from [ inventoryAvailable ] |> rt inventoryReserved

// fulfillment.alps.json (Fulfillment/orchestrator role — owns `fulfill`)
// Local placeholders, connected to the other roles purely by declaring the *same* def URI —
// not by referencing their descriptors at all.
let paymentAuthorizedRef = semantic "paymentAuthorized" |> def "https://example.org/order-states/paymentAuthorized"
let inventoryReservedRef = semantic "inventoryReserved" |> def "https://example.org/order-states/inventoryReserved"
let shipping = semantic "shipping" |> doc "Shipping region"
let billing  = semantic "billing"  |> doc "Billing region"

let fulfill =
    unsafe "fulfill"
    |> guardedBy    (All [ State paymentAuthorizedRef; State inventoryReservedRef ])
    |> entersRegions [ EnterState shipping; EnterState billing ]
```

The fulfillment resource's `CurrentStateResolver` is whatever the orchestrator wires up (a `Frank.Provenance` query, a service call — unspecified here, matches the parent design's existing seam posture) — it returns the `Uri list` of `def`-identified states currently true across roles. Matching is the *existing* walk, unchanged. Confirms `StateGuard`/`TransitionTarget` need no rewrite between the single-document sample and this cross-role target — the type built for traffic light is the type order-fulfillment actually needs.

## Error handling and edge cases

| Situation | Behaviour |
|---|---|
| Transition has `rt` but no `from`/`guardedBy` | Yields an unconditional edge (`FromGuard = None`) — behavior change from the parent design, see *Replacing `ProtocolTransition`*. |
| Transition has `entersRegions` but no `from`/`guardedBy`/`rt` | Unconditional fan-out edge. |
| Transition has both `from` and `guardedBy` | `guardedBy` wins for `ofProfile`/enforcement; `From` still serializes to `protocolState`/`availableInStates` ext as before (representational, unchanged). |
| Transition has both `rt` and `entersRegions` | `entersRegions` wins for `ofProfile`; `rt` still serializes as the wire-required single property. Which `ToTargets` element it mirrors when there's more than one is a separate, narrower open question — unrelated to the *Wire format* decision above, which only governs `StateGuard`/`ToTargets` themselves. |
| `History`/`DeepHistory` target whose region has no `initial`-marked child | Frank.Alps carries the marker; resolving "last active" is the consuming runtime's problem, not authored or validated here. |
| Two unrelated states accidentally share a `def` URI | Not validated — same "nothing to check" posture `hrefExternal` already has; author's responsibility. |
| `CurrentStateResolver` absent or returns `[]` | Unchanged: no state filtering, only authorization applies. |

## Testing

- `StateGuard`: `All`/`Any`/`Not`/`Predicate` fold semantics (`satisfiesGuard`), arbitrary nesting, against various active-state lists.
- `TransitionTarget`: `EnterState`/`History`/`DeepHistory` carried through `ofProfile` unchanged.
- `ofProfile`: new gating rule (targets-required, guard-optional); `Guard`-over-`From` and `Targets`-over-`Rt` precedence; collapsed single-`Any`-edge behavior for multi-`from` (replaces the existing N-edges-per-alternative test in `ProtocolGraphTests.fs`).
- `Excerpt.fs`: `satisfiesGuard` in place of the flat existential match; `None` guard never filtered; nested `All`/`Any`/`Not` against multi-region active-state lists.
- Traffic-light sample: `ofProfile` output asserted directly against the authored profile (guard edge, unconditional fan-out edge, history targets present).
- Order-fulfillment sample (separate, later): shared-`def` resolution across independently-authored descriptors in different profiles.

## Future work (separate)

- **Fan-out (write) enforcement** — actor-model work, [[project_actor_model_trajectory]].
- **Wire-format resolution** — the four options above, unresolved this session; needs a decision before or during implementation.
- **Cross-role fan-out targets** — if ever needed, a different mechanism (event/notification) than guard-leaf `def`-matching; not designed here.
- **Timed transitions, transition actions/side-effects** — explicitly out of scope, additive-if-ever-pursued, per the parent design's own posture.
- **Order-fulfillment sample** — built after traffic light proves the mechanism; not part of this issue's immediate scope.

## Sources

- Parent design: [2026-08-02-frank-alps-protocol-design.md](2026-08-02-frank-alps-protocol-design.md), *Reviewed against Harel's formalism* and *Future work* — origin of frank-fs/frank#489/#490.
- ALPS draft-07 §2.2.13 (`rt`, singular reference) and §2.2.6 (`ext` MAY be an array) — fetched and confirmed during this session (2026-08-08), not assumed from memory: https://datatracker.ietf.org/doc/html/draft-amundsen-richardson-foster-alps-07
- frank-fs/frank#489 (this doc), frank-fs/frank#490 (shipped prerequisite).
- `src/Frank.Alps/Excerpt.fs`, `ProtocolGraph.fs`, `Serialization.fs`, `Descriptor.fs`, `DescriptorTypes.fs` — verified current implementation, not assumed from the parent doc.
