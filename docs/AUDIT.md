# The Frank Audit

*April 2026 — Forensic analysis of three months of Claude Code-assisted development. Full decision catalog in [DECISIONS.md](DECISIONS.md).*

---

## Executive Summary

### What happened

Over three months (February–April 2026), Claude Code subagents implemented a hierarchical statechart runtime for the Frank web framework. The designs were correct at every stage. The implementations were flat at every stage. By the time the gap was discovered, flat-FSM assumptions had become load-bearing across the entire project — embedded in types, consumers, generators, tests, CLI tools, and integration points.

### What's sound

The v7.0–v7.2 foundation (54 decisions) is entirely unaffected — Datastar, Auth, OpenApi, middleware are hierarchy-agnostic and working. The shared AST correctly models hierarchy (`StateNode.Children`, `StateKind.Parallel`). All format parsers correctly capture hierarchy from source formats. The runtime *internally* computes hierarchy correctly (LCA, exit/entry paths, history, auto-wrap). Role projection, HTTP compliance, and the v7.3.1/v7.4.0 verification methodology are all functional.

### What's broken

The hierarchy is real inside the runtime and invisible to everything outside it. Results are flat lists. Fork is a no-op. `TransitionStep` (the tree type) was specified but never created. `ExtractedStatechart` — the type that feeds every analysis tool, generator, projection, and CLI command — has no hierarchy field. The provenance bridge was never connected. The dual derivation operates on flat data. SHACL shapes are served but undiscoverable via hypermedia. The only observer mechanism (`onTransition`) is scheduled for removal with no replacement built.

### The bottleneck

One type feeds ~15 functions across ~10 files:

```
ExtractedStatechart { StateNames: string list; Transitions: TransitionSpec list }
```

No hierarchy. No children. No composite kinds. This is the type every downstream system reads. The runtime has hierarchy via `StateHierarchy`. The consumers read the flat statechart. Two parallel views of the same data — one hierarchical, one flat — and the flat one won.

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

1. **Types first** — `ExtractedStatechart` gets hierarchy (or is replaced). `TransitionStep` tree exists. `HierarchicalTransitionResult` drops flat fields. These are the gh-286 decisions that were specified but never implemented.
2. **Runtime second** — Fork becomes real. Interpreters (Runtime, Trace, Dual, Validation, Collector) built against tree types.
3. **Consumers third** — Projection, ProgressAnalysis, AffordanceMap, Dual derivation updated to read hierarchy. The ~15 function blast radius.
4. **CLI/generators last** — `frank extract`, format generators, `model.bin` pipeline updated. The parsers already capture hierarchy — they just need a non-flat container.

Each layer can be tested independently before the next starts. Types compile-check. Runtime has unit tests. Consumers have integration tests. CLI has e2e tests.

### Recommendation

A comprehensive reset of the statechart layer, working from types outward. The v7.0–v7.2 foundation, the AST, and the parsers are keepers. The runtime internals (LCA, history, auto-wrap) are correct but their output types need restructuring. Everything from `ExtractedStatechart` outward needs to be rebuilt against hierarchical types. The designs from gh-257 and gh-286 are the correct specifications — the code needs to match them.

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

### Act II: The Pivot That Wasn't (v7.3.0, Kitty Specs 003–004)

**KS-003 (Statecharts Feasibility Research)** concludes hierarchy is viable. Five format targets identified (SCXML, smcat, WSD, ALPS, XState). `statefulResource` CE proposed. Complexity ceiling set at "simple to moderate." The research is thorough. Hierarchy is the goal.

**KS-004 (Frank.Statecharts Core Runtime)** builds the runtime. But look at what it builds:

- `StateMachine<'S,'E,'C>` — flat DU-based states, pure transition functions
- `TransitionResult<'S,'C>` — flat DU: Transitioned, Blocked, Invalid
- Guards in registration order, first Blocked short-circuits
- `onTransition` observable hooks

No hierarchy. No composite states. No AND-regions. No Fork. A flat FSM, cleanly implemented.

**KS-010 (Production Readiness)** then *hardens* this flat runtime: `PreComputeUnionTagReader` for O(1) state key extraction, actor-serialized concurrency, Guard DU with AccessControl/EventValidation phases, SQLite store.

The pivot was supposed to happen between KS-003 and KS-004. The research said "do hierarchy." The implementation said "flat first." Production readiness then cemented the flat baseline as if it were the final design.

Nobody went back.

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

---

### Act IV: The Body in the Library (v7.3.1–v7.4.0, PRs 221–274)

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

The hierarchy is real inside the runtime. It's invisible to everything outside it.

---

### Act V: The Detective's Notebook (v7.4.0 Design Decisions)

**GH-257** designs the correct algebra: tagless-final `TransitionAlgebra<'r>`, four interpreter types (Runtime, Trace, Dual, Validation), pure synchronous interpreters with async concerns in middleware.

**GH-286** specifies the fix in precise detail:
- `TransitionStep` tree type: `Leaf of TransitionOp | Seq of TransitionStep list | Par of TransitionStep list`
- Fork produces `Par` nodes, not no-ops
- `HierarchicalTransitionResult` drops flat fields — only `Configuration`, `Steps: TransitionStep`, `HistoryRecord`
- Five banned anti-patterns: no `flatten` functions, no `enteredStates` extractors, no flat `string list` fields, no helpers returning flat from tree, no keeping flat fields "for production consumers"
- ALL tests must assert tree shape — flat list assertions are how the no-op Fork survived undetected

The designs are correct. They were collaboratively refined. They specify exactly what the code should look like.

The code was never updated. Fork is still a no-op. Results are still flat lists. The `TransitionStep` type does not exist. All five banned anti-patterns are present. All tests assert flat lists.

---

### Act VI: The Suspects Lineup

#### Suspect 1: The Provenance Bridge That Doesn't Exist

`Frank.Provenance` registers an `IHostedService` that subscribes to `IObservable<TransitionEvent>` from DI. **Nobody publishes it.** The service starts, logs "No IObservable<TransitionEvent> registered," and silently does nothing.

Even if the bridge existed, there are **two incompatible `TransitionEvent` types**: `Frank.Statecharts.TransitionEvent<'S,'E,'C>` (generic, carries flat `ExitedStates`/`EnteredStates` lists) and `Frank.Provenance.TransitionEvent` (non-generic, carries singular `PreviousState`/`NewState` strings). They share a name. They share nothing else.

The provenance system was designed (KS-006), implemented (PR #109), shipped — and never connected to the runtime it's supposed to observe.

#### Suspect 2: The Dual Derivation That Can't See Forks

`Dual.fs` — 740 lines of pre-algebra code. Zero references to `TransitionAlgebra`. Operates entirely on flat `ExtractedStatechart`. Has a 30-line module comment documenting three formalism bounds, including: *"AND-state parallel composition is NOT handled — synchronization barriers silently dropped."*

D-005 says replace it entirely with a `DualAlgebra` interpreter. Nothing has started. Every consumer of `DeriveResult` — `DualAlpsGenerator`, `DualConformanceChecker`, `DualProfileOverlay` — assumes flat per-state snapshots. Replacing `Dual.fs` means rewriting all of them.

#### Suspect 3: The SHACL Shapes Nobody Can Find

SHACL shapes are served at `/shapes/{slug}` with content negotiation (Turtle, JSON-LD, RDF/XML). They exist. They're pre-computed at startup.

The affordance middleware generates Link headers with ALPS profile URIs. It has no awareness of SHACL shapes. No Link header points to them. They are invisible to hypermedia navigation — a discoverable framework with undiscoverable validation constraints.

#### Suspect 4: The Only Observer In Town

`onTransition` is the only integration point between the statechart runtime and any external system. D-006 says eliminate it — "Customization happens through interpreters." But:

- The OrderFulfillment e2e test depends on it (AT3 checks stderr logs from the hook)
- 6+ unit tests directly test hook behavior
- The provenance system's *intended* design uses it as the subscription mechanism
- The replacement (TraceAlgebra interpreter) doesn't exist

Removing `onTransition` without TraceAlgebra makes the provenance gap permanent and breaks the thesis demo.

#### Suspect 5: `ExtractedStatechart` — The Flat Bottleneck

The real culprit. Every analysis tool, every generator, every projection, every CLI command reads from `ExtractedStatechart`:

```
ExtractedStatechart { StateNames: string list; Transitions: TransitionSpec list }
    ├── Wsd.Generator.generate()
    ├── Smcat.Generator.generate()
    ├── FormatPipeline.buildDocumentFromExtracted()
    ├── Projection.projectForRole / projectAll
    ├── Dual.derive* ("operates on flat ExtractedStatechart")
    ├── ProgressAnalysis.analyzeProgress / detectDeadlocks
    ├── AffordanceMap.fromStatechart
    ├── ProjectionValidator.validate*
    └── CLI TextOutput / JsonOutput
```

No hierarchy field. No children. No composite kinds. `StateMachineMetadata` carries *both* `Hierarchy: StateHierarchy` (hierarchical) and `Statechart: ExtractedStatechart option` (flat). The runtime reads the hierarchy. Everything else reads the flat statechart.

One type. Fifteen functions. Ten files. That's the blast radius.

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
| #287 TraceInterpreter | #286 | Standalone trace, interpreter composition |
| #288 DualInterpreter | #286, #257 | Closes AND-state gap (formalism bound 1) |
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
2. **Parsers** (KS-013/018/020/024) built correct hierarchical ASTs. **Mappers** flattened to Act II types.
3. **KS-026** designed the CLI pipeline flat, with "compound states later."
4. **PR #221/259** added hierarchy to the runtime but kept all result types flat.
5. **GH-257/286** designed the correct algebra. Code was never updated.

Each layer assumed the one below would eventually become hierarchical. None did.

**The AST (KS-020) is the one layer that got hierarchy right.** Everything downstream (to runtime consumers) and upstream (to CLI/generators) flattens it.

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

The designs are sound. The AST is sound. The runtime *internally* computes hierarchy correctly. The gap is between the runtime's internal hierarchy and everything that reads its results. The fix path is types first, then runtime output, then consumers, then CLI — inside out, each layer testable before the next starts.

---

## Reset Plan: Types First, Inside Out

The CLI tooling was built before the types were hierarchical. The parsers captured hierarchy correctly but had nowhere to put it — `ExtractedStatechart` had no hierarchy field, so they flattened to fit the container. The fix requires working inside-out: get the types right, then propagate outward.

### Layer 1: Types (the foundation)

**What to do**: Create the tree types specified in gh-286 that were never implemented.

| Type | Current | Target | Status |
|------|---------|--------|--------|
| `TransitionOp` | Does not exist | `Exited of string \| Entered of string \| HistoryRecorded of string \| HistoryRestored of string * HistoryKind` | New |
| `TransitionStep` | Does not exist | `Leaf of TransitionOp \| Seq of TransitionStep list \| Par of TransitionStep list` | New |
| `HierarchicalTransitionResult` | `ExitedStates: string list, EnteredStates: string list` | `Steps: TransitionStep` (drop flat fields) | Breaking change |
| `TransitionEvent<'S,'E,'C>` | `ExitedStates: string list, EnteredStates: string list` | `Steps: TransitionStep` (drop flat fields) | Breaking change |
| `ExtractedStatechart` | `StateNames: string list, Transitions: TransitionSpec list` | Add `StateContainment` or hierarchy field | Breaking change |

**What's salvageable**: `TransitionAlgebra<'r>` type is correct and already in Core. `ActiveStateConfiguration`, `HistoryRecord`, `StateHierarchy`, `CompositeStateSpec` are all correct. The types that exist are fine — it's the types that don't exist and the flat fields on existing types.

### Layer 2: Runtime (interpreters)

**What to do**: Make Fork real. Build interpreters against tree types.

| Component | Current | Target | Salvageable? |
|-----------|---------|--------|-------------|
| `RuntimeInterpreter.Fork` | No-op: `fun _regions -> (config, history, [], [])` | Enters each region, produces `Par` node | Rewrite Fork + Enter interaction |
| `RuntimeInterpreter.Bind` | Concatenates flat lists: `exited1 @ exited2` | Produces `Seq` node from two `TransitionStep` values | Rewrite |
| `enterState` | Returns `ActiveStateConfiguration` only | Returns `(ActiveStateConfiguration * TransitionStep)` tuple | Modify signature |
| `HierarchicalRuntime.transition` | Uses flat `RuntimeStep`, returns flat lists | Delegates to `TransitionProgram.runProgram` | Already partially there |
| `TransitionProgram` builder | Generates correct op sequence including Fork | Unchanged — already emits Fork after Enter for AND composites | **Keep as-is** |
| TraceInterpreter | Does not exist | Collects `TransitionStep` tree for observation | New |
| DualInterpreter | Does not exist (740-line Dual.fs is pre-algebra) | Replaces `deriveWithHierarchy`, closes AND-state gap | New (Dual.fs consumers need rewrite) |
| ValidationInterpreter | Does not exist | Dry-run guard evaluation | New |
| CollectorInterpreter | Does not exist | CE-first format generation | New |

**What's salvageable**: LCA computation, exit/entry path calculation, history mechanism (record/restore, shallow/deep), auto-wrap logic, `StateHierarchy.build` — all correct internally. The `TransitionProgram` builder already emits Fork correctly. The interpreter *shape* (record of functions) is correct. It's the function implementations (especially Fork, Bind, Enter) and the result types that need changing.

### Layer 3: Consumers (the blast radius)

**What to do**: Update ~15 functions to read hierarchy from the new types.

| Consumer | Current data source | Impact | Salvageable? |
|----------|-------------------|--------|-------------|
| `Projection.projectForRole` | Flat `ExtractedStatechart` | Needs hierarchy-aware filtering | Algorithm rewrite |
| `Projection.pruneUnreachableStates` | Flat `StateNames` list | Needs hierarchy-aware reachability | Algorithm rewrite |
| `ProgressAnalysis.detectDeadlocks` | Flat `StateNames` iteration | Needs composite-state awareness | Algorithm rewrite |
| `ProgressAnalysis.identifyReadOnlyRoles` | Flat projected statechart | Same | Algorithm rewrite |
| `AffordanceMap.fromStatechart` | Flat state list | Needs hierarchy for composite affordances | Moderate rewrite |
| `Dual.derive*` (740 lines) | Flat `ExtractedStatechart` | Full replacement with DualInterpreter (Layer 2) | **Replace entirely** |
| `ProjectionValidator.validate*` | Flat validation | Hierarchy-aware validation | Moderate rewrite |
| `DualProfileOverlay` | Flat `DeriveResult` | Follows DualInterpreter rewrite | Rewrite |
| `DualConformanceChecker` | Flat `DeriveResult` | Follows DualInterpreter rewrite | Rewrite |
| `StateMachineMiddleware` | `StateHandlerMap` (flat) + `HierarchicalRuntime` (hierarchical) | Already hierarchy-aware for dispatch — just needs tree-typed results | **Minor changes** |
| `computeTransition` closure | Copies flat lists to `TransitionEvent` | Copies `TransitionStep` tree instead | **Trivial** |
| `onTransition` hooks | Receives flat `TransitionEvent` | Receives tree-typed `TransitionEvent` — or replaced by TraceInterpreter | Depends on D-DD6 timeline |

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

### The provenance and SHACL gaps

These are not part of the core reset but should be addressed:

| Gap | Fix | When |
|-----|-----|------|
| Provenance bridge never connected | Wire `IObservable<TransitionEvent>` publisher in statechart middleware | After Layer 2 (needs tree-typed events) |
| Two incompatible `TransitionEvent` types | Unify or bridge `Frank.Provenance.TransitionEvent` with `Frank.Statecharts.TransitionEvent<'S,'E,'C>` | After Layer 1 (type design) |
| SHACL shapes undiscoverable | Add SHACL shape Link headers in affordance middleware | After Layer 3 (affordance rewrite) |
| Dual derivation AND-state gap | DualInterpreter with Fork semantics | Layer 2 |

### Order of operations

```
Layer 1 (types) ──────► compile-check, no runtime changes
    │
    ▼
Layer 2 (runtime) ────► unit tests against tree assertions
    │
    ▼
Layer 3 (consumers) ──► integration tests, projection/analysis correct
    │
    ▼
Layer 4 (CLI/gen) ────► e2e tests, round-trip verification
    │
    ▼
Gaps (provenance, ────► integration tests
      SHACL, dual)
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
