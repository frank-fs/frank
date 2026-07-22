# v7.3.2 Completion Plan — v4 (outside-in, TWO proof journeys, zero deferrals)

Corrections from v3, per user:
- v3 mapped only the RUNTIME naive-client journey and wrongly treated authoring CLI (`finalize`/`validate`) as trimmable. v7.3.2's thesis is the AUTHORING pit-of-success (Q2/Q3 per `docs/AGENT_HYPOTHESIS.md` — v7.3.2 owns authoring infrastructure). → **two proof journeys**, both first-class.
- **Nothing deferred.** The v3 defer bucket is dissolved; every item is tag work, mapped to a journey step.
- Decisions locked: **D1** [LiveNetwork] CI job · **D2** allocation-delta gate · **D3** KEEP Frank.Semantic.Core split (bounded: collapse harmful vs #401 net8.0 FRANK002 canary; documented via DBT1) · **D4′** finalize stays (dissolved — was my mischaracterization).

Salvage stance unchanged (v2 §0-1): 0 components fire a rebuild trigger; rollback re-derives ~132 anti-false-green commits for no unique win except the F-CONF seam, which is retrofitted. This plan is the completion DELTA.

---

## 1. Two proofs (the outer acceptance tests)

**Proof A — Authoring journey** (the developer/authoring-agent produces a committed, buildable lock, deterministically). Owns Q2 (authoring iteration cost) + Q3 (diagnostic feedback loop).
Proven by: AT-S7 (vocab-swap regenerates lock), the build-gate negatives (proposed/unresolved → build fails), hash-drift negative (refresh reports, no auto-mutate), `VocabSwapTests`, `BuildGateIntegrationTests`, `RefreshCliTests`.

**Proof B — Runtime naive-client journey** (a client with only the entry URL navigates by discovery). Owns Q1 mechanisms (v7.3.2 ships the surface; measurement is v7.4.0+).
Proven by: AT-S6 + AT-S1..S11.

Both stay green at every step. Completion = make every step of BOTH honest on the real wire, deepest-blocking-first.

---

## 2. Proof A — Authoring journey. Steps · Observable · Proving AT · Blocking work

| Step | Observable | Proves | Blocking work (role) |
|---|---|---|---|
| A1 declare | `vocabulary { using "schema" }` + F# types compile | VocabularyRegistryTests | none (guard) |
| A2 `extract` | draft lock: proposed/unresolved entries with candidate terms+scores | extract tests | none (guard) |
| A3 diagnostics | convention engine surfaces WHY a mapping is ambiguous/excluded (feedback loop, Q3) | equivalentClass notice | **T2/#427** ConventionDiagnostic DU — generalize the single-case notice into a diagnostic DU with ≥2 cases **[fix]** |
| A4 decide (LLM path) | `clarify`→JSON contract, `accept`→lock, schemaVersion fail-closed | AcceptTests (v99→Error) | none (guard) |
| A4′ decide (zero-LLM) | `finalize`→confirm exact, exclude rest; regenerates integrity → lock buildable | Finalize tests | none (guard) — **kept, thesis-critical** |
| A5 `status` | resolved/excluded per package | status tests | none (guard) |
| A6 `refresh` | hash-drift reported (exit 2), confirmed entries NOT auto-mutated | RefreshCliTests | none (guard) |
| A7 `validate` | self-hosted vocab endpoints dereference (deref-thesis guard, authoring side) | validate tests | none (guard) |
| A8 build gate | lock with proposed/unresolved → `dotnet build` FAILS with guidance | BuildGateIntegrationTests MS001 | none (guard) |
| A9 codegen | MSBuild emits 5 `Generated*.fs` via Fabulous.AST, FCS-typechecked | Generate*TaskTests | none (guard) |

Proof A is almost entirely green guards. The one open item is **T2/#427** (A3 diagnostics) — now correctly journey-connected (improves the Q3 feedback loop), not deferrable techdebt.

---

## 3. Proof B — Runtime naive-client journey. Steps · Observable · Blocking slices (hardened AC)

### B1 Entry — `GET /` (json-home) → directory
- Observable: lists every reachable resource incl. the games collection; hrefs resolve.
- Proves: AT-S3 (+ collection AT).
- **R5** ItemList route missing (`GET /games`→404). **[slice/red]** — AC: `GET /games`→200 `schema:ItemList`; JSON Home ItemList href `GET`-resolves 200.

### B2 Affordances — `OPTIONS /games/{id}` → honest `Allow` + `Link`
- Observable: every advertised method served; every embodied type advertised `rel="type"`; `describedby`→ALPS.
- Proves: AT-S1 + **F-CONF**.
- **R2** HEAD advertised→405. **[slice]** — AC: `HEAD`→handled 200, empty body, header-set==GET's (assert handled, not merely ≠405).
- **R4** `relation` single-valued → `MoveAction` under-advertised. **REDESIGN slice** — AC: `rel="type"` set EXACTLY {schema:Game, schema:MoveAction} + absence of collapsed value; single-relation resource OPTIONS byte-unchanged.
- **F-CONF** seam — AC: every advertised method → not 405 → handled status; every advertised `describedby` present on 200/304/404/406/409/412/422. (The retrofit of the rebuild's one unique win; closes the v7.3.0 "advertised-but-not-served" family that R2/R4/R10 prove is still alive.)

### B3 Semantics — `GET` ALPS → schema.org descriptors
- Observable: schema.org IRIs, rt resolves, no phantom descriptors. Proves AT-S2/S11. **[guard]**

### B4 Representation — `GET /games/{id}` (ld+json) → `@context` + links
- Observable: real-schema.org `@context`; state-dependent validMoves; `describedby`→`/vocabulary`→two-hop Wikidata; `prov:has_provenance`; links survive `304`.
- Proves: AT-S5, two-hop (`:970`).
- **R9/T1** ETag captive-dependency + provider can't receive handler-in-progress context (#426). **[fix — serialized]** — AC: Scoped provider→correct per-request OR fail-fast startup; provider receives handler context, ETag reflects it.
- **R10** `describedby` dropped on 304/412 under legal order. **[fix — serialized w/ R9]** — folded into F-CONF survives-every-status.

### B5 THE THESIS (deepest) — deref schema.org IRIs → the REAL web
- Observable: IRIs dereference against REAL schema.org (recognition, not fixture-collusion).
- Proves: **AT-S6-live** via a **[LiveNetwork] CI job (D1)**; default suite keeps `SchemaOrgStub` (deterministic). Design-time guard: FRANK002.
- **D1 impl + DBT3.** **[slice]** — AC: AT-S6-live green in the LiveNetwork job vs real schema.org; hallucinated local-name 404s; **FRANK002 red on an undereferenceable term in a real `dotnet build`** (DBT3 — proves the analyzer fires in-build, not just its fn).

### B6 Action — `POST` move → SHACL, honest errors
- Observable: valid→200; invalid→422 `ValidationReport` citing schema.org IRIs; malformed→clean 4xx `problem+json`.
- Proves: AT-S4.
- **R6** sample bypasses ProblemJson (400 no Content-Type; 406 text/plain). **[fix]** — AC: 400 & 406→`application/problem+json` + type/title/status + 406 detail names exact alternates {ld+json,turtle,rdf+xml}.
- **R7** malformed body→unhandled JsonReaderException→Kestrel. **[investigate red-repro-first→fix]** — AC: STEP1 RED repro (isolated); STEP2 fix; STEP3 malformed→clean 4xx + zero Kestrel unhandled log.

### B7 Provenance — follow `prov:has_provenance` → PROV-O lineage
- Observable: dereferenceable activity IRIs; wasDerivedFrom chain. Proves AT-S8. **[guard]**

### B8 Completion — play by IRI only
- Observable: full game via discovery; vocab-swap breaks hardcoded client not discovery client. Proves AT-S6/S7/S9/S10. **[guard]**

### Cross-cutting (B-serving paths)
- **R3** per-request allocations (BoundedCache hit, ALPS/JSON-Home re-serialize, profileLink sprintf, ld-context re-parse). **[fix]** — AC: allocation-delta (`GC.GetAllocatedBytesForCurrentThread`) shows no per-request growth on a cache hit (**D2 = gated**, not inspection-only); bytes identical; suite==baseline.

### Tag hygiene (connects weakly, but in-scope — nothing deferred)
- **R8** doc-comment "Flat"→accurate (recursive type) + `Frank.Cli.fsproj` prop order == siblings. **[fix]**
- **DBT1** update spec/README: decision #9 reversal (Core split kept, D3) documented. **DBT2** spec §4 says 4 artifacts, reality 5 — reconcile. **[docs]**

---

## 4. Decisions — all locked

| # | Decision | Resolution |
|---|---|---|
| D1 | Step-B5 live-deref proof | **[LiveNetwork] CI job** running AT-S6-live vs real schema.org; default suite stub-backed (deterministic) |
| D2 | R3 perf posture | **Allocation-delta test gate** (enforced, not inspected) |
| D3 | Frank.Semantic.Core split | **KEEP** — collapse harmful vs #401 net8.0 FRANK002 canary; documented via DBT1 (not deferred — resolved) |
| D4′ | finalize | **Stays** — thesis-critical lock-decision step (dissolved; was my error) |

---

## 5. Outside-in execution sequence (both journeys green; deepest-first; collision-safe)

Serialization: sample-touchers (R2/R5/R6/R7) never parallel; ETag stream (R9/R10/T1) one serial chain; R3+R4 sequenced (shared DiscoveryMiddleware).

**Wave 0 (no code):** Step-0 baseline capture (run suite+E2E, record OBSERVED counts). Bookkeeping B1-B7. File all slices/fixes as issues under #409.

**Wave 1 (locked decisions → implement):** none pending (D1-D4 resolved); proceed.

**Wave 2 (slices, deepest-first, collision-safe):**
1. **B5 thesis FIRST** — D1 [LiveNetwork] AT-S6-live + DBT3 FRANK002-in-build. Prove the thesis on the real wire before polishing outer steps.
2. **B2** — F-CONF seam + R2 + R4 redesign (serialized, shared DiscoveryMiddleware).
3. **B1** — R5 collection route.
4. **B4/B6** — R9+R10+T1 (serial ETag chain); R6; R7 (time-boxed).
5. **A3** — T2/#427 ConventionDiagnostic DU (authoring diagnostics).
6. **Cross-cutting** — R3 caching (after paths settle) + D2 gate.
7. **Tag hygiene** — R8; DBT1/DBT2 docs.

**Wave 3 (closure):**
8. Full 7-expert re-panel vs integrated surface (the #409 gate).
9. **R1 walkthrough recapture LAST** (after re-panel; narrates both final journeys). Normalization mask (GUID/port/timestamp→placeholders) makes "byte-match every Verified block" executable.
10. Close #409 → #336 with per-step evidence; **maintainer merges master**.

---

## 6. Definition of done — both journeys honest end-to-end

1. Baseline captured (observed counts).
2. Proof A: all authoring steps green incl. A3 generalized diagnostics; build gate + vocab-swap + hash-drift negatives green.
3. Proof B: all 8 runtime steps green with observed evidence; **B5 proven on REAL schema.org** (LiveNetwork job); **F-CONF** proves no advertised-but-unserved affordance.
4. Every open slice/fix (R2-R10, T1, T2, F-CONF, R5, DBT3) adversarially re-verified.
5. D1/D2 implemented; D3/D4′ documented.
6. Full 7-expert re-panel: failure-class #1 absent, failure-class #2 family closed, no new blocking.
7. build+test+fantomas+E2E green (absolute-path re-run, not agent-reported).
8. R8 + DBT1/DBT2 done; linkage B1-B7 corrected; #409 + #336 closable.
9. Maintainer merges to master.

---

## 7. One-line summary
Two proofs — the AUTHORING journey (vocab CE → extract → decide via clarify/accept OR finalize → build gate → codegen) and the RUNTIME naive-client journey (json-home → OPTIONS → ALPS → ld+json → **live schema.org deref** → SHACL move → provenance → play-by-IRI). Completion = make every step of both honest on the real wire, deepest-first; the recurring "advertised-but-not-served" family (R2/R4/R10) is closed by one F-CONF seam; nothing deferred; rollback not evidence-justified.
