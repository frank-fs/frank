# Research: hrefVar / Route Template Validation

**Feature Branch**: `017-hrefvar-validation`
**Date**: 2026-08-08

## R1: Startup validation mechanism

### 2026-08-08 correction — `AddOptionsWithValidateOnStart` fires too early; superseded by `IStartupFilter`

**The original decision below is wrong and was never implemented.** Traced through Frank's actual source (`src/Frank/WebHostBuilder.fs:58-60`): `ResourceEndpointDataSource` — the data source `IApiDescriptionGroupCollectionProvider` reads from — is only added to `endpoints.DataSources` **inside** the `.Configure(fun app -> ...)` delegate, which runs when `GenericWebHostService.StartAsync` builds the request pipeline. `Microsoft.Extensions.Hosting`'s `Host.StartAsync` runs `IStartupValidator.Validate()` (the mechanism behind `AddOptionsWithValidateOnStart`) explicitly *before* iterating and starting any `IHostedService` — `GenericWebHostService` included. So at the moment `IValidateOptions<JsonHomeOptions>.Validate` would run, the routing table is still empty: `ApiSurface.ofApiDescriptions` returns `[]`, the check always reports success, and a real mismatch is never caught. Silent, exactly the failure class this feature exists to prevent — worse than doing nothing, because it looks like protection.

This was caught by the user, not verified by this session before being designed in: a foundational timing claim went into the plan on user-supplied-but-"confirmed elsewhere" citations (still true as far as they went — the validator does run before Kestrel *accepts connections* — but that's a necessary, not sufficient, condition; nothing confirmed it also runs after routing is *built*, which is the actually load-bearing fact here). Corrected via `IStartupFilter` — see the new decision below. Not independently re-verified against .NET source this session either (recollection of `Host.StartAsync` internals, not a fresh read) — flagged as a residual gap; T004's rewritten test (`HrefVarStartupFilterTests.fs`) is designed to empirically prove the fix through a real `Host.Build().Start()` sequence rather than a hand-constructed provider, closing exactly this kind of blind spot going forward.

**New decision**: `IStartupFilter`, registered via `services.AddSingleton<IStartupFilter, HrefVarStartupFilter>()`.

**Rationale**: `IStartupFilter.Configure(next: Action<IApplicationBuilder>) : Action<IApplicationBuilder>` returns an action that itself becomes part of the pipeline-construction sequence. Calling `next.Invoke(app)` first lets the rest of the pipeline — including the app's own `Configure`/`UseEndpoints`, which is where `ResourceEndpointDataSource` gets populated — build completely; only once that call returns does the routing table (and therefore `IApiDescriptionGroupCollectionProvider`) reflect the real, final set of resources. Checking after `next.Invoke(app)` returns is correct regardless of how many other `IStartupFilter`s are also registered, since `next` always resolves through to the real `Configure` eventually — no assumption about filter registration order is needed. Throwing from inside that action aborts pipeline construction inside `GenericWebHostService.StartAsync`, before the server (Kestrel/TestServer) is ever started — so the app still fails to start, just correctly positioned relative to routing.

**Side effect**: `IStartupFilter` needs nothing from the Options pattern — no `AddOptionsWithValidateOnStart`, no `IOptions<JsonHomeOptions>` resolution at all. This removes the dependency on #475's `IOptionsFactory<JsonHomeOptions>` work entirely; the DI-wiring task (plan.md T010) is no longer functionally blocked, only a soft merge-collision risk (same function, `WebHostBuilderExtensions.fs`'s `install`, that #475 is independently editing).

**FYI, not in scope for this issue**: #475 was independently also converging on `AddOptionsWithValidateOnStart` for its own duplicate-`rel` check. If that check similarly reads `IApiDescriptionGroupCollectionProvider`/`ApiSurface` data, it likely has the exact same timing bug. Flagging for awareness only — #474 stays scoped to its own fix, per the earlier "scope to #474" decision.

**Alternatives considered** (superseding the original two below): Custom `WebHostSpec.StartupChecks` hook (#475's proposal) — still not needed; `IStartupFilter` is equally framework-native and correctly positioned. `Middleware` that checks on first request — rejected: defers the failure to whenever the first request happens to arrive, not guaranteed at startup, and could pass silently in an app with no traffic yet.

---

### Original decision (superseded, kept for record)

**Decision**: ~~`services.AddOptionsWithValidateOnStart<JsonHomeOptions>()` + a DI-resolved `IValidateOptions<JsonHomeOptions>` implementation.~~

**Rationale**: ASP.NET Core's Options pattern (.NET 6+) runs an `IStartupValidator` hosted service during `Host.StartAsync()` — before Kestrel begins accepting connections — that resolves every options type registered with `ValidateOnStart()` and invokes its `IValidateOptions<T>` validators. `IValidateOptions<T>` implementations are constructed via DI, so a validator can take constructor dependencies (here, `IApiDescriptionGroupCollectionProvider`) beyond the options value itself. This requires zero Frank core changes and has no dependency on #475's proposed `WebHostSpec.StartupChecks` hook.

**Sources** (supplied by user during brainstorming; not independently re-verified this session — user confirmed "elsewhere"; the timing claims here are still literally true, just insufficient for this use case — see correction above):
- Options pattern - .NET | Microsoft Learn — https://learn.microsoft.com/en-us/dotnet/core/extensions/options — documents `AddOptions<T>().Validate(...).ValidateOnStart()` / `AddOptionsWithValidateOnStart<T>()`, and that a failing `IValidateOptions<T>.Validate` returning `ValidateOptionsResult.Fail(string)` surfaces as `OptionsValidationException` with a `.Failures` collection on access.
- adnanrafiq.com — https://adnanrafiq.com/blog/validation-options-on-startup-in-hosted-services-in-net8/ — traces the trigger: `Host.StartAsync()` resolves `IEnumerable<IHostedService>` before running any hosted service's own `StartAsync`, and that resolution is what causes eager validation to fire — before Kestrel accepts connections. Per this source, plain `ValidateOnStart()` needs something to actually touch `IOptions<T>.Value` to fire; `AddOptionsWithValidateOnStart<T>()` is the variant that doesn't require an incidental trigger, and is the one this feature uses.
- dotnet/runtime#102061 — https://github.com/dotnet/runtime/issues/102061 — confirms multiple failing options types aggregate into a single `AggregateException`, so failures across independent validators (e.g. this feature's `HrefVarValidator` alongside any other `IValidateOptions<T>` an app registers) are reported together, not one-at-a-time across repeated restarts.

**Alternatives considered**:
- Custom `WebHostSpec.StartupChecks: (IServiceProvider -> unit) list` (as proposed in #475) → Rejected for this feature: adds a Frank-core dependency this issue doesn't need, since the framework-native mechanism already does the job.
- Log via `ILogger` and serve anyway → Rejected: matches the spec's "partial hrefVars still conforms" language, but gives the weakest guarantee (logs are easy to miss); superseded once the startup-throw option was confirmed free of the #475 dependency.

## R2: Sharing the diff logic between analyzer and runtime validator without a `ProjectReference`

**Decision**: A dependency-free file (`HrefVarValidation.fs`/`.fsi` in `Frank.JsonHome`, containing only the `Mismatch` type, `diff`, and `UriTemplate`'s existing pure functions) is compiled into `Frank.JsonHome.fsproj` normally and **linked** (`<Compile Include>` with a relative path, no `ProjectReference`) into `Frank.Analyzers.fsproj`.

**Rationale**: `Frank.Analyzers.fsproj` currently has zero project references — it operates purely on syntax. Taking a `ProjectReference` on `Frank.JsonHome.fsproj` would transitively pull in `Microsoft.AspNetCore.App`, a framework reference no other file in the analyzer package needs. Linking the source file directly gives genuine single-implementation reuse (literally the same file, compiled into two assemblies) without that dependency edge, at the cost of `Frank.JsonHome.HrefVarValidation`'s namespace also appearing (harmlessly) inside `Frank.Analyzers.dll`.

**Alternatives considered**:
- `ProjectReference` on `Frank.JsonHome.fsproj` → Rejected: unwanted framework-reference edge on a previously dependency-free package.
- Hand-duplicate the ~15-line regex extraction in the analyzer → Rejected: two copies of the same logic can drift; explicitly ruled out during brainstorming ("do not write the diff logic twice").

**Note**: No existing `.fsproj` in this repo uses cross-project `<Compile Include>` (checked via `grep 'Compile Include="\.\./'` across all `.fsproj` files — no matches). This is a new pattern for the repo, not an established one.
