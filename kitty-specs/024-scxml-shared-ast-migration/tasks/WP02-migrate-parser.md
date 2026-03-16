---
work_package_id: "WP02"
subtasks:
  - "T009"
  - "T010"
  - "T011"
  - "T012"
  - "T013"
  - "T014"
  - "T015"
title: "Migrate Parser to Shared AST"
phase: "Phase 1 - Core Migration"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-008, FR-009, FR-010, FR-011, FR-012, FR-013, FR-014, FR-015, FR-016]
history:
  - timestamp: "2026-03-16T19:26:17Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- Migrate Parser to Shared AST

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

This WP depends on WP01:
```bash
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

- Rewrite `src/Frank.Statecharts/Scxml/Parser.fs` to produce `Ast.ParseResult` (containing `StatechartDocument`) directly, eliminating the need for the `Mapper.toStatechartDocument` direction.
- All three entry points (`parseString`, `parseReader`, `parseStream`) return `Ast.ParseResult` instead of `ScxmlParseResult`.
- The parser preserves all SCXML-specific data via `ScxmlAnnotation` entries on the appropriate AST nodes.
- `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds (tests may not compile yet -- they are updated in WP04).

## Context & Constraints

- **Spec**: `kitty-specs/024-scxml-shared-ast-migration/spec.md` (FR-001 through FR-016)
- **Plan**: `kitty-specs/024-scxml-shared-ast-migration/plan.md` (Migration Pattern Reference)
- **WSD Precedent**: `src/Frank.Statecharts/Wsd/Parser.fs` directly constructs `StatechartDocument`, `StateNode`, `TransitionEdge` from `Frank.Statecharts.Ast`. Follow this exact pattern.
- **Mapper Logic**: `src/Frank.Statecharts/Scxml/Mapper.fs` (function `toStatechartDocument` and helpers) contains the conversion logic that must be absorbed into the parser. Read it carefully before starting.
- **Key Architectural Decision**: The parser must now return `StateNode * TransitionEdge list` from recursive state parsing (same as `Mapper.toStateNodeAndTransitions`), because the shared AST separates states and transitions into different `StatechartElement` entries.

---

## Subtasks & Detailed Guidance

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
- **Notes**: After this change, any code calling `parseString` will get `Ast.ParseResult` instead of `ScxmlParseResult`. This will break test files (fixed in WP04) but should not break `Mapper.fs` calls to the parser (Mapper.fs calls the parser internally -- but after this change, the Mapper's `toStatechartDocument` function is no longer needed since the parser returns the shared AST directly).

---

## Risks & Mitigations

- **State-scoped data entry collection**: The current parser stores state-scoped data in `ScxmlState.DataEntries`. The shared AST has no per-state data -- it's all flattened into `StatechartDocument.DataEntries`. The parser must collect data entries from the state hierarchy during recursive parsing. Mitigation: return data entries alongside transitions from `parseState`, or do a separate collection pass.
- **Position type collision**: Both `Scxml.Types.SourcePosition` and `Ast.SourcePosition` are `[<Struct>]` with identical fields. Consider changing `extractPosition` to return `Ast.SourcePosition` directly, eliminating the need for a conversion helper.
- **Mapper dependency**: After this WP, `Mapper.toStatechartDocument` becomes dead code but `Mapper.fromStatechartDocument` is still used by the generator (until WP03). The Mapper file must still compile. This should be fine since we are not changing Mapper.fs in this WP.

---

## Review Guidance

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
- Verify `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` passes.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T19:26:17Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP02 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
