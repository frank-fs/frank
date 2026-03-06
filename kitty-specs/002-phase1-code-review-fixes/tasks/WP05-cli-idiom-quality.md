---
work_package_id: WP05
title: Frank.Cli.Core — Idiom & Quality
lane: planned
dependencies: []
subtasks:
- T023
- T024
- T025
- T027
- T028
- T029
- T030
phase: Phase 2 - Idiom
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-06T15:25:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-004, FR-014, FR-016, FR-017, FR-021]
---

# Work Package Prompt: WP05 – Frank.Cli.Core — Idiom & Quality

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
spec-kitty implement WP05
```

No hard dependencies, but logically follows WP04 since both modify `Frank.Cli.Core`.

---

## Objectives & Success Criteria

- `ValidationIssue.Severity` and `DiffEntry.Type` use type-safe alternatives (DU preferred unless hot path — escalate to user if uncertain)
- `AstAnalyzer.walkCeBody` uses functional fold (no `ResizeArray` + `ref`)
- `FSharpRdf` module no longer has `[<AutoOpen>]` — all consumers use explicit `open`
- `JsonDocument` in `CompileCommand.verifyRoundTrip` disposed via `use` (Constitution VI)
- Vocabulary constants in `Vocabularies.fs` use `[<Literal>]`
- FsToolkit.ErrorHandling added as dependency

## Context & Constraints

- **Tracking Issue**: #81 — Tier 2 (design issues) + Tier 3 (F# idiom) + Tier 4 (Literal annotations)
- **Constitution**: Principle II (Idiomatic F#), Principle V (Performance Parity), Principle VI (Resource Disposal)
- **Clarifications** (CRITICAL — read these carefully):
  - **DU vs performance**: The constitution emphasizes BOTH idiomatic F# AND performance. `ValidationIssue.Severity` and `DiffEntry.Type` are developer-time operations (validation, diff), likely NOT hot paths. DUs are probably appropriate. But if you discover they ARE on a hot path, **escalate to the user** — do not decide unilaterally.
  - **Composition style**: Both CEs and piped module functions are acceptable. Do NOT use repeated `Async.RunSynchronously`, `Option.get`, `Result.defaultValue` etc. mid-function. Compose naturally; extract once at the top-level call site.
  - **Fail-fast**: Exceptions for unrecoverable errors. Result/Option only for expected, recoverable outcomes.

## Subtasks & Detailed Guidance

### Subtask T023 – Add FsToolkit.ErrorHandling to Frank.Cli.Core

- **Purpose**: Provides `result {}`, `option {}`, `asyncResult {}` CEs for natural composition without custom implementation.
- **Steps**:
  1. Add to `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`:
     ```xml
     <PackageReference Include="FsToolkit.ErrorHandling" Version="4.*" />
     ```
     (Pin to latest stable 4.x at implementation time — do NOT use wildcard in final version)
  2. Run `dotnet restore src/Frank.Cli.Core/`
  3. Verify build: `dotnet build src/Frank.Cli.Core/`
- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
- **Parallel?**: No — must be done before T026

### Subtask T024 – Replace String-Typed ValidationIssue.Severity with DU

- **Purpose**: `ValidationIssue.Severity` is a string (`"Error"`, `"Warning"`, `"Info"`). Should be a discriminated union for type safety.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Commands/ValidateCommand.fs`
  2. Locate the `ValidationIssue` type definition (line ~14)
  3. Check if `Severity` is used in pattern matching, serialization, or display
  4. **Evaluate hot path**: Is `ValidateCommand` called in a tight loop or only during developer-invoked validation? (Almost certainly developer-time only.)
  5. If NOT a hot path (expected), create a DU:
     ```fsharp
     type Severity =
         | Error
         | Warning
         | Info
     ```
  6. Update `ValidationIssue` to use `Severity` type instead of `string`
  7. Update all pattern matches on severity strings to use DU cases
  8. Update any serialization/display code (e.g., `TextOutput`, `JsonOutput`) to convert DU to string for display
  9. If you discover this IS on a hot path, **STOP and ask the user** before proceeding
- **Files**:
  - `src/Frank.Cli.Core/Commands/ValidateCommand.fs`
  - `src/Frank.Cli.Core/Output/TextOutput.fs` (if it displays severity)
  - `src/Frank.Cli.Core/Output/JsonOutput.fs` (if it serializes severity)
- **Parallel?**: Yes — independent of T025
- **Notes**: This is a developer-time CLI operation. DU is almost certainly the right choice. The performance concern from the clarification is primarily about runtime hot paths in the middleware/request pipeline, not CLI commands.

### Subtask T025 – Replace String-Typed DiffEntry.Type with DU

- **Purpose**: `DiffEntry.Type` is a string (`"Added"`, `"Removed"`, `"Modified"`). Should be a discriminated union.
- **Steps**:
  1. Open `src/Frank.Cli.Core/State/DiffEngine.fs`
  2. Locate `DiffEntry` type definition (line ~7)
  3. Create DU:
     ```fsharp
     type DiffType =
         | Added
         | Removed
         | Modified
     ```
  4. Update `DiffEntry` to use `DiffType` instead of `string` for the type field
  5. Update `entryFromTriple` (line ~32) to use DU cases
  6. Update `formatDiff` and any display/serialization code
  7. Same hot-path evaluation as T024 — diff is developer-time, DU is appropriate
- **Files**:
  - `src/Frank.Cli.Core/State/DiffEngine.fs`
  - `src/Frank.Cli.Core/Output/TextOutput.fs` (if it displays diff type)
  - `src/Frank.Cli.Core/Output/JsonOutput.fs` (if it serializes diff type)
- **Parallel?**: Yes — independent of T024

### Subtask T027 – Replace Imperative Accumulation in AstAnalyzer.walkCeBody

- **Purpose**: `walkCeBody` uses `ResizeArray` + `ref` cells for accumulation. Replace with functional fold.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Analysis/AstAnalyzer.fs`
  2. Locate `walkCeBody` function
  3. Identify the `ResizeArray` and `ref` cells being used as mutable accumulators
  4. Determine what is being accumulated (likely: HTTP methods, metadata, linkedData flag, name)
  5. Define an immutable accumulator record:
     ```fsharp
     type private WalkState = {
         Methods: string list
         HasLinkedData: bool
         Name: string option
         // ... other accumulated fields
     }

     let private emptyState = {
         Methods = []
         HasLinkedData = false
         Name = None
     }
     ```
  6. Replace the `ResizeArray` + `ref` pattern with `List.fold` or recursive traversal:
     ```fsharp
     let walkCeBody (body: SynExpr list) =
         body
         |> List.fold (fun state expr ->
             match expr with
             | HttpMethodExpr method -> { state with Methods = method :: state.Methods }
             | LinkedDataExpr -> { state with HasLinkedData = true }
             | NameExpr name -> { state with Name = Some name }
             | _ -> state
         ) emptyState
     ```
  7. Preserve the exact same output — this is a pure refactoring, not a behavior change
  8. Build and run tests to verify identical behavior
- **Files**: `src/Frank.Cli.Core/Analysis/AstAnalyzer.fs`
- **Parallel?**: No
- **Notes**: The fold may need to handle nested expressions (e.g., `SynExpr.Sequential`). Preserve the traversal order.

### Subtask T028 – Remove `[<AutoOpen>]` from FSharpRdf Module

- **Purpose**: `[<AutoOpen>]` on the `FSharpRdf` module auto-exposes generic names (`UriNode`, `LiteralNode`, `createGraph`, etc.) into every file that opens the parent namespace. Remove it and require explicit `open`.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Rdf/FSharpRdf.fs`
  2. Remove the `[<AutoOpen>]` attribute from the module declaration
  3. Build the project — compiler will show errors for every file that implicitly used `FSharpRdf` names
  4. For each error, add `open Frank.Cli.Core.Rdf.FSharpRdf` (or the appropriate module path) at the top of the file
  5. Expected consumers (search for usage of `RdfNode`, `UriNode`, `LiteralNode`, `createGraph`, `createUriNode`, etc.):
     - All extraction modules (TypeMapper, ShapeGenerator, RouteMapper, CapabilityMapper, VocabularyAligner)
     - State modules (ExtractionState, DiffEngine)
     - Command modules (ExtractCommand, CompileCommand, ValidateCommand, DiffCommand)
     - Test files
  6. Build until clean: `dotnet build src/Frank.Cli.Core/ && dotnet build test/Frank.Cli.Core.Tests/`
- **Files**:
  - `src/Frank.Cli.Core/Rdf/FSharpRdf.fs` (remove attribute)
  - Multiple consumer files (add explicit `open`)
- **Parallel?**: No — touches many files, potential merge conflicts
- **Notes**: Let the compiler guide you. Each error indicates a file that needs an explicit `open` statement. This may touch 10+ files but each change is trivial (adding one line).

### Subtask T029 – Fix JsonDocument Disposal in CompileCommand

- **Purpose**: `CompileCommand.verifyRoundTrip` calls `JsonDocument.Parse` but doesn't dispose the result. Constitution VI requires `use` binding.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Commands/CompileCommand.fs`
  2. Locate `verifyRoundTrip` function
  3. Find `JsonDocument.Parse(...)` call
  4. Replace `let doc = JsonDocument.Parse(...)` with `use doc = JsonDocument.Parse(...)`
  5. Ensure the `use` scope covers all usage of `doc`
  6. Build: `dotnet build src/Frank.Cli.Core/`
- **Files**: `src/Frank.Cli.Core/Commands/CompileCommand.fs`
- **Parallel?**: Yes — independent of all others
- **Notes**: Simple one-line change (`let` → `use`).

### Subtask T030 – Add `[<Literal>]` to Vocabulary Constants

- **Purpose**: Vocabulary constants in `Vocabularies.fs` should use `[<Literal>]` for compile-time string embedding and pattern matching support.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Rdf/Vocabularies.fs`
  2. Find the namespace/vocabulary constants: `Rdf`, `Rdfs`, `Owl`, `Shacl`, `Hydra`, `SchemaOrg`, `Xsd`
  3. Add `[<Literal>]` attribute to each:
     ```fsharp
     [<Literal>]
     let Rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"

     [<Literal>]
     let Rdfs = "http://www.w3.org/2000/01/rdf-schema#"

     [<Literal>]
     let Owl = "http://www.w3.org/2002/07/owl#"
     // ... etc.
     ```
  4. Build to verify no issues: `dotnet build src/Frank.Cli.Core/`
  5. `[<Literal>]` enables these to be used in pattern matching and ensures compile-time string embedding
- **Files**: `src/Frank.Cli.Core/Rdf/Vocabularies.fs`
- **Parallel?**: Yes — independent of all others
- **Notes**: `[<Literal>]` requires the value to be a compile-time constant (string literals qualify). If any constants are computed (e.g., string concatenation), they may need adjustment.

## Risks & Mitigations

- **T028**: Removing `[<AutoOpen>]` will cause many compiler errors. Each is trivially fixed by adding `open`, but the changeset will touch many files. Recommend doing this subtask last to minimize merge conflicts with other WPs.
- **T024/T025**: If DU proves to be on a hot path (unlikely for CLI commands), the user must be consulted. Do not guess.
- **T027**: Fold refactoring must produce identical output. Run existing tests to verify.
- **T030**: If `[<Literal>]` causes issues with computed constants, the constant may need restructuring.

## Review Guidance

- Verify `ValidationIssue.Severity` and `DiffEntry.Type` are DUs (or justified performance alternatives)
- Verify `GraphLoader.load` has no nested match pyramid
- Verify `AstAnalyzer.walkCeBody` has no `ResizeArray` or `ref` — uses fold
- Verify `[<AutoOpen>]` removed from `FSharpRdf`; all consumers have explicit `open`
- Verify `JsonDocument` has `use` binding in `CompileCommand`
- Verify vocabulary constants have `[<Literal>]`
- Run all tests: `dotnet test`

## Activity Log

- 2026-03-06T15:25:00Z – system – lane=planned – Prompt created.
