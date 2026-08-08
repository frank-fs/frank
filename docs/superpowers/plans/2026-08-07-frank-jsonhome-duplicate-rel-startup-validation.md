# Frank.JsonHome: duplicate-rel startup validation

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two resources declaring the same JSON Home `rel` silently collide (later wins, no diagnostic) because `resources` in the served document is a JSON object keyed by `Rel`. Fix: an `IValidateOptions<JsonHomeOptions>` check, wired automatically into `useJsonHome`, that fails startup (via ASP.NET Core's `AddOptionsWithValidateOnStart`) naming every colliding route when two or more resources share a `rel`.

**Tracks:** frank-fs/frank#475 (reframed — no new Frank-core extension point; adopts the framework's own `ValidateOnStart` mechanism instead). No new issue.

**Explicitly out of scope** (see design doc's *Deferred / explicitly out of scope*): `Frank.OpenApi` duplicate operation ids, `Frank.Auth` unregistered policies (both named in #475 as future consumers of the same convention, not built here), any generic Frank-core `StartupChecks` type, and modifying `sample/Frank.JsonHome.Sample` to demonstrate the check (would mean shipping a deliberately broken sample).

**Design doc:** `docs/superpowers/specs/2026-08-07-frank-jsonhome-duplicate-rel-startup-validation.md`

## Global Constraints

- Every `.fs` file has a matching `.fsi` (`CLAUDE.md`). New file `DuplicateRelValidator.fs`/`.fsi` goes in `src/Frank.JsonHome/`, both types marked `internal` in both files (referenced only from `WebHostBuilderExtensions.fs`, same assembly).
- Test framework is Expecto.
- Verify across all three TFMs (`net8.0;net9.0;net10.0`) — this package multi-targets.
- Commit directly to this task's branch when done (trunk-based repo — no PR needed once merged back to master by the coordinator). Create the branch/worktree before starting; do not commit to `master`.
- No change to `JsonHomeOptions`'s public shape (no `[<CLIMutable>]`, stays a plain immutable record) — that's the point of the custom `IOptionsFactory<JsonHomeOptions>` (Task 2).
- No change to `Frank.fsproj`/`WebHostBuilder.fs`/`WebHostSpec` — this whole plan lives inside `Frank.JsonHome`.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/Frank.JsonHome/DuplicateRelValidator.fsi` | New | Public (internal) signature for `DuplicateRelValidator` and `FixedJsonHomeOptionsFactory` |
| `src/Frank.JsonHome/DuplicateRelValidator.fs` | New | `IValidateOptions<JsonHomeOptions>` duplicate-`rel` check; `IOptionsFactory<JsonHomeOptions>` fixed-value factory |
| `src/Frank.JsonHome/Frank.JsonHome.fsproj` | Modify | Add the new file pair to `<Compile>`, after `JsonHome.fs`/before `WebHostBuilderExtensions.fsi` (needs `JsonHomeOptions` and `ApiSurface`, is needed by `WebHostBuilderExtensions.fs`) |
| `src/Frank.JsonHome/WebHostBuilderExtensions.fs` | Modify | `install` registers the fixed-value factory, calls `AddOptionsWithValidateOnStart<JsonHomeOptions>()`, registers `DuplicateRelValidator` via `TryAddEnumerable` |
| `test/Frank.JsonHome.Tests/DuplicateRelValidatorTests.fs` | New | Unit tests for `DuplicateRelValidator.Validate` |
| `test/Frank.JsonHome.Tests/IntegrationTests.fs` | Modify | New test: two same-`rel` resources through `useJsonHome` → `host.Start()` throws |
| `test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj` | Modify | Add `DuplicateRelValidatorTests.fs` to `<Compile>` |
| `RELEASE_NOTES.md` | Modify | Note the new startup check and its `useJsonHome`-only opt-in (automatic, no new public API) |

---

### Task 1: `DuplicateRelValidator` and `FixedJsonHomeOptionsFactory`

**Files:** `src/Frank.JsonHome/DuplicateRelValidator.fsi`, `src/Frank.JsonHome/DuplicateRelValidator.fs`, `src/Frank.JsonHome/Frank.JsonHome.fsproj`, `test/Frank.JsonHome.Tests/DuplicateRelValidatorTests.fs`, `test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`.

**Interfaces:**
- Consumes: `ApiSurface.ofApiDescriptions` (`ApiSurface.fsi`, unchanged), `JsonHomeOptions` (`JsonHome.fsi`, unchanged), `Microsoft.AspNetCore.Mvc.ApiExplorer.IApiDescriptionGroupCollectionProvider` (framework type), `Microsoft.Extensions.Options.IValidateOptions<'T>`/`ValidateOptionsResult`/`IOptionsFactory<'T>` (framework types).
- Produces: `type internal DuplicateRelValidator = interface IValidateOptions<JsonHomeOptions>`, `type internal FixedJsonHomeOptionsFactory = interface IOptionsFactory<JsonHomeOptions>`.

**Exact contents** of `src/Frank.JsonHome/DuplicateRelValidator.fsi`:

```fsharp
namespace Frank.JsonHome

open Microsoft.AspNetCore.Mvc.ApiExplorer
open Microsoft.Extensions.Options

/// Fails startup (via ASP.NET Core's `ValidateOnStart`) when two or more
/// resources declare the same JSON Home `rel` -- `resources` in the served
/// document is a JSON object keyed by `Rel`, so a collision otherwise
/// silently drops one resource with no diagnostic (see JsonHome.fs's
/// `writeDocument` comment). Ignores its bound `JsonHomeOptions` value
/// entirely: validates the derived, app-wide resource surface via the
/// injected `IApiDescriptionGroupCollectionProvider`, the same source
/// `JsonHome.documentHandler` reads per-request.
[<Sealed>]
type internal DuplicateRelValidator =
    new: provider: IApiDescriptionGroupCollectionProvider -> DuplicateRelValidator
    interface IValidateOptions<JsonHomeOptions>

/// Returns the given `JsonHomeOptions` value unconditionally. `JsonHomeOptions`
/// is an immutable record with no parameterless constructor, so it cannot
/// flow through the default `IOptionsFactory<T>` (`Activator.CreateInstance`
/// + `IConfigureOptions<T>.Configure(Action<T>)` mutation). This factory
/// makes `IOptions<JsonHomeOptions>.Value` and `useJsonHome`'s own
/// closure-captured options the same instance -- no second, independently
/// configured copy of the same settings.
[<Sealed>]
type internal FixedJsonHomeOptionsFactory =
    new: value: JsonHomeOptions -> FixedJsonHomeOptionsFactory
    interface IOptionsFactory<JsonHomeOptions>
```

**Exact contents** of `src/Frank.JsonHome/DuplicateRelValidator.fs`:

```fsharp
namespace Frank.JsonHome

open Microsoft.AspNetCore.Mvc.ApiExplorer
open Microsoft.Extensions.Options

type internal DuplicateRelValidator(provider: IApiDescriptionGroupCollectionProvider) =
    interface IValidateOptions<JsonHomeOptions> with
        member _.Validate(_name: string, _options: JsonHomeOptions) : ValidateOptionsResult =
            let resources =
                provider.ApiDescriptionGroups.Items
                |> Seq.collect (fun g -> g.Items)
                |> ApiSurface.ofApiDescriptions

            let failures =
                resources
                |> List.groupBy (fun r -> r.Rel)
                |> List.choose (fun (rel, group) ->
                    match group with
                    | []
                    | [ _ ] -> None
                    | duplicates ->
                        let routes =
                            duplicates |> List.map (fun r -> r.Href) |> List.distinct |> String.concat ", "

                        Some $"duplicate JSON Home rel '%s{rel}': %s{routes}")

            match failures with
            | [] -> ValidateOptionsResult.Success
            | fs -> ValidateOptionsResult.Fail(fs: string seq)

type internal FixedJsonHomeOptionsFactory(value: JsonHomeOptions) =
    interface IOptionsFactory<JsonHomeOptions> with
        member _.Create(_name: string) : JsonHomeOptions = value
```

**Add to `Frank.JsonHome.fsproj`**, immediately after `JsonHome.fs` and before `WebHostBuilderExtensions.fsi`:

```xml
<Compile Include="DuplicateRelValidator.fsi" />
<Compile Include="DuplicateRelValidator.fs" />
```

**New tests** in `test/Frank.JsonHome.Tests/DuplicateRelValidatorTests.fs` (module `Frank.JsonHome.Tests.DuplicateRelValidatorTests`, add to `.fsproj`'s `<Compile>`):

- `"no duplicates -> Success"`: build a fake `IApiDescriptionGroupCollectionProvider` (or reuse whatever fake/stub `ApiSurfaceTests.fs` already uses to produce `ApiDescription` values, if one exists -- check that file first) exposing two resources with distinct `rel`s; assert `Validate` returns `Success`.
- `"two resources sharing a rel -> Fail naming both routes"`: two resources, same `rel`, different route templates; assert `Fail`'s failure message(s) contain both route templates.
- `"three resources, two share a rel -> only the colliding pair is reported"`: guards against over-flagging the non-colliding third resource.

**Verification:** `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter DuplicateRelValidatorTests` passes on all three TFMs.

---

### Task 2: Wire into `useJsonHome`

**Files:** `src/Frank.JsonHome/WebHostBuilderExtensions.fs`.

**Interfaces:**
- Consumes: `DuplicateRelValidator`/`FixedJsonHomeOptionsFactory` from Task 1 (must land first).
- Produces: `useJsonHome` (both overloads, since both call `install`) registers the fixed-value options factory, calls `AddOptionsWithValidateOnStart<JsonHomeOptions>()`, and registers `DuplicateRelValidator` via `TryAddEnumerable` -- no change to either overload's public signature.

**Exact change** to `install`'s `Services` field (currently just `AddEndpointsApiExplorer`):

```fsharp
Services =
    spec.Services
    >> fun services ->
        // AddEndpointsApiExplorer is what populates ApiDescription.
        // It is independent of OpenAPI, which merely calls it too.
        services.AddEndpointsApiExplorer() |> ignore

        // FixedJsonHomeOptionsFactory makes IOptions<JsonHomeOptions>.Value the
        // same instance documentHandler already renders from -- no second,
        // independently-configured copy of this useJsonHome call's options.
        services.AddSingleton<IOptionsFactory<JsonHomeOptions>>(FixedJsonHomeOptionsFactory(options))
        |> ignore

        // Fails startup if two resources declare the same rel (#475) --
        // DuplicateRelValidator.Validate runs during Host.StartAsync, before
        // Kestrel (or any other IHostedService) starts serving.
        services.AddOptionsWithValidateOnStart<JsonHomeOptions>() |> ignore

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<JsonHomeOptions>, DuplicateRelValidator>())

        services
```

Needs `open Microsoft.Extensions.Options` and `open Microsoft.Extensions.DependencyInjection.Extensions` (for `TryAddEnumerable`) added to this file's opens -- check neither is already implicitly available before adding.

**Tests:** covered by Task 3's integration test -- this task's own correctness is "the DI graph resolves and the validator actually runs," which only an end-to-end host-start test proves.

**Verification:** `dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj` succeeds on all three TFMs. Existing `IntegrationTests.fs`/`JsonHomeDocumentTests.fs` tests still pass unmodified (this task adds services, doesn't remove or change any existing registration or behavior for the non-duplicate case).

---

### Task 3: End-to-end startup-failure test

**Files:** `test/Frank.JsonHome.Tests/IntegrationTests.fs`.

**Interfaces:**
- Consumes: Task 2's wiring, `IntegrationTests.fs`'s existing `createServer` harness (`IntegrationTests.fs:51-99`), which already builds a real `IHost` via `Host.CreateDefaultBuilder([||]).ConfigureWebHost(...).Build()` and calls `host.Start()` -- the exact point `IStartupValidator.Validate()` fires (during `Host.StartAsync`, before hosted services start). Reuse this harness rather than building a second one; `createServer` may need a small variant (e.g. an overload or a second helper) that returns the `IHost` itself instead of (or in addition to) `host.GetTestClient()`, since this test needs to assert on `host.Start()` throwing rather than on an HTTP response.

**Test to add**, mirroring the existing tests' structure:

1. Two `Resource` values whose `RelMetadata` (however the existing tests attach it -- check `ResourceBuilderExtensions.fsi`/existing `IntegrationTests.fs` resources for the pattern) both declare `Rel = "widget"`, different route templates.
2. Build the host the same way `createServer` does, through `useJsonHome`, with both resources registered.
3. Assert `host.Start()` throws (Expecto's `Expect.throwsT` or equivalent) -- for exactly one failing options type this is `OptionsValidationException`; assert the exception's message (or, if it's `OptionsValidationException`, its `Failures`) contains both route templates.
4. A second case: three resources, only two colliding -- same assertion, guards the end-to-end wiring against the same over-flagging risk Task 1's unit test already checks in isolation.

**Verification:** `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter IntegrationTests` passes on all three TFMs. Full suite (`dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`) passes with no regressions from Tasks 1-2.

---

### Task 4: Release notes

**Files:** `RELEASE_NOTES.md`.

**Change:** one entry noting `useJsonHome` now fails startup (rather than silently dropping a resource) when two resources declare the same `rel`, and that this is automatic -- no new public API, no opt-in required. Reference #475.

**Verification:** none beyond review -- documentation only.
