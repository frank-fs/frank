# Feature Specification: SHACL Validation from F# Types

**Feature Branch**: `005-shacl-validation-from-fsharp-types`
**Created**: 2026-03-07
**Status**: Done
**GitHub Issue**: #76
**Dependencies**: Frank.LinkedData (#75, complete), Frank.Auth (capability-based authorization), Frank.Statecharts (#87, 004-frank-statecharts)
**Input**: Phase 2 of #80 (Semantic Metadata-Augmented Resources). SHACL shapes as semantic request/response constraints, derived automatically from F# types.

---

## Clarifications

### Session 2026-03-07

- Q: Are SHACL shapes hand-authored or derived? -> A: Derived from F# types (auto-generated). Custom constraints can extend auto-derived shapes, but the baseline is always the type system.
- Q: Where does validation run in the pipeline? -> A: After authorization (Frank.Auth), before handler dispatch. This means an authorized but invalid request is rejected before the handler sees it.
- Q: How are violations surfaced? -> A: As structured SHACL ValidationReport responses, content-negotiated via Frank.LinkedData (JSON-LD, Turtle, RDF/XML) or as standard problem details for non-semantic clients.
- Q: Does this compose with Frank.Auth? -> A: Yes. SHACL shapes can express capability preconditions -- e.g., a shape that constrains valid input differently based on the authenticated principal's capabilities.
- Q: What HTTP status code for validation failures? → A: 422 Unprocessable Content (RFC 9110) — request is syntactically valid but violates semantic constraints.
- Q: Does validation apply to request bodies only or also query parameters? → A: Both. Request bodies (POST/PUT/PATCH) and GET query parameters are validated against derived shapes.
- Q: How are GET query parameters mapped to SHACL property paths? → A: Query parameter names map directly to property paths using the parameter name as the RDF predicate local name. Nested properties use dot notation (e.g., ?address.zipCode maps to sh:path address/zipCode).

---

## User Scenarios & Testing

### User Story 1 -- Automatic Shape Derivation from F# Types (Priority: P1)

A Frank developer defines F# record types and discriminated unions as the domain model for their resource handlers. When they enable validation on a resource, SHACL NodeShapes are automatically derived from these types at startup -- record fields become SHACL property shapes with appropriate datatype constraints, required vs. optional cardinality, and value ranges. The developer writes zero SHACL by hand.

**Why this priority**: This is the foundation. Without automatic derivation, developers must hand-author SHACL shapes, which defeats the purpose of leveraging the F# type system as the single source of truth. Every other capability (validation, reporting, composition) depends on shapes existing.

**Independent Test**: Define a Frank resource with a record type containing required fields, optional fields, and constrained types. Start the application and inspect the derived SHACL shapes. Verify each field produces the correct property shape with matching datatype and cardinality.

**Acceptance Scenarios**:

1. **Given** a Frank resource handler that accepts an F# record type with `string`, `int`, `decimal`, and `DateTimeOffset` fields, **When** the application starts, **Then** a SHACL NodeShape is derived with sh:property entries for each field, each with the correct sh:datatype (xsd:string, xsd:integer, xsd:decimal, xsd:dateTimeOffset).
2. **Given** a record type with `option<string>` fields alongside required `string` fields, **When** shapes are derived, **Then** required fields have sh:minCount 1 and option fields have sh:minCount 0.
3. **Given** an F# discriminated union used as a field type (e.g., `PaymentMethod = CreditCard | BankTransfer | Crypto`), **When** shapes are derived, **Then** a sh:in constraint is produced listing all DU case names as allowed values.
4. **Given** a record type containing a nested record (e.g., `Address` inside `Customer`), **When** shapes are derived, **Then** the parent NodeShape references a child NodeShape via sh:node, and the child NodeShape is independently valid.

---

### User Story 2 -- Request Validation Before Handler Dispatch (Priority: P1)

When a client sends a POST or PUT request to a validated resource, the Frank.Validation middleware deserializes the request body, validates it against the derived SHACL shape, and either passes control to the handler (valid) or short-circuits with a structured violation response (invalid). The handler never sees invalid data.

**Why this priority**: This is the core runtime behavior. Shape derivation without enforcement is documentation, not validation. Intercepting invalid requests before handler dispatch is essential for correctness and security.

**Independent Test**: Send requests with missing required fields, wrong data types, and out-of-range values to a validated resource. Verify the handler is never invoked and the response contains a SHACL ValidationReport.

**Acceptance Scenarios**:

1. **Given** a validated resource expecting a record with required field `name: string`, **When** a POST arrives with a body missing the `name` field, **Then** the middleware returns a validation failure response before the handler executes.
2. **Given** a validated resource expecting `quantity: int`, **When** a POST arrives with `quantity: "abc"`, **Then** the middleware returns a validation failure citing a datatype constraint violation.
3. **Given** a validated resource with a DU-constrained field `status: OrderStatus`, **When** a POST arrives with `status: "InvalidValue"`, **Then** the middleware returns a validation failure citing an sh:in constraint violation.
4. **Given** a valid request that satisfies all SHACL constraints, **When** it passes through validation, **Then** the handler receives the deserialized, validated data and executes normally.
5. **Given** a validated resource expecting `status: OrderStatus` via query parameter, **When** a GET arrives with `?status=InvalidValue`, **Then** the middleware returns a validation failure citing an sh:in constraint violation.
6. **Given** a valid query parameter that satisfies all constraints, **When** a GET passes through validation, **Then** the handler executes normally.

---

### User Story 3 -- Structured Violation Reports (Priority: P2)

When validation fails, the response body is a SHACL ValidationReport containing one ValidationResult per violation. Each result identifies the focus node, the violated constraint, the offending value, and a human-readable message. The report is content-negotiated -- semantic clients receive JSON-LD/Turtle/RDF+XML via Frank.LinkedData, while standard clients receive RFC 9457 Problem Details JSON.

**Why this priority**: Structured reports enable clients to programmatically identify and correct errors. Without them, validation errors are opaque strings that require human interpretation.

**Independent Test**: Send an invalid request with multiple violations (missing field, wrong type, out-of-range value). Verify the response contains one ValidationResult per violation. Request the same endpoint with `Accept: application/ld+json` and `Accept: application/json` and verify appropriate content negotiation.

**Acceptance Scenarios**:

1. **Given** a request with three distinct violations, **When** validation fails, **Then** the ValidationReport contains exactly three ValidationResult entries, each with the fields specified in FR-010.
2. **Given** an invalid request and a client sending `Accept: application/ld+json`, **When** the violation response is returned, **Then** it is serialized as a JSON-LD SHACL ValidationReport via Frank.LinkedData.
3. **Given** an invalid request and a client sending `Accept: application/json`, **When** the violation response is returned, **Then** it is serialized as an RFC 9457 Problem Details response with violation details in the `errors` extension member.
4. **Given** a violation on a nested field (e.g., `customer.address.zipCode`), **Then** the sh:resultPath reflects the full property path from the root node.

---

### User Story 4 -- Frank.Auth Capability Composition (Priority: P3)

A Frank developer configures SHACL shapes that vary based on the authenticated principal's capabilities. For example, an admin can set any `OrderStatus` value, but a regular user can only set `Submitted` or `Cancelled`. The SHACL shapes compose with Frank.Auth's capability model so that authorization and validation form a unified constraint pipeline.

**Why this priority**: This is an advanced integration scenario. Core validation (P1/P2) must work standalone before adding capability-dependent shape selection.

**Independent Test**: Define a resource with capability-dependent shapes. Send requests as admin and regular user with the same body. Verify the admin request succeeds and the regular user request fails validation (not authorization -- they are authorized to call the endpoint but the shape constrains their allowed values).

**Acceptance Scenarios**:

1. **Given** a validated resource with a capability-dependent shape where admins have an unrestricted `status` field and regular users have sh:in [Submitted, Cancelled], **When** an admin POSTs `status: Refunded`, **Then** validation passes.
2. **Given** the same resource, **When** a regular user POSTs `status: Refunded`, **Then** validation fails with a constraint violation (not a 403 -- the user is authorized, the data is invalid for their capability set).
3. **Given** a principal with no special capabilities, **When** the shape resolver runs, **Then** the base auto-derived shape (most restrictive) is applied.

---

### User Story 5 -- Custom Constraint Extensions (Priority: P4)

A Frank developer extends an auto-derived shape with custom SHACL constraints that the type system cannot express -- for example, cross-field validation (end date must be after start date), regex patterns on strings, or domain-specific value ranges. Custom constraints are additive and cannot remove auto-derived constraints.

**Why this priority**: While the type system covers most constraints, some domain rules are inherently runtime concerns. This is the escape hatch for when F# types are not expressive enough.

**Independent Test**: Define a resource with an auto-derived shape, add a custom sh:pattern constraint on a string field and a custom SPARQL-based cross-field constraint. Send requests that satisfy the type-level constraints but violate the custom ones. Verify the custom violations appear in the ValidationReport.

**Acceptance Scenarios**:

1. **Given** an auto-derived shape for a record with field `email: string`, **When** a custom sh:pattern constraint `^[^@]+@[^@]+$` is added, **Then** requests with `email: "not-an-email"` are rejected with a pattern violation.
2. **Given** auto-derived shapes for `startDate: DateTimeOffset` and `endDate: DateTimeOffset`, **When** a custom cross-field constraint requiring `endDate > startDate` is added, **Then** requests where `endDate <= startDate` are rejected.
3. **Given** a custom constraint added to a field that also has auto-derived constraints, **When** validation runs, **Then** both the auto-derived and custom constraints are evaluated, and violations from either source appear in the report.
4. **Given** an attempt to define a custom constraint that contradicts an auto-derived constraint (e.g., making a required field optional), **When** the application starts, **Then** a startup error is raised identifying the conflict.

---

### User Story 6 -- Response Validation Diagnostic Mode (Priority: P4)

A Frank developer enables response validation as a diagnostic tool to verify that handler return values conform to the expected output shape. This is opt-in, non-blocking, and logs violations without interfering with the response.

**Why this priority**: Response validation is a development-time diagnostic, not a production enforcement mechanism. Core request validation must be solid first.

**Independent Test**: Enable response validation on a resource, return data that violates the output shape from the handler, and verify a warning is logged but the response is delivered unmodified.

**Acceptance Scenarios**:

1. **Given** a validated resource with response validation enabled, **When** the handler returns data that violates the output shape, **Then** a warning is logged but the response is NOT blocked.
2. **Given** response validation is not enabled (default), **When** the handler returns data, **Then** no output validation occurs.

---

### Edge Cases

- What happens with recursive/self-referential F# types (e.g., a `TreeNode` containing `children: TreeNode list`)? Shape derivation MUST detect cycles and produce finite shapes using sh:node references without infinite expansion. A maximum derivation depth is applied (configurable, default 5).
- What happens with `option` types nested inside collections (e.g., `tags: string option list`)? The collection shape applies sh:minCount/sh:maxCount at the list level; individual items within the list use the option's inner type as sh:datatype.
- What happens with DU types that have data payloads per case (e.g., `Shape = Circle of float | Rectangle of float * float`)? Each case produces a separate NodeShape, and the parent property uses sh:or to allow any case shape. The discriminator field identifies which case applies.
- What happens with empty or missing request bodies on validated endpoints? A missing body when a shape requires at least one property is a validation failure, not a deserialization error. The ValidationReport cites the root NodeShape's sh:minCount constraint.
- What happens when auto-derived and custom constraints conflict? Custom constraints are additive only. At startup, the shape merger checks for contradictions (e.g., custom sh:minCount 0 on a field the type system requires) and raises a configuration error.
- What happens when a handler accepts `HttpContext` or other framework types directly instead of a domain record? No shape is derived for framework/infrastructure types. Validation is skipped for handlers with no derivable input shape, with a startup warning if `validate` was explicitly enabled on such a resource.
- What happens with generic types (e.g., `PagedResult<'T>`)? Generic types are expanded at the point of use -- `PagedResult<Customer>` produces a concrete NodeShape with `Customer`-specific property shapes. The generic definition itself is not a shape.

## Requirements

### Functional Requirements

- **FR-001**: System MUST derive SHACL NodeShapes from F# record types, with one sh:property per record field, each carrying the appropriate sh:datatype mapped from the F# type.
- **FR-002**: System MUST derive sh:minCount 1 for required fields and sh:minCount 0 for `option`-wrapped fields.
- **FR-003**: System MUST derive sh:in constraints from F# discriminated union types used as field types, listing each case name as an allowed value.
- **FR-004**: System MUST derive nested sh:node references for record types containing other record types, producing a separate NodeShape for each nested type.
- **FR-005**: System MUST handle recursive/self-referential types by detecting cycles and limiting derivation depth, configurable via the maxDerivationDepth parameter on deriveShape, default 5.
- **FR-006**: System MUST derive sh:or constraints for discriminated unions with data payloads, producing a separate NodeShape per case.
- **FR-007**: System MUST expand generic type parameters at the point of use, producing concrete NodeShapes for each instantiation.
- **FR-008**: System MUST validate incoming request bodies (POST/PUT/PATCH) and GET query parameters against derived shapes after Frank.Auth authorization and before handler dispatch in the middleware pipeline.
- **FR-009**: System MUST short-circuit with a 422 Unprocessable Content response when validation fails, preventing the handler from executing.
- **FR-010**: System MUST produce a SHACL ValidationReport for constraint violations, containing one sh:ValidationResult per violation with sh:focusNode, sh:resultPath, sh:value, sh:sourceConstraintComponent, and sh:resultMessage. sh:value is included for datatype, pattern, in, and node constraint violations; omitted for minCount/maxCount cardinality violations.
- **FR-011**: System MUST content-negotiate violation responses via Frank.LinkedData -- serving SHACL ValidationReport as JSON-LD, Turtle, or RDF/XML for semantic clients.
- **FR-012**: System MUST serve RFC 9457 Problem Details JSON for non-semantic clients (Accept: application/json or no semantic Accept header).
- **FR-013**: System MUST support capability-dependent shape selection, where the active SHACL shape varies based on the authenticated principal's Frank.Auth capabilities.
- **FR-014**: System MUST apply the base (most restrictive) auto-derived shape when no capability-specific shape override is configured.
- **FR-015**: System MUST support extending auto-derived shapes with custom SHACL constraints (sh:pattern, sh:minInclusive, sh:maxInclusive, cross-field SPARQL constraints, etc.).
- **FR-016**: Custom constraints MUST be additive only -- they cannot weaken auto-derived constraints. The system MUST detect contradictions at startup and raise a configuration error.
- **FR-017**: System MUST skip validation for handlers whose input type is not a derivable domain type (e.g., `HttpContext`, `HttpRequest`), with a startup warning if validation was explicitly enabled on such a resource.
- **FR-018**: System MUST provide a `validate` custom operation on the `ResourceBuilder` computation expression, following the same extension pattern as Frank.Auth and Frank.LinkedData.
- **FR-019**: System MUST support response validation (postconditions) as an opt-in diagnostic mode, validating handler return types against output shapes and logging violations without blocking the response.
- **FR-020**: System MUST derive sh:minCount/sh:maxCount constraints for F# collection types (list, array, seq) at the collection level, using the inner type's datatype for individual items.

### Key Entities

- **ShaclShape**: A SHACL NodeShape derived from an F# type definition. Represents the set of constraints (property shapes, cardinality, datatypes, value restrictions) that valid data must satisfy. Produced at application startup via reflection on handler input/output types.
- **PropertyShape**: A SHACL property shape within a NodeShape, corresponding to a single field of an F# record. Carries datatype, cardinality (minCount/maxCount), and optional value constraints (sh:in, sh:pattern, sh:node for nested types).
- **ValidationReport**: A W3C SHACL ValidationReport produced when request data violates one or more constraints. Contains the conformance status (sh:conforms) and a collection of ValidationResult entries. Serializable via Frank.LinkedData or as RFC 9457 Problem Details.
- **ValidationResult**: A single constraint violation within a ValidationReport. Identifies the focus node (the data being validated), the result path (which property failed), the offending value, the source constraint component, and a human-readable message.
- **ShapeDerivation**: The startup-time process that maps F# types to SHACL shapes. Handles records, discriminated unions, option types, collections, nested types, recursive types, and generic type instantiations.
- **ShapeResolver**: The runtime component that selects the appropriate SHACL shape for a given request, considering capability-dependent overrides from Frank.Auth. Falls back to the base auto-derived shape when no override applies.
- **ConstraintKind**: Discriminated union enumerating constraint types (Datatype, MinCount, MaxCount, In, Pattern, Node, Or) used within PropertyShape.
- **CustomConstraint**: A developer-provided SHACL constraint that extends an auto-derived shape. Additive only -- merged with the base shape at startup, with conflict detection.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All F# record types used as handler input types produce valid, well-formed SHACL NodeShapes that pass W3C SHACL syntax validation.
- **SC-002**: Invalid requests are rejected before reaching handler code -- a handler instrumented with a counter confirms zero invocations for requests that fail validation.
- **SC-003**: Violation reports contain sufficient detail for clients to self-correct: every sh:resultMessage includes the field name, the violated constraint type, and the expected vs. actual value.
- **SC-004**: Less than 1ms of overhead per request for shapes with up to 20 property constraints, measured as p95 latency delta with and without validation enabled.
- **SC-005**: Content negotiation for violation responses works correctly -- JSON-LD, Turtle, RDF/XML, and Problem Details JSON all produce valid, parseable output for the same set of violations.
- **SC-006**: Capability-dependent shape selection produces different validation outcomes for the same request body when sent by principals with different capabilities.
- **SC-007**: Custom constraints are correctly merged with auto-derived shapes, and conflicting constraints are detected at startup (not at request time).
- **SC-008**: Existing Frank applications without Frank.Validation experience zero behavioral changes -- all current tests continue to pass.
- **SC-009**: Shape derivation for 50 types completes in under 500ms at application startup.

## Assumptions

- Frank.LinkedData (#75) is complete and provides the content negotiation infrastructure for serializing SHACL ValidationReports as JSON-LD, Turtle, and RDF/XML.
- Frank.Auth is stable and provides the `ClaimsPrincipal`-based capability model that shape resolution integrates with.
- The SHACL shapes produced conform to W3C SHACL 1.0 (Shapes Constraint Language).
- Shape derivation uses .NET reflection on handler parameter types at application startup. No FSharp.Compiler.Service dependency is required -- the compiled types carry sufficient metadata.
- The `validate` custom operation follows the same `[<AutoOpen>] module` + `[<CustomOperation>]` extension pattern established by Frank.Auth and Frank.LinkedData.
- The library targets the same .NET versions as Frank core (net8.0, net9.0, net10.0 multi-targeting).
- `dotNetRdf.Core` is available as a shared dependency (established in Phase 1) for SHACL shape representation and ValidationReport construction.
- Response validation (postconditions) is diagnostic-only in this phase -- it logs but does not block responses. Full response validation enforcement is deferred to a future phase.
- Cross-field SPARQL-based constraints (FR-015) depend on dotNetRDF's SPARQL engine, which is already a transitive dependency via `dotNetRdf.Core`.
