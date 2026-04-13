# The Frank Audit: A Design Mystery in Six Acts

*Forensic analysis of how flat-FSM semantics became load-bearing across an entire framework — identified during the April 2026 spec consolidation. Full decision catalog in [DECISIONS.md](DECISIONS.md).*

---

## Prologue: The Scene of the Crime

Frank is an F# web framework built on three pillars: HATEOAS, Harel statecharts, and semantic discovery. The thesis is that a client should be able to distinguish a hierarchical statechart from a flat FSM by observing HTTP responses alone.

Over three months of Claude Code-assisted development, the hierarchy was designed correctly at every stage — and implemented as a flat FSM at every stage. The designs got more explicit. The code stayed flat. By the time anyone noticed, flat-FSM assumptions were embedded in ~15 functions across ~10 files, feeding every analysis tool, every generator, every test assertion, and every CLI command.

This is the story of how that happened, told through the evidence.

---

## Act I: The Foundation (v7.0–v7.2, Spec Kit 001–016)

No statecharts. No FSMs. Just a clean F# web framework.

Datastar SSE streaming. Frank.Auth. Frank.OpenApi. Middleware ordering. The `resource` computation expression. Generic endpoint metadata extensibility via convention functions (D-SK27 — the mechanism every later extension builds on). Two-stage middleware pipeline (D-SK24). Native SSE over external dependencies (D-SK32).

54 design decisions across 16 specs. All sound. All hierarchy-agnostic. The victim hasn't entered the room yet.

---

## Act II: The Pivot That Wasn't (v7.3.0, Kitty Specs 003–004)

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

## Act III: The Parsers That Knew Too Much (v7.3.0, Kitty Specs 011–026)

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

## Act IV: The Body in the Library (v7.3.1–v7.4.0, PRs 221–274)

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

## Act V: The Detective's Notebook (v7.4.0 Design Decisions)

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

## Act VI: The Suspects Lineup

### Suspect 1: The Provenance Bridge That Doesn't Exist

`Frank.Provenance` registers an `IHostedService` that subscribes to `IObservable<TransitionEvent>` from DI. **Nobody publishes it.** The service starts, logs "No IObservable<TransitionEvent> registered," and silently does nothing.

Even if the bridge existed, there are **two incompatible `TransitionEvent` types**: `Frank.Statecharts.TransitionEvent<'S,'E,'C>` (generic, carries flat `ExitedStates`/`EnteredStates` lists) and `Frank.Provenance.TransitionEvent` (non-generic, carries singular `PreviousState`/`NewState` strings). They share a name. They share nothing else.

The provenance system was designed (KS-006), implemented (PR #109), shipped — and never connected to the runtime it's supposed to observe.

### Suspect 2: The Dual Derivation That Can't See Forks

`Dual.fs` — 740 lines of pre-algebra code. Zero references to `TransitionAlgebra`. Operates entirely on flat `ExtractedStatechart`. Has a 30-line module comment documenting three formalism bounds, including: *"AND-state parallel composition is NOT handled — synchronization barriers silently dropped."*

D-005 says replace it entirely with a `DualAlgebra` interpreter. Nothing has started. Every consumer of `DeriveResult` — `DualAlpsGenerator`, `DualConformanceChecker`, `DualProfileOverlay` — assumes flat per-state snapshots. Replacing `Dual.fs` means rewriting all of them.

### Suspect 3: The SHACL Shapes Nobody Can Find

SHACL shapes are served at `/shapes/{slug}` with content negotiation (Turtle, JSON-LD, RDF/XML). They exist. They're pre-computed at startup.

The affordance middleware generates Link headers with ALPS profile URIs. It has no awareness of SHACL shapes. No Link header points to them. They are invisible to hypermedia navigation — a discoverable framework with undiscoverable validation constraints.

### Suspect 4: The Only Observer In Town

`onTransition` is the only integration point between the statechart runtime and any external system. D-006 says eliminate it — "Customization happens through interpreters." But:

- The OrderFulfillment e2e test depends on it (AT3 checks stderr logs from the hook)
- 6+ unit tests directly test hook behavior
- The provenance system's *intended* design uses it as the subscription mechanism
- The replacement (TraceAlgebra interpreter) doesn't exist

Removing `onTransition` without TraceAlgebra makes the provenance gap permanent and breaks the thesis demo.

### Suspect 5: `ExtractedStatechart` — The Flat Bottleneck

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

## The Evidence Room: Closed Issue Audit

### v7.3.0 (the original audit)
5 of 54 issues closed with silently dropped requirements. This triggered the creation of ~200 lines of CLAUDE.md rules, 40+ feedback memories, and the entire verification methodology.

### v7.3.1 (13 issues)
**Clean.** Zero silently dropped requirements. Two issues (#203, #204) have documented deferrals to v7.4.0 with legitimate dependency chains. Every PR has a requirements table. The discipline works when applied.

### v7.4.0 closed (30 issues)
Dramatically better than v7.3.0. But not perfect:

| Issue | Gap | Severity |
|-------|-----|----------|
| **#245** (OrderFulfillment sample) | PR body is "Closes #245" with no requirements table. 12+ formalism proof points not individually verified. | Medium — most were verified in their own dedicated issues |
| **#254** (HTTP compliance) | text/event-stream content negotiation tested 3/4 Accept scenarios. Gap tracked as open #309. | Low — caught and tracked |
| **#239** (MSBuild integration) | Dedicated test project not created. Embedded resource timing regression noted. | Low — OOM fixed, regression documented |
| **#269** (Safe method transitions) | ALPS type annotation safety explicitly excluded from PR scope. | Low — documented deferral |
| **#126 WP1** (Session type conneg) | SHACL shape references requirement unclear from thin PR body. | Medium — cannot confirm from evidence |

### v7.4.0 open — the dependency chain

**12+ open issues are blocked on the TransitionAlgebra/interpreter chain (#257, #286–#290).** The entire discovery thesis proof (Track A, #252) is blocked downstream:

```
#286 (TransitionAlgebra) ← the linchpin
    ├── #287 (TraceInterpreter)
    ├── #288 (DualInterpreter — closes AND-state gap)
    ├── #289 (ValidationInterpreter)
    ├── #290 (CollectorInterpreter)
    ├── #282 (Transition CE)
    └── #283 (SCXML codegen) → #284 (MSBuild) → #298 (model.bin pipeline)
                                                      └── #252 (Discovery surface)
                                                            └── #301 (Naive client — THE THESIS TEST)
```

If the algebra work stalls, the thesis cannot be demonstrated.

---

## Root Cause: Each Layer Trusted The One Below

1. **KS-003** concluded hierarchy was feasible. **KS-004** built flat.
2. **Parsers** (KS-013/018/020/024) built correct hierarchical ASTs. **Mappers** flattened to Act II types.
3. **KS-026** designed the CLI pipeline flat, with "compound states later."
4. **PR #221/259** added hierarchy to the runtime but kept all result types flat.
5. **GH-257/286** designed the correct algebra. Code was never updated.

Each layer assumed the one below would eventually become hierarchical. None did.

**The AST (KS-020) is the one layer that got hierarchy right.** Everything downstream (to runtime consumers) and upstream (to CLI/generators) flattens it. The fix path runs through `ExtractedStatechart` — add hierarchy, propagate to ~15 functions.

---

## Epilogue: What's Sound

Not everything is broken. The evidence shows clear bright lines:

- **v7.0–v7.2 foundation**: All 54 decisions sound and hierarchy-agnostic
- **Shared AST** (KS-020): `StateNode.Children`, `StateKind.Parallel` — correct hierarchy model
- **Parsers**: All correctly capture hierarchy from source formats
- **Runtime dispatch**: `StateMachineMiddleware` delegates to `HierarchicalRuntime.resolveHandlers` — hierarchy-aware
- **LCA computation**: `StateHierarchy.computeLCA`, exit/entry path calculation — correct
- **History mechanism**: Shallow + deep history, record/restore — functional
- **Auto-wrap**: All resources uniformly use hierarchical dispatch
- **Role projection**: Allow/Link headers differ by role — working end-to-end
- **HTTP compliance**: 405/Allow, 202/Content-Location, content negotiation — RFC-correct
- **v7.3.1 discipline**: 13 issues, zero silent drops — the methodology works
- **v7.4.0 designs**: TransitionAlgebra, interpreter types, banned anti-patterns — correct specifications

The designs are sound. The AST is sound. The runtime *internally* computes hierarchy correctly. The gap is between the runtime's internal hierarchy and everything that reads its results.

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
