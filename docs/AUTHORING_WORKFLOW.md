# Authoring Workflow

How a Frank application gets built end-to-end, in the target state the v7.3.2 / v7.4.0 / Track C work is reaching toward.

This document describes the developer-facing loop rather than the architecture underneath it. For the architecture, see the [protocol-types ADR](./superpowers/specs/2026-04-21-v740-protocol-types-design.md) (algebra, projection, codegen, verification) and the [v7.3.2 semantic-discovery spec](./superpowers/specs/2026-04-20-v732-semantic-discovery-design.md) (vocabulary, lock file, MSBuild codegen). For the thesis the workflow is designed to prove, see [AGENT_HYPOTHESIS.md](./AGENT_HYPOTHESIS.md).

-----

## The loop

```
┌─────────┐   lift    ┌────────────┐   generate   ┌────────────┐
│ Sketch  │──────────▶│  Rigorous  │─────────────▶│  Running   │
│ format  │           │   format   │              │    app     │
└─────────┘           └────────────┘              └────────────┘
     ▲                      ▲                           │
     │                      │                           │
     │                      │                           │ inspect
     │                      │                           ▼
     │                      │                     ┌───────────┐
     │                      └─────────────────────│   LLM     │
     │   refine sketch                            │ authoring │
     └────────────────────────────────────────────│   agent   │
                                                  └───────────┘
```

Five stages, iterated as a loop:

1. **Sketch** — author draws the design in mermaid, smcat, or similar; fast, whiteboard-shaped, structurally lossy.
1. **Lift** — LLM promotes the sketch to a rigorous format (SCXML, Scribble, or F# CE), filling in the algebra constructs the sketch is missing; author reviews and accepts.
1. **Generate** — Frank’s build pipeline consumes the rigorous format, projects per-role actors, emits the discovery surface (ALPS, JSON Home, Link headers), semantic artifacts (SHACL, JSON-LD, PROV-O), and MSBuild-compiled F# source.
1. **Inspect** — the LLM exercises the running app: dry-runs through the protocol, reads generated artifacts, explores reachability, replays the journal, reports gaps or unreachable states.
1. **Iterate** — author edits the sketch (or, when working directly at the rigorous layer, the rigorous format) in response to the inspect output; loop repeats.

Each stage is independently useful. The loop is the thing that makes the whole stack better than the sum of its parts.

-----

## Stage 1: Sketch

**What the developer does:** draws a state diagram or protocol diagram in a sketch format. For a statechart-heavy design, that’s mermaid or smcat. For a protocol-heavy design (multi-party coordination), Scribble is already a rigorous format and the sketch stage is optional — the author can start directly at the rigorous layer.

**What the LLM does:** nothing automatic. The LLM may help the author produce the sketch conversationally (“draw me a state diagram for an order approval workflow with a revision loop”), but the sketch that lands in version control is the author’s artifact, reviewed by the author.

**Artifacts produced:**

- A sketch file committed to the repository (e.g., `design/OrderFulfillment.mmd`).
- Optionally, notes in the repo describing intent the sketch cannot express (e.g., `design/OrderFulfillment.notes.md` capturing role assignments, effect scopes, or communication semantics).

**What makes a good sketch:** names that carry domain meaning. The vocabulary layer (v7.3.2) resolves state and transition names against schema.org, wikidata, or domain ontologies; names like `ApproveOrder` and `FulfillmentComplete` lift better than `S1` and `S2` because the convention engine has something to match against. The lift stage is downstream of name quality.

**Why sketches are first-class:** the author’s mental model lives here. When something goes wrong downstream, the diagnostic is most legible when reported in sketch terms (“your protocol got stuck at `AwaitingShipment`”) rather than in algebra terms. Keeping the sketch authoritative in version control keeps the author’s artifact the one under review.

-----

## Stage 2: Lift

**What the developer does:** runs `frank lift` (CLI) to promote the sketch to a rigorous format. Reviews the proposed output. Accepts, edits, or rejects.

**What the LLM does:** reads the sketch. Identifies algebra constructs present in the sketch, constructs inferrable with high confidence, and constructs that must be supplied (roles, effects, scoped channels, communication semantics). Produces a draft rigorous-format artifact with inferrable constructs filled in and supplied-construct slots marked as proposals with provenance. Produces a structured diff summary for review.

**Commands:**

```
frank lift mermaid design/OrderFulfillment.mmd --to scxml --out design/OrderFulfillment.scxml
frank lift smcat design/OrderFulfillment.smcat --to scribble --out design/OrderFulfillment.scr
```

**Artifacts produced:**

- The rigorous-format artifact (e.g., `design/OrderFulfillment.scxml`). Committed, derived from the sketch.
- A lift-lock file (`design/OrderFulfillment.lift.lock.json`) recording the mappings the lift used: which sketch construct became which rigorous-format construct, which roles were inferred, which effects were proposed, confidence scores per mapping. Committed.
- A diff summary output for human review (not committed; consumed in the session).

**The review gate:** the lift never hands its output straight to the generator. Proposed slots must be accepted, edited, or replaced by the author; unresolved slots block subsequent stages. `frank lift accept` marks the lift-lock confirmed; only then can `frank generate` run.

**Re-running the lift:** if the sketch changes, the lift re-runs and produces a new rigorous-format artifact plus a new diff against the prior lift-lock. Confirmed mappings carry forward; new or changed mappings enter the proposal queue for review. This keeps the lift deterministic across runs while allowing author review to concentrate on actual changes.

**Failure modes in this stage:**

- *Sketch is too ambiguous.* The lift emits proposals for every role/effect/channel slot. The author’s options are to enrich the sketch (add annotations, split into more states), to accept the proposals, or to switch to direct rigorous-format authoring.
- *LLM hallucinates an algebra construct.* The review gate catches this: the author rejects the proposal and either re-prompts with tighter context or fills the slot manually. The lift-lock records the rejection so the same bad proposal does not recur on re-runs.
- *Sketch format lacks a construct the rigorous format needs.* Mermaid has no role concept; the lift must propose roles from state names or ask the author. This is not a bug; it is the reason the sketch-to-rigorous stage exists.

-----

## Stage 3: Generate

**What the developer does:** runs `dotnet build` (or equivalent). The MSBuild target reads the rigorous-format artifact (or the F# CE, when that is the rigorous-format choice), invokes the Frank code generator, and compiles the output into the application assembly.

**What the LLM does:** nothing. This stage is deterministic: same rigorous-format artifact in, same generated code out, same HTTP surface served at runtime.

**Artifacts produced (via codegen, into `obj/`):**

- Per-role F# actor implementations (`HierarchicalMPSTActor` instantiations, see protocol-types ADR §7).
- Per-role handler interfaces (`IEffectHandler<'Effect, 'Result>` per role).
- Log-schema documents describing what the actor emits to the journal at runtime.
- `GeneratedLinkedData.fs`, `GeneratedValidation.fs`, `GeneratedProvenance.fs`, `GeneratedDiscovery.fs` (v7.3.2) — the runtime discovery and semantic surface.
- SMT-LIB queries for Z3 verification of the source rigorous-format artifact (protocol-types ADR §8, §10.9).

**Build gates:**

- **Implementability check** (protocol-types ADR §4): the Li-et-al. projection either succeeds or fails the build with a witness trace naming the offending subprotocol.
- **Z3 verification** (Tier 1 always, Tier 2 per-protocol opt-in): properties of the source protocol — deadlock freedom, race freedom, payload refinement — discharged inline.
- **Semantic lock file check** (v7.3.2): any `proposed` or `unresolved` mapping entries block source generation with remediation guidance (`frank semantic clarify` to resolve).
- **Lift lock file check**: any unconfirmed lift proposals block source generation.

A build that passes all gates produces a running web application whose protocol behavior has been verified end-to-end against the source rigorous-format artifact — no manual code required.

**Failure modes in this stage:**

- *Implementability fails.* The generator reports a witness trace: “role X cannot distinguish branches after the merge of `OrderPlaced` and `OrderRejected`.” The author fixes the design (usually by adding a disambiguating message in the projected local type) and re-runs. This is the generator’s most valuable gate; the bug is caught before any code exists.
- *Z3 timeout or unknown result.* The build fails with a pointer to the offending construct. The author simplifies the property, adds a refinement annotation, or opts the property out per protocol.
- *Lock-file gates block.* The author runs `frank semantic clarify` or `frank lift accept` as prompted, resolves the blockers, and re-runs.

-----

## Stage 4: Inspect

**What the developer does:** runs the generated application. Interacts with it as a runtime agent would (manual HTTP, Postman, browser, a scripted client). Optionally, delegates this to the LLM in a structured inspection session.

**What the LLM does:** reads the generated artifacts served by the running application (ALPS, JSON Home, Link headers, SHACL shapes, PROV-O journal exports). Drives dry runs through the protocol as each role. Reports:

- Reachability gaps — states or transitions declared in the rigorous format but never reachable via valid runtime interactions.
- Unreachable branches — the Z3 gate caught none of these at build time, but dynamic inspection may find paths whose guards are never satisfied under realistic input.
- Affordance mismatches — the rigorous format declares a role can do X in state Y; the runtime discovery surface does not advertise X in state Y. (This is the v7.3.0 failure class, caught dynamically rather than by tests.)
- Semantic drift — the generated ALPS profile’s IRIs do not match the vocabulary declared in the v7.3.2 lock file.
- Journal traces — what the protocol actually did during each dry run, projected into domain-meaningful terms via PROV-O and the vocabulary mappings.

**Commands:**

```
frank inspect reachability --app http://localhost:5000
frank inspect dry-run --role Customer --scenario "happy-path-order"
frank inspect journal --trace-id <id> --format prov-o
```

**Artifacts produced:**

- An inspection report summarizing gaps and matches, with references back to specific sketch or rigorous-format constructs where issues were found.
- A structured JSON output consumable by the LLM for the next loop iteration’s lift refinement.

**Why this matters:** Stage 3 gates catch structural bugs; Stage 4 catches semantic bugs. Z3 proves “no deadlock is reachable”; inspection proves “the happy path an author described in plain language actually works end-to-end.” Both are necessary. The inspection stage is where the feedback loop closes — it is how the author discovers that their diagram, while formally correct, does not do what they meant.

**Failure modes in this stage:**

- *Inspection reports a gap; author disagrees.* The report includes the evidence (trace, artifact excerpts, vocabulary mappings). Author either accepts the gap and edits the sketch, or recognizes the report as a false positive and files a diagnostic issue against the inspection tooling.
- *Inspection is flaky across runs.* The inspection LLM’s exploration is non-deterministic; results should be treated as high-coverage sampling, not closed proofs. For closed properties, use Z3 or conformance testing. This is a known limitation, not a bug.
- *Running app not in sync with the rigorous format.* Inspection detects this immediately: affordances served do not match affordances declared. Re-run the build; if the mismatch persists, the generator has a bug and the v7.3.0-class regression discipline applies.

-----

## Stage 5: Iterate

**What the developer does:** reads the inspection report. Edits the authoritative source artifact (sketch, by the default convention) in response. Commits.

**What the LLM does:** optionally, proposes specific sketch edits that would close the gaps found in inspection. The developer reviews and accepts.

**Artifacts produced:** a new revision of the source artifact. The loop returns to Stage 2 (lift), which re-runs against the updated sketch, which triggers Stage 3 (generate) with new gates, which triggers Stage 4 (inspect) with new evidence.

**How the loop converges:** each iteration closes gaps and tightens the correspondence between the author’s intent (the sketch), the algebra-faithful representation (the rigorous format), and the running system (the generated application). The loop terminates when the inspection stage finds no gaps the author cares about — which is the operational definition of “done” for a Frank application.

-----

## Commit discipline

The loop depends on being able to reproduce any prior state and understand how it changed. That discipline is what keeps the LLM’s role in the loop bounded to review-gated authoring-time assistance.

**What is committed to version control:**

- Sketch artifacts (authoritative source, by default convention).
- Rigorous-format artifacts (derived, but committed so they can be diffed across lift re-runs).
- Lift-lock files (`*.lift.lock.json`): the recorded mappings from sketch constructs to rigorous-format constructs, per-file.
- Semantic-mapping lock file (`.frank/semantic-mappings.lock.json`): the v7.3.2 vocabulary lock, resolving F# types to vocabulary IRIs.
- Vocabulary declarations (the CE or equivalent), including prefix declarations and alignment assertions.
- Design notes where the sketch format cannot capture intent.

**What is not committed:**

- Generated F# source (emitted into `obj/` by MSBuild).
- Inspection reports (per-session diagnostics, not build inputs).
- Draft lift proposals before acceptance (ephemeral; become lift-lock entries on accept).

**Diff discipline:**

- Changes to the sketch should produce a corresponding change to the rigorous format via `frank lift`. A commit that changes only the rigorous format without a corresponding sketch change is flagged by drift detection; it is not forbidden (authors may need to work at the rigorous layer) but it needs a commit-message note explaining why.
- Changes to the lift-lock or semantic-mapping lock files are part of the same commit as the change that caused them. Lock files drifting from the artifact that should produce them is a merge-conflict hazard.
- The combined `build` convenience command (lift + intake + generate) does not bypass the review gate. It re-runs lifts whose acceptance is already recorded in the lock file; it fails on any unaccepted proposal.

**Source-of-truth convention:**

The default is **sketch as source, rigorous format as derived** — the author’s artifact is the one under review, and the rigorous format is regenerated from it via the lift. Projects where the author works directly at the rigorous layer (F# CE authors, Scribble authors writing from a formal specification) invert the convention: rigorous format as source, sketch (if any) as regenerated documentation. The per-project choice is recorded in `.frank/workflow.config.json`; the CLI enforces drift detection in both directions.

See protocol-types ADR §13 open question 15 for the unresolved parts of this convention.

-----

## Failure modes across the loop

Every stage has its own failure modes, documented in context above. Cross-cutting failure modes worth explicit attention:

- **The lift-review gate is skipped.** If `frank lift` is run in a CI pipeline that auto-accepts proposals, the v7.3.0-class regression pattern returns at the lift layer: the generated system “works” but does not match the author’s intent. The review gate is a discipline; enforcing it procedurally (e.g., lift-lock changes in a commit require a reviewer on the PR) is the operational backstop.
- **Vocabulary drift without code review.** A vocabulary schema changes upstream (schema.org updates); `frank semantic refresh` reports the drift; existing `confirmed` mappings are not auto-mutated. If the refresh step is skipped, generated IRIs continue to resolve against the locally-cached schema. This is the designed behavior — silent semantic changes are worse than slightly-stale schemas — but it means a vocabulary-refresh cadence must be part of the project’s maintenance discipline.
- **Inspection skipped entirely.** The loop works without inspection: sketch → lift → generate → ship. The build gates catch structural bugs and the Z3 gate catches protocol-level properties. What is lost is the semantic check: the author’s intent matches the system’s behavior. Projects that skip inspection are relying on the sketch-to-rigorous-to-generated chain being correct by construction, which is a stronger assumption than the loop’s normal operation makes.
- **Multiple authors working in the loop concurrently.** Sketch merges, lift-lock merges, and semantic-lock merges all need conflict-resolution conventions. The lock-file pattern is deliberately designed to make these merges tractable (deterministic regeneration from source artifacts), but the CLI needs to support `frank lift --reconcile` and `frank semantic --reconcile` to merge concurrent changes cleanly. This is a future-work item.

-----

## Current status (where the loop stands)

|Stage                      |v7.3.2                                                        |v7.4.0 (Track A)                           |v7.4.0 (Track C)                          |Stretch                                  |
|---------------------------|--------------------------------------------------------------|-------------------------------------------|------------------------------------------|-----------------------------------------|
|Sketch                     |—                                                             |—                                          |—                                         |Mermaid/smcat intake                     |
|Lift                       |—                                                             |—                                          |—                                         |LLM-assisted promotion to rigorous format|
|Generate (semantic surface)|✓ vocabulary CE, convention engine, lock file, MSBuild codegen|—                                          |—                                         |—                                        |
|Generate (protocol surface)|—                                                             |✓ CE → actors + discovery surface          |✓ role-projected SSE + AND-state broadcast|—                                        |
|Inspect                    |✓ `frank semantic status`, `clarify`                          |Partial — dry-run client via thesis track A|Partial — multi-role inspection           |LLM-driven reachability exploration      |
|Iterate                    |Manual                                                        |Manual                                     |Manual                                    |LLM-proposed sketch edits                |

**What works today:** semantic-discovery codegen with vocabulary alignment and LLM-assisted clarification (v7.3.2). The protocol-types ADR describes the v7.4.0 algebra and codegen; the v7.3.0 rollback and v7.4.0 falsifiable-HTTP-AC discipline exist specifically to prevent the generate stage from claiming correctness it does not have.

**What v7.4.0 adds:** the protocol side of the generate stage, plus partial inspection via the thesis tracks. Track A’s `#301` naive-client e2e test is exactly the inspection stage running against the generated system.

**What the stretch goal adds:** the sketch and lift stages, plus LLM-driven inspection and iteration. The sketch side is the most visible change for authors but the smallest amount of new machinery — the algebra, codegen, and verification layers are already where they need to be. The lift and LLM-driven inspection are the larger engineering investments, because the review gates and diagnostic surfaces need to be right.

**Why documenting the target end-state now:** the CLI commands, lock-file patterns, and review-gate disciplines being shaped in v7.3.2 are the commitments the full loop will inherit. Getting them aligned with the target workflow — rather than designed piecemeal per release — is what makes the decomposition into v7.3.2 / v7.4.0 / Track C a sequenced path rather than a collection of related projects.

-----

## Related documents

- [protocol-types ADR](./frank-protocol-types-adr.md) — algebra, projection, codegen, verification, build order. Appendix R documents the intake pipeline this workflow consumes.
- [v7.3.2 semantic-discovery spec](./superpowers/specs/2026-04-20-v732-semantic-discovery-design.md) — vocabulary CE, convention engine, lock file, MSBuild codegen. The semantic half of Stage 3.
- [AGENT_HYPOTHESIS.md](./AGENT_HYPOTHESIS.md) — the runtime-agent thesis this workflow’s authoring agents run in parallel to. See the “Two agent roles” section for the runtime/authoring split.