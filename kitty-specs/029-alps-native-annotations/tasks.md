# Work Packages: ALPS Native Annotations and Full Fidelity

**Inputs**: Design documents from `/kitty-specs/029-alps-native-annotations/`
**Prerequisites**: plan.md (required), spec.md (user stories), research.md (decisions), data-model.md (entities)

**Tests**: Round-trip and cross-format tests included per spec FR-009.

---

## Work Package WP01: Extract Shared Classification Module (Priority: P0) 🎯 Foundation

**Goal**: Extract shared intermediate types and Pass 2 classification heuristics from `JsonParser.fs` into `Alps/Classification.fs`. Refactor `JsonParser.fs` to import from the shared module.
**Independent Test**: `dotnet build` and `dotnet test` pass. JsonParser behavior unchanged.
**Prompt**: `tasks/WP01-extract-classification.md`

### Included Subtasks
- [ ] T001 Create `Alps/Classification.fs` with `internal` intermediate types (`ParsedDescriptor`, `ParsedExtension`, `ParsedLink`)
- [ ] T002 Move classification functions to `Classification.fs` (`isTransitionTypeStr`, `collectRtTargets`, `isStateDescriptor`, `buildDescriptorIndex`, `resolveRt`, `extractGuard`, `extractParameters`, `toTransitionKind`, `resolveDescriptor`, `extractTransitions`, `toStateNode`, `buildStateAnnotations`, `buildTransitionAnnotations`)
- [ ] T003 Update `JsonParser.fs` to import from `Classification` module instead of defining these privately
- [ ] T004 Add `Alps/Classification.fs` to `Frank.Statecharts.fsproj` BEFORE `Alps/JsonParser.fs` in compilation order
- [ ] T005 Verify `dotnet build` and `dotnet test` across all target frameworks

### Dependencies
- None (starting package).

### Risks & Mitigations
- F# compilation order matters — `Classification.fs` MUST appear before `JsonParser.fs` in `.fsproj`.
- Functions transition from `private` to `internal` — verify no naming conflicts with other modules.

---

## Work Package WP02: JSON Round-Trip Fidelity Tests (Priority: P1)

**Goal**: Add comprehensive JSON round-trip tests including Amundsen's onboarding example. Fix any fidelity gaps discovered.
**Independent Test**: All JSON round-trip tests pass — parse → generate → parse produces structurally equal ASTs.
**Prompt**: `tasks/WP02-json-round-trip-tests.md`

### Included Subtasks
- [ ] T006 Add Amundsen's onboarding example as inline test fixture
- [ ] T007 Add JSON round-trip test (parse → generate → parse → compare ASTs)
- [ ] T008 Add tests for edge cases: shared transitions, nested descriptors, data descriptors with documentation
- [ ] T009 Fix any JSON generator fidelity gaps discovered during testing
- [ ] T010 Verify `dotnet build` and `dotnet test`

### Dependencies
- Depends on WP01 (JsonParser.fs was refactored).

### Risks & Mitigations
- Round-trip may reveal unexpected fidelity gaps in the generator. Budget time for fixes in T009.

---

## Work Package WP03: ALPS XML Parser (Priority: P2)

**Goal**: Create new ALPS XML parser that produces identical ASTs to the JSON parser for equivalent input.
**Independent Test**: Parse ALPS XML documents; verify AST structure. Cross-format equivalence with JSON parser.
**Prompt**: `tasks/WP03-alps-xml-parser.md`

### Included Subtasks
- [ ] T011 Create `Alps/XmlParser.fs` — Pass 1: parse `<alps>`, `<descriptor>`, `<doc>`, `<ext>`, `<link>` elements to `ParsedDescriptor` list
- [ ] T012 Wire Pass 2 classification from `Classification` module to produce `StatechartDocument`
- [ ] T013 Add `Alps/XmlParser.fs` to `Frank.Statecharts.fsproj` after `Classification.fs`
- [ ] T014 Add XML parser tests in `test/Frank.Statecharts.Tests/Alps/`
- [ ] T015 Add cross-format equivalence test (JSON and XML parsers produce identical ASTs for equivalent input)
- [ ] T016 Verify `dotnet build` and `dotnet test`

### Dependencies
- Depends on WP01 (requires `Classification` module for shared types and Pass 2 logic).

### Risks & Mitigations
- XML attribute names differ from JSON property names in some cases (e.g., `rt` vs `returnType`). Verify mapping matches the ALPS spec.
- `System.Xml.Linq` dependency already available from SCXML work.

---

## Work Package WP04: ALPS XML Generator + Round-Trip Tests (Priority: P2)

**Goal**: Create ALPS XML generator and add XML round-trip tests. Verify end-to-end fidelity for both formats.
**Independent Test**: XML round-trip tests pass. Generated XML is well-formed and parseable.
**Prompt**: `tasks/WP04-alps-xml-generator.md`

### Included Subtasks
- [ ] T017 Create `Alps/XmlGenerator.fs` — generate ALPS XML from `StatechartDocument`
- [ ] T018 Add `Alps/XmlGenerator.fs` to `Frank.Statecharts.fsproj`
- [ ] T019 Add XML generator tests
- [ ] T020 Add XML round-trip test (parse XML → generate XML → parse XML → compare ASTs)
- [ ] T021 Verify `dotnet build` and `dotnet test`

### Dependencies
- Depends on WP01 (Classification module), WP03 (XML parser needed for round-trip tests).

### Risks & Mitigations
- XML formatting (indentation, attribute ordering) may differ between input and output. Compare at AST level, not string level.

---

## Dependency & Execution Summary

```
WP01 (Classification) ──┬──→ WP02 (JSON Round-Trip)
                         ├──→ WP03 (XML Parser)  ──→ WP04 (XML Generator + Round-Trip)
```

- **Sequence**: WP01 → { WP02, WP03 } (parallel) → WP04
- **Parallelization**: WP02 and WP03 can proceed concurrently after WP01.
- **MVP Scope**: WP01 + WP02 (JSON fidelity proven).

---

## Subtask Index (Reference)

| Subtask ID | Summary | Work Package | Priority | Parallel? |
|------------|---------|--------------|----------|-----------|
| T001 | Create Classification.fs with intermediate types | WP01 | P0 | No |
| T002 | Move classification functions | WP01 | P0 | No |
| T003 | Refactor JsonParser.fs imports | WP01 | P0 | No |
| T004 | Update .fsproj compilation order | WP01 | P0 | No |
| T005 | Verify build and tests | WP01 | P0 | No |
| T006 | Add Amundsen onboarding fixture | WP02 | P1 | No |
| T007 | Add JSON round-trip test | WP02 | P1 | No |
| T008 | Add edge case tests | WP02 | P1 | Yes |
| T009 | Fix generator fidelity gaps | WP02 | P1 | No |
| T010 | Verify build and tests | WP02 | P1 | No |
| T011 | Create XmlParser.fs Pass 1 | WP03 | P2 | No |
| T012 | Wire Pass 2 classification | WP03 | P2 | No |
| T013 | Update .fsproj | WP03 | P2 | No |
| T014 | Add XML parser tests | WP03 | P2 | No |
| T015 | Add cross-format equivalence test | WP03 | P2 | No |
| T016 | Verify build and tests | WP03 | P2 | No |
| T017 | Create XmlGenerator.fs | WP04 | P2 | No |
| T018 | Update .fsproj | WP04 | P2 | No |
| T019 | Add XML generator tests | WP04 | P2 | No |
| T020 | Add XML round-trip test | WP04 | P2 | No |
| T021 | Verify build and tests | WP04 | P2 | No |
