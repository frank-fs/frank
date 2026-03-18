---
work_package_id: WP04
title: Round-Trip Fidelity Tests
lane: "doing"
dependencies: [WP02, WP03]
base_branch: 028-scxml-native-annotations-WP04-merge-base
base_commit: 1e2789761cec425c4d5373145755060d31a3cb5b
created_at: '2026-03-18T07:39:15.430803+00:00'
subtasks: [T017, T018, T019, T020, T021]
phase: Phase 2 - Validation
assignee: ''
agent: ''
shell_pid: "51445"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T07:24:37Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-016, FR-017, FR-018]
---

# Work Package Prompt: WP04 – Round-Trip Fidelity Tests

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
spec-kitty implement WP04 --base WP02 --base WP03
```

Note: If the implement command only supports a single `--base`, merge WP02+WP03 first or implement after both are merged.

---

## Objectives & Success Criteria

- Comprehensive SCXML fixture exercises all captured content types
- Round-trip tests prove zero information loss for executable content, initial elements, data src
- SC-003, SC-004, SC-005 from spec are satisfied
- All tests pass

## Context & Constraints

- **Spec**: FR-016 (round-trip fidelity)
- **File**: `test/Frank.Statecharts.Tests/Scxml/RoundTripTests.fs`
- Existing round-trip test uses `stripDocPositions` for comparison — preserve this pattern
- Existing test has one fixture (reference document with states, transitions, data, final)

## Subtasks & Detailed Guidance

### Subtask T017 – Add comprehensive SCXML fixture

- **Purpose**: Create a SCXML document that exercises every feature the parser now captures.
- **File**: `test/Frank.Statecharts.Tests/Scxml/RoundTripTests.fs`
- **Steps**:
  1. Add a new fixture string covering all features:
     ```fsharp
     let comprehensiveFixture = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle" name="test">
       <datamodel>
         <data id="count" expr="0"/>
         <data id="config" src="config.json"/>
       </datamodel>
       <state id="idle">
         <onentry>
           <log expr="entering idle"/>
         </onentry>
         <onexit>
           <send event="leaving"/>
         </onexit>
         <transition event="start" target="active"/>
       </state>
       <state id="active" initial="sub1">
         <initial>
           <transition target="sub1"/>
         </initial>
         <state id="sub1">
           <transition event="next" target="sub2"/>
         </state>
         <state id="sub2">
           <transition event="done" target="finished"/>
         </state>
         <onentry>
           <assign location="count" expr="count + 1"/>
         </onentry>
         <transition event="cancel" target="idle"/>
       </state>
       <state id="finished">
         <history id="h1" type="shallow">
           <transition target="sub1"/>
         </history>
         <invoke type="http" src="http://example.com" id="inv1"/>
         <transition event="restart" target="active"/>
       </state>
       <final id="done"/>
     </scxml>"""
     ```
  2. This fixture covers: states, parallel (could add), final, history, invoke, datamodel with expr and src, onentry with log, onexit with send, onentry with assign, initial child element, transitions with events and targets.

### Subtask T018 – Executable content round-trip test

- **Purpose**: Prove `<onentry>`/`<onexit>` survive parse→generate→parse.
- **Steps**:
  1. Add test:
     ```fsharp
     testCase "executable content survives round-trip"
     <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <state id="s1">
             <onentry><log expr="hello"/><send event="go"/></onentry>
             <onexit><assign location="x" expr="1"/></onexit>
             <transition event="next" target="s2"/>
           </state>
           <state id="s2"/>
         </scxml>"""
         let result1 = parseString xml
         Expect.isEmpty result1.Errors "parse succeeds"
         // Verify activities populated
         let states = result1.Document.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)
         let s1 = states |> List.find (fun s -> s.Identifier = Some "s1")
         Expect.isSome s1.Activities "s1 has activities"
         Expect.isNonEmpty s1.Activities.Value.Entry "s1 has entry actions"
         // Round-trip
         let generated = generate result1.Document
         let result2 = parseString generated
         Expect.isEmpty result2.Errors "re-parse succeeds"
         let doc1 = stripDocPositions result1.Document
         let doc2 = stripDocPositions result2.Document
         Expect.equal doc1 doc2 "ASTs structurally equal"
     ```

### Subtask T019 – Initial element round-trip test

- **Purpose**: Prove `<initial>` child element survives round-trip.
- **Steps**:
  1. Add test with `<initial><transition target="..."/></initial>`:
     ```fsharp
     testCase "initial child element survives round-trip"
     <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="container">
           <state id="container">
             <initial><transition target="child1"/></initial>
             <state id="child1">
               <transition event="go" target="child2"/>
             </state>
             <state id="child2"/>
           </state>
         </scxml>"""
         let result1 = parseString xml
         Expect.isEmpty result1.Errors "parse succeeds"
         let generated = generate result1.Document
         let result2 = parseString generated
         Expect.isEmpty result2.Errors "re-parse succeeds"
         let doc1 = stripDocPositions result1.Document
         let doc2 = stripDocPositions result2.Document
         Expect.equal doc1 doc2 "ASTs structurally equal"
     ```

### Subtask T020 – Data src round-trip test

- **Purpose**: Prove `<data src="...">` survives round-trip.
- **Steps**:
  1. Add test:
     ```fsharp
     testCase "data src attribute survives round-trip"
     <| fun _ ->
         let xml = """<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="s1">
           <datamodel>
             <data id="settings" src="settings.json"/>
             <data id="count" expr="0"/>
           </datamodel>
           <state id="s1"/>
         </scxml>"""
         let result1 = parseString xml
         Expect.isEmpty result1.Errors "parse succeeds"
         let generated = generate result1.Document
         let result2 = parseString generated
         Expect.isEmpty result2.Errors "re-parse succeeds"
         let doc1 = stripDocPositions result1.Document
         let doc2 = stripDocPositions result2.Document
         Expect.equal doc1 doc2 "ASTs structurally equal"
     ```

### Subtask T021 – Verify build and tests

- **Purpose**: Final validation — all tests pass.
- **Steps**: `dotnet build` and `dotnet test` — all green.

## Risks & Mitigations

- **XML formatting**: `XDocument.Save()` may format differently than input (indentation, attribute quoting). Comparison is at AST level via `stripDocPositions`, not string level.
- **Namespace in re-serialized XML**: Raw XML stored in `ScxmlOnEntry` includes the original namespace. When re-parsed, the namespace should match. If the generator changes the namespace context, the raw XML may have a namespace mismatch. Mitigation: test with the W3C namespace (most common case).

## Review Guidance

- Verify comprehensive fixture covers: onentry, onexit, initial element, data src, history, invoke, transitions
- Verify each round-trip test asserts structural equality after `stripDocPositions`
- Verify executable content (activities + annotations) preserved
- Run `dotnet test` — all green

## Activity Log

- 2026-03-18T07:24:37Z – system – lane=planned – Prompt created.
