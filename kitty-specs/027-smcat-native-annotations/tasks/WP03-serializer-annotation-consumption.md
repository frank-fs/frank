---
work_package_id: WP03
title: Serializer Annotation Consumption
lane: "doing"
dependencies: [WP01]
base_branch: 027-smcat-native-annotations-WP01
base_commit: 1a657d3121f0d1d274c9aa8cae2459565738e29a
created_at: '2026-03-18T06:13:44.060056+00:00'
subtasks:
- T010
- T011
- T012
- T013
phase: Phase 1 - Implementation
assignee: ''
agent: "claude-opus-reviewer"
shell_pid: "39318"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T05:39:36Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-009, FR-010, FR-011]
---

# Work Package Prompt: WP03 – Serializer Annotation Consumption

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
spec-kitty implement WP03 --base WP01
```

---

## Objectives & Success Criteria

- Serializer emits `[type="..."]` for states with `SmcatStateType` annotation having `origin = Explicit`
- Serializer does NOT emit `[type="..."]` for `origin = Inferred`
- Serializer falls back to `StateNode.Kind` when no `SmcatAnnotation` present (cross-format support)
- All existing serializer behavior preserved for states without the new annotations
- FR-009, FR-010, FR-011 from spec are satisfied

## Context & Constraints

- **Spec**: FR-009, FR-010, FR-011
- **Research**: R-002 (annotation density), R-001 (DU design)
- **Data model**: Serializer annotation consumption section — decision tree and StateKind → string mapping
- **File**: `src/Frank.Statecharts/Smcat/Serializer.fs` (193 lines)

### Current Serializer State

The serializer currently:
- Extracts `SmcatColor`, `SmcatStateLabel`, and `SmcatActivity`/`SmcatCustomAttribute` from annotations
- Serializes them as `[color="..." label="..." key="..."]` attribute blocks
- Does NOT extract or emit `SmcatStateType` or `SmcatTransition`
- Has no `StateKind → string` mapping

### Target Serializer State

- New `stateKindToSmcatType` mapping function
- `serializeAttributes` extended to emit `type="..."` based on `SmcatStateType` annotation
- Fallback path for states without `SmcatAnnotation` (uses `StateNode.Kind`)

## Subtasks & Detailed Guidance

### Subtask T010 – Add `StateKind → smcat type string` mapping

- **Purpose**: Central mapping from shared `StateKind` DU to smcat `[type="..."]` attribute values.
- **File**: `src/Frank.Statecharts/Smcat/Serializer.fs`
- **Steps**:
  1. Add a private mapping function in the helpers section (after the quoting helpers, around line 23):
     ```fsharp
     /// Map StateKind to smcat type attribute value.
     let private stateKindToSmcatType (kind: StateKind) : string =
         match kind with
         | Regular -> "regular"
         | Initial -> "initial"
         | Final -> "final"
         | Parallel -> "parallel"
         | ShallowHistory -> "history"
         | DeepHistory -> "deep.history"
         | Choice -> "choice"
         | ForkJoin -> "forkjoin"
         | Terminate -> "terminate"
     ```
  2. Ensure this is an exhaustive match (no wildcard) — the compiler will enforce all `StateKind` cases are covered (SC-006).
- **Parallel?**: No (other subtasks depend on this).
- **Notes**: The mapping values match what `inferStateType` in `Smcat/Types.fs` accepts as input, ensuring round-trip consistency.

### Subtask T011 – Update `serializeAttributes` to consume `SmcatStateType`

- **Purpose**: Emit `[type="..."]` when `SmcatStateType` annotation has `origin = Explicit`. Skip when `origin = Inferred`.
- **File**: `src/Frank.Statecharts/Smcat/Serializer.fs`
- **Steps**:
  1. Add an extraction helper (alongside `extractColor`, `extractLabel`):
     ```fsharp
     /// Extract SmcatStateType from annotations.
     let private extractStateType (annotations: Annotation list) : (StateKind * SmcatTypeOrigin) option =
         annotations
         |> List.tryPick (function
             | SmcatAnnotation(SmcatStateType(kind, origin)) -> Some(kind, origin)
             | _ -> None)
     ```
  2. Update `serializeAttributes` (line 71) to include type attribute when explicit:
     ```fsharp
     // After existing color/label/custom attribute handling:
     // State type (only when explicitly declared)
     match extractStateType annotations with
     | Some(kind, Explicit) -> parts.Add(sprintf "type=\"%s\"" (stateKindToSmcatType kind))
     | Some(_, Inferred) -> ()  // Inferred types are not emitted
     | None -> ()               // No annotation = Regular/Inferred default
     ```
  3. Place the type attribute AFTER color, label, and custom attributes in the `[...]` block for consistent ordering.
- **Parallel?**: Yes (independent from T012).
- **Notes**: The `serializeAttributes` function is called from `serializeState` — it receives `node.Annotations` which may or may not contain `SmcatStateType`.

### Subtask T012 – Add fallback for states without `SmcatAnnotation`

- **Purpose**: When serializing a `StatechartDocument` from a non-smcat source (e.g., SCXML parser), states won't have `SmcatAnnotation` entries. The serializer should use `StateNode.Kind` to emit appropriate type attributes.
- **File**: `src/Frank.Statecharts/Smcat/Serializer.fs`
- **Steps**:
  1. The fallback applies in `serializeState` (line 118), not in `serializeAttributes`. The logic is:
     - If `extractStateType node.Annotations` returns `Some(_, _)`: handled by T011
     - If it returns `None` AND `node.Kind <> Regular`: emit `[type="..."]` via the attribute system
  2. Implementation approach: modify `serializeState` to check for the fallback case and inject a type attribute into the annotation-based rendering. One clean approach:
     ```fsharp
     // In serializeState, before calling serializeAttributes:
     let effectiveAnnotations =
         match extractStateType node.Annotations with
         | Some _ -> node.Annotations  // Already has SmcatStateType
         | None when node.Kind <> Regular ->
             // Fallback: synthesize an explicit type annotation for serialization
             SmcatAnnotation(SmcatStateType(node.Kind, Explicit)) :: node.Annotations
         | None -> node.Annotations    // Regular, no annotation needed
     ```
     Then pass `effectiveAnnotations` to `serializeAttributes` instead of `node.Annotations`.
  3. This ensures the same code path handles both annotated and non-annotated states.
- **Parallel?**: Yes (independent from T011).
- **Notes**: This fallback is important for cross-format serialization scenarios where a `StatechartDocument` from SCXML or WSD is serialized to smcat.

### Subtask T013 – Verify build and tests

- **Purpose**: Full build and test validation.
- **Steps**:
  1. `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`
  2. `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
  3. Existing serializer tests should still pass — they don't construct states with `SmcatStateType` annotations, so the new code paths aren't triggered. The fallback for `Kind = Regular` + no annotation produces the same output as before.

## Risks & Mitigations

- **Attribute ordering in tests**: If any test asserts exact attribute string order (e.g., `[color="red"]`), adding `type` would change the output. The `type` attribute is added AFTER other attributes, so existing tests with `color`/`label` only should be unaffected.
- **Serializer state parameter**: `serializeState` receives `node: StateNode` and `siblingTransitions`. The `effectiveAnnotations` approach in T012 is local to `serializeState` and doesn't change the function signature.

## Review Guidance

- Verify `stateKindToSmcatType` is exhaustive (no wildcard case)
- Verify Explicit origin emits `[type="..."]`
- Verify Inferred origin does NOT emit type
- Verify no-annotation + `Kind <> Regular` emits type (fallback)
- Verify no-annotation + `Kind = Regular` emits nothing (default)
- Verify attribute ordering: color, label, custom, type (type last)
- Run `dotnet test` — all green

## Activity Log

- 2026-03-18T05:39:36Z – system – lane=planned – Prompt created.
- 2026-03-18T06:13:44Z – claude-opus – shell_pid=37787 – lane=doing – Assigned agent via workflow command
- 2026-03-18T06:17:23Z – claude-opus – shell_pid=37787 – lane=for_review – All 4 subtasks complete. Serializer emits type attributes for Explicit, skips Inferred, falls back to Kind for cross-format. 827 tests pass.
- 2026-03-18T06:20:28Z – claude-opus-reviewer – shell_pid=39318 – lane=doing – Started review via workflow command
