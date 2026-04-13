# Frank Design Audit

Forensic analysis of how flat-FSM semantics became embedded across the project, identified during the April 2026 spec consolidation. Traces contradictions through their full timeline from kitty-specs (where statechart work began) through v7.4.0 design decisions. The full decision catalog is in [DECISIONS.md](DECISIONS.md).

---

## The Flat-to-Hierarchy Timeline

### Era 1: Pre-statechart foundation (v7.0–v7.2, Spec Kit 001–016)

Datastar SSE streaming, Frank.Auth, Frank.OpenApi, middleware ordering, Analyzers. **No statechart or FSM work existed yet.** These specs established the extension model, CE patterns, and ASP.NET Core integration that all later work builds on.

Key decisions: single SSE channel per page (D-SK15), generic endpoint metadata extensibility (D-SK27), two-stage middleware pipeline (D-SK24), native SSE (D-SK32). All remain sound and hierarchy-agnostic.

### Era 2: The pivot — hierarchy researched, flat runtime built (v7.3.0, Kitty Specs 001–010)

**KS-003 (Statecharts Feasibility)** researched hierarchy viability and concluded it was feasible. Identified SCXML, smcat, WSD, ALPS, XState as the five format targets. Established `statefulResource` CE as the API. Set the complexity ceiling at "simple to moderate" (D-KS-F12).

**KS-004 (Frank.Statecharts Core)** built the runtime — but as a flat FSM:
- `StateMachine<'S,'E,'C>` typed state machine with pure transition functions (D-KS-S1)
- `TransitionResult<'S,'C>` DU with Transitioned/Blocked/Invalid (D-KS-S2)
- Guards evaluated in registration order, first Blocked short-circuits (D-KS-S4)
- `onTransition` observable hooks (D-KS-S7) — later eliminated by D-006 but still in code

**KS-010 (Production Readiness)** hardened the flat runtime:
- `FSharpValue.PreComputeUnionTagReader` for O(1) state key extraction (D-KS-P1)
- Actor-serialized concurrency with unchanged `IStateMachineStore` interface (D-KS-P2)
- Guard DU with AccessControl/EventValidation phases (D-KS-P3)

**What happened**: The feasibility research (KS-003) said hierarchy was viable, but the implementation (KS-004) built flat. The flat runtime was then hardened (KS-010) as if it was the final design. This is the original sin — everything downstream built against the flat baseline.

### Era 3: Parsers capture hierarchy, mappers flatten it (v7.3.0, Kitty Specs 011–024)

The AST layer was designed correctly. The problem is downstream.

**KS-020 (Shared AST)**: `StateNode.Children` and `StateKind` (including `Parallel`, `ShallowHistory`, `DeepHistory`) correctly model hierarchy. **The AST is sound.**

**KS-013 (smcat Parser)**: Parser correctly captures composite states with `{ }` blocks. But the mapper produces `StateMachine<'S,'E,'C>` — the flat generic type. Hierarchy goes in, flat comes out. ForkJoin is a classification label, not operational (D-SUS4). "The mapper produces a StateMachine-compatible representation" — confirming flat mapping (D-SUS6).

**KS-018 (SCXML Parser)**: Parser captures `<parallel>`, `<history>`, `<invoke>` correctly. But history and invoke are labeled **"non-functional annotations preserved for LLM context"** (D-SUS1). Multi-target transitions split into one `TransitionEdge` per target, losing atomicity (D-SUS3). History is "structured but non-functional" (D-SUS7).

**KS-024 (SCXML Migration)**: `TransitionEdge.Target` holds only the first target; full list relegated to annotation (D-SUS2). History default transitions stored in annotation, not as `TransitionElement` (D-SUS3). State-scoped data flattened to document level (D-SUS4). ForkJoin states silently skipped by generator (D-SUS5).

**KS-022 (smcat Migration)**: Generator uses flat `StateHandlerMap` as data source. Creates self-transitions per HTTP method rather than inter-state transitions (D-SUS5). Guards stored as unstructured notes (D-SUS4).

**Pattern**: Every parser correctly captures hierarchy from its format. Every mapper/generator immediately flattens it back to the flat `StateMachine<'S,'E,'C>` or `StateMachineMetadata` types. The hierarchy information enters the system and is immediately discarded.

### Era 4: CLI pipeline — flat by explicit design (v7.3.0, KS-026)

**KS-026 (CLI Statechart Commands)** is the most deeply flat-dependent spec:
- Extract command reads `StateMachineMetadata` — flat state→HTTP-methods map (D-SUS1)
- "Transition targets between different states are NOT directly available from StateMachineMetadata" — explicitly acknowledged (D-SUS2)
- All five format generators consume flat metadata via `Wsd.Generator.generate` (D-SUS3)
- Validation compares hierarchical spec files against flat "code truth" (D-SUS4)
- Risk register says **"Implement flat-state mapping first. Compound states can be added later."** (D-SUS8)

The "added later" never happened. The entire `frank extract` → `frank compile` → `model.bin` → `useAffordances` pipeline is flat.

### Era 5: Hierarchy "operational" — but the baseline was flat (v7.3.1–v7.4.0)

**PR #221 (Hierarchical Runtime)**: Added `StateHierarchy`, `ActiveStateConfiguration`, LCA computation, entry/exit ordering. Opt-in via `StateMachineMetadata.Hierarchy: StateHierarchy option`.

**PR #259 (Make Hierarchy Operational)**: Auto-wrapped flat FSMs in synthetic `__root__` XOR. Store redesigned to persist `InstanceSnapshot` with hierarchy config. `TransitionEvent` carries `ExitedStates`/`EnteredStates` — but as **flat lists**.

**The gap**: The hierarchy runtime was wired in, but the result types, CLI pipeline, generators, provenance, and test assertions all remained flat. The runtime computes hierarchy internally, then flattens the results for every consumer.

### Era 6: Correct designs, unimplemented (v7.4.0, GitHub issues)

**GH-257 (Tagless Final Interpreter)**: Designed TransitionAlgebra, four interpreter types (Runtime, Trace, Dual, Validation), pure synchronous interpreters with async in middleware. **Correct design.**

**GH-286 (TransitionAlgebra + RuntimeInterpreter)**: Specified TransitionStep tree (Leaf, Seq, Par), Fork producing Par nodes, HierarchicalTransitionResult dropping flat fields, five banned anti-patterns. **Correct design — code doesn't match any of it.**

---

## Contradictions (full timeline)

### C-1: Fork semantics

| When | Source | Decision |
|------|--------|----------|
| v7.3.0 | KS-013 D-SUS4 | ForkJoin is a "state type classification for detection purposes" — no operational meaning |
| v7.3.0 | KS-018 D-SUS1 | `<parallel>` maps to "a compound state concept" — vague, non-operational |
| v7.4.0 | D-002, gh-286 | "Fork is explicit at the algebra level — DualAlgebra needs to see Fork to accumulate per-region obligations" |
| Code | `Hierarchy.fs` | `Fork = fun _regions (config, history) -> (config, history, [], [])` — no-op |

**The contradiction spans 3 eras.** Fork was never operational in any kitty-spec. The v7.4.0 design (D-002) made it explicit, but the code was never updated.

### C-2: Flat results

| When | Source | Decision |
|------|--------|----------|
| v7.3.0 | KS-004 D-KS-S2 | `TransitionResult<'S,'C>` — flat DU with single new state |
| v7.3.0 | KS-006 D-SUS1 | `TransitionEvent` has singular `PreviousState`/`NewState` |
| v7.3.1 | PR #259 | `HierarchicalTransitionResult` has `ExitedStates: string list`, `EnteredStates: string list` |
| v7.4.0 | gh-286 D-TA3 | "HierarchicalTransitionResult drops flat fields — only Configuration, Steps: TransitionStep, HistoryRecord" |
| Code | `Hierarchy.fs` | Flat `string list` fields. `TransitionStep` type does not exist. |

**The flat result type was designed in KS-004, carried through to KS-006 (PROV-O) and PR #259, and was never replaced despite gh-286 specifying its replacement.**

### C-3: Parser→mapper hierarchy loss

| When | Source | Decision |
|------|--------|----------|
| v7.3.0 | KS-020 | AST `StateNode.Children` + `StateKind.Parallel` — **hierarchy correctly modeled** |
| v7.3.0 | KS-013 D-SUS6 | smcat mapper produces flat `StateMachine<>` from hierarchical AST |
| v7.3.0 | KS-018 D-SUS3 | SCXML multi-target transitions split into individual edges |
| v7.3.0 | KS-024 D-SUS2 | Only first target in `TransitionEdge.Target`; rest in annotation |
| v7.3.0 | KS-022 D-SUS5 | Generator uses flat `StateHandlerMap`, creates self-transitions per HTTP method |

**The AST was always correct. Every downstream consumer immediately flattens it. This is not a bug in any single spec — it's the consistent pattern across four format specs.**

### C-4: CLI pipeline never updated

| When | Source | Decision |
|------|--------|----------|
| v7.3.0 | KS-026 D-SUS1-8 | CLI pipeline explicitly designed for flat StateMachineMetadata |
| v7.3.0 | KS-026 D-SUS8 | "Implement flat-state mapping first. Compound states can be added later." |
| v7.3.1–v7.4.0 | — | No spec, issue, or PR addresses hierarchy-aware CLI pipeline |

**"Added later" never happened. The CLI pipeline is the production path for generated artifacts — flat metadata in means flat artifacts out for all five formats.**

### C-5: SCXML history — non-functional vs. implemented

| When | Source | Decision |
|------|--------|----------|
| v7.3.0 | KS-018 D-SUS1 | `<history>` labeled "non-functional annotations preserved for LLM context" |
| v7.3.0 | KS-024 D-SUS3 | History default transitions stored in annotations, not as transitions |
| v7.3.1 | PR #221, #259 | History implemented in `Hierarchy.fs` — `RecordHistory`, `RestoreHistory`, shallow+deep |

**The parser spec (KS-018) called history non-functional. The runtime (PR #221) implemented it. Neither was updated to reconcile.**

### C-6: onTransition — designed, superseded, still present

| When | Source | Decision |
|------|--------|----------|
| v7.3.0 | KS-004 D-KS-S7 | `onTransition` observable hooks fire after successful transitions |
| v7.4.0 | D-006 | "onTransition does not exist. Customization happens through interpreters." |
| Code | `StatefulResourceBuilder.fs` | `onTransition` hooks still present and called |

**Legitimate evolution (hooks → interpreter customization) that was never applied to the codebase.**

### C-7: Multi-target transition atomicity

| When | Source | Decision |
|------|--------|----------|
| v7.3.0 | KS-018 D-SUS3 | SCXML multi-target transitions split into one edge per target |
| v7.3.0 | KS-024 D-SUS2 | First target in primary field, rest in annotation |
| Reality | SCXML spec | Multi-target = single atomic transition entering parallel regions |

**Splitting a compound transition into individual edges converts AND-state semantics into sequential flat-FSM semantics.**

### C-8: Cross-format merge without hierarchical scope

| When | Source | Decision |
|------|--------|----------|
| v7.3.0 | KS-030 D-SUS3 | State matching by flat identifier (exact match) |
| v7.3.0 | KS-030 D-SUS4 | Transition matching by flat (Source, Target, Event) triple |
| Risk | Harel semantics | Same identifier can appear at different hierarchy levels. Same triple can have different meaning at different scopes. |

**Merge rules work for flat state machines but produce incorrect results for hierarchical statecharts with same-named states at different levels.**

---

## Dropped Designs

### PROV-O Provenance (KS-006)
- **Designed**: W3C PROV-O recording via `TransitionObserver`, `ProvenanceRecord`, `ProvenanceStore`
- **Implemented**: v7.3.0 (PR #109), but against flat semantics — single `PreviousState`/`NewState`
- **Problem**: AND-state compound transitions produce multiple state changes that can't be captured
- **Not updated**: No subsequent spec revisits provenance for hierarchy

### SHACL Pipeline Integration (KS-005)
- **Designed**: SHACL shape derivation from F# types, dual-path validation
- **Implemented**: v7.3.0 (PR #109), functioning at endpoints
- **Gap**: Never wired into CLI validation pipeline or affordance map

### RDF SPARQL Validation (KS-015)
- **Designed**: SPARQL queries over RDF output for cross-format graph isomorphism
- **Status**: Test infrastructure only. No production code.

### Full Round-Trip Testing (KS-003)
- **Designed** (D-KS-F1): "Lossy-but-documented round-tripping"
- **Status**: Parsers→AST works. AST→generators uses flat metadata (C-4). Round-trip broken for hierarchy.

### Four Interpreters (GH-257)
- **Designed**: RuntimeAlgebra, TraceAlgebra, DualAlgebra, ValidationAlgebra
- **Status**: Only RuntimeInterpreter exists, with Fork as no-op. Three interpreters never built.

---

## Root Cause Analysis

The contradictions are not random. They follow a clear pattern:

1. **KS-003 concluded hierarchy was feasible.** KS-004 built flat.
2. **Format parsers (KS-013/018/020/024) built correct hierarchical AST.** Mappers/generators flattened to the flat types from KS-004.
3. **CLI pipeline (KS-026) was explicitly designed flat** with a "compound states later" note that was never revisited.
4. **PR #221/259 added hierarchy to the runtime** but kept all result types and consumers flat.
5. **GH-257/286 designed the correct algebra and tree types.** Code was never updated to match.

Each layer assumed the one below it would eventually become hierarchical. None of them did. The flat types from KS-004 became load-bearing across every consumer — CLI, generators, provenance, affordances, tests — and were never replaced.

**The AST (KS-020) is the one layer that got hierarchy right.** Everything from the AST downward (to runtime) and upward (to CLI/generators) flattens it.

---

## Summary Statistics

| Category | Count |
|----------|-------|
| Contradictions (full timeline) | 8 (C-1 through C-8) |
| Eras spanning contradictions | 3–4 eras per contradiction |
| Dropped designs | 5 |
| Kitty-spec decisions establishing flat baseline | ~30 across KS-004, KS-010, KS-013, KS-018, KS-022, KS-024, KS-026, KS-030 |
| v7.4.0 designs correct but not implemented | 4 (Fork, TransitionStep, DualAlgebra, onTransition removal) |
| Sound layers | AST (KS-020), parsers, foundational v7.0-v7.2 architecture |
