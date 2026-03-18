# Work Packages: smcat Native Annotations and Generator Fidelity

**Inputs**: Design documents from `/kitty-specs/027-smcat-native-annotations/`
**Prerequisites**: plan.md (required), spec.md (user stories), research.md (decisions), data-model.md (entities)

**Tests**: Round-trip tests included per spec FR-015 and User Story 4.

**Organization**: Fine-grained subtasks (`Txxx`) roll up into work packages (`WPxx`). Each work package is independently deliverable and testable.

**Prompt Files**: Each work package references a matching prompt file in `/tasks/`.

---

## Work Package WP01: AST Type Definitions and Rename (Priority: P0) 🎯 Foundation

**Goal**: Expand `SmcatMeta` DU with new types and rename `SmcatActivity` → `SmcatCustomAttribute` across all consumers. This is the atomic foundation that all other WPs depend on.
**Independent Test**: `dotnet build` passes across net8.0, net9.0, net10.0 with zero errors. All existing tests pass unchanged (behavioral semantics preserved by rename).
**Prompt**: `tasks/WP01-ast-type-definitions.md`

### Included Subtasks
- [x] T001 Add `SmcatTypeOrigin` and `SmcatTransitionKind` DUs to `src/Frank.Statecharts/Ast/Types.fs`
- [x] T002 Add `SmcatStateType` and `SmcatTransition` cases to `SmcatMeta` in `src/Frank.Statecharts/Ast/Types.fs`
- [x] T003 Rename `SmcatActivity` → `SmcatCustomAttribute` in `src/Frank.Statecharts/Ast/Types.fs`, `src/Frank.Statecharts/Smcat/Parser.fs`, `src/Frank.Statecharts/Smcat/Serializer.fs`
- [x] T004 Verify `dotnet build` and `dotnet test` across all target frameworks

### Implementation Notes
- T001-T002 are additive changes to `Ast/Types.fs` — no consumers break.
- T003 is a rename that touches 3 files: type definition, construction site (Parser.fs:377), pattern match (Serializer.fs:46).
- T004 must verify all 3 target frameworks and all existing tests pass.

### Parallel Opportunities
- T001 and T002 are independent additions to the same file (but sequential in practice since it's one file).

### Dependencies
- None (starting package).

### Risks & Mitigations
- Rename may have consumers not found by search → mitigated: impact analysis in research.md confirms only 3 source files reference `SmcatActivity`.

---

## Work Package WP02: Generator Native Annotations (Priority: P1) 🎯 MVP

**Goal**: Update the smcat generator to produce `StateNode` entries with correct `StateKind` values and `SmcatAnnotation` metadata on every state and transition.
**Independent Test**: Generate a `StatechartDocument` from test metadata; verify initial/final states have correct `Kind` and annotations, all transitions carry `SmcatTransition` annotations.
**Prompt**: `tasks/WP02-generator-native-annotations.md`

### Included Subtasks
- [ ] T005 Update initial pseudo-state node: `Kind = Initial` + `SmcatAnnotation(SmcatStateType(Initial, Explicit))`
- [ ] T006 Update final pseudo-state nodes: `Kind = Final` + `SmcatAnnotation(SmcatStateType(Final, Explicit))`
- [ ] T007 Add `SmcatAnnotation(SmcatTransition ...)` to all generated transitions with correct `SmcatTransitionKind`
- [ ] T008 Update `test/Frank.Statecharts.Tests/Smcat/GeneratorTests.fs` to verify Kind values and annotations
- [ ] T009 Verify `dotnet build` and `dotnet test`

### Implementation Notes
- Initial pseudo-state: `Identifier = Some "initial"` retained (smcat convention), `Kind` changed from `Regular` to `Initial`.
- Final pseudo-state: `Identifier = Some "final"` retained, `Kind` changed from `Regular` to `Final`.
- Regular states: no `SmcatStateType` annotation (absence = `Regular, Inferred` default).
- Transition kind inference: `initial =>` → `InitialTransition`, self-loops → `SelfTransition`, `=> final` → `FinalTransition`.
- Existing tests will need updating since they assert `Kind = Regular` and empty annotations.

### Parallel Opportunities
- T005 and T006 are independent state node changes. T007 is independent transition change. All in same file but logically separate.

### Dependencies
- Depends on WP01 (requires `SmcatStateType`, `SmcatTransition`, `SmcatTransitionKind` types).

### Risks & Mitigations
- Existing GeneratorTests assert on `Source = "initial"` string — these assertions remain valid. New assertions added for Kind and annotations.

---

## Work Package WP03: Serializer Annotation Consumption (Priority: P1)

**Goal**: Update the smcat serializer to consume `SmcatStateType` annotations for type attribute emission, with fallback for cross-format ASTs.
**Independent Test**: Serialize a hand-crafted `StatechartDocument` with explicit/inferred type annotations; verify explicit types emit `[type="..."]` and inferred types do not.
**Prompt**: `tasks/WP03-serializer-annotation-consumption.md`

### Included Subtasks
- [ ] T010 Add `StateKind → smcat type string` mapping function to `src/Frank.Statecharts/Smcat/Serializer.fs`
- [ ] T011 [P] Update `serializeAttributes` to consume `SmcatStateType` annotations (Explicit → emit, Inferred → skip)
- [ ] T012 [P] Add fallback for states without `SmcatAnnotation` (use `StateNode.Kind` for non-Regular)
- [ ] T013 Verify `dotnet build` and `dotnet test`

### Implementation Notes
- `StateKind` → string mapping: `Initial` → `"initial"`, `Final` → `"final"`, `Choice` → `"choice"`, `ForkJoin` → `"forkjoin"`, `ShallowHistory` → `"history"`, `DeepHistory` → `"deep.history"`, `Terminate` → `"terminate"`, `Parallel` → `"parallel"`, `Regular` → `"regular"`.
- Explicit origin: emit `type="<kind>"` in the `[...]` attribute block alongside color, label, custom attributes.
- Inferred origin: do NOT emit type attribute.
- Fallback (no SmcatAnnotation at all): check `StateNode.Kind` — if not Regular, emit `[type="<kind>"]`. This handles cross-format ASTs (e.g., SCXML → smcat serialization).

### Parallel Opportunities
- T011 and T012 are independent code paths in the serializer (annotation-based vs fallback).

### Dependencies
- Depends on WP01 (requires `SmcatStateType`, `SmcatTypeOrigin` types).

### Risks & Mitigations
- Attribute ordering: existing tests may assert specific attribute order. The new `type` attribute should be emitted after existing attributes (color, label, custom) to minimize test churn.

---

## Work Package WP04: Parser Type Origin Tracking (Priority: P1)

**Goal**: Update the smcat parser to store `SmcatStateType` annotations with Explicit/Inferred origin and `SmcatTransition` annotations on parsed transitions.
**Independent Test**: Parse smcat text with explicit `[type="initial"]` and naming-convention states; verify correct `SmcatStateType` annotations. Parse transitions and verify `SmcatTransition` annotations.
**Prompt**: `tasks/WP04-parser-type-origin-tracking.md`

### Included Subtasks
- [ ] T014 Update attribute-to-annotation conversion in `src/Frank.Statecharts/Smcat/Parser.fs` to store `SmcatAnnotation(SmcatStateType(kind, Explicit))` when `[type="..."]` attribute present
- [ ] T015 Add `SmcatAnnotation(SmcatStateType(kind, Inferred))` after `inferStateType` for non-Regular states
- [ ] T016 Add `SmcatAnnotation(SmcatTransition ...)` to parsed transitions based on structural analysis
- [ ] T017 Update `test/Frank.Statecharts.Tests/Smcat/ParserTests.fs` to verify SmcatStateType and SmcatTransition annotations
- [ ] T018 Verify `dotnet build` and `dotnet test`

### Implementation Notes
- Parser.fs line 370-377: attribute-to-annotation conversion. Currently `type` key is consumed by `inferStateType` and NOT stored. New behavior: consume AND store as `SmcatAnnotation(SmcatStateType(kind, Explicit))`.
- After `inferStateType` call (line ~380): if result is not `Regular`, add `SmcatAnnotation(SmcatStateType(kind, Inferred))`. If `Regular`, omit (default).
- Transition kind inference: `Source = "initial"` → `InitialTransition`, `Source = Target` → `SelfTransition`, `Target = Some "final"` → `FinalTransition`, inside composite `{...}` → `InternalTransition`, else → `ExternalTransition`.
- Existing ParserTests verify `Kind` and `SmcatColor` — add new assertions for `SmcatStateType` annotations.

### Parallel Opportunities
- T014 (explicit type) and T015 (inferred type) are independent code paths in the parser.
- T016 (transitions) is independent from T014/T015 (states).

### Dependencies
- Depends on WP01 (requires `SmcatStateType`, `SmcatTransition`, `SmcatTypeOrigin`, `SmcatTransitionKind` types).

### Risks & Mitigations
- Parser is the most complex file (894 lines). Changes should be surgical — only modify the annotation construction points, not the parsing logic.
- Transition kind inference for internal transitions requires detecting whether parsing is inside a `{...}` composite block. The parser already tracks nesting depth — use this.

---

## Work Package WP05: Round-Trip Fidelity Tests (Priority: P2)

**Goal**: Extend existing round-trip tests with annotation-aware structural comparison and new golden files that exercise the full annotation system.
**Independent Test**: All round-trip tests pass, including new structural equivalence checks that verify annotations survive parse→serialize→parse cycles.
**Prompt**: `tasks/WP05-round-trip-fidelity-tests.md`

### Included Subtasks
- [ ] T019 Add new golden files to `test/Frank.Statecharts.Tests/Smcat/RoundTripTests.fs` with explicit types, colors, custom attributes, and activities
- [ ] T020 Implement `assertStructuralEquivalence` function with annotation comparison
- [ ] T021 Update `roundtrip` helper to include structural equivalence assertion alongside semantic equivalence
- [ ] T022 Add test cases exercising explicit/inferred type preservation and SmcatTransitionKind round-trip
- [ ] T023 Verify `dotnet build` and `dotnet test`

### Implementation Notes
- Extend existing `RoundTripTests.fs` — do not create new test files.
- `assertStructuralEquivalence` should compare: state identifiers, kinds, annotations (including SmcatStateType origin), transition sources/targets/events/guards/actions/annotations, activities, children (recursive).
- New golden files should include: `myState [type="initial"];`, `idle [color="red"];`, composite states with internal transitions, states with `[type="regular"]` (explicit regular).
- SmcatTransitionKind on parsed transitions is inferred from structure — round-trip preserves it because the serializer outputs the same structure that the parser will re-infer from.

### Parallel Opportunities
- T019 (golden files) and T020 (comparison function) are independent.

### Dependencies
- Depends on WP02, WP03, WP04 (requires all annotation producers and consumers to be in place).

### Risks & Mitigations
- Structural comparison may be too strict (e.g., annotation ordering). Use set-based comparison for annotations, not list equality.
- Some golden files may produce slightly different whitespace on round-trip. Semantic + structural comparison catches real issues without being whitespace-sensitive.

---

## Dependency & Execution Summary

```
WP01 (AST Types) ──┬──→ WP02 (Generator)  ──┐
                    ├──→ WP03 (Serializer)  ──┼──→ WP05 (Round-Trip Tests)
                    └──→ WP04 (Parser)      ──┘
```

- **Sequence**: WP01 → { WP02, WP03, WP04 } (parallel) → WP05
- **Parallelization**: WP02, WP03, WP04 can all proceed concurrently after WP01 completes.
- **MVP Scope**: WP01 + WP02 (generator produces typed AST with annotations).

---

## Subtask Index (Reference)

| Subtask ID | Summary | Work Package | Priority | Parallel? |
|------------|---------|--------------|----------|-----------|
| T001 | Add SmcatTypeOrigin + SmcatTransitionKind DUs | WP01 | P0 | No |
| T002 | Add SmcatStateType + SmcatTransition cases to SmcatMeta | WP01 | P0 | No |
| T003 | Rename SmcatActivity → SmcatCustomAttribute (3 files) | WP01 | P0 | No |
| T004 | Verify build and tests | WP01 | P0 | No |
| T005 | Generator: initial state Kind + annotation | WP02 | P1 | No |
| T006 | Generator: final state Kind + annotation | WP02 | P1 | No |
| T007 | Generator: SmcatTransition annotations on all transitions | WP02 | P1 | No |
| T008 | Update GeneratorTests.fs | WP02 | P1 | No |
| T009 | Verify build and tests | WP02 | P1 | No |
| T010 | Serializer: StateKind → smcat type string mapping | WP03 | P1 | No |
| T011 | Serializer: consume SmcatStateType annotations | WP03 | P1 | Yes |
| T012 | Serializer: fallback for non-smcat ASTs | WP03 | P1 | Yes |
| T013 | Verify build and tests | WP03 | P1 | No |
| T014 | Parser: explicit type → SmcatStateType(kind, Explicit) | WP04 | P1 | No |
| T015 | Parser: inferred type → SmcatStateType(kind, Inferred) for non-Regular | WP04 | P1 | No |
| T016 | Parser: SmcatTransition annotations on transitions | WP04 | P1 | Yes |
| T017 | Update ParserTests.fs | WP04 | P1 | No |
| T018 | Verify build and tests | WP04 | P1 | No |
| T019 | Add new golden files with explicit types, colors, activities | WP05 | P2 | Yes |
| T020 | Implement assertStructuralEquivalence | WP05 | P2 | Yes |
| T021 | Update roundtrip helper | WP05 | P2 | No |
| T022 | Add explicit/inferred round-trip test cases | WP05 | P2 | No |
| T023 | Verify build and tests | WP05 | P2 | No |
