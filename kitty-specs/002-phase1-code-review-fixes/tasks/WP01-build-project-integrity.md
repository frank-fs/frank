---
work_package_id: WP01
title: Build & Project Integrity
lane: "doing"
dependencies: []
base_branch: master
base_commit: 081cc60979e19f9e180b351e104c4fe727eac9c8
created_at: '2026-03-06T17:16:33.761979+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
phase: Phase 1 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "93006"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-06T15:25:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-005, FR-006, FR-013, FR-019, FR-022]
---

# Work Package Prompt: WP01 – Build & Project Integrity

## ⚠️ IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP01
```

No dependencies — this is a starting package.

---

## Objectives & Success Criteria

- `Frank.Cli.MSBuild` is in `Frank.sln` and builds successfully
- `Frank.LinkedData.Sample` uses `ProjectReference` (not NuGet 7.3.0) for `Frank.Cli.MSBuild`
- Zero wildcard (`*`) package versions in any `.fsproj`
- `FSharp.Core` pinned consistently across all projects
- `ValidateFrankSemanticDefinitions` MSBuild target checks specific input artifacts
- `dotnet build Frank.sln` succeeds from clean state

## Context & Constraints

- **Tracking Issue**: #81 (Tier 1: bugs/will break + Tier 2: reproducible builds + Tier 3: MSBuild target)
- **Constitution**: Principle V (Performance Parity) — pinned versions ensure reproducible builds
- **Plan**: `kitty-specs/002-phase1-code-review-fixes/plan.md`
- **Research**: `kitty-specs/002-phase1-code-review-fixes/research.md` — see R7 for version pinning details

## Subtasks & Detailed Guidance

### Subtask T001 – Add Frank.Cli.MSBuild to Frank.sln

- **Purpose**: `dotnet build Frank.sln` currently does not build `Frank.Cli.MSBuild`, yet the sample project depends on it.
- **Steps**:
  1. From repo root, run: `dotnet sln Frank.sln add src/Frank.Cli.MSBuild/Frank.Cli.MSBuild.csproj`
  2. Verify the project appears in `Frank.sln` under the appropriate solution folder
  3. Run `dotnet build Frank.sln` to confirm no errors
- **Files**: `Frank.sln`
- **Parallel?**: Yes — independent of T003-T005

### Subtask T002 – Fix Sample ProjectReference for Frank.Cli.MSBuild

- **Purpose**: `Frank.LinkedData.Sample` references `Frank.Cli.MSBuild` as NuGet 7.3.0, which doesn't exist on any feed. Must be a `ProjectReference` for local development.
- **Steps**:
  1. Open `sample/Frank.LinkedData.Sample/Frank.LinkedData.Sample.fsproj`
  2. Find the `PackageReference` for `Frank.Cli.MSBuild` version `7.3.0`
  3. Replace with: `<ProjectReference Include="../../src/Frank.Cli.MSBuild/Frank.Cli.MSBuild.csproj" />`
  4. Verify `dotnet build sample/Frank.LinkedData.Sample/` succeeds
- **Files**: `sample/Frank.LinkedData.Sample/Frank.LinkedData.Sample.fsproj`
- **Parallel?**: Yes — independent of T003-T005
- **Notes**: The MSBuild targets/props from `Frank.Cli.MSBuild` should still be imported correctly via `ProjectReference` since the `.csproj` defines `buildTransitive/` content.

### Subtask T003 – Pin Wildcard Package Versions

- **Purpose**: Floating wildcard versions (`43.10.*`, `0.74.*`) cause non-reproducible builds. Constitution requires reproducibility.
- **Steps**:
  1. Open `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
  2. Find current wildcard references:
     - `FSharp.Compiler.Service` `43.10.*`
     - `Ionide.ProjInfo` `0.74.*`
     - `Ionide.ProjInfo.FCS` `0.74.*`
  3. Resolve latest stable versions: `dotnet list src/Frank.Cli.Core/ package --outdated`
  4. Replace each wildcard with the resolved specific version (e.g., `43.10.0` → exact version)
  5. Run `dotnet restore` and `dotnet build` to verify compatibility
- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
- **Parallel?**: Yes — independent of T001-T002
- **Notes**: Also check `System.CommandLine` if it uses a wildcard. Pin it if so.

### Subtask T004 – Normalize FSharp.Core Pinning

- **Purpose**: Inconsistent `FSharp.Core` versions across projects (10.0.101 in some, 10.0.103 in others). Must be uniform.
- **Steps**:
  1. Search all `.fsproj` files for `FSharp.Core` references: `grep -rn "FSharp.Core" src/ test/ sample/ --include="*.fsproj"`
  2. Also check `src/Directory.Build.props` and any `Directory.Packages.props` for centralized version management
  3. Pick the latest version currently in use (likely `10.0.103`)
  4. Update all projects to use the same version
  5. If using central package management, update it there; otherwise update each `.fsproj`
  6. Run `dotnet build Frank.sln` to verify
- **Files**: All `.fsproj` files with `FSharp.Core` references, `src/Directory.Build.props`
- **Parallel?**: Yes — independent of T001-T003

### Subtask T005 – Fix ValidateFrankSemanticDefinitions MSBuild Target

- **Purpose**: The MSBuild target fires on every clean build because it checks the output directory (which always changes). Should check specific input artifacts to avoid unnecessary re-validation.
- **Steps**:
  1. Open `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets`
  2. Find the `ValidateFrankSemanticDefinitions` target
  3. Add `Inputs` and `Outputs` attributes to the target:
     - `Inputs`: The specific semantic definition source files (e.g., `$(FrankCliOutputPath)/ontology.owl.xml`, `$(FrankCliOutputPath)/shapes.shacl.ttl`, `$(FrankCliOutputPath)/manifest.json`)
     - `Outputs`: A sentinel file or the embedded resources themselves
  4. This ensures the target is incremental — only runs when inputs change
  5. Also update `buildTransitive/Frank.Cli.MSBuild.targets` if it mirrors the same target
  6. Test: build twice in a row — second build should skip the validation target
- **Files**:
  - `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets`
  - `src/Frank.Cli.MSBuild/buildTransitive/Frank.Cli.MSBuild.targets` (if applicable)
- **Parallel?**: Yes — independent of all others

## Risks & Mitigations

- **Version pinning compatibility**: Pinned versions may have subtle incompatibilities. Run full build and test suite after all changes.
- **ProjectReference vs PackageReference behavior**: MSBuild targets/props from `ProjectReference` may behave differently than from NuGet. Verify `EmbedFrankSemanticDefinitions` target still fires correctly in the sample project.
- **FSharp.Core version**: Choosing the wrong version could break compilation. Use the latest version already in the project.

## Review Guidance

- Verify `dotnet build Frank.sln` succeeds from clean state
- Verify no `*` wildcards remain in any `.fsproj`
- Verify sample builds and MSBuild targets work correctly
- Check that `ValidateFrankSemanticDefinitions` is incremental (second build skips it)

## Activity Log

- 2026-03-06T15:25:00Z – system – lane=planned – Prompt created.
- 2026-03-06T17:16:33Z – claude-opus – shell_pid=85622 – lane=doing – Assigned agent via workflow command
- 2026-03-06T18:54:28Z – claude-opus – shell_pid=85622 – lane=for_review – Ready for review: All 5 subtasks complete. Zero wildcards, Frank.Cli.MSBuild in sln, sample uses ProjectReference, MSBuild targets incremental. 227 tests pass.
- 2026-03-06T19:59:25Z – claude-opus – shell_pid=93006 – lane=doing – Started review via workflow command
