---
work_package_id: WP01
title: Unified Model Types & Project Scaffolding
lane: "doing"
dependencies: []
base_branch: master
base_commit: 8a6c0503223ce83bc37f398547bf5d800eaecee0
created_at: '2026-03-19T02:54:47.111306+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
phase: Phase 0 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "15168"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-003
- FR-013
---

# Work Package Prompt: WP01 -- Unified Model Types & Project Scaffolding

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
Use language identifiers in code blocks: ````fsharp`, ````xml`, ````bash`

---

## Implementation Command

No dependencies (first work package):

```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

1. Define the unified resource model types (`UnifiedResource`, `DerivedResourceFields`, `HttpCapability`) in a new `UnifiedModel.fs` file under `src/Frank.Cli.Core/Unified/`.
2. Define the affordance map types (`AffordanceMapEntry`, `AffordanceLinkRelation`) in a new `AffordanceMap.fs` file under the new `Frank.Affordances` project.
3. Create the `Frank.Affordances` project with `Microsoft.AspNetCore.App` framework reference only -- no dependency on `Frank.Statecharts`.
4. Create the `Frank.Affordances.Tests` test project with Expecto.
5. Add `MessagePack` + `MessagePack.FSharpExtensions` NuGet packages to `Frank.Cli.Core` and verify F# DU serialization roundtrip.
6. All projects compile cleanly with `dotnet build`.

**Success**: The type system for the unified pipeline is fully defined, projects scaffold cleanly, and MessagePack can roundtrip F# records and DUs.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` (FR-001 through FR-013, key entities section)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` (project structure, complexity tracking)
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` (all type definitions, relationships, lifecycle)
- **Research**: `kitty-specs/031-unified-resource-pipeline/research.md` (R1: MessagePack decision, R3: affordance map key design)
- **Existing types to reuse** (unchanged):
  - `AnalyzedType`, `AnalyzedField`, `FieldKind`, `DuCase`, `TypeKind` in `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs`
  - `ExtractedStatechart`, `StateInfo` in `src/Frank.Cli.Core/Statechart/StatechartExtractor.fs` and `src/Frank.Statecharts/Types.fs`
  - `ExtractionState`, `ExtractionMetadata` in `src/Frank.Cli.Core/State/ExtractionState.fs`
  - `AnalyzedResource`, `SourceLocation` in `src/Frank.Cli.Core/Analysis/AstAnalyzer.fs`
- **Key constraints**:
  - `Frank.Affordances` MUST NOT reference `Frank.Statecharts`. The affordance map is a standalone schema consumed via the binary embedded resource. This decoupling is intentional (plan: "decoupled via the binary affordance map format").
  - `UnifiedResource` uses existing `AnalyzedType list` and `ExtractedStatechart option` -- it composes existing types, not replacing them.
  - Binary serialization uses MessagePack (research R1): cross-platform format, F# DU support via `MessagePack.FSharpExtensions`, no C# source generator requirement.
  - The affordance map key is a composite string `"{routeTemplate}|{stateKey}"` (research R3).

---

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `UnifiedModel.fs` with core unified types

- **Purpose**: Define the central record types that the unified extractor (WP02) will produce and all downstream commands will consume.
- **Steps**:
  1. Create directory `src/Frank.Cli.Core/Unified/` if it doesn't exist.
  2. Create `src/Frank.Cli.Core/Unified/UnifiedModel.fs`.
  3. Namespace: `Frank.Cli.Core.Unified`.
  4. Define these types (matching the data model document):

```fsharp
namespace Frank.Cli.Core.Unified

open System
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Statechart

/// HTTP capability for a resource, optionally scoped to a state.
type HttpCapability =
    { /// HTTP method (GET, POST, PUT, DELETE, PATCH)
      Method: string
      /// Which state this applies to (None = always available, for plain resources)
      StateKey: string option
      /// IANA or ALPS-derived link relation type URI
      LinkRelation: string
      /// true for GET/HEAD/OPTIONS (safe methods)
      IsSafe: bool }

/// Computed invariant checks for structure-behavior consistency.
type DerivedResourceFields =
    { /// State DU cases not covered by any inState call
      OrphanStates: string list
      /// DU cases in the state type but not in the statechart
      UnhandledCases: string list
      /// Per-state: which type fields are relevant
      StateStructure: Map<string, AnalyzedField list>
      /// Ratio of mapped types to total types (0.0-1.0)
      TypeCoverage: float }

/// A combined description of a single HTTP resource.
type UnifiedResource =
    { /// HTTP route pattern (e.g., /games/{gameId})
      RouteTemplate: string
      /// Filename-safe slug derived from route (e.g., games)
      ResourceSlug: string
      /// F# types associated with this resource (records, DUs)
      TypeInfo: AnalyzedType list
      /// Behavioral data (None for plain resource CEs)
      Statechart: ExtractedStatechart option
      /// Methods available (globally or per-state)
      HttpCapabilities: HttpCapability list
      /// Computed invariant checks
      DerivedFields: DerivedResourceFields }

/// The cached state persisted to binary.
type UnifiedExtractionState =
    { /// All extracted resources
      Resources: UnifiedResource list
      /// Hash of source files for staleness detection
      SourceHash: string
      /// Base URI for ALPS profile namespace
      BaseUri: string
      /// Schema.org vocabularies used for alignment
      Vocabularies: string list
      /// Timestamp of extraction
      ExtractedAt: DateTimeOffset
      /// CLI version for cache compatibility
      ToolVersion: string }
```

  5. Add a helper module for slug computation (reuse from `FormatPipeline.resourceSlug` pattern):

```fsharp
module UnifiedModel =

    /// Derive a filename-safe slug from a route template.
    /// "/games/{gameId}" -> "games", "/health" -> "health"
    let resourceSlug (routeTemplate: string) : string =
        routeTemplate.TrimStart('/')
        |> fun s ->
            match s.IndexOf('/') with
            | -1 -> s
            | i -> s.Substring(0, i)
        |> fun s ->
            match s.IndexOf('{') with
            | -1 -> s
            | i -> s.Substring(0, i).TrimEnd('/')
        |> fun s -> if System.String.IsNullOrEmpty(s) then "root" else s

    /// Empty derived fields for resources without statecharts.
    let emptyDerivedFields : DerivedResourceFields =
        { OrphanStates = []
          UnhandledCases = []
          StateStructure = Map.empty
          TypeCoverage = 1.0 }
```

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedModel.fs` (NEW)
- **Notes**:
  - `TypeInfo` is `AnalyzedType list` from the existing `TypeAnalyzer` -- the unified model composes, not replaces.
  - `Statechart` is `ExtractedStatechart option` from the existing `StatechartExtractor` types.
  - The `DerivedResourceFields.StateStructure` maps state names to the `AnalyzedField list` relevant to that state. For a tic-tac-toe `Won` state, this might be `["winner"; "board"]`. This is computed by the extractor in WP02.
  - `TypeCoverage` = 1.0 when all types in the resource are fully mapped. This is computed during extraction.
  - The `resourceSlug` function must handle edge cases: root route `/` -> `"root"`, parameterized first segment `/{tenant}/games` -> `"root"` (rare, acceptable).

### Subtask T002 -- Create `AffordanceMap.fs` types

- **Purpose**: Define the affordance map schema that the CLI generates and the runtime middleware consumes. This goes in `Frank.Affordances`, not `Frank.Cli.Core`, because the runtime library needs these types without depending on the CLI.
- **Steps**:
  1. Create `src/Frank.Affordances/AffordanceMap.fs`.
  2. Namespace: `Frank.Affordances`.
  3. Define these types:

```fsharp
namespace Frank.Affordances

/// A single link relation in an affordance map entry.
type AffordanceLinkRelation =
    { /// Link relation type (IANA registered or ALPS profile fragment URI)
      Rel: string
      /// Target URL template
      Href: string
      /// HTTP method for this transition
      Method: string
      /// Human-readable label (optional)
      Title: string option }

/// One entry per (route, state) pair in the affordance map.
type AffordanceMapEntry =
    { /// HTTP route pattern
      RouteTemplate: string
      /// State name, or "*" for stateless resources
      StateKey: string
      /// HTTP methods available in this state
      AllowedMethods: string list
      /// Available transitions with relation types
      LinkRelations: AffordanceLinkRelation list
      /// URL to the ALPS profile for this resource
      ProfileUrl: string }

/// The complete affordance map with version metadata.
type AffordanceMap =
    { /// Schema version for forward compatibility
      Version: string
      /// All affordance entries
      Entries: AffordanceMapEntry list }

module AffordanceMap =

    /// Current affordance map schema version.
    let currentVersion = "1.0"

    /// Wildcard state key for resources without statecharts.
    [<Literal>]
    let WildcardStateKey = "*"

    /// Separator for composite lookup keys.
    [<Literal>]
    let KeySeparator = "|"

    /// Build a composite lookup key from route template and state key.
    let lookupKey (routeTemplate: string) (stateKey: string) : string =
        routeTemplate + KeySeparator + stateKey

    /// Try to find an entry in the affordance map by route and state.
    let tryFind (routeTemplate: string) (stateKey: string) (map: AffordanceMap) : AffordanceMapEntry option =
        let key = lookupKey routeTemplate stateKey
        map.Entries
        |> List.tryFind (fun e -> lookupKey e.RouteTemplate e.StateKey = key)
```

- **Files**: `src/Frank.Affordances/AffordanceMap.fs` (NEW)
- **Notes**:
  - The `Version` field enables forward compatibility (FR-012). New fields can be added to entries without breaking older consumers that ignore unknown fields.
  - `tryFind` uses linear scan for now. The runtime middleware (later WP) will build a `Dictionary` at startup for O(1) lookup. This module provides the schema types; the middleware provides the hot-path implementation.
  - `WildcardStateKey = "*"` is the sentinel for plain resources.
  - `Href` in `AffordanceLinkRelation` is a URL template (may contain `{parameters}`) -- this is intentional for link relations pointing to parameterized routes.

### Subtask T003 -- Create `Frank.Affordances` project

- **Purpose**: Scaffold the `Frank.Affordances.fsproj` project. This is the runtime library that applications reference -- it must be lightweight and independent of `Frank.Statecharts`.
- **Steps**:
  1. Create directory `src/Frank.Affordances/`.
  2. Create `src/Frank.Affordances/Frank.Affordances.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>affordances;hateoas;hypermedia;link;middleware</PackageTags>
    <Description>Runtime affordance middleware for Frank: injects Link and Allow headers based on resource state</Description>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="AffordanceMap.fs" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

</Project>
```

  3. Add the project to `Frank.sln`:

```bash
dotnet sln Frank.sln add src/Frank.Affordances/Frank.Affordances.fsproj
```

  4. Verify the project builds:

```bash
dotnet build src/Frank.Affordances/Frank.Affordances.fsproj
```

- **Files**: `src/Frank.Affordances/Frank.Affordances.fsproj` (NEW), `Frank.sln` (MODIFIED)
- **Notes**:
  - Multi-target `net8.0;net9.0;net10.0` matches Frank core's targeting strategy.
  - Framework reference only -- no NuGet dependencies yet. MessagePack will be added when the binary deserialization module is needed (future WP).
  - No `ProjectReference` to `Frank.Statecharts` -- this is the key decoupling constraint. The affordance map is a standalone schema.
  - Check `src/Directory.Build.props` for shared packaging properties (VersionPrefix, Authors, etc.) that will be inherited.

### Subtask T004 -- Create `Frank.Affordances.Tests` test project

- **Purpose**: Scaffold the test project that will validate affordance map construction, lookup, and serialization.
- **Steps**:
  1. Create directory `test/Frank.Affordances.Tests/`.
  2. Create `test/Frank.Affordances.Tests/Frank.Affordances.Tests.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <GenerateProgramFile>false</GenerateProgramFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="AffordanceMapTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.Affordances/Frank.Affordances.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.3" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
  </ItemGroup>

</Project>
```

  3. Create `test/Frank.Affordances.Tests/AffordanceMapTests.fs` with initial tests:

```fsharp
module Frank.Affordances.Tests.AffordanceMapTests

open Expecto
open Frank.Affordances

[<Tests>]
let affordanceMapTests =
    testList "AffordanceMap" [
        testCase "lookupKey combines route and state" <| fun _ ->
            let key = AffordanceMap.lookupKey "/games/{gameId}" "XTurn"
            Expect.equal key "/games/{gameId}|XTurn" "Should combine with pipe separator"

        testCase "lookupKey with wildcard for stateless resource" <| fun _ ->
            let key = AffordanceMap.lookupKey "/health" AffordanceMap.WildcardStateKey
            Expect.equal key "/health|*" "Should use wildcard"

        testCase "tryFind returns matching entry" <| fun _ ->
            let entry =
                { RouteTemplate = "/games/{gameId}"
                  StateKey = "XTurn"
                  AllowedMethods = [ "GET"; "POST" ]
                  LinkRelations = []
                  ProfileUrl = "https://example.com/alps/games" }
            let map = { Version = AffordanceMap.currentVersion; Entries = [ entry ] }
            let result = AffordanceMap.tryFind "/games/{gameId}" "XTurn" map
            Expect.isSome result "Should find the entry"
            Expect.equal result.Value.AllowedMethods [ "GET"; "POST" ] "Should have correct methods"

        testCase "tryFind returns None for missing entry" <| fun _ ->
            let map = { Version = AffordanceMap.currentVersion; Entries = [] }
            let result = AffordanceMap.tryFind "/missing" "state" map
            Expect.isNone result "Should return None for missing"

        testCase "currentVersion is set" <| fun _ ->
            Expect.isNotEmpty AffordanceMap.currentVersion "Version should be non-empty"
    ]
```

  4. Create `test/Frank.Affordances.Tests/Program.fs`:

```fsharp
module Frank.Affordances.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    Tests.runTestsInAssemblyWithCLIArgs [] argv
```

  5. Add to solution:

```bash
dotnet sln Frank.sln add test/Frank.Affordances.Tests/Frank.Affordances.Tests.fsproj
```

  6. Run tests:

```bash
dotnet test test/Frank.Affordances.Tests/
```

- **Files**: `test/Frank.Affordances.Tests/Frank.Affordances.Tests.fsproj` (NEW), `test/Frank.Affordances.Tests/AffordanceMapTests.fs` (NEW), `test/Frank.Affordances.Tests/Program.fs` (NEW), `Frank.sln` (MODIFIED)
- **Notes**:
  - Test project targets `net10.0` only, matching the convention of other test projects in the repo.
  - Uses Expecto + YoloDev.Expecto.TestSdk, matching `Frank.Statecharts.Tests`.
  - These initial tests validate the lookup key construction and `tryFind` function. More tests will be added in later WPs when serialization and runtime middleware are implemented.

### Subtask T005 -- Add MessagePack + MessagePack.FSharpExtensions to Frank.Cli.Core

- **Purpose**: Add the binary serialization dependency and verify F# DU roundtrip, establishing the serialization pattern for the unified cache (WP03).
- **Steps**:
  1. Add NuGet packages to `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`:

```bash
dotnet add src/Frank.Cli.Core/Frank.Cli.Core.fsproj package MessagePack
dotnet add src/Frank.Cli.Core/Frank.Cli.Core.fsproj package MessagePack.FSharpExtensions
```

  2. Create a smoke test in the existing CLI test project (or in a new test file if no CLI test project exists) that verifies roundtrip serialization of a sample `UnifiedResource`:

```fsharp
// Verify MessagePack can roundtrip F# records and DUs
open MessagePack
open MessagePack.Resolvers
open MessagePack.FSharp

let private options =
    MessagePackSerializerOptions
        .Standard
        .WithResolver(
            CompositeResolver.Create(
                FSharpResolver.Instance,
                ContractlessStandardResolver.Instance))

testCase "MessagePack roundtrips UnifiedResource" <| fun _ ->
    let resource : UnifiedResource =
        { RouteTemplate = "/games/{gameId}"
          ResourceSlug = "games"
          TypeInfo = []
          Statechart = None
          HttpCapabilities =
            [ { Method = "GET"; StateKey = None; LinkRelation = "self"; IsSafe = true } ]
          DerivedFields = UnifiedModel.emptyDerivedFields }
    let bytes = MessagePackSerializer.Serialize(resource, options)
    let deserialized = MessagePackSerializer.Deserialize<UnifiedResource>(bytes, options)
    Expect.equal deserialized.RouteTemplate resource.RouteTemplate "RouteTemplate roundtrips"
    Expect.equal deserialized.HttpCapabilities.Length 1 "HttpCapabilities roundtrip"
```

  3. Also verify `DerivedResourceFields` roundtrip (includes `Map` and `float`):

```fsharp
testCase "MessagePack roundtrips DerivedResourceFields" <| fun _ ->
    let fields : DerivedResourceFields =
        { OrphanStates = [ "Abandoned" ]
          UnhandledCases = [ "Draw" ]
          StateStructure = Map.ofList [ "XTurn", [] ]
          TypeCoverage = 0.75 }
    let bytes = MessagePackSerializer.Serialize(fields, options)
    let deserialized = MessagePackSerializer.Deserialize<DerivedResourceFields>(bytes, options)
    Expect.equal deserialized.OrphanStates [ "Abandoned" ] "OrphanStates roundtrip"
    Expect.equal deserialized.TypeCoverage 0.75 "TypeCoverage roundtrip"
```

  4. Verify the `UnifiedExtractionState` type roundtrips (includes `DateTimeOffset`):

```fsharp
testCase "MessagePack roundtrips UnifiedExtractionState" <| fun _ ->
    let state : UnifiedExtractionState =
        { Resources = []
          SourceHash = "abc123"
          BaseUri = "https://example.com/"
          Vocabularies = [ "schema.org" ]
          ExtractedAt = DateTimeOffset(2026, 3, 19, 0, 0, 0, TimeSpan.Zero)
          ToolVersion = "7.0.0" }
    let bytes = MessagePackSerializer.Serialize(state, options)
    let deserialized = MessagePackSerializer.Deserialize<UnifiedExtractionState>(bytes, options)
    Expect.equal deserialized.SourceHash "abc123" "SourceHash roundtrips"
    Expect.equal deserialized.Vocabularies [ "schema.org" ] "Vocabularies roundtrip"
```

  5. Update `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` compile order to include `Unified/UnifiedModel.fs`:

```xml
<!-- Unified pipeline -->
<Compile Include="Unified/UnifiedModel.fs" />
```

Place this **after** the `State/` entries and **before** the `Statechart/` entries, since `UnifiedModel` depends on `Analysis/TypeAnalyzer` and `Statechart/StatechartExtractor` types. Wait -- `UnifiedModel` references `ExtractedStatechart` from the `Statechart` namespace. So `UnifiedModel.fs` must come **after** the `Statechart/StatechartExtractor.fs` entry. Place it after the statechart pipeline section:

```xml
    <!-- Statechart pipeline -->
    <Compile Include="Statechart/FormatDetector.fs" />
    <Compile Include="Statechart/StatechartError.fs" />
    <Compile Include="Statechart/StatechartExtractor.fs" />
    <Compile Include="Statechart/StatechartSourceExtractor.fs" />
    <Compile Include="Statechart/FormatPipeline.fs" />
    <Compile Include="Statechart/StatechartDocumentJson.fs" />
    <Compile Include="Statechart/ValidationReportFormatter.fs" />
    <!-- Unified pipeline -->
    <Compile Include="Unified/UnifiedModel.fs" />
```

- **Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (MODIFIED -- new packages + compile entry), test file for roundtrip verification
- **Notes**:
  - The `MessagePackSerializerOptions` with `FSharpResolver` + `ContractlessStandardResolver` is the canonical pattern. Store this options object in a module-level binding for reuse across cache read/write.
  - `ContractlessStandardResolver` handles public types without attributes. `FSharpResolver` handles DUs, options, lists, maps.
  - The `CompositeResolver.Create` call order matters: `FSharpResolver` first so it takes precedence for F# types.
  - If `AnalyzedType` or `AnalyzedField` fail to serialize (they contain `FieldKind` DU with nested cases), the smoke test will catch it immediately. If MessagePack cannot handle deeply nested DUs, we may need `[<MessagePackObject>]` attributes or a simplified serialization-friendly DTO layer. The smoke test is the validation gate.
  - `DateTimeOffset` serialization: MessagePack handles this via the `NativeDateTimeResolver`. If it fails, switch to serializing as ISO 8601 string.

---

## Test Strategy

- **AffordanceMap unit tests**: `test/Frank.Affordances.Tests/AffordanceMapTests.fs` -- lookup key construction, `tryFind` hit and miss, version field.
- **MessagePack roundtrip tests**: In the CLI test project -- verify `UnifiedResource`, `DerivedResourceFields`, `UnifiedExtractionState` all roundtrip through MessagePack serialization.
- **Build validation**: `dotnet build` from repository root must pass. `dotnet test` for all test projects must pass.

```bash
dotnet build
dotnet test test/Frank.Affordances.Tests/
```

---

## Risks & Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| MessagePack cannot serialize deeply nested F# DUs (`FieldKind` with `Optional(Collection(...))`) | Medium | T005 smoke test catches this immediately. Fallback: create a serialization-friendly DTO layer with explicit `[<MessagePackObject>]` attributes. |
| `AnalyzedType` contains `SourceLocation option` which has a `SourceLocation` type name collision with `Frank.Cli.Core.State.SourceLocation` | Low | `UnifiedModel.fs` opens `Frank.Cli.Core.Analysis` which has the correct `SourceLocation`. Use fully qualified names if ambiguous. |
| `Frank.Affordances` multi-targeting fails on TFMs where `Microsoft.AspNetCore.App` is unavailable | Low | Framework reference works for all three targets (net8.0/9.0/10.0). Standard pattern already used by Frank core. |
| Adding MessagePack increases `Frank.Cli.Core` package size | Low | MessagePack is ~500KB. Acceptable for a CLI tool. Does not affect `Frank.Affordances` (no MessagePack there yet). |

---

## Review Guidance

- Verify `UnifiedResource` record matches the data model document exactly (field names, types, nullability).
- Verify `Frank.Affordances.fsproj` has NO `ProjectReference` to `Frank.Statecharts`.
- Verify MessagePack roundtrip tests cover records, DUs, `Map`, `option`, `DateTimeOffset`, and nested types.
- Verify compile order in `Frank.Cli.Core.fsproj` places `Unified/UnifiedModel.fs` after `Statechart/StatechartExtractor.fs` (since it references `ExtractedStatechart`).
- Verify `dotnet build` and `dotnet test` pass cleanly.
- Verify `AffordanceMap.lookupKey` uses `|` separator (not `/` or other characters that appear in route templates).

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
- 2026-03-19T02:54:47Z – claude-opus – shell_pid=15168 – lane=doing – Assigned agent via workflow command
