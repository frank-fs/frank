---
work_package_id: WP04
title: ALPS XML Generator + Round-Trip Tests
lane: "done"
dependencies:
- WP01
base_branch: 029-alps-native-annotations-WP01
base_commit: 666360c2268bcc73f70027a51d33121deb4f4827
created_at: '2026-03-18T14:34:11.080134+00:00'
subtasks: [T017, T018, T019, T020, T021]
phase: Phase 2 - Implementation
assignee: ''
agent: "claude-opus-reviewer"
shell_pid: "74458"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-18T14:14:54Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-007, FR-009, FR-010, FR-011]
---

# Work Package Prompt: WP04 – ALPS XML Generator + Round-Trip Tests

## Review Feedback
*[Empty initially.]*

---

## Implementation Command
```bash
spec-kitty implement WP04 --base WP03
```

## Objectives & Success Criteria

- New ALPS XML generator produces well-formed ALPS XML from `StatechartDocument`
- XML round-trip (parse XML → generate XML → parse XML) produces structurally equal ASTs (SC-004)
- FR-007, FR-009 satisfied

## Context & Constraints

- **Spec**: FR-007 (XML generator), FR-009 (round-trip fidelity)
- **Research**: R-002 (ALPS XML structure)
- **Dependency**: `Alps/XmlParser.fs` from WP03 for round-trip testing
- Generator follows the same pattern as `JsonGenerator.fs` but outputs `XDocument` instead of JSON

### Target ALPS XML Output

```xml
<?xml version="1.0" encoding="utf-8"?>
<alps version="1.0">
  <doc>Onboarding API Profile</doc>
  <descriptor id="identifier" type="semantic"/>
  <descriptor id="home" type="semantic">
    <descriptor href="#startOnboarding"/>
  </descriptor>
  <descriptor id="startOnboarding" type="unsafe" rt="#WIP">
    <descriptor href="#identifier"/>
    <ext id="guard" value="isAdmin"/>
  </descriptor>
  <link rel="self" href="http://example.com/alps/profile"/>
</alps>
```

## Subtasks & Detailed Guidance

### T017 – Create XmlGenerator.fs

- **Purpose**: Generate ALPS XML from `StatechartDocument`, mirroring `JsonGenerator.fs` logic.
- **File**: `src/Frank.Statecharts/Alps/XmlGenerator.fs` (NEW)
- **Steps**:
  1. Create module:
     ```fsharp
     module internal Frank.Statecharts.Alps.XmlGenerator

     open System.Xml.Linq
     open Frank.Statecharts.Ast
     ```
  2. Follow the same structure as `JsonGenerator.fs`:
     - Extract states, transitions, shared transitions
     - Extract document-level annotations (version, doc, links, extensions, data descriptors)
     - Build `XElement` tree:
       - `<alps version="1.0">` root
       - `<doc>` from `AlpsDocumentation`
       - `<descriptor>` for data descriptors, states, shared transitions
       - `<link>` from `AlpsLink`
       - `<ext>` from `AlpsExtension`
     - For each state descriptor: `<descriptor id="..." type="semantic">` with child transitions, doc, extensions, links
     - For each transition: `<descriptor id="..." type="..." rt="...">` with parameters as href-only children, guard as ext
  3. Public API:
     ```fsharp
     let generateAlpsXml (doc: StatechartDocument) : string =
         let xdoc = buildXDocument doc
         use sw = new System.IO.StringWriter()
         xdoc.Save(sw)
         sw.ToString()

     let generateAlpsXmlTo (writer: System.IO.TextWriter) (doc: StatechartDocument) : unit =
         let xdoc = buildXDocument doc
         xdoc.Save(writer)
     ```
  4. Reuse annotation extraction helpers from `JsonGenerator.fs` — or better, extract them to a shared helper if there's significant overlap. Expert consensus: some duplication is acceptable here since the output format (XElement vs Utf8JsonWriter) differs enough that shared helpers would be awkward.

### T018 – Update .fsproj

- **File**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Steps**: Add `<Compile Include="Alps/XmlGenerator.fs" />` after `Alps/XmlParser.fs`.

### T019 – Add XML generator tests

- **File**: `test/Frank.Statecharts.Tests/Alps/XmlGeneratorTests.fs` (NEW) or extend existing
- **Steps**:
  - Test: generate from AST with states and transitions → output contains `<descriptor>` elements with correct attributes
  - Test: generate with documentation → `<doc>` elements at all levels
  - Test: generate with extensions and links → `<ext>` and `<link>` elements
  - Test: generate with shared transitions → href-only `<descriptor>` references
  - Test: generate with guard → `<ext id="guard" value="..."/>`

### T020 – Add XML round-trip test

- **Purpose**: Prove XML parse → generate → parse produces structurally equal ASTs.
- **Steps**:
  1. Use the ALPS XML equivalent of the onboarding example:
     ```fsharp
     testCase "XML round-trip"
     <| fun _ ->
         let xml = """<alps version="1.0">
           <doc>Onboarding API Profile</doc>
           <descriptor id="identifier" type="semantic"/>
           <descriptor id="home" type="semantic">
             <descriptor href="#startOnboarding"/>
           </descriptor>
           <descriptor id="startOnboarding" type="unsafe" rt="#WIP">
             <descriptor href="#identifier"/>
           </descriptor>
         </alps>"""
         let result1 = parseAlpsXml xml
         Expect.isEmpty result1.Errors "parse succeeds"
         let generated = generateAlpsXml result1.Document
         let result2 = parseAlpsXml generated
         Expect.isEmpty result2.Errors "re-parse succeeds"
         Expect.equal result1.Document result2.Document "ASTs equal"
     ```

### T021 – Verify build and tests

## Risks & Mitigations

- **XML formatting**: `XDocument.Save()` adds XML declaration and indentation. This is fine — comparison is at AST level.
- **Namespace on output**: ALPS XML typically has no namespace (unlike SCXML). Don't add one unless the original had one.
- **Annotation extraction duplication**: Some overlap with `JsonGenerator.fs` helpers is acceptable since output format differs.

## Review Guidance

- Verify generated XML is well-formed and contains all expected elements
- Verify XML round-trip produces structurally equal ASTs
- Verify `.fsproj` has correct compilation order
- Run `dotnet test` — all green

## Activity Log

- 2026-03-18T14:14:54Z – system – lane=planned – Prompt created.
- 2026-03-18T14:34:11Z – claude-opus – shell_pid=71999 – lane=doing – Assigned agent via workflow command
- 2026-03-18T15:11:37Z – claude-opus – shell_pid=71999 – lane=for_review – XML generator + 30 tests. 897 tests pass.
- 2026-03-18T15:11:49Z – claude-opus-reviewer – shell_pid=74458 – lane=doing – Started review via workflow command
- 2026-03-18T15:11:49Z – claude-opus-reviewer – shell_pid=74458 – lane=done – Review passed: XML generator mirrors JsonGenerator pattern. 30+ tests. 897 pass.
