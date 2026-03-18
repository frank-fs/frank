# Work Packages: Cross-Format Validation Pipeline and AST Merge

**Inputs**: Design documents from `/kitty-specs/030-cross-format-validation-pipeline/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md

**Tests**: End-to-end integration tests and unit tests included per spec FR-008 through FR-010.

---

## Work Package WP01: Foundation — FormatTag, StringDistance, AlpsXml Dispatch (Priority: P0) 🎯 Foundation

**Goal**: Add `FormatTag.AlpsXml`, implement Jaro-Winkler string distance, wire ALPS XML into pipeline dispatch.
**Independent Test**: `dotnet build` and `dotnet test` pass. Pipeline dispatches ALPS XML correctly.
**Prompt**: `tasks/WP01-foundation.md`

### Included Subtasks
- [x] T001 Add `AlpsXml` case to `FormatTag` in `src/Frank.Statecharts/Validation/Types.fs`
- [x] T002 Create `src/Frank.Statecharts/Validation/StringDistance.fs` with Jaro-Winkler implementation
- [x] T003 Wire `FormatTag.AlpsXml` dispatch in `src/Frank.Statecharts/Validation/Pipeline.fs`
- [x] T004 Update `Frank.Statecharts.fsproj` with new files in correct compilation order
- [x] T005 Fix exhaustive pattern matches broken by new `AlpsXml` case
- [x] T006 Verify `dotnet build` and `dotnet test`

### Dependencies
- None (starting package).

---

## Work Package WP02: Merge Function (Priority: P1) 🎯 MVP

**Goal**: Implement `Pipeline.mergeSources` — left fold over format-priority-sorted ASTs with annotation accumulation.
**Independent Test**: Merge WSD + ALPS sources → unified document with both topology and annotations.
**Prompt**: `tasks/WP02-merge-function.md`

### Included Subtasks
- [x] T007 Implement `formatPriority` function mapping `FormatTag` to integer priority
- [x] T008 Implement state matching by identifier with annotation accumulation
- [x] T009 Implement transition matching by (source, target, event) with annotation accumulation
- [x] T010 Implement `mergeSources` as left fold over priority-sorted parsed documents
- [x] T011 Handle edge cases: single source, no overlap, ALPS-only states
- [x] T012 Add merge unit tests
- [x] T013 Verify `dotnet build` and `dotnet test`

### Dependencies
- Depends on WP01.

---

## Work Package WP03: Near-Match Detection (Priority: P1)

**Goal**: Add near-match validation rule using Jaro-Winkler to detect similar-but-not-identical state/event names across formats.
**Independent Test**: Validate formats with "start" vs "startOnboarding" → near-match warning with similarity score.
**Prompt**: `tasks/WP03-near-match-detection.md`

### Included Subtasks
- [ ] T014 Add near-match validation rule to `CrossFormatRules` in `src/Frank.Statecharts/Validation/Validator.fs`
- [ ] T015 Rule checks state identifiers across format pairs for Jaro-Winkler similarity > threshold
- [ ] T016 Rule checks event names across format pairs for near-matches
- [ ] T017 Report near-match warnings with format pair, identifiers, similarity score
- [ ] T018 Add near-match unit tests
- [ ] T019 Verify `dotnet build` and `dotnet test`

### Dependencies
- Depends on WP01 (requires StringDistance.fs).

---

## Work Package WP04: End-to-End Integration Tests (Priority: P1)

**Goal**: Add integration tests that parse real format text through the full pipeline (validate + merge).
**Independent Test**: Parse same state machine in 4 formats, validate → zero failures, merge → unified document.
**Prompt**: `tasks/WP04-end-to-end-tests.md`

### Included Subtasks
- [ ] T020 Create multi-format test fixtures (same state machine in WSD, smcat, SCXML, ALPS JSON)
- [ ] T021 Add end-to-end test: consistent formats → zero validation failures
- [ ] T022 Add end-to-end test: intentional mismatches → correct failure detection
- [ ] T023 Add end-to-end test: validate then merge → unified document with annotations from all formats
- [ ] T024 Add end-to-end test: near-match detection with real format text
- [ ] T025 Verify `dotnet build` and `dotnet test`

### Dependencies
- Depends on WP02 (merge function), WP03 (near-match detection).

---

## Dependency & Execution Summary

```
WP01 (Foundation) ──┬──→ WP02 (Merge)      ──┐
                     └──→ WP03 (Near-Match) ──┼──→ WP04 (E2E Tests)
```

- **Sequence**: WP01 → { WP02, WP03 } (parallel) → WP04
- **Parallelization**: WP02 and WP03 can proceed concurrently after WP01.
- **MVP Scope**: WP01 + WP02 (merge function proven).

---

## Subtask Index (Reference)

| Subtask ID | Summary | Work Package | Priority | Parallel? |
|------------|---------|--------------|----------|-----------|
| T001 | Add FormatTag.AlpsXml | WP01 | P0 | No |
| T002 | Jaro-Winkler implementation | WP01 | P0 | Yes |
| T003 | Wire AlpsXml dispatch in Pipeline | WP01 | P0 | No |
| T004 | Update .fsproj | WP01 | P0 | No |
| T005 | Fix exhaustive pattern matches | WP01 | P0 | No |
| T006 | Verify build and tests | WP01 | P0 | No |
| T007 | Format priority function | WP02 | P1 | No |
| T008 | State matching with annotation accumulation | WP02 | P1 | No |
| T009 | Transition matching with annotation accumulation | WP02 | P1 | No |
| T010 | mergeSources left fold | WP02 | P1 | No |
| T011 | Edge cases (single source, no overlap, ALPS-only) | WP02 | P1 | No |
| T012 | Merge unit tests | WP02 | P1 | No |
| T013 | Verify build and tests | WP02 | P1 | No |
| T014 | Near-match validation rule | WP03 | P1 | No |
| T015 | State identifier near-match check | WP03 | P1 | No |
| T016 | Event name near-match check | WP03 | P1 | No |
| T017 | Near-match warning reporting | WP03 | P1 | No |
| T018 | Near-match unit tests | WP03 | P1 | No |
| T019 | Verify build and tests | WP03 | P1 | No |
| T020 | Multi-format test fixtures | WP04 | P1 | No |
| T021 | E2E: consistent formats | WP04 | P1 | No |
| T022 | E2E: intentional mismatches | WP04 | P1 | No |
| T023 | E2E: validate then merge | WP04 | P1 | No |
| T024 | E2E: near-match with real text | WP04 | P1 | No |
| T025 | Verify build and tests | WP04 | P1 | No |
