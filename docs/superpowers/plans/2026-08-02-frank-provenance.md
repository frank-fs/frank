# Frank.Provenance Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A new `Frank.Provenance` package: a PROV-O vocabulary layer, a `ProvenanceRecord` type, and a bounded, in-memory, SPARQL-backed store with a closed query vocabulary — the core, HTTP-independent half of the design.

**Architecture:** Five files. `ProvVocabulary.fs` declares the closed PROV-O vocabulary this package uses (`ProvClass`, `ProvRelation`, both `[<Struct>]`) with `toIri` conversions. `Prov.fs` is a module of named constructor functions (`activity`, `entity`, `agent`, `wasGeneratedBy`, `wasAssociatedWith`, `used`, `startedAtTime`, `endedAtTime`, `wasDerivedFrom`, `specializationOf`) building `Frank.Rdf.Description` values, so callers never write a raw PROV IRI string. `ProvenanceRecord.fs` is the flat, explicit-authoring record type and its `toDoc` projection into `Prov`'s vocabulary. `ProvenanceStore.fs` declares the store's public contract: a closed `ProvenanceQuery` vocabulary (not open SPARQL), `SparqlQueryResult`, `IProvenanceStore`, and an internal `toSparqlQuery` translating each `ProvenanceQuery` case into a parameterized, pre-built `SparqlQuery` — SPARQL is the *implementation* mechanism, never the public surface. `MailboxProcessorProvenanceStore.fs` is the v1 implementation: one dotNetRDF `TripleStore` holding one named graph per appended record, queried via `LeviathanQueryProcessor` over an `InMemoryDataset`, with bounded eviction of the oldest named graphs.

**Tech Stack:** F# 8.0+, .NET 8.0/9.0/10.0 multi-targeting, `Frank.Rdf` (project reference), dotNetRdf.Core 3.5.1, `Microsoft.Extensions.Logging.Abstractions`, Expecto.

**Design doc:** `docs/superpowers/specs/2026-08-02-frank-provenance-design.md`

## Global Constraints

- `src/Frank.Provenance/` targets `net8.0;net9.0;net10.0`. **No `ProjectReference` to `Frank`, no `FrameworkReference` to `Microsoft.AspNetCore.App`** — this package has zero ASP.NET Core dependency, matching `Frank.Rdf`'s own constraint. Only `ProjectReference` to `Frank.Rdf`, `PackageReference dotNetRdf.Core 3.5.1` (exact version, matching every other use of this library in the codebase), and `PackageReference Microsoft.Extensions.Logging.Abstractions` (lightweight, not an ASP.NET Core package — used broadly outside web hosting).
- Every `.fs` file gets a matching `.fsi` immediately above it in `<Compile>` order (`CLAUDE.md`). Update both together in every task.
- `ProvClass` and `ProvRelation` are `[<Struct; RequireQualifiedAccess>]` — every reference to a case is qualified (`ProvClass.Activity`, `ProvRelation.WasGeneratedBy`), and neither type allocates on the heap.
- `ProvenanceQuery` is `[<RequireQualifiedAccess>]`. It is the **only** public way to query a store — nothing in this package's public API accepts a raw `SparqlQuery` or query string. `toSparqlQuery` (the DU-to-SPARQL translation) is `internal`, exposed to the test project via `InternalsVisibleTo`, exactly like `Frank.Rdf`'s `resolveIri`/`validatePrefixes`.
- Test framework is **Expecto**, matching every other Frank test project — not xUnit/NUnit.
- Commit after every task (this repo is trunk-based — commit directly, no PR).

## Out of scope for this plan

Everything that touches `HttpContext`/ASP.NET Core is deliberately deferred to a follow-on plan, once this core package exists to build on — the same split `Frank.Rdf`'s own plan used for HTTP serving (`docs/superpowers/plans/2026-07-31-frank-rdf.md`, "Out of scope for this plan"):

- **Explicit recording's `HttpContext` convenience wrapper** (`Prov.record : HttpContext -> ProvenanceRecord -> unit`, resolving `IProvenanceStore` from `ctx.RequestServices`) and **`Prov.enrich`** (the intentional-correlation operation against the auto-captured Activity). Both need `Microsoft.AspNetCore.Http`. This plan's `IProvenanceStore.Append`/`Query` are directly usable without either — a caller with an `IProvenanceStore` instance in hand can already record and query.
- **Auto-capture middleware** (`useProvenance` on a resource) and the **`ActivityTypeResolver` enrichment seam** (`Endpoint -> Uri option`) — both need `Microsoft.AspNetCore.Http.Endpoint`/middleware registration.
- **The two HTTP exposures** (sidecar `GET /provenance?resource=` query endpoint; inline content-negotiated per-request provenance) — need `Frank` core (`resource { }`, `negotiate { }`, `WebLink`).
- **Tic-tac-toe leaderboard integration** — a separate repository with its own git history, same reasoning as `Frank.Rdf`'s plan.

That follow-on plan's first task can start directly from this package's `IProvenanceStore`/`ProvenanceRecord` — nothing here blocks it.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/Frank.Provenance/Frank.Provenance.fsproj` | Create | Project file |
| `src/Frank.Provenance/ProvVocabulary.fsi` / `.fs` | Create | `ProvClass`, `ProvRelation` (struct DUs), `toIri` |
| `src/Frank.Provenance/Prov.fsi` / `.fs` | Create | Named PROV-O constructor functions over `Frank.Rdf.Description` |
| `src/Frank.Provenance/ProvenanceRecord.fsi` / `.fs` | Create | `ProvenanceRecord`, `ProvenanceRecord.toDoc` |
| `src/Frank.Provenance/ProvenanceStore.fsi` / `.fs` | Create | `ProvenanceQuery`, `SparqlQueryResult`, `IProvenanceStore`, `ProvenanceStoreConfig`, internal `toSparqlQuery` |
| `src/Frank.Provenance/MailboxProcessorProvenanceStore.fsi` / `.fs` | Create | The v1 store implementation |
| `Frank.sln` | Modify | Register `Frank.Provenance` and `Frank.Provenance.Tests` |
| `test/Frank.Provenance.Tests/*` | Create | Unit, round-trip, and query-verification tests |

---

### Task 1: Project scaffold + `ProvVocabulary`

**Files:**
- Create: `src/Frank.Provenance/Frank.Provenance.fsproj`
- Create: `src/Frank.Provenance/ProvVocabulary.fsi`, `src/Frank.Provenance/ProvVocabulary.fs`
- Create: `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`
- Create: `test/Frank.Provenance.Tests/ProvVocabularyTests.fs`, `test/Frank.Provenance.Tests/Program.fs`
- Modify: `Frank.sln` (via `dotnet sln add`)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `[<Struct; RequireQualifiedAccess>] type ProvClass = Activity | Entity | Agent`
  - `module ProvClass = val toIri : ProvClass -> string`
  - `[<Struct; RequireQualifiedAccess>] type ProvRelation = WasGeneratedBy | WasAssociatedWith | Used | StartedAtTime | EndedAtTime | WasDerivedFrom | SpecializationOf`
  - `module ProvRelation = val toIri : ProvRelation -> string`

- [ ] **Step 1: Create the package project structure**

```bash
mkdir -p "C:/Users/ryanr/Code/frank/.claude/worktrees/provenance/src/Frank.Provenance"
mkdir -p "C:/Users/ryanr/Code/frank/.claude/worktrees/provenance/test/Frank.Provenance.Tests"
```

Create `src/Frank.Provenance/Frank.Provenance.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>rdf;provenance;prov-o;linked-data</PackageTags>
    <Description>PROV-O provenance recording and querying for Frank resources, built on Frank.Rdf</Description>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="ProvVocabulary.fsi" />
    <Compile Include="ProvVocabulary.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="dotNetRdf.Core" Version="3.5.1" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Frank.Rdf/Frank.Rdf.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Frank.Provenance.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write `ProvVocabulary.fsi`**

```fsharp
namespace Frank.Provenance

/// The PROV-O "starting-point" classes this package uses. Data-free cases -- [<Struct>]
/// is a clear win here (no heap allocation, no field-reservation cost, since no case carries data).
[<Struct; RequireQualifiedAccess>]
type ProvClass =
    | Activity
    | Entity
    | Agent

module ProvClass =
    /// The absolute PROV-O IRI for a class, e.g. "http://www.w3.org/ns/prov#Activity".
    val toIri: c: ProvClass -> string

/// The PROV-O relations this package uses. Data-free cases, same [<Struct>] reasoning as ProvClass.
[<Struct; RequireQualifiedAccess>]
type ProvRelation =
    | WasGeneratedBy
    | WasAssociatedWith
    | Used
    | StartedAtTime
    | EndedAtTime
    | WasDerivedFrom
    | SpecializationOf

module ProvRelation =
    /// The absolute PROV-O IRI for a relation, e.g. "http://www.w3.org/ns/prov#wasGeneratedBy".
    val toIri: r: ProvRelation -> string
```

- [ ] **Step 3: Write `ProvVocabulary.fs`**

```fsharp
namespace Frank.Provenance

[<Literal>]
let private ProvNamespace = "http://www.w3.org/ns/prov#"

[<Struct; RequireQualifiedAccess>]
type ProvClass =
    | Activity
    | Entity
    | Agent

module ProvClass =
    let toIri (c: ProvClass) : string =
        match c with
        | ProvClass.Activity -> ProvNamespace + "Activity"
        | ProvClass.Entity -> ProvNamespace + "Entity"
        | ProvClass.Agent -> ProvNamespace + "Agent"

[<Struct; RequireQualifiedAccess>]
type ProvRelation =
    | WasGeneratedBy
    | WasAssociatedWith
    | Used
    | StartedAtTime
    | EndedAtTime
    | WasDerivedFrom
    | SpecializationOf

module ProvRelation =
    let toIri (r: ProvRelation) : string =
        match r with
        | ProvRelation.WasGeneratedBy -> ProvNamespace + "wasGeneratedBy"
        | ProvRelation.WasAssociatedWith -> ProvNamespace + "wasAssociatedWith"
        | ProvRelation.Used -> ProvNamespace + "used"
        | ProvRelation.StartedAtTime -> ProvNamespace + "startedAtTime"
        | ProvRelation.EndedAtTime -> ProvNamespace + "endedAtTime"
        | ProvRelation.WasDerivedFrom -> ProvNamespace + "wasDerivedFrom"
        | ProvRelation.SpecializationOf -> ProvNamespace + "specializationOf"
```

- [ ] **Step 4: Write the test project**

Create `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`:

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
    <Compile Include="ProvVocabularyTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="Expecto" Version="10.*" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.Provenance/Frank.Provenance.fsproj" />
  </ItemGroup>

</Project>
```

Create `test/Frank.Provenance.Tests/Program.fs`:

```fsharp
module Frank.Provenance.Tests.Program

open Expecto

[<EntryPoint>]
let main argv = Tests.runTestsInAssemblyWithCLIArgs [] argv
```

Create `test/Frank.Provenance.Tests/ProvVocabularyTests.fs`:

```fsharp
module Frank.Provenance.Tests.ProvVocabularyTests

open Expecto
open Frank.Provenance

[<Tests>]
let tests =
    testList
        "ProvVocabulary"
        [ test "ProvClass.toIri produces the correct absolute PROV-O IRI for every case" {
              Expect.equal (ProvClass.toIri ProvClass.Activity) "http://www.w3.org/ns/prov#Activity" ""
              Expect.equal (ProvClass.toIri ProvClass.Entity) "http://www.w3.org/ns/prov#Entity" ""
              Expect.equal (ProvClass.toIri ProvClass.Agent) "http://www.w3.org/ns/prov#Agent" ""
          }

          test "ProvRelation.toIri produces the correct absolute PROV-O IRI for every case" {
              Expect.equal (ProvRelation.toIri ProvRelation.WasGeneratedBy) "http://www.w3.org/ns/prov#wasGeneratedBy" ""
              Expect.equal (ProvRelation.toIri ProvRelation.WasAssociatedWith) "http://www.w3.org/ns/prov#wasAssociatedWith" ""
              Expect.equal (ProvRelation.toIri ProvRelation.Used) "http://www.w3.org/ns/prov#used" ""
              Expect.equal (ProvRelation.toIri ProvRelation.StartedAtTime) "http://www.w3.org/ns/prov#startedAtTime" ""
              Expect.equal (ProvRelation.toIri ProvRelation.EndedAtTime) "http://www.w3.org/ns/prov#endedAtTime" ""
              Expect.equal (ProvRelation.toIri ProvRelation.WasDerivedFrom) "http://www.w3.org/ns/prov#wasDerivedFrom" ""
              Expect.equal (ProvRelation.toIri ProvRelation.SpecializationOf) "http://www.w3.org/ns/prov#specializationOf" ""
          } ]
```

- [ ] **Step 5: Register both projects in the solution**

```bash
cd "C:/Users/ryanr/Code/frank/.claude/worktrees/provenance"
dotnet sln Frank.sln add src/Frank.Provenance/Frank.Provenance.fsproj
dotnet sln Frank.sln add test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

- [ ] **Step 6: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

Expected: 2 tests pass.

- [ ] **Step 7: Commit**

```bash
git add Frank.sln src/Frank.Provenance test/Frank.Provenance.Tests
git commit -m "feat(provenance): scaffold Frank.Provenance package, add ProvClass/ProvRelation vocabulary"
```

---

### Task 2: `Prov` module — named PROV-O constructor functions

**Files:**
- Modify: `src/Frank.Provenance/Frank.Provenance.fsproj` (add `Prov.fsi`/`Prov.fs`)
- Create: `src/Frank.Provenance/Prov.fsi`, `src/Frank.Provenance/Prov.fs`
- Modify: `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`
- Create: `test/Frank.Provenance.Tests/ProvTests.fs`

**Interfaces:**
- Consumes: `ProvClass`, `ProvRelation` (Task 1); `Frank.Rdf.Node`, `Frank.Rdf.Value`, `Frank.Rdf.Literal`, `Frank.Rdf.Description`, `Frank.Rdf.describe`, `Frank.Rdf.RdfTypeIri` (`Frank.Rdf`, already shipped).
- Produces:
  - `val activity: id: Node -> Description`
  - `val entity: id: Node -> Description`
  - `val agent: id: Node -> Description`
  - `val wasGeneratedBy: activity: Node -> Description -> Description`
  - `val wasAssociatedWith: agent: Node -> Description -> Description`
  - `val used: entity: Node -> Description -> Description`
  - `val startedAtTime: t: System.DateTimeOffset -> Description -> Description`
  - `val endedAtTime: t: System.DateTimeOffset -> Description -> Description`
  - `val wasDerivedFrom: entity: Node -> Description -> Description`
  - `val specializationOf: entity: Node -> Description -> Description`

**Background you need:**

`Frank.Rdf.Description = { Subject: Node; Statements: (string * Value) list }` (plain record, `test/Frank.Rdf.Tests` already covers its shape). `describe subject { typ "..." }` (the `Frank.Rdf` CE) is the right tool for the three base constructors (`activity`/`entity`/`agent`), since each is a single-shot "make a new Description typed as X." The relation combinators (`wasGeneratedBy` etc.) take an *existing* `Description` and add one more statement to it — the CE's `Yield` always starts a fresh `Description`, so these are plain record-update functions instead, the same shape `DescribeBuilder.Typ`/`PropertyNode` already use internally (`{ d with Statements = d.Statements @ [...] }`). `ProvClass.toIri`/`ProvRelation.toIri` already return absolute IRIs, so passing them through `typ`/as a predicate needs no prefix declaration — `Frank.Rdf`'s `resolveIri` passes an already-absolute IRI through unchanged (covered by `test/Frank.Rdf.Tests/PrefixResolutionTests.fs`).

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Provenance.Tests/ProvTests.fs`:

```fsharp
module Frank.Provenance.Tests.ProvTests

open System
open Expecto
open Frank.Rdf
open Frank.Provenance

[<Tests>]
let tests =
    testList
        "Prov"
        [ test "activity types the subject as prov:Activity" {
              let d = Prov.activity (Node.Iri "https://example.org/a1")

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Activity") ]
                  ""
          }

          test "entity types the subject as prov:Entity" {
              let d = Prov.entity (Node.Iri "https://example.org/e1")

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Entity") ]
                  ""
          }

          test "agent types the subject as prov:Agent" {
              let d = Prov.agent (Node.Iri "https://example.org/ag1")

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Agent") ]
                  ""
          }

          test "wasGeneratedBy adds a prov:wasGeneratedBy statement pointing at the given activity" {
              let d =
                  Prov.entity (Node.Iri "https://example.org/e1")
                  |> Prov.wasGeneratedBy (Node.Iri "https://example.org/a1")

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasGeneratedBy", Value.Node(Node.Iri "https://example.org/a1"))
                  "Second statement, after the rdf:type from entity"
          }

          test "wasAssociatedWith adds a prov:wasAssociatedWith statement pointing at the given agent" {
              let d =
                  Prov.activity (Node.Iri "https://example.org/a1")
                  |> Prov.wasAssociatedWith (Node.Iri "https://example.org/ag1")

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasAssociatedWith", Value.Node(Node.Iri "https://example.org/ag1"))
                  ""
          }

          test "used adds a prov:used statement pointing at the given entity" {
              let d =
                  Prov.activity (Node.Iri "https://example.org/a1") |> Prov.used (Node.Iri "https://example.org/e1")

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#used", Value.Node(Node.Iri "https://example.org/e1"))
                  ""
          }

          test "startedAtTime and endedAtTime add DateTimeOffset-literal statements" {
              let t0 = DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)
              let t1 = DateTimeOffset(2026, 8, 2, 12, 0, 1, TimeSpan.Zero)

              let d =
                  Prov.activity (Node.Iri "https://example.org/a1")
                  |> Prov.startedAtTime t0
                  |> Prov.endedAtTime t1

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#startedAtTime", Value.Literal(Literal.DateTime t0))
                  ""

              Expect.equal
                  d.Statements.[2]
                  ("http://www.w3.org/ns/prov#endedAtTime", Value.Literal(Literal.DateTime t1))
                  ""
          }

          test "wasDerivedFrom and specializationOf add statements pointing at the given entity" {
              let d =
                  Prov.entity (Node.Iri "https://example.org/e2")
                  |> Prov.wasDerivedFrom (Node.Iri "https://example.org/e1")
                  |> Prov.specializationOf (Node.Iri "https://example.org/e1")

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasDerivedFrom", Value.Node(Node.Iri "https://example.org/e1"))
                  ""

              Expect.equal
                  d.Statements.[2]
                  ("http://www.w3.org/ns/prov#specializationOf", Value.Node(Node.Iri "https://example.org/e1"))
                  ""
          }

          test "combinators compose freely, in order, onto one Description" {
              let t0 = DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)
              let t1 = DateTimeOffset(2026, 8, 2, 12, 0, 1, TimeSpan.Zero)

              let d =
                  Prov.activity (Node.Iri "https://example.org/a1")
                  |> Prov.wasAssociatedWith (Node.Iri "https://example.org/ag1")
                  |> Prov.used (Node.Iri "https://example.org/e1")
                  |> Prov.startedAtTime t0
                  |> Prov.endedAtTime t1

              Expect.equal d.Statements.Length 5 "type + wasAssociatedWith + used + startedAtTime + endedAtTime"
              Expect.equal d.Subject (Node.Iri "https://example.org/a1") "Subject unchanged by combinators"
          } ]
```

Add it to `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="ProvTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

Expected: build failure — `Prov` is not defined.

- [ ] **Step 3: Write `Prov.fsi`**

```fsharp
namespace Frank.Provenance

open System
open Frank.Rdf

/// Named constructor functions for the closed PROV-O vocabulary this package uses. Callers never
/// write a raw PROV IRI string -- every function here wraps a `ProvClass`/`ProvRelation` case.
/// Builds directly on `Frank.Rdf.Description`; not a parallel triple model.
module Prov =
    /// A Description whose subject is typed prov:Activity.
    val activity: id: Node -> Description
    /// A Description whose subject is typed prov:Entity.
    val entity: id: Node -> Description
    /// A Description whose subject is typed prov:Agent.
    val agent: id: Node -> Description

    /// Adds prov:wasGeneratedBy, pointing at the given Activity node.
    val wasGeneratedBy: activity: Node -> Description -> Description
    /// Adds prov:wasAssociatedWith, pointing at the given Agent node.
    val wasAssociatedWith: agent: Node -> Description -> Description
    /// Adds prov:used, pointing at the given Entity node.
    val used: entity: Node -> Description -> Description
    /// Adds prov:startedAtTime as a DateTimeOffset-typed literal.
    val startedAtTime: t: DateTimeOffset -> Description -> Description
    /// Adds prov:endedAtTime as a DateTimeOffset-typed literal.
    val endedAtTime: t: DateTimeOffset -> Description -> Description
    /// Adds prov:wasDerivedFrom, pointing at the given Entity node.
    val wasDerivedFrom: entity: Node -> Description -> Description
    /// Adds prov:specializationOf, pointing at the given Entity node.
    val specializationOf: entity: Node -> Description -> Description
```

- [ ] **Step 4: Write `Prov.fs`**

```fsharp
namespace Frank.Provenance

open System
open Frank.Rdf

module Prov =
    let activity (id: Node) : Description = describe id { typ (ProvClass.toIri ProvClass.Activity) }
    let entity (id: Node) : Description = describe id { typ (ProvClass.toIri ProvClass.Entity) }
    let agent (id: Node) : Description = describe id { typ (ProvClass.toIri ProvClass.Agent) }

    let private addProperty (predicate: string) (value: Value) (d: Description) : Description =
        { d with
            Statements = d.Statements @ [ predicate, value ] }

    let wasGeneratedBy (activity: Node) (d: Description) : Description =
        d |> addProperty (ProvRelation.toIri ProvRelation.WasGeneratedBy) (Value.Node activity)

    let wasAssociatedWith (agent: Node) (d: Description) : Description =
        d |> addProperty (ProvRelation.toIri ProvRelation.WasAssociatedWith) (Value.Node agent)

    let used (entity: Node) (d: Description) : Description =
        d |> addProperty (ProvRelation.toIri ProvRelation.Used) (Value.Node entity)

    let startedAtTime (t: DateTimeOffset) (d: Description) : Description =
        d
        |> addProperty (ProvRelation.toIri ProvRelation.StartedAtTime) (Value.Literal(Literal.DateTime t))

    let endedAtTime (t: DateTimeOffset) (d: Description) : Description =
        d
        |> addProperty (ProvRelation.toIri ProvRelation.EndedAtTime) (Value.Literal(Literal.DateTime t))

    let wasDerivedFrom (entity: Node) (d: Description) : Description =
        d |> addProperty (ProvRelation.toIri ProvRelation.WasDerivedFrom) (Value.Node entity)

    let specializationOf (entity: Node) (d: Description) : Description =
        d |> addProperty (ProvRelation.toIri ProvRelation.SpecializationOf) (Value.Node entity)
```

Add `Prov.fsi`/`Prov.fs` to `src/Frank.Provenance/Frank.Provenance.fsproj`, after the `ProvVocabulary` entries:

```xml
    <Compile Include="ProvVocabulary.fsi" />
    <Compile Include="ProvVocabulary.fs" />
    <Compile Include="Prov.fsi" />
    <Compile Include="Prov.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Provenance test/Frank.Provenance.Tests
git commit -m "feat(provenance): Prov module -- named PROV-O constructors over Frank.Rdf.Description"
```

---

### Task 3: `ProvenanceRecord` + `toDoc`

**Files:**
- Modify: `src/Frank.Provenance/Frank.Provenance.fsproj`
- Create: `src/Frank.Provenance/ProvenanceRecord.fsi`, `src/Frank.Provenance/ProvenanceRecord.fs`
- Modify: `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`
- Create: `test/Frank.Provenance.Tests/ProvenanceRecordTests.fs`

**Interfaces:**
- Consumes: `Prov` (Task 2); `Frank.Rdf.Node`, `Frank.Rdf.Value`, `Frank.Rdf.Doc`, `Frank.Rdf.rdf`, `Frank.Rdf.Doc.toGraph`, `Frank.Rdf.Doc.toJsonLd` (`Frank.Rdf`, already shipped).
- Produces:
  - `type ProvenanceRecord = { Activity: Node; Resource: Node; Agent: Node; StartedAt: DateTimeOffset; EndedAt: DateTimeOffset; ActivityType: Uri option; Properties: (string * Value) list }`
  - `module ProvenanceRecord = val toDoc : ProvenanceRecord -> Doc`

**Background you need:**

Per the design doc's *Record shape* section: `Activity` gets `Prov.activity` plus, if `ActivityType` is `Some`, an additional plain `typ` assertion with that domain IRI (the "`@type` includes both `prov:Activity` and `schema:OrderAction`" case — not routed through `Prov`, since it's not PROV vocabulary). `Resource` gets `Prov.entity` plus `Prov.wasGeneratedBy` pointing at `Activity`. `Agent` gets `Prov.agent`. `StartedAt`/`EndedAt` land on the *Activity* Description via `Prov.startedAtTime`/`Prov.endedAtTime`. `Agent` is connected to the Activity via `Prov.wasAssociatedWith`, also on the Activity Description. `Properties` are appended to the Activity Description's own statement list, as-is. The result is three `Description`s (Activity, Resource, Agent), combined into one `Doc` via `rdf { about ...; about ...; about ... }`.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Provenance.Tests/ProvenanceRecordTests.fs`:

```fsharp
module Frank.Provenance.Tests.ProvenanceRecordTests

open System
open System.IO
open Expecto
open VDS.RDF
open VDS.RDF.Parsing
open Frank.Rdf
open Frank.Provenance

let private sampleRecord () : ProvenanceRecord =
    { Activity = Node.Iri "https://example.org/activities/1"
      Resource = Node.Iri "https://example.org/games/1"
      Agent = Node.Iri "https://example.org/users/42"
      StartedAt = DateTimeOffset(2026, 8, 2, 12, 0, 0, TimeSpan.Zero)
      EndedAt = DateTimeOffset(2026, 8, 2, 12, 0, 1, TimeSpan.Zero)
      ActivityType = None
      Properties = [] }

[<Tests>]
let tests =
    testList
        "ProvenanceRecord"
        [ test "toDoc types the Activity node as prov:Activity" {
              let graph = sampleRecord () |> ProvenanceRecord.toDoc |> Doc.toGraph
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let typeNode = graph.CreateUriNode(Uri RdfTypeIri)
              let activityClassNode = graph.CreateUriNode(Uri(ProvClass.toIri ProvClass.Activity))

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicateObject(activityNode, typeNode, activityClassNode) |> Seq.length)
                  0
                  "Activity node is typed prov:Activity"
          }

          test "toDoc types the Resource node as prov:Entity and connects it via wasGeneratedBy" {
              let graph = sampleRecord () |> ProvenanceRecord.toDoc |> Doc.toGraph
              let resourceNode = graph.CreateUriNode(Uri "https://example.org/games/1")
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let typeNode = graph.CreateUriNode(Uri RdfTypeIri)
              let entityClassNode = graph.CreateUriNode(Uri(ProvClass.toIri ProvClass.Entity))
              let wasGeneratedByNode = graph.CreateUriNode(Uri(ProvRelation.toIri ProvRelation.WasGeneratedBy))

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicateObject(resourceNode, typeNode, entityClassNode) |> Seq.length)
                  0
                  "Resource node is typed prov:Entity"

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicateObject(resourceNode, wasGeneratedByNode, activityNode)
                   |> Seq.length)
                  0
                  "Resource prov:wasGeneratedBy Activity"
          }

          test "toDoc types the Agent node as prov:Agent and connects it via wasAssociatedWith" {
              let graph = sampleRecord () |> ProvenanceRecord.toDoc |> Doc.toGraph
              let agentNode = graph.CreateUriNode(Uri "https://example.org/users/42")
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let typeNode = graph.CreateUriNode(Uri RdfTypeIri)
              let agentClassNode = graph.CreateUriNode(Uri(ProvClass.toIri ProvClass.Agent))
              let wasAssociatedWithNode = graph.CreateUriNode(Uri(ProvRelation.toIri ProvRelation.WasAssociatedWith))

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicateObject(agentNode, typeNode, agentClassNode) |> Seq.length)
                  0
                  "Agent node is typed prov:Agent"

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicateObject(activityNode, wasAssociatedWithNode, agentNode)
                   |> Seq.length)
                  0
                  "Activity prov:wasAssociatedWith Agent"
          }

          test "toDoc asserts startedAtTime and endedAtTime on the Activity" {
              let graph = sampleRecord () |> ProvenanceRecord.toDoc |> Doc.toGraph
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let startedNode = graph.CreateUriNode(Uri(ProvRelation.toIri ProvRelation.StartedAtTime))
              let endedNode = graph.CreateUriNode(Uri(ProvRelation.toIri ProvRelation.EndedAtTime))

              Expect.equal (graph.GetTriplesWithSubjectPredicate(activityNode, startedNode) |> Seq.length) 1 ""
              Expect.equal (graph.GetTriplesWithSubjectPredicate(activityNode, endedNode) |> Seq.length) 1 ""
          }

          test "toDoc adds an extra rdf:type for ActivityType, alongside prov:Activity, when Some" {
              let record =
                  { sampleRecord () with
                      ActivityType = Some(Uri "https://schema.org/OrderAction") }

              let graph = record |> ProvenanceRecord.toDoc |> Doc.toGraph
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let typeNode = graph.CreateUriNode(Uri RdfTypeIri)
              let domainTypeNode = graph.CreateUriNode(Uri "https://schema.org/OrderAction")
              let provActivityNode = graph.CreateUriNode(Uri(ProvClass.toIri ProvClass.Activity))

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicateObject(activityNode, typeNode, domainTypeNode) |> Seq.length)
                  0
                  "Domain type asserted"

              Expect.isGreaterThan
                  (graph.GetTriplesWithSubjectPredicateObject(activityNode, typeNode, provActivityNode) |> Seq.length)
                  0
                  "prov:Activity still asserted alongside the domain type"
          }

          test "toDoc omits any extra rdf:type when ActivityType is None" {
              let graph = sampleRecord () |> ProvenanceRecord.toDoc |> Doc.toGraph
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let typeNode = graph.CreateUriNode(Uri RdfTypeIri)

              Expect.equal (graph.GetTriplesWithSubjectPredicate(activityNode, typeNode) |> Seq.length) 1 "Only prov:Activity"
          }

          test "toDoc attaches Properties to the Activity node as-is" {
              let record =
                  { sampleRecord () with
                      Properties = [ "https://schema.org/cellIndex", Value.Literal(Literal.Int 4) ] }

              let graph = record |> ProvenanceRecord.toDoc |> Doc.toGraph
              let activityNode = graph.CreateUriNode(Uri "https://example.org/activities/1")
              let cellIndexNode = graph.CreateUriNode(Uri "https://schema.org/cellIndex")

              Expect.equal (graph.GetTriplesWithSubjectPredicate(activityNode, cellIndexNode) |> Seq.length) 1 ""
          }

          test "toDoc round-trips through JSON-LD to an isomorphic graph" {
              // Same pattern as Frank.Rdf's own RoundTripTests.fs: serialize, parse the JSON-LD back
              // into a graph with dotNetRDF's own reader, assert isomorphism. Stronger than asserting
              // against a hand-written expected string.
              let record =
                  { sampleRecord () with
                      ActivityType = Some(Uri "https://schema.org/OrderAction")
                      Properties = [ "https://schema.org/cellIndex", Value.Literal(Literal.Int 4) ] }

              let doc = ProvenanceRecord.toDoc record
              let originalGraph = Doc.toGraph doc :> IGraph

              let store = TripleStore()
              use reader = new StringReader(Doc.toJsonLd doc)
              JsonLdParser().Load(store, reader)
              let parsedGraph = store.Graphs |> Seq.exactlyOne

              Expect.isTrue (originalGraph.Equals(parsedGraph)) "Isomorphic after round-trip"
          } ]
```

Add it to `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="ProvenanceRecordTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

Expected: build failure — `ProvenanceRecord` is not defined.

- [ ] **Step 3: Write `ProvenanceRecord.fsi`**

```fsharp
namespace Frank.Provenance

open System
open Frank.Rdf

/// A single PROV-O record: an Activity, the Resource (Entity) it acted on, the Agent responsible,
/// when it ran, an optional domain type for the Activity, and any additional properties.
type ProvenanceRecord =
    { Activity: Node
      Resource: Node
      Agent: Node
      StartedAt: DateTimeOffset
      EndedAt: DateTimeOffset
      ActivityType: Uri option
      Properties: (string * Value) list }

module ProvenanceRecord =
    /// Projects a ProvenanceRecord into a Doc: Activity typed prov:Activity (plus ActivityType, if
    /// Some, as an additional rdf:type), Resource typed prov:Entity and connected via
    /// prov:wasGeneratedBy, Agent typed prov:Agent and connected via prov:wasAssociatedWith,
    /// StartedAt/EndedAt on the Activity, Properties attached to the Activity as-is.
    val toDoc: record: ProvenanceRecord -> Doc
```

- [ ] **Step 4: Write `ProvenanceRecord.fs`**

```fsharp
namespace Frank.Provenance

open System
open Frank.Rdf

type ProvenanceRecord =
    { Activity: Node
      Resource: Node
      Agent: Node
      StartedAt: DateTimeOffset
      EndedAt: DateTimeOffset
      ActivityType: Uri option
      Properties: (string * Value) list }

module ProvenanceRecord =
    let toDoc (record: ProvenanceRecord) : Doc =
        let activityDescription =
            let withProvStatements =
                Prov.activity record.Activity
                |> Prov.wasAssociatedWith record.Agent
                |> Prov.startedAtTime record.StartedAt
                |> Prov.endedAtTime record.EndedAt

            let withDomainType =
                match record.ActivityType with
                | Some iri ->
                    { withProvStatements with
                        Statements = withProvStatements.Statements @ [ RdfTypeIri, Value.Node(Node.Iri iri.AbsoluteUri) ] }
                | None -> withProvStatements

            { withDomainType with
                Statements = withDomainType.Statements @ record.Properties }

        let resourceDescription = Prov.entity record.Resource |> Prov.wasGeneratedBy record.Activity

        let agentDescription = Prov.agent record.Agent

        rdf {
            about activityDescription
            about resourceDescription
            about agentDescription
        }
```

Add `ProvenanceRecord.fsi`/`ProvenanceRecord.fs` to `src/Frank.Provenance/Frank.Provenance.fsproj`, after the `Prov` entries:

```xml
    <Compile Include="Prov.fsi" />
    <Compile Include="Prov.fs" />
    <Compile Include="ProvenanceRecord.fsi" />
    <Compile Include="ProvenanceRecord.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Provenance test/Frank.Provenance.Tests
git commit -m "feat(provenance): ProvenanceRecord + toDoc projects it into PROV-O triples"
```

---

### Task 4: Store contract — `ProvenanceQuery`, `SparqlQueryResult`, `IProvenanceStore`, `toSparqlQuery`

**Files:**
- Modify: `src/Frank.Provenance/Frank.Provenance.fsproj`
- Create: `src/Frank.Provenance/ProvenanceStore.fsi`, `src/Frank.Provenance/ProvenanceStore.fs`
- Modify: `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`
- Create: `test/Frank.Provenance.Tests/ProvenanceQueryTests.fs`

**Interfaces:**
- Consumes: `ProvClass`, `ProvRelation` (Task 1); `ProvenanceRecord` (Task 3).
- Produces:
  - `[<RequireQualifiedAccess>] type ProvenanceQuery = ByResource of resourceIri: string | ByAgent of agentIri: string | ByActivityId of activityIri: string`
  - `[<RequireQualifiedAccess>] type SparqlQueryResult = Bindings of VDS.RDF.Query.SparqlResultSet | Graph of VDS.RDF.IGraph`
  - `type IProvenanceStore = abstract Append: ProvenanceRecord -> unit; abstract Query: ProvenanceQuery -> SparqlQueryResult`
  - `type ProvenanceStoreConfig = { MaxRecords: int; EvictionBatchSize: int }` with `module ProvenanceStoreConfig = val defaults: ProvenanceStoreConfig`
  - `internal toSparqlQuery: ProvenanceQuery -> VDS.RDF.Query.SparqlQuery`

**Background you need:**

`SparqlParameterizedString` (`VDS.RDF.Query`) is dotNetRDF's safe query-parameterization type: construct it with the command text (using `@name` placeholders), call `SetUri(name, uri)` to bind a parameter, then `.ToString()` renders the final query text with the value safely substituted (verified against `dotnetrdf/dotnetrdf`'s `SparqlParameterizedString.cs` on GitHub — constructor `SparqlParameterizedString(command: string)`, `SetUri(name: string, value: Uri) : unit`, `@name` placeholder syntax). Parse the rendered text with `SparqlQueryParser().ParseFromString(text)` — there is no way to get a `SparqlQuery` directly from `SparqlParameterizedString`, this two-step is the only path.

Each `ProvenanceQuery` case becomes a `CONSTRUCT` (or `DESCRIBE`) query, so `SparqlQueryResult.Graph` is what callers get back — matching the design doc's HTTP surface, which wants graphs (JSON-LD), not bindings tables:

- `ByResource resourceIri`: everything about the resource itself, plus everything about every Activity it `prov:wasGeneratedBy` points at.
- `ByAgent agentIri`: everything about every Activity that `prov:wasAssociatedWith` points at the given agent.
- `ByActivityId activityIri`: everything about that one Activity — `DESCRIBE @activity` is exactly this.

`SparqlQueryResult.Bindings` exists in the type because `SparqlQuery`/`LeviathanQueryProcessor` can in general return either shape (`SELECT`/`ASK` vs. `CONSTRUCT`/`DESCRIBE`) — Task 5's `MailboxProcessorProvenanceStore` pattern-matches on the actual runtime result rather than assuming which one came back, so the type has to allow both even though every `ProvenanceQuery` case in *this* package happens to produce a `Graph`.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Provenance.Tests/ProvenanceQueryTests.fs`:

```fsharp
module Frank.Provenance.Tests.ProvenanceQueryTests

open Expecto
open Frank.Provenance

[<Tests>]
let tests =
    testList
        "ProvenanceQuery -> SparqlQuery"
        [ test "ByResource produces a query naming the resource IRI, resolvable by a real SPARQL parser" {
              let query = toSparqlQuery (ProvenanceQuery.ByResource "https://example.org/games/1")
              Expect.stringContains (query.ToString()) "https://example.org/games/1" ""
          }

          test "ByAgent produces a query naming the agent IRI" {
              let query = toSparqlQuery (ProvenanceQuery.ByAgent "https://example.org/users/42")
              Expect.stringContains (query.ToString()) "https://example.org/users/42" ""
          }

          test "ByActivityId produces a query naming the activity IRI" {
              let query = toSparqlQuery (ProvenanceQuery.ByActivityId "https://example.org/activities/1")
              Expect.stringContains (query.ToString()) "https://example.org/activities/1" ""
          }

          test "ProvenanceStoreConfig.defaults has a positive MaxRecords and EvictionBatchSize" {
              Expect.isGreaterThan ProvenanceStoreConfig.defaults.MaxRecords 0 ""
              Expect.isGreaterThan ProvenanceStoreConfig.defaults.EvictionBatchSize 0 ""
          } ]
```

Add it to `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="ProvenanceQueryTests.fs" />
```

Add `InternalsVisibleTo` is already declared (Task 1's `.fsproj`) — `toSparqlQuery` will be `internal`, visible to this test project the same way `Frank.Rdf`'s `resolveIri` is.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

Expected: build failure — `ProvenanceQuery`/`toSparqlQuery`/`ProvenanceStoreConfig` are not defined.

- [ ] **Step 3: Write `ProvenanceStore.fsi`**

```fsharp
namespace Frank.Provenance

open VDS.RDF
open VDS.RDF.Query

/// The closed, public vocabulary of query shapes this package recognizes as provenance-meaningful.
/// This is the ONLY way a caller queries a store -- there is no public API accepting a raw SparqlQuery
/// or query string. Adding a new provenance-meaningful query shape means adding a case here, not
/// widening the surface to open query text.
[<RequireQualifiedAccess>]
type ProvenanceQuery =
    | ByResource of resourceIri: string
    | ByAgent of agentIri: string
    | ByActivityId of activityIri: string

/// SPARQL SELECT/ASK return bindings; CONSTRUCT/DESCRIBE return a graph. A store's Query can produce
/// either, depending on the underlying SparqlQuery shape.
[<RequireQualifiedAccess>]
type SparqlQueryResult =
    | Bindings of SparqlResultSet
    | Graph of IGraph

/// A provenance store: append records, query them via the closed ProvenanceQuery vocabulary.
type IProvenanceStore =
    abstract Append: record: ProvenanceRecord -> unit
    abstract Query: query: ProvenanceQuery -> SparqlQueryResult

/// Bounds an in-memory store.
type ProvenanceStoreConfig =
    { MaxRecords: int
      EvictionBatchSize: int }

module ProvenanceStoreConfig =
    val defaults: ProvenanceStoreConfig

/// Translates a ProvenanceQuery into a pre-built, parameterized SparqlQuery. Internal: SPARQL is the
/// implementation mechanism, never part of the public surface.
internal val toSparqlQuery: query: ProvenanceQuery -> SparqlQuery
```

- [ ] **Step 4: Write `ProvenanceStore.fs`**

```fsharp
namespace Frank.Provenance

open System
open VDS.RDF
open VDS.RDF.Query

[<RequireQualifiedAccess>]
type ProvenanceQuery =
    | ByResource of resourceIri: string
    | ByAgent of agentIri: string
    | ByActivityId of activityIri: string

[<RequireQualifiedAccess>]
type SparqlQueryResult =
    | Bindings of SparqlResultSet
    | Graph of IGraph

type IProvenanceStore =
    abstract Append: record: ProvenanceRecord -> unit
    abstract Query: query: ProvenanceQuery -> SparqlQueryResult

type ProvenanceStoreConfig =
    { MaxRecords: int
      EvictionBatchSize: int }

module ProvenanceStoreConfig =
    let defaults = { MaxRecords = 1000; EvictionBatchSize = 100 }

let internal toSparqlQuery (query: ProvenanceQuery) : SparqlQuery =
    let parser = SparqlQueryParser()

    let render (commandText: string) (paramName: string) (iri: string) : SparqlQuery =
        let qs = SparqlParameterizedString(commandText)
        qs.SetUri(paramName, Uri iri)
        parser.ParseFromString(qs.ToString())

    match query with
    | ProvenanceQuery.ByResource resourceIri ->
        render
            """
            CONSTRUCT { @resource ?rp ?ro . ?activity ?ap ?ao . }
            WHERE {
                @resource ?rp ?ro .
                OPTIONAL {
                    @resource <http://www.w3.org/ns/prov#wasGeneratedBy> ?activity .
                    ?activity ?ap ?ao .
                }
            }
            """
            "resource"
            resourceIri

    | ProvenanceQuery.ByAgent agentIri ->
        render
            """
            CONSTRUCT { ?activity ?ap ?ao . }
            WHERE {
                ?activity <http://www.w3.org/ns/prov#wasAssociatedWith> @agent .
                ?activity ?ap ?ao .
            }
            """
            "agent"
            agentIri

    | ProvenanceQuery.ByActivityId activityIri -> render "DESCRIBE @activity" "activity" activityIri
```

Add `ProvenanceStore.fsi`/`ProvenanceStore.fs` to `src/Frank.Provenance/Frank.Provenance.fsproj`, after the `ProvenanceRecord` entries:

```xml
    <Compile Include="ProvenanceRecord.fsi" />
    <Compile Include="ProvenanceRecord.fs" />
    <Compile Include="ProvenanceStore.fsi" />
    <Compile Include="ProvenanceStore.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

Expected: all tests pass. If `SparqlParameterizedString`'s constructor or `SetUri` don't match exactly, the compiler error will name the actual mismatch — fix against that rather than the notes above (verified against `dotnetrdf/dotnetrdf`'s source on GitHub, not a local build of this exact version).

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Provenance test/Frank.Provenance.Tests
git commit -m "feat(provenance): closed ProvenanceQuery vocabulary, IProvenanceStore contract, toSparqlQuery"
```

---

### Task 5: `MailboxProcessorProvenanceStore`

**Files:**
- Modify: `src/Frank.Provenance/Frank.Provenance.fsproj`
- Create: `src/Frank.Provenance/MailboxProcessorProvenanceStore.fsi`, `src/Frank.Provenance/MailboxProcessorProvenanceStore.fs`
- Modify: `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`
- Create: `test/Frank.Provenance.Tests/MailboxProcessorProvenanceStoreTests.fs`

**Interfaces:**
- Consumes: `ProvenanceRecord`, `ProvenanceRecord.toDoc` (Task 3); `ProvenanceQuery`, `SparqlQueryResult`, `IProvenanceStore`, `ProvenanceStoreConfig`, `toSparqlQuery` (Task 4); `Frank.Rdf.Node`, `Frank.Rdf.Doc.toGraph` (`Frank.Rdf`).
- Produces:
  - `type MailboxProcessorProvenanceStore = new: config: ProvenanceStoreConfig * logger: Microsoft.Extensions.Logging.ILogger -> MailboxProcessorProvenanceStore` implementing `IProvenanceStore` and `System.IDisposable`.

**Background you need:**

dotNetRDF's `TripleStore` implements `IInMemoryQueryableStore`. Adding a graph as a *named* graph is: set `graph.BaseUri <- someUri` before calling `store.Add(graph, true)` (the `true` is `mergeIfExists`; harmless here since each graph's `BaseUri` is unique per record) — "if you insert a graph that doesn't have a Base URI then it is treated as the default unnamed graph of the store" (dotNetRDF docs), which is exactly why every graph here needs one set. `store.Remove(graphUri: Uri)` removes a named graph; removing one that doesn't exist is a no-op, not an error. For querying across every named graph without requiring callers to write a `GRAPH` clause, `InMemoryDataset(store, true)` — the `IInMemoryQueryableStore, bool` constructor overload, `true` meaning "union of all graphs is the default graph" (verified against dotNetRDF's docs and `InMemoryDataset.cs`) — then run the query the same way `test/Frank.Rdf.Tests/QueryVerificationTests.fs` already does: `LeviathanQueryProcessor(dataset).ProcessQuery(query)`, which returns `obj` — pattern-match on `SparqlResultSet` vs. `IGraph` to build a `SparqlQueryResult`.

Each record needs a graph name. `record.Activity` is the natural choice (unique per record, by construction): if it's `Node.Iri s`, use `Uri s` directly; if it's `Node.Blank id` (an app minted a blank node instead of an IRI for the activity — legal per `Frank.Rdf`'s `Node`, if unusual for an Activity specifically), synthesize `Uri (sprintf "urn:frank:provenance:%s" id)` so eviction still has something concrete to remove.

`MailboxProcessor<'Msg>` serializes every message through one loop, so `Append` and `Query` can never race with each other or with eviction — this is the concurrency story the design doc's *Store* section promises, not something this task adds on top.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Provenance.Tests/MailboxProcessorProvenanceStoreTests.fs`:

```fsharp
module Frank.Provenance.Tests.MailboxProcessorProvenanceStoreTests

open System
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open Frank.Rdf
open Frank.Provenance

let private record (activityIri: string) (resourceIri: string) (agentIri: string) : ProvenanceRecord =
    let now = DateTimeOffset.UtcNow

    { Activity = Node.Iri activityIri
      Resource = Node.Iri resourceIri
      Agent = Node.Iri agentIri
      StartedAt = now
      EndedAt = now.AddSeconds(1.0)
      ActivityType = None
      Properties = [] }

let private newStore (config: ProvenanceStoreConfig) : IProvenanceStore =
    new MailboxProcessorProvenanceStore(config, NullLogger.Instance) :> IProvenanceStore

[<Tests>]
let tests =
    testList
        "MailboxProcessorProvenanceStore"
        [ test "ByResource finds an activity generated-by the given resource" {
              let store = newStore ProvenanceStoreConfig.defaults

              store.Append(
                  record "https://example.org/activities/1" "https://example.org/games/1" "https://example.org/users/42"
              )

              match store.Query(ProvenanceQuery.ByResource "https://example.org/games/1") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 "Some triples came back"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph, ByResource is a CONSTRUCT query"
          }

          test "ByResource for an unknown resource returns an empty graph, not an error" {
              let store = newStore ProvenanceStoreConfig.defaults

              match store.Query(ProvenanceQuery.ByResource "https://example.org/games/does-not-exist") with
              | SparqlQueryResult.Graph g -> Expect.equal g.Triples.Count 0 "Nothing recorded for this resource"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "ByResource for one resource never returns another resource's activity data" {
              let store = newStore ProvenanceStoreConfig.defaults

              store.Append(
                  record
                      "https://example.org/activities/x1"
                      "https://example.org/games/x"
                      "https://example.org/users/x"
              )

              store.Append(
                  record
                      "https://example.org/activities/y1"
                      "https://example.org/games/y"
                      "https://example.org/users/y"
              )

              match store.Query(ProvenanceQuery.ByResource "https://example.org/games/x") with
              | SparqlQueryResult.Graph g ->
                  let activityYNode = g.CreateUriNode(Uri "https://example.org/activities/y1")
                  Expect.equal (g.GetTriplesWithSubject(activityYNode) |> Seq.length) 0 "No cross-contamination from games/y"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "ByAgent for one agent never returns another agent's activity data" {
              let store = newStore ProvenanceStoreConfig.defaults

              store.Append(
                  record
                      "https://example.org/activities/x2"
                      "https://example.org/games/x2"
                      "https://example.org/users/x2"
              )

              store.Append(
                  record
                      "https://example.org/activities/y2"
                      "https://example.org/games/y2"
                      "https://example.org/users/y2"
              )

              match store.Query(ProvenanceQuery.ByAgent "https://example.org/users/x2") with
              | SparqlQueryResult.Graph g ->
                  let activityYNode = g.CreateUriNode(Uri "https://example.org/activities/y2")
                  Expect.equal (g.GetTriplesWithSubject(activityYNode) |> Seq.length) 0 "No cross-contamination from users/y2"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "ByAgent finds an activity associated with the given agent" {
              let store = newStore ProvenanceStoreConfig.defaults

              store.Append(
                  record "https://example.org/activities/2" "https://example.org/games/2" "https://example.org/users/7"
              )

              match store.Query(ProvenanceQuery.ByAgent "https://example.org/users/7") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 ""
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "ByActivityId finds the named activity by its own id" {
              let store = newStore ProvenanceStoreConfig.defaults

              store.Append(
                  record "https://example.org/activities/3" "https://example.org/games/3" "https://example.org/users/9"
              )

              match store.Query(ProvenanceQuery.ByActivityId "https://example.org/activities/3") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 ""
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "eviction removes the oldest records once MaxRecords is exceeded" {
              let config = { MaxRecords = 2; EvictionBatchSize = 1 }
              let store = newStore config

              store.Append(
                  record "https://example.org/activities/a" "https://example.org/games/a" "https://example.org/users/a"
              )

              store.Append(
                  record "https://example.org/activities/b" "https://example.org/games/b" "https://example.org/users/b"
              )

              store.Append(
                  record "https://example.org/activities/c" "https://example.org/games/c" "https://example.org/users/c"
              )

              match store.Query(ProvenanceQuery.ByActivityId "https://example.org/activities/a") with
              | SparqlQueryResult.Graph g -> Expect.equal g.Triples.Count 0 "Oldest record evicted"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"

              match store.Query(ProvenanceQuery.ByActivityId "https://example.org/activities/c") with
              | SparqlQueryResult.Graph g -> Expect.isGreaterThan g.Triples.Count 0 "Newest record still present"
              | SparqlQueryResult.Bindings _ -> failwith "Expected a graph"
          }

          test "Append/Query from multiple threads never throws (mailbox serializes access)" {
              let store = newStore ProvenanceStoreConfig.defaults

              let work =
                  [ 0..19 ]
                  |> List.map (fun i ->
                      System.Threading.Tasks.Task.Run(fun () ->
                          store.Append(
                              record
                                  (sprintf "https://example.org/activities/thread-%d" i)
                                  "https://example.org/games/concurrent"
                                  "https://example.org/users/concurrent"
                          )

                          store.Query(ProvenanceQuery.ByResource "https://example.org/games/concurrent") |> ignore))
                  |> Array.ofList

              System.Threading.Tasks.Task.WaitAll(work)
          } ]
```

Add it to `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="MailboxProcessorProvenanceStoreTests.fs" />
```

Add a `PackageReference` the test project needs for `NullLogger`:

```xml
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="9.*" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

Expected: build failure — `MailboxProcessorProvenanceStore` is not defined.

- [ ] **Step 3: Write `MailboxProcessorProvenanceStore.fsi`**

```fsharp
namespace Frank.Provenance

open System
open Microsoft.Extensions.Logging

/// The v1, in-memory IProvenanceStore: one dotNetRDF TripleStore holding one named graph per
/// appended record, queried via SPARQL over the whole store's union graph, with bounded eviction
/// of the oldest records once ProvenanceStoreConfig.MaxRecords is exceeded.
[<Sealed>]
type MailboxProcessorProvenanceStore =
    new: config: ProvenanceStoreConfig * logger: ILogger -> MailboxProcessorProvenanceStore

    interface IProvenanceStore
    interface IDisposable
```

- [ ] **Step 4: Write `MailboxProcessorProvenanceStore.fs`**

```fsharp
namespace Frank.Provenance

open System
open Microsoft.Extensions.Logging
open VDS.RDF
open VDS.RDF.Query
open VDS.RDF.Query.Datasets
open Frank.Rdf

type private StoreMessage =
    | Append of ProvenanceRecord
    | Query of ProvenanceQuery * AsyncReplyChannel<SparqlQueryResult>

[<Sealed>]
type MailboxProcessorProvenanceStore(config: ProvenanceStoreConfig, logger: ILogger) =
    let store = TripleStore()

    let graphNameFor (record: ProvenanceRecord) : Uri =
        match record.Activity with
        | Node.Iri s -> Uri s
        | Node.Blank id -> Uri(sprintf "urn:frank:provenance:%s" id)

    let runQuery (query: ProvenanceQuery) : SparqlQueryResult =
        let sparqlQuery = toSparqlQuery query
        let dataset = InMemoryDataset(store, true)
        let processor = LeviathanQueryProcessor(dataset)

        match processor.ProcessQuery(sparqlQuery) with
        | :? SparqlResultSet as rs -> SparqlQueryResult.Bindings rs
        | :? IGraph as g -> SparqlQueryResult.Graph g
        | other -> failwithf "Frank.Provenance: unexpected SPARQL result shape %A" other

    let agent =
        MailboxProcessor<StoreMessage>.Start(fun inbox ->
            let rec loop (entries: (Uri * ProvenanceRecord) list) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Append record ->
                        let graphName = graphNameFor record
                        let graph = record |> ProvenanceRecord.toDoc |> Doc.toGraph
                        graph.BaseUri <- graphName
                        store.Add(graph, true) |> ignore
                        logger.LogDebug("Appended provenance record for activity {GraphName}", graphName)

                        let updated = entries @ [ graphName, record ]

                        let retained =
                            if updated.Length > config.MaxRecords then
                                let evictCount = min config.EvictionBatchSize updated.Length

                                for evictedUri, _ in updated |> List.truncate evictCount do
                                    store.Remove(evictedUri)
                                    logger.LogDebug("Evicted provenance record {GraphName}", evictedUri)

                                updated |> List.skip evictCount
                            else
                                updated

                        return! loop retained

                    | Query(query, reply) ->
                        reply.Reply(runQuery query)
                        return! loop entries
                }

            loop [])

    interface IProvenanceStore with
        member _.Append(record: ProvenanceRecord) = agent.Post(Append record)
        member _.Query(query: ProvenanceQuery) = agent.PostAndReply(fun reply -> Query(query, reply))

    interface IDisposable with
        member _.Dispose() = (agent :> IDisposable).Dispose()
```

Add `MailboxProcessorProvenanceStore.fsi`/`.fs` to `src/Frank.Provenance/Frank.Provenance.fsproj`, after the `ProvenanceStore` entries:

```xml
    <Compile Include="ProvenanceStore.fsi" />
    <Compile Include="ProvenanceStore.fs" />
    <Compile Include="MailboxProcessorProvenanceStore.fsi" />
    <Compile Include="MailboxProcessorProvenanceStore.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

Expected: all tests pass. If `TripleStore.Add`'s second parameter, `store.Remove`, or `InMemoryDataset`'s constructor don't match exactly, the compiler error will name the actual mismatch — fix against that (verified against dotNetRDF's public docs/GitHub source, not a local build of this exact version, same caveat as Task 4).

- [ ] **Step 6: Build across every target framework**

```bash
dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net8.0
dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net9.0
dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net10.0
```

Expected: all three succeed. This is the check `CLAUDE.md` calls out explicitly — signature mismatches between `.fsi`/`.fs` files can pass on one TFM and fail on another.

- [ ] **Step 7: Commit**

```bash
git add src/Frank.Provenance test/Frank.Provenance.Tests
git commit -m "feat(provenance): MailboxProcessorProvenanceStore -- TripleStore-backed, bounded, SPARQL-queried"
```

---

## After this plan

`Frank.Provenance` is a working, testable package: build a `ProvenanceRecord`, `Append` it to a `MailboxProcessorProvenanceStore`, `Query` it back via `ProvenanceQuery`. Nothing here depends on `Frank` or ASP.NET Core. The follow-on plan (see *Out of scope for this plan*) picks up from `IProvenanceStore` to add: the `HttpContext`-based `Prov.record`/`Prov.enrich` convenience API, the `ActivityTypeResolver` seam and auto-capture middleware, and the two HTTP exposures — all against a package that already works and is already tested.
