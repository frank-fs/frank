---
source: specs/008-update-hox-sample
status: complete
type: spec
---

# Feature Specification: Update Frank.Datastar.Hox Sample

**Feature Branch**: `008-update-hox-sample`
**Created**: 2026-02-02
**Status**: Draft
**Input**: User description: "Implement or update sample/Frank.Datastar.Hox as a clone of sample/Frank.Datastar.Basic using the Hox view engine (../Hox or https://hox.tunaxor.me/). All tests should pass. sample/Frank.Datastar.Basic has been validated against the sample/Frank.Datastar.Tests and the Datastar samples. Its implementation should be used in place of the current implementation except where generating HTML markup is needed."

## Clarifications

### Session 2026-02-02

- Q: Does Hox `Render.asString` return sync or async? → A: Returns `ValueTask<string>`; callers must await

## Overview

This specification defines the update of the Frank.Datastar.Hox sample application to align with the validated Frank.Datastar.Basic implementation. The Hox sample must replicate the exact architecture, SSE broadcasting pattern, and REST API behavior of the Basic sample while using Hox's CSS-selector-based DSL (`h()` function) for HTML generation instead of F# string templates.

The key difference from the current Hox implementation:
- **Current**: Uses separate MailboxProcessor channels per demo section (click-to-edit, search, etc.)
- **Target**: Uses a single SSE endpoint (`/sse`) with a shared broadcast channel (matching Basic sample architecture)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pass All Existing Playwright Tests (Priority: P1)

A developer wants to verify that the updated Hox sample passes all existing Playwright tests in Frank.Datastar.Tests without modification.

**Why this priority**: Test compatibility is the primary success criterion. The Basic sample has been validated against these tests, so passing them proves functional equivalence.

**Independent Test**: Start the Hox sample, run `DATASTAR_SAMPLE=Frank.Datastar.Hox dotnet test sample/Frank.Datastar.Tests/` - all tests must pass.

**Acceptance Scenarios**:

1. **Given** the Hox sample is running on port 5000, **When** the Playwright test suite is executed, **Then** all Click-to-Edit tests pass
2. **Given** the Hox sample is running on port 5000, **When** the Playwright test suite is executed, **Then** all Search Filter tests pass
3. **Given** the Hox sample is running on port 5000, **When** the Playwright test suite is executed, **Then** all Bulk Update tests pass
4. **Given** the Hox sample is running on port 5000, **When** the Playwright test suite is executed, **Then** all State Isolation tests pass

---

### User Story 2 - Single SSE Endpoint Architecture (Priority: P1)

A developer wants the Hox sample to use the same SSE architecture as the Basic sample - a single `/sse` endpoint that all client interactions broadcast to.

**Why this priority**: The current Hox implementation uses separate MailboxProcessor channels per section, which differs from the Basic sample's validated architecture.

**Independent Test**: Verify the Hox sample has a single `/sse` resource and uses the shared SseEvent broadcast pattern.

**Acceptance Scenarios**:

1. **Given** the Hox sample code, **When** examining the SSE implementation, **Then** there is exactly one `/sse` resource endpoint
2. **Given** multiple browser tabs connected to `/sse`, **When** a data change occurs, **Then** all connected tabs receive the SSE update
3. **Given** the broadcast channel pattern, **When** a contact is updated via PUT, **Then** the update is broadcast to all subscribed SSE connections

---

### User Story 3 - Click-to-Edit Pattern with Hox Views (Priority: P2)

A developer wants to edit a contact inline using the click-to-edit pattern with Hox-rendered HTML.

**Why this priority**: Click-to-edit is a fundamental CRUD pattern that demonstrates the view/edit state switching.

**Independent Test**: Load a contact, click Edit, modify fields, save - verify state transitions work and data persists.

**Acceptance Scenarios**:

1. **Given** the page has established SSE via `/sse`, **When** clicking "Load Contact" triggers GET `/contacts/1`, **Then** the contact view appears with name, email, and Edit button
2. **Given** a contact view is displayed, **When** clicking Edit triggers GET `/contacts/1/edit`, **Then** the edit form appears with input fields populated with current values
3. **Given** an edit form is displayed with modified values, **When** clicking Save triggers PUT `/contacts/1`, **Then** the updated view appears and data persists after page refresh
4. **Given** an edit form is displayed, **When** clicking Cancel triggers GET `/contacts/1`, **Then** the original view appears without saving changes

---

### User Story 4 - Search with Filtering Pattern (Priority: P2)

A developer wants to filter a collection using search input with debounced queries.

**Why this priority**: Search/filter demonstrates GET with query parameters and real-time list updates.

**Independent Test**: Load fruits, type search term, verify filtered results appear.

**Acceptance Scenarios**:

1. **Given** the page has established SSE, **When** clicking "Load Fruits" triggers GET `/fruits`, **Then** all 22 fruits appear in the list
2. **Given** a fruits list is displayed, **When** typing "ap" in search (triggering GET `/fruits?q=ap`), **Then** only matching fruits appear (Apple, Apricot, Grape, Papaya)
3. **Given** a filtered list, **When** clearing the search input (triggering GET `/fruits?q=`), **Then** the full list of fruits is restored
4. **Given** a search for "xyz", **When** no fruits match, **Then** an empty list is displayed (not an error)

---

### User Story 5 - Row Deletion Pattern (Priority: P3)

A developer wants to delete items from a table using HTTP DELETE.

**Why this priority**: Delete demonstrates fire-and-forget patterns with removeElement SSE events.

**Independent Test**: Load items, delete one, verify it disappears without page refresh.

**Acceptance Scenarios**:

1. **Given** the page has established SSE, **When** clicking "Load Items" triggers GET `/items`, **Then** the items table appears with 4 items
2. **Given** an items table, **When** clicking delete on an item triggers DELETE `/items/{id}`, **Then** the row disappears and HTTP 202 is returned
3. **Given** an attempt to delete a non-existent item, **When** DELETE `/items/99` is called, **Then** HTTP 404 is returned

---

### User Story 6 - Bulk Update Pattern (Priority: P3)

A developer wants to update multiple user statuses at once using checkboxes and bulk action buttons.

**Why this priority**: Bulk updates demonstrate collection-level operations with array signals.

**Independent Test**: Load users, select multiple, activate/deactivate, verify status changes.

**Acceptance Scenarios**:

1. **Given** the page has established SSE, **When** clicking "Load Users" triggers GET `/users`, **Then** the users table appears with 4 users and checkboxes
2. **Given** users selected via checkboxes, **When** clicking "Activate Selected" triggers PUT `/users/bulk?status=active`, **Then** selected users show Active status
3. **Given** users selected via checkboxes, **When** clicking "Deactivate Selected" triggers PUT `/users/bulk?status=inactive`, **Then** selected users show Inactive status
4. **Given** no users selected, **When** clicking bulk action buttons, **Then** no user statuses change

---

### User Story 7 - Form Validation Pattern (Priority: P3)

A developer wants to see real-time validation feedback as they fill out a registration form.

**Why this priority**: Form validation demonstrates POST for validation and creation with appropriate status codes.

**Independent Test**: Load form, enter invalid data, see errors, fix data, submit successfully.

**Acceptance Scenarios**:

1. **Given** the page has established SSE, **When** clicking "Show Registration Form" triggers GET `/registrations/form`, **Then** the registration form appears
2. **Given** an empty form, **When** validation triggers via POST `/registrations/validate`, **Then** validation errors appear ("Email is required", etc.)
3. **Given** valid form data, **When** submitting via POST `/registrations`, **Then** success message appears and HTTP 201 is returned
4. **Given** a duplicate email, **When** submitting, **Then** error message appears and HTTP 409 is returned

---

### Edge Cases

- What happens when SSE connection closes? The subscriber is removed from the broadcast list.
- How does the system handle concurrent SSE connections? Each connection subscribes independently and receives all broadcasts.
- What happens with malformed JSON signals? HTTP 400 Bad Request is returned.
- What happens when searching with empty query string? Full list is returned.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Sample MUST replace the existing MailboxProcessor-based SSE architecture with the single-channel broadcast pattern from Frank.Datastar.Basic
- **FR-002**: Sample MUST implement a single `/sse` endpoint that handles all SSE connections
- **FR-003**: Sample MUST use the SseEvent type with subscribe/unsubscribe/broadcast pattern matching Frank.Datastar.Basic
- **FR-004**: Sample MUST use Hox's `h()` CSS-selector DSL for all HTML generation (replacing F# string templates)
- **FR-005**: Sample MUST use `Hox.Rendering.Render.asString` for converting Hox nodes to HTML strings
- **FR-006**: Sample MUST implement all render functions to produce HTML identical in structure to Frank.Datastar.Basic (same IDs, classes, Datastar attributes)
- **FR-007**: Sample MUST return correct HTTP status codes matching Basic: 200/201/202 for success, 400/404/409 for errors
- **FR-008**: Sample MUST use the same in-memory data stores pattern (Dictionary for contacts/users, ResizeArray for items/registrations)
- **FR-009**: Sample MUST use the same wwwroot/index.html file (client-side code is identical)
- **FR-010**: Sample MUST implement the `datastar` resource builder for the SSE endpoint
- **FR-011**: Render functions MUST return `ValueTask<string>` (from `Render.asString`); all callers MUST await the result before broadcasting

### Key Entities

- **Contact**: Id, FirstName, LastName, Email - supports view/edit modes via `renderContactView` and `renderContactEdit`
- **User**: Id, Name, Email, Status (Active/Inactive) - supports bulk status updates via `renderUsersTable`
- **Item**: Id, Name - supports deletion via `renderItemsTable`
- **Registration**: Id, Email, FirstName, LastName - supports validation via `renderRegistrationForm`, `renderValidationFeedback`, `renderRegistrationSuccess`
- **Fruits**: Static list of 22 fruit names - filterable via `renderFruitsList`

### Signal Types (CLIMutable for JSON deserialization)

- **ContactSignals**: firstName, lastName, email
- **BulkUpdateSignals**: selections (bool array)
- **RegistrationSignals**: email, firstName, lastName

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All Playwright tests in Frank.Datastar.Tests pass when run against the Hox sample
- **SC-002**: Sample builds successfully with `dotnet build sample/Frank.Datastar.Hox`
- **SC-003**: Sample runs and serves index.html at root URL (http://localhost:5000/)
- **SC-004**: Single SSE endpoint at `/sse` handles all real-time updates
- **SC-005**: HTML responses contain identical element IDs to Basic sample: contact-view, fruits-list, items-table, items-list, users-table-container, users-list, registration-form, validation-feedback, registration-result
- **SC-006**: Code review confirms all string templates from Basic are replaced with equivalent Hox `h()` calls

## Assumptions

- The Hox NuGet package version 3.x is compatible with .NET 10.0
- The wwwroot/index.html file from Frank.Datastar.Basic can be reused without modification
- The Hox `h()` function supports CSS-selector notation for attributes including Datastar-specific attributes (data-on:click, data-bind, etc.)
- `Render.asString` returns `ValueTask<string>`, requiring callers to await the result

## Dependencies

- Frank 6.x (via project reference to Frank)
- Hox 3.x (via NuGet package)
- StarFederation.Datastar.FSharp (via project reference to Frank.Datastar)
- .NET 10.0 SDK
- Frank.Datastar.Basic as reference implementation
- Frank.Datastar.Tests for validation

## Research

# Research: Hox API Patterns for Frank.Datastar.Hox

**Feature**: 008-update-hox-sample
**Date**: 2026-02-02

## Summary

Research on translating Frank.Datastar.Basic's F# string templates to Hox `h()` calls with Datastar attributes.

## Decision 1: Hox CSS-Selector Syntax for Datastar Attributes

**Decision**: Use Hox's CSS-selector notation `[attr=value]` for all Datastar attributes

**Rationale**:
- Hox parser is very permissive - allows JSON, quotes, special characters within `[attr=value]` syntax
- Supports multiple attributes in sequence: `[data-on:click=...] [data-bind:name] [data-indicator:_fetching]`
- Compatible with all Datastar attribute patterns including complex JavaScript expressions

**Alternatives considered**:
- `.attr()` fluent API - more verbose, requires method chaining
- `attrs` record type - less readable for complex attributes

**Syntax patterns**:
```fsharp
// Simple attribute
h("button [data-on:click=@get('/path')]", [ Text "Click" ])

// Multiple attributes
h("input [type=text] [data-bind:firstName] [data-attr:disabled=$_fetching]", [])

// JSON in data-signals (use single quotes for JSON, F# double quotes for string)
h($"div [data-signals={{'firstName': '{value}', 'email': '{email}'}}]", [...])

// Complex JavaScript expressions
h("button [data-on:click=confirm('Delete?') && @delete('/items/{id}')]", [...])
```

## Decision 2: Render Function Signature

**Decision**: All render functions return `ValueTask<string>` via `Render.asString`

**Rationale**:
- `Render.asString` always returns `ValueTask<string>` (async)
- Callers must await the result before broadcasting
- Consistent pattern across all render functions

**Pattern**:
```fsharp
let renderContactView (contact: Contact) : ValueTask<string> =
    let node = h("div#contact-view", [...])
    Render.asString node

// Usage in handler:
task {
    let! html = renderContactView contact
    SseEvent.broadcast (PatchElements html)
}
```

## Decision 3: List Rendering with `fragment`

**Decision**: Use `fragment` function for rendering collections

**Rationale**:
- `fragment` handles parentless node collections
- Works with F# list comprehensions
- Clean syntax for table rows, list items, etc.

**Pattern**:
```fsharp
h("ul#fruits-list",
    fragment [ for f in fruits do h("li", [ Text f ]) ])

h("tbody#users-list",
    fragment [
        for user in users.Values do
            h("tr", [...])
    ])
```

## Decision 4: Text Content

**Decision**: Use `Text` wrapper for all text content

**Rationale**:
- Explicit text nodes prevent ambiguity
- Supports interpolated strings for dynamic content
- Consistent with Hox node model

**Pattern**:
```fsharp
h("p", [
    h("strong", [ Text "Label:" ])
    Text $" {value}"
])
```

## Decision 5: HTML Structure Matching

**Decision**: Generate identical HTML structure to Frank.Datastar.Basic

**Rationale**:
- Playwright tests depend on specific element IDs and selectors
- Client-side index.html expects specific container IDs
- Ensures test compatibility without modification

**Critical IDs to preserve**:
- `#contact-view` - Click-to-edit container
- `#fruits-list` - Search results list
- `#items-table`, `#items-list` - Delete demo table
- `#users-table-container`, `#users-list` - Bulk update table
- `#registration-form`, `#validation-feedback`, `#registration-result` - Form validation

## Translation Reference

### Basic Sample → Hox Sample Patterns

| Basic (string template) | Hox (h() call) |
|------------------------|----------------|
| `$"""<div id="x">"""` | `h("div#x", [...])` |
| `$"""<p><strong>Label:</strong> {value}</p>"""` | `h("p", [ h("strong", [ Text "Label:" ]); Text $" {value}" ])` |
| `$"""<button data-on:click="@get('/x')">"""` | `h("button [data-on:click=@get('/x')]", [ Text "..." ])` |
| `$"""<div data-signals="{{...}}">"""` | `h($"div [data-signals={{...}}]", [...])` |
| `data-bind:first-name` | `[data-bind:first-name]` |
| `data-indicator:_fetching` | `[data-indicator:_fetching]` |
| `data-attr:disabled="$_fetching"` | `[data-attr:disabled=$_fetching]` |

### Datastar Attribute Patterns

```fsharp
// Click handler
[data-on:click=@get('/contacts/{id}/edit')]

// Form binding (kebab-case for multi-word signals)
[data-bind:first-name]
[data-bind:email]

// Array binding
[data-bind:selections]
[data-bind:selections[{idx}]]

// Loading indicator
[data-indicator:_fetching]

// Conditional disable
[data-attr:disabled=$_fetching]

// Debounced events
[data-on:input__debounce.500ms=@post('/validate')]
[data-on:keydown__debounce.500ms=@post('/validate')]

// Signals initialization
[data-signals={'email': '', 'firstName': '', 'lastName': ''}]
[data-signals__ifmissing={_fetching: false, selections: Array(4).fill(false)}]

// Effects
[data-effect=$selections; $_all = $selections.every(Boolean)]

// Confirm dialog
[data-on:click=confirm('Delete?') && @delete('/items/{id}')]
```

## Open Issues

None - all patterns validated against existing Hox sample and parser tests.
