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

## Task 1 (SPIKE): Model `MoveRequest` with HONEST IRIs, produced via the `frank semantic` CLI clarify workflow

**Goal of the spike:** make the move request's two inputs appear as honestly-mapped descriptors in the generated ALPS profile AND as a SHACL shape, by adding a mapped `MoveRequest` type whose mapping is **produced through the CLI extract→clarify→accept pipeline** (the hand-written resolution simulating the human/LLM clarify decision), NOT by editing the lock JSON directly.

**Honest mappings (decided by meaning, not by test literals):**
- `MoveRequest` (type/class) → `schema:MoveAction`
- `Player` (field) → `schema:agent` (the load-bearing universal term)
- `Position` (field) → a **domain term** `ttt:square` (declare `prefix "ttt" "https://example.org/tictactoe#"`); there is no honest schema.org term for a board cell — the accept oracle is fail-open for the uncached `ttt:` namespace (`Accept.fs:266-271`), so a declared-prefix CURIE is permitted.

**Why a spike:** two live unknowns must be settled against the tooling and recorded here before the rest:
(U1) the `Move`↔`MoveRequest` shared-`schema:MoveAction` local-name collision in `ResolvedModel.checkLocalNameCollisions`; (U2) whether `accept` writes the new mapping with the exact field IRIs from the resolved template.

**This task SUPERSEDES the prior commit `732e12c5`** (which hand-edited the lock with the dishonest `schema:position`/`schema:Action`). Revert it first.

**Files:** `sample/TicTacToe-v732/Model.fs`, `Vocabulary.fs`, `.frank/semantic-mappings.lock.json` (written *by the CLI*), plus a scratch `resolved.json`.

- [ ] **Step 0: Revert the prior shortcut commit.**
  ```bash
  cd /Users/ryanr/Code/frank/.claude/worktrees/capstone-discovery && git revert --no-edit 732e12c5
  ```
  (Or `git reset` if it is still HEAD and nothing depends on it — confirm `git log` first.) Verify the lock no longer contains a `MoveRequest`/`schema:position` entry.

- [ ] **Step 1: Add the request type.** In `Model.fs`, after `Move`/`Game`, add:
  ```fsharp
  /// The wire shape of a move request — modeled so discovery/validation can describe it.
  type MoveRequest = { Position: SquarePosition; Player: Player }
  ```
  (There is currently no `Model.fsi` — do not create one. If the prior commit added one, the revert removes it.)

- [ ] **Step 2: Declare the domain prefix + the square constraint in `Vocabulary.fs`.** Inside the `vocabulary { … }` block add:
  ```fsharp
  prefix "ttt" "https://example.org/tictactoe#"
  constrainPattern typeof<MoveRequest> "Position" "^(TopLeft|TopCenter|TopRight|MiddleLeft|MiddleCenter|MiddleRight|BottomLeft|BottomCenter|BottomRight)$"
  ```
  (Confirm the op name `constrainPattern` and arg order in `VocabularyBuilder.fs:78`.) The pattern enumerates the legal squares — it is the self-describing value constraint that makes an out-of-range square 422.

- [ ] **Step 3: Run the CLI pipeline (this is the simulate-clarify workflow).**
  ```bash
  cd /Users/ryanr/Code/frank/.claude/worktrees/capstone-discovery
  # a) extract candidate mappings (MoveRequest now appears, convention-guessed)
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --project src/Frank.Cli -- semantic extract -p sample/TicTacToe-v732/TicTacToe.v732.fsproj -v sample/TicTacToe-v732/Vocabulary.fs
  # b) emit the resolution template (the LLM/human contract)
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --project src/Frank.Cli -- semantic clarify -p sample/TicTacToe-v732/TicTacToe.v732.fsproj --output-format resolved-template > /tmp/resolved.json
  ```
  Inspect `/tmp/resolved.json`. **Hand-write the resolution** for `TicTacToe.Model.MoveRequest` (this is the predetermined/simulated clarify decision): class `schema:MoveAction`, field `Position → ttt:square`, field `Player → schema:agent`. Keep the template's structure.

- [ ] **Step 4: Accept the resolution into the lock (CLI writes it — no manual JSON edit).**
  ```bash
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --project src/Frank.Cli -- semantic accept --input /tmp/resolved.json --source manual -p sample/TicTacToe-v732/TicTacToe.v732.fsproj
  # smoke-run the remaining CLI surface (coverage):
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet run --project src/Frank.Cli -- semantic status -p sample/TicTacToe-v732/TicTacToe.v732.fsproj
  ```
  Read the resulting lock entry and confirm the CLI wrote `schema:MoveAction` / `ttt:square` / `schema:agent` with `status: confirmed`. If `accept` rejected an IRI (term oracle), paste the verbatim error and STOP — do NOT swap to a test-pleasing IRI.

- [ ] **Step 5: Resolve the collision (U1).** Rebuild and watch for `checkLocalNameCollisions` ("share local name 'MoveAction'"):
  ```bash
  cd /Users/ryanr/Code/frank/.claude/worktrees/capstone-discovery && dotnet build-server shutdown && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build sample/TicTacToe-v732/TicTacToe.v732.fsproj 2>&1 | tail -25
  ```
  If it fires, resolve honestly and record which: (a) confirm `Move` does not separately need a class descriptor and exclude its class mapping, or (b) give the *recorded* move and the *request* distinct honest classes. Do NOT resolve it by renaming to a dishonest IRI. Re-run the build to green.

- [ ] **Step 6: Verify the generated artifacts (read them yourself).**
  ```bash
  cd /Users/ryanr/Code/frank/.claude/worktrees/capstone-discovery
  grep -nE "agent|square|example.org/tictactoe|MoveAction" sample/TicTacToe-v732/obj/Debug/net10.0/GeneratedDiscovery.fs
  grep -nE "schema.org/MoveAction|example.org/tictactoe#square|schema.org/agent|Pattern|RecordShape" sample/TicTacToe-v732/obj/Debug/net10.0/GeneratedValidation.fs
  ```
  EXPECTED: GeneratedDiscovery has field descriptors with hrefs `https://schema.org/agent` and `https://example.org/tictactoe#square`; GeneratedValidation has `RecordShape(System.Uri "https://schema.org/MoveAction", …)` whose props include path `…/agent` and path `…tictactoe#square` WITH `Pattern = Some "^(TopLeft|…)$"`.

- [ ] **Step 7: Lock-gate sanity** — the build must not fail the lock gate (no `proposed`/`unresolved`).

- [ ] **Step 8: Commit.**
  ```bash
  cd /Users/ryanr/Code/frank/.claude/worktrees/capstone-discovery && dotnet fantomas sample/TicTacToe-v732/Model.fs sample/TicTacToe-v732/Vocabulary.fs && git add sample/TicTacToe-v732/Model.fs sample/TicTacToe-v732/Vocabulary.fs sample/TicTacToe-v732/.frank/semantic-mappings.lock.json && git commit -m "feat(capstone): #333 model MoveRequest via CLI clarify — honest agent/ttt:square IRIs"
  ```

> **Reviewer gate:** confirm by reading the generated files yourself that the honest IRIs (`schema:agent`, `ttt:square`, `schema:MoveAction`) are present, that the lock was written **by the CLI** (not hand-edited), and record the U1/U2 resolutions. This is the load-bearing precondition for AT-S4/S6.

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
  Expected: PASS (invalid move → 422 ValidationReport, no `urn:frank:`, cites a vocabulary IRI). The SHACL shape targets `schema:MoveAction`, so the posted body MUST be `@type = https://schema.org/MoveAction`, agent key `https://schema.org/agent`, square key `https://example.org/tictactoe#square`, with an out-of-range value (`"NotASquare"`) tripping the `sh:pattern`. **Update the AT-S4 body in `SemanticTests.fs`** from the old `schema.org/Action`/`schema.org/position` literals to this honest contract. If still red, confirm the body `@type` equals the shape's targetClass and the pattern is present on the square property.

- [ ] **Step 4: Commit** — `git commit -m "feat(capstone): #333 wire useValidation — AT-S4 green (illegal move 422)"`.

---

## Task 4: AT-S6 (revised) — the agent-simulator: follow links, verify they resolve, verify the term set, play

**Files:** `sample/TicTacToe-v732.E2E/SemanticTests.fs` (AT-S6 rewritten — the current `fieldIri "position"` string-match is replaced).

**The anti-string-match rewrite.** The deterministic client is a *stand-in for an agent*. Passing must prove it read MEANING, not spelling. Rewrite AT-S6 to:

- [ ] **Step 1: Follow links and assert each resolves.** JSON Home → game/moves resources → OPTIONS → `Link rel=describedby` → ALPS. Assert the ALPS fetch returns 200 (the link "leads somewhere"), and that the field descriptor `href`s are well-formed absolute IRIs. Replace the `fieldIri name`-by-local-name helper with one that selects descriptors by their **absolute term IRI** (`href`).

- [ ] **Step 2: Verify the term set matches an EXPECTED SET.** Collect the ALPS descriptor term IRIs and assert the expected semantic set is present: `{ https://schema.org/MoveAction, https://schema.org/agent, https://example.org/tictactoe#square, https://schema.org/Game, https://schema.org/result }` (adjust to the actual generated set, but it MUST include the agent + square + class). A meaningless-but-named mapping would lack these IRIs → fail.

- [ ] **Step 3: Dereference EVERY URI received (D-deref).** Add a helper the client applies to every URI it encounters — resource links, the ALPS profile URL, each field/class term IRI, `seeAlso` targets, and (in Task 6) the lineage URL — that GETs/HEADs the URI and asserts it resolves (2xx, or 303→2xx). No URI may be a dead label. This REQUIRES that the `ttt:` domain vocabulary be served and that reused `schema.org` terms resolve — settle the two D-deref decisions (serve the domain vocab; decide CI handling of external term dereference) and record them. If a term IRI does not dereference, that is a real failure — fix the design (publish the term), do not skip the check.

- [ ] **Step 4: Identify inputs by meaning, then play.** The client selects the actor input by recognizing `https://schema.org/agent` and the target input by the `ttt:square` IRI (NOT by field name). It plays a full two-player game (X/O alternating) to a terminal state, POSTing ld+json bodies whose keys are the discovered IRIs and whose `@type` is the discovered class IRI — no hardcoded URLs, state names, class, or field IRIs. Legal squares come from the game-state representation (empty cells). The illegal-move leg posts an out-of-range square and asserts 422 citing a vocabulary IRI.

- [ ] **Step 6: Run AT-S6, confirm PASS.** Paste full output.

- [ ] **Step 7: Extend AT-S5 to three-format negotiation (LinkedData coverage).** AT-S5 currently asserts only `application/ld+json`. Add assertions that the same game also negotiates `application/json` (200, compact JSON) and `text/turtle` (200, Turtle body) — so `Frank.LinkedData` content negotiation is fully exercised per the coverage matrix. If `text/turtle` is not yet supported by the sample's `useLinkedData`, surface it (do not silently drop the format).

- [ ] **Step 8: Run the WHOLE SemanticTests suite** — AT-S1..S6 all green:
  ```bash
  DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test sample/TicTacToe-v732.E2E/TicTacToe.v732.E2E.fsproj --filter "SemanticTests" 2>&1 | tail -8
  ```
  Expected: 6/6 (was 4/6).

- [ ] **Step 9: Commit** — `git commit -m "feat(capstone): #333 AT-S6 agent-simulator + AT-S5 three-format negotiation"`.

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

## Task 6: AT-S8 — provenance as the end-of-game COMPLETE-CAPTURE audit

**Files:** `sample/TicTacToe-v732.E2E/SemanticTests.fs` (new AT-S8).

**Approach:** the closing act. As the AT-S6 navigator plays, it keeps **its own log** of every move it posted (agent + square, in order). At the end it follows the **discovered** `Link: rel="http://www.w3.org/ns/prov#has_provenance"` header (RFC 6903 — read, not hardcoded) to the lineage and proves the lineage captured the WHOLE session faithfully — not merely that one activity exists.

- [ ] **Step 1: Confirm the sample wires `useProvenance`** and that responses carry the `has_provenance` Link header. Quick check:
  ```bash
  # from a running sample: a GET /games/{id} response should carry
  # Link: <...>; rel="http://www.w3.org/ns/prov#has_provenance"
  ```

- [ ] **Step 2: Write AT-S8 (failing first) — cross-check lineage against the client's own move log.** The client plays a full game (reuse the AT-S6 navigator, capturing each posted move), reads the `has_provenance` Link from a response, GETs the lineage, and asserts:
  - **Completeness:** exactly one `prov:Activity` per posted move — count matches the client's log, none dropped, none invented; each typed `schema:MoveAction` and `wasAssociatedWith` an agent.
  - **Attribution + order:** each Activity's agent + targeted square match the client's log entry at that position; the Activities' `startedAtTime`/sequence reproduce the exact play order (alternating X/O) — a replay reconstructs the client's log.
  - **Outcome:** the recorded terminal outcome (final Activity's mapped `MoveResult` case — `schema:CompletedActionStatus` for Won/Draw) matches the final game state observed over HTTP.
  - The assertion must be falsifiable: a lineage that drops, reorders, or fabricates a move FAILS.

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
- [ ] **Step 2b: Coverage-matrix audit (D8).** Confirm every cell of the spec §D8 matrix is genuinely exercised: Semantic+CLI (extract/clarify/accept ran in Task 1; smoke `status`/`finalize`/`refresh`), all four generated modules compiled and consumed, Discovery (S1/S2/S3/S6), Validation (S4), LinkedData three formats (S5), Provenance complete-capture (S8), full composition (sample runs every `use*`). Any unexercised cell → add coverage or surface the gap; do not leave a shipped piece untested.
- [ ] **Step 3:** `dotnet fantomas --check src/` clean (and the sample files you changed).
- [ ] **Step 4: `/discipline`** on changed sample/E2E files; fix Criticals/Highs.
- [ ] **Step 5: `/self-reflect`** against this plan + spec ATs — confirm AT-S4/S6/S7/S8 each have observed green evidence; hunt for weakened assertions (an E2E that asserts only status, not the discovery behavior) and any hardcoded-knowledge leak in the navigator that would make AT-S6/S7 vacuous.
- [ ] **Step 6: `/expert-review`** — Miller (discovery/ALPS request-shape, content negotiation), Tim Berners-Lee (are the request-field IRIs dereferenceable/consistent), Fielding (HATEOAS: is the client truly navigating affordances vs reconstructing URLs), Claude-agent (is this genuinely "no out-of-band knowledge"). Surface all findings to the user; do not triage.
- [ ] **Step 7: STOP — do not merge or push.** Present results + the AT table for merge approval. On approval: `--ff-only` into master, push, close #333 (audit every AC first), reopen nothing silently.

---

## Self-Review (plan vs spec)

**Spec coverage:**
- D1 useValidation → Task 3. ✓
- D2 model MoveRequest via CLI clarify, honest IRIs → Task 1. ✓
- D3 client simulates an agent (follow links, resolve, expected term set, **dereference every URI**) → Task 4. ✓
- D4 AT-S6 full game → Task 4. ✓
- D5 AT-S7 vocab swap → Task 5. ✓
- D6 CI + sln → Task 7. ✓
- D7 AT-S8 provenance complete-capture audit → Task 6. ✓
- D8 capstone touches every shipped piece (coverage matrix) → Task 4 Step 5 (S5 three-format) + Task 8 Step 2b. ✓
- ACs AT-S1..S8 → Tasks 3/4 (S1-S6), 5 (S7), 6 (S8). ✓

**Placeholder scan:** Task 1 is a declared SPIKE (legitimate — it resolves the CLI/lock + U1 collision mechanics); Tasks 5 & 7 carry explicit "investigate the mechanism, then record it" steps (the `ex:` variant + Playwright-in-CI). No "add error handling"/"TBD" placeholders.

**Type consistency:** `MoveRequest = { Position: SquarePosition; Player: Player }` defined in Task 1, consumed by Task 2 (`accepts typeof<MoveRequest>`). The client selects inputs by **absolute term IRI** (`https://schema.org/agent`, the `ttt:square` IRI), not field name — Task 1 maps `Player → schema:agent` and `Position → ttt:square`.

**Settled decisions (D-deref, user-confirmed 2026-06-28):**
1. The sample **serves its `ttt:` domain vocabulary** (Frank as LD publisher); the agent-simulator rebases the term path onto the test server base and asserts the GET resolves.
2. Reused `schema.org` terms are **dereferenced live**; the simulator fails loudly if unreachable (no silent skip).

**Open items the spike must settle (surface if blocked):**
3. The `ex:` vocab-variant mechanism (Task 5 Step 1).
4. Playwright driver availability in CI (Task 7 Step 2).
5. U1: `Move`↔`MoveRequest` shared-class local-name collision (Task 1 Step 5).
