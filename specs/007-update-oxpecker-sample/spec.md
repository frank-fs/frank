# Feature Specification: Update Oxpecker Sample to Match Basic Sample

**Feature Branch**: `007-update-oxpecker-sample`
**Created**: 2026-02-02
**Status**: Draft
**Input**: User description: "Implement or update sample/Frank.Datastar.Oxpecker as a clone of sample/Frank.Datastar.Basic using the Oxpecker.ViewEngine. All tests should pass. sample/Frank.Datastar.Basic has been validated against the sample/Frank.Datastar.Tests and the Datastar samples. Its implementation should be used in place of the current implementation except where generating HTML markup is needed."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pass All Playwright Tests (Priority: P1)

As a developer running the test suite, I want the Frank.Datastar.Oxpecker sample to pass all tests in Frank.Datastar.Tests so that I can verify the Oxpecker.ViewEngine integration works correctly.

**Why this priority**: Tests are the primary acceptance criteria. If tests don't pass, the sample is not functional.

**Independent Test**: Run `DATASTAR_SAMPLE=Frank.Datastar.Oxpecker dotnet test sample/Frank.Datastar.Tests/` against the running Oxpecker sample server.

**Acceptance Scenarios**:

1. **Given** the Oxpecker sample server is running, **When** the ClickToEditTests test suite runs, **Then** all click-to-edit tests pass
2. **Given** the Oxpecker sample server is running, **When** the SearchFilterTests test suite runs, **Then** all search/filter tests pass
3. **Given** the Oxpecker sample server is running, **When** the BulkUpdateTests test suite runs, **Then** all bulk update tests pass
4. **Given** the Oxpecker sample server is running, **When** all test suites run, **Then** 100% of tests pass

---

### User Story 2 - Maintain Behavioral Parity with Basic Sample (Priority: P1)

As a developer comparing samples, I want the Oxpecker sample to behave identically to the Basic sample so that I can evaluate different view engine approaches with confidence that the underlying behavior is the same.

**Why this priority**: The Oxpecker sample exists to demonstrate the same Datastar patterns with a different view engine. Behavioral differences would defeat this purpose.

**Independent Test**: Run the same test suite against both samples and verify identical behavior.

**Acceptance Scenarios**:

1. **Given** a contact is displayed in view mode, **When** the user clicks Edit, **Then** the edit form appears with the same input fields and data-* attributes as the Basic sample
2. **Given** the user searches for fruits, **When** a filter query is entered, **Then** the filtered list updates via SSE identical to the Basic sample
3. **Given** multiple users are selected, **When** bulk activate/deactivate is clicked, **Then** the users table updates via SSE identical to the Basic sample
4. **Given** an item delete is confirmed, **When** the delete action executes, **Then** the item row is removed via SSE identical to the Basic sample

---

### User Story 3 - Use Oxpecker.ViewEngine for HTML Generation (Priority: P1)

As a developer learning Frank+Datastar, I want to see how Oxpecker.ViewEngine's computation expression syntax generates the same HTML output so that I can choose the view engine that fits my coding style.

**Why this priority**: This is the differentiating purpose of this sample - demonstrating Oxpecker.ViewEngine syntax.

**Independent Test**: Inspect the generated HTML in browser dev tools and verify it matches the Basic sample's HTML structure.

**Acceptance Scenarios**:

1. **Given** the contact view renders, **When** I inspect the HTML, **Then** it has the same `id`, `data-*` attributes, and structure as the Basic sample
2. **Given** the fruits list renders, **When** I inspect the HTML, **Then** the `<ul id="fruits-list">` contains `<li>` elements for each fruit
3. **Given** the items table renders, **When** I inspect the HTML, **Then** each row has `id="item-{id}"` and the delete button has the same `data-on:click` attribute

---

### Edge Cases

- What happens when the SSE connection is closed by the client? The server should clean up subscriptions gracefully.
- What happens when a contact/item/user ID doesn't exist? The server should return a 404 status code.
- What happens when signals are malformed or missing? The server should return a 400 status code.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Oxpecker sample MUST use the same SSE broadcast architecture as the Basic sample (single `/sse` endpoint with SseEvent.subscribe/broadcast pattern)
- **FR-002**: The Oxpecker sample MUST use identical HTTP status codes as the Basic sample (202 for fire-and-forget, 201 for creation, 404 for not found, 400 for bad request)
- **FR-003**: The Oxpecker sample MUST produce HTML with the same element IDs and data-* attributes as the Basic sample
- **FR-004**: The Oxpecker sample MUST use Oxpecker.ViewEngine computation expressions (`div()`, `button()`, `.attr()`) instead of string interpolation for HTML generation
- **FR-005**: The Oxpecker sample MUST implement all five demo patterns: Click-to-Edit (Contact), Search (Fruits), Delete (Items), Bulk Update (Users), Form Validation (Registration)
- **FR-006**: The Oxpecker sample MUST share the same data types (`Contact`, `ContactSignals`, `User`, `UserStatus`, `Item`, `Registration`, etc.) as the Basic sample
- **FR-007**: The Oxpecker sample MUST share the same in-memory data stores and initialization values as the Basic sample
- **FR-008**: The Oxpecker sample MUST use the same resource URL patterns as the Basic sample (`/contacts/{id}`, `/fruits`, `/items/{id}`, etc.)
- **FR-009**: All render functions MUST use `Render.toString` to convert Oxpecker elements to HTML strings for the SSE broadcast system

### Key Entities

- **Contact**: User contact information with Id, FirstName, LastName, Email. Demonstrates click-to-edit pattern.
- **User**: User account with Id, Name, Email, Status (Active/Inactive). Demonstrates bulk update pattern.
- **Item**: Simple item with Id, Name. Demonstrates row deletion pattern.
- **Registration**: Form submission with Id, Email, FirstName, LastName. Demonstrates form validation pattern.
- **SseEvent**: Discriminated union for SSE event types (PatchElements, RemoveElement, PatchSignals).

## Assumptions

- The existing Basic sample implementation is correct and serves as the reference implementation.
- The tests in Frank.Datastar.Tests are comprehensive and test all required behaviors.
- Oxpecker.ViewEngine is already a project dependency (it is in the current fsproj).
- The static files (index.html, CSS) are shared or identical between samples.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of Frank.Datastar.Tests pass when run against the Oxpecker sample
- **SC-002**: The generated HTML structure matches the Basic sample for all five demo patterns (verified by tests)
- **SC-003**: All SSE-driven updates work correctly (contact edit/save, fruit search, item delete, bulk user update, registration validation)
- **SC-004**: The codebase demonstrates clear use of Oxpecker.ViewEngine syntax without falling back to string interpolation for any HTML rendering
