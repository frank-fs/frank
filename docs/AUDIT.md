# Frank Design Audit

Contradictions, evolution timeline, suspect decisions, and dropped designs identified during the April 2026 spec consolidation. This is the forensic analysis; the full decision catalog is in [DECISIONS.md](DECISIONS.md).

**Status key**: Active = current and valid. Superseded = replaced by a later decision. Suspect = influenced by flat-semantics assumptions. Not Implemented = design is correct but code doesn't match.

---

## Contradictions and Evolution Timeline

This section documents the flat-to-hierarchy pivot and identifies where decisions contradict each other, where designs were dropped, and where specs were correct but implementation diverged.

### The Flat-to-Hierarchy Pivot

| Era | Specs | Model | Status |
|-----|-------|-------|--------|
| v7.0–v7.2 (Spec Kit 001–016) | Datastar, Auth, OpenAPI, middleware | **Flat FSM** — intentional, correct for scope | Sound |
| v7.3.0 (Kitty Specs 001–033) | Statecharts, parsers, CLI, discovery | **Hierarchy intended**, flat FSM implemented | Pivot point — designs say hierarchy, code stays flat |
| v7.3.1 (Superpowers specs) | IStatechartFeature, JSON Home, projection | **Hierarchy expected** from v7.3.0 | Built on incorrect flat baseline |
| v7.4.0 (GitHub issues, DESIGN_DECISIONS.md) | TransitionAlgebra, interpreters, analyzers | **Hierarchy explicit** — Fork must produce Par nodes | Designs correct, implementation still flat |

### Key Contradictions

#### C-1: Fork semantics — designed explicit, implemented as no-op
- **Design** (D-002, gh-286): "Fork is explicit at the algebra level — the DualAlgebra needs to see Fork to accumulate per-region obligations."
- **Code** (`Hierarchy.fs`): `Fork = fun _regions (config, history) -> (config, history, [], [])` — literal no-op with comment "Protocol marker."
- **Impact**: Every interpreter that needs Fork (Dual, Trace, Collector, Validation) cannot function correctly.

#### C-2: TransitionStep tree — specified, never created
- **Design** (gh-286 D-TA1): "`TransitionStep` tree (Leaf, Seq, Par) as fundamental runtime type preserving Harel AND-state parallelism. NO flatten functions."
- **Code**: `TransitionStep` type does not exist. Results use `ExitedStates: string list` and `EnteredStates: string list`.
- **Design** (gh-286 D-TA3): "`HierarchicalTransitionResult` drops flat fields — only Configuration, Steps: TransitionStep, HistoryRecord."
- **Code**: `HierarchicalTransitionResult` has `ExitedStates: string list`, `EnteredStates: string list`. No `Steps` field.

#### C-3: Five banned anti-patterns — all present in code
- **Design** (gh-286 D-TA5): "Five banned anti-patterns: no `flatten` functions, no `enteredStates` extractors, no flat `string list` fields on result types, no helper returning `string list` from tree input, no keeping flat fields 'for production consumers.'"
- **Code**: All five patterns exist. `enterState` returns flat configuration. `Bind` concatenates flat lists via `@`.

#### C-4: Four interpreters designed, one implemented (broken)
- **Design** (gh-257 D-TF3): "RuntimeAlgebra, TraceAlgebra, DualAlgebra, ValidationAlgebra."
- **Code**: Only `RuntimeInterpreter` exists, with Fork as no-op. Trace, Dual, and Validation interpreters are unimplemented.

#### C-5: onTransition — proposed, then eliminated, code still uses hooks
- **Design** (ks-004 D-S7, v7.3.0): `onTransition` observable hooks fire after successful transitions.
- **Design** (D-006, v7.4.0): "onTransition does not exist. Customization happens through interpreters."
- **Code**: `onTransition` hooks still exist in `StatefulResourceBuilder.fs`. The supersession was never applied to the codebase.

#### C-6: CLI pipeline — flat by design, never updated for hierarchy
- **Design** (ks-026): Entire CLI pipeline built on flat `StateMachineMetadata`. Spec explicitly says "state-capability views, not full transition graphs."
- **Impact**: `frank extract` → `frank compile` → `model.bin` is a flat pipeline. All generated artifacts (WSD, smcat, SCXML, ALPS, XState) are flat regardless of format capabilities. Validation compares hierarchical spec files against flat "code truth."
- **Never revisited**: No subsequent spec or decision addresses making the CLI pipeline hierarchy-aware.

#### C-7: SCXML history/invoke — parsed correctly, called "non-functional"
- **Design** (ks-018): SCXML `<history>` and `<invoke>` labeled "non-functional annotations preserved for LLM context."
- **Reality**: History is a fundamental Harel construct. Calling it "non-functional" means the runtime ignores it. The code actually does implement history in `Hierarchy.fs`, creating a contradiction between the spec (non-functional) and the code (functional but incomplete).

#### C-8: Multi-target transitions — atomicity lost
- **Design** (ks-018, ks-024): SCXML multi-target transitions split into one `TransitionEdge` per target.
- **Reality**: SCXML multi-target transitions are a single atomic transition entering parallel regions. Splitting loses atomicity — converts a compound transition into multiple independent transitions (flat-FSM thinking).

### Dropped Designs

These features appear in earlier specs and then vanish without explicit cancellation.

#### Dropped-1: PROV-O Provenance (ks-006)
- **Designed**: W3C PROV-O provenance recording via `TransitionObserver`, `ProvenanceRecord`, and `ProvenanceStore`.
- **Status**: Implemented in v7.3.0 (PR #109), but designed against flat FSM semantics — single `PreviousState`/`NewState` per record.
- **Problem**: AND-state compound transitions produce multiple state changes. The provenance model can't capture them.
- **Not updated**: No subsequent spec revisits provenance for hierarchy.

#### Dropped-2: SHACL Validation (ks-005)
- **Designed**: SHACL shape derivation from F# types, dual-path validation (SHACL + RFC 9457).
- **Status**: Implemented in v7.3.0 (PR #109), functioning.
- **Integration gap**: SHACL shapes are served at endpoints but never wired into the CLI validation pipeline or affordance map.

#### Dropped-3: RDF SPARQL Validation (ks-015)
- **Designed**: SPARQL queries over RDF output for cross-format graph isomorphism checks.
- **Status**: Test infrastructure only. No production code.

#### Dropped-4: Full round-trip testing across formats
- **Designed** (ks-003 D-F1): "Lossy-but-documented round-tripping... No behavioral information may be lost in runtime code itself."
- **Status**: Parsers produce ASTs correctly, but generators consume flat `StateMachineMetadata` (see C-6), so hierarchy is lost in the code→spec direction. Round-trip is broken for hierarchical features.

---

## v7.4.0 Algebra and Interpreter Decisions

Extracted from [DESIGN_DECISIONS.md](DESIGN_DECISIONS.md). All resolved during issue refinement. **These are the correct designs — the code needs to match them, not the other way around.**

### D-001: LCA is a parameter, not an algebra operation

- **Source**: [#286](https://github.com/frank-fs/frank/issues/286), DESIGN_DECISIONS.md §1a
- **Status**: Active
- **Decision**: `ComputeLCA` is a pure query on `StateHierarchy`, computed once externally and passed to the program. The algebra is a pure effect algebra (Exit, Enter, Fork, Sequence) with no query operations.

### D-002: Explicit Fork in algebra programs

- **Source**: [#286](https://github.com/frank-fs/frank/issues/286), DESIGN_DECISIONS.md §1b
- **Status**: Active — **NOT IMPLEMENTED** (Fork is no-op in code, see C-1)
- **Decision**: Fork is explicit at the algebra level — the DualAlgebra needs to see Fork to accumulate per-region obligations. The CE auto-generates algebra programs with Fork included.

### D-003: 'r varies per interpreter (tagless final)

- **Source**: [#286](https://github.com/frank-fs/frank/issues/286), DESIGN_DECISIONS.md §1c
- **Status**: Active — **PARTIALLY IMPLEMENTED** (RuntimeStep exists but uses flat lists)
- **Decision**: `'r` varies per interpreter. Programs are polymorphic: `TransitionAlgebra<'r> -> 'r`. Each interpreter chooses its own `'r`.

### D-004: ActiveStateConfiguration is opaque

- **Source**: [#286](https://github.com/frank-fs/frank/issues/286), DESIGN_DECISIONS.md §2
- **Status**: Active — implemented
- **Decision**: Export only the opaque type. Programs receive `ActiveStateConfiguration` from `RestoreHistory` and pass it through.

### D-005: DualAlgebra replaces deriveWithHierarchy entirely

- **Source**: [#288](https://github.com/frank-fs/frank/issues/288), DESIGN_DECISIONS.md §3
- **Status**: Active — **NOT IMPLEMENTED** (DualAlgebra does not exist)
- **Decision**: Replace `deriveWithHierarchy` entirely. The dual derivation IS a `DualAlgebra` interpreter.

### D-006: onTransition does not exist

- **Source**: [#282](https://github.com/frank-fs/frank/issues/282), DESIGN_DECISIONS.md §4
- **Status**: Active — **NOT IMPLEMENTED** (onTransition hooks still in code, see C-5)
- **Decision**: Every `transition` declaration auto-generates its algebra program from the hierarchy. Customization happens through interpreters, not custom programs.

### D-007: Single generated file per statechart

- **Source**: [#283](https://github.com/frank-fs/frank/issues/283), DESIGN_DECISIONS.md §5
- **Status**: Active — not yet reached (codegen not built)

### D-008: childOf uses value binding

- **Source**: [#293](https://github.com/frank-fs/frank/issues/293), DESIGN_DECISIONS.md §6
- **Status**: Active — not yet reached

### D-009: Two-path validation (build-time + startup)

- **Source**: [#296](https://github.com/frank-fs/frank/issues/296), DESIGN_DECISIONS.md §7
- **Status**: Active — not yet reached (ValidationAlgebra not built)

### D-010: Algebra types in Frank.Statecharts.Core

- **Source**: [#286](https://github.com/frank-fs/frank/issues/286), DESIGN_DECISIONS.md §8
- **Status**: Active — implemented (TransitionAlgebra is in Core)

### D-011: Instance ID uses :: separator

- **Source**: [#293](https://github.com/frank-fs/frank/issues/293), DESIGN_DECISIONS.md §9
- **Status**: Active — not yet reached

### D-012: RFC 9457 Problem Details for error responses

- **Source**: [#294](https://github.com/frank-fs/frank/issues/294), DESIGN_DECISIONS.md §10
- **Status**: Active — implemented

### D-013: frank-cli distributed via existing dotnet tool

- **Source**: [#284](https://github.com/frank-fs/frank/issues/284), DESIGN_DECISIONS.md §11
- **Status**: Active — implemented

### D-014: frank init uses three-layer approach

- **Source**: [#155](https://github.com/frank-fs/frank/issues/155), DESIGN_DECISIONS.md §12
- **Status**: Active — not yet reached

### D-015: Generated module naming conflicts are errors

- **Source**: [#283](https://github.com/frank-fs/frank/issues/283), DESIGN_DECISIONS.md §13
- **Status**: Active — not yet reached

### D-016: ALPS validator is semantic only

- **Source**: [#302](https://github.com/frank-fs/frank/issues/302), DESIGN_DECISIONS.md §14
- **Status**: Active — not yet reached

### D-017: CollectorAlgebra in Core, reconstruction in CLI

- **Source**: [#290](https://github.com/frank-fs/frank/issues/290), DESIGN_DECISIONS.md §15
- **Status**: Active — not yet reached

---

## Suspect Decisions from Kitty-Specs

Mined from the 11 suspect kitty-specs. Ordered by severity of flat-semantics contamination.

### KS-026: CLI pipeline is entirely flat

- **Status**: Suspect — **most deeply flat-dependent spec**
- **Decisions**: CLI extracts `StateMachineMetadata` (flat state→HTTP-methods map). All generators consume flat metadata. Validation compares hierarchical specs against flat "code truth." Risk register explicitly says "flat-state mapping first."
- **Impact**: The entire spec pipeline (extract → validate → generate) must be redesigned for hierarchy.

### KS-006: PROV-O models single-state transitions

- **Status**: Suspect
- **Decisions**: `PreviousState`/`NewState` as singular values. One `ProvenanceRecord` per transition. No compound transitions.
- **Impact**: Cannot record AND-state transitions that exit/enter multiple states simultaneously.

### KS-013/018/024: Parsers capture hierarchy, mappers flatten it

- **Status**: Suspect (mappers), Sound (parsers)
- **Pattern**: smcat/SCXML parsers correctly capture composite states and parallel regions in the AST. Then the mapper step flattens to `StateMachine<'S,'E,'C>` (flat generic type) or stores hierarchy in annotations rather than primary fields.
- **Impact**: The AST layer is sound. The mapping/generation layer needs to preserve hierarchy end-to-end.

### KS-020: Shared AST is structurally sound

- **Status**: Sound
- **Note**: `StateNode.Children` and `StateKind` (including `Parallel`, `ShallowHistory`, `DeepHistory`) correctly model hierarchy. The AST is not the problem.

### KS-030: Merge uses flat identifier matching

- **Status**: Suspect
- **Decision**: State matching by flat identifier, transition matching by flat (Source, Target, Event) triple. No hierarchical scope.
- **Impact**: Same-named states at different hierarchy levels would be incorrectly merged.

---

## Sound Foundational Decisions (v7.0–v7.2)

Key architectural decisions from the Spec Kit era that remain valid regardless of hierarchy.

### D-018: Resource-oriented design (Constitution §1)
- **Status**: Active
- Resources are the primary abstraction, not URL patterns with handlers.

### D-019: Library, not framework (Constitution §3)
- **Status**: Active
- No view engine, no ORM, no auth system. Compose with ASP.NET Core.

### D-020: No lightweight API
- **Status**: Active
- The CE ceremony IS the pit of success.

### D-021: Generic endpoint metadata extensibility
- **Source**: spec 013 (Frank.Auth), PR #71
- **Status**: Active
- `(EndpointBuilder -> unit) list` convention functions. Foundation for Frank.Auth, Frank.OpenApi, and future extensions.

### D-022: Two-stage middleware pipeline
- **Source**: spec 011, PR #69
- **Status**: Active
- `plugBeforeRouting` (before `UseRouting()`) + `plug` (between routing and endpoints).

### D-023: Native SSE over external dependency
- **Source**: spec 014, PR #72
- **Status**: Active
- Direct `IBufferWriter<byte>` writes. Zero external NuGet dependencies.

### D-024: TextWriter→Task streaming API
- **Source**: spec 015, PR #73
- **Status**: Active
- View engines write to `TextWriter`; `SseDataLineWriter` bridges to SSE format.

### D-025: Applicative over monad for TransitionResult
- **Source**: PR #223
- **Status**: Active
- `TransitionResult.apply` as primary abstraction. All algebraic laws verified via FsCheck.

---

## Key Evolution Decisions (v7.3.0–v7.4.0)

### D-026: Opt-in hierarchy via StateHierarchy option
- **Source**: PR #221
- **Status**: Active — this is the current architecture
- **Decision**: `StateMachineMetadata.Hierarchy: StateHierarchy option`. When `None`, flat FSM dispatch unchanged.

### D-027: Auto-wrap flat FSMs in synthetic __root__ XOR
- **Source**: PR #259 (`StatefulResourceBuilder.fs`)
- **Status**: Active — implemented
- **Decision**: ALL resources use hierarchical dispatch uniformly. Flat FSMs wrapped in `__root__` XOR composite.

### D-028: Store redesign for hierarchy persistence
- **Source**: PR #259
- **Status**: Active — implemented
- **Decision**: `IStatechartsStore<'S,'C>` with `InstanceSnapshot<'S,'C>` bundles State, Context, HierarchyConfig, HistoryRecord.

### D-029: Closed-world semantics for role projection
- **Source**: PR #274
- **Status**: Active — implemented
- **Decision**: When roles + transitions are declared, undeclared transitions blocked. Everything not explicitly declared is forbidden.

### D-030: Multi-role users see union of affordances
- **Source**: PR #279
- **Status**: Active — implemented
- **Decision**: Union of all matching roles' methods and link relations, not first-match.

### D-031: TransitionSafety DU (Safe/Unsafe/Idempotent)
- **Source**: PR #277
- **Status**: Active — implemented
- **Decision**: Three CE operations: `transition` (Unsafe/POST), `safeTransition` (Safe/GET), `idempotentTransition` (Idempotent/PUT).

### D-032: Link relations use ALPS profile fragment URIs
- **Source**: PR #281
- **Status**: Active — implemented
- **Decision**: `{profileUrl}#{EventName}` instead of bare kebab-case strings. RFC 8288 §2.1.2 compliant.

### D-033: Tagless final over free monad
- **Source**: gh-257
- **Status**: Active
- **Decision**: F# has first-class records of functions but no HKTs. Records compose trivially. Code generation is simpler.

---

## Decision Dependencies

- **D-001 + D-002 + D-007**: LCA is a parameter, Fork is explicit, generated files emit programs with both.
- **D-003 + D-006**: `'r` varies per interpreter; `onTransition` doesn't exist — customization is through interpreters.
- **D-008 + analyzer rules**: If childOf uses value binding, FRANK102 becomes largely unnecessary.
- **D-009 + #296 scope**: Both paths use the same `ValidationAlgebra` interpreter and rules.
- **D-026 + D-027**: Hierarchy is opt-in at spec level but universal at runtime dispatch level.
- **D-029 + D-030**: Closed-world semantics + union-of-affordances for multi-role users.

---

## Summary Statistics

| Category | Count | Status |
|----------|-------|--------|
| Active, implemented | 18 | Stable foundation |
| Active, not yet implemented | 8 | D-007 through D-017 (v7.4.0 roadmap) |
| Active, **NOT implemented despite being specified** | 4 | D-002 (Fork), D-003 (TransitionStep), D-005 (DualAlgebra), D-006 (onTransition removal) |
| Suspect (flat-semantics contamination) | 6 | KS-006, KS-013, KS-018, KS-024, KS-026, KS-030 |
| Contradictions identified | 8 | C-1 through C-8 |
| Dropped designs | 4 | PROV-O, SHACL integration, SPARQL, round-trip testing |
