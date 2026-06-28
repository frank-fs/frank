# Capstone Discovery Completion (#333) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the two red capstone E2E tests green — AT-S4 (illegal move → 422 SHACL) and AT-S6 (naive client plays a full game via discovery, incl. discovering the move request shape) — and add AT-S7 (vocab-swap negative) and AT-S8 (provenance verify-result via the discovered `has_provenance` link), then gate the E2E in CI.

**Architecture:** Close an *implementation gap* against the existing design (spec 2026-04-20 §6 / #333). The discovery substrate already works (AT-S1/S2/S3/S5 green). The fix is: (1) model the move **request** as a mapped type (`MoveRequest`) so its fields flow into the *existing* ALPS field-descriptor + SHACL-shape generators (no Frank.Discovery code change — investigation-confirmed), (2) wire `useValidation` into the sample, (3) make the move handler accept a ld+json `MoveRequest`, (4) complete the full-game navigation, negative, and provenance ATs, (5) wire the E2E into sln + CI.

**Tech Stack:** F# (net10.0 sample + E2E), ASP.NET Core, Frank.{Discovery,Validation,LinkedData,Provenance,Semantic}, `frank semantic` CLI + MSBuild codegen, NUnit + Playwright APIRequestContext (the existing E2E harness), dotNetRdf.

## Global Constraints

- **Build:** `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build …` (ICU mismatch on nix-darwin).
- **Worktree:** all work in `.claude/worktrees/capstone-discovery` (branch `capstone-discovery-completion`). Bash cwd resets to master between calls — **absolute worktree paths in every command**.
- **The E2E is the truth (STUBS.md discipline):** never stub the Playwright AT layer. Make deeper layers real until each AT is green with no `FRANK-STUB(AT-Sx)` remaining.
- **MSBuild task DLL caching:** run `dotnet build-server shutdown` before rebuilding the sample after any `Frank.Cli.MSBuild`/codegen change (src/CLAUDE.md).
- **Codegen MUST use Fabulous.AST** (no string concat) — but this plan adds **no emitter code**; it only changes the sample's model + lock so existing emitters produce more.
- **No hardcoded machine paths in tests** (the CI bug just fixed: use `Path.GetTempPath()`, not scratchpad paths).
- **Fantomas** must pass on changed `src/` before commits; the pre-commit hook enforces it.
- **Discovery-only constraint (AT-S6/S7):** the naive client may NOT hardcode URLs, state names, or message constructors — it reads them from JSON Home / ALPS / Link headers / response bodies only.
- Commit footer on every commit:
  ```
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01P5EphcEDpZQMv2A3roMkfh
  ```

---

## File Structure

| File | Change | Responsibility |
|------|--------|----------------|
| `sample/TicTacToe-v732/Model.fs(i)` | modify | add `MoveRequest` record (`Position: SquarePosition; Player: Player`) |
| `sample/TicTacToe-v732/Vocabulary.fs` | modify | declare `MoveRequest` alignment if the vocab CE drives it (prefixes/using already present) |
| `sample/TicTacToe-v732/.frank/semantic-mappings.lock.json` | regenerate | add the `MoveRequest` mapping (class IRI + `Position`/`Player` field IRIs) via the `frank semantic` pipeline |
| `sample/TicTacToe-v732/Program.fs` | modify | `useValidation`; move handler accepts ld+json `MoveRequest`; `accepts typeof<MoveRequest>` on the moves resource |
| `sample/TicTacToe-v732.E2E/SemanticTests.fs` | modify | AT-S6 turn green; add AT-S7 (vocab-swap), AT-S8 (provenance verify) |
| `sample/TicTacToe-v732.E2E/ServerFixture.fs` | maybe | second server config for the `ex:` vocab variant (AT-S7) |
| `Frank.sln` + `.github/workflows/ci.yml` | modify | add the E2E project to sln + CI |

**Read before starting:** `sample/TicTacToe-v732.E2E/SemanticTests.fs` (the AT harness), `sample/TicTacToe-v732/Program.fs` (current handler + webHost), `sample/TicTacToe-v732/Vocabulary.fs`, `sample/TicTacToe-v732/.frank/semantic-mappings.lock.json`, `src/Frank.Cli.Core/DiscoveryEmitter.fs` (descriptors come from `ResolvedModel` fields), `src/Frank.OpenApi/HandlerBuilder.fs` (`accepts`), `src/Frank.Validation/Frank.Validation.fs` (`useValidation`), `src/Frank.Cli/` (the `frank semantic` commands).

---

## Task 1 (SPIKE): Model `MoveRequest` into the sample's semantic artifacts

**Goal of the spike:** make `position` and `agent` (player) appear as schema.org-aligned descriptors in the generated ALPS profile AND as properties in the generated SHACL shape, by adding a mapped `MoveRequest` type. This is a spike because the exact `frank semantic` CLI invocation + lock-entry shape must be determined against the live tooling — do that first, then lock it into the steps below.

**Files:** `sample/TicTacToe-v732/Model.fs(i)`, `.frank/semantic-mappings.lock.json`, possibly `Vocabulary.fs`.

- [ ] **Step 1: Add the request type.** In `Model.fs` (and signature in `Model.fsi`), add:
  ```fsharp
  /// The wire shape of a move request — modeled so discovery/validation can describe it.
  type MoveRequest = { Position: SquarePosition; Player: Player }
  ```
  Place it after `Player`/`SquarePosition` are defined.

- [ ] **Step 2: Determine the semantic-pipeline commands.** Run the CLI help to learn the extract/accept/finalize flow:
  ```bash
  cd /Users/ryanr/Code/frank/.claude/worktrees/capstone-discovery
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --project src/Frank.Cli -- semantic --help 2>&1 | tail -40
  ```
  Identify the command(s) that (a) extract candidate mappings for new types, (b) accept/confirm a mapping. Record them in this task before proceeding.

- [ ] **Step 3: Map `MoveRequest`.** Get a `confirmed` lock entry for `TicTacToe.Model.MoveRequest` with `iri` = a schema.org class (e.g. `schema:MoveAction` reuse, or `schema:PlayAction`) AND field mappings `Position → schema:<position-ish>` (the lock already uses `schema:namedPosition` for SquarePosition payloads — reuse it) and `Player → schema:agent` (the lock already maps `agent → schema:agent`). Prefer the CLI accept path; hand-edit the lock only if the CLI cannot target a specific type, and if hand-edited, set `source: "manual"`, `status: "confirmed"`, `confidence: 1`.

- [ ] **Step 4: Regenerate + verify the artifacts contain the fields.**
  ```bash
  cd /Users/ryanr/Code/frank/.claude/worktrees/capstone-discovery
  dotnet build-server shutdown
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build sample/TicTacToe-v732/TicTacToe.v732.fsproj
  # ALPS descriptors now include the request fields:
  grep -E "position|namedPosition|agent" sample/TicTacToe-v732/obj/Debug/net10.0/GeneratedDiscovery.fs
  # SHACL shape now covers MoveRequest:
  grep -E "MoveRequest|MoveAction|PlayAction" sample/TicTacToe-v732/obj/Debug/net10.0/GeneratedValidation.fs
  ```
  Expected: the position + agent IRIs appear in `GeneratedDiscovery.fs`; a MoveRequest shape appears in `GeneratedValidation.fs`. If they do not, the lock mapping is wrong — fix and re-run.

- [ ] **Step 5: Lock-gate sanity.** `dotnet build` must not fail the lock gate (no `proposed`/`unresolved` entries introduced).

- [ ] **Step 6: Commit.**
  ```bash
  git add sample/TicTacToe-v732/Model.fs sample/TicTacToe-v732/Model.fsi sample/TicTacToe-v732/.frank/semantic-mappings.lock.json sample/TicTacToe-v732/Vocabulary.fs
  git commit -m "feat(capstone): #333 model MoveRequest so its fields generate ALPS+SHACL"
  ```

> **Reviewer gate:** confirm — by reading the generated `obj/.../GeneratedDiscovery.fs` and `GeneratedValidation.fs` yourself — that `position`/`agent` are really present. This is the load-bearing precondition for AT-S6.

---

## Task 2: Move handler accepts a ld+json `MoveRequest`; declare `accepts`

**Files:** `sample/TicTacToe-v732/Program.fs`.

**Interfaces:**
- Consumes: `MoveRequest` (Task 1), `Frank.OpenApi` `handler { … accepts typeof<…> }`, the existing `moveHandler`.
- Produces: a moves resource whose POST consumes ld+json `MoveRequest`, declares `accepts typeof<MoveRequest>`, and still `produces typeof<Move> 200` (from the provenance work).

- [ ] **Step 1:** Read the current `moveHandler` (it parses `doc["position"]`/`doc["player"]` from a JSON body) and the moves resource block in `Program.fs`.

- [ ] **Step 2:** Make the handler parse a ld+json (or json) body whose fields use the mapped property names. Keep backward-compatible parsing of `position`/`player` (the validation middleware validates the ld+json body upstream; the handler still reads the values). No behavior change beyond accepting `application/ld+json`.

- [ ] **Step 3:** Add `accepts typeof<MoveRequest>` to the moves resource handler CE (alongside `produces typeof<Move> 200`):
  ```fsharp
  post (handler {
      handle moveHandler
      accepts typeof<MoveRequest>
      produces typeof<Move> 200
  })
  ```

- [ ] **Step 4:** Build the sample, confirm it compiles. (`dotnet build-server shutdown` first.)

- [ ] **Step 5: Commit** — `git commit -m "feat(capstone): #333 moves resource accepts ld+json MoveRequest"`.

---

## Task 3: Wire `useValidation` → AT-S4 green

**Files:** `sample/TicTacToe-v732/Program.fs`; verify via `SemanticTests.fs` AT-S4.

- [ ] **Step 1: Confirm AT-S4 red** (baseline):
  ```bash
  cd /Users/ryanr/Code/frank/.claude/worktrees/capstone-discovery
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test sample/TicTacToe-v732.E2E/TicTacToe.v732.E2E.fsproj --filter "AT-S4" 2>&1 | tail -5
  ```
  Expected: FAIL (no 422 — validation not wired).

- [ ] **Step 2:** Add `useValidation` to the `webHost { }` block in `Program.fs` (the sample already references `Frank.Validation` and generates the shape). Order: validation validates the request body; place it so it runs before the handler. Per the post-#7 composition note, no strict outermost ordering is required, but keep `useProvenance` early; a reasonable order: `useProvenance; useValidation; useDiscovery; useLinkedData`.

- [ ] **Step 3:** Rebuild + run AT-S4:
  ```bash
  dotnet build-server shutdown
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test sample/TicTacToe-v732.E2E/TicTacToe.v732.E2E.fsproj --filter "AT-S4" 2>&1 | tail -5
  ```
  Expected: PASS (invalid move → 422 ValidationReport, no `urn:frank:`, cites schema.org). If still red, the SHACL shape for the move body isn't targeting the posted `@type` — verify the move body's `@type` matches the MoveRequest class IRI the shape targets.

- [ ] **Step 4: Commit** — `git commit -m "feat(capstone): #333 wire useValidation — AT-S4 green (illegal move 422)"`.

---

## Task 4: AT-S6 — naive client plays a full game via discovery

**Files:** `sample/TicTacToe-v732.E2E/SemanticTests.fs` (AT-S6 — likely needs the navigation completed now that the descriptors exist).

- [ ] **Step 1: Run AT-S6, capture the exact failure** (it currently dies at `failwith "ALPS missing position IRI"`, line ~208, then the SHACL-leg at ~267):
  ```bash
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test sample/TicTacToe-v732.E2E/TicTacToe.v732.E2E.fsproj --filter "AT-S6" 2>&1 | tail -25
  ```
  After Tasks 1–3, the `fieldIri "position"`/`"agent"` lookups should now resolve (descriptors exist) and the SHACL leg should 422. Re-run; the remaining failures (if any) are in the full-game turn loop.

- [ ] **Step 2: Complete the turn loop in AT-S6** if needed. The client must: discover game + moves URIs from JSON Home; read ALPS for the request fields; play a legal two-player game (X then O, alternating) by POSTing discovered move requests; detect terminal state via the affordance set (no available transitions / game-over in the response). It must NOT hardcode URLs/state-names/payload keys beyond what it reads from discovery. Use the existing helpers (`LinkRels`, JSON Home template expansion, `fieldIri`). Read the current AT-S6 body fully (lines 144–267) and finish whatever step throws.

- [ ] **Step 3: Run AT-S6, confirm PASS.** Paste full output.

- [ ] **Step 4: Run the WHOLE SemanticTests suite** — AT-S1..S6 all green:
  ```bash
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test sample/TicTacToe-v732.E2E/TicTacToe.v732.E2E.fsproj --filter "SemanticTests" 2>&1 | tail -8
  ```
  Expected: 6/6 (was 4/6).

- [ ] **Step 5: Commit** — `git commit -m "feat(capstone): #333 AT-S6 — naive client plays a full game via discovery"`.

---

## Task 5: AT-S7 — vocab-swap negative (the falsifiability lever)

**Files:** `sample/TicTacToe-v732.E2E/SemanticTests.fs` (new AT-S7), `ServerFixture.fs` (a second server started with an `ex:` vocab variant).

**Approach:** stand up the sample configured with a *local* `ex:` vocabulary (`using "ex"`) so the generated artifacts emit `ex:` IRIs instead of `schema:`. Then:
- A client that **hardcoded** `schema.org` IRIs fails against the `ex:` server (the IRIs it expects are absent).
- The **discovery** client (the AT-S6 navigator, which reads IRIs from ALPS at runtime) still completes a game.

- [ ] **Step 1: Decide the `ex:` variant mechanism.** Options to investigate and pick: (a) a second sample project / config that sets `using "ex"` with a local `ex:` vocab + its own lock; or (b) an env/MSBuild switch on the existing sample. Mirror however the spec's "vocab swap" negative was intended (check `frank semantic` for a vocab override). Record the chosen mechanism.

- [ ] **Step 2: Write AT-S7 (failing first).** Two assertions in one test:
  - hardcoded-IRI probe: against the `ex:` server, asserting a `schema.org`-expecting access fails (e.g. the ALPS profile does NOT contain `schema.org/...`, it contains `ex:`/the local namespace).
  - discovery navigator: the same link-following client from AT-S6, pointed at the `ex:` server, still reads the (now `ex:`) IRIs from ALPS and completes a game.

- [ ] **Step 3: Run — confirm it fails for the right reason, then make it pass.** Paste both.

- [ ] **Step 4: Commit** — `git commit -m "test(capstone): #333 AT-S7 vocab-swap — hardcoded client breaks, discovery survives"`.

> If a clean `ex:` variant proves disproportionately large, STOP and surface to the user — AT-S7 is the load-bearing falsifiability test, so it is not optional, but the *mechanism* may warrant a scoping decision.

---

## Task 6: AT-S8 — provenance verify-result via discovery

**Files:** `sample/TicTacToe-v732.E2E/SemanticTests.fs` (new AT-S8).

**Approach (verify-result — the smaller path per the spec):** after AT-S6 completes a game, the client follows the **discovered** `Link: rel="http://www.w3.org/ns/prov#has_provenance"` header (emitted by `ProvenanceMiddleware` on pass-through responses) to the lineage, then confirms the recorded provenance matches the played game.

- [ ] **Step 1: Confirm the sample wires `useProvenance`** (it does — from the provenance vertical) and that move responses carry the `has_provenance` Link header. Quick check:
  ```bash
  # from a running sample: a normal GET /games/{id} response should carry
  # Link: <...>; rel="http://www.w3.org/ns/prov#has_provenance"
  ```

- [ ] **Step 2: Write AT-S8 (failing first).** The client: plays a short game (reuse the AT-S6 navigator), reads the `has_provenance` Link from a game response (NOT hardcoded), GETs the lineage, and asserts:
  - the lineage contains one `prov:Activity` per move played (count matches), each `wasAssociatedWith` an agent and typed `schema:MoveAction`;
  - **verify-result:** the lineage is consistent with the final state (e.g. the number/sequence of move Activities reproduces the moves; or, if a terminal outcome Activity is recorded, its outcome IRI matches the game's actual result). Pick the concrete, falsifiable assertion against the real lineage body.

- [ ] **Step 3: Run — fail then pass.** Paste.

- [ ] **Step 4: Commit** — `git commit -m "test(capstone): #333 AT-S8 provenance verify-result via discovered has_provenance link"`.

---

## Task 7: CI + sln integration

**Files:** `Frank.sln`, `.github/workflows/ci.yml`.

- [ ] **Step 1: Add the E2E project to `Frank.sln`:**
  ```bash
  cd /Users/ryanr/Code/frank/.claude/worktrees/capstone-discovery
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet sln Frank.sln add sample/TicTacToe-v732.E2E/TicTacToe.v732.E2E.fsproj
  ```

- [ ] **Step 2: Add to CI.** Append to `.github/workflows/ci.yml`'s Test step:
  ```yaml
          dotnet test sample/TicTacToe-v732.E2E/TicTacToe.v732.E2E.fsproj
  ```
  Confirm the Playwright **APIRequestContext** driver works in the CI runner (it needs the playwright driver, not browsers). If the E2E requires `playwright install` or a driver download, add the install step before the test (e.g. `pwsh sample/TicTacToe-v732.E2E/bin/.../playwright.ps1 install-deps` or the dotnet `Microsoft.Playwright.CLI`). Investigate against the ServerFixture/E2E setup; record the exact step.

- [ ] **Step 3: Verify the E2E runs from a clean build locally** (mimics CI: it `dotnet run`s the sample):
  ```bash
  dotnet build-server shutdown
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test sample/TicTacToe-v732.E2E/TicTacToe.v732.E2E.fsproj 2>&1 | tail -8
  ```
  Expected: all AT-S1..S8 green.

- [ ] **Step 4: Commit** — `git commit -m "build(capstone): #333 gate TicTacToe discovery E2E in sln + CI"`.

---

## Task 8: Full verification + review + stop-before-merge

- [ ] **Step 1:** Full solution build (`dotnet build Frank.sln -c Release`) → 0 errors.
- [ ] **Step 2:** Run the E2E suite yourself (absolute path); confirm AT-S1..S8 all green. Re-run — do not trust a single pass for a server-backed E2E (flake check).
- [ ] **Step 3:** `dotnet fantomas --check src/` clean (and the sample files you changed).
- [ ] **Step 4: `/discipline`** on changed sample/E2E files; fix Criticals/Highs.
- [ ] **Step 5: `/self-reflect`** against this plan + spec ATs — confirm AT-S4/S6/S7/S8 each have observed green evidence; hunt for weakened assertions (an E2E that asserts only status, not the discovery behavior) and any hardcoded-knowledge leak in the navigator that would make AT-S6/S7 vacuous.
- [ ] **Step 6: `/expert-review`** — Miller (discovery/ALPS request-shape, content negotiation), Tim Berners-Lee (are the request-field IRIs dereferenceable/consistent), Fielding (HATEOAS: is the client truly navigating affordances vs reconstructing URLs), Claude-agent (is this genuinely "no out-of-band knowledge"). Surface all findings to the user; do not triage.
- [ ] **Step 7: STOP — do not merge or push.** Present results + the AT table for merge approval. On approval: `--ff-only` into master, push, close #333 (audit every AC first), reopen nothing silently.

---

## Self-Review (plan vs spec)

**Spec coverage:**
- D1 useValidation → Task 3. ✓
- D2 model MoveRequest → Task 1. ✓
- D3 request shape discoverable (ALPS + SHACL, no Discovery feature — investigation-resolved) → Task 1 (descriptors) + Task 2/3 (accepts + validation). ✓
- D4 AT-S6 full game → Task 4. ✓
- D5 AT-S7 vocab swap → Task 5. ✓
- D6 CI + sln → Task 7. ✓
- D7 AT-S8 provenance verify-result → Task 6. ✓
- ACs AT-S1..S8 → Tasks 3/4 (S1-S6), 5 (S7), 6 (S8). ✓

**Placeholder scan:** Task 1 is a declared SPIKE (legitimate — it resolves the lock/CLI mechanics); Tasks 5 & 7 carry explicit "investigate the mechanism, then record it" steps (the `ex:` variant + Playwright-in-CI) — these are real unknowns the implementer must settle against the tooling, not hand-waved code. No "add error handling"/"TBD" placeholders.

**Type consistency:** `MoveRequest = { Position: SquarePosition; Player: Player }` defined in Task 1, consumed by Task 2 (`accepts typeof<MoveRequest>`). The discovered field names the client looks up are `position`/`agent` (per the existing AT-S6 `fieldIri` calls + the lock's `schema:agent` mapping) — Task 1 Step 3 must map `Player → schema:agent` so the ALPS descriptor name the client scans for (`agent`) is present.

**Open items the implementer must settle early (surface if blocked):**
1. Exact `frank semantic` command to map a single new type (Task 1 Step 2).
2. Whether the move body's `@type` must equal the MoveRequest class IRI for the SHACL shape to target it (Task 3 Step 3).
3. The `ex:` vocab-variant mechanism (Task 5 Step 1).
4. Playwright driver availability in CI (Task 7 Step 2).
