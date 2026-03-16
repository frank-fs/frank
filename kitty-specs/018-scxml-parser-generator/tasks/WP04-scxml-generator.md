---
work_package_id: "WP04"
subtasks:
  - "T013"
  - "T014"
  - "T015"
  - "T016"
  - "T017"
  - "T018"
  - "T019"
title: "SCXML Generator"
phase: "Phase 2 - Implementation"
lane: "for_review"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs: ["FR-016", "FR-017", "FR-018", "FR-019", "FR-020", "FR-021", "FR-022", "FR-023"]
history:
  - timestamp: "2026-03-16T01:17:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP04 -- SCXML Generator

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
spec-kitty implement WP04 --base WP01
```

**Note**: WP04 depends only on WP01 (Types). It can be developed in parallel with WP02 and WP03 (Parser).

---

## Objectives & Success Criteria

- Create `src/Frank.Statecharts/Scxml/Generator.fs` with the complete SCXML XML generator.
- Generator produces valid W3C SCXML XML from `ScxmlDocument` types.
- All nine in-scope element types are generated correctly.
- Generated output includes the SCXML namespace declaration and `version="1.0"`.
- Generated XML can be re-parsed by the SCXML parser without errors (SC-002).
- `dotnet build src/Frank.Statecharts` succeeds.

## Context & Constraints

- **Spec**: User Story 2, FR-016 through FR-023
- **Contracts**: `kitty-specs/018-scxml-parser-generator/contracts/generator-api.md` -- public API surface and behavioral contract
- **Data model**: `kitty-specs/018-scxml-parser-generator/data-model.md` -- type definitions (built in WP01)
- **Quickstart**: `kitty-specs/018-scxml-parser-generator/quickstart.md` -- generation example
- **Dependency**: Uses `Scxml/Types.fs` from WP01 only

## Subtasks & Detailed Guidance

### Subtask T013 -- Create Generator.fs with Scaffolding

- **Purpose**: Establish the generator module with namespace constants and the main `generate` function entry point.

- **Steps**:
  1. Create `src/Frank.Statecharts/Scxml/Generator.fs`.
  2. Add module declaration and imports:
     ```fsharp
     module internal Frank.Statecharts.Scxml.Generator

     open System.Xml.Linq
     open Frank.Statecharts.Scxml.Types
     ```
  3. Define the SCXML namespace constant (same as parser):
     ```fsharp
     let private scxmlNs = XNamespace.Get("http://www.w3.org/2005/07/scxml")
     ```
  4. Create the main `generate` function signature:
     ```fsharp
     let generate (doc: ScxmlDocument) : string =
         let root = generateRoot doc  // Defined in subsequent subtasks
         let xdoc = XDocument(XDeclaration("1.0", "utf-8", null), root)
         use sw = new System.IO.StringWriter()
         xdoc.Save(sw)
         sw.ToString()
     ```

  5. Create the root element builder:
     ```fsharp
     let private generateRoot (doc: ScxmlDocument) : XElement =
         let root = XElement(scxmlNs + "scxml")
         root.SetAttributeValue(XName.Get "version", "1.0")
         // Optional attributes
         doc.InitialId |> Option.iter (fun id -> root.SetAttributeValue(XName.Get "initial", id))
         doc.Name |> Option.iter (fun n -> root.SetAttributeValue(XName.Get "name", n))
         doc.DatamodelType |> Option.iter (fun dm -> root.SetAttributeValue(XName.Get "datamodel", dm))
         doc.Binding |> Option.iter (fun b -> root.SetAttributeValue(XName.Get "binding", b))
         // Children added by T014, T016
         root
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs` (new file)
- **Parallel?**: No -- foundation for T014-T018.
- **Notes**:
  - Using `XElement(scxmlNs + "scxml")` automatically adds the `xmlns` namespace declaration on the root element.
  - `XDocument.Save` with default settings produces indented output.
  - The `XDeclaration` produces `<?xml version="1.0" encoding="utf-8"?>` at the top. If the spec prefers no XML declaration, use `root.ToString()` instead of `XDocument.Save`.

### Subtask T014 -- Generate State Elements

- **Purpose**: Emit `<state>`, `<final>`, or `<parallel>` elements from `ScxmlState` records, with recursive child state generation for compound/parallel states.

- **Steps**:
  1. Create `generateState` function:
     ```fsharp
     let rec private generateState (state: ScxmlState) : XElement =
         let elementName =
             match state.Kind with
             | Final -> "final"
             | Parallel -> "parallel"
             | Simple | Compound -> "state"

         let el = XElement(scxmlNs + elementName)

         // id attribute
         state.Id |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "id", id))

         // initial attribute (for compound states)
         state.InitialId |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "initial", id))

         // Add datamodel if present (T016)
         // Add transitions (T015)
         // Add history nodes (T017)
         // Add invoke nodes (T017)

         // Add child states recursively
         for child in state.Children do
             el.Add(generateState child)

         el
     ```

  2. Integrate with root element builder: after creating the root element, add states:
     ```fsharp
     // In generateRoot:
     for state in doc.States do
         root.Add(generateState state)
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Parallel?**: No -- depends on T013.
- **Notes**:
  - Element name derivation: `Final` -> `"final"`, `Parallel` -> `"parallel"`, `Simple`/`Compound` -> `"state"`.
  - Child states are added via recursive call. `XElement.Add` appends in order.
  - The namespace is inherited from the root `scxmlNs` -- child elements created with `scxmlNs + name` will not emit redundant `xmlns` attributes (XLinq handles namespace inheritance).

### Subtask T015 -- Generate Transition Elements

- **Purpose**: Emit `<transition>` elements from `ScxmlTransition` records, with `event`, `cond`, `target`, and `type` attributes.

- **Steps**:
  1. Create `generateTransition` function:
     ```fsharp
     let private generateTransition (t: ScxmlTransition) : XElement =
         let el = XElement(scxmlNs + "transition")

         // event attribute
         t.Event |> Option.iter (fun ev -> el.SetAttributeValue(XName.Get "event", ev))

         // cond attribute (guard)
         t.Guard |> Option.iter (fun g -> el.SetAttributeValue(XName.Get "cond", g))

         // target attribute (joined with spaces for multi-target)
         match t.Targets with
         | [] -> ()  // Targetless transition -- omit target attribute
         | targets ->
             el.SetAttributeValue(XName.Get "target", System.String.Join(" ", targets))

         // type attribute (omit when External, which is the default)
         match t.TransitionType with
         | Internal -> el.SetAttributeValue(XName.Get "type", "internal")
         | External -> ()  // Default, do not emit

         el
     ```

  2. Integrate with `generateState`: add transitions before child states:
     ```fsharp
     // In generateState, after setting attributes:
     for t in state.Transitions do
         el.Add(generateTransition t)
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Parallel?**: No -- integrates with T014.
- **Notes**:
  - Multi-target transitions: join `Targets` list with spaces (e.g., `["s1"; "s2"]` -> `target="s1 s2"`).
  - `type="external"` is the default per W3C spec, so it is omitted from the output to produce canonical/minimal XML (per generator-api.md contract section 9).
  - An empty `Targets` list means a targetless transition -- the `target` attribute is omitted entirely.

### Subtask T016 -- Generate Datamodel/Data Elements

- **Purpose**: Emit `<datamodel>` container with `<data>` children from `DataEntry` lists.

- **Steps**:
  1. Create `generateDatamodel` function:
     ```fsharp
     let private generateDatamodel (entries: DataEntry list) : XElement option =
         match entries with
         | [] -> None
         | entries ->
             let dm = XElement(scxmlNs + "datamodel")
             for entry in entries do
                 let data = XElement(scxmlNs + "data")
                 data.SetAttributeValue(XName.Get "id", entry.Id)
                 entry.Expression |> Option.iter (fun expr ->
                     data.SetAttributeValue(XName.Get "expr", expr))
                 dm.Add(data)
             Some dm
     ```

  2. Integrate with root element builder (top-level datamodel):
     ```fsharp
     // In generateRoot, after setting attributes but before adding states:
     generateDatamodel doc.DataEntries
     |> Option.iter (fun dm -> root.Add(dm))
     ```

  3. Integrate with `generateState` (state-scoped datamodel):
     ```fsharp
     // In generateState, after setting attributes but before transitions:
     generateDatamodel state.DataEntries
     |> Option.iter (fun dm -> el.Add(dm))
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Parallel?**: No -- integrates with T013/T014.
- **Notes**:
  - Data entries with no expression produce `<data id="..."/>` (self-closing, no `expr` attribute).
  - Data entries with an expression produce `<data id="..." expr="..."/>`.
  - The generator always uses the `expr` attribute form (not child text content), per generator-api.md contract section 6. This is the canonical output form.
  - Return `None` when the entries list is empty to avoid emitting an empty `<datamodel/>` element.

### Subtask T017 -- Generate History and Invoke Elements

- **Purpose**: Emit `<history>` and `<invoke>` elements from the corresponding AST node lists.

- **Steps**:
  1. Create `generateHistory` function:
     ```fsharp
     let private generateHistory (h: ScxmlHistory) : XElement =
         let el = XElement(scxmlNs + "history")
         el.SetAttributeValue(XName.Get "id", h.Id)

         // type attribute
         let typeStr =
             match h.Kind with
             | Shallow -> "shallow"
             | Deep -> "deep"
         el.SetAttributeValue(XName.Get "type", typeStr)

         // Default transition (child <transition> element)
         h.DefaultTransition |> Option.iter (fun t ->
             el.Add(generateTransition t))

         el
     ```

  2. Create `generateInvoke` function:
     ```fsharp
     let private generateInvoke (inv: ScxmlInvoke) : XElement =
         let el = XElement(scxmlNs + "invoke")
         inv.InvokeType |> Option.iter (fun t -> el.SetAttributeValue(XName.Get "type", t))
         inv.Src |> Option.iter (fun s -> el.SetAttributeValue(XName.Get "src", s))
         inv.Id |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "id", id))
         el
     ```

  3. Integrate with `generateState`: add history and invoke nodes after datamodel, before transitions:
     ```fsharp
     // In generateState:
     for h in state.HistoryNodes do
         el.Add(generateHistory h)
     for inv in state.InvokeNodes do
         el.Add(generateInvoke inv)
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Parallel?**: No -- integrates with T014.
- **Notes**:
  - `<history>` always emits the `type` attribute (even for "shallow" which is the default) for explicitness in generated output.
  - `<invoke>` attributes are all optional -- only emit when `Some`.
  - `<history>` default transition: if present, emit a child `<transition>` element using the same `generateTransition` function from T015.

### Subtask T018 -- Implement `generateTo` (TextWriter) Overload

- **Purpose**: Provide a `generateTo` function that writes SCXML XML directly to a `TextWriter` instead of returning a string.

- **Steps**:
  1. Refactor `generate` to build the `XDocument` in a shared helper:
     ```fsharp
     let private buildXDocument (doc: ScxmlDocument) : XDocument =
         let root = generateRoot doc
         XDocument(XDeclaration("1.0", "utf-8", null), root)
     ```

  2. Rewrite `generate`:
     ```fsharp
     let generate (doc: ScxmlDocument) : string =
         let xdoc = buildXDocument doc
         use sw = new System.IO.StringWriter()
         xdoc.Save(sw)
         sw.ToString()
     ```

  3. Implement `generateTo`:
     ```fsharp
     let generateTo (writer: System.IO.TextWriter) (doc: ScxmlDocument) : unit =
         let xdoc = buildXDocument doc
         xdoc.Save(writer)
     ```

- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Parallel?**: No -- refactors T013.
- **Notes**:
  - Caller owns the `TextWriter` lifetime (per generator-api.md contract).
  - `XDocument.Save(TextWriter)` writes indented XML by default.
  - The function signature is `TextWriter -> ScxmlDocument -> unit` (TextWriter first for piping convenience), matching the contract in generator-api.md.

### Subtask T019 -- Update `.fsproj` with `Scxml/Generator.fs` Compile Entry

- **Purpose**: Wire the generator file into the F# compilation.

- **Steps**:
  1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`.
  2. Add `<Compile Include="Scxml/Generator.fs" />` immediately after `<Compile Include="Scxml/Parser.fs" />`.

  The compile entries should include:
  ```xml
  <ItemGroup>
    <Compile Include="Scxml/Types.fs" />
    <Compile Include="Scxml/Parser.fs" />
    <Compile Include="Scxml/Generator.fs" />
    <Compile Include="Wsd/Types.fs" />
    ...
  </ItemGroup>
  ```

  **Note**: If WP04 is being implemented in parallel with WP02 (parser), `Scxml/Parser.fs` may not yet exist in the `.fsproj`. In that case, add `Scxml/Generator.fs` after `Scxml/Types.fs` and the Parser entry will be added by WP02. The final order should be Types -> Parser -> Generator.

- **Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj` (edit existing)
- **Parallel?**: No -- must be done after Generator.fs exists.

## Validation

After completing all subtasks:

```bash
dotnet build src/Frank.Statecharts
```

Verify by mentally tracing the generation of this `ScxmlDocument`:

```fsharp
let doc =
    { Name = None
      InitialId = Some "idle"
      DatamodelType = None; Binding = None
      States =
        [ { Id = Some "idle"; Kind = Simple; InitialId = None
            Transitions = [ { Event = Some "start"; Guard = None; Targets = ["active"]
                              TransitionType = External; Position = None } ]
            Children = []; DataEntries = []; HistoryNodes = []; InvokeNodes = []
            Position = None }
          { Id = Some "done"; Kind = Final; InitialId = None
            Transitions = []; Children = []; DataEntries = []
            HistoryNodes = []; InvokeNodes = []; Position = None } ]
      DataEntries = []; Position = None }
```

Expected output:
```xml
<?xml version="1.0" encoding="utf-8"?>
<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle">
    <transition event="start" target="active" />
  </state>
  <final id="done" />
</scxml>
```

## Risks & Mitigations

- **Risk**: Redundant `xmlns` attributes on child elements.
  - **Mitigation**: Using `scxmlNs + elementName` for all elements ensures XLinq handles namespace inheritance correctly -- only the root gets the `xmlns` declaration.

- **Risk**: `XDocument.Save` may produce platform-specific line endings.
  - **Mitigation**: Not a functional issue. Roundtrip tests compare parsed ASTs, not raw strings.

- **Risk**: Attribute ordering may differ from expected canonical form.
  - **Mitigation**: `SetAttributeValue` adds attributes in call order. Call `id` first, then other attributes, matching the contract's canonical order.

## Review Guidance

- Verify root element has `xmlns="http://www.w3.org/2005/07/scxml"` and `version="1.0"`.
- Verify optional `<scxml>` attributes are only emitted when `Some`.
- Verify state element name derivation: `Final` -> `<final>`, `Parallel` -> `<parallel>`, Simple/Compound -> `<state>`.
- Verify transition multi-target: `Targets = ["s1"; "s2"]` produces `target="s1 s2"`.
- Verify `type="external"` is NOT emitted (it is the default).
- Verify `type="internal"` IS emitted.
- Verify empty `DataEntries` does NOT produce an empty `<datamodel/>` element.
- Verify `<history>` always emits `type` attribute.
- Verify `<invoke>` omits absent optional attributes.
- Verify `generateTo` writes to TextWriter without closing it.
- Verify `dotnet build src/Frank.Statecharts` succeeds.

## Activity Log

- 2026-03-16T01:17:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:21:37Z – unknown – lane=for_review – Moved to for_review
