---
work_package_id: "WP03"
subtasks:
  - "T016"
  - "T017"
  - "T018"
  - "T019"
  - "T020"
  - "T021"
title: "Migrate Generator to Shared AST"
phase: "Phase 1 - Core Migration"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs: [FR-017, FR-018, FR-019, FR-020, FR-021, FR-022, FR-023, FR-024]
history:
  - timestamp: "2026-03-16T19:26:17Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP03 -- Migrate Generator to Shared AST

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: Update `review_status: acknowledged` when you begin addressing feedback.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

This WP depends on WP01 (can proceed in parallel with WP02):
```bash
spec-kitty implement WP03 --base WP01
```

---

## Objectives & Success Criteria

- Rewrite `src/Frank.Statecharts/Scxml/Generator.fs` to accept `StatechartDocument` instead of `ScxmlDocument`, absorbing the `Mapper.fromStatechartDocument` direction.
- The generator reads shared AST types (`StateNode`, `TransitionEdge`, `Ast.DataEntry`) and extracts SCXML-specific data from `ScxmlAnnotation` entries.
- `generate` and `generateTo` accept `StatechartDocument` and produce valid SCXML XML.
- `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds.

## Context & Constraints

- **Spec**: `kitty-specs/024-scxml-shared-ast-migration/spec.md` (FR-017 through FR-024)
- **Plan**: `kitty-specs/024-scxml-shared-ast-migration/plan.md` (D4: Non-SCXML StateKind Handling)
- **WSD Precedent**: `src/Frank.Statecharts/Wsd/Serializer.fs` directly reads `StatechartDocument` from `Frank.Statecharts.Ast`. Follow this pattern.
- **Mapper Logic**: `src/Frank.Statecharts/Scxml/Mapper.fs` (function `fromStatechartDocument` and helpers) contains the conversion logic to absorb. Read it carefully.
- **Current Generator**: `src/Frank.Statecharts/Scxml/Generator.fs` currently consumes `ScxmlDocument`/`ScxmlState`/`ScxmlTransition` from `Scxml.Types`.

---

## Subtasks & Detailed Guidance

### Subtask T016 -- Change module opens

- **Purpose**: The generator must work with shared AST types.
- **Steps**:
  1. Open `src/Frank.Statecharts/Scxml/Generator.fs`
  2. Replace the opens:
     ```fsharp
     module internal Frank.Statecharts.Scxml.Generator

     open System.Xml.Linq
     open Frank.Statecharts.Ast
     ```
  3. Remove `open Frank.Statecharts.Scxml.Types` -- the generator should not depend on format-specific types after migration.
  4. The generator may still need the SCXML namespace constant. Keep it:
     ```fsharp
     let private scxmlNs = XNamespace.Get("http://www.w3.org/2005/07/scxml")
     ```
- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`

### Subtask T017 -- Rewrite `generateState` for `StateNode`

- **Purpose**: The recursive state generator must accept `StateNode` and a transition lookup map, producing XML `<state>`, `<parallel>`, or `<final>` elements.
- **Steps**:
  1. Change the signature to accept `StateNode` and a transitions-by-source map:
     ```fsharp
     let rec private generateState
         (transitionsBySource: Map<string, TransitionEdge list>)
         (state: StateNode)
         : XElement option =
     ```
     Note: returns `XElement option` because some `StateKind` values have no SCXML equivalent and should be skipped.
  2. Map `StateKind` to SCXML element name:
     ```fsharp
     let elementNameOpt =
         match state.Kind with
         | Final -> Some "final"
         | Parallel -> Some "parallel"
         | Regular | Initial -> Some "state"
         | ShallowHistory | DeepHistory -> None  // handled separately by generateHistory
         | Choice | ForkJoin | Terminate -> None  // no SCXML equivalent, skip
     ```
  3. For `None` cases, return `None` (caller skips).
  4. For `Some elementName`, build the XElement:
     ```fsharp
     let el = XElement(scxmlNs + elementName)
     if state.Identifier <> "" then
         el.SetAttributeValue(XName.Get "id", state.Identifier)
     ```
  5. Extract `ScxmlInitial` annotation for the `initial` attribute:
     ```fsharp
     state.Annotations
     |> List.tryPick (fun a ->
         match a with
         | ScxmlAnnotation(ScxmlInitial(id)) -> Some id
         | _ -> None)
     |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "initial", id))
     ```
  6. Generate data model for the state (skip -- data entries are flattened to document level in the shared AST).
  7. Separate children into history nodes and regular nodes:
     ```fsharp
     let historyChildren, regularChildren =
         state.Children
         |> List.partition (fun c ->
             match c.Kind with
             | ShallowHistory | DeepHistory -> true
             | _ -> false)
     ```
  8. Generate history elements:
     ```fsharp
     for h in historyChildren do
         el.Add(generateHistory h)
     ```
  9. Generate transitions for this state:
     ```fsharp
     let ownTransitions =
         transitionsBySource
         |> Map.tryFind state.Identifier
         |> Option.defaultValue []
     for t in ownTransitions do
         el.Add(generateTransition t)
     ```
  10. Generate invoke elements from `ScxmlAnnotation(ScxmlInvoke(...))`:
      ```fsharp
      state.Annotations
      |> List.iter (fun a ->
          match a with
          | ScxmlAnnotation(ScxmlInvoke(invokeType, src, id)) ->
              let inv = XElement(scxmlNs + "invoke")
              if invokeType <> "" then inv.SetAttributeValue(XName.Get "type", invokeType)
              src |> Option.iter (fun s -> inv.SetAttributeValue(XName.Get "src", s))
              id |> Option.iter (fun i -> inv.SetAttributeValue(XName.Get "id", i))
              el.Add(inv)
          | _ -> ())
      ```
  11. Recursively generate regular child states:
      ```fsharp
      for child in regularChildren do
          match generateState transitionsBySource child with
          | Some childEl -> el.Add(childEl)
          | None -> ()  // skip non-SCXML state kinds
      ```
  12. Return `Some el`.
- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Notes**: The order of child elements matters for valid SCXML: datamodel, history, transitions, invoke, child states. Follow the existing generator's ordering.

### Subtask T018 -- Rewrite `generateTransition` for `TransitionEdge`

- **Purpose**: Generate a `<transition>` XML element from a `TransitionEdge`, extracting SCXML-specific data from annotations.
- **Steps**:
  1. Change the signature:
     ```fsharp
     let private generateTransition (t: TransitionEdge) : XElement =
     ```
  2. Build the element:
     ```fsharp
     let el = XElement(scxmlNs + "transition")
     t.Event |> Option.iter (fun ev -> el.SetAttributeValue(XName.Get "event", ev))
     t.Guard |> Option.iter (fun g -> el.SetAttributeValue(XName.Get "cond", g))
     ```
  3. Handle target -- check for `ScxmlMultiTarget` annotation first:
     ```fsharp
     let targets =
         t.Annotations
         |> List.tryPick (fun a ->
             match a with
             | ScxmlAnnotation(ScxmlMultiTarget(targets)) -> Some targets
             | _ -> None)
         |> Option.defaultWith (fun () ->
             t.Target |> Option.toList)

     match targets with
     | [] -> ()
     | targets -> el.SetAttributeValue(XName.Get "target", System.String.Join(" ", targets))
     ```
  4. Handle transition type -- check for `ScxmlTransitionType` annotation:
     ```fsharp
     t.Annotations
     |> List.tryPick (fun a ->
         match a with
         | ScxmlAnnotation(ScxmlTransitionType(isInternal)) -> Some isInternal
         | _ -> None)
     |> Option.iter (fun isInternal ->
         if isInternal then
             el.SetAttributeValue(XName.Get "type", "internal"))
     ```
  5. Return `el`.
- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Notes**: The default transition type is external (no `type` attribute emitted). Only emit `type="internal"` when the annotation is present and `isInternal = true`.

### Subtask T019 -- Rewrite `generateHistory` for `StateNode`

- **Purpose**: Generate a `<history>` XML element from a `StateNode` that has `Kind = ShallowHistory` or `Kind = DeepHistory`.
- **Steps**:
  1. Change the signature to accept `StateNode`:
     ```fsharp
     let private generateHistory (h: StateNode) : XElement =
     ```
  2. Build the element:
     ```fsharp
     let el = XElement(scxmlNs + "history")
     ```
  3. Extract history metadata from `ScxmlAnnotation(ScxmlHistory(...))`:
     ```fsharp
     let historyMeta =
         h.Annotations
         |> List.tryPick (fun a ->
             match a with
             | ScxmlAnnotation(ScxmlHistory(id, kind, defaultTarget)) -> Some(id, kind, defaultTarget)
             | _ -> None)
     ```
  4. Set attributes:
     ```fsharp
     match historyMeta with
     | Some(id, kind, defaultTarget) ->
         el.SetAttributeValue(XName.Get "id", id)
         let typeStr = match kind with | Deep -> "deep" | Shallow -> "shallow"
         el.SetAttributeValue(XName.Get "type", typeStr)
         defaultTarget |> Option.iter (fun target ->
             let t = XElement(scxmlNs + "transition")
             t.SetAttributeValue(XName.Get "target", target)
             el.Add(t))
     | None ->
         // Fallback: use StateNode fields directly
         if h.Identifier <> "" then
             el.SetAttributeValue(XName.Get "id", h.Identifier)
         let typeStr = match h.Kind with | DeepHistory -> "deep" | _ -> "shallow"
         el.SetAttributeValue(XName.Get "type", typeStr)
     ```
  5. Return `el`.
- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Notes**: The fallback path (no `ScxmlHistory` annotation) should be defensive. In normal round-trip flow, the annotation is always present.

### Subtask T020 -- Rewrite `generateRoot` for `StatechartDocument`

- **Purpose**: Generate the root `<scxml>` XML element from a `StatechartDocument`.
- **Steps**:
  1. Change the signature:
     ```fsharp
     let private generateRoot
         (transitionsBySource: Map<string, TransitionEdge list>)
         (doc: StatechartDocument)
         : XElement =
     ```
  2. Build the root element:
     ```fsharp
     let root = XElement(scxmlNs + "scxml")
     root.SetAttributeValue(XName.Get "version", "1.0")
     doc.InitialStateId |> Option.iter (fun id -> root.SetAttributeValue(XName.Get "initial", id))
     doc.Title |> Option.iter (fun n -> root.SetAttributeValue(XName.Get "name", n))
     ```
  3. Extract document-level SCXML annotations:
     ```fsharp
     doc.Annotations
     |> List.iter (fun a ->
         match a with
         | ScxmlAnnotation(ScxmlDatamodelType(dm)) ->
             root.SetAttributeValue(XName.Get "datamodel", dm)
         | ScxmlAnnotation(ScxmlBinding(b)) ->
             root.SetAttributeValue(XName.Get "binding", b)
         | _ -> ())
     ```
  4. Generate datamodel from `doc.DataEntries`:
     ```fsharp
     match doc.DataEntries with
     | [] -> ()
     | entries ->
         let dm = XElement(scxmlNs + "datamodel")
         for entry in entries do
             let data = XElement(scxmlNs + "data")
             data.SetAttributeValue(XName.Get "id", entry.Name)
             entry.Expression |> Option.iter (fun expr ->
                 data.SetAttributeValue(XName.Get "expr", expr))
             dm.Add(data)
         root.Add(dm)
     ```
  5. Extract top-level `StateNode` entries from `doc.Elements` and generate them:
     ```fsharp
     let stateNodes =
         doc.Elements
         |> List.choose (fun el ->
             match el with
             | StateDecl s -> Some s
             | _ -> None)

     for state in stateNodes do
         match generateState transitionsBySource state with
         | Some el -> root.Add(el)
         | None -> ()
     ```
  6. Return `root`.
- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Notes**: The data entry field is `entry.Name` (shared AST) instead of `entry.Id` (SCXML-specific). This maps to the SCXML `<data id="...">` attribute.

### Subtask T021 -- Rewrite `generate` and `generateTo` signatures

- **Purpose**: Update the public API to accept `StatechartDocument` and wire up the transition lookup.
- **Steps**:
  1. Create transition lookup helper (same pattern as `Mapper.fromStatechartDocument`):
     ```fsharp
     let private buildTransitionMap (doc: StatechartDocument) : Map<string, TransitionEdge list> =
         doc.Elements
         |> List.choose (fun el ->
             match el with
             | TransitionElement t -> Some t
             | _ -> None)
         |> List.groupBy (fun t -> t.Source)
         |> Map.ofList
     ```
  2. Update `buildXDocument`:
     ```fsharp
     let private buildXDocument (doc: StatechartDocument) : XDocument =
         let transitionsBySource = buildTransitionMap doc
         let root = generateRoot transitionsBySource doc
         XDocument(XDeclaration("1.0", "utf-8", null), root :> obj)
     ```
  3. Update `generate`:
     ```fsharp
     let generate (doc: StatechartDocument) : string =
         let xdoc = buildXDocument doc
         use sw = new System.IO.StringWriter()
         xdoc.Save(sw)
         sw.ToString()
     ```
  4. Update `generateTo`:
     ```fsharp
     let generateTo (writer: System.IO.TextWriter) (doc: StatechartDocument) : unit =
         let xdoc = buildXDocument doc
         xdoc.Save(writer)
     ```
- **Files**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Notes**: The function signatures are the only externally visible change. Test files will need updating (WP04) because they construct `ScxmlDocument` values to pass to `generate`.

---

## Risks & Mitigations

- **Missing annotation extraction**: If a `ScxmlAnnotation` is not present on a node, the generator must use sensible defaults. For transition type: default to external (no `type` attribute). For invoke: skip if no annotation. For history: use `StateNode.Kind` and `StateNode.Identifier` as fallback. Mitigation: always use `List.tryPick` with a `None` fallback path.
- **Non-SCXML state kinds**: The generator may receive `StateNode` entries with `Kind = Choice`, `ForkJoin`, or `Terminate`. These have no SCXML equivalent. Mitigation: `generateState` returns `XElement option` and these kinds return `None` (silently skipped, matching plan D4).
- **Element ordering**: SCXML has conventions for child element ordering within a state. The generator should emit: datamodel, history, transitions, invoke, child states. Verify this matches the current generator's output.

---

## Review Guidance

- Verify `generate` and `generateTo` accept `StatechartDocument` (not `ScxmlDocument`).
- Verify the generator no longer references any type from `Scxml.Types` (no `ScxmlDocument`, `ScxmlState`, `ScxmlTransition`, `ScxmlHistory`, `ScxmlInvoke`).
- Verify annotation extraction for all 7 `ScxmlMeta` cases is handled (either used or safely ignored).
- Verify non-SCXML state kinds are silently skipped.
- Verify `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` passes.
- Spot-check: construct a simple `StatechartDocument` and verify `generate` produces valid SCXML (manual test in F# Interactive or a scratch script).

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T19:26:17Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP03 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
