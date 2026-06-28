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

### D2 — Model the move request as a mapped type, with HONEST IRIs, produced via the CLI clarify workflow (the crux)
Introduce a request type the move operation accepts:
```fsharp
type MoveRequest = { Position: SquarePosition; Player: Player }
```
Declare it on the moves resource via the **`accepts typeof<MoveRequest>`** operation (the request-side
counterpart to `produces`, already in `Frank.OpenApi`'s handler CE). The generators then have a typed
request to describe.

**Honest mappings (semantics first, NOT string-to-test):** the earlier draft mapped `Position →
schema:position` and the class to `schema:Action` purely because the test literals spelled those — a
shortcut. Corrected, by meaning:

| Element | IRI | Why it is honest |
|---|---|---|
| `Player` field | `schema:agent` | A player performing a move *is* the agent (schema.org: "the direct performer of the action"). This is the load-bearing **cross-domain term** — a vocabulary-literate client recognizes it without knowing tic-tac-toe. Already mapped for `GameStore.agent`. |
| `MoveRequest` type (class) | `schema:MoveAction` | A move request *is* a move action — the same class the existing `Move` mapping uses. (NOT the generic `schema:Action`, which was grabbed only to match the test's `@type` literal.) |
| `Position` field | a **domain-vocabulary** term (e.g. `ttt:square` under a declared `prefix "ttt"`) | **There is no honest schema.org term for a tic-tac-toe square** — `schema:position` means *ordinal position in a series*, not a board cell. This is a real finding, not a gap to paper over: the thesis is *reuse global vocabulary where it fits (agent), domain vocabulary where it must (square)*. The accept oracle is fail-open for uncached namespaces (`Accept.fs:266-271`), so a declared-prefix CURIE for an un-fetched namespace is permitted. |

**Produced via the canonical CLI workflow (simulating clarify), not hand-edited JSON.** The lock entry is
generated by exercising the full `frank semantic` pipeline:
`extract` → `clarify --output-format resolved-template` (emits `resolved.json`) → **hand-write the
resolution** (the predetermined mapping IS the simulated human/LLM clarify decision) → `accept --input
resolved.json --source manual` → lock. Raw JSON surgery on the lock is disallowed; the curation must flow
through `clarify`/`accept` so their validation (term-existence oracle, structure) runs.

**Known implementation risk (spike must resolve):** `Move` already maps to `schema:MoveAction`. Two
class-mapped resources sharing a class IRI collide in `ResolvedModel.checkLocalNameCollisions` (keys on the
IRI-derived local name "MoveAction"). The Task-1 spike must resolve this — e.g. exclude `Move` from the
resolved model if it is not separately required as a discovery descriptor, or give the request a distinct
honest class — and report which, with evidence.

### D3 — The client SIMULATES an agent: follow links, verify they resolve, verify terms match an expected set
The deterministic client is a **stand-in for an agent**, not a test of an LLM's comprehension (the agentic
version is #376, separate). What makes its success prove *semantic understanding* rather than string-luck:

1. **Follow discovered links and verify each leads somewhere.** JSON Home → game/moves resources; OPTIONS
   → `Link rel=describedby` → ALPS (must return 200); ALPS field descriptors carry `href` IRIs. The client
   asserts each followed link resolves (the profile fetch succeeds; the descriptor hrefs are well-formed
   absolute IRIs), so the discovery graph is genuinely traversable, not decorative.
2. **Verify the encountered terms match an EXPECTED SET.** Instead of `fieldIri "position"` (a string
   match that a meaningless mapping would pass), the client collects the descriptor term IRIs and asserts
   the expected semantic set is present — e.g. `{ schema:MoveAction, schema:agent, ttt:square, schema:Game,
   schema:result }`. The client then **identifies the actor input by recognizing `schema:agent`** (the
   universal term) and the target-square input by the domain term — proving it read meaning, not spelling.
3. **Construct the move from discovered IRIs.** The ld+json POST body keys are the discovered field IRIs
   (`schema:agent`, the `ttt:square` IRI) and `@type` is the discovered class IRI (`schema:MoveAction`) —
   none hardcoded; all read from discovery. Legal squares come from the game-state representation (empty
   cells), which is instance-level self-description.
4. **Dereference EVERY URI it receives.** Linked Data discipline (TBL): every URI the client encounters —
   resource links, the ALPS profile URL, every field/class **term IRI**, the `has_provenance` lineage URL,
   `seeAlso` targets — the client attempts to dereference (GET/HEAD) and asserts it resolves to *something*
   (2xx, or 303→2xx). No URI may be a dead label. This forces a real constraint on the design: **all URIs
   must be dereferenceable** — app-served URIs resolve against the running sample; the reused `schema.org`
   term IRIs resolve against schema.org; and the **domain `ttt:` vocabulary must itself be served** (the
   sample publishes its vocabulary terms at a real, dereferenceable route — you can't reuse a global term
   for a board cell, so you must *publish* one). See open decisions D-deref below.

> **INVESTIGATION RESOLVED (2026-06-27):** `DiscoveryEmitter.projectDiscovery` builds ALPS descriptors
> purely from `ResolvedModel.Resources` — a type-level descriptor (ClassIri) plus **field descriptors**
> from each resolved type's `Fields`. It reads nothing from `accepts`/request metadata. Modeling
> `MoveRequest` as a *mapped resolved type* makes its fields appear automatically in the flat ALPS
> descriptor list; the descriptor `Id` is the IRI **local-name** (`DiscoveryEmitter.fs:33,44`) and `href`
> is the absolute IRI. So the expected-set check matches descriptor hrefs (absolute IRIs), and the local
> name (`agent`, `square`) is incidental. No Frank.Discovery code change. (`accepts typeof<MoveRequest>`
> is still wanted for Validation/OpenApi request-typing.)
>
> **Validation / SHACL second channel:** the generated SHACL shape for `MoveRequest` (targetClass
> `schema:MoveAction`, a `sh:pattern`/value constraint on the square enumerating the legal cells) both
> rejects an illegal move with a vocabulary-IRI-citing report AND is a second machine-readable source of
> the request contract.

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

### D7 — Provenance is the end-of-game COMPLETE-CAPTURE audit (every action taken was recorded)
Provenance is the closing act of the capstone and must do real work. After the client plays the full game,
it follows the discovered `Link: rel="http://www.w3.org/ns/prov#has_provenance"` header (RFC 6903 — read,
not hardcoded) to the lineage and proves the lineage **completely captured what actually happened**:

- **Completeness:** the lineage contains exactly one `prov:Activity` per move the client posted — the count
  matches the moves played, and each Activity is typed `schema:MoveAction`, `wasAssociatedWith` the agent
  that made it, and references the square it targeted. No move is missing; no extra move is invented.
- **Order:** the Activities' `startedAtTime` (or recorded sequence) reproduces the exact order the client
  played — alternating X/O — i.e., a **replay** of the lineage reconstructs the move sequence the client
  actually performed.
- **Outcome:** the recorded terminal state (the final Activity's outcome / mapped `MoveResult` case —
  `schema:CompletedActionStatus` for Won/Draw) matches the game's actual final state observed over HTTP.

This is stronger than "follow the link and see an activity": the client cross-checks the lineage against
its own action log, so a lineage that dropped, reordered, or fabricated a move FAILS. That proves the
shipped Provenance vertical captured the whole session faithfully and is semantically usable, not
decorative.

### D8 — Capstone touches every shipped v7.3.2 piece (no decorative package)
The capstone is the integration proof for the entire milestone, so every shipped vertical and every CLI
stage must be exercised by at least one AT. The Task-1 spike and the AT review MUST confirm this matrix
holds; if any cell is unexercised, add coverage or surface the gap (do not silently leave it untested):

| Shipped piece | Exercised by | How |
|---|---|---|
| `Frank.Semantic` + `frank semantic` CLI (`extract`/`clarify`/`accept`; `status`/`finalize`/`refresh` at least smoke-run) | Task 1 build pipeline | the `MoveRequest` mapping is produced through extract → clarify → accept; lock-gate + term oracle run |
| MSBuild codegen (`GeneratedDiscovery.fs`, `GeneratedValidation.fs`, `GeneratedLinkedData.fs`, `GeneratedProvenance.fs`) | sample build | all four generated modules compiled and consumed by the running sample |
| `Frank.Discovery` (JSON Home, OPTIONS+Allow, `describedby`, ALPS) | AT-S1/S2/S3/S6 | client navigates home → resource → profile, links resolve, term set matches |
| `Frank.Validation` (SHACL 422, vocabulary IRIs) | AT-S4, AT-S6 illegal-move leg | out-of-range square → 422 ValidationReport |
| `Frank.LinkedData` (content negotiation: `application/json`, `application/ld+json` w/ external `@context`, `text/turtle`; `seeAlso` outbound links) | AT-S5 (extend to assert all three media types negotiate) | same game in JSON, JSON-LD, and Turtle |
| `Frank.Provenance` (PROV-O lineage, `has_provenance` link) | AT-S8 (D7) | end-of-game complete-capture audit |
| Composition (all middleware enabled together) | the running sample | every `use*` wired; ATs pass with the full stack on |

### D-deref — Every URI dereferenceable (DECIDED 2026-06-28)
The directive "all URIs dereferenceable in some way" is settled as follows (user-confirmed defaults):
1. **The sample SERVES its domain (`ttt:`) vocabulary.** Since no global term fits a board cell, Frank acts
   as a Linked Data *publisher*: the sample exposes its vocabulary namespace at a route, so `ttt:square`
   dereferences to a definition. The lock IRI base is build-time-fixed while the test host is dynamic
   (localhost:port) — the agent-simulator resolves this by **rebasing the term's path onto the test server
   base it is already talking to**. The spike must prove a GET on the (rebased) `ttt:square` IRI resolves
   (2xx) to something describing the term.
2. **Reused `schema.org` terms dereference LIVE over the network.** The agent-simulator GET/HEADs
   `https://schema.org/agent` etc. and asserts each resolves; it **fails loudly** if unreachable (GitHub
   runners have network). No silent skip. (Alternative local mirror was considered and declined.)

## Acceptance criteria

The existing AT-S1..AT-S6 ALL green (S4/S6 currently red → must turn green). AT-S4 and AT-S6 are
**revised**, not merely flipped:
- **AT-S4 (revised):** the illegal-move POST body uses the discovered/honest contract — `@type =
  https://schema.org/MoveAction`, the agent key `https://schema.org/agent`, the square key the `ttt:`
  IRI — and an out-of-range square (`"NotASquare"`) → 422 W3C `ValidationReport` citing a vocabulary IRI,
  no `urn:frank:`.
- **AT-S6 (revised — the anti-string-match):** the client (a) follows JSON Home → resource → `describedby`
  → ALPS and asserts each link resolves; (b) collects ALPS term IRIs and asserts the expected set
  `{ schema:MoveAction, schema:agent, ttt:square, schema:Game, schema:result }` is present; (c) identifies
  the actor input by recognizing `schema:agent` and the target by the domain term; (d) plays a full
  two-player game to a terminal state using only discovered IRIs — no hardcoded URLs, state names, class,
  or field IRIs. A meaningless-but-correctly-named mapping must NOT pass.

Also revised:
- **AT-S5 (revised):** the same game negotiates in **all three** representations — `application/json`,
  `application/ld+json` (external `schema.org` `@context` + `seeAlso`/wikidata outbound link), and
  `text/turtle` — so `Frank.LinkedData` content negotiation is fully exercised, not just ld+json.

PLUS:
- **AT-S7 (D5):** vocab swap `schema`→`ex` → hardcoded-IRI client fails, discovery client still completes a game.
- **AT-S8 (D7 — provenance complete-capture audit):** as the client plays, it keeps its own log of the moves
  it posted (agent + square, in order). At the end it follows the discovered `has_provenance` Link to the
  lineage and asserts the lineage **fully captured the session**: (1) one `prov:Activity` per posted move —
  count matches, no move dropped or invented; (2) each Activity attributed to the right agent and square;
  (3) order (`startedAtTime`/sequence) reproduces the client's log (replay); (4) the recorded terminal
  outcome matches the observed final game state. A lineage that drops, reorders, or fabricates a move fails.

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
