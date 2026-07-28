# HandlerDefinition Metadata Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `HandlerDefinition` openly extensible by collapsing its fixed fields into a metadata list, and move the resource-builder plumbing that operates on it out of `Frank.OpenApi` and into Frank core where it belongs.

**Architecture:** `HandlerDefinition` becomes `{ Handler: RequestDelegate; Metadata: obj list }`. Its six existing fields are already mapped 1:1 onto stock ASP.NET metadata types by `HandlerDefinitionMetadata.toConventions`, so the fields are a redundant staging copy — the `handler` computation expression operations write those metadata objects directly instead. `src/Frank.OpenApi/ResourceBuilderExtensions.fs` contains no OpenAPI code at all (it opens `Frank.Builder` and operates on `ResourceSpec` and `HandlerDefinition`, both core types); its overloads become intrinsic members of `ResourceBuilder` and its private per-method convention wrapper becomes a public `ResourceBuilder.AddMethodMetadata`.

**Tech Stack:** F# 8.0+, .NET 8.0/9.0/10.0 multi-targeting, ASP.NET Core, Expecto for tests.

## Global Constraints

- `src/Frank/Frank.fsproj` targets `net8.0;net9.0;net10.0`. All code moved into it MUST compile on all three. `src/Frank.OpenApi/Frank.OpenApi.fsproj` is single-target `net10.0`, so code moving out of it has never been compiled against net8.0/net9.0 before.
- Every `.fs` file under `src/Frank.*/` has a matching `.fsi` signature file listed directly above it in the `.fsproj` `<Compile>` order. Both must be updated together.
- Signature mismatches only surface at compile time and only on some TFMs. Verify with a real build across every TFM, not just net10.0.
- The `handler` computation expression operations (`handle`, `name`, `summary`, `description`, `tags`, `produces`, `producesEmpty`, `accepts`) MUST keep their exact current signatures. No handler-authoring code in samples or tests may need to change.
- `Frank.OpenApi` must continue to build and pass its tests unchanged apart from its `.fsproj` file list.
- This is a **minor version bump**, not a major one: `ProducesInfo` and `AcceptsInfo` are removed, but the authoring surface is untouched.

## Conflict Warning

Task 3 **deletes** `src/Frank.OpenApi/ResourceBuilderExtensions.fs` and `.fsi` and edits `src/Frank.OpenApi/Frank.OpenApi.fsproj`. If another session is editing those files, coordinate before starting Task 3. Tasks 1 and 2 touch only `src/Frank/` and `test/Frank.Tests/`.

## File Structure

| File | Change | Responsibility after |
|---|---|---|
| `src/Frank/HandlerDefinition.fsi` / `.fs` | Rewrite | The `HandlerDefinition` record, typed metadata accessors, and projection to endpoint conventions |
| `src/Frank/HandlerBuilder.fsi` / `.fs` | Modify | The `handler` CE; each operation now appends a stock ASP.NET metadata object |
| `src/Frank/ResourceBuilder.fsi` / `.fs` | Modify | Adds `AddMethodMetadata` and seven `HandlerDefinition` overloads |
| `test/Frank.Tests/HandlerBuilderTests.fs` | Rewrite | Asserts against emitted metadata rather than staging fields |
| `test/Frank.Tests/ResourceBuilderMetadataTests.fs` | Create | Covers per-method metadata scoping |
| `src/Frank.OpenApi/ResourceBuilderExtensions.fsi` / `.fs` | Delete | — |
| `src/Frank.OpenApi/Frank.OpenApi.fsproj` | Modify | Drops the two deleted files |

---

### Task 1: Collapse HandlerDefinition to a metadata list

**Files:**
- Modify: `src/Frank/HandlerDefinition.fsi` (full rewrite, 37 lines)
- Modify: `src/Frank/HandlerDefinition.fs` (full rewrite, 84 lines)
- Modify: `src/Frank/HandlerBuilder.fs:41-108` (metadata operations)
- Test: `test/Frank.Tests/HandlerBuilderTests.fs` (full rewrite)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `type HandlerDefinition = { Handler: RequestDelegate; Metadata: obj list }` with `static member Empty`
  - `HandlerDefinition.addMetadata : obj -> HandlerDefinition -> HandlerDefinition`
  - `HandlerDefinition.tryFind<'T when 'T : not struct> : HandlerDefinition -> 'T option`
  - `HandlerDefinition.findAll<'T when 'T : not struct> : HandlerDefinition -> 'T list`
  - `HandlerDefinitionMetadata.toConventions : HandlerDefinition -> (EndpointBuilder -> unit) list` (unchanged signature)

**Background you need:**

`HandlerDefinition.fs:45-83` currently maps each field onto a stock ASP.NET metadata type. Preserve these mappings exactly — they are the behaviour contract:

| Field | Emitted metadata |
|---|---|
| `Name` | `EndpointNameMetadata(name)` |
| `Summary` | `EndpointSummaryAttribute(summary)` |
| `Description` | `EndpointDescriptionAttribute(description)` |
| `Tags` | `TagsAttribute(tags)` |
| `Produces` | `ProducesResponseTypeMetadata(statusCode, type, contentTypes)` |
| `Accepts` | `AcceptsMetadata(contentTypes, requestType, isOptional)` |

Two behaviours that are easy to break:

1. `producesEmpty n` currently emits `ProducesResponseTypeMetadata(n, typeof<Void>, [| "application/json" |])`. The empty `ContentTypes` list is replaced by the JSON default inside `toConventions`, so the emitted content types are **not** empty. Preserve that.
2. Metadata order changes from a fixed order (name, summary, description, tags, produces, accepts) to CE declaration order. That is the intended new behaviour; assert it.

A module and a type may not share a name in F# unless the module carries `[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]`. The `HandlerDefinition` module needs it, in **both** the `.fs` and the `.fsi`.

- [ ] **Step 1: Rewrite the test file to assert against emitted metadata**

Replace the entire contents of `test/Frank.Tests/HandlerBuilderTests.fs`:

```fsharp
module Frank.Tests.HandlerBuilderTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Expecto
open Frank.Builder

// Sample types for testing
type Product = { Name: string; Price: decimal }
type CreateRequest = { Name: string }

/// Stands in for metadata an external library would attach.
type CustomMarker(label: string) =
    member _.Label = label

[<Tests>]
let tests =
    testList
        "HandlerBuilder"
        [ test "handler with handle operation produces HandlerDefinition with handler set" {
              let def = handler { handle (fun (ctx: HttpContext) -> Task.CompletedTask) }

              Expect.isNotNull def.Handler "Handler should be set"
              Expect.isEmpty def.Metadata "Metadata should be empty"
          }

          test "handler with metadata operations emits endpoint metadata" {
              let def =
                  handler {
                      name "createProduct"
                      summary "Creates a new product"
                      description "Detailed description of product creation"
                      tags [ "Products"; "Admin" ]
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let nameMeta = HandlerDefinition.tryFind<IEndpointNameMetadata> def
              Expect.isSome nameMeta "Name metadata should be present"
              Expect.equal nameMeta.Value.EndpointName "createProduct" "Name should be set"

              let summaryMeta = HandlerDefinition.tryFind<IEndpointSummaryMetadata> def
              Expect.isSome summaryMeta "Summary metadata should be present"
              Expect.equal summaryMeta.Value.Summary "Creates a new product" "Summary should be set"

              let descMeta = HandlerDefinition.tryFind<IEndpointDescriptionMetadata> def
              Expect.isSome descMeta "Description metadata should be present"

              Expect.equal
                  descMeta.Value.Description
                  "Detailed description of product creation"
                  "Description should be set"

              let tagsMeta = HandlerDefinition.tryFind<ITagsMetadata> def
              Expect.isSome tagsMeta "Tags metadata should be present"
              Expect.sequenceEqual tagsMeta.Value.Tags [ "Products"; "Admin" ] "Tags should be set"
          }

          test "handler with produces operation emits response metadata" {
              let def =
                  handler {
                      produces typeof<Product> 200
                      produces typeof<Product> 201
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let produces = HandlerDefinition.findAll<IProducesResponseTypeMetadata> def
              Expect.hasLength produces 2 "Should have 2 produces entries"

              Expect.equal produces.[0].StatusCode 200 "First status code should be 200"
              Expect.equal produces.[0].Type (typeof<Product>) "First response type should be Product"

              Expect.sequenceEqual
                  produces.[0].ContentTypes
                  [ "application/json" ]
                  "First content types should be default"

              Expect.equal produces.[1].StatusCode 201 "Second status code should be 201"
          }

          test "handler with producesEmpty operation emits Void response metadata" {
              let def =
                  handler {
                      producesEmpty 204
                      producesEmpty 404
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let produces = HandlerDefinition.findAll<IProducesResponseTypeMetadata> def
              Expect.hasLength produces 2 "Should have 2 produces entries"

              Expect.equal produces.[0].StatusCode 204 "First status code should be 204"
              Expect.equal produces.[0].Type (typeof<Void>) "First response type should be Void"

              Expect.sequenceEqual
                  produces.[0].ContentTypes
                  [ "application/json" ]
                  "Empty responses still carry the JSON default content type"

              Expect.equal produces.[1].StatusCode 404 "Second status code should be 404"
          }

          test "handler with accepts operation emits request metadata" {
              let def =
                  handler {
                      accepts typeof<CreateRequest>
                      accepts typeof<Product>
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let accepts = HandlerDefinition.findAll<IAcceptsMetadata> def
              Expect.hasLength accepts 2 "Should have 2 accepts entries"

              Expect.equal accepts.[0].RequestType (typeof<CreateRequest>) "First request type should be CreateRequest"

              Expect.sequenceEqual
                  accepts.[0].ContentTypes
                  [ "application/json" ]
                  "First content types should be default"

              Expect.isFalse accepts.[0].IsOptional "First should not be optional"
              Expect.equal accepts.[1].RequestType (typeof<Product>) "Second request type should be Product"
          }

          test "handler with all metadata combined accumulates correctly" {
              let handlerDef: HandlerDefinition =
                  handler {
                      name "createProduct"
                      summary "Create product"
                      description "Creates a new product in the catalog"
                      tags [ "Products" ]
                      produces typeof<Product> 201
                      producesEmpty 400
                      accepts typeof<CreateRequest>
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              Expect.hasLength handlerDef.Metadata 7 "Should have 7 metadata entries"

              Expect.hasLength
                  (HandlerDefinition.findAll<IProducesResponseTypeMetadata> handlerDef)
                  2
                  "Should have 2 produces entries"

              Expect.hasLength
                  (HandlerDefinition.findAll<IAcceptsMetadata> handlerDef)
                  1
                  "Should have 1 accepts entry"

              Expect.isNotNull handlerDef.Handler "Handler should be set"
          }

          test "metadata is retained in declaration order" {
              let def =
                  handler {
                      name "first"
                      tags [ "second" ]
                      producesEmpty 204
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let kinds =
                  def.Metadata
                  |> List.map (fun m ->
                      match m with
                      | :? IEndpointNameMetadata -> "name"
                      | :? ITagsMetadata -> "tags"
                      | :? IProducesResponseTypeMetadata -> "produces"
                      | _ -> "other")

              Expect.equal kinds [ "name"; "tags"; "produces" ] "Order should match declaration order"
          }

          test "external metadata can be attached and read back" {
              let def =
                  handler { handle (fun (ctx: HttpContext) -> Task.CompletedTask) }
                  |> HandlerDefinition.addMetadata (CustomMarker "discovery")

              let marker = HandlerDefinition.tryFind<CustomMarker> def
              Expect.isSome marker "Custom metadata should be readable"
              Expect.equal marker.Value.Label "discovery" "Custom metadata should round-trip"
          }

          test "handler without handle operation fails validation" {
              let buildInvalidHandler () = handler { name "incomplete" } |> ignore

              Expect.throws buildInvalidHandler "Should throw when handler is not set"
          }

          test "handler with async<unit> handler converts to Task correctly" {
              let def = handler { handle (fun (ctx: HttpContext) -> async { do () }) }

              Expect.isNotNull def.Handler "Handler should be set"
          }

          test "handler with async<'a> handler converts to Task<'a> correctly" {
              let def = handler { handle (fun (ctx: HttpContext) -> async { return "result" }) }

              Expect.isNotNull def.Handler "Handler should be set"
          }

          test "handler with Task<'a> handler is accepted" {
              let def = handler { handle (fun (ctx: HttpContext) -> Task.FromResult("result")) }

              Expect.isNotNull def.Handler "Handler should be set"
          }

          test "handler with custom content types for content negotiation" {
              let def =
                  handler {
                      produces typeof<Product> 200 [ "application/xml"; "application/json" ]
                      accepts typeof<CreateRequest> [ "application/xml" ]
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let produces = HandlerDefinition.findAll<IProducesResponseTypeMetadata> def
              Expect.hasLength produces 1 "Should have 1 produces entry"

              Expect.containsAll
                  produces.[0].ContentTypes
                  [ "application/xml"; "application/json" ]
                  "Should support both XML and JSON"

              let accepts = HandlerDefinition.findAll<IAcceptsMetadata> def
              Expect.hasLength accepts 1 "Should have 1 accepts entry"
              Expect.contains accepts.[0].ContentTypes "application/xml" "Should accept XML"
          } ]
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test test/Frank.Tests/Frank.Tests.fsproj
```

Expected: FAIL at compile time with errors like `The type 'HandlerDefinition' does not define the field, constructor or member 'Metadata'` and `The value, namespace, type or module 'HandlerDefinition' is not defined`.

- [ ] **Step 3: Rewrite `src/Frank/HandlerDefinition.fsi`**

Replace the entire file:

```fsharp
namespace Frank.Builder

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

[<AutoOpen>]
module internal MediaTypes =
    [<Literal>]
    val ApplicationJson : string = "application/json"

/// A request handler together with the endpoint metadata it contributes.
/// Metadata is an open list so that extension libraries can attach their own
/// types without Frank core knowing about them.
type HandlerDefinition =
    { Handler: RequestDelegate
      Metadata: obj list }

    static member Empty : HandlerDefinition

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HandlerDefinition =

    /// Appends a metadata object, preserving declaration order.
    val addMetadata : metadata:obj -> def:HandlerDefinition -> HandlerDefinition

    /// The first metadata entry assignable to 'T, if any.
    val tryFind<'T when 'T : not struct> : def:HandlerDefinition -> 'T option

    /// Every metadata entry assignable to 'T, in declaration order.
    val findAll<'T when 'T : not struct> : def:HandlerDefinition -> 'T list

module HandlerDefinitionMetadata =

    val toConventions : def:HandlerDefinition -> (EndpointBuilder -> unit) list
```

- [ ] **Step 4: Rewrite `src/Frank/HandlerDefinition.fs`**

Replace the entire file:

```fsharp
namespace Frank.Builder

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http

[<AutoOpen>]
module internal MediaTypes =
    [<Literal>]
    let ApplicationJson = "application/json"

type HandlerDefinition =
    { Handler: RequestDelegate
      Metadata: obj list }

    static member Empty =
        { Handler = Unchecked.defaultof<_>
          Metadata = [] }

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HandlerDefinition =

    let addMetadata (metadata: obj) (def: HandlerDefinition) =
        { def with
            Metadata = def.Metadata @ [ metadata ] }

    let tryFind<'T when 'T: not struct> (def: HandlerDefinition) : 'T option =
        def.Metadata
        |> List.tryPick (fun m ->
            match m with
            | :? 'T as t -> Some t
            | _ -> None)

    let findAll<'T when 'T: not struct> (def: HandlerDefinition) : 'T list =
        def.Metadata
        |> List.choose (fun m ->
            match m with
            | :? 'T as t -> Some t
            | _ -> None)

module HandlerDefinitionMetadata =

    let toConventions (def: HandlerDefinition) : (EndpointBuilder -> unit) list =
        def.Metadata
        |> List.map (fun m -> fun (b: EndpointBuilder) -> b.Metadata.Add m)
```

- [ ] **Step 5: Rewrite the metadata operations in `src/Frank/HandlerBuilder.fs`**

Replace lines 40-108 (everything from the `// Metadata operations` comment through the last `Accepts` member) with:

```fsharp
    // Metadata operations
    [<CustomOperation("name")>]
    member _.Name(def: HandlerDefinition, name: string) =
        HandlerDefinition.addMetadata (EndpointNameMetadata(name)) def

    [<CustomOperation("summary")>]
    member _.Summary(def: HandlerDefinition, summary: string) =
        HandlerDefinition.addMetadata (EndpointSummaryAttribute(summary)) def

    [<CustomOperation("description")>]
    member _.Description(def: HandlerDefinition, description: string) =
        HandlerDefinition.addMetadata (EndpointDescriptionAttribute(description)) def

    [<CustomOperation("tags")>]
    member _.Tags(def: HandlerDefinition, tags: string list) =
        HandlerDefinition.addMetadata (TagsAttribute(tags |> List.toArray)) def

    // Response type operations
    [<CustomOperation("produces")>]
    member _.Produces(def: HandlerDefinition, responseType: Type, statusCode: int) =
        HandlerDefinition.addMetadata
            (ProducesResponseTypeMetadata(statusCode, responseType, [| ApplicationJson |]))
            def

    [<CustomOperation("produces")>]
    member _.Produces(def: HandlerDefinition, responseType: Type, statusCode: int, contentTypes: string list) =
        let contentTypes =
            if List.isEmpty contentTypes then
                [| ApplicationJson |]
            else
                contentTypes |> Array.ofList

        HandlerDefinition.addMetadata (ProducesResponseTypeMetadata(statusCode, responseType, contentTypes)) def

    [<CustomOperation("producesEmpty")>]
    member _.ProducesEmpty(def: HandlerDefinition, statusCode: int) =
        HandlerDefinition.addMetadata
            (ProducesResponseTypeMetadata(statusCode, typeof<Void>, [| ApplicationJson |]))
            def

    // Request type operation
    [<CustomOperation("accepts")>]
    member _.Accepts(def: HandlerDefinition, requestType: Type) =
        HandlerDefinition.addMetadata (AcceptsMetadata([| ApplicationJson |], requestType, false)) def

    [<CustomOperation("accepts")>]
    member _.Accepts(def: HandlerDefinition, requestType: Type, contentTypes: string list) =
        let contentTypes =
            if List.isEmpty contentTypes then
                [| ApplicationJson |]
            else
                contentTypes |> Array.ofList

        HandlerDefinition.addMetadata (AcceptsMetadata(contentTypes, requestType, false)) def
```

Then add these two `open` statements after `open Microsoft.AspNetCore.Http` at the top of the file (lines 3-5):

```fsharp
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
```

`src/Frank/HandlerBuilder.fsi` needs **no change** — every operation keeps its signature.

- [ ] **Step 6: Build across all target frameworks**

```bash
dotnet build src/Frank/Frank.fsproj
```

Expected: succeeds for net8.0, net9.0, and net10.0. If `AcceptsMetadata` or `ProducesResponseTypeMetadata` is not resolvable, check the `open` statements from Step 5 — the old code resolved them through `Microsoft.AspNetCore.Http.Metadata` and `Microsoft.AspNetCore.Routing`.

- [ ] **Step 7: Run the tests to verify they pass**

```bash
dotnet test test/Frank.Tests/Frank.Tests.fsproj
```

Expected: PASS, all tests.

- [ ] **Step 8: Verify dependent projects still build**

```bash
dotnet build src/Frank.OpenApi/Frank.OpenApi.fsproj
dotnet build src/Frank.Auth/Frank.Auth.fsproj
```

Expected: both succeed. `Frank.OpenApi` uses only `def.Handler` and `HandlerDefinitionMetadata.toConventions`, both unchanged.

- [ ] **Step 9: Commit**

```bash
git add src/Frank/HandlerDefinition.fsi src/Frank/HandlerDefinition.fs src/Frank/HandlerBuilder.fs test/Frank.Tests/HandlerBuilderTests.fs
git commit -m "refactor(core): collapse HandlerDefinition fields into a metadata list

The six staging fields were already mapped 1:1 onto stock ASP.NET
metadata types by toConventions, so they were a redundant copy. The
handler CE operations now write those metadata objects directly, and
the list is open so extension libraries can attach their own types.

Removes ProducesInfo and AcceptsInfo. Every handler CE operation keeps
its signature, so no handler-authoring code changes."
```

---

### Task 2: Add per-method metadata scoping to ResourceBuilder

**Files:**
- Modify: `src/Frank/ResourceBuilder.fsi:30` (add member after `AddMetadata`)
- Modify: `src/Frank/ResourceBuilder.fs:64-66` (add member after `AddMetadata`)
- Test: `test/Frank.Tests/ResourceBuilderMetadataTests.fs` (create)
- Modify: `test/Frank.Tests/Frank.Tests.fsproj` (register the new test file)

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `ResourceBuilder.AddMethodMetadata : httpMethod:string * spec:ResourceSpec * convention:(EndpointBuilder -> unit) -> ResourceSpec`

**Background you need:**

`ResourceSpec.Metadata` conventions are applied to **every** endpoint in a resource — see `ResourceBuilder.fs:44-45`, which loops all conventions over each endpoint builder. `ResourceBuilder.fs:41` adds `HttpMethodMetadata` to each builder *before* that loop runs, so a convention can inspect the builder to discover which method it is being applied to and no-op for the others. `Frank.OpenApi` already relies on this; Task 3 needs it too, so it becomes a first-class core operation.

- [ ] **Step 1: Write the failing test**

Create `test/Frank.Tests/ResourceBuilderMetadataTests.fs`:

```fsharp
module Frank.Tests.ResourceBuilderMetadataTests

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Expecto
open Frank.Builder

type MethodMarker(label: string) =
    member _.Label = label

let private endpointFor (resource: Resource) (httpMethod: string) =
    resource.Endpoints
    |> Array.find (fun e ->
        match e.Metadata.GetMetadata<HttpMethodMetadata>() with
        | null -> false
        | meta -> meta.HttpMethods |> Seq.contains httpMethod)

[<Tests>]
let tests =
    testList
        "ResourceBuilder.AddMethodMetadata"
        [ test "method-scoped metadata lands only on the matching endpoint" {
              let spec =
                  ResourceSpec.Empty
                  |> fun s -> ResourceBuilder.AddHandler("GET", s, RequestDelegate(fun _ -> Task.CompletedTask))
                  |> fun s -> ResourceBuilder.AddHandler("POST", s, RequestDelegate(fun _ -> Task.CompletedTask))
                  |> fun s ->
                      ResourceBuilder.AddMethodMetadata(
                          "POST",
                          s,
                          fun b -> b.Metadata.Add(MethodMarker "post-only")
                      )

              let resource = spec.Build("/things")

              let postMarker = (endpointFor resource "POST").Metadata.GetMetadata<MethodMarker>()
              Expect.isNotNull postMarker "POST endpoint should carry the marker"
              Expect.equal postMarker.Label "post-only" "Marker label should match"

              let getMarker = (endpointFor resource "GET").Metadata.GetMetadata<MethodMarker>()
              Expect.isNull getMarker "GET endpoint should not carry the marker"
          }

          test "resource-wide metadata still lands on every endpoint" {
              let spec =
                  ResourceSpec.Empty
                  |> fun s -> ResourceBuilder.AddHandler("GET", s, RequestDelegate(fun _ -> Task.CompletedTask))
                  |> fun s -> ResourceBuilder.AddHandler("POST", s, RequestDelegate(fun _ -> Task.CompletedTask))
                  |> fun s -> ResourceBuilder.AddMetadata(s, fun b -> b.Metadata.Add(MethodMarker "everywhere"))

              let resource = spec.Build("/things")

              for httpMethod in [ "GET"; "POST" ] do
                  let marker = (endpointFor resource httpMethod).Metadata.GetMetadata<MethodMarker>()
                  Expect.isNotNull marker (httpMethod + " endpoint should carry the marker")
          } ]
```

Register it in `test/Frank.Tests/Frank.Tests.fsproj`, directly after `HandlerBuilderTests.fs`:

```xml
    <Compile Include="HandlerBuilderTests.fs" />
    <Compile Include="ResourceBuilderMetadataTests.fs" />
    <Compile Include="MiddlewareOrderingTests.fs" />
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~ResourceBuilder"
```

Expected: FAIL at compile time with `The type 'ResourceBuilder' does not define the field, constructor or member 'AddMethodMetadata'`.

- [ ] **Step 3: Add the member to `src/Frank/ResourceBuilder.fs`**

Insert directly after the existing `AddMetadata` member (which ends at line 66):

```fsharp
    static member AddMethodMetadata
        (
            httpMethod: string,
            spec: ResourceSpec,
            convention: EndpointBuilder -> unit
        ) : ResourceSpec =
        // ResourceSpec.Metadata conventions run against every endpoint in the
        // resource. Build() adds HttpMethodMetadata before running them, so a
        // convention can scope itself by inspecting the builder.
        let methodScoped (builder: EndpointBuilder) =
            let matches =
                builder.Metadata
                |> Seq.tryPick (fun m ->
                    match m with
                    | :? HttpMethodMetadata as meta -> Some meta
                    | _ -> None)
                |> Option.map (fun meta -> meta.HttpMethods |> Seq.contains httpMethod)
                |> Option.defaultValue false

            if matches then
                convention builder

        ResourceBuilder.AddMetadata(spec, methodScoped)
```

- [ ] **Step 4: Add the signature to `src/Frank/ResourceBuilder.fsi`**

Insert directly after the existing `AddMetadata` line (line 30):

```fsharp
    static member AddMethodMetadata:
        httpMethod: string * spec: ResourceSpec * convention: (EndpointBuilder -> unit) -> ResourceSpec
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~ResourceBuilder"
```

Expected: PASS, 2 tests.

- [ ] **Step 6: Build across all target frameworks**

```bash
dotnet build src/Frank/Frank.fsproj
```

Expected: succeeds for net8.0, net9.0, and net10.0.

- [ ] **Step 7: Commit**

```bash
git add src/Frank/ResourceBuilder.fsi src/Frank/ResourceBuilder.fs test/Frank.Tests/ResourceBuilderMetadataTests.fs test/Frank.Tests/Frank.Tests.fsproj
git commit -m "feat(core): add ResourceBuilder.AddMethodMetadata

Resource metadata conventions apply to every endpoint in the resource.
Frank.OpenApi worked around this with a private wrapper that scopes a
convention to one HTTP method; a second consumer is coming, so promote
it to a core operation instead of copying it."
```

---

### Task 3: Move the HandlerDefinition overloads into Frank core

**Files:**
- Modify: `src/Frank/ResourceBuilder.fs` (add seven overloads)
- Modify: `src/Frank/ResourceBuilder.fsi` (add seven signatures)
- Delete: `src/Frank.OpenApi/ResourceBuilderExtensions.fs`
- Delete: `src/Frank.OpenApi/ResourceBuilderExtensions.fsi`
- Modify: `src/Frank.OpenApi/Frank.OpenApi.fsproj:10-11`

**Interfaces:**
- Consumes: `HandlerDefinition` and `HandlerDefinitionMetadata.toConventions` from Task 1; `ResourceBuilder.AddMethodMetadata` from Task 2.
- Produces: `ResourceBuilder` members `Get`, `Post`, `Put`, `Delete`, `Patch`, `Head`, `Options`, each with a `spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec` overload.

**Background you need:**

`src/Frank.OpenApi/ResourceBuilderExtensions.fs` has no OpenAPI content — it opens `Frank.Builder` and operates on `ResourceSpec` and `HandlerDefinition`, both core types. It lives in `Frank.OpenApi` only because `HandlerDefinition` used to, before commit 5315f52a moved it to core.

Because `ResourceBuilder.fs` compiles *after* `HandlerDefinition.fs` in `Frank.fsproj`, these can be **intrinsic** members rather than type extensions. That matters for two reasons: intrinsic members get better overload resolution, and `[<CustomOperation>]` must appear on exactly one overload of a given name. The existing `.fsi` already establishes the pattern — `[<CustomOperation("get")>]` sits on the `RequestDelegate` overload only (`ResourceBuilder.fsi:74-75`) and the remaining `Get` overloads carry no attribute. Add the new overloads **without** the attribute.

Overload order in the `.fsi` must match the `.fs`. Put each new `HandlerDefinition` overload last within its method's group.

`Connect` and `Trace` deliberately get no `HandlerDefinition` overload — matching the current `Frank.OpenApi` set exactly. Do not add them.

- [ ] **Step 1: Write the failing test**

Append this test to the `testList` in `test/Frank.Tests/ResourceBuilderMetadataTests.fs`, before the closing `]`:

```fsharp
          test "handler definition metadata is scoped to its own HTTP method" {
              let listing =
                  handler {
                      name "listThings"
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let creating =
                  handler {
                      name "createThing"
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let built =
                  resource "/things" {
                      get listing
                      post creating
                  }

              let getName =
                  (endpointFor built "GET").Metadata.GetMetadata<Microsoft.AspNetCore.Routing.EndpointNameMetadata>()

              Expect.isNotNull getName "GET endpoint should carry a name"
              Expect.equal getName.EndpointName "listThings" "GET should carry its own name only"

              let postName =
                  (endpointFor built "POST").Metadata.GetMetadata<Microsoft.AspNetCore.Routing.EndpointNameMetadata>()

              Expect.isNotNull postName "POST endpoint should carry a name"
              Expect.equal postName.EndpointName "createThing" "POST should carry its own name only"
          }
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~ResourceBuilder"
```

Expected: FAIL at compile time — `Frank.Tests` references only `src/Frank`, so the `HandlerDefinition` overloads of `get`/`post` do not exist yet. The error names the `get` custom operation or reports no matching overload.

- [ ] **Step 3: Add the shared helper to `src/Frank/ResourceBuilder.fs`**

Insert directly after the `AddMethodMetadata` member added in Task 2:

```fsharp
    static member AddHandlerDefinition(httpMethod: string, spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec =
        let specWithHandler =
            { spec with
                Handlers = (httpMethod, def.Handler) :: spec.Handlers }

        HandlerDefinitionMetadata.toConventions def
        |> List.fold
            (fun s conv -> ResourceBuilder.AddMethodMetadata(httpMethod, s, conv))
            specWithHandler
```

- [ ] **Step 4: Add the seven overloads to `src/Frank/ResourceBuilder.fs`**

Add each one as the **last** member of its existing method group — after the `(HttpContext -> unit)` overload for that method. For example, `Get`'s group currently ends with `member __.Get(spec, handler: HttpContext -> unit)`; the new member goes immediately after it.

```fsharp
    member _.Get(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Get, spec, handlerDef)
```

```fsharp
    member _.Post(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Post, spec, handlerDef)
```

```fsharp
    member _.Put(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Put, spec, handlerDef)
```

```fsharp
    member _.Delete(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Delete, spec, handlerDef)
```

```fsharp
    member _.Patch(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Patch, spec, handlerDef)
```

```fsharp
    member _.Head(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Head, spec, handlerDef)
```

```fsharp
    member _.Options(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Options, spec, handlerDef)
```

- [ ] **Step 5: Add the signatures to `src/Frank/ResourceBuilder.fsi`**

Add `AddHandlerDefinition` directly after the `AddMethodMetadata` signature from Task 2:

```fsharp
    static member AddHandlerDefinition:
        httpMethod: string * spec: ResourceSpec * def: HandlerDefinition -> ResourceSpec
```

Then add each overload as the **last** entry in its method's group, matching the `.fs` order exactly. For `Get`, that means after `member Get: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec` (line 85):

```fsharp
    member Get: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
```

Repeat the same pattern for `Post`, `Put`, `Delete`, `Patch`, `Head`, and `Options`, each as the last member of its own group:

```fsharp
    member Post: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member Put: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member Delete: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member Patch: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member Head: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member Options: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
```

- [ ] **Step 6: Run the test to verify it passes**

```bash
dotnet test test/Frank.Tests/Frank.Tests.fsproj
```

Expected: PASS, all tests. If you get "A unique overload for method could not be determined", the `.fsi` overload order does not match the `.fs` — fix the order rather than removing overloads.

- [ ] **Step 7: Delete the Frank.OpenApi copies**

```bash
git rm src/Frank.OpenApi/ResourceBuilderExtensions.fs src/Frank.OpenApi/ResourceBuilderExtensions.fsi
```

Then remove these two lines from `src/Frank.OpenApi/Frank.OpenApi.fsproj` (lines 10-11):

```xml
    <Compile Include="ResourceBuilderExtensions.fsi" />
    <Compile Include="ResourceBuilderExtensions.fs" />
```

The `<ItemGroup>` should be left as:

```xml
  <ItemGroup>
    <Compile Include="WebHostBuilderExtensions.fsi" />
    <Compile Include="WebHostBuilderExtensions.fs" />
  </ItemGroup>
```

- [ ] **Step 8: Build and test everything**

```bash
dotnet build src/Frank/Frank.fsproj
dotnet build Frank.sln
dotnet test test/Frank.Tests/Frank.Tests.fsproj
dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj
dotnet test test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj
```

Expected: all succeed. `Frank.OpenApi.Tests` and the samples reach the overloads through `Frank.Builder` now instead of `Frank.OpenApi`; because `resource` itself comes from `Frank.Builder`, any file using the CE already has that namespace open, so no source changes should be needed. If a sample fails to compile, add `open Frank.Builder` to it rather than restoring the deleted files.

- [ ] **Step 9: Commit**

```bash
git add -A src/Frank/ResourceBuilder.fsi src/Frank/ResourceBuilder.fs src/Frank.OpenApi/ test/Frank.Tests/ResourceBuilderMetadataTests.fs
git commit -m "refactor: move HandlerDefinition resource overloads to Frank core

ResourceBuilderExtensions in Frank.OpenApi contained no OpenAPI code --
it operated on ResourceSpec and HandlerDefinition, both core types, and
lived there only because HandlerDefinition used to. The overloads become
intrinsic ResourceBuilder members built on AddMethodMetadata.

Frank.OpenApi is left holding only its OpenAPI wiring."
```

---

## Verification Checklist

Run after all three tasks:

- [ ] `dotnet build src/Frank/Frank.fsproj` succeeds on net8.0, net9.0, and net10.0
- [ ] `dotnet build Frank.sln` succeeds
- [ ] `dotnet test test/Frank.Tests/Frank.Tests.fsproj` passes
- [ ] `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj` passes
- [ ] `dotnet test test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj` passes
- [ ] `dotnet test test/Frank.Datastar.Tests/Frank.Datastar.Tests.fsproj` passes
- [ ] `git grep -n "ProducesInfo\|AcceptsInfo"` returns nothing
- [ ] `git grep -rn "Frank.OpenApi.ResourceBuilderExtensions"` returns nothing
- [ ] Samples build: `dotnet build sample/Frank.OpenApi.Sample/Frank.OpenApi.Sample.fsproj`
