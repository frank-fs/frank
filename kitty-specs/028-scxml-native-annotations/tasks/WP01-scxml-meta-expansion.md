---
work_package_id: WP01
title: ScxmlMeta DU Expansion
lane: "doing"
dependencies: []
base_branch: master
base_commit: 5f0d33f40fd6441ec59fa4dadf8566fdd80b437e
created_at: '2026-03-18T07:29:53.570799+00:00'
subtasks: [T001, T002]
phase: Phase 0 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "49351"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T07:24:37Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-017, FR-018]
---

# Work Package Prompt: WP01 – ScxmlMeta DU Expansion

## ⚠️ IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

- Add 4 new cases to `ScxmlMeta` DU in `Ast/Types.fs`
- `dotnet build` passes across net8.0, net9.0, net10.0
- `dotnet test` passes with zero regressions (purely additive change)

## Context & Constraints

- **Spec**: `kitty-specs/028-scxml-native-annotations/spec.md` — FR-001 through FR-004
- **Data model**: `kitty-specs/028-scxml-native-annotations/data-model.md` — ScxmlMeta expansion table
- **Constitution**: `.kittify/memory/constitution.md` — Principle II (Idiomatic F#): DUs for modeling choices
- All SCXML modules are `internal` — breaking changes acceptable (unreleased)

## Subtasks & Detailed Guidance

### Subtask T001 – Add 4 new ScxmlMeta cases

- **Purpose**: Expand the `ScxmlMeta` DU to carry executable content, initial element, and data source metadata needed for lossless round-trip.
- **File**: `src/Frank.Statecharts/Ast/Types.fs`
- **Steps**:
  1. Locate the `ScxmlMeta` DU (currently lines 89-97, after the `-- SCXML annotation stub --` comment).
  2. Append 4 new cases after `ScxmlInitial`:
     ```fsharp
     type ScxmlMeta =
         | ScxmlInvoke of invokeType: string * src: string option * id: string option
         | ScxmlHistory of id: string * historyKind: HistoryKind * defaultTarget: string option
         | ScxmlNamespace of string
         | ScxmlTransitionType of isInternal: bool
         | ScxmlMultiTarget of targets: string list
         | ScxmlDatamodelType of datamodel: string
         | ScxmlBinding of binding: string
         | ScxmlInitial of initialId: string
         | ScxmlOnEntry of xml: string
         | ScxmlOnExit of xml: string
         | ScxmlInitialElement of targetId: string
         | ScxmlDataSrc of name: string * src: string
     ```
  3. Update the doc comment on `ScxmlMeta` to reflect it's no longer a stub:
     ```fsharp
     /// SCXML-specific annotation metadata.
     /// Carries invoke, history, namespace, transition type, multi-target,
     /// datamodel, binding, initial, executable content, and data source metadata.
     ```
- **Parallel?**: No.

### Subtask T002 – Verify build and tests

- **Purpose**: Confirm all changes compile and existing tests pass unchanged.
- **Steps**:
  1. Run `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` — must succeed across all targets
  2. Run `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` — all tests must pass
- **Parallel?**: No (depends on T001).

## Risks & Mitigations

- Minimal risk — purely additive DU expansion. No existing consumers pattern-match on `ScxmlMeta` exhaustively (they use `List.tryPick`), so new cases don't break anything.

## Review Guidance

- Verify all 4 new cases have correct field names and types matching the data model
- Verify existing 8 cases are unchanged
- Verify doc comment updated
- Run `dotnet build` and `dotnet test` — all green

## Activity Log

- 2026-03-18T07:24:37Z – system – lane=planned – Prompt created.
- 2026-03-18T07:29:53Z – claude-opus – shell_pid=49351 – lane=doing – Assigned agent via workflow command
