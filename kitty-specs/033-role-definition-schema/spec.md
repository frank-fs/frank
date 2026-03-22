# Feature Specification: Role Definition Schema

**Feature Branch**: `033-role-definition-schema`
**Created**: 2026-03-21
**Status**: Draft
**Input**: User description: "#106 Role Definition Schema for statefulResource"

## User Scenarios & Testing

### User Story 1 - Declare Named Roles on a Stateful Resource (Priority: P1)

A framework user defining a multi-party stateful resource (e.g., a game, an order workflow) declares named roles with criteria that determine who qualifies for each role. Each role maps a human-readable name to a condition evaluated against the user's identity claims.

**Why this priority**: This is the foundational capability. Without role declarations, no downstream consumer — projection, content negotiation, progress analysis — can operate. Everything else depends on roles being formally declared.

**Independent Test**: Can be fully tested by defining a stateful resource with multiple roles and verifying that the role definitions appear in the resource's endpoint metadata at startup.

**Acceptance Scenarios**:

1. **Given** a stateful resource definition, **When** the developer adds named role declarations with identity-matching criteria, **Then** each role is captured in the resource's metadata with its name and matching rule.
2. **Given** a stateful resource with role declarations, **When** the application starts, **Then** the roles are available as part of the resource's endpoint metadata.
3. **Given** a stateful resource with no role declarations, **When** the application starts, **Then** the resource functions as before with no roles in metadata (backward compatible).

---

### User Story 2 - Resolve Roles at Request Time (Priority: P2)

When an authenticated user makes a request to a stateful resource, the system evaluates all declared roles against the user's identity and determines which roles the user holds. The resolved role set is cached via a typed feature interface and available for the duration of the request.

**Why this priority**: Guards, handlers, and downstream middleware all need to know which roles the current user holds. Eager resolution avoids repeated predicate evaluation and ensures consistency within a single request.

**Independent Test**: Can be tested by sending requests with different user identities to a stateful resource and verifying the correct role set is resolved for each.

**Acceptance Scenarios**:

1. **Given** a user whose identity matches two role criteria, **When** they make a request, **Then** both roles are resolved and available as an unordered set.
2. **Given** a user whose identity matches no role criteria, **When** they make a request, **Then** an empty role set is resolved.
3. **Given** an unauthenticated user, **When** they make a request, **Then** only roles whose criteria accept unauthenticated users (e.g., Observer) are resolved.
4. **Given** a user who makes multiple requests, **When** each request is processed, **Then** roles are resolved independently per request (no cross-request caching).

---

### User Story 3 - Reference Roles in Guard Logic (Priority: P3)

Guard predicates can check whether the current user holds a specific role by name, rather than directly inspecting identity claims. This makes guard logic declarative and self-documenting.

**Why this priority**: Current guard predicates manually inspect claims, which works for runtime enforcement but is opaque to tooling. Named role references make guards legible and enable the projection operator to reason about role-state relationships without reverse-engineering predicates.

**Independent Test**: Can be tested by writing a guard that references a role name and verifying it allows or blocks requests based on role membership.

**Acceptance Scenarios**:

1. **Given** a guard that checks for role "PlayerX", **When** a user holding role "PlayerX" makes a request, **Then** the guard allows the request.
2. **Given** a guard that checks for role "PlayerX", **When** a user NOT holding role "PlayerX" makes a request, **Then** the guard blocks the request with an appropriate reason.
3. **Given** a guard that checks for a role name that was never declared, **When** evaluated, **Then** the check returns false (role not held).

---

### User Story 4 - Extract Role Definitions for Spec Pipeline (Priority: P3)

The spec pipeline can extract role definitions from a running application's metadata and include them in generated specification artifacts. This enables downstream consumers — projection, ALPS generation, progress analysis — to reason about the role structure.

**Why this priority**: Role definitions are inputs to the projection operator (#107), ALPS profile generation, and progress analysis (#108). Without extractable role metadata, these downstream systems cannot function.

**Independent Test**: Can be tested by registering a stateful resource with roles and verifying that metadata extraction returns the role names and structure.

**Acceptance Scenarios**:

1. **Given** a stateful resource with role declarations, **When** the spec pipeline extracts metadata, **Then** role names are included in the extracted specification.
2. **Given** a stateful resource with roles, **When** role metadata is extracted from the running application's endpoint metadata, **Then** role information is included alongside state and capability data.

---

### Edge Cases

- What happens when two roles have the same name on the same resource? Rejected at startup — duplicate role names are a configuration error.
- What happens when a role predicate throws an exception during evaluation? The role is not resolved for that request; the error is logged.
- Can a resource have zero roles? Yes — backward compatible with existing stateful resources.
- How does role resolution interact with anonymous requests? Anonymous users receive a null/empty ClaimsPrincipal; only roles whose criteria accept this are resolved.
- Can the same user hold all declared roles simultaneously? Yes — roles are an unordered set, and if all predicates match, all roles are held.

## Requirements

### Functional Requirements

- **FR-001**: System MUST allow developers to declare named roles on a stateful resource, each with a name and identity-matching criterion.
- **FR-002**: System MUST resolve all matching roles for the current user at the start of each request and make them available as an unordered set for the duration of the request.
- **FR-003**: System MUST expose resolved roles through a typed feature interface, consistent with existing statechart feature patterns.
- **FR-004**: System MUST provide a mechanism for guard predicates to check role membership by name.
- **FR-005**: System MUST reject duplicate role names on the same resource at startup (fail-fast).
- **FR-006**: System MUST include role definitions in endpoint metadata so the spec pipeline can extract them.
- **FR-007**: System MUST provide a portable, zero-dependency role representation (name and optional human-readable description) for spec pipeline consumers, separate from the runtime predicate.
- **FR-008**: The portable role representation MUST be hierarchy-neutral — it must not assume flat or hierarchical state structures.
- **FR-009**: System MUST support users holding multiple roles simultaneously (union semantics).
- **FR-010**: Stateful resources without role declarations MUST continue to function unchanged (backward compatibility).

### Key Entities

- **RoleDefinition**: A named role with an identity-matching criterion. Per-resource, not global. The criterion is the source of truth for runtime evaluation; the name is a label for tooling, projection, and cross-validation.
- **RoleInfo**: A portable, zero-dependency representation of a role containing its name and optional descriptive metadata. Used by spec pipeline consumers. Hierarchy-neutral — does not assume flat or hierarchical state structures.
- **Resolved Role Set**: The unordered set of role names that a user holds for a given request, determined by evaluating all role criteria against the user's identity. Cached on a typed feature interface for the request lifetime.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Developers can declare roles on a stateful resource and have them appear in endpoint metadata without writing any infrastructure code.
- **SC-002**: Guard predicates can reference roles by name instead of directly inspecting identity claims.
- **SC-003**: Role definitions are extractable by the spec pipeline for inclusion in generated specification artifacts.
- **SC-004**: Existing stateful resources without role declarations continue to function with no changes required.
- **SC-005**: A user holding multiple roles has all matching roles resolved and available — no role is silently dropped.

## Assumptions

- ASP.NET Core's ClaimsPrincipal remains the authority for identity information. Role definitions label and index claims-based checks; they do not replace the claims infrastructure.
- The projection operator (#107) and progress analysis (#108) will consume role definitions as inputs but are not part of this feature's scope.
- Role definitions are per-resource. There is no global role registry.
- The predicate is the source of truth. Role names are labels for documentation, projection, and cross-validation.
- CLI source extraction of role declarations from F# source code is not in scope. Role names are available via runtime endpoint metadata (same current status as guard name extraction from source).

## Dependencies

- #87 (Core runtime library) — provides statefulResource CE (prerequisite, already complete).

## Related

- #107 (Projection Operator) — primary consumer of role definitions.
- #75 (Frank.LinkedData) — will use role definitions for projected content negotiation.
- #91 (Cross-Validator) — will use role definitions for projection consistency checks.
- #108 (Progress Analysis) — will use role definitions for deadlock/starvation detection.
- #135 (MPST Vocabulary) — needs role definitions for session type vocabulary.
