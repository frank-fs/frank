# Frank.JsonHome Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Serve a [JSON Home](https://datatracker.ietf.org/doc/html/draft-nottingham-json-home-06) document describing a Frank application's entry-point resources, advertised by a `Link` header on every response, filtered by the current principal's authorization.

**Architecture:** A new `Frank.JsonHome` package projects ASP.NET's `ApiDescription` collection (the same description that feeds OpenAPI) into a format-neutral `ApiSurface`, then serializes it as `application/json-home`. Resources opt in by declaring a link relation type. Frank core gains a shared `IResponseLinkProvider` mechanism so that `Link` has one owner instead of a middleware per extension.

**Tech Stack:** F# 8.0+, .NET 8.0/9.0/10.0 multi-targeting, ASP.NET Core (ApiExplorer, `IAuthorizationService`), `System.Text.Json`, Expecto.

**Design doc:** `docs/superpowers/specs/2026-07-28-frank-jsonhome-design.md`

## Global Constraints

- `Frank.JsonHome` targets `net8.0;net9.0;net10.0` and MUST have **zero NuGet package dependencies** — only `FrameworkReference Microsoft.AspNetCore.App` and `ProjectReference ../Frank/Frank.fsproj`.
- No dependency on `Frank.Auth` or `Frank.OpenApi`. Authorization filtering reads stock `IAuthorizeData` / `AuthorizationPolicy` endpoint metadata.
- Every `.fs` file under `src/Frank.*/` has a matching `.fsi` signature file listed directly above it in the `.fsproj` `<Compile>` order. Both must be updated together. Verify with a real build across every TFM — signature mismatches surface only at compile time.
- JSON Home hint names use draft-06 **camelCase** (`acceptPatch`, `acceptPost`, `acceptPut`, `acceptRanges`, `acceptPrefer`, `preconditionRequired`, `authSchemes`, `docs`, `status`, `allow`, `formats`). Earlier drafts used hyphenated names; most blog posts still show those. Do not copy them.
- Media type is exactly `application/json-home`.
- Default document path is `/.well-known/home.json`; default link relation type is `home`. Both configurable. Neither is IANA-registered — that is a known, accepted project convention, not an oversight.
- `WebHostBuilder.Run` builds *and blocks*, so integration tests cannot use the `webHost { }` CE. Middleware and endpoint logic MUST be exposed as plain functions that `useJsonHome` composes, so tests can wire them into a `TestServer` directly. Mirror the harness in `test/Frank.Auth.Tests/AuthorizationTests.fs:26-97`.

## Independence

This plan does **not** depend on `docs/superpowers/plans/2026-07-28-handlerdefinition-metadata-refactor.md`. JSON Home needs no handler-level metadata — `rel`, `hrefVar`, `docs`, `deprecated`, and `gone` are all resource-level, and `hints.formats` / `acceptPost` come from the existing `produces` / `accepts`. The two efforts touch disjoint files. The only shared file is `src/Frank/Frank.fsproj`, where both add `<Compile>` entries — expect a trivial conflict there if both land at once.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/Frank/WebLink.fsi` / `.fs` | Create | `WebLink`, `IResponseLinkProvider`, RFC 8288 formatting, the response middleware |
| `src/Frank/WebHostBuilder.fsi` / `.fs` | Modify | `link` operation; installs the link middleware when providers exist |
| `src/Frank/Frank.fsproj` | Modify | Register `WebLink.fsi` / `.fs` before `WebHostBuilder` |
| `src/Frank.JsonHome/UriTemplate.fsi` / `.fs` | Create | ASP.NET route template → RFC 6570 |
| `src/Frank.JsonHome/HomeMetadata.fsi` / `.fs` | Create | Endpoint metadata types carrying `rel`, `hrefVar`, `docs`, `status` |
| `src/Frank.JsonHome/ResourceBuilderExtensions.fsi` / `.fs` | Create | The `rel` / `hrefVar` / `docs` / `deprecated` / `gone` operations |
| `src/Frank.JsonHome/ApiSurface.fsi` / `.fs` | Create | `ApiDescription` → format-neutral resource descriptions |
| `src/Frank.JsonHome/JsonHome.fsi` / `.fs` | Create | Document model, serializer, request handler |
| `src/Frank.JsonHome/WebHostBuilderExtensions.fsi` / `.fs` | Create | `useJsonHome` |
| `test/Frank.JsonHome.Tests/*` | Create | Unit + integration tests |

---

### Task 1: Shared response-link mechanism in Frank core

**Files:**
- Create: `src/Frank/WebLink.fsi`, `src/Frank/WebLink.fs`
- Modify: `src/Frank/Frank.fsproj:8-18` (register the new files first, before `ContentNegotiation`)
- Modify: `src/Frank/WebHostBuilder.fs` (add `link`, install middleware in `Run`)
- Modify: `src/Frank/WebHostBuilder.fsi` (add `link` signature)
- Test: `test/Frank.Tests/WebLinkTests.fs` (create), `test/Frank.Tests/Frank.Tests.fsproj` (register)

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `type WebLink = { Target: string; Rel: string; Params: (string * string) list }`
  - `type IResponseLinkProvider = abstract GetLinks : HttpContext -> WebLink seq`
  - `WebLink.create : string -> string -> WebLink`
  - `WebLink.format : WebLink -> string`
  - `WebLink.middleware : IResponseLinkProvider[] -> (HttpContext -> (unit -> Task) -> Task)`
  - `WebHostBuilder` custom operation `link : spec:WebHostSpec * target:string * rel:string -> WebHostSpec`

**Background you need:**

[RFC 8288](https://www.rfc-editor.org/rfc/rfc8288) field value format is `<target>; rel="home"; title="Example"`. The target goes in angle brackets; parameter values are quoted strings in which `\` and `"` must be backslash-escaped. Multiple links may be sent as repeated `Link` header values — do that rather than comma-joining, and **append** to any existing values rather than assigning, because assigning clobbers other contributors.

`WebHostSpec.BeforeRoutingMiddleware` (`WebHostBuilder.fs:15`) runs before `app.UseRouting()` (`WebHostBuilder.fs:47-48`), which is where this belongs: a 404 is where a lost client most needs the home link.

Resolve providers **once at startup** from `app.ApplicationServices`, not per request. If the array is empty, install no middleware at all, so applications not using links pay nothing.

- [ ] **Step 1: Write the failing test**

Create `test/Frank.Tests/WebLinkTests.fs`:

```fsharp
module Frank.Tests.WebLinkTests

open Expecto
open Frank.Builder

[<Tests>]
let tests =
    testList
        "WebLink"
        [ test "format emits an RFC 8288 field value" {
              let link = WebLink.create "/.well-known/home.json" "home"

              Expect.equal
                  (WebLink.format link)
                  "</.well-known/home.json>; rel=\"home\""
                  "Target is bracketed and rel is quoted"
          }

          test "format appends quoted parameters in order" {
              let link =
                  { WebLink.create "/docs" "service-doc" with
                      Params = [ "title", "Docs"; "type", "text/html" ] }

              Expect.equal
                  (WebLink.format link)
                  "</docs>; rel=\"service-doc\"; title=\"Docs\"; type=\"text/html\""
                  "Parameters follow rel in declaration order"
          }

          test "format escapes quotes and backslashes in parameter values" {
              let link =
                  { WebLink.create "/x" "about" with
                      Params = [ "title", "a \"quoted\" c:\\path" ] }

              Expect.equal
                  (WebLink.format link)
                  "</x>; rel=\"about\"; title=\"a \\\"quoted\\\" c:\\\\path\""
                  "Backslashes and quotes are escaped"
          } ]
```

Register it in `test/Frank.Tests/Frank.Tests.fsproj` as the first `<Compile>` entry:

```xml
    <Compile Include="WebLinkTests.fs" />
    <Compile Include="HandlerBuilderTests.fs" />
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~WebLink"
```

Expected: FAIL at compile time — `The value, namespace, type or module 'WebLink' is not defined`.

- [ ] **Step 3: Create `src/Frank/WebLink.fsi`**

```fsharp
namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// A web link (RFC 8288) contributed to every response.
type WebLink =
    { Target: string
      Rel: string
      Params: (string * string) list }

/// Implemented by libraries that contribute links to responses.
/// Register instances in DI; the provider list is resolved once at startup,
/// while GetLinks is called per request.
type IResponseLinkProvider =
    abstract GetLinks: ctx: HttpContext -> WebLink seq

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module WebLink =

    /// A link with no parameters.
    val create: target: string -> rel: string -> WebLink

    /// Formats a link as an RFC 8288 field value.
    val format: link: WebLink -> string

    /// Middleware appending every provider's links to the Link response header.
    /// Returns None when there are no providers, so callers can skip installing it.
    val middleware: providers: IResponseLinkProvider[] -> (HttpContext -> (unit -> Task) -> Task) option
```

- [ ] **Step 4: Create `src/Frank/WebLink.fs`**

```fsharp
namespace Frank.Builder

open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives

type WebLink =
    { Target: string
      Rel: string
      Params: (string * string) list }

type IResponseLinkProvider =
    abstract GetLinks: ctx: HttpContext -> WebLink seq

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module WebLink =

    let create target rel = { Target = target; Rel = rel; Params = [] }

    let private escape (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let format (link: WebLink) =
        let sb = StringBuilder()
        sb.Append('<').Append(link.Target).Append(">; rel=\"").Append(escape link.Rel).Append('"') |> ignore

        for name, value in link.Params do
            sb.Append("; ").Append(name).Append("=\"").Append(escape value).Append('"') |> ignore

        sb.ToString()

    let middleware (providers: IResponseLinkProvider[]) =
        if Array.isEmpty providers then
            None
        else
            Some(fun (ctx: HttpContext) (next: unit -> Task) ->
                let formatted =
                    providers
                    |> Array.collect (fun p -> p.GetLinks ctx |> Seq.map format |> Array.ofSeq)

                if not (Array.isEmpty formatted) then
                    // Append rather than assign: other contributors may have added links.
                    let existing = ctx.Response.Headers.Link
                    ctx.Response.Headers.Link <- StringValues.Concat(existing, StringValues formatted)

                next ())
```

- [ ] **Step 5: Register the new files in `src/Frank/Frank.fsproj`**

Add as the first two `<Compile>` entries in the existing `<ItemGroup>`, before `ContentNegotiation.fsi`:

```xml
    <Compile Include="WebLink.fsi" />
    <Compile Include="WebLink.fs" />
    <Compile Include="ContentNegotiation.fsi" />
```

- [ ] **Step 6: Run the test to verify it passes**

```bash
dotnet test test/Frank.Tests/Frank.Tests.fsproj --filter "FullyQualifiedName~WebLink"
```

Expected: PASS, 3 tests.

- [ ] **Step 7: Add the `link` operation and install the middleware**

In `src/Frank/WebHostBuilder.fs`, add this type above `WebHostSpec` (after the `open` block):

```fsharp
type private StaticLinkProvider(links: WebLink[]) =
    interface IResponseLinkProvider with
        member _.GetLinks(_) = links :> seq<_>
```

Add the `link` custom operation to `WebHostBuilder`, directly after the `service` operation (line 112-115):

```fsharp
    [<CustomOperation("link")>]
    member __.Link(spec, target: string, rel: string) : WebHostSpec =
        { spec with
            Services =
                spec.Services
                >> fun services ->
                    services.AddSingleton<IResponseLinkProvider>(StaticLinkProvider [| WebLink.create target rel |])
                    |> ignore

                    services }
```

In `Run`, install the middleware ahead of `BeforeRoutingMiddleware`. Replace the `.Configure(fun app -> ...)` block (lines 45-54) with:

```fsharp
                    .Configure(fun app ->
                        let linkProviders =
                            app.ApplicationServices.GetServices<IResponseLinkProvider>() |> Array.ofSeq

                        // Annotate both lambda parameters. IApplicationBuilder.Use has two
                        // overloads -- Func<HttpContext, Func<Task>, Task> and
                        // Func<HttpContext, RequestDelegate, Task> -- and F# cannot pick
                        // between them from an unannotated lambda.
                        match WebLink.middleware linkProviders with
                        | Some run ->
                            app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                                run ctx (fun () -> next.Invoke ctx))
                            |> ignore
                        | None -> ()

                        app
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> spec.Middleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                let dataSource = ResourceEndpointDataSource(spec.Endpoints)
                                endpoints.DataSources.Add(dataSource))
                        |> ignore)
```

- [ ] **Step 8: Add the `link` signature to `src/Frank/WebHostBuilder.fsi`**

Insert after the `service` operation (line 59-60):

```fsharp
    [<CustomOperation("link")>]
    member Link: spec: WebHostSpec * target: string * rel: string -> WebHostSpec
```

- [ ] **Step 9: Build across all target frameworks and run the full suite**

```bash
dotnet build src/Frank/Frank.fsproj
dotnet test test/Frank.Tests/Frank.Tests.fsproj
```

Expected: build succeeds on net8.0, net9.0, and net10.0; all tests pass.

- [ ] **Step 10: Commit**

```bash
git add src/Frank/WebLink.fsi src/Frank/WebLink.fs src/Frank/Frank.fsproj src/Frank/WebHostBuilder.fs src/Frank/WebHostBuilder.fsi test/Frank.Tests/WebLinkTests.fs test/Frank.Tests/Frank.Tests.fsproj
git commit -m "feat(core): add WebLink and IResponseLinkProvider

The Link response header has several would-be contributors -- service-desc
(RFC 8631) for OpenAPI, profile (RFC 6906) for ALPS, home for JSON Home --
and it only works if they cooperate: assigning the header clobbers other
contributors, and each would otherwise repeat RFC 8288 formatting.

Providers are resolved once at startup; applications registering none pay
nothing, since no middleware is installed."
```

---

### Task 2: Route template translation

**Files:**
- Create: `src/Frank.JsonHome/Frank.JsonHome.fsproj`, `src/Frank.JsonHome/UriTemplate.fsi`, `src/Frank.JsonHome/UriTemplate.fs`
- Create: `test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`, `test/Frank.JsonHome.Tests/UriTemplateTests.fs`, `test/Frank.JsonHome.Tests/Program.fs`
- Modify: `Frank.sln`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `UriTemplate.ofRouteTemplate : string -> string`
  - `UriTemplate.variables : string -> string list`
  - `UriTemplate.isTemplated : string -> bool`

**Background you need:**

ASP.NET route templates are not RFC 6570 URI Templates. They carry constraints, optional markers, default values, and catch-all syntax that RFC 6570 has no equivalent for:

| ASP.NET | RFC 6570 | Why |
|---|---|---|
| `{id}` | `{id}` | identical |
| `{id:guid}` | `{id}` | constraints are a routing concern |
| `{id:minlength(4)}` | `{id}` | constraint arguments may contain `:` and `()` |
| `{id?}` | `{id}` | RFC 6570 has no optional marker |
| `{id=1}` | `{id}` | RFC 6570 has no defaults |
| `{*rest}` | `{+rest}` | catch-all spans `/`, which is reserved expansion |
| `{**rest}` | `{+rest}` | same, with round-trip escaping |

Literal text outside braces passes through unchanged. A template is "templated" if it contains at least one variable.

- [ ] **Step 1: Create the project files**

`src/Frank.JsonHome/Frank.JsonHome.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>json-home;hypermedia;discovery;rest</PackageTags>
    <Description>JSON Home document support for the Frank web framework</Description>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="UriTemplate.fsi" />
    <Compile Include="UriTemplate.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Frank/Frank.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

</Project>
```

`test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`:

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
    <Compile Include="UriTemplateTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="Expecto" Version="10.*" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.*" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0-preview.1.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.JsonHome/Frank.JsonHome.fsproj" />
  </ItemGroup>

</Project>
```

`test/Frank.JsonHome.Tests/Program.fs`:

```fsharp
module Frank.JsonHome.Tests.Program

open Expecto

[<EntryPoint>]
let main argv = Tests.runTestsInAssemblyWithCLIArgs [] argv
```

Add both projects to the solution:

```bash
dotnet sln Frank.sln add src/Frank.JsonHome/Frank.JsonHome.fsproj test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj
```

- [ ] **Step 2: Write the failing test**

Create `test/Frank.JsonHome.Tests/UriTemplateTests.fs`:

```fsharp
module Frank.JsonHome.Tests.UriTemplateTests

open Expecto
open Frank.JsonHome

[<Tests>]
let tests =
    testList
        "UriTemplate"
        [ test "translates ASP.NET route templates to RFC 6570" {
              let cases =
                  [ "/products", "/products"
                    "/products/{id}", "/products/{id}"
                    "/products/{id:guid}", "/products/{id}"
                    "/products/{id:minlength(4)}", "/products/{id}"
                    "/products/{id?}", "/products/{id}"
                    "/products/{id=1}", "/products/{id}"
                    "/files/{*path}", "/files/{+path}"
                    "/files/{**path}", "/files/{+path}"
                    "/a/{x}/b/{y:int}", "/a/{x}/b/{y}" ]

              for input, expected in cases do
                  Expect.equal (UriTemplate.ofRouteTemplate input) expected ("Translating " + input)
          }

          test "extracts variable names" {
              Expect.equal (UriTemplate.variables "/a/{x}/b/{y:int}") [ "x"; "y" ] "Names without constraints"
              Expect.equal (UriTemplate.variables "/files/{*path}") [ "path" ] "Catch-all name without star"
              Expect.equal (UriTemplate.variables "/products") [] "No variables"
          }

          test "detects templated routes" {
              Expect.isTrue (UriTemplate.isTemplated "/products/{id}") "Has a variable"
              Expect.isFalse (UriTemplate.isTemplated "/products") "No variables"
          } ]
```

- [ ] **Step 3: Run the test to verify it fails**

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj
```

Expected: FAIL at compile time — `The namespace or module 'Frank.JsonHome' is not defined`.

- [ ] **Step 4: Create `src/Frank.JsonHome/UriTemplate.fsi`**

```fsharp
namespace Frank.JsonHome

/// Translation between ASP.NET routing templates and RFC 6570 URI Templates.
module UriTemplate =

    /// Rewrites an ASP.NET route template as an RFC 6570 URI Template, dropping
    /// inline constraints, optional markers, and default values, and mapping
    /// catch-all segments onto reserved expansion.
    val ofRouteTemplate: routeTemplate: string -> string

    /// The variable names appearing in a route template, in order.
    val variables: routeTemplate: string -> string list

    /// True when the template contains at least one variable.
    val isTemplated: routeTemplate: string -> bool
```

- [ ] **Step 5: Create `src/Frank.JsonHome/UriTemplate.fs`**

```fsharp
namespace Frank.JsonHome

open System.Text
open System.Text.RegularExpressions

module UriTemplate =

    // Matches one {...} segment. Constraint arguments may contain braces-free
    // punctuation such as ':' and '()', so the body is captured lazily up to
    // the closing brace.
    let private segment = Regex(@"\{(?<body>[^{}]*)\}", RegexOptions.Compiled)

    /// Splits a segment body into its catch-all marker and bare variable name,
    /// discarding constraints, optional markers, and default values.
    let private parseBody (body: string) =
        let isCatchAll = body.StartsWith "*"
        let trimmed = body.TrimStart '*'

        // Order matters: a default value may follow a constraint ("{id:int=1}"),
        // and the optional marker trails the name ("{id?}").
        let name =
            let beforeDefault =
                match trimmed.IndexOf '=' with
                | -1 -> trimmed
                | i -> trimmed.Substring(0, i)

            let beforeConstraint =
                match beforeDefault.IndexOf ':' with
                | -1 -> beforeDefault
                | i -> beforeDefault.Substring(0, i)

            beforeConstraint.TrimEnd '?'

        isCatchAll, name

    let ofRouteTemplate (routeTemplate: string) =
        segment.Replace(
            routeTemplate,
            fun m ->
                let isCatchAll, name = parseBody (m.Groups["body"].Value)
                if isCatchAll then "{+" + name + "}" else "{" + name + "}"
        )

    let variables (routeTemplate: string) =
        segment.Matches routeTemplate
        |> Seq.map (fun m -> snd (parseBody (m.Groups["body"].Value)))
        |> List.ofSeq

    let isTemplated (routeTemplate: string) = segment.IsMatch routeTemplate
```

- [ ] **Step 6: Run the test to verify it passes**

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj
```

Expected: PASS, 3 tests.

- [ ] **Step 7: Build across all target frameworks**

```bash
dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj
```

Expected: succeeds on net8.0, net9.0, and net10.0.

- [ ] **Step 8: Commit**

```bash
git add src/Frank.JsonHome/ test/Frank.JsonHome.Tests/ Frank.sln
git commit -m "feat(jsonhome): add project and route template translation

ASP.NET route templates carry constraints, optional markers, defaults, and
catch-all syntax that RFC 6570 has no equivalent for, so hrefTemplate needs
a real translation rather than a string copy."
```

---

### Task 3: Resource-level discovery metadata

**Files:**
- Create: `src/Frank.JsonHome/HomeMetadata.fsi`, `.fs`
- Create: `src/Frank.JsonHome/ResourceBuilderExtensions.fsi`, `.fs`
- Modify: `src/Frank.JsonHome/Frank.JsonHome.fsproj` (register, after `UriTemplate`)
- Test: `test/Frank.JsonHome.Tests/ResourceMetadataTests.fs` (create), register in the test `.fsproj` before `Program.fs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `type RelMetadata = { Rel: string }`
  - `type HrefVarMetadata = { Name: string; Uri: string }`
  - `type DocsMetadata = { Uri: string }`
  - `type ResourceStatus = Deprecated | Gone` and `type StatusMetadata = { Status: ResourceStatus }`
  - `ResourceBuilder` operations `rel`, `hrefVar`, `docs`, `deprecated`, `gone`

**Background you need:**

These are **resource-level**, so they use `ResourceBuilder.AddMetadata` (`src/Frank/ResourceBuilder.fsi:30`), which applies a convention to every endpoint in the resource. That is correct here: a link relation type describes the resource, not one method of it.

Resources without a `rel` are omitted from the home document. JSON Home is a curated entry point, not a sitemap — the draft's own examples mint an extension relation type per resource (`tag:me@example.com,2016:widgets`).

- [ ] **Step 1: Write the failing test**

Create `test/Frank.JsonHome.Tests/ResourceMetadataTests.fs`:

```fsharp
module Frank.JsonHome.Tests.ResourceMetadataTests

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Builder
open Frank.JsonHome

let private noop: RequestDelegate = RequestDelegate(fun _ -> Task.CompletedTask)

[<Tests>]
let tests =
    testList
        "Resource discovery metadata"
        [ test "rel is attached to every endpoint in the resource" {
              let built =
                  resource "/products" {
                      rel "tag:example.com,2026:products"
                      get noop
                      post noop
                  }

              Expect.hasLength built.Endpoints 2 "Two endpoints"

              for endpoint in built.Endpoints do
                  let meta = endpoint.Metadata.GetMetadata<RelMetadata>()
                  Expect.isNotNull (box meta) "Every endpoint carries the rel"
                  Expect.equal meta.Rel "tag:example.com,2026:products" "Rel value matches"
          }

          test "hrefVar, docs, and status are attached" {
              let built =
                  resource "/products/{id}" {
                      rel "tag:example.com,2026:product"
                      hrefVar "id" "https://example.com/param/product-id"
                      docs "https://example.com/docs/products"
                      deprecated
                      get noop
                  }

              let endpoint = built.Endpoints.[0]

              let hrefVars = endpoint.Metadata.GetOrderedMetadata<HrefVarMetadata>()
              Expect.hasLength hrefVars 1 "One hrefVar"
              Expect.equal hrefVars.[0].Name "id" "Variable name"
              Expect.equal hrefVars.[0].Uri "https://example.com/param/product-id" "Variable URI"

              let docsMeta = endpoint.Metadata.GetMetadata<DocsMetadata>()
              Expect.equal docsMeta.Uri "https://example.com/docs/products" "Docs URI"

              let status = endpoint.Metadata.GetMetadata<StatusMetadata>()
              Expect.equal status.Status ResourceStatus.Deprecated "Status is deprecated"
          }

          test "resources without a rel carry no rel metadata" {
              let built = resource "/internal" { get noop }

              Expect.isNull (box (built.Endpoints.[0].Metadata.GetMetadata<RelMetadata>())) "No rel metadata"
          } ]
```

Register it in `test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`:

```xml
    <Compile Include="UriTemplateTests.fs" />
    <Compile Include="ResourceMetadataTests.fs" />
    <Compile Include="Program.fs" />
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter "FullyQualifiedName~ResourceMetadata"
```

Expected: FAIL at compile time — `The type 'RelMetadata' is not defined`.

- [ ] **Step 3: Create `src/Frank.JsonHome/HomeMetadata.fsi`**

```fsharp
namespace Frank.JsonHome

/// Whether a resource is still current, per the JSON Home "status" hint.
[<RequireQualifiedAccess>]
type ResourceStatus =
    | Deprecated
    | Gone

/// The link relation type keying this resource in the home document.
type RelMetadata = { Rel: string }

/// The absolute URI identifying a template variable's semantics.
type HrefVarMetadata = { Name: string; Uri: string }

/// Documentation for this resource's link relation type.
type DocsMetadata = { Uri: string }

/// This resource's status hint.
type StatusMetadata = { Status: ResourceStatus }
```

- [ ] **Step 4: Create `src/Frank.JsonHome/HomeMetadata.fs`**

```fsharp
namespace Frank.JsonHome

[<RequireQualifiedAccess>]
type ResourceStatus =
    | Deprecated
    | Gone

type RelMetadata = { Rel: string }

type HrefVarMetadata = { Name: string; Uri: string }

type DocsMetadata = { Uri: string }

type StatusMetadata = { Status: ResourceStatus }
```

- [ ] **Step 5: Create `src/Frank.JsonHome/ResourceBuilderExtensions.fsi`**

```fsharp
namespace Frank.JsonHome

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =

    type ResourceBuilder with

        /// Declares the link relation type keying this resource in the home
        /// document. Resources without one are omitted.
        [<CustomOperation("rel")>]
        member Rel: spec: ResourceSpec * rel: string -> ResourceSpec

        /// Declares the absolute URI identifying a route variable's semantics.
        [<CustomOperation("hrefVar")>]
        member HrefVar: spec: ResourceSpec * name: string * uri: string -> ResourceSpec

        /// Declares documentation for this resource's link relation type.
        [<CustomOperation("docs")>]
        member Docs: spec: ResourceSpec * uri: string -> ResourceSpec

        /// Marks this resource deprecated.
        [<CustomOperation("deprecated")>]
        member Deprecated: spec: ResourceSpec -> ResourceSpec

        /// Marks this resource gone.
        [<CustomOperation("gone")>]
        member Gone: spec: ResourceSpec -> ResourceSpec
```

- [ ] **Step 6: Create `src/Frank.JsonHome/ResourceBuilderExtensions.fs`**

```fsharp
namespace Frank.JsonHome

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =

    type ResourceBuilder with

        [<CustomOperation("rel")>]
        member _.Rel(spec: ResourceSpec, rel: string) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Rel = rel }))

        [<CustomOperation("hrefVar")>]
        member _.HrefVar(spec: ResourceSpec, name: string, uri: string) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Name = name; Uri = uri }))

        [<CustomOperation("docs")>]
        member _.Docs(spec: ResourceSpec, uri: string) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add({ DocsMetadata.Uri = uri })))

        [<CustomOperation("deprecated")>]
        member _.Deprecated(spec: ResourceSpec) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Status = ResourceStatus.Deprecated }))

        [<CustomOperation("gone")>]
        member _.Gone(spec: ResourceSpec) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Status = ResourceStatus.Gone }))
```

- [ ] **Step 7: Register the new files in `src/Frank.JsonHome/Frank.JsonHome.fsproj`**

```xml
    <Compile Include="UriTemplate.fsi" />
    <Compile Include="UriTemplate.fs" />
    <Compile Include="HomeMetadata.fsi" />
    <Compile Include="HomeMetadata.fs" />
    <Compile Include="ResourceBuilderExtensions.fsi" />
    <Compile Include="ResourceBuilderExtensions.fs" />
```

- [ ] **Step 8: Run the test to verify it passes**

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj
```

Expected: PASS. If `HrefVarMetadata` and `DocsMetadata` record construction is ambiguous (both have a `Uri` field), qualify the record type as shown in Step 6.

- [ ] **Step 9: Build across all target frameworks and commit**

```bash
dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj
git add src/Frank.JsonHome/ test/Frank.JsonHome.Tests/
git commit -m "feat(jsonhome): add resource-level discovery metadata

rel, hrefVar, docs, deprecated, and gone describe the resource rather than
one method of it, so they attach via ResourceBuilder.AddMetadata. Resources
without a rel are omitted from the home document."
```

---

### Task 4: ApiSurface extraction

**Files:**
- Create: `src/Frank.JsonHome/ApiSurface.fsi`, `.fs`
- Modify: `src/Frank.JsonHome/Frank.JsonHome.fsproj` (register after `ResourceBuilderExtensions`)
- Test: `test/Frank.JsonHome.Tests/ApiSurfaceTests.fs` (create), register before `Program.fs`

**Interfaces:**
- Consumes: `UriTemplate` (Task 2), `RelMetadata` / `HrefVarMetadata` / `DocsMetadata` / `StatusMetadata` (Task 3).
- Produces:
  - `type ResourceDescription = { Rel: string; Href: string; IsTemplated: bool; HrefVars: (string * string) list; Methods: string list; Formats: string list; Accepts: (string * string list) list; Docs: string option; Status: ResourceStatus option; Metadata: obj list }`
  - `ApiSurface.ofApiDescriptions : ApiDescription seq -> ResourceDescription list`

**Background you need:**

`IApiDescriptionGroupCollectionProvider` yields one `ApiDescription` per endpoint **and method**. Group them by `RelativePath` to get one home-document resource per route template. `ApiDescription` carries:

- `RelativePath` — the route template **without** a leading slash (`"products/{id}"`)
- `HttpMethod`
- `SupportedResponseTypes` — each with `StatusCode` and `ApiResponseFormats` (`.MediaType`)
- `SupportedRequestFormats` — each with `.MediaType`
- `ActionDescriptor.EndpointMetadata` — where Task 3's metadata lives

`formats` comes only from the GET description's 2xx responses. `accepts` is per method, keyed by the method name so the serializer can emit `acceptPost` / `acceptPut` / `acceptPatch`.

For later authorization filtering, keep the metadata rather than an `Endpoint`: ApiExplorer does not expose the endpoint itself, and `ActionDescriptor.EndpointMetadata` carries everything the filter needs (`IAuthorizeData` and `AuthorizationPolicy`). Hence the `Metadata: obj list` field.

- [ ] **Step 1: Write the failing test**

Create `test/Frank.JsonHome.Tests/ApiSurfaceTests.fs`:

```fsharp
module Frank.JsonHome.Tests.ApiSurfaceTests

open Microsoft.AspNetCore.Mvc.Abstractions
open Microsoft.AspNetCore.Mvc.ApiExplorer
open Expecto
open Frank.JsonHome

/// Builds an ApiDescription the way ApiExplorer would for one endpoint+method.
let private describe (relativePath: string) (httpMethod: string) (metadata: obj list) =
    let action = ActionDescriptor()
    action.EndpointMetadata <- ResizeArray metadata

    let description = ApiDescription()
    description.RelativePath <- relativePath
    description.HttpMethod <- httpMethod
    description.ActionDescriptor <- action
    description

[<Tests>]
let tests =
    testList
        "ApiSurface"
        [ test "groups descriptions by route template and collects methods" {
              let metadata: obj list = [ { Rel = "tag:example.com,2026:products" } ]

              let surface =
                  ApiSurface.ofApiDescriptions
                      [ describe "products" "GET" metadata
                        describe "products" "POST" metadata ]

              Expect.hasLength surface 1 "One resource"
              Expect.equal surface.[0].Rel "tag:example.com,2026:products" "Rel carried through"
              Expect.equal surface.[0].Href "/products" "Leading slash restored"
              Expect.isFalse surface.[0].IsTemplated "No variables"
              Expect.equal surface.[0].Methods [ "GET"; "POST" ] "Methods collected in order"
          }

          test "templated routes are translated and carry hrefVars" {
              let metadata: obj list =
                  [ { Rel = "tag:example.com,2026:product" }
                    { Name = "id"; Uri = "https://example.com/param/product-id" } ]

              let surface = ApiSurface.ofApiDescriptions [ describe "products/{id:guid}" "GET" metadata ]

              Expect.hasLength surface 1 "One resource"
              Expect.isTrue surface.[0].IsTemplated "Has a variable"
              Expect.equal surface.[0].Href "/products/{id}" "Constraint stripped"

              Expect.equal
                  surface.[0].HrefVars
                  [ "id", "https://example.com/param/product-id" ]
                  "hrefVars carried through"
          }

          test "resources without a rel are excluded" {
              let surface = ApiSurface.ofApiDescriptions [ describe "internal" "GET" [] ]

              Expect.isEmpty surface "No rel means no entry"
          } ]
```

Register it in the test `.fsproj` before `Program.fs`.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter "FullyQualifiedName~ApiSurface"
```

Expected: FAIL at compile time — `The value, namespace, type or module 'ApiSurface' is not defined`.

- [ ] **Step 3: Create `src/Frank.JsonHome/ApiSurface.fsi`**

```fsharp
namespace Frank.JsonHome

open Microsoft.AspNetCore.Mvc.ApiExplorer

/// One entry-point resource, described independently of any output format.
type ResourceDescription =
    { Rel: string
      Href: string
      IsTemplated: bool
      HrefVars: (string * string) list
      Methods: string list
      Formats: string list
      /// Request content types, keyed by HTTP method.
      Accepts: (string * string list) list
      Docs: string option
      Status: ResourceStatus option
      /// Endpoint metadata, retained for authorization filtering.
      Metadata: obj list }

module ApiSurface =

    /// Projects ApiExplorer descriptions into entry-point resources, grouping by
    /// route template. Descriptions without a RelMetadata are excluded.
    val ofApiDescriptions: descriptions: ApiDescription seq -> ResourceDescription list
```

- [ ] **Step 4: Create `src/Frank.JsonHome/ApiSurface.fs`**

```fsharp
namespace Frank.JsonHome

open Microsoft.AspNetCore.Mvc.ApiExplorer

type ResourceDescription =
    { Rel: string
      Href: string
      IsTemplated: bool
      HrefVars: (string * string) list
      Methods: string list
      Formats: string list
      Accepts: (string * string list) list
      Docs: string option
      Status: ResourceStatus option
      Metadata: obj list }

module ApiSurface =

    let private metadataOf (description: ApiDescription) =
        match description.ActionDescriptor with
        | null -> []
        | action ->
            match action.EndpointMetadata with
            | null -> []
            | items -> List.ofSeq items

    let private pick<'T when 'T: not struct> (metadata: obj list) =
        metadata
        |> List.tryPick (fun m ->
            match m with
            | :? 'T as t -> Some t
            | _ -> None)

    let private pickAll<'T when 'T: not struct> (metadata: obj list) =
        metadata
        |> List.choose (fun m ->
            match m with
            | :? 'T as t -> Some t
            | _ -> None)

    let private responseFormats (description: ApiDescription) =
        description.SupportedResponseTypes
        |> Seq.filter (fun r -> r.StatusCode >= 200 && r.StatusCode < 300)
        |> Seq.collect (fun r -> r.ApiResponseFormats |> Seq.map (fun f -> f.MediaType))
        |> Seq.distinct
        |> List.ofSeq

    let private requestFormats (description: ApiDescription) =
        description.SupportedRequestFormats
        |> Seq.map (fun f -> f.MediaType)
        |> Seq.distinct
        |> List.ofSeq

    let ofApiDescriptions (descriptions: ApiDescription seq) : ResourceDescription list =
        descriptions
        |> Seq.filter (fun d -> not (isNull d.RelativePath))
        |> Seq.groupBy (fun d -> d.RelativePath)
        |> Seq.choose (fun (relativePath, group) ->
            let group = List.ofSeq group
            let metadata = group |> List.collect metadataOf

            match pick<RelMetadata> metadata with
            | None -> None
            | Some rel ->
                let routeTemplate = "/" + relativePath.TrimStart '/'

                let accepts =
                    group
                    |> List.choose (fun d ->
                        match requestFormats d with
                        | [] -> None
                        | formats -> Some(d.HttpMethod, formats))

                let formats =
                    group
                    |> List.tryFind (fun d -> d.HttpMethod = "GET")
                    |> Option.map responseFormats
                    |> Option.defaultValue []

                Some
                    { Rel = rel.Rel
                      Href = UriTemplate.ofRouteTemplate routeTemplate
                      IsTemplated = UriTemplate.isTemplated routeTemplate
                      HrefVars = pickAll<HrefVarMetadata> metadata |> List.map (fun v -> v.Name, v.Uri)
                      Methods = group |> List.map (fun d -> d.HttpMethod) |> List.distinct
                      Formats = formats
                      Accepts = accepts
                      Docs = pick<DocsMetadata> metadata |> Option.map (fun d -> d.Uri)
                      Status = pick<StatusMetadata> metadata |> Option.map (fun s -> s.Status)
                      Metadata = metadata })
        |> List.ofSeq
```

- [ ] **Step 5: Register in the `.fsproj`, run the test, verify it passes**

```xml
    <Compile Include="ApiSurface.fsi" />
    <Compile Include="ApiSurface.fs" />
```

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj
```

Expected: PASS. Note `HrefVars` may contain duplicates if the same resource declares a variable twice; that is acceptable and the serializer de-duplicates by key.

- [ ] **Step 6: Build across all target frameworks and commit**

```bash
dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj
git add src/Frank.JsonHome/ test/Frank.JsonHome.Tests/
git commit -m "feat(jsonhome): project ApiDescription into an ApiSurface

ApiExplorer yields one description per endpoint and method; the home document
wants one resource per route template, so descriptions are grouped and their
metadata merged. Endpoint metadata is retained for authorization filtering."
```

---

### Task 5: Serialize and serve the home document

**Files:**
- Create: `src/Frank.JsonHome/JsonHome.fsi`, `.fs`
- Create: `src/Frank.JsonHome/WebHostBuilderExtensions.fsi`, `.fs`
- Modify: `src/Frank.JsonHome/Frank.JsonHome.fsproj`
- Test: `test/Frank.JsonHome.Tests/JsonHomeDocumentTests.fs` (create), register before `Program.fs`

**Interfaces:**
- Consumes: `ResourceDescription` (Task 4), `WebLink` / `IResponseLinkProvider` (Task 1).
- Produces:
  - `type JsonHomeOptions = { Path: string; Rel: string; Title: string option; Links: (string * string) list }` with `JsonHomeOptions.Default`
  - `JsonHome.serialize : JsonHomeOptions -> ResourceDescription list -> string`
  - `WebHostBuilder` operations `useJsonHome` and `useJsonHome(configure)`

**Background you need:**

The `resources` member is a JSON **object keyed by link relation type**, so a hand-written `Utf8JsonWriter` is simpler and more predictable than attribute-driven serialization. Members are omitted entirely when absent — never emitted as `null`.

Target shape, from draft-06:

```json
{
  "api": {
    "title": "Example API",
    "links": { "author": "mailto:api-admin@example.com" }
  },
  "resources": {
    "tag:me@example.com,2016:widgets": { "href": "/widgets/" },
    "tag:me@example.com,2016:widget": {
      "hrefTemplate": "/widgets/{widget_id}",
      "hrefVars": { "widget_id": "https://example.org/param/widget" },
      "hints": {
        "allow": ["GET", "PUT", "DELETE", "PATCH"],
        "formats": { "application/json": {} },
        "acceptPatch": ["application/json-patch+json"],
        "acceptRanges": ["bytes"]
      }
    }
  }
}
```

Note `formats` is an **object** whose keys are media types and whose values are empty objects — not an array.

Method-to-hint mapping: `POST` → `acceptPost`, `PUT` → `acceptPut`, `PATCH` → `acceptPatch`. Other methods contribute no accept hint.

`status` is `"deprecated"` or `"gone"`.

- [ ] **Step 1: Write the failing test**

Create `test/Frank.JsonHome.Tests/JsonHomeDocumentTests.fs`:

```fsharp
module Frank.JsonHome.Tests.JsonHomeDocumentTests

open System.Text.Json
open Expecto
open Frank.JsonHome

let private parse (json: string) = JsonDocument.Parse(json).RootElement

let private widgets =
    { Rel = "tag:me@example.com,2016:widgets"
      Href = "/widgets/"
      IsTemplated = false
      HrefVars = []
      Methods = [ "GET" ]
      Formats = []
      Accepts = []
      Docs = None
      Status = None
      Metadata = [] }

let private widget =
    { Rel = "tag:me@example.com,2016:widget"
      Href = "/widgets/{widget_id}"
      IsTemplated = true
      HrefVars = [ "widget_id", "https://example.org/param/widget" ]
      Methods = [ "GET"; "PUT"; "DELETE"; "PATCH" ]
      Formats = [ "application/json" ]
      Accepts = [ "PATCH", [ "application/json-patch+json" ] ]
      Docs = None
      Status = None
      Metadata = [] }

[<Tests>]
let tests =
    testList
        "JsonHome.serialize"
        [ test "reproduces the draft-06 example document" {
              let options =
                  { JsonHomeOptions.Default with
                      Title = Some "Example API"
                      Links = [ "author", "mailto:api-admin@example.com" ] }

              let root = parse (JsonHome.serialize options [ widgets; widget ])

              Expect.equal (root.GetProperty("api").GetProperty("title").GetString()) "Example API" "api.title"

              Expect.equal
                  (root.GetProperty("api").GetProperty("links").GetProperty("author").GetString())
                  "mailto:api-admin@example.com"
                  "api.links.author"

              let resources = root.GetProperty "resources"

              let widgetsEntry = resources.GetProperty "tag:me@example.com,2016:widgets"
              Expect.equal (widgetsEntry.GetProperty("href").GetString()) "/widgets/" "href for the collection"

              let widgetEntry = resources.GetProperty "tag:me@example.com,2016:widget"

              Expect.equal
                  (widgetEntry.GetProperty("hrefTemplate").GetString())
                  "/widgets/{widget_id}"
                  "hrefTemplate for the item"

              Expect.isFalse (fst (widgetEntry.TryGetProperty "href")) "Templated resources omit href"

              Expect.equal
                  (widgetEntry.GetProperty("hrefVars").GetProperty("widget_id").GetString())
                  "https://example.org/param/widget"
                  "hrefVars"

              let hints = widgetEntry.GetProperty "hints"

              Expect.equal
                  (hints.GetProperty("allow").EnumerateArray() |> Seq.map (fun e -> e.GetString()) |> List.ofSeq)
                  [ "GET"; "PUT"; "DELETE"; "PATCH" ]
                  "allow"

              Expect.equal
                  (hints.GetProperty("formats").GetProperty("application/json").ValueKind)
                  JsonValueKind.Object
                  "formats is an object of empty objects"

              Expect.equal
                  (hints.GetProperty("acceptPatch").EnumerateArray()
                   |> Seq.map (fun e -> e.GetString())
                   |> List.ofSeq)
                  [ "application/json-patch+json" ]
                  "acceptPatch uses the camelCase draft-06 name"
          }

          test "omits api when unconfigured and omits empty hints" {
              let root = parse (JsonHome.serialize JsonHomeOptions.Default [ widgets ])

              Expect.isFalse (fst (root.TryGetProperty "api")) "No api member when nothing is configured"

              let entry = root.GetProperty("resources").GetProperty "tag:me@example.com,2016:widgets"
              let hints = entry.GetProperty "hints"
              Expect.isFalse (fst (hints.TryGetProperty "formats")) "No formats hint when none are declared"
          }

          test "emits the status hint" {
              let root =
                  parse (JsonHome.serialize JsonHomeOptions.Default [ { widgets with Status = Some ResourceStatus.Gone } ])

              let hints =
                  root.GetProperty("resources").GetProperty("tag:me@example.com,2016:widgets").GetProperty "hints"

              Expect.equal (hints.GetProperty("status").GetString()) "gone" "status"
          } ]
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter "FullyQualifiedName~JsonHome"
```

Expected: FAIL at compile time — `The value, namespace, type or module 'JsonHome' is not defined`.

- [ ] **Step 3: Create `src/Frank.JsonHome/JsonHome.fsi`**

```fsharp
namespace Frank.JsonHome

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type JsonHomeOptions =
    { /// Path the document is served from.
      Path: string
      /// Link relation type used when advertising the document.
      Rel: string
      /// Optional api.title member.
      Title: string option
      /// Optional api.links members.
      Links: (string * string) list }

    /// Path "/.well-known/home.json", rel "home", no api member.
    static member Default: JsonHomeOptions

module JsonHome =

    [<Literal>]
    val MediaType: string = "application/json-home"

    /// Renders resources as a draft-06 JSON Home document.
    val serialize: options: JsonHomeOptions -> resources: ResourceDescription list -> string

    /// Writes the document as an HTTP response.
    val write: options: JsonHomeOptions -> resources: ResourceDescription list -> ctx: HttpContext -> Task
```

- [ ] **Step 4: Create `src/Frank.JsonHome/JsonHome.fs`**

```fsharp
namespace Frank.JsonHome

open System.IO
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

type JsonHomeOptions =
    { Path: string
      Rel: string
      Title: string option
      Links: (string * string) list }

    static member Default =
        { Path = "/.well-known/home.json"
          Rel = "home"
          Title = None
          Links = [] }

module JsonHome =

    [<Literal>]
    let MediaType = "application/json-home"

    /// draft-06 names the accept hints per method; other methods contribute none.
    let private acceptHintName httpMethod =
        match httpMethod with
        | "POST" -> Some "acceptPost"
        | "PUT" -> Some "acceptPut"
        | "PATCH" -> Some "acceptPatch"
        | _ -> None

    let private statusName status =
        match status with
        | ResourceStatus.Deprecated -> "deprecated"
        | ResourceStatus.Gone -> "gone"

    let private writeStringArray (writer: Utf8JsonWriter) name values =
        writer.WriteStartArray(name: string)
        for value in values do writer.WriteStringValue(value: string)
        writer.WriteEndArray()

    let private writeHints (writer: Utf8JsonWriter) (resource: ResourceDescription) =
        writer.WriteStartObject "hints"

        if not (List.isEmpty resource.Methods) then
            writeStringArray writer "allow" resource.Methods

        if not (List.isEmpty resource.Formats) then
            writer.WriteStartObject "formats"
            // Each media type maps to an empty object, per the draft.
            for mediaType in List.distinct resource.Formats do
                writer.WriteStartObject(mediaType)
                writer.WriteEndObject()
            writer.WriteEndObject()

        for httpMethod, contentTypes in resource.Accepts do
            match acceptHintName httpMethod with
            | Some hint when not (List.isEmpty contentTypes) -> writeStringArray writer hint contentTypes
            | _ -> ()

        resource.Docs |> Option.iter (fun uri -> writer.WriteString("docs", uri))
        resource.Status |> Option.iter (fun s -> writer.WriteString("status", statusName s))

        writer.WriteEndObject()

    let private writeResource (writer: Utf8JsonWriter) (resource: ResourceDescription) =
        writer.WriteStartObject(resource.Rel)

        if resource.IsTemplated then
            writer.WriteString("hrefTemplate", resource.Href)

            let hrefVars = resource.HrefVars |> List.distinctBy fst

            if not (List.isEmpty hrefVars) then
                writer.WriteStartObject "hrefVars"
                for name, uri in hrefVars do writer.WriteString(name, uri)
                writer.WriteEndObject()
        else
            writer.WriteString("href", resource.Href)

        writeHints writer resource
        writer.WriteEndObject()

    let private writeDocument (writer: Utf8JsonWriter) options resources =
        writer.WriteStartObject()

        if options.Title.IsSome || not (List.isEmpty options.Links) then
            writer.WriteStartObject "api"
            options.Title |> Option.iter (fun t -> writer.WriteString("title", t))

            if not (List.isEmpty options.Links) then
                writer.WriteStartObject "links"
                for rel, target in options.Links do writer.WriteString(rel, target)
                writer.WriteEndObject()

            writer.WriteEndObject()

        writer.WriteStartObject "resources"
        // Later duplicates would overwrite earlier ones in a JSON object, so
        // duplicate rels are rejected at startup rather than silently merged.
        for resource in resources do writeResource writer resource
        writer.WriteEndObject()

        writer.WriteEndObject()

    let serialize (options: JsonHomeOptions) (resources: ResourceDescription list) =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream)
        writeDocument writer options resources
        writer.Flush()
        System.Text.Encoding.UTF8.GetString(stream.ToArray())

    let write (options: JsonHomeOptions) (resources: ResourceDescription list) (ctx: HttpContext) : Task =
        ctx.Response.ContentType <- MediaType
        ctx.Response.WriteAsync(serialize options resources)
```

- [ ] **Step 5: Register in the `.fsproj`, run the test, verify it passes**

```xml
    <Compile Include="JsonHome.fsi" />
    <Compile Include="JsonHome.fs" />
```

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj
```

Expected: PASS.

- [ ] **Step 6: Create `src/Frank.JsonHome/WebHostBuilderExtensions.fsi`**

```fsharp
namespace Frank.JsonHome

open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =

    type WebHostBuilder with

        /// Serves a JSON Home document and advertises it with a Link header.
        [<CustomOperation("useJsonHome")>]
        member UseJsonHome: spec: WebHostSpec -> WebHostSpec

        [<CustomOperation("useJsonHome")>]
        member UseJsonHome: spec: WebHostSpec * configure: (JsonHomeOptions -> JsonHomeOptions) -> WebHostSpec
```

- [ ] **Step 7: Create `src/Frank.JsonHome/WebHostBuilderExtensions.fs`**

```fsharp
namespace Frank.JsonHome

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc.ApiExplorer
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

type private HomeLinkProvider(options: JsonHomeOptions) =
    let links = [| WebLink.create options.Path options.Rel |]

    interface IResponseLinkProvider with
        member _.GetLinks(_) = links :> seq<_>

[<AutoOpen>]
module WebHostBuilderExtensions =

    let private install (options: JsonHomeOptions) (spec: WebHostSpec) =
        { spec with
            Services =
                spec.Services
                >> fun services ->
                    // AddEndpointsApiExplorer is what populates ApiDescription.
                    // It is independent of OpenAPI, which merely calls it too.
                    services.AddEndpointsApiExplorer() |> ignore
                    services.AddSingleton<IResponseLinkProvider>(HomeLinkProvider options) |> ignore
                    services
            BeforeRoutingMiddleware =
                spec.BeforeRoutingMiddleware
                >> fun app ->
                    // Both lambda parameters must be annotated: IApplicationBuilder.Use has
                    // Func<HttpContext, Func<Task>, Task> and Func<HttpContext, RequestDelegate, Task>
                    // overloads that F# cannot choose between otherwise.
                    app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                        if ctx.Request.Path.Equals(PathString options.Path) then
                            let provider =
                                ctx.RequestServices.GetRequiredService<IApiDescriptionGroupCollectionProvider>()

                            let resources =
                                provider.ApiDescriptionGroups.Items
                                |> Seq.collect (fun g -> g.Items)
                                |> ApiSurface.ofApiDescriptions

                            JsonHome.write options resources ctx
                        else
                            next.Invoke ctx)) }

    type WebHostBuilder with

        [<CustomOperation("useJsonHome")>]
        member _.UseJsonHome(spec: WebHostSpec) : WebHostSpec = install JsonHomeOptions.Default spec

        [<CustomOperation("useJsonHome")>]
        member _.UseJsonHome(spec: WebHostSpec, configure: JsonHomeOptions -> JsonHomeOptions) : WebHostSpec =
            install (configure JsonHomeOptions.Default) spec
```

- [ ] **Step 8: Register in the `.fsproj`, build all TFMs, run all tests**

```xml
    <Compile Include="WebHostBuilderExtensions.fsi" />
    <Compile Include="WebHostBuilderExtensions.fs" />
```

```bash
dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj
```

Expected: build succeeds on all three TFMs; all tests pass.

- [ ] **Step 9: Verify no NuGet dependencies leaked in**

```bash
dotnet list src/Frank.JsonHome/Frank.JsonHome.fsproj package --include-transitive
```

Expected: only `FSharp.Core` and framework references. If `Microsoft.AspNetCore.OpenApi` or anything else appears, remove it — `AddEndpointsApiExplorer` lives in `Microsoft.AspNetCore.App`.

- [ ] **Step 10: Commit**

```bash
git add src/Frank.JsonHome/ test/Frank.JsonHome.Tests/
git commit -m "feat(jsonhome): serialize and serve the home document

Hand-written Utf8JsonWriter rather than attribute-driven serialization,
because resources is an object keyed by link relation type and absent
members must be omitted rather than emitted as null.

useJsonHome registers AddEndpointsApiExplorer itself, so JSON Home works
with no OpenAPI dependency."
```

---

### Task 6: Authorization filtering

**Files:**
- Create: `src/Frank.JsonHome/AuthorizationFilter.fsi`, `.fs`
- Modify: `src/Frank.JsonHome/WebHostBuilderExtensions.fs` (apply the filter, add cache directives)
- Modify: `src/Frank.JsonHome/Frank.JsonHome.fsproj` (register before `WebHostBuilderExtensions`)
- Test: `test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs` (create), register before `Program.fs`

**Interfaces:**
- Consumes: `ResourceDescription` (Task 4), `JsonHomeOptions` (Task 5).
- Produces: `AuthorizationFilter.apply : HttpContext -> ResourceDescription list -> Task<ResourceDescription list>`

**Background you need:**

`Frank.Auth` emits an `AuthorizeAttribute` plus a built `AuthorizationPolicy` onto endpoint metadata (`src/Frank.Auth/EndpointAuth.fs:11-32`). Reading those two stock types is all that is needed — **do not reference `Frank.Auth`**.

A resource with no `IAuthorizeData` in its metadata is unauthenticated and always included. A resource with `IAuthorizeData` is evaluated with `IAuthorizationService.AuthorizeAsync(user, resource, policy)`, where the policy is the combination of any `AuthorizationPolicy` instances found in metadata, falling back to `AuthorizationPolicy.CombineAsync` over the `IAuthorizeData` entries via `IAuthorizationPolicyProvider`.

Filtering is **resource-granular**: `Frank.Auth`'s operations are on `ResourceBuilder`, so their metadata lands on every endpoint in the resource and the whole resource appears or does not.

Authorization evaluation that throws must be treated as **denied** — failing closed is the only safe direction.

When filtering is active, emit `Cache-Control: private, no-cache` and `Vary: Authorization`, so a shared cache cannot serve one principal's document to another. This is the one place this feature can leak information.

- [ ] **Step 1: Write the failing test**

Create `test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs`:

```fsharp
module Frank.JsonHome.Tests.AuthorizationFilterTests

open System.Security.Claims
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Expecto
open Frank.JsonHome

let private adminOnly =
    let builder = AuthorizationPolicyBuilder()
    builder.RequireRole "admin" |> ignore
    builder.Build()

let private describe rel (metadata: obj list) =
    { Rel = rel
      Href = "/" + rel
      IsTemplated = false
      HrefVars = []
      Methods = [ "GET" ]
      Formats = []
      Accepts = []
      Docs = None
      Status = None
      Metadata = metadata }

let private contextFor (roles: string list) =
    let services = ServiceCollection()
    services.AddAuthorization() |> ignore
    services.AddLogging() |> ignore

    let ctx = DefaultHttpContext()
    ctx.RequestServices <- services.BuildServiceProvider()

    let claims = roles |> List.map (fun r -> Claim(ClaimTypes.Role, r))
    ctx.User <- ClaimsPrincipal(ClaimsIdentity(claims, "Test"))
    ctx

[<Tests>]
let tests =
    testList
        "AuthorizationFilter"
        [ testTask "resources without authorization metadata are always included" {
              let ctx = contextFor []
              let! result = AuthorizationFilter.apply ctx [ describe "public" [] ]

              Expect.hasLength result 1 "Public resource is included"
          }

          testTask "resources the principal cannot reach are omitted" {
              let ctx = contextFor []
              let guarded = describe "admin" [ AuthorizeAttribute(); adminOnly ]
              let! result = AuthorizationFilter.apply ctx [ describe "public" []; guarded ]

              Expect.equal (result |> List.map (fun r -> r.Rel)) [ "public" ] "Guarded resource omitted"
          }

          testTask "resources the principal can reach are included" {
              let ctx = contextFor [ "admin" ]
              let guarded = describe "admin" [ AuthorizeAttribute(); adminOnly ]
              let! result = AuthorizationFilter.apply ctx [ describe "public" []; guarded ]

              Expect.equal (result |> List.map (fun r -> r.Rel)) [ "public"; "admin" ] "Both included"
          } ]
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter "FullyQualifiedName~AuthorizationFilter"
```

Expected: FAIL at compile time — `The value, namespace, type or module 'AuthorizationFilter' is not defined`.

- [ ] **Step 3: Create `src/Frank.JsonHome/AuthorizationFilter.fsi`**

```fsharp
namespace Frank.JsonHome

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

module AuthorizationFilter =

    /// True when any resource declares authorization requirements, meaning the
    /// document varies by principal and must not be cached by a shared cache.
    val varies: resources: ResourceDescription list -> bool

    /// Drops resources the current principal cannot reach. Resources with no
    /// authorization metadata are always kept; evaluation failures deny.
    val apply: ctx: HttpContext -> resources: ResourceDescription list -> Task<ResourceDescription list>
```

- [ ] **Step 4: Create `src/Frank.JsonHome/AuthorizationFilter.fs`**

```fsharp
namespace Frank.JsonHome

open System.Threading.Tasks
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module AuthorizationFilter =

    let private authorizeData (resource: ResourceDescription) =
        resource.Metadata
        |> List.choose (fun m ->
            match m with
            | :? IAuthorizeData as d -> Some d
            | _ -> None)

    let private policies (resource: ResourceDescription) =
        resource.Metadata
        |> List.choose (fun m ->
            match m with
            | :? AuthorizationPolicy as p -> Some p
            | _ -> None)

    let varies (resources: ResourceDescription list) =
        resources |> List.exists (fun r -> not (List.isEmpty (authorizeData r)))

    let private resolvePolicy (ctx: HttpContext) (resource: ResourceDescription) =
        task {
            match policies resource with
            | [] ->
                let provider = ctx.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>()
                return! AuthorizationPolicy.CombineAsync(provider, authorizeData resource)
            | explicitPolicies -> return AuthorizationPolicy.Combine(explicitPolicies)
        }

    let private isAllowed (ctx: HttpContext) (resource: ResourceDescription) =
        task {
            if List.isEmpty (authorizeData resource) then
                return true
            else
                try
                    match! resolvePolicy ctx resource with
                    | null -> return true
                    | policy ->
                        let service = ctx.RequestServices.GetRequiredService<IAuthorizationService>()
                        let! result = service.AuthorizeAsync(ctx.User, box resource, policy)
                        return result.Succeeded
                with _ ->
                    // Fail closed: an evaluation error must never widen access.
                    return false
        }

    let apply (ctx: HttpContext) (resources: ResourceDescription list) =
        task {
            let kept = ResizeArray()

            for resource in resources do
                let! allowed = isAllowed ctx resource
                if allowed then kept.Add resource

            return List.ofSeq kept
        }
```

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter "FullyQualifiedName~AuthorizationFilter"
```

Expected: PASS, 3 tests.

- [ ] **Step 6: Apply the filter in the request path**

In `src/Frank.JsonHome/WebHostBuilderExtensions.fs`, replace the body of the `app.Use(...)` lambda inside `install` with:

```fsharp
                    app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                        if ctx.Request.Path.Equals(PathString options.Path) then
                            task {
                                let provider =
                                    ctx.RequestServices.GetRequiredService<IApiDescriptionGroupCollectionProvider>()

                                let all =
                                    provider.ApiDescriptionGroups.Items
                                    |> Seq.collect (fun g -> g.Items)
                                    |> ApiSurface.ofApiDescriptions

                                let! resources = AuthorizationFilter.apply ctx all

                                if AuthorizationFilter.varies all then
                                    // A shared cache must never serve one principal's view to another.
                                    ctx.Response.Headers.CacheControl <- "private, no-cache"
                                    ctx.Response.Headers.Vary <- "Authorization"

                                do! JsonHome.write options resources ctx
                            }
                            :> Task
                        else
                            next.Invoke ctx)
```

Add `open System.Threading.Tasks` to the file's `open` block.

- [ ] **Step 7: Register in the `.fsproj` (before `WebHostBuilderExtensions`), build, test**

```xml
    <Compile Include="AuthorizationFilter.fsi" />
    <Compile Include="AuthorizationFilter.fs" />
    <Compile Include="WebHostBuilderExtensions.fsi" />
    <Compile Include="WebHostBuilderExtensions.fs" />
```

```bash
dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj
```

Expected: build succeeds on all three TFMs; all tests pass.

- [ ] **Step 8: Commit**

```bash
git add src/Frank.JsonHome/ test/Frank.JsonHome.Tests/
git commit -m "feat(jsonhome): filter the home document by principal

Reads stock IAuthorizeData and AuthorizationPolicy endpoint metadata, so it
works with Frank.Auth without referencing it and equally with a plain
AuthorizeAttribute. Evaluation failures deny.

Emits Cache-Control: private, no-cache and Vary: Authorization whenever any
resource is guarded -- otherwise a shared cache could serve one principal's
document to another."
```

---

### Task 7: End-to-end integration test

**Files:**
- Create: `test/Frank.JsonHome.Tests/IntegrationTests.fs`, register before `Program.fs`
- Modify: `test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj` (add a `ProjectReference` to `src/Frank.Auth` for the role-based case)

**Interfaces:**
- Consumes: everything from Tasks 1-6.
- Produces: nothing.

**Background you need:**

`WebHostBuilder.Run` builds *and blocks*, so tests must wire the pipeline by hand. Mirror `test/Frank.Auth.Tests/AuthorizationTests.fs:26-97`: a `TestEndpointDataSource`, a header-driven `TestAuthHandler`, and `.UseTestServer()`.

`IApiDescriptionGroupCollectionProvider` needs `services.AddEndpointsApiExplorer()`, and it discovers endpoints from registered `EndpointDataSource` instances — so the test data source must be registered in DI, not only added inside `UseEndpoints`.

- [ ] **Step 1: Write the test**

Create `test/Frank.JsonHome.Tests/IntegrationTests.fs`:

```fsharp
module Frank.JsonHome.Tests.IntegrationTests

open System
open System.Net.Http
open System.Security.Claims
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.JsonHome

let [<Literal>] TestScheme = "TestScheme"

type TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

type TestAuthHandler(options, logger, encoder) =
    inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

    override this.HandleAuthenticateAsync() =
        let user = this.Request.Headers["X-Test-User"].ToString()

        if String.IsNullOrEmpty user then
            Task.FromResult(AuthenticateResult.NoResult())
        else
            let claims = ResizeArray [ Claim(ClaimTypes.Name, user) ]
            let roles = this.Request.Headers["X-Test-Roles"].ToString()

            if not (String.IsNullOrEmpty roles) then
                for role in roles.Split ';' do
                    claims.Add(Claim(ClaimTypes.Role, role))

            let identity = ClaimsIdentity(claims, TestScheme)
            let ticket = AuthenticationTicket(ClaimsPrincipal identity, TestScheme)
            Task.FromResult(AuthenticateResult.Success ticket)

let private options = JsonHomeOptions.Default

let private createServer (resources: Resource list) =
    let endpoints = resources |> List.collect (fun r -> List.ofArray r.Endpoints) |> Array.ofList

    let host =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        services.AddEndpointsApiExplorer() |> ignore

                        services.AddAuthentication(TestScheme)
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, fun _ -> ())
                        |> ignore

                        services.AddAuthorization() |> ignore

                        // ApiExplorer discovers endpoints through registered data sources.
                        services.AddSingleton<EndpointDataSource>(TestEndpointDataSource endpoints) |> ignore)
                    .Configure(fun app ->
                        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                            let links = [| WebLink.create options.Path options.Rel |]

                            match WebLink.middleware [| { new IResponseLinkProvider with
                                                            member _.GetLinks(_) = links :> seq<_> } |] with
                            | Some run -> run ctx (fun () -> next.Invoke ctx)
                            | None -> next.Invoke ctx)
                        |> ignore

                        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                            if ctx.Request.Path.Equals(PathString options.Path) then
                                task {
                                    let provider =
                                        ctx.RequestServices
                                            .GetRequiredService<Microsoft.AspNetCore.Mvc.ApiExplorer.IApiDescriptionGroupCollectionProvider>()

                                    let all =
                                        provider.ApiDescriptionGroups.Items
                                        |> Seq.collect (fun g -> g.Items)
                                        |> ApiSurface.ofApiDescriptions

                                    let! kept = AuthorizationFilter.apply ctx all
                                    do! JsonHome.write options kept ctx
                                }
                                :> Task
                            else
                                next.Invoke ctx)
                        |> ignore

                        app
                            .UseRouting()
                            .UseAuthentication()
                            .UseAuthorization()
                            .UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                        |> ignore)
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()

let private ok: RequestDelegate = RequestDelegate(fun ctx -> ctx.Response.WriteAsync "OK")

[<Tests>]
let tests =
    testList
        "JSON Home integration"
        [ testTask "serves the document with the json-home media type" {
              let products =
                  resource "/products" {
                      rel "tag:example.com,2026:products"
                      get ok
                  }

              use client = createServer [ products ]
              let! (response: HttpResponseMessage) = client.GetAsync options.Path

              Expect.equal
                  (response.Content.Headers.ContentType.MediaType)
                  "application/json-home"
                  "Media type"

              let! body = response.Content.ReadAsStringAsync()
              let root = JsonDocument.Parse(body).RootElement

              Expect.isTrue
                  (fst (root.GetProperty("resources").TryGetProperty "tag:example.com,2026:products"))
                  "Resource is present"
          }

          testTask "advertises the document with a Link header, including on 404s" {
              let products =
                  resource "/products" {
                      rel "tag:example.com,2026:products"
                      get ok
                  }

              use client = createServer [ products ]

              let! (found: HttpResponseMessage) = client.GetAsync "/products"
              let! (missing: HttpResponseMessage) = client.GetAsync "/nope"

              let expected = "</.well-known/home.json>; rel=\"home\""

              Expect.contains (found.Headers.GetValues "Link") expected "Link on a matched route"
              Expect.contains (missing.Headers.GetValues "Link") expected "Link on a 404"
          } ]
```

Add the `Frank.Auth` project reference to `test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj` so a follow-up role test can use `requireRole`:

```xml
    <ProjectReference Include="../../src/Frank.JsonHome/Frank.JsonHome.fsproj" />
    <ProjectReference Include="../../src/Frank.Auth/Frank.Auth.fsproj" />
```

- [ ] **Step 2: Run the tests**

```bash
dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj
```

Expected: PASS, all tests. If the home document comes back with an empty `resources` object, `IApiDescriptionGroupCollectionProvider` is not seeing the endpoints — confirm `EndpointDataSource` is registered as a **singleton in DI**, not only added inside `UseEndpoints`.

- [ ] **Step 3: Commit**

```bash
git add test/Frank.JsonHome.Tests/
git commit -m "test(jsonhome): end-to-end document, media type, and Link header

Wires the pipeline by hand because WebHostBuilder.Run builds and blocks,
mirroring the harness in Frank.Auth.Tests."
```

---

## Verification Checklist

Run after all seven tasks:

- [ ] `dotnet build src/Frank/Frank.fsproj` succeeds on net8.0, net9.0, and net10.0
- [ ] `dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj` succeeds on net8.0, net9.0, and net10.0
- [ ] `dotnet build Frank.sln` succeeds
- [ ] `dotnet test test/Frank.Tests/Frank.Tests.fsproj` passes
- [ ] `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj` passes
- [ ] `dotnet test test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj` passes
- [ ] `dotnet test test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj` passes
- [ ] `dotnet list src/Frank.JsonHome/Frank.JsonHome.fsproj package --include-transitive` shows no packages beyond `FSharp.Core`
- [ ] `git grep -n "Frank.Auth" src/Frank.JsonHome/` returns nothing
- [ ] `git grep -n "accept-post\|accept-put\|accept-patch\|precondition-req\|auth-req" src/` returns nothing (hyphenated hint names are from superseded drafts)

## Deferred

Recorded in the design doc, deliberately not in this plan:

- **Duplicate `rel` detection at startup.** The design calls for a startup error when two resources declare the same relation type; `ApiSurface` currently lets the later one win, matching JSON object semantics. Needs a hook that runs at application start, which Frank does not currently offer.
- **`hrefVar` validation** against the route template's actual variables.
- **`Frank.OpenApi` emitting `service-desc`** — a one-liner on top of Task 1, but it belongs to the OpenAPI work.
- **Per-method `allow` filtering**, which needs handler-level authorization in `Frank.Auth`.
