---
work_package_id: "WP01"
title: "Shared AST Type Definitions"
phase: "Phase 1 - Foundation"
lane: "done"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "Ryan Riley"
dependencies: []
requirement_refs:
  - "FR-001"
  - "FR-002"
  - "FR-003"
  - "FR-004"
  - "FR-005"
  - "FR-006"
  - "FR-007"
  - "FR-013"
  - "FR-014"
  - "FR-015"
  - "FR-016"
  - "FR-017"
  - "FR-019"
  - "FR-020"
  - "FR-021"
  - "FR-022"
subtasks:
  - "T001"
  - "T002"
  - "T003"
  - "T004"
  - "T005"
  - "T006"
history:
  - timestamp: "2026-03-15T23:59:08Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP01 -- Shared AST Type Definitions

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

- Create a new file `src/Frank.Statecharts/Ast/Types.fs` containing all shared AST types defined in the data model
- All types are `public` (not `internal`) per planning decision PD-001
- All record types support structural equality (no mutable fields)
- `SourcePosition` is a `[<Struct>]` type
- Multi-target build succeeds: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` for net8.0, net9.0, and net10.0
- The `.fsproj` compile order places `Ast/Types.fs` before `Wsd/Types.fs`

## Context & Constraints

- **Spec**: `kitty-specs/020-shared-statechart-ast/spec.md` (FR-001 through FR-022)
- **Data Model**: `kitty-specs/020-shared-statechart-ast/data-model.md` (complete type definitions)
- **Plan**: `kitty-specs/020-shared-statechart-ast/plan.md` (compile order, type migration map)
- **Research**: `kitty-specs/020-shared-statechart-ast/research.md` (decisions D-001 through D-013)
- **Constitution**: Types are pure data -- no mutable state, no framework coupling, no `IDisposable`
- **Key decision D-006**: No trivia pattern; position and annotations live directly on AST nodes
- **Key decision PD-006**: Mutually recursive types use F# `and` keyword

## Subtasks & Detailed Guidance

### Subtask T001 -- Define core type building blocks

**Purpose**: Establish the foundational types that other AST types depend on: `SourcePosition`, `HistoryKind`, `StateKind`, all annotation-related types (`ArrowStyle`, `Direction`, `TransitionStyle`, `WsdNotePosition`, `WsdMeta`, `AlpsMeta`, `ScxmlMeta`, `SmcatMeta`, `XStateMeta`, `Annotation`).

**Steps**:

1. Create directory `src/Frank.Statecharts/Ast/` if it does not exist.
2. Create file `src/Frank.Statecharts/Ast/Types.fs` with module declaration:
   ```fsharp
   namespace Frank.Statecharts.Ast
   ```
   Note: Use `namespace` (not `module`) since we need multiple types defined at the namespace level. This allows other modules to `open Frank.Statecharts.Ast` to access the types.

3. Define `SourcePosition` (FR-007):
   ```fsharp
   [<Struct>]
   type SourcePosition = { Line: int; Column: int }
   ```

4. Define `HistoryKind` (FR-019):
   ```fsharp
   type HistoryKind =
       | Shallow
       | Deep
   ```

5. Define `StateKind` (FR-005) -- 9 cases:
   ```fsharp
   type StateKind =
       | Regular
       | Initial
       | Final
       | Parallel
       | ShallowHistory
       | DeepHistory
       | Choice
       | ForkJoin
       | Terminate
   ```

6. Define WSD annotation payload types (FR-020):
   ```fsharp
   type ArrowStyle = Solid | Dashed
   type Direction = Forward | Deactivating

   type TransitionStyle =
       { ArrowStyle: ArrowStyle
         Direction: Direction }

   type WsdNotePosition = Over | LeftOf | RightOf

   type WsdMeta =
       | WsdTransitionStyle of TransitionStyle
       | WsdNotePosition of WsdNotePosition
   ```

7. Define ALPS annotation stub (D-010):
   ```fsharp
   type AlpsTransitionKind = Safe | Unsafe | Idempotent

   type AlpsMeta =
       | AlpsTransitionType of AlpsTransitionKind
       | AlpsDescriptorHref of string
       | AlpsExtension of name: string * value: string
   ```

8. Define SCXML annotation stub:
   ```fsharp
   type ScxmlMeta =
       | ScxmlInvoke of invokeType: string * src: string option
       | ScxmlHistory of id: string * historyKind: HistoryKind
       | ScxmlNamespace of string
   ```

9. Define smcat annotation stub:
   ```fsharp
   type SmcatMeta =
       | SmcatColor of string
       | SmcatStateLabel of string
       | SmcatActivity of kind: string * body: string
   ```

10. Define XState annotation stub:
    ```fsharp
    type XStateMeta =
        | XStateAction of string
        | XStateService of string
    ```

11. Define `Annotation` DU (FR-006) -- 5 cases:
    ```fsharp
    type Annotation =
        | WsdAnnotation of WsdMeta
        | AlpsAnnotation of AlpsMeta
        | ScxmlAnnotation of ScxmlMeta
        | SmcatAnnotation of SmcatMeta
        | XStateAnnotation of XStateMeta
    ```

**Files**: `src/Frank.Statecharts/Ast/Types.fs` (new file)
**Parallel?**: No (other subtasks depend on these types)

### Subtask T002 -- Define supporting record types and DUs

**Purpose**: Define `StateActivities`, `DataEntry`, `NoteContent`, `GroupKind`, and `Directive` types that are used by the mutually recursive types in T003.

**Steps**:

1. Add `StateActivities` record after `Annotation`:
   ```fsharp
   type StateActivities =
       { Entry: string list
         Exit: string list
         Do: string list }
   ```

2. Add `DataEntry` record (FR-004):
   ```fsharp
   type DataEntry =
       { Name: string
         Expression: string option
         Position: SourcePosition option }
   ```

3. Add `NoteContent` record:
   ```fsharp
   type NoteContent =
       { Target: string
         Content: string
         Position: SourcePosition option
         Annotations: Annotation list }
   ```

4. Add `GroupKind` DU (FR-021) -- 7 cases, same as existing WSD GroupKind:
   ```fsharp
   type GroupKind =
       | Alt
       | Opt
       | Loop
       | Par
       | Break
       | Critical
       | Ref
   ```

5. Add `Directive` DU:
   ```fsharp
   type Directive =
       | TitleDirective of title: string * position: SourcePosition option
       | AutoNumberDirective of position: SourcePosition option
   ```

**Files**: `src/Frank.Statecharts/Ast/Types.fs`
**Parallel?**: No (depends on T001, T003 depends on this)

### Subtask T003 -- Define mutually recursive types

**Purpose**: Define the core AST node types that reference each other: `GroupBranch`, `GroupBlock`, `StateNode`, `TransitionEdge`, `StatechartElement`, and `StatechartDocument`. These form a mutually recursive type group.

**Steps**:

1. Define the mutually recursive type block using `type ... and ... and ...`:

```fsharp
type StatechartElement =
    | StateDecl of StateNode
    | TransitionElement of TransitionEdge
    | NoteElement of NoteContent
    | GroupElement of GroupBlock
    | DirectiveElement of Directive

and GroupBranch =
    { Condition: string option
      Elements: StatechartElement list }

and GroupBlock =
    { Kind: GroupKind
      Branches: GroupBranch list
      Position: SourcePosition option }

and StateNode =
    { Identifier: string
      Label: string option
      Kind: StateKind
      Children: StateNode list
      Activities: StateActivities option
      Position: SourcePosition option
      Annotations: Annotation list }

and TransitionEdge =
    { Source: string
      Target: string option
      Event: string option
      Guard: string option
      Action: string option
      Parameters: string list
      Position: SourcePosition option
      Annotations: Annotation list }
```

2. Define `StatechartDocument` (FR-001) after the recursive block:
```fsharp
type StatechartDocument =
    { Title: string option
      InitialStateId: string option
      Elements: StatechartElement list
      DataEntries: DataEntry list
      Annotations: Annotation list }
```

**CRITICAL**: `StatechartDocument` references `StatechartElement` but is NOT part of the recursive cycle (nothing references it back). It can be defined as a standalone `type` after the `and` block.

**CRITICAL**: `NoteContent` does NOT reference any of the recursive types, so it is defined separately in T002. Only `StatechartElement` references `NoteContent`, not the other way around.

**Files**: `src/Frank.Statecharts/Ast/Types.fs`
**Parallel?**: No (depends on T001 and T002)
**Notes**: The F# compiler requires all types in a mutually recursive group to be connected via `and`. `StatechartElement` -> `GroupBlock` -> `GroupBranch` -> `StatechartElement` forms the core cycle. `StateNode` is recursive (children). `TransitionEdge` is NOT recursive but is included for convenience since `StatechartElement` wraps it.

### Subtask T004 -- Define parse result types

**Purpose**: Define `ParseFailure`, `ParseWarning`, and `ParseResult` as shared types used by all format parsers. These are the last types in the file.

**Steps**:

1. Add `ParseFailure` (FR-008) -- note `Position` is now `option`:
   ```fsharp
   type ParseFailure =
       { Position: SourcePosition option
         Description: string
         Expected: string
         Found: string
         CorrectiveExample: string }
   ```

2. Add `ParseWarning` (FR-009) -- note `Position` is now `option`:
   ```fsharp
   type ParseWarning =
       { Position: SourcePosition option
         Description: string
         Suggestion: string option }
   ```

3. Add `ParseResult` (FR-010):
   ```fsharp
   type ParseResult =
       { Document: StatechartDocument
         Errors: ParseFailure list
         Warnings: ParseWarning list }
   ```

**Files**: `src/Frank.Statecharts/Ast/Types.fs`
**Parallel?**: No (depends on T003 for `StatechartDocument`)
**Notes**: The key change from WSD's types: `Position` fields become `option` (PD-005), and `Diagram` becomes `Document: StatechartDocument`.

### Subtask T005 -- Update project compile order

**Purpose**: Add `Ast/Types.fs` to the `.fsproj` as the first compile item so all other source files can reference the shared AST types.

**Steps**:

1. Edit `src/Frank.Statecharts/Frank.Statecharts.fsproj`
2. In the `<ItemGroup>` containing `<Compile>` items, add as the FIRST entry:
   ```xml
   <Compile Include="Ast/Types.fs" />
   ```
3. The compile order should be:
   ```xml
   <Compile Include="Ast/Types.fs" />
   <Compile Include="Wsd/Types.fs" />
   <Compile Include="Wsd/Lexer.fs" />
   <Compile Include="Wsd/GuardParser.fs" />
   <Compile Include="Wsd/Parser.fs" />
   <Compile Include="Types.fs" />
   <Compile Include="Store.fs" />
   <!-- ... rest unchanged ... -->
   ```

**Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
**Parallel?**: No

### Subtask T006 -- Verify multi-target build

**Purpose**: Confirm the new types compile correctly under all three target frameworks.

**Steps**:

1. Run `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` and confirm success
2. Specifically verify no warnings about:
   - Unused `open` directives
   - Shadowed type names
   - Struct layout issues

**Files**: N/A (build verification)
**Parallel?**: Yes (can run after T001-T005)
**Notes**: At this stage, the build will still succeed because existing WSD types have not been removed yet. Both the shared AST types and the WSD types coexist (with potential shadowing that will be resolved in WP02).

## Risks & Mitigations

- **Type shadowing**: Both `Ast.Types` and `Wsd.Types` define `SourcePosition`, `GroupKind`, `ParseFailure`, etc. As long as WP01 only adds the new file without modifying existing files, both can coexist temporarily. The WSD code continues to use its own types via `open Frank.Statecharts.Wsd.Types`.
- **Namespace vs module**: Using `namespace Frank.Statecharts.Ast` (not `module`) is required since we have multiple top-level types. If using `module`, all types would need to be prefixed with the module name.
- **Struct equality**: `[<Struct>]` on `SourcePosition` automatically gets structural equality. No `[<StructuralEquality>]` attribute needed.

## Review Guidance

- Verify all 22 FRs from the spec have corresponding types
- Check that `StateKind` has exactly 9 cases
- Check that `GroupKind` has exactly 7 cases
- Check that `Annotation` has exactly 5 format cases
- Verify `SourcePosition` is `[<Struct>]`
- Verify `ParseFailure.Position` and `ParseWarning.Position` are `SourcePosition option`
- Verify no mutable fields on any record type
- Run `dotnet build` for multi-target success

## Activity Log

- 2026-03-15T23:59:08Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:25:01Z – unknown – lane=done – Moved to done
