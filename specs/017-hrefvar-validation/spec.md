# Feature Specification: hrefVar / Route Template Validation

**Feature Branch**: `017-hrefvar-validation`
**Created**: 2026-08-08
**Status**: Draft
**Input**: GitHub issue #474 (deferred from #473) — `hrefVar` declarations are accepted without checking them against the route template they belong to. A typo (`hrefVar "prodId"` for a `/products/{id}` route) silently produces a `hrefVars` entry that resolves nothing; a template variable with no declaration is equally silent.

## Clarifications

### Session 2026-08-07/08 (brainstorming-copilot)

- Q: Analyzer (compile-time) vs log/throw at build-time (runtime) vs both? → A: Both — compile-time analyzer catches the common case at the declaration site; runtime check is a backstop for anything built dynamically.
- Q: Does #475 (application-start extension point) need to land first? → A: No. ASP.NET Core's native `AddOptionsWithValidateOnStart<T>()` (.NET 6+) is framework-native and requires no Frank core change — this issue has no dependency on #475's custom `StartupChecks` hook.
- Q: Analyzer severity? → A: `Severity.Error` (FRANK001/002 use `Warning`; a dangling/missing hrefVar silently breaks the served document, so this one is stricter).
- Q: Which mismatch directions are flagged? → A: Both — a template variable with no `hrefVar` declaration ("missing"), and a `hrefVar` declared with no matching template variable ("extra" — the motivating typo case).
- Q: How to register `JsonHomeOptions` in DI for `ValidateOnStart`? → A: Reuse the `IOptionsFactory<JsonHomeOptions>` #475 is independently adding (avoids `[<CLIMutable>]` and config-binding). #474's DI *wiring line* is therefore blocked on #475 landing; the validator class itself is not.
- Q: How does the analyzer reuse `UriTemplate.variables` without taking a `ProjectReference` (and its transitive `Microsoft.AspNetCore.App` framework reference) on `Frank.JsonHome`? → A: Link the source file directly (`<Compile Include>` with a relative path) into `Frank.Analyzers.fsproj`. Split the shared logic into a dependency-free `HrefVarValidation.fs` (linked into both projects) separate from the ASP.NET-dependent `HrefVarValidator.fs` (Frank.JsonHome only).
- Q: Where does the negative-case (mismatched hrefVar) demonstration live, given the startup check would crash a sample app that committed one? → A: Both tests (validator unit test + analyzer fixture) and the sample README (documented, not live in `Program.fs`).

### Session 2026-08-08 (correction)

- Q: Does `AddOptionsWithValidateOnStart<JsonHomeOptions>()` actually see the real, running application's resources? → A: No. Traced through `src/Frank/WebHostBuilder.fs:58-60`: the routing data source is only populated inside the `Configure` delegate, which runs when `GenericWebHostService.StartAsync` builds the pipeline — and `Host.StartAsync` runs options-validation *before* starting any `IHostedService`, `GenericWebHostService` included. The original mechanism would always see zero resources and silently report success regardless of real mismatches. Corrected to `IStartupFilter` (`services.AddSingleton<IStartupFilter, HrefVarStartupFilter>()`), which runs its check only after calling `next.Invoke(app)` — i.e. after the real pipeline, including routing, has been built. See research.md R1.
- Q: Does this reopen the #475 dependency for the DI-wiring task? → A: No — the opposite. `IStartupFilter` needs no `IOptions<JsonHomeOptions>` resolution at all, so the wiring task no longer needs #475's `IOptionsFactory<JsonHomeOptions>` work to exist first. It still touches the same function in the same file #475 is independently editing, so a textual merge conflict is still possible — but the hard functional dependency is gone.

## User Scenarios & Testing

### User Story 1 - Catch a typo'd hrefVar at compile time (Priority: P1)

As a Frank developer, I want the compiler/editor to flag a `hrefVar` declaration that doesn't match any variable in its resource's route template, so I catch the typo before running the app.

**Why this priority**: This is the fast-feedback path — it reports at the exact declaration site, costs nothing at runtime, and is the primary way developers will encounter this check day to day.

**Independent Test**: Write a resource with `resource "/products/{id}" { hrefVar "prodId" "..." }` (typo — should be `"id"`) and run `fsharp-analyzers` against the file; verify FRANK003 is reported.

**Acceptance Scenarios**:

1. **Given** a resource `resource "/products/{id}" { hrefVar "prodId" "..." }`, **When** the analyzer runs, **Then** it reports FRANK003 at the `hrefVar "prodId"` call site, because `prodId` matches no route template variable.
2. **Given** a resource `resource "/products/{id}" { get handler }` with no `hrefVar` declaration at all, **When** the analyzer runs, **Then** it reports FRANK003 at the `resource "/products/{id}"` call site, because `id` has no declaration.
3. **Given** a resource `resource "/products/{id}" { hrefVar "id" "..." }` where the declaration matches the template exactly, **When** the analyzer runs, **Then** no FRANK003 is reported.
4. **Given** a non-templated resource `resource "/products" { hrefVar "id" "..." }` (a `hrefVar` on a route with no `{...}` segments at all), **When** the analyzer runs, **Then** it reports FRANK003 — every declared name is "extra" when the template has zero variables.

---

### User Story 2 - Fail fast at startup for cases the analyzer can't see (Priority: P2)

As a Frank developer, I want my application to refuse to start if any resource's `hrefVar`s don't match its route template, so a mismatch built dynamically (not caught by the compile-time analyzer) can't silently ship a broken discovery document.

**Why this priority**: Backstop for the case the analyzer structurally cannot cover (metadata assembled outside a literal `resource "..." { hrefVar ... }` call site) and for anyone who hasn't wired the analyzer into their build. Depends on nothing new from #475 — `IStartupFilter` is framework-native, and (per the 2026-08-08 correction above) not on the Options pattern either.

**Independent Test**: Build an app whose resource set has a mismatched `hrefVar`, start the host, and verify it throws `HrefVarValidationException` before the server begins accepting requests — with a real `Host`/`TestServer`, not a hand-constructed provider, since the whole point of this check is being correctly positioned relative to when routing is actually built (see research.md R1 correction).

**Acceptance Scenarios**:

1. **Given** an application with a resource whose `hrefVar` doesn't match its route template, **When** the host starts, **Then** startup throws `HrefVarValidationException` listing every mismatch found (not just the first), before Kestrel accepts connections.
2. **Given** an application with no mismatches, **When** the host starts, **Then** startup succeeds and the JSON Home document serves normally.
3. **Given** an application with mismatches across multiple resources, **When** startup fails, **Then** the exception's failure messages identify every mismatched resource, not only one.

---

### Edge Cases

- A `hrefVar` declared on a resource whose route template has zero `{...}` segments (always "extra").
- A route template variable that appears more than once is only ever emitted once by `UriTemplate.variables` — matched against declared names as a set, not positionally.
- `resource productByIdResource` (referencing an already-built value by identifier, as used inside `webHost { }`) must NOT be mistaken by the analyzer for a `resource "<template>" { }` builder call — the string-literal-template pattern is what distinguishes them.
- Multiple resources sharing the same route template with different `hrefVar` sets (unusual, but each is checked independently).

## Requirements

### Functional Requirements

- **FR-001**: A new `Frank.Analyzers` rule (FRANK003, `Severity.Error`) MUST flag a `hrefVar "name" "uri"` declaration inside a `resource "<template>" { }` call whose `name` does not appear as a `{name}` segment in `<template>`.
- **FR-002**: FRANK003 MUST also flag a route template variable that has no corresponding `hrefVar` declaration in the same `resource { }` body.
- **FR-003**: The analyzer MUST NOT flag `resource <identifier> { }` call sites (an already-built `Resource`/`ResourceSpec` value passed by name, e.g. inside `webHost { }`) — only `resource "<string-literal>" { }` builder call sites are checked.
- **FR-004**: `Frank.JsonHome` MUST provide an `IStartupFilter` implementation that performs the same check once the application's request pipeline (including routing) has been built, using `IApiDescriptionGroupCollectionProvider` (already registered via `AddEndpointsApiExplorer`) to enumerate the real, running application's resources.
- **FR-005**: The startup filter MUST be registered via `services.AddSingleton<IStartupFilter, HrefVarStartupFilter>()`, requiring no new Frank core extension point and no dependency on the Options pattern.
- **FR-006**: Both the analyzer and the startup filter MUST call the same shared, pure diff function — the comparison logic MUST NOT be implemented twice.
- **FR-007**: The startup filter MUST aggregate every mismatch across every resource into a single failure (via `HrefVarValidationException` carrying every message), not just the first one found.
- **FR-008**: The DI registration wiring the startup filter into `useJsonHome` (`WebHostBuilderExtensions.fs`) touches the same function #475 is independently editing (for unrelated reasons) — sequence the actual landing to avoid a textual merge conflict, but this is no longer a functional dependency (see 2026-08-08 correction above).

### Key Entities

- **Mismatch**: The result of comparing a route template's variables against a resource's declared `hrefVar` names — a `Missing` list (template variables with no declaration) and an `Extra` list (declared names with no matching template variable).
- **HrefVarStartupFilter**: The `IStartupFilter` implementation that runs the check against the real, running application's `ApiSurface` once the request pipeline (including routing) has been built, but before the server starts accepting connections.
- **HrefVarAnalyzer**: The `Frank.Analyzers` rule (FRANK003) that runs the same check against literal `resource "<template>" { hrefVar ... }` AST call sites at compile time.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The motivating typo (`hrefVar "prodId"` on `/products/{id}`) is reported by the analyzer with the file and line of the `hrefVar` call.
- **SC-002**: An application with any hrefVar/template mismatch fails to start (via `HrefVarStartupFilter` throwing during pipeline construction, before Kestrel accepts connections) rather than serving an incomplete `/.well-known/home.json`.
- **SC-003**: An application with no mismatches (e.g. the existing `Frank.JsonHome.Sample`) is unaffected — starts and serves exactly as before.
- **SC-004**: The diff logic exists in exactly one place (`HrefVarValidation.diff`), consumed by both the analyzer and the runtime validator.
