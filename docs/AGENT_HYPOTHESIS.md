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

1. **Response body hypermedia** (~10-15pp) — Phase 1 demo work. The [reference app](https://github.com/frank-fs/tic-tac-toe) will include structured JSON responses with embedded affordances.

2. **Client dual derivation** (~5pp) — [v7.4.0 milestone](MULTI-PARTY_SESSIONS.md). Mechanically derives the client's protocol obligations from the server's session type.

## The LLM Discovery Problem

Will an LLM even know to look for `Link` headers, ALPS profiles, or JSON Home? **No — not without prompting.**

LLMs are trained overwhelmingly on OpenAPI/Swagger patterns: `GET /api/v1/things`, JSON bodies, static schemas. They don't naturally reach for `Link` headers, `rel="profile"`, or JSON Home because those patterns barely exist in training data. If you point Claude or GPT at `http://localhost:5000` and say "play tic-tac-toe," it will try to guess URLs like `/api/games` or `/moves`, not parse `Link` headers from the response.

However, LLMs *can* use these standards effectively when told to. The RFCs are well-documented in training data. The question isn't capability — it's default behavior.

### The Five-Level Demo

The strongest demonstration shows the **same agent, same game, with progressively less hand-holding**:

| Level | Agent Setup | Discovery Mechanism | Expected Success |
|---|---|---|---|
| **0** | URL only, API headers only | LLM must find `Link`/`Allow` in HTTP headers | ~30-40% |
| **1** | URL only, HTML discovery page | `<link>` tags, semantic HTML, human-readable instructions | ~60-70% |
| **2** | URL only, HTML + agent welcome | The page explicitly teaches the agent how to use the API | ~80% |
| **3** | 3-sentence system prompt | External instruction to follow HTTP standards | ~80-85% |
| **4** | MCP tool-augmented | `fetch_and_parse_links` tool for mechanical header parsing | ~95% |

Example system prompt for Level 3:

> "You are an HTTP client. Start at the root URL. Read response headers — follow `Link` relations, respect `Allow` methods, fetch `rel="profile"` for semantics. Never guess URLs."

### HTML as the Discovery On-Ramp

The reason LLMs don't find `Link` headers at Level 0 is that HTTP headers are invisible in training data — nobody writes blog posts about response headers. But LLMs are *extremely* good at reading HTML. It's their most common training format.

If the discovery hints are **in the HTML itself**, no system prompt is needed. The application teaches the agent how to use it:

```html
<head>
  <link rel="service-doc" type="application/json-home" href="/" />
  <link rel="profile" href="/alps/tictactoe" type="application/alps+json" />
  <meta name="description" content="Self-describing TicTacToe API" />
</head>
```

At Level 2, a welcome page becomes the system prompt *inside the application*:

```html
<h1>TicTacToe</h1>
<p>This API is self-describing. To discover available resources:</p>
<ol>
  <li>Send GET / with Accept: application/json-home for the resource directory</li>
  <li>Follow Link headers with rel="profile" for action semantics</li>
  <li>Respect Allow headers — they change based on game state</li>
</ol>
```

An LLM fetches the root, reads that HTML, and knows exactly what to do. The "3 sentences of instruction" move from an external system prompt **into the application itself**. Self-describing all the way down.

### Semantic HTML and ARIA

Beyond `<link>` and `<meta>`, standard HTML patterns that LLMs understand deeply from training data:

- `<form action="/games/game1" method="POST">` with `<input name="position" type="number">` — LLMs know how forms work
- `<nav>` with labeled links — `<a rel="profile" href="/alps/tictactoe">API Profile</a>`
- `aria-label="Make a move"` on interactive elements
- Semantic structure: `<article>`, `<section>`, headings that describe state

A well-structured HTML form with proper `action`, `method`, and labeled inputs may be more discoverable to an LLM than any amount of ALPS profiles — because forms are the single most common interactive pattern in the LLM's training data.

### Framework vs. Application Concern

This is explicitly **not a framework feature**. Frank serves the HTTP headers and ALPS profiles. The HTML discovery page is an application-level pattern that the reference app demonstrates. The author controls the on-ramp:

- **Frank provides**: `Allow`, `Link`, ALPS profiles, JSON Home, projected profiles
- **The app author adds**: HTML welcome page, `<link>` tags, semantic forms, agent instructions
- **The reference app demonstrates**: Both layers working together

### What Each Level Proves

The **gap between Level 0 and Level 1** proves that HTML discovery is more natural for LLMs than HTTP headers. Moving hints from headers to `<link>` tags roughly doubles success rates without any external configuration.

**Levels 2 and 3 converge** (~80%). Whether the instructions come from a system prompt or from the application's own HTML welcome page, the success rate is similar. But Level 2 is more powerful because the app teaches the agent itself — no external configuration, no agent-specific setup, progressive enhancement for browsers.

The **small gap between Level 3 and Level 4** proves that HTTP standards are sufficient without custom tooling. The agent doesn't need a bespoke SDK or hand-written tool definitions.

### The Deeper Argument

HATEOAS was designed for browsers that understood link relations natively. LLMs are the first new "browser" in 30 years — but they need a nudge to act like one. The critical question for the thesis is whether that nudge is **3 sentences or 300 lines of tool definitions**.

If 3 sentences — whether in a system prompt or in the app's own HTML — get an LLM to ~80% success, Frank wins the argument against schema-first approaches (OpenAPI + tool-calling). The server does the work; the agent just needs to be a competent HTTP client.

If it takes 300 lines of custom tooling, the HATEOAS thesis is academically correct but practically irrelevant — and OpenAPI + tool-calling wins by default because the ecosystem already exists.

The demo must answer this question empirically.

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
