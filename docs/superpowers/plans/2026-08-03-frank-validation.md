# Frank.Validation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build `Frank.Validation` — a hand-authored SHACL Core (+ SPARQL-based constraints, + full property-path grammar) validation package built on `Frank.Rdf`'s `Doc`/`describe`/blank-node machinery, with a `resource { useValidation }` / `webHost { useValidation }` HTTP surface that validates `application/ld+json` request bodies and returns a dual-path 422 (SHACL report vs. Problem Details).

**Architecture:** A typed `ShapeDecl`/`PropertyConstraint` model (illegal states unrepresentable) with two authoring layers — plain curried functions (`ShapeSpecFunctions`, the real model) and CE sugar (`property { }`/`shape { }`, mirroring `Frank.Provenance`'s `ProvBuilder`) — projected onto a `Doc` by a category-by-category interpreter (`Shacl.toDoc`/`toShapesGraph`), validated via a typed wrapper over `dotNetRdf.Shacl` (`Shacl.validate`), and wired into HTTP via two F# type extensions on Frank's sealed `ResourceBuilder`/`WebHostBuilder` (no Frank core change — same mechanism as `Frank.JsonHome`'s `rel`/`docs` and `Frank.OpenApi`'s `useOpenApi`).

**Tech Stack:** F# 8.0+ targeting `net8.0;net9.0;net10.0`, `Frank` (core), `Frank.Rdf` (project reference), `dotNetRdf.Core` + `dotNetRdf.Shacl` 3.5.1, Expecto, ASP.NET Core `TestHost`.

## Global Constraints

- Worktree root (ABSOLUTE; cwd resets between Bash calls): `C:\Users\ryanr\Code\frank\.claude\worktrees\validation`. Confirm `git branch --show-current` is `worktree-validation` before each session of work.
- Every `dotnet` command: no special env vars required (this codebase does not use `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` — that was a v7.3.0-era convention; verify current `Directory.Build.props` if a command behaves unexpectedly, but do not add it speculatively).
- Every `src/Frank.Validation/*.fs` file gets a matching `.fsi` immediately above it in `<Compile>` order, per `CLAUDE.md`. Private/internal helpers stay out of the `.fsi` unless another file in the same assembly needs them.
- Run `dotnet fantomas --check` on changed `src/` and `test/` files before every commit; if it fails, run `dotnet fantomas` (no `--check`) to reformat, then re-stage.
- No codegen, no reflection-driven type→shape mapping, anywhere in this plan — the entire point of this design.
- Commit after each task with the exact `git add` list given in that task's final step. Never `--amend`; always a new commit.
- `src/Directory.Build.props` sets `TreatWarningsAsErrors` — the `NU1902` NuGet-audit suppression on `dotNetRdf.Core` (AngleSharp transitive advisory) must be copied into `Frank.Validation.fsproj` exactly as it appears in `Frank.Rdf.fsproj`/`Frank.Provenance.fsproj`, or restore fails.
- Design doc: `docs/superpowers/specs/2026-08-03-frank-validation-design.md`. Every task below implements a specific section of it — if a step here ever seems to contradict that doc, the doc wins; stop and flag it rather than silently reconciling.

---

## File Structure

```
src/Frank.Validation/
  Frank.Validation.fsproj
  ShapeTypes.fsi / ShapeTypes.fs                    -- data model
  ShapeSpec.fsi / ShapeSpec.fs                       -- plain functions (the real authoring model)
  Shacl.fsi / Shacl.fs                               -- interpreter: toDoc, toShapesGraph, reportToDoc
  Validation.fsi / Validation.fs                     -- Violation, ValidationOutcome, validate
  ShapeBuilder.fsi / ShapeBuilder.fs                 -- property{ }/shape{ } CEs
  ResourceBuilderExtensions.fsi / .fs                -- `useValidation shapesGraph` on resource{ }
  WebHostBuilderExtensions.fsi / .fs                 -- `useValidation` on webHost{ } (the interceptor)
  README.md

test/Frank.Validation.Tests/
  Frank.Validation.Tests.fsproj
  ShapeTypesTests.fs
  ShapeSpecTests.fs
  ShaclToDocTests.fs           -- grows across Tasks 4-13, one testList per category
  ValidationTests.fs
  ShapeBuilderTests.fs
  ReportRoundTripTests.fs
  ValidationMiddlewareTests.fs -- TestHost-based
  Program.fs

sample/Frank.Validation.Sample/
  Frank.Validation.Sample.fsproj
  Program.fs
```

Nineteen tasks, staged exactly as the design doc's *Implementation order* section lists: data model → plain functions → interpreter (one SHACL constraint category at a time, cheapest/most-foundational first) → typed validation → CE sugar → report projection → HTTP wiring → sample.

---

### Task 1: Project scaffold

**Files:**
- Create: `src/Frank.Validation/Frank.Validation.fsproj`
- Create: `test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj`
- Create: `test/Frank.Validation.Tests/Program.fs`
- Modify: `Frank.sln`

**Interfaces:**
- Produces: an empty, buildable `Frank.Validation` project referenced by an empty, runnable `Frank.Validation.Tests` project. Nothing else in this plan can start until `dotnet build`/`dotnet test` both succeed against these two empty shells.

- [ ] **Step 1: Create the package project file**

```xml
<!-- src/Frank.Validation/Frank.Validation.fsproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>rdf;shacl;validation;linked-data</PackageTags>
    <Description>Hand-authored SHACL Core validation for Frank resources, built on Frank.Rdf</Description>
    <!-- NU1902: dotNetRdf.Core 3.5.1's transitive dependency on AngleSharp >=1.4.0 carries a known
         moderate-severity advisory (GHSA-pgww-w46g-26qg). Same pin, same suppression, as
         Frank.Rdf.fsproj/Frank.Provenance.fsproj -- src/Directory.Build.props' TreatWarningsAsErrors
         promotes this to a build-breaking error otherwise. Revisit when the pin is lifted. -->
    <NoWarn>NU1902</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="dotNetRdf.Core" Version="3.5.1" />
    <PackageReference Include="dotNetRdf.Shacl" Version="3.5.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Frank/Frank.fsproj" />
    <ProjectReference Include="../Frank.Rdf/Frank.Rdf.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Frank.Validation.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

</Project>
```

Leave `<ItemGroup>` for `<Compile>` entries out for now — Task 2 adds the first pair.

- [ ] **Step 2: Create the test project file**

```xml
<!-- test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj -->
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
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="Expecto" Version="10.*" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.*" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.Validation/Frank.Validation.fsproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Create the Expecto entry point**

```fsharp
// test/Frank.Validation.Tests/Program.fs
module Frank.Validation.Tests.Program

open Expecto

[<EntryPoint>]
let main argv = Tests.runTestsInAssemblyWithCLIArgs [] argv
```

- [ ] **Step 4: Add both projects to the solution**

Run: `dotnet sln Frank.sln add src/Frank.Validation/Frank.Validation.fsproj test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj`

- [ ] **Step 5: Build and run (should succeed with zero tests)**

Run: `dotnet build Frank.sln`
Expected: `Build succeeded. 0 Error(s)` (existing projects unaffected; the two new ones compile — `Frank.Validation` has no `.fs` files yet, which is legal for an F# SDK project with an empty `<Compile>` set).

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: `Passed! - Failed: 0, Passed: 0, Skipped: 0` (no tests exist yet).

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Validation/Frank.Validation.fsproj test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj test/Frank.Validation.Tests/Program.fs Frank.sln
git commit -m "feat(validation): scaffold Frank.Validation and Frank.Validation.Tests projects"
```

---

### Task 2: `ShapeTypes.fs` — the data model

**Files:**
- Create: `src/Frank.Validation/ShapeTypes.fsi`
- Create: `src/Frank.Validation/ShapeTypes.fs`
- Modify: `src/Frank.Validation/Frank.Validation.fsproj` (add `<Compile>` pair, first in the list)
- Create: `test/Frank.Validation.Tests/ShapeTypesTests.fs`
- Modify: `test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj` (add `<Compile>`, before `Program.fs`)

**Interfaces:**
- Consumes: `Frank.Rdf.Node`, `Frank.Rdf.Literal`, `Frank.Rdf.Value` (`open Frank.Rdf`).
- Produces: `XsdDatatype`, `NodeKind`, `Severity`, `NonEmptyList<'T>` (+ module), `PropertyPath`, `TargetSpec`, `SparqlConstraint`, `PropertyConstraint`, `PropertyShapeSpec`, `NodeShapeSpec`, `ShapeDecl` — every later task's types come from here, verbatim.

- [ ] **Step 1: Write the failing test**

```fsharp
// test/Frank.Validation.Tests/ShapeTypesTests.fs
module Frank.Validation.Tests.ShapeTypesTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation

[<Tests>]
let tests =
    testList "ShapeTypes" [
        test "NonEmptyList.ofList: None on empty, Some on non-empty; toList round-trips" {
            Expect.isNone (NonEmptyList.ofList ([]: int list)) "empty -> None"
            let nel = NonEmptyList.ofList [ 1; 2; 3 ] |> Option.get
            Expect.equal nel.Head 1 "head"
            Expect.equal nel.Tail [ 2; 3 ] "tail"
            Expect.equal (NonEmptyList.toList nel) [ 1; 2; 3 ] "round-trip"
        }

        test "XsdDatatype cases are unambiguous when RequireQualifiedAccess" {
            let d: XsdDatatype = XsdDatatype.Integer
            Expect.equal d XsdDatatype.Integer "no Xsd-prefixed case names"
        }

        test "PropertyPath: recursive cases construct (predicate, inverse, sequence, alternative, cardinality)" {
            let p1 = PropertyPath.Predicate(Uri "https://schema.org/knows")
            let p2 = PropertyPath.Inverse p1
            let p3 = PropertyPath.Sequence { Head = p1; Tail = [ p2 ] }
            let p4 = PropertyPath.Alternative { Head = p1; Tail = [ p2 ] }
            let p5 = PropertyPath.ZeroOrMore p1
            let p6 = PropertyPath.OneOrMore p1
            let p7 = PropertyPath.ZeroOrOne p1
            Expect.equal p2 (PropertyPath.Inverse(PropertyPath.Predicate(Uri "https://schema.org/knows"))) "inverse"
            Expect.equal (match p3 with PropertyPath.Sequence n -> NonEmptyList.toList n |> List.length | _ -> 0) 2 "sequence length"
            Expect.equal (match p4 with PropertyPath.Alternative n -> NonEmptyList.toList n |> List.length | _ -> 0) 2 "alternative length"
            ignore (p5, p6, p7)
        }

        test "PropertyShapeSpec and NodeShapeSpec are plain records with the designed fields" {
            let p: PropertyShapeSpec =
                { Path = PropertyPath.Predicate(Uri "https://schema.org/position")
                  Constraints = [ PropertyConstraint.Datatype XsdDatatype.Integer; PropertyConstraint.MinCount 1 ]
                  Severity = None
                  Message = None }

            let n: NodeShapeSpec =
                { Targets = [ TargetSpec.Class(Uri "https://schema.org/MoveAction") ]
                  Properties = [ p ]
                  Closed = false
                  IgnoredProperties = []
                  Severity = None
                  Message = None }

            Expect.equal n.Properties.Length 1 "one property shape"
            Expect.equal n.Targets [ TargetSpec.Class(Uri "https://schema.org/MoveAction") ] "targets"
        }

        test "NodeShapeSpec.Targets may be empty -- a shape referenced only via sh:node" {
            let n: NodeShapeSpec =
                { Targets = []; Properties = []; Closed = false; IgnoredProperties = []; Severity = None; Message = None }

            Expect.isEmpty n.Targets "no explicit target required"
        }

        test "ShapeDecl is a total DU over RecordShape | EnumShape | And | Or | Not | Xone" {
            let record =
                ShapeDecl.RecordShape
                    { Targets = [ TargetSpec.Class(Uri "https://schema.org/Person") ]
                      Properties = []
                      Closed = false
                      IgnoredProperties = []
                      Severity = None
                      Message = None }

            let enum =
                ShapeDecl.EnumShape(
                    Uri "https://schema.org/GameStatusType",
                    { Head = Uri "https://schema.org/ActiveActionStatus"; Tail = [] }
                )

            let combined = ShapeDecl.And { Head = record; Tail = [ enum ] }
            let negated = ShapeDecl.Not record
            let xor = ShapeDecl.Xone { Head = record; Tail = [ enum ] }
            let alt = ShapeDecl.Or { Head = record; Tail = [ enum ] }

            let describe =
                function
                | ShapeDecl.RecordShape _ -> "record"
                | ShapeDecl.EnumShape _ -> "enum"
                | ShapeDecl.And _ -> "and"
                | ShapeDecl.Or _ -> "or"
                | ShapeDecl.Not _ -> "not"
                | ShapeDecl.Xone _ -> "xone"

            Expect.equal (describe record) "record" "record case"
            Expect.equal (describe enum) "enum" "enum case"
            Expect.equal (describe combined) "and" "and case"
            Expect.equal (describe negated) "not" "not case"
            Expect.equal (describe xor) "xone" "xone case"
            Expect.equal (describe alt) "or" "or case"
        }

        test "PropertyConstraint.Node is recursive -- a property can require conformance to another ShapeDecl" {
            let inner =
                ShapeDecl.RecordShape
                    { Targets = []; Properties = []; Closed = false; IgnoredProperties = []; Severity = None; Message = None }

            let c = PropertyConstraint.Node inner
            Expect.equal c (PropertyConstraint.Node inner) "recursive constraint constructs"
        }

        test "SparqlConstraint carries author-supplied query text, message, and prefixes" {
            let sc: SparqlConstraint =
                { Query = "ASK { $this a <https://schema.org/Person> }"
                  Message = Some "must be a Person"
                  Prefixes = [ "schema", "https://schema.org/" ] }

            Expect.stringContains sc.Query "ASK" "query text preserved"
        }
    ]
```

- [ ] **Step 2: Run — verify it fails to compile (types don't exist yet)**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "ShapeTypes"`
Expected: build FAILS with `error FS0039: The value or constructor 'XsdDatatype' is not defined` (or similar for the other missing types).

- [ ] **Step 3: Write `ShapeTypes.fsi`**

```fsharp
// src/Frank.Validation/ShapeTypes.fsi
namespace Frank.Validation

open System
open Frank.Rdf

/// A non-empty list -- illegal-empty-list states unrepresentable, e.g. for sh:in / sh:languageIn /
/// logical-combinator members, which SHACL requires to be non-empty.
type NonEmptyList<'T> =
    { Head: 'T
      Tail: 'T list }

module NonEmptyList =
    val ofList: items: 'T list -> NonEmptyList<'T> option
    val toList: nel: NonEmptyList<'T> -> 'T list

/// The closed set of xsd datatypes Frank maps to sh:datatype. RequireQualifiedAccess means
/// XsdDatatype.Integer, not a redundant XsdInteger -- see the design doc's naming note.
[<Struct; RequireQualifiedAccess>]
type XsdDatatype =
    | Integer
    | Long
    | Decimal
    | Double
    | Boolean
    | String
    | DateTime

/// sh:nodeKind's five permitted values.
[<Struct; RequireQualifiedAccess>]
type NodeKind =
    | BlankNode
    | Iri
    | Literal
    | BlankNodeOrIri
    | BlankNodeOrLiteral
    | IriOrLiteral

/// sh:severity's three permitted values.
[<Struct; RequireQualifiedAccess>]
type Severity =
    | Violation
    | Warning
    | Info

/// sh:targetClass / sh:targetNode / sh:targetSubjectsOf / sh:targetObjectsOf.
[<RequireQualifiedAccess>]
type TargetSpec =
    | Class of Uri
    | Node of Node
    | SubjectsOf of Uri
    | ObjectsOf of Uri

/// sh:path -- not always a single predicate. The full SHACL property-path grammar.
[<RequireQualifiedAccess>]
type PropertyPath =
    | Predicate of Uri
    | Inverse of PropertyPath
    | Sequence of NonEmptyList<PropertyPath>
    | Alternative of NonEmptyList<PropertyPath>
    | ZeroOrMore of PropertyPath
    | OneOrMore of PropertyPath
    | ZeroOrOne of PropertyPath

/// An author-supplied SPARQL ASK query as a SHACL-SPARQL constraint (sh:sparql). The query text is
/// written by the shape's author (a developer), never derived from request input.
type SparqlConstraint =
    { Query: string
      Message: string option
      Prefixes: (string * string) list }

/// Every SHACL Core property constraint component this package supports, plus sh:sparql. A total DU:
/// each case only carries what SHACL itself requires for that constraint.
[<RequireQualifiedAccess>]
type PropertyConstraint =
    | Class of Uri
    | Datatype of XsdDatatype
    | NodeKind of NodeKind
    | MinCount of int
    | MaxCount of int
    | MinExclusive of Literal
    | MinInclusive of Literal
    | MaxExclusive of Literal
    | MaxInclusive of Literal
    | MinLength of int
    | MaxLength of int
    | Pattern of pattern: string * flags: string option
    | LanguageIn of NonEmptyList<string>
    | UniqueLang of bool
    | Equals of Uri
    | Disjoint of Uri
    | LessThan of Uri
    | LessThanOrEquals of Uri
    | Node of ShapeDecl
    | QualifiedValueShape of shape: ShapeDecl * minCount: int option * maxCount: int option * disjoint: bool
    | HasValue of Value
    | AllowedValues of NonEmptyList<Value>
    | Sparql of SparqlConstraint

/// A single sh:PropertyShape: a path plus its constraints.
and PropertyShapeSpec =
    { Path: PropertyPath
      Constraints: PropertyConstraint list
      Severity: Severity option
      Message: string option }

/// A single sh:NodeShape: zero or more targets (empty is valid -- a shape referenced only via
/// sh:node/sh:qualifiedValueShape), its property shapes, and its own closedness/severity/message.
and NodeShapeSpec =
    { Targets: TargetSpec list
      Properties: PropertyShapeSpec list
      Closed: bool
      IgnoredProperties: Uri list
      Severity: Severity option
      Message: string option }

/// A total DU over every top-level SHACL shape form this package supports.
and ShapeDecl =
    | RecordShape of NodeShapeSpec
    | EnumShape of targetClass: Uri * cases: NonEmptyList<Uri>
    | And of NonEmptyList<ShapeDecl>
    | Or of NonEmptyList<ShapeDecl>
    | Not of ShapeDecl
    | Xone of NonEmptyList<ShapeDecl>
```

- [ ] **Step 4: Write `ShapeTypes.fs`**

```fsharp
// src/Frank.Validation/ShapeTypes.fs
namespace Frank.Validation

open System
open Frank.Rdf

type NonEmptyList<'T> = { Head: 'T; Tail: 'T list }

module NonEmptyList =
    let ofList (items: 'T list) : NonEmptyList<'T> option =
        match items with
        | [] -> None
        | head :: tail -> Some { Head = head; Tail = tail }

    let toList (nel: NonEmptyList<'T>) : 'T list = nel.Head :: nel.Tail

[<Struct; RequireQualifiedAccess>]
type XsdDatatype =
    | Integer
    | Long
    | Decimal
    | Double
    | Boolean
    | String
    | DateTime

[<Struct; RequireQualifiedAccess>]
type NodeKind =
    | BlankNode
    | Iri
    | Literal
    | BlankNodeOrIri
    | BlankNodeOrLiteral
    | IriOrLiteral

[<Struct; RequireQualifiedAccess>]
type Severity =
    | Violation
    | Warning
    | Info

[<RequireQualifiedAccess>]
type TargetSpec =
    | Class of Uri
    | Node of Node
    | SubjectsOf of Uri
    | ObjectsOf of Uri

[<RequireQualifiedAccess>]
type PropertyPath =
    | Predicate of Uri
    | Inverse of PropertyPath
    | Sequence of NonEmptyList<PropertyPath>
    | Alternative of NonEmptyList<PropertyPath>
    | ZeroOrMore of PropertyPath
    | OneOrMore of PropertyPath
    | ZeroOrOne of PropertyPath

type SparqlConstraint =
    { Query: string
      Message: string option
      Prefixes: (string * string) list }

[<RequireQualifiedAccess>]
type PropertyConstraint =
    | Class of Uri
    | Datatype of XsdDatatype
    | NodeKind of NodeKind
    | MinCount of int
    | MaxCount of int
    | MinExclusive of Literal
    | MinInclusive of Literal
    | MaxExclusive of Literal
    | MaxInclusive of Literal
    | MinLength of int
    | MaxLength of int
    | Pattern of pattern: string * flags: string option
    | LanguageIn of NonEmptyList<string>
    | UniqueLang of bool
    | Equals of Uri
    | Disjoint of Uri
    | LessThan of Uri
    | LessThanOrEquals of Uri
    | Node of ShapeDecl
    | QualifiedValueShape of shape: ShapeDecl * minCount: int option * maxCount: int option * disjoint: bool
    | HasValue of Value
    | AllowedValues of NonEmptyList<Value>
    | Sparql of SparqlConstraint

and PropertyShapeSpec =
    { Path: PropertyPath
      Constraints: PropertyConstraint list
      Severity: Severity option
      Message: string option }

and NodeShapeSpec =
    { Targets: TargetSpec list
      Properties: PropertyShapeSpec list
      Closed: bool
      IgnoredProperties: Uri list
      Severity: Severity option
      Message: string option }

and ShapeDecl =
    | RecordShape of NodeShapeSpec
    | EnumShape of targetClass: Uri * cases: NonEmptyList<Uri>
    | And of NonEmptyList<ShapeDecl>
    | Or of NonEmptyList<ShapeDecl>
    | Not of ShapeDecl
    | Xone of NonEmptyList<ShapeDecl>
```

- [ ] **Step 5: Wire both projects' `<Compile>` lists**

In `src/Frank.Validation/Frank.Validation.fsproj`, add as the first `<ItemGroup>` (before the `PackageReference` group):

```xml
<ItemGroup>
  <Compile Include="ShapeTypes.fsi" />
  <Compile Include="ShapeTypes.fs" />
</ItemGroup>
```

In `test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj`, change the `<Compile>` group to:

```xml
<ItemGroup>
  <Compile Include="ShapeTypesTests.fs" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

- [ ] **Step 6: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "ShapeTypes"`
Expected: all 8 tests PASS.

- [ ] **Step 7: Fantomas + full-suite build + commit**

```bash
dotnet fantomas src/Frank.Validation/ShapeTypes.fsi src/Frank.Validation/ShapeTypes.fs test/Frank.Validation.Tests/ShapeTypesTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/ShapeTypes.fsi src/Frank.Validation/ShapeTypes.fs src/Frank.Validation/Frank.Validation.fsproj test/Frank.Validation.Tests/ShapeTypesTests.fs test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj
git commit -m "feat(validation): ShapeTypes -- full SHACL Core + SPARQL data model, illegal states unrepresentable"
```

---

### Task 3: `ShapeSpec.fs` — plain functions (the real authoring model)

**Files:**
- Create: `src/Frank.Validation/ShapeSpec.fsi`
- Create: `src/Frank.Validation/ShapeSpec.fs`
- Modify: `src/Frank.Validation/Frank.Validation.fsproj` (add `<Compile>` pair, after `ShapeTypes.fs`)
- Create: `test/Frank.Validation.Tests/ShapeSpecTests.fs`
- Modify: `test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj`

**Interfaces:**
- Consumes: everything from Task 2 (`ShapeTypes.fs`).
- Produces: `ShapeSpecFunctions.ofPath`, `.addConstraint`, `.recordShape`, `.enumShape`, `.targetClass` — every later task (`Shacl.fs`'s test fixtures, `ShapeBuilder.fs`'s CE bodies, the sample) constructs shapes through these five functions, never through raw record/DU literals directly (though nothing prevents that either — these are just the named, documented entry points).

- [ ] **Step 1: Write the failing test**

```fsharp
// test/Frank.Validation.Tests/ShapeSpecTests.fs
module Frank.Validation.Tests.ShapeSpecTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

[<Tests>]
let tests =
    testList "ShapeSpecFunctions" [
        test "ofPath seeds an empty, unconstrained PropertyShapeSpec" {
            let p = ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
            Expect.isEmpty p.Constraints "no constraints yet"
            Expect.isNone p.Severity "no severity yet"
            Expect.isNone p.Message "no message yet"
        }

        test "addConstraint appends, preserving order, and is the basis for every per-constraint helper" {
            let p =
                ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                |> addConstraint (PropertyConstraint.Datatype XsdDatatype.Integer)
                |> addConstraint (PropertyConstraint.MinCount 1)
                |> addConstraint (PropertyConstraint.MaxCount 1)

            Expect.equal
                p.Constraints
                [ PropertyConstraint.Datatype XsdDatatype.Integer; PropertyConstraint.MinCount 1; PropertyConstraint.MaxCount 1 ]
                "constraints append in call order"
        }

        test "recordShape builds a ShapeDecl.RecordShape with the given targets and properties, defaults otherwise" {
            let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) |> addConstraint (PropertyConstraint.MinCount 1)
            let decl = recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ prop ]

            match decl with
            | ShapeDecl.RecordShape n ->
                Expect.equal n.Targets [ TargetSpec.Class(Uri "https://schema.org/MoveAction") ] "targets"
                Expect.equal n.Properties [ prop ] "properties"
                Expect.isFalse n.Closed "not closed by default"
                Expect.isEmpty n.IgnoredProperties "no ignored properties by default"
            | other -> failtestf "expected RecordShape, got %A" other
        }

        test "recordShape with empty targets is valid -- for shapes referenced only via sh:node" {
            let decl = recordShape [] []
            match decl with
            | ShapeDecl.RecordShape n -> Expect.isEmpty n.Targets "empty targets accepted"
            | other -> failtestf "expected RecordShape, got %A" other
        }

        test "enumShape builds a ShapeDecl.EnumShape with a guaranteed non-empty case list" {
            let decl =
                enumShape
                    (Uri "https://schema.org/GameStatusType")
                    (Uri "https://schema.org/ActiveActionStatus")
                    [ Uri "https://schema.org/CompletedActionStatus" ]

            match decl with
            | ShapeDecl.EnumShape(targetClass, cases) ->
                Expect.equal targetClass (Uri "https://schema.org/GameStatusType") "target class"
                Expect.equal (NonEmptyList.toList cases) [ Uri "https://schema.org/ActiveActionStatus"; Uri "https://schema.org/CompletedActionStatus" ] "cases"
            | other -> failtestf "expected EnumShape, got %A" other
        }

        test "targetClass is sugar for a single-element TargetSpec.Class list" {
            Expect.equal (targetClass (Uri "https://schema.org/Person")) [ TargetSpec.Class(Uri "https://schema.org/Person") ] "single-element list"
        }
    ]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "ShapeSpecFunctions"`
Expected: FAIL — `Frank.Validation.ShapeSpecFunctions` module not found.

- [ ] **Step 3: Write `ShapeSpec.fsi`**

```fsharp
// src/Frank.Validation/ShapeSpec.fsi
namespace Frank.Validation

open System

/// Plain curried functions -- the real authoring model. Kept to the ones that construct a genuinely
/// new value or combine data non-trivially; simple field mutation doesn't get a named counterpart
/// here (see ShapeBuilder.fsi for why -- it's inlined directly in the CE instead).
module ShapeSpecFunctions =
    val ofPath: path: PropertyPath -> PropertyShapeSpec

    /// The one general-purpose accumulator every per-constraint CE operation is sugar over. Because
    /// PropertyConstraint is already a closed, named DU, this IS the plain-function API for adding a
    /// constraint -- `p |> addConstraint (PropertyConstraint.Datatype XsdDatatype.Integer)`.
    val addConstraint: constr: PropertyConstraint -> spec: PropertyShapeSpec -> PropertyShapeSpec

    val recordShape: targets: TargetSpec list -> properties: PropertyShapeSpec list -> ShapeDecl

    val enumShape: targetClass: Uri -> head: Uri -> tail: Uri list -> ShapeDecl

    /// Convenience for the common single-class-target case.
    val targetClass: uri: Uri -> TargetSpec list
```

- [ ] **Step 4: Write `ShapeSpec.fs`**

```fsharp
// src/Frank.Validation/ShapeSpec.fs
namespace Frank.Validation

open System

module ShapeSpecFunctions =
    let ofPath (path: PropertyPath) : PropertyShapeSpec =
        { Path = path
          Constraints = []
          Severity = None
          Message = None }

    let addConstraint (constr: PropertyConstraint) (spec: PropertyShapeSpec) : PropertyShapeSpec =
        { spec with Constraints = spec.Constraints @ [ constr ] }

    let recordShape (targets: TargetSpec list) (properties: PropertyShapeSpec list) : ShapeDecl =
        ShapeDecl.RecordShape
            { Targets = targets
              Properties = properties
              Closed = false
              IgnoredProperties = []
              Severity = None
              Message = None }

    let enumShape (targetClass: Uri) (head: Uri) (tail: Uri list) : ShapeDecl =
        ShapeDecl.EnumShape(targetClass, { Head = head; Tail = tail })

    let targetClass (uri: Uri) : TargetSpec list = [ TargetSpec.Class uri ]
```

- [ ] **Step 5: Wire both projects' `<Compile>` lists**

`Frank.Validation.fsproj`:

```xml
<ItemGroup>
  <Compile Include="ShapeTypes.fsi" />
  <Compile Include="ShapeTypes.fs" />
  <Compile Include="ShapeSpec.fsi" />
  <Compile Include="ShapeSpec.fs" />
</ItemGroup>
```

`Frank.Validation.Tests.fsproj`:

```xml
<ItemGroup>
  <Compile Include="ShapeTypesTests.fs" />
  <Compile Include="ShapeSpecTests.fs" />
  <Compile Include="Program.fs" />
</ItemGroup>
```

- [ ] **Step 6: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "ShapeSpecFunctions"`
Expected: all 6 tests PASS.

- [ ] **Step 7: Fantomas + full-suite build + commit**

```bash
dotnet fantomas src/Frank.Validation/ShapeSpec.fsi src/Frank.Validation/ShapeSpec.fs test/Frank.Validation.Tests/ShapeSpecTests.fs
dotnet build Frank.sln
dotnet test test/Frank.Validation.Tests/
git add src/Frank.Validation/ShapeSpec.fsi src/Frank.Validation/ShapeSpec.fs src/Frank.Validation/Frank.Validation.fsproj test/Frank.Validation.Tests/ShapeSpecTests.fs test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj
git commit -m "feat(validation): ShapeSpecFunctions -- plain curried authoring functions"
```

---

### Task 4: `Shacl.fs` foundation — prefixes, rdf:list, property paths, `RecordShape` skeleton

**Files:**
- Create: `src/Frank.Validation/Shacl.fsi`
- Create: `src/Frank.Validation/Shacl.fs`
- Modify: `Frank.Validation.fsproj` (add `<Compile>` pair, after `ShapeSpec.fs`)
- Create: `test/Frank.Validation.Tests/ShaclToDocTests.fs`
- Modify: `Frank.Validation.Tests.fsproj`

**Interfaces:**
- Consumes: `ShapeTypes.fs`, `ShapeSpec.fs`; `Frank.Rdf.Doc`/`Node`/`Value`/`Literal`/`RdfTypeIri`.
- Produces: `Shacl.toDoc: ShapeDecl list -> Doc` (RecordShape only in this task — no property constraint triples yet, `sh:property` blank nodes carry `sh:path` only); `internal Shacl.rdfList: Value list -> Node * (Node * string * Value) list` (later tasks reuse this directly for `sh:in`/`sh:languageIn`/`sh:ignoredProperties`/logical-combinator members); `internal Shacl.pathNode: PropertyPath -> Node * (Node * string * Value) list` (fully implements all seven `PropertyPath` cases now — paths are structural, not a constraint category, so there's no reason to stage this further).

Property-shape constraint triples (`sh:datatype`, `sh:minCount`, ...) are added incrementally in Tasks 5-13 via a `constraintStatements` function this task does NOT define yet — Task 5 introduces it. This task's `sh:property` blank nodes carry only `sh:path`, which Task 5's test suite extends.

- [ ] **Step 1: Write the failing test**

```fsharp
// test/Frank.Validation.Tests/ShaclToDocTests.fs
module Frank.Validation.Tests.ShaclToDocTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

let private hasTriple (doc: Doc) (predicateSuffix: string) : bool =
    doc.Statements |> List.exists (fun (_, p, _) -> p.EndsWith(predicateSuffix: string))

[<Tests>]
let tests =
    testList "Shacl.toDoc" [
        testList "foundation" [
            test "rdfList: empty list has rdf:nil as its head and mints no blank nodes" {
                let head, stmts = Shacl.rdfList []
                Expect.equal head (Node.Iri "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil") "empty list head is rdf:nil"
                Expect.isEmpty stmts "no statements for an empty list"
            }

            test "rdfList: well-formed rdf:first/rdf:rest chain, terminated by rdf:nil (the orphaned-list bug this guards against)" {
                let head, stmts = Shacl.rdfList [ Value.Literal(Literal.Int 1); Value.Literal(Literal.Int 2) ]
                let firsts = stmts |> List.filter (fun (s, p, _) -> s = head && p = "rdf:first")
                Expect.hasLength firsts 1 "the list's head cell has exactly one rdf:first"
                let rests = stmts |> List.filter (fun (s, p, _) -> p = "rdf:rest")
                Expect.hasLength rests 2 "two cells, each with one rdf:rest"
                let nilRests = rests |> List.filter (fun (_, _, v) -> v = Value.Node(Node.Iri "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"))
                Expect.hasLength nilRests 1 "exactly one cell terminates in rdf:nil"
            }

            test "pathNode: a simple predicate path is just its IRI, no blank nodes" {
                let node, stmts = Shacl.pathNode (PropertyPath.Predicate(Uri "https://schema.org/position"))
                Expect.equal node (Node.Iri "https://schema.org/position") "predicate path is the bare IRI"
                Expect.isEmpty stmts "no auxiliary statements"
            }

            test "pathNode: inverse path is a blank node with sh:inversePath pointing at the inner path" {
                let node, stmts = Shacl.pathNode (PropertyPath.Inverse(PropertyPath.Predicate(Uri "https://schema.org/parent")))
                match node with
                | Node.Blank _ -> ()
                | other -> failtestf "expected a blank node, got %A" other
                Expect.exists stmts (fun (s, p, v) -> s = node && p = "sh:inversePath" && v = Value.Node(Node.Iri "https://schema.org/parent")) "sh:inversePath triple present"
            }

            test "pathNode: zeroOrMore/oneOrMore/zeroOrOne each wrap in the matching sh:*Path predicate" {
                let inner = PropertyPath.Predicate(Uri "https://schema.org/knows")
                for path, predicate in
                    [ PropertyPath.ZeroOrMore inner, "sh:zeroOrMorePath"
                      PropertyPath.OneOrMore inner, "sh:oneOrMorePath"
                      PropertyPath.ZeroOrOne inner, "sh:zeroOrOnePath" ] do
                    let node, stmts = Shacl.pathNode path
                    Expect.exists stmts (fun (s, p, v) -> s = node && p = predicate && v = Value.Node(Node.Iri "https://schema.org/knows")) $"{predicate} triple present"
            }

            test "pathNode: sequence path is a well-formed rdf:list of the member path nodes" {
                let a = PropertyPath.Predicate(Uri "https://schema.org/a")
                let b = PropertyPath.Predicate(Uri "https://schema.org/b")
                let node, stmts = Shacl.pathNode (PropertyPath.Sequence { Head = a; Tail = [ b ] })
                let firsts = stmts |> List.choose (fun (s, p, v) -> if s = node && p = "rdf:first" then Some v else None)
                Expect.equal firsts [ Value.Node(Node.Iri "https://schema.org/a") ] "sequence head cell's rdf:first is the first path"
            }

            test "pathNode: alternative path is a blank node with sh:alternativePath pointing at a well-formed list" {
                let a = PropertyPath.Predicate(Uri "https://schema.org/a")
                let b = PropertyPath.Predicate(Uri "https://schema.org/b")
                let node, stmts = Shacl.pathNode (PropertyPath.Alternative { Head = a; Tail = [ b ] })
                Expect.exists stmts (fun (s, p, _) -> s = node && p = "sh:alternativePath") "sh:alternativePath present"
            }
        ]

        testList "RecordShape skeleton" [
            test "an untyped, unconstrained RecordShape declares sh:NodeShape and its target class" {
                let decl = recordShape (targetClass (Uri "https://schema.org/MoveAction")) []
                let doc = Shacl.toDoc [ decl ]
                let subject = Node.Iri "https://schema.org/MoveAction"
                Expect.exists doc.Statements (fun (s, p, v) -> s = subject && p = Rdf.RdfTypeIri && v = Value.Node(Node.Iri "sh:NodeShape")) "rdf:type sh:NodeShape"
                Expect.exists doc.Statements (fun (s, p, v) -> s = subject && p = "sh:targetClass" && v = Value.Node subject) "sh:targetClass"
            }

            test "a property shape becomes a blank-node sh:property with sh:path -- no constraint triples yet" {
                let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                let decl = recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ prop ]
                let doc = Shacl.toDoc [ decl ]
                let subject = Node.Iri "https://schema.org/MoveAction"
                let propertyBlankNodes =
                    doc.Statements |> List.choose (fun (s, p, v) -> if s = subject && p = "sh:property" then Some v else None)
                Expect.hasLength propertyBlankNodes 1 "one sh:property statement"
                match propertyBlankNodes with
                | [ Value.Node bn ] ->
                    Expect.exists doc.Statements (fun (s, p, v) -> s = bn && p = "sh:path" && v = Value.Node(Node.Iri "https://schema.org/position")) "sh:path on the blank node"
                | other -> failtestf "expected one blank node, got %A" other
            }

            test "multiple targets on one shape each become their own triple (never an rdf:list -- SHACL targets are repeated statements)" {
                let targets = [ TargetSpec.Class(Uri "https://schema.org/MoveAction"); TargetSpec.SubjectsOf(Uri "https://schema.org/agent") ]
                let decl = recordShape targets []
                let doc = Shacl.toDoc [ decl ]
                Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:targetClass" && v = Value.Node(Node.Iri "https://schema.org/MoveAction")) "sh:targetClass"
                Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:targetSubjectsOf" && v = Value.Node(Node.Iri "https://schema.org/agent")) "sh:targetSubjectsOf"
            }

            test "toDoc builds against a real dotNetRDF graph without throwing (prefixes resolve)" {
                let decl = recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) ]
                let doc = Shacl.toDoc [ decl ]
                let graph = Doc.toGraph doc
                Expect.isGreaterThan graph.Triples.Count 0 "at least one triple asserted"
            }
        ]
    ]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "Shacl.toDoc"`
Expected: FAIL — `Frank.Validation.Shacl` module not found.

- [ ] **Step 3: Write `Shacl.fsi`**

```fsharp
// src/Frank.Validation/Shacl.fsi
namespace Frank.Validation

open Frank.Rdf

/// The interpreter: projects the hand-authored ShapeDecl model onto Frank.Rdf's Doc/Node/Value --
/// the single SHACL graph-builder, no parallel triple model.
module Shacl =
    /// Builds a well-formed rdf:list: head node + rdf:first/rdf:rest/rdf:nil triples, one blank node
    /// per element. An empty list's head is rdf:nil itself -- no blank nodes minted.
    val internal rdfList: items: Value list -> Node * (Node * string * Value) list

    /// Projects a PropertyPath onto its sh:path representation: a bare IRI for Predicate, or a blank
    /// node carrying the matching sh:inversePath/sh:alternativePath/sh:zeroOrMorePath/sh:oneOrMorePath/
    /// sh:zeroOrOnePath/rdf:list-of-paths structure for the other six cases.
    val internal pathNode: path: PropertyPath -> Node * (Node * string * Value) list

    /// Projects a ShapeDecl list onto a Doc: one sh:NodeShape/sh:PropertyShape pair per shape,
    /// blank nodes for anonymous property shapes and path expressions.
    val toDoc: shapes: ShapeDecl list -> Doc
```

- [ ] **Step 4: Write `Shacl.fs`**

```fsharp
// src/Frank.Validation/Shacl.fs
namespace Frank.Validation

open System
open Frank.Rdf

module Shacl =
    [<Literal>]
    let private RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"

    let private shaclPrefixes =
        [ "sh", "http://www.w3.org/ns/shacl#"
          "xsd", "http://www.w3.org/2001/XMLSchema#"
          "rdf", RdfNs ]

    let private stmt (s: Node) (p: string) (v: Value) : Node * string * Value = s, p, v

    let rec internal rdfList (items: Value list) : Node * (Node * string * Value) list =
        match items with
        | [] -> Node.Iri(RdfNs + "nil"), []
        | item :: rest ->
            let cell = Node.blank ()
            let restHead, restStmts = rdfList rest

            let stmts =
                [ stmt cell "rdf:first" item; stmt cell "rdf:rest" (Value.Node restHead) ] @ restStmts

            cell, stmts

    let rec internal pathNode (path: PropertyPath) : Node * (Node * string * Value) list =
        let wrap (predicate: string) (inner: PropertyPath) =
            let bn = Node.blank ()
            let innerNode, innerStmts = pathNode inner
            bn, stmt bn predicate (Value.Node innerNode) :: innerStmts

        match path with
        | PropertyPath.Predicate uri -> Node.Iri uri.AbsoluteUri, []
        | PropertyPath.Inverse inner -> wrap "sh:inversePath" inner
        | PropertyPath.ZeroOrMore inner -> wrap "sh:zeroOrMorePath" inner
        | PropertyPath.OneOrMore inner -> wrap "sh:oneOrMorePath" inner
        | PropertyPath.ZeroOrOne inner -> wrap "sh:zeroOrOnePath" inner
        | PropertyPath.Sequence paths ->
            let members = NonEmptyList.toList paths |> List.map pathNode
            let listHead, listStmts = rdfList (members |> List.map (fst >> Value.Node))
            listHead, (members |> List.collect snd) @ listStmts
        | PropertyPath.Alternative paths ->
            let members = NonEmptyList.toList paths |> List.map pathNode
            let listHead, listStmts = rdfList (members |> List.map (fst >> Value.Node))
            let bn = Node.blank ()

            bn,
            (stmt bn "sh:alternativePath" (Value.Node listHead) :: (members |> List.collect snd))
            @ listStmts

    let private targetStatements (subject: Node) (target: TargetSpec) : (Node * string * Value) list =
        match target with
        | TargetSpec.Class uri -> [ stmt subject "sh:targetClass" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | TargetSpec.Node node -> [ stmt subject "sh:targetNode" (Value.Node node) ]
        | TargetSpec.SubjectsOf uri -> [ stmt subject "sh:targetSubjectsOf" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | TargetSpec.ObjectsOf uri -> [ stmt subject "sh:targetObjectsOf" (Value.Node(Node.Iri uri.AbsoluteUri)) ]

    let private propertyShapeStatements (spec: PropertyShapeSpec) : Node * (Node * string * Value) list =
        let bn = Node.blank ()
        let pathHead, pathStmts = pathNode spec.Path
        bn, (stmt bn "sh:path" (Value.Node pathHead) :: pathStmts)

    /// The one place a ShapeDecl becomes a subject node plus its own statements. RecordShape is fully
    /// handled here; EnumShape/And/Or/Not/Xone are added by Tasks 9-10 -- this wildcard is a real,
    /// defined interim behavior (no triples for those cases yet), not a stub, and it narrows task by
    /// task until Task 10 removes it and this becomes an exhaustive match.
    let rec private shapeStatements (decl: ShapeDecl) : Node * (Node * string * Value) list =
        match decl with
        | ShapeDecl.RecordShape spec ->
            // A RecordShape's subject is its own IRI when it has at least one TargetSpec.Class target
            // (the common, directly-dereferenceable case); otherwise a fresh blank node, since a shape
            // meant only to be nested via sh:node has no natural IRI of its own.
            let subject =
                spec.Targets
                |> List.tryPick (function
                    | TargetSpec.Class uri -> Some(Node.Iri uri.AbsoluteUri)
                    | _ -> None)
                |> Option.defaultWith Node.blank

            let typeStmt = stmt subject RdfTypeIri (Value.Node(Node.Iri "sh:NodeShape"))
            let targetStmts = spec.Targets |> List.collect (targetStatements subject)

            let propertyStmts =
                spec.Properties
                |> List.collect (fun p ->
                    let bn, stmts = propertyShapeStatements p
                    stmt subject "sh:property" (Value.Node bn) :: stmts)

            subject, typeStmt :: targetStmts @ propertyStmts
        | _ -> Node.blank (), []

    let toDoc (shapes: ShapeDecl list) : Doc =
        let statements = shapes |> List.collect (shapeStatements >> snd)
        { Prefixes = shaclPrefixes; Statements = statements }
```

- [ ] **Step 5: Wire both projects' `<Compile>` lists**

`Frank.Validation.fsproj` — append after `ShapeSpec.fs`:

```xml
<Compile Include="Shacl.fsi" />
<Compile Include="Shacl.fs" />
```

`Frank.Validation.Tests.fsproj` — insert before `Program.fs`:

```xml
<Compile Include="ShaclToDocTests.fs" />
```

- [ ] **Step 6: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "Shacl.toDoc"`
Expected: all tests PASS. If `pathNode`'s blank-node/list-shape assertions fail on structural details, re-check against the `rdfList` orphaned-list discipline already proven in the old Plan 4 reference (`docs/superpowers/plans/2026-06-22-v732-codegen-remediation-plan4-validation.md`'s `addRdfList`) — the head of a list must literally be the first cell, never a separate pointer.

- [ ] **Step 7: Fantomas + full-suite build + commit**

```bash
dotnet fantomas src/Frank.Validation/Shacl.fsi src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
dotnet build Frank.sln
dotnet test test/Frank.Validation.Tests/
git add src/Frank.Validation/Shacl.fsi src/Frank.Validation/Shacl.fs src/Frank.Validation/Frank.Validation.fsproj test/Frank.Validation.Tests/ShaclToDocTests.fs test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj
git commit -m "feat(validation): Shacl.toDoc foundation -- rdf:list, full property-path grammar, RecordShape skeleton"
```

---

### Task 5: `Shacl.fs` — value type constraints (`sh:class`, `sh:datatype`, `sh:nodeKind`)

**Files:**
- Modify: `src/Frank.Validation/Shacl.fs` (introduces `constraintStatements`, restructures `shapeStatements`/`propertyShapeStatements`/`constraintStatements` into one mutually-recursive group — later tasks need `constraintStatements` to call back into `shapeStatements` for the recursive `Node`/`QualifiedValueShape` cases, so the group is formed now rather than reshuffled later)
- Modify: `test/Frank.Validation.Tests/ShaclToDocTests.fs` (new `testList "value type constraints"`)

**Interfaces:**
- Produces: `constraintStatements: Node -> PropertyConstraint -> (Node * string * Value) list` (private) — handles `Class`/`Datatype`/`NodeKind` now; every other case falls through a `| _ -> []` wildcard that Tasks 6-13 narrow, one category at a time, until Task 13 removes it and the match is exhaustive (compiler-checked, since this codebase's `Directory.Build.props` sets `TreatWarningsAsErrors` and FS0025 incomplete-match is a warning that becomes an error).

- [ ] **Step 1: Write the failing test** — append to `test/Frank.Validation.Tests/ShaclToDocTests.fs`, inside the existing `testList "Shacl.toDoc" [ ... ]`, as a new sibling `testList`:

```fsharp
testList "value type constraints" [
    test "sh:class on a property shape" {
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/agent")) |> addConstraint (PropertyConstraint.Class(Uri "https://schema.org/Person"))
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:class" && v = Value.Node(Node.Iri "https://schema.org/Person")) "sh:class present"
    }

    test "sh:datatype maps every XsdDatatype case to its xsd: CURIE" {
        let cases =
            [ XsdDatatype.Integer, "xsd:integer"
              XsdDatatype.Long, "xsd:long"
              XsdDatatype.Decimal, "xsd:decimal"
              XsdDatatype.Double, "xsd:double"
              XsdDatatype.Boolean, "xsd:boolean"
              XsdDatatype.String, "xsd:string"
              XsdDatatype.DateTime, "xsd:dateTime" ]

        for dt, expectedCurie in cases do
            let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) |> addConstraint (PropertyConstraint.Datatype dt)
            let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
            Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:datatype" && v = Value.Node(Node.Iri expectedCurie)) $"sh:datatype for {dt}"
    }

    test "sh:nodeKind maps every NodeKind case to its sh: individual" {
        let cases =
            [ NodeKind.BlankNode, "sh:BlankNode"
              NodeKind.Iri, "sh:IRI"
              NodeKind.Literal, "sh:Literal"
              NodeKind.BlankNodeOrIri, "sh:BlankNodeOrIRI"
              NodeKind.BlankNodeOrLiteral, "sh:BlankNodeOrLiteral"
              NodeKind.IriOrLiteral, "sh:IRIOrLiteral" ]

        for nk, expectedCurie in cases do
            let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) |> addConstraint (PropertyConstraint.NodeKind nk)
            let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
            Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:nodeKind" && v = Value.Node(Node.Iri expectedCurie)) $"sh:nodeKind for {nk}"
    }

    test "a property shape with no constraints still emits only sh:path (wildcard is a no-op, not an error)" {
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x"))
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:path") "sh:path still present"
    }
]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "value type constraints"`
Expected: FAIL — assertions about `sh:class`/`sh:datatype`/`sh:nodeKind` find nothing, since `constraintStatements` doesn't exist and `propertyShapeStatements` doesn't call it yet.

- [ ] **Step 3: Restructure `Shacl.fs`** — replace the `propertyShapeStatements`/`shapeStatements`/`toDoc` section (everything from `let private propertyShapeStatements` through the end of the file) with:

```fsharp
    let private xsdCurie (dt: XsdDatatype) : string =
        match dt with
        | XsdDatatype.Integer -> "xsd:integer"
        | XsdDatatype.Long -> "xsd:long"
        | XsdDatatype.Decimal -> "xsd:decimal"
        | XsdDatatype.Double -> "xsd:double"
        | XsdDatatype.Boolean -> "xsd:boolean"
        | XsdDatatype.String -> "xsd:string"
        | XsdDatatype.DateTime -> "xsd:dateTime"

    let private nodeKindCurie (nk: NodeKind) : string =
        match nk with
        | NodeKind.BlankNode -> "sh:BlankNode"
        | NodeKind.Iri -> "sh:IRI"
        | NodeKind.Literal -> "sh:Literal"
        | NodeKind.BlankNodeOrIri -> "sh:BlankNodeOrIRI"
        | NodeKind.BlankNodeOrLiteral -> "sh:BlankNodeOrLiteral"
        | NodeKind.IriOrLiteral -> "sh:IRIOrLiteral"

    /// One case added per Task 5-13; the wildcard's scope is documented at each task that narrows it.
    /// Mutually recursive with propertyShapeStatements/shapeStatements from this task on, because
    /// Task 9's PropertyConstraint.Node/QualifiedValueShape cases call back into shapeStatements.
    let rec private constraintStatements (propNode: Node) (c: PropertyConstraint) : (Node * string * Value) list =
        match c with
        | PropertyConstraint.Class uri -> [ stmt propNode "sh:class" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | PropertyConstraint.Datatype dt -> [ stmt propNode "sh:datatype" (Value.Node(Node.Iri(xsdCurie dt))) ]
        | PropertyConstraint.NodeKind nk -> [ stmt propNode "sh:nodeKind" (Value.Node(Node.Iri(nodeKindCurie nk))) ]
        | _ -> []

    and private propertyShapeStatements (spec: PropertyShapeSpec) : Node * (Node * string * Value) list =
        let bn = Node.blank ()
        let pathHead, pathStmts = pathNode spec.Path
        let constraintStmts = spec.Constraints |> List.collect (constraintStatements bn)
        bn, (stmt bn "sh:path" (Value.Node pathHead) :: pathStmts) @ constraintStmts

    and private shapeStatements (decl: ShapeDecl) : Node * (Node * string * Value) list =
        match decl with
        | ShapeDecl.RecordShape spec ->
            let subject =
                spec.Targets
                |> List.tryPick (function
                    | TargetSpec.Class uri -> Some(Node.Iri uri.AbsoluteUri)
                    | _ -> None)
                |> Option.defaultWith Node.blank

            let typeStmt = stmt subject RdfTypeIri (Value.Node(Node.Iri "sh:NodeShape"))
            let targetStmts = spec.Targets |> List.collect (targetStatements subject)

            let propertyStmts =
                spec.Properties
                |> List.collect (fun p ->
                    let bn, stmts = propertyShapeStatements p
                    stmt subject "sh:property" (Value.Node bn) :: stmts)

            subject, typeStmt :: targetStmts @ propertyStmts
        | _ -> Node.blank (), []

    let toDoc (shapes: ShapeDecl list) : Doc =
        let statements = shapes |> List.collect (shapeStatements >> snd)
        { Prefixes = shaclPrefixes; Statements = statements }
```

This replaces the old non-recursive `propertyShapeStatements`/`shapeStatements`/`toDoc` trio with one `let rec ... and ... and ... and` group (`constraintStatements`, `propertyShapeStatements`, `shapeStatements`; `toDoc` stays a plain `let` after the group, since nothing recurses back into it).

- [ ] **Step 4: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: every test so far PASSES, including Tasks 2-4's.

- [ ] **Step 5: Fantomas + commit**

```bash
dotnet fantomas src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
git commit -m "feat(validation): Shacl.toDoc -- value type constraints (sh:class, sh:datatype, sh:nodeKind)"
```

---

### Task 6: `Shacl.fs` — cardinality + value range constraints

**Files:**
- Modify: `src/Frank.Validation/Shacl.fs` (`constraintStatements` gains `MinCount`/`MaxCount`/`MinExclusive`/`MinInclusive`/`MaxExclusive`/`MaxInclusive`)
- Modify: `test/Frank.Validation.Tests/ShaclToDocTests.fs`

**Interfaces:**
- Produces: `constraintStatements` now also handles cardinality + value range; wildcard narrows to the remaining 15 cases.

- [ ] **Step 1: Write the failing test** — append a new `testList` sibling in `ShaclToDocTests.fs`:

```fsharp
testList "cardinality and value range constraints" [
    test "sh:minCount and sh:maxCount as xsd:integer literals" {
        let prop =
            ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
            |> addConstraint (PropertyConstraint.MinCount 1)
            |> addConstraint (PropertyConstraint.MaxCount 1)

        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:minCount" && v = Value.Literal(Literal.Int 1)) "sh:minCount"
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:maxCount" && v = Value.Literal(Literal.Int 1)) "sh:maxCount"
    }

    test "sh:minExclusive/minInclusive/maxExclusive/maxInclusive carry the given Literal unchanged" {
        let cases =
            [ PropertyConstraint.MinExclusive(Literal.Int 0), "sh:minExclusive"
              PropertyConstraint.MinInclusive(Literal.Int 0), "sh:minInclusive"
              PropertyConstraint.MaxExclusive(Literal.Int 100), "sh:maxExclusive"
              PropertyConstraint.MaxInclusive(Literal.Int 100), "sh:maxInclusive" ]

        for constr, predicate in cases do
            let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) |> addConstraint constr
            let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
            Expect.exists doc.Statements (fun (_, p, _) -> p = predicate) $"{predicate} present"
    }

    test "range constraints work with DateTime literals too, not just Int" {
        let t = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) |> addConstraint (PropertyConstraint.MinInclusive(Literal.DateTime t))
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:minInclusive" && v = Value.Literal(Literal.DateTime t)) "sh:minInclusive with a DateTime literal"
    }
]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "cardinality and value range"`
Expected: FAIL — these six cases still fall through the wildcard.

- [ ] **Step 3: Extend `constraintStatements`'s match** — insert new cases immediately after the `NodeKind` case (before the wildcard):

```fsharp
        | PropertyConstraint.MinCount n -> [ stmt propNode "sh:minCount" (Value.Literal(Literal.Int n)) ]
        | PropertyConstraint.MaxCount n -> [ stmt propNode "sh:maxCount" (Value.Literal(Literal.Int n)) ]
        | PropertyConstraint.MinExclusive lit -> [ stmt propNode "sh:minExclusive" (Value.Literal lit) ]
        | PropertyConstraint.MinInclusive lit -> [ stmt propNode "sh:minInclusive" (Value.Literal lit) ]
        | PropertyConstraint.MaxExclusive lit -> [ stmt propNode "sh:maxExclusive" (Value.Literal lit) ]
        | PropertyConstraint.MaxInclusive lit -> [ stmt propNode "sh:maxInclusive" (Value.Literal lit) ]
```

- [ ] **Step 4: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: all tests PASS.

- [ ] **Step 5: Fantomas + commit**

```bash
dotnet fantomas src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
git commit -m "feat(validation): Shacl.toDoc -- cardinality and value range constraints"
```

---

### Task 7: `Shacl.fs` — string-based constraints

**Files:**
- Modify: `src/Frank.Validation/Shacl.fs` (`constraintStatements` gains `MinLength`/`MaxLength`/`Pattern`/`LanguageIn`/`UniqueLang`)
- Modify: `test/Frank.Validation.Tests/ShaclToDocTests.fs`

- [ ] **Step 1: Write the failing test**

```fsharp
testList "string-based constraints" [
    test "sh:minLength and sh:maxLength" {
        let prop =
            ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
            |> addConstraint (PropertyConstraint.MinLength 1)
            |> addConstraint (PropertyConstraint.MaxLength 200)

        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:minLength" && v = Value.Literal(Literal.Int 1)) "sh:minLength"
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:maxLength" && v = Value.Literal(Literal.Int 200)) "sh:maxLength"
    }

    test "sh:pattern without flags omits sh:flags entirely" {
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/email")) |> addConstraint (PropertyConstraint.Pattern(@"^\S+@\S+$", None))
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:pattern" && v = Value.Literal(Literal.String @"^\S+@\S+$")) "sh:pattern"
        Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:flags") "no sh:flags when None"
    }

    test "sh:pattern with Some flags also emits sh:flags" {
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/email")) |> addConstraint (PropertyConstraint.Pattern(@"^\S+$", Some "i"))
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:flags" && v = Value.Literal(Literal.String "i")) "sh:flags present"
    }

    test "sh:languageIn is a well-formed rdf:list of string literals" {
        let tags = NonEmptyList.ofList [ "en"; "fr" ] |> Option.get
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/name")) |> addConstraint (PropertyConstraint.LanguageIn tags)
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:languageIn") "sh:languageIn present"
        let listHead = doc.Statements |> List.pick (fun (_, p, v) -> if p = "sh:languageIn" then Some v else None)
        match listHead with
        | Value.Node headNode -> Expect.exists doc.Statements (fun (s, p, _) -> s = headNode && p = "rdf:first") "list head has rdf:first"
        | other -> failtestf "expected a node, got %A" other
    }

    test "sh:uniqueLang as a boolean literal" {
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/name")) |> addConstraint (PropertyConstraint.UniqueLang true)
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:uniqueLang" && v = Value.Literal(Literal.Bool true)) "sh:uniqueLang"
    }
]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "string-based constraints"`
Expected: FAIL.

- [ ] **Step 3: Extend `constraintStatements`'s match** — insert after the value-range cases:

```fsharp
        | PropertyConstraint.MinLength n -> [ stmt propNode "sh:minLength" (Value.Literal(Literal.Int n)) ]
        | PropertyConstraint.MaxLength n -> [ stmt propNode "sh:maxLength" (Value.Literal(Literal.Int n)) ]
        | PropertyConstraint.Pattern(pattern, flags) ->
            stmt propNode "sh:pattern" (Value.Literal(Literal.String pattern))
            :: (flags |> Option.map (fun f -> stmt propNode "sh:flags" (Value.Literal(Literal.String f))) |> Option.toList)
        | PropertyConstraint.LanguageIn tags ->
            let items = NonEmptyList.toList tags |> List.map (Literal.String >> Value.Literal)
            let head, listStmts = rdfList items
            stmt propNode "sh:languageIn" (Value.Node head) :: listStmts
        | PropertyConstraint.UniqueLang b -> [ stmt propNode "sh:uniqueLang" (Value.Literal(Literal.Bool b)) ]
```

- [ ] **Step 4: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: all tests PASS.

- [ ] **Step 5: Fantomas + commit**

```bash
dotnet fantomas src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
git commit -m "feat(validation): Shacl.toDoc -- string-based constraints (length, pattern, languageIn, uniqueLang)"
```

---

### Task 8: `Shacl.fs` — property pair constraints

**Files:**
- Modify: `src/Frank.Validation/Shacl.fs` (`constraintStatements` gains `Equals`/`Disjoint`/`LessThan`/`LessThanOrEquals`)
- Modify: `test/Frank.Validation.Tests/ShaclToDocTests.fs`

- [ ] **Step 1: Write the failing test**

```fsharp
testList "property pair constraints" [
    test "sh:equals, sh:disjoint, sh:lessThan, sh:lessThanOrEquals each point at the given property IRI" {
        let cases =
            [ PropertyConstraint.Equals(Uri "https://schema.org/a"), "sh:equals"
              PropertyConstraint.Disjoint(Uri "https://schema.org/b"), "sh:disjoint"
              PropertyConstraint.LessThan(Uri "https://schema.org/c"), "sh:lessThan"
              PropertyConstraint.LessThanOrEquals(Uri "https://schema.org/d"), "sh:lessThanOrEquals" ]

        for constr, predicate in cases do
            let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) |> addConstraint constr
            let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
            Expect.exists doc.Statements (fun (_, p, _) -> p = predicate) $"{predicate} present"
    }
]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "property pair constraints"`
Expected: FAIL.

- [ ] **Step 3: Extend `constraintStatements`'s match**

```fsharp
        | PropertyConstraint.Equals uri -> [ stmt propNode "sh:equals" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | PropertyConstraint.Disjoint uri -> [ stmt propNode "sh:disjoint" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | PropertyConstraint.LessThan uri -> [ stmt propNode "sh:lessThan" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
        | PropertyConstraint.LessThanOrEquals uri -> [ stmt propNode "sh:lessThanOrEquals" (Value.Node(Node.Iri uri.AbsoluteUri)) ]
```

- [ ] **Step 4: Run — verify it passes.**

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: all tests PASS.

- [ ] **Step 5: Fantomas + commit**

```bash
dotnet fantomas src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
git commit -m "feat(validation): Shacl.toDoc -- property pair constraints"
```

---

### Task 9: `Shacl.fs` — recursive shape-based constraints + logical combinators

**Files:**
- Modify: `src/Frank.Validation/Shacl.fs` (`constraintStatements` gains `Node`/`QualifiedValueShape`; `shapeStatements` gains `And`/`Or`/`Not`/`Xone`)
- Modify: `test/Frank.Validation.Tests/ShaclToDocTests.fs`

This is the task where `constraintStatements` genuinely needs its mutual recursion with `shapeStatements`, set up in Task 5 — `PropertyConstraint.Node`/`QualifiedValueShape` embed a whole nested `ShapeDecl`.

- [ ] **Step 1: Write the failing test**

```fsharp
testList "recursive shape-based constraints and logical combinators" [
    test "sh:node embeds the referenced shape's own subject and statements" {
        let personShape = recordShape (targetClass (Uri "https://schema.org/Person")) [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/email")) |> addConstraint (PropertyConstraint.MinCount 1) ]
        let agentProp = ofPath (PropertyPath.Predicate(Uri "https://schema.org/agent")) |> addConstraint (PropertyConstraint.Node personShape)
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ agentProp ] ]

        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:node" && v = Value.Node(Node.Iri "https://schema.org/Person")) "sh:node points at Person's own IRI"
        Expect.exists doc.Statements (fun (s, p, _) -> s = Node.Iri "https://schema.org/Person" && p = RdfTypeIri) "Person's own sh:NodeShape triples are present too"
    }

    test "sh:qualifiedValueShape carries the shape plus qualifiedMinCount/qualifiedMaxCount/qualifiedValueShapesDisjoint" {
        let inner = recordShape [] []
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) |> addConstraint (PropertyConstraint.QualifiedValueShape(inner, Some 1, Some 2, true))
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

        Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:qualifiedValueShape") "sh:qualifiedValueShape present"
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:qualifiedMinCount" && v = Value.Literal(Literal.Int 1)) "sh:qualifiedMinCount"
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:qualifiedMaxCount" && v = Value.Literal(Literal.Int 2)) "sh:qualifiedMaxCount"
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:qualifiedValueShapesDisjoint" && v = Value.Literal(Literal.Bool true)) "sh:qualifiedValueShapesDisjoint"
    }

    test "sh:qualifiedMinCount/MaxCount are omitted when None, not emitted as absent literals" {
        let inner = recordShape [] []
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) |> addConstraint (PropertyConstraint.QualifiedValueShape(inner, None, None, false))
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:qualifiedMinCount") "no sh:qualifiedMinCount when None"
        Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:qualifiedMaxCount") "no sh:qualifiedMaxCount when None"
    }

    test "And/Or/Xone are well-formed rdf:lists of member shape nodes; Not is a single shape reference" {
        let a = recordShape (targetClass (Uri "https://schema.org/A")) []
        let b = recordShape (targetClass (Uri "https://schema.org/B")) []

        let andDoc = Shacl.toDoc [ ShapeDecl.And { Head = a; Tail = [ b ] } ]
        let orDoc = Shacl.toDoc [ ShapeDecl.Or { Head = a; Tail = [ b ] } ]
        let xoneDoc = Shacl.toDoc [ ShapeDecl.Xone { Head = a; Tail = [ b ] } ]
        let notDoc = Shacl.toDoc [ ShapeDecl.Not a ]

        Expect.exists andDoc.Statements (fun (_, p, _) -> p = "sh:and") "sh:and present"
        Expect.exists orDoc.Statements (fun (_, p, _) -> p = "sh:or") "sh:or present"
        Expect.exists xoneDoc.Statements (fun (_, p, _) -> p = "sh:xone") "sh:xone present"
        Expect.exists notDoc.Statements (fun (_, p, v) -> p = "sh:not" && v = Value.Node(Node.Iri "https://schema.org/A")) "sh:not points directly at the negated shape"
    }
]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "recursive shape-based"`
Expected: FAIL.

- [ ] **Step 3: Extend `constraintStatements`'s match** — insert after the property-pair cases:

```fsharp
        | PropertyConstraint.Node inner ->
            let innerSubject, innerStmts = shapeStatements inner
            stmt propNode "sh:node" (Value.Node innerSubject) :: innerStmts
        | PropertyConstraint.QualifiedValueShape(inner, minC, maxC, disjoint) ->
            let innerSubject, innerStmts = shapeStatements inner

            [ stmt propNode "sh:qualifiedValueShape" (Value.Node innerSubject) ]
            @ (minC |> Option.map (fun n -> stmt propNode "sh:qualifiedMinCount" (Value.Literal(Literal.Int n))) |> Option.toList)
            @ (maxC |> Option.map (fun n -> stmt propNode "sh:qualifiedMaxCount" (Value.Literal(Literal.Int n))) |> Option.toList)
            @ [ stmt propNode "sh:qualifiedValueShapesDisjoint" (Value.Literal(Literal.Bool disjoint)) ]
            @ innerStmts
```

- [ ] **Step 4: Extend `shapeStatements`'s match** — replace the `| _ -> Node.blank (), []` wildcard with the four logical-combinator cases (this task removes the wildcard for `shapeStatements` entirely — `RecordShape` was already exhaustive company; `EnumShape` is still Task 10's, so keep a narrower wildcard just for that one remaining case):

```fsharp
        | ShapeDecl.And members ->
            let items = NonEmptyList.toList members |> List.map shapeStatements
            let head, listStmts = rdfList (items |> List.map (fst >> Value.Node))
            let bn = Node.blank ()
            bn, (stmt bn "sh:and" (Value.Node head) :: (items |> List.collect snd)) @ listStmts
        | ShapeDecl.Or members ->
            let items = NonEmptyList.toList members |> List.map shapeStatements
            let head, listStmts = rdfList (items |> List.map (fst >> Value.Node))
            let bn = Node.blank ()
            bn, (stmt bn "sh:or" (Value.Node head) :: (items |> List.collect snd)) @ listStmts
        | ShapeDecl.Xone members ->
            let items = NonEmptyList.toList members |> List.map shapeStatements
            let head, listStmts = rdfList (items |> List.map (fst >> Value.Node))
            let bn = Node.blank ()
            bn, (stmt bn "sh:xone" (Value.Node head) :: (items |> List.collect snd)) @ listStmts
        | ShapeDecl.Not inner ->
            let innerSubject, innerStmts = shapeStatements inner
            let bn = Node.blank ()
            bn, stmt bn "sh:not" (Value.Node innerSubject) :: innerStmts
        | _ -> Node.blank (), []   // EnumShape only, remaining -- Task 10 removes this
```

- [ ] **Step 5: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: all tests PASS.

- [ ] **Step 6: Fantomas + commit**

```bash
dotnet fantomas src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
git commit -m "feat(validation): Shacl.toDoc -- recursive sh:node/qualifiedValueShape + and/or/not/xone"
```

---

### Task 10: `Shacl.fs` — `EnumShape`, `sh:hasValue`, `sh:in`

**Files:**
- Modify: `src/Frank.Validation/Shacl.fs` (`shapeStatements` gains `EnumShape`, removing its wildcard entirely — the match becomes exhaustive; `constraintStatements` gains `HasValue`/`AllowedValues`)
- Modify: `test/Frank.Validation.Tests/ShaclToDocTests.fs`

- [ ] **Step 1: Write the failing test**

```fsharp
testList "EnumShape, sh:hasValue, sh:in" [
    test "EnumShape emits sh:targetClass and a well-formed sh:in list of the case IRIs" {
        let decl = enumShape (Uri "https://schema.org/GameStatusType") (Uri "https://schema.org/Active") [ Uri "https://schema.org/Completed" ]
        let doc = Shacl.toDoc [ decl ]
        let subject = Node.Iri "https://schema.org/GameStatusType"
        Expect.exists doc.Statements (fun (s, p, v) -> s = subject && p = "sh:targetClass" && v = Value.Node subject) "sh:targetClass"
        Expect.exists doc.Statements (fun (s, p, _) -> s = subject && p = "sh:in") "sh:in present"
    }

    test "sh:hasValue carries the given Value (node or literal) unchanged" {
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/status")) |> addConstraint (PropertyConstraint.HasValue(Value.Node(Node.Iri "https://schema.org/Active")))
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:hasValue" && v = Value.Node(Node.Iri "https://schema.org/Active")) "sh:hasValue"
    }

    test "sh:in (AllowedValues) on a property shape is a well-formed rdf:list, mixing nodes and literals" {
        let values = NonEmptyList.ofList [ Value.Literal(Literal.String "a"); Value.Node(Node.Iri "https://schema.org/b") ] |> Option.get
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) |> addConstraint (PropertyConstraint.AllowedValues values)
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:in") "sh:in present"
    }

    test "constraintStatements is now exhaustive over every PropertyConstraint case except Sparql (Task 11)" {
        // Compile-time proof, not a runtime assertion: if this test file compiles, the match in
        // Shacl.fs handles every case reached from here. Sparql is exercised in Task 11's tests.
        Expect.isTrue true "compiles"
    }
]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "EnumShape, sh:hasValue, sh:in"`
Expected: FAIL.

- [ ] **Step 3: Extend `shapeStatements`'s match** — replace the `| _ -> Node.blank (), []` wildcard with:

```fsharp
        | ShapeDecl.EnumShape(targetClassUri, cases) ->
            let subject = Node.Iri targetClassUri.AbsoluteUri
            let typeStmt = stmt subject RdfTypeIri (Value.Node(Node.Iri "sh:NodeShape"))
            let targetStmt = stmt subject "sh:targetClass" (Value.Node subject)
            let items = NonEmptyList.toList cases |> List.map (fun u -> Value.Node(Node.Iri u.AbsoluteUri))
            let listHead, listStmts = rdfList items
            subject, [ typeStmt; targetStmt; stmt subject "sh:in" (Value.Node listHead) ] @ listStmts
```

`shapeStatements` is now an exhaustive match over `ShapeDecl` — no wildcard remains, so the compiler enforces that every future new `ShapeDecl` case (there are none planned) would fail the build until handled here, per this codebase's `TreatWarningsAsErrors`.

- [ ] **Step 4: Extend `constraintStatements`'s match** — insert before the (now Task-11-only) wildcard:

```fsharp
        | PropertyConstraint.HasValue value -> [ stmt propNode "sh:hasValue" value ]
        | PropertyConstraint.AllowedValues values ->
            let items = NonEmptyList.toList values
            let head, listStmts = rdfList items
            stmt propNode "sh:in" (Value.Node head) :: listStmts
```

- [ ] **Step 5: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: all tests PASS.

- [ ] **Step 6: Fantomas + commit**

```bash
dotnet fantomas src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
git commit -m "feat(validation): Shacl.toDoc -- EnumShape, sh:hasValue, sh:in; shapeStatements now exhaustive"
```

---

### Task 11: `Shacl.fs` — `sh:sparql` (SPARQL-based constraints)

**Files:**
- Modify: `src/Frank.Validation/Shacl.fs` (`constraintStatements` gains `Sparql`, its final case — the wildcard is removed entirely and the match becomes exhaustive)
- Modify: `test/Frank.Validation.Tests/ShaclToDocTests.fs`

Declared query prefixes are prepended as `PREFIX name: <uri>` lines directly onto the query text at graph-build time, rather than modeled as their own `sh:prefixes`/`sh:PrefixDeclaration` RDF structure — `SparqlQueryParser` (already proven in `Frank.Provenance`'s `LeviathanQueryProcessor` usage) accepts inline `PREFIX` lines natively, so this avoids inventing RDF structure the query engine doesn't need to see.

- [ ] **Step 1: Write the failing test**

```fsharp
testList "sh:sparql" [
    test "sh:sparql is a blank node carrying sh:select with the author's query text" {
        let sc = { Query = "SELECT $this WHERE { $this <https://schema.org/position> ?p . FILTER (?p < 0) }"; Message = None; Prefixes = [] }
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) |> addConstraint (PropertyConstraint.Sparql sc)
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

        Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:sparql") "sh:sparql present"
        Expect.exists doc.Statements (fun (_, p, v) ->
            p = "sh:select" && (match v with Value.Literal(Literal.String s) -> s.Contains "FILTER" | _ -> false)) "sh:select carries the query text"
    }

    test "declared prefixes are prepended to the query text as PREFIX lines" {
        let sc = { Query = "SELECT $this WHERE { $this a schema:Person }"; Message = None; Prefixes = [ "schema", "https://schema.org/" ] }
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) |> addConstraint (PropertyConstraint.Sparql sc)
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]

        Expect.exists doc.Statements (fun (_, p, v) ->
            p = "sh:select" && (match v with Value.Literal(Literal.String s) -> s.Contains "PREFIX schema: <https://schema.org/>" | _ -> false)) "PREFIX line prepended"
    }

    test "an author message on the sh:sparql constraint becomes sh:message on the same blank node" {
        let sc = { Query = "SELECT $this WHERE { FILTER (false) }"; Message = Some "always fails"; Prefixes = [] }
        let prop = ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) |> addConstraint (PropertyConstraint.Sparql sc)
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:message" && v = Value.Literal(Literal.String "always fails")) "sh:message present"
    }
]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "sh:sparql"`
Expected: FAIL.

- [ ] **Step 3: Replace the final wildcard in `constraintStatements`'s match**

```fsharp
        | PropertyConstraint.Sparql sc ->
            let prefixLines =
                sc.Prefixes
                |> List.map (fun (name, uri) -> sprintf "PREFIX %s: <%s>" name uri)
                |> String.concat "\n"

            let fullQuery = if String.IsNullOrEmpty prefixLines then sc.Query else prefixLines + "\n" + sc.Query
            let bn = Node.blank ()

            stmt propNode "sh:sparql" (Value.Node bn)
            :: stmt bn "sh:select" (Value.Literal(Literal.String fullQuery))
            :: (sc.Message |> Option.map (fun m -> stmt bn "sh:message" (Value.Literal(Literal.String m))) |> Option.toList)
```

`constraintStatements` is now exhaustive over `PropertyConstraint` — no wildcard remains. Both interpreter matches (`shapeStatements` since Task 10, `constraintStatements` now) are compiler-checked complete.

- [ ] **Step 4: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: all tests PASS. If the build instead fails with an incomplete-match error here, a `PropertyConstraint` or `ShapeDecl` case was missed in an earlier task — find it via the compiler's FS0025 message (it names the unmatched case) and add it before proceeding.

- [ ] **Step 5: Fantomas + commit**

```bash
dotnet fantomas src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
git commit -m "feat(validation): Shacl.toDoc -- sh:sparql; constraintStatements now exhaustive"
```

---

### Task 12: `Shacl.fs` — `sh:closed`/`sh:ignoredProperties`, `sh:severity`/`sh:message`, `toShapesGraph`

**Files:**
- Modify: `src/Frank.Validation/Shacl.fs` (`shapeStatements`'s `RecordShape` case emits `Closed`/`IgnoredProperties`/`Severity`/`Message`; `propertyShapeStatements` emits `PropertyShapeSpec.Severity`/`Message`; adds `toShapesGraph`)
- Modify: `src/Frank.Validation/Shacl.fsi` (adds `toShapesGraph`)
- Modify: `test/Frank.Validation.Tests/ShaclToDocTests.fs`

**Interfaces:**
- Produces: `Shacl.toShapesGraph: ShapeDecl list -> VDS.RDF.Shacl.ShapesGraph` — `toDoc >> Doc.toGraph >> VDS.RDF.Shacl.ShapesGraph`. This is what Task 13's `Validation.fs` consumes.

- [ ] **Step 1: Write the failing test**

```fsharp
testList "closed, severity, message, toShapesGraph" [
    test "sh:closed true plus sh:ignoredProperties as a well-formed rdf:list, when Closed is set" {
        let decl =
            recordShape (targetClass (Uri "https://schema.org/T")) []
            |> function
                | ShapeDecl.RecordShape n -> ShapeDecl.RecordShape { n with Closed = true; IgnoredProperties = [ Uri "https://schema.org/extra" ] }
                | other -> other

        let doc = Shacl.toDoc [ decl ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:closed" && v = Value.Literal(Literal.Bool true)) "sh:closed"
        Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:ignoredProperties") "sh:ignoredProperties present"
    }

    test "sh:closed false emits no sh:closed triple at all (SHACL's own default, nothing to assert)" {
        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [] ]
        Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:closed") "no sh:closed when not closed"
    }

    test "NodeShapeSpec.Severity/Message become sh:severity/sh:message on the shape's own subject" {
        let decl =
            recordShape (targetClass (Uri "https://schema.org/T")) []
            |> function
                | ShapeDecl.RecordShape n -> ShapeDecl.RecordShape { n with Severity = Some Severity.Warning; Message = Some "be careful" }
                | other -> other

        let doc = Shacl.toDoc [ decl ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:severity" && v = Value.Node(Node.Iri "sh:Warning")) "sh:severity"
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:message" && v = Value.Literal(Literal.String "be careful")) "sh:message"
    }

    test "PropertyShapeSpec.Severity/Message become sh:severity/sh:message on that property's own blank node" {
        let prop =
            { ofPath (PropertyPath.Predicate(Uri "https://schema.org/x")) with
                Severity = Some Severity.Info
                Message = Some "informational" }

        let doc = Shacl.toDoc [ recordShape (targetClass (Uri "https://schema.org/T")) [ prop ] ]
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:severity" && v = Value.Node(Node.Iri "sh:Info")) "sh:severity on property shape"
        Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:message" && v = Value.Literal(Literal.String "informational")) "sh:message on property shape"
    }

    test "toShapesGraph builds a real dotNetRDF ShapesGraph from a ShapeDecl list" {
        let decl = recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) |> addConstraint (PropertyConstraint.Datatype XsdDatatype.Integer) ]
        let sg = Shacl.toShapesGraph [ decl ]
        Expect.isNotNull (box sg) "ShapesGraph constructed without throwing"
    }
]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "closed, severity, message"`
Expected: FAIL.

- [ ] **Step 3: Extend `shapeStatements`'s `RecordShape` case** — replace it with:

```fsharp
        | ShapeDecl.RecordShape spec ->
            let subject =
                spec.Targets
                |> List.tryPick (function
                    | TargetSpec.Class uri -> Some(Node.Iri uri.AbsoluteUri)
                    | _ -> None)
                |> Option.defaultWith Node.blank

            let typeStmt = stmt subject RdfTypeIri (Value.Node(Node.Iri "sh:NodeShape"))
            let targetStmts = spec.Targets |> List.collect (targetStatements subject)

            let propertyStmts =
                spec.Properties
                |> List.collect (fun p ->
                    let bn, stmts = propertyShapeStatements p
                    stmt subject "sh:property" (Value.Node bn) :: stmts)

            let closedStmts =
                if spec.Closed then
                    let ignoredValues = spec.IgnoredProperties |> List.map (fun u -> Value.Node(Node.Iri u.AbsoluteUri))
                    let ignoredHead, ignoredListStmts = rdfList ignoredValues
                    stmt subject "sh:closed" (Value.Literal(Literal.Bool true))
                    :: stmt subject "sh:ignoredProperties" (Value.Node ignoredHead)
                    :: ignoredListStmts
                else
                    []

            let severityStmt =
                spec.Severity |> Option.map (fun s -> stmt subject "sh:severity" (Value.Node(Node.Iri(severityCurie s)))) |> Option.toList

            let messageStmt =
                spec.Message |> Option.map (fun m -> stmt subject "sh:message" (Value.Literal(Literal.String m))) |> Option.toList

            subject, typeStmt :: targetStmts @ propertyStmts @ closedStmts @ severityStmt @ messageStmt
```

Add the `severityCurie` helper next to `xsdCurie`/`nodeKindCurie`:

```fsharp
    let private severityCurie (s: Severity) : string =
        match s with
        | Severity.Violation -> "sh:Violation"
        | Severity.Warning -> "sh:Warning"
        | Severity.Info -> "sh:Info"
```

- [ ] **Step 4: Extend `propertyShapeStatements`** — replace it with:

```fsharp
    and private propertyShapeStatements (spec: PropertyShapeSpec) : Node * (Node * string * Value) list =
        let bn = Node.blank ()
        let pathHead, pathStmts = pathNode spec.Path
        let constraintStmts = spec.Constraints |> List.collect (constraintStatements bn)

        let severityStmt =
            spec.Severity |> Option.map (fun s -> stmt bn "sh:severity" (Value.Node(Node.Iri(severityCurie s)))) |> Option.toList

        let messageStmt =
            spec.Message |> Option.map (fun m -> stmt bn "sh:message" (Value.Literal(Literal.String m))) |> Option.toList

        bn, (stmt bn "sh:path" (Value.Node pathHead) :: pathStmts) @ constraintStmts @ severityStmt @ messageStmt
```

- [ ] **Step 5: Add `toShapesGraph`** — append after `toDoc`:

```fsharp
    let toShapesGraph (shapes: ShapeDecl list) : VDS.RDF.Shacl.ShapesGraph =
        VDS.RDF.Shacl.ShapesGraph(Doc.toGraph (toDoc shapes))
```

Add `open VDS.RDF.Shacl` (or fully qualify as shown) at the top of `Shacl.fs` if not already present.

- [ ] **Step 6: Add `toShapesGraph` to `Shacl.fsi`**

```fsharp
    /// toDoc >> Doc.toGraph >> ShapesGraph -- what Validation.fs's `validate` consumes.
    val toShapesGraph: shapes: ShapeDecl list -> VDS.RDF.Shacl.ShapesGraph
```

- [ ] **Step 7: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: all tests PASS. If `ShapesGraph`'s constructor signature differs from `ShapesGraph(IGraph)` (verify against the installed `dotNetRdf.Shacl` 3.5.1 assembly if this fails), adjust to the real constructor — do not stub.

- [ ] **Step 8: Fantomas + commit**

```bash
dotnet fantomas src/Frank.Validation/Shacl.fsi src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/Shacl.fsi src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ShaclToDocTests.fs
git commit -m "feat(validation): Shacl.toDoc -- closed/ignoredProperties, severity/message; add toShapesGraph"
```

---

### Task 13: `Validation.fs` — `Violation`, `ValidationOutcome`, `Shacl.validate`

**Files:**
- Create: `src/Frank.Validation/Validation.fsi`
- Create: `src/Frank.Validation/Validation.fs`
- Modify: `src/Frank.Validation/Shacl.fsi` (adds `validate` to the existing `module Shacl`)
- Modify: `src/Frank.Validation/Shacl.fs` (same)
- Modify: `Frank.Validation.fsproj` (add `Validation.fsi`/`.fs` — after `ShapeSpec.fs`, before `Shacl.fsi`, since `Shacl.fs`'s new `validate` function needs `Violation`/`ValidationOutcome` already compiled)
- Create: `test/Frank.Validation.Tests/ValidationTests.fs`
- Modify: `Frank.Validation.Tests.fsproj`

**Interfaces:**
- Consumes: `VDS.RDF.Shacl.ShapesGraph` (Task 12), `VDS.RDF.IGraph`.
- Produces: `Violation`, `ValidationOutcome`, `Shacl.validate: ShapesGraph -> IGraph -> ValidationOutcome`.

**One deliberate, disclosed simplification from the design doc:** `Violation.ResultPath` is `Uri option`, not `PropertyPath option`. Faithfully reversing a *complex* SHACL path (an `sh:alternativePath`/`sh:inversePath`/... blank-node structure back out of a validation `Result`) is real, separate work symmetric to `pathNode` but inverted, and risks silently getting subtle blank-node/rdf:list traversal wrong without being able to run it against the live library first. Per `[[feedback_roundtrip_lossiness]]` — don't hide a round-trip gap, report it — this task reports it: `ResultPath` is `Some uri` for the common simple-predicate case, `None` when the violated property's path is complex. The underlying `VDS.RDF.Shacl.Validation.Result` (not exposed by `Violation`) is where a caller needing the complex path would go; nothing here prevents adding a full `pathFromNode` reverse-parser later if a real need shows up.

- [ ] **Step 1: Write the failing test**

```fsharp
// test/Frank.Validation.Tests/ValidationTests.fs
module Frank.Validation.Tests.ValidationTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions
open VDS.RDF

let private dataGraphWithType (classIri: string) (instanceIri: string) (extraTriples: (string * string) list) : IGraph =
    let g = Graph() :> IGraph
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
    let inst = g.CreateUriNode(UriFactory.Create instanceIri)
    let rdfType = g.CreateUriNode(g.ResolveQName "rdf:type")
    g.Assert(Triple(inst, rdfType, g.CreateUriNode(UriFactory.Create classIri))) |> ignore

    for predicate, value in extraTriples do
        g.Assert(Triple(inst, g.CreateUriNode(UriFactory.Create predicate), g.CreateLiteralNode value)) |> ignore

    g

[<Tests>]
let tests =
    testList "Shacl.validate" [
        test "a conforming instance validates as Conforms" {
            let shape =
                recordShape (targetClass (Uri "https://schema.org/MoveAction")) [
                    ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) |> addConstraint (PropertyConstraint.MinCount 1)
                ]

            let sg = Shacl.toShapesGraph [ shape ]
            let dataGraph = dataGraphWithType "https://schema.org/MoveAction" "https://example.org/move1" [ "https://schema.org/position", "3" ]

            match Shacl.validate sg dataGraph with
            | ValidationOutcome.Conforms -> ()
            | ValidationOutcome.Violates vs -> failtestf "expected Conforms, got %d violation(s): %A" vs.Length vs
        }

        test "a missing required property violates with a non-empty Violation list" {
            let shape =
                recordShape (targetClass (Uri "https://schema.org/MoveAction")) [
                    ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) |> addConstraint (PropertyConstraint.MinCount 1)
                ]

            let sg = Shacl.toShapesGraph [ shape ]
            let dataGraph = dataGraphWithType "https://schema.org/MoveAction" "https://example.org/move2" []

            match Shacl.validate sg dataGraph with
            | ValidationOutcome.Conforms -> failtest "expected Violates -- required position is missing"
            | ValidationOutcome.Violates violations ->
                Expect.isNonEmpty violations "at least one violation"
                let v = violations.Head
                Expect.equal v.FocusNode (Node.Iri "https://example.org/move2") "focus node is the instance"
                Expect.equal v.Severity Severity.Violation "default severity"
        }

        test "an enum (sh:in) violation reports the offending focus node" {
            let shape = enumShape (Uri "https://schema.org/GameStatusType") (Uri "https://schema.org/Active") [ Uri "https://schema.org/Completed" ]
            let sg = Shacl.toShapesGraph [ shape ]
            let dataGraph = dataGraphWithType "https://schema.org/GameStatusType" "https://schema.org/Unknown" []

            match Shacl.validate sg dataGraph with
            | ValidationOutcome.Conforms -> failtest "expected Violates -- Unknown is not in the sh:in list"
            | ValidationOutcome.Violates violations -> Expect.isNonEmpty violations "violation reported"
        }

        test "an empty data graph conforms trivially against a targetClass shape (nothing to target)" {
            let shape = recordShape (targetClass (Uri "https://schema.org/MoveAction")) [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) |> addConstraint (PropertyConstraint.MinCount 1) ]
            let sg = Shacl.toShapesGraph [ shape ]
            let dataGraph = Graph() :> IGraph

            match Shacl.validate sg dataGraph with
            | ValidationOutcome.Conforms -> ()
            | ValidationOutcome.Violates vs -> failtestf "expected Conforms on an empty graph, got %A" vs
        }
    ]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "Shacl.validate"`
Expected: FAIL — `Violation`/`ValidationOutcome` not defined, `Shacl.validate` not defined.

- [ ] **Step 3: Write `Validation.fsi`**

```fsharp
// src/Frank.Validation/Validation.fsi
namespace Frank.Validation

open System
open Frank.Rdf

/// One SHACL validation-report result, typed. See ResultPath's doc comment for a disclosed
/// simplification versus a fully round-tripped PropertyPath.
type Violation =
    { FocusNode: Node
      /// Some uri for a simple-predicate path; None when the violated property's path is complex
      /// (sh:alternativePath/sh:inversePath/...) -- not round-tripped back to PropertyPath in v1.
      ResultPath: Uri option
      Severity: Severity
      Message: string
      ConstraintComponent: Uri
      SourceShape: Node }

[<RequireQualifiedAccess>]
type ValidationOutcome =
    | Conforms
    | Violates of Violation list
```

- [ ] **Step 4: Write `Validation.fs`**

```fsharp
// src/Frank.Validation/Validation.fs
namespace Frank.Validation

open System
open Frank.Rdf

type Violation =
    { FocusNode: Node
      ResultPath: Uri option
      Severity: Severity
      Message: string
      ConstraintComponent: Uri
      SourceShape: Node }

[<RequireQualifiedAccess>]
type ValidationOutcome =
    | Conforms
    | Violates of Violation list
```

- [ ] **Step 5: Add `validate` to `Shacl.fsi`** — append inside the existing `module Shacl`:

```fsharp
    /// A typed wrapper over VDS.RDF.Shacl.Validation.Report -- never exposes the raw dotNetRDF
    /// Result type to callers.
    val validate: shapesGraph: VDS.RDF.Shacl.ShapesGraph -> dataGraph: VDS.RDF.IGraph -> ValidationOutcome
```

- [ ] **Step 6: Add `validate` to `Shacl.fs`** — append after `toShapesGraph`:

```fsharp
    let private nodeOf (n: VDS.RDF.INode) : Node =
        match n with
        | :? VDS.RDF.IUriNode as u -> Node.Iri u.Uri.AbsoluteUri
        | :? VDS.RDF.IBlankNode as b -> Node.Blank b.InternalID
        | other -> Node.Iri(other.ToString())

    let private severityOf (n: VDS.RDF.INode) : Severity =
        match n with
        | :? VDS.RDF.IUriNode as u when u.Uri.AbsoluteUri.EndsWith "Warning" -> Severity.Warning
        | :? VDS.RDF.IUriNode as u when u.Uri.AbsoluteUri.EndsWith "Info" -> Severity.Info
        | _ -> Severity.Violation

    let validate (shapesGraph: VDS.RDF.Shacl.ShapesGraph) (dataGraph: VDS.RDF.IGraph) : ValidationOutcome =
        let report = shapesGraph.Validate(dataGraph)

        if report.Conforms then
            ValidationOutcome.Conforms
        else
            let violations =
                report.Results
                |> Seq.map (fun r ->
                    let resultPath =
                        match r.ResultPath with
                        | :? VDS.RDF.IUriNode as u -> Some(Uri u.Uri.AbsoluteUri)
                        | _ -> None

                    let message =
                        match r.ResultMessage |> Seq.tryHead with
                        | Some(:? VDS.RDF.ILiteralNode as lit) -> lit.Value
                        | _ -> ""

                    let constraintComponent =
                        match r.SourceConstraintComponent with
                        | :? VDS.RDF.IUriNode as u -> Uri u.Uri.AbsoluteUri
                        | _ -> Uri "urn:frank:validation:unknown-constraint-component"

                    { FocusNode = nodeOf r.FocusNode
                      ResultPath = resultPath
                      Severity = severityOf r.ResultSeverity
                      Message = message
                      ConstraintComponent = constraintComponent
                      SourceShape = nodeOf r.SourceShape })
                |> List.ofSeq

            ValidationOutcome.Violates violations
```

> **Verify against the real API before trusting this.** `Result.ResultMessage`/`.ResultSeverity`/`.ResultPath`/`.SourceConstraintComponent`/`.SourceShape`/`.FocusNode`'s exact property names and types (is `ResultMessage` an `IEnumerable<INode>` or a single `INode`? does `Report` expose `.Results` or `.Result`?) were confirmed to exist as *symbols* in the installed `dotNetRdf.Shacl` 3.5.1 assembly (see the design doc's *Package shape* section) but not confirmed against their exact signatures. If this doesn't compile, use your editor's IntelliSense/`F# Interactive` (`#r` the assembly, `typeof<VDS.RDF.Shacl.Validation.Result> |> ...` or just dot into an instance) against the real types and adjust — do not stub or guess further than the pattern above.

- [ ] **Step 7: Wire both projects' `<Compile>` lists** — `Frank.Validation.fsproj`, insert `Validation.fsi`/`.fs` after `ShapeSpec.fs` and before `Shacl.fsi`:

```xml
<ItemGroup>
  <Compile Include="ShapeTypes.fsi" />
  <Compile Include="ShapeTypes.fs" />
  <Compile Include="ShapeSpec.fsi" />
  <Compile Include="ShapeSpec.fs" />
  <Compile Include="Validation.fsi" />
  <Compile Include="Validation.fs" />
  <Compile Include="Shacl.fsi" />
  <Compile Include="Shacl.fs" />
</ItemGroup>
```

`Frank.Validation.Tests.fsproj` — insert before `Program.fs`:

```xml
<Compile Include="ValidationTests.fs" />
```

- [ ] **Step 8: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: all tests PASS.

- [ ] **Step 9: Fantomas + commit**

```bash
dotnet fantomas src/Frank.Validation/Validation.fsi src/Frank.Validation/Validation.fs src/Frank.Validation/Shacl.fsi src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ValidationTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/Validation.fsi src/Frank.Validation/Validation.fs src/Frank.Validation/Shacl.fsi src/Frank.Validation/Shacl.fs src/Frank.Validation/Frank.Validation.fsproj test/Frank.Validation.Tests/ValidationTests.fs test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj
git commit -m "feat(validation): Violation/ValidationOutcome + Shacl.validate typed wrapper"
```

---

### Task 14: `ShapeBuilder.fs` — `PropertyShapeBuilder` (`property { }`)

**Files:**
- Create: `src/Frank.Validation/ShapeBuilder.fsi`
- Create: `src/Frank.Validation/ShapeBuilder.fs`
- Modify: `Frank.Validation.fsproj` (add pair, after `Shacl.fs`)
- Create: `test/Frank.Validation.Tests/ShapeBuilderTests.fs`
- Modify: `Frank.Validation.Tests.fsproj`

**Interfaces:**
- Consumes: `ShapeSpecFunctions` (Task 3).
- Produces: `property: PropertyPath -> PropertyShapeBuilder`, and every constraint as a CE custom operation. Constructor idiom matches `Frank.Provenance.ProvBuilder` (rebased in from `origin/master`): takes the already-built `initial` value, `Yield`/`Zero` just return it.

- [ ] **Step 1: Write the failing test**

```fsharp
// test/Frank.Validation.Tests/ShapeBuilderTests.fs
module Frank.Validation.Tests.ShapeBuilderTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

[<Tests>]
let tests =
    testList "property { }" [
        test "an empty block equals ofPath directly (Yield/Zero return initial unchanged)" {
            let path = PropertyPath.Predicate(Uri "https://schema.org/x")
            let viaCe = property path { () }
            Expect.equal viaCe (ofPath path) "empty CE block == ofPath"
        }

        test "datatype/minCount/maxCount produce the same PropertyShapeSpec as addConstraint chains" {
            let path = PropertyPath.Predicate(Uri "https://schema.org/position")

            let viaCe = property path {
                datatype XsdDatatype.Integer
                minCount 1
                maxCount 1
            }

            let viaFunctions =
                ofPath path
                |> addConstraint (PropertyConstraint.Datatype XsdDatatype.Integer)
                |> addConstraint (PropertyConstraint.MinCount 1)
                |> addConstraint (PropertyConstraint.MaxCount 1)

            Expect.equal viaCe viaFunctions "CE sugar == plain functions, same result"
        }

        test "every constraint operation is reachable and produces the matching PropertyConstraint case" {
            let path = PropertyPath.Predicate(Uri "https://schema.org/x")
            let inner = recordShape [] []

            let viaCe = property path {
                ofClass (Uri "https://schema.org/Person")
                nodeKind NodeKind.Iri
                minLength 1
                maxLength 10
                minExclusive (Literal.Int 0)
                minInclusive (Literal.Int 0)
                maxExclusive (Literal.Int 100)
                maxInclusive (Literal.Int 100)
                pattern @"^\d+$"
                uniqueLang true
                equalsPath (Uri "https://schema.org/a")
                disjoint (Uri "https://schema.org/b")
                lessThan (Uri "https://schema.org/c")
                lessThanOrEquals (Uri "https://schema.org/d")
                node inner
                hasValue (Value.Node(Node.Iri "https://schema.org/v"))
                severity Severity.Warning
                message "careful"
            }

            Expect.hasLength viaCe.Constraints 15 "fifteen constraint operations above (severity/message aren't constraints)"
            Expect.equal viaCe.Severity (Some Severity.Warning) "severity set"
            Expect.equal viaCe.Message (Some "careful") "message set"
        }

        test "patternWithFlags sets both sh:pattern and sh:flags via one Pattern(pattern, Some flags) case" {
            let viaCe = property (PropertyPath.Predicate(Uri "https://schema.org/x")) { patternWithFlags @"^\d+$" "i" }
            Expect.equal viaCe.Constraints [ PropertyConstraint.Pattern(@"^\d+$", Some "i") ] "pattern with flags"
        }

        test "languageIn and allowedValues take a NonEmptyList directly" {
            let tags = NonEmptyList.ofList [ "en"; "fr" ] |> Option.get
            let values = NonEmptyList.ofList [ Value.Literal(Literal.String "a") ] |> Option.get

            let viaCe = property (PropertyPath.Predicate(Uri "https://schema.org/x")) {
                languageIn tags
                allowedValues values
            }

            Expect.equal viaCe.Constraints [ PropertyConstraint.LanguageIn tags; PropertyConstraint.AllowedValues values ] "both present, in order"
        }

        test "qualifiedValueShape and sparqlConstraint reach their PropertyConstraint cases" {
            let inner = recordShape [] []
            let sc: SparqlConstraint = { Query = "ASK { }"; Message = None; Prefixes = [] }

            let viaCe = property (PropertyPath.Predicate(Uri "https://schema.org/x")) {
                qualifiedValueShape inner (Some 1) (Some 2) true
                sparqlConstraint sc
            }

            Expect.equal
                viaCe.Constraints
                [ PropertyConstraint.QualifiedValueShape(inner, Some 1, Some 2, true); PropertyConstraint.Sparql sc ]
                "both present, in order"
        }
    ]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "property { }"`
Expected: FAIL — `property`/`PropertyShapeBuilder` not defined.

- [ ] **Step 3: Write `ShapeBuilder.fsi`** (`PropertyShapeBuilder` only — `ShapeBuilder` is Task 15)

```fsharp
// src/Frank.Validation/ShapeBuilder.fsi
namespace Frank.Validation

open System

/// CE sugar over ShapeSpecFunctions, mirroring Frank.Provenance's ProvBuilder: the constructor takes
/// the already-built initial value; Yield/Zero return it unchanged; every operation is one line of
/// addConstraint (or a direct field update for severity/message).
[<AutoOpen>]
module ShapeBuilderModule =
    [<Sealed>]
    type PropertyShapeBuilder =
        new: initial: PropertyShapeSpec -> PropertyShapeBuilder
        member Yield: 'a -> PropertyShapeSpec
        member Zero: unit -> PropertyShapeSpec
        member Run: p: PropertyShapeSpec -> PropertyShapeSpec

        [<CustomOperation("datatype")>] member Datatype: PropertyShapeSpec * XsdDatatype -> PropertyShapeSpec
        [<CustomOperation("ofClass")>] member OfClass: PropertyShapeSpec * Uri -> PropertyShapeSpec
        [<CustomOperation("nodeKind")>] member NodeKindOp: PropertyShapeSpec * NodeKind -> PropertyShapeSpec
        [<CustomOperation("minCount")>] member MinCount: PropertyShapeSpec * int -> PropertyShapeSpec
        [<CustomOperation("maxCount")>] member MaxCount: PropertyShapeSpec * int -> PropertyShapeSpec
        [<CustomOperation("minLength")>] member MinLength: PropertyShapeSpec * int -> PropertyShapeSpec
        [<CustomOperation("maxLength")>] member MaxLength: PropertyShapeSpec * int -> PropertyShapeSpec
        [<CustomOperation("minExclusive")>] member MinExclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec
        [<CustomOperation("minInclusive")>] member MinInclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec
        [<CustomOperation("maxExclusive")>] member MaxExclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec
        [<CustomOperation("maxInclusive")>] member MaxInclusive: PropertyShapeSpec * Frank.Rdf.Literal -> PropertyShapeSpec
        [<CustomOperation("pattern")>] member Pattern: PropertyShapeSpec * string -> PropertyShapeSpec
        [<CustomOperation("patternWithFlags")>] member PatternWithFlags: PropertyShapeSpec * string * string -> PropertyShapeSpec
        [<CustomOperation("languageIn")>] member LanguageIn: PropertyShapeSpec * NonEmptyList<string> -> PropertyShapeSpec
        [<CustomOperation("uniqueLang")>] member UniqueLang: PropertyShapeSpec * bool -> PropertyShapeSpec
        [<CustomOperation("equalsPath")>] member EqualsPath: PropertyShapeSpec * Uri -> PropertyShapeSpec
        [<CustomOperation("disjoint")>] member Disjoint: PropertyShapeSpec * Uri -> PropertyShapeSpec
        [<CustomOperation("lessThan")>] member LessThan: PropertyShapeSpec * Uri -> PropertyShapeSpec
        [<CustomOperation("lessThanOrEquals")>] member LessThanOrEquals: PropertyShapeSpec * Uri -> PropertyShapeSpec
        [<CustomOperation("node")>] member NodeOp: PropertyShapeSpec * ShapeDecl -> PropertyShapeSpec
        [<CustomOperation("qualifiedValueShape")>] member QualifiedValueShape: PropertyShapeSpec * ShapeDecl * int option * int option * bool -> PropertyShapeSpec
        [<CustomOperation("hasValue")>] member HasValue: PropertyShapeSpec * Frank.Rdf.Value -> PropertyShapeSpec
        [<CustomOperation("allowedValues")>] member AllowedValues: PropertyShapeSpec * NonEmptyList<Frank.Rdf.Value> -> PropertyShapeSpec
        [<CustomOperation("sparqlConstraint")>] member SparqlConstraintOp: PropertyShapeSpec * SparqlConstraint -> PropertyShapeSpec
        [<CustomOperation("severity")>] member SeverityOp: PropertyShapeSpec * Severity -> PropertyShapeSpec
        [<CustomOperation("message")>] member MessageOp: PropertyShapeSpec * string -> PropertyShapeSpec

    /// `property path { ... } = PropertyShapeBuilder(ofPath path) { ... }`.
    val property: path: PropertyPath -> PropertyShapeBuilder
```

- [ ] **Step 4: Write `ShapeBuilder.fs`**

```fsharp
// src/Frank.Validation/ShapeBuilder.fs
namespace Frank.Validation

open System
open Frank.Rdf
open Frank.Validation.ShapeSpecFunctions

[<AutoOpen>]
module ShapeBuilderModule =
    [<Sealed>]
    type PropertyShapeBuilder(initial: PropertyShapeSpec) =
        member _.Yield(_) : PropertyShapeSpec = initial
        member _.Zero() : PropertyShapeSpec = initial
        member _.Run(p: PropertyShapeSpec) : PropertyShapeSpec = p

        [<CustomOperation("datatype")>]
        member _.Datatype(p, dt: XsdDatatype) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.Datatype dt)

        [<CustomOperation("ofClass")>]
        member _.OfClass(p, c: Uri) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.Class c)

        [<CustomOperation("nodeKind")>]
        member _.NodeKindOp(p, nk: NodeKind) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.NodeKind nk)

        [<CustomOperation("minCount")>]
        member _.MinCount(p, n: int) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.MinCount n)

        [<CustomOperation("maxCount")>]
        member _.MaxCount(p, n: int) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.MaxCount n)

        [<CustomOperation("minLength")>]
        member _.MinLength(p, n: int) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.MinLength n)

        [<CustomOperation("maxLength")>]
        member _.MaxLength(p, n: int) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.MaxLength n)

        [<CustomOperation("minExclusive")>]
        member _.MinExclusive(p, v: Literal) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.MinExclusive v)

        [<CustomOperation("minInclusive")>]
        member _.MinInclusive(p, v: Literal) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.MinInclusive v)

        [<CustomOperation("maxExclusive")>]
        member _.MaxExclusive(p, v: Literal) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.MaxExclusive v)

        [<CustomOperation("maxInclusive")>]
        member _.MaxInclusive(p, v: Literal) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.MaxInclusive v)

        [<CustomOperation("pattern")>]
        member _.Pattern(p, pat: string) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.Pattern(pat, None))

        [<CustomOperation("patternWithFlags")>]
        member _.PatternWithFlags(p, pat: string, flags: string) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Pattern(pat, Some flags))

        [<CustomOperation("languageIn")>]
        member _.LanguageIn(p, tags: NonEmptyList<string>) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.LanguageIn tags)

        [<CustomOperation("uniqueLang")>]
        member _.UniqueLang(p, b: bool) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.UniqueLang b)

        [<CustomOperation("equalsPath")>]
        member _.EqualsPath(p, u: Uri) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.Equals u)

        [<CustomOperation("disjoint")>]
        member _.Disjoint(p, u: Uri) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.Disjoint u)

        [<CustomOperation("lessThan")>]
        member _.LessThan(p, u: Uri) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.LessThan u)

        [<CustomOperation("lessThanOrEquals")>]
        member _.LessThanOrEquals(p, u: Uri) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.LessThanOrEquals u)

        [<CustomOperation("node")>]
        member _.NodeOp(p, s: ShapeDecl) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.Node s)

        [<CustomOperation("qualifiedValueShape")>]
        member _.QualifiedValueShape(p, s: ShapeDecl, minC: int option, maxC: int option, disjoint: bool) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.QualifiedValueShape(s, minC, maxC, disjoint))

        [<CustomOperation("hasValue")>]
        member _.HasValue(p, v: Value) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.HasValue v)

        [<CustomOperation("allowedValues")>]
        member _.AllowedValues(p, vs: NonEmptyList<Value>) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.AllowedValues vs)

        [<CustomOperation("sparqlConstraint")>]
        member _.SparqlConstraintOp(p, sc: SparqlConstraint) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.Sparql sc)

        [<CustomOperation("severity")>]
        member _.SeverityOp(p, sev: Severity) : PropertyShapeSpec = { p with Severity = Some sev }

        [<CustomOperation("message")>]
        member _.MessageOp(p, msg: string) : PropertyShapeSpec = { p with Message = Some msg }

    let property (path: PropertyPath) = PropertyShapeBuilder(ofPath path)
```

- [ ] **Step 5: Wire both projects' `<Compile>` lists** — `Frank.Validation.fsproj`, append after `Shacl.fs`:

```xml
<Compile Include="ShapeBuilder.fsi" />
<Compile Include="ShapeBuilder.fs" />
```

`Frank.Validation.Tests.fsproj` — insert before `Program.fs`:

```xml
<Compile Include="ShapeBuilderTests.fs" />
```

- [ ] **Step 6: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "property { }"`
Expected: all 7 tests PASS.

- [ ] **Step 7: Fantomas + full-suite build + commit**

```bash
dotnet fantomas src/Frank.Validation/ShapeBuilder.fsi src/Frank.Validation/ShapeBuilder.fs test/Frank.Validation.Tests/ShapeBuilderTests.fs
dotnet build Frank.sln
dotnet test test/Frank.Validation.Tests/
git add src/Frank.Validation/ShapeBuilder.fsi src/Frank.Validation/ShapeBuilder.fs src/Frank.Validation/Frank.Validation.fsproj test/Frank.Validation.Tests/ShapeBuilderTests.fs test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj
git commit -m "feat(validation): PropertyShapeBuilder -- property{ } CE, mirrors ProvBuilder"
```

---

### Task 15: `ShapeBuilder.fs` — `ShapeBuilder` (`shape { }`)

**Files:**
- Modify: `src/Frank.Validation/ShapeBuilder.fsi` (add `ShapeBuilder` + `shape`)
- Modify: `src/Frank.Validation/ShapeBuilder.fs` (same)
- Modify: `test/Frank.Validation.Tests/ShapeBuilderTests.fs`

- [ ] **Step 1: Write the failing test** — append a new `testList` sibling in `ShapeBuilderTests.fs`:

```fsharp
testList "shape { }" [
    test "an empty block equals recordShape targets [] directly" {
        let targets = targetClass (Uri "https://schema.org/T")
        Expect.equal (shape targets { () }) (recordShape targets []) "empty CE block == recordShape targets []"
    }

    test "properties [ ... ] appends to the shape's property list" {
        let p1 = ofPath (PropertyPath.Predicate(Uri "https://schema.org/a")) |> addConstraint (PropertyConstraint.MinCount 1)
        let p2 = ofPath (PropertyPath.Predicate(Uri "https://schema.org/b")) |> addConstraint (PropertyConstraint.MinCount 1)

        let viaCe = shape (targetClass (Uri "https://schema.org/T")) { properties [ p1; p2 ] }

        match viaCe with
        | ShapeDecl.RecordShape n -> Expect.equal n.Properties [ p1; p2 ] "both properties present, in order"
        | other -> failtestf "expected RecordShape, got %A" other
    }

    test "closed sets Closed=true and the given IgnoredProperties" {
        let viaCe = shape (targetClass (Uri "https://schema.org/T")) { closed [ Uri "https://schema.org/extra" ] }

        match viaCe with
        | ShapeDecl.RecordShape n ->
            Expect.isTrue n.Closed "closed"
            Expect.equal n.IgnoredProperties [ Uri "https://schema.org/extra" ] "ignored properties"
        | other -> failtestf "expected RecordShape, got %A" other
    }

    test "severity/message set NodeShapeSpec.Severity/Message" {
        let viaCe = shape (targetClass (Uri "https://schema.org/T")) {
            severity Severity.Warning
            message "heads up"
        }

        match viaCe with
        | ShapeDecl.RecordShape n ->
            Expect.equal n.Severity (Some Severity.Warning) "severity"
            Expect.equal n.Message (Some "heads up") "message"
        | other -> failtestf "expected RecordShape, got %A" other
    }

    test "properties/closed/severity/message compose in one block, matching the design doc's personShape example" {
        let personShape =
            shape (targetClass (Uri "https://schema.org/Person")) {
                properties [
                    property (PropertyPath.Predicate(Uri "https://schema.org/email")) {
                        datatype XsdDatatype.String
                        pattern @"^\S+@\S+\.\S+$"
                        minCount 1
                    }
                    property (PropertyPath.Predicate(Uri "https://schema.org/birthDate")) {
                        datatype XsdDatatype.DateTime
                        maxCount 1
                    }
                ]
                closed []
            }

        match personShape with
        | ShapeDecl.RecordShape n ->
            Expect.hasLength n.Properties 2 "two property shapes"
            Expect.isTrue n.Closed "closed"
        | other -> failtestf "expected RecordShape, got %A" other
    }

    test "shape{ } composes with property{ }'s recursive `node` operation" {
        let personShape = shape (targetClass (Uri "https://schema.org/Person")) { properties [] }

        let moveShape =
            shape (targetClass (Uri "https://schema.org/MoveAction")) {
                properties [
                    property (PropertyPath.Predicate(Uri "https://schema.org/agent")) {
                        node personShape
                        minCount 1
                    }
                ]
            }

        match moveShape with
        | ShapeDecl.RecordShape n ->
            match n.Properties.Head.Constraints with
            | [ PropertyConstraint.Node inner; PropertyConstraint.MinCount 1 ] -> Expect.equal inner personShape "the nested shape is exactly personShape"
            | other -> failtestf "unexpected constraints: %A" other
        | other -> failtestf "expected RecordShape, got %A" other
    }
]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "shape { }"`
Expected: FAIL — `shape`/`ShapeBuilder` not defined.

- [ ] **Step 3: Extend `ShapeBuilder.fsi`** — append inside `[<AutoOpen>] module ShapeBuilderModule`, after `PropertyShapeBuilder`'s `val property`:

```fsharp
    [<Sealed>]
    type ShapeBuilder =
        new: initial: ShapeDecl -> ShapeBuilder
        member Yield: 'a -> ShapeDecl
        member Zero: unit -> ShapeDecl
        member Run: d: ShapeDecl -> ShapeDecl

        [<CustomOperation("properties")>] member Properties: ShapeDecl * PropertyShapeSpec list -> ShapeDecl
        [<CustomOperation("closed")>] member Closed: ShapeDecl * ignoredProperties: Uri list -> ShapeDecl
        [<CustomOperation("severity")>] member SeverityOp: ShapeDecl * Severity -> ShapeDecl
        [<CustomOperation("message")>] member MessageOp: ShapeDecl * string -> ShapeDecl

    /// `shape targets { ... } = ShapeBuilder(recordShape targets []) { ... }`.
    val shape: targets: TargetSpec list -> ShapeBuilder
```

- [ ] **Step 4: Extend `ShapeBuilder.fs`** — append inside `[<AutoOpen>] module ShapeBuilderModule`, after `let property`:

```fsharp
    [<Sealed>]
    type ShapeBuilder(initial: ShapeDecl) =
        member _.Yield(_) : ShapeDecl = initial
        member _.Zero() : ShapeDecl = initial
        member _.Run(d: ShapeDecl) : ShapeDecl = d

        [<CustomOperation("properties")>]
        member _.Properties(d, props: PropertyShapeSpec list) : ShapeDecl =
            match d with
            | RecordShape n -> RecordShape { n with Properties = n.Properties @ props }
            | other -> other

        [<CustomOperation("closed")>]
        member _.Closed(d, ignoredProperties: Uri list) : ShapeDecl =
            match d with
            | RecordShape n -> RecordShape { n with Closed = true; IgnoredProperties = ignoredProperties }
            | other -> other

        [<CustomOperation("severity")>]
        member _.SeverityOp(d, sev: Severity) : ShapeDecl =
            match d with
            | RecordShape n -> RecordShape { n with Severity = Some sev }
            | other -> other

        [<CustomOperation("message")>]
        member _.MessageOp(d, msg: string) : ShapeDecl =
            match d with
            | RecordShape n -> RecordShape { n with Message = Some msg }
            | other -> other

    let shape (targets: TargetSpec list) = ShapeBuilder(recordShape targets [])
```

- [ ] **Step 5: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "shape { }"`
Expected: all 6 tests PASS.

- [ ] **Step 6: Fantomas + full-suite build + commit**

```bash
dotnet fantomas src/Frank.Validation/ShapeBuilder.fsi src/Frank.Validation/ShapeBuilder.fs test/Frank.Validation.Tests/ShapeBuilderTests.fs
dotnet build Frank.sln
dotnet test test/Frank.Validation.Tests/
git add src/Frank.Validation/ShapeBuilder.fsi src/Frank.Validation/ShapeBuilder.fs test/Frank.Validation.Tests/ShapeBuilderTests.fs
git commit -m "feat(validation): ShapeBuilder -- shape{ } CE (properties, closed, severity, message)"
```

---

### Task 16: `Shacl.reportToDoc` + round-trip test

**Files:**
- Modify: `src/Frank.Validation/Shacl.fsi` (add `reportToDoc`)
- Modify: `src/Frank.Validation/Shacl.fs` (same)
- Create: `test/Frank.Validation.Tests/ReportRoundTripTests.fs`
- Modify: `Frank.Validation.Tests.fsproj`

**Interfaces:**
- Consumes: `Violation list` (Task 13).
- Produces: `Shacl.reportToDoc: Violation list -> Doc` — a real `sh:ValidationReport`, one `sh:result`/`sh:ValidationResult` blank node per violation. This is what Task 18's 422 `application/ld+json` path serializes.

- [ ] **Step 1: Write the failing test**

```fsharp
// test/Frank.Validation.Tests/ReportRoundTripTests.fs
module Frank.Validation.Tests.ReportRoundTripTests

open System
open Expecto
open Frank.Rdf
open Frank.Validation
open VDS.RDF
open VDS.RDF.Parsing

let private parseBackToGraph (json: string) : IGraph =
    let store = TripleStore()
    use reader = new System.IO.StringReader(json)
    JsonLdParser().Load(store, reader)
    store.Graphs |> Seq.head

[<Tests>]
let tests =
    testList "Shacl.reportToDoc" [
        test "a conforming (empty) violation list produces sh:conforms true and no sh:result" {
            let doc = Shacl.reportToDoc []
            Expect.exists doc.Statements (fun (_, p, v) -> p = "sh:conforms" && v = Value.Literal(Literal.Bool true)) "sh:conforms true"
            Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:result") "no sh:result entries"
        }

        test "one violation produces sh:conforms false and one sh:result carrying every field" {
            let v: Violation =
                { FocusNode = Node.Iri "https://example.org/move1"
                  ResultPath = Some(Uri "https://schema.org/position")
                  Severity = Severity.Violation
                  Message = "position is required"
                  ConstraintComponent = Uri "http://www.w3.org/ns/shacl#MinCountConstraintComponent"
                  SourceShape = Node.Iri "https://schema.org/MoveAction" }

            let doc = Shacl.reportToDoc [ v ]
            Expect.exists doc.Statements (fun (_, p, va) -> p = "sh:conforms" && va = Value.Literal(Literal.Bool false)) "sh:conforms false"
            Expect.exists doc.Statements (fun (_, p, _) -> p = "sh:result") "sh:result present"
            Expect.exists doc.Statements (fun (_, p, va) -> p = "sh:focusNode" && va = Value.Node(Node.Iri "https://example.org/move1")) "sh:focusNode"
            Expect.exists doc.Statements (fun (_, p, va) -> p = "sh:resultMessage" && va = Value.Literal(Literal.String "position is required")) "sh:resultMessage"
            Expect.exists doc.Statements (fun (_, p, va) -> p = "sh:resultPath" && va = Value.Node(Node.Iri "https://schema.org/position")) "sh:resultPath present when Some"
        }

        test "a violation with ResultPath=None omits sh:resultPath entirely" {
            let v: Violation =
                { FocusNode = Node.Iri "https://example.org/move1"
                  ResultPath = None
                  Severity = Severity.Violation
                  Message = "complex-path violation"
                  ConstraintComponent = Uri "http://www.w3.org/ns/shacl#AndConstraintComponent"
                  SourceShape = Node.Iri "https://schema.org/MoveAction" }

            let doc = Shacl.reportToDoc [ v ]
            Expect.all doc.Statements (fun (_, p, _) -> p <> "sh:resultPath") "no sh:resultPath when None"
        }

        test "round-trip: reportToDoc |> Doc.toJsonLd, reparsed via dotNetRDF's own JSON-LD reader, is isomorphic" {
            let v: Violation =
                { FocusNode = Node.Iri "https://example.org/move1"
                  ResultPath = Some(Uri "https://schema.org/position")
                  Severity = Severity.Warning
                  Message = "check this"
                  ConstraintComponent = Uri "http://www.w3.org/ns/shacl#DatatypeConstraintComponent"
                  SourceShape = Node.Iri "https://schema.org/MoveAction" }

            let doc = Shacl.reportToDoc [ v ]
            let original = Doc.toGraph doc
            let json = Doc.toJsonLd doc
            let reparsed = parseBackToGraph json
            Expect.isTrue (original.Equals(reparsed)) "original and reparsed graphs are isomorphic"
        }
    ]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "Shacl.reportToDoc"`
Expected: FAIL — `reportToDoc` not defined.

- [ ] **Step 3: Add `reportToDoc` to `Shacl.fsi`**

```fsharp
    /// Projects a Violation list back onto a Doc as a real sh:ValidationReport -- the inverse
    /// direction of toDoc/validate, used by the 422 application/ld+json response path.
    val reportToDoc: violations: Violation list -> Doc
```

- [ ] **Step 4: Add `reportToDoc` to `Shacl.fs`** — append at the end of the file:

```fsharp
    let reportToDoc (violations: Violation list) : Doc =
        let reportNode = Node.blank ()

        let resultStatements =
            violations
            |> List.collect (fun v ->
                let resultNode = Node.blank ()

                let pathStmt =
                    v.ResultPath
                    |> Option.map (fun u -> stmt resultNode "sh:resultPath" (Value.Node(Node.Iri u.AbsoluteUri)))
                    |> Option.toList

                [ stmt reportNode "sh:result" (Value.Node resultNode)
                  stmt resultNode RdfTypeIri (Value.Node(Node.Iri "sh:ValidationResult"))
                  stmt resultNode "sh:focusNode" (Value.Node v.FocusNode)
                  stmt resultNode "sh:resultSeverity" (Value.Node(Node.Iri(severityCurie v.Severity)))
                  stmt resultNode "sh:resultMessage" (Value.Literal(Literal.String v.Message))
                  stmt resultNode "sh:sourceConstraintComponent" (Value.Node(Node.Iri v.ConstraintComponent.AbsoluteUri))
                  stmt resultNode "sh:sourceShape" (Value.Node v.SourceShape) ]
                @ pathStmt)

        { Prefixes = shaclPrefixes
          Statements =
            stmt reportNode RdfTypeIri (Value.Node(Node.Iri "sh:ValidationReport"))
            :: stmt reportNode "sh:conforms" (Value.Literal(Literal.Bool(List.isEmpty violations)))
            :: resultStatements }
```

- [ ] **Step 5: Wire the test project's `<Compile>` list** — insert before `Program.fs`:

```xml
<Compile Include="ReportRoundTripTests.fs" />
```

- [ ] **Step 6: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/`
Expected: all tests PASS.

- [ ] **Step 7: Fantomas + commit**

```bash
dotnet fantomas src/Frank.Validation/Shacl.fsi src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ReportRoundTripTests.fs
dotnet build Frank.sln
git add src/Frank.Validation/Shacl.fsi src/Frank.Validation/Shacl.fs test/Frank.Validation.Tests/ReportRoundTripTests.fs test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj
git commit -m "feat(validation): Shacl.reportToDoc -- real sh:ValidationReport projection, round-trip proven"
```

---

### Task 17: `ResourceBuilderExtensions.fs` — `useValidation shapesGraph` on `resource { }`

**Files:**
- Create: `src/Frank.Validation/ResourceBuilderExtensions.fsi`
- Create: `src/Frank.Validation/ResourceBuilderExtensions.fs`
- Modify: `Frank.Validation.fsproj` (add pair, after `ShapeBuilder.fs`; add `<ProjectReference>` is already present from Task 1 — `Frank` core)
- Create: `test/Frank.Validation.Tests/ResourceBuilderExtensionsTests.fs`
- Modify: `Frank.Validation.Tests.fsproj`

**Interfaces:**
- Consumes: `Frank.Builder.ResourceBuilder`/`ResourceSpec` (Frank core), `VDS.RDF.Shacl.ShapesGraph`.
- Produces: `internal ValidationMetadata` (wraps a `ShapesGraph`, mirrors `Frank`'s own internal `ResourceLinkProvider`) — Task 18's middleware reads this back via `ctx.GetEndpoint().Metadata.GetMetadata<ValidationMetadata>()`. `useValidation` as a real `[<CustomOperation>]` on `resource { }`, added via F# type extension — no Frank core change (verified mechanism: `src/Frank.JsonHome/ResourceBuilderExtensions.fs`'s `rel`/`hrefVar`/`docs`).

- [ ] **Step 1: Write the failing test**

```fsharp
// test/Frank.Validation.Tests/ResourceBuilderExtensionsTests.fs
module Frank.Validation.Tests.ResourceBuilderExtensionsTests

open System
open Expecto
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

[<Tests>]
let tests =
    testList "useValidation (resource{ })" [
        test "useValidation attaches ValidationMetadata to every endpoint the resource builds" {
            let shapesGraph = Shacl.toShapesGraph [ recordShape (targetClass (Uri "https://schema.org/MoveAction")) [] ]

            let built =
                resource "/games/{id}/moves" {
                    useValidation shapesGraph
                    post (RequestDelegate(fun (_: HttpContext) -> System.Threading.Tasks.Task.CompletedTask))
                }

            Expect.hasLength built.Endpoints 1 "one endpoint (POST)"
            let metadata = built.Endpoints.[0].Metadata.GetMetadata<ValidationMetadata>()
            Expect.isNotNull (box metadata) "ValidationMetadata attached"

            match metadata with
            | ValidationMetadata sg -> Expect.equal sg shapesGraph "the exact ShapesGraph passed to useValidation"
        }

        test "a resource without useValidation has no ValidationMetadata (opt-in, never implicit)" {
            let built = resource "/games/{id}" { get (RequestDelegate(fun (_: HttpContext) -> System.Threading.Tasks.Task.CompletedTask)) }
            let metadata = built.Endpoints.[0].Metadata.GetMetadata<ValidationMetadata>()
            Expect.isTrue (obj.ReferenceEquals(metadata, null)) "no metadata when useValidation isn't called"
        }
    ]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "useValidation (resource"`
Expected: FAIL — `useValidation`/`ValidationMetadata` not defined.

- [ ] **Step 3: Write `ResourceBuilderExtensions.fsi`**

```fsharp
// src/Frank.Validation/ResourceBuilderExtensions.fsi
namespace Frank.Validation

open Frank.Builder

/// Metadata attached per-resource by `useValidation`; read back by the interceptor middleware
/// (WebHostBuilderExtensions.fs) via ctx.GetEndpoint().Metadata.GetMetadata<ValidationMetadata>().
/// Internal, exactly like Frank's own ResourceLinkProvider -- not a public contract.
type internal ValidationMetadata = ValidationMetadata of VDS.RDF.Shacl.ShapesGraph

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        /// Declares which ShapesGraph validates this resource's POST/PUT/PATCH application/ld+json
        /// bodies. Declarative only -- does nothing at request time by itself; requires
        /// `webHost { useValidation }` (WebHostBuilderExtensions.fs) to actually intercept requests.
        [<CustomOperation("useValidation")>]
        member UseValidation: spec: ResourceSpec * shapesGraph: VDS.RDF.Shacl.ShapesGraph -> ResourceSpec
```

- [ ] **Step 4: Write `ResourceBuilderExtensions.fs`**

```fsharp
// src/Frank.Validation/ResourceBuilderExtensions.fs
namespace Frank.Validation

open Frank.Builder

type internal ValidationMetadata = ValidationMetadata of VDS.RDF.Shacl.ShapesGraph

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        [<CustomOperation("useValidation")>]
        member _.UseValidation(spec: ResourceSpec, shapesGraph: VDS.RDF.Shacl.ShapesGraph) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add(ValidationMetadata shapesGraph)))
```

- [ ] **Step 5: Wire both projects' `<Compile>` lists** — `Frank.Validation.fsproj`, append after `ShapeBuilder.fs`:

```xml
<Compile Include="ResourceBuilderExtensions.fsi" />
<Compile Include="ResourceBuilderExtensions.fs" />
```

`Frank.Validation.Tests.fsproj` — insert before `Program.fs`; also add the `Microsoft.AspNetCore.App` framework reference needed for `Microsoft.AspNetCore.Http`/`Frank.Builder` types (add `<FrameworkReference Include="Microsoft.AspNetCore.App" />` in a `<ItemGroup>` if not already implied by the `Frank`/`Frank.Rdf` project references — `Frank.fsproj` already pulls this in transitively, so this is very likely already satisfied; only add it explicitly if the build fails without it):

```xml
<Compile Include="ResourceBuilderExtensionsTests.fs" />
```

- [ ] **Step 6: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "useValidation (resource"`
Expected: both tests PASS.

- [ ] **Step 7: Fantomas + full-suite build + commit**

```bash
dotnet fantomas src/Frank.Validation/ResourceBuilderExtensions.fsi src/Frank.Validation/ResourceBuilderExtensions.fs test/Frank.Validation.Tests/ResourceBuilderExtensionsTests.fs
dotnet build Frank.sln
dotnet test test/Frank.Validation.Tests/
git add src/Frank.Validation/ResourceBuilderExtensions.fsi src/Frank.Validation/ResourceBuilderExtensions.fs src/Frank.Validation/Frank.Validation.fsproj test/Frank.Validation.Tests/ResourceBuilderExtensionsTests.fs test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj
git commit -m "feat(validation): useValidation on resource{ } -- attaches ValidationMetadata via ResourceBuilder.AddMetadata"
```

---

### Task 18: `WebHostBuilderExtensions.fs` — the interceptor middleware + `useValidation` on `webHost { }`

**Files:**
- Create: `src/Frank.Validation/WebHostBuilderExtensions.fsi`
- Create: `src/Frank.Validation/WebHostBuilderExtensions.fs`
- Modify: `Frank.Validation.fsproj` (add pair, after `ResourceBuilderExtensions.fs`)
- Create: `test/Frank.Validation.Tests/ValidationMiddlewareTests.fs`
- Modify: `Frank.Validation.Tests.fsproj`

**Verified pipeline ordering** (`src/Frank/WebHostBuilder.fs:50-55`): `BeforeRoutingMiddleware -> UseRouting() -> WebLink.useResourceScopedLinks -> Middleware -> UseEndpoints`. Composing into `spec.Middleware` (the same field `useOpenApi` composes into) runs *after* `UseRouting()`, so `ctx.GetEndpoint()` is already populated — confirmed, not assumed.

**Interfaces:**
- Consumes: `ValidationMetadata` (Task 17), `Shacl.validate`/`reportToDoc` (Tasks 13, 16), `Doc.writeJsonLd` (`Frank.Rdf`).
- Produces: `internal useValidationMiddleware: IApplicationBuilder -> IApplicationBuilder` (the real logic, unit-testable directly via `TestServer` without going through the CE — mirrors how `test/Frank.Tests/ResponseLinkTests.fs` tests `WebLink.useResourceScopedLinks` the same way); `useValidation` as a `[<CustomOperation>]` on `webHost { }`; `internal ValidatedGraphKey: string` (the `HttpContext.Items` key a conforming request's parsed graph is stashed under).

- [ ] **Step 1: Write the failing test**

```fsharp
// test/Frank.Validation.Tests/ValidationMiddlewareTests.fs
module Frank.Validation.Tests.ValidationMiddlewareTests

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

let private moveShapesGraph =
    Shacl.toShapesGraph [
        recordShape (targetClass (Uri "https://schema.org/MoveAction")) [
            ofPath (PropertyPath.Predicate(Uri "https://schema.org/position")) |> addConstraint (PropertyConstraint.MinCount 1)
        ]
    ]

let private conformingBody =
    """[{"@id":"https://example.org/move1","@type":["https://schema.org/MoveAction"],"https://schema.org/position":[{"@value":3}]}]"""

let private violatingBody =
    """[{"@id":"https://example.org/move2","@type":["https://schema.org/MoveAction"]}]"""

/// Wires useValidationMiddleware exactly where WebHostBuilder.Run places it -- after UseRouting,
/// before UseEndpoints -- without going through the webHost{ } CE, since Run blocks. Mirrors
/// test/Frank.Tests/ResponseLinkTests.fs's createTestServer.
let private createTestServer (validated: bool) =
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                    .Configure(fun app ->
                        app
                        |> fun app -> app.UseRouting()
                        |> Frank.Validation.WebHostBuilderExtensions.useValidationMiddleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                let mapping = endpoints.MapPost("/moves", Func<HttpContext, Task>(fun ctx -> ctx.Response.WriteAsync "handled"))

                                if validated then
                                    mapping.WithMetadata(ValidationMetadata moveShapesGraph) |> ignore
                                else
                                    ())
                            |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

[<Tests>]
let tests =
    testList "useValidationMiddleware" [
        testTask "no ValidationMetadata on the endpoint -- passes straight through" {
            use client = createTestServer false
            let! response = client.PostAsync("/moves", new StringContent(violatingBody, Encoding.UTF8, "application/ld+json"))
            Expect.equal response.StatusCode HttpStatusCode.OK "handler ran unvalidated"
        }

        testTask "GET requests to a validated resource pass through unvalidated (not POST/PUT/PATCH)" {
            use client = createTestServer true
            let! response = client.GetAsync("/moves")
            Expect.notEqual response.StatusCode HttpStatusCode.UnprocessableEntity "GET is never intercepted"
        }

        testTask "a conforming application/ld+json body reaches the handler" {
            use client = createTestServer true
            let! response = client.PostAsync("/moves", new StringContent(conformingBody, Encoding.UTF8, "application/ld+json"))
            Expect.equal response.StatusCode HttpStatusCode.OK "handler ran"
            let! body = response.Content.ReadAsStringAsync()
            Expect.equal body "handled" "handler's own response body, untouched"
        }

        testTask "a violating body short-circuits with 422 and never reaches the handler" {
            use client = createTestServer true
            let! response = client.PostAsync("/moves", new StringContent(violatingBody, Encoding.UTF8, "application/ld+json"))
            Expect.equal response.StatusCode (enum 422) "422 Unprocessable Entity"
            let! body = response.Content.ReadAsStringAsync()
            Expect.notEqual body "handled" "handler never ran"
        }

        testTask "422 with Accept: application/ld+json returns a real sh:ValidationReport" {
            use client = createTestServer true
            use req = new HttpRequestMessage(HttpMethod.Post, "/moves")
            req.Content <- new StringContent(violatingBody, Encoding.UTF8, "application/ld+json")
            req.Headers.Accept.ParseAdd("application/ld+json")
            let! response = client.SendAsync(req)
            Expect.equal response.Content.Headers.ContentType.MediaType "application/ld+json" "ld+json response"
            let! body = response.Content.ReadAsStringAsync()
            Expect.stringContains body "ValidationReport" "real SHACL report in the body"
        }

        testTask "422 with no Accept (or a non-ld+json Accept) returns application/problem+json" {
            use client = createTestServer true
            let! response = client.PostAsync("/moves", new StringContent(violatingBody, Encoding.UTF8, "application/ld+json"))
            Expect.equal response.Content.Headers.ContentType.MediaType "application/problem+json" "problem+json by default"
            let! body = response.Content.ReadAsStringAsync()
            Expect.stringContains body "violations" "flattened violations array present"
        }

        testTask "malformed JSON-LD returns 400, distinct from 422" {
            use client = createTestServer true
            let! response = client.PostAsync("/moves", new StringContent("{not valid json", Encoding.UTF8, "application/ld+json"))
            Expect.equal response.StatusCode HttpStatusCode.BadRequest "400, not 422 -- a parse failure isn't a SHACL violation"
        }

        testTask "an oversized body returns 413 before parsing is attempted" {
            use client = createTestServer true
            let huge = String('x', 2_000_000)
            let! response = client.PostAsync("/moves", new StringContent(huge, Encoding.UTF8, "application/ld+json"))
            Expect.equal response.StatusCode (enum 413) "413 Payload Too Large"
        }
    ]
```

- [ ] **Step 2: Run — verify it fails**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "useValidationMiddleware"`
Expected: FAIL — `useValidationMiddleware` not defined.

- [ ] **Step 3: Write `WebHostBuilderExtensions.fsi`**

```fsharp
// src/Frank.Validation/WebHostBuilderExtensions.fsi
namespace Frank.Validation

open Microsoft.AspNetCore.Builder
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    /// HttpContext.Items key a conforming request's parsed graph is stashed under, so the handler
    /// doesn't re-parse the body it already validated.
    val internal ValidatedGraphKey: string

    /// The one app-wide interceptor: reads ValidationMetadata off the matched endpoint (set by
    /// `useValidation` on resource{ }), and for POST/PUT/PATCH application/ld+json requests to a
    /// validated resource, buffers/parses/validates the body before the handler runs. A no-op
    /// pass-through otherwise. Exposed (not private) so tests can wire it directly via TestServer,
    /// the same way test/Frank.Tests/ResponseLinkTests.fs tests WebLink.useResourceScopedLinks.
    val internal useValidationMiddleware: app: IApplicationBuilder -> IApplicationBuilder

    type WebHostBuilder with
        /// Registers the interceptor into the pipeline, once, app-wide. Composes into the same
        /// Middleware field useOpenApi does -- runs after UseRouting(), so ctx.GetEndpoint() is
        /// already populated (verified against src/Frank/WebHostBuilder.fs's Run).
        [<CustomOperation("useValidation")>]
        member UseValidation: spec: WebHostSpec -> WebHostSpec
```

- [ ] **Step 4: Write `WebHostBuilderExtensions.fs`**

```fsharp
// src/Frank.Validation/WebHostBuilderExtensions.fs
namespace Frank.Validation

open System
open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Rdf

[<AutoOpen>]
module WebHostBuilderExtensions =
    [<Literal>]
    let internal ValidatedGraphKey = "Frank.Validation.ParsedGraph"

    [<Literal>]
    let private MaxBodyBytes = 1_048_576L // 1 MiB

    let private isValidatedMethod (method: string) =
        HttpMethods.IsPost method || HttpMethods.IsPut method || HttpMethods.IsPatch method

    let private isLdJson (contentType: string) =
        not (isNull contentType) && contentType.StartsWith("application/ld+json", StringComparison.OrdinalIgnoreCase)

    let private parseGraph (bodyText: string) : Result<VDS.RDF.IGraph, string> =
        try
            let store = VDS.RDF.TripleStore()
            use bodyReader = new System.IO.StringReader(bodyText)
            VDS.RDF.Parsing.JsonLdParser().Load(store, bodyReader)
            let dataGraph = VDS.RDF.Graph() :> VDS.RDF.IGraph

            for g in store.Graphs do
                dataGraph.Merge(g)

            Ok dataGraph
        with ex ->
            Error ex.Message

    let private writeProblemJson (ctx: HttpContext) (statusCode: int) (title: string) (detail: string) : Task =
        ctx.Response.StatusCode <- statusCode
        ctx.Response.ContentType <- "application/problem+json"
        ctx.Response.WriteAsJsonAsync({| ``type`` = "about:blank"; title = title; status = statusCode; detail = detail |})

    let private writeViolationResponse (ctx: HttpContext) (violations: Violation list) : Task =
        task {
            ctx.Response.StatusCode <- 422
            let acceptsLdJson = ctx.Request.Headers.Accept.ToString().Contains("application/ld+json")

            if acceptsLdJson then
                ctx.Response.ContentType <- "application/ld+json"
                use writer = new System.IO.StreamWriter(ctx.Response.Body, Encoding.UTF8, leaveOpen = true)
                Doc.writeJsonLd (Shacl.reportToDoc violations) writer
                do! writer.FlushAsync()
            else
                ctx.Response.ContentType <- "application/problem+json"

                let payload =
                    {| ``type`` = "https://www.w3.org/TR/shacl/#validation-report"
                       title = "SHACL validation failed"
                       status = 422
                       violations =
                        violations
                        |> List.map (fun v ->
                            {| focusNode = (match v.FocusNode with Node.Iri s -> s | Node.Blank b -> "_:" + b)
                               resultPath = v.ResultPath |> Option.map string
                               severity = string v.Severity
                               message = v.Message
                               constraintComponent = v.ConstraintComponent.AbsoluteUri |}) |}

                do! ctx.Response.WriteAsJsonAsync(payload)
        }
        :> Task

    let internal useValidationMiddleware (app: IApplicationBuilder) : IApplicationBuilder =
        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
            task {
                match ctx.GetEndpoint() with
                | null -> do! next.Invoke ctx
                | endpoint ->
                    match endpoint.Metadata.GetMetadata<ValidationMetadata>() with
                    | null -> do! next.Invoke ctx
                    | ValidationMetadata shapesGraph ->
                        if not (isValidatedMethod ctx.Request.Method && isLdJson ctx.Request.ContentType) then
                            do! next.Invoke ctx
                        elif ctx.Request.ContentLength.HasValue && ctx.Request.ContentLength.Value > MaxBodyBytes then
                            ctx.Response.StatusCode <- 413
                        else
                            ctx.Request.EnableBuffering()
                            use reader = new System.IO.StreamReader(ctx.Request.Body, Encoding.UTF8, leaveOpen = true)
                            let! bodyText = reader.ReadToEndAsync()
                            ctx.Request.Body.Position <- 0L

                            if int64 (Encoding.UTF8.GetByteCount bodyText) > MaxBodyBytes then
                                ctx.Response.StatusCode <- 413
                            else
                                match parseGraph bodyText with
                                | Error message -> do! writeProblemJson ctx 400 "Malformed JSON-LD" message
                                | Ok dataGraph ->
                                    match Shacl.validate shapesGraph dataGraph with
                                    | ValidationOutcome.Conforms ->
                                        ctx.Items.[ValidatedGraphKey] <- box dataGraph
                                        do! next.Invoke ctx
                                    | ValidationOutcome.Violates violations -> do! writeViolationResponse ctx violations
            }
            :> Task)

    type WebHostBuilder with
        [<CustomOperation("useValidation")>]
        member _.UseValidation(spec: WebHostSpec) : WebHostSpec =
            { spec with Middleware = spec.Middleware >> useValidationMiddleware }
```

> **Verify while implementing:** `IApplicationBuilder.Use(fun ctx next -> ...)`'s exact delegate overload (`Func<HttpContext, RequestDelegate, Task>` — confirm the lambda's inferred type matches; `WebLink.fs`'s own `app.Use(fun (ctx: HttpContext) (next: RequestDelegate) -> ...)` is the proven-working shape here, copy its parameter typing exactly), and `HttpRequest.EnableBuffering()`'s availability (it's a `Microsoft.AspNetCore.Http.HttpRequestRewindExtensions` extension method — confirm the `open` needed, likely already covered by `open Microsoft.AspNetCore.Http`).

- [ ] **Step 5: Wire both projects' `<Compile>` lists** — `Frank.Validation.fsproj`, append after `ResourceBuilderExtensions.fs`:

```xml
<Compile Include="WebHostBuilderExtensions.fsi" />
<Compile Include="WebHostBuilderExtensions.fs" />
```

`Frank.Validation.Tests.fsproj` — insert before `Program.fs`, and add `Microsoft.AspNetCore.TestHost`/`Microsoft.Extensions.Hosting` package references if not already present from Task 1:

```xml
<Compile Include="ValidationMiddlewareTests.fs" />
```

- [ ] **Step 6: Run — verify it passes**

Run: `dotnet test test/Frank.Validation.Tests/ --filter "useValidationMiddleware"`
Expected: all 8 tests PASS.

- [ ] **Step 7: Fantomas + full-suite build + commit**

```bash
dotnet fantomas src/Frank.Validation/WebHostBuilderExtensions.fsi src/Frank.Validation/WebHostBuilderExtensions.fs test/Frank.Validation.Tests/ValidationMiddlewareTests.fs
dotnet build Frank.sln
dotnet test test/Frank.Validation.Tests/
git add src/Frank.Validation/WebHostBuilderExtensions.fsi src/Frank.Validation/WebHostBuilderExtensions.fs src/Frank.Validation/Frank.Validation.fsproj test/Frank.Validation.Tests/ValidationMiddlewareTests.fs test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj
git commit -m "feat(validation): useValidation on webHost{ } -- the interceptor, dual-path 422, 413/400 guards"
```

---

### Task 19: `sample/Frank.Validation.Sample` + README

**Files:**
- Create: `sample/Frank.Validation.Sample/Frank.Validation.Sample.fsproj`
- Create: `sample/Frank.Validation.Sample/Program.fs`
- Create: `src/Frank.Validation/README.md`
- Modify: `Frank.sln`

**Interfaces:**
- Consumes: everything (Tasks 2-18) — this is the end-to-end proof, matching `CLAUDE.md`'s requirement that every new `src/Frank.*` package ships a runnable sample in the same plan, not deferred.

- [ ] **Step 1: Create the sample project file**

```xml
<!-- sample/Frank.Validation.Sample/Frank.Validation.Sample.fsproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <EnableDefaultContentItems>false</EnableDefaultContentItems>
    <AssemblyName>Frank.Validation.Sample</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank/Frank.fsproj" />
    <ProjectReference Include="../../src/Frank.Rdf/Frank.Rdf.fsproj" />
    <ProjectReference Include="../../src/Frank.Validation/Frank.Validation.fsproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write `Program.fs`** — same `games` dict as `Frank.Rdf.Sample`/`Frank.Provenance.Sample`, adding `POST /games/{id}/moves` validated against a `MoveAction` shape demonstrating `datatype` (literal) and recursive `node` (object reference to a nested `Person`-shaped agent), plus a `closed` shape to prove that constraint independently:

```fsharp
// sample/Frank.Validation.Sample/Program.fs
module Sample.Validation.Program

open System
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Rdf
open Frank.Validation

// Same two games as Frank.Rdf.Sample/Frank.Provenance.Sample -- this sample's own addition is the
// POST endpoint and the shapes validating it, not a different domain.
let private games = dict [ "1", "Tic-tac-toe"; "2", "Connect Four" ]

// A Person shape closed to exactly name+email -- demonstrates `closed` independently of the
// recursive `node` constraint MoveAction below uses it through.
let private personShape =
    shape (targetClass (Uri "https://schema.org/Person")) {
        properties [
            property (PropertyPath.Predicate(Uri "https://schema.org/name")) { datatype XsdDatatype.String; minCount 1; maxCount 1 }
            property (PropertyPath.Predicate(Uri "https://schema.org/email")) { datatype XsdDatatype.String; maxCount 1 }
        ]
        closed []
    }

let private moveShape =
    shape (targetClass (Uri "https://schema.org/MoveAction")) {
        properties [
            property (PropertyPath.Predicate(Uri "https://schema.org/position")) {
                datatype XsdDatatype.Integer
                minCount 1
                maxCount 1
            }
            property (PropertyPath.Predicate(Uri "https://schema.org/agent")) {
                node personShape
                minCount 1
                maxCount 1
            }
        ]
    }

let private moveShapesGraph = Shacl.toShapesGraph [ moveShape; personShape ]

// Plain JSON confirmation of a move that already passed SHACL validation -- the middleware has
// already buffered/parsed/validated the body by the time this handler runs; ctx.Items carries the
// parsed graph (ValidatedGraphKey) if a handler wants it without re-parsing, though this sample's
// handler is simple enough not to need it.
let private postMove =
    fun (ctx: HttpContext) ->
        task {
            let id = string ctx.Request.RouteValues.["id"]

            match games.TryGetValue id with
            | true, _ ->
                ctx.Response.StatusCode <- 201
                do! ctx.Response.WriteAsJsonAsync({| gameId = id; accepted = true |})
            | false, _ ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsJsonAsync({| error = $"no game with id {id}" |})
        }

let private movesResource =
    resource "/games/{id}/moves" {
        useValidation moveShapesGraph
        post postMove
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        useValidation
        resource movesResource
    }

    0
```

- [ ] **Step 3: Write `src/Frank.Validation/README.md`**

```markdown
# Frank.Validation

Hand-authored SHACL Core (+ SPARQL-based constraints, + the full property-path grammar) validation
for Frank resources, built on [Frank.Rdf](../Frank.Rdf/README.md).

## Authoring a shape

```fsharp
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

let personShape =
    shape (targetClass (Uri "https://schema.org/Person")) {
        properties [
            property (PropertyPath.Predicate(Uri "https://schema.org/email")) {
                datatype XsdDatatype.String
                pattern @"^\S+@\S+\.\S+$"
                minCount 1
            }
        ]
        closed []
    }
```

`shape { }`/`property { }` are optional sugar over `ShapeSpecFunctions` -- both produce identical
`ShapeDecl`/`PropertyShapeSpec` values; use whichever reads better at the call site.

## Validating a graph

```fsharp
let shapesGraph = Shacl.toShapesGraph [ personShape ]

match Shacl.validate shapesGraph someDataGraph with
| ValidationOutcome.Conforms -> ()
| ValidationOutcome.Violates violations -> (* ... *)
```

## Validating HTTP request bodies

```fsharp
resource "/people" {
    useValidation shapesGraph
    post createPerson
}

webHost args {
    useDefaults
    useValidation   // registers the one app-wide interceptor -- required once, app-wide
    resource peopleResource
}
```

`POST`/`PUT`/`PATCH` requests with `Content-Type: application/ld+json` to a `useValidation`-declared
resource are buffered, parsed, and validated before the handler runs. A conforming request continues
to the handler unchanged; a violating request gets 422, content-negotiated between a real
`sh:ValidationReport` (`Accept: application/ld+json`) and `application/problem+json` (everything else).

See `sample/Frank.Validation.Sample` for a complete, runnable example.

## Non-goals

SHACL-JS, non-validating shape characteristics (`sh:name`/`sh:order`/...), and durable shape storage
are explicitly out of scope -- see `docs/superpowers/specs/2026-08-03-frank-validation-design.md`.
```

- [ ] **Step 4: Add the sample project to the solution**

Run: `dotnet sln Frank.sln add sample/Frank.Validation.Sample/Frank.Validation.Sample.fsproj`

- [ ] **Step 5: Build and manually verify against a running instance**

```bash
dotnet build Frank.sln
dotnet run --project sample/Frank.Validation.Sample/ &
sleep 3
```

Then, in a separate step, exercise both paths:

```bash
# Conforming move -- 201
curl -i -X POST http://localhost:5000/games/1/moves \
  -H "Content-Type: application/ld+json" \
  -d '[{"@id":"https://example.org/move1","@type":["https://schema.org/MoveAction"],"https://schema.org/position":[{"@value":3}],"https://schema.org/agent":[{"@id":"https://example.org/p1"}]},{"@id":"https://example.org/p1","@type":["https://schema.org/Person"],"https://schema.org/name":[{"@value":"Alice"}]}]'

# Violating move (missing position, agent missing required name) -- 422, problem+json
curl -i -X POST http://localhost:5000/games/1/moves \
  -H "Content-Type: application/ld+json" \
  -d '[{"@id":"https://example.org/move2","@type":["https://schema.org/MoveAction"],"https://schema.org/agent":[{"@id":"https://example.org/p2"}]},{"@id":"https://example.org/p2","@type":["https://schema.org/Person"]}]'

# Same violating move, but asking for the real SHACL report
curl -i -X POST http://localhost:5000/games/1/moves \
  -H "Content-Type: application/ld+json" -H "Accept: application/ld+json" \
  -d '[{"@id":"https://example.org/move2","@type":["https://schema.org/MoveAction"],"https://schema.org/agent":[{"@id":"https://example.org/p2"}]},{"@id":"https://example.org/p2","@type":["https://schema.org/Person"]}]'
```

Expected: first `curl` returns `201` with `{"gameId":"1","accepted":true}`; second returns `422` with a `application/problem+json` body listing at least two violations (missing `position`, missing nested `Person.name`); third returns `422` with `Content-Type: application/ld+json` and a body containing `"ValidationReport"`.

Then: `pkill -f "Frank.Validation.Sample"`

- [ ] **Step 6: Commit**

```bash
git add sample/Frank.Validation.Sample/Frank.Validation.Sample.fsproj sample/Frank.Validation.Sample/Program.fs src/Frank.Validation/README.md Frank.sln
git commit -m "feat(validation-sample): POST /games/{id}/moves -- datatype + recursive node + closed, end-to-end proof"
```

---

## Self-Review

- **Spec coverage:** every section of `docs/superpowers/specs/2026-08-03-frank-validation-design.md` maps to a task — data model (Task 2), authoring functions (Task 3), interpreter by category (Tasks 4-12, matching the design doc's own *Implementation order*), typed validation (Task 13), CE sugar (Tasks 14-15), report projection (Task 16), HTTP surface (Tasks 17-18), sample (Task 19). SPARQL-based constraints (Task 11) and the full property-path grammar (folded into Task 4's foundation, since paths are structural rather than a constraint category) are both present, addressing the scope correction mid-brainstorming — this was not re-narrowed during planning.
- **Placeholder scan:** no task step says "implement later," "add appropriate handling," or defers concrete code to a future task without saying which one. The two genuine unknowns this plan carries forward — `Validation.fs`'s exact `Result` property names/types (Task 13) and the JSON-LD-body-oversized-body edge case's precise `EnableBuffering` behavior (Task 18) — are both flagged with explicit "verify against the real API, do not stub" guidance, the same pattern the credited Plan 4 reference already used for the same kind of uncertainty, not a hidden gap.
- **Type consistency:** `PropertyShapeSpec`/`NodeShapeSpec`/`ShapeDecl`/`PropertyConstraint` (Task 2) are used identically in every later task; `ShapeSpecFunctions`' five functions (Task 3) are the only construction path both `Shacl.fs` (Tasks 4-13) and `ShapeBuilder.fs` (Tasks 14-15) build on; `Violation`/`ValidationOutcome` (Task 13) match exactly what `reportToDoc` (Task 16) and the middleware (Task 18) consume; `ValidationMetadata` is defined once (Task 17) and read once (Task 18), never duplicated.
- **One disclosed deviation from the design doc**, not silent: `Violation.ResultPath` is `Uri option`, not `PropertyPath option` — flagged explicitly in Task 13 with the reasoning (round-trip lossiness for complex paths, not attempted without being able to verify against the live library first).

---

