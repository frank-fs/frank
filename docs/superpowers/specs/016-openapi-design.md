---
source: specs/016-openapi
status: released
type: spec
---

# Feature Specification: OpenAPI Document Generation Support

**Feature Branch**: `016-openapi`
**Created**: 2026-02-09
**Status**: Draft
**Input**: User description: "GitHub issue #56 and PR #55 relate to Open API support. This was previously waiting on completion of FSharp.Data.JsonSchema, which now has an FSharp.Data.JsonSchema.OpenApi library option that should work with ASP.NET Core Minimal API support for Open API. Refer to the design doc at /Users/ryanr/Downloads/frank-openapi-spec.md. FSharp.Data.JsonSchema source code is available at /Users/ryanr/Code/FSharp.Data.JsonSchema"

## Clarifications

### Session 2026-02-09

- Q: Should the HandlerBuilder be specified in the spec as a user-facing concept, or deferred to planning as an implementation detail? → A: Add to spec as a user-facing concept — a way to define handlers with embedded metadata (Option A)
- Q: Where should the handler definition type, handler builder, and ResourceBuilder overloads live? → A: All in `Frank.OpenApi` — core Frank is unchanged; ResourceBuilder overloads added via type extensions (Option A)
- Q: Should `Frank.OpenApi` provide `useOpenApi` convenience on `WebHostBuilder`? → A: Yes, provide `useOpenApi` with optional configuration callback, following the Frank.Auth pattern (Option A)
- Q: Should `ResourceSpec.Name` (from `resource "/path" { name "X" }`) auto-map to `operationId` in the OpenAPI document? → A: Yes, as a fallback — Frank.OpenApi should register an `IOpenApiOperationTransformer` that derives `operationId` from the endpoint's display name when no explicit `EndpointNameMetadata` is present. This bridges basic usage (US1) with the HandlerBuilder path (US3) without modifying core Frank. See research.md R8

## User Scenarios & Testing

### User Story 1 - Serve OpenAPI Document from Frank Application (Priority: P1)

As a Frank web application developer, I want my application to serve an OpenAPI document describing my API endpoints so that API consumers can discover available operations, understand request/response formats, and generate client code.

**Why this priority**: This is the core functionality - without the ability to serve an OpenAPI document, none of the other features matter. This represents the minimum viable product.

**Independent Test**: Can be fully tested by creating a simple Frank application with one endpoint and verifying that `/openapi/v1.json` returns a valid OpenAPI document describing that endpoint.

**Acceptance Scenarios**:

1. **Given** a Frank application with defined resource endpoints, **When** the application is configured to enable OpenAPI, **Then** the application serves a valid OpenAPI 3.0+ document at a designated endpoint
2. **Given** the OpenAPI document is requested, **When** the document is retrieved, **Then** it includes all registered Frank resource endpoints with their HTTP methods and route templates
3. **Given** a Frank application without OpenAPI enabled, **When** the application runs, **Then** no OpenAPI endpoint is exposed and existing functionality remains unchanged

---

### User Story 2 - Document Request/Response Types for F# Types (Priority: P2)

As a Frank developer, I want my F# record and discriminated union types used in endpoint handlers to be accurately represented in the OpenAPI document so that API consumers understand the exact structure and validation rules for request and response payloads.

**Why this priority**: OpenAPI documents without accurate schema information are of limited value. This builds on P1 by adding rich type information that makes the API documentation actually useful.

**Independent Test**: Can be fully tested by creating an endpoint that accepts an F# record as input and returns a discriminated union as output, then verifying the OpenAPI document contains correct JSON Schema definitions for both types.

**Acceptance Scenarios**:

1. **Given** an endpoint accepts a request body of type F# record, **When** the OpenAPI document is generated, **Then** the document includes a schema definition showing all record fields with correct types and required field markers
2. **Given** an endpoint returns a multi-case discriminated union, **When** the OpenAPI document is generated, **Then** the schema represents the union as `anyOf` with appropriate discriminator properties
3. **Given** an endpoint uses `option<'T>` types, **When** the OpenAPI document is generated, **Then** the schema marks these fields as nullable
4. **Given** an endpoint uses collection types (`list`, `array`, `seq`, `Map`), **When** the OpenAPI document is generated, **Then** the schema accurately represents these as JSON array or object types
5. **Given** recursive F# types are used, **When** the OpenAPI document is generated, **Then** the schema uses `$ref` references to break cycles and define component schemas

---

### User Story 3 - Define Handlers with Embedded OpenAPI Metadata (Priority: P3)

As a Frank developer, I want a dedicated handler builder that lets me define an endpoint handler alongside its OpenAPI metadata (operation name, description, tags, response types, accepted request types) as a single composable unit, so I can pass this enriched handler to the `resource` builder in place of a plain handler function.

**Why this priority**: While P1 and P2 provide functional OpenAPI documents with basic endpoint information, this story gives developers explicit control over the richness of their API documentation. The handler builder is the primary user-facing API for attaching metadata to individual operations.

**Independent Test**: Can be fully tested by creating a handler definition with metadata (name, tags, produces, accepts), passing it to a `resource` builder's `get`/`post` operation, and verifying the resulting OpenAPI document contains the specified operation metadata.

**Acceptance Scenarios**:

1. **Given** a developer defines a handler using the handler builder with an operation name, **When** that handler is used in a resource's HTTP method operation, **Then** the OpenAPI document includes the specified name as the operation's `operationId`
2. **Given** a handler builder specifies description and tags, **When** the OpenAPI document is generated, **Then** the operation includes the description and is grouped under the specified tags
3. **Given** a handler builder specifies multiple response types with status codes, **When** the OpenAPI document is generated, **Then** the operation lists all specified responses with their schema types
4. **Given** a handler builder specifies accepted request content types, **When** the OpenAPI document is generated, **Then** the operation's `requestBody` includes the specified content types and schema
5. **Given** a resource mixes plain handler functions (no metadata) and handler builder definitions, **When** the OpenAPI document is generated, **Then** both styles work correctly — plain handlers appear with inferred defaults, handler builder definitions appear with explicit metadata
6. **Given** a developer defines a handler using the handler builder, **When** the handler builder is evaluated, **Then** it produces a value that can be passed directly to any of the `resource` builder's HTTP method operations (`get`, `post`, `put`, `delete`, `patch`) in place of a plain handler function

---

### Edge Cases

- What happens when an endpoint handler uses F# types not directly representable in JSON Schema (e.g., functions, units of measure)?
- How does the system handle endpoints registered at runtime after the OpenAPI document is first requested?
- What happens when the same F# type is used with different JSON serialization settings in different parts of the application?
- How are generic types handled when the same generic type is instantiated with different type parameters across different endpoints?
- What happens when discriminated union encoding settings in `FSharp.SystemTextJson` don't match the schema generation configuration?
- How does the system behave when an endpoint has no explicit response type annotation?

## Requirements

### Functional Requirements

- **FR-001**: Frank applications MUST be able to serve an OpenAPI document describing all registered resource endpoints
- **FR-002**: The OpenAPI integration MUST be provided as a separate `Frank.OpenApi` extension library, not built into the core Frank library
- **FR-003**: The `Frank.OpenApi` library MUST use the existing extensibility points in `ResourceBuilder` to attach metadata to endpoints and add handler definition overloads, without modifying core Frank code
- **FR-004**: The OpenAPI integration MUST be opt-in - existing Frank applications continue to work without modification
- **FR-005**: The `Frank.OpenApi` library MUST provide a `useOpenApi` convenience operation on `WebHostBuilder` (with optional configuration callback) that wires up both service registration and middleware, following the established Frank extension pattern
- **FR-006**: The system MUST generate accurate JSON Schema definitions for F# record types including field names, types, and required/optional distinction
- **FR-007**: The system MUST generate accurate JSON Schema definitions for F# discriminated unions representing them as `anyOf` or `oneOf` with discriminator properties matching the serialization format
- **FR-008**: The system MUST handle `option<'T>` and `voption<'T>` types by marking fields as nullable in the schema
- **FR-009**: The system MUST handle collection types (`list<'T>`, `'T[]`, `seq<'T>`, `Set<'T>`) as array schemas with item types
- **FR-010**: The system MUST handle `Map<string, 'T>` types as object schemas with additionalProperties
- **FR-011**: The system MUST handle recursive F# types by generating `$ref` references and component schema definitions to avoid infinite loops
- **FR-012**: The `Frank.OpenApi` library MUST provide a handler builder that produces a handler definition combining a request handler function with OpenAPI metadata
- **FR-013**: The handler builder MUST support specifying operation name, description, tags, response types with status codes, and accepted request types
- **FR-014**: Handler definitions produced by the handler builder MUST be usable in place of plain handler functions in the `resource` builder's HTTP method operations (`get`, `post`, `put`, `delete`, `patch`), via overloads provided by `Frank.OpenApi` type extensions
- **FR-015**: Developers MUST be able to specify accepted request body types and content types via the handler builder
- **FR-016**: The system MUST maintain backward compatibility - existing `resource` computation expression syntax remains unchanged
- **FR-017**: The system MUST support endpoints defined with both handler builder definitions and legacy plain handler functions in the same application
- **FR-018**: Generated OpenAPI documents MUST be valid according to the OpenAPI 3.0+ specification
- **FR-019**: The system MUST integrate with ASP.NET Core's built-in OpenAPI infrastructure (`AddOpenApi`, `MapOpenApi`)
- **FR-020**: F# type schema generation MUST match the JSON serialization behavior of `FSharp.SystemTextJson` to ensure schema accuracy

### Key Entities

- **OpenAPI Document**: A machine-readable description of the API conforming to OpenAPI 3.0+ specification, including paths, operations, schemas, and metadata
- **Handler Builder**: A user-facing builder (provided by `Frank.OpenApi`) that allows developers to define a handler function together with OpenAPI metadata as a single composable unit, producing a Handler Definition
- **Handler Definition**: The output of the Handler Builder — a combination of an HTTP request handler function and associated metadata (operation name, description, tags, request/response type annotations) that can be passed to `resource` builder HTTP method operations in place of a plain handler function
- **Endpoint Metadata**: Information attached to ASP.NET Core endpoints including operation names, request/response types, status codes, and content types
- **JSON Schema Definition**: A schema describing the structure of a JSON object, derived from F# types and included in the OpenAPI document's component schemas
- **Resource**: A Frank abstraction representing a URL path with associated HTTP method handlers (GET, POST, PUT, DELETE, PATCH)

## Success Criteria

### Measurable Outcomes

- **SC-001**: A developer can add OpenAPI support to an existing Frank application with no more than 5 lines of configuration code
- **SC-002**: Generated OpenAPI documents pass validation against the OpenAPI 3.0 specification schema
- **SC-003**: JSON Schema definitions for F# types in the OpenAPI document accurately match the JSON produced by `FSharp.SystemTextJson` serialization (verified by round-trip tests)
- **SC-004**: Developers can view Frank API documentation in Swagger UI or other OpenAPI-compatible tools without errors or warnings
- **SC-005**: Client code generators (Kiota, NSwag, OpenAPI Generator) can successfully generate working client libraries from Frank-generated OpenAPI documents
- **SC-006**: 100% of existing Frank sample applications continue to work without modification after the OpenAPI feature is added to the framework
- **SC-007**: A Frank application with 50 endpoints generates a complete OpenAPI document in under 500ms on first request

## Research

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
