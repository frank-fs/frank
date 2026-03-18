---
work_package_id: WP01
title: Extract Shared Classification Module
lane: "doing"
dependencies: []
base_branch: master
base_commit: a631caa8f67d215172a83b8feb7e0faacce0b5a2
created_at: '2026-03-18T14:31:00.726756+00:00'
subtasks: [T001, T002, T003, T004, T005]
phase: Phase 0 - Foundation
assignee: ''
agent: "claude-opus-reviewer"
shell_pid: "71665"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-18T14:14:54Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-005, FR-008, FR-010, FR-011]
---

# Work Package Prompt: WP01 – Extract Shared Classification Module

## Review Feedback
*[Empty initially.]*

---

## Implementation Command
```bash
spec-kitty implement WP01
```

## Objectives & Success Criteria

- Create `src/Frank.Statecharts/Alps/Classification.fs` with shared intermediate types and Pass 2 heuristics
- Refactor `Alps/JsonParser.fs` to import from Classification instead of defining types privately
- `dotnet build` and `dotnet test` pass — JsonParser behavior completely unchanged
- Constitution Principle VIII satisfied (no duplicated logic)

## Context & Constraints

- **Spec**: FR-005 (XML parser uses same classification), FR-008 (identical ASTs)
- **Research**: R-001 (shared classification module design)
- **Constitution**: Principle VIII — no duplicated logic across modules
- **File**: `src/Frank.Statecharts/Alps/JsonParser.fs` (392 lines) — source of code to extract

## Subtasks & Detailed Guidance

### T001 – Create Classification.fs with intermediate types

- **Purpose**: Define shared intermediate types used by both JSON and XML parsers.
- **File**: `src/Frank.Statecharts/Alps/Classification.fs` (NEW)
- **Steps**:
  1. Create the file with module declaration:
     ```fsharp
     module internal Frank.Statecharts.Alps.Classification

     open Frank.Statecharts.Ast
     ```
  2. Move these types from `JsonParser.fs` (currently `private`, make `internal`):
     ```fsharp
     /// Intermediate type for parsing pass — shared between JSON and XML parsers.
     type ParsedDescriptor =
         { Id: string option
           Type: string option
           Href: string option
           ReturnType: string option
           DocFormat: string option
           DocValue: string option
           Children: ParsedDescriptor list
           Extensions: ParsedExtension list
           Links: ParsedLink list }

     and ParsedExtension =
         { Id: string
           Href: string option
           Value: string option }

     and ParsedLink =
         { Rel: string
           Href: string }
     ```
  3. Note: remove `private` modifier — these become `internal` (visible within the assembly).

### T002 – Move classification functions

- **Purpose**: Extract Pass 2 heuristics so both parsers can reuse them.
- **File**: `src/Frank.Statecharts/Alps/Classification.fs`
- **Steps**:
  1. Move these functions from `JsonParser.fs` to `Classification.fs`, changing `private` to no access modifier (they'll be `internal` by virtue of the module being `internal`):
     - `isTransitionTypeStr`
     - `collectRtTargets`
     - `isStateDescriptor`
     - `buildDescriptorIndex`
     - `resolveRt`
     - `extractGuard`
     - `extractParameters`
     - `toTransitionKind`
     - `buildStateAnnotations`
     - `buildTransitionAnnotations`
     - `resolveDescriptor`
     - `extractTransitions`
     - `toStateNode`
  2. Each function should keep its existing signature and logic — this is a pure move, not a refactor.
  3. Add a high-level classification function that both parsers can call:
     ```fsharp
     /// Classify parsed descriptors into a StatechartDocument.
     /// This is Pass 2 of the ALPS parsing pipeline, shared between JSON and XML parsers.
     let classifyDescriptors
         (descriptors: ParsedDescriptor list)
         (version: string option)
         (rootDocFormat: string option)
         (rootDocValue: string option)
         (rootLinks: ParsedLink list)
         (rootExtensions: ParsedExtension list)
         : StatechartDocument =
         // ... existing Pass 2 logic from JsonParser.fs lines 301-368
     ```

### T003 – Refactor JsonParser.fs imports

- **Purpose**: JsonParser.fs should use the shared module instead of its own definitions.
- **File**: `src/Frank.Statecharts/Alps/JsonParser.fs`
- **Steps**:
  1. Add `open Frank.Statecharts.Alps.Classification` at the top.
  2. Remove the moved type definitions and functions (they're now in Classification.fs).
  3. Keep only Pass 1 JSON-specific code: `tryGetString`, `tryGetArray`, `parseExtension`, `parseLink`, `parseDescriptor`, `emptyDoc`, `parseAlpsJson`.
  4. In `parseAlpsJson`, replace the inline Pass 2 code (lines 301-368) with a call to `classifyDescriptors`.
  5. The file should shrink from ~392 lines to ~120-150 lines.

### T004 – Update .fsproj compilation order

- **Purpose**: F# requires files in dependency order. Classification.fs must compile before JsonParser.fs.
- **File**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Steps**:
  1. Find the `<Compile Include="Alps/JsonParser.fs" />` entry.
  2. Add `<Compile Include="Alps/Classification.fs" />` BEFORE it.

### T005 – Verify build and tests

- **Steps**: `dotnet build` and `dotnet test` — all must pass. JsonParser behavior must be completely unchanged.

## Risks & Mitigations

- **Compilation order**: F# is order-dependent. If Classification.fs appears after JsonParser.fs, build fails. Verify .fsproj ordering.
- **Access modifier change**: Types go from `private` to `internal`. This is fine — the assembly is internal anyway. But verify no naming conflicts with other modules.
- **Function signatures**: Ensure all moved functions keep exact same signatures. This is a refactor, not a rewrite.

## Review Guidance

- Verify `Classification.fs` contains ALL shared types and functions (none left private in JsonParser.fs)
- Verify `JsonParser.fs` imports from Classification and has no duplicated definitions
- Verify `.fsproj` has correct compilation order
- Verify all existing ALPS tests pass unchanged — this is a pure refactor
- Run `dotnet test` — all green

## Activity Log

- 2026-03-18T14:14:54Z – system – lane=planned – Prompt created.
- 2026-03-18T14:31:00Z – claude-opus – shell_pid=71187 – lane=doing – Assigned agent via workflow command
- 2026-03-18T14:33:52Z – claude-opus – shell_pid=71187 – lane=for_review – Pure refactor: Classification.fs created, JsonParser.fs reduced. 869 tests pass.
- 2026-03-18T14:34:00Z – claude-opus-reviewer – shell_pid=71665 – lane=doing – Started review via workflow command
