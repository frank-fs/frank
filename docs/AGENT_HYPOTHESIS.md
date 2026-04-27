# Agent Hypothesis: Feature Combinations for Agentic API Consumption

How well can agents (LLM-based and rule-based) correctly use a Frank application, given different combinations of discovery and protocol features — and at what iteration cost?

This assessment uses Tic-Tac-Toe as the reference scenario. The runtime measure of "success" is not binary: an agent with no discovery surface may eventually complete a game by retrying, the question is *how many retries, how much context, and how often a session abandons*. Discovery and formalism reduce that cost; they do not (and need not) make completion uniquely possible.

## Three load-bearing questions

The thesis lives or dies on three iteration-cost questions, not on a single binary "agent navigates / agent fails" claim. Earlier framings of this document conflated capability with cost; the three questions below are testable independently and cleanly.

### Q1 — Runtime iteration cost

*Does discovery reduce first-try failure rate, retry count, and per-session context consumption for a runtime agent driving the API?*

A naive client without `Allow`/`Link`/ALPS still completes simple tic-tac-toe given enough retries. The question is whether discovery turns a 12-retry-with-context-bloat session into a 2-retry-with-tight-context session. Measured via the Five-Level Demo with **retry count, abandoned-session rate, and context tokens per game** alongside the binary success rate.

### Q2 — Authoring iteration cost

*Does formalism + tooling reduce the authoring iteration count required to produce a correct first-try implementation?*

A developer can hand-author ALPS profiles, JSON Home roots, and SHACL shapes. The question is whether `vocabulary { } + lock-file + codegen` reduces the authoring iteration count compared to hand-authoring against the same statechart fixture. Measured by **time-to-correct-first-try** and **rework count** on the same target artifact, with and without the tooling.

### Q3 — Continuous-improvement feedback loop

*Do discovery + formalism together produce diagnostics rich enough to drive an authoring agent's next iteration without human re-explanation?*

The runtime artifacts (ALPS, SHACL violations, OPTIONS responses, journal traces) are also authoring-agent inputs. The question is whether an authoring agent re-running on those diagnostics improves the source sketch or rigorous format more than a baseline that only sees the original spec. Measured by **iteration-over-iteration improvement rate** on a fixed authoring task with diagnostic feedback vs. without.

These three are independent: Q1 can succeed while Q2 fails (LLMs are smart enough at runtime, but tooling adds little authoring value), Q2 can succeed while Q1 fails (codegen wins on the developer side but agents still struggle at runtime), and Q3 requires both Q1 and Q2 mechanisms to be in place to test at all.

### The goal is the seam, not the ceiling

The thesis is not "all features matter." It is "find the *minimum sufficient combination* of features that drives correct agent behavior." Not all agent capabilities are always available — a session may lack tools, prior authoring context, or feedback-loop memory — and the framework must work across that variance. The diminishing-returns analysis of the Five-Level Demo is the experiment's primary output: at which feature layer does adding more discovery / formalism / structure stop reducing iteration cost? That layer is the seam, and it tells us where to stop investing.

### Smallest-sufficient-model is the cost-efficiency win

A successful framework lets cheaper models do agent work that previously required expensive ones. The deeper goal is: route most agent work to Haiku, Sonnet for harder problems, Opus for genuinely hard cases only. The Five-Level Demo therefore measures across three model sizes (Haiku / Sonnet / Opus) and reports, per F-layer, the *smallest model that achieves the iteration-cost floor*. If Haiku reaches near-instant success at F4, that is where the cost-efficiency goal lands.

### What depends on v7.3.2+ and what doesn't

Q1 (runtime / agent hypothesis) is provable on the foundational state machine alone, via hand-authored discovery. The tic-tac-toe experiment does exactly this — no v7.3.2+ machinery required.

Q2 (authoring pit of success) and Q3 (continuous-improvement feedback loop) genuinely require the v7.3.2+ infrastructure: the codegen pipeline, vocabulary lock, analyzers, Li-et-al implementability check, MSBuild integration. Statecharts and MPST as design patterns stay foundational and agent-facing — the state machine that drives projection at richer F-layers is part of the design from the beginning. The *additional formal verification structure of v7.3.2+* is what's developer-facing and what Q2/Q3 measure.

This split lets us prove Q1 first via measurement, then decide whether v7.3.2+'s authoring-side investment is justified by the Q1 results plus the predicted Q2/Q3 wins.

## Two agent roles: runtime and authoring

This document's probability tables and demos concern the **runtime agent** — an LLM or rule-based client that consumes a running Frank application, reads its discovery surface, and drives it to correct completion. That is the load-bearing empirical claim the thesis has to defend, and its confidence level is the one the Five-Level Demo measures.

The same LLM capabilities show up in a second role during development: the **authoring agent** works alongside the developer, reads design-time artifacts (vocabulary CE declarations, sketches in mermaid or smcat, generated ALPS/SHACL/PROV-O), and produces proposed mappings, lifts, and refinements that land in committed lock files after human review. The two roles share the same underlying capabilities but answer to different bars:

| Concern | Runtime agent | Authoring agent |
| --- | --- | --- |
| Correctness gate | Probabilistic; measured in the Five-Level Demo | Human-reviewed before any artifact is committed |
| Failure mode | Illegal action, stuck state, abandoned session | Proposed mapping the developer rejects |
| Determinism | Must hold across sessions and model versions | Not required — output is reviewed once and locked |
| Load-bearing artifact | The server's discovery surface | The committed lock file |

Separating the roles matters because critics who push back on the runtime probability numbers are pushing on the harder of the two claims. The authoring-time value is defensible independent of how well runtime agents navigate a live API: a developer using Frank's CLI to iterate on a design, with LLM assistance at authoring time and deterministic codegen at build time, gets correctness guarantees that do not depend on any runtime inference by an agent. The runtime demos establish the ceiling; the authoring workflow establishes the floor. Both matter, but conflating them makes the argument weaker than it needs to be.

The feedback loop sketched in `AUTHORING_WORKFLOW.md` operates entirely in the authoring-agent role: the LLM reads generated artifacts, exercises the running app through dry runs, inspects journal traces, and proposes refinements to the source sketch or rigorous format. Whether a runtime agent in production hits 80% or 40% on the Five-Level Demo, the authoring loop delivers value by closing the gap between the author's intent and the generated system's behavior, with human review at every commit point.

## Initial qualitative estimates (pending Five-Level Demo measurement)

The percentages below are *predictions to be replaced with measurements*, not forecasts. They were authored before any empirical run of the Five-Level Demo and reflect intuition about where each feature should move the needle on Q1 (runtime iteration cost). The Five-Level Demo replaces each row's qualitative success rate with measured retry counts, abandoned-session rates, and context-token consumption. Treat the table below as a *starting hypothesis grid*: features with surprising-on-measurement low impact are candidates for scope reduction; features with surprising-on-measurement high impact are candidates for earlier delivery.

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

## Current State (v7.4.0 in progress)

### v7.3.0/v7.3.1 Rollback

The v7.3.0 implementation claimed features #1-6 were working, with extensive test suites passing. However, the implementation had to be rolled back because **tests proved test infrastructure, not the framework**:

- The statechart hierarchy was flat — states were treated as a flat FSM despite hierarchy being declared
- Link headers never made it into actual HTTP responses — tests validated helpers that produced correct output, but the middleware pipeline didn't carry the information through
- model.bin was wrong or empty — the affordance middleware loaded nothing

The core failure: tests passed but an agent making real HTTP requests would not receive the advertised discovery information. The thesis was unprovable.

### v7.4.0 Correction

All v7.4.0 issues exist to fix these failures. Current feature status:

- [x] `Allow` headers — state-dependent, per-resource ([statechart affordances](STATECHARTS.md)) — **working, verified via HTTP**
- [ ] `Link` headers — `rel="profile"`, `rel="self"`, state-dependent relations — **[#298](https://github.com/frank-fs/frank/issues/298): typed codegen pipeline ([#283](https://github.com/frank-fs/frank/issues/283), [#284](https://github.com/frank-fs/frank/issues/284)) + affordance middleware wiring**
- [ ] ALPS profiles — semantic descriptors, transition types, field definitions — **[#272](https://github.com/frank-fs/frank/issues/272): serve generated ALPS at profile URI**
- [ ] JSON Home — `GET /` with strict content negotiation — **[#299](https://github.com/frank-fs/frank/issues/299): wire useJsonHome + root resource**
- [ ] Role projection — per-role ALPS profiles via projection operator — **[#278](https://github.com/frank-fs/frank/issues/278): multi-role fix**
- [ ] Structured response bodies — **gap**; current sample returns plaintext
- [ ] Client dual — **[#288](https://github.com/frank-fs/frank/issues/288): DualAlgebra AND-state gap closure**

**Current estimated success rate: ~15% (LLM) / ~5% (rule-based)** — Allow headers work, but Link headers, ALPS profiles, and JSON Home are not served. This is feature level #2, not #6.

**Target success rate after v7.4.0: ~80% (LLM) / ~70% (rule-based)** — the rate v7.3.0 claimed but didn't achieve.

## Closing the Gap

> **Status (v7.3.2, April 2026):** All three thesis tracks depend on the protocol algebra (`TransitionAlgebra`) being implemented correctly as a tree-shaped, hierarchy-preserving type rather than a flat-list approximation. Earlier attempts repeatedly collapsed to flat-list patterns; the v7.3.2 reset cleared that scar tissue and the algebra is being rebuilt per the [v7.4.0 protocol-types spec](superpowers/specs/2026-04-21-v740-protocol-types-design.md). The per-issue probability estimates below assume that rebuild lands cleanly. Read them as conditional on resolving the flat-type bottleneck, not as unconditional forecasts.

### Three thesis proof tracks

v7.4.0 proves the thesis through three progressively demanding demonstrations:

**Track A — REST agent (request/response discovery)**

A naive client navigates the order fulfillment lifecycle using only Link headers, ALPS profiles, JSON Home, and OPTIONS — no prior knowledge beyond the entry URL.

| Issue | What | Prob (session) |
|---|---|---|
| [#286](https://github.com/frank-fs/frank/issues/286) | TransitionAlgebra + Abstractions package | ~88% |
| [#283](https://github.com/frank-fs/frank/issues/283) | frank-cli extract --format fsharp | ~78% |
| [#284](https://github.com/frank-fs/frank/issues/284) | MSBuild codegen integration | ~88% |
| [#298](https://github.com/frank-fs/frank/issues/298) | Generated types + Link/Allow headers | ~80% |
| [#299](https://github.com/frank-fs/frank/issues/299) | JSON Home entry point | ~90% |
| [#272](https://github.com/frank-fs/frank/issues/272) | ALPS profile endpoint | ~90% |
| [#300](https://github.com/frank-fs/frank/issues/300) | OPTIONS discovery | ~88% |
| [#301](https://github.com/frank-fs/frank/issues/301) | Naive client e2e test | ~75% |

Note: #286 → #283 → #284 → #298 is a sequential chain (algebra → codegen → MSBuild → wiring). #299, #272, #300 are parallel. #301 depends on all.

**Track B — Reactive streaming agent (Datastar/CQRS + SSE)**

Server receives async events (webhooks, background jobs), defers events the current state can't handle, and pushes state changes to clients via Server-Sent Events. Demonstrable in `frank-fs/tic-tac-toe` directly: real async sources are already present in the multi-game-with-auth surface (session expiry mid-move, second player joining while first player's move is in flight, SSE reconnect after disconnect). These are not contrived; they are what happens when you put auth + concurrency + streaming on a non-trivial app.

| Issue | What | Prob (session) |
|---|---|---|
| [#263](https://github.com/frank-fs/frank/issues/263) | Deferred events | ~70% |

In classic HTTP, the client IS the event queue (retry on 400/405). In Datastar/CQRS, the server receives events from multiple sources and the client is passive. Deferred events prevent event loss when events arrive out of order.

**Track C — Concurrent multi-role agents (MPST + broadcast + role-projected SSE)**

Multiple agents (different MPST roles) connected simultaneously via SSE. When one role advances a region in an AND-state composite, inter-region broadcast fires, and each connected agent receives their role-projected affordance update in real time.

The simplest possible Track C demonstration is also already in `frank-fs/tic-tac-toe`: Player A wins → Game Play region transitions to Won → reset becomes available to both assigned players but not to spectators — a single broadcast firing role-different SSE patches across three connection types (X, O, Spectator). Order fulfillment remains the paper-realism existence proof; tic-tac-toe is the working proof.

| Issue | What | Prob (session) |
|---|---|---|
| [#304](https://github.com/frank-fs/frank/issues/304) | AND-state event broadcast | ~75% |
| [#305](https://github.com/frank-fs/frank/issues/305) | Datastar multi-region SSE push | ~82% |
| [#306](https://github.com/frank-fs/frank/issues/306) | Role-projected concurrent SSE | ~68% |

The strongest thesis proof in the order-fulfillment context: Warehouse completes pick, Pack region auto-advances, Customer sees fulfillment progress, ShippingProvider sees nothing (their region is unchanged) — all via role-projected SSE streams, all from a single state change.

### Probability metrics

Two metrics matter:

| Metric | What it measures | How computed |
|---|---|---|
| **Implementation probability** | All required issues complete with normal iteration | Product of per-issue probability with retry: 1-(1-p)^2 |
| **Thesis probability** | An agent actually navigates correctly via the implemented features | Implementation probability × verification factor |

**Implementation probability** assumes each issue gets up to two attempts — a failed session produces diagnostics that inform the next attempt. This is conservative; most well-defined issues succeed within two tries.

**Thesis probability** adds a verification factor. Even with all issues complete, the v7.3.0 failure pattern (tests pass, HTTP responses wrong) could recur. The verification factor reflects confidence that the acceptance criteria — falsifiable HTTP request/response pairs verified via real HTTP requests — prevent this.

The factor is high for Track A (~95%) because [#301](https://github.com/frank-fs/frank/issues/301) IS the thesis test: a naive client navigating the full order lifecycle by following links. If #301 passes via real HTTP requests, the thesis is proven by definition. The factor is lower for Track C (~85%) because [#306](https://github.com/frank-fs/frank/issues/306)'s concurrent multi-role SSE scenario has more ways to pass tests while subtly failing in production.

**Per-issue probability** (the "Prob (session)" column in the issue tables above) is the chance that a single Claude Code session completes an individual issue. This is the input to the compound calculations — not a metric on its own.

|  | Implementation probability | Verification factor | **Thesis probability** |
|---|---|---|---|
| **Track A** (REST) | 92% | ~95% | **~87%** |
| **Track A+B** (Reactive) | 87% | ~93% | **~81%** |
| **Track A+B+C** (Full) | 82% | ~85% | **~70%** |

These estimates assume well-defined implementation plans with resolved design decisions, concrete type signatures, exact file paths, and falsifiable HTTP acceptance criteria — the standard established by the issue refinement process. Per-issue session success averages ~92% when plans are robust and well-defined.

Track A at **~87%** is high confidence — 8 issues, all refined, with the e2e test (#301) directly verifying the thesis via real HTTP requests.

Track A+B+C at **~70%** is achievable — proving the complete thesis (REST + reactive + concurrent agents, all role-projected, all hierarchical) within v7.4.0 is realistic with well-defined plans. Each track is independently valuable; even if only Track A ships initially, Tracks B and C follow with their infrastructure already in place.

**Remaining risk**: The design decisions and prior contradictions that drove the v7.3.2 reset are now consolidated in the three architectural specs ([2026-04-20](superpowers/specs/2026-04-20-v732-semantic-discovery-design.md), [2026-04-21](superpowers/specs/2026-04-21-v740-protocol-types-design.md), [2026-04-23](superpowers/specs/2026-04-23-v740-resource-oriented-hypermedia-design.md)). The earlier `DECISIONS.md` and `AUDIT.md` analyses are recoverable from git history if the historical investigation is needed.

### Improving thesis probability

The verification factor is the new lever. v7.3.0 had ~0% verification (tests passed, HTTP was wrong). v7.4.0 improvements:

| Safeguard | What it prevents | Issues |
|---|---|---|
| Falsifiable HTTP ACs | Tests that pass without correct HTTP responses | All issues |
| e2e navigation test | Discovery working in unit tests but not in assembled app | #301, #306 |
| ALPS profile validation | Profiles with dangling refs, type inconsistencies | #302, #303 |
| Compile-time analyzers | Middleware not wired, role projections incomplete | #291-297 |
| Interpreter algebra | Flat hierarchy passing as hierarchical (formal semantics) | #286-290 |

Each safeguard raises the verification factor. The analyzers and algebra don't directly prove the thesis — they prevent the class of failures that made v7.3.0 unprovable.

### Highest-risk issues to the thesis

| Issue | Prob | If it fails |
|---|---|---|
| [#306](https://github.com/frank-fs/frank/issues/306) | 68% | Track C dead — role-projected SSE is the culminating proof |
| [#263](https://github.com/frank-fs/frank/issues/263) | 70% | Track B dead — deferred events can't be demonstrated |
| [#288](https://github.com/frank-fs/frank/issues/288) | 72% | DualAlgebra — affects MPST projection correctness for Track C |
| [#304](https://github.com/frank-fs/frank/issues/304) | 75% | Track C dead — no broadcast, no multi-region push |
| [#301](https://github.com/frank-fs/frank/issues/301) | 75% | Track A dead — the thesis test itself |

### Why HTTP simplifies statechart formalism

HTTP's request/response model eliminates several Harel formalisms for Track A:

| Formalism | Why HTTP doesn't need it | But Datastar/CQRS does |
|---|---|---|
| Deferred events | Client IS the queue (retry on 400/405) | Server receives async events from multiple sources |
| Inter-region communication | Agent advances regions via separate requests | Background completion should auto-advance siblings |
| Entry/exit actions | One request = one response; handler does side effects | SSE push sequence matters for UI ordering |
| Internal transitions | Agent sees same Link headers regardless | History recording correctness |

Tracks B and C prove the thesis under conditions where HTTP's simplifications no longer hold — the same architecture works for both interaction models.

### Remaining gap after v7.4.0

1. **Response body hypermedia** (~10-15pp) — Structured JSON responses with embedded affordances. Currently returns plaintext.
2. **Client dual derivation** (~5pp) — [#288](https://github.com/frank-fs/frank/issues/288) (DualAlgebra) enables mechanically deriving the client's protocol obligations.

## The LLM Discovery Problem

Will an LLM even know to look for `Link` headers, ALPS profiles, or JSON Home? **No — not without prompting.**

LLMs are trained overwhelmingly on OpenAPI/Swagger patterns: `GET /api/v1/things`, JSON bodies, static schemas. They don't naturally reach for `Link` headers, `rel="profile"`, or JSON Home because those patterns barely exist in training data. If you point Claude or GPT at `http://localhost:5000` and say "play tic-tac-toe," it will try to guess URLs like `/api/games` or `/moves`, not parse `Link` headers from the response.

However, LLMs *can* use these standards effectively when told to. The RFCs are well-documented in training data. The question isn't capability — it's default behavior.

### The Five-Level Demo — instrument for Q1, gradient for Q2 and Q3

The Five-Level Demo answers Q1 (runtime iteration cost) directly. The same demo also instruments Q2 and Q3 because each level supplies a different richness of feedback to an authoring agent.

**Baseline note:** the v7.2.0 baseline ships `Frank.OpenApi` (route-table → OpenAPI doc), so any reference application at Level 0 already serves an OpenAPI document at a standard path. The agent at Level 0 has *that* doc plus HTTP headers — it does not have a no-discovery surface. The Five-Level gradient measures HATEOAS additions *over* the OpenAPI baseline, which is the realistic comparison against schema-first APIs.

| Level | Agent Setup | Discovery Mechanism | Expected Success | Q2/Q3 instrument |
|---|---|---|---|---|
| **0** | URL only; OpenAPI doc + HTTP headers available | LLM may read OpenAPI doc; must find `Link`/`Allow` in HTTP headers | ~30-40% | Authoring agent sees OpenAPI + HTTP — moderate diagnostic surface |
| **1** | URL only, HTML discovery page | `<link>` tags, semantic HTML, human-readable instructions | ~60-70% | Authoring agent reads same HTML the runtime agent does |
| **2** | URL only, HTML + agent welcome | The page explicitly teaches the agent how to use the API | ~80% | Application self-explains — authoring loop reads the welcome page as a contract |
| **3** | 3-sentence system prompt | External instruction to follow HTTP standards | ~80-85% | Same as L2 plus baseline-vs-instructed comparison data |
| **4** | RPC-tool-call MCP (the OpenAPI-style alternative) | App exposed as MCP tool calls (`new_game`, `get_board`, `make_move`, ...) — no HTTP discovery surface | ~95% | The *null hypothesis* Frank's HATEOAS thesis competes against, not a discovery aid. If V_proto's full F-curve (F1–F4 discovery + F6–F8 semantic) at E1 matches or beats this ceiling, the thesis holds: discovery reaches RPC parity with one-time framework investment vs per-API tool authoring. The earlier "MCP tool that parses discovery headers" framing was retired — it tested server-metadata completeness, which the rest of the experiment already assumes (circular). The replacement RPC MCP is built as part of the tic-tac-toe layered experiment. |

Example system prompt for Level 3:

> "You are an HTTP client. Start at the root URL. Read response headers — follow `Link` relations, respect `Allow` methods, fetch `rel="profile"` for semantics. Never guess URLs.

**Q1 measurement:** for each level, record retry count per game, abandoned-session rate, context-token consumption per completed game, and binary success rate. The success column above is the predicted starting point; the demo replaces it with measurements.

**Q2 measurement:** hand-author the L2 artifacts (HTML welcome, ALPS profile, JSON Home root) and time-to-correct-first-try; then run codegen against the same statechart fixture and time-to-correct-first-try; compare. Q2 confirmed if codegen reduces authoring iteration count materially without regression in runtime success at L2.

**Q3 measurement:** run the authoring agent in two configurations on the same iteration task — one with full diagnostic feedback (ALPS, SHACL violations, journal traces from a failed run), one with only the original spec. Q3 confirmed if the diagnostic-fed configuration converges on a correct artifact in fewer iterations than the spec-only configuration.

A Level 0 app offers the authoring loop nothing to work with; a Level 2 app lets the authoring agent explain what the reference client will and will not find. The runtime bet and the authoring loop are served by the same investment.

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

These probabilities are *initial qualitative estimates* — predictions to be replaced with measurements from the Five-Level Demo. They are based on:

- What information each feature provides to the agent
- How much ambiguity remains at each level
- The difference between "can guess from context" (LLM) and "needs formal structure" (rule-based)
- The actual discovery surface of the reference application

### Simplicity is the argument, not a hedge

The reference scenario `frank-fs/tic-tac-toe` is deliberately a simple game. Earlier framings of this document characterised that as "5 states, 2 roles, linear protocol" — implicitly apologetic, as though the simplicity weakened the result. **That framing has it backwards.** Picking the simplest possible useful protocol is the *strongest* test bed for a discovery framework's value:

- Guessing works on simple protocols. An LLM with no prior exposure to this exact API can produce a partially-correct request set just by being a competent HTTP client. That makes the *zero-discovery baseline* genuinely competitive.
- Hand-coding works on simple protocols. A human can write the entire client in an afternoon. That makes the *no-framework alternative* genuinely competitive.
- If the framework demonstrably helps an agent navigate the simplest case where guessing and hand-coding are both maximally viable, **complex protocols can only benefit more** — this document's own observation that "the gap between feature levels widens" with complexity makes this an a fortiori argument. The simple case is a lower bound on framework value, not a weak demonstration.

There is also precedent the F# community already accepts. The reference app cites Wlaschin's *Enterprise Tic-Tac-Toe*, and there is a decade-plus F# tradition of using tic-tac-toe as the canonical pedagogical surface for non-trivial architectural claims — domain-driven design, type-driven design, railway-oriented programming. Picking it for a discovery-framework claim slots into that tradition cleanly; reviewers grok the move without needing the simplicity defended.

### What the agent actually has to discover

The "5 states, 2 roles, linear protocol" line undercounts the actual surface. At Levels 1–5 the agent has to discover all of:

- A collection resource at `/` with N concurrent games (join-vs-create choice)
- A child auth state machine that must complete before any `/games` operation (a sequencing constraint)
- Implicit role assignment: first POST to `/games/{id}` both moves and assigns X; second joiner is assigned O; subsequent users are spectators with neither role's affordances — a three-role asymmetry emerging from a single endpoint
- Two orthogonal regions in the statechart (Game Play × Player Identity), an AND-state composite — the same structure Track C is built to demonstrate
- Role-gated lifecycle (reset/delete require assignment) — a clean affordance-discrimination check
- MoveError recovery via history — tests whether the agent uses returned affordances to retry or spirals into 4xx loops

The LLM's tic-tac-toe priors only carry weight inside the game itself ("play X to win"). They do not cover any of the bullets above. The Five-Level Demo measures whether the agent discovers *protocol structure*, not whether it can compute a winning move; the familiar inner game is useful as a clean victory condition without having to teach the rules.

### Two complementary demos, two rhetorical jobs

Tic-tac-toe and order fulfillment serve different purposes; neither replaces the other:

| Demo | Rhetorical job |
|---|---|
| **Tic-tac-toe** (this document's headline matrix) | Proves the framework *adds value against the strongest possible alternative*. Guessing works; hand-coding is trivial; if HATEOAS still wins, the value is real. Lower bound on framework value. |
| **Order fulfillment** (Track A/C #301 e2e) | Proves the framework *doesn't break down under realistic protocol shape*. Multi-role choreography, AND-states, complex affordance projection. Existence proof of generalisation. |

Reading the headline matrix as tic-tac-toe and the capstone e2e as order fulfillment is *not* inconsistent — it's complementary. The doc should say this explicitly, and going forward the two demos should be cited side-by-side as discharging the two rhetorical jobs.

### The killer baseline: V_swagger

The matrix as currently structured varies feature combinations *within* Frank. The result tells you what each Frank feature contributes to the Frank-internal curve. It does not directly answer the architectural question — *is HATEOAS better than the dominant industry alternative (OpenAPI + tool-calling)?*

Closing that loop requires a horizontal comparison at fixed simplicity: same simple game, three implementations:

| Implementation | What the agent sees |
|---|---|
| **V_swagger** — same paths as V_proto, OpenAPI mounted, plain JSON/HTML, no role projection, no state-dependent affordances. Participates in F1–F4 discovery + F6–F8 semantic with role-uniform implementations; never role-projects. | OpenAPI doc + (as F-layers land) role-uniform Allow / Link / ALPS / JSON Home / JSON-LD / PROV-O / SHACL |
| **Frank F0 / V_proto** | OpenAPI doc (Frank.OpenApi) + role-projected HTML/Datastar surface; no HATEOAS additions in headers yet |
| **Frank full F-curve / V_proto** | Role-projected discovery (F1–F4) + role-projected semantic layer (F6–F8): state-dependent + role-projected Allow / Link / ALPS / JSON Home / JSON-LD / PROV-O / SHACL |

If V_swagger lands at ~30–40% (LLM stumbles on state-dependent invariants, retries through 4xx) and Frank's full F-curve lands at ~85%, that is the headline number — framework value vs dominant alternative, not within-framework feature value. As a bonus this **dissolves the path-name contamination concern**: V_swagger uses the same paths V_proto does, so the LLM's path-guessing prior gets the same free hit on both. The gap between V_swagger and the full F-curve is pure discovery + role-projection signal, not vocabulary luck.

The F-axis has two phases: **F1–F4 (discovery)** = Allow, Link rel=profile, ALPS, JSON Home; **F6–F8 (semantic)** = JSON-LD content negotiation, PROV-O provenance, SHACL validation. F5 and the original F7-as-role-projection are skipped — state-dependence and role-projection are V_proto's per-layer design properties carried throughout, not separate layers to add.

For complex protocols beyond what tic-tac-toe exposes (more states, more roles, branching paths), the gap between feature levels widens further — particularly dual derivation becomes more valuable as protocol complexity increases.

Going forward the input quantities (per-feature success rate, retry count, abandoned-session rate, context-token consumption) come from runs of the Five-Level Demo, not from a-priori estimates. The methodology section is preserved as the analytical structure that consumes those measured inputs; the predictions stay only to record what we expected before measurement.

### Implementation probability methodology

Session completion probabilities (the "Prob" columns in the thesis tracks) estimate the chance that a single Claude Code session completes the issue — builds, tests pass, meets acceptance criteria verified via real HTTP requests.

Compound track probabilities multiply individual issue probabilities (assuming independence). "With retry" uses P(success in 2 attempts) = 1 - (1-p)^2 per issue.

These are deliberately conservative. The v7.3.0/v7.3.1 experience showed that claiming high confidence on extensive test suites is meaningless if the tests don't verify actual HTTP responses. Every v7.4.0 acceptance criterion is a falsifiable HTTP request/response pair — the bar is "an agent gets correct information," not "tests pass."

## v7.4.0 Issue Map

### Parent issues (tracked via children)

| Issue | Children |
|---|---|
| [#252](https://github.com/frank-fs/frank/issues/252) Discovery surface | #298, #299, #272, #300, #301 |
| [#257](https://github.com/frank-fs/frank/issues/257) Interpreter algebra | #286, #287, #288, #289, #290 |
| [#264](https://github.com/frank-fs/frank/issues/264) Inter-region communication | #304, #305, #306 |
| [#273](https://github.com/frank-fs/frank/issues/273) childOf | #293, #294 |
| [#285](https://github.com/frank-fs/frank/issues/285) Analyzers | #291, #292, #295, #297 |
| [#176](https://github.com/frank-fs/frank/issues/176) ALPS validation | #302, #303 |

### Layered architecture (opt-in)

Each layer works independently. Higher layers strengthen guarantees.

```
Layer 0: ASP.NET Core          (endpoints, middleware, DI)
Layer 1: resource/statefulResource CE   (existing, working)
Layer 2: Discovery middleware   (useJsonHome, useOptionsDiscovery — existing, needs wiring)
Layer 3: Affordance middleware  (useAffordances from generated types — existing, needs wiring)
─────────────── Q1 sufficiency line is hypothesised here; Five-Level Demo will measure where it actually falls ───────────────
Layer 4: Interpreter algebra    (opt-in formalization of HierarchicalRuntime)
Layer 5: Typed codegen          (opt-in DX, replaces string-based model.bin)
Layer 6: Analyzers              (opt-in compile-time validation)
Layer 7: Transition CE          (opt-in hand-authoring path)
```

Layer 3 is the *predicted* sufficiency line for Q1 (runtime iteration cost) — we expect that an L2 application running on Layer 3 hits the iteration-cost target the Five-Level Demo will measure. The actual line may sit lower (LLMs are smarter than the 2026 estimates suggested) or higher (formal semantics are load-bearing in ways the predictions missed). Layers 4–7 exist because Q2 and Q3 (authoring iteration cost, feedback-loop convergence) plausibly require formal semantics, compile-time checks, and correct codegen — independent of where the Q1 line falls. The v7.3.0/v7.3.1 failure showed that without those upper layers, the lower layers silently produce wrong output that passes tests but fails agents; that risk applies to Q2/Q3 even if Q1 is met without them.

## Related Documents

- [Semantic Resources](SEMANTIC-RESOURCES.md) — ALPS profiles, RDF, Schema.org alignment
- [Multi-Party Sessions](MULTI-PARTY_SESSIONS.md) — Projection, duality, progress analysis
- [Statecharts](STATECHARTS.md) — State machines, transitions, guards, affordance maps
- [Spec Pipeline](SPEC-PIPELINE.md) — Cross-format validation, artifact generation
- [Comparison](COMPARISON.md) — Frank vs. other approaches
