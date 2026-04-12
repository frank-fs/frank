---
source: specs/002-datastar-sample
status: complete
type: spec
---

# Feature Specification: Frank.Datastar Sample Application

**Feature Branch**: `002-datastar-sample`
**Created**: 2026-01-27
**Status**: Draft
**Input**: User description: "create or update a sample application using Frank.Datastar in sample/Frank.Datastar.Basic to demonstrate use of Datastar with Frank. The example should show correct resource usage according to the resource model of HTTP, e.g. GET, POST, PUT, etc. against a url where the url references an identifiable stateful or stateless resource. Avoid RPC-style url+HTTP method for each action. Use F# string templates for the basic sample. A handful of examples from the datastar website would be useful, i.e. https://data-star.dev/examples/"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Learn RESTful Datastar Patterns (Priority: P1)

A developer exploring Frank.Datastar wants to understand how to build interactive web applications using proper HTTP resource semantics combined with Datastar's hypermedia-first approach. They run the sample application and see working examples of resources identified by URLs with appropriate HTTP methods.

**Why this priority**: This is the core educational purpose of the sample - demonstrating the correct integration of RESTful HTTP resource models with Datastar's SSE-based UI updates. Without this, developers may fall into RPC-style anti-patterns.

**Independent Test**: Can be fully tested by running the sample application and interacting with each example. Delivers immediate value by showing idiomatic Frank.Datastar usage patterns.

**Acceptance Scenarios**:

1. **Given** the sample application is running, **When** a developer navigates to the home page, **Then** they see a list of available examples organized by HTTP method and resource type.

2. **Given** the sample is running, **When** a developer clicks on the "Contact" example, **Then** they see a contact resource that can be viewed (GET), edited (PUT), and demonstrates proper resource identification via URL (e.g., `/contacts/1`).

3. **Given** the sample is running, **When** a developer views the code for any example, **Then** the URL structure identifies a resource (noun) rather than an action (verb).

---

### User Story 2 - Implement Click-to-Edit Pattern (Priority: P2)

A developer wants to implement inline editing for a resource. They reference the sample to see how to transition between view and edit modes using GET for retrieval and PUT for updates on the same resource URL.

**Why this priority**: Click-to-edit is one of the most common interactive patterns in web applications and demonstrates the fundamental GET/PUT resource pattern clearly.

**Independent Test**: Can be tested by interacting with a contact resource - viewing it, clicking edit, modifying fields, and saving changes. The resource URL remains constant while the representation changes.

**Acceptance Scenarios**:

1. **Given** a contact resource at `/contacts/{id}`, **When** I perform GET, **Then** I receive the read-only view of the contact.

2. **Given** a contact in view mode, **When** I click "Edit", **Then** a GET to `/contacts/{id}/edit` returns an editable form for the same resource.

3. **Given** a contact in edit mode with modified fields, **When** I click "Save", **Then** a PUT to `/contacts/{id}` updates the resource and returns the updated read-only view.

4. **Given** a contact in edit mode, **When** I click "Cancel", **Then** a GET to `/contacts/{id}` returns the original unmodified view.

---

### User Story 3 - Implement Search with Filtering (Priority: P2)

A developer wants to implement real-time search/filtering. They reference the sample to see how to treat search results as a queryable resource using GET with query parameters.

**Why this priority**: Search is ubiquitous and demonstrates how query parameters on a resource URL enable filtering without creating action-oriented endpoints.

**Independent Test**: Can be tested by typing in a search box and observing filtered results. The resource URL uses query parameters rather than RPC-style action endpoints.

**Acceptance Scenarios**:

1. **Given** a searchable collection at `/fruits`, **When** I perform GET without parameters, **Then** I receive all items in the collection.

2. **Given** a searchable collection, **When** I perform GET `/fruits?q=apple`, **Then** I receive only items matching the search term.

3. **Given** a search input bound to the query parameter, **When** I type characters, **Then** the results update via debounced GET requests to the same resource URL with updated query parameters.

---

### User Story 4 - Implement Row Deletion (Priority: P3)

A developer wants to implement item deletion from a collection. They reference the sample to see how DELETE on a specific resource URL removes that item.

**Why this priority**: Deletion demonstrates the DELETE method on a specific resource identifier, completing the CRUD pattern demonstration.

**Independent Test**: Can be tested by viewing a list of items, clicking delete on one, confirming the action, and observing the item removed from the list.

**Acceptance Scenarios**:

1. **Given** a collection with items at `/items`, **When** I view the collection, **Then** each item shows a delete button.

2. **Given** an item with id 1, **When** I click delete and confirm, **Then** a DELETE request to `/items/1` removes the item.

3. **Given** a successful deletion, **When** the server responds, **Then** the item is removed from the displayed list without a full page refresh.

---

### User Story 5 - Implement Bulk Operations (Priority: P3)

A developer wants to implement bulk updates on a collection. They reference the sample to see how PUT/PATCH on a collection resource can update multiple items.

**Why this priority**: Bulk operations demonstrate collection-level resource manipulation, showing that resources can be both individual items and collections.

**Independent Test**: Can be tested by selecting multiple items in a list and applying a status change (activate/deactivate) to all selected items at once.

**Acceptance Scenarios**:

1. **Given** a collection of users at `/users`, **When** I view the list, **Then** each row has a checkbox for selection.

2. **Given** multiple users selected, **When** I click "Activate Selected", **Then** a PUT to `/users/bulk` with the selected IDs updates their status.

3. **Given** a successful bulk update, **When** the server responds, **Then** the table reflects the updated status for all affected users.

---

### User Story 6 - Understand Form Validation Pattern (Priority: P3)

A developer wants to implement real-time form validation. They reference the sample to see how validation requests use POST to a validation resource while the form submission targets the actual resource.

**Why this priority**: Form validation demonstrates how to handle validation as a separate concern without polluting resource endpoints with validation-specific behavior.

**Independent Test**: Can be tested by filling out a form with invalid data and observing real-time validation feedback, then submitting valid data.

**Acceptance Scenarios**:

1. **Given** a registration form, **When** I type in form fields, **Then** debounced POST requests to `/registrations/validate` check field validity.

2. **Given** validation errors, **When** I correct the fields, **Then** the validation feedback updates to show success.

3. **Given** valid form data, **When** I submit the form, **Then** a POST to `/registrations` creates the new resource.

---

### Edge Cases

- **Not Found**: GET/PUT/DELETE on non-existent ID returns HTTP 404 with a simple HTML error message (e.g., "Contact not found").
- **Already Deleted**: DELETE on already-deleted resource returns HTTP 404 (resource no longer exists).
- **Validation Errors**: Empty or malformed input returns HTTP 400 with HTML listing specific validation errors.
- **Conflict**: POST to create a resource that already exists (e.g., duplicate email in registration) returns HTTP 409 with an HTML error message explaining the conflict.
- **Method Not Allowed**: Unsupported HTTP method on a resource (e.g., DELETE on `/fruits`) returns HTTP 405 with an HTML error message listing allowed methods.
- **Not Acceptable**: Request with unsupported Accept header returns HTTP 406 (optional - sample primarily returns HTML via SSE).
- **Concurrent Edits**: Out of scope for sample app (last-write-wins is acceptable for demo purposes).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The sample MUST demonstrate GET, POST, PUT, and DELETE HTTP methods on identifiable resources.

- **FR-002**: All resource URLs MUST follow REST conventions using nouns (e.g., `/contacts/{id}`) rather than verbs (e.g., `/getContact`, `/updateContact`).

- **FR-003**: The sample MUST include a contact resource demonstrating the click-to-edit pattern with GET for viewing and PUT for updating.

- **FR-004**: The sample MUST include a searchable collection demonstrating filtering via query parameters on GET requests.

- **FR-005**: The sample MUST include a collection demonstrating row deletion via DELETE on specific resource URLs.

- **FR-006**: The sample MUST include a collection demonstrating bulk status updates via PUT on the collection resource.

- **FR-007**: The sample MUST include a form demonstrating real-time validation with clear separation between validation endpoints and resource creation endpoints.

- **FR-008**: All HTML generation MUST use F# string templates (interpolated strings) rather than external templating libraries.

- **FR-009**: The sample MUST preserve the existing hypermedia-first examples (patchElements pattern) already in the codebase.

- **FR-010**: Each example MUST include a brief comment explaining the resource model being demonstrated.

- **FR-011**: The home page MUST organize examples by pattern type (Click-to-Edit, Search, Delete, Bulk Update, Validation) with clear descriptions.

### Key Entities

- **Contact**: Represents a person with first name, last name, and email. Identified by `/contacts/{id}`. Supports view, edit, and update operations.

- **Fruit**: Represents a searchable item with a name. Collection at `/fruits` supports filtering via query parameters.

- **Item**: Represents a deletable list item. Identified by `/items/{id}`. Supports deletion.

- **User**: Represents a user with name and status (active/inactive). Collection at `/users` supports bulk status updates.

- **Registration**: Represents a user registration. Validated at `/registrations/validate`, created at `/registrations`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Developers can run the sample and interact with all examples within 2 minutes of cloning the repository.

- **SC-002**: Each example demonstrates exactly one resource pattern, making it easy to understand and copy.

- **SC-003**: All HTTP methods used match their semantic meaning: GET for retrieval, POST for creation, PUT for full update, DELETE for removal.

- **SC-004**: Zero RPC-style endpoints exist in the sample (no `/doSomething`, `/performAction` URL patterns).

- **SC-005**: 100% of examples use F# string templates for HTML generation.

- **SC-006**: The sample compiles and runs without errors on .NET 8.0, 9.0, and 10.0.

## Assumptions

- The sample targets developers already familiar with F# basics and ASP.NET Core concepts.
- An `index.html` file with Datastar script references exists and will be updated to include navigation to new examples.
- In-memory data storage is acceptable for demonstration purposes (no persistent database required).
- The existing examples in `Program.fs` will be retained and enhanced, not replaced.
- Any new tests belong in `test/Frank.Datastar.Tests` and should test the Frank.Datastar library functionality, not the sample application itself.

## Clarifications

### Session 2026-01-27

- Q: How should edge cases (not found, already deleted, etc.) be handled? -> A: Return appropriate HTTP status codes (400, 404, 405, 409) with simple HTML error messages. See Edge Cases section for details.

## Research

# Research: Frank.Datastar Sample Application

**Feature Branch**: `002-datastar-sample`
**Date**: 2026-01-27

## Architecture: SSE Connection Model

### Key Constraint: Long-Lived Connections

SSE connections are **long-lived**. Opening multiple SSE connections per page exhausts browser connection limits, making the page unusable. Design for **one SSE connection per page** that serves as the channel for all UI updates.

### Which Endpoints Establish SSE?

Any HTTP method *can* establish an SSE connection, though some are more common:

| Method | Likelihood | Typical Use Case |
|--------|------------|------------------|
| GET | Common | On-load handler to fetch initial data and keep channel open |
| POST | Common | Form submission that streams validation, success, delayed redirect |
| PUT | Possible | Update that streams progress or confirmation |
| DELETE | Unlikely | Rarely needs streaming response |

The decision depends on whether the endpoint needs to:
- Send multiple/progressive updates
- Keep the connection open for subsequent server-initiated updates

### Fire-and-Forget Pattern

Endpoints that perform mutations without establishing SSE:
- Return appropriate HTTP status (200, 202, 404, etc.)
- UI updates are sent over the **existing SSE connection** (established elsewhere)
- No optimistic updates - client waits for server instruction via SSE

### Example: Items List with Deletion

1. **Page loads** -> GET `/items` establishes SSE, sends initial list, keeps connection open
2. **User clicks delete** -> DELETE `/items/1` returns 202 Accepted (no SSE)
3. **Server sends** `removeElement #item-1` over the existing SSE connection
4. **Client removes** element only after receiving server instruction

### Example: Form with Streaming Validation

1. **User submits** -> POST `/registrations` establishes SSE
2. **Server streams** validation feedback via `patchElements`
3. **On success** -> sends success message via `patchElements`
4. **After delay** -> sends `executeScript` to redirect page

---

## Research Questions Resolved

### RQ-001: How should click-to-edit pattern be implemented with proper REST semantics?

**Decision**: Use GET `/contacts/{id}` for read-only view, GET `/contacts/{id}/edit` for edit form, PUT `/contacts/{id}` for updates.

**Rationale**: The Datastar website example (https://data-star.dev/examples/click_to_edit) uses a similar pattern. The key insight is that the edit form is a separate representation of the same resource, accessed via a sub-resource URL. This maintains REST semantics where:
- GET retrieves a representation
- PUT replaces the resource state
- The URL identifies the resource, not the action

**Alternatives Considered**:
- Single endpoint with mode parameter (`?mode=edit`) - Rejected because query parameters should filter collections, not change representation type
- POST for editing - Rejected because POST creates new resources; PUT updates existing

**Implementation Notes**:
- Edit form uses `data-bind` for two-way binding with signals
- Save button triggers `@put('/contacts/{id}')` which sends signal values
- Cancel button triggers `@get('/contacts/{id}')` to restore read-only view
- Server reads signals via `tryReadSignals<Contact>` helper

---

### RQ-002: How should search/filtering be implemented?

**Decision**: Use GET `/fruits?q={term}` with debounced input binding.

**Rationale**: The Datastar active search example (https://data-star.dev/examples/active_search) demonstrates this pattern. Query parameters on a collection URL are the RESTful way to filter collections. This differs from RPC-style `/searchFruits` endpoints.

**Alternatives Considered**:
- POST with search body - Rejected because GET with query params is idiomatic for filtering
- Separate `/search` resource - Rejected as it's RPC-style

**Implementation Notes**:
- Input uses `data-bind:search` for signal binding
- Input uses `data-on:input__debounce.200ms="@get('/fruits')"` for debounced requests
- Server reads `q` from query string, filters in-memory collection
- Empty query returns all items

---

### RQ-003: How should row deletion be implemented?

**Decision**: Use DELETE `/items/{id}` as a fire-and-forget mutation, with UI updates sent over the existing SSE connection.

**Rationale**: DELETE on a specific resource URL is the RESTful approach. Since DELETE rarely needs to stream responses, it returns a simple HTTP status. The UI update (`removeElement`) is sent over the SSE connection established by the initial GET `/items` on page load.

**Alternatives Considered**:
- POST `/items/{id}/delete` - Rejected as RPC-style
- Soft delete with PUT - Rejected as overcomplicates the demo

**Implementation Notes**:
- Initial load: GET `/items` establishes SSE connection, sends item list, keeps connection open
- Delete button uses `data-on:click="confirm('Are you sure?') && @delete('/items/{id}')"`
- Server removes item from in-memory collection
- Server returns 202 Accepted (or 200 OK) with no body
- Server sends `removeElement #item-{id}` over the existing SSE connection
- 404 returned if item doesn't exist (already deleted)

---

### RQ-004: How should bulk updates be implemented?

**Decision**: Use PUT `/users/bulk` with array of selected user IDs. Two patterns are viable:

1. **Fire-and-forget**: PUT returns 202, table update sent over existing SSE connection
2. **PUT establishes SSE**: PUT streams the updated table directly (simpler for sample)

**Rationale**: The Datastar bulk update example (https://data-star.dev/examples/bulk_update) demonstrates checkbox selection with activate/deactivate buttons. Using PUT on a collection endpoint for bulk updates follows REST semantics.

**Alternatives Considered**:
- PATCH on collection - Viable but PUT is clearer for full status replacement
- Individual PUT calls - Rejected as inefficient for bulk operations
- PUT `/users` without `/bulk` - Viable but `/bulk` makes the intent clearer

**Implementation Notes**:
- Initial load: GET `/users` could establish SSE connection for the users section
- Checkboxes use `data-bind:selections` as array signal
- "Select all" checkbox syncs with individual selections via `data-effect`
- Activate/Deactivate buttons use `@put('/users/bulk?status=active')` or `?status=inactive`
- Server reads selections array from signals, status from query param
- Server updates user statuses and sends updated table via appropriate channel

---

### RQ-005: How should form validation be implemented?

**Decision**: Use POST `/registrations/validate` for real-time validation, POST `/registrations` for creation. Both establish SSE connections to stream responses.

**Rationale**: The Datastar inline validation example (https://data-star.dev/examples/inline_validation) shows debounced validation on input. POST is a common method to establish SSE for forms - it streams validation feedback, success messages, and can send delayed `executeScript` for redirects.

**Alternatives Considered**:
- Client-side only validation - Rejected because server validation is more authoritative
- Validation on same endpoint with `?validate=true` - Rejected as it conflates concerns

**Implementation Notes**:
- Inputs use `data-on:keydown__debounce.500ms="@post('/registrations/validate')"`
- Validation endpoint establishes SSE, streams validation feedback
- Submit button uses `@post('/registrations')` for actual creation
- POST `/registrations` establishes SSE to:
  - Stream validation errors if any
  - Send success message on valid submission
  - Optionally send delayed `executeScript` to redirect after success
- Server validates email format, required fields, uniqueness

---

### RQ-006: How to preserve existing examples while adding new ones?

**Decision**: Add new examples as separate sections in Program.fs and index.html, maintaining the existing structure and organization.

**Rationale**: FR-009 requires preserving existing hypermedia-first examples. The current structure has 10 examples organized by pattern type. New examples will be added in separate sections with clear demarcation.

**Implementation Notes**:
- Keep all existing examples (displayDate, removeDate, searchItems, etc.)
- Add new section headers for RESTful resource patterns
- Update index.html navigation to include new examples
- Maintain the existing "Primary Pattern" vs "Minimal Signals" organization

---

### RQ-007: What in-memory data structures are needed?

**Decision**: Use mutable dictionaries/lists wrapped in thread-safe access for demo purposes.

**Rationale**: The spec states "In-memory data storage is acceptable for demonstration purposes." This keeps the focus on Datastar patterns rather than data access complexity.

**Alternatives Considered**:
- Concurrent collections - Overkill for demo; adds complexity
- Persistent storage - Out of scope per spec

**Implementation Notes**:
- `contacts: Dictionary<int, Contact>` for click-to-edit
- `fruits: string list` for search (immutable)
- `items: ResizeArray<Item>` for deletion
- `users: Dictionary<int, User>` for bulk updates
- `registrations: ResizeArray<Registration>` for form validation

---

## Technology Best Practices Applied

### Datastar v1.0 Patterns

1. **Server-Sent Events (SSE)**: All Datastar interactions use SSE for server-to-client communication
2. **Signal Binding**: Use `data-bind:fieldname` for two-way binding
3. **Debouncing**: Use `__debounce.NNNms` modifier for input events
4. **Loading States**: Use `data-indicator:_fetching` and `data-attr:disabled="$_fetching"`
5. **HTML Patching**: Server sends HTML fragments via `patchElements`

### Frank Resource Model

1. **Resource CE**: Use `resource "/path" { ... }` for all endpoints
2. **HTTP Methods**: Use appropriate methods (get, put, post, delete) as CE operations
3. **Handler Signature**: `fun ctx -> task { ... }` for all handlers
4. **SSE Integration**: Use `datastar` CE operation for Datastar endpoints

### F# String Templates

1. **Interpolated Strings**: Use `$"..."` for HTML generation
2. **Multi-line**: Use triple-quoted strings `$"""..."""` for complex HTML
3. **No External Templates**: Per FR-008, no Razor, Scriban, etc.

---

## Integration Patterns

### URL Structure

| Resource | URL Pattern | Methods |
|----------|-------------|---------|
| Contact | `/contacts/{id}` | GET, PUT |
| Contact Edit Form | `/contacts/{id}/edit` | GET |
| Fruits Collection | `/fruits` | GET (with ?q param) |
| Item | `/items/{id}` | DELETE |
| Items Collection | `/items` | GET |
| Users Bulk | `/users/bulk` | PUT |
| Users Collection | `/users` | GET |
| Registration Validate | `/registrations/validate` | POST |
| Registrations | `/registrations` | POST |

### Error Handling

| Scenario | HTTP Status | Response |
|----------|-------------|----------|
| Resource not found | 404 | HTML error message |
| Validation error | 400 | HTML with field errors |
| Already deleted | 404 | HTML "not found" message |
| Success | 200 | HTML fragment or redirect |
