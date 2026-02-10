# Tasks: OpenAPI Document Generation Support

**Input**: Design documents from `/specs/016-openapi/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are included as part of each user story to verify acceptance scenarios.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Library source**: `src/Frank.OpenApi/`
- **Test source**: `test/Frank.OpenApi.Tests/`
- Follow existing Frank extension patterns (Frank.Auth, Frank.Datastar)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the Frank.OpenApi project and test project with correct dependencies and build configuration

- [X] T001 Create the `src/Frank.OpenApi/Frank.OpenApi.fsproj` project file targeting `net9.0;net10.0` with project reference to `src/Frank/Frank.fsproj`, framework reference `Microsoft.AspNetCore.App`, and conditional package references for `Microsoft.AspNetCore.OpenApi` (9.0.x on net9.0, 10.0.x on net10.0) and `FSharp.Data.JsonSchema.OpenApi` (3.0.0). Include `PackageTags` and `Description` following the Frank.Auth.fsproj pattern.
- [X] T002 Create the `test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj` test project targeting `net10.0` with project reference to `src/Frank.OpenApi/Frank.OpenApi.fsproj`, and package references for `Expecto`, `YoloDev.Expecto.TestSdk`, `Microsoft.NET.Test.Sdk`, and `Microsoft.AspNetCore.TestHost` (matching version patterns from `test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj`).
- [X] T003 Create `test/Frank.OpenApi.Tests/Program.fs` with Expecto test runner entry point (follow `test/Frank.Auth.Tests/Program.fs` pattern).
- [X] T004 Verify projects compile by running `dotnet build src/Frank.OpenApi/Frank.OpenApi.fsproj` and `dotnet build test/Frank.OpenApi.Tests/Frank.OpenApi.Tests.fsproj`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Define the core data types used by all user stories — HandlerDefinition and supporting records

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T005 Create `src/Frank.OpenApi/HandlerDefinition.fs` defining the `Frank.OpenApi` namespace with three record types: `ProducesInfo` (`StatusCode: int`, `ResponseType: Type option`, `ContentTypes: string list`, `Description: string option`), `AcceptsInfo` (`RequestType: Type`, `ContentTypes: string list`, `IsOptional: bool`), and `HandlerDefinition` (`Handler: RequestDelegate`, `Name: string option`, `Summary: string option`, `Description: string option`, `Tags: string list`, `Produces: ProducesInfo list`, `Accepts: AcceptsInfo list`) with a `static member Empty` that uses `Unchecked.defaultof<_>` for Handler and empty defaults for all optional fields. See `contracts/api-surface.md` for exact type signatures.
- [X] T006 Create a static helper module `HandlerDefinitionMetadata` in `src/Frank.OpenApi/HandlerDefinition.fs` (or a separate file) with a function `toConventions : HandlerDefinition -> (EndpointBuilder -> unit) list` that converts each field of HandlerDefinition to ASP.NET Core endpoint metadata conventions: `Name` → `EndpointNameMetadata`, `Summary` → `EndpointSummaryMetadata`, `Description` → `EndpointDescriptionMetadata`, `Tags` → `TagsMetadata`, `Produces` → `ProducesResponseTypeMetadata`, `Accepts` → `AcceptsMetadata`. See `data-model.md` Metadata Conversion section and `research.md` R3 for the exact metadata types to use.
- [X] T007 Verify foundational types compile by running `dotnet build src/Frank.OpenApi/Frank.OpenApi.fsproj`.

**Checkpoint**: Foundation ready - HandlerDefinition types and metadata conversion are in place

---

## Phase 3: User Story 1 - Serve OpenAPI Document from Frank Application (Priority: P1) MVP

**Goal**: A Frank application can enable OpenAPI via `useOpenApi` and serve a valid OpenAPI document at `/openapi/v1.json` listing all registered resource endpoints with their HTTP methods and route templates.

**Independent Test**: Create a Frank application with a simple resource endpoint using `useOpenApi`, request `/openapi/v1.json`, and verify it returns a valid OpenAPI document containing the endpoint.

### Implementation for User Story 1

- [X] T008 [US1] Create `src/Frank.OpenApi/WebHostBuilderExtensions.fs` with an `[<AutoOpen>]` module `WebHostBuilderExtensions` containing type extensions on `WebHostBuilder` with two `[<CustomOperation("useOpenApi")>]` overloads: (1) `UseOpenApi(spec: WebHostSpec) : WebHostSpec` that registers `AddOpenApi` with `FSharpSchemaTransformer` default config in Services and `MapOpenApi()` in Middleware, and (2) `UseOpenApi(spec: WebHostSpec, configure: OpenApiOptions -> unit) : WebHostSpec` that passes the user's configuration callback. Both overloads should also register an `IOpenApiOperationTransformer` that derives a fallback `operationId` from the endpoint's `DisplayName` (which carries `ResourceSpec.Name`) when no explicit `EndpointNameMetadata` is present — see `research.md` R8. Follow the Frank.Auth `WebHostBuilderExtensions.fs` pattern for Services/Middleware composition. See `contracts/api-surface.md` useOpenApi Implementation Pattern and `research.md` R7 for the exact implementation.
- [X] T009 [US1] Create `test/Frank.OpenApi.Tests/OpenApiDocumentTests.fs` with Expecto tests verifying US1 acceptance scenarios: (1) a test that creates a Frank resource with a simple GET handler and `name "Products"`, builds a test host with `useOpenApi` equivalent setup (using `Host.CreateDefaultBuilder` + `UseTestServer` + `AddOpenApi` + `MapOpenApi` + custom `TestEndpointDataSource` — following the Frank.Auth.Tests pattern since `ResourceEndpointDataSource` is internal), sends GET to `/openapi/v1.json`, and asserts HTTP 200 with JSON content containing the resource path, GET method, and an auto-derived `operationId` from the resource name (see R8); (2) a test that verifies the OpenAPI document includes multiple endpoints from multiple resources; (3) a test verifying an app without OpenAPI configured does not expose the `/openapi/v1.json` endpoint. Use `let! (response: HttpResponseMessage)` with explicit type annotations for Expecto async compatibility.
- [X] T010 [US1] Run tests with `dotnet test test/Frank.OpenApi.Tests/` and verify all US1 tests pass.

**Checkpoint**: User Story 1 complete — Frank applications can serve OpenAPI documents via `useOpenApi`

---

## Phase 4: User Story 2 - Document Request/Response Types for F# Types (Priority: P2)

**Goal**: F# record types, discriminated unions, option types, collections, and recursive types used in endpoint handlers are accurately represented as JSON Schema definitions in the generated OpenAPI document via `FSharpSchemaTransformer`.

**Independent Test**: Create endpoints with F# record and DU types as response types (using `ProducesResponseTypeMetadata`), request the OpenAPI document, and verify the schema definitions match expected JSON Schema representations.

**Note**: This story depends on FSharp.Data.JsonSchema.OpenApi's `FSharpSchemaTransformer` doing the actual schema generation. Frank.OpenApi's role is to ensure the schema transformer is wired in and that type metadata is correctly attached to endpoints.

### Implementation for User Story 2

- [X] T011 [US2] Create `test/Frank.OpenApi.Tests/SchemaTests.fs` with Expecto tests verifying US2 acceptance scenarios. Define sample F# types for testing: a `Product` record with required and optional fields, a `Shape` discriminated union with multiple cases, a `Tree` recursive type, and types using `list`, `Map`, and `option`. Create test endpoints that attach `ProducesResponseTypeMetadata` for each type via `ResourceBuilder.AddMetadata`. Build a test host with `AddOpenApi` + `FSharpSchemaTransformer`, request `/openapi/v1.json`, parse the JSON, and verify: (1) record schemas have correct properties and required fields; (2) DU schemas use `anyOf` with discriminator; (3) option fields are nullable; (4) collection types are arrays; (5) recursive types use `$ref`. Note: these tests verify the integration of FSharpSchemaTransformer with Frank's endpoint metadata, not the schema generation logic itself (which is tested in FSharp.Data.JsonSchema).
- [X] T012 [US2] Run tests with `dotnet test test/Frank.OpenApi.Tests/` and verify all US2 tests pass alongside US1 tests.

**Checkpoint**: User Story 2 complete — F# type schemas appear correctly in the OpenAPI document

---

## Phase 5: User Story 3 - Define Handlers with Embedded OpenAPI Metadata (Priority: P3)

**Goal**: Developers can use the `handler` computation expression to define handlers with embedded OpenAPI metadata (name, description, tags, produces, accepts) and pass these to `resource` builder HTTP method operations.

**Independent Test**: Create handler definitions using the `handler` builder with full metadata, pass them to `resource` operations, and verify the OpenAPI document contains the specified operationId, tags, response types, and request body schemas.

### Implementation for User Story 3

- [X] T013 [US3] Create `src/Frank.OpenApi/HandlerBuilder.fs` with the `[<Sealed>] type HandlerBuilder()` computation expression. Implement: `Yield(_)` returning `HandlerDefinition.Empty`, `Run(def)` returning def (or validating Handler is set). Add `[<CustomOperation>]` members: `handle` (with overloads for `HttpContext -> Task`, `HttpContext -> Task<'a>`, `HttpContext -> Async<'a>` — converting to `RequestDelegate` like `ResourceBuilder.AddHandler` does), `name`, `summary`, `description`, `tags`, `produces<'T>` (adding `ProducesInfo` with `typeof<'T>`), `producesEmpty` (adding `ProducesInfo` with `None` ResponseType), `accepts<'T>` (adding `AcceptsInfo` with `typeof<'T>`). Add a module-level `let handler = HandlerBuilder()` instance. See `contracts/api-surface.md` HandlerBuilder section for exact signatures.
- [X] T014 [US3] Create `src/Frank.OpenApi/ResourceBuilderExtensions.fs` with an `[<AutoOpen>]` module `ResourceBuilderExtensions` containing type extensions on `ResourceBuilder`. Add a private static helper `AddHandlerDefinition(httpMethod: string, spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec` that: (1) adds the handler via `{ spec with Handlers = (httpMethod, def.Handler) :: spec.Handlers }`, and (2) appends metadata conventions from `HandlerDefinitionMetadata.toConventions` via `{ spec with Metadata = spec.Metadata @ conventions }`. Then add `[<CustomOperation>]` overloads for `get`, `post`, `put`, `delete`, `patch`, `head`, and `options` that each call `AddHandlerDefinition` with the appropriate HTTP method. See `contracts/api-surface.md` ResourceBuilder Extensions section and `data-model.md` Metadata Conversion section.
- [X] T015 [P] [US3] Create `test/Frank.OpenApi.Tests/HandlerBuilderTests.fs` with Expecto unit tests verifying: (1) `handler { handle (fun ctx -> Task.CompletedTask) }` produces a HandlerDefinition with the handler set; (2) `handler { name "Op"; description "Desc"; tags ["T1"]; handle ... }` populates Name, Description, Tags fields; (3) `handler { produces<Product> 200; producesEmpty 404; handle ... }` populates Produces list with correct types and status codes; (4) `handler { accepts<CreateRequest>; handle ... }` populates Accepts list; (5) metadata combinations accumulate correctly in the HandlerDefinition record.
- [X] T016 [P] [US3] Create `test/Frank.OpenApi.Tests/MetadataTests.fs` with Expecto integration tests verifying: (1) a HandlerDefinition passed to `resource "/test" { get handlerDef }` results in endpoints with correct metadata objects (`EndpointNameMetadata`, `TagsMetadata`, `ProducesResponseTypeMetadata`, etc.) in the endpoint's metadata collection; (2) a resource mixing plain handlers and HandlerDefinitions both work — plain handlers have `HttpMethodMetadata` only, HandlerDefinitions have full metadata; (3) HandlerDefinitions work with all HTTP methods (get, post, put, delete, patch). Build resources using `ResourceSpec.Build` and inspect `Endpoint.Metadata` directly (no test host needed for metadata-level tests).
- [X] T017 [US3] Add end-to-end tests to `test/Frank.OpenApi.Tests/OpenApiDocumentTests.fs` verifying US3 acceptance scenarios with the full pipeline: (1) create a `handler` definition with name, tags, produces, accepts metadata; (2) pass it to a resource; (3) build a test host with OpenAPI enabled; (4) request `/openapi/v1.json`; (5) verify the OpenAPI document contains the correct `operationId`, `tags`, `responses` with schema types, and `requestBody`; (6) verify a resource with mixed plain handlers and handler definitions produces correct OpenAPI output for both.
- [X] T018 [US3] Run all tests with `dotnet test test/Frank.OpenApi.Tests/` and verify all US1, US2, and US3 tests pass.

**Checkpoint**: All user stories complete — HandlerBuilder, ResourceBuilder extensions, and WebHostBuilder extensions are fully functional

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation, documentation, and integration

- [X] T019 Verify existing Frank tests still pass by running `dotnet test test/Frank.Tests/` and `dotnet test test/Frank.Auth.Tests/` and `dotnet test test/Frank.Datastar.Tests/` to confirm no regressions from adding the Frank.OpenApi project.
- [X] T020 Verify the Frank.OpenApi library compiles for both target frameworks by running `dotnet build src/Frank.OpenApi/Frank.OpenApi.fsproj -f net9.0` and `dotnet build src/Frank.OpenApi/Frank.OpenApi.fsproj -f net10.0`.
- [X] T021 Validate the quickstart scenario from `specs/016-openapi/quickstart.md` works end-to-end: create a minimal test in `test/Frank.OpenApi.Tests/OpenApiDocumentTests.fs` (or verify existing tests cover it) that matches the quickstart's basic usage and handler builder usage patterns.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (T001-T004)
- **User Story 1 (Phase 3)**: Depends on Phase 2 (T005-T007) — delivers MVP
- **User Story 2 (Phase 4)**: Depends on Phase 2 (T005-T007) — can run in parallel with US1, but US1 provides the `useOpenApi` test infrastructure
- **User Story 3 (Phase 5)**: Depends on Phase 2 (T005-T007) — can run in parallel with US1/US2 for implementation, but end-to-end tests (T017) depend on US1's `useOpenApi`
- **Polish (Phase 6)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Phase 2. No dependencies on other stories. **This is the MVP.**
- **User Story 2 (P2)**: Can start after Phase 2. Tests build on US1's test host infrastructure but are independently testable.
- **User Story 3 (P3)**: Can start after Phase 2. HandlerBuilder (T013) and ResourceBuilder extensions (T014) are independent of US1/US2. End-to-end tests (T017) use the `useOpenApi` test host from US1.

### Within Each User Story

- Types/models before services/logic
- Implementation before integration tests
- Unit tests can be written in parallel with implementation (marked [P])

### Parallel Opportunities

- T001 and T002 can be done sequentially (T002 depends on T001)
- T005 and T006 are sequential (T006 depends on T005 types)
- T015 and T016 can run in parallel (different test files, test different aspects)
- US1 and US3 implementation (T008 vs T013+T014) touch different files and can run in parallel

---

## Parallel Example: User Story 3

```text
# These can be implemented in parallel (different files):
T013: HandlerBuilder CE in src/Frank.OpenApi/HandlerBuilder.fs
T014: ResourceBuilder extensions in src/Frank.OpenApi/ResourceBuilderExtensions.fs

# These tests can run in parallel (different test files):
T015: HandlerBuilder unit tests in test/Frank.OpenApi.Tests/HandlerBuilderTests.fs
T016: Metadata integration tests in test/Frank.OpenApi.Tests/MetadataTests.fs

# Then sequential:
T017: End-to-end OpenAPI document tests (depends on T013, T014, and T008)
T018: Run all tests (depends on all above)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001-T004)
2. Complete Phase 2: Foundational types (T005-T007)
3. Complete Phase 3: User Story 1 — `useOpenApi` (T008-T010)
4. **STOP and VALIDATE**: Frank apps can serve OpenAPI documents
5. Deploy/demo if ready — this alone delivers value for basic API discovery

### Incremental Delivery

1. Complete Setup + Foundational → Types and project structure ready
2. Add User Story 1 → Test independently → OpenAPI document served (MVP!)
3. Add User Story 2 → Test independently → F# types in schema
4. Add User Story 3 → Test independently → HandlerBuilder + rich metadata
5. Each story adds value without breaking previous stories

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- `ResourceEndpointDataSource` is `internal` in Frank — tests must create their own `TestEndpointDataSource` (see Frank.Auth.Tests pattern)
- F# type annotations are needed on `let!` bindings for `HttpResponseMessage` in Expecto tasks
- The `Frank.OpenApi.fsproj` targets net9.0 and net10.0 only (not net8.0) because `Microsoft.AspNetCore.OpenApi` requires .NET 9+
- `MapOpenApi()` is an endpoint-mapping extension, not traditional middleware — it goes in the `Middleware` phase of `WebHostSpec` which runs after `UseRouting()` but before `UseEndpoints()`
