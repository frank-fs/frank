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

1. **Page loads** → GET `/items` establishes SSE, sends initial list, keeps connection open
2. **User clicks delete** → DELETE `/items/1` returns 202 Accepted (no SSE)
3. **Server sends** `removeElement #item-1` over the existing SSE connection
4. **Client removes** element only after receiving server instruction

### Example: Form with Streaming Validation

1. **User submits** → POST `/registrations` establishes SSE
2. **Server streams** validation feedback via `patchElements`
3. **On success** → sends success message via `patchElements`
4. **After delay** → sends `executeScript` to redirect page

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
