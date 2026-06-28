# v7.3.2 Capstone (#333) — discovery completion

Date: 2026-06-27
Status: **Implementation-gap closure, not new design.** The design is already settled in
`docs/superpowers/specs/2026-04-20-v732-semantic-discovery-design.md` §6 (Capstone test + Negative tests)
and issue #333. This document records the measured baseline, the implementation decisions needed to
close the gap to that design, and the falsifiable ATs — it does NOT re-litigate the design.

Issue: #333 (`[B21]` Capstone — TicTacToe naive-client navigation via discovery only).

## Thesis (from the existing design)

A deterministic client navigates the TicTacToe API **entirely via discovery** — JSON Home → ALPS →
Link headers → content negotiation — and plays a full game, **including discovering the move request
shape**, and is SHACL-validated on illegal moves. This proves the discovery surface is complete enough
for an agent end-to-end. Falsifiable: a vocab swap (`schema`→`ex`) breaks a client that hardcoded
schema.org IRIs, while the discovery client survives. (The *agentic* version is pursued separately in
`~/Code/tic-tac-toe/experiments` and tracked by #376 — out of scope here.)

## Measured baseline (ran `sample/TicTacToe-v732.E2E/SemanticTests.fs`, 2026-06-27)

| AT | What it checks | Status |
|----|----------------|--------|
| AT-S1 | JSON Home lists resources with vocabulary-mapped rels | ✅ green |
| AT-S2 | OPTIONS → `Allow` + `Link rel=describedby` → ALPS | ✅ green |
| AT-S3 | ALPS descriptors reference schema.org IRIs | ✅ green |
| AT-S4 | Invalid move → 422 SHACL ValidationReport citing schema.org IRIs | ❌ **red** |
| AT-S5 | Game negotiates JSON-LD with external schema.org `@context` | ✅ green |
| AT-S6 | Naive client plays a full game via discovery only | ❌ **red** |

The discovery substrate (S1/S2/S3/S5) already works. The gap is S4 + S6.

## Root cause (the implementation gap)

1. **`useValidation` is not wired into the sample's `webHost`.** `Frank.Validation` IS referenced and
   `GeneratedValidation.fs` IS generated (the SHACL shape exists), but the host calls only
   `useProvenance`/`useDiscovery`/`useLinkedData`. So no validation middleware runs → AT-S4's 422 never
   fires, and AT-S6's "illegal move rejected by SHACL" leg fails. **One-line wiring gap.**

2. **The move's request shape is never modeled.** The wire request `{position, player}` is parsed inside
   `moveHandler` (`doc["position"]`, `doc["player"]`) — it is not a mapped semantic type. Consequently
   nothing in the framework can describe it: the generated ALPS descriptors cover the **Game entity**
   (`identifier`, `result`) + `MoveLog` + the `MoveAction`/`Game` classes, but there are **no `position`
   or `agent` input descriptors**, and there is no SHACL shape for the request. AT-S6 dies at
   `failwith "ALPS missing position IRI"` (SemanticTests.fs:208) because a discovery client cannot learn
   how to construct the POST. **This is the load-bearing gap.**

This is not a composition problem (Discovery + Validation compose fine). It is a missing model: the
move *action's input contract* is not declared anywhere the generators can read.

## Implementation decisions

### D1 — Wire `useValidation`
Add `useValidation` to the sample `webHost { }` block. Order per the established composition rules
(Provenance outermost is no longer required post-#7; Validation validates request bodies). AT-S4 + AT-S6
SHACL leg depend on this.

### D2 — Model the move request as a mapped type (the crux)
Introduce a request type the move operation accepts — e.g.:
```fsharp
type MoveRequest = { Position: SquarePosition; Player: Player }   // or { Position; Agent }
```
mapped in the vocabulary/lock so its fields resolve to schema.org property IRIs (`position`→a schema
property, `player`/`agent`→`schema:agent`, already present in the lock for `GameStore.agent`). Declare it
on the moves resource via the **`accepts typeof<MoveRequest>`** operation (the request-side counterpart
to `produces`, already in `Frank.OpenApi`'s handler CE). The generators then have a typed request to
describe.

### D3 — Make the request shape discoverable through BOTH channels (per maintainer: "depends on what's available")
- **Discovery / ALPS:** the ALPS profile should emit `descriptor` entries for the accepted request's
  input fields (`position`, `agent`) with their vocabulary IRIs, so a client reads the profile and learns
  the request shape.
- **Validation / SHACL:** the generated SHACL shape for `MoveRequest` describes the same required
  properties; a client may read the shape (or learn from a 422 report) as a second source.

> **INVESTIGATION RESOLVED (2026-06-27):** `DiscoveryEmitter.projectDiscovery` builds ALPS descriptors
> purely from `ResolvedModel.Resources` — a type-level descriptor (ClassIri) plus **field descriptors**
> from each resolved type's `Fields`. It reads nothing from `accepts`/request metadata. **Therefore no
> Discovery feature is needed:** modeling `MoveRequest` as a *mapped resolved type* (lock entry with
> `Position`/`Player` fields → schema IRIs) makes those fields appear automatically in the existing flat
> ALPS descriptor list. AT-S6's `fieldIri "position"`/`"agent"` just scans the ALPS doc for those names,
> so a flat descriptor entry satisfies it. SHACL shapes likewise generate from the mapped type's fields.
> D3 is **modeling + wiring**, not a Frank.Discovery code change. (`accepts typeof<MoveRequest>` on the
> resource is still wanted for Validation/OpenApi request-typing, but ALPS discovery does not depend on it.)

### D4 — AT-S6 full two-player game via discovery
Make the existing AT-S6 green: discover game + moves URIs from JSON Home, read ALPS for the request
shape (D3), play a full two-player game to a terminal state detected via the affordance set (no
available transitions), with **no hardcoded URLs / state names / message constructors**.

### D5 — Vocab-swap negative (the falsifiability lever)
Implement the §6 negative test: a second vocabulary (`using "ex"` with a local `ex:` vocab) → lock
regenerates → generated artifacts emit `ex:` IRIs. A client that hardcoded `schema.org` IRIs breaks; the
same client navigating purely via discovery still completes the game. This is what makes "discovery is
load-bearing" falsifiable.

### D6 — CI + sln integration
The E2E project (`sample/TicTacToe-v732.E2E`) is in neither `Frank.sln` nor CI. Wire it in (mirroring the
v7.3.2 suite additions already made) so the capstone is gated on merge. Note: it starts the sample via
`dotnet run` and uses Playwright's API request context — confirm the CI runner has the Playwright driver
(API context needs the driver, not browsers).

### D7 — Provenance is usable via discovery (replay or verify-result)
Provenance is in scope and must do real work, not merely be advertised. After the naive client plays the
game, it follows the `Link: rel="http://www.w3.org/ns/prov#has_provenance"` header (RFC 6903) — discovered,
not hardcoded — to the lineage, and uses it for ONE of:
- **Replay:** reconstruct the move sequence from the ordered PROV-O Activities (each a `schema:MoveAction`
  with `startedAtTime`, agent, resource) and confirm replaying them reproduces the final game state; or
- **Verify result:** confirm the recorded terminal Activity's outcome IRI (e.g. win/draw via the mapped
  `MoveResult` cases — `schema:CompletedActionStatus`) matches the game's actual final state.

Either path proves the shipped Provenance vertical participates in the discovery story and the lineage is
semantically usable, not decorative. (Verify-result is the smaller of the two — recommended first.)

## Acceptance criteria

The existing AT-S1..AT-S6 ALL green (S4/S6 currently red → must turn green), PLUS:
- **AT-S7 (D5):** vocab swap `schema`→`ex` → hardcoded-IRI client fails, discovery client still completes a game.
- **AT-S8 (D7, in scope):** the client follows the discovered `has_provenance` Link to the lineage and
  uses it to **verify the game result** (recorded terminal Activity outcome IRI matches the final state) —
  or **replay** the move sequence to reproduce the final state. Proves provenance is discoverable AND
  semantically usable, not decorative.

All are real HTTP request/response checks against the live sample (the existing E2E style).

## Out of scope

- The agentic / LLM naive-client (open-ended "figure it out") — #376, pursued in `~/Code/tic-tac-toe/experiments`.
- Build-gate + hash-drift negatives (§6) — already covered by the shared lock-gate tests; not re-implemented here unless found missing during D6.

## Sources

- Existing design (authoritative): `docs/superpowers/specs/2026-04-20-v732-semantic-discovery-design.md` §6
- Issue: #333
- Existing harness: `sample/TicTacToe-v732.E2E/SemanticTests.fs` (AT-S1..S6), `ServerFixture.fs`
- Request-side op: `src/Frank.OpenApi/HandlerBuilder.fs` (`accepts`)
- Shipped verticals: Frank.Discovery, Frank.Validation, Frank.LinkedData, Frank.Provenance
