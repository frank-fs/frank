---
work_package_id: WP04
title: End-to-End Integration Tests
lane: planned
dependencies:
- WP02
subtasks: [T020, T021, T022, T023, T024, T025]
phase: Phase 2 - Validation
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T17:06:48Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-008, FR-009, FR-010]
---

# Work Package Prompt: WP04 – End-to-End Integration Tests

## Review Feedback
*[Empty initially.]*

---

## Implementation Command
```bash
spec-kitty implement WP04 --base WP02 --base WP03
```

Note: If implement only supports single --base, merge WP02+WP03 first or implement after both are merged.

## Objectives & Success Criteria

- Integration tests parse REAL format text (not hand-constructed ASTs)
- Consistent 4-format input → zero validation failures (SC-003)
- Intentional mismatches → correct failure detection (SC-004)
- Validate-then-merge workflow tested end-to-end (SC-001)
- Near-match detection with real text tested

## Context & Constraints

- **Spec**: FR-008 (real format text), FR-009 (consistent input), FR-010 (inconsistent input)
- **Dependencies**: WP02 (mergeSources), WP03 (near-match rule)
- **File**: `test/Frank.Statecharts.Tests/Validation/` — new or extend existing test files
- Need test fixtures representing the SAME state machine in WSD, smcat, SCXML, and ALPS JSON

## Subtasks & Detailed Guidance

### T020 – Create multi-format test fixtures

- Create inline string fixtures (or a shared golden file module) with the SAME simple state machine in all 4 formats.
- Suggested state machine: 3 states (idle, active, done), 3 transitions (start, complete, reset), 1 guard.
- Fixtures:
  - WSD: `idle->active: start\nactive->done: complete\ndone->idle: reset`
  - smcat: `idle => active: start;\nactive => done: complete;\ndone => idle: reset;`
  - SCXML: `<scxml ...><state id="idle"><transition event="start" target="active"/></state>...</scxml>`
  - ALPS JSON: `{"alps":{"version":"1.0","descriptor":[{"id":"idle","type":"semantic","descriptor":[{"id":"start","type":"unsafe","rt":"#active"}]},...]}}`
- Ensure state names and event names are consistent across all 4 formats.

### T021 – E2E: consistent formats → zero failures

```fsharp
testCase "consistent 4-format input produces zero validation failures"
<| fun _ ->
    let sources = [
        (Wsd, wsdFixture)
        (Smcat, smcatFixture)
        (Scxml, scxmlFixture)
        (Alps, alpsFixture) ]
    let result = Pipeline.validateSources sources
    Expect.isEmpty result.Errors "no pipeline errors"
    Expect.equal result.Report.TotalFailures 0 "zero validation failures"
```

### T022 – E2E: intentional mismatches → correct failures

- Create a variant WSD fixture with "Idle" (capital I) instead of "idle"
- Create a variant ALPS fixture missing one transition event
- Validate and verify:
  - Casing mismatch detected for "Idle" vs "idle"
  - Missing event detected
  - Failure count matches expected

### T023 – E2E: validate then merge → unified document

```fsharp
testCase "validate then merge produces unified document"
<| fun _ ->
    let sources = [
        (Wsd, wsdFixture)
        (Alps, alpsFixture) ]
    let validationResult = Pipeline.validateSources sources
    Expect.equal validationResult.Report.TotalFailures 0 "validation passes"
    let mergeResult = Pipeline.mergeSources sources
    match mergeResult with
    | Ok merged ->
        // Verify WSD topology preserved
        let states = merged.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)
        Expect.isTrue (states |> List.exists (fun s -> s.Identifier = Some "idle")) "has idle"
        // Verify ALPS annotations accumulated
        let hasAlpsAnnotation = states |> List.exists (fun s ->
            s.Annotations |> List.exists (function AlpsAnnotation _ -> true | _ -> false))
        Expect.isTrue hasAlpsAnnotation "has ALPS annotations"
    | Error errs -> failwithf "Merge failed: %A" errs
```

### T024 – E2E: near-match detection with real text

- Create WSD with event "startOnboarding" and smcat with event "start"
- Validate → verify near-match warning in validation report
- Verify the warning includes both names and a similarity score

### T025 – Verify build and tests

## Review Guidance
- Verify fixtures represent the SAME state machine (consistent names, events, transitions)
- Verify consistent test asserts zero failures
- Verify mismatch test asserts correct failure count
- Verify merge produces unified document with annotations from both formats
- Verify near-match warning includes similarity score
- Run `dotnet test` — all green

## Activity Log
- 2026-03-18T17:06:48Z – system – lane=planned – Prompt created.
