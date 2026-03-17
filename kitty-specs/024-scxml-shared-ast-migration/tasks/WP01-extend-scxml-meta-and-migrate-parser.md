---
work_package_id: WP01
title: Extend ScxmlMeta DU + Migrate Parser to Shared AST
lane: "done"
dependencies: []
base_branch: master
base_commit: 2c8d91f0f1fe2b6bc3c8f334aa5d366779bd4ec9
created_at: '2026-03-16T22:51:46.152807+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
- T006
- T007
- T008
- T009
- T010
- T011
- T012
- T013
- T014
- T015
- T016
phase: Phase 0+1 - Foundation & Parser Migration
assignee: ''
agent: "claude-opus"
shell_pid: "26098"
review_status: approved
reviewed_by: claude-opus
history:
- timestamp: '2026-03-16T19:26:17Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
- timestamp: '2026-03-16T23:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Merged old WP01 (extend ScxmlMeta) and old WP02 (migrate parser) into single atomic WP. Eliminates transitional Mapper.fs fix issue.
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, FR-010, FR-011, FR-012, FR-013, FR-014, FR-015, FR-016, FR-028]
---

# Work Package Prompt: WP01 -- Extend ScxmlMeta DU + Migrate Parser to Shared AST

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

No dependencies -- this is the starting package:
```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

**Part A -- ScxmlMeta Extension (T001-T008)**:
- Extend the `ScxmlMeta` discriminated union in `src/Frank.Statecharts/Ast/Types.fs` with 5 new cases and extend 2 existing cases with additional fields.
- All existing code that pattern-matches on `ScxmlMeta` must be updated to handle the new cases and extended field signatures.
- No behavioral changes to existing functionality -- this is purely additive type work.

**Part B -- Parser Migration (T009-T016)**:
- Rewrite `src/Frank.Statecharts/Scxml/Parser.fs` to produce `Ast.ParseResult` (containing `StatechartDocument`) directly, eliminating the need for the `Mapper.toStatechartDocument` direction.
- All three entry points (`parseString`, `parseReader`, `parseStream`) return `Ast.ParseResult` instead of `ScxmlParseResult`.
- The parser preserves all SCXML-specific data via `ScxmlAnnotation` entries on the appropriate AST nodes.
- Delete `Scxml/Mapper.fs` (no longer needed once parser produces shared AST directly).

**Combined Success Criteria**:
- `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds across net8.0, net9.0, and net10.0 with zero errors.
- The transitional Mapper.fs fix problem is eliminated -- ScxmlMeta extension and parser migration happen atomically, and Mapper.fs deletion occurs at the end of this WP.

## Context & Constraints

- **Spec**: `kitty-specs/024-scxml-shared-ast-migration/spec.md` (FR-001 through FR-016, FR-028)
- **Plan**: `kitty-specs/024-scxml-shared-ast-migration/plan.md` (D1: ScxmlMeta Extension Strategy, Migration Pattern Reference)
- **Pattern**: Follow the existing `WsdMeta` and `AlpsMeta` patterns in `Ast/Types.fs` -- each case uses named fields.
- **WSD Precedent**: `src/Frank.Statecharts/Wsd/Parser.fs` directly constructs `StatechartDocument`, `StateNode`, `TransitionEdge` from `Frank.Statecharts.Ast`. Follow this exact pattern.
- **Mapper Logic**: `src/Frank.Statecharts/Scxml/Mapper.fs` (function `toStatechartDocument` and helpers) contains the conversion logic that must be absorbed into the parser. Read it carefully before starting.
- **Key Architectural Decision**: The parser must now return `StateNode * TransitionEdge list` from recursive state parsing (same as `Mapper.toStateNodeAndTransitions`), because the shared AST separates states and transitions into different `StatechartElement` entries.
- **Constraint**: All changes are within the `Frank.Statecharts` project. No public API changes (the project is `internal`).

---

## Subtasks & Detailed Guidance

### Part A: ScxmlMeta Extension (T001-T008)

### Subtask T001 -- Extend `ScxmlInvoke` with `id` field

- **Purpose**: SCXML `<invoke>` elements have an `id` attribute that must survive round-trip. The current `ScxmlInvoke` case carries `invokeType: string * src: string option` but not `id`.
- **Steps**:
  1. Open `src/Frank.Statecharts/Ast/Types.fs`
  2. Find the `ScxmlMeta` DU, locate the `ScxmlInvoke` case
  3. Change from:
     ```fsharp
     | ScxmlInvoke of invokeType: string * src: string option
     ```
     To:
     ```fsharp
     | ScxmlInvoke of invokeType: string * src: string option * id: string option
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`
- **Notes**: This is a breaking change for all existing call sites that construct or pattern-match `ScxmlInvoke`. These are fixed in T008.

### Subtask T002 -- Extend `ScxmlHistory` with `defaultTarget` field

- **Purpose**: SCXML `<history>` elements can contain a `<transition>` child for the default target. This must be preserved for round-trip fidelity. Storing it in the annotation avoids creating a separate `TransitionElement` for history defaults.
- **Steps**:
  1. In `src/Frank.Statecharts/Ast/Types.fs`, locate the `ScxmlHistory` case
  2. Change from:
     ```fsharp
     | ScxmlHistory of id: string * historyKind: HistoryKind
     ```
     To:
     ```fsharp
     | ScxmlHistory of id: string * historyKind: HistoryKind * defaultTarget: string option
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`
- **Notes**: The `defaultTarget` is `None` when the `<history>` element has no child `<transition>`.

### Subtask T003 -- Add `ScxmlTransitionType` case

- **Purpose**: SCXML transitions have a `type` attribute that can be `internal` or `external` (default). This must be preserved as an annotation on `TransitionEdge` for round-trip fidelity.
- **Steps**:
  1. Add to the `ScxmlMeta` DU:
     ```fsharp
     | ScxmlTransitionType of internal: bool
     ```
  2. `true` means internal, `false` means external.
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`
- **Notes**: We use a simple `bool` rather than referencing the `Scxml.Types.ScxmlTransitionType` DU, to avoid coupling the shared AST to format-internal types. The plan (D1) confirms this design.

### Subtask T004 -- Add `ScxmlMultiTarget` case

- **Purpose**: SCXML transitions can have space-separated multi-target `target` attributes (e.g., `target="s1 s2 s3"`). The shared AST `TransitionEdge.Target` only holds one target. The full list must be preserved via annotation.
- **Steps**:
  1. Add to the `ScxmlMeta` DU:
     ```fsharp
     | ScxmlMultiTarget of targets: string list
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`
- **Notes**: When the transition has a single target, no `ScxmlMultiTarget` annotation is needed (the `TransitionEdge.Target` field suffices). The annotation is only added for multi-target transitions (2+ targets).

### Subtask T005 -- Add `ScxmlDatamodelType` case

- **Purpose**: The SCXML root `<scxml>` element can have a `datamodel` attribute (e.g., `datamodel="ecmascript"`). This document-level attribute must be preserved via annotation on `StatechartDocument`.
- **Steps**:
  1. Add to the `ScxmlMeta` DU:
     ```fsharp
     | ScxmlDatamodelType of datamodel: string
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`

### Subtask T006 -- Add `ScxmlBinding` case

- **Purpose**: The SCXML root `<scxml>` element can have a `binding` attribute (e.g., `binding="early"` or `binding="late"`). This must be preserved for round-trip fidelity.
- **Steps**:
  1. Add to the `ScxmlMeta` DU:
     ```fsharp
     | ScxmlBinding of binding: string
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`

### Subtask T007 -- Add `ScxmlInitial` case

- **Purpose**: SCXML compound `<state>` elements can have an `initial` attribute specifying the default child state. This state-level attribute must be preserved via annotation on `StateNode`.
- **Steps**:
  1. Add to the `ScxmlMeta` DU:
     ```fsharp
     | ScxmlInitial of initialId: string
     ```
- **Files**: `src/Frank.Statecharts/Ast/Types.fs`

### Subtask T008 -- Fix all existing pattern matches on `ScxmlMeta`

- **Purpose**: After extending `ScxmlInvoke`/`ScxmlHistory` and adding new cases, all existing code that pattern-matches on `ScxmlMeta` will fail to compile. These must be updated.
- **Steps**:
  1. Search for all pattern matches on `ScxmlMeta` cases across the project:
     ```bash
     grep -rn "ScxmlInvoke\|ScxmlHistory\|ScxmlNamespace" src/ test/ --include="*.fs"
     ```
  2. **`src/Frank.Statecharts/Scxml/Mapper.fs`**: No need for transitional fixes -- Mapper.fs will be deleted at the end of this WP (T016). Skip Mapper.fs fixes entirely.
  3. **`src/Frank.Statecharts/Validation/`**: Check if any validator pattern-matches on `ScxmlMeta`. If so, add wildcard matches for new cases.
  4. **`test/Frank.Statecharts.Tests/Ast/`**: Check AST test files for `ScxmlMeta` construction. Update field counts. Specifically, `test/Frank.Statecharts.Tests/Ast/TypeConstructionTests.fs` and `test/Frank.Statecharts.Tests/Ast/PartialPopulationTests.fs` construct `ScxmlHistory("h1", Deep)` with 2 args which will break when the DU gains a 3rd field (`defaultTarget`). These must be updated to `ScxmlHistory("h1", Deep, None)` (3 args). Similarly, any `ScxmlInvoke` constructions must gain the new `id` field.
  5. The build does NOT need to pass after T008 alone -- Mapper.fs will have compile errors from the extended DU cases. This is intentional: the parser migration (T009-T015) and Mapper.fs deletion (T016) happen atomically within this same WP.
- **Files**:
  - `src/Frank.Statecharts/Validation/Validator.fs` (if applicable)
  - `test/Frank.Statecharts.Tests/Ast/TypeConstructionTests.fs` (confirmed in scope -- constructs `ScxmlHistory` and `ScxmlInvoke` with old arg counts)
  - `test/Frank.Statecharts.Tests/Ast/PartialPopulationTests.fs` (confirmed in scope -- constructs `ScxmlHistory` with old arg count)
- **Notes**: The AST test file fixes are permanent -- they update test construction to use the new field counts. Mapper.fs fixes are NOT needed because Mapper.fs is deleted at the end of this WP (T016).

---

### Part B: Parser Migration (T009-T016)

### Subtask T009 -- Change module opens

- **Purpose**: The parser must work with shared AST types while retaining access to SCXML-specific internal types needed during parsing (`ScxmlTransitionType`, `ScxmlHistoryKind`, `SourcePosition`).
- **Steps**:
  1. Open `src/Frank.Statecharts/Scxml/Parser.fs`
  2. Add `open Frank.Statecharts.Ast` after the module declaration
  3. Keep `open Frank.Statecharts.Scxml.Types` but be aware of name collisions:
     - `SourcePosition` exists in both namespaces (same structure, different types)
     - `DataEntry` exists in both (different field names: `Id` vs `Name`)
     - Use type aliases if needed: `type private ScxmlPos = Frank.Statecharts.Scxml.Types.SourcePosition`
  4. The module declaration should look like:
     ```fsharp
     module internal Frank.Statecharts.Scxml.Parser

     open System.Xml
     open System.Xml.Linq
     open Frank.Statecharts.Ast
     open Frank.Statecharts.Scxml.Types
     ```
- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Notes**: Name collisions between `Scxml.Types.SourcePosition` and `Ast.SourcePosition` are the main concern. The parser reads XML positions using the `Scxml.Types.SourcePosition` struct internally but must output `Ast.SourcePosition` values. Use explicit qualification or aliases.

### Subtask T010 -- Rewrite `parseState` to produce `StateNode` directly

- **Purpose**: Instead of producing `ScxmlState`, the recursive state parser must produce `StateNode * TransitionEdge list` -- a state node plus all transitions collected from the subtree.
- **Steps**:
  1. Change the return type of `parseState` from `ScxmlState` to `StateNode * TransitionEdge list`
  2. Map element names to `StateKind`:
     - `"final"` -> `Final`
     - `"parallel"` -> `Parallel`
     - `"state"` -> `Regular` (both simple and compound -- the shared AST uses `Regular` for both; child presence is implicit in `Children`)
     - Default -> `Regular`
  3. Recursively parse child states: call `parseState` on each child, collect `StateNode` entries and `TransitionEdge` lists
  4. Parse history nodes via `parseHistory` (see T012) -- history nodes become child `StateNode` entries
  5. Parse invoke nodes -- convert to `ScxmlAnnotation(ScxmlInvoke(invokeType, src, id))` annotations on the `StateNode`
  6. Parse state-level `initial` attribute -- if present, add `ScxmlAnnotation(ScxmlInitial(initialId))` to the `StateNode`
  7. Build the `StateNode`:
     ```fsharp
     let stateNode =
         { Identifier = stateId
           Label = None
           Kind = astKind
           Children = childNodes @ historyNodes
           Activities = None
           Position = extractPosition el |> Option.map toAstPosition
           Annotations = invokeAnnotations @ initialAnnotation }
     ```
  8. Collect own transitions (from `parseTransition`, T011) and combine with child transitions:
     ```fsharp
     stateNode, ownTransitions @ childTransitions
     ```
- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Notes**:
  - The `stateId` is `(attrValue "id" el) |> Option.defaultValue ""`
  - Position conversion helper: `let private toAstPosition (pos: Scxml.Types.SourcePosition) : Ast.SourcePosition = { Line = pos.Line; Column = pos.Column }` -- or if `extractPosition` is changed to return `Ast.SourcePosition` directly, even simpler.
  - Consider changing `extractPosition` to return `Ast.SourcePosition option` directly, eliminating the conversion step everywhere.

### Subtask T011 -- Rewrite `parseTransition` to produce `TransitionEdge` directly

- **Purpose**: Transitions must be produced as `TransitionEdge` values with SCXML-specific data in annotations, rather than as `ScxmlTransition` values.
- **Steps**:
  1. Change `parseTransition` to accept the parent state's identifier as a parameter (needed for `TransitionEdge.Source`):
     ```fsharp
     let private parseTransition (sourceId: string) (el: XElement) : TransitionEdge =
     ```
  2. Parse targets from the `target` attribute (space-separated):
     ```fsharp
     let targets =
         match attrValue "target" el with
         | Some t -> t.Split(' ', System.StringSplitOptions.RemoveEmptyEntries) |> Array.toList
         | None -> []
     ```
  3. Build annotations list:
     - If transition type is internal: add `ScxmlAnnotation(ScxmlTransitionType(true))`
     - If targets has 2+ entries: add `ScxmlAnnotation(ScxmlMultiTarget(targets))`
  4. Build the `TransitionEdge`:
     ```fsharp
     { Source = sourceId
       Target = targets |> List.tryHead
       Event = attrValue "event" el
       Guard = attrValue "cond" el
       Action = None
       Parameters = []
       Position = extractPosition el |> Option.map toAstPosition
       Annotations = annotations }
     ```
  5. Update the call sites in `parseState` to pass `sourceId`:
     ```fsharp
     let ownTransitions =
         el.Elements()
         |> Seq.filter (fun child -> isElement "transition" child)
         |> Seq.map (parseTransition stateId)
         |> Seq.toList
     ```
- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Notes**:
  - Only add `ScxmlTransitionType` annotation when the type is internal. External is the default and does not need an annotation (generator defaults to external when no annotation is present).
  - Only add `ScxmlMultiTarget` when there are 2+ targets. Single-target transitions use `TransitionEdge.Target` alone.

### Subtask T012 -- Rewrite `parseHistory` to produce `StateNode`

- **Purpose**: History pseudo-states must be represented as child `StateNode` entries with `Kind = ShallowHistory/DeepHistory` and a `ScxmlAnnotation(ScxmlHistory(...))` carrying the id, kind, and default transition target.
- **Steps**:
  1. Change the return type from `ScxmlHistory` to `StateNode`:
     ```fsharp
     let private parseHistory (warnings: ResizeArray<Ast.ParseWarning>) (el: XElement) : StateNode =
     ```
  2. Determine history kind from `type` attribute (same logic as current, but map to `Ast.HistoryKind` and `Ast.StateKind`):
     - `"deep"` -> `StateKind.DeepHistory`, `HistoryKind.Deep`
     - `"shallow"` or default -> `StateKind.ShallowHistory`, `HistoryKind.Shallow`
  3. Parse default transition target:
     ```fsharp
     let defaultTarget =
         el.Elements()
         |> Seq.tryFind (fun child -> child.Name.LocalName = "transition")
         |> Option.bind (fun t -> attrValue "target" t)
     ```
  4. Build the annotation:
     ```fsharp
     ScxmlAnnotation(ScxmlHistory(historyId, astHistoryKind, defaultTarget))
     ```
  5. Build the `StateNode`:
     ```fsharp
     { Identifier = historyId
       Label = None
       Kind = stateKind  // ShallowHistory or DeepHistory
       Children = []
       Activities = None
       Position = extractPosition el |> Option.map toAstPosition
       Annotations = [ ScxmlAnnotation(ScxmlHistory(historyId, astHistoryKind, defaultTarget)) ] }
     ```
  6. Update warning type: the `warnings` parameter now collects `Ast.ParseWarning` instead of `Scxml.Types.ParseWarning`:
     ```fsharp
     warnings.Add(
         { Ast.ParseWarning.Position = extractPosition el |> Option.map toAstPosition
           Description = sprintf "Invalid <history> type value '%s'; defaulting to 'shallow'" invalid
           Suggestion = Some "Use 'shallow' or 'deep'" })
     ```
- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Notes**: The default transition target is a simple string (the target state id), not a full `TransitionEdge`. This keeps the annotation payload simple and avoids creating separate `TransitionElement` entries for history defaults.

### Subtask T013 -- Rewrite `parseDataEntries` to produce `Ast.DataEntry`

- **Purpose**: Data entries must use the shared AST `DataEntry` type (which has `Name` instead of `Id`).
- **Steps**:
  1. Change the return type from `Scxml.Types.DataEntry list` to `Ast.DataEntry list`:
     ```fsharp
     let private parseDataEntries (parent: XElement) : Ast.DataEntry list =
     ```
  2. Update the `Seq.map` to construct `Ast.DataEntry`:
     ```fsharp
     { Ast.DataEntry.Name = (attrValue "id" dataEl) |> Option.defaultValue ""
       Expression = expr
       Position = extractPosition dataEl |> Option.map toAstPosition }
     ```
  3. Note: `Ast.DataEntry.Name` corresponds to SCXML `<data id="...">`. The SCXML `id` attribute maps to the shared AST `Name` field.
- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Notes**: If using type alias or explicit qualification to disambiguate from `Scxml.Types.DataEntry`, prefer explicit qualification: `Ast.DataEntry`.

### Subtask T014 -- Rewrite `parseDocument` to produce `Ast.ParseResult`

- **Purpose**: The core `parseDocument` function must produce `Ast.ParseResult` (with `StatechartDocument`) instead of `ScxmlParseResult` (with `ScxmlDocument option`).
- **Steps**:
  1. Change the return type:
     ```fsharp
     let private parseDocument (xdoc: XDocument) : Ast.ParseResult =
     ```
  2. Handle the null/non-scxml-root error cases by returning an empty `StatechartDocument` with error entries (instead of `Document = None`):
     ```fsharp
     if root = null then
         { Document =
             { Title = None; InitialStateId = None; Elements = []; DataEntries = []; Annotations = [] }
           Errors =
             [ { Position = None
                 Description = "Empty XML document: no root element found"
                 Expected = ""; Found = ""; CorrectiveExample = "" } ]
           Warnings = [] }
     ```
  3. In the success path, change the warnings accumulator to `ResizeArray<Ast.ParseWarning>`:
     ```fsharp
     let warnings = ResizeArray<Ast.ParseWarning>()
     ```
  4. Parse top-level states using the rewritten `parseState` (returns `StateNode * TransitionEdge list` per state):
     ```fsharp
     let stateResults =
         root.Elements()
         |> Seq.filter isStateElement
         |> Seq.map (parseState warnings)
         |> Seq.toList

     let stateNodes = stateResults |> List.map fst
     let allTransitions = stateResults |> List.collect snd
     ```
  5. Build the `Elements` list (states first, then transitions -- matching Mapper order):
     ```fsharp
     let stateElements = stateNodes |> List.map StateDecl
     let transitionElements = allTransitions |> List.map TransitionElement
     let elements = stateElements @ transitionElements
     ```
  6. Parse document-level data entries:
     ```fsharp
     let dataEntries = parseDataEntries root
     ```
     Also collect state-scoped data entries. The Mapper flattens all data entries from the state hierarchy into `StatechartDocument.DataEntries`. Replicate this:
     ```fsharp
     let rec collectStateData (s: StateNode) =
         // State-scoped data entries were already parsed into the parent state's context
         // They need to be passed through from parseState
     ```
     **Important**: The current parser already parses state-scoped data entries into `ScxmlState.DataEntries`. After migration, state-scoped data entries should be collected during `parseState` and bubbled up alongside transitions. Alternatively, `parseState` can return a triple: `StateNode * TransitionEdge list * Ast.DataEntry list`.

     The simplest approach: have `parseState` return state-scoped data entries as a third element, then `parseDocument` combines document-level + all state-scoped entries into `StatechartDocument.DataEntries`.

  7. Build document-level annotations:
     ```fsharp
     let docAnnotations =
         [ match attrValue "datamodel" root with
           | Some dm -> yield ScxmlAnnotation(ScxmlDatamodelType(dm))
           | None -> ()
           match attrValue "binding" root with
           | Some b -> yield ScxmlAnnotation(ScxmlBinding(b))
           | None -> () ]
     ```
  8. Initial state inference (same logic as current):
     ```fsharp
     let initialId =
         match attrValue "initial" root with
         | Some id -> Some id
         | None -> stateNodes |> List.tryHead |> Option.map (fun s -> s.Identifier)
     ```
  9. Build the `StatechartDocument`:
     ```fsharp
     let doc =
         { Title = attrValue "name" root
           InitialStateId = initialId
           Elements = elements
           DataEntries = allDataEntries  // document-level + state-scoped, flattened
           Annotations = docAnnotations }
     ```
  10. Build the `ParseResult`:
      ```fsharp
      { Document = doc
        Errors = []
        Warnings = warnings |> Seq.toList }
      ```
- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Notes**:
  - Warning type for `collectRootWarnings` and `collectChildWarnings` must be updated from `Scxml.Types.ParseWarning` to `Ast.ParseWarning`.
  - The `Ast.ParseWarning` has `Position: SourcePosition option`, `Description: string`, `Suggestion: string option` -- same fields as the SCXML version.

### Subtask T015 -- Update entry point return types

- **Purpose**: The public-facing parse functions must return `Ast.ParseResult` instead of `ScxmlParseResult`.
- **Steps**:
  1. Update `tryParseWith`:
     ```fsharp
     let private tryParseWith (loadFn: unit -> XDocument) : Ast.ParseResult =
         try
             loadFn () |> parseDocument
         with :? XmlException as ex ->
             { Document =
                 { Title = None; InitialStateId = None; Elements = []; DataEntries = []; Annotations = [] }
               Errors =
                 [ { Position = Some { Line = ex.LineNumber; Column = ex.LinePosition }
                     Description = ex.Message
                     Expected = ""; Found = ""; CorrectiveExample = "" } ]
               Warnings = [] }
     ```
  2. Update `parseString`, `parseReader`, `parseStream` return types:
     ```fsharp
     let parseString (xml: string) : Ast.ParseResult = ...
     let parseReader (reader: System.IO.TextReader) : Ast.ParseResult = ...
     let parseStream (stream: System.IO.Stream) : Ast.ParseResult = ...
     ```
  3. Note: the function bodies do not change -- they just call `tryParseWith`.
- **Files**: `src/Frank.Statecharts/Scxml/Parser.fs`
- **Notes**: After this change, any code calling `parseString` will get `Ast.ParseResult` instead of `ScxmlParseResult`. This will break test files (fixed in WP03) and Mapper.fs (deleted in T016).

### Subtask T016 -- Delete Mapper.fs and remove from fsproj

- **Purpose**: With the parser now producing `Ast.ParseResult` directly, `Mapper.fs` is no longer needed. The `toStatechartDocument` direction has been absorbed into the parser. The `fromStatechartDocument` direction is absorbed into Generator.fs in WP02.
- **Steps**:
  1. Delete `src/Frank.Statecharts/Scxml/Mapper.fs`
  2. Remove `<Compile Include="Scxml/Mapper.fs" />` from `src/Frank.Statecharts/Frank.Statecharts.fsproj`
  3. Verify that no other source file (besides tests, which are updated in WP03) references `Scxml.Mapper`. The generator (`Generator.fs`) currently calls `Mapper.fromStatechartDocument` -- if it does, that call must be temporarily commented out or the generator's WP02 migration must account for the mapper's absence.
  4. Run `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` to confirm the build passes.
- **Files**: `src/Frank.Statecharts/Scxml/Mapper.fs` (deleted), `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Notes**: This subtask completes the atomic merge. By combining ScxmlMeta extension + parser migration + Mapper deletion into one WP, we avoid the transitional state where Mapper.fs has compile errors from the extended DU cases.

---

## Risks & Mitigations

- **Breaking existing call sites**: Extending `ScxmlInvoke` from 2 to 3 fields and `ScxmlHistory` from 2 to 3 fields breaks all existing construction and pattern-match sites. Mitigation: T008 fixes non-Mapper sites; Mapper.fs is deleted in T016 (no transitional fixes needed).
- **Missing pattern matches**: New `ScxmlMeta` cases added in T003-T007 will cause incomplete pattern match warnings. Mitigation: check for exhaustive matches in validation code; add wildcard branches where needed.
- **State-scoped data entry collection**: The current parser stores state-scoped data in `ScxmlState.DataEntries`. The shared AST has no per-state data -- it's all flattened into `StatechartDocument.DataEntries`. The parser must collect data entries from the state hierarchy during recursive parsing. Mitigation: return data entries alongside transitions from `parseState`, or do a separate collection pass.
- **Position type collision**: Both `Scxml.Types.SourcePosition` and `Ast.SourcePosition` are `[<Struct>]` with identical fields. Consider changing `extractPosition` to return `Ast.SourcePosition` directly, eliminating the need for a conversion helper.

---

## Review Guidance

**Part A (ScxmlMeta)**:
- Verify the `ScxmlMeta` DU in `Ast/Types.fs` has exactly 8 cases after changes (3 original + 5 new, with 2 originals extended).
- Verify field names match the plan (D1): `invokeType`, `src`, `id`, `historyKind`, `defaultTarget`, `internal`, `targets`, `datamodel`, `binding`, `initialId`.

**Part B (Parser)**:
- Verify all three parse entry points return `Ast.ParseResult`.
- Verify the parser no longer constructs any `ScxmlDocument`, `ScxmlState`, `ScxmlTransition`, or `ScxmlParseResult` values.
- Verify all SCXML-specific data is preserved via `ScxmlAnnotation` entries:
  - Transition type (internal) -> `ScxmlAnnotation(ScxmlTransitionType(true))`
  - Multi-target -> `ScxmlAnnotation(ScxmlMultiTarget(targets))`
  - History -> `ScxmlAnnotation(ScxmlHistory(id, kind, defaultTarget))`
  - Invoke -> `ScxmlAnnotation(ScxmlInvoke(type, src, id))`
  - State initial -> `ScxmlAnnotation(ScxmlInitial(id))`
  - Document datamodel -> `ScxmlAnnotation(ScxmlDatamodelType(dm))`
  - Document binding -> `ScxmlAnnotation(ScxmlBinding(b))`
- Verify error cases return an empty `StatechartDocument` (not `None`/`null`).
- Verify `Mapper.fs` is deleted and removed from fsproj.

**Combined**:
- Verify `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` passes on all 3 TFMs.
- Verify no incomplete pattern match warnings related to `ScxmlMeta`.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T19:26:17Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T23:00:00Z -- system -- lane=planned -- Merged old WP01 (extend ScxmlMeta) and old WP02 (migrate parser) into single atomic WP.

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP01 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-16T22:51:46Z – claude-opus – shell_pid=26098 – lane=doing – Assigned agent via workflow command
- 2026-03-16T22:57:38Z – claude-opus – shell_pid=26098 – lane=for_review – Ready for review: Extended ScxmlMeta DU with 5 new cases and 2 extended cases, migrated SCXML parser to produce Ast.ParseResult directly, deleted Mapper.fs. Build passes on all 3 TFMs with 0 warnings/errors.
- 2026-03-16T23:15:00Z – claude-opus – lane=done – review_status=approved – Code review APPROVED. All 16 subtasks verified: ScxmlMeta DU has 8 cases (correct), parser returns Ast.ParseResult from all 3 entry points, Mapper.fs deleted and removed from fsproj, Generator also migrated to shared AST (bonus), test files updated for extended DU signatures, no remaining references to old SCXML types. No blocking issues found.
