# Research: OpenAPI Document Generation Support

**Feature Branch**: `016-openapi`
**Date**: 2026-02-09

## R1: Target Framework for Frank.OpenApi

**Decision**: Target `net9.0;net10.0` (not net8.0)

**Rationale**: `Microsoft.AspNetCore.OpenApi` — the package providing `AddOpenApi()`, `MapOpenApi()`, and `IOpenApiSchemaTransformer` — was introduced in .NET 9. It has no backport to .NET 8. Since Frank.OpenApi depends on this package for its core functionality, it cannot target net8.0.

**Alternatives considered**:
- Target net8.0 with a shim or polyfill for OpenAPI support → Rejected: would require re-implementing OpenAPI document generation, defeating the purpose of using ASP.NET Core's built-in support
- Target net8.0 with Swashbuckle as fallback → Rejected: Swashbuckle is deprecated and maintenance-mode; would add a second code path
- Drop net8.0 from Frank core → Out of scope; core Frank supports net8.0 and this extension simply doesn't apply to it

## R2: OpenAPI Metadata Discovery Mechanism

**Decision**: Use standard ASP.NET Core endpoint metadata types with the built-in `EndpointMetadataApiDescriptionProvider`; no custom `IApiDescriptionProvider` needed

**Rationale**: Frank's `ResourceSpec.Build()` creates `RouteEndpoint` instances with `HttpMethodMetadata` already attached. The built-in `EndpointMetadataApiDescriptionProvider` discovers any `RouteEndpoint` with `IHttpMethodMetadata`. By adding additional metadata types (`ProducesResponseTypeMetadata`, `AcceptsMetadata`, `EndpointNameMetadata`, `TagsMetadata`, `EndpointDescriptionMetadata`) via the existing `ResourceSpec.Metadata` extensibility point, the OpenAPI infrastructure will read them automatically.

**Alternatives considered**:
- Custom `IApiDescriptionProvider` → Rejected: adds complexity and duplicates built-in functionality
- Rely on handler reflection for type inference → Not feasible: Frank handlers are `RequestDelegate` (takes `HttpContext`, returns `Task`), so the API Explorer cannot infer typed parameters or responses from reflection

## R3: Endpoint Metadata Types Required

**Decision**: Use the following ASP.NET Core metadata types (all in the framework reference, no extra NuGet):

| OpenAPI Field | Metadata Type | Added Via |
|---|---|---|
| `operationId` | `EndpointNameMetadata` (implements `IEndpointNameMetadata`) | `builder.Metadata.Add(EndpointNameMetadata(name))` |
| `summary` | `EndpointSummaryMetadata` (implements `IEndpointSummaryMetadata`) | `builder.Metadata.Add(EndpointSummaryMetadata(summary))` |
| `description` | `EndpointDescriptionMetadata` (implements `IEndpointDescriptionMetadata`) | `builder.Metadata.Add(EndpointDescriptionMetadata(desc))` |
| `tags` | `TagsMetadata` (implements `ITagsMetadata`) | `builder.Metadata.Add(TagsMetadata(tags))` |
| `responses` | `ProducesResponseTypeMetadata` (implements `IProducesResponseTypeMetadata`) | `builder.Metadata.Add(ProducesResponseTypeMetadata(type, statusCode, contentTypes))` |
| `requestBody` | `AcceptsMetadata` (implements `IAcceptsMetadata`) | `builder.Metadata.Add(AcceptsMetadata(type, contentTypes))` |

**Rationale**: These are the exact types the `EndpointMetadataApiDescriptionProvider` reads. Using them ensures the OpenAPI document generator produces correct output without custom transformers for metadata (schema transformers are still needed for F# types).

## R4: FSharp.Data.JsonSchema.OpenApi Integration

**Decision**: Take a NuGet dependency on `FSharp.Data.JsonSchema.OpenApi` (3.0.0); wire `FSharpSchemaTransformer` into `OpenApiOptions.AddSchemaTransformer` via the `useOpenApi` convenience operation

**Rationale**: FSharp.Data.JsonSchema.OpenApi already provides:
- `FSharpSchemaTransformer` implementing `IOpenApiSchemaTransformer`
- Handles F# records, discriminated unions (all encoding styles), option types, collections, recursive types
- Targets net9.0 and net10.0 with conditional compilation for Microsoft.OpenApi v1 vs v2
- Depends on FSharp.Data.JsonSchema.Core for the type analysis IR

**Alternatives considered**:
- Inline schema generation logic → Rejected: duplicates FSharp.Data.JsonSchema.Core's type analysis
- Use only the default System.Text.Json JsonSchemaExporter → Rejected: does not understand DUs, option types, or F#-specific patterns

## R5: HandlerBuilder CE Design

**Decision**: Implement `HandlerBuilder` as a standard F# computation expression that produces `HandlerDefinition` records. The builder uses `Yield`/`Run` with `[<CustomOperation>]` members for metadata. The `handle` operation sets the request handler function.

**Rationale**: Follows the established Frank CE pattern (ResourceBuilder, WebHostBuilder). The HandlerBuilder accumulates metadata into a `HandlerDefinition` record, which is then consumed by ResourceBuilder type extensions.

**Design constraints**:
- `handle` must be a `[<CustomOperation>]` (not `Bind`/`Return`) because the CE is metadata-accumulation-style, not monadic
- `produces<'T>` and `accepts<'T>` need generic type parameters — this works with F# CE custom operations by using `MaintainsVariableSpaceUsingBind = true` or via static member overloads
- The `HandlerDefinition` record holds the `RequestDelegate` plus metadata lists; at registration time, metadata is converted to `EndpointBuilder -> unit` conventions

## R6: Microsoft.OpenApi Version Differences (net9.0 vs net10.0)

**Decision**: Use conditional package references matching FSharp.Data.JsonSchema.OpenApi's pattern:
- net9.0: `Microsoft.AspNetCore.OpenApi` 9.0.x (uses Microsoft.OpenApi v1.x)
- net10.0: `Microsoft.AspNetCore.OpenApi` 10.0.x (uses Microsoft.OpenApi v2.x)

**Rationale**: The OpenAPI types differ significantly between versions:
- net9.0: `Microsoft.OpenApi.Models.OpenApiSchema` with string type properties
- net10.0: `Microsoft.OpenApi.OpenApiSchema` with `JsonSchemaType` enum flags

Frank.OpenApi itself does not manipulate `OpenApiSchema` objects directly — it attaches metadata to endpoints and delegates schema generation to `FSharpSchemaTransformer`. However, conditional package references are needed because `OpenApiOptions` and transformer interfaces may differ.

**Key difference**: On net10.0, `WithOpenApi()` is deprecated (ASPDEPR002), but Frank doesn't use it anyway since metadata is attached at the endpoint level.

## R7: WebHostBuilder useOpenApi Design

**Decision**: Follow the Frank.Auth pattern — modify both `Services` and `Middleware` fields of `WebHostSpec`:
- Services: call `AddOpenApi()` with optional configuration callback (including default `FSharpSchemaTransformer` registration)
- Middleware: call `MapOpenApi()` to expose the `/openapi/v1.json` endpoint

**Rationale**: The Frank.Auth `useAuthentication` and `useAuthorization` operations set the established pattern: modify `spec.Services` for DI registration and `spec.Middleware` for middleware pipeline. `useOpenApi` follows this exactly.

**Note**: `MapOpenApi()` is an endpoint middleware (not a traditional middleware). It should be added in the Middleware phase to ensure it's registered after routing but before endpoint execution. The built-in WebHostBuilder Run method calls UseRouting() then Middleware then UseEndpoints(), so MapOpenApi() placed in Middleware will work correctly.

**Alternatives considered**:
- Separate `addOpenApi` (services) and `mapOpenApi` (middleware) operations → Rejected: over-engineering for what is always a paired setup; an optional `OpenApiOptions -> unit` callback provides escape hatch for advanced scenarios

## R8: ResourceSpec.Name and operationId Auto-Mapping

**Decision**: Frank.OpenApi should auto-map `ResourceSpec.Name` to `EndpointNameMetadata` (operationId) as a fallback when no explicit `HandlerDefinition.Name` is provided

**Context**: Frank core's `ResourceSpec.Name` (set via `resource "/path" { name "Products" }`) is currently used only for `DisplayName` in the built-in `ResourceSpec.Build()`:

```fsharp
let displayName = httpMethod+" "+(if String.IsNullOrEmpty name then routeTemplate else name)
```

PR #55 auto-derived `operationId` from `ResourceSpec.Name` + HTTP method (e.g., "Products" + GET → "getProducts"). The current plan uses `HandlerDefinition.Name` for operationId, but endpoints without HandlerDefinitions (basic usage, US1) would have no operationId in the OpenAPI document.

**Approach**: Add an `IOpenApiOperationTransformer` in the `useOpenApi` setup that reads each endpoint's `DisplayName` and generates a fallback `operationId` if none is set by metadata. This keeps the mapping in Frank.OpenApi (not core Frank), uses existing endpoint data, and provides a sensible default for the basic usage path without requiring the HandlerBuilder.

**Rationale**: This bridges the gap between basic usage (US1: just `resource` + `useOpenApi`) and rich metadata usage (US3: `handler` builder). Without it, the basic quickstart produces a valid but less useful OpenAPI document with anonymous operations. With it, the resource `name` automatically flows to operationId, matching the behavior PR #55 had.

**Alternative considered**:
- Add `EndpointNameMetadata` directly in Frank core's `ResourceSpec.Build()` → Rejected: this would couple core Frank to OpenAPI concerns, violating the separation principle. The mapping belongs in Frank.OpenApi
