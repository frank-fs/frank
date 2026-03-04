---
work_package_id: WP01
title: Project Scaffolding
lane: "doing"
dependencies: []
base_branch: master
base_commit: ef8ffa5d959f6fb918a4930155826d47490f826f
created_at: '2026-03-04T22:55:37.489789+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
- T006
- T007
phase: Phase 0 - Setup
assignee: ''
agent: "claude-opus"
shell_pid: "18978"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-04T22:10:13Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001]
---

# WP01: Project Scaffolding

## Implementation Command

```
spec-kitty implement WP01
```

## Objectives

Create all new .fsproj files, configure NuGet dependencies, wire project references, update Frank.sln. All projects build with `dotnet build`.

## Context

- Follow existing Frank conventions from `src/Directory.Build.props` (VersionPrefix, multi-target net8.0;net9.0;net10.0 for libraries)
- Test projects target net10.0 only, use Expecto 10.x + YoloDev.Expecto.TestSdk 0.14.x + Microsoft.NET.Test.Sdk 17.x
- Reference existing .fsproj files as patterns (Frank.Auth, Frank.OpenApi, Frank.Tests)
- The project layout is defined in plan.md's Source Code section

## Subtask Details

### T001: Create Frank.Cli.Core.fsproj

- Multi-target net8.0;net9.0;net10.0 (or net10.0 only if FCS requires it — verify whether FSharp.Compiler.Service 43.10.* supports multi-targeting before committing to one approach)
- NuGet dependencies:
  - `dotNetRdf.Core` 3.5.1
  - `dotNetRdf.Ontology` 3.5.1
  - `dotNetRdf.Shacl` 3.5.1
  - `FSharp.Compiler.Service` 43.10.*
  - `Ionide.ProjInfo` 0.74.*
  - `Ionide.ProjInfo.FCS` (matching version)
- Create empty placeholder .fs files matching the source structure: `Rdf/`, `Extraction/`, `Analysis/`, `State/`, `Commands/` subdirectories
- Add a minimal `Module.fs` or `AssemblyInfo.fs` so the project compiles immediately
- Ensure the project file includes all placeholder .fs files in correct compile order

### T002: Create Frank.Cli.fsproj (dotnet tool)

- Target net10.0 (console app, single target)
- Set `<PackAsTool>true</PackAsTool>` and `<ToolCommandName>frank-cli</ToolCommandName>`
- Add project reference to Frank.Cli.Core
- NuGet dependency: `System.CommandLine` (latest stable)
- Create placeholder `Program.fs` with a minimal entry point (e.g., `[<EntryPoint>] let main _ = 0`)
- Verify `dotnet run` produces no errors

### T003: Create Frank.Cli.MSBuild.csproj

- This is a content-only package — no compiled code, just MSBuild .props/.targets files
- Use `<None Include="build/**" Pack="true" PackagePath="build/" />` pattern for packaging
- Also package `buildTransitive/` directory using the same pattern
- Create empty placeholder files: `build/Frank.Cli.MSBuild.props`, `build/Frank.Cli.MSBuild.targets`, `buildTransitive/Frank.Cli.MSBuild.props`
- Target framework: use `<TargetFramework>netstandard2.0</TargetFramework>` for broadest compatibility, or omit entirely if MSBuild SDK allows it
- Set `<IsPackable>true</IsPackable>`, `<IncludeBuildOutput>false</IncludeBuildOutput>`, `<ContentTargetFolders>content</ContentTargetFolders>`

### T004: Create Frank.LinkedData.fsproj

- Multi-target net8.0;net9.0;net10.0 (matching Frank library convention)
- NuGet dependency: `dotNetRdf.Core` 3.5.1
- Project reference to Frank core: `src/Frank/Frank.fsproj`
- Create empty placeholder .fs files per plan.md structure (e.g., `LinkedData.fs`, `ResourceExtensions.fs`, `Serialization.fs`)
- Follow Frank.Auth and Frank.OpenApi as structural patterns (look at their .fsproj and file organization)
- Set packaging properties consistent with other Frank libraries (GeneratePackageOnBuild, etc.)

### T005: Create test .fsproj files

Create two test projects:

**Frank.Cli.Core.Tests.fsproj**:
- Target: net10.0
- NuGet: `Expecto` 10.*, `YoloDev.Expecto.TestSdk` 0.14.*, `Microsoft.NET.Test.Sdk` 17.*
- Project reference to Frank.Cli.Core
- Properties: `<GenerateProgramFile>false</GenerateProgramFile>`, `<IsTestProject>true</IsTestProject>`, `<IsPackable>false</IsPackable>`
- Create placeholder `Program.fs` with Expecto entry point using `[<EntryPoint>] let main args = runTestsWithCLIArgs [] args [||]`

**Frank.LinkedData.Tests.fsproj**:
- Same test dependencies as above
- Project reference to Frank.LinkedData
- Additional NuGet: `Microsoft.AspNetCore.TestHost` 10.0.0-preview.* (for integration tests using TestHost pattern consistent with Frank.Tests)
- Same properties: GenerateProgramFile=false, IsTestProject=true, IsPackable=false
- Create placeholder `Program.fs` with Expecto entry point

Both projects need at least one passing test (e.g., a trivial `testCase "placeholder" <| fun _ -> ()`) so `dotnet test` succeeds.

### T006: Create sample .fsproj

**Frank.LinkedData.Sample.fsproj**:
- Target: net10.0 (samples use single target)
- Project references: Frank core (`src/Frank/Frank.fsproj`) and Frank.LinkedData
- Properties: `<IsPackable>false</IsPackable>`, `<IsPublishable>false</IsPublishable>`
- Create placeholder `Program.fs` with a minimal ASP.NET Core host that starts up without error

### T007: Update Frank.sln

- Add all new projects to the solution using `dotnet sln add` commands:
  ```
  dotnet sln Frank.sln add src/Frank.Cli.Core/Frank.Cli.Core.fsproj
  dotnet sln Frank.sln add src/Frank.Cli/Frank.Cli.fsproj
  dotnet sln Frank.sln add src/Frank.Cli.MSBuild/Frank.Cli.MSBuild.csproj
  dotnet sln Frank.sln add src/Frank.LinkedData/Frank.LinkedData.fsproj
  dotnet sln Frank.sln add test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj
  dotnet sln Frank.sln add test/Frank.LinkedData.Tests/Frank.LinkedData.Tests.fsproj
  dotnet sln Frank.sln add samples/Frank.LinkedData.Sample/Frank.LinkedData.Sample.fsproj
  ```
- After adding, verify with `dotnet build Frank.sln` — all projects must compile successfully
- Adjust paths based on actual directory layout from plan.md

## Risks

- FSharp.Compiler.Service (FCS) 43.10.* may not support multi-targeting — if so, fall back to net10.0 only for Frank.Cli.Core
- Ionide.ProjInfo version compatibility with FCS version — ensure they are aligned
- dotNetRdf 3.5.1 NuGet availability — confirm on nuget.org before writing the project files

## Review Guidance

- Verify all projects compile with zero errors: `dotnet build Frank.sln`
- Run `dotnet test` and confirm placeholder tests pass
- Confirm NuGet package restore succeeds (no missing packages)
- Check project references are bidirectionally correct (no circular refs)
- Verify solution file opens and loads all projects

## Activity Log

- 2026-03-04T22:55:37Z – claude-opus – shell_pid=18978 – lane=doing – Assigned agent via workflow command
