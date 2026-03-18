---
work_package_id: WP03
title: ALPS XML Parser
lane: "doing"
dependencies: [WP01]
base_branch: 029-alps-native-annotations-WP01
base_commit: 666360c2268bcc73f70027a51d33121deb4f4827
created_at: '2026-03-18T14:34:09.976306+00:00'
subtasks: [T011, T012, T013, T014, T015, T016]
phase: Phase 1 - Implementation
assignee: ''
agent: "claude-opus-reviewer"
shell_pid: "74458"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T14:14:54Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-005, FR-006, FR-008]
---

# Work Package Prompt: WP03 – ALPS XML Parser

## Review Feedback
*[Empty initially.]*

---

## Implementation Command
```bash
spec-kitty implement WP03 --base WP01
```

## Objectives & Success Criteria

- New ALPS XML parser produces `Ast.ParseResult` with `StatechartDocument`
- Uses shared `Classification` module for Pass 2 (same heuristics as JSON parser)
- XML and JSON parsers produce identical ASTs for equivalent input (SC-002)
- FR-005, FR-006, FR-008 satisfied

## Context & Constraints

- **Spec**: FR-005 (XML parser), FR-006 (XML elements), FR-008 (cross-format equivalence)
- **Research**: R-002 (ALPS XML structure), R-004 (.fsproj updates)
- **Data model**: XML element → ParsedDescriptor mapping table
- **Dependency**: `Alps/Classification.fs` from WP01 provides intermediate types and Pass 2

### ALPS XML Structure

```xml
<alps version="1.0">
  <doc format="text">Description</doc>
  <descriptor id="identifier" type="semantic"/>
  <descriptor id="home" type="semantic">
    <descriptor href="#startOnboarding"/>
    <doc format="text">Home state</doc>
  </descriptor>
  <descriptor id="startOnboarding" type="unsafe" rt="#WIP">
    <descriptor href="#identifier"/>
    <ext id="guard" value="isAdmin"/>
  </descriptor>
  <link rel="self" href="http://example.com/alps/profile"/>
</alps>
```

### XML → ParsedDescriptor Mapping

| XML | ParsedDescriptor Field |
|-----|----------------------|
| `<descriptor id="x">` | `Id = Some "x"` |
| `type="safe"` | `Type = Some "safe"` |
| `href="#ref"` | `Href = Some "#ref"` |
| `rt="#target"` | `ReturnType = Some "#target"` |
| `<doc format="text">value</doc>` | `DocFormat = Some "text"`, `DocValue = Some "value"` |
| `<ext id="..." href="..." value="..."/>` | `Extensions` entry |
| `<link rel="..." href="..."/>` | `Links` entry |
| Nested `<descriptor>` | `Children` |

## Subtasks & Detailed Guidance

### T011 – Create XmlParser.fs Pass 1

- **Purpose**: Parse ALPS XML elements into the shared `ParsedDescriptor` intermediate type.
- **File**: `src/Frank.Statecharts/Alps/XmlParser.fs` (NEW)
- **Steps**:
  1. Create module:
     ```fsharp
     module internal Frank.Statecharts.Alps.XmlParser

     open System.Xml.Linq
     open Frank.Statecharts.Ast
     open Frank.Statecharts.Alps.Classification
     ```
  2. Implement XML → `ParsedDescriptor` conversion:
     ```fsharp
     let private attrValue (name: string) (el: XElement) : string option =
         match el.Attribute(XName.Get name) with
         | null -> None
         | attr -> Some attr.Value

     let private parseExtension (el: XElement) : ParsedExtension =
         { Id = (attrValue "id" el) |> Option.defaultValue ""
           Href = attrValue "href" el
           Value = attrValue "value" el }

     let private parseLink (el: XElement) : ParsedLink =
         { Rel = (attrValue "rel" el) |> Option.defaultValue ""
           Href = (attrValue "href" el) |> Option.defaultValue "" }

     let rec private parseDescriptor (el: XElement) : ParsedDescriptor =
         let docFormat, docValue =
             el.Elements()
             |> Seq.tryFind (fun child -> child.Name.LocalName = "doc")
             |> Option.map (fun doc ->
                 (attrValue "format" doc, Some(doc.Value.Trim())))
             |> Option.defaultValue (None, None)

         { Id = attrValue "id" el
           Type = attrValue "type" el
           Href = attrValue "href" el
           ReturnType = attrValue "rt" el
           DocFormat = docFormat
           DocValue = docValue
           Children =
               el.Elements()
               |> Seq.filter (fun child -> child.Name.LocalName = "descriptor")
               |> Seq.map parseDescriptor
               |> Seq.toList
           Extensions =
               el.Elements()
               |> Seq.filter (fun child -> child.Name.LocalName = "ext")
               |> Seq.map parseExtension
               |> Seq.toList
           Links =
               el.Elements()
               |> Seq.filter (fun child -> child.Name.LocalName = "link")
               |> Seq.map parseLink
               |> Seq.toList }
     ```

### T012 – Wire Pass 2 classification

- **Purpose**: Use the shared classification module to produce `StatechartDocument` from parsed descriptors.
- **Steps**:
  1. In the public API function, extract root-level data, then call `classifyDescriptors`:
     ```fsharp
     let parseAlpsXml (xml: string) : ParseResult =
         try
             let xdoc = XDocument.Parse(xml)
             let root = xdoc.Root
             if root = null || root.Name.LocalName <> "alps" then
                 // error handling
             else
                 let version = attrValue "version" root
                 let rootDocFormat, rootDocValue = // parse <doc> under <alps>
                 let rootLinks = // parse <link> under <alps>
                 let rootExtensions = // parse <ext> under <alps>
                 let descriptors = // parse <descriptor> under <alps>

                 let doc = classifyDescriptors descriptors version rootDocFormat rootDocValue rootLinks rootExtensions
                 { Document = doc; Errors = []; Warnings = [] }
         with :? System.Xml.XmlException as ex ->
             // error handling
     ```
  2. Also add `parseAlpsXmlReader` and `parseAlpsXmlStream` entry points for consistency with SCXML parser pattern.

### T013 – Update .fsproj

- **File**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Steps**: Add `<Compile Include="Alps/XmlParser.fs" />` after `Alps/Classification.fs` (and before or after `Alps/JsonParser.fs` — order doesn't matter between sibling parsers).

### T014 – Add XML parser tests

- **File**: `test/Frank.Statecharts.Tests/Alps/XmlParserTests.fs` (NEW) or extend existing
- **Steps**: Add tests mirroring JSON parser test patterns but with XML input:
  - Parse basic ALPS XML with states and transitions
  - Parse descriptors with documentation, extensions, links
  - Parse nested descriptors
  - Parse shared transitions (href-only references)
  - Error handling for malformed XML

### T015 – Cross-format equivalence test

- **Purpose**: Prove JSON and XML parsers produce identical ASTs for the same ALPS profile.
- **Steps**:
  1. Create equivalent JSON and XML representations of the same profile.
  2. Parse both.
  3. Compare ASTs — must be structurally equal.
  ```fsharp
  testCase "JSON and XML parsers produce identical ASTs"
  <| fun _ ->
      let jsonResult = parseAlpsJson jsonFixture
      let xmlResult = parseAlpsXml xmlFixture
      Expect.equal jsonResult.Document xmlResult.Document "cross-format equivalence"
  ```

### T016 – Verify build and tests

## Risks & Mitigations

- **Namespace handling**: ALPS XML may or may not have a namespace. Use `LocalName` comparisons (like SCXML parser does).
- **Text content in `<doc>`**: XML preserves whitespace differently than JSON. Trim text content for consistency.

## Review Guidance

- Verify XML parser uses `Classification.classifyDescriptors` (not duplicated logic)
- Verify cross-format equivalence test passes
- Verify `.fsproj` has correct compilation order
- Run `dotnet test` — all green

## Activity Log

- 2026-03-18T14:14:54Z – system – lane=planned – Prompt created.
- 2026-03-18T14:34:10Z – claude-opus – shell_pid=71891 – lane=doing – Assigned agent via workflow command
- 2026-03-18T14:41:41Z – claude-opus – shell_pid=71891 – lane=for_review – XML parser with cross-format equivalence. 666 lines added, 906 tests pass.
- 2026-03-18T15:11:47Z – claude-opus-reviewer – shell_pid=74458 – lane=doing – Started review via workflow command
