---
work_package_id: WP01
title: SCXML Types and Project Setup
lane: "doing"
dependencies: []
base_branch: master
base_commit: 64a2cc78c24e4febcd6631dd8f37f8405a4269d8
created_at: '2026-03-16T04:02:40.398390+00:00'
subtasks:
- T001
- T002
phase: Phase 1 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "98569"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-16T01:17:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-024]
---

# Work Package Prompt: WP01 -- SCXML Types and Project Setup

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

No dependencies -- run:
```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

- Create `src/Frank.Statecharts/Scxml/Types.fs` containing all SCXML-specific parse types as F# records and discriminated unions.
- Update `Frank.Statecharts.fsproj` to include the new file in the compile list.
- `dotnet build src/Frank.Statecharts` succeeds across all three target frameworks (net8.0, net9.0, net10.0).
- All types use structural equality (F# records, no mutable fields).
- Types match the data model specification in `kitty-specs/018-scxml-parser-generator/data-model.md`.

## Context & Constraints

- **Spec**: `kitty-specs/018-scxml-parser-generator/spec.md` -- defines 6 key entities
- **Data model**: `kitty-specs/018-scxml-parser-generator/data-model.md` -- exact type definitions
- **Plan**: `kitty-specs/018-scxml-parser-generator/plan.md` -- layered architecture
- **Existing pattern**: `src/Frank.Statecharts/Wsd/Types.fs` -- follow the same module/internal conventions
- **Project file**: `src/Frank.Statecharts/Frank.Statecharts.fsproj` -- F# compile order matters; `Scxml/Types.fs` must appear before any other `Scxml/*.fs` entries
- **No additional NuGet dependencies** -- types use only FSharp.Core and System types

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `Scxml/Types.fs` with All SCXML Parse Types

- **Purpose**: Define the complete set of SCXML-specific parse types that the parser will produce and the generator will consume. These types represent the parsed SCXML document structure independent of the shared AST (spec 020).

- **Steps**:
  1. Create directory `src/Frank.Statecharts/Scxml/` if it does not exist.
  2. Create file `src/Frank.Statecharts/Scxml/Types.fs`.
  3. Add module declaration: `module internal Frank.Statecharts.Scxml.Types`
  4. Define all types in the following order (F# requires types to be defined before they are used):

  **a) `SourcePosition`** (struct):
  ```fsharp
  [<Struct>]
  type SourcePosition = { Line: int; Column: int }
  ```
  Note: This mirrors `Frank.Statecharts.Wsd.Types.SourcePosition`. They are separate types in separate modules. If spec 020 later defines a shared `SourcePosition`, both can be migrated.

  **b) `ScxmlTransitionType`** (DU):
  ```fsharp
  type ScxmlTransitionType =
      | Internal
      | External
  ```

  **c) `ScxmlHistoryKind`** (DU):
  ```fsharp
  type ScxmlHistoryKind =
      | Shallow
      | Deep
  ```

  **d) `ScxmlStateKind`** (DU):
  ```fsharp
  type ScxmlStateKind =
      | Simple       // <state> with no child states (atomic)
      | Compound     // <state> with child states
      | Parallel     // <parallel>
      | Final        // <final>
  ```

  **e) `DataEntry`** (record):
  ```fsharp
  type DataEntry =
      { Id: string
        Expression: string option
        Position: SourcePosition option }
  ```

  **f) `ScxmlTransition`** (record):
  ```fsharp
  type ScxmlTransition =
      { Event: string option
        Guard: string option
        Targets: string list
        TransitionType: ScxmlTransitionType
        Position: SourcePosition option }
  ```
  Note: `Targets` is a `string list` because W3C SCXML allows space-separated target IDs. An empty list means a targetless transition.

  **g) `ScxmlHistory`** (record):
  ```fsharp
  type ScxmlHistory =
      { Id: string
        Kind: ScxmlHistoryKind
        DefaultTransition: ScxmlTransition option
        Position: SourcePosition option }
  ```

  **h) `ScxmlInvoke`** (record):
  ```fsharp
  type ScxmlInvoke =
      { InvokeType: string option
        Src: string option
        Id: string option
        Position: SourcePosition option }
  ```

  **i) `ScxmlState`** (record):
  ```fsharp
  type ScxmlState =
      { Id: string option
        Kind: ScxmlStateKind
        InitialId: string option
        Transitions: ScxmlTransition list
        Children: ScxmlState list
        DataEntries: DataEntry list
        HistoryNodes: ScxmlHistory list
        InvokeNodes: ScxmlInvoke list
        Position: SourcePosition option }
  ```
  Note: `Id` is `string option` because the W3C spec allows states without IDs (though practically always present). `Children` enables recursive compound/parallel state hierarchies.

  **j) `ScxmlDocument`** (record):
  ```fsharp
  type ScxmlDocument =
      { Name: string option
        InitialId: string option
        DatamodelType: string option
        Binding: string option
        States: ScxmlState list
        DataEntries: DataEntry list
        Position: SourcePosition option }
  ```

  **k) `ParseError`** (record):
  ```fsharp
  type ParseError =
      { Description: string
        Position: SourcePosition option }
  ```

  **l) `ParseWarning`** (record):
  ```fsharp
  type ParseWarning =
      { Description: string
        Position: SourcePosition option
        Suggestion: string option }
  ```

  **m) `ScxmlParseResult`** (record):
  ```fsharp
  type ScxmlParseResult =
      { Document: ScxmlDocument option
        Errors: ParseError list
        Warnings: ParseWarning list }
  ```

- **Files**: `src/Frank.Statecharts/Scxml/Types.fs` (new file, ~80-100 lines)
- **Parallel?**: No -- must be completed before T002.
- **Notes**: The type definition order matters in F# -- types must be defined before they are referenced. The order listed above satisfies all forward reference constraints. `ScxmlTransition` is defined before `ScxmlHistory` (which uses it for `DefaultTransition`), and both are defined before `ScxmlState`.

### Subtask T002 -- Update `.fsproj` with `Scxml/Types.fs` Compile Entry

- **Purpose**: Wire the new types file into the F# compilation so `dotnet build` includes it.

- **Steps**:
  1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`.
  2. In the `<ItemGroup>` containing `<Compile>` entries, add `<Compile Include="Scxml/Types.fs" />` **before** the existing `<Compile Include="Wsd/Types.fs" />` line.
  3. The Scxml files should appear before Wsd files because they are independent modules and alphabetical ordering is conventional.

  The compile entry section should look like:
  ```xml
  <ItemGroup>
    <Compile Include="Scxml/Types.fs" />
    <Compile Include="Wsd/Types.fs" />
    <Compile Include="Wsd/Lexer.fs" />
    <Compile Include="Wsd/GuardParser.fs" />
    <Compile Include="Wsd/Parser.fs" />
    <Compile Include="Types.fs" />
    ...
  </ItemGroup>
  ```

- **Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj` (edit existing)
- **Parallel?**: No -- depends on T001 completing first (file must exist).
- **Notes**: F# compile order is significant. `Scxml/Types.fs` must come before any future `Scxml/Parser.fs` or `Scxml/Generator.fs` entries. Placing it before Wsd entries is fine since these modules are independent.

## Validation

After completing both subtasks:

```bash
dotnet build src/Frank.Statecharts
```

This must succeed for all three target frameworks (net8.0, net9.0, net10.0). Verify no warnings related to the new types.

## Risks & Mitigations

- **Risk**: Type ordering wrong, causing F# forward reference errors.
  - **Mitigation**: Follow the exact order specified in T001 (SourcePosition -> DUs -> simple records -> complex records -> result type).

- **Risk**: Struct `SourcePosition` causes unexpected boxing in generic contexts.
  - **Mitigation**: Only used in `option` fields -- F# handles struct options efficiently in modern versions.

## Review Guidance

- Verify all types match `data-model.md` exactly (field names, types, optionality).
- Verify `module internal` is used (not `module` -- types should not be public API).
- Verify structural equality works (no `[<ReferenceEquality>]` or mutable fields).
- Verify `.fsproj` compile entry is in the correct position.
- Verify `dotnet build src/Frank.Statecharts` succeeds on all three TFMs.

## Activity Log

- 2026-03-16T01:17:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:02:40Z – claude-opus – shell_pid=98569 – lane=doing – Assigned agent via workflow command
