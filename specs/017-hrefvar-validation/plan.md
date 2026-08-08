# Implementation Plan: hrefVar / Route Template Validation

**Branch**: `017-hrefvar-validation` (current worktree branch: `worktree-hrefVars`) | **Date**: 2026-08-08
**Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md)
**Input**: GitHub issue #474

## Summary

Add FRANK003 (compile-time, `Frank.Analyzers`) and a runtime `IStartupFilter` check (`Frank.JsonHome`) that both flag a mismatch between a resource's route template `{variables}` and its declared `hrefVar`s — in both directions (undeclared template variable, and declared-but-unmatched hrefVar). Both mechanisms call the same pure diff function, `HrefVarValidation.diff`, which lives in a new dependency-free `Frank.JsonHome` file that is also source-linked (no `ProjectReference`) into `Frank.Analyzers`. See [research.md](research.md) for why (the analyzer can't take a `ProjectReference` without an unwanted framework-reference edge).

**2026-08-08 correction:** the runtime mechanism was originally designed as `AddOptionsWithValidateOnStart<JsonHomeOptions>()` + `IValidateOptions<JsonHomeOptions>`. That mechanism fires during `Host.StartAsync`, before `GenericWebHostService.StartAsync` runs the `Configure` delegate that populates routing (`src/Frank/WebHostBuilder.fs:58-60`) — so it would always see zero resources and silently report success. Corrected to `IStartupFilter`, which checks only after the real pipeline has been built. See research.md R1. This also means the DI-wiring task (T010) is **no longer functionally blocked on #475** — `IStartupFilter` doesn't touch the Options pattern at all — though it still edits the same function in `WebHostBuilderExtensions.fs` that #475 is independently changing, so landing order still matters to avoid a textual merge conflict.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (matching `Frank.JsonHome`/`Frank.Analyzers`'s existing multi-targeting)
**Primary Dependencies**: No new NuGet packages. `Frank.JsonHome` (project reference, unchanged), `FSharp.Analyzers.SDK` (existing, `Frank.Analyzers`), `Microsoft.Extensions.Options` / `Microsoft.AspNetCore.Mvc.ApiExplorer` (both already implicitly available via `Microsoft.AspNetCore.App` framework reference already on `Frank.JsonHome.fsproj`)
**Testing**: Expecto (matching `test/Frank.JsonHome.Tests`), `fsharp-analyzers` CLI + fixture files (matching `test/Frank.Analyzers.Tests`)
**Project Type**: Additive change to two existing packages (`Frank.JsonHome`, `Frank.Analyzers`) — not a new package, so the "every new package needs README + sample" rule doesn't apply; the *existing* `Frank.JsonHome.Sample` README gets a documentation addition instead (Task T011).
**Constraints**: No new Frank core extension point (confirmed unnecessary — see research.md R1). No `ProjectReference` from `Frank.Analyzers` to `Frank.JsonHome` (see research.md R2). `src/Frank.JsonHome/WebHostBuilderExtensions.fs` changes (T010) should be sequenced with #475's independent edit to the same `install` function to avoid a textual merge conflict — no longer a functional dependency (see 2026-08-08 correction).

## Design

### Type: `HrefVarValidation.Mismatch`

```fsharp
namespace Frank.JsonHome

module HrefVarValidation =
    type Mismatch = { Missing: string list; Extra: string list }
    val diff: routeTemplate: string -> declaredNames: string list -> Mismatch
    val isValid: mismatch: Mismatch -> bool
```

`Missing` = template variables with no `hrefVar`. `Extra` = declared `hrefVar` names with no matching template variable (the motivating typo case). Both computed via `Set.difference` against `UriTemplate.variables routeTemplate`, so a template variable repeated across multiple `{...}` segments (e.g. `/a/{id}/b/{id}`) is not double-counted (spec Edge Cases).

### Type: `HrefVarStartupFilter`

```fsharp
namespace Frank.JsonHome

exception HrefVarValidationException of messages: string list

[<Sealed>]
type HrefVarStartupFilter =
    new: apiDescriptions: IApiDescriptionGroupCollectionProvider -> HrefVarStartupFilter
    interface IStartupFilter
```

`IStartupFilter.Configure(next)` returns an `Action<IApplicationBuilder>` that itself runs during pipeline construction. Calling `next.Invoke(app)` first lets the rest of the pipeline — including the app's own `Configure`/`UseEndpoints`, which is where routing gets populated — build completely; the check only runs, and only inspects `apiDescriptions`, after that call returns. This is correct regardless of how many other `IStartupFilter`s are registered, since `next` always resolves through to the real `Configure` eventually. Throwing at that point aborts pipeline construction inside `GenericWebHostService.StartAsync`, before the server starts — see research.md R1 for why the original `IValidateOptions<JsonHomeOptions>` design didn't achieve this.

### Analyzer AST shape

An F# CE call `resource "<template>" { <body> }` parses (pre-type-check) as `SynExpr.App(funcExpr = SynExpr.App(funcExpr = SynExpr.Ident "resource"; argExpr = SynExpr.Const(SynConst.String template)); argExpr = SynExpr.ComputationExpr(expr = body))` — this is the same two-level curried-application shape `DuplicateHandlerAnalyzer.fs:178-186` already relies on for `datastar HttpMethods.Get handler`. Requiring the template argument to be a `SynExpr.Const(SynConst.String ...)` (not an arbitrary identifier) is exactly what excludes `resource productByIdResource` (the already-built-value form used inside `webHost { }`, per spec FR-003 / Edge Cases) from being misidentified as a builder call.

`hrefVar "name" "uri"` inside the body is the same two-argument curried shape, one level in: `SynExpr.App(funcExpr = SynExpr.App(funcExpr = SynExpr.Ident "hrefVar"; argExpr = SynExpr.Const(SynConst.String name)); argExpr = SynExpr.Const(SynConst.String uri))`.

## Tasks

**Shortcut audit (2026-08-08, `adversarial-reviewer` ac-review mode):** ran against the original checkpoints below; findings folded in inline. Two real (non-gaming) bugs it caught: `collectHrefVars` had no `LetOrUse` case, so a `let` binding inside a `resource { }` body would drop a real `hrefVar` declaration and produce a false-positive "Missing"; and it required `hrefVar`'s uri argument to be a string literal, which the diff logic never needs. Both fixed below. It also flagged that several build-only checkpoints (T001, T003, T006) don't prove the shared `HrefVarValidation.diff` function is actually called rather than reimplemented against the same handful of literal template/name pairs reused across every task — grep-based checkpoints and held-out test cases added throughout to close that.

### Phase 1: Shared diff logic

#### T001: Add `HrefVarValidation.fsi` + `HrefVarValidation.fs` to `Frank.JsonHome`

**Files:** `src/Frank.JsonHome/HrefVarValidation.fsi` (new), `src/Frank.JsonHome/HrefVarValidation.fs` (new), `src/Frank.JsonHome/Frank.JsonHome.fsproj`

**Before** (`Frank.JsonHome.fsproj`, relevant excerpt):
```xml
    <Compile Include="UriTemplate.fsi" />
    <Compile Include="UriTemplate.fs" />
    <Compile Include="HomeMetadata.fsi" />
```

**After**:
```xml
    <Compile Include="UriTemplate.fsi" />
    <Compile Include="UriTemplate.fs" />
    <Compile Include="HrefVarValidation.fsi" />
    <Compile Include="HrefVarValidation.fs" />
    <Compile Include="HomeMetadata.fsi" />
```

`HrefVarValidation.fsi`:
```fsharp
namespace Frank.JsonHome

/// Compares a route template's `{name}` variables against a resource's
/// declared `hrefVar` names. Dependency-free -- shared by the FRANK003
/// compile-time analyzer (linked directly, no ProjectReference; see
/// research.md R2) and the runtime IStartupFilter check.
module HrefVarValidation =

    /// Template variables with no matching declaration, and declared names
    /// with no matching template variable.
    type Mismatch = { Missing: string list; Extra: string list }

    /// Diffs a route template's variables against a set of declared hrefVar
    /// names. A template variable repeated across multiple segments is not
    /// double-counted.
    val diff: routeTemplate: string -> declaredNames: string list -> Mismatch

    /// True when neither list has an entry.
    val isValid: mismatch: Mismatch -> bool
```

`HrefVarValidation.fs`:
```fsharp
namespace Frank.JsonHome

module HrefVarValidation =

    type Mismatch = { Missing: string list; Extra: string list }

    let diff (routeTemplate: string) (declaredNames: string list) : Mismatch =
        let expected = UriTemplate.variables routeTemplate |> Set.ofList
        let declared = declaredNames |> Set.ofList

        { Missing = Set.difference expected declared |> Set.toList |> List.sort
          Extra = Set.difference declared expected |> Set.toList |> List.sort }

    let isValid (mismatch: Mismatch) =
        List.isEmpty mismatch.Missing && List.isEmpty mismatch.Extra
```

**Checkpoint:** `dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj` succeeds for all three TFMs, AND `grep -c "UriTemplate.variables" src/Frank.JsonHome/HrefVarValidation.fs` is ≥ 1 — `diff` must call the existing pure function, not reimplement variable extraction or special-case the literal template/name pairs used in T002's tests.

**Anti-shortcut:** Do not implement `diff` as a lookup table keyed on the specific `(routeTemplate, declaredNames)` tuples used in T002's tests (e.g. a `match` on `"/products/{id}"`). T002 includes a held-out case using template/name values not reused anywhere else in this plan specifically to catch that.

**Scope lock:** Do NOT modify any file not listed above. Do NOT touch `WebHostBuilderExtensions.fs`.

---

#### T002: Add `HrefVarValidationTests.fs` to `Frank.JsonHome.Tests`

**Files:** `test/Frank.JsonHome.Tests/HrefVarValidationTests.fs` (new), `test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`

**Before** (`.fsproj` excerpt):
```xml
    <Compile Include="UriTemplateTests.fs" />
    <Compile Include="ResourceMetadataTests.fs" />
```

**After**:
```xml
    <Compile Include="UriTemplateTests.fs" />
    <Compile Include="HrefVarValidationTests.fs" />
    <Compile Include="ResourceMetadataTests.fs" />
```

`HrefVarValidationTests.fs`:
```fsharp
module Frank.JsonHome.Tests.HrefVarValidationTests

open Expecto
open Frank.JsonHome

[<Tests>]
let tests =
    testList
        "HrefVarValidation.diff"
        [ test "no mismatch when declared names match template variables exactly" {
              let result = HrefVarValidation.diff "/products/{id}" [ "id" ]
              Expect.isTrue (HrefVarValidation.isValid result) "expected no mismatch"
          }

          test "flags a declared name with no matching template variable" {
              let result = HrefVarValidation.diff "/products/{id}" [ "prodId" ]
              Expect.equal result.Extra [ "prodId" ] "extra"
              Expect.equal result.Missing [ "id" ] "missing"
          }

          test "flags a template variable with no declaration" {
              let result = HrefVarValidation.diff "/products/{id}" []
              Expect.equal result.Missing [ "id" ] "missing"
              Expect.isEmpty result.Extra "no extras"
          }

          test "a non-templated route flags every declared name as extra" {
              let result = HrefVarValidation.diff "/products" [ "id" ]
              Expect.equal result.Extra [ "id" ] "extra"
              Expect.isEmpty result.Missing "no missing"
          }

          test "a repeated template variable is not double-counted" {
              let result = HrefVarValidation.diff "/a/{id}/b/{id}" [ "id" ]
              Expect.isTrue (HrefVarValidation.isValid result) "expected no mismatch"
          }

          // Held out deliberately: no other task in this plan uses this
          // template/name pair. A `diff` implemented as a lookup table over
          // the literal tuples used elsewhere (T001/T004/T007 all reuse
          // "/products/{id}" / "id" / "prodId") cannot pass this case.
          test "a distinct template/name pair not reused elsewhere in this plan" {
              let result = HrefVarValidation.diff "/orders/{orderId}/items/{itemId}" [ "orderId" ]
              Expect.equal result.Missing [ "itemId" ] "missing"
              Expect.isEmpty result.Extra "no extras"
          }

          // Both directions in the same diff call -- T004/T007's fixtures
          // never combine Missing and Extra in one resource; this does.
          test "missing and extra reported together in one diff" {
              let result = HrefVarValidation.diff "/a/{x}/{y}" [ "x"; "z" ]
              Expect.equal result.Missing [ "y" ] "missing"
              Expect.equal result.Extra [ "z" ] "extra"
          } ]
```

**Checkpoint:** `dotnet test test/Frank.JsonHome.Tests/` — all 7 new tests pass.

**Scope lock:** Do NOT modify any file not listed above.

---

### Phase 2: Runtime check (unblocked — see Summary correction; DI wiring is T010, still sequenced against #475 for merge-collision reasons only)

#### T003: Add `HrefVarStartupFilter.fsi` + `HrefVarStartupFilter.fs` to `Frank.JsonHome`

**Files:** `src/Frank.JsonHome/HrefVarStartupFilter.fsi` (new), `src/Frank.JsonHome/HrefVarStartupFilter.fs` (new), `src/Frank.JsonHome/Frank.JsonHome.fsproj`

**Before** (`.fsproj` excerpt):
```xml
    <Compile Include="JsonHome.fsi" />
    <Compile Include="JsonHome.fs" />
    <Compile Include="WebHostBuilderExtensions.fsi" />
```

**After**:
```xml
    <Compile Include="JsonHome.fsi" />
    <Compile Include="JsonHome.fs" />
    <Compile Include="HrefVarStartupFilter.fsi" />
    <Compile Include="HrefVarStartupFilter.fs" />
    <Compile Include="WebHostBuilderExtensions.fsi" />
```

`HrefVarStartupFilter.fsi`:
```fsharp
namespace Frank.JsonHome

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.ApiExplorer

/// Raised by HrefVarStartupFilter when one or more resources have a
/// hrefVar/route-template mismatch. Carries every mismatch found, not just
/// the first.
exception HrefVarValidationException of messages: string list

/// Runs HrefVarValidation.diff against every resource in the running
/// application's ApiSurface, once the request pipeline (including routing)
/// has been built -- see research.md R1 for why IStartupFilter, not
/// IValidateOptions, is the correct hook for this check. Not yet wired into
/// useJsonHome's DI registration -- see WebHostBuilderExtensions.fs (T010).
[<Sealed>]
type HrefVarStartupFilter =
    new: apiDescriptions: IApiDescriptionGroupCollectionProvider -> HrefVarStartupFilter
    interface IStartupFilter
```

`HrefVarStartupFilter.fs`:
```fsharp
namespace Frank.JsonHome

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc.ApiExplorer

exception HrefVarValidationException of messages: string list

type HrefVarStartupFilter(apiDescriptions: IApiDescriptionGroupCollectionProvider) =

    interface IStartupFilter with
        member _.Configure(next: Action<IApplicationBuilder>) : Action<IApplicationBuilder> =
            Action<IApplicationBuilder>(fun app ->
                // Let the rest of the pipeline -- including UseEndpoints --
                // configure first. Only after this call returns does the
                // routing EndpointDataSource (and therefore
                // IApiDescriptionGroupCollectionProvider) reflect the real,
                // final set of resources.
                next.Invoke(app)

                let descriptions =
                    apiDescriptions.ApiDescriptionGroups.Items
                    |> Seq.collect (fun group -> group.Items)

                let failures =
                    ApiSurface.ofApiDescriptions descriptions
                    |> List.collect (fun resource ->
                        let mismatch = HrefVarValidation.diff resource.Href (resource.HrefVars |> List.map fst)

                        [ for name in mismatch.Missing ->
                              $"Resource '{resource.Rel}' ({resource.Href}): route variable '{{{name}}}' has no hrefVar declaration"
                          for name in mismatch.Extra ->
                              $"Resource '{resource.Rel}' ({resource.Href}): hrefVar '{name}' does not match any route template variable" ])

                if not (List.isEmpty failures) then
                    raise (HrefVarValidationException failures))
```

**Checkpoint:** `dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj` succeeds for all three TFMs, AND `grep -c "HrefVarValidation.diff" src/Frank.JsonHome/HrefVarStartupFilter.fs` is ≥ 1. **Neither proves correctness on its own** — a stub that never raises, or one that string-sniffs for the literal names `"prodId"`/`"id"` instead of calling `HrefVarValidation.diff` per resource, both compile and could pass a superficial read. This task is not done until T004's tests (which go through a real `Host`/`TestServer` startup sequence, not a hand-constructed provider — see research.md R1 correction on why that matters here specifically) pass; do not report T003 complete on its own.

**Anti-shortcut:** The action must call `next.Invoke(app)` **before** reading `apiDescriptions` — reading it first (or not calling `next` at all) would see the same empty routing table that broke the original `IValidateOptions` design. Must call `HrefVarValidation.diff resource.Href (resource.HrefVars |> List.map fst)` once per resource from `ApiSurface.ofApiDescriptions` — not pattern-match/string-search for specific names.

**Scope lock:** Do NOT modify `WebHostBuilderExtensions.fs`/`.fsi` — the filter class must compile and be independently testable without being registered anywhere.

---

#### T004: Add `HrefVarStartupFilterTests.fs` to `Frank.JsonHome.Tests`

**Files:** `test/Frank.JsonHome.Tests/HrefVarStartupFilterTests.fs` (new), `test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`

**Before** (`.fsproj` excerpt):
```xml
    <Compile Include="AuthorizationFilterTests.fs" />
    <Compile Include="IntegrationTests.fs" />
```

**After**:
```xml
    <Compile Include="AuthorizationFilterTests.fs" />
    <Compile Include="HrefVarStartupFilterTests.fs" />
    <Compile Include="IntegrationTests.fs" />
```

**Design intent:** this test goes through a real `Host.Build()` + `host.Start()` sequence (not a hand-constructed `IApiDescriptionGroupCollectionProvider`) specifically because the bug this whole mechanism exists to avoid (research.md R1) was a timing assumption that a unit-level test could easily paper over. If `HrefVarStartupFilter` is ever changed to check *before* calling `next.Invoke(app)`, this test must fail.

`HrefVarStartupFilterTests.fs` (a scoped-down copy of `IntegrationTests.fs`'s `TestEndpointDataSource` pattern — no auth needed here, so not shared with that file, to keep this task's scope lock to files it alone owns):
```fsharp
module Frank.JsonHome.Tests.HrefVarStartupFilterTests

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Expecto
open Frank.Builder
open Frank.JsonHome

type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

let private noop: RequestDelegate = RequestDelegate(fun ctx -> ctx.Response.WriteAsync "")

let private buildHost (resources: Resource list) : IHost =
    let endpoints = resources |> List.collect (fun r -> List.ofArray r.Endpoints) |> Array.ofList

    Host
        .CreateDefaultBuilder([||])
        .ConfigureWebHost(fun webBuilder ->
            webBuilder
                .UseTestServer()
                .ConfigureServices(fun services ->
                    services.AddRouting() |> ignore
                    services.AddEndpointsApiExplorer() |> ignore
                    services.AddSingleton<EndpointDataSource>(TestEndpointDataSource endpoints) |> ignore
                    services.AddSingleton<IStartupFilter, HrefVarStartupFilter>() |> ignore)
                .Configure(fun app ->
                    app.UseRouting().UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                    |> ignore)
            |> ignore)
        .Build()

// IHostedService startup failures are sometimes wrapped in AggregateException
// -- unwrap defensively rather than assuming a bare HrefVarValidationException.
let private startAndCaptureFailure (host: IHost) : string list option =
    try
        host.Start()
        None
    with
    | :? AggregateException as agg ->
        match agg.Flatten().InnerExceptions |> Seq.tryPick (function
            | HrefVarValidationException messages -> Some messages
            | _ -> None) with
        | Some messages -> Some messages
        | None -> reraise ()
    | HrefVarValidationException messages -> Some messages

[<Tests>]
let tests =
    testList
        "HrefVarStartupFilter"
        [ test "starts successfully when hrefVar matches the route template" {
              let productResource =
                  resource "/products/{id}" {
                      rel "tag:example.com,2026:product"
                      hrefVar "id" "https://example.com/param/product-id"
                      get noop
                  }

              use host = buildHost [ productResource ]
              Expect.isNone (startAndCaptureFailure host) "expected the host to start"
          }

          test "fails to start when hrefVar doesn't match any route template variable" {
              let productResource =
                  resource "/products/{id}" {
                      rel "tag:example.com,2026:product"
                      hrefVar "prodId" "https://example.com/param/product-id"
                      get noop
                  }

              use host = buildHost [ productResource ]

              match startAndCaptureFailure host with
              | Some messages -> Expect.stringContains (String.concat " " messages) "prodId" "names the mismatched hrefVar"
              | None -> failtest "expected startup to fail"
          }

          test "fails to start when a route template variable has no hrefVar declaration" {
              let productResource =
                  resource "/products/{id}" {
                      rel "tag:example.com,2026:product"
                      get noop
                  }

              use host = buildHost [ productResource ]

              match startAndCaptureFailure host with
              | Some messages -> Expect.stringContains (String.concat " " messages) "id" "names the missing variable"
              | None -> failtest "expected startup to fail"
          }

          // FR-007: failures must aggregate across every mismatched resource,
          // not just the first one found. A filter that raises on the first
          // bad resource (List.tryFind / List.exists short-circuit instead
          // of List.collect over all of them) fails this test even though
          // it passes the three single-resource tests above.
          test "aggregates mismatches across multiple resources into one failure" {
              let productResource =
                  resource "/products/{id}" {
                      rel "tag:example.com,2026:product"
                      hrefVar "prodId" "https://example.com/param/product-id"
                      get noop
                  }

              let orderResource =
                  resource "/orders/{orderId}" {
                      rel "tag:example.com,2026:order"
                      get noop
                  }

              use host = buildHost [ productResource; orderResource ]

              match startAndCaptureFailure host with
              | Some messages ->
                  let text = String.concat " " messages
                  Expect.stringContains text "prodId" "names the product mismatch"
                  Expect.stringContains text "orderId" "also names the order mismatch"
              | None -> failtest "expected startup to fail"
          } ]
```

**Checkpoint:** `dotnet test test/Frank.JsonHome.Tests/` — all 4 new tests pass. If `AggregateException`-unwrapping in `startAndCaptureFailure` turns out not to be how .NET actually surfaces this (i.e. `HrefVarValidationException` propagates bare), the `HrefVarValidationException messages` branch of the `try/with` catches it directly — confirm which path actually fires when this test is first run, and simplify `startAndCaptureFailure` if the `AggregateException` branch is dead.

**Scope lock:** Do NOT modify `IntegrationTests.fs`, `WebHostBuilderExtensions.fs`, `WebHostBuilderExtensions.fsi`, or any other existing file.

---

### Phase 3: Compile-time analyzer

#### T005: Link `UriTemplate` + `HrefVarValidation` source into `Frank.Analyzers`

**Files:** `src/Frank.Analyzers/Frank.Analyzers.fsproj`

**Before**:
```xml
  <ItemGroup>
    <Compile Include="DuplicateHandlerAnalyzer.fsi" />
    <Compile Include="DuplicateHandlerAnalyzer.fs" />
  </ItemGroup>
```

**After**:
```xml
  <ItemGroup>
    <!-- Linked, not ProjectReference'd: HrefVarAnalyzer reuses Frank.JsonHome's
         pure UriTemplate/HrefVarValidation logic without pulling the
         Microsoft.AspNetCore.App framework reference into this analyzer
         package. See specs/017-hrefvar-validation/research.md R2. -->
    <Compile Include="../Frank.JsonHome/UriTemplate.fsi" Link="UriTemplate.fsi" />
    <Compile Include="../Frank.JsonHome/UriTemplate.fs" Link="UriTemplate.fs" />
    <Compile Include="../Frank.JsonHome/HrefVarValidation.fsi" Link="HrefVarValidation.fsi" />
    <Compile Include="../Frank.JsonHome/HrefVarValidation.fs" Link="HrefVarValidation.fs" />
    <Compile Include="DuplicateHandlerAnalyzer.fsi" />
    <Compile Include="DuplicateHandlerAnalyzer.fs" />
    <Compile Include="HrefVarAnalyzer.fsi" />
    <Compile Include="HrefVarAnalyzer.fs" />
  </ItemGroup>
```

**Checkpoint:** After T006 exists, `dotnet build src/Frank.Analyzers/Frank.Analyzers.fsproj` succeeds with zero new `ProjectReference` or `PackageReference` entries in the `.fsproj` (a raw `<Reference Include="...Frank.JsonHome.dll">` would satisfy the checkpoint's letter while reintroducing the framework-reference edge this task exists to avoid — don't do that either).

**Note (unverified until a real build after T007):** once `Frank.Analyzers.Tests.fsproj` references both `Frank.Analyzers.fsproj` (which links `Frank.JsonHome`'s source) and `Frank.JsonHome.fsproj` directly (added in T007), the fixtures project sees two independently-compiled `Frank.JsonHome.UriTemplate`/`HrefVarValidation` modules — one baked into `Frank.Analyzers.dll`, one in the real `Frank.JsonHome.dll`. Fixtures never reference these modules unqualified today, so this is expected to build clean, but confirm with a real `dotnet build test/Frank.Analyzers.Tests/Frank.Analyzers.Tests.fsproj` after T007 rather than assuming.

**Scope lock:** Do NOT add a `ProjectReference` to `Frank.JsonHome.fsproj`. Do NOT modify `src/Frank.JsonHome/*` from this task.

---

#### T006: Add `HrefVarAnalyzer.fsi` + `HrefVarAnalyzer.fs`

**Files:** `src/Frank.Analyzers/HrefVarAnalyzer.fsi` (new), `src/Frank.Analyzers/HrefVarAnalyzer.fs` (new)

`HrefVarAnalyzer.fsi`:
```fsharp
module Frank.Analyzers.HrefVarAnalyzer

open FSharp.Analyzers.SDK
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text

/// Every `hrefVar "name" <uri-expr>` call captured from inside a
/// `resource "<template>" { }` body, with the range of its declaration.
/// Recurses through `let` bindings inside the CE body so a hrefVar
/// declaration after an intervening `let` isn't silently dropped.
val collectHrefVars: bodyExpr: SynExpr -> (string * range) list

/// Recognizes a `resource "<template>" { <body> }` call site (string-literal
/// template only -- NOT `resource <identifier> { }`, the already-built-value
/// form used inside `webHost { }`). Returns the template text, its range,
/// and the CE body to scan for hrefVar declarations.
val tryResourceLiteral: expr: SynExpr -> (string * range * SynExpr) option

/// Message for a route template variable with no hrefVar declaration.
val createMissingMessage: varName: string -> resourceRange: range -> Message

/// Message for a declared hrefVar with no matching route template variable.
val createExtraMessage: varName: string -> declRange: range -> Message

/// Analyze a parsed F# file for hrefVar / route template mismatches.
val analyzeFile: parseTree: ParsedInput -> Message list

[<Literal>]
val name: string = "HrefVarAnalyzer"

[<Literal>]
val shortDescription: string =
    "Detects hrefVar declarations that don't match the resource's route template variables (FRANK003)"

[<Literal>]
val helpUri: string = "https://github.com/frank-fs/frank/issues/474"

/// Editor analyzer for IDE integration (Ionide, Visual Studio, Rider)
[<EditorAnalyzer(name, shortDescription, helpUri)>]
val editorAnalyzer: Analyzer<EditorContext>

/// CLI analyzer for command-line and CI/CD usage
[<CliAnalyzer(name, shortDescription, helpUri)>]
val cliAnalyzer: Analyzer<CliContext>
```

`HrefVarAnalyzer.fs`:
```fsharp
module Frank.Analyzers.HrefVarAnalyzer

open FSharp.Analyzers.SDK
open FSharp.Analyzers.SDK.ASTCollecting
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open Frank.JsonHome

// Note: only the NAME argument needs to be a string literal (that's the
// value the diff runs against); the uri argument is deliberately unmatched
// (`argExpr = _`) since diff never looks at it -- requiring it to also be a
// literal would silently drop declarations using a computed/shared uri
// value. LetOrUse is handled so a `let` between CE statements doesn't drop
// a real hrefVar declaration into a false-positive "Missing".
let rec collectHrefVars (bodyExpr: SynExpr) : (string * range) list =
    match bodyExpr with
    | SynExpr.App(
        funcExpr = SynExpr.App(funcExpr = SynExpr.Ident hrefVarIdent
                                argExpr = SynExpr.Const(constant = SynConst.String(text = varName)))
        argExpr = _
        range = r) when hrefVarIdent.idText = "hrefVar" -> [ varName, r ]

    | SynExpr.Sequential(expr1 = e1; expr2 = e2) -> collectHrefVars e1 @ collectHrefVars e2
    | SynExpr.Paren(expr = e) -> collectHrefVars e
    | SynExpr.LetOrUse(bindings = bindings; body = body) ->
        (bindings |> List.collect (fun (SynBinding(expr = e)) -> collectHrefVars e))
        @ collectHrefVars body
    | _ -> []

let tryResourceLiteral (expr: SynExpr) : (string * range * SynExpr) option =
    match expr with
    | SynExpr.App(
        funcExpr = SynExpr.App(funcExpr = SynExpr.Ident resourceIdent
                                argExpr = SynExpr.Const(constant = SynConst.String(text = routeTemplate); range = templateRange))
        argExpr = SynExpr.ComputationExpr(expr = bodyExpr)) when resourceIdent.idText = "resource" ->
        Some(routeTemplate, templateRange, bodyExpr)
    | _ -> None

let createMissingMessage (varName: string) (resourceRange: range) : Message =
    { Type = "hrefVar / route template mismatch"
      Message = sprintf "Route template variable '{%s}' has no matching hrefVar declaration in this resource." varName
      Code = "FRANK003"
      Severity = Severity.Error
      Range = resourceRange
      Fixes = [] }

let createExtraMessage (varName: string) (declRange: range) : Message =
    { Type = "hrefVar / route template mismatch"
      Message = sprintf "hrefVar '%s' does not match any variable in this resource's route template." varName
      Code = "FRANK003"
      Severity = Severity.Error
      Range = declRange
      Fixes = [] }

let analyzeFile (parseTree: ParsedInput) : Message list =
    let messages = ResizeArray<Message>()

    let rec walk (expr: SynExpr) =
        match tryResourceLiteral expr with
        | Some(routeTemplate, templateRange, bodyExpr) ->
            let declared = collectHrefVars bodyExpr
            let mismatch = HrefVarValidation.diff routeTemplate (declared |> List.map fst)

            for varName in mismatch.Missing do
                messages.Add(createMissingMessage varName templateRange)

            for varName in mismatch.Extra do
                let declRange = declared |> List.find (fun (n, _) -> n = varName) |> snd
                messages.Add(createExtraMessage varName declRange)

        | None ->
            match expr with
            | SynExpr.App(funcExpr = f; argExpr = a) ->
                walk f
                walk a
            | SynExpr.ComputationExpr(expr = e) -> walk e
            | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
                walk e1
                walk e2
            | SynExpr.Paren(expr = e) -> walk e
            | SynExpr.Lambda(body = b) -> walk b
            | SynExpr.LetOrUse(bindings = bindings; body = body) ->
                for binding in bindings do
                    match binding with
                    | SynBinding(expr = e) -> walk e

                walk body
            | SynExpr.IfThenElse(ifExpr = i; thenExpr = t; elseExpr = eOpt) ->
                walk i
                walk t
                eOpt |> Option.iter walk
            | _ -> ()

    let exprCollector =
        { new SyntaxCollectorBase() with
            override _.WalkExpr(_, expr: SynExpr) = walk expr }

    walkAst exprCollector parseTree

    messages |> List.ofSeq

[<Literal>]
let name = "HrefVarAnalyzer"

[<Literal>]
let shortDescription =
    "Detects hrefVar declarations that don't match the resource's route template variables (FRANK003)"

[<Literal>]
let helpUri = "https://github.com/frank-fs/frank/issues/474"

[<EditorAnalyzer(name, shortDescription, helpUri)>]
let editorAnalyzer: Analyzer<EditorContext> =
    fun (ctx: EditorContext) -> async { return analyzeFile ctx.ParseFileResults.ParseTree }

[<CliAnalyzer(name, shortDescription, helpUri)>]
let cliAnalyzer: Analyzer<CliContext> =
    fun (ctx: CliContext) -> async { return analyzeFile ctx.ParseFileResults.ParseTree }
```

**Checkpoint:** `dotnet build src/Frank.Analyzers/Frank.Analyzers.fsproj` succeeds. **Build success alone does not prove correctness** — an `analyzeFile` that always returns `[]` also compiles. This task is not done until T008's `run-analyzer-tests.sh` reports the four new fixtures (see T007) passing, including the content-grep checks T008 adds; do not report T006 complete on its own.

**Anti-shortcut:** `analyzeFile` must derive its diagnostics from the actual `SynExpr` tree via `tryResourceLiteral`/`collectHrefVars`/`HrefVarValidation.diff` — not by branching on the parsed file's name (`ParsedInput.ImplFile`'s embedded filename/`SourceText` identity). `run-analyzer-tests.sh`'s `check_test` only greps combined CLI output for `"$fixture.fs.*$code"`, which a filename-keyed fake could satisfy; T008 adds message-content greps specifically to narrow this, but the requirement is on the implementation regardless of whether the test would catch every variant.

**Scope lock:** Do NOT modify `DuplicateHandlerAnalyzer.fs`/`.fsi`.

---

#### T007: Add analyzer fixtures + wire into the fixtures project

**Files:** `test/Frank.Analyzers.Tests/fixtures/HrefVarExtra.fs` (new), `test/Frank.Analyzers.Tests/fixtures/HrefVarMissing.fs` (new), `test/Frank.Analyzers.Tests/fixtures/HrefVarValid.fs` (new), `test/Frank.Analyzers.Tests/fixtures/HrefVarWithLet.fs` (new), `test/Frank.Analyzers.Tests/Frank.Analyzers.Tests.fsproj`

**Before** (`.fsproj` excerpt):
```xml
  <ItemGroup>
    <Compile Include="fixtures/DuplicateGet.fs" />
```
```xml
  <ItemGroup>
    <ProjectReference Include="../../src/Frank/Frank.fsproj" />
    <ProjectReference Include="../../src/Frank.Datastar/Frank.Datastar.fsproj" />
  </ItemGroup>
```

**After**:
```xml
  <ItemGroup>
    <Compile Include="fixtures/DuplicateGet.fs" />
```
(unchanged position; new entries appended at the end of the existing `<Compile>` `ItemGroup`, after `fixtures/DistinctAccepts.fs`):
```xml
    <Compile Include="fixtures/DistinctAccepts.fs" />
    <Compile Include="fixtures/HrefVarExtra.fs" />
    <Compile Include="fixtures/HrefVarMissing.fs" />
    <Compile Include="fixtures/HrefVarValid.fs" />
    <Compile Include="fixtures/HrefVarWithLet.fs" />
  </ItemGroup>
```
```xml
  <ItemGroup>
    <ProjectReference Include="../../src/Frank/Frank.fsproj" />
    <ProjectReference Include="../../src/Frank.Datastar/Frank.Datastar.fsproj" />
    <ProjectReference Include="../../src/Frank.JsonHome/Frank.JsonHome.fsproj" />
  </ItemGroup>
```

`fixtures/HrefVarExtra.fs`:
```fsharp
module TestFixtures.HrefVarExtra

open Frank.Builder
open Frank.JsonHome

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK003 - "prodId" matches no {..} in the template
let hrefVarExtraResource =
    resource "/products/{id}" {
        rel "tag:example.com,2026:product"
        hrefVar "prodId" "https://example.com/param/product-id" // Typo - should be "id"
        get handler
    }
```

`fixtures/HrefVarMissing.fs`:
```fsharp
module TestFixtures.HrefVarMissing

open Frank.Builder
open Frank.JsonHome

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK003 - "id" has no hrefVar declaration
let hrefVarMissingResource =
    resource "/products/{id}" {
        rel "tag:example.com,2026:product"
        get handler
    }
```

`fixtures/HrefVarValid.fs`:
```fsharp
module TestFixtures.HrefVarValid

open Frank.Builder
open Frank.JsonHome

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should NOT trigger FRANK003 - "id" matches the template exactly
let hrefVarValidResource =
    resource "/products/{id}" {
        rel "tag:example.com,2026:product"
        hrefVar "id" "https://example.com/param/product-id"
        get handler
    }
```

`fixtures/HrefVarWithLet.fs` (regression case for the `LetOrUse` fix above — a `let` and a non-literal uri argument between the resource call and the `hrefVar` operation must NOT cause a false-positive):
```fsharp
module TestFixtures.HrefVarWithLet

open Frank.Builder
open Frank.JsonHome

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let private idVarUri = "https://example.com/param/product-id"

// This should NOT trigger FRANK003 - hrefVar is declared correctly, just
// with an intervening `let` and a non-literal uri argument.
let hrefVarWithLetResource =
    resource "/products/{id}" {
        rel "tag:example.com,2026:product"
        let uri = idVarUri
        hrefVar "id" uri
        get handler
    }
```

**Checkpoint:** `dotnet build test/Frank.Analyzers.Tests/Frank.Analyzers.Tests.fsproj` succeeds.

**Scope lock:** Do NOT modify any existing fixture file.

---

#### T008: Wire fixtures into `run-analyzer-tests.sh`

**Files:** `test/Frank.Analyzers.Tests/run-analyzer-tests.sh`

**Before**:
```bash
# Duplicate accepts media-type detection
check_test "DuplicateAccepts" true "Duplicate accepts media type detection" "FRANK002"
check_test "DistinctAccepts" false "Distinct accepts media types (no warning)" "FRANK002"

echo ""
```

**After**:
```bash
# Duplicate accepts media-type detection
check_test "DuplicateAccepts" true "Duplicate accepts media type detection" "FRANK002"
check_test "DistinctAccepts" false "Distinct accepts media types (no warning)" "FRANK002"

# hrefVar / route template validation
check_test "HrefVarExtra" true "hrefVar with no matching template variable" "FRANK003"
check_test "HrefVarMissing" true "template variable with no hrefVar declaration" "FRANK003"
check_test "HrefVarValid" false "hrefVar matches template variable (no warning)" "FRANK003"
check_test "HrefVarWithLet" false "hrefVar after an intervening let (no warning)" "FRANK003"

# Content checks -- code-only grep (check_test above) can't tell a real
# per-file diagnostic from a filename-keyed fake that emits a canned
# message regardless of the actual mismatch. Require the mismatched name
# to appear in the message text too, matching HrefVarAnalyzer.fs's actual
# createExtraMessage/createMissingMessage format strings.
if echo "$ANALYZER_OUTPUT" | grep -q "HrefVarExtra.fs" && echo "$ANALYZER_OUTPUT" | grep -q "hrefVar 'prodId'"; then
    echo -e "${GREEN}PASS${NC}: HrefVarExtra - message names the mismatched hrefVar 'prodId'"
    PASSED=$((PASSED + 1))
else
    echo -e "${RED}FAIL${NC}: HrefVarExtra - message does not name 'prodId'"
    FAILED=$((FAILED + 1))
fi

if echo "$ANALYZER_OUTPUT" | grep -q "HrefVarMissing.fs" && echo "$ANALYZER_OUTPUT" | grep -q "variable '{id}'"; then
    echo -e "${GREEN}PASS${NC}: HrefVarMissing - message names the missing variable '{id}'"
    PASSED=$((PASSED + 1))
else
    echo -e "${RED}FAIL${NC}: HrefVarMissing - message does not name '{id}'"
    FAILED=$((FAILED + 1))
fi

echo ""
```

**Checkpoint:** `test/Frank.Analyzers.Tests/run-analyzer-tests.sh` reports `Total: 23 | Passed: 23 | Failed: 0` (17 existing + 4 new `check_test` + 2 content checks).

**Scope lock:** Do NOT modify any `check_test` line other than the insertion above.

---

### Phase 4: Sample documentation (negative case)

#### T009: Document the mismatch failure mode in the sample README

**Files:** `sample/Frank.JsonHome.Sample/README.md`

**Before** (end of file):
```markdown
Even a 404 carries it -- that's where a lost client most needs the link.
```

**After** (new section appended):
```markdown
Even a 404 carries it -- that's where a lost client most needs the link.

## What happens if hrefVars don't match the route template

`productByIdResource` above declares `hrefVar "id" "..."` for `/products/{id}` -- the name matches the template's `{id}` segment exactly. If it didn't (a typo, e.g. `hrefVar "prodId" "..."`), two independent checks would catch it:

- **At compile time**: `Frank.Analyzers`' FRANK003 rule reports an error at the `hrefVar` call site, in your editor or `dotnet build` output, before you ever run the app.
- **At startup**: once wired up (tracked in issue #474 as T010), the application would refuse to start at all -- `HrefVarStartupFilter` raises `HrefVarValidationException` listing every mismatched resource, before Kestrel accepts connections, rather than serving a `/.well-known/home.json` with a `hrefVars` entry that resolves nothing.

This sample intentionally has no mismatches -- see `test/Frank.JsonHome.Tests/HrefVarStartupFilterTests.fs` and `test/Frank.Analyzers.Tests/fixtures/HrefVarExtra.fs` for the failing cases exercised directly, without breaking a runnable sample.
```

**Checkpoint:** "Manual read-through" alone is non-falsifiable and doesn't tie the prose back to what T003-T008 actually built. Instead: `grep -q "FRANK003" sample/Frank.JsonHome.Sample/README.md`, `grep -q "HrefVarValidationException" sample/Frank.JsonHome.Sample/README.md`, `test -f test/Frank.JsonHome.Tests/HrefVarStartupFilterTests.fs`, and `test -f test/Frank.Analyzers.Tests/fixtures/HrefVarExtra.fs` (the two paths the README cites) all succeed. Additionally, diff the README's paraphrased error text against `HrefVarStartupFilter.fs`'s actual format strings (`"Resource '{rel}' ({href}): hrefVar '{name}' does not match..."` / `"... route variable '{{{name}}}' has no hrefVar declaration"`) to catch drift if T003 changes wording after this task is written.

**Scope lock:** Do NOT modify `Program.fs` or any other sample file.

---

### Phase 5: DI wiring (no longer blocked — sequence against #475 to avoid a merge conflict)

#### T010: Wire `HrefVarStartupFilter` into `useJsonHome`

**Files:** `src/Frank.JsonHome/WebHostBuilderExtensions.fs`

**Before** (`WebHostBuilderExtensions.fs:10-20`):
```fsharp
    let private install (options: JsonHomeOptions) (spec: WebHostSpec) =
        let document = JsonHome.documentResource options

        { spec with
            Services =
                spec.Services
                >> fun services ->
                    // AddEndpointsApiExplorer is what populates ApiDescription.
                    // It is independent of OpenAPI, which merely calls it too.
                    services.AddEndpointsApiExplorer() |> ignore
                    services
```

**After**:
```fsharp
    let private install (options: JsonHomeOptions) (spec: WebHostSpec) =
        let document = JsonHome.documentResource options

        { spec with
            Services =
                spec.Services
                >> fun services ->
                    // AddEndpointsApiExplorer is what populates ApiDescription.
                    // It is independent of OpenAPI, which merely calls it too.
                    services.AddEndpointsApiExplorer() |> ignore

                    services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter, HrefVarStartupFilter>()
                    |> ignore

                    services
```

**No longer blocked on #475** (see Summary correction) — `IStartupFilter` needs no `IOptions<JsonHomeOptions>` resolution, so #475's `IOptionsFactory<JsonHomeOptions>` work is irrelevant to this task. It still edits the same `install` function #475 is independently changing (for its own `IOptionsFactory<JsonHomeOptions>` registration, unrelated to this line) — coordinate the actual land order with that session to land as two small, non-overlapping diffs to the same function rather than risk a conflicting simultaneous edit.

**Checkpoint:** `dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj` succeeds. `dotnet test test/Frank.JsonHome.Tests/` — `IntegrationTests.fs`'s existing tests still pass unchanged (confirms the filter doesn't break an app with no mismatches), AND a new assertion (in `IntegrationTests.fs` or a small addition to `HrefVarStartupFilterTests.fs`) that `useJsonHome` on an app built with `Frank.JsonHome.Sample`'s actual resource set starts without throwing.

**Scope lock:** Do NOT modify `HrefVarStartupFilter.fs`/`.fsi` from this task.
