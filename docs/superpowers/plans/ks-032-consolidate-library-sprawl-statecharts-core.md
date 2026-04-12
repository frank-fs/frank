---
source: kitty-specs/032-consolidate-library-sprawl
type: plan
---

# Extract Frank.Statecharts.Core + Doc Comments Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract the shared statechart AST types into a zero-dependency leaf assembly `Frank.Statecharts.Core`, and add doc comments from Wlaschin/Seemann expert feedback.

**Architecture:** Move `Ast/Types.fs` (268 lines) from Frank.Statecharts into a new `Frank.Statecharts.Core` project. Namespace stays `Frank.Statecharts.Ast` — no consumer changes needed (65 files already `open Frank.Statecharts.Ast`). Add a doc comment on `RuntimeStateInfo.Description` explaining the `string` vs `string option` impedance mismatch with `StateInfo.Description`.

**Tech Stack:** F# 8.0+ targeting net8.0;net9.0;net10.0 (multi-targeting).

---

## File Structure

### New files

| File | Responsibility |
|------|---------------|
| `src/Frank.Statecharts.Core/Frank.Statecharts.Core.fsproj` | Project file: multi-target, zero deps |
| `src/Frank.Statecharts.Core/Types.fs` | Moved from `src/Frank.Statecharts/Ast/Types.fs` — all shared AST types |

### Files to modify

| File | Change |
|------|--------|
| `src/Frank.Statecharts/Frank.Statecharts.fsproj` | Remove `<Compile Include="Ast/Types.fs" />`, add ProjectReference to Frank.Statecharts.Core |
| `src/Frank.Resources.Model/Frank.Resources.Model.fsproj` | No change — Frank.Resources.Model has zero deps, doesn't use AST types |
| `src/Frank.Resources.Model/RuntimeTypes.fs` | Add doc comment on `RuntimeStateInfo.Description` |
| `Frank.sln` | Add Frank.Statecharts.Core to solution |
| `build.ps1` | No change needed — Frank.Statecharts.Core has no tests and no NuGet pack (not published separately yet) |
| `.github/workflows/ci.yml` | Add pack step for Frank.Statecharts.Core |

---

### Task 1: Create Frank.Statecharts.Core project and move AST types

**Files:**
- Create: `src/Frank.Statecharts.Core/Frank.Statecharts.Core.fsproj`
- Move: `src/Frank.Statecharts/Ast/Types.fs` → `src/Frank.Statecharts.Core/Types.fs`
- Modify: `src/Frank.Statecharts/Frank.Statecharts.fsproj`

- [ ] **Step 1: Create the project file**

Create `src/Frank.Statecharts.Core/Frank.Statecharts.Core.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>statecharts;ast;parser</PackageTags>
    <Description>Shared statechart AST types for format parsers and generators (zero dependencies)</Description>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Types.fs" />
  </ItemGroup>

</Project>
```

NO PackageReferences, NO ProjectReferences, NO FrameworkReferences. Zero dependencies.

- [ ] **Step 2: Move the AST types file**

Move `src/Frank.Statecharts/Ast/Types.fs` to `src/Frank.Statecharts.Core/Types.fs`. The file keeps its namespace `Frank.Statecharts.Ast` unchanged.

Delete the empty `src/Frank.Statecharts/Ast/` directory after moving.

- [ ] **Step 3: Update Frank.Statecharts.fsproj**

In `src/Frank.Statecharts/Frank.Statecharts.fsproj`:

Remove the Compile entry:
```xml
<!-- Remove this line -->
<Compile Include="Ast/Types.fs" />
```

Add ProjectReference in the existing ProjectReference ItemGroup:
```xml
<ProjectReference Include="../Frank.Statecharts.Core/Frank.Statecharts.Core.fsproj" />
```

- [ ] **Step 4: Verify both projects build**

Run: `dotnet build src/Frank.Statecharts.Core/Frank.Statecharts.Core.fsproj`
Expected: Build succeeds across all 3 TFMs.

Run: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`
Expected: Build succeeds — all 30 files that `open Frank.Statecharts.Ast` resolve via the ProjectReference.

- [ ] **Step 5: Commit**

```bash
git add src/Frank.Statecharts.Core/ src/Frank.Statecharts/
git commit -m "refactor: extract Frank.Statecharts.Core with shared AST types"
```

---

### Task 2: Add doc comment on RuntimeStateInfo.Description

**Files:**
- Modify: `src/Frank.Resources.Model/RuntimeTypes.fs:15-21`

- [ ] **Step 1: Add the doc comment**

In `src/Frank.Resources.Model/RuntimeTypes.fs`, update `RuntimeStateInfo` to add a doc comment on the `Description` field explaining the impedance mismatch:

Replace:
```fsharp
/// Per-state metadata for statechart format generation.
type RuntimeStateInfo =
    { /// HTTP methods allowed in this state
      AllowedMethods: string list
      /// Whether this is a final/terminal state
      IsFinal: bool
      /// Human-readable description (empty string if none)
      Description: string }
```

With:
```fsharp
/// Per-state metadata for statechart format generation.
/// This is a runtime-optimized projection of StateInfo. Key difference:
/// StateInfo.Description is string option (semantically correct — "not provided"
/// differs from "empty"). RuntimeStateInfo.Description is string (flattened via
/// Option.defaultValue "" in StartupProjection for zero-allocation runtime matching).
type RuntimeStateInfo =
    { /// HTTP methods allowed in this state
      AllowedMethods: string list
      /// Whether this is a final/terminal state
      IsFinal: bool
      /// Human-readable description, or empty string if none was provided.
      /// Flattened from StateInfo.Description (string option) for runtime efficiency.
      Description: string }
```

- [ ] **Step 2: Verify build**

Run: `dotnet build src/Frank.Resources.Model/Frank.Resources.Model.fsproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Frank.Resources.Model/RuntimeTypes.fs
git commit -m "docs: add Seemann-inspired doc comment on RuntimeStateInfo.Description impedance mismatch"
```

---

### Task 3: Add to solution, build scripts, and CI

**Files:**
- Modify: `Frank.sln`
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Add to solution**

Run:
```bash
dotnet sln Frank.sln add src/Frank.Statecharts.Core/Frank.Statecharts.Core.fsproj --solution-folder src
```

- [ ] **Step 2: Add pack step to CI**

In `.github/workflows/ci.yml`, add a pack line for Frank.Statecharts.Core in the Pack step (before Frank.LinkedData):

```yaml
          dotnet pack src/Frank.Statecharts.Core -c Release --no-restore --include-symbols -o out
```

- [ ] **Step 3: Verify full solution builds**

Run: `dotnet build Frank.sln`
Expected: 0 errors.

- [ ] **Step 4: Run tests**

Run: `dotnet test test/Frank.Statecharts.Tests`
Expected: All tests pass (AST types resolve via transitive reference through Frank.Statecharts).

Run: `dotnet test test/Frank.Resources.Model.Tests`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add Frank.sln .github/workflows/ci.yml
git commit -m "chore: add Frank.Statecharts.Core to solution and CI"
```

---

### Task 4: Update README dependency graph

**Files:**
- Modify: `README.md`

- [ ] **Step 1: Update the dependency graph**

In `README.md`, update the Package Dependency Graph to show Frank.Statecharts.Core as a zero-dep leaf and Frank.Statecharts depending on it:

Replace:
```
├── Frank.Statecharts ─────────── Frank + Frank.Resources.Model
│   └── WSD, ALPS, SCXML, smcat, XState parsers/generators
│   └── Cross-format validation pipeline
│   └── Affordance middleware (Link + Allow headers per state)
```

With:
```
├── Frank.Statecharts.Core ────── (zero dependencies)
│   └── Shared statechart AST (StatechartDocument, StateNode, TransitionEdge, Annotation, ParseResult)
│
├── Frank.Statecharts ─────────── Frank + Frank.Resources.Model + Frank.Statecharts.Core
│   └── WSD, ALPS, SCXML, smcat, XState parsers/generators
│   └── Cross-format validation pipeline
│   └── Affordance middleware (Link + Allow headers per state)
```

Also add Frank.Statecharts.Core to the packages table:

```
| **Frank.Statecharts.Core** | Shared statechart AST types for format parsers and generators (zero dependencies) | [![NuGet](https://img.shields.io/nuget/v/Frank.Statecharts.Core)](https://www.nuget.org/packages/Frank.Statecharts.Core/) |
```

Insert this row immediately before the Frank.Statecharts row.

- [ ] **Step 2: Verify no broken formatting**

Visually inspect the README table and graph for alignment.

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: add Frank.Statecharts.Core to README packages and dependency graph"
```
