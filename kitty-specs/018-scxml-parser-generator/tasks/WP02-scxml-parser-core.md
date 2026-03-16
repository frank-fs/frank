---
work_package_id: WP02
title: SCXML Parser -- Core Parsing
lane: "done"
dependencies: [WP01]
base_branch: 018-scxml-parser-generator-WP01
base_commit: 54eb41bfafff5aa8654f91de8815a1f2e2a99c0d
created_at: '2026-03-16T04:17:17.394788+00:00'
subtasks:
- T003
- T004
- T005
- T006
- T009
- T012
phase: Phase 2 - Implementation
assignee: ''
agent: "claude-opus"
shell_pid: "4865"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-16T01:17:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-012, FR-013, FR-014]
---

# Work Package Prompt: WP02 -- SCXML Parser -- Core Parsing

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

Depends on WP01 -- run:
```bash
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

- Create `src/Frank.Statecharts/Scxml/Parser.fs` with the core SCXML parsing logic.
- Parser handles `<scxml>` root element, `<state>`, `<final>`, `<parallel>`, and `<transition>` elements.
- Parser correctly infers initial state when `<scxml initial="...">` is absent.
- Parser correctly derives `ScxmlStateKind` (Simple vs. Compound based on child states).
- `dotnet build src/Frank.Statecharts` succeeds.
- `parseString` on a canonical SCXML document returns a correct `ScxmlDocument` with states, transitions, events, and guards.

## Context & Constraints

- **Spec**: `kitty-specs/018-scxml-parser-generator/spec.md` -- User Story 1, FR-001 through FR-007, FR-012, FR-013, FR-014
- **Contracts**: `kitty-specs/018-scxml-parser-generator/contracts/parser-api.md` -- public API surface and behavioral contract
- **Research**: `kitty-specs/018-scxml-parser-generator/research.md` -- R2 (System.Xml.Linq strategy), R4 (namespace handling)
- **Data model**: `kitty-specs/018-scxml-parser-generator/data-model.md` -- type definitions (built in WP01)
- **Existing pattern**: `src/Frank.Statecharts/Wsd/Parser.fs` -- reference for internal module conventions
- **Dependency**: Uses `Scxml/Types.fs` from WP01

## Subtasks & Detailed Guidance

### Subtask T003 -- Create Parser.fs with Namespace and Loading Helpers

- **Purpose**: Establish the parser module with shared utilities: the SCXML XML namespace constant, `XDocument` loading with line info, and a position extraction helper.

- **Steps**:
  1. Create `src/Frank.Statecharts/Scxml/Parser.fs`.
  2. Add module declaration:
     ```fsharp
     module internal Frank.Statecharts.Scxml.Parser

     open System.Xml
     open System.Xml.Linq
     open Frank.Statecharts.Scxml.Types
     ```
  3. Define the SCXML namespace constant:
     ```fsharp
     let private scxmlNs = XNamespace.Get("http://www.w3.org/2005/07/scxml")
     ```
  4. Create `extractPosition` helper that extracts `SourcePosition option` from any `XObject`:
     ```fsharp
     let private extractPosition (obj: XObject) : SourcePosition option =
         let lineInfo = obj :> IXmlLineInfo
         if lineInfo.HasLineInfo() then
             Some { Line = lineInfo.LineNumber; Column = lineInfo.LinePosition }
         else
             None
     ```
  5. Create a helper to get attribute value as `string option`:
     ```fsharp
     let private attrValue (name: string) (el: XElement) : string option =
         match el.Attribute(XName.Get name) with
         | null -> None
         | attr -> Some attr.Value
     ```
  6. Create the `parseString` function signature (implementation filled in by T004):
     ```fsharp
     let parseString (xml: string) : ScxmlParseResult =
         // Implementation in subsequent subtasks
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs` (new file)
- **Parallel?**: No -- foundation for T004-T006.
- **Notes**: `IXmlLineInfo` is in `System.Xml` namespace. The cast `obj :> IXmlLineInfo` works because `XElement` and `XAttribute` both implement this interface. `LoadOptions.SetLineInfo` must be used when loading the document or line info will not be available.

### Subtask T004 -- Parse `<scxml>` Root Element

- **Purpose**: Implement parsing of the `<scxml>` root element, extracting `initial`, `name`, `datamodel`, and `binding` attributes, and validating the namespace.

- **Steps**:
  1. In `parseString`, load the XML document:
     ```fsharp
     let xdoc = XDocument.Parse(xml, LoadOptions.SetLineInfo)
     ```
     (Error handling for `XmlException` is deferred to WP03/T010 -- for now, let it propagate.)

  2. Get the root element and verify it is `<scxml>` in the SCXML namespace:
     ```fsharp
     let root = xdoc.Root
     // Check: root.Name = scxmlNs + "scxml" OR root.Name.LocalName = "scxml" (for no-namespace case)
     ```

  3. Extract attributes from `<scxml>`:
     - `initial` -> `InitialId: string option`
     - `name` -> `Name: string option`
     - `datamodel` -> `DatamodelType: string option`
     - `binding` -> `Binding: string option`

  4. Build the `ScxmlDocument` record (States and DataEntries populated by T005/WP03):
     ```fsharp
     { Name = attrValue "name" root
       InitialId = ... // See T009 for inference logic
       DatamodelType = attrValue "datamodel" root
       Binding = attrValue "binding" root
       States = ... // Populated by T005
       DataEntries = [] // Populated by WP03/T007
       Position = extractPosition root }
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Parallel?**: No -- depends on T003.
- **Notes**: The `version` attribute on `<scxml>` is not stored in the data model (it is always "1.0" per the W3C spec). Handle documents with no namespace gracefully -- check both qualified (`scxmlNs + "scxml"`) and local name (`"scxml"`) to support both default and no-namespace documents.

### Subtask T005 -- Parse `<state>`, `<final>`, `<parallel>` Elements

- **Purpose**: Implement recursive parsing of state elements into `ScxmlState` records, with correct `ScxmlStateKind` derivation.

- **Steps**:
  1. Create a `parseState` function that takes an `XElement` and returns `ScxmlState`:
     ```fsharp
     let rec private parseState (el: XElement) : ScxmlState =
         let localName = el.Name.LocalName
         let kind =
             match localName with
             | "final" -> Final
             | "parallel" -> Parallel
             | "state" ->
                 // Determine Simple vs Compound based on child state elements
                 let hasChildStates =
                     el.Elements()
                     |> Seq.exists (fun child ->
                         let n = child.Name.LocalName
                         n = "state" || n = "parallel" || n = "final")
                 if hasChildStates then Compound else Simple
             | _ -> Simple // Fallback (should not occur for valid SCXML)

         let children =
             el.Elements()
             |> Seq.filter (fun child ->
                 let n = child.Name.LocalName
                 n = "state" || n = "parallel" || n = "final")
             |> Seq.map parseState
             |> Seq.toList

         let transitions =
             el.Elements(scxmlNs + "transition")
             |> Seq.map parseTransition  // Defined in T006
             |> Seq.toList
             // Also check unqualified "transition" for no-namespace docs

         { Id = attrValue "id" el
           Kind = kind
           InitialId = attrValue "initial" el
           Transitions = transitions
           Children = children
           DataEntries = []       // Filled by WP03/T007
           HistoryNodes = []      // Filled by WP03/T008
           InvokeNodes = []       // Filled by WP03/T008
           Position = extractPosition el }
     ```

  2. In the root parsing (T004), collect top-level state elements:
     ```fsharp
     let states =
         root.Elements()
         |> Seq.filter (fun el ->
             let n = el.Name.LocalName
             n = "state" || n = "parallel" || n = "final")
         |> Seq.map parseState
         |> Seq.toList
     ```

  3. Handle namespace resolution: check both `scxmlNs + elementName` and local name to support prefixed and no-namespace documents.

- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Parallel?**: No -- depends on T003/T004, needed by T006.
- **Notes**:
  - `ScxmlStateKind` derivation rules from `data-model.md`: `<state>` with child state elements -> Compound, without -> Simple. `<parallel>` -> always Parallel. `<final>` -> always Final.
  - Filter child elements to only `<state>`, `<parallel>`, `<final>` for the `Children` list (other child elements like `<transition>`, `<datamodel>` are handled separately).
  - Recursive call to `parseState` for child states enables arbitrarily deep hierarchies.

### Subtask T006 -- Parse `<transition>` Elements

- **Purpose**: Implement parsing of `<transition>` elements into `ScxmlTransition` records, handling event, guard (cond), multi-target splitting, and transition type.

- **Steps**:
  1. Create `parseTransition` function:
     ```fsharp
     let private parseTransition (el: XElement) : ScxmlTransition =
         let targets =
             match attrValue "target" el with
             | Some t ->
                 t.Split(' ', System.StringSplitOptions.RemoveEmptyEntries)
                 |> Array.toList
             | None -> []

         let transType =
             match attrValue "type" el with
             | Some "internal" -> Internal
             | _ -> External  // Default per W3C spec

         { Event = attrValue "event" el
           Guard = attrValue "cond" el
           Targets = targets
           TransitionType = transType
           Position = extractPosition el }
     ```

  2. This function is called from `parseState` (T005) for each `<transition>` child element.

- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Parallel?**: No -- called from T005's `parseState`.
- **Notes**:
  - W3C SCXML section 3.5: `target` can be a space-separated list of state IDs. Split on whitespace.
  - `type` attribute defaults to "external" when absent (per W3C spec).
  - `event` and `cond` are both optional -- a transition may have neither (completion transition), one, or both.
  - An empty `Targets` list represents a targetless/internal transition.

### Subtask T009 -- Implement Initial State Inference and Compound State Detection

- **Purpose**: When `<scxml>` has no `initial` attribute, infer the initial state from the first child state element (per W3C section 3.2). Also ensure compound state detection works correctly in `parseState`.

- **Steps**:
  1. After parsing states in the `parseString` function, apply initial state inference:
     ```fsharp
     let initialId =
         match attrValue "initial" root with
         | Some id -> Some id
         | None ->
             // Per W3C section 3.2: use first child state's id
             states
             |> List.tryHead
             |> Option.bind (fun s -> s.Id)
     ```

  2. Verify compound state detection in `parseState` (T005): a `<state>` element with any child `<state>`, `<parallel>`, or `<final>` elements should have `Kind = Compound`. This should already be handled by the logic in T005, but verify it works for:
     - `<state>` with only `<transition>` children -> Simple
     - `<state>` with `<state>` children -> Compound
     - `<state>` with `<parallel>` children -> Compound
     - `<state>` with `<final>` children -> Compound

- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Parallel?**: No -- integrates with T004/T005.
- **Notes**: Initial state inference is a key requirement (FR-013, spec edge case). The fallback to the first child state's ID is the W3C-mandated behavior. If there are no child states AND no `initial` attribute, `InitialId` should be `None` (empty SCXML document case).

### Subtask T012 -- Update `.fsproj` with `Scxml/Parser.fs` Compile Entry

- **Purpose**: Wire the parser file into the F# compilation.

- **Steps**:
  1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`.
  2. Add `<Compile Include="Scxml/Parser.fs" />` immediately after `<Compile Include="Scxml/Types.fs" />`.

  The compile entries should look like:
  ```xml
  <ItemGroup>
    <Compile Include="Scxml/Types.fs" />
    <Compile Include="Scxml/Parser.fs" />
    <Compile Include="Wsd/Types.fs" />
    ...
  </ItemGroup>
  ```

- **Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj` (edit existing)
- **Parallel?**: No -- must be done after Parser.fs exists.
- **Notes**: Parser.fs must come after Types.fs (it references the types) and before any downstream Scxml files.

## Validation

After completing all subtasks:

```bash
dotnet build src/Frank.Statecharts
```

Must succeed on all three TFMs. Then verify basic functionality by mentally tracing through this SCXML input:

```xml
<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle">
    <transition event="start" target="active"/>
  </state>
  <state id="active">
    <transition event="submit" cond="isValid" target="done"/>
  </state>
  <final id="done"/>
</scxml>
```

Expected result: `ScxmlDocument` with `InitialId = Some "idle"`, 3 states (idle=Simple, active=Simple, done=Final), idle has 1 transition (event="start", target=["active"]), active has 1 transition (event="submit", guard="isValid", target=["done"]).

## Risks & Mitigations

- **Risk**: Namespace handling breaks on documents without the SCXML namespace declaration.
  - **Mitigation**: Check both qualified and local element names in `parseState` and state collection.

- **Risk**: Recursive `parseState` for deeply nested states causes stack overflow on pathological inputs.
  - **Mitigation**: 5+ levels deep is an edge case per spec. F# default stack is sufficient for realistic documents. Not a practical risk for the 100+ state benchmark.

- **Risk**: `XDocument.Parse` throws `XmlException` on malformed input before reaching our code.
  - **Mitigation**: WP03/T010 adds try/catch. For now, the exception propagates naturally.

## Review Guidance

- Verify namespace handling supports both `xmlns="..."` (default) and `xmlns:sc="..."` (prefixed) forms.
- Verify `parseTransition` correctly splits space-separated targets.
- Verify initial state inference falls back to first child state when `initial` attribute is absent.
- Verify compound state detection: `<state>` with child states -> `Compound`, without -> `Simple`.
- Verify `parseState` is `rec` and handles recursive child state parsing.
- Verify `DataEntries`, `HistoryNodes`, `InvokeNodes` are stubbed as empty lists (filled by WP03).
- Verify `dotnet build src/Frank.Statecharts` succeeds.

## Activity Log

- 2026-03-16T01:17:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:17:17Z – claude-opus – shell_pid=4865 – lane=doing – Assigned agent via workflow command
- 2026-03-16T04:29:39Z – claude-opus – shell_pid=4865 – lane=done – Review passed: All 6 subtasks (T003, T004, T005, T006, T009, T012) implemented correctly. Parser handles namespace resolution (both default and prefixed), space-separated transition targets, initial state inference per W3C 3.2, compound state detection, recursive state parsing. DataEntries/HistoryNodes/InvokeNodes correctly stubbed for WP03. Builds clean on all 3 TFMs with 0 warnings. All 246 existing tests pass. Internal parseDocument helper enables clean WP03 overload additions.
- 2026-03-16T14:33:09Z – claude-opus – shell_pid=4865 – lane=done – Moved to done
