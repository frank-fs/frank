---
work_package_id: WP09
title: MSBuild Target for Binary Embedding
lane: "done"
dependencies: [WP03]
base_branch: 031-unified-resource-pipeline-WP03
base_commit: 80c92fa5cc9a58f84715b0f763f317b04ae09435
created_at: '2026-03-19T03:41:38.596500+00:00'
subtasks:
- T052
- T053
- T054
- T055
- T056
phase: Phase 2 - Runtime
assignee: ''
agent: "claude-opus-wp09"
shell_pid: "21017"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-013
---

# Work Package Prompt: WP09 -- MSBuild Target for Binary Embedding

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````python`, ````bash`

---

## Implementation Command

Depends on WP03 (cache format / binary serialization):

```bash
spec-kitty implement WP09 --base WP03
```

**NOTE**: WP09 can run in parallel with WP04-WP08 since it only depends on WP03 (cache format). It does not depend on WP05 (ALPS generation), WP06 (affordance map), WP07 (middleware), or WP08 (startup projection). This is an independent work stream that provides the embedding mechanism those components consume.

---

## Objectives & Success Criteria

1. Create `Frank.Affordances.MSBuild` project (C# .csproj for MSBuild targets/props packaging).
2. Create `Frank.Affordances.targets` that auto-discovers `obj/frank-cli/unified-state.bin` and adds it as `<EmbeddedResource>`.
3. Wire the target to run after Build (only embeds if the binary exists).
4. Verify embedding works end-to-end: extract, build, read embedded resource via `Assembly.GetManifestResourceStream()`.
5. Document the NuGet packaging strategy for distributing the MSBuild target.

**Success**: After running `frank-cli extract --project <fsproj>` and then `dotnet build`, the compiled assembly contains `Frank.Affordances.unified-state.bin` as an embedded resource accessible via `Assembly.GetManifestResourceStream()`.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` -- FR-013 (binary embedded resource via MSBuild target)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` -- Project Structure (`src/Frank.Affordances.MSBuild/`)
- **Clarification**: "Same pattern as existing semantic artifacts (`Frank.Semantic.ontology.owl.xml`). No resx -- use `<EmbeddedResource>` directly."
- **Binary file location**: `obj/frank-cli/unified-state.bin` (produced by the CLI extraction, WP03)
- **Logical name**: `Frank.Affordances.unified-state.bin` (used by the runtime to locate the resource via `Assembly.GetManifestResourceStream`)
- **MSBuild target distribution**: The target must auto-activate when the `Frank.Affordances.MSBuild` NuGet package is installed. This requires placing the `.targets` file in `buildTransitive/` in the NuGet package layout.
- **No forced re-extraction**: The target only embeds the binary IF it exists. If the developer hasn't run `frank-cli extract`, the build succeeds without an embedded resource (the runtime middleware degrades gracefully per WP07 T042).

---

## Subtasks & Detailed Guidance

### Subtask T052 -- Create `Frank.Affordances.MSBuild` Project

- **Purpose**: Create a C# project that packages MSBuild targets and props for NuGet distribution. This project contains NO code -- only MSBuild files.
- **Steps**:
  1. Create directory: `src/Frank.Affordances.MSBuild/`
  2. Create `src/Frank.Affordances.MSBuild/Frank.Affordances.MSBuild.csproj`:
     ```xml
     <Project Sdk="Microsoft.NET.Sdk">
       <PropertyGroup>
         <TargetFramework>netstandard2.0</TargetFramework>
         <!-- This project produces no assembly -- it's a build-time-only package -->
         <IncludeBuildOutput>false</IncludeBuildOutput>
         <SuppressDependenciesWhenPacking>true</SuppressDependenciesWhenPacking>
         <GeneratePackageOnBuild>true</GeneratePackageOnBuild>
         <DevelopmentDependency>true</DevelopmentDependency>
         <NoPackageAnalysis>true</NoPackageAnalysis>
         <!-- Package metadata -->
         <PackageId>Frank.Affordances.MSBuild</PackageId>
         <Description>MSBuild targets for embedding Frank unified state binary into assemblies</Description>
       </PropertyGroup>

       <ItemGroup>
         <!-- Pack the .targets file into buildTransitive/ so it auto-imports for all consuming projects -->
         <None Include="build/Frank.Affordances.MSBuild.targets" Pack="true" PackagePath="buildTransitive/" />
         <None Include="build/Frank.Affordances.MSBuild.targets" Pack="true" PackagePath="build/" />
       </ItemGroup>
     </Project>
     ```
  3. Create the `build/` directory for the targets file:
     ```bash
     mkdir -p src/Frank.Affordances.MSBuild/build
     ```
  4. The project targets `netstandard2.0` (convention for MSBuild-only packages) and produces no assembly output.

- **Files**: `src/Frank.Affordances.MSBuild/Frank.Affordances.MSBuild.csproj` (NEW, ~25 lines), `src/Frank.Affordances.MSBuild/build/` directory
- **Notes**:
  - C# is used for the project (not F#) because MSBuild target packages are conventionally C# projects. The project contains no C# code -- only MSBuild files.
  - `DevelopmentDependency=true` marks this as a build-time-only dependency (not shipped as a runtime dependency of consuming projects).
  - `IncludeBuildOutput=false` prevents an empty DLL from being included in the package.
  - Both `build/` and `buildTransitive/` paths are included so the target works for both direct and transitive package consumers.

### Subtask T053 -- Create `Frank.Affordances.targets`

- **Purpose**: Define the MSBuild target that auto-discovers the unified state binary and adds it as an `<EmbeddedResource>` with the logical name expected by the runtime.
- **Steps**:
  1. Create `src/Frank.Affordances.MSBuild/build/Frank.Affordances.MSBuild.targets`:
     ```xml
     <Project>
       <!-- Property: path to the unified state binary produced by frank-cli -->
       <PropertyGroup>
         <FrankUnifiedStatePath Condition="'$(FrankUnifiedStatePath)' == ''">$(BaseIntermediateOutputPath)frank-cli/unified-state.bin</FrankUnifiedStatePath>
         <FrankUnifiedStateLogicalName Condition="'$(FrankUnifiedStateLogicalName)' == ''">Frank.Affordances.unified-state.bin</FrankUnifiedStateLogicalName>
       </PropertyGroup>

       <!-- Target: embed the unified state binary if it exists -->
       <Target Name="EmbedFrankUnifiedState"
               BeforeTargets="CoreCompile"
               Condition="Exists('$(FrankUnifiedStatePath)')">
         <Message Importance="normal"
                  Text="Frank: Embedding unified state from '$(FrankUnifiedStatePath)' as '$(FrankUnifiedStateLogicalName)'" />
         <ItemGroup>
           <EmbeddedResource Include="$(FrankUnifiedStatePath)"
                             LogicalName="$(FrankUnifiedStateLogicalName)"
                             Link="frank-cli/unified-state.bin" />
         </ItemGroup>
       </Target>

       <!-- Target: warn if the binary doesn't exist (informational only) -->
       <Target Name="WarnMissingFrankUnifiedState"
               BeforeTargets="CoreCompile"
               Condition="!Exists('$(FrankUnifiedStatePath)')">
         <Message Importance="normal"
                  Text="Frank: No unified state binary found at '$(FrankUnifiedStatePath)'. Run 'frank-cli extract --project $(MSBuildProjectFullPath)' to generate it. Affordance headers will not be available at runtime." />
       </Target>
     </Project>
     ```
  2. Key design decisions:
     - **`BeforeTargets="CoreCompile"`**: The embedded resource must be added before compilation so the compiler includes it in the output assembly.
     - **Condition guard**: `Exists('$(FrankUnifiedStatePath)')` ensures the target is a no-op if the binary doesn't exist. No build failure, just an informational message.
     - **Overridable properties**: Both `FrankUnifiedStatePath` and `FrankUnifiedStateLogicalName` can be overridden in the consuming project's `.fsproj`/`.csproj` if the developer uses a non-standard path.
     - **`$(BaseIntermediateOutputPath)`**: This MSBuild property resolves to `obj/` by default, making the full path `obj/frank-cli/unified-state.bin`.

- **Files**: `src/Frank.Affordances.MSBuild/build/Frank.Affordances.MSBuild.targets` (NEW, ~25 lines)
- **Notes**:
  - The `LogicalName` attribute is critical -- it determines the name used in `Assembly.GetManifestResourceStream()`. Without it, the embedded resource name would be derived from the file path, which varies by project structure.
  - The `Link` attribute controls how the embedded resource appears in the Solution Explorer (visual only, does not affect the logical name).
  - The `Condition` attribute makes this zero-impact for projects that haven't run `frank-cli extract`. The build proceeds normally.
  - `Message Importance="normal"` means the message appears during build with normal verbosity. Use `"high"` if you want it visible at minimal verbosity.

### Subtask T054 -- Wire Target Execution Timing

- **Purpose**: Ensure the `EmbedFrankUnifiedState` target runs at the correct point in the build pipeline and integrates cleanly with incremental builds.
- **Steps**:
  1. Verify `BeforeTargets="CoreCompile"` is the correct timing:
     - `CoreCompile` is when the compiler runs. Embedded resources must be in the `EmbeddedResource` item group before this target.
     - Alternative: `BeforeTargets="PrepareForBuild"` runs earlier. Either works, but `CoreCompile` is more conventional for resource embedding.
  2. Add incremental build support with `Inputs`/`Outputs`:
     ```xml
     <Target Name="EmbedFrankUnifiedState"
             BeforeTargets="CoreCompile"
             Condition="Exists('$(FrankUnifiedStatePath)')"
             Inputs="$(FrankUnifiedStatePath)"
             Outputs="$(IntermediateOutputPath)frank-unified-state.stamp">
       <!-- Touch a stamp file for incremental build tracking -->
       <Touch Files="$(IntermediateOutputPath)frank-unified-state.stamp" AlwaysCreate="true" />
       <ItemGroup>
         <EmbeddedResource Include="$(FrankUnifiedStatePath)"
                           LogicalName="$(FrankUnifiedStateLogicalName)"
                           Link="frank-cli/unified-state.bin" />
       </ItemGroup>
     </Target>
     ```
  3. Verify the target does NOT run during `dotnet restore` (restore phase should not execute custom targets).
  4. Test with `dotnet build --verbosity diagnostic` to see target execution order and confirm `EmbedFrankUnifiedState` runs before `CoreCompile`.

- **Files**: `src/Frank.Affordances.MSBuild/build/Frank.Affordances.MSBuild.targets` (refines T053)
- **Notes**:
  - Incremental build support is optional for the first version. The binary file is small (~10-100KB), so re-embedding on every build is acceptable.
  - If using `Inputs`/`Outputs`, the stamp file approach ensures the target re-runs when the binary changes (frank-cli re-extracted).
  - Do NOT use `AfterTargets="Build"` -- by then, compilation is complete and the resource would not be embedded. `BeforeTargets="CoreCompile"` is correct.

### Subtask T055 -- Verify End-to-End Embedding

- **Purpose**: Create a test project that verifies the full workflow: extract -> build -> read embedded resource.
- **Steps**:
  1. Create a test project or use the existing test infrastructure:
     ```fsharp
     // Test: verify embedded resource is accessible
     [<Test>]
     let ``unified state binary is accessible via GetManifestResourceStream`` () =
         let assembly = Assembly.GetExecutingAssembly()
         use stream = assembly.GetManifestResourceStream("Frank.Affordances.unified-state.bin")
         Expect.isNotNull stream "Embedded resource should exist"
         Expect.isGreaterThan stream.Length 0L "Embedded resource should have content"
     ```
  2. Set up the test workflow:
     a. Create a minimal F# project that references `Frank.Affordances.MSBuild` (or includes the targets file directly for testing).
     b. Place a test `unified-state.bin` file in `obj/frank-cli/` of the test project.
     c. Build the test project.
     d. Run the test to verify `GetManifestResourceStream` returns the binary.
  3. Verify the embedded resource appears in the assembly:
     ```bash
     # After building, inspect the assembly's embedded resources
     dotnet run --project test/Frank.Affordances.Tests/ -- list-resources
     # Or use:
     dotnet tool install --global ILSpy.Console
     ilspycmd path/to/assembly.dll --list-resources
     ```
  4. Alternative verification using `System.Reflection`:
     ```fsharp
     let names = assembly.GetManifestResourceNames()
     Expect.contains names "Frank.Affordances.unified-state.bin" "Resource name should match logical name"
     ```

- **Files**: Test project (NEW or extend `test/Frank.Affordances.Tests/`), test binary fixture
- **Notes**:
  - The test binary doesn't need to be a real `UnifiedExtractionState` -- any binary file works for verifying the embedding mechanism. Use a small test file (`echo "test" > unified-state.bin`).
  - For a full integration test: serialize a test `UnifiedExtractionState` to MessagePack, place it as the test binary, verify deserialization after embedding.
  - The test project must reference the MSBuild targets. Options:
    - Direct `<Import>` of the targets file (for local testing).
    - PackageReference to the locally-packed NuGet package (more realistic but heavier).
  - For CI, use the direct import approach. For release validation, test with the NuGet package.

### Subtask T056 -- Document NuGet Packaging Strategy

- **Purpose**: Document how the MSBuild target package is structured for NuGet distribution so that consuming projects get automatic embedding.
- **Steps**:
  1. The NuGet package layout:
     ```
     Frank.Affordances.MSBuild.nupkg
     ├── build/
     │   └── Frank.Affordances.MSBuild.targets    # For direct consumers
     ├── buildTransitive/
     │   └── Frank.Affordances.MSBuild.targets    # For transitive consumers
     └── lib/
         └── netstandard2.0/
             └── _._                               # Empty marker (no assembly)
     ```
  2. Document in the project's README or as comments in the .csproj:
     - `build/` targets auto-import for projects that directly reference the package.
     - `buildTransitive/` targets auto-import for projects that transitively reference the package (e.g., a project references `Frank.Affordances` which depends on `Frank.Affordances.MSBuild`).
     - The `lib/netstandard2.0/_._` marker prevents NuGet warnings about empty lib folders.
  3. Add the `_._` marker file:
     ```bash
     mkdir -p src/Frank.Affordances.MSBuild/lib/netstandard2.0
     touch src/Frank.Affordances.MSBuild/lib/netstandard2.0/_._
     ```
  4. Add to the .csproj:
     ```xml
     <ItemGroup>
       <None Include="lib/netstandard2.0/_._" Pack="true" PackagePath="lib/netstandard2.0/" />
     </ItemGroup>
     ```
  5. Test NuGet packaging:
     ```bash
     dotnet pack src/Frank.Affordances.MSBuild/Frank.Affordances.MSBuild.csproj
     # Inspect the package:
     unzip -l src/Frank.Affordances.MSBuild/bin/Release/Frank.Affordances.MSBuild.*.nupkg
     ```
  6. Verify that `Frank.Affordances` package should declare a dependency on `Frank.Affordances.MSBuild`:
     ```xml
     <!-- In Frank.Affordances.fsproj -->
     <ItemGroup>
       <PackageReference Include="Frank.Affordances.MSBuild" Version="$(VersionPrefix)"
                         PrivateAssets="all" />
     </ItemGroup>
     ```
     The `PrivateAssets="all"` ensures the MSBuild package is not a runtime dependency -- it's build-time only.

- **Files**: Documentation as comments in `Frank.Affordances.MSBuild.csproj`, possible `_._` marker file
- **Notes**:
  - This is documentation and packaging configuration, not code. The deliverable is a correctly structured .csproj that produces a valid NuGet package.
  - The `DevelopmentDependency=true` property (from T052) is the NuGet-level equivalent of `PrivateAssets="all"`. Both should be set for belt-and-suspenders safety.
  - If `Frank.Affordances` is published as a NuGet package, it should pull in `Frank.Affordances.MSBuild` automatically so consumers don't need to reference both packages manually.
  - Version pinning: `Frank.Affordances.MSBuild` version should match `Frank.Affordances` version. Use `$(VersionPrefix)` from `Directory.Build.props`.

---

## Test Strategy

- **Build verification**: Create a test project with the MSBuild target imported, place a test binary, build, verify resource exists in the output assembly.
- **NuGet packaging**: Pack the project, inspect the .nupkg layout, verify `build/` and `buildTransitive/` contain the targets file.
- **Negative test**: Build without the binary file present, verify build succeeds with no errors (only informational message).
- **Incremental build**: Build twice with the binary present, verify the second build is incremental (doesn't recompile if nothing changed).

Run tests:
```bash
# Build the MSBuild project
dotnet build src/Frank.Affordances.MSBuild/

# Pack and inspect
dotnet pack src/Frank.Affordances.MSBuild/ --output ./nupkg-test
unzip -l ./nupkg-test/Frank.Affordances.MSBuild.*.nupkg

# Integration test (if test project exists)
dotnet test test/Frank.Affordances.Tests/ --filter "Embedding"
```

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| `$(BaseIntermediateOutputPath)` resolves differently across project types | Default to `obj/` which is standard. Allow override via `FrankUnifiedStatePath` property. |
| MSBuild target doesn't auto-import from NuGet | Verify targets file is in both `build/` and `buildTransitive/` in the package. Test with a fresh project referencing the local package. |
| Embedded resource logical name collision with other Frank packages | Use fully qualified name `Frank.Affordances.unified-state.bin`. The `Frank.Affordances.` prefix scopes it. |
| Large binary files bloating assembly size | The unified state for a typical project (20 resources) should be <100KB in MessagePack binary. Monitor size in integration tests. |
| F# projects handle `<EmbeddedResource>` differently than C# | F# projects support `<EmbeddedResource>` with `LogicalName` identically to C#. Verify with `dotnet build` on an F# consuming project. |

---

## Review Guidance

- Verify the .targets file syntax is valid MSBuild XML.
- Verify `BeforeTargets="CoreCompile"` is the correct timing for embedded resource injection.
- Verify `Condition="Exists(...)"` prevents build failures when the binary is absent.
- Verify the logical name `Frank.Affordances.unified-state.bin` matches what `AffordanceMap.tryLoadFromAssembly` (WP07 T038) expects.
- Verify the NuGet package layout includes both `build/` and `buildTransitive/` directories.
- Verify `DevelopmentDependency=true` and `IncludeBuildOutput=false` are set correctly.
- Verify `dotnet build` and `dotnet pack` succeed cleanly.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

### How to Add Activity Log Entries

**When adding an entry**:
1. Scroll to the bottom of this file (Activity Log section below "Valid lanes")
2. **APPEND the new entry at the END** (do NOT prepend or insert in middle)
3. Use exact format: `- YYYY-MM-DDTHH:MM:SSZ -- agent_id -- lane=<lane> -- <action>`
4. Timestamp MUST be current time in UTC (check with `date -u "+%Y-%m-%dT%H:%M:%SZ"`)
5. Lane MUST match the frontmatter `lane:` field exactly
6. Agent ID should identify who made the change (claude-sonnet-4-5, codex, etc.)

**Format**:
```
- YYYY-MM-DDTHH:MM:SSZ -- <agent_id> -- lane=<lane> -- <brief action description>
```

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

**Initial entry**:
- 2026-03-19T02:15:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-19T03:41:38Z – claude-opus-wp09 – shell_pid=21017 – lane=doing – Assigned agent via workflow command
- 2026-03-19T03:59:15Z – claude-opus-wp09 – shell_pid=21017 – lane=for_review – Ready for review: MSBuild target for binary embedding, NuGet package layout verified, 3 embedding tests passing
- 2026-03-19T03:59:43Z – claude-opus-wp09 – shell_pid=21017 – lane=done – Review passed: MSBuild target verified, NuGet package layout correct (build/+buildTransitive/+lib/), 3 embedding tests pass, overridable properties
