---
work_package_id: WP02
title: JSON Round-Trip Fidelity Tests
lane: "doing"
dependencies: [WP01]
base_branch: 029-alps-native-annotations-WP01
base_commit: 666360c2268bcc73f70027a51d33121deb4f4827
created_at: '2026-03-18T14:34:08.798584+00:00'
subtasks: [T006, T007, T008, T009, T010]
phase: Phase 1 - Validation
assignee: ''
agent: "claude-opus-reviewer"
shell_pid: "74458"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T14:14:54Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-009]
---

# Work Package Prompt: WP02 – JSON Round-Trip Fidelity Tests

## Review Feedback
*[Empty initially.]*

---

## Implementation Command
```bash
spec-kitty implement WP02 --base WP01
```

## Objectives & Success Criteria

- Amundsen's onboarding example round-trips through JSON parse → generate → parse with structural equality (SC-001)
- Edge cases (shared transitions, nested descriptors, data descriptors with doc) round-trip cleanly
- Any fidelity gaps discovered are fixed in the generator
- SC-003 satisfied

## Context & Constraints

- **Spec**: FR-001 (JSON round-trip), FR-002-004 (generator fidelity)
- **Research**: R-003 (JSON fidelity gaps analysis — current gaps are minimal)
- **Files**: `test/Frank.Statecharts.Tests/Alps/` test files, `src/Frank.Statecharts/Alps/JsonGenerator.fs` (if fixes needed)

## Subtasks & Detailed Guidance

### T006 – Add Amundsen's onboarding example fixture

- **Purpose**: The canonical ALPS document for validation — from Mike Amundsen's RESTFest 2018 talk.
- **File**: Add to existing ALPS test file or create `test/Frank.Statecharts.Tests/Alps/RoundTripTests.fs`
- **Steps**:
  1. Add the onboarding ALPS JSON as an inline fixture string:
     ```fsharp
     let amundsenOnboarding = """{
       "alps": {
         "version": "1.0",
         "doc": { "value": "Onboarding API Profile" },
         "descriptor": [
           { "id": "identifier", "type": "semantic" },
           { "id": "name", "type": "semantic" },
           { "id": "email", "type": "semantic" },
           { "id": "home", "type": "semantic",
             "descriptor": [{ "href": "#startOnboarding" }] },
           { "id": "WIP", "type": "semantic",
             "descriptor": [
               { "href": "#identifier" },
               { "href": "#collectCustomerData" },
               { "href": "#completeOnboarding" }
             ] },
           { "id": "startOnboarding", "type": "unsafe", "rt": "#WIP",
             "descriptor": [{ "href": "#identifier" }] },
           { "id": "collectCustomerData", "type": "safe", "rt": "#customerData",
             "descriptor": [{ "href": "#identifier" }, { "href": "#name" }, { "href": "#email" }] },
           { "id": "completeOnboarding", "type": "unsafe", "rt": "#home",
             "descriptor": [{ "href": "#identifier" }] }
         ]
       }
     }"""
     ```

### T007 – Add JSON round-trip test

- **Purpose**: Prove JSON parse → generate → parse produces structurally equal ASTs.
- **Steps**:
  1. Parse fixture with `parseAlpsJson`
  2. Generate back with `generateAlpsJson`
  3. Parse the generated JSON
  4. Compare ASTs (strip positions if needed)
  5. Verify structural equality

### T008 – Add edge case tests

- **Purpose**: Cover scenarios beyond the onboarding example.
- **Steps**: Add tests for:
  - Shared transitions (href-only references in multiple states)
  - Data descriptors with documentation
  - Extensions at document, state, and transition levels
  - Links at document and state levels
  - Guard extensions on transitions
  - Empty descriptors array
  - Version preservation

### T009 – Fix generator fidelity gaps

- **Purpose**: If round-trip tests reveal gaps, fix them here.
- **File**: `src/Frank.Statecharts/Alps/JsonGenerator.fs`
- **Notes**: Research R-003 found the JSON round-trip is already close to lossless. This subtask may be a no-op if all tests pass. If gaps are found, document what was fixed.

### T010 – Verify build and tests

## Review Guidance

- Verify Amundsen's onboarding example round-trips cleanly
- Verify edge case tests cover shared transitions, data descriptors, extensions, links, guards
- If T009 required changes, verify they don't break existing tests
- Run `dotnet test` — all green

## Activity Log

- 2026-03-18T14:14:54Z – system – lane=planned – Prompt created.
- 2026-03-18T14:34:09Z – claude-opus – shell_pid=71783 – lane=doing – Assigned agent via workflow command
- 2026-03-18T15:11:36Z – claude-opus – shell_pid=71783 – lane=for_review – 10 new JSON round-trip tests, Amundsen onboarding fixture. No generator fixes needed. 879 tests pass.
- 2026-03-18T15:11:46Z – claude-opus-reviewer – shell_pid=74458 – lane=doing – Started review via workflow command
