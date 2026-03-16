---
work_package_id: WP01
title: Foundation Types and Utilities
lane: "done"
dependencies: []
base_branch: master
base_commit: 68071d57fe1b9f4a7609eff52c062f851c88d506
created_at: '2026-03-16T04:02:27.447120+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
- T006
phase: Phase 1 - Foundation
assignee: ''
agent: "claude-opus-reviewer"
shell_pid: "3730"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-15T23:59:04Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-001
- FR-002
- FR-003
- FR-006
---

# Work Package Prompt: WP01 -- Foundation Types and Utilities

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

No dependencies -- this is the starting work package:

```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

1. All help system types are defined in `HelpTypes.fs` and compile successfully.
2. FuzzyMatch module provides Levenshtein distance computation and suggestion ranking.
3. StalenessChecker module is extracted from ValidateCommand with `computeFileHash` and `checkStaleness` functions.
4. ValidateCommand is refactored to use StalenessChecker (no duplicated logic -- Constitution Principle VIII).
5. `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` succeeds.
6. `dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj` passes (existing tests remain green).

## Context & Constraints

- **Spec**: `kitty-specs/016-frank-cli-help-system/spec.md` (FR-001 through FR-006 for content model)
- **Data Model**: `kitty-specs/016-frank-cli-help-system/data-model.md` (type definitions)
- **Research**: `kitty-specs/016-frank-cli-help-system/research.md` (R3: StalenessChecker extraction, R4: FuzzyMatch)
- **Plan**: `kitty-specs/016-frank-cli-help-system/plan.md` (file locations, compile order)
- **Constitution**: Principle VIII (No Duplicated Logic) requires extracting staleness detection to shared module.
- **Constitution**: Principle VI (Resource Disposal) requires `use` bindings on file streams in StalenessChecker.

## Subtasks & Detailed Guidance

### Subtask T001 -- Create HelpTypes.fs

**Purpose**: Define the content model types that all other help system modules depend on. These types form the backbone of the entire feature.

**Steps**:

1. Create `src/Frank.Cli.Core/Help/HelpTypes.fs` (new directory `Help/` under `Frank.Cli.Core`).

2. Use namespace `Frank.Cli.Core.Help`.

3. Define the following types exactly as specified in `data-model.md`:

```fsharp
type CommandExample =
    { Invocation: string
      Description: string }

type WorkflowPosition =
    { StepNumber: int
      Prerequisites: string list
      NextSteps: string list
      IsOptional: bool }

type CommandHelp =
    { Name: string
      Summary: string
      Examples: CommandExample list
      Workflow: WorkflowPosition
      Context: string }

type HelpTopic =
    { Name: string
      Summary: string
      Content: string }

type HelpLookupResult =
    | CommandMatch of CommandHelp
    | TopicMatch of HelpTopic
    | NoMatch of suggestions: string list
```

**Files**: `src/Frank.Cli.Core/Help/HelpTypes.fs` (new, ~45 lines)
**Parallel?**: Yes -- can proceed alongside T002 and T003.

---

### Subtask T002 -- Create FuzzyMatch.fs

**Purpose**: Implement Levenshtein distance for "did you mean?" suggestions when an unknown argument is passed to `frank-cli help`. With only 9 candidates (7 commands + 2 topics), a simple implementation is adequate.

**Steps**:

1. Create `src/Frank.Cli.Core/Help/FuzzyMatch.fs`.

2. Use namespace `Frank.Cli.Core.Help`.

3. Implement Levenshtein distance using a standard dynamic programming approach:

```fsharp
module FuzzyMatch =
    /// Compute the Levenshtein edit distance between two strings.
    let levenshteinDistance (a: string) (b: string) : int =
        // Standard DP matrix approach (O(n*m) time, O(n) space with single-row optimization)
        ...

    /// Find suggestions from a list of candidates, sorted by distance (closest first).
    /// Includes candidates where: distance <= maxDistance OR input is a prefix of candidate.
    let suggest (input: string) (candidates: string list) (maxDistance: int) : (string * int) list =
        ...
```

4. The `suggest` function should:
   - Compute Levenshtein distance between `input` (lowered) and each candidate (lowered).
   - Include candidates where distance <= `maxDistance` (default 3).
   - Also include candidates where `input` is a prefix of the candidate (e.g., "ext" matches "extract").
   - Sort results by distance ascending (closest matches first).
   - Return tuples of `(candidateName, distance)`.

5. Use case-insensitive comparison (lowercase both input and candidates).

**Files**: `src/Frank.Cli.Core/Help/FuzzyMatch.fs` (new, ~50 lines)
**Parallel?**: Yes -- independent of T001 and T003.

**Edge Cases**:
- Empty input: return all candidates (distance = candidate length).
- Exact match: distance 0, should appear first.
- Input longer than any candidate: may exceed threshold -- that's OK, return empty suggestions.

---

### Subtask T003 -- Create StalenessChecker.fs

**Purpose**: Extract the staleness detection logic from `ValidateCommand.fs` into a shared module. Both ValidateCommand and the new StatusCommand will use this module. This satisfies Constitution Principle VIII (No Duplicated Logic).

**Steps**:

1. Create `src/Frank.Cli.Core/Commands/StalenessChecker.fs`.

2. Use namespace `Frank.Cli.Core.Commands`.

3. Define the `StalenessResult` discriminated union:

```fsharp
type StalenessResult =
    | Fresh
    | Stale
    | Indeterminate
```

4. Move `computeFileHash` from ValidateCommand.fs (lines 124-128) into StalenessChecker:

```fsharp
module StalenessChecker =
    let computeFileHash (filePath: string) : string =
        use sha256 = System.Security.Cryptography.SHA256.Create()
        use stream = System.IO.File.OpenRead(filePath)
        let hash = sha256.ComputeHash(stream)
        System.BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()
```

5. Move `checkStaleness` logic from ValidateCommand.fs (lines 130-164) into StalenessChecker, but return `StalenessResult` instead of `ValidationIssue list`:

```fsharp
    let checkStaleness (state: ExtractionState) : StalenessResult =
        let sourceFiles =
            state.SourceMap
            |> Map.values
            |> Seq.map (fun loc -> loc.File)
            |> Seq.distinct
            |> Seq.toList

        if sourceFiles.IsEmpty then
            Indeterminate
        else
            let currentHashes =
                sourceFiles
                |> List.choose (fun f ->
                    if System.IO.File.Exists f then Some(computeFileHash f) else None)
                |> String.concat ""

            let currentCombinedHash =
                if currentHashes.Length > 0 then
                    use sha256 = System.Security.Cryptography.SHA256.Create()
                    let bytes = System.Text.Encoding.UTF8.GetBytes(currentHashes)
                    System.BitConverter.ToString(sha256.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant()
                else
                    ""

            if currentCombinedHash <> state.Metadata.SourceHash
               && state.Metadata.SourceHash <> "" then
                Stale
            else
                Fresh
```

6. Ensure `use` bindings are used for SHA256 and File streams (Constitution Principle VI).

**Files**: `src/Frank.Cli.Core/Commands/StalenessChecker.fs` (new, ~45 lines)
**Parallel?**: Yes -- independent of T001 and T002.

**Notes**: The opens needed are `System`, `System.IO`, `System.Security.Cryptography`, and `Frank.Cli.Core.State`.

---

### Subtask T004 -- Add ProjectStatus Types to HelpTypes.fs

**Purpose**: Define the types needed by the StatusCommand to represent project state inspection results.

**Steps**:

1. Add the following types to `src/Frank.Cli.Core/Help/HelpTypes.fs` (after the HelpLookupResult type):

```fsharp
type ExtractionStatus =
    | NotExtracted
    | Current
    | Stale
    | Unreadable of reason: string

type ArtifactStatus =
    | Present
    | Missing of missingFiles: string list

type RecommendedAction =
    | RunExtract
    | ReExtract
    | RunCompile
    | UpToDate
    | RecoverExtract of reason: string

type ProjectStatus =
    { ProjectPath: string
      Extraction: ExtractionStatus
      Artifacts: ArtifactStatus
      RecommendedAction: RecommendedAction
      StateDirectory: string }
```

**Files**: `src/Frank.Cli.Core/Help/HelpTypes.fs` (extend, ~30 additional lines)
**Parallel?**: No -- extends the same file as T001.

---

### Subtask T005 -- Update Frank.Cli.Core.fsproj

**Purpose**: Add Compile entries for the three new files in the correct dependency order.

**Steps**:

1. Open `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`.

2. Add the new Compile entries. The ordering is critical in F# -- files can only reference types defined in files listed earlier. Insert them as follows:

```xml
<!-- After State/DiffEngine.fs, before Output/ArtifactSerializer.fs -->
<Compile Include="Help/HelpTypes.fs" />
<Compile Include="Help/FuzzyMatch.fs" />

<!-- After Commands/CompileCommand.fs, but BEFORE Commands/ValidateCommand.fs -->
<!-- This means reordering: StalenessChecker before ValidateCommand -->
<Compile Include="Commands/StalenessChecker.fs" />
```

3. **CRITICAL**: `StalenessChecker.fs` must appear **before** `ValidateCommand.fs` in the compile order because ValidateCommand will call StalenessChecker after the T006 refactoring. The current order has `ValidateCommand.fs` at position 3 in Commands. The new order should be:

```xml
<Compile Include="Commands/ExtractCommand.fs" />
<Compile Include="Commands/ClarifyCommand.fs" />
<Compile Include="Commands/StalenessChecker.fs" />
<Compile Include="Commands/ValidateCommand.fs" />
<Compile Include="Commands/DiffCommand.fs" />
<Compile Include="Commands/CompileCommand.fs" />
```

4. The full Compile item group after this subtask:

```xml
<ItemGroup>
    <Compile Include="Rdf/FSharpRdf.fs" />
    <Compile Include="Rdf/Vocabularies.fs" />
    <Compile Include="Analysis/AstAnalyzer.fs" />
    <Compile Include="Analysis/TypeAnalyzer.fs" />
    <Compile Include="Extraction/UriHelpers.fs" />
    <Compile Include="Extraction/TypeMapper.fs" />
    <Compile Include="Extraction/RouteMapper.fs" />
    <Compile Include="Extraction/CapabilityMapper.fs" />
    <Compile Include="Extraction/ShapeGenerator.fs" />
    <Compile Include="Extraction/VocabularyAligner.fs" />
    <Compile Include="Analysis/ProjectLoader.fs" />
    <Compile Include="State/ExtractionState.fs" />
    <Compile Include="State/DiffEngine.fs" />
    <Compile Include="Help/HelpTypes.fs" />
    <Compile Include="Help/FuzzyMatch.fs" />
    <Compile Include="Output/ArtifactSerializer.fs" />
    <Compile Include="Commands/ExtractCommand.fs" />
    <Compile Include="Commands/ClarifyCommand.fs" />
    <Compile Include="Commands/StalenessChecker.fs" />
    <Compile Include="Commands/ValidateCommand.fs" />
    <Compile Include="Commands/DiffCommand.fs" />
    <Compile Include="Commands/CompileCommand.fs" />
    <Compile Include="Output/JsonOutput.fs" />
    <Compile Include="Output/TextOutput.fs" />
</ItemGroup>
```

**Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (modify)
**Parallel?**: No -- must be done after T001-T004 create the files.

---

### Subtask T006 -- Refactor ValidateCommand to Use StalenessChecker

**Purpose**: Remove the duplicated `computeFileHash` and `checkStaleness` functions from ValidateCommand.fs and replace them with calls to the shared StalenessChecker module.

**Steps**:

1. Open `src/Frank.Cli.Core/Commands/ValidateCommand.fs`.

2. **Remove** the private `computeFileHash` function (lines 124-128):
```fsharp
// DELETE these lines:
let private computeFileHash (filePath: string) : string =
    use sha256 = SHA256.Create()
    use stream = File.OpenRead(filePath)
    let hash = sha256.ComputeHash(stream)
    BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant()
```

3. **Remove** the private `checkStaleness` function (lines 130-164):
```fsharp
// DELETE these lines:
let private checkStaleness (state: ExtractionState) : ValidationIssue list =
    // ... entire function body ...
```

4. **Replace** the call in `execute` (line 177) with a call to StalenessChecker and map the result:

```fsharp
let stalenessIssues =
    match StalenessChecker.checkStaleness state with
    | StalenessChecker.Stale ->
        [ { Severity = Warning
            Message = "Source files have changed since last extraction. Run 'frank-cli extract' to update."
            Uri = None } ]
    | StalenessChecker.Fresh
    | StalenessChecker.Indeterminate -> []
```

5. Remove the `open System.Security.Cryptography` import if it is no longer needed (it was only used by `computeFileHash`). Keep `open System.IO` if still needed for other parts of ValidateCommand.

6. **Verify**: Run `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` and `dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj` to confirm no regressions.

**Files**: `src/Frank.Cli.Core/Commands/ValidateCommand.fs` (modify)
**Parallel?**: No -- depends on T003 (StalenessChecker) and T005 (.fsproj ordering).

**Validation**:
- [ ] `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` succeeds
- [ ] `dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj` passes (ValidateCommandTests unchanged behavior)
- [ ] No `computeFileHash` or `checkStaleness` private functions remain in ValidateCommand.fs
- [ ] StalenessChecker module is called instead

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| ValidateCommand refactoring breaks existing behavior | Run existing ValidateCommandTests after refactoring; behavior must be identical |
| .fsproj compile order wrong | F# compiler will produce clear "unknown type" errors; fix by reordering |
| StalenessChecker namespace conflicts | Use `Frank.Cli.Core.Commands` namespace, matching other command modules |

## Review Guidance

- Verify all types match the data-model.md definitions exactly.
- Verify FuzzyMatch handles edge cases (empty input, exact match, case insensitivity).
- Verify StalenessChecker uses `use` bindings for SHA256 and FileStream.
- Verify ValidateCommand no longer has its own `computeFileHash`/`checkStaleness`.
- Verify .fsproj compile order: HelpTypes before FuzzyMatch, StalenessChecker before ValidateCommand.
- Run `dotnet build` and `dotnet test` to confirm green.

## Activity Log

- 2026-03-15T23:59:04Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:02:27Z – claude-opus – shell_pid=98171 – lane=doing – Assigned agent via workflow command
- 2026-03-16T04:15:13Z – claude-opus – shell_pid=98171 – lane=for_review – Moved to for_review
- 2026-03-16T04:15:51Z – claude-opus-reviewer – shell_pid=3730 – lane=doing – Started review via workflow command
- 2026-03-16T04:18:47Z – claude-opus-reviewer – shell_pid=3730 – lane=done – Review passed: All types in HelpTypes.fs match data-model.md exactly (CommandExample, WorkflowPosition, CommandHelp, HelpTopic, HelpLookupResult, ExtractionStatus, ArtifactStatus, RecommendedAction, ProjectStatus). FuzzyMatch handles edge cases correctly (empty input, exact match, case insensitivity, prefix matching). StalenessChecker uses 'use' bindings for SHA256 and FileStream (Constitution VI). ValidateCommand refactored correctly - no computeFileHash/checkStaleness remain. .fsproj compile order correct: HelpTypes before FuzzyMatch, StalenessChecker before ValidateCommand. Build succeeds with 0 warnings, all 107 tests pass. Commit is clean - only the 5 expected source files.
