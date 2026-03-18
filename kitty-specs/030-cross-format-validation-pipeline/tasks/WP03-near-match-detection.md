---
work_package_id: WP03
title: Near-Match Detection
lane: "doing"
dependencies: [WP01]
base_branch: 030-cross-format-validation-pipeline-WP01
base_commit: a9b25b95191f60d55fcba7c44d7688ce7674d54b
created_at: '2026-03-18T17:48:45.096751+00:00'
subtasks: [T014, T015, T016, T017, T018, T019]
phase: Phase 1 - Implementation
assignee: ''
agent: ''
shell_pid: "85249"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T17:06:48Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-012, FR-013]
---

# Work Package Prompt: WP03 – Near-Match Detection

## Review Feedback
*[Empty initially.]*

---

## Implementation Command
```bash
spec-kitty implement WP03 --base WP01
```

## Objectives & Success Criteria

- New cross-format validation rule detects near-matches between state/event names
- Uses Jaro-Winkler from `StringDistance.fs` (WP01)
- Reports warnings with format pair, identifiers, similarity score
- FR-012, FR-013 satisfied

## Context & Constraints

- **Spec**: FR-012 (near-match detection), FR-013 (warning details)
- **Research**: R-004 (near-match rule design)
- **File**: `src/Frank.Statecharts/Validation/Validator.fs` — `CrossFormatRules` module (~line 342)
- **Dependency**: `StringDistance.jaroWinkler` from WP01
- Default threshold: 0.8 (configurable)

## Subtasks & Detailed Guidance

### T014 – Add near-match validation rule

- **File**: `src/Frank.Statecharts/Validation/Validator.fs`
- Add a new rule to `CrossFormatRules.rules` list:
  ```fsharp
  let nearMatchThreshold = 0.8

  let nearMatchRule : ValidationRule =
      { Name = "cross-format-near-match"
        RequiredFormats = Set.empty  // applies to any pair of formats
        Check = fun artifacts -> ... }
  ```
- The rule compares each pair of format artifacts for near-matches.

### T015 – State identifier near-match check

- For each pair of artifacts (A, B):
  - Collect state identifiers from A and B
  - For each state in A not exactly found in B:
    - Compare against all states in B using `StringDistance.jaroWinkler`
    - If score > threshold: produce a failure
  - Vice versa (B states not in A)

### T016 – Event name near-match check

- Same logic as T015 but for event names extracted from transitions
- Extract all unique event names from each format's transitions
- Compare unmatched events across formats

### T017 – Near-match warning reporting

- Each near-match produces a `ValidationFailure`:
  ```fsharp
  { Formats = [formatA; formatB]
    EntityType = "state" // or "event"
    Expected = identifierInA
    Actual = identifierInB
    Description = sprintf "Near-match: '%s' in %A ↔ '%s' in %A (similarity: %.2f)" ... }
  ```
- Also produce a `ValidationCheck` with `Status = Fail` and descriptive reason.

### T018 – Near-match unit tests

Add tests:
- "start" vs "startOnboarding" → near-match detected (Jaro-Winkler ~0.78)
- "Idle" vs "idle" → near-match detected (casing, very high similarity)
- "login" vs "shutdown" → no near-match (too different)
- Identical names → no near-match warning (exact match handles these)
- Multiple near-matches across formats → all reported

### T019 – Verify build and tests

## Review Guidance
- Verify threshold is configurable (or at least a named constant)
- Verify near-matches are reported as warnings, not blocking errors
- Verify the rule doesn't fire for exact matches (those are handled by existing rules)
- Verify similarity scores are included in the warning description
- Run `dotnet test` — all green

## Activity Log
- 2026-03-18T17:06:48Z – system – lane=planned – Prompt created.
