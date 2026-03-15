---
work_package_id: WP01
title: Project Scaffold + ProvVocabulary + Core Types
lane: "doing"
dependencies: []
base_branch: master
base_commit: 9aa38215ad756a6530526dcd467907c32819df36
created_at: '2026-03-08T17:30:50.626046+00:00'
subtasks: [T001, T002, T003, T004, T005, T006]
shell_pid: "39978"
agent: "claude-opus-reviewer"
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-002, FR-012, FR-014]
---

# Work Package Prompt: WP01 -- Project Scaffold + ProvVocabulary + Core Types

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
spec-kitty implement WP01
```

No dependencies -- this is the starting package.

---

## Objectives & Success Criteria

- Create `Frank.Provenance` library project targeting net8.0;net9.0;net10.0
- Create `Frank.Provenance.Tests` test project targeting net10.0
- Define PROV-O vocabulary constants in `ProvVocabulary.fs`
- Define all core types: `AgentType` DU, `ProvenanceAgent`, `ProvenanceActivity`, `ProvenanceEntity`, `ProvenanceRecord`, `ProvenanceGraph`, `ProvenanceStoreConfig`
- Both projects compile; types are usable from test project
- All projects added to `Frank.sln`
- `dotnet build` succeeds on all three target frameworks
- `dotnet test` passes with vocabulary and type construction tests

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/006-prov-o-state-change-tracking/spec.md` -- Feature specification (FR-001 through FR-015)
- `kitty-specs/006-prov-o-state-change-tracking/data-model.md` -- Entity definitions, F# type signatures, PROV-O triple pattern
- `kitty-specs/006-prov-o-state-change-tracking/research.md` -- Decision 1 (PROV-O vocabulary mapping)

**Key constraints**:
- Follow `src/Frank.LinkedData/Frank.LinkedData.fsproj` structure for project configuration
- Multi-target: `net8.0;net9.0;net10.0` for library, `net10.0` for tests
- Project references to Frank, Frank.LinkedData (NOT NuGet)
- Package reference to `dotNetRdf.Core` version 3.5.1 (matching Frank.LinkedData)
- All types MUST be immutable F# records; `AgentType` MUST be a discriminated union
- PROV-O namespace: `http://www.w3.org/ns/prov#`
- Frank namespace: `https://frank-web.dev/ns/provenance/`
- `ProvVocabulary.fs` must be first in compilation order (other files depend on it)
- Test project uses Expecto (matching existing Frank test patterns)

---

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `Frank.Provenance.fsproj`

**Purpose**: Scaffold the library project with correct multi-targeting, project references, and NuGet dependencies.

**Steps**:
1. Create directory `src/Frank.Provenance/`
2. Create `Frank.Provenance.fsproj` with this structure:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>provenance;prov-o;audit;state-tracking;rdf</PackageTags>
    <Description>PROV-O provenance tracking for Frank stateful resource state changes</Description>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="ProvVocabulary.fs" />
    <Compile Include="Types.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Frank/Frank.fsproj" />
    <ProjectReference Include="../Frank.LinkedData/Frank.LinkedData.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="dotNetRdf.Core" Version="3.5.1" />
  </ItemGroup>

</Project>
```

3. Add to `Frank.sln`:
   ```bash
   dotnet sln Frank.sln add src/Frank.Provenance/Frank.Provenance.fsproj
   ```

**Files**: `src/Frank.Provenance/Frank.Provenance.fsproj`
**Notes**: The `<Compile>` list will grow in later WPs (Store.fs, MailboxProcessorStore.fs, GraphBuilder.fs, TransitionObserver.fs, Middleware.fs, WebHostBuilderExtensions.fs). Start with ProvVocabulary.fs and Types.fs only. Verify `src/Directory.Build.props` applies for shared packaging props (VersionPrefix, etc.). Do NOT add a Frank.Statecharts project reference yet -- it may not exist in the repo. WP05 (TransitionObserver) will add it when needed.

### Subtask T002 -- Create `ProvVocabulary.fs` with PROV-O constants

**Purpose**: Define all PROV-O and Frank-specific namespace URIs and term constants used throughout the library.

**Steps**:
1. Create `src/Frank.Provenance/ProvVocabulary.fs`
2. Use namespace `Frank.Provenance`
3. Define constants for all PROV-O classes, properties, and Frank extensions:

```fsharp
namespace Frank.Provenance

/// W3C PROV-O vocabulary URIs and Frank provenance extension terms.
/// All provenance-related URI constants are defined here (Constitution VIII: no duplicated logic).
[<RequireQualifiedAccess>]
module ProvVocabulary =

    /// PROV-O namespace prefix
    [<Literal>]
    let ProvNamespace = "http://www.w3.org/ns/prov#"

    /// Frank provenance extension namespace
    [<Literal>]
    let FrankNamespace = "https://frank-web.dev/ns/provenance/"

    /// XSD namespace for typed literals
    [<Literal>]
    let XsdNamespace = "http://www.w3.org/2001/XMLSchema#"

    // PROV-O Classes
    [<Literal>]
    let Entity = "http://www.w3.org/ns/prov#Entity"
    [<Literal>]
    let Activity = "http://www.w3.org/ns/prov#Activity"
    [<Literal>]
    let Agent = "http://www.w3.org/ns/prov#Agent"
    [<Literal>]
    let Person = "http://www.w3.org/ns/prov#Person"
    [<Literal>]
    let SoftwareAgent = "http://www.w3.org/ns/prov#SoftwareAgent"

    // PROV-O Properties
    [<Literal>]
    let wasGeneratedBy = "http://www.w3.org/ns/prov#wasGeneratedBy"
    [<Literal>]
    let used = "http://www.w3.org/ns/prov#used"
    [<Literal>]
    let wasAssociatedWith = "http://www.w3.org/ns/prov#wasAssociatedWith"
    [<Literal>]
    let wasAttributedTo = "http://www.w3.org/ns/prov#wasAttributedTo"
    [<Literal>]
    let wasDerivedFrom = "http://www.w3.org/ns/prov#wasDerivedFrom"
    [<Literal>]
    let startedAtTime = "http://www.w3.org/ns/prov#startedAtTime"
    [<Literal>]
    let endedAtTime = "http://www.w3.org/ns/prov#endedAtTime"
    [<Literal>]
    let label = "http://www.w3.org/ns/prov#label"

    // RDF type
    [<Literal>]
    let RdfType = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"

    // XSD types
    [<Literal>]
    let XsdDateTime = "http://www.w3.org/2001/XMLSchema#dateTime"

    // Frank extensions
    [<Literal>]
    let LlmAgent = "https://frank-web.dev/ns/provenance/LlmAgent"
    [<Literal>]
    let httpMethod = "https://frank-web.dev/ns/provenance/httpMethod"
    [<Literal>]
    let eventName = "https://frank-web.dev/ns/provenance/eventName"
    [<Literal>]
    let stateName = "https://frank-web.dev/ns/provenance/stateName"
    [<Literal>]
    let agentModel = "https://frank-web.dev/ns/provenance/agentModel"
```

**Files**: `src/Frank.Provenance/ProvVocabulary.fs`
**Notes**: All URIs use `[<Literal>]` for compile-time constants. `[<RequireQualifiedAccess>]` ensures callers write `ProvVocabulary.Entity` not just `Entity`, preventing name collisions with the `ProvenanceEntity` type. This module satisfies FR-014 (W3C PROV-O namespace) and Constitution VIII (no duplicated logic -- all URIs defined once).

### Subtask T003 -- Create `Types.fs` with core records and DU

**Purpose**: Define all core domain types as immutable F# records and discriminated unions.

**Steps**:
1. Create `src/Frank.Provenance/Types.fs`
2. Use namespace `Frank.Provenance`
3. Define types in this order (dependency order):

```fsharp
namespace Frank.Provenance

open System

/// Classifies the agent responsible for a state change.
type AgentType =
    /// Authenticated human user (from ClaimsPrincipal with identity claims)
    | Person of name: string * identifier: string
    /// System process or unauthenticated request
    | SoftwareAgent of identifier: string
    /// LLM-originated change (prov:SoftwareAgent + frank:LlmAgent annotation)
    | LlmAgent of identifier: string * model: string option

/// The actor responsible for a state change. Maps to prov:Agent.
type ProvenanceAgent = {
    Id: string
    AgentType: AgentType
}

/// The state transition event. Maps to prov:Activity.
type ProvenanceActivity = {
    Id: string
    HttpMethod: string
    ResourceUri: string
    EventName: string
    PreviousState: string
    NewState: string
    StartedAt: DateTimeOffset
    EndedAt: DateTimeOffset
}

/// A snapshot of resource state at a point in time. Maps to prov:Entity.
type ProvenanceEntity = {
    Id: string
    ResourceUri: string
    StateName: string
    CapturedAt: DateTimeOffset
}

/// Aggregate root: a complete PROV-O assertion for a single successful state transition.
type ProvenanceRecord = {
    Id: string
    ResourceUri: string
    Agent: ProvenanceAgent
    Activity: ProvenanceActivity
    UsedEntity: ProvenanceEntity
    GeneratedEntity: ProvenanceEntity
    RecordedAt: DateTimeOffset
}

/// A queryable collection of provenance records for a given resource scope.
type ProvenanceGraph = {
    ResourceUri: string
    Records: ProvenanceRecord list
}

/// Configuration for the default in-memory provenance store.
type ProvenanceStoreConfig = {
    MaxRecords: int
    EvictionBatchSize: int
}

module ProvenanceStoreConfig =
    let defaults = { MaxRecords = 10_000; EvictionBatchSize = 100 }
```

**Files**: `src/Frank.Provenance/Types.fs`
**Notes**:
- All records are immutable (no mutable fields).
- `AgentType` is a discriminated union with three cases matching spec FR-012.
- `ProvenanceRecord` is the aggregate root (data-model.md) -- not a PROV-O class itself but the container for the complete assertion.
- `ProvenanceStoreConfig.defaults` provides sensible defaults matching spec (10K records, batch 100).
- `ProvenanceEntity.Id` is GUID-based, distinct per snapshot (pre vs post transition).
- String-typed state names (not generic `'State`) per data-model.md Decision 1 -- avoids boxing complexity.

### Subtask T004 -- Create test project

**Purpose**: Scaffold the test project so types and vocabulary can be validated.

**Steps**:
1. Create directory `test/Frank.Provenance.Tests/`
2. Create `Frank.Provenance.Tests.fsproj`:

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
    <Compile Include="VocabularyTests.fs" />
    <Compile Include="TypeTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.3" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.Provenance/Frank.Provenance.fsproj" />
  </ItemGroup>

</Project>
```

3. Create `test/Frank.Provenance.Tests/Program.fs`:

```fsharp
module Program

open Expecto

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
```

4. Add to solution:
   ```bash
   dotnet sln Frank.sln add test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
   ```

**Files**: `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`, `test/Frank.Provenance.Tests/Program.fs`
**Notes**: Check existing test projects (e.g., `test/Frank.LinkedData.Tests/`) for exact Expecto/TestSdk versions and match them. The `<Compile>` list will grow in later WPs.

### Subtask T005 -- Create `VocabularyTests.fs`

**Purpose**: Validate that all PROV-O URI constants are correct and well-formed.

**Steps**:
1. Create `test/Frank.Provenance.Tests/VocabularyTests.fs`
2. Write Expecto tests verifying:

**a. Namespace URIs end with correct delimiters**:
- `ProvNamespace` ends with `#`
- `FrankNamespace` ends with `/`
- `XsdNamespace` ends with `#`

**b. PROV-O class URIs start with the PROV namespace**:
- `Entity`, `Activity`, `Agent`, `Person`, `SoftwareAgent` all start with `ProvNamespace`

**c. PROV-O property URIs start with the PROV namespace**:
- `wasGeneratedBy`, `used`, `wasAssociatedWith`, `wasAttributedTo`, `wasDerivedFrom`, `startedAtTime`, `endedAtTime`, `label` all start with `ProvNamespace`

**d. Frank extension URIs start with the Frank namespace**:
- `LlmAgent`, `httpMethod`, `eventName`, `stateName`, `agentModel` all start with `FrankNamespace`

**e. All URIs are valid System.Uri instances**:
- Construct `System.Uri` from each constant; verify no exceptions

**Files**: `test/Frank.Provenance.Tests/VocabularyTests.fs`
**Notes**: These tests serve as a safety net against typos in namespace URIs, which would produce invalid RDF. Test each URI individually so failures pinpoint the exact constant.

### Subtask T006 -- Create `TypeTests.fs`

**Purpose**: Validate that all core types can be constructed, pattern-matched, and have correct default values.

**Steps**:
1. Create `test/Frank.Provenance.Tests/TypeTests.fs`
2. Write Expecto tests covering:

**a. AgentType DU construction and pattern matching**:
- Construct `Person("Jane Doe", "jane@example.com")` and verify fields
- Construct `SoftwareAgent("system")` and verify field
- Construct `LlmAgent("claude", Some "claude-opus-4")` and verify fields
- Construct `LlmAgent("unknown", None)` and verify model is None

**b. ProvenanceAgent construction**:
- Create agent with `Person` type, verify `Id` and `AgentType` fields

**c. ProvenanceActivity construction**:
- Create activity with all fields populated, verify `StartedAt < EndedAt`

**d. ProvenanceEntity construction**:
- Create entity, verify all fields accessible

**e. ProvenanceRecord aggregate construction**:
- Create a complete record with agent, activity, used entity, generated entity
- Verify `UsedEntity.StateName` differs from `GeneratedEntity.StateName`

**f. ProvenanceGraph construction**:
- Create graph with multiple records, verify list ordering

**g. ProvenanceStoreConfig defaults**:
- Verify `ProvenanceStoreConfig.defaults.MaxRecords = 10_000`
- Verify `ProvenanceStoreConfig.defaults.EvictionBatchSize = 100`

**Files**: `test/Frank.Provenance.Tests/TypeTests.fs`
**Validation**: `dotnet test test/Frank.Provenance.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build` on both projects to verify multi-target compilation
- Run `dotnet test test/Frank.Provenance.Tests/` to verify all vocabulary and type tests pass
- Verify `dotnet build Frank.sln` succeeds (solution-level build)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| dotNetRdf.Core version conflict with Frank.LinkedData | Pin to 3.5.1 (same version). Verify with multi-target build. |
| Frank.Statecharts project reference missing | Do NOT add Statecharts reference in WP01. Types are self-contained. WP05 adds it. |
| `AgentType` name collision with `ProvenanceAgent.AgentType` field | F# handles this; field access is `agent.AgentType` (record field), DU is `AgentType.Person` (qualified). |

---

## Review Guidance

- Verify `.fsproj` matches Frank.LinkedData pattern (multi-target, project references, framework reference)
- Verify ProvVocabulary.fs is first in compilation order in `.fsproj`
- Verify all PROV-O URIs use the correct `http://www.w3.org/ns/prov#` namespace (not https)
- Verify `[<RequireQualifiedAccess>]` on `ProvVocabulary` module
- Verify all types are immutable records (no `mutable` keyword)
- Verify `ProvenanceStoreConfig.defaults` values match spec (10K records, 100 batch)
- Verify test project follows existing Frank test project patterns (Expecto, TestSdk versions)
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-08T17:30:50Z – claude-opus – shell_pid=97922 – lane=doing – Assigned agent via workflow command
- 2026-03-08T17:56:53Z – claude-opus – shell_pid=97922 – lane=for_review – T001-T006 complete: Vocabulary.fs, Types.fs, project scaffold. Builds clean.
- 2026-03-15T19:20:01Z – claude-opus – shell_pid=97922 – lane=for_review – Moved to for_review
- 2026-03-15T19:39:58Z – claude-opus-reviewer – shell_pid=39978 – lane=doing – Started review via workflow command
