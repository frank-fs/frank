---
work_package_id: WP02
title: Parser Captures All Content
lane: "for_review"
dependencies: [WP01]
base_branch: 028-scxml-native-annotations-WP01
base_commit: 5fe78bcab9b411214c67617e5a715e3629be9aec
created_at: '2026-03-18T07:31:12.543197+00:00'
subtasks: [T003, T004, T005, T006, T007, T008, T009]
phase: Phase 1 - Implementation
assignee: ''
agent: "claude-opus"
shell_pid: "49969"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T07:24:37Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-005, FR-006, FR-007, FR-008, FR-009, FR-010]
---

# Work Package Prompt: WP02 – Parser Captures All Content

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
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

- Parser stores one `ScxmlOnEntry` annotation per `<onentry>` block with raw XML content
- Parser stores one `ScxmlOnExit` annotation per `<onexit>` block with raw XML content
- Parser populates `StateActivities.Entry`/`Exit` with action descriptions from executable content
- Parser stores `ScxmlInitialElement(targetId)` for `<initial>` child elements
- Parser stores `ScxmlDataSrc(name, src)` for `<data src="...">` attributes
- Parser stores `ScxmlNamespace` with actual namespace string
- SC-001, SC-002 from spec are satisfied
- All existing tests pass + new tests added

## Context & Constraints

- **Spec**: FR-005 through FR-010
- **Research**: R-003 (action description format), R-004 (impact analysis)
- **Data model**: Annotation placement rules — parser sections
- **File**: `src/Frank.Statecharts/Scxml/Parser.fs` (401 lines) — surgical changes only
- **Clarification**: One `ScxmlOnEntry` annotation per `<onentry>` block (not concatenated). Matches `ScxmlInvoke` pattern.

### Current Parser State

- `outOfScopeElements` set (line 38-52) includes `"onentry"` and `"onexit"` — these are silently skipped
- `parseState` function (line 183) parses child states, transitions, history, invoke, but NOT onentry/onexit
- `StateActivities` is always `None` (never populated)
- `<initial>` is in `knownStateChildElements` but not parsed (only `initial` attribute is captured)
- `parseDataEntries` captures `id` and `expr` but not `src` attribute
- Namespace is checked for element matching but not stored

## Subtasks & Detailed Guidance

### Subtask T003 – Parse `<onentry>` blocks

- **Purpose**: Capture executable content from `<onentry>` blocks as both portable activities and raw XML annotations.
- **File**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Steps**:
  1. Remove `"onentry"` from `outOfScopeElements` set (line 38-52).
  2. In `parseState` function (line 183+), after the existing `invokeAnnotations` block (line 226-235), add parsing for `<onentry>`:
     ```fsharp
     // Parse <onentry> elements -> raw XML annotations + activity descriptions
     let onEntryElements =
         el.Elements()
         |> Seq.filter (fun child ->
             child.Name.LocalName = "onentry"
             && (child.Name.Namespace = scxmlNs || child.Name.Namespace = XNamespace.None))
         |> Seq.toList

     let onEntryAnnotations =
         onEntryElements
         |> List.map (fun onEntryEl ->
             ScxmlAnnotation(ScxmlOnEntry(onEntryEl.ToString())))

     let entryActions =
         onEntryElements
         |> List.collect (fun onEntryEl ->
             onEntryEl.Elements()
             |> Seq.map (fun action ->
                 let key = action.Name.LocalName
                 let value =
                     match attrValue "event" action with
                     | Some v -> v
                     | None ->
                         match attrValue "expr" action with
                         | Some v -> v
                         | None ->
                             match attrValue "location" action with
                             | Some v -> v
                             | None -> ""
                 if value = "" then key else sprintf "%s %s" key value)
             |> Seq.toList)
     ```
  3. Build the annotations list with onentry annotations included:
     ```fsharp
     Annotations = invokeAnnotations @ initialAnnotation @ onEntryAnnotations @ onExitAnnotations
     ```
  4. Build `StateActivities` when any content exists:
     ```fsharp
     let activities =
         match entryActions, exitActions with
         | [], [] -> None
         | _ -> Some { Entry = entryActions; Exit = exitActions; Do = [] }
     ```
     Set `Activities = activities` on the `StateNode`.
- **Parallel?**: No (foundational for T004).
- **Notes**: Action description format per research R-003: `"{elementName} {key-attribute-value}"`.

### Subtask T004 – Parse `<onexit>` blocks

- **Purpose**: Same pattern as T003 but for `<onexit>` → `ScxmlOnExit` + `StateActivities.Exit`.
- **File**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Steps**:
  1. Remove `"onexit"` from `outOfScopeElements` set.
  2. Add same parsing pattern as T003 but for `<onexit>`:
     ```fsharp
     let onExitElements =
         el.Elements()
         |> Seq.filter (fun child ->
             child.Name.LocalName = "onexit"
             && (child.Name.Namespace = scxmlNs || child.Name.Namespace = XNamespace.None))
         |> Seq.toList

     let onExitAnnotations =
         onExitElements
         |> List.map (fun onExitEl ->
             ScxmlAnnotation(ScxmlOnExit(onExitEl.ToString())))

     let exitActions =
         onExitElements
         |> List.collect (fun onExitEl ->
             onExitEl.Elements()
             |> Seq.map (fun action -> ... same pattern as entryActions ...)
             |> Seq.toList)
     ```
  3. Integrate into `activities` construction alongside T003.
- **Parallel?**: No (coupled with T003 for `StateActivities` construction).

### Subtask T005 – Parse `<initial>` child elements

- **Purpose**: Capture `<initial>` child elements (distinct from the `initial` attribute).
- **File**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Steps**:
  1. In `parseState`, after history parsing (line 219-224), add:
     ```fsharp
     // Parse <initial> child elements
     let initialElementAnnotations =
         el.Elements()
         |> Seq.filter (fun child ->
             child.Name.LocalName = "initial"
             && (child.Name.Namespace = scxmlNs || child.Name.Namespace = XNamespace.None))
         |> Seq.collect (fun initEl ->
             initEl.Elements()
             |> Seq.filter (fun t -> t.Name.LocalName = "transition")
             |> Seq.map (fun t ->
                 let target = attrValue "target" t |> Option.defaultValue ""
                 ScxmlAnnotation(ScxmlInitialElement(target))))
         |> Seq.toList
     ```
  2. When `ScxmlInitialElement` is present, it takes precedence over `ScxmlInitial` (attribute form). Ensure both can coexist on the annotation list — the generator will check for `ScxmlInitialElement` first.
  3. Include in annotations: `Annotations = invokeAnnotations @ initialAnnotation @ initialElementAnnotations @ onEntryAnnotations @ onExitAnnotations`
- **Parallel?**: Yes (independent from T003/T004).

### Subtask T006 – Capture `<data src="...">` attribute

- **Purpose**: Preserve the `src` attribute on `<data>` elements for round-trip fidelity.
- **File**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Steps**:
  1. In `parseDataEntries` (line 96-119), after extracting `id` and `expr`, also capture `src`:
     ```fsharp
     let src = attrValue "src" dataEl
     ```
  2. Store as a document-level annotation. The cleanest approach: return a tuple `(DataEntry list * Annotation list)` from `parseDataEntries`, where the annotation list contains `ScxmlDataSrc` for each data entry with a `src` attribute.

     Alternatively, accumulate `ScxmlDataSrc` annotations alongside data entries and add them to the document's annotations list in `parseDocument`.
  3. Add to document annotations:
     ```fsharp
     let dataSrcAnnotations =
         allDataEntries
         |> List.choose (fun (entry, srcOpt) ->
             srcOpt |> Option.map (fun src ->
                 ScxmlAnnotation(ScxmlDataSrc(entry.Name, src))))
     ```
- **Parallel?**: Yes (independent from T003-T005).
- **Notes**: `DataEntry` record doesn't have a `src` field, so we use a document-level annotation keyed by data entry name.

### Subtask T007 – Store ScxmlNamespace

- **Purpose**: Track namespace origin for round-trip fidelity.
- **File**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Steps**:
  1. In `parseDocument` (line 281), after root element validation succeeds, extract the namespace:
     ```fsharp
     let namespaceAnnotation =
         [ ScxmlAnnotation(ScxmlNamespace(root.Name.Namespace.NamespaceName)) ]
     ```
  2. Prepend to `docAnnotations`:
     ```fsharp
     let docAnnotations =
         namespaceAnnotation @
         [ match attrValue "datamodel" root with
           | Some dm -> yield ScxmlAnnotation(ScxmlDatamodelType(dm))
           | None -> ()
           ... ]
     ```
- **Parallel?**: Yes (independent from T003-T006).

### Subtask T008 – Update ParserTests.fs

- **Purpose**: Verify the parser correctly captures all new content types.
- **File**: `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs`
- **Steps**:
  1. Add test: `<onentry>` with `<send>` and `<log>` → verify `Activities.Entry` contains `["send done"; "log hello"]` AND `Annotations` contains `ScxmlOnEntry` with raw XML.
  2. Add test: `<onexit>` → same pattern for Exit.
  3. Add test: multiple `<onentry>` blocks → multiple `ScxmlOnEntry` annotations.
  4. Add test: no executable content → `Activities = None`, no OnEntry annotations.
  5. Add test: `<initial><transition target="s1"/></initial>` → `ScxmlInitialElement("s1")` annotation.
  6. Add test: `<data id="x" src="file.json"/>` → `ScxmlDataSrc("x", "file.json")` annotation.
  7. Add test: namespace stored as `ScxmlNamespace`.
- **Parallel?**: No (depends on T003-T007).

### Subtask T009 – Verify build and tests

- **Purpose**: Full build and test validation.
- **Steps**: `dotnet build` and `dotnet test` — all must pass.

## Risks & Mitigations

- **`parseDataEntries` signature change**: If returning `(DataEntry list * Annotation list)` is too invasive, accumulate `ScxmlDataSrc` annotations separately and merge at the `parseDocument` level.
- **Action description extraction**: Some executable content elements may not have a meaningful key attribute. Default to just the element name (e.g., `"foreach"`, `"script"`).

## Review Guidance

- Verify `"onentry"` and `"onexit"` removed from `outOfScopeElements`
- Verify one annotation per block (not concatenated)
- Verify `StateActivities` populated when content exists, `None` when not
- Verify `ScxmlInitialElement` annotation stored from `<initial>` child element
- Verify `ScxmlDataSrc` annotation stored with correct name and src
- Verify `ScxmlNamespace` stored with actual namespace string
- Run `dotnet test` — all green

## Activity Log

- 2026-03-18T07:24:37Z – system – lane=planned – Prompt created.
- 2026-03-18T07:31:12Z – claude-opus – shell_pid=49969 – lane=doing – Assigned agent via workflow command
- 2026-03-18T07:38:28Z – claude-opus – shell_pid=49969 – lane=for_review – All 7 subtasks done. Parser captures onentry/onexit, initial elements, data src, namespace. 12 new tests, 853 total pass.
