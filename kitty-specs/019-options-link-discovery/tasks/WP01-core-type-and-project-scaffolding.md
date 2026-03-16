---
work_package_id: WP01
title: Core Type and Project Scaffolding
lane: "doing"
dependencies: []
base_branch: master
base_commit: 7b7d58d7a253edee23d92878e00801326a122534
created_at: '2026-03-16T04:02:38.594495+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
phase: Phase 0 - Setup
assignee: ''
agent: ''
shell_pid: "98469"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-16T01:20:58Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-003]
---

# Work Package Prompt: WP01 -- Core Type and Project Scaffolding

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

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````xml`

---

## Objectives & Success Criteria

- Add the `DiscoveryMediaType` struct record type to Frank core (`src/Frank/Builder.fs`).
- Scaffold the new `Frank.Discovery` library project with correct multi-targeting and project references.
- Scaffold the new `Frank.Discovery.Tests` test project with Expecto + TestHost.
- Add both new projects to `Frank.sln`.
- `dotnet build` succeeds for the entire solution.

## Context & Constraints

- **Spec**: `kitty-specs/019-options-link-discovery/spec.md`
- **Plan**: `kitty-specs/019-options-link-discovery/plan.md` -- AD-01 defines where `DiscoveryMediaType` lives
- **Data Model**: `kitty-specs/019-options-link-discovery/data-model.md` -- defines the struct's fields
- **Research**: `kitty-specs/019-options-link-discovery/research.md` -- R-05 explains struct design rationale
- **Constitution**: `.kittify/memory/constitution.md` -- Principle V (Performance Parity) mandates struct for zero allocation

**Implementation command**: `spec-kitty implement WP01`

---

## Subtasks & Detailed Guidance

### Subtask T001 -- Add `DiscoveryMediaType` struct to `src/Frank/Builder.fs`

- **Purpose**: Define the shared endpoint metadata type that extensions use to advertise their supported media types. This type must live in Frank core so that extension packages (Frank.LinkedData, Frank.Statecharts, Frank.Discovery) can all reference it without cross-dependencies.

- **Steps**:
  1. Open `src/Frank/Builder.fs`.
  2. Add the following struct record type definition. Place it **after** the `Resource` struct type (line ~16) and **before** the `ResourceSpec` type (line ~18), keeping it near the other endpoint-related types:
     ```fsharp
     /// Media type metadata for HTTP discovery (OPTIONS + Link headers).
     /// Extensions add instances to endpoint metadata to advertise supported content types.
     [<Struct>]
     type DiscoveryMediaType =
         { /// The content type string (e.g., "application/ld+json", "text/turtle").
           MediaType: string
           /// The link relation type for Link header generation (e.g., "describedby").
           Rel: string }
     ```
  3. Verify the type compiles: `dotnet build src/Frank/Frank.fsproj`

- **Files**: `src/Frank/Builder.fs`
- **Parallel?**: No -- this must be done first as other subtasks depend on this type existing.
- **Notes**: The struct has exactly two fields. No `Option` types -- `Rel` always has a value (per research R-05). No factory methods or validation -- extensions are responsible for providing valid values.

### Subtask T002 -- Create `src/Frank.Discovery/Frank.Discovery.fsproj`

- **Purpose**: Create the new extension package project file following established patterns.

- **Steps**:
  1. Create directory `src/Frank.Discovery/`.
  2. Create `Frank.Discovery.fsproj` with:
     ```xml
     <Project Sdk="Microsoft.NET.Sdk">

       <PropertyGroup>
         <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
         <PackageTags>discovery;options;link;rfc8288</PackageTags>
         <Description>HTTP discovery extensions for Frank web framework (OPTIONS responses and RFC 8288 Link headers)</Description>
       </PropertyGroup>

       <ItemGroup>
         <!-- Compile items will be added in WP02 and WP03 -->
       </ItemGroup>

       <ItemGroup>
         <ProjectReference Include="../Frank/Frank.fsproj" />
       </ItemGroup>

       <ItemGroup>
         <FrameworkReference Include="Microsoft.AspNetCore.App" />
       </ItemGroup>

     </Project>
     ```
  3. The `ItemGroup` for `Compile` items is intentionally empty -- source files will be added by WP02 and WP03.
  4. The `FrameworkReference` to `Microsoft.AspNetCore.App` provides access to `IApplicationBuilder`, `HttpContext`, `EndpointDataSource`, etc.
  5. Verify: `dotnet build src/Frank.Discovery/Frank.Discovery.fsproj`

- **Files**: `src/Frank.Discovery/Frank.Discovery.fsproj`
- **Parallel?**: No -- subsequent subtasks reference this project.
- **Notes**: Follow the exact pattern from `src/Frank.Auth/Frank.Auth.fsproj`. The `TargetFrameworks` must match Frank core's multi-targeting (`net8.0;net9.0;net10.0`). The `PackageTags` and `Description` are picked up by `src/Directory.Build.props` for NuGet packaging. No `OutputType` needed (inherited as `Library` from `Directory.Build.props`).

### Subtask T003 -- Create `test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`

- **Purpose**: Scaffold the test project following the Frank.Auth.Tests pattern.

- **Steps**:
  1. Create directory `test/Frank.Discovery.Tests/`.
  2. Create `Frank.Discovery.Tests.fsproj` with:
     ```xml
     <Project Sdk="Microsoft.NET.Sdk">

       <PropertyGroup>
         <OutputType>Exe</OutputType>
         <TargetFramework>net10.0</TargetFramework>
         <IsPackable>false</IsPackable>
         <IsTestProject>true</IsTestProject>
         <GenerateProgramFile>false</GenerateProgramFile>
       </PropertyGroup>

       <ItemGroup>
         <Compile Include="Program.fs" />
       </ItemGroup>

       <ItemGroup>
         <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
         <PackageReference Include="Expecto" Version="10.2.3" />
         <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.3" />
         <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0" />
       </ItemGroup>

       <ItemGroup>
         <ProjectReference Include="../../src/Frank.Discovery/Frank.Discovery.fsproj" />
       </ItemGroup>

     </Project>
     ```
  3. The `Compile` items currently only include `Program.fs`. Test source files will be added in WP02 (OptionsDiscoveryTests.fs), WP03 (LinkHeaderTests.fs), and WP04 (EdgeCaseTests.fs).
  4. Package versions match `test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj` exactly.

- **Files**: `test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`
- **Parallel?**: No -- depends on T002 for the project reference to exist.
- **Notes**: `GenerateProgramFile` is `false` because we provide an explicit `Program.fs` with the Expecto entry point. `IsPackable` is `false` for test projects. Single target `net10.0` (test projects don't need multi-targeting).

### Subtask T004 -- Create `test/Frank.Discovery.Tests/Program.fs`

- **Purpose**: Provide the Expecto test runner entry point.

- **Steps**:
  1. Create `test/Frank.Discovery.Tests/Program.fs` with:
     ```fsharp
     module Frank.Discovery.Tests.Program

     open Expecto

     [<EntryPoint>]
     let main args = runTestsInAssemblyWithCLIArgs [] args
     ```
  2. Verify: `dotnet build test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`

- **Files**: `test/Frank.Discovery.Tests/Program.fs`
- **Parallel?**: No -- depends on T003.
- **Notes**: This follows the exact pattern from `test/Frank.Auth.Tests/Program.fs`.

### Subtask T005 -- Add projects to `Frank.sln`

- **Purpose**: Register both new projects in the solution file so they are included in solution-wide builds and IDE discovery.

- **Steps**:
  1. From the repository root, run:
     ```bash
     dotnet sln Frank.sln add src/Frank.Discovery/Frank.Discovery.fsproj --solution-folder src
     dotnet sln Frank.sln add test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj --solution-folder test
     ```
  2. Verify: `dotnet build Frank.sln`
  3. Verify: `dotnet test test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj` (should pass with 0 tests since no test files exist yet).

- **Files**: `Frank.sln`
- **Parallel?**: No -- depends on T002-T004.
- **Notes**: The `--solution-folder` flag places projects under the correct solution folder nodes (`src` and `test`), matching the existing convention in the `.sln` file.

---

## Risks & Mitigations

- **Risk**: `DiscoveryMediaType` struct position in Builder.fs affects F# compilation order (F# files are order-dependent). **Mitigation**: Place it between `Resource` and `ResourceSpec` since no existing type depends on it, and downstream types (`ResourceSpec`, `ResourceBuilder`) may want to reference it in the future.
- **Risk**: Solution file GUIDs may conflict. **Mitigation**: `dotnet sln add` auto-generates unique GUIDs.
- **Risk**: Package version drift. **Mitigation**: Match exact versions from Frank.Auth.Tests.

## Review Guidance

- Verify `DiscoveryMediaType` is a `[<Struct>]` record (not a class, not a DU).
- Verify the `.fsproj` multi-targeting matches Frank core (`net8.0;net9.0;net10.0`).
- Verify test project targets only `net10.0` (matching other test projects).
- Verify `dotnet build Frank.sln` succeeds after all changes.
- Check that `Frank.sln` has the new projects in the correct solution folders.

## Activity Log

- 2026-03-16T01:20:58Z -- system -- lane=planned -- Prompt created.
