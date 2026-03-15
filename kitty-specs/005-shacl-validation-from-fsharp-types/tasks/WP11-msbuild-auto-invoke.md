---
work_package_id: WP11
title: MSBuild Auto-Invoke Target
lane: "doing"
dependencies:
- WP09
- WP10
base_branch: 005-shacl-validation-from-fsharp-types-WP11-merge-base
base_commit: c0a8102670d8a4b4a3a7b2e7d6071ad5cab8f18c
created_at: '2026-03-15T13:53:25.695367+00:00'
subtasks: [T060, T061, T062, T063]
shell_pid: "44929"
agent: "claude-opus-reviewer"
reviewed_by: "Ryan Riley"
review_status: "approved"
history:
- timestamp: '2026-03-14T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated from build-time SHACL unification design spec
design_ref: docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md
requirement_refs: []
---

# Work Package Prompt: WP11 -- MSBuild Auto-Invoke Target

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP11 --base WP10
```

Depends on WP09 + WP10 (enriched extraction pipeline). Can be developed in parallel with WP12.

---

## Objectives & Success Criteria

- Add `GenerateFrankSemanticDefinitions` MSBuild target to `Frank.Cli.MSBuild.targets`
- Target shells out to `frank-cli extract --emit-artifacts` via `<Exec>`
- Target runs `AfterTargets="ResolveAssemblyReferences"` and `BeforeTargets="CoreCompile"`
- Incremental build support: skip regeneration if source files haven't changed
- Existing `EmbedFrankSemanticDefinitions` target picks up the output
- Add `--emit-artifacts` flag to `frank-cli extract` command
- `frank-cli extract --emit-artifacts` writes `shapes.shacl.ttl`, `ontology.owl.xml`, `manifest.json` directly
- Clear warning if `frank-cli` tool is not installed
- Building a sample project with Frank.Cli.MSBuild produces an assembly with embedded validation-grade shapes

---

## Context & Constraints

**Reference documents**:
- `docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md` -- WP11 section
- `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets` -- Existing targets to extend
- `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.props` -- Existing properties
- `src/Frank.Cli.Core/Commands/ExtractCommand.fs` -- Extraction pipeline to extend
- `src/Frank.Cli.Core/Commands/CompileCommand.fs` -- Compile command (artifact output)

**Key constraints**:
- Frank.Cli.MSBuild is a content-only NuGet package (`IncludeBuildOutput=false`, targets `netstandard2.0`)
- The target uses `<Exec>` to invoke `frank-cli` as a dotnet tool -- no FCS inside the MSBuild task DLL
- Auto-invoke only runs for projects that explicitly reference Frank.Cli.MSBuild
- Frank.Cli.Core targets net10.0 only; the `<Exec>` invocation runs `dotnet frank-cli` in a separate process
- The `extract` command currently persists state to `.frank/` and requires a subsequent `compile` to emit artifacts; `--emit-artifacts` unifies this

---

## Subtasks & Detailed Guidance

### Subtask T060 -- Add `--emit-artifacts` flag to `frank-cli extract`

**Purpose**: Extend the extract command to optionally write Turtle, OWL, and manifest files directly, unifying the extract+compile workflow.

**Steps**:
1. In `ExtractCommand.fs`, add `--emit-artifacts` and `--output` parameters.
2. When `--emit-artifacts` is set, after the extraction pipeline completes:
   - Serialize `shapesGraph` to Turtle and write to `{output}/shapes.shacl.ttl`
   - Serialize `ontologyGraph` to OWL/XML and write to `{output}/ontology.owl.xml`
   - Write manifest JSON to `{output}/manifest.json`
3. Reuse serialization logic from `CompileCommand.fs` (or extract it to a shared module to avoid duplication).
4. The existing `extract` command (without `--emit-artifacts`) continues to work as before -- state persistence only.
5. The existing `compile` command continues to work as before -- reads persisted state and emits artifacts.

**Files**: `src/Frank.Cli.Core/Commands/ExtractCommand.fs`, potentially `src/Frank.Cli.Core/Output/` modules
**Notes**: Constitution VIII (No Duplicated Logic) -- share the serialization code between `compile` and `extract --emit-artifacts`. Do not copy-paste.

### Subtask T061 -- Add `GenerateFrankSemanticDefinitions` MSBuild target

**Purpose**: Auto-invoke shape generation during `dotnet build`.

**Steps**:
1. In `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets`, add:

```xml
<Target Name="GenerateFrankSemanticDefinitions"
        AfterTargets="ResolveAssemblyReferences"
        BeforeTargets="CoreCompile"
        Inputs="@(Compile)"
        Outputs="$(FrankCliOutputPath)shapes.shacl.ttl"
        Condition="'$(FrankCliSkipGeneration)' != 'true'">
  <Exec Command="dotnet frank-cli extract --project &quot;$(MSBuildProjectFullPath)&quot; --emit-artifacts --output &quot;$(FrankCliOutputPath)&quot;"
        IgnoreExitCode="true"
        ConsoleToMSBuild="true">
    <Output TaskParameter="ExitCode" PropertyName="_FrankCliExitCode" />
  </Exec>
  <Warning Condition="'$(_FrankCliExitCode)' != '0'"
           Text="Frank CLI shape generation failed (exit code $(_FrankCliExitCode)). Shapes may be stale. Run 'dotnet tool restore' if frank-cli is not installed." />
</Target>
```

2. Add `FrankCliSkipGeneration` property for opt-out:
   - In `Frank.Cli.MSBuild.props`: `<FrankCliSkipGeneration>false</FrankCliSkipGeneration>`

3. Ensure the target runs BEFORE `EmbedFrankSemanticDefinitions` (which already has `BeforeTargets="CoreCompile"`). MSBuild ordering: `ResolveAssemblyReferences` -> `GenerateFrankSemanticDefinitions` -> `EmbedFrankSemanticDefinitions` -> `CoreCompile`.

**Files**: `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets`, `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.props`
**Notes**: `IgnoreExitCode="true"` + `Warning` ensures the build does not fail if the tool is not installed -- it degrades gracefully. The existing `ValidateFrankSemanticDefinitions` target will warn separately if the artifacts are missing.

### Subtask T062 -- Implement incremental build support

**Purpose**: Skip shape regeneration when source files haven't changed to minimize build time impact.

**Steps**:
1. The `Inputs="@(Compile)"` / `Outputs="$(FrankCliOutputPath)shapes.shacl.ttl"` on the target provides basic incremental support.
2. MSBuild skips the target if all Outputs are newer than all Inputs.
3. Test: build twice without source changes -- second build should skip `GenerateFrankSemanticDefinitions`.
4. Test: modify a `.fs` source file -- next build should regenerate shapes.

**Files**: `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets`
**Notes**: FCS analysis is the expensive operation (~5-15s). Incremental build support is critical-path for developer experience. The `Inputs` should include all F# source files in the project; `@(Compile)` captures this.

### Subtask T063 -- Create integration test for MSBuild pipeline

**Purpose**: Verify the full build-time pipeline: build -> generate -> embed -> verify.

**Steps**:
1. Use an existing sample project (or create a minimal test project) that references Frank.Cli.MSBuild.
2. Run `dotnet build` on the project.
3. Verify the built assembly contains embedded resource `Frank.Semantic.shapes.shacl.ttl`.
4. Read the embedded resource and verify it contains validation-grade SHACL triples (not just basic structural SHACL).
5. Verify incremental build: run `dotnet build` again without changes, confirm shapes are not regenerated (check MSBuild output for target skip message).

**Files**: Test project or script in `test/` or `sample/`
**Validation**: `dotnet build` on the test project succeeds with embedded shapes.

---

## Test Strategy

- Run `dotnet build` on a sample project with Frank.Cli.MSBuild reference
- Verify embedded resource contains validation-grade shapes
- Verify incremental build skips regeneration
- Verify graceful degradation when frank-cli is not installed

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| FCS analysis adds ~5-15s to first build | Incremental build (Inputs/Outputs) ensures subsequent builds skip generation |
| frank-cli not installed | Target uses IgnoreExitCode + Warning; existing ValidateFrankSemanticDefinitions warns about missing artifacts |
| MSBuild target ordering | AfterTargets/BeforeTargets ensures correct sequencing; test with `dotnet build -v diag` to verify order |
| FCS project load requires resolved references | Target runs AfterTargets="ResolveAssemblyReferences" to ensure all NuGet/project references are available |

---

## Review Guidance

- Verify MSBuild target ordering: ResolveAssemblyReferences -> Generate -> Embed -> CoreCompile
- Verify incremental build works (Inputs/Outputs correctly configured)
- Verify graceful degradation when CLI tool is missing (warning, not error)
- Verify `--emit-artifacts` flag reuses serialization logic from CompileCommand (no duplication)
- Verify existing `extract` and `compile` commands still work independently
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-14T00:00:00Z -- system -- lane=planned -- Prompt created from build-time SHACL unification design.
- 2026-03-15T13:53:26Z – claude-opus – shell_pid=8654 – lane=doing – Assigned agent via workflow command
- 2026-03-15T14:09:57Z – claude-opus – shell_pid=8654 – lane=for_review – Ready for review: MSBuild auto-invoke via compile --project, ArtifactSerializer shared module, incremental build, graceful degradation
- 2026-03-15T18:46:28Z – claude-opus – shell_pid=8654 – lane=done – Review feedback addressed: exit codes, FrankCliBaseUri default, incremental build fix, capturing pipeline
- 2026-03-15T19:08:53Z – claude-opus – shell_pid=8654 – lane=for_review – Re-running through spec-kitty review workflow for constitution check
- 2026-03-15T19:08:58Z – claude-opus – shell_pid=26695 – lane=doing – Started review via workflow command
- 2026-03-15T19:12:00Z – claude-opus – shell_pid=26695 – lane=done – Review passed: Constitution check clean. MSBuild auto-invoke target correctly implements GenerateFrankSemanticDefinitions with incremental build, graceful degradation, and proper target ordering. ArtifactSerializer shared module satisfies Constitution VIII (no duplication). CompileCommand.compileFromProject capturing pipeline avoids redundant disk I/O. All previous review feedback items verified as addressed.
- 2026-03-15T19:19:53Z – claude-opus – shell_pid=26695 – lane=for_review – Moved to for_review
- 2026-03-15T19:50:58Z – claude-opus-reviewer – shell_pid=44929 – lane=doing – Started review via workflow command
