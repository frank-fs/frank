---
work_package_id: WP01
title: XState Serializer & Project Visibility
lane: "doing"
dependencies: []
base_branch: master
base_commit: 8c9e0df6d0e6825253965765f97d6fd8da81e27a
created_at: '2026-03-16T22:51:41.142113+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
- T006
assignee: ''
agent: "claude-opus-4-6"
shell_pid: "25918"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-16T19:12:54Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
---

# Work Package Prompt: WP01 -- XState Serializer & Project Visibility

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

No dependencies -- start from feature branch base:

```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

1. Add `InternalsVisibleTo` to `Frank.Statecharts.fsproj` so that `Frank.Cli.Core` can access internal modules (generators, parsers, mappers).
2. Add a project reference from `Frank.Cli.Core` to `Frank.Statecharts`.
3. Create an XState v5 JSON serializer (`StatechartDocument` -> XState JSON string).
4. Create an XState v5 JSON deserializer (XState JSON string -> `Ast.ParseResult`).
5. Solution builds cleanly with `dotnet build`.

**Success**: `dotnet build` passes with all new files compiled. XState modules follow the same internal module pattern as existing format modules.

---

## Context & Constraints

- **Spec**: `kitty-specs/026-cli-statechart-commands/spec.md` (FR-017, FR-018)
- **Plan**: `kitty-specs/026-cli-statechart-commands/plan.md` (D-004, D-005)
- **Key decision D-004**: Use `InternalsVisibleTo` rather than making modules public.
- **Key decision D-005**: XState goes in `src/Frank.Statecharts/XState/` subdirectory.
- **Existing patterns**: See `src/Frank.Statecharts/Alps/` (JsonGenerator.fs, JsonParser.fs, Mapper.fs) for format module conventions.
- **AST types**: `src/Frank.Statecharts/Ast/Types.fs` -- `StatechartDocument`, `ParseResult`, `StateNode`, `TransitionEdge`, etc.

---

## Subtasks & Detailed Guidance

### Subtask T001 -- Add InternalsVisibleTo to Frank.Statecharts.fsproj

- **Purpose**: Allow `Frank.Cli.Core` to access `internal` modules in `Frank.Statecharts` (generators, parsers, serializers, mappers).
- **Steps**:
  1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`
  2. In the existing `<ItemGroup>` that contains `<InternalsVisibleTo Include="Frank.Statecharts.Tests" />`, add:
     ```xml
     <InternalsVisibleTo Include="Frank.Cli.Core" />
     ```
- **Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Notes**: This is a single-line addition. The existing `InternalsVisibleTo` for tests is already present.

### Subtask T002 -- Add Frank.Statecharts project reference to Frank.Cli.Core.fsproj

- **Purpose**: Enable `Frank.Cli.Core` to reference types and modules from `Frank.Statecharts`.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
  2. Add a new `<ItemGroup>` with the project reference:
     ```xml
     <ItemGroup>
       <ProjectReference Include="../Frank.Statecharts/Frank.Statecharts.fsproj" />
     </ItemGroup>
     ```
  3. Also add the `FrameworkReference` for `Microsoft.AspNetCore.App` if not already present (needed for types like `HttpContext`, `RequestDelegate` used by `StateMachineMetadata`):
     ```xml
     <ItemGroup>
       <FrameworkReference Include="Microsoft.AspNetCore.App" />
     </ItemGroup>
     ```
- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
- **Notes**: The `Microsoft.AspNetCore.App` framework reference is needed because `StateMachineMetadata` contains `RequestDelegate` and `HttpContext` types.

### Subtask T003 -- Create XState Serializer.fs

- **Purpose**: Serialize a `StatechartDocument` AST to XState v5 JSON format (FR-017).
- **Steps**:
  1. Create `src/Frank.Statecharts/XState/Serializer.fs`
  2. Module declaration: `module internal Frank.Statecharts.XState.Serializer`
  3. Open required namespaces: `Frank.Statecharts.Ast`, `System.IO`, `System.Text`, `System.Text.Json`
  4. Implement `serialize (document: StatechartDocument) : string`

  **XState v5 JSON schema** (flat states only):
  ```json
  {
    "id": "<title or 'statechart'>",
    "initial": "<initialStateId>",
    "states": {
      "<stateName>": {
        "on": {
          "<eventName>": "<targetState>"
        },
        "type": "final",
        "meta": {
          "description": "<label>"
        }
      }
    }
  }
  ```

  5. Implementation details:
     - Extract all `StateNode` values from `document.Elements` (filter for `StateDecl`)
     - Extract all `TransitionEdge` values from `document.Elements` (filter for `TransitionElement`)
     - For each state, collect its transitions (where `Source = state.Identifier`)
     - Group transitions by event name, map to target state
     - If `StateKind = Final`, add `"type": "final"`
     - Use `Utf8JsonWriter` with `JsonWriterOptions(Indented = true)` for output
     - `id` from `document.Title |> Option.defaultValue "statechart"`
     - `initial` from `document.InitialStateId |> Option.defaultValue ""`

- **Files**: `src/Frank.Statecharts/XState/Serializer.fs` (NEW, ~80-120 lines)
- **Parallel?**: Yes -- can proceed alongside T004.
- **Notes**: Follow the `Utf8JsonWriter` pattern from `Alps.JsonGenerator.generateAlpsJson`. Only handle flat states (no nested/parallel). Self-transitions (where `Source = Target`) should still be included in the `on` map.

### Subtask T004 -- Create XState Deserializer.fs

- **Purpose**: Deserialize XState v5 JSON text to the shared `Ast.ParseResult` (FR-018).
- **Steps**:
  1. Create `src/Frank.Statecharts/XState/Deserializer.fs`
  2. Module declaration: `module internal Frank.Statecharts.XState.Deserializer`
  3. Open required namespaces: `Frank.Statecharts.Ast`, `System.Text.Json`
  4. Implement `deserialize (json: string) : ParseResult`

  5. Implementation details:
     - Use `JsonDocument.Parse(json)` wrapped in try/with for parse errors
     - Extract `id` -> `Title`
     - Extract `initial` -> `InitialStateId`
     - Iterate `states` object properties:
       - Each property name is a state identifier -> create `StateNode`
       - Check for `"type": "final"` -> set `Kind = Final`
       - Check for `"meta"."description"` -> set `Label`
       - Iterate `on` object properties:
         - Each property name is an event, value is target state -> create `TransitionEdge`
     - Assemble `StatechartDocument` from collected nodes and edges
     - Return `ParseResult` with `Document`, `Errors` (from malformed JSON), `Warnings` (empty)

  6. Error handling:
     - `JsonException` -> `ParseFailure` with description, empty position
     - Missing `states` property -> warning, return empty document
     - State with no `on` property -> valid (terminal state)

- **Files**: `src/Frank.Statecharts/XState/Deserializer.fs` (NEW, ~100-150 lines)
- **Parallel?**: Yes -- can proceed alongside T003.
- **Notes**: Use `JsonDocument` (read-only DOM) rather than `JsonSerializer` for fine-grained control. Follow error reporting patterns from `Alps.JsonParser.fs`.

### Subtask T005 -- Add XState compile entries to Frank.Statecharts.fsproj

- **Purpose**: Register the new XState source files in the project's compile order.
- **Steps**:
  1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`
  2. Add the following entries in the `<ItemGroup>` with `<Compile>` entries, **after** the SCXML entries and **before** the Validation entries:
     ```xml
     <!-- XState serializer -->
     <Compile Include="XState/Serializer.fs" />
     <Compile Include="XState/Deserializer.fs" />
     ```
  3. The Serializer must come before Deserializer since Deserializer may reference serializer types.

- **Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Notes**: F# compile order matters. XState modules depend on `Ast/Types.fs` (already compiled earlier) and `Types.fs` (already compiled earlier). They must be compiled before `Validation/` modules since validation uses `FormatTag.XState`.

### Subtask T006 -- Verify solution builds

- **Purpose**: Confirm all changes compile cleanly.
- **Steps**:
  1. Run `dotnet build` from the repository root
  2. Fix any compilation errors
  3. Verify no warnings related to new modules
- **Files**: N/A
- **Notes**: This is a gating step. Do not proceed to WP02 until the build is clean.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| XState v5 schema complexity (parallel/compound states) | Implement flat-state mapping only. Document limitation. |
| Compile order issues in fsproj | Place XState after SCXML, before Validation. Test with `dotnet build`. |
| FrameworkReference conflict in Frank.Cli.Core | Frank.Cli.Core targets net10.0. Microsoft.AspNetCore.App is available. |

---

## Review Guidance

- Verify `InternalsVisibleTo` is correctly placed in `Frank.Statecharts.fsproj`.
- Verify XState Serializer produces valid XState v5 JSON for a simple statechart.
- Verify XState Deserializer handles malformed JSON gracefully with `ParseFailure` entries.
- Verify compile order in fsproj does not break existing modules.
- Verify `dotnet build` passes cleanly.

---

## Activity Log

- 2026-03-16T19:12:54Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T22:51:41Z – claude-opus-4-6 – shell_pid=25918 – lane=doing – Assigned agent via workflow command
