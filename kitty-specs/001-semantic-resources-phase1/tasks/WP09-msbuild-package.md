---
work_package_id: WP09
title: Frank.Cli.MSBuild Package
lane: planned
dependencies: [WP06]
subtasks:
- T045
- T046
- T047
- T048
- T049
phase: Phase 1 - Build Integration
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-04T22:10:13Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-012
---

> **Review Feedback Status**: No review feedback yet.

## Review Feedback

_No feedback recorded._

> **Markdown Formatting Note**: All prose in this file uses plain Markdown. Code samples use fenced code blocks with language tags. Lists use `-` bullets.

## Objectives & Success Criteria

Create the MSBuild integration package (`Frank.Cli.MSBuild`) that auto-embeds semantic artifacts from `obj/frank-cli/` into compiled assemblies.

**Success criteria**:
- Content-only NuGet package with no compiled output, only `.props`/`.targets` files
- `EmbedFrankSemanticDefinitions` target runs before `CoreCompile` and embeds the three artifact files using consistent logical names
- Targets are present in both `build/` and `buildTransitive/` to support both direct and indirect consumers
- A validation target emits an MSBuild warning when the package is referenced but no artifacts are present
- Package works correctly for both single-target and multi-target (e.g. `net8.0;net9.0;net10.0`) consuming projects

## Context & Constraints

- This is a content-only NuGet package: no code, only MSBuild `.props`/`.targets` files
- Files in `build/` are auto-imported for direct consumers; `buildTransitive/` for indirect consumers
- Must work with multi-target projects (`net8.0;net9.0;net10.0`)
- Reference `research.md` Decision 3 for MSBuild packaging patterns
- Reference `data-model.md` for artifact naming (`Frank.Semantic.ontology.owl.xml`, `Frank.Semantic.shapes.shacl.ttl`, `Frank.Semantic.manifest.json`)
- Similar to how `Grpc.Tools` works: generate artifacts, auto-include as build items
- Depends on WP06 (CLI Compile command) which generates the artifact files into `obj/frank-cli/`

## Subtasks & Detailed Guidance

### T045 — Frank.Cli.MSBuild.props

**File**: `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.props`

Define the default output path property so consumers can override it if needed:

```xml
<Project>
  <PropertyGroup>
    <FrankCliOutputPath Condition="'$(FrankCliOutputPath)' == ''">$(IntermediateOutputPath)frank-cli/</FrankCliOutputPath>
  </PropertyGroup>
</Project>
```

The `Condition` attribute ensures consumers can set `<FrankCliOutputPath>` in their project file to override the default without having their value overwritten.

---

### T046 — Frank.Cli.MSBuild.targets (build/)

**File**: `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets`

Define the `EmbedFrankSemanticDefinitions` target that fires before compilation and conditionally adds the three artifact files as embedded resources:

```xml
<Project>
  <Target Name="EmbedFrankSemanticDefinitions"
          BeforeTargets="CoreCompile"
          Condition="Exists('$(FrankCliOutputPath)')">
    <ItemGroup>
      <EmbeddedResource Include="$(FrankCliOutputPath)ontology.owl.xml"
                        LogicalName="Frank.Semantic.ontology.owl.xml"
                        Condition="Exists('$(FrankCliOutputPath)ontology.owl.xml')" />
      <EmbeddedResource Include="$(FrankCliOutputPath)shapes.shacl.ttl"
                        LogicalName="Frank.Semantic.shapes.shacl.ttl"
                        Condition="Exists('$(FrankCliOutputPath)shapes.shacl.ttl')" />
      <EmbeddedResource Include="$(FrankCliOutputPath)manifest.json"
                        LogicalName="Frank.Semantic.manifest.json"
                        Condition="Exists('$(FrankCliOutputPath)manifest.json')" />
    </ItemGroup>
  </Target>
</Project>
```

Use `LogicalName` to ensure consistent embedded resource names regardless of the physical file path. This is critical: `Frank.LinkedData` must be able to load resources by a known, stable name at runtime.

---

### T047 — Transitive targets (buildTransitive/)

**File**: `src/Frank.Cli.MSBuild/buildTransitive/Frank.Cli.MSBuild.targets`

Same content as `build/Frank.Cli.MSBuild.targets`. This duplication is intentional: NuGet only flows `buildTransitive/` targets through transitive dependencies, not `build/` targets.

This matters when another library (e.g., `Frank.LinkedData`) depends on `Frank.Cli.MSBuild`: apps that reference `Frank.LinkedData` need the embedding targets to apply even though they don't reference `Frank.Cli.MSBuild` directly.

---

### T048 — Package configuration (.csproj)

**File**: `src/Frank.Cli.MSBuild/Frank.Cli.MSBuild.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <NoBuild>true</NoBuild>
    <IncludeBuildOutput>false</IncludeBuildOutput>
    <PackageId>Frank.Cli.MSBuild</PackageId>
    <Description>MSBuild integration for Frank.Cli: auto-embeds semantic artifacts into compiled assemblies.</Description>
    <!-- Version, Authors, License inherited from Directory.Build.props -->
  </PropertyGroup>

  <ItemGroup>
    <None Include="build/**" Pack="true" PackagePath="build/" />
    <None Include="buildTransitive/**" Pack="true" PackagePath="buildTransitive/" />
  </ItemGroup>
</Project>
```

Key points:
- `NoBuild` and `IncludeBuildOutput` suppress compilation output entirely
- `TargetFramework` is required by the SDK even for content-only packages; `netstandard2.0` is the conventional choice
- The `None` items with `Pack="true"` cause the `.props`/`.targets` files to be included in the package at the correct paths

---

### T049 — Build-time validation target

Add a second target to `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets` (and `buildTransitive/`):

```xml
<Target Name="ValidateFrankSemanticDefinitions"
        BeforeTargets="Build"
        Condition="!Exists('$(FrankCliOutputPath)')">
  <Warning Text="Frank.Cli.MSBuild is referenced but no semantic definitions found in '$(FrankCliOutputPath)'. Run 'frank-cli compile' to generate them." />
</Target>
```

This target fires only when the `FrankCliOutputPath` directory does not exist at all (i.e., `frank-cli compile` has never been run). It emits a non-fatal MSBuild warning so the build still succeeds, but the developer is informed they need to run the CLI step.

## Risks & Mitigations

- **MSBuild target ordering**: `BeforeTargets="CoreCompile"` is correct for embedding files, but verify with multi-target builds where `CoreCompile` runs once per target framework. Test with `<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>` in the consuming project.
- **dotnet restore/clean safety**: Content-only packages must not interfere with restore or clean. The targets only add `EmbeddedResource` items; they do not delete files or modify global state, so clean should be safe.
- **LogicalName conflicts**: If the consuming project happens to have its own embedded resource named `Frank.Semantic.ontology.owl.xml`, there will be a conflict. Document this as a known limitation; it is unlikely in practice.

## Review Guidance

To validate this WP:

1. Create a minimal test project that adds `<PackageReference Include="Frank.Cli.MSBuild" />` (or use a project reference during development)
2. Manually create files in `obj/frank-cli/`: `ontology.owl.xml`, `shapes.shacl.ttl`, `manifest.json`
3. Run `dotnet build` and verify:
   - Build succeeds
   - The compiled assembly contains embedded resources with logical names `Frank.Semantic.ontology.owl.xml`, `Frank.Semantic.shapes.shacl.ttl`, `Frank.Semantic.manifest.json` (use `dotnet-ildasm` or `ILSpy` to inspect)
4. Delete the `obj/frank-cli/` directory and re-run `dotnet build`
   - Build should still succeed
   - A warning should appear: "Frank.Cli.MSBuild is referenced but no semantic definitions found..."
5. Repeat steps 2-4 with a multi-target consuming project (`net8.0;net9.0;net10.0`)
6. Verify `dotnet restore` and `dotnet clean` complete without errors in all scenarios

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-04T22:10:13Z | system | Prompt generated via /spec-kitty.tasks |
