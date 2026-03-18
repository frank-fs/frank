---
work_package_id: WP01
title: AST Type Definitions and Rename
lane: "for_review"
dependencies: []
base_branch: master
base_commit: a002e99e6b4dd059e6b305ae20db9b65d2c7624d
created_at: '2026-03-18T05:50:35.769769+00:00'
subtasks:
- T001
- T002
- T003
- T004
phase: Phase 0 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "34417"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T05:39:36Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-016, FR-017]
---

# Work Package Prompt: WP01 – AST Type Definitions and Rename

## ⚠️ IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

- Expand the `SmcatMeta` discriminated union with `SmcatStateType` and `SmcatTransition` cases
- Add `SmcatTypeOrigin` and `SmcatTransitionKind` supporting DUs
- Rename `SmcatActivity` → `SmcatCustomAttribute` across all consumers
- `dotnet build` passes across net8.0, net9.0, net10.0
- `dotnet test` passes with zero regressions (behavioral semantics unchanged by rename)

## Context & Constraints

- **Spec**: `kitty-specs/027-smcat-native-annotations/spec.md` — FR-001 through FR-004
- **Research**: `kitty-specs/027-smcat-native-annotations/research.md` — R-001 (DU design), R-004 (impact analysis)
- **Data model**: `kitty-specs/027-smcat-native-annotations/data-model.md` — full DU structure
- **Constitution**: `.kittify/memory/constitution.md` — Principle II (Idiomatic F#): DUs for modeling choices
- **Breaking changes are acceptable** — code is unreleased, all smcat modules are `internal`

### Key Design Decisions (from clarification/research)

- `SmcatTypeOrigin = Explicit | Inferred` — two-case DU, not a bool (self-documenting at match sites)
- `SmcatTransition of SmcatTransitionKind` — case name ≠ payload type name (consistent with `SmcatStateType of StateKind * SmcatTypeOrigin`)
- `SmcatCustomAttribute of key: string * value: string` — renamed from `SmcatActivity` to avoid confusion with `StateActivities`

## Subtasks & Detailed Guidance

### Subtask T001 – Add `SmcatTypeOrigin` and `SmcatTransitionKind` DUs

- **Purpose**: Define the two supporting DUs that `SmcatMeta` cases will wrap.
- **File**: `src/Frank.Statecharts/Ast/Types.fs`
- **Steps**:
  1. Add `SmcatTypeOrigin` DU in the `-- smcat annotation stub --` section (after line 99, before the `SmcatMeta` type):
     ```fsharp
     /// Tracks whether a state's type was declared via [type="..."] attribute
     /// or inferred from naming convention / default.
     type SmcatTypeOrigin =
         | Explicit
         | Inferred
     ```
  2. Add `SmcatTransitionKind` DU immediately after `SmcatTypeOrigin`:
     ```fsharp
     /// Semantic role of a transition in smcat format.
     type SmcatTransitionKind =
         | InitialTransition
         | FinalTransition
         | SelfTransition
         | ExternalTransition
         | InternalTransition
     ```
  3. Place both BEFORE the `SmcatMeta` type definition so they're in scope when `SmcatMeta` references them.
- **Parallel?**: No (must precede T002).

### Subtask T002 – Add `SmcatStateType` and `SmcatTransition` cases to `SmcatMeta`

- **Purpose**: Expand the `SmcatMeta` DU with typed annotation cases for state types and transition kinds.
- **File**: `src/Frank.Statecharts/Ast/Types.fs`
- **Steps**:
  1. Add two new cases to the `SmcatMeta` DU (currently at lines 103-106):
     ```fsharp
     type SmcatMeta =
         | SmcatColor of string
         | SmcatStateLabel of string
         | SmcatActivity of kind: string * body: string   // ← will be renamed in T003
         | SmcatStateType of kind: StateKind * origin: SmcatTypeOrigin
         | SmcatTransition of SmcatTransitionKind
     ```
  2. Update the doc comment on `SmcatMeta` to reflect it's no longer a stub:
     ```fsharp
     /// smcat-specific annotation metadata.
     /// Carries state type origin tracking, transition semantic roles,
     /// visual attributes, and custom key-value pairs.
     ```
- **Parallel?**: No (must follow T001, must precede T003).

### Subtask T003 – Rename `SmcatActivity` → `SmcatCustomAttribute`

- **Purpose**: Eliminate naming confusion between `SmcatActivity` (custom `[key=value]` attributes) and `StateActivities` (`entry/`/`exit/`/`...` activities).
- **Files** (3 total):
  1. **`src/Frank.Statecharts/Ast/Types.fs`** (line 106): Change type definition
     ```fsharp
     // Before:
     | SmcatActivity of kind: string * body: string
     // After:
     | SmcatCustomAttribute of key: string * value: string
     ```
  2. **`src/Frank.Statecharts/Smcat/Parser.fs`** (line 377): Change constructor call
     ```fsharp
     // Before:
     SmcatActivity(kind, attr.Value)
     // After:
     SmcatCustomAttribute(key, attr.Value)
     ```
     Note: The variable name `kind` in the surrounding code may also need renaming to `key` for consistency. Check the surrounding match expression.
  3. **`src/Frank.Statecharts/Smcat/Serializer.fs`** (line 46): Change pattern match
     ```fsharp
     // Before:
     | SmcatAnnotation(SmcatActivity(kind, body)) -> Some (kind, body)
     // After:
     | SmcatAnnotation(SmcatCustomAttribute(key, value)) -> Some (key, value)
     ```
     Also update the function name `extractCustomAttributes` if it references "activity" in comments.
- **Steps**:
  1. Make all 3 changes atomically
  2. Search for any remaining references to `SmcatActivity` in `.fs` files (should find none after these 3 changes)
- **Parallel?**: No (must follow T002).

### Subtask T004 – Verify build and tests

- **Purpose**: Confirm all changes compile and existing tests pass unchanged.
- **Steps**:
  1. Run `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` — must succeed
  2. Run `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` — all tests must pass
  3. If any tests reference `SmcatActivity` in pattern matches, update them (check `test/Frank.Statecharts.Tests/Ast/TypeConstructionTests.fs` — line 318 uses `SmcatColor`, not `SmcatActivity`, so it should be fine)
- **Parallel?**: No (depends on T001-T003).

## Risks & Mitigations

- **Hidden consumers**: Impact analysis in research.md confirms only 3 source files reference `SmcatActivity`. If `dotnet build` reveals others, update them following the same pattern.
- **Test file references**: `TypeConstructionTests.fs:318` uses `SmcatAnnotation(SmcatColor "red")`, not `SmcatActivity`. `ParserTests.fs` uses `SmcatColor` patterns. Neither should break from the rename.

## Review Guidance

- Verify `SmcatTypeOrigin` and `SmcatTransitionKind` are placed BEFORE `SmcatMeta` in file order (F# requires types to be defined before use)
- Verify named fields on `SmcatCustomAttribute` are `key` and `value` (not `kind` and `body`)
- Verify `SmcatStateType` has named fields: `kind: StateKind * origin: SmcatTypeOrigin`
- Verify exhaustive pattern matching compiles without warnings in downstream files
- Run `dotnet build` and `dotnet test` to confirm

## Activity Log

- 2026-03-18T05:39:36Z – system – lane=planned – Prompt created.
- 2026-03-18T05:50:36Z – claude-opus – shell_pid=34417 – lane=doing – Assigned agent via workflow command
- 2026-03-18T06:08:28Z – claude-opus – shell_pid=34417 – lane=for_review – All 4 subtasks complete. 3 files changed, 827 tests pass, zero warnings.
