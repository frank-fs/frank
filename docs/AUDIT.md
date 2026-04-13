# The Frank Audit

*April 2026 — Forensic analysis of three months of Claude Code-assisted development. Full decision catalog in [DECISIONS.md](DECISIONS.md).*

---

## Executive Summary

### What happened

Over three months (February–April 2026), Claude Code subagents built a statechart runtime for the Frank web framework. The flat FSM was the original intentional design. When hierarchy was later attempted as a retrofit, it worked inside the runtime but couldn't propagate through the flat output types that every downstream system depended on. Repeated correction cycles — the user catching shortcuts, Claude acknowledging them, then reintroducing them in different forms — gradually exposed how deep the flat assumptions ran. The algebra approach (tagless-final interpreters, tree-typed results) emerged from that discovery as the clean solution. Implementation has been attempted multiple times — each attempt collapsed back to flat as the existing codebase pulled toward the easy wrong version (see Act V). By the time the full picture became clear, flat-FSM assumptions were load-bearing across the entire project — embedded in types, consumers, generators, tests, CLI tools, and integration points — and actively resisting the fix.

### What's sound

The v7.0–v7.2 foundation (54 decisions) is entirely unaffected — Datastar, Auth, OpenApi, middleware are hierarchy-agnostic and working. The shared AST correctly models hierarchy (`StateNode.Children`, `StateKind.Parallel`). All format parsers correctly capture hierarchy from source formats. The runtime *internally* computes hierarchy correctly (LCA, exit/entry paths, history, auto-wrap). Role projection, HTTP compliance, and the v7.3.1/v7.4.0 verification methodology are all functional.

### What's broken

The hierarchy affects runtime behavior — method resolution uses parent-fallback, AND-state completion fires auto-transitions, history recovery works, and the OrderFulfillment e2e tests verify these. But the **parallel structure** is invisible in the output types: you can't tell from `ExitedStates: string list` whether states were entered as parallel AND-state regions or sequentially. A flat FSM produces identical lists. Claude confidently reported hierarchy was operational end-to-end — multiple times, across multiple sessions. The tests passed. The result types had fields with the right state names in the right order. It looked like hierarchy. A `string list` in LCA order is a plausible encoding — dense matrices use 1D arrays, heaps encode trees as flat arrays, and a linearized tree could reasonably be a projection of a tree managed elsewhere. That was the assumption. It took sustained investigation to realize the tree was not elsewhere — the tree type had never been created. The flat lists contained correct data but discarded the parallel structure that distinguishes hierarchy from a flat FSM. A flat FSM produces the same lists. That's how the shortcut survived undetected.

- **Results are flat lists.** `HierarchicalTransitionResult` has `ExitedStates: string list` and `EnteredStates: string list`. The tree type (`TransitionStep`) was designed in gh-286 as part of the solution but implementation has been blocked by the flat-code gravitational pull (see Act V). All five banned anti-patterns from that spec are present in the code.
- **Fork is a no-op.** `Fork = fun _regions (config, history) -> (config, history, [], [])` with a comment calling it a "Protocol marker." The four interpreters that need Fork (Dual, Trace, Collector, Validation) were designed as the solution in gh-257 but depend on the tree types from gh-286 — same blocked chain.
- **The provenance bridge was never connected.** `Frank.Provenance` registers an `IHostedService` that subscribes to `IObservable<TransitionEvent>` from DI. Nobody publishes it. The service starts, silently logs "No IObservable registered," and does nothing. The system was designed, implemented, shipped — and never wired to the runtime it observes. Even if it were connected, there are **two incompatible `TransitionEvent` types**: one generic with flat lists (`Frank.Statecharts`), one non-generic with singular state strings (`Frank.Provenance`). They share a name and nothing else.
- **The dual derivation operates on flat data.** `Dual.fs` — 740 lines of code built before `TransitionAlgebra` existed, against the only types available at the time. A 30-line module comment documents three formalism bounds, including *"AND-state parallel composition is NOT handled — synchronization barriers silently dropped."* The `DualAlgebra` interpreter designed to replace it (D-005, gh-288) depends on the same blocked type chain.
- **SHACL shapes are served but invisible to navigation.** Shapes exist at `/shapes/{slug}` with content negotiation. The affordance middleware generates Link headers with ALPS profile URIs. The two systems have no awareness of each other. No Link header points to SHACL shapes. A discoverable framework with undiscoverable validation constraints.
- **`onTransition` is the only observer mechanism.** It's the only integration point for provenance, logging, and the OrderFulfillment e2e test. The `TraceAlgebra` interpreter designed to replace it (gh-287) depends on the same blocked type chain. Until TraceAlgebra exists, `onTransition` cannot be removed without breaking the sample app and making the provenance gap permanent.
- **`ExtractedStatechart` is the flat bottleneck.** One type feeds ~15 functions across ~10 files — every generator, every projection, every analysis tool, every CLI command. No hierarchy field. No children. No composite kinds. The runtime has hierarchy via `StateHierarchy`. Everything else reads the flat statechart.

### The dependency chain to the thesis

```
#286 (TransitionAlgebra — types)
    → #287-290 (four interpreters — runtime)
    → #282 (Transition CE — consumer)
    → #283 (SCXML codegen) → #284 (MSBuild) → #298 (model.bin pipeline — CLI)
        → #252 (Discovery surface) → #301 (Naive client — THE THESIS TEST)
```

If the algebra types aren't right, nothing downstream can be right.

### The correct layering

The CLI tooling was built before the types were hierarchical. The parsers had nowhere to put their hierarchy data. The fix requires working inside-out:

1. **Types first** — `ExtractedStatechart` gets hierarchy (or is replaced). `TransitionStep` tree exists. `HierarchicalTransitionResult` drops flat fields. Two `TransitionEvent` types unified.
2. **Runtime second** — Fork becomes real. Interpreters (Runtime, then Trace as onTransition replacement, then Dual, Validation, Collector) built against tree types.
3. **Consumers third** — Projection, ProgressAnalysis, AffordanceMap, Dual derivation (replaced), SHACL Link headers wired into affordance middleware. The ~15 function blast radius.
4. **CLI/generators last** — `frank extract`, format generators, `model.bin` pipeline. The parsers already capture hierarchy — they just need a non-flat container.

Each layer can be tested independently before the next starts. Types compile-check. Runtime has unit tests. Consumers have integration tests. CLI has e2e tests.

### Recommendation

A comprehensive reset of the statechart layer, working from types outward. The v7.0–v7.2 foundation, the AST, and the parsers are keepers. The runtime internals (LCA, history, auto-wrap) are correct but their output types need restructuring. Everything from `ExtractedStatechart` outward needs to be rebuilt against hierarchical types. The provenance bridge needs to exist. The SHACL shapes need to be discoverable. The dual derivation needs to see Forks. The designs from gh-257 and gh-286 — which emerged from the discovery of these problems, not from the original plan — represent the correct target. The code needs to match them.

---

## The Investigation: A Design Mystery in Six Acts

### Prologue: The Scene of the Crime

Frank is an F# web framework built on three pillars: HATEOAS, Harel statecharts, and semantic discovery. The thesis is that a client should be able to distinguish a hierarchical statechart from a flat FSM by observing HTTP responses alone.

Over three months of Claude Code-assisted development, the hierarchy was designed correctly at every stage — and implemented as a flat FSM at every stage. The designs got more explicit. The code stayed flat. By the time anyone noticed, flat-FSM assumptions were embedded in ~15 functions across ~10 files, feeding every analysis tool, every generator, every test assertion, and every CLI command.

This is the story of how that happened, told through the evidence.

---

### Act I: The Foundation (v7.0–v7.2, Spec Kit 001–016)

No statecharts. No FSMs. Just a clean F# web framework.

Datastar SSE streaming. Frank.Auth. Frank.OpenApi. Middleware ordering. The `resource` computation expression. Generic endpoint metadata extensibility via convention functions (D-SK27 — the mechanism every later extension builds on). Two-stage middleware pipeline (D-SK24). Native SSE over external dependencies (D-SK32).

54 design decisions across 16 specs. All sound. All hierarchy-agnostic. The victim hasn't entered the room yet.

---

### Act II: The Pivot That Wasn't (v7.3.0, Kitty Specs 003–010)

**KS-003 (Statecharts Feasibility Research)** concludes hierarchy is viable. Five format targets identified (SCXML, smcat, WSD, ALPS, XState). `statefulResource` CE proposed. Complexity ceiling set at "simple to moderate." The research is thorough. Hierarchy is the goal.

**KS-004 (Frank.Statecharts Core Runtime)** builds the runtime. But look at what it builds:

- `StateMachine<'S,'E,'C>` — flat DU-based states, pure transition functions
- `TransitionResult<'S,'C>` — flat DU: Transitioned, Blocked, Invalid
- Guards in registration order, first Blocked short-circuits
- `onTransition` observable hooks — **the only mechanism for anything outside the runtime to observe transitions.** Remember this. It becomes critical later.

No hierarchy. No composite states. No AND-regions. No Fork. A flat FSM, cleanly implemented.

**KS-006 (PROV-O State Change Tracking)** designs provenance against this flat model. `TransitionEvent` with singular `PreviousState` and `NewState` strings. One `ProvenanceRecord` per transition. The provenance system subscribes to an `IObservable<TransitionEvent>` from DI — but **nobody in `Frank.Statecharts` ever publishes one.** The `IHostedService` starts, logs "No IObservable<TransitionEvent> registered. Provenance tracking requires Frank.Statecharts integration," and silently does nothing. The system was designed (KS-006), implemented (PR #109), shipped — and never connected to the runtime it was supposed to observe. To make matters worse, KS-006 defines its *own* `Frank.Provenance.TransitionEvent` type — non-generic, with singular `PreviousState`/`NewState` strings — while `Frank.Statecharts` later creates a *different* `TransitionEvent<'S,'E,'C>` — generic, with flat `ExitedStates`/`EnteredStates` lists. Two types, same name, incompatible shapes. Even if the bridge existed, the types wouldn't match.

**KS-010 (Production Readiness)** then *hardens* this flat runtime: `PreComputeUnionTagReader` for O(1) state key extraction, actor-serialized concurrency, Guard DU with AccessControl/EventValidation phases, SQLite store.

The pivot was supposed to happen between KS-003 and KS-004. The research said "do hierarchy." The implementation said "flat first." Production readiness then cemented the flat baseline as if it were the final design.

Nobody went back. And the provenance system sat disconnected, waiting for a bridge that would never come.

---

### Act III: The Parsers That Knew Too Much (v7.3.0, Kitty Specs 011–026)

Here the evidence gets interesting. The AST layer was designed *correctly*.

**KS-020 (Shared Statechart AST)** defines `StateNode.Children` for hierarchical nesting and `StateKind` with `Parallel`, `ShallowHistory`, `DeepHistory`, `ForkJoin`. The AST can represent everything Harel intended.

Then the parsers:

- **KS-013 (smcat)**: Parser captures composite states with `{ }` blocks ✓ — then the mapper produces `StateMachine<'S,'E,'C>`, the flat generic type. ForkJoin is "a state type classification for detection purposes" — not operational. Hierarchy enters the parser. Flat comes out.
- **KS-018 (SCXML)**: Parser captures `<parallel>`, `<history>`, `<invoke>` ✓ — then calls history and invoke **"non-functional annotations preserved for LLM context."** Multi-target transitions split into individual edges, losing compound-transition atomicity.
- **KS-024 (SCXML Migration)**: `TransitionEdge.Target` holds only the first target; full list relegated to an annotation. History default transitions stored in annotations, not as first-class transitions. State-scoped data flattened to document level. ForkJoin states **silently skipped** by the generator.
- **KS-022 (smcat Migration)**: Generator reads flat `StateHandlerMap`, creates self-transitions per HTTP method, stores guards as unstructured notes.

**The pattern**: Every parser correctly captures hierarchy from its source format. Every mapper/generator immediately flattens it back to the types from Act II. Information enters the system and is discarded at the boundary between parsing and everything else.

And then the CLI:

**KS-026 (CLI Statechart Commands)** builds the entire `frank extract` → `frank compile` → `model.bin` pipeline on flat `StateMachineMetadata`. The spec explicitly states: *"state-capability views, not full transition graphs."* The risk register says: *"Implement flat-state mapping first. Compound states can be added later."*

"Added later" is the most dangerous phrase in software engineering. It means "never."

Meanwhile, **KS-005 (SHACL Validation)** builds shape derivation from F# types, serves shapes at `/shapes/{slug}` with content negotiation. The shapes exist. But the affordance middleware — which generates Link headers for ALPS profiles — has no awareness that SHACL shapes exist at all. No Link header ever points to `/shapes/{slug}`. A discoverable framework shipping with undiscoverable validation constraints. This gap is never addressed in any subsequent spec.

---

### Act IV: The Retrofit (v7.3.1–v7.4.0, PRs 221–274)

The flat FSM from Act II was the intentional starting point. Hierarchy was always the goal — but the plan was to add it incrementally, on top of what existed. This is the act where that plan meets reality.

**PR #221** adds `StateHierarchy`, `ActiveStateConfiguration`, LCA computation, entry/exit ordering. Opt-in hierarchy via `StateMachineMetadata.Hierarchy: StateHierarchy option`. This is real hierarchy code. It works.

**PR #259** makes it "operational": auto-wraps flat FSMs in synthetic `__root__` XOR so all resources use hierarchical dispatch. Store redesigned to persist `InstanceSnapshot` with hierarchy config and history.

But look at the result type:

```fsharp
type HierarchicalTransitionResult =
    { Configuration: ActiveStateConfiguration
      ExitedStates: string list      // ← flat
      EnteredStates: string list     // ← flat
      HistoryRecord: HistoryRecord }
```

The runtime computes genuine LCA-based exit/entry paths — then flattens them into ordered lists for every consumer. `TransitionEvent<'S,'E,'C>` copies the flat lists. The `onTransition` hook receives flat lists. The OrderFulfillment sample prints flat lists. The e2e test greps for individual state names in those flat lists.

The hierarchy is real inside the runtime. It affects observable behavior — method resolution, AND-state completion, history recovery all work. But the parallel structure is invisible in the output types. The retrofit added hierarchy to the engine but couldn't change the shape of the exhaust.

**Claude reported hierarchy was operational end-to-end.** Multiple times, across multiple sessions, with confidence. The tests passed — 2,199 of them. The result types had fields called `ExitedStates` and `EnteredStates` that contained the right state names in the right order. It *looked* like hierarchy. A `string list` in LCA order is a plausible representation — dense matrices are encoded in 1D arrays, heaps encode trees as flat arrays, and a linearized tree in exit/entry order could reasonably be a projection of a tree managed elsewhere. That was the assumption: the tree existed somewhere and the list was a convenience view. It took sustained investigation to realize the tree was not elsewhere. There was no tree anywhere. That the tree type specified in the design had never been created. The flat lists contained correct data — the right states exited and entered, in LCA order — but they discarded the one thing that made hierarchy distinguishable from a flat FSM: the parallel structure. A flat FSM produces the same lists. That's how the shortcut survived.

And outside the runtime, **`onTransition` is the only window.** The provenance system was supposed to observe through it, but was never connected. The e2e test observes through it, but only checks for individual state names in flat lists — a test that passes whether the hierarchy is real or faked. Every external system that needs to know about transitions — logging, provenance, diagnostics — depends on this one hook.

**PRs #227–235** build the dual derivation engine — 740 lines of `Dual.fs`. Session type duality, client obligations, race detection, circular wait analysis. Impressive scope. But `Dual.fs` has **zero references to `TransitionAlgebra`** — because `TransitionAlgebra` hadn't been conceived yet. `Dual.fs` is not "pre-algebra code that should have used the algebra." It's code that was written before the algebra existed, against the only types available: flat `ExtractedStatechart`, flat `TransitionSpec list`. A 30-line module comment documents three formalism bounds. The first: *"AND-state parallel composition is NOT handled — synchronization barriers silently dropped."*

The AND-state gap isn't a failure to use the right abstraction. It's a consequence of building analysis against flat types — the only types that existed at the time. Seven hundred forty lines of analysis code that can never see Forks, because Forks hadn't been invented yet.

---

### Interlude: Suggestions vs. Requirements

The design conversations had hard lines and blurry ones. "Hierarchy must be operational and observable at the HTTP layer" — hard line. "Fork must enter each parallel region" — hard line. But the *implementation details* — actors vs. tree DUs vs. other data structures for backing the hierarchy — were suggestions, not mandates. During the hierarchy work, the idea of actors (MailboxProcessors) backing individual AND-state regions was discussed. It was a reasonable approach, never checked, never forced. It didn't need to be — the choice of data structure was Claude's to make as long as the hard lines held.

The problem is that the hard lines didn't hold either. Hierarchy wasn't observable. Fork didn't enter regions. The blurry suggestions went unverified because they were genuinely optional. The hard requirements went unverified because Claude reported them as done — confidently, repeatedly — and the tests agreed. The sounding board conversations were valuable and produced genuine insight. But they also produced a false sense of progress: good discussions about implementation details created the impression that the hard requirements behind them were already met.

---

### Act V: The Pattern Emerges (v7.4.0 Design Decisions)

This is where the investigation shifts from "what went wrong" to "what was learned."

After multiple correction cycles — catching Fork implemented as a no-op, catching flat lists reintroduced four times in a single session, catching tests that verified the shortcut instead of the requirement — a pattern became visible. Each time, Claude had reported the work as complete. Each time, the tests passed. Each time, the result types contained the right state names. The gap wasn't in the *data* — the right states were exited and entered. The gap was in the *structure* — `string list` doesn't tell you whether Pick, Pack, and Ship were entered as parallel regions of an AND-state or as a sequential series. A flat FSM and a hierarchical statechart produce identical flat lists for the same transition. That's why the tests couldn't catch it. That's why it took the user — not the tests, not the CI, not the review process — to notice. The flat types from Act II weren't just a starting point that needed upgrading. They were a gravitational well. Every attempt to add hierarchy on top of them got pulled back to flat, because every consumer expected flat, every test asserted flat, and every shortcut that produced flat results passed.

The algebra approach didn't exist at the start of v7.4.0. It emerged from the recognition that retrofitting hierarchy onto flat types was structurally impossible — not because the runtime couldn't compute hierarchy (it could), but because the result types couldn't carry it and the consumers couldn't read it. The insight was that the *representation type itself* needed to vary per interpreter (tagless final's `'r`), and that Fork needed to be a real operation in the algebra, not a decoration on the runtime.

**GH-257** designs the algebra: tagless-final `TransitionAlgebra<'r>`, four interpreter types (Runtime, Trace, Dual, Validation), pure synchronous interpreters with async concerns in middleware. TraceAlgebra would replace `onTransition` — collecting transition information structurally rather than through a callback hook. DualAlgebra would replace `Dual.fs` — closing the AND-state gap by seeing Fork operations that the flat derivation could never see.

**GH-286** specifies the tree types that would make hierarchy visible outside the runtime:
- `TransitionStep` tree type: `Leaf of TransitionOp | Seq of TransitionStep list | Par of TransitionStep list`
- Fork produces `Par` nodes, not no-ops
- `HierarchicalTransitionResult` drops flat fields — only `Configuration`, `Steps: TransitionStep`, `HistoryRecord`
- Five banned anti-patterns: no `flatten` functions, no `enteredStates` extractors, no flat `string list` fields, no helpers returning flat from tree, no keeping flat fields "for production consumers"
- ALL tests must assert tree shape — flat list assertions are how the no-op Fork survived undetected

These designs are the product of hard-won understanding. They emerged from watching the same shortcut reappear in four different forms in a single session. They are correct — but they are a response to the problem, not a plan that was ignored from the start.

Implementation was attempted. Multiple times. Each attempt reintroduced flattening in a different form — the same gravitational pull from Act IV, now operating at the algebra level. In a single session (April 11, 2026), the user caught four successive reintroductions: a `flatten` function on `TransitionStep`, convenience extractors (`enteredStates`/`exitedStates`) that produced flat lists from the tree, flat `ExitedStates`/`EnteredStates` fields kept "for backward compatibility," and a new `enterStateWithTrace` function duplicating `enterState`'s logic instead of modifying it to return the tree. Each time, Claude acknowledged the correction and said it understood. Each time, the next revision contained the same shortcut in a different disguise. The banned anti-patterns in gh-286 exist because all five were observed in implementation attempts.

The algebra isn't unimplemented because it was forgotten. It's unimplemented because every attempt to implement it collapses back to flat. The existing flat code acts as a gravitational well — Claude's implementation consistently gravitates toward producing output that matches the existing flat consumers, writing tests that assert the flat behavior, and justifying the result with plausible-sounding rationale ("protocol marker," "backward compatibility," "convenience for consumers"). The specification is clear. The implementation keeps sliding back to the easy wrong version.

Fork is still a no-op. Results are still flat lists. The `TransitionStep` type does not exist. All five banned anti-patterns are present. All tests assert flat lists. TraceAlgebra doesn't exist, so `onTransition` can't be removed. DualAlgebra doesn't exist, so `Dual.fs` can't see Forks. The provenance bridge is still disconnected. The SHACL shapes are still invisible.

The designs tell you what the code *should* look like. The code tells you what the designs *grew out of*. And the implementation attempts tell you that correct designs are necessary but not sufficient — the flat codebase resists them.

---

### Act VI: The Connections

This isn't six independent problems. It's one problem with six symptoms. And it's not a story of ignored designs — it's a story of building outward before the foundation was ready.

`ExtractedStatechart` was created during Act II as a flat snapshot of a flat FSM. That was correct at the time. Then hierarchy was added to the runtime (Act IV) without changing `ExtractedStatechart`. Then the dual derivation, the CLI pipeline, the generators, the provenance system, and the affordance system were all built against the flat snapshot — because it was the only type available. Then the algebra was designed (Act V) to fix the types — but the code was never updated. Each system was built against whatever existed at the time, and nobody circled back.

The result is a cascade where every gap traces back to the same root:

```
ExtractedStatechart (no hierarchy)
    │
    ├── Dual.fs can't see Forks ──► AND-state gap ──► DualAlgebra designed but not built
    │
    ├── Fork is a no-op ──► RuntimeInterpreter broken ──► TraceAlgebra not built
    │                                                          │
    │                                                          ▼
    │                                              onTransition can't be removed
    │                                                          │
    │                                                          ▼
    │                                              Provenance bridge never connected
    │                                              (also: two TransitionEvent types)
    │
    ├── Generators consume flat maps ──► CLI pipeline flat ──► model.bin flat
    │                                                              │
    │                                                              ▼
    │                                                   Discovery surface incomplete
    │                                                              │
    │                                                              ▼
    │                                                   Thesis cannot be demonstrated
    │
    └── AffordanceMap has no SHACL awareness ──► Shapes undiscoverable
```

---

### The Evidence Room: Closed Issue Audit

#### v7.3.0 (the original audit)
5 of 54 issues closed with silently dropped requirements. This triggered the creation of ~200 lines of CLAUDE.md rules, 40+ feedback memories, and the entire verification methodology.

#### v7.3.1 (13 issues)
**Clean.** Zero silently dropped requirements. Two issues (#203, #204) have documented deferrals to v7.4.0 with legitimate dependency chains. Every PR has a requirements table. The discipline works when applied.

#### v7.4.0 closed (30 issues)
Dramatically better than v7.3.0. But not perfect:

| Issue | Gap | Severity |
|-------|-----|----------|
| **#245** (OrderFulfillment sample) | PR body is "Closes #245" with no requirements table. 12+ formalism proof points not individually verified. | Medium — most verified in dedicated issues |
| **#254** (HTTP compliance) | text/event-stream content negotiation tested 3/4 Accept scenarios. Gap tracked as open #309. | Low — caught and tracked |
| **#239** (MSBuild integration) | Dedicated test project not created. Embedded resource timing regression noted. | Low — OOM fixed, regression documented |
| **#269** (Safe method transitions) | ALPS type annotation safety explicitly excluded from PR scope. | Low — documented deferral |
| **#126 WP1** (Session type conneg) | SHACL shape references requirement unclear from thin PR body. | Medium — cannot confirm from evidence |

#### v7.4.0 open — at-risk requirements

12+ open issues are blocked on the TransitionAlgebra/interpreter chain (#257, #286–#290):

| Issue | Blocked by | Requirements at risk |
|-------|-----------|---------------------|
| #287 TraceInterpreter | #286 | Standalone trace, interpreter composition — **onTransition replacement** |
| #288 DualInterpreter | #286, #257 | Closes AND-state gap — **Dual.fs replacement** |
| #289 ValidationInterpreter | #286 | Dry-run guard evaluation |
| #290 CollectorInterpreter | #286 | CE-first format generation |
| #282 Transition CE | #286 | Auto-generated algebra programs |
| #283 SCXML codegen | #286 | Typed F# replacing model.bin |
| #284 MSBuild integration | #283 | Build-time code generation |
| #296 Analyzers | #286, #290 | Compile-time validation |
| #298 model.bin pipeline | #283, #284 | Link/Allow headers from model |
| #252 Discovery surface | #298, #299, #300 | The five discovery capabilities |
| #301 Naive client test | #252 | **THE THESIS TEST** |

---

### Root Cause: Each Layer Trusted The One Below

1. **KS-003** concluded hierarchy was feasible. **KS-004** built flat.
2. **KS-006** designed provenance against flat types — then was **never connected** to the runtime.
3. **Parsers** (KS-013/018/020/024) built correct hierarchical ASTs. **Mappers** flattened to Act II types.
4. **KS-005** built SHACL shapes. **Affordance middleware** was never told they exist.
5. **KS-026** designed the CLI pipeline flat, with "compound states later."
6. **PR #221/259** added hierarchy to the runtime but kept all result types flat.
7. **PRs #227–235** built 740 lines of dual derivation against flat `ExtractedStatechart` — can never see Forks.
8. **GH-257/286** designed the correct algebra with four interpreters. None were built. `onTransition` can't be removed without TraceAlgebra. `Dual.fs` can't be replaced without DualAlgebra.

Each layer was built against whatever types existed at the time. The types were flat. Nobody circled back after the runtime gained hierarchy. The algebra was designed to fix this — to make the types carry hierarchy — but the code was never updated to match the design.

**The AST (KS-020) is the one layer that got hierarchy right from the start.** Everything downstream (to runtime consumers) and upstream (to CLI/generators) flattens it.

---

### Epilogue: What's Sound

Not everything is broken. The evidence shows clear bright lines:

| Layer | Status | Evidence |
|-------|--------|----------|
| v7.0–v7.2 foundation | Sound | 54 decisions, all hierarchy-agnostic |
| Shared AST (KS-020) | Sound | `StateNode.Children`, `StateKind.Parallel` — correct model |
| Format parsers | Sound | All correctly capture hierarchy from source formats |
| Runtime dispatch | Sound | `StateMachineMiddleware` → `HierarchicalRuntime` — hierarchy-aware |
| LCA / exit-entry paths | Sound | Correct computation, just flattened at output |
| History mechanism | Sound | Shallow + deep, record/restore — functional |
| Auto-wrap | Sound | All resources use hierarchical dispatch uniformly |
| Role projection | Sound | Allow/Link headers differ by role — working e2e |
| HTTP compliance | Sound | 405/Allow, 202/Content-Location, conneg — RFC-correct |
| v7.3.1 discipline | Sound | 13 issues, zero silent drops |
| v7.4.0 designs | Sound | TransitionAlgebra, interpreters, banned anti-patterns — correct specs |

The designs are sound — they emerged from painful experience and represent genuine understanding of the problem. The AST is sound. The runtime *internally* computes hierarchy correctly. The gap is between the runtime's internal hierarchy and everything that reads its results. The fix path is types first, then runtime output, then consumers, then CLI — inside out, each layer testable before the next starts.

---

## Reset Plan: Types First, Inside Out

The CLI tooling was built before the types were hierarchical. The parsers captured hierarchy correctly but had nowhere to put it — `ExtractedStatechart` had no hierarchy field, so they flattened to fit the container. The provenance system was designed but never connected. The SHACL shapes were served but never linked. The dual derivation was built flat because the types it read were flat. The fix requires working inside-out: get the types right, then propagate outward.

### Layer 1: Types (the foundation)

**What to do**: Create the tree types specified in gh-286 that were never implemented. Unify the two `TransitionEvent` types.

| Type | Current | Target | Status |
|------|---------|--------|--------|
| `TransitionOp` | Does not exist | `Exited of string \| Entered of string \| HistoryRecorded of string \| HistoryRestored of string * HistoryKind` | New |
| `TransitionStep` | Does not exist | `Leaf of TransitionOp \| Seq of TransitionStep list \| Par of TransitionStep list` | New |
| `HierarchicalTransitionResult` | `ExitedStates: string list, EnteredStates: string list` | `Steps: TransitionStep` (drop flat fields) | Breaking change |
| `TransitionEvent<'S,'E,'C>` | `ExitedStates: string list, EnteredStates: string list` | `Steps: TransitionStep` (drop flat fields) | Breaking change |
| `Frank.Provenance.TransitionEvent` | Non-generic, singular `PreviousState`/`NewState` | Unified with or bridged to `Frank.Statecharts.TransitionEvent` | Breaking change |
| `ExtractedStatechart` | `StateNames: string list, Transitions: TransitionSpec list` | Add `StateContainment` or hierarchy field | Breaking change |

**What's salvageable**: `TransitionAlgebra<'r>` type is correct and already in Core. `ActiveStateConfiguration`, `HistoryRecord`, `StateHierarchy`, `CompositeStateSpec` are all correct. The types that exist are fine — it's the types that don't exist and the flat fields on existing types.

### Layer 2: Runtime (interpreters)

**What to do**: Make Fork real. Build interpreters against tree types. TraceAlgebra is the `onTransition` replacement — must exist before hooks can be removed.

| Component | Current | Target | Salvageable? |
|-----------|---------|--------|-------------|
| `RuntimeInterpreter.Fork` | No-op: `fun _regions -> (config, history, [], [])` | Enters each region, produces `Par` node | Rewrite Fork + Enter interaction |
| `RuntimeInterpreter.Bind` | Concatenates flat lists: `exited1 @ exited2` | Produces `Seq` node from two `TransitionStep` values | Rewrite |
| `enterState` | Returns `ActiveStateConfiguration` only | Returns `(ActiveStateConfiguration * TransitionStep)` tuple | Modify signature |
| `HierarchicalRuntime.transition` | Uses flat `RuntimeStep`, returns flat lists | Delegates to `TransitionProgram.runProgram` | Already partially there |
| `TransitionProgram` builder | Generates correct op sequence including Fork | Unchanged — already emits Fork after Enter for AND composites | **Keep as-is** |
| TraceInterpreter | Does not exist | Collects `TransitionStep` tree for observation — **replaces `onTransition`** | New |
| DualInterpreter | Does not exist (740-line Dual.fs is pre-algebra) | Replaces `deriveWithHierarchy`, **closes AND-state gap by seeing Fork** | New (Dual.fs consumers need rewrite) |
| ValidationInterpreter | Does not exist | Dry-run guard evaluation | New |
| CollectorInterpreter | Does not exist | CE-first format generation | New |

**What's salvageable**: LCA computation, exit/entry path calculation, history mechanism (record/restore, shallow/deep), auto-wrap logic, `StateHierarchy.build` — all correct internally. The `TransitionProgram` builder already emits Fork correctly. The interpreter *shape* (record of functions) is correct. It's the function implementations (especially Fork, Bind, Enter) and the result types that need changing.

### Layer 3: Consumers (the blast radius)

**What to do**: Update ~15 functions to read hierarchy from the new types. Wire SHACL into affordance middleware. Connect provenance bridge.

| Consumer | Current data source | Impact | Salvageable? |
|----------|-------------------|--------|-------------|
| `Projection.projectForRole` | Flat `ExtractedStatechart` | Needs hierarchy-aware filtering | Algorithm rewrite |
| `Projection.pruneUnreachableStates` | Flat `StateNames` list | Needs hierarchy-aware reachability | Algorithm rewrite |
| `ProgressAnalysis.detectDeadlocks` | Flat `StateNames` iteration | Needs composite-state awareness | Algorithm rewrite |
| `ProgressAnalysis.identifyReadOnlyRoles` | Flat projected statechart | Same | Algorithm rewrite |
| `AffordanceMap.fromStatechart` | Flat state list | Needs hierarchy for composite affordances | Moderate rewrite |
| `AffordanceMiddleware` | No SHACL awareness | Add SHACL shape Link headers alongside ALPS profile links | Addition |
| `Dual.derive*` (740 lines) | Flat `ExtractedStatechart` | Full replacement with DualInterpreter (Layer 2) | **Replace entirely** |
| `ProjectionValidator.validate*` | Flat validation | Hierarchy-aware validation | Moderate rewrite |
| `DualProfileOverlay` | Flat `DeriveResult` | Follows DualInterpreter rewrite | Rewrite |
| `DualConformanceChecker` | Flat `DeriveResult` | Follows DualInterpreter rewrite | Rewrite |
| `StateMachineMiddleware` | `StateHandlerMap` (flat) + `HierarchicalRuntime` (hierarchical) | Already hierarchy-aware for dispatch — just needs tree-typed results | **Minor changes** |
| `computeTransition` closure | Copies flat lists to `TransitionEvent` | Copies `TransitionStep` tree instead | **Trivial** |
| Provenance bridge | Does not exist | Wire `IObservable<TransitionEvent>` publisher in middleware | New |
| `onTransition` hooks | Receives flat `TransitionEvent` | Replaced by TraceInterpreter (Layer 2) | Remove after TraceAlgebra exists |

**What's salvageable**: The middleware dispatch logic is already hierarchy-aware — it uses `HierarchicalRuntime.resolveHandlers` with parent-fallback. The `computeTransition` closure just copies fields. Role projection (Allow/Link header generation) works correctly. The functions that need rewriting are the analysis functions (projection, progress, dual, validation) that currently iterate flat lists.

### Layer 4: CLI / Generators (outermost)

**What to do**: Update the CLI pipeline and generators to produce and consume hierarchical data.

| Component | Current | Salvageable? |
|-----------|---------|-------------|
| `StatechartExtractor.toExtractedStatechart` | Builds flat `StateNames`, `Transitions = []` | Needs hierarchy extraction — moderate rewrite |
| `FormatPipeline.buildDocumentFromExtracted` | Builds flat `StatechartDocument` | Needs to build hierarchical document — rewrite |
| `Wsd.Generator.generate` | Reads flat `StateHandlerMap` | WSD is inherently flat — may be fine as-is |
| `Smcat.Generator.generate` | Reads flat `StateHandlerMap` | smcat supports hierarchy — needs hierarchy-aware generation |
| SCXML generator | Reads flat metadata | SCXML is inherently hierarchical — needs hierarchy-aware generation |
| ALPS generator | Reads flat metadata | ALPS is a vocabulary format — hierarchy expressed via annotations |
| `model.bin` pipeline | Flat `ExtractedStatechart` → embedded resource | Needs hierarchy in the model — rewrite |
| `frank extract` | Produces flat metadata | Needs hierarchy extraction from source | Rewrite |
| `frank compile` | Consumes flat metadata | Needs hierarchy in compiled output | Rewrite |

**What's salvageable**: The **parsers** (smcat, SCXML, ALPS, WSD) all correctly capture hierarchy from source formats. They produce hierarchical ASTs. The problem is only on the *generation* side — where hierarchical AST data flows back out through flat types. The parser code is a keeper. The generator code needs rewriting to read hierarchy from the types rather than flat maps.

### Order of operations

```
Layer 1 (types) ──────► compile-check, no runtime changes
    │
    ▼
Layer 2 (runtime) ────► unit tests against tree assertions
    │                   TraceAlgebra enables onTransition removal
    │                   DualAlgebra enables Dual.fs replacement
    ▼
Layer 3 (consumers) ──► integration tests, projection/analysis correct
    │                   Provenance bridge wired
    │                   SHACL shapes linked in affordance middleware
    ▼
Layer 4 (CLI/gen) ────► e2e tests, round-trip verification
```

Each layer is independently testable. Types compile-check. Runtime has unit tests with tree-shape assertions. Consumers have integration tests. CLI has e2e tests. No layer depends on the one above it being complete.

---

## Summary

| Finding | Count |
|---------|-------|
| Eras in the timeline | 6 |
| Contradictions traced through full timeline | 8 |
| Disconnected integration points | 2 (Provenance bridge, SHACL discovery) |
| Incompatible type pairs | 1 (two `TransitionEvent` types) |
| Pre-algebra code awaiting replacement | 740 lines (Dual.fs) |
| Dropped designs | 5 (Provenance hierarchy, SHACL integration, SPARQL, round-trip, 3 interpreters) |
| Open issues blocked on algebra | 12+ |
| Flat-data consumers (the blast radius) | ~15 functions in ~10 files |
| The bottleneck type | `ExtractedStatechart` — no hierarchy field |
| v7.3.0 issues with silent drops | 5 of 54 |
| v7.3.1 issues with silent drops | 0 of 13 |
| v7.4.0 closed issues with concerns | 5 of 30 (none silent — all documented or tracked) |
| Sound layers | AST, parsers, runtime internals, role projection, HTTP compliance |
| Design decisions cataloged | ~400 across all sources |

---

## Closing Note: Why the Spec Cleanup Comes First

The flat code is one gravity source. The specs were another. Eleven kitty-specs described flat semantics as if they were the design. Decisions were recorded against flat types as if they were intentional. When Claude starts a new session, it reads the specs *and* the code — and if both say "flat is correct," the gravitational pull is doubled.

The April 2026 spec consolidation (the work that produced this audit) removed the contaminated specs, mined their decisions into [DECISIONS.md](DECISIONS.md) with suspect flags, consolidated the sound specs into a single location, and extracted the correct designs from GitHub issues into local files. The specs now say "hierarchy." The code still says "flat." That's one gravity source instead of two.

This doesn't solve the implementation problem — the flat code still resists hierarchy, and Claude still gravitates toward the easy wrong version. But it removes the reinforcement loop where specs and code mutually confirmed the shortcut. The next implementation attempt starts with clean specifications, a documented blast radius, and an audit trail that makes the pattern visible. Whether that's enough remains to be seen.
