---
work_package_id: WP03
title: Generator Emits All Content
lane: "doing"
dependencies: [WP01]
base_branch: 028-scxml-native-annotations-WP01
base_commit: 5fe78bcab9b411214c67617e5a715e3629be9aec
created_at: '2026-03-18T07:31:13.859862+00:00'
subtasks: [T010, T011, T012, T013, T014, T015, T016]
phase: Phase 1 - Implementation
assignee: ''
agent: ''
shell_pid: "50078"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T07:24:37Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-011, FR-012, FR-013, FR-014, FR-015]
---

# Work Package Prompt: WP03 – Generator Emits All Content

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
spec-kitty implement WP03 --base WP01
```

---

## Objectives & Success Criteria

- Generator emits `<onentry>` blocks from `ScxmlOnEntry` annotations
- Generator emits `<onexit>` blocks from `ScxmlOnExit` annotations
- Generator emits `<initial>` child elements from `ScxmlInitialElement` annotations (instead of `initial` attribute)
- Generator emits `src` attribute on `<data>` from `ScxmlDataSrc` annotations
- Generator respects namespace from `ScxmlNamespace` annotation
- All existing tests pass + new tests added

## Context & Constraints

- **Spec**: FR-011 through FR-015
- **Data model**: Generator reconstruction rules
- **File**: `src/Frank.Statecharts/Scxml/Generator.fs` (206 lines)
- Generator already consumes annotations: `ScxmlMultiTarget`, `ScxmlTransitionType`, `ScxmlInvoke`, `ScxmlHistory`, `ScxmlInitial`, `ScxmlDatamodelType`, `ScxmlBinding`

### Current Generator Architecture

- `generateTransition`: extracts `ScxmlMultiTarget`, `ScxmlTransitionType` from annotations → emits `<transition>`
- `generateHistory`: extracts `ScxmlHistory` from annotations → emits `<history>`
- `generateState` (line 76): recursive, emits `<state>`/`<parallel>`/`<final>`, extracts `ScxmlInitial` for attribute, `ScxmlInvoke` for `<invoke>` elements
- `generateRoot` (line 145): emits `<scxml>`, extracts `ScxmlDatamodelType`, `ScxmlBinding`
- `buildXDocument` (line 192): assembles final `XDocument`

## Subtasks & Detailed Guidance

### Subtask T010 – Emit `<onentry>` blocks

- **Purpose**: Reconstruct `<onentry>` elements from `ScxmlOnEntry` raw XML annotations.
- **File**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Steps**:
  1. In `generateState` (line 76), after the existing `ScxmlInvoke` emission block (line 125-135), add:
     ```fsharp
     // Emit <onentry> blocks from ScxmlOnEntry annotations
     state.Annotations
     |> List.iter (fun a ->
         match a with
         | ScxmlAnnotation(ScxmlOnEntry(xml)) ->
             try
                 let onEntryEl = System.Xml.Linq.XElement.Parse(xml)
                 el.Add(onEntryEl)
             with :? System.Xml.XmlException -> ()  // Skip malformed XML
         | _ -> ())
     ```
  2. Emit `<onentry>` BEFORE child states and transitions for correct element ordering per W3C spec.
- **Parallel?**: No (foundational for T011).

### Subtask T011 – Emit `<onexit>` blocks

- **Purpose**: Same pattern as T010 for `<onexit>`.
- **File**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Steps**:
  1. Add immediately after T010's `<onentry>` emission:
     ```fsharp
     // Emit <onexit> blocks from ScxmlOnExit annotations
     state.Annotations
     |> List.iter (fun a ->
         match a with
         | ScxmlAnnotation(ScxmlOnExit(xml)) ->
             try
                 let onExitEl = System.Xml.Linq.XElement.Parse(xml)
                 el.Add(onExitEl)
             with :? System.Xml.XmlException -> ()
         | _ -> ())
     ```
  2. Emit after `<onentry>` blocks per W3C ordering.
- **Parallel?**: No (coupled with T010 for ordering).

### Subtask T012 – Emit `<initial>` child elements

- **Purpose**: When `ScxmlInitialElement` annotation is present, emit `<initial><transition target="..."/></initial>` instead of the `initial` attribute.
- **File**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Steps**:
  1. In `generateState`, check for `ScxmlInitialElement` BEFORE the existing `ScxmlInitial` attribute emission (line 97-103):
     ```fsharp
     // Check for <initial> child element form (takes precedence over attribute)
     let hasInitialElement =
         state.Annotations
         |> List.tryPick (fun a ->
             match a with
             | ScxmlAnnotation(ScxmlInitialElement(targetId)) -> Some targetId
             | _ -> None)

     match hasInitialElement with
     | Some targetId ->
         // Emit <initial> child element
         let initEl = XElement(scxmlNs + "initial")
         let transEl = XElement(scxmlNs + "transition")
         transEl.SetAttributeValue(XName.Get "target", targetId)
         initEl.Add(transEl)
         el.Add(initEl)
     | None ->
         // Fall back to initial attribute (existing behavior)
         state.Annotations
         |> List.tryPick (fun a ->
             match a with
             | ScxmlAnnotation(ScxmlInitial(id)) -> Some id
             | _ -> None)
         |> Option.iter (fun id -> el.SetAttributeValue(XName.Get "initial", id))
     ```
  2. The namespace used for `<initial>` and `<transition>` should match the document namespace (see T014).
- **Parallel?**: Yes (independent from T010/T011).

### Subtask T013 – Emit `<data src>` attribute

- **Purpose**: Add `src` attribute to `<data>` elements from `ScxmlDataSrc` annotations.
- **File**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Steps**:
  1. In `generateRoot` (line 145), in the datamodel generation block (line 164-175), after creating each `<data>` element:
     ```fsharp
     // Check for ScxmlDataSrc annotation matching this data entry
     doc.Annotations
     |> List.tryPick (fun a ->
         match a with
         | ScxmlAnnotation(ScxmlDataSrc(name, src)) when name = entry.Name -> Some src
         | _ -> None)
     |> Option.iter (fun src ->
         data.SetAttributeValue(XName.Get "src", src))
     ```
  2. When `src` is present, per W3C spec `expr` should not also be present. But the generator should emit both if both exist (the parser would have stored both — it's the source document's responsibility).
- **Parallel?**: Yes (independent from T010-T012).

### Subtask T014 – Respect namespace annotation

- **Purpose**: Use the namespace from `ScxmlNamespace` annotation instead of hardcoding W3C namespace.
- **File**: `src/Frank.Statecharts/Scxml/Generator.fs`
- **Steps**:
  1. In `buildXDocument` (line 192) or `generateRoot` (line 145), extract the namespace:
     ```fsharp
     let effectiveNs =
         doc.Annotations
         |> List.tryPick (fun a ->
             match a with
             | ScxmlAnnotation(ScxmlNamespace(ns)) -> Some ns
             | _ -> None)
         |> Option.map (fun ns ->
             if System.String.IsNullOrEmpty(ns) then XNamespace.None
             else XNamespace.Get(ns))
         |> Option.defaultValue scxmlNs  // Default to W3C namespace
     ```
  2. Thread `effectiveNs` through `generateRoot` and `generateState`. Replace all uses of `scxmlNs` with `effectiveNs` when constructing elements.
  3. This is the most invasive change — `scxmlNs` is used in `generateTransition`, `generateHistory`, `generateState`, `generateRoot`. Either:
     (a) Pass `effectiveNs` as a parameter to all functions, or
     (b) Compute it once in `buildXDocument` and pass it down.
     Option (b) is cleaner.
- **Parallel?**: Yes (independent from T010-T013, but affects all generation functions).
- **Notes**: This change touches multiple functions. Be careful not to break existing behavior — when no `ScxmlNamespace` annotation exists, default to `scxmlNs` (backward compatible).

### Subtask T015 – Update GeneratorTests.fs

- **Purpose**: Verify the generator correctly emits all new content types.
- **File**: `test/Frank.Statecharts.Tests/Scxml/GeneratorTests.fs`
- **Steps**:
  1. Add test: `StateNode` with `ScxmlOnEntry` annotation → output contains `<onentry>` block.
  2. Add test: `StateNode` with `ScxmlOnExit` annotation → output contains `<onexit>` block.
  3. Add test: `StateNode` with `ScxmlInitialElement` → output contains `<initial><transition target="..."/></initial>`.
  4. Add test: `ScxmlDataSrc` annotation → `<data>` element has `src` attribute.
  5. Add test: `ScxmlNamespace("")` → no namespace on elements.
  6. Add test: no `ScxmlNamespace` → default W3C namespace (backward compatible).
- **Parallel?**: No (depends on T010-T014).

### Subtask T016 – Verify build and tests

- **Purpose**: Full build and test validation.
- **Steps**: `dotnet build` and `dotnet test` — all must pass.

## Risks & Mitigations

- **Namespace threading**: Changing from module-level `scxmlNs` to parameterized namespace affects multiple functions. Pass as parameter from `buildXDocument` down through `generateRoot` → `generateState` → `generateTransition`/`generateHistory`.
- **Malformed XML in annotations**: `XElement.Parse()` wrapped in `try/catch` — skip silently on failure.
- **Element ordering**: W3C SCXML spec defines element ordering within `<state>`: datamodel, onentry, onexit, transition, initial, state, parallel, final, history, invoke. Follow this ordering.

## Review Guidance

- Verify `<onentry>`/`<onexit>` emitted from raw XML annotations
- Verify `<initial>` child element takes precedence over `initial` attribute
- Verify `<data src>` emitted when `ScxmlDataSrc` annotation present
- Verify namespace respects `ScxmlNamespace` annotation
- Verify backward compatibility when no new annotations present
- Run `dotnet test` — all green

## Activity Log

- 2026-03-18T07:24:37Z – system – lane=planned – Prompt created.
