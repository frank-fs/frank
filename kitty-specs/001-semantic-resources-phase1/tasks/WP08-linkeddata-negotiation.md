---
work_package_id: "WP08"
subtasks:
  - "T039"
  - "T040"
  - "T041"
  - "T042"
  - "T043"
  - "T044"
title: "Frank.LinkedData — Content Negotiation"
phase: "Phase 2 - LinkedData"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP07"]
requirement_refs: ["FR-016", "FR-017", "FR-018", "FR-019"]
history:
  - timestamp: "2026-03-04T22:10:13Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# WP08: Frank.LinkedData — Content Negotiation

> **Review Feedback Status**: No review feedback yet.

## Review Feedback

_No feedback recorded._

> **Markdown Formatting Note**: Use ATX headings (`#`), fenced code blocks with language tags, and standard bullet lists. Do not use HTML tags or custom directives.

## Implementation Command

```
spec-kitty implement WP08 --base WP07
```

## Objectives & Success Criteria

Implement three ASP.NET Core output formatters (JSON-LD, Turtle, RDF/XML) and wire them into Frank's resource builder computation expression and WebHostBuilder extensions, following the same `[<AutoOpen>]` and `CustomOperation` patterns used by `Frank.Auth` and `Frank.OpenApi`.

Success criteria:
- A resource annotated with `linkedData` in the resource builder CE returns JSON-LD when `Accept: application/ld+json` is sent
- The same resource returns Turtle for `Accept: text/turtle` and RDF/XML for `Accept: application/rdf+xml`
- A resource NOT annotated with `linkedData` is unaffected — it returns the same response as before this WP (zero behavioral change, FR-019)
- An application that has `useLinkedData` in the WebHostBuilder but no embedded resources fails at startup with a descriptive error (not a runtime NullReferenceException)
- All existing Frank tests pass unchanged

## Context & Constraints

- Follow the exact `[<AutoOpen>]` + `type X with [<CustomOperation(...)>]` pattern from:
  - `src/Frank.Auth/ResourceBuilderExtensions.fs`
  - `src/Frank.OpenApi/ResourceBuilderExtensions.fs`
- Use `ResourceBuilder.AddMetadata` (or the equivalent pattern from those files) to attach a marker type to endpoint metadata — do not modify `ResourceSpec` or `ResourceBuilder` core types
- Reference `src/Frank/ContentNegotiation.fs` to understand how Frank currently selects a response format, so the semantic formatters integrate without conflicting
- Reference `src/Frank/Builder.fs` for `ResourceSpec` and `ResourceBuilder` definitions
- The three formatters must be added to `MvcOptions.OutputFormatters` AFTER the default formatters to avoid intercepting `application/json` requests
- `LinkedDataConfig` is loaded once at startup and stored as a singleton in the DI container; formatters resolve it via `IServiceProvider` / constructor injection
- Multi-target: `Frank.LinkedData` targets net8.0, net9.0, net10.0 — all formatter and extension APIs used must be available across all three TFMs

## Subtasks & Detailed Guidance

### T039: JsonLdFormatter.fs

Module: `Frank.LinkedData.Formatters.JsonLdFormatter`

```fsharp
type JsonLdFormatter(config: LinkedDataConfig) =
    inherit OutputFormatter()
```

- `SupportedMediaTypes`: add `"application/ld+json"`
- Override `CanWriteResult`: return `true` only when the endpoint metadata contains `LinkedDataMarker` (see T042) AND the selected media type is `application/ld+json`
- Override `WriteResponseBodyAsync`:
  1. Retrieve the handler return value from `OutputFormatterWriteContext.Object`
  2. Call `InstanceProjector.project config.OntologyGraph resourceUri instance` where `resourceUri` is derived from `HttpContext.Request.Path` (combine with `config.BaseUri`)
  3. Serialise the resulting `IGraph` using dotNetRdf's `JsonLdWriter` to a `MemoryStream`, then copy to `HttpContext.Response.Body`
  4. Set `Content-Type: application/ld+json` on the response
- Register via constructor injection: `LinkedDataConfig` is resolved from DI

### T040: TurtleFormatter.fs

Module: `Frank.LinkedData.Formatters.TurtleFormatter`

Same structure as `JsonLdFormatter`:
- `SupportedMediaTypes`: `"text/turtle"`
- `CanWriteResult`: endpoint has `LinkedDataMarker` AND media type is `text/turtle`
- `WriteResponseBodyAsync`: project instance to `IGraph`, write with `CompressingTurtleWriter`, set `Content-Type: text/turtle; charset=utf-8`

Use `CompressingTurtleWriter` with default settings (base URI compression enabled).

### T041: RdfXmlFormatter.fs

Module: `Frank.LinkedData.Formatters.RdfXmlFormatter`

Same structure:
- `SupportedMediaTypes`: `"application/rdf+xml"`
- `CanWriteResult`: endpoint has `LinkedDataMarker` AND media type is `application/rdf+xml`
- `WriteResponseBodyAsync`: project instance to `IGraph`, write with `PrettyRdfXmlWriter`, set `Content-Type: application/rdf+xml; charset=utf-8`

### T042: ResourceBuilderExtensions.fs

Module: `Frank.LinkedData.ResourceBuilderExtensions`

```fsharp
/// Marker placed in endpoint metadata to signal that this resource supports LinkedData content negotiation.
type LinkedDataMarker = LinkedDataMarker

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        [<CustomOperation("linkedData")>]
        member _.LinkedData(spec: ResourceSpec) : ResourceSpec =
            // Add LinkedDataMarker to endpoint metadata using the same pattern
            // as Frank.Auth.ResourceBuilderExtensions or Frank.OpenApi.ResourceBuilderExtensions
            ...
```

Read `src/Frank.Auth/ResourceBuilderExtensions.fs` carefully to reproduce the exact mechanism for adding metadata (the `AddMetadata` helper or equivalent). Do not invent a new pattern; replicate what already exists.

The `LinkedDataMarker` type must be in the `Frank.LinkedData` namespace so consuming code can reference it for testing (e.g., `endpointMetadata.GetMetadata<LinkedDataMarker>() <> null`).

### T043: WebHostBuilderExtensions.fs

Module: `Frank.LinkedData.WebHostBuilderExtensions`

```fsharp
[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with
        [<CustomOperation("useLinkedData")>]
        member _.UseLinkedData(spec: WebHostSpec) : WebHostSpec =
            ...
```

What `useLinkedData` must do when the application starts:
1. Register `LinkedDataConfig` as a singleton: call `loadConfig (Assembly.GetEntryAssembly())` during `IServiceCollection` configuration; if the result is `Result.Error msg`, throw `InvalidOperationException(msg)` so the application fails at startup with a clear message
2. Register the three formatters: add `JsonLdFormatter`, `TurtleFormatter`, `RdfXmlFormatter` to `IServiceCollection` as transient services (constructor injection of `LinkedDataConfig`)
3. Configure `MvcOptions`: retrieve the formatter list and append the three formatters AFTER the existing formatters — use `Add` (not `Insert(0, ...)`)
4. Startup validation: after registering services, inspect all endpoints in the application's `EndpointDataSource`; if any endpoint has `LinkedDataMarker` metadata but `LinkedDataConfig` loading returned an error, throw `InvalidOperationException` with a message naming the affected endpoint

Follow the same `WebHostBuilder with [<CustomOperation>]` pattern used by `Frank.OpenApi` and `Frank.Auth` webhost extensions.

### T044: Tests

Location: `test/Frank.LinkedData.Tests/`

**Formatter unit tests** (one per formatter):

For each of JSON-LD, Turtle, and RDF/XML:
- Construct a minimal `IGraph` with two triples
- Invoke the formatter's `WriteResponseBodyAsync` via a mock `OutputFormatterWriteContext` (use `DefaultHttpContext` for the response)
- Capture the response body bytes and parse them back using the corresponding dotNetRdf parser
- Assert the round-tripped graph contains the original two triples

**Content negotiation integration tests** (use `Microsoft.AspNetCore.TestHost`):

Setup:
- Build a `TestHost` with a minimal Frank app that has one resource annotated with `linkedData` and one resource without it
- Register `useLinkedData` in the WebHostBuilder
- Embed three minimal test resources (ontology, shapes, manifest) in the test assembly

Tests:
- `"Accept: application/ld+json returns JSON-LD for linkedData resource"` — verify response `Content-Type` is `application/ld+json` and body is valid JSON-LD
- `"Accept: text/turtle returns Turtle for linkedData resource"` — verify `Content-Type: text/turtle` and parseable Turtle body
- `"Accept: application/rdf+xml returns RDF/XML for linkedData resource"` — verify `Content-Type: application/rdf+xml` and parseable XML
- `"Accept: text/html returns standard handler response for linkedData resource"` — verify the standard handler result is returned (not a LinkedData format)
- `"Non-linkedData resource is unaffected by all Accept headers"` — send all three RDF Accept headers to the non-annotated resource and verify a non-RDF response is returned each time
- `"useLinkedData without embedded resources throws at startup"` — build a `TestHost` with `useLinkedData` but no embedded resources in the test assembly; verify `InvalidOperationException` is thrown during host startup, not during request handling

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Formatter ordering: semantic formatters may intercept `application/json` requests if added incorrectly | The `CanWriteResult` override checks for `LinkedDataMarker` in endpoint metadata; resources without the marker are never handled by these formatters regardless of ordering |
| `OutputFormatter.CanWriteResult` API signature differs between ASP.NET Core 8 and 10 | Confirm the base class API across all three TFMs before writing; use `#if NET10_0_OR_GREATER` conditional compilation only if strictly necessary |
| `Assembly.GetEntryAssembly()` returns `null` in some test host scenarios | In `useLinkedData`, accept an optional `Assembly` parameter (default `null`); if null, use `Assembly.GetEntryAssembly()` and warn if that is also null rather than crashing |
| dotNetRdf `JsonLdWriter` may require additional NuGet packages not in `dotNetRdf.Core` | Check the dotNetRdf 3.5.1 package split; if JSON-LD writing is in a separate package, add it to `Frank.LinkedData.fsproj` and document the dependency |
| Startup validation (step 4 in T043) requires iterating endpoints before the middleware pipeline is fully built | Defer validation to `IApplicationBuilder.Use` or `IHostedService.StartAsync` rather than `IServiceCollection` configuration time; test this with the TestHost integration test |

## Review Guidance

- Run `dotnet test test/Frank.LinkedData.Tests/` on all three TFMs: `dotnet test -f net8.0`, `dotnet test -f net9.0`, `dotnet test -f net10.0` — all must pass
- Run the full Frank test suite (`dotnet test Frank.sln`) and confirm zero regressions
- Manually test all Accept header variations against a running `Frank.LinkedData.Sample`
- Verify that a resource without `linkedData` in its builder returns identical responses before and after `useLinkedData` is added to the WebHostBuilder
- Confirm the startup-failure message is human-readable and names the missing resource file(s)
- Check that no `application/json` request is accidentally intercepted by the RDF formatters (send a plain JSON request to a `linkedData` resource and verify the standard JSON response is returned)

## Activity Log

| Timestamp | Lane | Agent | Action |
|---|---|---|---|
| 2026-03-04T22:10:13Z | planned | system | Prompt generated via /spec-kitty.tasks |
