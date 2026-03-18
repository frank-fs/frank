---
work_package_id: WP02
title: Merge Function
lane: "doing"
dependencies: [WP01]
base_branch: 030-cross-format-validation-pipeline-WP01
base_commit: a9b25b95191f60d55fcba7c44d7688ce7674d54b
created_at: '2026-03-18T17:48:43.642625+00:00'
subtasks: [T007, T008, T009, T010, T011, T012, T013]
phase: Phase 1 - Implementation
assignee: ''
agent: "claude-opus-reviewer"
shell_pid: "86274"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T17:06:48Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-011]
---

# Work Package Prompt: WP02 – Merge Function

## Review Feedback
*[Empty initially.]*

---

## Implementation Command
```bash
spec-kitty implement WP02 --base WP01
```

## Objectives & Success Criteria

- `Pipeline.mergeSources` function merges multiple format sources into one unified `StatechartDocument`
- Priority ordering: SCXML (0) > smcat (1) > WSD (2) > ALPS/AlpsXml (3) for structure
- Annotations accumulate from all formats (DU discriminates)
- States matched by identifier, transitions by (source, target, event)
- SC-001, SC-002 satisfied

## Context & Constraints

- **Spec**: FR-001 through FR-006, FR-011
- **Research**: R-001 (merge algorithm — left fold over priority-sorted documents)
- **Data model**: merge algorithm flow, priority table
- **File**: `src/Frank.Statecharts/Validation/Pipeline.fs` — add `mergeSources`
- Alternatively: create `src/Frank.Statecharts/Validation/Merge.fs` if the logic is substantial

## Subtasks & Detailed Guidance

### T007 – Format priority function

```fsharp
let formatPriority (tag: FormatTag) : int =
    match tag with
    | Scxml -> 0
    | Smcat -> 1
    | Wsd -> 2
    | Alps | AlpsXml -> 3
    | XState -> 4
```

### T008 – State matching with annotation accumulation

- Match states by `Identifier` (exact string match)
- For matched states: keep structural fields from higher-priority format, accumulate annotations from both
- For unmatched states: add to merged document as-is
- Non-None fields from enriching doc fill gaps (e.g., if base has `Label = None` and enriching has `Label = Some "X"`, merged gets `Some "X"`)

### T009 – Transition matching with annotation accumulation

- Match transitions by `(Source, Target, Event)` triple (exact match)
- Matched: accumulate annotations from both
- Unmatched: add to merged document
- `Guard`, `Action`, `Parameters` from higher-priority format take precedence

### T010 – mergeSources as left fold

```fsharp
let mergeSources (sources: (FormatTag * string) list) : Result<StatechartDocument, PipelineError list> =
    // 1. Parse all sources
    // 2. Sort by format priority (lowest number = highest priority)
    // 3. Base = first (highest priority) document
    // 4. Fold remaining documents into base
    // 5. Return merged document
```

### T011 – Edge cases

- Single source → return as-is
- No overlapping states → union of all states
- ALPS contributes state identifier not in structural formats → add as `StateNode` with `Kind = Regular`
- Empty sources list → return empty document or error

### T012 – Merge unit tests

Add tests in `test/Frank.Statecharts.Tests/Validation/` (new or extend existing):
- Merge WSD + ALPS → annotations from both
- Merge SCXML + WSD with conflicting Kind → SCXML wins
- Merge single source → identity
- Merge with non-overlapping states → union
- Merge transitions with annotation accumulation

### T013 – Verify build and tests

## Review Guidance
- Verify priority ordering matches spec: SCXML > smcat > WSD > ALPS
- Verify annotations accumulate (not replace)
- Verify structural fields from highest-priority win
- Verify single-source merge is identity
- Run `dotnet test` — all green

## Activity Log
- 2026-03-18T17:06:48Z – system – lane=planned – Prompt created.
- 2026-03-18T17:48:43Z – claude-opus – shell_pid=85141 – lane=doing – Assigned agent via workflow command
- 2026-03-18T17:59:18Z – claude-opus – shell_pid=85141 – lane=for_review – mergeSources implemented with priority ordering, annotation accumulation, 8 test groups. 977 tests pass.
- 2026-03-18T18:00:02Z – claude-opus-reviewer – shell_pid=86274 – lane=doing – Started review via workflow command
