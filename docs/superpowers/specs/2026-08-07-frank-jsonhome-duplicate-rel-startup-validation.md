# Frank.JsonHome: duplicate-`rel` startup validation via `AddOptionsWithValidateOnStart`

**Date**: 2026-08-07
**Branch**: not yet created — implementation needs a worktree/branch off `master`
**Status**: Draft — ready for implementation

## Context

frank-fs/frank#475. Two resources may declare the same JSON Home link relation type. `resources` in the served document is a JSON object keyed by `Rel`, so the later entry silently overwrites the earlier one and a resource vanishes from the home document with no diagnostic. `JsonHome.fs`'s `writeDocument` already flags this inline:

> "ApiSurface groups by route template, not by rel, so two resources that declare the same rel are not merged upstream — both are written here as separate object entries under the identical key. Most JSON parsers resolve that to whichever entry comes last, but nothing in this pipeline currently detects or rejects the duplication at startup; that is tracked separately (#475)." (`JsonHome.fs:163-168`)

#475's original framing proposed a new Frank-core extension point — `WebHostSpec.StartupChecks: (IServiceProvider -> unit) list`, run after `Build()` and before `Run()` — because "`WebHostBuilder.Run` builds and blocks, and Frank exposes no hook that runs once at application start."

## Reframing: no new Frank-core mechanism needed

ASP.NET Core (.NET 8+) already ships this exact mechanism: `OptionsBuilder<T>.ValidateOnStart()` / `AddOptionsWithValidateOnStart<T>()`. Verified against three independent sources during design:

- **Mechanism and API shape** — [Options pattern - .NET | Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/core/extensions/options): `IValidateOptions<T>.Validate(name, options)` returns `ValidateOptionsResult.Fail(string)`; accessed via `IOptions<T>.Value` it throws `OptionsValidationException` (carries `Failures: IEnumerable<string>`). `AddOptionsWithValidateOnStart<T>()` is the documented way to force this at boot rather than on first use.
- **Timing** — [adnanrafiq.com: Validate Options\<T\> on Startup in Hosted Services in .NET8](https://adnanrafiq.com/blog/validation-options-on-startup-in-hosted-services-in-net8/): `Host.StartAsync()` resolves hosted services (`Services.GetRequiredService<IEnumerable<IHostedService>>()`), which is what triggers eager validation — before any hosted service (including Kestrel) starts, so a failing validator blocks the app before it accepts a single request.
- **Aggregation across multiple failing options types** — [dotnet/runtime#102061](https://github.com/dotnet/runtime/issues/102061): "If more than one options type fails, StartupValidator collects the exceptions and rethrows them as an AggregateException, so you see every broken section in one log line" — exactly the "one bad start reports every problem" behavior #475 asked for.

`WebHostBuilder.Run` (`src/Frank/WebHostBuilder.fs:70`, `configured.Build().Run()`) already goes through `Host.CreateDefaultBuilder(args)` — the same generic-host pipeline `ValidateOnStart` hooks into. No change to `WebHostBuilder.fs` or `WebHostSpec` is needed.

**Decision**: #475 is reframed from "build a Frank-core startup-check extension point" to "adopt and document `AddOptionsWithValidateOnStart<T>` as Frank's convention for extension packages that need to fail fast at boot," with JsonHome's duplicate-`rel` check as the first (and, for this branch, only) consumer. No new GitHub issue — this spec and its plan track directly against #475; #475 stays open, re-scoped, until this lands.

Every extension package #475 named as a future consumer (`Frank.OpenApi` dup operation ids, `Frank.Auth` policies referenced but never registered) can adopt the same convention later, independently — nothing here blocks them, and nothing here builds infrastructure on their behalf.

## The check

`JsonHome.documentHandler` (`JsonHome.fs:187-205`) already computes exactly the data needed, per-request: it resolves `IApiDescriptionGroupCollectionProvider` from `ctx.RequestServices` and projects it through `ApiSurface.ofApiDescriptions` to get `ResourceDescription list`. That provider is equally resolvable from the *root* `IHost.Services` after `Build()` — `spec.Endpoints` (and the `ResourceEndpointDataSource` built from it) is fixed during `webHost { }` CE composition, before `Build()` ever runs, so the endpoint surface `ApiDescriptionGroupCollectionProvider` reflects is already complete once the host exists. No request needs to be in flight.

Startup validation runs before any `HttpContext` exists, so it necessarily sees the same *unfiltered* `ResourceDescription list` `AuthorizationFilter.apply` would later filter per-principal — matching today's actual bug (the later-wins collision already happens on the unfiltered list, before authorization is ever evaluated).

```fsharp
// DuplicateRelValidator.fs
type internal DuplicateRelValidator(provider: IApiDescriptionGroupCollectionProvider) =
    interface IValidateOptions<JsonHomeOptions> with
        member _.Validate(_name, _options) =
            let resources =
                provider.ApiDescriptionGroups.Items
                |> Seq.collect (fun g -> g.Items)
                |> ApiSurface.ofApiDescriptions

            let failures =
                resources
                |> List.groupBy (fun r -> r.Rel)
                |> List.choose (fun (rel, group) ->
                    match group with
                    | [] | [ _ ] -> None
                    | duplicates ->
                        let routes = duplicates |> List.map (fun r -> r.Href) |> List.distinct |> String.concat ", "
                        Some $"duplicate JSON Home rel '%s{rel}': %s{routes}")

            match failures with
            | [] -> ValidateOptionsResult.Success
            | fs -> ValidateOptionsResult.Fail(fs: string list)
```

Failure messages name the colliding route templates (`ResourceDescription.Href`), not just the `rel` — e.g. `"duplicate JSON Home rel 'widget': /widgets, /widgets/{id}/gadgets"`. A bare rel name is enough to locate the bug in a small app; naming the routes is what makes the message actionable without also reading the source.

## Reusing `JsonHomeOptions` as the validated type

Decision: `IValidateOptions<JsonHomeOptions>`, not a dedicated marker type — despite the check having nothing to do with `JsonHomeOptions`'s own fields (`Path`/`Rel`/`Title`/`Links`). Precedented by Microsoft's own docs example (`ValidateSettingsOptions(IConfiguration config)`, which also ignores its bound value and validates via an injected dependency instead). The `Validate` method's `options` parameter is unused; `DuplicateRelValidator` gets everything it needs from the constructor-injected `IApiDescriptionGroupCollectionProvider`.

This creates one piece of friction: `JsonHomeOptions` is an immutable F# record with no `[<CLIMutable>]` and no parameterless constructor, but the Options system's default `IOptionsFactory<T>` calls `Activator.CreateInstance<T>()` then applies `IConfigureOptions<T>.Configure(Action<T>)` delegates in place — a shape that doesn't fit an immutable record without adding `[<CLIMutable>]` (which would let `JsonHomeOptions` be constructed two different ways: the closure-captured value `install` actually uses to render the document, and a separately-constructed DI-bound copy for the Options system — two representations of the same configuration, only one of which is real).

Resolution: a custom `IOptionsFactory<JsonHomeOptions>` singleton that returns the literal closure-captured `options` value unconditionally, bypassing `Activator`/`Configure` entirely:

```fsharp
// DuplicateRelValidator.fs
type internal FixedJsonHomeOptionsFactory(value: JsonHomeOptions) =
    interface IOptionsFactory<JsonHomeOptions> with
        member _.Create(_name) = value
```

`IOptions<JsonHomeOptions>.Value` (were anything ever to resolve it) and `documentHandler`'s `homeOptions` are then provably the same instance — no drift possible, no `[<CLIMutable>]` needed, the record stays genuinely immutable.

## Wiring: `useJsonHome` auto-registers the check

`install` (`WebHostBuilderExtensions.fs:10-30`) is the one place `useJsonHome`'s `options` value is in scope, and it already runs exactly once per `useJsonHome` call. The check is wired there — callers get it for free; `useJsonHome` alone provides both the document endpoint and the startup validation, no separate opt-in step:

```fsharp
Services =
    spec.Services
    >> fun services ->
        services.AddEndpointsApiExplorer() |> ignore
        services.AddSingleton<IOptionsFactory<JsonHomeOptions>>(FixedJsonHomeOptionsFactory(options)) |> ignore
        services.AddOptionsWithValidateOnStart<JsonHomeOptions>() |> ignore
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<JsonHomeOptions>, DuplicateRelValidator>())
        services
```

## New file

`DuplicateRelValidator.fs` + `DuplicateRelValidator.fsi`, `src/Frank.JsonHome/`, both types `internal` (referenced only from `WebHostBuilderExtensions.fs` within the same assembly — per `CLAUDE.md`'s `.fsi` convention, internal members used by another file in the assembly stay in the signature, marked `internal` in both files).

## Deferred / explicitly out of scope

- **`Frank.OpenApi` duplicate operation ids, `Frank.Auth` unregistered policies.** #475 named these as future consumers of the same convention. Not built here — nothing in this design blocks them, but they're separate work, separate packages, separate branches.
- **A generic Frank-core `StartupChecks` extension point.** Explicitly not built — the reframing's whole point is that `AddOptionsWithValidateOnStart<T>` already *is* that extension point, supplied by the framework Frank already builds on.
- **Sample demonstration.** `sample/Frank.JsonHome.Sample` is not modified to include a deliberately duplicated `rel` — that would mean shipping a broken sample on purpose. The check is exercised by tests only (see Verification).

## Verification

- `Frank.JsonHome.Tests`: unit coverage for `DuplicateRelValidator.Validate` directly (no duplicates → `Success`; two resources sharing a `rel` → `Fail` with both route templates named in the message).
- Integration test (mirroring `IntegrationTests.fs`'s existing `createServer` harness, which already builds a real `IHost` via `Host.CreateDefaultBuilder([||])...Build()` and calls `host.Start()`): register two resources with the same `rel` through `useJsonHome`, assert `host.Start()` throws (`OptionsValidationException` for the single-failure case).
- Build across all three TFMs (`net8.0;net9.0;net10.0`) per project convention.
