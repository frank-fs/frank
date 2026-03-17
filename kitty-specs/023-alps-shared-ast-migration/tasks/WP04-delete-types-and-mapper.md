---
work_package_id: "WP04"
title: "Delete Format-Specific Types and Mapper"
lane: "done"
dependencies: ["WP02", "WP03"]
requirement_refs:
  - "FR-018"
  - "FR-019"
subtasks:
  - "T013"
  - "T014"
  - "T015"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "claude-opus"
history:
  - timestamp: "2026-03-16T19:13:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP04 -- Delete Format-Specific Types and Mapper

## Important: Review Feedback Status

- **Has review feedback?**: Check the `review_status` field above.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Objectives & Success Criteria

- `Alps/Types.fs` is deleted (contains `AlpsDocument`, `Descriptor`, `DescriptorType`, `AlpsParseError`, `AlpsSourcePosition`, `AlpsDocumentation`, `AlpsExtension`, `AlpsLink`).
- `Alps/Mapper.fs` is deleted (contains `toStatechartDocument`, `fromStatechartDocument`).
- `Frank.Statecharts.fsproj` no longer references these files.
- `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds across all three target frameworks (net8.0, net9.0, net10.0).
- No source file in `src/` references deleted types (`grep -r "AlpsDocument\|Alps\.Types\|Alps\.Mapper" src/` returns zero matches).

## Context & Constraints

- **Spec**: `kitty-specs/023-alps-shared-ast-migration/spec.md` (FR-018, FR-019, User Story 4)
- **Plan**: `kitty-specs/023-alps-shared-ast-migration/plan.md` (Compilation Order Changes)
- **Prerequisites**: WP02 (parser no longer imports `Alps.Types`) and WP03 (generator no longer imports `Alps.Types`) must be complete.
- After this WP, `JsonParser.fs` should only `open Frank.Statecharts.Ast` and `System.Text.Json`. `JsonGenerator.fs` should only `open Frank.Statecharts.Ast`, `System.IO`, `System.Text`, and `System.Text.Json`.

## Implementation Command

```bash
spec-kitty implement WP04 --base WP03
```

## Subtasks & Detailed Guidance

### Subtask T013 -- Delete `Alps/Types.fs`

- **Purpose**: Remove the format-specific types that are no longer referenced. This is the proof that the parser and generator have been fully migrated to the shared AST.

- **File to delete**: `src/Frank.Statecharts/Alps/Types.fs`

- **Steps**:
  1. Verify that `JsonParser.fs` does NOT contain `open Frank.Statecharts.Alps.Types` (it should have been removed in WP02). If it still has this import, remove it now.
  2. Verify that `JsonGenerator.fs` does NOT contain `open Frank.Statecharts.Alps.Types` (it should have been removed in WP03). If it still has this import, remove it now.
  3. Delete the file: `rm src/Frank.Statecharts/Alps/Types.fs`
  4. Verify no other source files reference the deleted module:
     ```bash
     grep -r "Alps\.Types\|open.*Alps\.Types" src/
     ```
     Expected: zero matches (Mapper.fs also references it but is deleted in T014).

- **Types being deleted**: `AlpsSourcePosition`, `AlpsParseError`, `AlpsDocumentation`, `AlpsExtension` (record type), `AlpsLink` (record type), `DescriptorType`, `Descriptor`, `AlpsDocument`.

### Subtask T014 -- Delete `Alps/Mapper.fs`

- **Purpose**: Remove the bridge module that is no longer needed. The parser now does what `toStatechartDocument` did, and the generator now does what `fromStatechartDocument` did.

- **File to delete**: `src/Frank.Statecharts/Alps/Mapper.fs`

- **Steps**:
  1. Verify no source files reference the mapper:
     ```bash
     grep -r "Alps\.Mapper\|open.*Alps\.Mapper\|toStatechartDocument\|fromStatechartDocument" src/
     ```
     Expected: matches only in `Alps/Mapper.fs` itself (which is being deleted).
  2. Delete the file: `rm src/Frank.Statecharts/Alps/Mapper.fs`

### Subtask T015 -- Update `Frank.Statecharts.fsproj`

- **Purpose**: Remove the deleted files from the F# compilation list. Without this, the build will fail looking for missing files.

- **File**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`

- **Steps**:
  1. Open the project file and locate the ALPS section (currently lines 31-35):
     ```xml
     <!-- ALPS parser -->
     <Compile Include="Alps/Types.fs" />
     <Compile Include="Alps/JsonParser.fs" />
     <Compile Include="Alps/JsonGenerator.fs" />
     <Compile Include="Alps/Mapper.fs" />
     ```
  2. Remove the two deleted files:
     ```xml
     <!-- ALPS parser -->
     <Compile Include="Alps/JsonParser.fs" />
     <Compile Include="Alps/JsonGenerator.fs" />
     ```
  3. Save the file.

  4. Verify the build succeeds:
     ```bash
     dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj
     ```
     This should succeed for all three target frameworks (net8.0, net9.0, net10.0).

  5. Verify no lingering references:
     ```bash
     grep -r "AlpsDocument\|Alps\.Types\|Alps\.Mapper\|DescriptorType\|AlpsParseError" src/
     ```
     Expected: zero matches.

## Risks & Mitigations

- **Risk**: A source file still references a deleted type.
  - **Mitigation**: Run grep verification before and after deletion. The build will also fail immediately if any reference remains.
- **Risk**: Compilation order change causes issues.
  - **Mitigation**: `JsonParser.fs` comes before `JsonGenerator.fs` in the compile list, which is correct (generator may depend on parser types, not vice versa). No other ordering constraints exist after deletion.

## Review Guidance

- Verify both files are deleted (not just emptied).
- Verify the project file has exactly two ALPS entries: `Alps/JsonParser.fs` and `Alps/JsonGenerator.fs`.
- Verify `dotnet build` succeeds for all three target frameworks.
- Verify grep finds zero references to deleted types across `src/`.

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-17T04:30:00Z -- claude-opus -- lane=done -- Review APPROVED. All 9 checklist items pass: Types.fs deleted (52 lines), Mapper.fs deleted (286 lines), fsproj has exactly 2 ALPS entries, zero references to deleted types in src/, build succeeds on net8.0/net9.0/net10.0 with 0 warnings, stale Alps.Types import in JsonParser.fs correctly cleaned up. Commit 797d3b1.
