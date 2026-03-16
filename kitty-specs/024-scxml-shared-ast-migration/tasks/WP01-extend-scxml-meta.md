---
work_package_id: "WP01"
subtasks:
  - "T001"
  - "T002"
  - "T003"
  - "T004"
  - "T005"
  - "T006"
  - "T007"
  - "T008"
title: "Extend Shared AST ScxmlMeta Cases"
phase: "Phase 0 - Foundation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: []
requirement_refs: [FR-028]
history:
  - timestamp: "2026-03-16T19:26:17Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP01 -- Extend Shared AST ScxmlMeta Cases

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

No dependencies -- this is the starting package:
```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

- Extend the `ScxmlMeta` discriminated union in `src/Frank.Statecharts/Ast/Types.fs` with 5 new cases and extend 2 existing cases with additional fields.
- All existing code that pattern-matches on `ScxmlMeta` must be updated to handle the new cases and extended field signatures.
- `dotnet build` succeeds across net8.0, net9.0, and net10.0 with zero errors.
- No behavioral changes to existing functionality -- this is purely additive type work.

## Context & Constraints

- **Spec**: `kitty-specs/024-scxml-shared-ast-migration/spec.md` (FR-028)
- **Plan**: `kitty-specs/024-scxml-shared-ast-migration/plan.md` (D1: ScxmlMeta Extension Strategy)
- **Tasks**: `kitty-specs/024-scxml-shared-ast-migration/tasks.md` (WP01)
- **Pattern**: Follow the existing `WsdMeta` and `AlpsMeta` patterns in `Ast/Types.fs` -- each case uses named fields.
- **Constraint**: All changes are within the `Frank.Statecharts` project. No public API changes (the project is `internal`).

---

## Subtasks & Detailed Guidance

### Subtask T001 -- Extend `ScxmlInvoke` with `id` field

- **Purpose**: SCXML `<invoke>` elements have an `id` attribute that must survive round-trip. The current `ScxmlInvoke` case carries `invokeType: string * src: string option` but not `id`.
- **Steps**:
  1. Open `src/Frank.Statecharts/Ast/Types.fs`
  2. Find the `ScxmlMeta` DU, locate the `ScxmlInvoke` case
  3. Change from:
     ```fsharp
     | ScxmlInvoke of invokeType: string * src: string option
     ```
     To:
     ```fsharp
     | ScxmlInvoke of invokeType: string * src: string option * id: string option
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`
- **Notes**: This is a breaking change for all existing call sites that construct or pattern-match `ScxmlInvoke`. These are fixed in T008.

### Subtask T002 -- Extend `ScxmlHistory` with `defaultTarget` field

- **Purpose**: SCXML `<history>` elements can contain a `<transition>` child for the default target. This must be preserved for round-trip fidelity. Storing it in the annotation avoids creating a separate `TransitionElement` for history defaults.
- **Steps**:
  1. In `src/Frank.Statecharts/Ast/Types.fs`, locate the `ScxmlHistory` case
  2. Change from:
     ```fsharp
     | ScxmlHistory of id: string * historyKind: HistoryKind
     ```
     To:
     ```fsharp
     | ScxmlHistory of id: string * historyKind: HistoryKind * defaultTarget: string option
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`
- **Notes**: The `defaultTarget` is `None` when the `<history>` element has no child `<transition>`.

### Subtask T003 -- Add `ScxmlTransitionType` case

- **Purpose**: SCXML transitions have a `type` attribute that can be `internal` or `external` (default). This must be preserved as an annotation on `TransitionEdge` for round-trip fidelity.
- **Steps**:
  1. Add to the `ScxmlMeta` DU:
     ```fsharp
     | ScxmlTransitionType of internal: bool
     ```
  2. `true` means internal, `false` means external.
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`
- **Notes**: We use a simple `bool` rather than referencing the `Scxml.Types.ScxmlTransitionType` DU, to avoid coupling the shared AST to format-internal types. The plan (D1) confirms this design.

### Subtask T004 -- Add `ScxmlMultiTarget` case

- **Purpose**: SCXML transitions can have space-separated multi-target `target` attributes (e.g., `target="s1 s2 s3"`). The shared AST `TransitionEdge.Target` only holds one target. The full list must be preserved via annotation.
- **Steps**:
  1. Add to the `ScxmlMeta` DU:
     ```fsharp
     | ScxmlMultiTarget of targets: string list
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`
- **Notes**: When the transition has a single target, no `ScxmlMultiTarget` annotation is needed (the `TransitionEdge.Target` field suffices). The annotation is only added for multi-target transitions (2+ targets).

### Subtask T005 -- Add `ScxmlDatamodelType` case

- **Purpose**: The SCXML root `<scxml>` element can have a `datamodel` attribute (e.g., `datamodel="ecmascript"`). This document-level attribute must be preserved via annotation on `StatechartDocument`.
- **Steps**:
  1. Add to the `ScxmlMeta` DU:
     ```fsharp
     | ScxmlDatamodelType of datamodel: string
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`

### Subtask T006 -- Add `ScxmlBinding` case

- **Purpose**: The SCXML root `<scxml>` element can have a `binding` attribute (e.g., `binding="early"` or `binding="late"`). This must be preserved for round-trip fidelity.
- **Steps**:
  1. Add to the `ScxmlMeta` DU:
     ```fsharp
     | ScxmlBinding of binding: string
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`

### Subtask T007 -- Add `ScxmlInitial` case

- **Purpose**: SCXML compound `<state>` elements can have an `initial` attribute specifying the default child state. This state-level attribute must be preserved via annotation on `StateNode`.
- **Steps**:
  1. Add to the `ScxmlMeta` DU:
     ```fsharp
     | ScxmlInitial of initialId: string
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`

### Subtask T008 -- Fix all existing pattern matches on `ScxmlMeta`

- **Purpose**: After extending `ScxmlInvoke`/`ScxmlHistory` and adding new cases, all existing code that pattern-matches on `ScxmlMeta` will fail to compile. These must be updated.
- **Steps**:
  1. Search for all pattern matches on `ScxmlMeta` cases across the project:
     ```bash
     grep -rn "ScxmlInvoke\|ScxmlHistory\|ScxmlNamespace" src/ test/ --include="*.fs"
     ```
  2. **`src/Frank.Statecharts/Scxml/Mapper.fs`** (lines ~114, ~121, ~220-222, ~287-288):
     - Update `ScxmlInvoke(invokeType, src)` patterns to `ScxmlInvoke(invokeType, src, _id)` or `ScxmlInvoke(invokeType, src, id)` as appropriate
     - Update `ScxmlHistory(id, kind)` patterns to `ScxmlHistory(id, kind, _defaultTarget)` or `ScxmlHistory(id, kind, defaultTarget)`
     - Construction sites: add the new field (e.g., `ScxmlInvoke(invokeType, inv.Src, None)` temporarily -- the Mapper will be deleted in WP05)
  3. **`src/Frank.Statecharts/Validation/`**: Check if any validator pattern-matches on `ScxmlMeta`. If so, add wildcard matches for new cases.
  4. **`test/Frank.Statecharts.Tests/Ast/`**: Check AST test files for `ScxmlMeta` construction. Update field counts.
  5. Run `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` and fix any remaining compilation errors.
- **Files**:
  - `src/Frank.Statecharts/Scxml/Mapper.fs` (temporary fixes, will be deleted in WP05)
  - `src/Frank.Statecharts/Validation/Validator.fs` (if applicable)
  - `test/Frank.Statecharts.Tests/Ast/TypeConstructionTests.fs` (if applicable)
  - `test/Frank.Statecharts.Tests/Ast/PartialPopulationTests.fs` (if applicable)
- **Notes**: These are temporary compatibility fixes. The Mapper.fs fixes will be removed when the file is deleted in WP05. The goal here is to keep the build green after the type changes.

---

## Risks & Mitigations

- **Breaking existing call sites**: Extending `ScxmlInvoke` from 2 to 3 fields and `ScxmlHistory` from 2 to 3 fields breaks all existing construction and pattern-match sites. Mitigation: T008 systematically fixes them all. Use `dotnet build` to find remaining issues.
- **Missing pattern matches**: New `ScxmlMeta` cases added in T003-T007 will cause incomplete pattern match warnings. Mitigation: check for exhaustive matches in Mapper.fs and validation code; add wildcard branches where needed.

---

## Review Guidance

- Verify the `ScxmlMeta` DU in `Ast/Types.fs` has exactly 8 cases after changes (3 original + 5 new, with 2 originals extended).
- Verify field names match the plan (D1): `invokeType`, `src`, `id`, `historyKind`, `defaultTarget`, `internal`, `targets`, `datamodel`, `binding`, `initialId`.
- Verify `dotnet build` passes on all 3 TFMs.
- Verify no incomplete pattern match warnings related to `ScxmlMeta`.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T19:26:17Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP01 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
