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

### Three thesis proof tracks

v7.4.0 proves the thesis through three progressively demanding demonstrations:

**Track A — REST agent (request/response discovery)**

A naive client navigates the order fulfillment lifecycle using only Link headers, ALPS profiles, JSON Home, and OPTIONS — no prior knowledge beyond the entry URL.

| Issue | What | Prob (session) |
|---|---|---|
| [#286](https://github.com/frank-fs/frank/issues/286) | TransitionAlgebra + Abstractions package | ~88% |
| [#283](https://github.com/frank-fs/frank/issues/283) | frank-cli extract --format fsharp | ~78% |
| [#284](https://github.com/frank-fs/frank/issues/284) | Frank.Statecharts.Tools (MSBuild) | ~88% |
| [#298](https://github.com/frank-fs/frank/issues/298) | Generated types + Link/Allow headers | ~80% |
| [#299](https://github.com/frank-fs/frank/issues/299) | JSON Home entry point | ~90% |
| [#272](https://github.com/frank-fs/frank/issues/272) | ALPS profile endpoint | ~90% |
| [#300](https://github.com/frank-fs/frank/issues/300) | OPTIONS discovery | ~88% |
| [#301](https://github.com/frank-fs/frank/issues/301) | Naive client e2e test | ~75% |

Note: #286 → #283 → #284 → #298 is a sequential chain (algebra → codegen → MSBuild → wiring). #299, #272, #300 are parallel. #301 depends on all.

**Track B — Reactive streaming agent (Datastar/CQRS + SSE)**

Server receives async events (webhooks, background jobs), defers events the current state can't handle, and pushes state changes to clients via Server-Sent Events. Proven via TicTacToe (existing Datastar SSE pattern) extended with deferred events.

| Issue | What | Prob (session) |
|---|---|---|
| [#263](https://github.com/frank-fs/frank/issues/263) | Deferred events | ~70% |

In classic HTTP, the client IS the event queue (retry on 400/405). In Datastar/CQRS, the server receives events from multiple sources and the client is passive. Deferred events prevent event loss when events arrive out of order.

**Track C — Concurrent multi-role agents (MPST + broadcast + role-projected SSE)**

Multiple agents (different MPST roles) connected simultaneously via SSE. When one role advances a region in an AND-state composite, inter-region broadcast fires, and each connected agent receives their role-projected affordance update in real time.

| Issue | What | Prob (session) |
|---|---|---|
| [#304](https://github.com/frank-fs/frank/issues/304) | AND-state event broadcast | ~75% |
| [#305](https://github.com/frank-fs/frank/issues/305) | Datastar multi-region SSE push | ~82% |
| [#306](https://github.com/frank-fs/frank/issues/306) | Role-projected concurrent SSE | ~68% |

This is the strongest thesis proof: Warehouse completes pick, Pack region auto-advances, Customer sees fulfillment progress, ShippingProvider sees nothing (their region is unchanged) — all via role-projected SSE streams, all from a single state change.

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

**Remaining risk**: Design decisions now resolved and consolidated in [DECISIONS.md](DECISIONS.md). Contradictions between design and implementation documented in [AUDIT.md](AUDIT.md).

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

Each layer works independently. The thesis can be proven at Layer 3. Higher layers strengthen guarantees.

```
Layer 0: ASP.NET Core          (endpoints, middleware, DI)
Layer 1: resource/statefulResource CE   (existing, working)
Layer 2: Discovery middleware   (useJsonHome, useOptionsDiscovery — existing, needs wiring)
Layer 3: Affordance middleware  (useAffordances from generated types — existing, needs wiring)
─────────────── thesis provable above this line ───────────────
Layer 4: Interpreter algebra    (opt-in formalization of HierarchicalRuntime)
Layer 5: Typed codegen          (opt-in DX, replaces string-based model.bin)
Layer 6: Analyzers              (opt-in compile-time validation)
Layer 7: Transition CE          (opt-in hand-authoring path)
```

Layers 0-3 are the thesis. Layers 4-7 exist because v7.3.0/v7.3.1 showed that without formal semantics (Layer 4), compile-time checks (Layer 6), and correct codegen (Layer 5), the lower layers silently produce wrong output that passes tests but fails agents.

## Related Documents

- [Semantic Resources](SEMANTIC-RESOURCES.md) — ALPS profiles, RDF, Schema.org alignment
- [Multi-Party Sessions](MULTI-PARTY_SESSIONS.md) — Projection, duality, progress analysis
- [Statecharts](STATECHARTS.md) — State machines, transitions, guards, affordance maps
- [Spec Pipeline](SPEC-PIPELINE.md) — Cross-format validation, artifact generation
- [Comparison](COMPARISON.md) — Frank vs. other approaches
