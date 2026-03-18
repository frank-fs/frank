---
work_package_id: WP01
title: Foundation — FormatTag, StringDistance, AlpsXml Dispatch
lane: "for_review"
dependencies: []
base_branch: master
base_commit: e99ea7fbaf00ba560afe579fa47c149ad2632024
created_at: '2026-03-18T17:10:41.129880+00:00'
subtasks: [T001, T002, T003, T004, T005, T006]
phase: Phase 0 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "82433"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T17:06:48Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-007, FR-012, FR-013, FR-014, FR-015]
---

# Work Package Prompt: WP01 – Foundation: FormatTag, StringDistance, AlpsXml Dispatch

## Review Feedback
*[Empty initially.]*

---

## Implementation Command
```bash
spec-kitty implement WP01
```

## Objectives & Success Criteria

- `FormatTag.AlpsXml` case added and all pattern matches updated
- Jaro-Winkler string distance implemented (~30 lines, no external deps)
- Pipeline dispatches ALPS XML to `Alps.XmlParser.parseAlpsXml`
- All existing tests pass

## Context & Constraints

- **Spec**: FR-007 (ALPS XML support), FR-012/FR-013 (near-match detection uses string distance)
- **Research**: R-002 (Jaro-Winkler algorithm), R-003 (FormatTag.AlpsXml design)
- **File**: `src/Frank.Statecharts/Validation/Types.fs` — FormatTag DU
- **File**: `src/Frank.Statecharts/Validation/Pipeline.fs` — format dispatch
- **File**: `src/Frank.Statecharts/Validation/Validator.fs` — pattern matches on FormatTag
- **New file**: `src/Frank.Statecharts/Validation/StringDistance.fs`

## Subtasks & Detailed Guidance

### T001 – Add FormatTag.AlpsXml

- **File**: `src/Frank.Statecharts/Validation/Types.fs`
- Add `| AlpsXml` case to the `FormatTag` DU (after `XState`).

### T002 – Create StringDistance.fs

- **File**: `src/Frank.Statecharts/Validation/StringDistance.fs` (NEW)
- Implement Jaro-Winkler string distance:
  ```fsharp
  module internal Frank.Statecharts.Validation.StringDistance

  /// Compute Jaro similarity between two strings (0.0 to 1.0).
  let jaro (s1: string) (s2: string) : float = ...

  /// Compute Jaro-Winkler similarity (0.0 to 1.0).
  /// Applies prefix bonus (up to 4 chars, scaling factor 0.1).
  let jaroWinkler (s1: string) (s2: string) : float = ...
  ```
- Algorithm:
  1. Match window = `max(|s1|, |s2|) / 2 - 1`
  2. Count matching chars (within window) and transpositions
  3. Jaro = `(m/|s1| + m/|s2| + (m-t)/m) / 3` where m=matches, t=transpositions/2
  4. Winkler bonus: `jaro + (prefix * 0.1 * (1 - jaro))` where prefix = common prefix length (max 4)
- Handle edge cases: empty strings, identical strings, single char strings.

### T003 – Wire AlpsXml dispatch

- **File**: `src/Frank.Statecharts/Validation/Pipeline.fs`
- In `parserFor` function (line 14-20), add:
  ```fsharp
  | FormatTag.AlpsXml -> Some Frank.Statecharts.Alps.XmlParser.parseAlpsXml
  ```

### T004 – Update .fsproj

- **File**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- Add `<Compile Include="Validation/StringDistance.fs" />` BEFORE `Validation/Validator.fs` (StringDistance must compile before Validator uses it for near-match rules in WP03).

### T005 – Fix exhaustive pattern matches

- Search all `.fs` files for pattern matches on `FormatTag` that need the new `AlpsXml` case.
- Key locations: `Validator.fs` (CrossFormatRules), `Pipeline.fs` (parserFor, other switches).
- `AlpsXml` should generally behave identically to `Alps` in validation rules (same semantic layer, just different input format).

### T006 – Verify build and tests

## Review Guidance
- Verify Jaro-Winkler produces correct results: `jaroWinkler "startOnboarding" "start"` should be ~0.78
- Verify `FormatTag.AlpsXml` dispatches to XML parser
- Verify all pattern matches are exhaustive (no compiler warnings)
- Run `dotnet test` — all green

## Activity Log
- 2026-03-18T17:06:48Z – system – lane=planned – Prompt created.
- 2026-03-18T17:10:41Z – claude-opus – shell_pid=82433 – lane=doing – Assigned agent via workflow command
- 2026-03-18T17:15:24Z – claude-opus – shell_pid=82433 – lane=for_review – FormatTag.AlpsXml, Jaro-Winkler, AlpsXml dispatch. 948 tests pass.
