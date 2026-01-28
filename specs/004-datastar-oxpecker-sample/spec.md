# Feature Specification: Frank.Datastar.Oxpecker Sample

**Feature Branch**: `004-datastar-oxpecker-sample`
**Created**: 2026-01-27
**Status**: Draft
**Input**: User description: "Generate a sample matching the implementation of sample/Frank.Datastar.Basic and sample/Frank.Datastar.Hox in sample/Frank.Datastar.Oxpecker. Use the same test.sh script as is used in those projects to verify behavior. Use the Oxpecker.ViewEngine library."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - F# Developer Evaluates Oxpecker.ViewEngine with Datastar (Priority: P1)

An F# developer wants to see how Oxpecker.ViewEngine (computation expression-based HTML DSL) integrates with Frank and Datastar for building reactive web applications. They clone the Frank repository and run the Oxpecker sample to understand the patterns.

**Why this priority**: This is the core purpose of the sample - demonstrating the integration pattern between Frank.Datastar and Oxpecker.ViewEngine for developers evaluating their options.

**Independent Test**: Can be tested by building and running the sample application, then verifying all RESTful patterns work via the existing test.sh script.

**Acceptance Scenarios**:

1. **Given** a developer has cloned the Frank repository, **When** they navigate to sample/Frank.Datastar.Oxpecker and run `dotnet build`, **Then** the project compiles without errors.
2. **Given** the sample is running, **When** the developer runs `./test.sh`, **Then** all tests pass (same tests as Frank.Datastar.Basic and Frank.Datastar.Hox).
3. **Given** the sample is running, **When** the developer opens the application in a browser, **Then** they see a functional demo with all Datastar patterns working.

---

### User Story 2 - Developer Compares View Engine Approaches (Priority: P2)

A developer wants to compare the Oxpecker.ViewEngine approach (computation expressions) with the Hox approach (CSS selector DSL) and raw string templates for generating HTML in Datastar applications.

**Why this priority**: Having comparable implementations allows developers to make informed decisions about which view engine best fits their project needs.

**Independent Test**: Can be tested by comparing the Program.fs files across all three samples to verify they implement identical functionality with different HTML generation approaches.

**Acceptance Scenarios**:

1. **Given** the Oxpecker sample exists, **When** a developer compares it with Frank.Datastar.Basic, **Then** both implement the same RESTful resource patterns (contacts, fruits, items, users, registrations).
2. **Given** the Oxpecker sample exists, **When** a developer compares it with Frank.Datastar.Hox, **Then** the only significant difference is the HTML rendering approach (Oxpecker.ViewEngine vs Hox).
3. **Given** a developer reads VIEW_ENGINE_COMPARISON.md, **When** they review the Oxpecker sample code, **Then** the code demonstrates the patterns described in the comparison document.

---

### User Story 3 - Sample Passes Automated Testing (Priority: P3)

The sample must pass the same automated test suite as the other Datastar samples to ensure feature parity and correctness.

**Why this priority**: Test compatibility ensures the sample maintains the same behavior as existing samples and catches regressions.

**Independent Test**: Run test.sh against the running application and verify all tests pass.

**Acceptance Scenarios**:

1. **Given** the Oxpecker sample is running on port 5000, **When** `./test.sh` is executed, **Then** tests 11-28 all pass (same test numbers as other samples).
2. **Given** a contact resource exists, **When** a GET request is made to /contacts/1, **Then** an SSE stream with contact HTML is returned.
3. **Given** the fruits resource exists, **When** a GET request is made to /fruits?q=ap, **Then** HTTP 202 is returned (search query).

---

### Edge Cases

- What happens when an invalid contact ID is requested? Returns HTTP 404 with error message.
- What happens when a duplicate email is submitted for registration? Returns HTTP 409 conflict.
- What happens when signals JSON is malformed? Returns HTTP 400 bad request.
- What happens when an unsupported HTTP method is used? Returns HTTP 405 method not allowed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a sample project at `sample/Frank.Datastar.Oxpecker/` with identical functionality to `sample/Frank.Datastar.Basic` and `sample/Frank.Datastar.Hox`.
- **FR-002**: System MUST use `Oxpecker.ViewEngine` library for HTML generation instead of raw F# string templates or Hox.
- **FR-003**: Sample MUST implement all five RESTful resource patterns:
  - Click-to-Edit (Contact)
  - Search (Fruits)
  - Delete (Items)
  - Bulk Update (Users)
  - Form Validation (Registration)
- **FR-004**: Sample MUST use the same test.sh script from the other samples to verify behavior.
- **FR-005**: Sample MUST target .NET 10.0 to match the other Datastar samples.
- **FR-006**: Sample MUST reference `Frank.Datastar` project (via ProjectReference) for Datastar SSE functionality.
- **FR-007**: Sample MUST use Oxpecker.ViewEngine's `Render.toString` function (or equivalent) to convert HtmlElement to string for SSE responses.
- **FR-008**: Sample MUST use Oxpecker.ViewEngine's computation expression syntax (e.g., `div(id="foo") { p() { "text" } }`) for HTML generation.
- **FR-009**: Sample MUST support custom data-* attributes for Datastar bindings using the `.attr()` or `.data()` extension methods.

### Key Entities

- **Contact**: Represents a contact with Id, FirstName, LastName, Email - used for click-to-edit pattern.
- **Item**: Represents a deletable item with Id, Name - used for row deletion pattern.
- **User**: Represents a user with Id, Name, Email, Status (Active/Inactive) - used for bulk update pattern.
- **Registration**: Represents a registration with Id, Email, FirstName, LastName - used for form validation pattern.
- **Fruits**: Static list of fruit names - used for search/filter pattern.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 18 tests in test.sh pass when run against the Oxpecker sample (tests 11-28).
- **SC-002**: The project compiles without errors or warnings using `dotnet build`.
- **SC-003**: The sample demonstrates Oxpecker.ViewEngine's computation expression syntax clearly in all render functions.
- **SC-004**: The sample code structure mirrors the other Datastar samples for easy comparison.
- **SC-005**: No Hox or raw HTML string templates are used - all HTML is generated via Oxpecker.ViewEngine.
