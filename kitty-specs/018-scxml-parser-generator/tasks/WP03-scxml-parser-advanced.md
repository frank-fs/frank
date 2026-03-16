---
work_package_id: WP03
title: SCXML Parser -- Data Model, History, Invoke, Errors, and Overloads
lane: "done"
dependencies: [WP02]
base_branch: 018-scxml-parser-generator-WP02
base_commit: a586f2fc55d542f912d187f67b5779360c6b8b48
created_at: '2026-03-16T04:31:57.107511+00:00'
subtasks:
- T007
- T008
- T010
- T011
phase: Phase 2 - Implementation
assignee: ''
agent: "claude-opus"
shell_pid: "15369"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-16T01:17:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-008, FR-009, FR-010, FR-011, FR-015]
---

# Work Package Prompt: WP03 -- SCXML Parser -- Data Model, History, Invoke, Errors, and Overloads

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

Depends on WP02 -- run:
```bash
spec-kitty implement WP03 --base WP02
```

---

## Objectives & Success Criteria

- Parser handles `<datamodel>`/`<data>` elements with both `expr` attribute and child text content forms.
- Parser handles `<history>` and `<invoke>` elements with all specified attributes.
- Malformed XML input produces structured `ParseError` results (not unhandled exceptions).
- Structural warnings are emitted for unknown/out-of-scope elements.
- `parseReader` and `parseStream` overloads work correctly.
- `dotnet build src/Frank.Statecharts` succeeds.

## Context & Constraints

- **Spec**: User Stories 3 and 4 (data model, parallel/history/invoke), FR-008 through FR-015
- **Contracts**: `kitty-specs/018-scxml-parser-generator/contracts/parser-api.md` -- behavioral contract sections 4-9
- **Research**: `kitty-specs/018-scxml-parser-generator/research.md` -- R5 (error handling), R6 (data scoping)
- **Data model**: `kitty-specs/018-scxml-parser-generator/data-model.md` -- DataEntry, ScxmlHistory, ScxmlInvoke types
- **Dependency**: Builds on `Scxml/Parser.fs` from WP02

## Subtasks & Detailed Guidance

### Subtask T007 -- Parse `<datamodel>`/`<data>` Elements

- **Purpose**: Extract data model entries from `<datamodel>` containers at both the document level and state level. Each `<data>` element becomes a `DataEntry` record.

- **Steps**:
  1. Create a `parseDataEntries` helper function:
     ```fsharp
     let private parseDataEntries (parent: XElement) : DataEntry list =
         let datamodels =
             parent.Elements()
             |> Seq.filter (fun el -> el.Name.LocalName = "datamodel")
         datamodels
         |> Seq.collect (fun dm ->
             dm.Elements()
             |> Seq.filter (fun el -> el.Name.LocalName = "data"))
         |> Seq.map (fun dataEl ->
             let expr =
                 match attrValue "id" dataEl with  // Note: this should be "expr"
                 | _ ->
                     // expr attribute takes precedence over child text content
                     match attrValue "expr" dataEl with
                     | Some e -> Some e
                     | None ->
                         // Fall back to trimmed child text content
                         let text = dataEl.Value.Trim()
                         if System.String.IsNullOrEmpty(text) then None
                         else Some text
             { Id = (attrValue "id" dataEl) |> Option.defaultValue ""
               Expression = expr
               Position = extractPosition dataEl })
         |> Seq.toList
     ```

  2. Integrate into `parseState` (from WP02): replace `DataEntries = []` with:
     ```fsharp
     DataEntries = parseDataEntries el
     ```

  3. Integrate into root document parsing: replace `DataEntries = []` with:
     ```fsharp
     DataEntries = parseDataEntries root
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Parallel?**: No -- modifies the existing `parseState` function.
- **Notes**:
  - Per parser-api.md contract (section 7): `expr` attribute takes precedence over child text content.
  - `<data id="x"/>` with no `expr` and no child content -> `Expression = None`.
  - `<data>` elements may appear inside `<datamodel>` at both `<scxml>` level and inside `<state>` elements (per R6 decision).
  - The `id` attribute on `<data>` is required per W3C spec. If absent, use empty string (or emit a warning -- see T010).

### Subtask T008 -- Parse `<history>` and `<invoke>` Elements

- **Purpose**: Extract `<history>` pseudo-states and `<invoke>` annotations from within state elements.

- **Steps**:
  1. Create `parseHistory` function:
     ```fsharp
     let private parseHistory (el: XElement) : ScxmlHistory =
         let kind =
             match attrValue "type" el with
             | Some "deep" -> Deep
             | _ -> Shallow  // Default per W3C spec

         let defaultTransition =
             el.Elements()
             |> Seq.tryFind (fun child -> child.Name.LocalName = "transition")
             |> Option.map parseTransition

         { Id = (attrValue "id" el) |> Option.defaultValue ""
           Kind = kind
           DefaultTransition = defaultTransition
           Position = extractPosition el }
     ```

  2. Create `parseInvoke` function:
     ```fsharp
     let private parseInvoke (el: XElement) : ScxmlInvoke =
         { InvokeType = attrValue "type" el
           Src = attrValue "src" el
           Id = attrValue "id" el
           Position = extractPosition el }
     ```

  3. Integrate both into `parseState`: replace `HistoryNodes = []` and `InvokeNodes = []` with:
     ```fsharp
     HistoryNodes =
         el.Elements()
         |> Seq.filter (fun child -> child.Name.LocalName = "history")
         |> Seq.map parseHistory
         |> Seq.toList
     InvokeNodes =
         el.Elements()
         |> Seq.filter (fun child -> child.Name.LocalName = "invoke")
         |> Seq.map parseInvoke
         |> Seq.toList
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Parallel?**: No -- modifies `parseState`.
- **Notes**:
  - `<history type="...">` defaults to "shallow" when absent (FR-010).
  - `<history>` may contain a child `<transition>` element specifying the default history target (W3C spec). This is parsed as `DefaultTransition: ScxmlTransition option`.
  - `<invoke>` attributes are all optional -- preserve whatever is present.
  - Both `<history>` and `<invoke>` are non-functional annotations -- they are parsed for structure but do not affect Frank runtime behavior.

### Subtask T010 -- Implement Structured Error Handling

- **Purpose**: Ensure malformed XML produces structured `ParseError` results instead of unhandled exceptions, and emit warnings for structural issues and unknown elements.

- **Steps**:
  1. Wrap the `parseString` function body in a `try/with` block:
     ```fsharp
     let parseString (xml: string) : ScxmlParseResult =
         try
             let xdoc = XDocument.Parse(xml, LoadOptions.SetLineInfo)
             // ... existing parsing logic ...
             { Document = Some doc; Errors = []; Warnings = warnings }
         with
         | :? XmlException as ex ->
             { Document = None
               Errors =
                 [ { Description = ex.Message
                     Position =
                       Some { Line = ex.LineNumber
                              Column = ex.LinePosition } } ]
               Warnings = [] }
     ```

  2. Thread a mutable `ResizeArray<ParseWarning>` (or accumulate in a functional style) through the parsing to collect warnings:
     - Unknown elements inside `<state>` that are not `<state>`, `<parallel>`, `<final>`, `<transition>`, `<datamodel>`, `<history>`, `<invoke>`, `<onentry>`, `<onexit>`: emit warning.
     - Out-of-scope but known elements (`<onentry>`, `<onexit>`, `<script>`, `<send>`, `<raise>`, `<log>`, `<assign>`, `<cancel>`, `<foreach>`, `<param>`, `<content>`, `<donedata>`, `<finalize>`): silently skip (no warning per parser-api.md contract section 9).
     - Invalid `<history type="...">` value (not "shallow" or "deep"): emit warning, default to Shallow.

  3. Validate root element: if the root element is not `<scxml>`, produce a `ParseError`:
     ```fsharp
     if root.Name.LocalName <> "scxml" then
         { Document = None
           Errors = [ { Description = "Root element must be <scxml>"
                        Position = extractPosition root } ]
           Warnings = [] }
     ```

  4. Ensure no bare `with _ ->` handlers (constitution principle VII). Only catch specific exception types (`XmlException`).

- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Parallel?**: No -- modifies the main parsing flow.
- **Notes**:
  - `XmlException` provides `.LineNumber` and `.LinePosition` (1-based) natively.
  - For `IXmlLineInfo` on elements within valid XML, line info is available only when `LoadOptions.SetLineInfo` was used.
  - The parser should continue past structural warnings to provide maximum information (contract section 3).
  - The distinction between "out-of-scope but known" (silently skipped) and "truly unknown" (warning) is important per the parser-api.md contract.

### Subtask T011 -- Implement `parseReader` and `parseStream` Overloads

- **Purpose**: Provide alternative input methods per the parser API contract: `TextReader` and `Stream`.

- **Steps**:
  1. Refactor the core parsing logic out of `parseString` into a private function that takes an `XDocument`:
     ```fsharp
     let private parseDocument (xdoc: XDocument) : ScxmlParseResult =
         // ... all the parsing logic currently in parseString ...
     ```

  2. Rewrite `parseString` to load and delegate:
     ```fsharp
     let parseString (xml: string) : ScxmlParseResult =
         try
             let xdoc = XDocument.Parse(xml, LoadOptions.SetLineInfo)
             parseDocument xdoc
         with
         | :? XmlException as ex ->
             { Document = None
               Errors = [ { Description = ex.Message
                            Position = Some { Line = ex.LineNumber; Column = ex.LinePosition } } ]
               Warnings = [] }
     ```

  3. Implement `parseReader`:
     ```fsharp
     let parseReader (reader: System.IO.TextReader) : ScxmlParseResult =
         try
             let xdoc = XDocument.Load(reader, LoadOptions.SetLineInfo)
             parseDocument xdoc
         with
         | :? XmlException as ex ->
             { Document = None
               Errors = [ { Description = ex.Message
                            Position = Some { Line = ex.LineNumber; Column = ex.LinePosition } } ]
               Warnings = [] }
     ```

  4. Implement `parseStream`:
     ```fsharp
     let parseStream (stream: System.IO.Stream) : ScxmlParseResult =
         try
             let xdoc = XDocument.Load(stream, LoadOptions.SetLineInfo)
             parseDocument xdoc
         with
         | :? XmlException as ex ->
             { Document = None
               Errors = [ { Description = ex.Message
                            Position = Some { Line = ex.LineNumber; Column = ex.LinePosition } } ]
               Warnings = [] }
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Parallel?**: No -- refactors the function created in T003/T004.
- **Notes**:
  - Callers own the `TextReader`/`Stream` lifetime (per parser-api.md contract). The parser does not close them.
  - `XDocument.Load` overloads accept `TextReader` and `Stream` directly.
  - The try/with pattern is identical across all three overloads -- consider extracting a helper to reduce duplication:
    ```fsharp
    let private tryParseWith (loadFn: unit -> XDocument) : ScxmlParseResult =
        try
            loadFn () |> parseDocument
        with
        | :? XmlException as ex -> ...
    ```

## Validation

After completing all subtasks:

```bash
dotnet build src/Frank.Statecharts
```

Verify that:
1. A document with `<datamodel>` at both top level and state level produces `DataEntry` lists in the correct locations.
2. A document with `<history>` and `<invoke>` produces structured nodes.
3. Malformed XML (e.g., unclosed tags) returns `Document = None` with a `ParseError`.
4. A document with unknown child elements inside `<state>` produces warnings.

## Risks & Mitigations

- **Risk**: `<data>` child content mixed with child elements (e.g., `<data id="x"><child/></data>`).
  - **Mitigation**: Use `el.Value` which returns concatenated text of all descendant text nodes. If child elements exist alongside text, `Value` still works but may produce unexpected content. This is an edge case that the spec does not explicitly address -- treat it as best-effort.

- **Risk**: Error handling catches too broadly, masking bugs.
  - **Mitigation**: Only catch `XmlException`. Let all other exceptions propagate. No bare `with _ ->`.

- **Risk**: Warning accumulation makes the code harder to follow.
  - **Mitigation**: Use a simple `ResizeArray<ParseWarning>` threaded through parsing, or build up a list functionally and flatten at the end.

## Review Guidance

- Verify `<data>` parsing handles both `expr` attribute and child text content, with `expr` taking precedence.
- Verify `<history>` defaults to `Shallow` when `type` attribute is absent.
- Verify `<invoke>` preserves all optional attributes.
- Verify malformed XML produces structured `ParseError` with line/column from `XmlException`.
- Verify only `XmlException` is caught -- no bare `with _ ->`.
- Verify `parseReader` and `parseStream` share the same core logic as `parseString`.
- Verify out-of-scope elements (`<onentry>`, etc.) are silently skipped.
- Verify unknown elements produce warnings.
- Verify `dotnet build src/Frank.Statecharts` succeeds.

## Activity Log

- 2026-03-16T01:17:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:31:57Z – claude-opus – shell_pid=15369 – lane=doing – Assigned agent via workflow command
- 2026-03-16T14:33:09Z – claude-opus – shell_pid=15369 – lane=done – Moved to done
