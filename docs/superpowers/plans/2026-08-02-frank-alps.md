# Frank.Alps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A new `Frank.Alps` package providing hand-authored ALPS (draft-amundsen-richardson-foster-alps-07) profile documents — two authoring surfaces over one `Descriptor` type, state- and orthogonality-aware composite-state authoring, a derived protocol graph, and two HTTP exposures.

**Architecture:** `Descriptor`/`Doc`/`Link`/`Ext`/`DescriptorRef` form a plain, self-referential record model (`DescriptorTypes.fs`). `Descriptor.fs` provides every combinator as a plain `... -> Descriptor -> Descriptor` function. `DescriptorBuilder.fs` adds a `descriptor { }` computation expression as a second authoring surface over the same type, reusing `semantic`/`safe`/`unsafe`/`idempotent` as custom operations. `ProtocolGraph.fs` derives a read-only `ProtocolTransition list` from `From`/`Rt`. `Serialization.fs` writes draft-07 JSON directly via `Utf8JsonWriter`, projecting `From` into `protocolState`/`availableInStates` `ext` elements at write time. `ResourceBuilderExtensions.fs` adds `binds` to `handler { }` and validates `Type` against the bound HTTP method at startup. `EndpointSurface.fs` and `AuthorizationFilter.fs` read `Endpoint.Metadata` directly (no `Frank.JsonHome`/ApiExplorer dependency) to back both HTTP exposures: `AlpsDocument.fs` (app-wide, `useAlps` on `webHost { }`, mirrors `useJsonHome`) and `Excerpt.fs` (`Alps.excerpt`, wired manually into `negotiate { }` per resource, mirroring the `Frank.Rdf` sample's own `application/ld+json` case).

**Tech Stack:** F# 8.0+, .NET 8.0/9.0/10.0 multi-targeting (`net10.0`-only for HTTP-surface files that need `Microsoft.AspNetCore.App`... — no: multi-targeting matches `Frank.Rdf`/`Frank.JsonHome`, all `net8.0;net9.0;net10.0`), `Frank` (project reference), `Microsoft.AspNetCore.App` (framework reference), Expecto, `Microsoft.AspNetCore.TestHost`.

**Design doc:** `docs/superpowers/specs/2026-08-02-frank-alps-protocol-design.md`

## Global Constraints

- `src/Frank.Alps/` targets `net8.0;net9.0;net10.0`, matching `Frank.Rdf`/`Frank.JsonHome`. `ProjectReference` to `Frank` only. `FrameworkReference Microsoft.AspNetCore.App`. **No** dependency on `Frank.Rdf`, `Frank.Provenance`, or `Frank.JsonHome`.
- Every `.fs` file gets a matching `.fsi` immediately above it in `<Compile>` order (`CLAUDE.md`). Update both together in every task.
- `DescriptorType` is `[<Struct; RequireQualifiedAccess>]`. `DocFormat` is `[<Struct>]`. `Descriptor`, `Doc`, `Link`, `Ext`, `DescriptorRef`, `ProtocolTransition`, `StateComposition` are plain reference types — do not add `[<Struct>]` to any of these (see the design doc's `[<Struct>]` section; this rides on issue #485, not decided here).
- ALPS `ext` ids for Frank-authored markers live under `https://frank-fs.github.io/alps-ext/` — reuse the existing namespace (`protocolState`, `availableInStates` from PR #165/#214) and extend it with `.../initial` and `.../orthogonal`. Do not invent a different namespace.
- Test framework is **Expecto**, matching every other Frank test project — not xUnit/NUnit.
- Every combinator is a plain function (`... -> Descriptor -> Descriptor`, pipeable) **and** available as a `[<CustomOperation>]` inside `descriptor { }` — both surfaces must produce structurally equal `Descriptor` values for the same profile (design doc, *Two authoring surfaces*).
- Commit after every task (this repo is trunk-based — commit directly, no PR).

## Out of scope for this plan

- **Multi-document/per-resource profiles**, **`CompoundProtocolTransition`**, **`CurrentStateResolver`-as-a-set**, **role-projected statecharts**, **the `Frank.Analyzers` paired analyzer** — tracked as frank-fs/frank#488, #489, #490, (role-projection has no dedicated issue, see design doc future work), #491 respectively. None are touched here.
- **`[<Struct>]` evaluation** for `Descriptor` and friends beyond what's already decided (`DescriptorType`/`DocFormat`) — frank-fs/frank#485.
- **A durable or non-`webHost {}` hosting story** — this plan targets the same `WebHostBuilder`/`resource { }`/`handler { }` core every other Frank package targets.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/Frank.Alps/Frank.Alps.fsproj` | Create | Project file |
| `src/Frank.Alps/DescriptorTypes.fsi`/`.fs` | Create | `DescriptorType`, `DocFormat`, `Doc`, `Link`, `Ext`, `Descriptor`, `DescriptorRef` |
| `src/Frank.Alps/Descriptor.fsi`/`.fs` | Create | Every plain combinator: `semantic`/`safe`/`unsafe`/`idempotent`, `doc`/`docWith`/`def`/`tag`/`rel`/`named`, `ext`/`extWith`, `link`/`linkWith`, `contains`, `rt`, `href`/`hrefExternal`, `initial`/`regions`, `from`; `StateComposition` |
| `src/Frank.Alps/ProtocolGraph.fsi`/`.fs` | Create | `ProtocolTransition`, `ProtocolGraph.ofProfile` |
| `src/Frank.Alps/DescriptorBuilder.fsi`/`.fs` | Create | `descriptor { }` CE |
| `src/Frank.Alps/Serialization.fsi`/`.fs` | Create | `Descriptor list -> JSON` (draft-07), `href` local-vs-external resolution, `protocolState`/`availableInStates` projection |
| `src/Frank.Alps/HandlerBuilderExtensions.fsi`/`.fs` | Create | `binds` on `handler { }` (`HandlerBuilder`, core `Frank` type) |
| `src/Frank.Alps/EndpointSurface.fsi`/`.fs` | Create | `descriptorsForRoute`, `allDescriptors` |
| `src/Frank.Alps/AuthorizationFilter.fsi`/`.fs` | Create | Principal-based filtering over `(Endpoint * Descriptor)` |
| `src/Frank.Alps/AlpsDocument.fsi`/`.fs` | Create | App-wide document handler, `useAlps` on `WebHostBuilder`, startup `Type`-vs-method validation |
| `src/Frank.Alps/Excerpt.fsi`/`.fs` | Create | `CurrentStateResolver`, `contains`-ancestry state matching, `Alps.excerpt` |
| `Frank.sln` | Modify | Register `Frank.Alps`, `Frank.Alps.Tests`, `Frank.Alps.Sample` |
| `test/Frank.Alps.Tests/*` | Create | Unit, serialization, and `TestHost` integration tests |
| `sample/Frank.Alps.Sample/Program.fs` | Create | Demonstrates both `Link` headers (app-wide `rel="profile"`, per-resource `rel="profile"` with `type` param) |

---

### Task 1: Project scaffold + `DescriptorTypes`

**Files:**
- Create: `src/Frank.Alps/Frank.Alps.fsproj`
- Create: `src/Frank.Alps/DescriptorTypes.fsi`, `src/Frank.Alps/DescriptorTypes.fs`
- Create: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/DescriptorTypesTests.fs`, `test/Frank.Alps.Tests/Program.fs`
- Modify: `Frank.sln` (via `dotnet sln add`)

**Interfaces:**
- Consumes: nothing.
- Produces: `DescriptorType`, `DocFormat`, `Doc`, `Link`, `Ext`, `Descriptor`, `DescriptorRef` — exact shapes below.

- [ ] **Step 1: Create the package project structure**

```bash
mkdir -p "C:/Users/ryanr/Code/frank/src/Frank.Alps"
mkdir -p "C:/Users/ryanr/Code/frank/test/Frank.Alps.Tests"
```

Create `src/Frank.Alps/Frank.Alps.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>alps;hypermedia;rest;statechart</PackageTags>
    <Description>Hand-authored ALPS profile documents for Frank resources</Description>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="DescriptorTypes.fsi" />
    <Compile Include="DescriptorTypes.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Frank/Frank.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>Frank.Alps.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write `DescriptorTypes.fsi`**

```fsharp
namespace Frank.Alps

open System

/// The four ALPS descriptor kinds (draft-07 §2.2.16). Struct: data-free, no allocation.
[<Struct; RequireQualifiedAccess>]
type DescriptorType =
    | Semantic
    | Safe
    | Unsafe
    | Idempotent

/// Values of `doc`'s `format` attribute (draft-07 §2.2.5). Struct: data-free, no allocation.
[<Struct>]
type DocFormat =
    | Text
    | Html
    | Asciidoc
    | Markdown

/// A descriptor's `doc` element: free-form documentation text plus optional href/format/contentType/tag.
type Doc =
    { Value: string
      Href: Uri option
      Format: DocFormat option
      ContentType: string option
      Tag: string list }

/// An RFC 8288 web link on a descriptor -- distinct from a descriptor's own `href` (inheritance).
type Link =
    { Href: Uri
      Rel: string
      Title: string option
      Tag: string list }

/// A descriptor's `ext` element: author-specific extension data (draft-07 §2.2.6).
type Ext =
    { Id: string
      Href: Uri option
      Value: string option
      Tag: string list }

/// One ALPS descriptor. Self-referential: `Rt`, `Descriptors`, `From`, and (via `DescriptorRef`)
/// `InheritsFrom` all hold other `Descriptor` values directly, not string ids -- dangling references
/// are compile errors, not runtime failures. Deliberately not `[<Struct>]`: an 11-field record threaded
/// through every combinator and CE step would mean copying the whole record at each pipe step rather
/// than passing one reference (design doc, `[<Struct>]` section).
type Descriptor =
    { Id: string
      Name: string option
      Type: DescriptorType
      Def: Uri option
      Doc: Doc option
      Ext: Ext list
      InheritsFrom: DescriptorRef option
      Rt: Descriptor option
      From: Descriptor list
      Rel: string option
      Tag: string list
      Link: Link list
      Descriptors: Descriptor list }

/// Where a descriptor's `href` (inheritance) points: a value in this process, or a URI into a
/// document this codebase does not own (nothing to check against, so a bare Uri).
and DescriptorRef =
    | Local of Descriptor
    | External of Uri
```

- [ ] **Step 3: Write `DescriptorTypes.fs`**

```fsharp
namespace Frank.Alps

open System

[<Struct; RequireQualifiedAccess>]
type DescriptorType =
    | Semantic
    | Safe
    | Unsafe
    | Idempotent

[<Struct>]
type DocFormat =
    | Text
    | Html
    | Asciidoc
    | Markdown

type Doc =
    { Value: string
      Href: Uri option
      Format: DocFormat option
      ContentType: string option
      Tag: string list }

type Link =
    { Href: Uri
      Rel: string
      Title: string option
      Tag: string list }

type Ext =
    { Id: string
      Href: Uri option
      Value: string option
      Tag: string list }

type Descriptor =
    { Id: string
      Name: string option
      Type: DescriptorType
      Def: Uri option
      Doc: Doc option
      Ext: Ext list
      InheritsFrom: DescriptorRef option
      Rt: Descriptor option
      From: Descriptor list
      Rel: string option
      Tag: string list
      Link: Link list
      Descriptors: Descriptor list }

and DescriptorRef =
    | Local of Descriptor
    | External of Uri
```

- [ ] **Step 4: Write the test project**

Create `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`:

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
    <Compile Include="DescriptorTypesTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="Expecto" Version="10.*" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.*" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0-preview.1.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.Alps/Frank.Alps.fsproj" />
  </ItemGroup>

</Project>
```

Create `test/Frank.Alps.Tests/Program.fs`:

```fsharp
module Frank.Alps.Tests.Program

open Expecto

[<EntryPoint>]
let main argv = Tests.runTestsInAssemblyWithCLIArgs [] argv
```

Create `test/Frank.Alps.Tests/DescriptorTypesTests.fs`:

```fsharp
module Frank.Alps.Tests.DescriptorTypesTests

open Expecto
open Frank.Alps

let private emptyDescriptor (id: string) : Descriptor =
    { Id = id
      Name = None
      Type = DescriptorType.Semantic
      Def = None
      Doc = None
      Ext = []
      InheritsFrom = None
      Rt = None
      From = []
      Rel = None
      Tag = []
      Link = []
      Descriptors = [] }

[<Tests>]
let tests =
    testList
        "DescriptorTypes"
        [ test "a Descriptor can nest itself via Rt without a compiler error" {
              let target = emptyDescriptor "target"
              let d = { emptyDescriptor "source" with Rt = Some target }
              Expect.equal d.Rt.Value.Id "target" ""
          }

          test "a Descriptor can nest itself via Descriptors without a compiler error" {
              let child = emptyDescriptor "child"
              let d = { emptyDescriptor "parent" with Descriptors = [ child ] }
              Expect.equal d.Descriptors.Length 1 ""
          }

          test "DescriptorRef.Local holds a Descriptor value directly" {
              let target = emptyDescriptor "target"
              let d = { emptyDescriptor "source" with InheritsFrom = Some(DescriptorRef.Local target) }

              match d.InheritsFrom with
              | Some(DescriptorRef.Local t) -> Expect.equal t.Id "target" ""
              | _ -> failwith "expected Local"
          }

          test "DescriptorRef.External holds a bare Uri" {
              let uri = System.Uri "https://example.org/other-profile#thing"
              let d = { emptyDescriptor "source" with InheritsFrom = Some(DescriptorRef.External uri) }

              match d.InheritsFrom with
              | Some(DescriptorRef.External u) -> Expect.equal u uri ""
              | _ -> failwith "expected External"
          } ]
```

- [ ] **Step 5: Register both projects in the solution**

```bash
cd "C:/Users/ryanr/Code/frank"
dotnet sln Frank.sln add src/Frank.Alps/Frank.Alps.fsproj
dotnet sln Frank.sln add test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

- [ ] **Step 6: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: 4 tests pass. (Structural sanity tests on types just written — no meaningful red state to check first.)

- [ ] **Step 7: Commit**

```bash
git add Frank.sln src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): scaffold Frank.Alps package, add Descriptor and friends"
```

---

### Task 2: Constructors + simple field combinators

**Files:**
- Create: `src/Frank.Alps/Descriptor.fsi`, `src/Frank.Alps/Descriptor.fs`
- Modify: `src/Frank.Alps/Frank.Alps.fsproj` (add `Descriptor.fsi`/`.fs` after `DescriptorTypes.fs`)
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/ConstructorTests.fs`

**Interfaces:**
- Consumes: `Descriptor`, `DescriptorType`, `Doc`, `DocFormat` (Task 1).
- Produces: `semantic`/`safe`/`unsafe`/`idempotent: string -> Descriptor`, `doc: string -> Descriptor -> Descriptor`, `docWith: Doc -> Descriptor -> Descriptor`, `def: string -> Descriptor -> Descriptor`, `tag: string -> Descriptor -> Descriptor`, `rel: string -> Descriptor -> Descriptor`, `named: string -> Descriptor -> Descriptor`.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/ConstructorTests.fs`:

```fsharp
module Frank.Alps.Tests.ConstructorTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "Constructors and simple combinators"
        [ test "semantic sets Id and Type, everything else empty" {
              let d = semantic "product"
              Expect.equal d.Id "product" ""
              Expect.equal d.Type DescriptorType.Semantic ""
              Expect.equal d.Doc None ""
              Expect.equal d.Descriptors [] ""
          }

          test "safe/unsafe/idempotent set the expected Type" {
              Expect.equal (safe "listProducts").Type DescriptorType.Safe ""
              Expect.equal (unsafe "createProduct").Type DescriptorType.Unsafe ""
              Expect.equal (idempotent "replaceProduct").Type DescriptorType.Idempotent ""
          }

          test "doc sets a shorthand Doc with only Value populated" {
              let d = semantic "price" |> doc "Price in minor units"
              Expect.equal d.Doc.Value.Value "Price in minor units" ""
              Expect.equal d.Doc.Value.Href None ""
              Expect.equal d.Doc.Value.Format None ""
          }

          test "docWith sets the full Doc record verbatim" {
              let full =
                  { Value = "Price"
                    Href = Some(System.Uri "https://example.org/docs/price")
                    Format = Some DocFormat.Markdown
                    ContentType = Some "text/markdown"
                    Tag = [ "money" ] }

              let d = semantic "price" |> docWith full
              Expect.equal d.Doc.Value full ""
          }

          test "def sets Def as a parsed Uri" {
              let d = semantic "productId" |> def "https://schema.org/productID"
              Expect.equal d.Def.Value (System.Uri "https://schema.org/productID") ""
          }

          test "tag sets Tag" {
              let d = semantic "price" |> tag "money currency"
              Expect.equal d.Tag [ "money currency" ] ""
          }

          test "tag called twice appends, not replaces" {
              let d = semantic "price" |> tag "money" |> tag "currency"
              Expect.equal d.Tag [ "money"; "currency" ] ""
          }

          test "rel sets Rel" {
              let d = semantic "product" |> rel "tag:example.com,2026:product"
              Expect.equal d.Rel (Some "tag:example.com,2026:product") ""
          }

          test "named sets Name" {
              let d = semantic "productId" |> named "id"
              Expect.equal d.Name (Some "id") ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="DescriptorTypesTests.fs" />
    <Compile Include="ConstructorTests.fs" />
    <Compile Include="Program.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `semantic`/`doc`/`docWith`/`def`/`tag`/`rel`/`named` are not defined.

- [ ] **Step 3: Write `Descriptor.fsi`**

```fsharp
namespace Frank.Alps

open System

/// Constructs a bare `Descriptor` of the given `DescriptorType` -- `Id` set, everything else empty.
val private makeDescriptor: id: string -> descriptorType: DescriptorType -> Descriptor

/// A semantic (state/data) descriptor -- the spec's default `type` when omitted.
val semantic: id: string -> Descriptor

/// A safe (idempotent, side-effect-free) transition descriptor -- valid HTTP methods: GET, HEAD.
val safe: id: string -> Descriptor

/// An unsafe transition descriptor -- valid HTTP method: POST.
val unsafe: id: string -> Descriptor

/// An idempotent, non-safe transition descriptor -- valid HTTP methods: PUT, DELETE.
val idempotent: id: string -> Descriptor

/// Sets `doc` from plain text -- shorthand for the common case. Use `docWith` for href/format/contentType/tag.
val doc: text: string -> Descriptor -> Descriptor

/// Sets `doc` from a full `Doc` record.
val docWith: doc: Doc -> Descriptor -> Descriptor

/// Sets `def` -- the descriptor's source-definition IRI. Raises if `iri` isn't a well-formed absolute URI.
val def: iri: string -> Descriptor -> Descriptor

/// Appends a `tag` value (draft-07 §2.2.14: whitespace-separated list of non-unique values).
val tag: value: string -> Descriptor -> Descriptor

/// Sets `rel` -- an RFC 8288 relation type.
val rel: relation: string -> Descriptor -> Descriptor

/// Sets `name` -- rare; only for describing a pre-existing design where the descriptor's id conflicts
/// with another name (draft-07 §2.2.11).
val named: name: string -> Descriptor -> Descriptor
```

- [ ] **Step 4: Write `Descriptor.fs`**

```fsharp
namespace Frank.Alps

open System

module private Internal =
    let makeDescriptor (id: string) (descriptorType: DescriptorType) : Descriptor =
        { Id = id
          Name = None
          Type = descriptorType
          Def = None
          Doc = None
          Ext = []
          InheritsFrom = None
          Rt = None
          From = []
          Rel = None
          Tag = []
          Link = []
          Descriptors = [] }

open Internal

let semantic (id: string) : Descriptor = makeDescriptor id DescriptorType.Semantic
let safe (id: string) : Descriptor = makeDescriptor id DescriptorType.Safe
let unsafe (id: string) : Descriptor = makeDescriptor id DescriptorType.Unsafe
let idempotent (id: string) : Descriptor = makeDescriptor id DescriptorType.Idempotent

let doc (text: string) (d: Descriptor) : Descriptor =
    { d with
        Doc =
            Some
                { Value = text
                  Href = None
                  Format = None
                  ContentType = None
                  Tag = [] } }

let docWith (doc: Doc) (d: Descriptor) : Descriptor = { d with Doc = Some doc }

let def (iri: string) (d: Descriptor) : Descriptor = { d with Def = Some(Uri iri) }

let tag (value: string) (d: Descriptor) : Descriptor = { d with Tag = d.Tag @ [ value ] }

let rel (relation: string) (d: Descriptor) : Descriptor = { d with Rel = Some relation }

let named (name: string) (d: Descriptor) : Descriptor = { d with Name = Some name }
```

*Note:* `makeDescriptor` is `private` inside an `Internal` module rather than directly `private` at namespace scope — F# doesn't allow a bare `let private` function in a namespace to be referenced from a later-declared `.fsi`-exposed `val`'s implementation across the same file boundary as cleanly as a nested module does; this mirrors no specific prior file in this codebase but is standard F# for a namespace-scoped (not module-scoped) file. If this causes friction against `DescriptorTypes.fs`'s plain namespace style when actually compiling, move `Descriptor.fs`'s contents into a `[<AutoOpen>] module Descriptor` instead (matching `Frank.Rdf`'s `Rdf.fs` `[<AutoOpen>] module Rdf` shape) and drop the `Internal` wrapper — verify against a real build rather than assuming either shape compiles first try.

Update `src/Frank.Alps/Frank.Alps.fsproj`:

```xml
    <Compile Include="DescriptorTypes.fsi" />
    <Compile Include="DescriptorTypes.fs" />
    <Compile Include="Descriptor.fsi" />
    <Compile Include="Descriptor.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): semantic/safe/unsafe/idempotent constructors, doc/def/tag/rel/named"
```

---

### Task 3: `ext`/`extWith`, `link`/`linkWith`

**Files:**
- Modify: `src/Frank.Alps/Descriptor.fsi`, `src/Frank.Alps/Descriptor.fs`
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/ExtLinkTests.fs`

**Interfaces:**
- Consumes: `Descriptor`, `Ext`, `Link` (Task 1).
- Produces: `ext: string -> string -> Descriptor -> Descriptor`, `extWith: Ext -> Descriptor -> Descriptor`, `link: string -> string -> Descriptor -> Descriptor`, `linkWith: Link -> Descriptor -> Descriptor`.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/ExtLinkTests.fs`:

```fsharp
module Frank.Alps.Tests.ExtLinkTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "ext and link"
        [ test "ext appends an Ext with Id and Value set, Href/Tag empty" {
              let d = semantic "state" |> ext "https://frank-fs.github.io/alps-ext/example" "value"

              Expect.equal
                  d.Ext
                  [ { Id = "https://frank-fs.github.io/alps-ext/example"
                      Href = None
                      Value = Some "value"
                      Tag = [] } ]
                  ""
          }

          test "ext called twice appends both, order preserved" {
              let d = semantic "state" |> ext "a" "1" |> ext "b" "2"
              Expect.equal (d.Ext |> List.map (fun e -> e.Id)) [ "a"; "b" ] ""
          }

          test "extWith appends a full Ext record verbatim" {
              let full =
                  { Id = "https://frank-fs.github.io/alps-ext/example"
                    Href = Some(System.Uri "https://frank-fs.github.io/alps-ext/")
                    Value = Some "value"
                    Tag = [ "internal" ] }

              let d = semantic "state" |> extWith full
              Expect.equal d.Ext [ full ] ""
          }

          test "link appends a Link with Href and Rel set, Title/Tag empty" {
              let d = semantic "product" |> link "https://example.org/docs" "help"

              Expect.equal
                  d.Link
                  [ { Href = System.Uri "https://example.org/docs"
                      Rel = "help"
                      Title = None
                      Tag = [] } ]
                  ""
          }

          test "linkWith appends a full Link record verbatim" {
              let full =
                  { Href = System.Uri "https://example.org/docs"
                    Rel = "tag-doc"
                    Title = Some "Tag vocabulary"
                    Tag = [] }

              let d = semantic "product" |> linkWith full
              Expect.equal d.Link [ full ] ""
          }

          test "link called twice appends both" {
              let d = semantic "product" |> link "https://a" "help" |> link "https://b" "tag-doc"
              Expect.equal d.Link.Length 2 ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="ExtLinkTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `ext`/`extWith`/`link`/`linkWith` are not defined.

- [ ] **Step 3: Append to `Descriptor.fsi`**

```fsharp

/// Appends an `ext` element with `id` and `value` set (shorthand). Use `extWith` for href/tag.
val ext: id: string -> value: string -> Descriptor -> Descriptor

/// Appends a full `Ext` record verbatim.
val extWith: ext: Ext -> Descriptor -> Descriptor

/// Appends an RFC 8288 `link` element with `href` and `rel` set (shorthand). Use `linkWith` for title/tag.
/// Distinct from `href`/`hrefExternal` (descriptor inheritance) -- this is an arbitrary web link, e.g.
/// `rel="tag-doc"` per draft-07 §2.2.14's guidance for documenting tag vocabularies.
val link: href: string -> rel: string -> Descriptor -> Descriptor

/// Appends a full `Link` record verbatim.
val linkWith: link: Link -> Descriptor -> Descriptor
```

- [ ] **Step 4: Append to `Descriptor.fs`**

```fsharp

let ext (id: string) (value: string) (d: Descriptor) : Descriptor =
    { d with
        Ext =
            d.Ext
            @ [ { Id = id
                  Href = None
                  Value = Some value
                  Tag = [] } ] }

let extWith (ext: Ext) (d: Descriptor) : Descriptor = { d with Ext = d.Ext @ [ ext ] }

let link (href: string) (rel: string) (d: Descriptor) : Descriptor =
    { d with
        Link =
            d.Link
            @ [ { Href = Uri href
                  Rel = rel
                  Title = None
                  Tag = [] } ] }

let linkWith (link: Link) (d: Descriptor) : Descriptor = { d with Link = d.Link @ [ link ] }
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): ext/extWith, link/linkWith combinators"
```

---

### Task 4: `contains` — general nesting

**Files:**
- Modify: `src/Frank.Alps/Descriptor.fsi`, `src/Frank.Alps/Descriptor.fs`
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/ContainsTests.fs`

**Interfaces:**
- Consumes: `Descriptor` (Task 1).
- Produces: `contains: Descriptor list -> Descriptor -> Descriptor`.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/ContainsTests.fs`:

```fsharp
module Frank.Alps.Tests.ContainsTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "contains"
        [ test "contains sets Descriptors to the given list, in order" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let d = semantic "parent" |> contains [ a; b; c ]
              Expect.equal (d.Descriptors |> List.map (fun x -> x.Id)) [ "a"; "b"; "c" ] ""
          }

          test "contains accepts children of any DescriptorType, not just semantic" {
              // draft-07 §2.2.4: any descriptor type may nest under any other. This is what leaves
              // room for composite/substate hierarchy later -- contains is untyped by design.
              let child = safe "listChildren"
              let d = semantic "parent" |> contains [ child ]
              Expect.equal d.Descriptors.[0].Type DescriptorType.Safe ""
          }

          test "contains called on an already-contains'd descriptor replaces Descriptors, not appends" {
              // Unlike tag/ext/link (append-only, multiple calls compose), contains sets the whole
              // nested-descriptor list at once -- there is exactly one `descriptor` array per parent
              // in the wire format, so a second call is a deliberate replacement, not an accumulation.
              let a, b = semantic "a", semantic "b"
              let d = semantic "parent" |> contains [ a ] |> contains [ b ]
              Expect.equal (d.Descriptors |> List.map (fun x -> x.Id)) [ "b" ] ""
          }

          test "nesting is recursive: a contains'd child can itself contain further children" {
              let grandchild = semantic "grandchild"
              let child = semantic "child" |> contains [ grandchild ]
              let d = semantic "parent" |> contains [ child ]
              Expect.equal d.Descriptors.[0].Descriptors.[0].Id "grandchild" ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="ContainsTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `contains` is not defined.

- [ ] **Step 3: Append to `Descriptor.fsi`**

```fsharp

/// Sets the nested `descriptor` array (draft-07 §2.2.4). Deliberately untyped by child `DescriptorType`
/// -- any descriptor may nest under any other. Replaces any previously-set `Descriptors`, unlike the
/// append-only `tag`/`ext`/`link` -- there is exactly one nested-descriptor array per parent.
val contains: children: Descriptor list -> Descriptor -> Descriptor
```

- [ ] **Step 4: Append to `Descriptor.fs`**

```fsharp

let contains (children: Descriptor list) (d: Descriptor) : Descriptor = { d with Descriptors = children }
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): contains -- general, untyped nesting"
```

---

### Task 5: `rt`, `href`, `hrefExternal`

**Files:**
- Modify: `src/Frank.Alps/Descriptor.fsi`, `src/Frank.Alps/Descriptor.fs`
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/ReferenceTests.fs`

**Interfaces:**
- Consumes: `Descriptor`, `DescriptorRef` (Task 1).
- Produces: `rt: Descriptor -> Descriptor -> Descriptor`, `href: Descriptor -> Descriptor -> Descriptor`, `hrefExternal: string -> Descriptor -> Descriptor`.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/ReferenceTests.fs`:

```fsharp
module Frank.Alps.Tests.ReferenceTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "rt, href, hrefExternal"
        [ test "rt sets Rt to the target descriptor value, not a string" {
              let product = semantic "product"
              let d = safe "listProducts" |> rt product
              Expect.equal d.Rt.Value.Id "product" ""
          }

          test "href sets InheritsFrom to DescriptorRef.Local wrapping the target" {
              let shared = semantic "shared"
              let d = semantic "local" |> href shared

              match d.InheritsFrom with
              | Some(DescriptorRef.Local t) -> Expect.equal t.Id "shared" ""
              | _ -> failwith "expected Local"
          }

          test "hrefExternal sets InheritsFrom to DescriptorRef.External wrapping a parsed Uri" {
              let d = semantic "local" |> hrefExternal "https://example.org/other-profile#shared"

              match d.InheritsFrom with
              | Some(DescriptorRef.External u) ->
                  Expect.equal u (System.Uri "https://example.org/other-profile#shared") ""
              | _ -> failwith "expected External"
          }

          test "rt and href/hrefExternal are independent fields -- setting one doesn't clear the other" {
              let product = semantic "product"
              let shared = semantic "shared"
              let d = safe "listProducts" |> rt product |> href shared
              Expect.isTrue d.Rt.IsSome ""
              Expect.isTrue d.InheritsFrom.IsSome ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="ReferenceTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `rt`/`href`/`hrefExternal` are not defined.

- [ ] **Step 3: Append to `Descriptor.fsi`**

```fsharp

/// Sets `rt` -- the target resource type/state for a safe/unsafe/idempotent transition (draft-07
/// §2.2.13). Descriptor-typed: a dangling reference is a compile error, not a wrong document.
val rt: target: Descriptor -> Descriptor -> Descriptor

/// Sets `href` (inheritance) to a descriptor value in this process. Compile-checked, same discipline
/// as `rt`. Neither this nor `hrefExternal` has a real caller until multi-document profiles exist
/// (frank-fs/frank#488) -- both exist now so `Descriptor` doesn't need a breaking field change later.
val href: target: Descriptor -> Descriptor -> Descriptor

/// Sets `href` (inheritance) to a URI into a document this codebase doesn't own. Nothing to check
/// against, so a bare string/URI -- the same reasoning that makes a descriptor's own `id` a string.
val hrefExternal: uri: string -> Descriptor -> Descriptor
```

- [ ] **Step 4: Append to `Descriptor.fs`**

```fsharp

let rt (target: Descriptor) (d: Descriptor) : Descriptor = { d with Rt = Some target }

let href (target: Descriptor) (d: Descriptor) : Descriptor =
    { d with
        InheritsFrom = Some(DescriptorRef.Local target) }

let hrefExternal (uri: string) (d: Descriptor) : Descriptor =
    { d with
        InheritsFrom = Some(DescriptorRef.External(Uri uri)) }
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): rt, href, hrefExternal"
```

---

### Task 6: `initial`, `regions`, `StateComposition`

**Files:**
- Modify: `src/Frank.Alps/Descriptor.fsi`, `src/Frank.Alps/Descriptor.fs` (adds validation to `contains` from Task 4)
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/CompositeStateTests.fs`

**Interfaces:**
- Consumes: `Descriptor`, `contains` (Tasks 1, 4).
- Produces: `initial: Descriptor -> Descriptor`, `regions: Descriptor list -> Descriptor -> Descriptor`, `StateComposition` (`Leaf | Alternatives of Descriptor list | Regions of Descriptor list`), `StateComposition.ofDescriptor: Descriptor -> StateComposition`, `StateComposition.initialChild: Descriptor -> Descriptor option`. Modifies `contains` to raise if more than one direct child carries the `initial` marker.

**Background you need:**

Both `initial` and `regions` ride the `ext` mechanism under the `https://frank-fs.github.io/alps-ext/` namespace already established by PR #165/#214 (`protocolState`/`availableInStates`, not touched by this task). `initial` appends an `Ext` with `Id = "https://frank-fs.github.io/alps-ext/initial"` to the *child* descriptor. `regions` is `contains` plus an `Ext` with `Id = "https://frank-fs.github.io/alps-ext/orthogonal"` on the *parent* — same `Descriptors` field as `contains`, no shape change. Neither one goes through `contains`'s new validation from this task in the `regions` case — the "at most one initial" rule is specific to OR-decomposition (`contains`); an AND-region composition has no "default" to disambiguate, so `regions` does not call the validating path.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/CompositeStateTests.fs`:

```fsharp
module Frank.Alps.Tests.CompositeStateTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "initial, regions, StateComposition"
        [ test "initial appends the canonical ext marker" {
              let d = semantic "waitingForPlayer" |> initial

              Expect.contains
                  d.Ext
                  { Id = "https://frank-fs.github.io/alps-ext/initial"
                    Href = None
                    Value = None
                    Tag = [] }
                  ""
          }

          test "contains raises when more than one direct child is marked initial" {
              let a = semantic "a" |> initial
              let b = semantic "b" |> initial

              Expect.throws (fun () -> semantic "parent" |> contains [ a; b ] |> ignore) ""
          }

          test "contains does not raise with zero or one initial child" {
              let a = semantic "a" |> initial
              let b = semantic "b"
              semantic "parent" |> contains [ a; b ] |> ignore
              semantic "parent" |> contains [ b ] |> ignore
          }

          test "regions sets Descriptors like contains, plus the orthogonal ext marker on the parent" {
              let network = semantic "network"
              let session = semantic "session"
              let d = semantic "inGame" |> regions [ network; session ]

              Expect.equal (d.Descriptors |> List.map (fun x -> x.Id)) [ "network"; "session" ] ""

              Expect.contains
                  d.Ext
                  { Id = "https://frank-fs.github.io/alps-ext/orthogonal"
                    Href = None
                    Value = None
                    Tag = [] }
                  ""
          }

          test "regions does not enforce the at-most-one-initial rule" {
              let a = semantic "a" |> initial
              let b = semantic "b" |> initial
              semantic "parent" |> regions [ a; b ] |> ignore
          }

          test "StateComposition.ofDescriptor: a descriptor with no Descriptors is Leaf" {
              Expect.equal (StateComposition.ofDescriptor (semantic "x")) StateComposition.Leaf ""
          }

          test "StateComposition.ofDescriptor: contains without the orthogonal marker is Alternatives" {
              let d = semantic "open" |> contains [ semantic "a"; semantic "b" ]

              match StateComposition.ofDescriptor d with
              | StateComposition.Alternatives children -> Expect.equal children.Length 2 ""
              | other -> failwithf "expected Alternatives, got %A" other
          }

          test "StateComposition.ofDescriptor: regions is Regions" {
              let d = semantic "inGame" |> regions [ semantic "network"; semantic "session" ]

              match StateComposition.ofDescriptor d with
              | StateComposition.Regions children -> Expect.equal children.Length 2 ""
              | other -> failwithf "expected Regions, got %A" other
          }

          test "StateComposition.initialChild finds the marked child among Alternatives" {
              let waiting = semantic "waitingForPlayer" |> initial
              let inProgress = semantic "inProgress"
              let d = semantic "open" |> contains [ waiting; inProgress ]

              Expect.equal (StateComposition.initialChild d) (Some waiting) ""
          }

          test "StateComposition.initialChild is None when no child is marked" {
              let d = semantic "open" |> contains [ semantic "a"; semantic "b" ]
              Expect.equal (StateComposition.initialChild d) None ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="CompositeStateTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `initial`/`regions`/`StateComposition` are not defined.

- [ ] **Step 3: Append to `Descriptor.fsi`**

```fsharp

/// Two of the canonical Frank.Alps ext ids under the shared https://frank-fs.github.io/alps-ext/
/// namespace (protocolState/availableInStates, from PR #165/#214, are declared in Serialization.fsi --
/// Task 8, alongside the projection logic that's their only user).
[<Literal>]
val InitialExtId: string = "https://frank-fs.github.io/alps-ext/initial"

[<Literal>]
val OrthogonalExtId: string = "https://frank-fs.github.io/alps-ext/orthogonal"

/// Marks this descriptor as the default child entered when its parent (a composite state) is targeted
/// without naming a substate. No native ALPS property -- rides `ext` under `InitialExtId`. Any
/// ALPS-agnostic reader ignores the unrecognized ext element; the document stays fully spec-valid.
val initial: Descriptor -> Descriptor

/// Orthogonal (AND) composition, distinct from `contains`'s OR/substate decomposition: `regions
/// [a; b]` means being in the parent implies being concurrently in some state within *each* of `a`
/// and `b`. Same `Descriptors` field as `contains`, plus `OrthogonalExtId` on the parent -- no
/// `Descriptor` shape change. Does not enforce `contains`'s at-most-one-`initial` rule: an AND-region
/// composition has no single default to disambiguate.
val regions: children: Descriptor list -> Descriptor -> Descriptor

/// Whether a descriptor's nested `Descriptors` are OR-alternatives (substates -- exactly one is
/// current) or AND-regions (orthogonal -- all are concurrently current), derived by reading the
/// `OrthogonalExtId` marker `regions` sets. Purely a read of already-authored data -- no runtime
/// execution.
[<RequireQualifiedAccess>]
type StateComposition =
    | Leaf
    | Alternatives of Descriptor list
    | Regions of Descriptor list

module StateComposition =
    val ofDescriptor: Descriptor -> StateComposition

    /// The child marked `initial`, if any. Meaningful only when `ofDescriptor` returns `Alternatives`
    /// -- an AND-region composition has no single default child.
    val initialChild: Descriptor -> Descriptor option
```

- [ ] **Step 4: Append to `Descriptor.fs`**

Replace the existing `contains` definition (Task 4) with:

```fsharp

[<Literal>]
let InitialExtId = "https://frank-fs.github.io/alps-ext/initial"

[<Literal>]
let OrthogonalExtId = "https://frank-fs.github.io/alps-ext/orthogonal"

let private hasExtId (extId: string) (d: Descriptor) : bool =
    d.Ext |> List.exists (fun e -> e.Id = extId)

let contains (children: Descriptor list) (d: Descriptor) : Descriptor =
    let initialCount = children |> List.filter (hasExtId InitialExtId) |> List.length

    if initialCount > 1 then
        failwithf
            "Frank.Alps: descriptor '%s' has %d children marked `initial`, at most one is allowed"
            d.Id
            initialCount

    { d with Descriptors = children }

let initial (d: Descriptor) : Descriptor =
    { d with
        Ext =
            d.Ext
            @ [ { Id = InitialExtId
                  Href = None
                  Value = None
                  Tag = [] } ] }

let regions (children: Descriptor list) (d: Descriptor) : Descriptor =
    { d with
        Descriptors = children
        Ext =
            d.Ext
            @ [ { Id = OrthogonalExtId
                  Href = None
                  Value = None
                  Tag = [] } ] }

[<RequireQualifiedAccess>]
type StateComposition =
    | Leaf
    | Alternatives of Descriptor list
    | Regions of Descriptor list

module StateComposition =
    let ofDescriptor (d: Descriptor) : StateComposition =
        match d.Descriptors with
        | [] -> StateComposition.Leaf
        | children when hasExtId OrthogonalExtId d -> StateComposition.Regions children
        | children -> StateComposition.Alternatives children

    let initialChild (d: Descriptor) : Descriptor option =
        match ofDescriptor d with
        | StateComposition.Alternatives children -> children |> List.tryFind (hasExtId InitialExtId)
        | StateComposition.Regions _
        | StateComposition.Leaf -> None
```

*Note:* moving `contains`'s definition means it must now come after `hasExtId`/`InitialExtId` in file order (`.fs` files are order-sensitive) — place the `InitialExtId`/`OrthogonalExtId`/`hasExtId` block immediately before `contains`, replacing the old standalone `contains` from Task 4 in place, not appending a second definition (F# would otherwise shadow rather than error, silently leaving the old unvalidated `contains` dead but confusing).

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass, including `ContainsTests.fs` from Task 4 (still valid — validation only rejects the >1-initial case, which none of those tests trigger).

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): initial, regions, StateComposition; contains validates at-most-one-initial"
```

---

### Task 7: `from`

**Files:**
- Modify: `src/Frank.Alps/Descriptor.fsi`, `src/Frank.Alps/Descriptor.fs`
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/FromTests.fs`

**Interfaces:**
- Consumes: `Descriptor` (Task 1).
- Produces: `from: Descriptor list -> Descriptor -> Descriptor`.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/FromTests.fs`:

```fsharp
module Frank.Alps.Tests.FromTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "from"
        [ test "from sets the From field to the given descriptor list" {
              let openState = semantic "open"
              let closedState = semantic "closed"
              let d = unsafe "makeMove" |> from [ openState; closedState ]
              Expect.equal (d.From |> List.map (fun x -> x.Id)) [ "open"; "closed" ] ""
          }

          test "a transition with no from has an empty From list" {
              let d = safe "viewResult"
              Expect.equal d.From [] ""
          }

          test "from and rt are independent fields" {
              let openState = semantic "open"
              let game = semantic "game"
              let d = safe "viewGame" |> from [ openState ] |> rt game
              Expect.equal d.From.Length 1 ""
              Expect.isTrue d.Rt.IsSome ""
          }

          test "from replaces, not appends, on a second call" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let d = unsafe "x" |> from [ a ] |> from [ b; c ]
              Expect.equal (d.From |> List.map (fun x -> x.Id)) [ "b"; "c" ] ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="FromTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `from` is not defined.

- [ ] **Step 3: Append to `Descriptor.fsi`**

```fsharp

/// Marks a safe/unsafe/idempotent transition as valid only from the given source state(s). Not an
/// ALPS property -- sets `From`, a Frank.Alps-only field. A transition with no `from` (`From = []`) is
/// never filtered by state -- graceful degradation, matching how a transition with no auth requirement
/// is never filtered by authorization. Serialization (Task 8) projects a non-empty `From` into one
/// `protocolState`/`availableInStates` ext pair per declared state -- `From` itself is not serialized
/// as ext directly.
val from: sources: Descriptor list -> Descriptor -> Descriptor
```

- [ ] **Step 4: Append to `Descriptor.fs`**

```fsharp

let from (sources: Descriptor list) (d: Descriptor) : Descriptor = { d with From = sources }
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): from -- source-state(s) for a transition"
```

---

### Task 8: `ProtocolGraph`

**Files:**
- Create: `src/Frank.Alps/ProtocolGraph.fsi`, `src/Frank.Alps/ProtocolGraph.fs`
- Modify: `src/Frank.Alps/Frank.Alps.fsproj` (add after `Descriptor.fs`)
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/ProtocolGraphTests.fs`

**Interfaces:**
- Consumes: `Descriptor` (Task 1), `from`/`rt`/`contains` (Tasks 4, 5, 7).
- Produces: `ProtocolTransition = { FromState: Descriptor; Transition: Descriptor; ToState: Descriptor }`, `ProtocolGraph.ofProfile: Descriptor list -> ProtocolTransition list`.

**Background you need:**

`ofProfile` walks the *entire* descriptor tree reachable from the given list, not just its top-level elements — a transition could be nested via `contains` under some other descriptor rather than sitting as a top-level sibling, and there's no reason to silently miss it. Walk `Descriptors` recursively (not `Rt`/`From`/`InheritsFrom` — those are *references*, not containment, and walking them would visit the same handful of state/transition descriptors from every edge that points at them, producing duplicate or wrong results). A descriptor declaring **both** `From <> []` and `Rt = Some _` yields one `ProtocolTransition` per element of `From`; anything else yields none.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/ProtocolGraphTests.fs`:

```fsharp
module Frank.Alps.Tests.ProtocolGraphTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "ProtocolGraph.ofProfile"
        [ test "a transition with from and rt yields one edge" {
              let openState = semantic "open"
              let move = semantic "move"
              let makeMove = unsafe "makeMove" |> from [ openState ] |> rt move

              let edges = ProtocolGraph.ofProfile [ openState; move; makeMove ]

              Expect.equal edges.Length 1 ""
              Expect.equal edges.[0].FromState.Id "open" ""
              Expect.equal edges.[0].Transition.Id "makeMove" ""
              Expect.equal edges.[0].ToState.Id "move" ""
          }

          test "from [A; B] |> rt C yields two edges, one per source state" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let t = unsafe "t" |> from [ a; b ] |> rt c

              let edges = ProtocolGraph.ofProfile [ a; b; c; t ]

              Expect.equal edges.Length 2 ""
              Expect.equal (edges |> List.map (fun e -> e.FromState.Id) |> List.sort) [ "a"; "b" ] ""
              Expect.isTrue (edges |> List.forall (fun e -> e.ToState.Id = "c")) ""
          }

          test "a transition with from but no rt yields no edge" {
              let openState = semantic "open"
              let t = unsafe "t" |> from [ openState ]
              Expect.equal (ProtocolGraph.ofProfile [ openState; t ]) [] ""
          }

          test "a transition with rt but no from yields no edge" {
              let move = semantic "move"
              let t = unsafe "t" |> rt move
              Expect.equal (ProtocolGraph.ofProfile [ move; t ]) [] ""
          }

          test "a plain semantic descriptor with neither yields no edge" {
              Expect.equal (ProtocolGraph.ofProfile [ semantic "x" ]) [] ""
          }

          test "a transition nested via contains is still found" {
              let openState = semantic "open"
              let move = semantic "move"
              let makeMove = unsafe "makeMove" |> from [ openState ] |> rt move
              let resource = semantic "resource" |> contains [ makeMove ]

              let edges = ProtocolGraph.ofProfile [ openState; move; resource ]

              Expect.equal edges.Length 1 ""
              Expect.equal edges.[0].Transition.Id "makeMove" ""
          }

          test "an empty profile yields no edges" { Expect.equal (ProtocolGraph.ofProfile []) [] "" } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="ProtocolGraphTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `ProtocolTransition`/`ProtocolGraph` are not defined.

- [ ] **Step 3: Write `ProtocolGraph.fsi`**

```fsharp
namespace Frank.Alps

/// One edge in the protocol graph derived from authored descriptors: `Transition` is valid from
/// `FromState`, and moves to `ToState`. Traced to
/// https://wizardsofsmart.wordpress.com/2018/12/05/state-transitions-through-sequence-diagrams/'s
/// `Transition<'State,'Message> = { FromState; Message; ToState }`, generalized to `Descriptor`.
type ProtocolTransition =
    { FromState: Descriptor
      Transition: Descriptor
      ToState: Descriptor }

module ProtocolGraph =
    /// Derives every ProtocolTransition edge from a profile's authored descriptors, walking nested
    /// `Descriptors` recursively. A descriptor declaring both `From` (non-empty) and `Rt` (`Some`)
    /// yields one edge per `From` element; anything else yields none.
    val ofProfile: profile: Descriptor list -> ProtocolTransition list
```

- [ ] **Step 4: Write `ProtocolGraph.fs`**

```fsharp
namespace Frank.Alps

type ProtocolTransition =
    { FromState: Descriptor
      Transition: Descriptor
      ToState: Descriptor }

module ProtocolGraph =
    let rec private flatten (d: Descriptor) : Descriptor list = d :: (d.Descriptors |> List.collect flatten)

    let ofProfile (profile: Descriptor list) : ProtocolTransition list =
        profile
        |> List.collect flatten
        |> List.collect (fun d ->
            match d.Rt with
            | Some toState when not (List.isEmpty d.From) ->
                d.From
                |> List.map (fun fromState ->
                    { FromState = fromState
                      Transition = d
                      ToState = toState })
            | _ -> [])
```

Update `src/Frank.Alps/Frank.Alps.fsproj`:

```xml
    <Compile Include="Descriptor.fsi" />
    <Compile Include="Descriptor.fs" />
    <Compile Include="ProtocolGraph.fsi" />
    <Compile Include="ProtocolGraph.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): ProtocolGraph.ofProfile derives ProtocolTransition edges"
```

---

### Task 9: `descriptor { }` — `DescriptorBuilder`

**Files:**
- Create: `src/Frank.Alps/DescriptorBuilder.fsi`, `src/Frank.Alps/DescriptorBuilder.fs`
- Modify: `src/Frank.Alps/Frank.Alps.fsproj` (add after `ProtocolGraph.fs`)
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/DescriptorBuilderTests.fs`

**Interfaces:**
- Consumes: `Descriptor`, every combinator from Tasks 2-7 (`doc`/`docWith`/`def`/`tag`/`rel`/`named`/`ext`/`extWith`/`link`/`linkWith`/`contains`/`rt`/`href`/`hrefExternal`/`initial`/`regions`/`from`).
- Produces: `DescriptorBuilder` (sealed type, one `[<CustomOperation>]` per combinator above plus `semantic`/`safe`/`unsafe`/`idempotent`), `val descriptor: id: string -> DescriptorBuilder`.

**Background you need:**

This mirrors `Frank.Rdf`'s `DescribeBuilder`/`describe` exactly (`src/Frank.Rdf/Rdf.fsi`/`.fs`, already in this repo) — one accumulator, no `Combine`/`Delay`, `Run` returns a plain value. Two non-obvious requirements from that precedent, both load-bearing:

1. **`Yield` must be generic (`'a -> Descriptor`), not `unit -> Descriptor`.** F#'s custom operations desugar `descriptor "id" { doc "x" }` into `b.Doc(b.Yield(()), "x")` — `Yield` is invoked with an explicit unit-typed seed value. A signature file has no syntax distinguishing a member taking a real `unit`-typed argument from a nullary member, so `member Yield: unit -> Descriptor` only ever matches a nullary `Yield()` implementation, which can't be *called* with the seed value the desugaring passes. Write `member _.Yield(_) : Descriptor = ...` (no type annotation on the parameter) in the `.fs`, and `member Yield: 'a -> Descriptor` in the `.fsi`.
2. **`Zero` is required**, despite not otherwise appearing in this design. An entirely-`()`-bodied block (`descriptor "id" { () }`, no custom operation, nothing yielded) desugars to `b.Zero()`, not `b.Yield(())` — omitting `Zero` fails with FS0708 ("this control construct may only be used if the computation expression builder defines a 'Zero' method").

`semantic`/`safe`/`unsafe`/`idempotent` become zero-*extra*-argument custom operations here — they still take the threaded `Descriptor` state (every custom operation does), just no additional user-supplied argument, exactly like F#'s own `query { }` builder's `distinct`. They set `Type`; `Yield`'s seed already defaults `Type` to `DescriptorType.Semantic` (draft-07 §2.2.16's own stated default), so an unstated kind is correct without calling any of the four.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/DescriptorBuilderTests.fs`:

```fsharp
module Frank.Alps.Tests.DescriptorBuilderTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "descriptor { }"
        [ test "an empty block defaults to Type = Semantic, everything else empty" {
              let d = descriptor "productId" { () }
              Expect.equal d.Id "productId" ""
              Expect.equal d.Type DescriptorType.Semantic ""
              Expect.equal d.Doc None ""
          }

          test "semantic/safe/unsafe/idempotent as custom operations set Type" {
              Expect.equal (descriptor "a" { semantic }).Type DescriptorType.Semantic ""
              Expect.equal (descriptor "a" { safe }).Type DescriptorType.Safe ""
              Expect.equal (descriptor "a" { unsafe }).Type DescriptorType.Unsafe ""
              Expect.equal (descriptor "a" { idempotent }).Type DescriptorType.Idempotent ""
          }

          test "doc/def/tag/rel/named compose in one block" {
              let d =
                  descriptor "productId" {
                      def "https://schema.org/productID"
                      doc "The product's id"
                      tag "core"
                      rel "self"
                      named "id"
                  }

              Expect.equal d.Def.Value (System.Uri "https://schema.org/productID") ""
              Expect.equal d.Doc.Value.Value "The product's id" ""
              Expect.equal d.Tag [ "core" ] ""
              Expect.equal d.Rel (Some "self") ""
              Expect.equal d.Name (Some "id") ""
          }

          test "contains, rt, from, initial, regions all work as custom operations" {
              let productId = descriptor "productId" { def "https://schema.org/productID" }
              let product = descriptor "product" { contains [ productId ] }
              let openState = descriptor "open" { () }
              let closedState = descriptor "closed" { () }

              let listProducts = descriptor "listProducts" { safe; rt product }

              let makeMove =
                  descriptor "makeMove" {
                      unsafe
                      from [ openState ]
                      rt closedState
                  }

              let waitingForPlayer = descriptor "waitingForPlayer" { initial }
              let inGame = descriptor "inGame" { regions [ openState; closedState ] }

              Expect.equal listProducts.Rt.Value.Id "product" ""
              Expect.equal makeMove.From.[0].Id "open" ""
              Expect.equal makeMove.Rt.Value.Id "closed" ""
              Expect.isTrue (waitingForPlayer.Ext |> List.exists (fun e -> e.Id = InitialExtId)) ""
              Expect.isTrue (inGame.Ext |> List.exists (fun e -> e.Id = OrthogonalExtId)) ""
          }

          test "href, hrefExternal, ext, extWith, link, linkWith, docWith all work as custom operations" {
              let shared = descriptor "shared" { () }

              let d =
                  descriptor "local" {
                      href shared
                      ext "x" "1"
                      link "https://example.org" "help"
                  }

              Expect.isTrue d.InheritsFrom.IsSome ""
              Expect.equal d.Ext.Length 1 ""
              Expect.equal d.Link.Length 1 ""

              let e = descriptor "external" { hrefExternal "https://example.org/other#thing" }
              Expect.isTrue e.InheritsFrom.IsSome ""
          }

          test "the same profile built via |> and via descriptor { } is structurally equal" {
              let viaPlain = semantic "productId" |> def "https://schema.org/productID" |> doc "The id"
              let viaCe = descriptor "productId" { def "https://schema.org/productID"; doc "The id" }
              Expect.equal viaPlain viaCe ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="DescriptorBuilderTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `descriptor` is not defined.

- [ ] **Step 3: Write `DescriptorBuilder.fsi`**

```fsharp
namespace Frank.Alps

open System

/// Builds a `Descriptor` via computation expression, as an alternative to plain `|>` combinators --
/// both produce identical `Descriptor` values. Mirrors `Frank.Rdf`'s `DescribeBuilder`/`describe`
/// exactly: one accumulator, no `Combine`/`Delay`, `Run` returns a plain value.
[<Sealed>]
type DescriptorBuilder =
    new: id: string -> DescriptorBuilder
    member Yield: 'a -> Descriptor
    member Zero: unit -> Descriptor
    member Run: d: Descriptor -> Descriptor

    [<CustomOperation("semantic")>]
    member Semantic: d: Descriptor -> Descriptor

    [<CustomOperation("safe")>]
    member Safe: d: Descriptor -> Descriptor

    [<CustomOperation("unsafe")>]
    member Unsafe: d: Descriptor -> Descriptor

    [<CustomOperation("idempotent")>]
    member Idempotent: d: Descriptor -> Descriptor

    [<CustomOperation("doc")>]
    member Doc: d: Descriptor * text: string -> Descriptor

    [<CustomOperation("docWith")>]
    member DocWith: d: Descriptor * doc: Doc -> Descriptor

    [<CustomOperation("def")>]
    member Def: d: Descriptor * iri: string -> Descriptor

    [<CustomOperation("tag")>]
    member Tag: d: Descriptor * value: string -> Descriptor

    [<CustomOperation("rel")>]
    member Rel: d: Descriptor * relation: string -> Descriptor

    [<CustomOperation("named")>]
    member Named: d: Descriptor * name: string -> Descriptor

    [<CustomOperation("ext")>]
    member Ext: d: Descriptor * id: string * value: string -> Descriptor

    [<CustomOperation("extWith")>]
    member ExtWith: d: Descriptor * ext: Ext -> Descriptor

    [<CustomOperation("link")>]
    member Link: d: Descriptor * href: string * rel: string -> Descriptor

    [<CustomOperation("linkWith")>]
    member LinkWith: d: Descriptor * link: Link -> Descriptor

    [<CustomOperation("contains")>]
    member Contains: d: Descriptor * children: Descriptor list -> Descriptor

    [<CustomOperation("rt")>]
    member Rt: d: Descriptor * target: Descriptor -> Descriptor

    [<CustomOperation("href")>]
    member Href: d: Descriptor * target: Descriptor -> Descriptor

    [<CustomOperation("hrefExternal")>]
    member HrefExternal: d: Descriptor * uri: string -> Descriptor

    [<CustomOperation("initial")>]
    member Initial: d: Descriptor -> Descriptor

    [<CustomOperation("regions")>]
    member Regions: d: Descriptor * children: Descriptor list -> Descriptor

    [<CustomOperation("from")>]
    member From: d: Descriptor * sources: Descriptor list -> Descriptor

/// Enters a `descriptor { }` block: `descriptor "listProducts" { safe; rt product }`.
val descriptor: id: string -> DescriptorBuilder
```

- [ ] **Step 4: Write `DescriptorBuilder.fs`**

```fsharp
namespace Frank.Alps

[<Sealed>]
type DescriptorBuilder(id: string) =
    member _.Yield(_) : Descriptor = semantic id
    member _.Zero() : Descriptor = semantic id
    member _.Run(d: Descriptor) : Descriptor = d

    [<CustomOperation("semantic")>]
    member _.Semantic(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Semantic }

    [<CustomOperation("safe")>]
    member _.Safe(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Safe }

    [<CustomOperation("unsafe")>]
    member _.Unsafe(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Unsafe }

    [<CustomOperation("idempotent")>]
    member _.Idempotent(d: Descriptor) : Descriptor = { d with Type = DescriptorType.Idempotent }

    [<CustomOperation("doc")>]
    member _.Doc(d: Descriptor, text: string) : Descriptor = d |> doc text

    [<CustomOperation("docWith")>]
    member _.DocWith(d: Descriptor, docValue: Doc) : Descriptor = d |> docWith docValue

    [<CustomOperation("def")>]
    member _.Def(d: Descriptor, iri: string) : Descriptor = d |> def iri

    [<CustomOperation("tag")>]
    member _.Tag(d: Descriptor, value: string) : Descriptor = d |> tag value

    [<CustomOperation("rel")>]
    member _.Rel(d: Descriptor, relation: string) : Descriptor = d |> rel relation

    [<CustomOperation("named")>]
    member _.Named(d: Descriptor, name: string) : Descriptor = d |> named name

    [<CustomOperation("ext")>]
    member _.Ext(d: Descriptor, extId: string, value: string) : Descriptor = d |> ext extId value

    [<CustomOperation("extWith")>]
    member _.ExtWith(d: Descriptor, extValue: Ext) : Descriptor = d |> extWith extValue

    [<CustomOperation("link")>]
    member _.Link(d: Descriptor, href: string, rel: string) : Descriptor = d |> link href rel

    [<CustomOperation("linkWith")>]
    member _.LinkWith(d: Descriptor, linkValue: Link) : Descriptor = d |> linkWith linkValue

    [<CustomOperation("contains")>]
    member _.Contains(d: Descriptor, children: Descriptor list) : Descriptor = d |> contains children

    [<CustomOperation("rt")>]
    member _.Rt(d: Descriptor, target: Descriptor) : Descriptor = d |> rt target

    [<CustomOperation("href")>]
    member _.Href(d: Descriptor, target: Descriptor) : Descriptor = d |> href target

    [<CustomOperation("hrefExternal")>]
    member _.HrefExternal(d: Descriptor, uri: string) : Descriptor = d |> hrefExternal uri

    [<CustomOperation("initial")>]
    member _.Initial(d: Descriptor) : Descriptor = d |> initial

    [<CustomOperation("regions")>]
    member _.Regions(d: Descriptor, children: Descriptor list) : Descriptor = d |> regions children

    [<CustomOperation("from")>]
    member _.From(d: Descriptor, sources: Descriptor list) : Descriptor = d |> from sources

let descriptor (id: string) = DescriptorBuilder(id)
```

Update `src/Frank.Alps/Frank.Alps.fsproj`:

```xml
    <Compile Include="ProtocolGraph.fsi" />
    <Compile Include="ProtocolGraph.fs" />
    <Compile Include="DescriptorBuilder.fsi" />
    <Compile Include="DescriptorBuilder.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass. If a `[<CustomOperation>]` name collides or fails to resolve inside the CE block (F#'s custom-operation resolution can be pickier than ordinary overload resolution), the compiler error will name the actual conflict — fix against that rather than the code above; this pattern (one operation name, one non-overloaded member) avoids the overload-resolution pitfall `Frank.Rdf`'s `DescribeBuilder` hit with `property`, so no overloads are expected here.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): descriptor { } CE, a second authoring surface over Descriptor"
```

---

### Task 10: `Serialization` — draft-07 JSON

**Files:**
- Create: `src/Frank.Alps/Serialization.fsi`, `src/Frank.Alps/Serialization.fs`
- Modify: `src/Frank.Alps/Frank.Alps.fsproj` (add after `DescriptorBuilder.fs`)
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/SerializationTests.fs`

**Interfaces:**
- Consumes: `Descriptor` and friends (Task 1), every combinator (Tasks 2-7).
- Produces: `ProtocolStateExtId`/`AvailableInStatesExtId` (string literals), `Serialization.toJson: Descriptor list -> string`.

**Background you need:**

Writes `System.Text.Json.Utf8JsonWriter` directly into a `MemoryStream`, then decodes to a string — the same approach `Frank.JsonHome/JsonHome.fs`'s `serialize` uses, not a streaming `TextWriter` API (`Frank.Rdf`'s `writeJsonLd` streams because JSON-LD graphs can be large; ALPS profiles are small, and this package has no equivalent need).

Shape, from draft-07 and the design doc's *Output* section: `{ "alps": { "version": "1.0", "descriptor": [ ... ] } }`. Per descriptor: `id` always; `type` **omitted when `Semantic`** (the spec's own default, draft-07 §2.2.16) and written as lowercase `"safe"`/`"unsafe"`/`"idempotent"` otherwise; `href` (from `InheritsFrom`) as `"#" + id` for `Local`, the URI verbatim for `External`; `rt` (from `Rt`) always as `"#" + target.Id` — v1 has no `rtExternal`, `rt` is only ever a compile-checked in-process reference; `tag` as one space-joined string (draft-07 §2.2.14: "whitespace-separated list", a single wire attribute, not a JSON array — this is why `Descriptor.Tag` is a `string list` in-process but becomes one string on the wire); `ext`/`link`/nested `descriptor` as JSON arrays, omitted entirely when empty.

`From` is never serialized directly — a non-empty `From` is projected into `ProtocolStateExtId`/`AvailableInStatesExtId` ext *pairs*, one pair per declared state, each pair's `value` set to that state's own local reference (`"#" + state.Id"`) — continuing PR #165/#214's canonical URIs, computed at write time, not stored data.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/SerializationTests.fs`:

```fsharp
module Frank.Alps.Tests.SerializationTests

open System.Text.Json
open Expecto
open Frank.Alps

let private parse (json: string) = JsonDocument.Parse(json).RootElement

let private descriptorArray (root: JsonElement) =
    root.GetProperty("alps").GetProperty("descriptor").EnumerateArray() |> List.ofSeq

let private findById (id: string) (descriptors: JsonElement list) =
    descriptors |> List.find (fun d -> d.GetProperty("id").GetString() = id)

[<Tests>]
let tests =
    testList
        "Serialization.toJson"
        [ test "root shape is alps.version = 1.0, alps.descriptor as an array" {
              let root = Serialization.toJson [ semantic "x" ] |> parse
              Expect.equal (root.GetProperty("alps").GetProperty("version").GetString()) "1.0" ""
              Expect.equal (descriptorArray root |> List.length) 1 ""
          }

          test "type is omitted for Semantic, present and lowercase otherwise" {
              let root =
                  Serialization.toJson [ semantic "a"; safe "b"; unsafe "c"; idempotent "d" ] |> parse

              let descriptors = descriptorArray root
              let hasType id = (findById id descriptors).TryGetProperty("type") |> fst

              Expect.isFalse (hasType "a") "semantic omits type"
              Expect.equal ((findById "b" descriptors).GetProperty("type").GetString()) "safe" ""
              Expect.equal ((findById "c" descriptors).GetProperty("type").GetString()) "unsafe" ""
              Expect.equal ((findById "d" descriptors).GetProperty("type").GetString()) "idempotent" ""
          }

          test "def, doc, tag, rel serialize correctly" {
              let d =
                  semantic "price"
                  |> def "https://schema.org/price"
                  |> doc "Price in minor units"
                  |> tag "money"
                  |> tag "currency"
                  |> rel "self"

              let json = findById "price" (descriptorArray (Serialization.toJson [ d ] |> parse))

              Expect.equal (json.GetProperty("def").GetString()) "https://schema.org/price" ""
              Expect.equal (json.GetProperty("doc").GetProperty("value").GetString()) "Price in minor units" ""
              Expect.equal (json.GetProperty("tag").GetString()) "money currency" ""
              Expect.equal (json.GetProperty("rel").GetString()) "self" ""
          }

          test "rt serializes as a local #id reference" {
              let product = semantic "product"
              let d = safe "listProducts" |> rt product
              let json = findById "listProducts" (descriptorArray (Serialization.toJson [ product; d ] |> parse))
              Expect.equal (json.GetProperty("rt").GetString()) "#product" ""
          }

          test "href with a Local target serializes as #id; hrefExternal serializes the URI verbatim" {
              let shared = semantic "shared"
              let local = semantic "local" |> href shared
              let external' = semantic "external" |> hrefExternal "https://example.org/other#thing"

              let descriptors = descriptorArray (Serialization.toJson [ shared; local; external' ] |> parse)

              Expect.equal ((findById "local" descriptors).GetProperty("href").GetString()) "#shared" ""

              Expect.equal
                  ((findById "external" descriptors).GetProperty("href").GetString())
                  "https://example.org/other#thing"
                  ""
          }

          test "contains serializes as a nested descriptor array" {
              let child = semantic "productId"
              let parent = semantic "product" |> contains [ child ]
              let json = findById "product" (descriptorArray (Serialization.toJson [ parent ] |> parse))
              let nested = json.GetProperty("descriptor").EnumerateArray() |> List.ofSeq
              Expect.equal nested.Length 1 ""
              Expect.equal (nested.[0].GetProperty("id").GetString()) "productId" ""
          }

          test "a transition with from [A; B] emits two protocolState/availableInStates ext pairs" {
              let a, b = semantic "a", semantic "b"
              let c = semantic "c"
              let t = unsafe "t" |> from [ a; b ] |> rt c

              let json = findById "t" (descriptorArray (Serialization.toJson [ a; b; c; t ] |> parse))
              let extIds = json.GetProperty("ext").EnumerateArray() |> Seq.map (fun e -> e.GetProperty("id").GetString()) |> List.ofSeq

              Expect.equal
                  (extIds |> List.sort)
                  ([ ProtocolStateExtId; ProtocolStateExtId; AvailableInStatesExtId; AvailableInStatesExtId ]
                   |> List.sort)
                  "one pair per declared from state"
          }

          test "a transition with no from emits no protocolState/availableInStates ext" {
              let t = unsafe "t"
              let json = findById "t" (descriptorArray (Serialization.toJson [ t ] |> parse))
              Expect.isFalse (json.TryGetProperty("ext") |> fst) ""
          }

          test "empty tag/link/descriptor/ext are omitted entirely, not written as empty arrays" {
              let json = findById "x" (descriptorArray (Serialization.toJson [ semantic "x" ] |> parse))
              Expect.isFalse (json.TryGetProperty("tag") |> fst) ""
              Expect.isFalse (json.TryGetProperty("link") |> fst) ""
              Expect.isFalse (json.TryGetProperty("descriptor") |> fst) ""
              Expect.isFalse (json.TryGetProperty("ext") |> fst) ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="SerializationTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `Serialization`/`ProtocolStateExtId`/`AvailableInStatesExtId` are not defined.

- [ ] **Step 3: Write `Serialization.fsi`**

```fsharp
namespace Frank.Alps

/// Canonical ext ids under https://frank-fs.github.io/alps-ext/, from PR #165/#214 -- unchanged from
/// the rolled-back v7.3.0 line's shipped generator output, continued here for wire-format continuity.
[<Literal>]
val ProtocolStateExtId: string = "https://frank-fs.github.io/alps-ext/protocolState"

[<Literal>]
val AvailableInStatesExtId: string = "https://frank-fs.github.io/alps-ext/availableInStates"

module Serialization =
    /// Serializes a profile (the same `Descriptor list` passed to `useAlps`, or any subset for the
    /// per-resource excerpt) as draft-07 JSON: `{ "alps": { "version": "1.0", "descriptor": [...] } }`.
    val toJson: profile: Descriptor list -> string
```

- [ ] **Step 4: Write `Serialization.fs`**

```fsharp
namespace Frank.Alps

open System.IO
open System.Text
open System.Text.Json

[<Literal>]
let ProtocolStateExtId = "https://frank-fs.github.io/alps-ext/protocolState"

[<Literal>]
let AvailableInStatesExtId = "https://frank-fs.github.io/alps-ext/availableInStates"

module Serialization =
    let private formatToString (f: DocFormat) : string =
        match f with
        | DocFormat.Text -> "text"
        | DocFormat.Html -> "html"
        | DocFormat.Asciidoc -> "asciidoc"
        | DocFormat.Markdown -> "markdown"

    let private resolveHref (r: DescriptorRef) : string =
        match r with
        | DescriptorRef.Local target -> "#" + target.Id
        | DescriptorRef.External uri -> uri.ToString()

    let private writeDoc (writer: Utf8JsonWriter) (doc: Doc) : unit =
        writer.WriteStartObject("doc")
        writer.WriteString("value", doc.Value)
        doc.Href |> Option.iter (fun h -> writer.WriteString("href", h.ToString()))
        doc.Format |> Option.iter (fun f -> writer.WriteString("format", formatToString f))
        doc.ContentType |> Option.iter (fun c -> writer.WriteString("contentType", c))
        if not (List.isEmpty doc.Tag) then
            writer.WriteString("tag", String.concat " " doc.Tag)
        writer.WriteEndObject()

    let private writeLinkElement (writer: Utf8JsonWriter) (l: Link) : unit =
        writer.WriteStartObject()
        writer.WriteString("href", l.Href.ToString())
        writer.WriteString("rel", l.Rel)
        l.Title |> Option.iter (fun t -> writer.WriteString("title", t))
        if not (List.isEmpty l.Tag) then
            writer.WriteString("tag", String.concat " " l.Tag)
        writer.WriteEndObject()

    let private writeExtElement (writer: Utf8JsonWriter) (e: Ext) : unit =
        writer.WriteStartObject()
        writer.WriteString("id", e.Id)
        e.Href |> Option.iter (fun h -> writer.WriteString("href", h.ToString()))
        e.Value |> Option.iter (fun v -> writer.WriteString("value", v))
        if not (List.isEmpty e.Tag) then
            writer.WriteString("tag", String.concat " " e.Tag)
        writer.WriteEndObject()

    let private stateExtPairs (from_: Descriptor list) : Ext list =
        from_
        |> List.collect (fun state ->
            let value = Some("#" + state.Id)

            [ { Id = ProtocolStateExtId
                Href = None
                Value = value
                Tag = [] }
              { Id = AvailableInStatesExtId
                Href = None
                Value = value
                Tag = [] } ])

    let rec private writeDescriptor (writer: Utf8JsonWriter) (d: Descriptor) : unit =
        writer.WriteStartObject()
        writer.WriteString("id", d.Id)
        d.Name |> Option.iter (fun n -> writer.WriteString("name", n))

        match d.Type with
        | DescriptorType.Semantic -> ()
        | DescriptorType.Safe -> writer.WriteString("type", "safe")
        | DescriptorType.Unsafe -> writer.WriteString("type", "unsafe")
        | DescriptorType.Idempotent -> writer.WriteString("type", "idempotent")

        d.Def |> Option.iter (fun uri -> writer.WriteString("def", uri.ToString()))
        d.Doc |> Option.iter (writeDoc writer)

        let allExt = d.Ext @ stateExtPairs d.From

        if not (List.isEmpty allExt) then
            writer.WriteStartArray("ext")
            allExt |> List.iter (writeExtElement writer)
            writer.WriteEndArray()

        d.InheritsFrom |> Option.iter (fun r -> writer.WriteString("href", resolveHref r))
        d.Rt |> Option.iter (fun target -> writer.WriteString("rt", "#" + target.Id))
        d.Rel |> Option.iter (fun r -> writer.WriteString("rel", r))

        if not (List.isEmpty d.Tag) then
            writer.WriteString("tag", String.concat " " d.Tag)

        if not (List.isEmpty d.Link) then
            writer.WriteStartArray("link")
            d.Link |> List.iter (writeLinkElement writer)
            writer.WriteEndArray()

        if not (List.isEmpty d.Descriptors) then
            writer.WriteStartArray("descriptor")
            d.Descriptors |> List.iter (writeDescriptor writer)
            writer.WriteEndArray()

        writer.WriteEndObject()

    let toJson (profile: Descriptor list) : string =
        use stream = new MemoryStream()

        (use writer = new Utf8JsonWriter(stream)
         writer.WriteStartObject()
         writer.WriteStartObject("alps")
         writer.WriteString("version", "1.0")
         writer.WriteStartArray("descriptor")
         profile |> List.iter (writeDescriptor writer)
         writer.WriteEndArray()
         writer.WriteEndObject()
         writer.WriteEndObject())

        Encoding.UTF8.GetString(stream.ToArray())
```

Update `src/Frank.Alps/Frank.Alps.fsproj`:

```xml
    <Compile Include="DescriptorBuilder.fsi" />
    <Compile Include="DescriptorBuilder.fs" />
    <Compile Include="Serialization.fsi" />
    <Compile Include="Serialization.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass. If `Utf8JsonWriter`'s disposal-before-read ordering doesn't match the nested `use` block above (the writer must flush/dispose *before* `stream.ToArray()` is called, since it buffers internally), the compiler/runtime error will be a truncated or empty JSON string, not a build failure — verify the parenthesized `use writer = ...` block genuinely disposes before `Encoding.UTF8.GetString` runs, adjusting the block structure if not.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): Serialization.toJson -- draft-07 JSON, protocolState/availableInStates projection"
```

---

### Task 11: `binds` on `handler { }`

**Files:**
- Create: `src/Frank.Alps/HandlerBuilderExtensions.fsi`, `src/Frank.Alps/HandlerBuilderExtensions.fs`
- Modify: `src/Frank.Alps/Frank.Alps.fsproj` (add after `Serialization.fs`)
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/HandlerBuilderExtensionsTests.fs`

**Interfaces:**
- Consumes: `Descriptor` (Task 1); `HandlerDefinition`, `HandlerDefinition.addMetadata`, `HandlerDefinition.tryFind`, `handler` (`Frank`, core — already shipped, `src/Frank/HandlerBuilder.fs`/`src/Frank/HandlerDefinition.fs`).
- Produces: a `[<CustomOperation("binds")>]` member on `HandlerBuilder` (`type HandlerBuilder with`, `[<AutoOpen>]` module), mirroring exactly how `src/Frank.JsonHome/ResourceBuilderExtensions.fs` extends `ResourceBuilder` from a sibling package.

**Background you need:**

`HandlerDefinition.addMetadata (metadata: obj) (def: HandlerDefinition) : HandlerDefinition` already exists in `Frank` core and is exactly what every other metadata-adding custom operation on `HandlerBuilder` uses (`name`/`summary`/`tags`/etc. in `src/Frank/HandlerBuilder.fs`). `binds` just adds one more, boxing a `Descriptor`: `handler { handle listHandler; binds Catalog.listProducts }`. Retrieving it back later (`EndpointSurface`, Task 13) uses `HandlerDefinition.tryFind<Descriptor>`, already generic and already shipped.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/HandlerBuilderExtensionsTests.fs`:

```fsharp
module Frank.Alps.Tests.HandlerBuilderExtensionsTests

open Microsoft.AspNetCore.Http
open Expecto
open Frank.Builder
open Frank.Alps

[<Tests>]
let tests =
    testList
        "binds"
        [ test "binds attaches a Descriptor retrievable via HandlerDefinition.tryFind" {
              let listProducts = safe "listProducts"

              let def =
                  handler {
                      handle (fun (ctx: HttpContext) -> System.Threading.Tasks.Task.CompletedTask)
                      binds listProducts
                  }

              Expect.equal (HandlerDefinition.tryFind<Descriptor> def) (Some listProducts) ""
          }

          test "a handler without binds has no bound Descriptor" {
              let def =
                  handler { handle (fun (ctx: HttpContext) -> System.Threading.Tasks.Task.CompletedTask) }

              Expect.equal (HandlerDefinition.tryFind<Descriptor> def) None ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="HandlerBuilderExtensionsTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `binds` is not a recognized custom operation on `handler { }`.

- [ ] **Step 3: Write `HandlerBuilderExtensions.fsi`**

```fsharp
namespace Frank.Alps

open Frank.Builder

/// Adds `binds` to `handler { }`: attaches the transition `Descriptor` this handler implements, so
/// `EndpointSurface` (Task 13) and `AlpsDocument`'s startup validation (Task 14) can retrieve it back
/// via `HandlerDefinition.tryFind<Descriptor>`/`Endpoint.Metadata.GetOrderedMetadata<Descriptor>()`.
[<AutoOpen>]
module HandlerBuilderExtensions =
    type HandlerBuilder with

        [<CustomOperation("binds")>]
        member Binds: def: HandlerDefinition * descriptor: Descriptor -> HandlerDefinition
```

- [ ] **Step 4: Write `HandlerBuilderExtensions.fs`**

```fsharp
namespace Frank.Alps

open Frank.Builder

[<AutoOpen>]
module HandlerBuilderExtensions =
    type HandlerBuilder with

        [<CustomOperation("binds")>]
        member _.Binds(def: HandlerDefinition, descriptor: Descriptor) : HandlerDefinition =
            HandlerDefinition.addMetadata descriptor def
```

Update `src/Frank.Alps/Frank.Alps.fsproj`:

```xml
    <Compile Include="Serialization.fsi" />
    <Compile Include="Serialization.fs" />
    <Compile Include="HandlerBuilderExtensions.fsi" />
    <Compile Include="HandlerBuilderExtensions.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass. If `HandlerBuilder`'s actual member-extension syntax or `HandlerDefinition.addMetadata`'s exact signature has drifted from what's documented above, the compiler error will name the mismatch directly against `src/Frank/HandlerBuilder.fs`/`src/Frank/HandlerDefinition.fsi` — fix against the live core source, not this plan's assumption.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): binds -- attach a transition Descriptor to a handler"
```

---

### Task 12: `EndpointSurface`

**Files:**
- Create: `src/Frank.Alps/EndpointSurface.fsi`, `src/Frank.Alps/EndpointSurface.fs`
- Modify: `src/Frank.Alps/Frank.Alps.fsproj` (add after `HandlerBuilderExtensions.fs`)
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/EndpointSurfaceTests.fs`

**Interfaces:**
- Consumes: `Descriptor` (Task 1), `binds` (Task 11, so a real `Endpoint`'s metadata can carry a `Descriptor`).
- Produces: `EndpointSurface.allDescriptors: IServiceProvider -> (Endpoint * Descriptor) list`, `EndpointSurface.descriptorsForRoute: IServiceProvider -> string -> (Endpoint * Descriptor) list`.

**Background you need:**

Reads `Microsoft.AspNetCore.Http.EndpointDataSource` (resolved from DI) directly — not `Frank.JsonHome`'s `ApiSurface`/`IApiDescriptionGroupCollectionProvider` mechanism, which ties resource discovery to ASP.NET Core's ApiExplorer and would require `services.AddEndpointsApiExplorer()`; `Frank.Alps` doesn't need or want that dependency (design doc, *HTTP surface*). `binds` (Task 11) already attaches a `Descriptor` directly to the exact `Endpoint.Metadata` it's bound to via `HandlerDefinitionMetadata.toConventions`/`AddMethodMetadata`, unchanged core mechanism — so reading it back is a direct `endpoint.Metadata.GetOrderedMetadata<Descriptor>()` call, no aggregation step needed.

**Before writing this task's tests**, read `test/Frank.JsonHome.Tests/IntegrationTests.fs` in full — it already has a working `TestEndpointDataSource`/`TestServer` setup in this exact codebase for constructing real `Endpoint` objects with attached metadata and a `DI` container to resolve them from. Mirror that pattern exactly for this task's tests (a `TestEndpointDataSource : EndpointDataSource` wrapping a plain `Endpoint[]`, registered as a singleton on a `ServiceCollection`) rather than reconstructing it from this plan's description — this plan was written without that file's exact source in hand, so treat the real file as authoritative over any specifics implied below.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/EndpointSurfaceTests.fs`:

```fsharp
module Frank.Alps.Tests.EndpointSurfaceTests

open System.Collections.Generic
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Primitives
open Expecto
open Frank.Alps

// Mirrors test/Frank.JsonHome.Tests/IntegrationTests.fs's own TestEndpointDataSource -- read that
// file first and match its exact shape; this is a from-scratch reconstruction, not a copy.
type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> IReadOnlyList<Endpoint>
    override _.GetChangeToken() = NullChangeToken.Singleton :> IChangeToken

let private noopDelegate: RequestDelegate = RequestDelegate(fun _ -> System.Threading.Tasks.Task.CompletedTask)

let private makeEndpoint (routePattern: string) (metadata: obj list) : Endpoint =
    RouteEndpoint(noopDelegate, RoutePattern.Parse routePattern, 0, EndpointMetadataCollection(metadata), routePattern)

let private servicesWith (endpoints: Endpoint[]) : System.IServiceProvider =
    let services = ServiceCollection()
    services.AddSingleton<EndpointDataSource>(TestEndpointDataSource(endpoints) :> EndpointDataSource) |> ignore
    services.BuildServiceProvider() :> System.IServiceProvider

[<Tests>]
let tests =
    testList
        "EndpointSurface"
        [ test "allDescriptors finds a Descriptor attached to one endpoint's metadata" {
              let d = safe "listProducts"
              let services = servicesWith [| makeEndpoint "/products" [ box d ] |]

              let result = EndpointSurface.allDescriptors services

              Expect.equal (result |> List.map snd) [ d ] ""
          }

          test "allDescriptors skips endpoints with no Descriptor metadata" {
              let services = servicesWith [| makeEndpoint "/health" [] |]
              Expect.equal (EndpointSurface.allDescriptors services) [] ""
          }

          test "allDescriptors collects across multiple endpoints" {
              let a, b = safe "listProducts", unsafe "createProduct"
              let services = servicesWith [| makeEndpoint "/products" [ box a ]; makeEndpoint "/products" [ box b ] |]

              Expect.equal (EndpointSurface.allDescriptors services |> List.map snd |> List.sortBy (fun d -> d.Id)) [ a; b ] ""
          }

          test "descriptorsForRoute filters to endpoints sharing exactly that route pattern" {
              let a, b, c = safe "listProducts", unsafe "createProduct", safe "listOrders"

              let services =
                  servicesWith
                      [| makeEndpoint "/products" [ box a ]
                         makeEndpoint "/products" [ box b ]
                         makeEndpoint "/orders" [ box c ] |]

              let result = EndpointSurface.descriptorsForRoute services "/products" |> List.map snd |> List.sortBy (fun d -> d.Id)

              Expect.equal result [ a; b ] ""
          }

          test "descriptorsForRoute against an unknown route pattern is empty" {
              let services = servicesWith [| makeEndpoint "/products" [ box (safe "listProducts") ] |]
              Expect.equal (EndpointSurface.descriptorsForRoute services "/orders") [] ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="EndpointSurfaceTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `EndpointSurface` is not defined.

- [ ] **Step 3: Write `EndpointSurface.fsi`**

```fsharp
namespace Frank.Alps

open System
open Microsoft.AspNetCore.Http

/// Reads bound transition descriptors directly off registered endpoints' metadata -- no
/// ApiExplorer/`Frank.JsonHome` dependency; `binds` (Task 11) already puts the `Descriptor` exactly
/// where this looks.
module EndpointSurface =
    /// Every (Endpoint, Descriptor) pair across every endpoint the DI-registered `EndpointDataSource`
    /// knows about.
    val allDescriptors: services: IServiceProvider -> (Endpoint * Descriptor) list

    /// (Endpoint, Descriptor) pairs restricted to endpoints sharing exactly `routePattern` -- one
    /// resource's several HTTP-method endpoints, each carrying the Descriptor its own `binds` attached.
    val descriptorsForRoute: services: IServiceProvider -> routePattern: string -> (Endpoint * Descriptor) list
```

- [ ] **Step 4: Write `EndpointSurface.fs`**

```fsharp
namespace Frank.Alps

open System
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection

module EndpointSurface =
    let allDescriptors (services: IServiceProvider) : (Endpoint * Descriptor) list =
        let dataSource = services.GetRequiredService<EndpointDataSource>()

        [ for endpoint in dataSource.Endpoints do
              for descriptor in endpoint.Metadata.GetOrderedMetadata<Descriptor>() do
                  yield endpoint, descriptor ]

    let descriptorsForRoute (services: IServiceProvider) (routePattern: string) : (Endpoint * Descriptor) list =
        allDescriptors services
        |> List.filter (fun (endpoint, _) ->
            match endpoint with
            | :? RouteEndpoint as re -> re.RoutePattern.RawText = routePattern
            | _ -> false)
```

Update `src/Frank.Alps/Frank.Alps.fsproj`:

```xml
    <Compile Include="HandlerBuilderExtensions.fsi" />
    <Compile Include="HandlerBuilderExtensions.fs" />
    <Compile Include="EndpointSurface.fsi" />
    <Compile Include="EndpointSurface.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass. If `RouteEndpoint`'s constructor, `EndpointMetadataCollection`, or `RoutePattern.Parse` don't match the signatures assumed above, fix against whatever `test/Frank.JsonHome.Tests/IntegrationTests.fs` actually does — it's a real, currently-passing use of the same types in this repo.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): EndpointSurface -- read bound descriptors off live endpoint metadata"
```

---

### Task 13: `AuthorizationFilter`

**Files:**
- Create: `src/Frank.Alps/AuthorizationFilter.fsi`, `src/Frank.Alps/AuthorizationFilter.fs`
- Modify: `src/Frank.Alps/Frank.Alps.fsproj` (add after `EndpointSurface.fs`)
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/AuthorizationFilterTests.fs`

**Interfaces:**
- Consumes: `Descriptor` (Task 1), `(Endpoint * Descriptor) list` shape (Task 12).
- Produces: `AuthorizationFilter.isAllowed: HttpContext -> Endpoint -> Task<bool>`, `AuthorizationFilter.filter: HttpContext -> (Endpoint * Descriptor) list -> Task<Descriptor list>`, `AuthorizationFilter.varies: (Endpoint * Descriptor) list -> bool`.

**Background you need:**

This is a direct port of `src/Frank.JsonHome/AuthorizationFilter.fs`'s evaluation logic (already in this repo, read it in full before starting) — same `IAuthorizeData`/`AuthorizationPolicy` gathering, same `IAuthorizationPolicyProvider`/`AuthorizationPolicy.CombineAsync` resolution, same fail-closed-on-evaluation-error behavior ("an evaluation error must never widen access") — retargeted from `ResourceDescription.MethodMetadata: obj list` to a real `Endpoint`'s own `Metadata: EndpointMetadataCollection` (`GetOrderedMetadata<T>()`/`GetMetadata<T>()`, not a plain `obj list` filter), since `Frank.Alps` reads endpoint metadata directly (Task 12) rather than through `Frank.JsonHome`'s `ResourceDescription`.

**Before writing this task's tests**, read `test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs` in full — it already has a working pattern in this repo for constructing a `HttpContext` with real `IAuthorizationService`/`IAuthorizationPolicyProvider` wired into `RequestServices` and a test principal on `ctx.User`. Mirror that setup exactly.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/AuthorizationFilterTests.fs`, following `AuthorizationFilterTests.fs`'s own `HttpContext`/DI setup pattern (from the file just read) to exercise these cases:

```fsharp
module Frank.Alps.Tests.AuthorizationFilterTests

open Expecto
open Frank.Alps

// Build ctx/endpoint helpers identical in spirit to test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs
// -- read that file and reuse its exact DI-wiring/HttpContext-construction approach here, since this
// plan does not have its literal source in hand.

[<Tests>]
let tests =
    testList
        "AuthorizationFilter"
        [ testAsync "an endpoint with no auth metadata is always allowed" {
              // Arrange: an endpoint with empty metadata, any ctx.
              // Act: isAllowed ctx endpoint.
              // Assert: true.
              ()
          }

          testAsync "AllowAnonymous metadata is always allowed regardless of auth state" { () }

          testAsync "IAuthorizeData present, principal satisfies it -> allowed" { () }

          testAsync "IAuthorizeData present, principal does not satisfy it -> denied" { () }

          testAsync "an evaluation error (e.g. unresolvable policy) fails closed -- denied, not thrown" { () }

          testAsync "filter keeps only the Descriptors whose endpoint is allowed, in order" { () }

          test "varies is true when any pair's endpoint carries auth metadata" { () }

          test "varies is false when no pair's endpoint carries auth metadata" { () } ]
```

*Note to whoever implements this task:* the six `()` bodies above are placeholders for **this task's own work**, not a plan gap to carry forward — replace each with the real arrange/act/assert from the mirrored `AuthorizationFilterTests.fs` pattern before treating Step 1 as done; do not proceed to Step 2 with any test body still `()`.

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `AuthorizationFilter` is not defined (once the test bodies are filled in per the note above; a build against literal `()` bodies would trivially "pass" and prove nothing).

- [ ] **Step 3: Write `AuthorizationFilter.fsi`**

```fsharp
namespace Frank.Alps

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// Principal-based filtering, ported from `Frank.JsonHome/AuthorizationFilter.fs`'s evaluation logic
/// and retargeted to read a real `Endpoint`'s own `Metadata` directly (Task 12's `EndpointSurface`)
/// instead of `Frank.JsonHome`'s `ResourceDescription`.
module AuthorizationFilter =
    /// Whether `ctx`'s principal is allowed to see `endpoint`, per its `IAuthorizeData`/
    /// `AuthorizationPolicy` metadata (or `IAllowAnonymous`). Fails closed: any evaluation error
    /// returns `false`, never `true`.
    val isAllowed: ctx: HttpContext -> endpoint: Endpoint -> Task<bool>

    /// Keeps only the Descriptors whose bound endpoint `isAllowed` returns true for, order preserved.
    val filter: ctx: HttpContext -> pairs: (Endpoint * Descriptor) list -> Task<Descriptor list>

    /// True if any pair's endpoint carries authorization metadata -- callers use this to decide
    /// whether to set `Cache-Control: private, no-cache` / `Vary: Authorization`.
    val varies: pairs: (Endpoint * Descriptor) list -> bool
```

- [ ] **Step 4: Write `AuthorizationFilter.fs`**

```fsharp
namespace Frank.Alps

open System.Threading.Tasks
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module AuthorizationFilter =
    let private authorizeData (endpoint: Endpoint) : IAuthorizeData list =
        endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>() |> List.ofSeq

    let private policies (endpoint: Endpoint) : AuthorizationPolicy list =
        endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>() |> List.ofSeq

    let private isAnonymous (endpoint: Endpoint) : bool =
        not (isNull (endpoint.Metadata.GetMetadata<IAllowAnonymous>() |> box))

    let private hasAuthorizationMetadata (endpoint: Endpoint) : bool =
        not (List.isEmpty (authorizeData endpoint)) || not (List.isEmpty (policies endpoint))

    let varies (pairs: (Endpoint * Descriptor) list) : bool =
        pairs |> List.exists (fun (endpoint, _) -> hasAuthorizationMetadata endpoint)

    let private resolvePolicy (ctx: HttpContext) (data: IAuthorizeData list) (pols: AuthorizationPolicy list) =
        task {
            if List.isEmpty data then
                return AuthorizationPolicy.Combine(pols)
            else
                let provider = ctx.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>()
                return! AuthorizationPolicy.CombineAsync(provider, data, pols)
        }

    let isAllowed (ctx: HttpContext) (endpoint: Endpoint) : Task<bool> =
        task {
            if isAnonymous endpoint then
                return true
            else
                let data = authorizeData endpoint
                let pols = policies endpoint

                if List.isEmpty data && List.isEmpty pols then
                    return true
                else
                    try
                        match! resolvePolicy ctx data pols with
                        | null -> return true
                        | policy ->
                            let service = ctx.RequestServices.GetRequiredService<IAuthorizationService>()
                            let! result = service.AuthorizeAsync(ctx.User, box endpoint, policy)
                            return result.Succeeded
                    with _ ->
                        // Fail closed: an evaluation error must never widen access.
                        return false
        }

    let filter (ctx: HttpContext) (pairs: (Endpoint * Descriptor) list) : Task<Descriptor list> =
        task {
            let kept = ResizeArray()

            for endpoint, descriptor in pairs do
                let! ok = isAllowed ctx endpoint
                if ok then kept.Add descriptor

            return List.ofSeq kept
        }
```

Update `src/Frank.Alps/Frank.Alps.fsproj`:

```xml
    <Compile Include="EndpointSurface.fsi" />
    <Compile Include="EndpointSurface.fs" />
    <Compile Include="AuthorizationFilter.fsi" />
    <Compile Include="AuthorizationFilter.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass. If `isNull (endpoint.Metadata.GetMetadata<IAllowAnonymous>() |> box)` doesn't compile against the real `EndpointMetadataCollection.GetMetadata<T>()` signature (it may return `'T` directly with a non-nullable-friendly generic constraint rather than something boxable this way), use whatever null-check idiom `src/Frank.JsonHome/AuthorizationFilter.fs`'s own `isAnonymous` uses instead — it solves the identical problem against the identical API one file away.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): AuthorizationFilter -- principal-based filtering over live endpoints"
```

---

### Task 14: `AlpsDocument` — app-wide document, `useAlps`, startup validation

**Files:**
- Create: `src/Frank.Alps/AlpsDocument.fsi`, `src/Frank.Alps/AlpsDocument.fs`
- Modify: `src/Frank.Alps/Frank.Alps.fsproj` (add after `AuthorizationFilter.fs`)
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/AlpsDocumentTests.fs`

**Interfaces:**
- Consumes: `Descriptor`, `Serialization.toJson` (Task 10), `EndpointSurface` (Task 12), `AuthorizationFilter` (Task 13); `WebHostBuilder`/`WebHostSpec`/`WebLink`/`useAppWideLinks` (`Frank` core, already shipped — same shape `src/Frank.JsonHome/WebHostBuilderExtensions.fs` already extends from).
- Produces: `AlpsDocument.validate: (Endpoint * Descriptor) list -> unit` (raises on a `Type`-vs-bound-HTTP-method mismatch), a `[<CustomOperation("useAlps")>]` member on `WebHostBuilder`.

**Background you need:**

Two things to get right, both because `Frank.Alps` needs to validate *every* registered resource's bound transitions against their HTTP methods, not just its own document's endpoint:

1. **Response writing/headers**: read `src/Frank.JsonHome/JsonHome.fs` in full before starting — its `documentHandler` already does exactly this shape of work (call `AuthorizationFilter.apply`/`varies`, conditionally set `Cache-Control: private, no-cache` + `Vary: Authorization`, set `ContentType`, write the body) against the equivalent JsonHome types. Mirror its exact header-setting code, substituting `Serialization.toJson`/`AuthorizationFilter` (Tasks 10, 13) for JsonHome's own.
2. **Startup validation timing**: `useAlps`'s install function only sees `spec.Endpoints` as accumulated *so far* in the `webHost { }` block — if `useAlps` happens to be called before some other `resource { }` operation in that same block, validating against `spec.Endpoints` there would silently miss it. Register an `IHostedService` instead (via `spec.Services`), whose `StartAsync` resolves the DI-registered `EndpointDataSource` and calls `EndpointSurface.allDescriptors`/`AlpsDocument.validate` — this runs during host startup, after routing has fully built every endpoint regardless of `webHost { }` block order, and before the app accepts its first request. This is the robust version of "fails at startup, not on first request."

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/AlpsDocumentTests.fs`:

```fsharp
module Frank.Alps.Tests.AlpsDocumentTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "AlpsDocument.validate"
        [ test "a safe transition bound to GET passes" {
              let endpoint = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "GET" ]) ]
              AlpsDocument.validate [ endpoint, safe "x" ]
          }

          test "a safe transition bound to POST raises" {
              let endpoint = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "POST" ]) ]
              Expect.throws (fun () -> AlpsDocument.validate [ endpoint, safe "x" ]) ""
          }

          test "an idempotent transition bound to PUT or DELETE passes, bound to GET raises" {
              let put = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "PUT" ]) ]
              AlpsDocument.validate [ put, idempotent "x" ]

              let get = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "GET" ]) ]
              Expect.throws (fun () -> AlpsDocument.validate [ get, idempotent "x" ]) ""
          }

          test "an unsafe transition bound to POST passes, bound to GET raises" {
              let post = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "POST" ]) ]
              AlpsDocument.validate [ post, unsafe "x" ]

              let get = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "GET" ]) ]
              Expect.throws (fun () -> AlpsDocument.validate [ get, unsafe "x" ]) ""
          }

          test "semantic descriptors are never validated against a bound method" {
              let endpoint = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "POST" ]) ]
              AlpsDocument.validate [ endpoint, semantic "x" ]
          } ]
```

*Note:* this reuses `EndpointSurfaceTests.makeEndpoint` from Task 12 — make that `let private` binding `internal` instead (drop `private`, since `Frank.Alps.Tests` is one assembly and `internal`/no-modifier module-level `let`s in an `.fs`-only test project are visible across files in the same project without any special wiring) before this task, so both test files can share it. If `makeEndpoint` was written as `private` to its file, remove `private` in Task 12's file as part of this task's Step 1, and re-run Task 12's own tests to confirm nothing broke.

Add the new file to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="AlpsDocumentTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `AlpsDocument` is not defined.

- [ ] **Step 3: Write `AlpsDocument.fsi`**

```fsharp
namespace Frank.Alps

open Microsoft.AspNetCore.Http
open Frank.Builder

/// The app-wide ALPS document: served at a fixed path (default `/.well-known/alps.json`), registered
/// via `useAlps` exactly the way `useJsonHome` registers its own document (`src/Frank.JsonHome`).
module AlpsDocument =
    /// Raises if any non-semantic descriptor's bound endpoint's HTTP method(s) don't match its
    /// `DescriptorType` (`Safe` -> GET/HEAD, `Idempotent` -> PUT/DELETE, `Unsafe` -> POST). Semantic
    /// descriptors are never validated -- they aren't transitions bound to a method.
    val validate: pairs: (Endpoint * Descriptor) list -> unit

type AlpsOptions =
    { Path: string
      Rel: string }

    static member Default: AlpsOptions

[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with

        [<CustomOperation("useAlps")>]
        member UseAlps: spec: WebHostSpec * profile: Descriptor list -> WebHostSpec

        [<CustomOperation("useAlps")>]
        member UseAlps: spec: WebHostSpec * profile: Descriptor list * configure: (AlpsOptions -> AlpsOptions) -> WebHostSpec
```

- [ ] **Step 4: Write `AlpsDocument.fs`**

Start from `src/Frank.JsonHome/JsonHome.fs`'s `documentHandler`/`serialize`/`write` and `src/Frank.JsonHome/WebHostBuilderExtensions.fs`'s `install`, both already read — adapt them to `Frank.Alps`'s types below rather than reinventing the header-setting/hosting-registration shape:

```fsharp
namespace Frank.Alps

open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Frank.Builder

module AlpsDocument =
    let private validMethods (t: DescriptorType) : string list =
        match t with
        | DescriptorType.Safe -> [ "GET"; "HEAD" ]
        | DescriptorType.Idempotent -> [ "PUT"; "DELETE" ]
        | DescriptorType.Unsafe -> [ "POST" ]
        | DescriptorType.Semantic -> []

    let validate (pairs: (Endpoint * Descriptor) list) : unit =
        for endpoint, descriptor in pairs do
            let allowed = validMethods descriptor.Type

            if not (List.isEmpty allowed) then
                let actual =
                    match endpoint.Metadata.GetMetadata<HttpMethodMetadata>() with
                    | null -> []
                    | m -> m.HttpMethods |> List.ofSeq

                let ok = not (List.isEmpty actual) && actual |> List.forall (fun m -> List.contains m allowed)

                if not ok then
                    failwithf
                        "Frank.Alps: descriptor '%s' (%A) is bound to HTTP method(s) %A, expected one of %A"
                        descriptor.Id
                        descriptor.Type
                        actual
                        allowed

    let private documentHandler (profile: Descriptor list) : RequestDelegate =
        RequestDelegate(fun ctx ->
            (task {
                let pairs =
                    EndpointSurface.allDescriptors ctx.RequestServices
                    |> List.filter (fun (_, d) -> profile |> List.exists (fun p -> p.Id = d.Id))

                let! allowedIds = AuthorizationFilter.filter ctx pairs
                let allowed = allowedIds |> List.map (fun d -> d.Id) |> Set.ofList

                let served =
                    profile
                    |> List.filter (fun d -> d.Type = DescriptorType.Semantic || Set.contains d.Id allowed)

                if AuthorizationFilter.varies pairs then
                    // Mirror src/Frank.JsonHome/JsonHome.fs's own Cache-Control/Vary header-setting
                    // code here verbatim -- same rule, same headers, same reason (an auth-filtered
                    // document must never be cached across principals).
                    ()

                ctx.Response.ContentType <- "application/alps+json"
                return! ctx.Response.WriteAsync(Serialization.toJson served)
             })
            :> Task)

    type private ValidationHostedService(services: System.IServiceProvider) =
        interface IHostedService with
            member _.StartAsync(_: CancellationToken) : Task =
                EndpointSurface.allDescriptors services |> validate
                Task.CompletedTask

            member _.StopAsync(_: CancellationToken) : Task = Task.CompletedTask

type AlpsOptions =
    { Path: string
      Rel: string }

    static member Default =
        { Path = "/.well-known/alps.json"
          Rel = "profile" }

[<AutoOpen>]
module WebHostBuilderExtensions =
    let private install (options: AlpsOptions) (profile: Descriptor list) (spec: WebHostSpec) =
        // Mirror src/Frank.JsonHome/WebHostBuilderExtensions.fs's own `install`: build a resource {}
        // at options.Path with a single `get` handler wrapping AlpsDocument's private documentHandler,
        // and append its .Endpoints the same way JsonHome appends document.Endpoints. That function is
        // `private` to AlpsDocument.fs above -- expose a small internal accessor from AlpsDocument if
        // this file needs it across the module boundary, matching however JsonHome itself structures
        // that same private-handler-into-a-resource step (re-check JsonHome.fs's own documentResource
        // wiring for the exact mechanism, since it solves this identical problem).
        { spec with
            Services = spec.Services >> fun services -> services.AddHostedService<AlpsDocument.private_ValidationHostedService>()
            LinkProviders =
                spec.LinkProviders
                @ [ fun (_: HttpContext) -> Seq.singleton { Target = options.Path; Rel = options.Rel; Params = [] } ] }

    type WebHostBuilder with

        [<CustomOperation("useAlps")>]
        member _.UseAlps(spec: WebHostSpec, profile: Descriptor list) : WebHostSpec =
            install AlpsOptions.Default profile spec

        [<CustomOperation("useAlps")>]
        member _.UseAlps(spec: WebHostSpec, profile: Descriptor list, configure: AlpsOptions -> AlpsOptions) : WebHostSpec =
            install (configure AlpsOptions.Default) profile spec
```

*This step has two deliberately unresolved wrinkles, both flagged in the code above rather than guessed at:* how `documentHandler` (kept `private` to stay an implementation detail) gets wired into a `resource { }`'s endpoints from a different `install` function, and the exact syntax for registering a private nested type with `AddHostedService<'T>` from outside its declaring module. **Resolve both by reading `src/Frank.JsonHome/JsonHome.fs`'s `documentResource`/`JsonHome.write` and `WebHostBuilderExtensions.fs`'s `install` in full** (both already read once for this plan, re-read them now for the exact mechanism) and matching their structure — this plan's own reconstruction above is a close approximation, not a verified copy, and the real JsonHome source is the authority here.

Update `src/Frank.Alps/Frank.Alps.fsproj`:

```xml
    <Compile Include="AuthorizationFilter.fsi" />
    <Compile Include="AuthorizationFilter.fs" />
    <Compile Include="AlpsDocument.fsi" />
    <Compile Include="AlpsDocument.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: `AlpsDocumentTests.fs`'s five `validate` tests pass. (The full `useAlps`/`documentHandler`/hosted-service wiring gets its own end-to-end coverage in Task 16's sample and any `TestHost` integration tests added there — this task's tests only exercise `validate` directly, which is fully concrete above and doesn't depend on resolving the two flagged wrinkles.)

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): AlpsDocument -- app-wide document, useAlps, startup type/method validation"
```

---

### Task 15: `Excerpt` — `CurrentStateResolver`, state matching, `Alps.excerpt`

**Files:**
- Create: `src/Frank.Alps/Excerpt.fsi`, `src/Frank.Alps/Excerpt.fs`
- Modify: `src/Frank.Alps/Frank.Alps.fsproj` (add after `AlpsDocument.fs`)
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/ExcerptTests.fs`

**Interfaces:**
- Consumes: `Descriptor` (Task 1), `EndpointSurface.descriptorsForRoute` (Task 12), `AuthorizationFilter.filter` (Task 13), `Serialization.toJson` (Task 10).
- Produces: `CurrentStateResolver = string -> Uri option`, `Excerpt.satisfiesState: Uri -> Descriptor -> bool`, `Alps.excerpt: CurrentStateResolver option -> RequestDelegate`.

**Background you need:**

`satisfiesState current candidate` decides whether an authored `from`-state `candidate` is satisfied by the resolver's returned `current` URI, walking `contains` ancestry: `candidate` itself matches if `candidate.Def = Some current`, or *any* of its (recursively nested) `Descriptors` matches — this is "being in a substate means being in all its ancestors," read the other direction (design doc, *State-based filtering*). This makes `def` the required identity for any state that wants to participate in resolver-based filtering — a state with no `def` can never be matched this way, which is an honest, minimal requirement rather than inventing a synthetic identity scheme the design doc doesn't call for.

The design doc's own illustrative example (`CurrentStateResolver "games/{id}"`) uses the *route template*; that's shorthand, not literal — the actual `resourceIri` passed to the resolver here is `ctx.Request.Path.Value` (the resolved instance path, e.g. `/games/42`), since a resolver answering "what state is *this* game in" needs the specific instance, not the route pattern every instance shares.

- [ ] **Step 1: Write the failing tests**

Create `test/Frank.Alps.Tests/ExcerptTests.fs`:

```fsharp
module Frank.Alps.Tests.ExcerptTests

open System
open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "satisfiesState"
        [ test "a state with a matching Def satisfies directly" {
              let uri = Uri "https://example.org/states/open"
              let openState = semantic "open" |> def "https://example.org/states/open"
              Expect.isTrue (Excerpt.satisfiesState uri openState) ""
          }

          test "a state with a non-matching Def does not satisfy" {
              let uri = Uri "https://example.org/states/open"
              let closedState = semantic "closed" |> def "https://example.org/states/closed"
              Expect.isFalse (Excerpt.satisfiesState uri closedState) ""
          }

          test "a state with no Def never satisfies" {
              let uri = Uri "https://example.org/states/open"
              Expect.isFalse (Excerpt.satisfiesState uri (semantic "open")) ""
          }

          test "a composite (contains) state satisfies when a nested child's Def matches" {
              let uri = Uri "https://example.org/states/inProgress"
              let inProgress = semantic "inProgress" |> def "https://example.org/states/inProgress"
              let waiting = semantic "waiting" |> def "https://example.org/states/waiting"
              let openState = semantic "open" |> contains [ waiting; inProgress ]

              Expect.isTrue (Excerpt.satisfiesState uri openState) "matches via a nested descendant"
          }

          test "matching is recursive through more than one level of nesting" {
              let uri = Uri "https://example.org/states/deep"
              let deep = semantic "deep" |> def "https://example.org/states/deep"
              let mid = semantic "mid" |> contains [ deep ]
              let top = semantic "top" |> contains [ mid ]

              Expect.isTrue (Excerpt.satisfiesState uri top) ""
          } ]
```

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="ExcerptTests.fs" />
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: build failure — `Excerpt`/`CurrentStateResolver` are not defined.

- [ ] **Step 3: Write `Excerpt.fsi`**

```fsharp
namespace Frank.Alps

open System
open Microsoft.AspNetCore.Http

/// Answers "what state is this specific resource in", if the application supplies one -- a plain
/// function wired at composition time, no dependency on `Frank.Provenance` or any other package. The
/// natural implementation queries a provenance/event store; absent, or returning `None`, means state
/// filtering simply does not apply (design doc, *State-based filtering*).
type CurrentStateResolver = resourceIri: string -> Uri option

module Excerpt =
    /// Whether the resolver's returned `current` state satisfies an authored `from`-state `candidate`,
    /// walking `contains` ancestry: `candidate` matches directly via `Def`, or any of its (recursively
    /// nested) children does. A `candidate` with no `Def` anywhere in its own subtree can never match.
    val satisfiesState: current: Uri -> candidate: Descriptor -> bool

module Alps =
    /// Serves the ALPS excerpt for the *specific resource* the current request's endpoint belongs to:
    /// every HTTP method's `binds`-bound descriptor sharing this endpoint's route pattern
    /// (`EndpointSurface.descriptorsForRoute`), filtered by principal and, if `resolver` is `Some`, by
    /// `CurrentStateResolver`. Wire this into a `negotiate { }` block's `accepts "application/alps+json"`
    /// case -- this is not automatic middleware (design doc, *HTTP surface*).
    val excerpt: resolver: CurrentStateResolver option -> RequestDelegate
```

- [ ] **Step 4: Write `Excerpt.fs`**

```fsharp
namespace Frank.Alps

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

type CurrentStateResolver = string -> Uri option

module Excerpt =
    let rec satisfiesState (current: Uri) (candidate: Descriptor) : bool =
        (candidate.Def = Some current) || (candidate.Descriptors |> List.exists (satisfiesState current))

module Alps =
    let private routePatternOf (ctx: HttpContext) : string =
        match ctx.GetEndpoint() with
        | :? RouteEndpoint as re -> re.RoutePattern.RawText
        | _ -> failwith "Frank.Alps: Alps.excerpt requires a routed endpoint"

    let excerpt (resolver: CurrentStateResolver option) : RequestDelegate =
        RequestDelegate(fun ctx ->
            (task {
                let pairs = EndpointSurface.descriptorsForRoute ctx.RequestServices (routePatternOf ctx)
                let! authAllowed = AuthorizationFilter.filter ctx pairs

                let stateFiltered =
                    match resolver with
                    | None -> authAllowed
                    | Some resolve ->
                        match resolve ctx.Request.Path.Value with
                        | None -> authAllowed
                        | Some current ->
                            authAllowed
                            |> List.filter (fun d ->
                                List.isEmpty d.From || d.From |> List.exists (Excerpt.satisfiesState current))

                ctx.Response.ContentType <- "application/alps+json"
                return! ctx.Response.WriteAsync(Serialization.toJson stateFiltered)
             })
            :> Task)
```

Update `src/Frank.Alps/Frank.Alps.fsproj`:

```xml
    <Compile Include="AlpsDocument.fsi" />
    <Compile Include="AlpsDocument.fs" />
    <Compile Include="Excerpt.fsi" />
    <Compile Include="Excerpt.fs" />
```

- [ ] **Step 5: Run the tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/Frank.Alps test/Frank.Alps.Tests
git commit -m "feat(alps): Excerpt -- CurrentStateResolver, contains-ancestry state matching, Alps.excerpt"
```

---

### Task 16: Sample — both `Link` headers

**Files:**
- Create: `sample/Frank.Alps.Sample/Frank.Alps.Sample.fsproj`, `sample/Frank.Alps.Sample/Program.fs`
- Modify: `Frank.sln`
- Modify: `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`
- Create: `test/Frank.Alps.Tests/SampleIntegrationTests.fs`

**Interfaces:**
- Consumes: everything from Tasks 1-15.
- Produces: a runnable sample demonstrating (a) the app-wide document's app-wide `Link: rel="profile"` header (from `useAlps`) and (b) the per-resource excerpt's resource-scoped `Link: rel="profile"; type="application/alps+json"` header (from `resource { link (...) }`), exactly mirroring `sample/Frank.Rdf.Sample/Program.fs`'s own two-representations-plus-link-header shape.

**Background you need:**

Read `sample/Frank.Rdf.Sample/Program.fs` in full before starting — it's the closest proven precedent (`negotiate { }`, resource-scoped `link`, `webHost { }`). One genuine uncertainty this plan can't resolve without a live build: whether `negotiate { }`'s `accepts "type" (handlerDefinition: HandlerDefinition)` overload (confirmed to exist) propagates that `HandlerDefinition`'s `Metadata` — and so `binds`'s attached `Descriptor` — through into the final composed endpoint's metadata, or whether `NegotiateBuilder` needs its own `binds`-equivalent custom operation added in a follow-up to this task. The sample below assumes the former (`accepts "application/json" (handler { handle ...; binds Catalog.viewGame })`); **verify this against `src/Frank/NegotiateBuilder.fs` before treating this task as done** — if `binds`'s metadata does *not* survive into the endpoint, `EndpointSurface`/`Alps.excerpt` will see no bound descriptor for the `GET` case and the excerpt will be empty for that method, which the integration test below would catch.

- [ ] **Step 1: Write the sample**

Create `sample/Frank.Alps.Sample/Frank.Alps.Sample.fsproj` (mirror `sample/Frank.Rdf.Sample/Frank.Rdf.Sample.fsproj`'s shape — single `net10.0` target, `OutputType Exe`, `ProjectReference`s to `Frank` and `Frank.Alps`).

Create `sample/Frank.Alps.Sample/Program.fs`:

```fsharp
module Frank.Alps.Sample.Program

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Alps

module Catalog =
    let openState = semantic "open" |> doc "Accepting moves" |> def "https://tictactoe.example/states/open"
    let closedState = semantic "closed" |> doc "Game finished" |> def "https://tictactoe.example/states/closed"
    let game = semantic "game" |> doc "A tic-tac-toe game"

    let viewGame = safe "viewGame" |> rt game
    let makeMove = unsafe "makeMove" |> from [ openState ] |> rt closedState

let private getGameJson: RequestDelegate =
    RequestDelegate(fun ctx -> ctx.Response.WriteAsJsonAsync {| id = ctx.Request.RouteValues.["id"] |})

let private makeMoveHandler: RequestDelegate =
    RequestDelegate(fun ctx -> ctx.Response.WriteAsJsonAsync {| ok = true |})

let private gameResource =
    resource "/games/{id}" {
        link (fun ctx ->
            Seq.singleton
                { Target = string ctx.Request.Path
                  Rel = "profile"
                  Params = [ "type", "application/alps+json" ] })

        get (
            negotiate {
                accepts "application/json" (handler {
                    handle getGameJson
                    binds Catalog.viewGame
                })

                accepts "application/alps+json" (Alps.excerpt None)
            }
        )

        post (handler {
            handle makeMoveHandler
            binds Catalog.makeMove
        })
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        resource gameResource

        useAlps [ Catalog.openState; Catalog.closedState; Catalog.game; Catalog.viewGame; Catalog.makeMove ]
    }

    0
```

Register the sample:

```bash
cd "C:/Users/ryanr/Code/frank"
dotnet sln Frank.sln add sample/Frank.Alps.Sample/Frank.Alps.Sample.fsproj
```

- [ ] **Step 2: Write the integration test**

Create `test/Frank.Alps.Tests/SampleIntegrationTests.fs`, following `test/Frank.JsonHome.Tests/IntegrationTests.fs`'s exact `TestServer`/`createServer` pattern (read again here if not still fresh) against `gameResource`/`useAlps` from the sample above — add a `ProjectReference` from `Frank.Alps.Tests` to `Frank.Alps.Sample` (or factor `gameResource`/`Catalog`/`useAlps` registration into a small internal module the test project can reference directly, whichever this repo's existing `*.Sample`/`*.Tests` pairing already does — check `Frank.Rdf.Tests`/`Frank.Rdf.Sample`'s relationship for the established convention and match it):

```fsharp
module Frank.Alps.Tests.SampleIntegrationTests

open Expecto

[<Tests>]
let tests =
    testList
        "Sample: both Link headers"
        [ testAsync "GET /.well-known/alps.json is advertised app-wide via Link: rel=profile" {
              // Arrange: start the sample's webHost via TestServer (per IntegrationTests.fs's pattern).
              // Act: GET /games/1 (any resource route -- useAppWideLinks applies to every response).
              // Assert: response Link header contains `</.well-known/alps.json>; rel="profile"`.
              ()
          }

          testAsync "GET /games/1 advertises the per-resource excerpt via a resource-scoped Link header" {
              // Act: GET /games/1 with Accept: application/json (the primary representation).
              // Assert: response Link header contains
              //   `</games/1>; rel="profile"; type="application/alps+json"`.
              ()
          }

          testAsync "GET /games/1 with Accept: application/alps+json returns the excerpt containing makeMove" {
              // Act: GET /games/1, Accept: application/alps+json.
              // Assert: response body parses as ALPS JSON and contains a descriptor with id "makeMove".
              ()
          }

          testAsync "GET /.well-known/alps.json returns the full profile including openState/closedState/game" {
              // Act: GET /.well-known/alps.json.
              // Assert: response body contains descriptors "open", "closed", "game", "viewGame", "makeMove".
              ()
          } ]
```

*Note to whoever implements this task:* the four `()` bodies are this task's own work (same instruction as Task 13's `AuthorizationFilterTests.fs`) — fill each in against the real `TestServer` pattern before treating Step 2 as done.

Add it to `test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`, before `Program.fs`:

```xml
    <Compile Include="SampleIntegrationTests.fs" />
```

- [ ] **Step 3: Run the sample manually and confirm both `Link` headers by hand**

```bash
cd "C:/Users/ryanr/Code/frank"
dotnet run --project sample/Frank.Alps.Sample/Frank.Alps.Sample.fsproj &
sleep 2
curl -sD - -o /dev/null http://localhost:5000/games/1
curl -sD - -o /dev/null http://localhost:5000/.well-known/alps.json
curl -s -H "Accept: application/alps+json" http://localhost:5000/games/1
kill %1
```

Expected: the first `curl` shows two `Link` headers — one `rel="profile"` pointing at `/.well-known/alps.json` (app-wide) and one `rel="profile"; type="application/alps+json"` pointing at `/games/1` itself (resource-scoped). The third `curl` returns ALPS JSON containing `makeMove`.

- [ ] **Step 4: Run the automated tests and verify they pass**

```bash
dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj
```

Expected: all tests pass, including the four filled-in `SampleIntegrationTests.fs` cases.

- [ ] **Step 5: Commit**

```bash
git add Frank.sln sample/Frank.Alps.Sample test/Frank.Alps.Tests
git commit -m "feat(alps): sample demonstrating both app-wide and per-resource Link headers"
```

---

## Self-Review

**Spec coverage:** Goals 1-6 (design doc) map to Tasks 2-3/9 (authoring, both surfaces), 2-7 (full field coverage), 5/7 (compile-checked references), 6/8 (hierarchy/orthogonality authoring, `ProtocolGraph`), 15 (`CurrentStateResolver` seam), 14/15/16 (both HTTP exposures). Non-goals are respected throughout — no dependency on `Frank.Rdf`/`Frank.Provenance`/`Frank.JsonHome` anywhere in the file list; `[<Struct>]` applied only to `DescriptorType`/`DocFormat` (Task 1); `CompoundProtocolTransition`/multi-document/role-projection/parallel-region filtering are untouched (Out of scope). The `From` field gap caught during spec review (design doc, *Descriptor type*) is reflected correctly in Task 1's type and every task that touches `from`/`ProtocolGraph`/`Excerpt`.

**Placeholder scan:** Two spots intentionally contain `()` test bodies with explicit "this is the task's own work, not a plan gap" notes (Task 13, Task 16) — both are real ASP.NET Core `HttpContext`/`TestServer` setups this plan cannot respecify without the exact private test-helper source of `test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs`/`IntegrationTests.fs`, which were described but not transcribed in full during research; both instruct reading the real file first, matching the pattern already used successfully in Tasks 12/14/16 for the same class of gap. No other placeholders, `TBD`s, or "add appropriate X" phrasing appear.

**Type consistency:** `Descriptor`/`DescriptorType`/`DocFormat`/`Doc`/`Link`/`Ext`/`DescriptorRef` (Task 1) are used identically in every later task. `ProtocolTransition { FromState; Transition; ToState }` (Task 8) matches its use in the design doc and nowhere gets renamed. `CurrentStateResolver = string -> Uri option` (Task 15) matches the design doc's signature. `EndpointSurface.allDescriptors`/`descriptorsForRoute` (Task 12) are consumed with matching signatures in Tasks 14 and 15. `AuthorizationFilter.filter`/`varies` (Task 13) are consumed identically in Tasks 14 and 15.

---

Plan complete and saved to `docs/superpowers/plans/2026-08-02-frank-alps.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** — Execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
