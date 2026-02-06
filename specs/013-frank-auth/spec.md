# Feature Specification: Resource-Level Authorization Library (Frank.Auth)

**Feature Branch**: `013-frank-auth`
**Created**: 2026-02-05
**Status**: Draft
**Input**: User description: "Add a new library, Frank.Auth, that provides a ResourceBuilder extension. A draft of a specification can be found in /Users/ryanr/Downloads/frank-auth-specification.md"

## Clarifications

### Session 2026-02-05

- Q: Are all types and integration points accessible from the new library, or is anything internal/private that would be required? → A: Frank core's `ResourceSpec` record cannot be extended with new fields from an external library (F# type extensions can add methods but not record fields). The `ResourceSpec.Build()` method constructs endpoints with hardcoded metadata and provides no hook for attaching additional metadata such as authorization policies. Frank core MUST be updated with a generic endpoint metadata extensibility point — a `Metadata : (EndpointBuilder -> unit) list` field on `ResourceSpec` — that Frank.Auth (and future extensions) can use to attach metadata to endpoints during resource construction. `Build()` must switch from constructing `RouteEndpoint` directly to using `RouteEndpointBuilder`, applying metadata functions before calling `Build()`.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Restrict Resource to Authenticated Users (Priority: P1)

As a Frank developer, I want to mark a resource as requiring authentication so that only users with a verified identity can access it, while unauthenticated requests are automatically rejected.

**Why this priority**: Authentication gating is the most fundamental authorization requirement. It serves as the foundation for all other authorization patterns and is the most common need across web applications.

**Independent Test**: Can be fully tested by defining a resource with an authentication requirement and verifying that authenticated requests succeed while unauthenticated requests are rejected with the appropriate status.

**Acceptance Scenarios**:

1. **Given** a resource with an authentication requirement, **When** an unauthenticated request is made, **Then** the system returns a 401 Unauthorized response without executing the resource handler.
2. **Given** a resource with an authentication requirement, **When** an authenticated request is made, **Then** the request proceeds to the resource handler normally.
3. **Given** a resource with no authorization requirements, **When** any request is made, **Then** the request proceeds to the resource handler regardless of authentication status.

---

### User Story 2 - Restrict Resource by Claim (Priority: P1)

As a Frank developer, I want to require specific claims on a resource so that only users possessing the correct claim type and value can access it, enabling fine-grained access control.

**Why this priority**: Claim-based authorization is the most versatile authorization pattern and the primary mechanism for expressing domain-specific access rules. It is equally important as basic authentication gating.

**Independent Test**: Can be fully tested by defining a resource with a claim requirement and verifying that users with the correct claim are granted access while users without it are rejected.

**Acceptance Scenarios**:

1. **Given** a resource requiring a specific claim with a single value, **When** a user possessing that exact claim and value makes a request, **Then** the request proceeds to the resource handler.
2. **Given** a resource requiring a specific claim with a single value, **When** a user lacking that claim makes a request, **Then** the system returns a 403 Forbidden response.
3. **Given** a resource requiring a claim with multiple accepted values, **When** a user possessing any one of the accepted values makes a request, **Then** the request proceeds to the resource handler.
4. **Given** a resource requiring a claim with multiple accepted values, **When** a user possessing none of the accepted values makes a request, **Then** the system returns a 403 Forbidden response.
5. **Given** a resource with two separate claim requirements (e.g., "scope=admin" and "department=engineering"), **When** a user satisfies only one requirement, **Then** the system returns a 403 Forbidden response.
6. **Given** a resource with two separate claim requirements, **When** a user satisfies both requirements, **Then** the request proceeds to the resource handler.

---

### User Story 3 - Restrict Resource by Role (Priority: P2)

As a Frank developer, I want to require a specific role on a resource so that only users belonging to that role can access it.

**Why this priority**: Role-based access control is a widely understood authorization pattern and a common requirement, but it is a subset of claim-based authorization in terms of underlying capability.

**Independent Test**: Can be fully tested by defining a resource with a role requirement and verifying that users in the role are granted access while others are rejected.

**Acceptance Scenarios**:

1. **Given** a resource requiring a specific role, **When** a user belonging to that role makes a request, **Then** the request proceeds to the resource handler.
2. **Given** a resource requiring a specific role, **When** a user not belonging to that role makes a request, **Then** the system returns a 403 Forbidden response.

---

### User Story 4 - Restrict Resource by Named Policy (Priority: P2)

As a Frank developer, I want to reference a named authorization policy on a resource so that I can apply complex authorization logic configured at the application level without duplicating it across resources.

**Why this priority**: Named policies enable reusable, centrally-managed authorization logic for scenarios too complex for simple claim or role checks.

**Independent Test**: Can be fully tested by registering a named policy at the application level, referencing it on a resource, and verifying that the policy is enforced.

**Acceptance Scenarios**:

1. **Given** a named policy registered at the application level and a resource referencing that policy, **When** a user satisfying the policy makes a request, **Then** the request proceeds to the resource handler.
2. **Given** a named policy registered at the application level and a resource referencing that policy, **When** a user not satisfying the policy makes a request, **Then** the system returns a 403 Forbidden response.

---

### User Story 5 - Configure Authentication and Authorization at Application Level (Priority: P1)

As a Frank developer, I want to register authentication and authorization services and configure policies using Frank's builder syntax so that I can set up the entire authorization infrastructure within the same compositional style as the rest of my Frank application.

**Why this priority**: Without application-level service registration, none of the resource-level authorization features can function. This is the foundational wiring that enables all other stories.

**Independent Test**: Can be fully tested by configuring authentication and authorization services via the builder and verifying that protected resources enforce their requirements correctly.

**Acceptance Scenarios**:

1. **Given** a Frank application with authentication and authorization services registered via the builder, **When** a protected resource receives a request, **Then** authorization is evaluated as configured.
2. **Given** a Frank application with a named authorization policy registered via the builder, **When** a resource references that policy name, **Then** the policy is resolved and enforced correctly.

---

### User Story 6 - Compose Multiple Authorization Requirements (Priority: P2)

As a Frank developer, I want to apply multiple authorization requirements to a single resource so that access is granted only when all requirements are satisfied (AND semantics).

**Why this priority**: Composability of authorization constraints is essential for real-world security policies, but depends on the individual requirement types being available first.

**Independent Test**: Can be fully tested by combining authentication, claim, role, and policy requirements on a single resource and verifying that all must pass.

**Acceptance Scenarios**:

1. **Given** a resource requiring authentication, a specific claim, and a specific role, **When** a user satisfies all three requirements, **Then** the request proceeds to the resource handler.
2. **Given** a resource requiring authentication, a specific claim, and a specific role, **When** a user satisfies only two of three requirements, **Then** the system returns a 403 Forbidden response.

---

### Edge Cases

- What happens when a resource has no authorization requirements? The resource is publicly accessible with no authorization overhead.
- What happens when an unauthenticated user accesses a resource requiring a specific claim? The system returns 401 Unauthorized (no identity established) rather than 403 Forbidden.
- What happens when a resource references a named policy that has not been registered? ASP.NET Core's authorization middleware raises an `InvalidOperationException` at request time when the policy cannot be resolved. Frank.Auth does not add custom handling for this — it relies on the platform's default behavior (typically a 500 response in production, or an exception in development).
- What happens when multiple claim requirements are applied with the same claim type but different values? Each requirement is evaluated independently (AND semantics across requirements); the user must satisfy each one.
- What happens when an empty list of claim values is provided? This is treated as a requirement that the claim type exists with any value.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a separate library (Frank.Auth) that extends Frank's core builder types with authorization capabilities.
- **FR-002**: The system MUST provide a `requireAuth` operation on the resource builder that marks a resource as requiring an authenticated user.
- **FR-003**: The system MUST provide a `requireClaim` operation on the resource builder that requires a specific claim type with a single accepted value.
- **FR-004**: The system MUST provide a `requireClaim` operation on the resource builder that requires a specific claim type with multiple accepted values (OR semantics: user must possess at least one).
- **FR-005**: The system MUST provide a `requireRole` operation on the resource builder that requires membership in a named role.
- **FR-006**: The system MUST provide a `requirePolicy` operation on the resource builder that delegates to a named authorization policy.
- **FR-007**: Multiple authorization requirements on a single resource MUST use AND semantics — all requirements must be satisfied for access to be granted.
- **FR-008**: Resources with no authorization requirements MUST remain publicly accessible with zero authorization overhead.
- **FR-009**: The system MUST provide builder-level operations (`useAuthentication`, `useAuthorization`, `authorizationPolicy`) for registering authentication and authorization services at the application level.
- **FR-010**: Authorization failures for unauthenticated users MUST result in a 401 Unauthorized response.
- **FR-011**: Authorization failures for authenticated users with insufficient claims, roles, or policy compliance MUST result in a 403 Forbidden response.
- **FR-012**: Authorization enforcement MUST occur before the resource handler executes — unauthorized requests must never reach application handler code.
- **FR-013**: Frank.Auth MUST be packaged and versioned independently from Frank core, validating Frank's extensibility model.
- **FR-014**: Within a single `requireClaim` operation specifying multiple values, the user MUST possess at least one of the listed values (OR semantics within a requirement).
- **FR-015**: Across multiple `requireClaim` operations on the same resource, the user MUST satisfy each operation independently (AND semantics across requirements).
- **FR-016**: Frank core MUST provide a `Metadata : (EndpointBuilder -> unit) list` field on `ResourceSpec` that allows extension libraries to attach endpoint metadata via convention functions during resource construction, without requiring further core modifications per extension. `Build()` MUST switch from constructing `RouteEndpoint` directly to using `RouteEndpointBuilder`, applying all metadata functions before calling `Build()`. This internal change is transparent — the return type and endpoint behavior are unchanged.
- **FR-017**: The core extensibility point MUST be generic — not authorization-specific — so that future extensions (e.g., CORS, rate limiting, OpenAPI metadata) can use the same mechanism. The `(EndpointBuilder -> unit)` function type allows any extension to add typed metadata without exposing `obj` in the public API.
- **FR-018**: The core extensibility point MUST preserve backward compatibility: existing resources that do not use metadata MUST continue to work without modification and incur no additional overhead. The default value is an empty list, and `Build()` produces identical endpoints when no metadata functions are present.

### Key Entities

- **Endpoint Metadata** (Frank core): A `Metadata : (EndpointBuilder -> unit) list` field on `ResourceSpec`. Extension libraries provide typed convention functions that configure `EndpointBuilder.Metadata` during resource construction, without modifying Frank core per extension. Empty by default — existing resources are unaffected.
- **AuthRequirement** (Frank.Auth): A single authorization constraint applied to a resource. Variants include: authenticated user, claim with values, named role, and named policy.
- **AuthConfig** (Frank.Auth): The collected set of authorization requirements for a resource. Empty by default (no authorization required). Requirements are additive. Translated into typed `(EndpointBuilder -> unit)` convention functions during resource construction.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All acceptance scenarios across all user stories pass automated testing.
- **SC-002**: Existing Frank applications without Frank.Auth continue to work without modification (the core extensibility point is additive and backward-compatible).
- **SC-003**: Resources with no authorization requirements incur zero additional processing overhead.
- **SC-004**: Developers can add authorization to a resource by adding a single line (e.g., `requireAuth`) to an existing resource definition.
- **SC-005**: The authorization operations follow the same compositional patterns and naming conventions as existing Frank builder operations.
- **SC-006**: Frank.Auth can be added to a project as an independent package dependency without forcing unnecessary transitive dependencies on projects that don't use authorization.

## Scope

**In Scope**:
- A generic endpoint metadata extensibility point (`Metadata : (EndpointBuilder -> unit) list`) on Frank core's `ResourceSpec`, with `Build()` switching internally to `RouteEndpointBuilder`
- New Frank.Auth library with resource builder extensions for `requireAuth`, `requireClaim`, `requireRole`, and `requirePolicy`
- Application-level builder operations for `useAuthentication`, `useAuthorization`, and `authorizationPolicy`
- Authorization enforcement that translates requirements into endpoint metadata via the core extensibility point
- Unit and integration tests for all authorization patterns (plus tests for the core extensibility point itself)
- Documentation updates for the README

**Out of Scope**:
- Authentication scheme configuration (JWT, cookie, API key setup) — these are configured via the platform's authentication builder, not by Frank.Auth
- Per-HTTP-method authorization (e.g., GET requires "read", POST requires "write") — deferred to a future specification
- Custom authorization handler or requirement types beyond platform-provided patterns
- Rate limiting, IP filtering, CORS, and other security concerns orthogonal to identity-based authorization
- Provider-specific packages (e.g., Frank.Auth.JwtBearer)

## Non-Goals

- Frank.Auth does not prescribe authentication schemes — those are configured at the application host level
- Frank.Auth does not introduce custom authorization handler abstractions — it uses the platform's built-in authorization infrastructure
- Frank.Auth does not handle custom challenge or forbid response formatting — those are configured via authentication options at the host level

## Assumptions

- Frank core's `ResourceBuilder` and `WebHostBuilder` are public and extensible via F# type extensions, allowing Frank.Auth to add custom operations (confirmed by codebase review)
- Frank core's `ResourceSpec` record currently has only `Name` and `Handlers` fields; it MUST be extended with a `Metadata : (EndpointBuilder -> unit) list` field to support endpoint metadata from extension libraries (this is a required core change, not an assumption — see FR-016 through FR-018)
- Frank core's `ResourceSpec.Build()` method currently constructs `RouteEndpoint` directly; it MUST switch internally to `RouteEndpointBuilder` and apply collected metadata functions before calling `Build()`. This is an internal change — the return type and public behavior are unchanged.
- The platform's authorization middleware handles 401/403 response generation — Frank.Auth provides typed convention functions that add authorization metadata to `EndpointBuilder.Metadata`
- F# 6.0+ is available, enabling custom operation overloads for the `requireClaim` variants
- Developers are responsible for registering authentication schemes at the host level before using Frank.Auth's resource-level operations
- The core extensibility point is designed to be generic so that Frank.Datastar and other future extensions could also adopt it (but migrating existing extensions is out of scope for this feature)
