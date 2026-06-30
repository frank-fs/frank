# Capstone #333 — Expert-Review Fix Plan (decisions ratified 2026-06-29)

Source: `/expert-review` panel (Berners-Lee, Miller, Fielding, @7sharp9) on branch `capstone-discovery-completion` (diff vs master). The capstone's AT-S1..S8 were partly **false-green** (passed without proving the claim). Every item below is a **maintainer-ratified decision** (grilled one-by-one). NO deferrals to Track A unless explicitly noted; two attempts to over-defer (#6, #7) were caught and corrected — read-side semantic self-description IS v7.3.2.

## Foundation (interdependent — do first)

**#6 — Relative term IRI + runtime base-resolution (drop `example.org` + client rebasing).**
`example.org` was an unforced placeholder I (assistant) wrote into the plan; NOT a ratified decision; a dereferenceable namespace was always achievable. Fix: the `ttt:` term is **relative** (`/tictactoe#square`); resolve it against the **request origin** consistently across THREE layers so the term's absolute identity is identical everywhere:
- Discovery: ALPS `href` stored relative; client resolves to the serving host.
- LinkedData: served graph emitted with `@base` = request origin (Turtle `@base` / JSON-LD `@base`).
- Validation: SHACL `sh:path` ↔ POST body key must resolve to the SAME request-host IRI (today AT-S4 only passes because both are the same `example.org` string — keep them equal under host-resolution).
Drop the AT-S6 rebasing; add a test asserting the relative-resolved (unrebased) IRI dereferences 2xx to the term. Semantic note (accepted): a host-relative term has a deployment-specific absolute IRI — correct for an app-owned vocabulary.

**#1 (+#5, +#7) — Per-resource instance graph; LinkedData serves ONLY endpoint-configured graphs; safe-method guard; self-describing state.**
Today `GET /games/{id}` ld+json/turtle returns the GLOBAL ontology, not the game (AT-S5/S6 conneg = false green — they assert only `@context`/`schema.org` presence, which the ontology has). Empirically confirmed `POST /moves` + `Accept: text/turtle` → ontology dump, **move never executes** (#5).
- Game endpoint emits its OWN graph: subject = the game IRI; `status`→`schema:actionStatus` (Active/CompletedActionStatus), cells/validMoves → `ttt:` terms, identifier, etc. (#7: read-side self-describes via vocab terms.)
- LinkedDataMiddleware serves RDF only for endpoints carrying a `LinkedDataConfig` graph (no global-for-every-path fallback) → `POST /moves` (no graph) passes through, move runs.
- Safe-method guard: RDF short-circuit only for GET/HEAD (#5).
- AT-S5/S6 assert the GAME's triples (not just ontology presence); AT-S6 reads `status`/validMoves via IRIs, not `GetProperty("status")`/hardcoded `"Won"`/`"Draw"`.

**#4 — Field-shape ALPS nesting (pull forward from the Track-C deferral, which was about STATE/TRANSITION-per-role nesting, a different thing).**
`DiscoveryEmitter` nests field descriptors under their class (`MoveAction` *contains* `agent` + `square`) + emits the move as an `unsafe` transition descriptor (`rt`→Game). Meets D2 ("discover the request shape"). AT-S6 then selects the target by ROLE ("the MoveAction input that isn't the recognized agent") — not by hardcoded IRI/local-name. Subsumes Miller's flat-ALPS findings.

## Acceptance-test corrections (after foundation)

**#2 — `has_provenance` affordance is inverted.** Per PROV-AQ (NOT RFC 6903 — fix the comment): Link target = the provenance document (`/provenance?resource=<uri>`) + `anchor=<resource>`; drop the misleading `type`. AT-S8 then FOLLOWS the discovered Link directly (no hardcoded `/provenance?resource=` construction).

**#3 — Provenance body attributes: validate + emit IRIs.** (a) Validate body-attribute keys with `Uri.TryCreate(Absolute)`; drop + `ILogger.LogWarning` invalid ones (fixes a client-controlled-URI → `CreateUri` → 500/DoS). (b) For a property whose declared range is a class (carry range from the resolved model), emit the value as a URI node (`ttt:TopLeft`, the agent IRI) — not a string literal — honoring `rdfs:range` and linking into the vocab.

**#4-AT-S7 — real term swap by role.** Rename one `ex:` term (`ex:square`→`ex:cell`); the discovery client survives by ROLE (structural, via #4 nesting), proving resilience to a genuine term change, not just a prefix rename. `ex:` server also serves its own vocab (#18).

**#17 — union-case outcome IRIs in ALPS.** Emit case descriptors (`Won`/`Draw`→`schema:CompletedActionStatus`) so the outcome term AT-S8 cross-checks is discoverable (couples to #4 nesting + #7 status self-description).

## Cleanups (mostly independent)

- **#8** `Vary: Accept` on all conneg responses + 406 (RFC 9110).
- **#9** JSON Home `href-template` MUST emit `href-vars` (json-home draft).
- **#10** JSON Home Content-Type `application/json-home` (not `…+json`).
- **#11** ALPS descriptor-`id` uniqueness check (post-projection) in DiscoveryEmitter.
- **#12** AT-S6 asserts 200 per move POST (drop `let! _ =`).
- **#13** Buffer the request body ONCE at the edge (Provenance+Validation currently each `EnableBuffering`; 413 guard only in Validation). Moderate.
- **#14** Provenance body capture: widen from POST-only to **POST/PUT/PATCH** (body-bearing verbs). [ratified: expand]
- **#15** 9 cell individuals get `rdfs:label`.
- **#16** provenance + `linkedDataGraph` `@context` include `schema`/`ttt` so terms compact.
- **#18** `ex:` server serves its own vocab too (no dangling term IRIs; consistency with #6).
- **#19** `parseDeclaredPrefixes`: narrow `with _` → `:? JsonException`.
- **#20** `prefixOfCurie`: guard `://` (returns `"http"` for absolute IRIs today).
- **#21** Extract the declared-prefix-merge helper (duplicated in `ResolvedModel` + `Accept`; rule 8).

## Genuinely deferred (Track A / v7.4.0 — confirmed, not a dodge)
Full hypermedia state-machine: next-moves as *followable link affordances* (`rel=schema:potentialAction`) the client GETs/POSTs without knowing the move URL. NOT required to close #7 (semantic read-side self-description is enough for v7.3.2).

## Execution order
1. Foundation: #6 (relative IRIs + base resolution) → #1/#5/#7 (per-resource graph, method guard, self-describing state) → #4 (ALPS nesting). These are codegen + middleware changes; TDD each.
2. AT rewrites: AT-S4 (host-resolved IRI), AT-S5 (game's own triples ×3 formats), AT-S6 (role-based selection, read via IRIs, no rebasing, deref relative IRI), AT-S7 (`ex:cell` real swap), AT-S8 (#2 follow the link, per-move IRIs). #17.
3. Cleanups #8-21.
4. Re-run /expert-review (TBL/Miller/Fielding) to confirm the false-green items are now genuinely green; full suite repeatably green; fantomas; /self-reflect; STOP for merge approval.
