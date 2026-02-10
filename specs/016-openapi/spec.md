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
