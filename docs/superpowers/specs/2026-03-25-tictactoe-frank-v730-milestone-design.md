# frank-v7.3.0 Milestone for frank-fs/tic-tac-toe

## Context

Frank v7.3.0 shipped with stateful resources, affordance middleware, discovery bundle (OPTIONS + Link + JSON Home), ALPS profiles with role projection, a CLI pipeline, and LinkedData support. The tic-tac-toe reference app is still on Frank 7.1.0 with hand-rolled state management (MailboxProcessor actors). This milestone upgrades the app to 7.3.0, replaces bespoke state plumbing with Frank.Statecharts, adds discovery and ontology features, and runs a series of agent experiments to prove Frank's HATEOAS thesis.

**Thesis under test:** A web application built with HATEOAS, statecharts, and semantic discovery is self-describing enough that an agent with no prior knowledge can discover and play a game using only server-provided information — and the same server design that enables agentic play also produces correct-by-construction human interfaces.

**Headline metric:** Beginner agent success rate at L2 (server-side discovery only, no system prompt, no MCP tools). If >80%, the server is self-describing enough. If <50%, the discovery surface needs more work.

**Secondary metric:** Chaos agent rejection rate — what percentage of adversarial probes are correctly handled by statechart guards without custom error handling code? This measures "pit of success" quantitatively.

**Three audiences for results:**
1. **Academic** — Researchers in HATEOAS, statechart theory, multi-party session types, and semantic web. The experiment produces evidence for or against the composability of these ideas. Blog posts and potential papers target this audience directly, using proper terminology (ALPS, RFC 8288, MPST).
2. **Developer** — Practitioners building web APIs. The reference app demonstrates that `useDiscovery` and `statefulResource` CEs hide the academic machinery behind declarative configuration. Developer-facing blog posts translate findings into practical guidance.
3. **Popular** — Broader audience interested in how the web works. Later blog posts frame the results as "what if your API could teach clients how to use it?" without requiring background knowledge.

**Two discovery surfaces:**
- **HTML for humans** — Clickable squares, conditional controls, visual turn indicators. Already working in the current app. The statechart-driven affordances formalize what the UI already does.
- **HTTP for agents** — Allow headers, Link headers, ALPS profiles, JSON Home. This milestone adds these. The same statechart metadata drives both surfaces.

**Constraints:**
- TicTacToe.Engine is NOT touched. The core game logic stays as-is.
- All work lives in frank-fs/tic-tac-toe
- Experiment branches are orthogonal: level branches (L0-L4) configure discovery features; persona scripts (beginner, expert, chaos) run against any level

## Milestone: `frank-v7.3.0` on frank-fs/tic-tac-toe

### Issue Dependency Graph

```
[1: Upgrade] → [2: Discovery] → [3: Statecharts] → [4: Protocol Tests]
                                        ↓
                               [5: Ontology/ALPS] → [6: CLI Build]
                                        ↓
                               [7: Agent Auth] → [8: Persona Framework]
                                                        ↓
                                               [9: Level Branches] → [10: Run Experiments]
```

Issues 2-6 can partially parallelize (discovery and ontology don't strictly depend on statecharts), but the natural authoring order is sequential.

---

### Issue 1: Upgrade Frank packages to 7.3.0

**Labels:** `upgrade`, `foundation`

Update all Frank package references from 7.1.0 to 7.3.0:
- `Frank` 7.1.0 → 7.3.0
- `Frank.Datastar` 7.1.0 → 7.3.0
- `Frank.Auth` 7.1.0 → 7.3.0
- Add new references: `Frank.Discovery`, `Frank.Statecharts`, `Frank.Cli.MSBuild`

**Acceptance criteria:**
- `dotnet build` succeeds
- All existing Playwright tests pass unchanged
- No behavior changes — pure dependency upgrade

**Branch:** Work on `main` via PR

---

### Issue 2: Add discovery middleware

**Labels:** `discovery`, `foundation`
**Depends on:** #1

Add `useDiscovery` to the WebHostBuilder to enable OPTIONS, Link headers, and JSON Home.

**Changes to `Program.fs`:**
- Add `useDiscovery` CE operation (bundles OPTIONS + Link + JSON Home)
- JSON Home document served at `GET /` via content negotiation (`Accept: application/json-home`)

**Expected behavior:**
- `OPTIONS /games/{id}` → `Allow: GET, POST, DELETE` (during active play), `Allow: GET, DELETE` (after game ends)
- `GET /games/{id}` → Link headers with `rel="profile"` pointing to ALPS
- `GET /` with `Accept: application/json-home` → JSON Home document listing all resources

**Acceptance criteria:**
- Discovery responses verified manually or via curl
- No regression in existing Playwright tests

**Branch:** Work on `main` via PR

---

### Issue 3: Adopt Frank.Statecharts for state-dependent routing and affordances

**Labels:** `statecharts`, `foundation`, `refactor`
**Depends on:** #1
**Supersedes:** tic-tac-toe#12

Replace hand-rolled state management in TicTacToe.Web with Frank.Statecharts infrastructure. The Engine stays untouched.

**Design requirement:** This issue needs its own standalone design document before implementation, because it involves three non-trivial integration challenges (see below). The issue should be scoped as "design + implement" with the design phase producing a short doc resolving all three.

#### Challenge 1: MoveResult → GamePhase mapping

The Engine's `MoveResult` is a parameterized DU (`XTurn of GameState * ValidMovesForX[]`). It cannot be used directly as statechart state. An intermediate state DU is needed:

```fsharp
// New type in TicTacToe.Web (NOT in Engine)
type GamePhase = XTurn | OTurn | Won | Draw | Error

let toGamePhase (result: MoveResult) : GamePhase =
    match result with
    | MoveResult.XTurn _ -> GamePhase.XTurn
    | MoveResult.OTurn _ -> GamePhase.OTurn
    | MoveResult.Won _   -> GamePhase.Won
    | MoveResult.Draw _  -> GamePhase.Draw
    | MoveResult.Error _ -> GamePhase.Error
```

The state machine metadata maps `GamePhase` (not `MoveResult`) to statechart states. The `Error` case must be handled (likely Allow GET only, same as terminal states).

#### Challenge 2: GameSupervisor lifecycle ≠ state store

`GameSupervisor` manages game *lifecycles* (create by ID, lookup by ID, count active games, dispose), not just state. `IStateMachineStore` only manages per-instance state. Two options:

- **Option A:** Keep a slimmed-down `GameSupervisor` as a game registry (create/lookup/count/dispose). It delegates state tracking to the statechart store but retains lifecycle ownership. `PlayerAssignmentManager` is replaced by statechart guards.
- **Option B:** Build a `GameRegistry` adapter that wraps both the Engine's `Game` actor and the statechart store, exposing a unified interface for lifecycle + state.

The design phase must choose one. Either way, the Engine's `Game` MailboxProcessor actor stays — it IS the game instance.

#### Challenge 3: SSE/Datastar subscription bridge

The current architecture has `Game : IObservable<MoveResult>` driving SSE broadcasts via `SseBroadcast.fs`. The statechart store has its own subscription mechanism. The design must specify how these integrate:

- State changes from Engine (`IObservable<MoveResult>`) must update the statechart store's `GamePhase`
- SSE broadcasts must continue to receive full `MoveResult` (they need `GameState` data for rendering, not just phase)
- The `broadcastPerRole` pattern (per-user rendering of clickable squares) must survive

Likely approach: Engine's `Game.Subscribe` remains the SSE source. A separate observer updates the statechart store's state key whenever `MoveResult` changes. The two subscriptions are independent.

#### Role predicates

Player assignment is dynamic and per-game (managed by `PlayerAssignmentManager` today). Static claim checks like `user.HasClaim("player", "X")` won't work. Role predicates need access to the game instance's assignment state — likely via a service lookup or the game registry.

#### What gets replaced:
- `PlayerAssignmentManager` move validation → Statechart guards with role definitions
- Manual state checking in handlers → `inState` blocks for state-dependent routing
- `resource` CE → `statefulResource` CE
- `GameSupervisor` is slimmed down or replaced by a lighter registry (design decides)

**State machine metadata** maps `GamePhase` to statechart states:
- `XTurn` → Allow GET, POST, DELETE (POST guarded to PlayerX role)
- `OTurn` → Allow GET, POST, DELETE (POST guarded to PlayerO role)
- `Won` → Allow GET, DELETE
- `Draw` → Allow GET, DELETE
- `Error` → Allow GET only

**Affordance middleware:**
- `useAffordancesWith` injects pre-computed Allow + Link headers per state
- `IStatechartFeature` set by state key resolver (reads Engine state via `toGamePhase`, maps to state key)
- Zero per-request allocation for header injection

**Datastar compatibility note:** Verify that `statefulResource` CE supports the `datastar` operation for the `/sse` endpoint. If `StatefulResourceBuilder` doesn't wrap `ResourceBuilder`'s Datastar operations, the SSE endpoint may need to remain a plain `resource`.

**Acceptance criteria:**
- All existing Playwright tests pass (behavior unchanged)
- Design doc resolves all three challenges before code starts
- Statechart guards reject invalid moves with 403/405/409 automatically
- Affordance headers injected correctly per game state (including DELETE where applicable)
- SSE broadcasts continue working with per-role rendering

**Branch:** Work on `main` via PR

---

### Issue 4: Protocol-level test coverage

**Labels:** `testing`, `foundation`
**Depends on:** #2, #3

Add tests verifying Frank 7.3.0 protocol features that the existing Playwright tests don't cover.

**Test cases (Expecto + TestHost or Playwright HTTP assertions):**
- `OPTIONS /games/{id}` returns correct `Allow` header per game state
- `GET /games/{id}` includes `Link` header with `rel="profile"`
- `GET /` with `Accept: application/json-home` returns valid JSON Home
- `GET /` without JSON Home accept header returns normal HTML
- Link header changes when game state transitions (e.g., POST move → GET shows updated Allow)
- 405 Method Not Allowed when POSTing to a finished game
- 403 Forbidden when wrong player attempts a move
- ALPS profile endpoint returns valid ALPS JSON

**Acceptance criteria:**
- All new tests pass
- Tests are reproducible in CI

**Branch:** Work on `main` via PR

---

### Issue 5: Reference external game ontology + ALPS profiles

**Labels:** `ontology`, `linked-data`, `foundation`
**Depends on:** #2

Research and reference existing external ontologies for game terms.

**Research targets:**
- [schema.org/Game](https://schema.org/Game) — `Game`, `GamePlayMode`, `player`, `numberOfPlayers`
- [schema.org/Action](https://schema.org/Action) — `MoveAction`, `agent`, `object`, `result`
- [Game Ontology Project (GOP)](http://gamestudies.org/0802/articles/zagal_mateas_fernandez_hochhalter_lichti) if available as linked data
- DBpedia/Wikidata: `wd:Q36801` (tic-tac-toe), `wd:Q11410` (board game)

**Deliverable:**
- Minimal ALPS profile for tic-tac-toe referencing external vocabulary URIs
- Terms like "player", "move", "board", "turn", "win", "draw" link to established vocabulary
- ALPS profile references schema.org where possible, avoids inventing new terms
- Profile served at `/alps/tictactoe`

**ALPS endpoint note:** Frank.Discovery serves Link headers *pointing to* ALPS profiles but does not auto-serve the profile content itself. The app needs an explicit resource (or static file endpoint) at `/alps/tictactoe` that returns the ALPS JSON. This may be handled by `Frank.Cli.MSBuild` artifact serving (check in #6) or via a manual resource.

**Acceptance criteria:**
- ALPS profile is valid JSON
- External vocabulary URIs resolve to real definitions
- Profile is minimal — only terms the app actually uses
- `GET /alps/tictactoe` returns the ALPS profile document
- Discoverable via Link headers on game resources

**Branch:** Work on `main` via PR

---

### Issue 6: CLI build integration

**Labels:** `cli`, `tooling`, `foundation`
**Depends on:** #3, #5

Integrate the Frank CLI pipeline into the build process.

**Changes:**
- Add `Frank.Cli.MSBuild` package reference
- Create or adapt game spec file (smcat or SCXML) from existing `docs/statechart.scxml`
- Wire `frank extract` + `frank compile` as MSBuild targets
- Generated `model.bin` embedded as assembly resource
- Switch from `useAffordancesWith gameAffordanceMap` to `useAffordances` (auto-load from embedded resource)

**Verification:**
- `frank validate` passes (cross-format consistency between spec and runtime)
- `frank project --base-uri http://localhost:5228` generates per-role ALPS profiles
- Build produces embedded `model.bin`

**Acceptance criteria:**
- `dotnet build` runs CLI extraction automatically
- Affordances auto-loaded from embedded resource
- All tests pass

**Branch:** Work on `main` via PR

---

### Issue 7: Agent authentication scaffold

**Labels:** `auth`, `agent`, `foundation`
**Depends on:** #1

Extend authentication to support machine players via `X-Agent-Id` header.

**Changes:**
- Extend `GameUserClaimsTransformation` to check for `X-Agent-Id` header
- If present, use header value as user identity instead of generating cookie-based GUID
- Agent identity flows through same player assignment mechanism (statechart guards after #3)
- No special treatment — agents are HTTP clients with identity

**Acceptance criteria:**
- Agent with `X-Agent-Id: agent-beginner-1` gets assigned to a player slot
- Agent can make moves via `POST /games/{id}` with form data
- Agent rejected from full game (both slots taken) gets 403
- Existing cookie-based auth still works for human players

**Branch:** Work on `main` via PR

---

### Issue 8: Agent persona framework

**Labels:** `experiment`, `agent`
**Depends on:** #7

Create agent persona definitions and orchestrator infrastructure in `experiments/` directory.

**Personas (as prompt/config files):**

1. **`beginner.md`** — No game knowledge. Discovers rules through ALPS descriptors, affordance headers, and HTML content. Explores cautiously. Expected: high discovery time, some invalid moves early, eventually learns.

2. **`expert.md`** — Knows tic-tac-toe strategy (center first, corners, forks). Focuses on optimal play through available moves. Expected: fast discovery, minimal invalid moves, strong play.

3. **`chaos.md`** — Adversarial. Probes for:
   - Invalid moves (occupied squares, out-of-turn)
   - Malformed requests (bad content types, missing fields, extra fields)
   - Race conditions (concurrent move attempts)
   - Header injection attempts
   - Moves after game over
   - Playing as wrong player
   - Expected: many 4xx responses, findings feed back as robustness improvements

**Orchestrator:**
- Script that starts server, runs persona against it, captures full HTTP transcript
- Results format: JSON log with `{ request, response, decision_reasoning, outcome }`
- Configurable: which persona, which server URL, how many games

**Acceptance criteria:**
- Each persona file defines clear goals, constraints, and success metrics
- Orchestrator can run any persona against a running server
- Results captured in structured format

**Branch:** Work on `main` via PR

---

### Issue 9: Five-level discovery experiment branches

**Labels:** `experiment`, `discovery`
**Depends on:** #6, #8

Create experiment branches off `main`, each configuring a different discovery level.

**Branches:**

| Branch | Server Changes | Agent-Side Changes | What Agent Sees |
|--------|---------------|-------------------|-----------------|
| `exp/L0-raw-http` | Strip discovery middleware | None | URL + raw HTTP status codes + response bodies only |
| `exp/L1-html-links` | Add `<link>` tags in HTML responses | None | HTML with `<link rel="alps" href="/alps/tictactoe">` |
| `exp/L2-welcome-page` | Add HTML welcome page at `/` | None | Human-readable explanation of available resources + links |
| `exp/L3-system-prompt` | None (server same as L2) | 3-sentence system prompt provided to agent | Same server as L2, but agent has initial guidance |
| `exp/L4-mcp-tool` | None (server same as L2) | MCP tool server for mechanical header parsing | Same server as L2, but agent has tooling for structured HTTP parsing |

**Key distinction:** L0-L2 are server-side configuration changes. L3-L4 are agent-side interventions (same server as L2, different agent capabilities). This is intentional — it measures whether the bottleneck is server discoverability or agent reasoning.

**Each branch:**
- Forks from completed `main` (all foundation issues merged)
- Server-side branches modify discovery configuration only — game logic and handlers identical
- Agent-side branches add files to `experiments/` only — no server changes
- Includes a `LEVEL.md` describing what's enabled and why

**Acceptance criteria:**
- Each branch builds and tests pass
- Discovery features match the level specification
- Levels are cumulative where sensible (L2 includes L1's links, etc.)

**Branch:** 5 long-lived experiment branches off `main`

---

### Issue 10: Run experiments and capture results

**Labels:** `experiment`, `results`
**Depends on:** #9

Execute persona × level matrix and capture results for blog material.

**Experiment matrix:**

| | L0 | L1 | L2 | L3 | L4 |
|---|---|---|---|---|---|
| **Beginner** | ? | ? | ? | ? | ? |
| **Expert** | ? | ? | ? | ? | ? |
| **Chaos** | ? | ? | ? | ? | ? |

**Metrics per cell:**
- Discovery success rate (found game, understood rules, made valid first move)
- Game completion rate (finished a full game)
- Invalid request rate (4xx responses / total requests)
- Time to first valid move (request count)
- For chaos: unique vulnerability classes discovered

**Headline results to extract:**
- **Thesis metric:** Beginner success rate at L2. This is the primary evidence for/against self-describing APIs.
- **Pit of success metric:** Chaos rejection rate — percentage of adversarial probes correctly handled by framework guards (not custom code).
- **Inflection point:** At which level does beginner success rate cross 80%? If L2, server wins. If L3/L4, agents need help.

**Deliverables:**
- Completed matrix with metrics
- Annotated transcripts (3-5 most interesting runs, selected for different audiences)
- Chaos agent findings as bug reports or hardening recommendations
- Blog series material targeting three audiences:
  - Academic: "Composing HATEOAS, Statecharts, and ALPS for Self-Describing APIs" — full experiment design, methodology, evidence
  - Developer: "Your API Can Teach Clients How to Use It" — practical guide using Frank + tic-tac-toe results
  - Popular: "What Happens When AI Agents Discover a Game Through HTTP Alone?" — narrative-driven, results-focused

**Acceptance criteria:**
- All 15 cells executed (3 personas x 5 levels)
- Results reproducible via orchestrator script
- At least 3 annotated transcripts ready for blog
- Headline metrics (L2 beginner success, chaos rejection rate) computed and documented

---

## Recommendation Issues (NOT in `frank-v7.3.0` milestone)

These are created as standalone issues on frank-fs/tic-tac-toe, unassigned to any milestone. They capture improvement opportunities discovered during planning.

**Labels for all:** `enhancement`, `recommendation`

### Rec Issue A: Unify available moves into typed data on MoveResult

The Engine's `MoveResult` carries available moves as separate array types (`ValidMovesForX[]`, `ValidMovesForO[]`). A unified typed representation would simplify affordance projection — the statechart layer wouldn't need to pattern-match on the DU case to extract which moves are legal.

**Impact:** Simplifies Issue 3 (statechart adoption) by giving the `GamePhase` mapper direct access to legal moves without knowing whose turn it is.

### Rec Issue B: Add async Game.GetStateAsync()

`Game.GetState()` is synchronous (reads MailboxProcessor state directly). An async variant would compose better with ASP.NET Core middleware pipelines and avoid blocking the thread pool on state reads.

**Impact:** Would allow the state key resolver middleware (Issue 3) to use `task { }` natively instead of wrapping a sync call.

### Rec Issue C: Expose typed event stream from Game actor

The `Game : IObservable<MoveResult>` pattern works well for SSE broadcasting, but a typed event stream (e.g., `IObservable<GameEvent>` with `MoveMade | GameReset | GameDisposed` cases) would enable richer statechart integration and more precise SSE event types.

**Impact:** Would simplify the SSE/Datastar subscription bridge in Issue 3 (Challenge 3) by providing event-level granularity rather than full-state snapshots.

### Rec Issue D: Add JSON response format with embedded hypermedia

Alongside HTML responses, a JSON representation using HAL, Siren, or JSON:API would let agents parse responses without HTML scraping. This is particularly relevant for the L0/L1 experiment levels where agents have minimal discovery support.

**Impact:** Could significantly improve agent success rates at lower discovery levels. The beginner agent at L0 would have structured data to work with instead of HTML parsing.

## Verification

After all foundation issues (1-7) are merged to `main`:
1. `dotnet build TicTacToe.sln` succeeds
2. `dotnet test` — all Playwright tests pass
3. `curl -H "Accept: application/json-home" http://localhost:5228/` returns JSON Home
4. `curl -X OPTIONS http://localhost:5228/games/{id}` returns Allow header
5. `frank validate` passes (spec ↔ runtime consistency)
6. Agent auth: `curl -H "X-Agent-Id: test-agent" http://localhost:5228/games` works

After experiment branches (8-10):
7. Orchestrator runs beginner persona against L2 branch successfully
8. Chaos persona generates at least one actionable finding
9. Results matrix has data for all 15 cells
