---
work_package_id: "WP01"
title: "Extend AlpsMeta DU"
lane: "done"
dependencies: []
requirement_refs:
  - "FR-007"
subtasks:
  - "T001"
  - "T002"
  - "T003"
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

# Work Package Prompt: WP01 -- Extend AlpsMeta DU

## Important: Review Feedback Status

- **Has review feedback?**: Check the `review_status` field above.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Objectives & Success Criteria

- Extend the `AlpsMeta` discriminated union in `src/Frank.Statecharts/Ast/Types.fs` with 4 new cases and expand the existing `AlpsExtension` case.
- All existing code that pattern-matches on `AlpsExtension` compiles with the updated signature.
- `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds with no errors across all three target frameworks.
- Existing tests continue to pass (no behavioral change, only type additions).

## Context & Constraints

- **Spec**: `kitty-specs/023-alps-shared-ast-migration/spec.md` (FR-007)
- **Plan**: `kitty-specs/023-alps-shared-ast-migration/plan.md` (Phase 1: Design & Contracts)
- **Data Model**: `kitty-specs/023-alps-shared-ast-migration/data-model.md` (full field specifications)
- **Research**: `kitty-specs/023-alps-shared-ast-migration/research.md` (D-001, D-002)
- **Key Decision D-001**: Expand `AlpsExtension` to 3 fields: `id: string * href: string option * value: string option`
- **Key Decision D-002**: Add 4 new `AlpsMeta` cases: `AlpsDocumentation`, `AlpsLink`, `AlpsDataDescriptor`, `AlpsVersion`
- `AlpsMeta` is defined in an `internal` namespace (`Frank.Statecharts.Ast`), visible via `InternalsVisibleTo` to the test project only.

## Implementation Command

```bash
spec-kitty implement WP01
```

## Subtasks & Detailed Guidance

### Subtask T001 -- Extend `AlpsMeta` DU in `Ast/Types.fs`

- **Purpose**: Add the annotation cases needed for full-fidelity ALPS roundtripping. Without these, the parser and generator cannot preserve ALPS-specific metadata through the shared AST.

- **File**: `src/Frank.Statecharts/Ast/Types.fs` (lines 78-81)

- **Steps**:
  1. Open `src/Frank.Statecharts/Ast/Types.fs` and locate the `AlpsMeta` type (currently lines 78-81).
  2. Change the existing `AlpsExtension` case from:
     ```fsharp
     | AlpsExtension of name: string * value: string
     ```
     To:
     ```fsharp
     | AlpsExtension of id: string * href: string option * value: string option
     ```
  3. Add 4 new cases after `AlpsExtension`:
     ```fsharp
     | AlpsDocumentation of format: string option * value: string
     | AlpsLink of rel: string * href: string
     | AlpsDataDescriptor of id: string * doc: (string option * string) option
     | AlpsVersion of string
     ```
  4. Update the doc comment above `AlpsMeta` to remove the "stub" language and reflect the full set of cases.

- **Target shape** (complete):
  ```fsharp
  /// ALPS-specific annotation metadata.
  type AlpsMeta =
      | AlpsTransitionType of AlpsTransitionKind
      | AlpsDescriptorHref of string
      | AlpsExtension of id: string * href: string option * value: string option
      | AlpsDocumentation of format: string option * value: string
      | AlpsLink of rel: string * href: string
      | AlpsDataDescriptor of id: string * doc: (string option * string) option
      | AlpsVersion of string
  ```

- **Notes**:
  - The `AlpsDataDescriptor` doc field is `(string option * string) option` where the inner tuple is `(format option, value)`. `None` means the data descriptor has no documentation.
  - Keep `AlpsTransitionType` and `AlpsDescriptorHref` unchanged -- they are already correct.

### Subtask T002 -- Update existing `AlpsExtension` pattern matches

- **Purpose**: The `AlpsMeta.AlpsExtension` DU case (in `Ast/Types.fs`) changed from 2 fields to 3 fields. Any code that pattern-matches on it must be updated.

- **Important distinction**: `Alps.Types.AlpsExtension` is a record type `{ Id: string; Href: string option; Value: string option }` which already has 3 fields and does NOT need updating. `AlpsMeta.AlpsExtension` is the DU case in `Ast/Types.fs` which is the one that changed shape in T001. These are different types in different modules. Only `AlpsMeta.AlpsExtension` (the DU case) is affected by this task.

- **Steps**:
  1. Search for all pattern matches on `AlpsMeta.AlpsExtension` (the DU case, NOT the record type) across the entire `src/` and `test/` trees.
  2. The only references to `AlpsMeta.AlpsExtension` (as opposed to `Alps.Types.AlpsExtension` which is a separate record type) are:
     - No pattern matches exist in `src/` outside `Alps/Mapper.fs` (which will be deleted in WP04).
     - No pattern matches exist in `test/` (tests reference `Alps.Types.AlpsExtension` record, not `AlpsMeta.AlpsExtension` DU case).
  3. The `Alps/Mapper.fs` file does not pattern-match on `AlpsMeta.AlpsExtension` -- it uses `Alps.Types.AlpsExtension` records. So no updates are needed in Mapper.fs for this specific change.
  4. Verify the build succeeds: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`

- **Files to check**:
  - `src/Frank.Statecharts/Validation/` -- does NOT reference `AlpsMeta` (confirmed by grep)
  - `src/Frank.Statecharts/Alps/Mapper.fs` -- references `Alps.Types.AlpsExtension` (record), not `AlpsMeta.AlpsExtension` (DU case)

### Subtask T003 -- Verify no external references to `AlpsMeta.AlpsExtension`

- **Purpose**: Confirm that the field rename does not break any code outside the Alps module.

- **Steps**:
  1. Run: `grep -r "AlpsExtension" src/ test/` and review every match.
  2. Categorize each match as:
     - `Alps.Types.AlpsExtension` (record type) -- unchanged, no action needed
     - `AlpsMeta.AlpsExtension` (DU case) -- must use new 3-field signature
  3. Confirm zero matches need updating (based on research, this is the case).
  4. Run `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` and confirm zero errors.
  5. Run `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` and confirm all existing tests pass.

## Risks & Mitigations

- **Risk**: Other modules may have wildcard matches on `AlpsMeta` that would break with new cases.
  - **Mitigation**: New DU cases only cause issues if a match is non-exhaustive and the compiler enforces exhaustiveness. Since F# requires wildcard or exhaustive matches, and existing code uses wildcards for `AlpsMeta`, this is safe.
- **Risk**: Field rename from `name` to `id` could break code using named field syntax.
  - **Mitigation**: Grep confirms no code uses named field syntax for `AlpsMeta.AlpsExtension`. All existing usage is in positional syntax inside `Alps/Mapper.fs` which is deleted in WP04.

## Review Guidance

- Verify the `AlpsMeta` type matches the target shape exactly (field names, types, order).
- Verify `dotnet build` passes for all 3 target frameworks.
- Verify existing tests still pass.

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T23:01:00Z -- claude-opus -- lane=done -- Review APPROVED: All 3 subtasks verified. T001: AlpsMeta DU extended with 4 new cases (AlpsDocumentation, AlpsLink, AlpsDataDescriptor, AlpsVersion) and AlpsExtension expanded from (name, value) to (id, href option, value option). Type matches target shape exactly. T002: No pattern match updates needed -- grep confirms no code pattern-matches on AlpsMeta.AlpsExtension (only Alps.Types.AlpsExtension record). T003: Build passes 0 errors across net8.0/net9.0/net10.0; all 828 tests pass. Stub comment removed from doc. Commit 9e9e71f.
