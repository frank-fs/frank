---
work_package_id: WP04
title: Frank.Cli.Core — Extraction Deduplication & State
lane: "doing"
dependencies: []
base_branch: master
base_commit: 0fa8b1a8903680cb246a4f2e6284671457a1e054
created_at: '2026-03-06T18:55:23.080589+00:00'
subtasks:
- T016
- T017
- T018
- T019
- T020
- T021
- T022
phase: Phase 1 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "88552"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-06T15:25:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-002, FR-010, FR-012, FR-018]
---

# Work Package Prompt: WP04 – Frank.Cli.Core — Extraction Deduplication & State

## ⚠️ IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP04
```

No dependencies — this is an independent module package.

---

## Objectives & Success Criteria

- URI construction helpers (`classUri`, `propertyUri`, `resourceUri`, `routeToSlug`, `fieldKindToRange`) defined exactly once in `UriHelpers.fs` (Constitution VIII)
- `TypeMapper`, `ShapeGenerator`, `RouteMapper`, `CapabilityMapper` reference shared helpers
- `ExtractionState.SourceMap` uses `Map<string, SourceLocation>` (not `Dictionary<Uri, SourceLocation>`)
- Dead `scope` parameter removed from `ExtractCommand`
- All existing CLI tests pass

## Context & Constraints

- **Tracking Issue**: #81 — Tier 2 (duplicated helpers, mutable state, null safety) + Tier 3 (dead parameter)
- **Constitution**: Principle VIII (No Duplicated Logic)
- **Research**: R5 (ExtractionState type change)
- **Data Model**: See `data-model.md` — UriHelpers function signatures and ExtractionState migration
- **Key insight**: F# compilation order matters — `UriHelpers.fs` must appear before all consumers in `.fsproj`

## Subtasks & Detailed Guidance

### Subtask T016 – Create UriHelpers.fs Shared Module

- **Purpose**: 5 URI construction helpers are duplicated across 4 extraction modules. Constitution VIII requires single definition.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Extraction/UriHelpers.fs`
  2. Define the module with all shared helpers:
     ```fsharp
     module Frank.Cli.Core.Extraction.UriHelpers

     /// Create class URI: {baseUri}/types/{typeName}
     let classUri (baseUri: string) (typeName: string) =
         sprintf "%s/types/%s" baseUri typeName

     /// Create property URI: {baseUri}/properties/{typeName}/{fieldName}
     let propertyUri (baseUri: string) (typeName: string) (fieldName: string) =
         sprintf "%s/properties/%s/%s" baseUri typeName fieldName

     /// Remove route decorations (/, {, }) to create a slug
     let routeToSlug (routeTemplate: string) =
         routeTemplate.Replace("/", "").Replace("{", "").Replace("}", "")

     /// Create resource URI: {baseUri}/resources/{slug}
     let resourceUri (baseUri: string) (routeTemplate: string) =
         sprintf "%s/resources/%s" baseUri (routeToSlug routeTemplate)

     /// Map F# FieldKind to XSD range URI and isObjectProperty flag
     let fieldKindToRange (fieldKind: FieldKind) : string * bool =
         // Copy the canonical implementation from TypeMapper.fs
         // Returns (xsdUri, isObjectProperty)
         match fieldKind with
         | ...
     ```
  3. Copy the implementations from `TypeMapper.fs` (the canonical source) — do not modify behavior
  4. Add `UriHelpers.fs` to `Frank.Cli.Core.fsproj` BEFORE `TypeMapper.fs`, `ShapeGenerator.fs`, `RouteMapper.fs`, `CapabilityMapper.fs` in the `<Compile Include>` list
  5. Build to verify: `dotnet build src/Frank.Cli.Core/`
- **Files**:
  - `src/Frank.Cli.Core/Extraction/UriHelpers.fs` (new)
  - `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (add compile reference)
- **Parallel?**: No — must be done before T017-T020
- **Notes**: The `fieldKindToRange` function depends on the `FieldKind` type from `TypeAnalyzer.fs`. Ensure `TypeAnalyzer.fs` compiles before `UriHelpers.fs` in the `.fsproj`.

### Subtask T017 – Update TypeMapper.fs to Use UriHelpers

- **Purpose**: Remove duplicated `classUri`, `propertyUri`, `fieldKindToRange` from `TypeMapper.fs`.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Extraction/TypeMapper.fs`
  2. Remove local definitions:
     - `classUri` (line ~32-33)
     - `propertyUri` (line ~35-36)
     - `fieldKindToRange` (line ~38-53)
  3. Add `open Frank.Cli.Core.Extraction.UriHelpers` at the top
  4. Verify all call sites still compile with the shared versions
  5. Build: `dotnet build src/Frank.Cli.Core/`
- **Files**: `src/Frank.Cli.Core/Extraction/TypeMapper.fs`
- **Parallel?**: Depends on T016

### Subtask T018 – Update ShapeGenerator.fs to Use UriHelpers

- **Purpose**: Remove duplicated `classUri`, `propertyUri`, `fieldKindToRange` from `ShapeGenerator.fs`.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs`
  2. Remove local definitions:
     - `classUri` (line ~15-16) — IDENTICAL to TypeMapper
     - `propertyUri` (line ~18-19) — IDENTICAL to TypeMapper
     - `fieldKindToRange` (line ~21-36) — IDENTICAL to TypeMapper
  3. Keep `shapeUri` (line ~12-13) — this is unique to ShapeGenerator
  4. Add `open Frank.Cli.Core.Extraction.UriHelpers`
  5. Build: `dotnet build src/Frank.Cli.Core/`
- **Files**: `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs`
- **Parallel?**: Yes — can proceed in parallel with T019, T020 (after T016)

### Subtask T019 – Update RouteMapper.fs to Use UriHelpers

- **Purpose**: Remove duplicated `routeToSlug`, `resourceUri` from `RouteMapper.fs`.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Extraction/RouteMapper.fs`
  2. Remove local definitions:
     - `routeToSlug` (line ~20-21) — IDENTICAL to CapabilityMapper
     - `resourceUri` (line ~23-26) — IDENTICAL to CapabilityMapper
  3. Keep `uriTemplate` and `findLinkedClass` — these are unique to RouteMapper
  4. Add `open Frank.Cli.Core.Extraction.UriHelpers`
  5. Build: `dotnet build src/Frank.Cli.Core/`
- **Files**: `src/Frank.Cli.Core/Extraction/RouteMapper.fs`
- **Parallel?**: Yes — can proceed in parallel with T018, T020 (after T016)

### Subtask T020 – Update CapabilityMapper.fs to Use UriHelpers

- **Purpose**: Remove duplicated `routeToSlug`, `resourceUri` from `CapabilityMapper.fs`.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Extraction/CapabilityMapper.fs`
  2. Remove local definitions:
     - `routeToSlug` (line ~32-33)
     - `resourceUri` (line ~35-38)
  3. Keep `httpMethodToSchemaAction`, `httpMethodToString` — unique to CapabilityMapper
  4. Add `open Frank.Cli.Core.Extraction.UriHelpers`
  5. Build: `dotnet build src/Frank.Cli.Core/`
- **Files**: `src/Frank.Cli.Core/Extraction/CapabilityMapper.fs`
- **Parallel?**: Yes — can proceed in parallel with T018, T019 (after T016)

### Subtask T021 – Migrate ExtractionState.SourceMap to Map

- **Purpose**: `Dictionary<Uri, SourceLocation>` is mutable inside an immutable F# record and has surprising `Uri` equality semantics. Replace with `Map<string, SourceLocation>`.
- **Steps**:
  1. Open `src/Frank.Cli.Core/State/ExtractionState.fs`
  2. Change the `SourceMap` field type:
     - From: `SourceMap: Dictionary<Uri, SourceLocation>`
     - To: `SourceMap: Map<string, SourceLocation>`
  3. Update the `save` function:
     - Serialize `Map<string, SourceLocation>` directly (keys are already strings)
  4. Update the `load` function:
     - Handle backward compatibility: if existing `state.json` has Uri-formatted keys, convert via `Uri.ToString()` → string
     - Parse both old format (Uri keys) and new format (string keys) gracefully
  5. Update all call sites that construct or query the `SourceMap`:
     - Replace `dict.[uri]` with `Map.find key` or `Map.tryFind key`
     - Replace `dict.Add(uri, loc)` with `Map.add key loc`
     - Replace `Dictionary<Uri, SourceLocation>()` with `Map.empty`
  6. Build and test: `dotnet build src/Frank.Cli.Core/ && dotnet test test/Frank.Cli.Core.Tests/`
- **Files**:
  - `src/Frank.Cli.Core/State/ExtractionState.fs` (primary)
  - Any files that construct or query `ExtractionState.SourceMap`
- **Parallel?**: Yes — independent of T016-T020
- **Notes**: The migration must be backward-compatible. The `load` function should detect the old format and convert. Use `Uri.ToString()` to produce the string key from old Uri keys.

### Subtask T022 – Remove Dead scope Parameter from ExtractCommand

- **Purpose**: `ExtractCommand` accepts a `scope` parameter that is never used. Dead code should be removed.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Commands/ExtractCommand.fs`
  2. Find the `scope` parameter in the `execute` function signature
  3. Remove it from the function signature
  4. Open `src/Frank.Cli/Program.fs`
  5. Find the `extract` subcommand definition
  6. Remove the `--scope` option from the `System.CommandLine` argument definition
  7. Update the call site where `ExtractCommand.execute` is invoked to remove the scope argument
  8. Build: `dotnet build src/Frank.Cli/`
- **Files**:
  - `src/Frank.Cli.Core/Commands/ExtractCommand.fs`
  - `src/Frank.Cli/Program.fs`
- **Parallel?**: Yes — independent of T016-T021
- **Notes**: This is a breaking CLI change (the `--scope` flag will no longer be accepted). Since this is an internal tool, this is acceptable.

## Risks & Mitigations

- **Compilation order**: F# requires files to appear in dependency order in `.fsproj`. `UriHelpers.fs` must come after `TypeAnalyzer.fs` (for `FieldKind` type) and before all consumer modules.
- **Backward compatibility**: `ExtractionState` load must handle both old and new formats. Include a test for this.
- **Dead parameter removal**: CLI users using `--scope` will get an error. Document in release notes.

## Review Guidance

- Run `grep -rn "let classUri\|let propertyUri\|let resourceUri\|let routeToSlug\|let fieldKindToRange" src/Frank.Cli.Core/` — each should appear exactly once (in `UriHelpers.fs`)
- Verify `ExtractionState.SourceMap` is `Map<string, SourceLocation>` — no `Dictionary` or `Uri` keys
- Verify `scope` parameter is completely removed from CLI
- Verify compilation order in `.fsproj` is correct

## Activity Log

- 2026-03-06T15:25:00Z – system – lane=planned – Prompt created.
- 2026-03-06T18:55:23Z – claude-opus – shell_pid=88552 – lane=doing – Assigned agent via workflow command
