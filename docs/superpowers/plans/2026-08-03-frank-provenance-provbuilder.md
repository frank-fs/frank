# Frank.Provenance ProvBuilder CE Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `ProvBuilder`, a computation-expression sugar layer over `Frank.Provenance`'s existing `Prov` module functions, and demonstrate it in the `Frank.Provenance.Sample` app.

**Architecture:** One new file pair (`ProvBuilder.fsi`/`.fs`) in `src/Frank.Provenance/`, mirroring `Frank.Rdf`'s `DescribeBuilder`/`describe` and `Frank.Alps`'s `DescriptorBuilder`/`descriptor` exactly: a `[<Sealed>]` CE builder over one accumulator type (`Description`), no `Combine`/`Delay`, `Run` returns a plain value. Three entry points (`activity`/`entity`/`agent`) each seed the builder via the matching `Prov` constructor; seven `CustomOperation`s each forward 1:1 to a `Prov` modifier function. `ProvenanceRecord.toDoc` is not touched. The sample gets one new endpoint, `GET /provenance/lineage`, that hand-authors a `wasDerivedFrom` relationship via the CE — a PROV-O shape `ProvenanceRecord`/`IProvenanceStore.Append` cannot express today.

**Tech Stack:** F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching `Frank.Provenance`'s existing `Frank.Provenance.fsproj`), Expecto (existing test framework in `Frank.Provenance.Tests`), ASP.NET Core (sample only, via `Frank`/`Frank.Rdf`/`Frank.Provenance` project references already in `Frank.Provenance.Sample.fsproj`).

## Global Constraints

- Every `.fs` module under `src/Frank.*/` gets a matching `.fsi`, placed directly above it in `<Compile>` order (repo-wide `CLAUDE.md` rule).
- Verify the build across every targeted TFM (`net8.0`, `net9.0`, `net10.0`), not just `net10.0` — signature mismatches only surface at compile time per-TFM (repo-wide `CLAUDE.md` rule).
- No new authoring logic: every `ProvBuilder` member is a direct call to an existing `Prov` function. No kind-switching custom operation (see design doc's *Non-goals*).
- `ProvenanceRecord.toDoc` (`src/Frank.Provenance/ProvenanceRecord.fs`) stays exactly as-is — do not refactor it to use `ProvBuilder`.
- Design doc: `docs/superpowers/specs/2026-08-03-frank-provenance-provbuilder-design.md`.

---

### Task 1: `ProvBuilder` CE

**Files:**
- Create: `src/Frank.Provenance/ProvBuilder.fsi`
- Create: `src/Frank.Provenance/ProvBuilder.fs`
- Modify: `src/Frank.Provenance/Frank.Provenance.fsproj` (register the new pair)
- Test: `test/Frank.Provenance.Tests/ProvBuilderTests.fs`
- Modify: `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj` (register the test file)

**Interfaces:**
- Consumes: `Frank.Rdf.Node`, `Frank.Rdf.Value`, `Frank.Rdf.Literal`, `Frank.Rdf.Description = { Subject: Node; Statements: (string * Value) list }`, `Frank.Rdf.RdfTypeIri: string` (all in `src/Frank.Rdf/RdfTypes.fsi`/`Rdf.fsi`, already referenced by this project). `Frank.Provenance.Prov.activity/entity/agent: id: Node -> Description` and `Frank.Provenance.Prov.wasGeneratedBy/wasAssociatedWith/used/startedAtTime/endedAtTime/wasDerivedFrom/specializationOf` (`src/Frank.Provenance/Prov.fsi`, already in this project).
- Produces: `Frank.Provenance.ProvBuilder` (sealed type) and `Frank.Provenance.activity/entity/agent : id: Node -> ProvBuilder`, all `[<AutoOpen>]` in namespace `Frank.Provenance`. Task 2 (the sample) consumes `activity`/`entity`/`agent` and the CE's `wasDerivedFrom` operation directly by name — no other task depends on this one.

- [ ] **Step 1: Write the failing test file**

Create `test/Frank.Provenance.Tests/ProvBuilderTests.fs`:

```fsharp
module Frank.Provenance.Tests.ProvBuilderTests

open System
open Expecto
open Frank.Rdf
open Frank.Provenance

[<Tests>]
let tests =
    testList
        "ProvBuilder"
        [ test "activity seeds a Description typed prov:Activity" {
              let d = activity (Node.Iri "https://example.org/a1") { () }

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Activity") ]
                  ""
          }

          test "entity seeds a Description typed prov:Entity" {
              let d = entity (Node.Iri "https://example.org/e1") { () }

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Entity") ]
                  ""
          }

          test "agent seeds a Description typed prov:Agent" {
              let d = agent (Node.Iri "https://example.org/ag1") { () }

              Expect.equal
                  d.Statements
                  [ RdfTypeIri, Value.Node(Node.Iri "http://www.w3.org/ns/prov#Agent") ]
                  ""
          }

          test "wasGeneratedBy adds a prov:wasGeneratedBy statement pointing at the given activity" {
              let d =
                  entity (Node.Iri "https://example.org/e1") { wasGeneratedBy (Node.Iri "https://example.org/a1") }

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasGeneratedBy", Value.Node(Node.Iri "https://example.org/a1"))
                  "Second statement, after the rdf:type from entity"
          }

          test "wasAssociatedWith adds a prov:wasAssociatedWith statement pointing at the given agent" {
              let d =
                  activity (Node.Iri "https://example.org/a1") {
                      wasAssociatedWith (Node.Iri "https://example.org/ag1")
                  }

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasAssociatedWith", Value.Node(Node.Iri "https://example.org/ag1"))
                  ""
          }

          test "used adds a prov:used statement pointing at the given entity" {
              let d =
                  activity (Node.Iri "https://example.org/a1") { used (Node.Iri "https://example.org/e1") }

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#used", Value.Node(Node.Iri "https://example.org/e1"))
                  ""
          }

          test "startedAtTime and endedAtTime add DateTimeOffset-literal statements" {
              let t0 = DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)
              let t1 = DateTimeOffset(2026, 8, 3, 12, 0, 1, TimeSpan.Zero)

              let d =
                  activity (Node.Iri "https://example.org/a1") {
                      startedAtTime t0
                      endedAtTime t1
                  }

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
                  entity (Node.Iri "https://example.org/e2") {
                      wasDerivedFrom (Node.Iri "https://example.org/e1")
                      specializationOf (Node.Iri "https://example.org/e1")
                  }

              Expect.equal
                  d.Statements.[1]
                  ("http://www.w3.org/ns/prov#wasDerivedFrom", Value.Node(Node.Iri "https://example.org/e1"))
                  ""

              Expect.equal
                  d.Statements.[2]
                  ("http://www.w3.org/ns/prov#specializationOf", Value.Node(Node.Iri "https://example.org/e1"))
                  ""
          }

          test "CE and |> combinators produce identical Descriptions" {
              let t0 = DateTimeOffset(2026, 8, 3, 12, 0, 0, TimeSpan.Zero)
              let t1 = DateTimeOffset(2026, 8, 3, 12, 0, 1, TimeSpan.Zero)
              let a = Node.Iri "https://example.org/a1"
              let ag = Node.Iri "https://example.org/ag1"
              let e = Node.Iri "https://example.org/e1"

              let viaCe =
                  activity a {
                      wasAssociatedWith ag
                      used e
                      startedAtTime t0
                      endedAtTime t1
                  }

              let viaPipe =
                  Prov.activity a
                  |> Prov.wasAssociatedWith ag
                  |> Prov.used e
                  |> Prov.startedAtTime t0
                  |> Prov.endedAtTime t1

              Expect.equal viaCe viaPipe "CE block produces the same Description as the equivalent |> chain"
          } ]
```

- [ ] **Step 2: Register the test file in the test project**

In `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`, insert a new line directly after `<Compile Include="ProvTests.fs" />` and before `<Compile Include="ProvenanceRecordTests.fs" />`:

```xml
    <Compile Include="ProvBuilderTests.fs" />
```

- [ ] **Step 3: Run the test project and confirm it fails to build**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`
Expected: Build error — `activity`/`entity`/`agent` (and `wasGeneratedBy`/`wasAssociatedWith`/`used`/`startedAtTime`/`endedAtTime`/`wasDerivedFrom`/`specializationOf` as CE operations) are not defined in `Frank.Provenance`. This confirms the test is exercising code that doesn't exist yet.

- [ ] **Step 4: Write the signature file**

Create `src/Frank.Provenance/ProvBuilder.fsi`:

```fsharp
namespace Frank.Provenance

open System
open Frank.Rdf

/// Builds a `Description` via computation expression, as an alternative to plain `|>` combinators
/// over `Prov`'s functions -- both produce identical `Description` values. Mirrors `Frank.Rdf`'s
/// `DescribeBuilder`/`describe` and `Frank.Alps`'s `DescriptorBuilder`/`descriptor`: one accumulator,
/// no `Combine`/`Delay`, `Run` returns a plain value.
[<AutoOpen>]
module ProvBuilderModule =
    [<Sealed>]
    type ProvBuilder =
        new: initial: Description -> ProvBuilder
        member Yield: 'a -> Description
        member Zero: unit -> Description
        member Run: d: Description -> Description

        [<CustomOperation("wasGeneratedBy")>]
        member WasGeneratedBy: d: Description * activity: Node -> Description

        [<CustomOperation("wasAssociatedWith")>]
        member WasAssociatedWith: d: Description * agent: Node -> Description

        [<CustomOperation("used")>]
        member Used: d: Description * entity: Node -> Description

        [<CustomOperation("startedAtTime")>]
        member StartedAtTime: d: Description * t: DateTimeOffset -> Description

        [<CustomOperation("endedAtTime")>]
        member EndedAtTime: d: Description * t: DateTimeOffset -> Description

        [<CustomOperation("wasDerivedFrom")>]
        member WasDerivedFrom: d: Description * entity: Node -> Description

        [<CustomOperation("specializationOf")>]
        member SpecializationOf: d: Description * entity: Node -> Description

    /// Enters an `activity id { }` block: `activity a { wasAssociatedWith ag; startedAtTime t0; endedAtTime t1 }`.
    val activity: id: Node -> ProvBuilder
    /// Enters an `entity id { }` block: `entity e { wasGeneratedBy a }`.
    val entity: id: Node -> ProvBuilder
    /// Enters an `agent id { }` block: `agent ag { }`.
    val agent: id: Node -> ProvBuilder
```

- [ ] **Step 5: Write the implementation file**

Create `src/Frank.Provenance/ProvBuilder.fs`:

```fsharp
namespace Frank.Provenance

open System
open Frank.Rdf

[<AutoOpen>]
module ProvBuilderModule =
    [<Sealed>]
    type ProvBuilder(initial: Description) =
        member _.Yield(_) : Description = initial
        member _.Zero() : Description = initial
        member _.Run(d: Description) : Description = d

        [<CustomOperation("wasGeneratedBy")>]
        member _.WasGeneratedBy(d: Description, activity: Node) : Description = d |> Prov.wasGeneratedBy activity

        [<CustomOperation("wasAssociatedWith")>]
        member _.WasAssociatedWith(d: Description, agent: Node) : Description = d |> Prov.wasAssociatedWith agent

        [<CustomOperation("used")>]
        member _.Used(d: Description, entity: Node) : Description = d |> Prov.used entity

        [<CustomOperation("startedAtTime")>]
        member _.StartedAtTime(d: Description, t: DateTimeOffset) : Description = d |> Prov.startedAtTime t

        [<CustomOperation("endedAtTime")>]
        member _.EndedAtTime(d: Description, t: DateTimeOffset) : Description = d |> Prov.endedAtTime t

        [<CustomOperation("wasDerivedFrom")>]
        member _.WasDerivedFrom(d: Description, entity: Node) : Description = d |> Prov.wasDerivedFrom entity

        [<CustomOperation("specializationOf")>]
        member _.SpecializationOf(d: Description, entity: Node) : Description = d |> Prov.specializationOf entity

    let activity (id: Node) = ProvBuilder(Prov.activity id)
    let entity (id: Node) = ProvBuilder(Prov.entity id)
    let agent (id: Node) = ProvBuilder(Prov.agent id)
```

- [ ] **Step 6: Register the new pair in the package project**

In `src/Frank.Provenance/Frank.Provenance.fsproj`, insert two new lines directly after `<Compile Include="Prov.fs" />` and before `<Compile Include="ProvenanceRecord.fsi" />`:

```xml
    <Compile Include="ProvBuilder.fsi" />
    <Compile Include="ProvBuilder.fs" />
```

- [ ] **Step 7: Run the test project and confirm it passes**

Run: `dotnet test test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`
Expected: All tests pass, including the 9 new `ProvBuilder` tests and every pre-existing test in this project (no regressions).

- [ ] **Step 8: Verify the build across every targeted TFM**

Run each of:
```bash
dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net8.0
dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net9.0
dotnet build src/Frank.Provenance/Frank.Provenance.fsproj -f net10.0
```
Expected: All three succeed with no errors or warnings. This catches `.fsi`/`.fs` signature mismatches (e.g. an inferred return type differing by TFM) that a single-TFM build can miss — see `CLAUDE.md`'s Code Style section.

- [ ] **Step 9: Commit**

```bash
git add src/Frank.Provenance/ProvBuilder.fsi src/Frank.Provenance/ProvBuilder.fs src/Frank.Provenance/Frank.Provenance.fsproj test/Frank.Provenance.Tests/ProvBuilderTests.fs test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
git commit -m "feat(provenance): ProvBuilder CE -- sugar over Prov.fs, mirrors DescriptorBuilder"
```

---

### Task 2: Demonstrate `ProvBuilder` in the sample

**Files:**
- Modify: `sample/Frank.Provenance.Sample/Program.fs`
- Modify: `sample/Frank.Provenance.Sample/README.md`

**Interfaces:**
- Consumes: `Frank.Provenance.entity: id: Node -> ProvBuilder` and the CE's `wasDerivedFrom` operation (Task 1). `Frank.Rdf.rdf { }`/`about`, `Frank.Rdf.Doc`, `Frank.Rdf.Doc.toJsonLd: doc: Doc -> string`, `Frank.Rdf.Node.Iri` (all already used elsewhere in `Program.fs` or available via the existing `open Frank.Rdf`). `Frank.Builder.resource` (already used in this file for `gameResource`/`provenanceResource`).
- Produces: nothing consumed by another task — this is the plan's last task.

- [ ] **Step 1: Add the lineage document, handler, and resource to `Program.fs`**

In `sample/Frank.Provenance.Sample/Program.fs`, insert the following directly after the existing `provenanceResource` definition (after the line `let private provenanceResource = resource "/provenance" { get getProvenance }`) and before the `[<EntryPoint>]` block:

```fsharp
// ProvBuilder demo: IProvenanceStore.Append only accepts a ProvenanceRecord, and
// ProvenanceRecord.toDoc only ever emits wasGeneratedBy/wasAssociatedWith/startedAtTime/endedAtTime
// -- there is no way to record a wasDerivedFrom relationship through the store. This endpoint
// hand-authors that relationship directly via ProvBuilder, served independently of the store, to
// show the one PROV-O shape the record model can't produce: Connect Four (games/2) wasDerivedFrom
// Tic-tac-toe (games/1).
let private catalogLineage (baseUri: string) : Doc =
    rdf {
        about (entity (Node.Iri $"{baseUri}/games/2") { wasDerivedFrom (Node.Iri $"{baseUri}/games/1") })
    }

let private getCatalogLineage =
    fun (ctx: HttpContext) ->
        task {
            let baseUri = $"{ctx.Request.Scheme}://{ctx.Request.Host}"
            ctx.Response.ContentType <- "application/ld+json"
            do! ctx.Response.WriteAsync(catalogLineage baseUri |> Doc.toJsonLd)
        }

let private lineageResource = resource "/provenance/lineage" { get getCatalogLineage }
```

Then update the `webHost` block so it also registers the new resource:

```fsharp
[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        resource gameResource
        resource provenanceResource
        resource lineageResource
    }

    0
```

- [ ] **Step 2: Update the sample README**

In `sample/Frank.Provenance.Sample/README.md`, add a new paragraph directly after the existing paragraph that ends `...serializes the resulting dotNetRDF graph as JSON-LD.` (the paragraph describing `GET /provenance?resource={iri}`):

```markdown
`GET /provenance/lineage` returns a hand-authored PROV-O relationship that the record model
above can't express: `IProvenanceStore.Append` only accepts a `ProvenanceRecord`, and
`ProvenanceRecord.toDoc` only ever emits `wasGeneratedBy`/`wasAssociatedWith`/`startedAtTime`/
`endedAtTime`. This endpoint builds a `Description` directly via `Frank.Provenance`'s `ProvBuilder`
CE (`entity ... { wasDerivedFrom ... }`) asserting that Connect Four was derived from Tic-tac-toe,
and serves it as JSON-LD independent of the store.
```

Then add a new example to the "Try it" section, directly after the existing block that checks `Content-Type: application/ld+json` (after the line `# Content-Type: application/ld+json`) and before the "A resource that was never viewed..." block:

```markdown
# GET /provenance/lineage is unrelated to the store above -- it's a hand-authored relationship,
# served the same way on every request, not something that accumulates from Append calls.
curl -s http://localhost:5000/provenance/lineage | jq
# [{"@id":"http://localhost:5000/games/2","@type":["http://www.w3.org/ns/prov#Entity"],
#   "http://www.w3.org/ns/prov#wasDerivedFrom":[{"@id":"http://localhost:5000/games/1"}]}]
```

- [ ] **Step 3: Build the sample for its target framework**

Run: `dotnet build sample/Frank.Provenance.Sample/Frank.Provenance.Sample.fsproj -f net10.0`
Expected: Succeeds with no errors or warnings.

- [ ] **Step 4: Run the sample and verify the new endpoint over real HTTP**

Start the sample in the background:
```bash
dotnet run --project sample/Frank.Provenance.Sample/ &
```
Wait for `Now listening on: http://localhost:5000` in its output, then run:
```bash
curl -s http://localhost:5000/provenance/lineage
```
Expected: JSON-LD (a JSON array). Verify it contains all three of the following substrings:
- `http://localhost:5000/games/2`
- `http://www.w3.org/ns/prov#wasDerivedFrom`
- `http://localhost:5000/games/1`

Then verify content negotiation is honest:
```bash
curl -i -s http://localhost:5000/provenance/lineage | head -5
```
Expected: `Content-Type: application/ld+json` in the response headers.

Stop the sample process (`kill %1` or equivalent) once both checks pass.

- [ ] **Step 5: Commit**

```bash
git add sample/Frank.Provenance.Sample/Program.fs sample/Frank.Provenance.Sample/README.md
git commit -m "feat(provenance-sample): demonstrate ProvBuilder via GET /provenance/lineage"
```
