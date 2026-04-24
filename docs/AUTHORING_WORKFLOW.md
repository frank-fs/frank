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
1. **Lift** — LLM promotes the sketch to a rigorous format (SCXML, Scribble, or F# CE), filling in the algebra constructs the sketch is missing; CLI records the mapping with provenance; author reviews and accepts.
1. **Generate** — Frank’s build pipeline consumes the rigorous format, projects per-role actors, emits the discovery surface (ALPS, JSON Home, Link headers), semantic artifacts (SHACL, JSON-LD, PROV-O), and MSBuild-compiled F# source.
1. **Inspect** — the LLM exercises the running app: dry-runs through the protocol using CLI-provided discovery and journal-query tools, reports gaps or unreachable states.
1. **Iterate** — author edits the sketch (or, when working directly at the rigorous layer, the rigorous format) in response to the inspect output; loop repeats.

Each stage is independently useful. The loop is the thing that makes the whole stack better than the sum of its parts.

-----

## Design principles

Three principles govern how the CLI, the LLM, and the author divide the work. They are the operational expression of the architecture the generated applications themselves embody: a discoverable tool surface, a reasoning agent, and a state artifact that coordinates between them.

**1. The CLI provides deterministic tools, not judgment.**

CLI commands parse, extract, generate, validate, diff, and record. They do not interpret a sketch’s intent, propose role assignments, hallucinate algebra constructs, or narrate a design choice. Anything that requires reading context and producing a judgment is the LLM’s work; anything that requires a repeatable input-to-output transform with a fixed schema is the CLI’s work. The boundary is sharp on purpose: it is what lets the lock files be trustworthy, the gates be enforceable, and the loop be reproducible across authors and sessions.

The practical consequence is that no single command does “the lift” or “the inspection.” Each stage is a choreography: the LLM reads structured CLI output, reasons about it, writes artifacts to disk, calls CLI tools to record what it did with provenance, and the author gates the result. The CLI is a toolkit the LLM composes; the choreography lives in the LLM.

**2. The CLI is self-describing.**

Every command emits structured help in a form an LLM can consume to compose workflows — the same semantic-discovery principle Frank’s generated applications follow for runtime agents. `frank describe` emits the full tool surface as JSON (commands, arguments, input schemas, output schemas, side effects, state-file interactions). Each subcommand accepts `--help --json` for the same data scoped to that command. Commands that produce machine-consumable output default to JSON on stdout and human-readable formatting only when attached to a TTY.

This is the authoring-time analog of JSON Home and ALPS. The LLM does not need to be trained on Frank’s CLI; it reads the tool surface the same way a runtime agent reads a generated app’s affordance surface. New commands become immediately usable without documentation updates to the LLM’s context, because the commands describe themselves.

**3. The state file is the coordination point.**

`.frank/authoring.state.json` records where each source artifact sits in the loop, which gates are passing or blocked, and what the recommended next step is for the author and for the LLM. The LLM reads the state file to orient at the start of a session; the CLI writes to it as tools execute; the author reads it to know what they owe the loop. It is the authoring-time analog of the statechart journal the runtime uses — a record that makes progress legible and resumable.

See [The state file](#the-state-file) below for schema and conventions.

-----

## Stage 1: Sketch

**What the developer does:** draws a state diagram or protocol diagram in a sketch format. For a statechart-heavy design, that’s mermaid or smcat. For a protocol-heavy design (multi-party coordination), Scribble is already a rigorous format and the sketch stage is optional — the author can start directly at the rigorous layer.

**What the LLM does:** nothing automatic. The LLM may help the author produce the sketch conversationally (“draw me a state diagram for an order approval workflow with a revision loop”), but the sketch that lands in version control is the author’s artifact, reviewed by the author.

**What the CLI does:** records the sketch as the loop’s entry point. `frank state note-sketch design/OrderFulfillment.mmd` registers the artifact in the state file and sets its next recommended step to `lift`. No parsing or interpretation happens at this stage — just acknowledgment that a sketch exists.

**Artifacts produced:**

- A sketch file committed to the repository (e.g., `design/OrderFulfillment.mmd`).
- Optionally, notes in the repo describing intent the sketch cannot express (e.g., `design/OrderFulfillment.notes.md` capturing role assignments, effect scopes, or communication semantics).
- A state-file entry for the sketch, with `stage: sketch`, `next: lift`.

**What makes a good sketch:** names that carry domain meaning. The vocabulary layer (v7.3.2) resolves state and transition names against schema.org, wikidata, or domain ontologies; names like `ApproveOrder` and `FulfillmentComplete` lift better than `S1` and `S2` because the convention engine has something to match against. The lift stage is downstream of name quality.

**Why sketches are first-class:** the author’s mental model lives here. When something goes wrong downstream, the diagnostic is most legible when reported in sketch terms (“your protocol got stuck at `AwaitingShipment`”) rather than in algebra terms. Keeping the sketch authoritative in version control keeps the author’s artifact the one under review.

-----

## Stage 2: Lift

The lift is the point at which structurally lossy sketch constructs are promoted to a rigorous algebra. That promotion necessarily involves judgment the CLI cannot make, which is why the CLI’s role is a set of deterministic tools the LLM composes — parsing, validating, recording mappings, diffing, gating — with the reasoning work of identifying roles, effects, and channels living in the LLM.

**What the CLI provides (deterministic tools):**

- `frank sketch parse <file>` — parse mermaid, smcat, or other sketch formats to a structured JSON representation. Stable schema, idempotent.
- `frank rigorous validate <file>` — schema-validate a rigorous-format artifact (SCXML, Scribble, F# CE output). Returns a list of violations, not a judgment about whether the artifact is correct.
- `frank lift record --sketch <path> --rigorous <path> --mapping <json>` — write a lift-lock entry: which sketch construct maps to which rigorous-format construct, the provenance (LLM proposal, human edit, prior lock carryforward), a confidence annotation supplied by the caller. Appends to `<sketch>.lift.lock.json`.
- `frank lift diff <sketch>` — diff the current proposed mappings against the prior confirmed lift-lock; emit structured JSON describing changed, added, and removed mappings.
- `frank lift status <sketch>` — report which mappings are confirmed, proposed, or rejected.
- `frank lift accept <sketch>` — mark all proposed mappings as confirmed. The review gate; requires a clean diff or an explicit `--force-review` acknowledgment.

**What the LLM does (reasoning):**

- Reads the parsed sketch (output of `frank sketch parse`) and any companion notes.
- Identifies algebra constructs present in the sketch, constructs inferrable with high confidence, and constructs that must be supplied (roles, effects, scoped channels, communication semantics).
- Produces the rigorous-format artifact by writing it to disk directly.
- Records each mapping with provenance via `frank lift record`, including its own confidence in the proposal.
- Narrates the review gate to the author, summarizing what was inferred versus what was supplied and flagging slots where the author’s judgment is most needed.

**What the developer does:** reviews the LLM’s proposals, using `frank lift status` and `frank lift diff` to see what is being asserted and what changed. Accepts with `frank lift accept`, edits the rigorous artifact directly and re-records, or rejects specific proposals (which the LLM can revise).

**Artifacts produced:**

- The rigorous-format artifact (e.g., `design/OrderFulfillment.scxml`). Committed, derived from the sketch.
- A lift-lock file (`design/OrderFulfillment.lift.lock.json`) recording the mappings and provenance. Committed.
- Updates to `.frank/authoring.state.json`: the sketch’s stage advances to `lift`, with `next: review` while proposals exist and `next: generate` once `frank lift accept` succeeds.

**The review gate:** the lift never hands its output straight to the generator. Proposed slots must be accepted, edited, or replaced by the author; unresolved slots block subsequent stages. `frank lift accept` marks the lift-lock confirmed; only then does the state file advance the stage past `lift`, and only then can `frank generate` (or `dotnet build`) run without the lift-lock gate blocking it.

**Re-running the lift:** if the sketch changes, the LLM re-parses it (`frank sketch parse`), regenerates proposals, and records them. `frank lift diff` shows the author what changed relative to the last confirmed lock. Confirmed mappings carry forward unchanged; new or changed mappings enter the proposal queue for review. The determinism of the CLI tools guarantees the diff is meaningful across runs even when the LLM’s phrasing varies.

**Failure modes in this stage:**

- *Sketch is too ambiguous.* The LLM’s proposals come back with low confidence for many slots, and the lift-lock records this explicitly. The author’s options are to enrich the sketch (add annotations, split into more states), to accept the proposals after review, or to switch to direct rigorous-format authoring.
- *LLM hallucinates an algebra construct.* The review gate catches this: the author rejects the proposal (the CLI records the rejection in the lift-lock) and either re-prompts with tighter context or fills the slot manually. The lift-lock’s record of the rejection survives across re-runs so the same bad proposal does not recur.
- *Sketch format lacks a construct the rigorous format needs.* Mermaid has no role concept; the LLM must propose roles from state names or ask the author. This is not a bug; it is the reason the sketch-to-rigorous stage exists. The proposals are flagged `source: inferred` versus `source: supplied` in the lift-lock so the provenance is explicit.

-----

## Stage 3: Generate

**What the developer does:** runs `dotnet build` (or equivalent). The MSBuild target reads the rigorous-format artifact (or the F# CE, when that is the rigorous-format choice), invokes the Frank code generator, and compiles the output into the application assembly.

**What the LLM does:** nothing. This stage is deterministic: same rigorous-format artifact in, same generated code out, same HTTP surface served at runtime.

**What the CLI does:** writes state-file entries for each gate’s result (pass, fail with pointer, skipped with reason). On failure, the state file’s `next` field points to the specific remediation command (e.g., `frank semantic accept PaymentMethod` or `frank lift accept design/OrderFulfillment.mmd`).

**Artifacts produced (via codegen, into `obj/`):**

- Per-role F# actor implementations (`HierarchicalMPSTActor` instantiations, see protocol-types ADR §7).
- Per-role handler interfaces (`IEffectHandler<'Effect, 'Result>` per role).
- Log-schema documents describing what the actor emits to the journal at runtime.
- `GeneratedLinkedData.fs`, `GeneratedValidation.fs`, `GeneratedProvenance.fs`, `GeneratedDiscovery.fs` (v7.3.2) — the runtime discovery and semantic surface.
- SMT-LIB queries for Z3 verification of the source rigorous-format artifact (protocol-types ADR §8, §10.9).

**Build gates:**

- **Implementability check** (protocol-types ADR §4): the Li-et-al. projection either succeeds or fails the build with a witness trace naming the offending subprotocol.
- **Z3 verification** (Tier 1 always, Tier 2 per-protocol opt-in): properties of the source protocol — deadlock freedom, race freedom, payload refinement — discharged inline.
- **Semantic lock file check** (v7.3.2): any `proposed` or `unresolved` mapping entries block source generation. The state file’s `next` field points to the remediation command.
- **Lift lock file check**: any unconfirmed lift proposals block source generation. The state file’s `next` field points to `frank lift accept`.

A build that passes all gates produces a running web application whose protocol behavior has been verified end-to-end against the source rigorous-format artifact — no manual code required.

**Failure modes in this stage:**

- *Implementability fails.* The generator reports a witness trace: “role X cannot distinguish branches after the merge of `OrderPlaced` and `OrderRejected`.” The author fixes the design (usually by adding a disambiguating message in the projected local type) and re-runs. This is the generator’s most valuable gate; the bug is caught before any code exists.
- *Z3 timeout or unknown result.* The build fails with a pointer to the offending construct. The author simplifies the property, adds a refinement annotation, or opts the property out per protocol.
- *Lock-file gates block.* The state file’s `next` field already points to the resolving command; the author runs it, resolves the blockers, and re-runs.

-----

## Stage 4: Inspect

The LLM is the inspection agent; the CLI provides deterministic tools for fetching the discovery surface, querying the journal, computing static reachability, and diffing declared affordances against advertised ones. Scenario construction and trace interpretation are reasoning tasks and live in the LLM.

**What the CLI provides (deterministic tools):**

- `frank inspect discovery fetch --app <url>` — fetch ALPS, JSON Home, Link headers, and SHACL shapes from the running application; return as structured JSON.
- `frank inspect reachability static --rigorous <file>` — static reachability analysis over the rigorous-format artifact; emit a structured list of states, transitions, and unreachable nodes.
- `frank inspect journal query --app <url> --trace-id <id> [--format prov-o|json]` — fetch and optionally reformat a journal trace.
- `frank inspect affordance diff --declared <file> --app <url>` — compare affordances declared in the rigorous format against affordances advertised at runtime; emit a structured diff.
- `frank inspect session start|record <finding>|end` — maintain an inspection session journal the LLM appends findings to as it explores. The session file is `.frank/inspect.<timestamp>.json`; it is not committed.

**What the LLM does (reasoning and exploration):**

- Reads the fetched discovery surface and the static reachability output.
- Drives dry runs through the protocol as each role, by issuing HTTP requests (the LLM is the client — the CLI does not script dry-runs, because scenario construction is a reasoning task).
- Interprets journal traces and correlates them with the vocabulary mappings.
- Records findings via `frank inspect session record` as it explores.
- Writes the prose inspection report at the end of the session, drawing from the structured session file.
- Proposes sketch edits (as input to Stage 5) that would close the gaps it found.

**What the developer does:** optionally drives the inspection manually, or delegates to the LLM and reviews the report.

**What the LLM reports on:**

- Reachability gaps — states or transitions declared in the rigorous format but never reachable via valid runtime interactions.
- Unreachable branches — the Z3 gate caught none of these at build time, but dynamic inspection may find paths whose guards are never satisfied under realistic input.
- Affordance mismatches — the rigorous format declares a role can do X in state Y; the runtime discovery surface does not advertise X in state Y. (This is the v7.3.0 failure class, caught dynamically rather than by tests.)
- Semantic drift — the generated ALPS profile’s IRIs do not match the vocabulary declared in the v7.3.2 lock file.
- Journal coherence — what the protocol actually did during each dry run, projected into domain-meaningful terms via PROV-O and the vocabulary mappings.

**Artifacts produced:**

- An inspection report (markdown, written by the LLM), with references back to specific sketch or rigorous-format constructs where issues were found.
- The session JSON (`.frank/inspect.<timestamp>.json`), consumable by the LLM for the next loop iteration’s lift refinement. Not committed.
- Updates to `.frank/authoring.state.json`: the inspection result (findings count, session reference) is recorded under the source artifact’s entry.

**Why this matters:** Stage 3 gates catch structural bugs; Stage 4 catches semantic bugs. Z3 proves “no deadlock is reachable”; inspection proves “the happy path an author described in plain language actually works end-to-end.” Both are necessary. The inspection stage is where the feedback loop closes — it is how the author discovers that their diagram, while formally correct, does not do what they meant.

**Failure modes in this stage:**

- *Inspection reports a gap; author disagrees.* The report includes the evidence (trace excerpts from `frank inspect journal query`, affordance diff output, vocabulary mappings). The author either accepts the gap and edits the sketch, or recognizes the report as a false positive and files a diagnostic issue against the inspection tooling.
- *Inspection is flaky across runs.* The LLM’s exploration is non-deterministic; results should be treated as high-coverage sampling, not closed proofs. For closed properties, use Z3 or conformance testing. This is a known limitation, not a bug. The CLI’s extraction tools are deterministic; the non-determinism lives entirely in the LLM’s scenario construction.
- *Running app not in sync with the rigorous format.* The affordance diff detects this immediately. Re-run the build; if the mismatch persists, the generator has a bug and the v7.3.0-class regression discipline applies.

-----

## Stage 5: Iterate

**What the developer does:** reads the inspection report. Edits the authoritative source artifact (sketch, by the default convention) in response. Commits.

**What the LLM does:** optionally proposes specific sketch edits that would close the gaps found in inspection. Because the LLM’s session JSON records the evidence for each finding, the proposed edit can cite its own provenance. The developer reviews and accepts.

**What the CLI does:** once the source artifact changes, `frank state next` re-evaluates the state file and points back to Stage 2 (lift). All downstream lock-file entries whose inputs changed are invalidated and re-enter the proposal queue.

**Artifacts produced:** a new revision of the source artifact. The loop returns to Stage 2 (lift), which re-runs against the updated sketch, which triggers Stage 3 (generate) with new gates, which triggers Stage 4 (inspect) with new evidence.

**How the loop converges:** each iteration closes gaps and tightens the correspondence between the author’s intent (the sketch), the algebra-faithful representation (the rigorous format), and the running system (the generated application). The loop terminates when the inspection stage finds no gaps the author cares about — which is the operational definition of “done” for a Frank application.

-----

## The state file

`.frank/authoring.state.json` is the coordination point between the author, the LLM, and the CLI. It is the one artifact every party reads and writes, and it is what makes the loop resumable across sessions and legible across collaborators.

**Shape (illustrative, not normative):**

```json
{
  "schemaVersion": "1",
  "project": { "root": ".", "sourceConvention": "sketch" },
  "artifacts": {
    "design/OrderFulfillment.mmd": {
      "stage": "lift",
      "gates": {
        "liftAccepted": { "status": "blocked", "reason": "3 proposals pending review" },
        "semanticResolved": { "status": "pass" },
        "generateClean": { "status": "notRun" },
        "implementability": { "status": "notRun" },
        "z3Verified": { "status": "notRun" }
      },
      "next": {
        "forAuthor": "Run `frank lift status design/OrderFulfillment.mmd` to review proposals, then `frank lift accept` when ready.",
        "forLLM": "3 proposals at `design/OrderFulfillment.lift.lock.json` await author review. Do not advance past lift until accepted."
      },
      "lastSession": {
        "kind": "lift",
        "timestamp": "2026-04-23T14:32:00Z",
        "agent": "llm",
        "summary": "Proposed role assignments for Customer, Warehouse, Courier; 3 channel effects inferred; 1 scope ambiguity flagged."
      }
    }
  }
}
```

**Who writes what:**

- The CLI writes the state file. Every command that changes artifact status, gate results, or next-step recommendations updates it atomically. The LLM and the author do not edit the state file directly; they issue CLI commands that do.
- The LLM reads the state file at the start of every session to orient: which artifacts exist, which gates are blocked, what the recommended next step is. The LLM does not trust its own context over the state file — if the state file and the LLM’s recollection disagree, the state file wins.
- The author reads the state file (directly, or via `frank state show`) to know what the loop owes them.

**Commands that interact with state:**

- `frank state show [--artifact <path>]` — print state summary; default to TTY formatting, JSON under `--json`.
- `frank state next` — print only the `next` block for the focused artifact, defaulting to the most recently modified one. The first command the LLM should run at session start.
- `frank state describe` — emit the state-file schema for LLM consumption. The LLM uses this to parse state files of any schema version without out-of-band documentation.

**Concurrency:** state-file writes are atomic (write-temp-then-rename). Concurrent writes from parallel CLI invocations are not supported in v1; the tooling should fail fast rather than merge. The commit-discipline section below treats the state file as a normal versioned artifact.

**Why this belongs in the CLI, not the LLM’s context:** LLM context windows are finite, sessions end, and LLM recollection is not reliable across runs. A committed state file makes the loop resumable by a different LLM, a different author, or the same author after a week. It is the authoring-time analog of the PROV-O journal the runtime emits — a record that makes the process legible to parties who were not present when it happened.

-----

## CLI discoverability

The CLI is the LLM’s tool surface. For the LLM to use the CLI correctly without bespoke integration, the CLI must describe itself — in the same way a Frank-generated application describes itself to runtime agents via JSON Home, ALPS, and Link headers.

**Commands for discovery:**

- `frank describe` — emit the full tool surface as JSON: every command, its arguments, input schemas, output schemas, side effects (what it reads and writes on disk, what state-file fields it touches), and links to related commands. This is the authoring-time analog of JSON Home.
- `frank describe <command>` — the same, scoped to a single command. The authoring-time analog of an ALPS profile.
- `frank <command> --help --json` — structured help for a specific command invocation, including argument shapes and examples.
- `frank describe --state-schema` — emit the state-file schema.
- `frank describe --lock-schemas` — emit the lift-lock and semantic-mapping lock schemas.

**Output conventions:**

- Commands default to JSON on stdout when not attached to a TTY, and to human-readable formatting when attached. `--json` and `--no-json` force the mode.
- Error output goes to stderr with a structured JSON body under `--json`, including an error code, a human-readable message, and — when relevant — a pointer to the state-file field or command that would resolve the error.
- Every command that advances the loop prints the `next` field from the state file on success, so the LLM’s next step is always visible without a follow-up query.

**Why this is worth the design attention:** the cost of getting CLI discoverability wrong is not that the LLM fails to use the CLI — it is that the LLM guesses. Guesses become hallucinated commands, hallucinated arguments, and hallucinated behavior, and the loop’s review-gate discipline is only as strong as the LLM’s willingness to consult the actual tool surface. A CLI that describes itself turns “what command do I run next?” from a context-window problem into a tool-call problem, which is the same shift Frank’s runtime agents rely on.

-----

## Commit discipline

The loop depends on being able to reproduce any prior state and understand how it changed. That discipline is what keeps the LLM’s role in the loop bounded to review-gated authoring-time assistance.

**What is committed to version control:**

- Sketch artifacts (authoritative source, by default convention).
- Rigorous-format artifacts (derived, but committed so they can be diffed across lift re-runs).
- Lift-lock files (`*.lift.lock.json`): the recorded mappings from sketch constructs to rigorous-format constructs, per-file.
- Semantic-mapping lock file (`.frank/semantic-mappings.lock.json`): the v7.3.2 vocabulary lock, resolving F# types to vocabulary IRIs.
- Authoring state file (`.frank/authoring.state.json`).
- Vocabulary declarations (the CE or equivalent), including prefix declarations and alignment assertions.
- Design notes where the sketch format cannot capture intent.

**What is not committed:**

- Generated F# source (emitted into `obj/` by MSBuild).
- Inspection reports and session files (`.frank/inspect.*.json`) — per-session diagnostics, not build inputs.
- Draft lift proposals recorded but not yet reviewed — these live in the lift-lock with `status: proposed` and do transit through version control, but should not merge to the default branch without an accompanying `frank lift accept`.

**Diff discipline:**

- Changes to the sketch should produce a corresponding change to the rigorous format via the lift choreography (LLM + `frank lift record`). A commit that changes only the rigorous format without a corresponding sketch change is flagged by drift detection; it is not forbidden (authors may need to work at the rigorous layer) but it needs a commit-message note explaining why, and the state file records the deviation.
- Changes to the lift-lock, semantic-mapping lock, or authoring state file are part of the same commit as the change that caused them. Lock files drifting from the artifact that should produce them is a merge-conflict hazard.
- There is no combined `build` convenience command that bypasses the review gate. `dotnet build` runs the full generate stage and fails closed on any unaccepted lift-lock or semantic-mapping proposal.

**Source-of-truth convention:**

The default is **sketch as source, rigorous format as derived** — the author’s artifact is the one under review, and the rigorous format is regenerated from it via the lift choreography. Projects where the author works directly at the rigorous layer (F# CE authors, Scribble authors writing from a formal specification) invert the convention: rigorous format as source, sketch (if any) as regenerated documentation. The per-project choice is recorded in `.frank/workflow.config.json` under `sourceConvention`, and the state file respects it; the CLI enforces drift detection in both directions.

See protocol-types ADR §13 open question 15 for the unresolved parts of this convention.

-----

## Failure modes across the loop

Every stage has its own failure modes, documented in context above. Cross-cutting failure modes worth explicit attention:

- **The lift-review gate is skipped.** If the lift choreography runs in a CI pipeline that auto-accepts proposals, the v7.3.0-class regression pattern returns at the lift layer: the generated system “works” but does not match the author’s intent. The review gate is a discipline; enforcing it procedurally (e.g., lift-lock changes in a commit require a reviewer on the PR, and `frank lift accept` is not invoked from CI without an explicit override) is the operational backstop. The state file records who accepted each proposal and when, so post-hoc audit is possible.
- **Vocabulary drift without code review.** A vocabulary schema changes upstream (schema.org updates); the CLI’s `frank semantic refresh` command records the drift in the state file; existing `confirmed` mappings are not auto-mutated. If the refresh step is skipped, generated IRIs continue to resolve against the locally-cached schema. This is the designed behavior — silent semantic changes are worse than slightly-stale schemas — but it means a vocabulary-refresh cadence must be part of the project’s maintenance discipline.
- **Inspection skipped entirely.** The loop works without inspection: sketch → lift → generate → ship. The build gates catch structural bugs and the Z3 gate catches protocol-level properties. What is lost is the semantic check: the author’s intent matches the system’s behavior. Projects that skip inspection are relying on the sketch-to-rigorous-to-generated chain being correct by construction, which is a stronger assumption than the loop’s normal operation makes. The state file records when inspection was last run per artifact, making the skip visible.
- **Multiple authors working in the loop concurrently.** Sketch merges, lift-lock merges, semantic-lock merges, and state-file merges all need conflict-resolution conventions. The lock-file pattern is deliberately designed to make these merges tractable (deterministic regeneration from source artifacts), but concurrent writes to the state file are not yet supported and the CLI needs `frank lift reconcile` and `frank semantic reconcile` commands to merge concurrent proposal sets cleanly. This is a future-work item.
- **LLM operates without reading the state file.** If the LLM acts on stale context rather than consulting `frank state next`, it can re-propose already-rejected mappings or skip gates that advanced in a prior session. The discipline is that every LLM authoring session begins with `frank state next`; the CLI’s output format is designed to make that cheap.

-----

## Current status (where the loop stands)

|Stage                      |v7.3.2                                                        |v7.4.0 (Track A)                           |v7.4.0 (Track C)                          |Stretch                                                                                    |
|---------------------------|--------------------------------------------------------------|-------------------------------------------|------------------------------------------|-------------------------------------------------------------------------------------------|
|Sketch                     |—                                                             |—                                          |—                                         |Mermaid/smcat intake                                                                       |
|Lift                       |—                                                             |—                                          |—                                         |LLM lift choreography with `frank sketch parse` / `frank lift record` / `frank lift accept`|
|Generate (semantic surface)|✓ vocabulary CE, convention engine, lock file, MSBuild codegen|—                                          |—                                         |—                                                                                          |
|Generate (protocol surface)|—                                                             |✓ CE → actors + discovery surface          |✓ role-projected SSE + AND-state broadcast|—                                                                                          |
|Inspect                    |✓ `frank semantic status`                                     |Partial — dry-run client via thesis Track A|Partial — multi-role inspection           |LLM-driven reachability exploration using `frank inspect` tool family                      |
|Iterate                    |Manual                                                        |Manual                                     |Manual                                    |LLM-proposed sketch edits driven by inspection session JSON                                |
|State file                 |—                                                             |—                                          |—                                         |`.frank/authoring.state.json` with `frank state` command family                            |
|CLI discoverability        |—                                                             |—                                          |—                                         |`frank describe` and `--help --json` across all commands                                   |

**What works today:** semantic-discovery codegen with vocabulary alignment and LLM-assisted clarification (v7.3.2). The protocol-types ADR describes the v7.4.0 algebra and codegen; the v7.3.0 rollback and v7.4.0 falsifiable-HTTP-AC discipline exist specifically to prevent the generate stage from claiming correctness it does not have.

**What v7.4.0 adds:** the protocol side of the generate stage, plus partial inspection via the thesis tracks. Track A’s `#301` naive-client e2e test is exactly the inspection stage running against the generated system.

**What the stretch goal adds:** the sketch and lift stages, the state file and its commands, CLI discoverability (`frank describe`), and LLM-driven inspection and iteration. The sketch side is the most visible change for authors but the smallest amount of new machinery — the algebra, codegen, and verification layers are already where they need to be. The larger engineering investments are (a) the deterministic CLI tool family for lift and inspect, (b) the state file as the coordination artifact, and (c) the self-describing command surface. The review gates and diagnostic surfaces need to be right because they are what keep the LLM’s role bounded.

**Why documenting the target end-state now:** the CLI commands, lock-file patterns, and review-gate disciplines being shaped in v7.3.2 are the commitments the full loop will inherit. Getting them aligned with the target workflow — rather than designed piecemeal per release — is what makes the decomposition into v7.3.2 / v7.4.0 / Track C a sequenced path rather than a collection of related projects. In particular, the CLI/LLM/state-file separation is easier to adopt now, when the command surface is small, than to retrofit once the lift and inspect stages are in use.

-----

## Related documents

- [protocol-types ADR](./superpowers/specs/2026-04-21-v740-protocol-types-design.md) — algebra, projection, codegen, verification, build order. Appendix R documents the intake pipeline this workflow consumes.
- [v7.3.2 semantic-discovery spec](./superpowers/specs/2026-04-20-v732-semantic-discovery-design.md) — vocabulary CE, convention engine, lock file, MSBuild codegen. The semantic half of Stage 3.
- [AGENT_HYPOTHESIS.md](./AGENT_HYPOTHESIS.md) — the runtime-agent thesis this workflow’s authoring agents run in parallel to. See the “Two agent roles” section for the runtime/authoring split. The design principles in this document (deterministic tool surface, self-description, state coordination) are the authoring-time mirror of the runtime-agent architecture described there.