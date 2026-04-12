---
source: kitty-specs/032-consolidate-library-sprawl
type: plan
---

# Extract Frank.Resources.Model Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extract pure resource model types from Frank.Statecharts into a zero-dependency leaf assembly `Frank.Resources.Model`.

**Architecture:** Move all pure data types and pure functions that describe resources (type shapes, capabilities, affordances, runtime projections) from `Frank.Statecharts/UnifiedModel.fs`, `Frank.Statecharts/Affordances/RuntimeProjection.fs`, and `Frank.Statecharts/Affordances/AffordanceMap.fs` into a new `src/Frank.Resources.Model` project. Update all consumers to reference the new project and namespace. `PreComputedAffordance` and `preCompute` stay in Frank.Statecharts (they use `StringValues` from ASP.NET Core). `StateInfo` moves from `Frank.Statecharts/Types.fs` into the new project since it describes HTTP capabilities, not statechart formalism.

**Tech Stack:** F# 8.0+ targeting net8.0;net9.0;net10.0 (multi-targeting, matching Frank.Statecharts), Expecto 10.2.3 for tests.

**Namespace:** `Frank.Resources.Model` (single namespace for all types in the leaf assembly).

**Key constraint:** Frank.Resources.Model has ZERO NuGet dependencies and ZERO project references. It is a pure leaf.

---

## File Structure

### New files to create

| File | Responsibility |
|------|---------------|
| `src/Frank.Resources.Model/Frank.Resources.Model.fsproj` | Project file: multi-target, zero deps |
| `src/Frank.Resources.Model/TypeAnalysis.fs` | Analyzed type shape types: SourceLocation, FieldKind, ConstraintAttribute, AnalyzedField, DuCase, TypeKind, AnalyzedType |
| `src/Frank.Resources.Model/ResourceTypes.fs` | Resource capability types: StateInfo, HttpCapability, ExtractedStatechart, DerivedResourceFields, ProjectedProfiles, UnifiedResource, UnifiedExtractionState + helper functions (resourceSlug, emptyDerivedFields, ProjectedProfiles.empty/isEmpty) |
| `src/Frank.Resources.Model/RuntimeTypes.fs` | Runtime projection types: RuntimeHttpCapability, RuntimeStateInfo, RuntimeStatechart, RuntimeResource, RuntimeState + helpers (RuntimeStatechart.empty/isEmpty) |
| `src/Frank.Resources.Model/AffordanceTypes.fs` | Affordance model types + pure functions: AffordanceLinkRelation, AffordanceMapEntry, AffordanceMap (data type, constants, lookupKey, tryFind, empty, generateFromResources, fromRuntimeState). Excludes PreComputedAffordance/preCompute. |
| `test/Frank.Resources.Model.Tests/Frank.Resources.Model.Tests.fsproj` | Test project: net10.0, Expecto |
| `test/Frank.Resources.Model.Tests/ResourceSlugTests.fs` | Tests for resourceSlug pure function |
| `test/Frank.Resources.Model.Tests/AffordanceMapTests.fs` | Tests for AffordanceMap pure functions (moved from Frank.Statecharts.Tests) |
| `test/Frank.Resources.Model.Tests/Program.fs` | Expecto entry point |

### Files to modify

| File | Change |
|------|--------|
| `src/Frank.Statecharts/Frank.Statecharts.fsproj` | Add ProjectReference to Frank.Resources.Model, remove UnifiedModel.fs from Compile, remove Affordances/RuntimeProjection.fs and Affordances/AffordanceMap.fs from Compile (type definitions only — keep AffordanceMiddleware.fs, StartupProjection.fs, StatechartProjection.fs, ProfileMiddleware.fs, WebHostBuilderExtensions.fs) |
| `src/Frank.Statecharts/Types.fs` | Remove StateInfo (moves to Frank.Resources.Model) |
| `src/Frank.Statecharts/Affordances/AffordanceMiddleware.fs` | Change namespace + opens, keep PreComputedAffordance here |
| `src/Frank.Statecharts/Affordances/StartupProjection.fs` | Change opens to Frank.Resources.Model |
| `src/Frank.Statecharts/Affordances/StatechartProjection.fs` | Change opens to Frank.Resources.Model |
| `src/Frank.Statecharts/Affordances/ProfileMiddleware.fs` | Change opens to Frank.Resources.Model |
| `src/Frank.Statecharts/Affordances/WebHostBuilderExtensions.fs` | Change opens to Frank.Resources.Model |
| `src/Frank.Statecharts/StatefulResourceBuilder.fs` | Add open Frank.Resources.Model (for StateInfo) |
| `src/Frank.Statecharts/Middleware.fs` | Verify — may need open Frank.Resources.Model if it uses StateInfo |
| `src/Frank.Statecharts/StatechartFeature.fs` | No change (doesn't use model types) |
| `src/Frank.Statecharts/Wsd/Generator.fs` | Has `open Frank.Statecharts` — verify if it uses StateInfo |
| `src/Frank.Statecharts/Smcat/Generator.fs` | Has `open Frank.Statecharts` — verify if it uses StateInfo |
| `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` | Add ProjectReference to Frank.Resources.Model |
| `src/Frank.Cli.Core/Unified/UnifiedModel.fs` | Remove re-export shim (no longer needed) or update to re-export from Frank.Resources.Model |
| All `src/Frank.Cli.Core/**/*.fs` files with `open Frank.Statecharts.Unified` | Change to `open Frank.Resources.Model` |
| All `src/Frank.Cli.Core/**/*.fs` files with `open Frank.Affordances` | Change to `open Frank.Resources.Model` (for type access; keep `open Frank.Affordances` if they use PreComputedAffordance/middleware) |
| `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` | Add ProjectReference to Frank.Resources.Model |
| `test/Frank.Statecharts.Tests/Affordances/AffordanceMapTests.fs` | Move to Frank.Resources.Model.Tests |
| All test files with `open Frank.Statecharts.Unified` | Change to `open Frank.Resources.Model` |
| All test files with `open Frank.Affordances` | Change to `open Frank.Resources.Model` where accessing model types |
| `test/Frank.Cli.Core.Tests/Unified/UnifiedModelTests.fs` | Move resourceSlug tests to Frank.Resources.Model.Tests; keep MessagePack roundtrip tests here (they test serialization, a CLI concern) but update namespace opens |
| `sample/Frank.TicTacToe.Sample/*.fs` | Change `open Frank.Affordances` to `open Frank.Resources.Model` where model types are used |
| `Frank.sln` | Add Frank.Resources.Model and Frank.Resources.Model.Tests projects |

---

### Task 1: Create Frank.Resources.Model project scaffold

**Files:**
- Create: `src/Frank.Resources.Model/Frank.Resources.Model.fsproj`

- [ ] **Step 1: Create the project file**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>resource;model;affordances</PackageTags>
    <Description>Pure resource model types for the Frank web framework: type analysis, capabilities, affordances, and runtime projections</Description>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="TypeAnalysis.fs" />
    <Compile Include="ResourceTypes.fs" />
    <Compile Include="RuntimeTypes.fs" />
    <Compile Include="AffordanceTypes.fs" />
  </ItemGroup>

</Project>
```

Note: NO PackageReferences, NO ProjectReferences, NO FrameworkReferences. Zero dependencies.

- [ ] **Step 2: Verify the project file is valid**

Run: `dotnet restore src/Frank.Resources.Model/Frank.Resources.Model.fsproj`
Expected: Restore succeeds (no deps to restore, just validates the project file).

- [ ] **Step 3: Commit**

```bash
git add src/Frank.Resources.Model/Frank.Resources.Model.fsproj
git commit -m "chore: scaffold Frank.Resources.Model project (empty)"
```

---

### Task 2: Create TypeAnalysis.fs — analyzed type shape types

**Files:**
- Create: `src/Frank.Resources.Model/TypeAnalysis.fs`
- Source: Copy types from `src/Frank.Statecharts/UnifiedModel.fs:39-79`

- [ ] **Step 1: Create TypeAnalysis.fs**

Write `src/Frank.Resources.Model/TypeAnalysis.fs` with namespace `Frank.Resources.Model` containing these types in order:
1. `SourceLocation` — `{ File: string; Line: int; Column: int }`
2. `FieldKind` — DU: Primitive, Guid, Optional, Collection, Reference
3. `ConstraintAttribute` — DU: PatternAttr, MinInclusiveAttr, MaxInclusiveAttr, MinLengthAttr, MaxLengthAttr
4. `AnalyzedField` — `{ Name; Kind; IsRequired; IsScalar; Constraints }`
5. `DuCase` — `{ Name; Fields }`
6. `TypeKind` — DU: Record, DiscriminatedUnion, Enum
7. `AnalyzedType` — `{ FullName; ShortName; Kind; GenericParameters; SourceLocation; IsClosed }`

Copy these exactly from `src/Frank.Statecharts/UnifiedModel.fs` lines 39-79, changing only the namespace from `Frank.Statecharts.Unified` to `Frank.Resources.Model`.

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Frank.Resources.Model/Frank.Resources.Model.fsproj`
Expected: Build succeeds (will have warnings about other missing files — that's OK, we'll create them next).

Actually — the fsproj references all 4 files. Create empty placeholder files for the other 3 first:

```fsharp
// ResourceTypes.fs
namespace Frank.Resources.Model
// placeholder

// RuntimeTypes.fs
namespace Frank.Resources.Model
// placeholder

// AffordanceTypes.fs
namespace Frank.Resources.Model
// placeholder
```

Then build. Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/Frank.Resources.Model/TypeAnalysis.fs src/Frank.Resources.Model/ResourceTypes.fs src/Frank.Resources.Model/RuntimeTypes.fs src/Frank.Resources.Model/AffordanceTypes.fs
git commit -m "feat: add TypeAnalysis types to Frank.Resources.Model"
```

---

### Task 3: Create ResourceTypes.fs — resource capability and extraction types

**Files:**
- Create: `src/Frank.Resources.Model/ResourceTypes.fs`
- Source: `src/Frank.Statecharts/Types.fs:42-45` (StateInfo), `src/Frank.Statecharts/UnifiedModel.fs:7-34,86-173` (remaining types)

- [ ] **Step 1: Write ResourceTypes.fs**

Write `src/Frank.Resources.Model/ResourceTypes.fs` with namespace `Frank.Resources.Model` containing these types in order:

1. `StateInfo` — from `src/Frank.Statecharts/Types.fs:42-45`: `{ AllowedMethods: string list; IsFinal: bool; Description: string option }`
2. `ProjectedProfiles` record + companion module (from UnifiedModel.fs lines 11-33)
3. `ExtractedStatechart` — from UnifiedModel.fs lines 86-91: `{ RouteTemplate; StateNames; InitialStateKey; GuardNames; StateMetadata: Map<string, StateInfo> }`
4. `HttpCapability` — from UnifiedModel.fs lines 98-106
5. `DerivedResourceFields` — from UnifiedModel.fs lines 109-117
6. `UnifiedResource` — from UnifiedModel.fs lines 120-132
7. `UnifiedExtractionState` — from UnifiedModel.fs lines 135-149

Then the `UnifiedModel` module with pure functions (from UnifiedModel.fs lines 151-172):
- `resourceSlug`
- `emptyDerivedFields`

Copy exactly, changing namespace to `Frank.Resources.Model`. Add `open System` at the top (needed for `DateTimeOffset` in UnifiedExtractionState and `String` in resourceSlug).

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Frank.Resources.Model/Frank.Resources.Model.fsproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Frank.Resources.Model/ResourceTypes.fs
git commit -m "feat: add resource capability types to Frank.Resources.Model"
```

---

### Task 4: Create RuntimeTypes.fs — runtime projection types

**Files:**
- Create: `src/Frank.Resources.Model/RuntimeTypes.fs`
- Source: `src/Frank.Statecharts/Affordances/RuntimeProjection.fs:6-65`

- [ ] **Step 1: Write RuntimeTypes.fs**

Write `src/Frank.Resources.Model/RuntimeTypes.fs` with namespace `Frank.Resources.Model` containing:

1. `RuntimeHttpCapability` — `{ Method; StateKey; LinkRelation; IsSafe }`
2. `RuntimeStateInfo` — `{ AllowedMethods; IsFinal; Description: string }` (note: Description is `string`, not `string option` — differs from StateInfo)
3. `RuntimeStatechart` — `{ StateNames; InitialStateKey; GuardNames; StateMetadata: Map<string, RuntimeStateInfo> }`
4. `RuntimeResource` — `{ RouteTemplate; ResourceSlug; Statechart; HttpCapabilities }`
5. `RuntimeStatechart` module — `empty` and `isEmpty` functions
6. `RuntimeState` — `{ Resources; BaseUri; Profiles: ProjectedProfiles }`

Copy exactly from `src/Frank.Statecharts/Affordances/RuntimeProjection.fs` lines 6-65, changing namespace to `Frank.Resources.Model` and removing the `open Frank.Statecharts.Unified` (ProjectedProfiles is now in the same namespace).

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Frank.Resources.Model/Frank.Resources.Model.fsproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add src/Frank.Resources.Model/RuntimeTypes.fs
git commit -m "feat: add runtime projection types to Frank.Resources.Model"
```

---

### Task 5: Create AffordanceTypes.fs — affordance map types and pure functions

**Files:**
- Create: `src/Frank.Resources.Model/AffordanceTypes.fs`
- Source: `src/Frank.Statecharts/Affordances/AffordanceMap.fs:1-179` (excluding PreComputedAffordance and preCompute)

- [ ] **Step 1: Write AffordanceTypes.fs**

Write `src/Frank.Resources.Model/AffordanceTypes.fs` with namespace `Frank.Resources.Model` containing:

1. `AffordanceLinkRelation` — `[<RequireQualifiedAccess>] type` with `{ Rel; Href; Method; Title }` (from AffordanceMap.fs lines 10-19)
2. `AffordanceMapEntry` — `[<RequireQualifiedAccess>] type` with `{ RouteTemplate; StateKey; AllowedMethods; LinkRelations; ProfileUrl }` (lines 21-33)
3. `AffordanceMap` — `{ Version; Entries }` (lines 36-40)
4. `AffordanceMap` module containing:
   - `currentVersion` = "1.0"
   - `empty`
   - `WildcardStateKey` = "*" literal
   - `KeySeparator` = "|" literal
   - `lookupKey` function
   - `tryFind` function
   - `private formatLinkValue` function
   - `private profileUrl` function
   - `private buildLinkRelations` function (uses RuntimeHttpCapability)
   - `private buildEntries` function (uses RuntimeResource, RuntimeStatechart)
   - `generateFromResources` function
   - `fromRuntimeState` function

Copy from AffordanceMap.fs, changing namespace to `Frank.Resources.Model`. Remove `open Frank.Statecharts.Unified` (ProjectedProfiles is local). Remove the `open` for `System.Collections.Generic`, `System.IO`, `System.Text.Json` (only needed by preCompute). Keep `open System` and `open Microsoft.Extensions.Primitives`.

**IMPORTANT:** Do NOT include `PreComputedAffordance` type or `preCompute` function — these depend on `Microsoft.Extensions.Primitives.StringValues` and stay in Frank.Statecharts. Also remove the `open Microsoft.Extensions.Primitives` since nothing in this file needs it.

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/Frank.Resources.Model/Frank.Resources.Model.fsproj`
Expected: Build succeeds with zero deps.

- [ ] **Step 3: Commit**

```bash
git add src/Frank.Resources.Model/AffordanceTypes.fs
git commit -m "feat: add affordance map types to Frank.Resources.Model"
```

---

### Task 6: Create Frank.Resources.Model.Tests project with moved tests

**Files:**
- Create: `test/Frank.Resources.Model.Tests/Frank.Resources.Model.Tests.fsproj`
- Create: `test/Frank.Resources.Model.Tests/ResourceSlugTests.fs`
- Create: `test/Frank.Resources.Model.Tests/AffordanceMapTests.fs`
- Create: `test/Frank.Resources.Model.Tests/Program.fs`

- [ ] **Step 1: Create the test project file**

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
    <Compile Include="ResourceSlugTests.fs" />
    <Compile Include="AffordanceMapTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.Resources.Model/Frank.Resources.Model.fsproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create ResourceSlugTests.fs**

Move the `resourceSlug` tests from `test/Frank.Cli.Core.Tests/Unified/UnifiedModelTests.fs` lines 23-43. Change module to `Frank.Resources.Model.Tests.ResourceSlugTests`, change opens to `Frank.Resources.Model`:

```fsharp
module Frank.Resources.Model.Tests.ResourceSlugTests

open Expecto
open Frank.Resources.Model

[<Tests>]
let resourceSlugTests =
    testList
        "resourceSlug"
        [ testCase "derives slug from simple route"
          <| fun _ ->
              let slug = UnifiedModel.resourceSlug "/health"
              Expect.equal slug "health" "Simple route"

          testCase "derives slug from parameterized route"
          <| fun _ ->
              let slug = UnifiedModel.resourceSlug "/games/{gameId}"
              Expect.equal slug "games" "Parameterized route"

          testCase "derives slug from root route"
          <| fun _ ->
              let slug = UnifiedModel.resourceSlug "/"
              Expect.equal slug "root" "Root route"

          testCase "derives slug from nested route"
          <| fun _ ->
              let slug = UnifiedModel.resourceSlug "/api/games"
              Expect.equal slug "api" "Nested route uses first segment" ]
```

- [ ] **Step 3: Create AffordanceMapTests.fs**

Move tests from `test/Frank.Statecharts.Tests/Affordances/AffordanceMapTests.fs`. Change module to `Frank.Resources.Model.Tests.AffordanceMapTests`, change opens to `Frank.Resources.Model`:

```fsharp
module Frank.Resources.Model.Tests.AffordanceMapTests

open Expecto
open Frank.Resources.Model

[<Tests>]
let affordanceMapTests =
    testList
        "AffordanceMap"
        [ testCase "lookupKey combines route and state"
          <| fun _ ->
              let key = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
              Expect.equal key "/games/{gameId}|XTurn" "Should combine with pipe separator"

          testCase "lookupKey with wildcard for stateless resource"
          <| fun _ ->
              let key = AffordanceMap.lookupKey "/health" AffordanceMap.WildcardStateKey
              Expect.equal key "/health|*" "Should use wildcard"

          testCase "tryFind returns matching entry"
          <| fun _ ->
              let entry =
                  { AffordanceMapEntry.RouteTemplate = "/games/{gameId}"
                    AffordanceMapEntry.StateKey = "XTurn"
                    AffordanceMapEntry.AllowedMethods = [ "GET"; "POST" ]
                    AffordanceMapEntry.LinkRelations = []
                    AffordanceMapEntry.ProfileUrl = "https://example.com/alps/games" }

              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries = [ entry ] }

              let result = AffordanceMap.tryFind "/games/{gameId}" "XTurn" map
              Expect.isSome result "Should find the entry"
              Expect.equal result.Value.AllowedMethods [ "GET"; "POST" ] "Should have correct methods"

          testCase "tryFind returns None for missing entry"
          <| fun _ ->
              let map =
                  { Version = AffordanceMap.currentVersion
                    Entries = [] }

              let result = AffordanceMap.tryFind "/missing" "state" map
              Expect.isNone result "Should return None for missing"

          testCase "currentVersion is set"
          <| fun _ -> Expect.isNotEmpty AffordanceMap.currentVersion "Version should be non-empty" ]
```

- [ ] **Step 4: Create Program.fs**

```fsharp
module Frank.Resources.Model.Tests.Program

open Expecto

[<EntryPoint>]
let main args = runTestsInAssemblyWithCLIArgs [] args
```

- [ ] **Step 5: Verify tests pass**

Run: `dotnet test test/Frank.Resources.Model.Tests/Frank.Resources.Model.Tests.fsproj`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add test/Frank.Resources.Model.Tests/
git commit -m "feat: add Frank.Resources.Model.Tests with resource slug and affordance map tests"
```

---

### Task 7: Wire Frank.Statecharts to depend on Frank.Resources.Model

This is the big task — update Frank.Statecharts to consume types from Frank.Resources.Model instead of defining them locally.

**Files:**
- Modify: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- Modify: `src/Frank.Statecharts/Types.fs`
- Delete: `src/Frank.Statecharts/UnifiedModel.fs`
- Delete: `src/Frank.Statecharts/Affordances/RuntimeProjection.fs`
- Modify: `src/Frank.Statecharts/Affordances/AffordanceMap.fs` (keep only PreComputedAffordance + preCompute)
- Modify: `src/Frank.Statecharts/Affordances/AffordanceMiddleware.fs`
- Modify: `src/Frank.Statecharts/Affordances/StartupProjection.fs`
- Modify: `src/Frank.Statecharts/Affordances/StatechartProjection.fs`
- Modify: `src/Frank.Statecharts/Affordances/ProfileMiddleware.fs`
- Modify: `src/Frank.Statecharts/Affordances/WebHostBuilderExtensions.fs`
- Modify: Various other Frank.Statecharts files that use StateInfo

- [ ] **Step 1: Update Frank.Statecharts.fsproj**

Add ProjectReference to Frank.Resources.Model. Remove the Compile entries for files whose types moved entirely (UnifiedModel.fs, Affordances/RuntimeProjection.fs). Keep Affordances/AffordanceMap.fs (it will retain PreComputedAffordance + preCompute only).

In the `<ItemGroup>` with project references, add:
```xml
<ProjectReference Include="../Frank.Resources.Model/Frank.Resources.Model.fsproj" />
```

Remove these Compile entries:
```xml
<Compile Include="UnifiedModel.fs" />
<Compile Include="Affordances/RuntimeProjection.fs" />
```

- [ ] **Step 2: Remove StateInfo from Types.fs**

In `src/Frank.Statecharts/Types.fs`, remove lines 41-45:
```fsharp
/// Metadata about a single state (HTTP configuration).
type StateInfo =
    { AllowedMethods: string list
      IsFinal: bool
      Description: string option }
```

Add `open Frank.Resources.Model` after line 3 (`open System.Security.Claims`) so that `StateInfo` is still available to `StateMachine` which references it at line 60.

- [ ] **Step 3: Delete UnifiedModel.fs and RuntimeProjection.fs**

Delete `src/Frank.Statecharts/UnifiedModel.fs` — all types moved to Frank.Resources.Model.
Delete `src/Frank.Statecharts/Affordances/RuntimeProjection.fs` — all types moved to Frank.Resources.Model.

- [ ] **Step 4: Reduce AffordanceMap.fs to PreComputedAffordance + preCompute only**

Replace `src/Frank.Statecharts/Affordances/AffordanceMap.fs` with ONLY the types/functions that depend on `StringValues`:

```fsharp
namespace Frank.Affordances

open System
open System.Collections.Generic
open Microsoft.Extensions.Primitives
open Frank.Resources.Model

/// Pre-computed header values for a single (route, state) pair.
/// Built at startup for zero per-request allocation beyond header assignment.
type PreComputedAffordance =
    { /// Pre-formatted Allow header value, e.g. "GET, POST"
      AllowHeaderValue: StringValues
      /// Pre-formatted Link header values as a single StringValues (from string array).
      /// Each entry follows RFC 8288 syntax: `<URI>; rel="relation-type"`
      LinkHeaderValues: StringValues }

module AffordancePreCompute =

    /// Format a single link relation as an RFC 8288 Link header value.
    let private formatLinkValue (href: string) (rel: string) : string =
        sprintf "<%s>; rel=\"%s\"" href rel

    /// Pre-compute header strings for all entries in the affordance map.
    /// Returns a dictionary indexed by composite key for O(1) request-time lookup.
    let preCompute (map: AffordanceMap) : Dictionary<string, PreComputedAffordance> =
        let dict = Dictionary<string, PreComputedAffordance>(StringComparer.Ordinal)

        for entry in map.Entries do
            let key = AffordanceMap.lookupKey entry.RouteTemplate entry.StateKey
            let allowHeader = StringValues(String.Join(", ", entry.AllowedMethods))

            let linkValues =
                [| if not (String.IsNullOrEmpty entry.ProfileUrl) then
                       formatLinkValue entry.ProfileUrl "profile"

                   for lr in entry.LinkRelations do
                       formatLinkValue lr.Href lr.Rel |]

            let linkHeader = StringValues(linkValues)

            dict.[key] <-
                { AllowHeaderValue = allowHeader
                  LinkHeaderValues = linkHeader }

        dict
```

Note: The module is renamed from `AffordanceMap` (which is now a type in Frank.Resources.Model) to `AffordancePreCompute`. This avoids ambiguity. The `formatLinkValue` helper also moves here since it's only used by `preCompute`.

**All callers of `AffordanceMap.preCompute` must be updated to `AffordancePreCompute.preCompute`:**
- `src/Frank.Statecharts/Affordances/WebHostBuilderExtensions.fs` line 17
- `test/Frank.TicTacToe.Tests/AffordanceIntegrationTests.fs` line 182
- Any other callers found by grepping for `AffordanceMap.preCompute`

- [ ] **Step 5: Update AffordanceMiddleware.fs**

Change `src/Frank.Statecharts/Affordances/AffordanceMiddleware.fs`:
- Add `open Frank.Resources.Model` after the namespace declaration
- The middleware itself references `AffordanceMap.lookupKey` and `AffordanceMap.WildcardStateKey` which are now in Frank.Resources.Model — the `open Frank.Resources.Model` makes them available

- [ ] **Step 6: Update StartupProjection.fs**

Change `src/Frank.Statecharts/Affordances/StartupProjection.fs`:
- Replace `open Frank.Statecharts.Unified` with `open Frank.Resources.Model`
- The types it uses (HttpCapability, UnifiedResource, UnifiedExtractionState, RuntimeHttpCapability, RuntimeStatechart, RuntimeResource, RuntimeState, AffordanceMap) are all in Frank.Resources.Model now
- The reference to `Frank.Statecharts.StateInfo` on line 44 becomes just `StateInfo` (from Frank.Resources.Model)

- [ ] **Step 7: Update StatechartProjection.fs**

Change `src/Frank.Statecharts/Affordances/StatechartProjection.fs`:
- Replace `open Frank.Statecharts.Unified` with `open Frank.Resources.Model` (if present — check; it currently doesn't open Unified, it opens Ast)
- Verify it compiles — it uses RuntimeResource, RuntimeStatechart from Frank.Resources.Model

Wait — StatechartProjection.fs currently has `namespace Frank.Affordances` and `open Frank.Statecharts.Ast`. It uses `RuntimeResource`, `RuntimeStatechart`, `RuntimeStatechart.isEmpty` from RuntimeProjection.fs (Frank.Affordances namespace). Since RuntimeProjection types moved to Frank.Resources.Model, add `open Frank.Resources.Model`.

- [ ] **Step 8: Update ProfileMiddleware.fs**

Change `src/Frank.Statecharts/Affordances/ProfileMiddleware.fs`:
- Replace `open Frank.Statecharts.Unified` with `open Frank.Resources.Model`
- `ProjectedProfiles` is now in Frank.Resources.Model

- [ ] **Step 9: Update WebHostBuilderExtensions.fs**

Change `src/Frank.Statecharts/Affordances/WebHostBuilderExtensions.fs`:
- Add `open Frank.Resources.Model` (for AffordanceMap type)
- Update reference from `AffordanceMap.preCompute` to `AffordancePreCompute.preCompute`
- Update reference from `AffordanceMap.currentVersion` to `AffordanceMap.currentVersion` (still works — it's in Frank.Resources.Model)

- [ ] **Step 10: Update other Frank.Statecharts files that use StateInfo**

Search for files in `src/Frank.Statecharts/` that reference `StateInfo` and add `open Frank.Resources.Model`:
- `StatefulResourceBuilder.fs` — uses `StateInfo` in `StateMachineMetadata.StateMetadataMap` and in the builder
- `Middleware.fs` — check if it uses StateInfo
- `Wsd/Generator.fs` — has `open Frank.Statecharts`, check if it uses StateInfo
- `Smcat/Generator.fs` — has `open Frank.Statecharts`, check if it uses StateInfo

For each file that uses `StateInfo`, add `open Frank.Resources.Model` after the namespace declaration.

- [ ] **Step 11: Verify Frank.Statecharts builds**

Run: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`
Expected: Build succeeds across all 3 target frameworks.

- [ ] **Step 12: Commit**

```bash
git add src/Frank.Statecharts/ src/Frank.Resources.Model/
git commit -m "refactor: wire Frank.Statecharts to consume types from Frank.Resources.Model"
```

---

### Task 8: Update Frank.Cli.Core to reference Frank.Resources.Model

**Files:**
- Modify: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
- Modify or delete: `src/Frank.Cli.Core/Unified/UnifiedModel.fs` (re-export shim)
- Modify: All Frank.Cli.Core files with `open Frank.Statecharts.Unified` or `open Frank.Affordances`

- [ ] **Step 1: Add ProjectReference**

In `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`, add to the ProjectReference ItemGroup:
```xml
<ProjectReference Include="../Frank.Resources.Model/Frank.Resources.Model.fsproj" />
```

- [ ] **Step 2: Delete the re-export shim**

Delete `src/Frank.Cli.Core/Unified/UnifiedModel.fs` — it was a shim re-exporting from Frank.Statecharts.Unified. Now consumers open Frank.Resources.Model directly.

Also remove its `<Compile Include="Unified/UnifiedModel.fs" />` entry from `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`.

- [ ] **Step 3: Update namespace opens across Frank.Cli.Core**

For every file in `src/Frank.Cli.Core/` that has `open Frank.Statecharts.Unified`:
- Replace with `open Frank.Resources.Model`
- If the file ALSO uses types from `Frank.Statecharts` namespace (e.g., StateMachine), keep `open Frank.Statecharts`
- **KEEP `open Frank.Cli.Core.Unified` wherever it appears** — this namespace still contains `UnifiedExtractor`, `UnifiedCache`, `AffordanceMapGenerator`, `ExtractionStateProjector`, `UnifiedAlpsGenerator`, `OpenApiConsistencyValidator`. Only the re-export shim file was deleted, not these modules.

Files to update (from earlier grep — 14+ files):
- `Unified/UnifiedExtractor.fs`
- `Unified/ExtractionStateProjector.fs`
- `Unified/UnifiedCache.fs`
- `Unified/AffordanceMapGenerator.fs`
- `Unified/OpenApiConsistencyValidator.fs`
- `Unified/UnifiedAlpsGenerator.fs`
- `Commands/CompileCommand.fs`
- `Commands/OpenApiValidateCommand.fs`
- `Extraction/TypeMapper.fs`
- `Extraction/UriHelpers.fs`
- `Extraction/CapabilityMapper.fs`
- `Extraction/RouteMapper.fs`
- `Extraction/ShapeGenerator.fs`
- `Analysis/TypeAnalyzer.fs`
- `Statechart/FormatPipeline.fs`
- `Statechart/StatechartExtractor.fs`
- `Statechart/StatechartSourceExtractor.fs`
- Various Command files

For each file:
1. Replace `open Frank.Statecharts.Unified` → `open Frank.Resources.Model`
2. Replace `open Frank.Affordances` → `open Frank.Resources.Model` (where the file uses model types like AffordanceMap, AffordanceMapEntry). If the file also uses `PreComputedAffordance` or `AffordancePreCompute`, keep `open Frank.Affordances` too.
3. **Keep `open Frank.Cli.Core.Unified`** — only the shim was deleted, not the namespace.
4. Search for fully-qualified references like `Frank.Statecharts.Unified.ExtractedStatechart` and replace with `Frank.Resources.Model.ExtractedStatechart`.
5. For files that reference `Frank.Cli.Core.Unified.UnifiedModel` (the deleted shim), change to `Frank.Resources.Model.UnifiedModel`.

- [ ] **Step 4: Verify Frank.Cli.Core builds**

Run: `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add src/Frank.Cli.Core/
git commit -m "refactor: update Frank.Cli.Core to use Frank.Resources.Model"
```

---

### Task 9: Update test projects

**Files:**
- Modify: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`
- Modify: `test/Frank.Statecharts.Tests/Affordances/AffordanceMapTests.fs` → remove (moved to Frank.Resources.Model.Tests)
- Modify: All test files with `open Frank.Statecharts.Unified` or `open Frank.Affordances`
- Modify: `test/Frank.Cli.Core.Tests/Unified/UnifiedModelTests.fs`
- Modify: `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj`

- [ ] **Step 1: Update Frank.Statecharts.Tests.fsproj**

Add ProjectReference:
```xml
<ProjectReference Include="../../src/Frank.Resources.Model/Frank.Resources.Model.fsproj" />
```

Remove AffordanceMapTests.fs from Compile (moved to Frank.Resources.Model.Tests):
```xml
<!-- Remove this line -->
<Compile Include="Affordances/AffordanceMapTests.fs" />
```

Delete the file `test/Frank.Statecharts.Tests/Affordances/AffordanceMapTests.fs`.

- [ ] **Step 2: Update Frank.Statecharts.Tests namespace opens**

For all test files with `open Frank.Statecharts.Unified` or `open Frank.Affordances` that access model types:
- `Affordances/AffordanceMiddlewareTests.fs` — `open Frank.Affordances` stays (uses middleware), add `open Frank.Resources.Model`
- `Affordances/ProfileMiddlewareTests.fs` — replace `open Frank.Affordances` with `open Frank.Resources.Model`, keep `open Frank.Affordances` if it uses PreComputedAffordance
- `Affordances/EmbeddingTests.fs` — update opens
- Other test files that use StateInfo or model types

- [ ] **Step 3: Update Frank.Cli.Core.Tests**

In `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj`, add:
```xml
<ProjectReference Include="../../src/Frank.Resources.Model/Frank.Resources.Model.fsproj" />
```

Update `test/Frank.Cli.Core.Tests/Unified/UnifiedModelTests.fs`:
- Remove the `resourceSlug` test cases (moved to Frank.Resources.Model.Tests)
- Replace `open Frank.Statecharts.Unified` with `open Frank.Resources.Model`
- **Keep `open Frank.Cli.Core.Unified`** — it is still needed for other modules in that namespace (UnifiedExtractor, UnifiedCache, etc.). Only the shim file was deleted, not the namespace.
- Replace `open Frank.Affordances` with `open Frank.Resources.Model` (the model types moved there)

Update all other test files:
- Replace `open Frank.Statecharts.Unified` → `open Frank.Resources.Model`
- **Keep `open Frank.Cli.Core.Unified`** wherever it appears
- Search for fully-qualified `Frank.Statecharts.Unified.` type references (e.g., in `UnifiedExtractorTests.fs` lines 201, 237, 271, 323) and replace with `Frank.Resources.Model.`

- [ ] **Step 4: Update sample project**

For `sample/Frank.TicTacToe.Sample/*.fs` files:
- `Domain.fs` line 100: `open Frank.Affordances` — if this uses AffordanceMap or model types, change to `open Frank.Resources.Model`
- `SseHandlers.fs` line 13: same
- `Program.fs` line 27: same

Also update `test/Frank.TicTacToe.Tests/AffordanceIntegrationTests.fs`:
- Line 22: update namespace opens
- Line 182: rename `AffordanceMap.preCompute` → `AffordancePreCompute.preCompute`

Note: Sample and test projects that reference `Frank.Statecharts` get transitive access to `Frank.Resources.Model`. If the F# compiler cannot resolve types transitively, add explicit `<ProjectReference>` entries to their `.fsproj` files.

- [ ] **Step 5: Verify the full solution builds**

Run: `dotnet build Frank.sln`
Expected: Build succeeds.

- [ ] **Step 6: Run all tests**

Run: `dotnet test Frank.sln`
Expected: All tests pass.

- [ ] **Step 7: Commit**

```bash
git add test/ sample/
git commit -m "refactor: update all test and sample projects to use Frank.Resources.Model"
```

---

### Task 10: Add projects to Frank.sln

**Files:**
- Modify: `Frank.sln`

- [ ] **Step 1: Add Frank.Resources.Model to the solution**

Run:
```bash
dotnet sln Frank.sln add src/Frank.Resources.Model/Frank.Resources.Model.fsproj --solution-folder src
dotnet sln Frank.sln add test/Frank.Resources.Model.Tests/Frank.Resources.Model.Tests.fsproj --solution-folder test
```

- [ ] **Step 2: Verify full solution builds and tests pass**

Run:
```bash
dotnet build Frank.sln
dotnet test Frank.sln
```
Expected: All builds succeed, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add Frank.sln
git commit -m "chore: add Frank.Resources.Model and tests to Frank.sln"
```

---

### Task 11: Clean up stale references

- [ ] **Step 1: Search for any remaining references to old namespaces**

Grep for `Frank.Statecharts.Unified` and `open Frank.Affordances` across all `.fs` and `.fsproj` files. Any remaining references in source code (not docs/kitty-specs) indicate incomplete migration.

The `namespace Frank.Affordances` should only remain in:
- `src/Frank.Statecharts/Affordances/AffordanceMap.fs` (PreComputedAffordance + preCompute)
- `src/Frank.Statecharts/Affordances/AffordanceMiddleware.fs`
- `src/Frank.Statecharts/Affordances/StartupProjection.fs`
- `src/Frank.Statecharts/Affordances/StatechartProjection.fs`
- `src/Frank.Statecharts/Affordances/ProfileMiddleware.fs`
- `src/Frank.Statecharts/Affordances/WebHostBuilderExtensions.fs`

`Frank.Statecharts.Unified` should appear NOWHERE in source code.

- [ ] **Step 2: Remove any empty directories or orphaned files**

Check for empty `src/Frank.Statecharts/Affordances/` files that were fully moved.

- [ ] **Step 3: Final build and test**

Run:
```bash
dotnet build Frank.sln
dotnet test Frank.sln
```
Expected: All green.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore: clean up stale namespace references after Frank.Resources.Model extraction"
```
