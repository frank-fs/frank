# Agent Hypothesis: Feature Combinations for Agentic API Consumption

How well can agents (LLM-based and rule-based) correctly use a Frank application, given different combinations of discovery and protocol features?

This assessment uses Tic-Tac-Toe as the reference scenario. "Success" means the agent discovers the API, plays only legal moves, respects turn-taking, and recognizes game-over — all without prior knowledge of the API beyond an entry-point URL.

## Probability Assessment

| # | Features Available | LLM Agent | Rule-Based Agent | Notes |
|---|---|---|---|---|
| 1 | Raw HTTP only (no headers, no discovery) | ~5% | ~0% | Must guess URLs, methods, semantics. LLM might infer from path names |
| 2 | `Allow` header only | ~15% | ~5% | Knows GET vs GET+POST per state, but no semantics for *what* to POST |
| 3 | `Allow` + `Link` headers | ~30% | ~15% | Can follow links, knows profile exists, but must fetch+parse ALPS |
| 4 | #3 + [ALPS profile](SEMANTIC-RESOURCES.md) | ~60% | ~45% | Full semantic vocabulary: states, transitions, field descriptors, roles |
| 5 | #4 + JSON Home (`GET /`) | ~70% | ~55% | Entry point discovery — agent needs no URL upfront |
| 6 | #5 + [Projected ALPS profiles](MULTI-PARTY_SESSIONS.md) (per-role) | ~80% | ~70% | Agent sees only *its own* affordances, reducing decision space |
| 7 | #6 + Structured response bodies (JSON with hypermedia controls) | ~90% | ~85% | Closes the in-band hypermedia gap; state + next actions in one response |
| 8 | #7 + [Client dual](MULTI-PARTY_SESSIONS.md) (session type obligations) | ~95% | ~95% | Complete protocol: not just what agent *can* do but what it *must* do |

## Key Inflection Points

### Allow → ALPS (+30pp)

The biggest single jump. `Allow` tells the agent *what methods are permitted*; ALPS tells it *what they mean*. Without ALPS, an LLM can guess from context clues; a rule-based agent is stuck.

Relevant features: [ALPS profiles](SEMANTIC-RESOURCES.md), semantic descriptors, `rt` (return type) links between states.

### ALPS → Projection (+10-15pp)

Reduces the search space from "all actions in all states for all roles" to "your actions in this state." Cuts false positive attempts — e.g., PlayerO trying to move on XTurn gets a profile that simply doesn't include that transition.

Relevant features: [Role projection](MULTI-PARTY_SESSIONS.md), `frank project` CLI command, projected profile middleware.

### Projection → Structured Bodies (+10-15pp)

This is the gap [Fielding identified](COMPARISON.md) — `Link` + `Allow` headers carry the protocol, but the body is opaque. JSON responses with embedded `_links` or action descriptors let the agent parse state *and* next actions in one response, eliminating the need for a separate OPTIONS or profile fetch per request.

### Structured Bodies → Client Dual (+5pp)

Marginal for a simple two-player game, but transformative for complex multi-party protocols. The [dual derivation](MULTI-PARTY_SESSIONS.md) gives the agent an offline-complete playbook — the full set of valid interaction sequences derived mechanically from the server's session type.

## Current State (v7.3.0)

The Frank stack currently provides features **#1 through #6**:

- [x] `Allow` headers — state-dependent, per-resource ([statechart affordances](STATECHARTS.md))
- [x] `Link` headers — `rel="profile"`, `rel="self"`, state-dependent relations
- [x] ALPS profiles — semantic descriptors, transition types, field definitions
- [x] JSON Home — `GET /` with strict content negotiation
- [x] Role projection — per-role ALPS profiles via projection operator
- [ ] Structured response bodies — **gap**; current sample returns plaintext
- [ ] Client dual — **v7.4.0 scope** (issues [#124](https://github.com/frank-fs/frank/issues/124), [#125](https://github.com/frank-fs/frank/issues/125))

**Current estimated success rate: ~80% (LLM) / ~70% (rule-based)**

This is past the HATEOAS threshold — agents can discover and use the API through standard HTTP mechanisms without prior API-specific knowledge.

## Closing the Gap

The remaining ~20% splits between two work items:

1. **Response body hypermedia** (~10-15pp) — Phase 1 demo work. The [reference app](https://github.com/frank-fs/frank-tictactoe) will include structured JSON responses with embedded affordances.

2. **Client dual derivation** (~5pp) — [v7.4.0 milestone](MULTI-PARTY_SESSIONS.md). Mechanically derives the client's protocol obligations from the server's session type.

## Methodology

These probabilities are qualitative estimates based on:

- What information each feature provides to the agent
- How much ambiguity remains at each level
- The difference between "can guess from context" (LLM) and "needs formal structure" (rule-based)
- The Tic-Tac-Toe game's relative simplicity (5 states, 2 roles, linear protocol)

For complex protocols (more states, more roles, branching paths), the gap between feature levels widens — particularly the dual derivation becomes more valuable as protocol complexity increases.

## Related Documents

- [Semantic Resources](SEMANTIC-RESOURCES.md) — ALPS profiles, RDF, Schema.org alignment
- [Multi-Party Sessions](MULTI-PARTY_SESSIONS.md) — Projection, duality, progress analysis
- [Statecharts](STATECHARTS.md) — State machines, transitions, guards, affordance maps
- [Spec Pipeline](SPEC-PIPELINE.md) — Cross-format validation, artifact generation
- [Comparison](COMPARISON.md) — Frank vs. other approaches
