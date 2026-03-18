# Work Packages: SCXML Native Annotations and Generator Fidelity

**Inputs**: Design documents from `/kitty-specs/028-scxml-native-annotations/`
**Prerequisites**: plan.md (required), spec.md (user stories), research.md (decisions), data-model.md (entities)

**Tests**: Round-trip and unit tests included per spec FR-016-018.

**Organization**: Fine-grained subtasks (`Txxx`) roll up into work packages (`WPxx`). Each work package is independently deliverable and testable.

**Prompt Files**: Each work package references a matching prompt file in `/tasks/`.

---

## Work Package WP01: ScxmlMeta DU Expansion (Priority: P0) 🎯 Foundation

**Goal**: Add 4 new cases to `ScxmlMeta` DU for executable content, initial elements, and data source.
**Independent Test**: `dotnet build` passes across net8.0, net9.0, net10.0. All existing tests pass unchanged.
**Prompt**: `tasks/WP01-scxml-meta-expansion.md`

### Included Subtasks
- [x] T001 Add `ScxmlOnEntry`, `ScxmlOnExit`, `ScxmlInitialElement`, `ScxmlDataSrc` cases to `ScxmlMeta` in `src/Frank.Statecharts/Ast/Types.fs`
- [x] T002 Verify `dotnet build` and `dotnet test` across all target frameworks

### Implementation Notes
- T001 is a purely additive change — 4 new cases appended to the existing 8-case DU. No consumers break.
- T002 must verify all 3 target frameworks and all existing tests pass.

### Parallel Opportunities
- None (single file, sequential).

### Dependencies
- None (starting package).

### Risks & Mitigations
- Minimal risk — additive DU expansion with no consumer changes.

---

## Work Package WP02: Parser Captures All Content (Priority: P1)

**Goal**: Update SCXML parser to capture executable content, `<initial>` elements, `<data src>`, and namespace origin. Populate `StateActivities` from `<onentry>`/`<onexit>`.
**Independent Test**: Parse SCXML with all features; verify `StateActivities` populated, all annotations present, existing tests pass.
**Prompt**: `tasks/WP02-parser-captures-content.md`

### Included Subtasks
- [x] T003 Parse `<onentry>` blocks: remove from `outOfScopeElements`, store raw XML as `ScxmlOnEntry`, extract actions to `StateActivities.Entry`
- [x] T004 Parse `<onexit>` blocks: same pattern as T003 but for `<onexit>` → `ScxmlOnExit` + `StateActivities.Exit`
- [x] T005 [P] Parse `<initial>` child elements: store `ScxmlInitialElement(targetId)` annotation
- [x] T006 [P] Capture `<data src="...">` attribute: store `ScxmlDataSrc(name, src)` annotation
- [x] T007 [P] Store `ScxmlNamespace` annotation with actual namespace from parsed document
- [x] T008 Update `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` with new tests for all captured content
- [x] T009 Verify `dotnet build` and `dotnet test`

### Implementation Notes
- T003 is the largest change: remove `"onentry"` from `outOfScopeElements` set (line 38-52), add parsing logic in `parseState` function, build `StateActivities`, store raw XML annotation. One `ScxmlOnEntry` per `<onentry>` block (clarification Q1).
- Action description format: `"{elementName} {key-attribute-value}"` — e.g., `"send done"`, `"log hello"`, `"assign x"`.
- `StateActivities` set to `Some { Entry = ...; Exit = ...; Do = [] }` when any content exists, `None` when no content.
- T005-T007 are independent from T003-T004 (different element types).

### Parallel Opportunities
- T005, T006, T007 are independent (different elements/attributes in the parser).

### Dependencies
- Depends on WP01 (requires `ScxmlOnEntry`, `ScxmlOnExit`, `ScxmlInitialElement`, `ScxmlDataSrc` types).

### Risks & Mitigations
- Parser complexity: Parser.fs is 401 lines. Changes are additive — new parsing blocks within existing `parseState` function.
- Multiple `<onentry>` blocks: one annotation per block, aggregated `StateActivities.Entry`.
- `<data src>` needs to be stored as document-level annotation since `DataEntry` record doesn't have a `src` field. Use `ScxmlDataSrc` on the document's annotation list.

---

## Work Package WP03: Generator Emits All Content (Priority: P1)

**Goal**: Update SCXML generator to emit executable content, `<initial>` elements, `<data src>`, and respect namespace from annotations.
**Independent Test**: Generate SCXML from annotated AST; verify output contains `<onentry>`/`<onexit>`, `<initial>` elements, `<data src>` attributes, correct namespace.
**Prompt**: `tasks/WP03-generator-emits-content.md`

### Included Subtasks
- [ ] T010 Emit `<onentry>` blocks from `ScxmlOnEntry` annotations via `XElement.Parse`
- [ ] T011 Emit `<onexit>` blocks from `ScxmlOnExit` annotations via `XElement.Parse`
- [ ] T012 [P] Emit `<initial>` child elements from `ScxmlInitialElement` annotations
- [ ] T013 [P] Emit `src` attribute on `<data>` elements from `ScxmlDataSrc` annotations
- [ ] T014 [P] Respect namespace from `ScxmlNamespace` annotation (or default to W3C)
- [ ] T015 Update `test/Frank.Statecharts.Tests/Scxml/GeneratorTests.fs` with new tests
- [ ] T016 Verify `dotnet build` and `dotnet test`

### Implementation Notes
- T010-T011: For each `ScxmlOnEntry`/`ScxmlOnExit` annotation, call `XElement.Parse(xml)` and add the resulting element to the state's `XElement`. Emit in order: `<onentry>` blocks, then `<onexit>` blocks, then child states and transitions.
- T012: When `ScxmlInitialElement` is present, emit `<initial><transition target="targetId"/></initial>` instead of the `initial` attribute. Remove `initial` attribute emission when the child element form is used.
- T013: Match `ScxmlDataSrc` annotations by data entry name and add `src` attribute to the `<data>` element.
- T014: Check document annotations for `ScxmlNamespace`. If present and non-empty, use that namespace. If empty string, use `XNamespace.None`. If absent, default to W3C namespace (backward compatible).
- Generator.fs is 206 lines — changes are in `generateState` and `generateRoot`.

### Parallel Opportunities
- T012, T013, T014 are independent (different elements/attributes in the generator).

### Dependencies
- Depends on WP01 (requires new `ScxmlMeta` cases).

### Risks & Mitigations
- `XElement.Parse()` may fail on malformed XML stored in annotations. Wrap in `try/catch` — if parse fails, skip the block and emit a warning comment in the XML.
- Namespace change affects the `XNamespace` used for all element construction. Need to thread the namespace through `generateState` and `generateRoot`.

---

## Work Package WP04: Round-Trip Fidelity Tests (Priority: P2)

**Goal**: Extend SCXML round-trip tests with comprehensive fixtures covering all captured content. Prove zero information loss.
**Independent Test**: All round-trip tests pass, including executable content, initial elements, data src, namespace preservation.
**Prompt**: `tasks/WP04-round-trip-fidelity.md`

### Included Subtasks
- [ ] T017 Add comprehensive SCXML fixture with onentry/onexit, history, invoke, data, initial elements, namespace
- [ ] T018 Add round-trip test for executable content preservation (onentry/onexit survive cycle)
- [ ] T019 Add round-trip test for `<initial>` child element preservation
- [ ] T020 Add round-trip test for `<data src>` preservation
- [ ] T021 Verify `dotnet build` and `dotnet test`

### Implementation Notes
- Extend existing `test/Frank.Statecharts.Tests/Scxml/RoundTripTests.fs`.
- Use existing `stripDocPositions` pattern for comparison (positions differ between parses).
- Comprehensive fixture should exercise: `<state>`, `<parallel>`, `<final>`, `<history>`, `<invoke>`, `<datamodel>`/`<data>`, `<onentry>`, `<onexit>`, `<initial>` child element, `<transition>` with multi-target, internal transitions.
- Each test: parse XML → generate → parse again → compare ASTs (stripped of positions).

### Parallel Opportunities
- T018-T020 are independent test cases.

### Dependencies
- Depends on WP02, WP03 (requires both parser capture and generator emission to be in place).

### Risks & Mitigations
- XML formatting differences: `XDocument.Save()` may format differently than input. Comparison is at AST level, not string level, so this is handled.
- Namespace differences in serialized XML: the `ScxmlNamespace` annotation ensures the generator uses the correct namespace.

---

## Dependency & Execution Summary

```
WP01 (ScxmlMeta) ──┬──→ WP02 (Parser)    ──┐
                    └──→ WP03 (Generator)  ──┼──→ WP04 (Round-Trip Tests)
```

- **Sequence**: WP01 → { WP02, WP03 } (parallel) → WP04
- **Parallelization**: WP02 and WP03 can proceed concurrently after WP01 completes.
- **MVP Scope**: WP01 + WP02 + WP03.

---

## Subtask Index (Reference)

| Subtask ID | Summary | Work Package | Priority | Parallel? |
|------------|---------|--------------|----------|-----------|
| T001 | Add 4 new ScxmlMeta cases | WP01 | P0 | No |
| T002 | Verify build and tests | WP01 | P0 | No |
| T003 | Parse onentry blocks (raw XML + activities) | WP02 | P1 | No |
| T004 | Parse onexit blocks (raw XML + activities) | WP02 | P1 | No |
| T005 | Parse initial child elements | WP02 | P1 | Yes |
| T006 | Capture data src attribute | WP02 | P1 | Yes |
| T007 | Store ScxmlNamespace | WP02 | P1 | Yes |
| T008 | Update ParserTests | WP02 | P1 | No |
| T009 | Verify build and tests | WP02 | P1 | No |
| T010 | Emit onentry from annotations | WP03 | P1 | No |
| T011 | Emit onexit from annotations | WP03 | P1 | No |
| T012 | Emit initial child elements | WP03 | P1 | Yes |
| T013 | Emit data src attribute | WP03 | P1 | Yes |
| T014 | Respect namespace annotation | WP03 | P1 | Yes |
| T015 | Update GeneratorTests | WP03 | P1 | No |
| T016 | Verify build and tests | WP03 | P1 | No |
| T017 | Add comprehensive SCXML fixture | WP04 | P2 | No |
| T018 | Executable content round-trip test | WP04 | P2 | Yes |
| T019 | Initial element round-trip test | WP04 | P2 | Yes |
| T020 | Data src round-trip test | WP04 | P2 | Yes |
| T021 | Verify build and tests | WP04 | P2 | No |
