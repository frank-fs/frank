# v7.3.2 Codegen Remediation — Plan 5: Validation MSBuild wiring + Codegen E2E

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Wire the typed `ValidationEmitter` into a real build so `GeneratedValidation.fs` is emitted, compiled, and yields a working `ShapesGraph` — proving the codegen pipeline end-to-end. (The HTTP 422 runtime middleware/CE is SEPARATE V3 runtime work, explicitly OUT of scope.)

**Architecture:** `GenerateValidationTask` (clone of `GenerateLinkedDataTask`) reads the lock, evaluates the vocabulary registry via `VocabularyEvaluator.evalRegistry`, extracts field `TypeInfo` via a NEW `Extractor.extractTypeInfosFromSources`, builds `typesByName`, calls `ValidationEmitter.emit`, and writes `GeneratedValidation.fs`. A target pair (clone of the LinkedData pair) generates + injects it. E2E builds the `TicTacToe-v732` sample.

**Tech Stack:** F# net8/9/10, FSharp.Compiler.Service, MSBuild custom tasks, dotNetRdf + Shacl, Fabulous.AST (via ValidationEmitter).

## Global Constraints

- Worktree root (ABSOLUTE; cwd RESETS between Bash calls): `/Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation`. `cd` first in every command; confirm branch `v732-codegen-remediation`.
- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` on every `dotnet` command.
- **MSBuild gotcha:** rebuilding `Frank.Cli.MSBuild` needs `dotnet build-server shutdown` before a consumer build picks up the new task DLL (MSBuild caches task assemblies). Generated `.fs` must precede consumers in `@(Compile)` — the inject target does the Program.fs-or-last reorder. See `src/CLAUDE.md`.
- Suites by path: `test/Frank.Cli.Core.Tests` (189), `test/Frank.Cli.MSBuild.Tests`, plus the new MSBuild task test.
- `dotnet fantomas --check` on changed src + test before each commit.
- Commit after each task with the exact `git add` list.

## Reference facts (verified)

- `ValidationEmitter.emit : moduleName -> registry -> lock -> typesByName:Map<string,TypeInfo> -> Result<string,string>` (the 4th param is what needs build-time supply).
- `Extractor` has `extractTypeInfosFromSource (sourceCode: string)` and `extractTypeInfos (projectFile: string)`, but NOT a `(sourceFiles[], refs[])` entry. The MSBuild task has `SourceFiles: ITaskItem[]` + `AssemblyRefs: ITaskItem[]` (same inputs `VocabularyEvaluator.evalRegistry` consumes).
- Clone sources: `src/Frank.Cli.MSBuild/GenerateLinkedDataTask.fs`; the `FrankGenerateLinkedData` / `FrankInjectGeneratedLinkedDataFile` target pair + `UsingTask` in `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets` (~lines 122-208); `test/Frank.Cli.MSBuild.Tests/GenerateLinkedDataTaskTests.fs` (+ `Fixtures.fs`, `StubBuildEngine.fs`).
- Sample: `sample/TicTacToe-v732/` (`Model.fs`, `Vocabulary.fs`, `Program.fs`, `TicTacToe.v732.fsproj`) already drives Discovery/LinkedData/SemanticModel codegen.

---

### Task 1: `Extractor.extractTypeInfosFromSources` (source-set → TypeInfo)

**Files:** Modify `src/Frank.Cli.Core/Extractor.fs`; test `test/Frank.Cli.Core.Tests/ExtractorTests.fs` (or wherever Extractor is tested).

**Interfaces — Produces:** `Extractor.extractTypeInfosFromSources : sourceFiles:string[] -> refs:string[] -> Result<TypeInfo list, string>` — FCS-parses the given source files (with the given assembly refs) and returns the `TypeInfo` list (same entity-walk `extractTypeInfos`/`extractTypeInfosFromSource` already use; reuse the private `buildProjectOptions`/walk — do NOT duplicate the entity-walk logic).

- [ ] **Step 1: Failing test.** Feed two small inline source files (write them to temp paths) + the core refs (reuse how existing Extractor tests obtain refs), assert the returned `TypeInfo` list contains the expected types with non-empty `FieldInfo.TypeName` (e.g. a record with `count: int`, `note: string option`). Use the REAL normalized TypeName strings (`int`, `string`).
- [ ] **Step 2: Run — fails.** `... dotnet test test/Frank.Cli.Core.Tests/ --filter "extractTypeInfosFromSources"`
- [ ] **Step 3: Implement.** Add `extractTypeInfosFromSources` reusing the existing FCS plumbing (`extractTypeInfos` already builds project options from a project file + walks entities; factor the source-files+refs → `FSharpProjectOptions` → `ParseAndCheckProject` → entity-walk path so both entry points share it). A second FCS parse alongside `evalRegistry` is acceptable; do NOT refactor `evalRegistry`.
- [ ] **Step 4: Run — passes. Step 5: full Cli.Core suite + fantomas + commit.**
```bash
git add src/Frank.Cli.Core/Extractor.fs test/Frank.Cli.Core.Tests/ExtractorTests.fs
git commit -m "feat(cli): Extractor.extractTypeInfosFromSources (source-set TypeInfo for Validation codegen)"
```

---

### Task 2: `GenerateValidationTask`

**Files:** Create `src/Frank.Cli.MSBuild/GenerateValidationTask.fs`; modify `src/Frank.Cli.MSBuild/Frank.Cli.MSBuild.fsproj` (add the file); test `test/Frank.Cli.MSBuild.Tests/GenerateValidationTaskTests.fs` (+ its fsproj entry).

- [ ] **Step 1: Failing task test** — clone `GenerateLinkedDataTaskTests.fs` (reuse `Fixtures.fs`/`StubBuildEngine.fs`): run `GenerateValidationTask` over the fixture lock + sources → emitted `GeneratedValidation.fs`; plus an FCS compile gate (the AT1 anti-drift proof) — typecheck the emitted file against the real `Frank.Semantic`/`Frank.Validation` assemblies (reuse the real-assembly typecheck approach from `FcsTypecheck.typecheckAgainstRealAssemblies` if reachable, or the MSBuild test's own FCS harness). Assert: `Some XsdInteger`/`RecordShape`/`EnumShape` present; `sh:`-free emitter output; no `urn:frank:`; deterministic (two runs byte-identical).
- [ ] **Step 2: Run — fails** (`GenerateValidationTask` undefined). `... dotnet test test/Frank.Cli.MSBuild.Tests/ --filter "Validation"`
- [ ] **Step 3: Create `GenerateValidationTask.fs`** — clone `GenerateLinkedDataTask.fs` with `LinkedData`→`Validation`. UNLIKE LinkedData, after `evalRegistry` succeeds, ALSO extract types and pass them:

```fsharp
match VocabularyEvaluator.evalRegistry refs sources binding with
| Error msg -> this.Log.LogError($"GenerateValidationTask: FSI evaluation failed: {msg}"); false
| Ok registry ->
    match Extractor.extractTypeInfosFromSources (List.toArray sources) (List.toArray refs) with
    | Error msg -> this.Log.LogError($"GenerateValidationTask: type extraction failed: {msg}"); false
    | Ok typeInfos ->
        let typesByName = typeInfos |> List.map (fun ti -> ti.FullName, ti) |> Map.ofList
        match ValidationEmitter.emit this.ModuleName registry lock typesByName with
        | Error msg -> this.Log.LogError($"GenerateValidationTask: code generation failed: {msg}"); false
        | Ok source -> this.WriteOutput source
```
`WriteOutput` writes `Path.Combine(this.OutputPath, "GeneratedValidation.fs")`. Same `[<Required>]` props as the LinkedData task (LockFilePath/OutputPath/ModuleName/SourceFiles/AssemblyRefs) + `[<Output>] GeneratedFile` + `VocabularyBinding`. Add `<Compile Include="GenerateValidationTask.fs" />` to the MSBuild fsproj.
- [ ] **Step 4: Run task tests — pass** (incl FCS gate). `dotnet build-server shutdown` first if the task DLL seems stale.
- [ ] **Step 5: Full MSBuild test suite + fantomas + commit.**
```bash
git add src/Frank.Cli.MSBuild/GenerateValidationTask.fs src/Frank.Cli.MSBuild/Frank.Cli.MSBuild.fsproj test/Frank.Cli.MSBuild.Tests/GenerateValidationTaskTests.fs test/Frank.Cli.MSBuild.Tests/*.fsproj
git commit -m "feat(msbuild): GenerateValidationTask (evalRegistry + extractTypeInfos + ValidationEmitter) + FCS gate"
```

---

### Task 3: `.targets` — `FrankGenerateValidation` + inject pair

**Files:** Modify `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets`.

- [ ] **Step 1: Add the UsingTask + target pair** cloned from the LinkedData pair (~lines 122-208):
  - `<UsingTask TaskName="Frank.Cli.MSBuild.GenerateValidationTask" .../>`.
  - `FrankGenerateValidation` — `BeforeTargets="FrankInjectGeneratedValidationFile"`, `Inputs="$(FrankLockFilePath);@(Compile)"`, `Outputs="$(IntermediateOutputPath)GeneratedValidation.fs"`, `Condition` = lock exists AND `Frank.Validation` referenced (package or project); `FrankValidationModuleName` default `$(RootNamespace).GeneratedValidation`; same `_FrankVocabSource`/SourceFiles/AssemblyRefs item logic the LinkedData target uses; calls `GenerateValidationTask`.
  - `FrankInjectGeneratedValidationFile` — `BeforeTargets="BeforeCompile;CoreCompile"`, `Condition="Exists('$(IntermediateOutputPath)GeneratedValidation.fs')"`, the identical Program.fs-or-last `@(Compile)` reorder with `Validation`-suffixed item names (generated file must precede its consumers).
- [ ] **Step 2: Commit** (verified by Task 4's real sample build — no unit test for the .targets XML itself).
```bash
git add src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets
git commit -m "feat(msbuild): FrankGenerateValidation + inject target pair"
```

---

### Task 4: Codegen E2E — sample build emits + compiles + validates

**Files:** Modify `sample/TicTacToe-v732/TicTacToe.v732.fsproj` (add `Frank.Validation` ProjectReference). (Vocabulary/Model already drive the lock; a `constrainPattern` may be added if a string field maps to an IRI — optional.)

- [ ] **Step 1: Reference Frank.Validation in the sample** (`<ProjectReference Include="../../src/Frank.Validation/Frank.Validation.fsproj" />`) — this satisfies the `FrankGenerateValidation` condition.
- [ ] **Step 2: Build the sample** (drives the codegen for real). `dotnet build-server shutdown; DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build sample/TicTacToe-v732/TicTacToe.v732.fsproj`. Expected: build OK; `find sample/TicTacToe-v732/obj -name GeneratedValidation.fs` exists.
- [ ] **Step 3: AT — assert the generated artifact.** The emitted `GeneratedValidation.fs`: contains `schema.org` IRIs and `RecordShape`/`EnumShape`/`Shapes.toShapesGraph`; contains NO `urn:frank:`; compiled successfully (the build succeeding proves it compiles into the sample against the real `Frank.Semantic`/`Frank.Validation`).
```bash
F=$(find sample/TicTacToe-v732/obj -name GeneratedValidation.fs | head -1)
grep -q "schema.org" "$F" && grep -q "Shapes.toShapesGraph" "$F" && ! grep -q "urn:frank:" "$F" && echo "AT-CODEGEN PASS"
```
- [ ] **Step 4 (AT — runtime shape):** add a small test (in the sample's E2E harness `sample/TicTacToe-v732.E2E`, or a focused test) that loads the sample's generated `shapesGraph` and runs `.Validate` on a data graph — confirming `Shapes.toShapesGraph` yields a *working* `ShapesGraph` (conforms on valid, not on invalid). If wiring a runtime test into the sample is heavy, the unit-level `Frank.Validation.Tests` ShapesTests already prove `toShapesGraph` semantics — in that case this step's AT is satisfied by "the sample's generated shapesGraph compiles + is non-null" and the unit conformance tests; note which.
- [ ] **Step 5: Full repo verification + commit.** All suites by path + `dotnet build Frank.sln` + `dotnet fantomas --check src/`. 
```bash
git add sample/TicTacToe-v732/TicTacToe.v732.fsproj
git commit -m "feat(sample): wire Frank.Validation — codegen E2E emits + compiles GeneratedValidation.fs"
```

---

## Acceptance (feature-complete gate)

- #324 AT1 — generated `GeneratedValidation.fs` compiles to a valid `ShapesGraph` (Task 2 FCS gate + Task 4 real sample build).
- #324 AT2 — `sh:targetClass` = vocabulary IRI, never `urn:frank:` (Task 2 + Task 4 grep).
- #324 AT5 — byte-identical across two builds (Task 2 determinism).
- Codegen E2E — sample build emits + compiles `GeneratedValidation.fs`; `Shapes.toShapesGraph` yields a working `ShapesGraph` (Task 4).
- All suites green; `fantomas --check src/` clean; `dotnet build Frank.sln` clean.
- THEN: feature complete → hand to user for review (NO merge before this).

## Out of scope (NOT this feature)
- `Frank.Validation` runtime middleware / `useValidation` CE / the HTTP 422 + `ValidationReport` path (V3 runtime vertical).
- `Frank.Provenance` emitter.
