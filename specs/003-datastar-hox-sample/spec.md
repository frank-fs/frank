# Feature Specification: Frank.Datastar.Hox Sample Application

**Feature Branch**: `003-datastar-hox-sample`
**Created**: 2026-01-27
**Status**: Draft
**Input**: User description: "Generate a new sample project using Frank.Datastar and the Hox view engine. This sample should mirror sample/FSharp.Datastar.Basic in functionality and be stored in sample/FSharp.Datastar.Hox. The Hox library is available locally at /Users/ryanr/Code/Hox for reference. There is an existing implementation that should be replaced. The same test.sh used to verify sample/Frank.Datastar.Basic should work for the new implementation."

## Overview

This specification defines a sample application that demonstrates Frank.Datastar integration with the Hox view engine. The sample must provide identical RESTful functionality to the existing Frank.Datastar.Basic sample but use Hox's CSS-selector-based DSL for HTML generation instead of F# string templates.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run Existing Tests Against Hox Sample (Priority: P1)

A developer wants to verify that the Hox-based sample implements the same RESTful patterns as the Basic sample, ensuring API compatibility.

**Why this priority**: Test compatibility is the primary success criterion - if the same test.sh script passes for both samples, the Hox sample is functionally equivalent.

**Independent Test**: Run `./test.sh` against the Hox sample running on localhost:5000 - all tests should pass identically to the Basic sample.

**Acceptance Scenarios**:

1. **Given** the Hox sample is running on port 5000, **When** `./test.sh 5000` is executed, **Then** all 18 tests pass (tests 11-28 in the current test.sh)
2. **Given** the Hox sample is running, **When** the developer makes requests to `/contacts/1`, `/fruits`, `/items`, `/users`, `/registrations/form`, **Then** the responses contain the expected HTML structure and return correct HTTP status codes

---

### User Story 2 - Click-to-Edit Contact Pattern with Hox (Priority: P2)

A developer wants to see how to implement a click-to-edit pattern using Hox for HTML rendering, demonstrating GET/PUT resource semantics with SSE channels.

**Why this priority**: Click-to-edit is a fundamental CRUD pattern that demonstrates view/edit state switching and data persistence.

**Independent Test**: Load `/contacts/1`, see contact details rendered with Hox, click Edit, modify fields, save - verify state transitions work correctly.

**Acceptance Scenarios**:

1. **Given** the sample is running, **When** GET `/contacts/1` is called, **Then** SSE response contains HTML with contact view (First Name, Last Name, Email, Edit button)
2. **Given** a contact view is displayed, **When** GET `/contacts/1/edit` is called, **Then** channel receives HTML with editable form fields
3. **Given** an edit form is displayed, **When** PUT `/contacts/1` is called with updated data, **Then** data is saved and view mode HTML is pushed to channel
4. **Given** a non-existent contact ID, **When** GET `/contacts/99` is called, **Then** HTTP 404 is returned

---

### User Story 3 - Search with Filtering Pattern (Priority: P2)

A developer wants to see how to implement a searchable collection using GET with query parameters, rendered with Hox.

**Why this priority**: Search/filter is a common pattern for displaying and narrowing collections.

**Independent Test**: Load `/fruits`, see full list, search with `?q=ap`, verify filtered results.

**Acceptance Scenarios**:

1. **Given** the sample is running, **When** GET `/fruits` is called, **Then** SSE response contains HTML list of all 22 fruits
2. **Given** a fruits list is displayed, **When** GET `/fruits?q=ap` is called, **Then** channel receives filtered list containing only matching fruits (Apple, Apricot, Grape, Papaya)

---

### User Story 4 - Row Deletion Pattern (Priority: P3)

A developer wants to see how to implement item deletion using HTTP DELETE, with Hox-rendered table rows.

**Why this priority**: Delete operations demonstrate fire-and-forget patterns with removeElement SSE events.

**Independent Test**: Load `/items`, see table, delete an item, verify removal without page refresh.

**Acceptance Scenarios**:

1. **Given** the sample is running, **When** GET `/items` is called, **Then** SSE response contains HTML table with items
2. **Given** an items table is displayed, **When** DELETE `/items/1` is called, **Then** HTTP 202 is returned and removeElement event removes the row
3. **Given** a non-existent item, **When** DELETE `/items/99` is called, **Then** HTTP 404 is returned

---

### User Story 5 - Bulk Update Pattern (Priority: P3)

A developer wants to see how to update multiple resources at once using PUT on a collection endpoint, with Hox-rendered status changes.

**Why this priority**: Bulk operations demonstrate collection-level updates with checkbox selections.

**Independent Test**: Load `/users`, select multiple users, activate/deactivate, verify status changes.

**Acceptance Scenarios**:

1. **Given** the sample is running, **When** GET `/users` is called, **Then** SSE response contains HTML table with users and checkboxes
2. **Given** a users table with selections, **When** PUT `/users/bulk?status=active` is called with selections, **Then** selected users are updated and new table HTML is pushed

---

### User Story 6 - Form Validation Pattern (Priority: P3)

A developer wants to see real-time form validation using separate validation endpoint, with Hox-rendered feedback messages.

**Why this priority**: Validation patterns demonstrate POST for validation and creation with appropriate status codes.

**Independent Test**: Load registration form, enter invalid data, see errors, fix data, submit successfully.

**Acceptance Scenarios**:

1. **Given** the sample is running, **When** GET `/registrations/form` is called, **Then** SSE response contains HTML form with validation feedback area
2. **Given** a registration form, **When** POST `/registrations/validate` is called with empty fields, **Then** HTTP 202 is returned and validation errors are pushed
3. **Given** valid form data, **When** POST `/registrations` is called, **Then** HTTP 201 is returned and success message is pushed
4. **Given** a duplicate email, **When** POST `/registrations` is called, **Then** HTTP 409 Conflict is returned

---

### Edge Cases

- What happens when Hox rendering fails? (Server error should be handled gracefully)
- How does the system handle concurrent SSE connections? (Each connection should maintain independent state)
- What happens with malformed JSON signals? (HTTP 400 Bad Request)
- How does the sample handle unsupported HTTP methods? (HTTP 405 Method Not Allowed)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Sample MUST replace existing Frank.Datastar.Hox implementation entirely
- **FR-002**: Sample MUST implement identical REST API endpoints as Frank.Datastar.Basic
- **FR-003**: Sample MUST use Hox's `h()` CSS-selector DSL for all HTML generation (no F# string templates)
- **FR-004**: Sample MUST use `Hox.Rendering.Render.asString` for converting Hox nodes to HTML strings
- **FR-005**: Sample MUST pass all tests in the existing test.sh script without modification
- **FR-006**: Sample MUST implement SSE channel pattern with MailboxProcessor for real-time updates
- **FR-007**: Sample MUST return correct HTTP status codes: 200/201/202 for success, 400/404/405/409 for errors
- **FR-008**: Sample MUST use the same in-memory data stores pattern (Dictionary, ResizeArray)
- **FR-009**: Sample MUST implement all six RESTful patterns: Click-to-Edit, Search, Delete, Bulk Update, Form Validation
- **FR-010**: Sample MUST maintain the same wwwroot/index.html structure for client-side Datastar integration
- **FR-011**: Sample MUST reference Hox via NuGet package (Version="3.*")

### Key Entities

- **Contact**: Represents a person with Id, FirstName, LastName, Email - supports view/edit modes
- **User**: Represents a user account with Id, Name, Email, Status (Active/Inactive) - supports bulk status updates
- **Item**: Represents a deletable row with Id, Name
- **Registration**: Represents a form submission with Id, Email, FirstName, LastName - supports validation
- **Fruits**: Static list of fruit names for search demonstration

### Signal Types (for Datastar integration)

- **ContactSignals**: firstName, lastName, email (mutable for form binding)
- **BulkUpdateSignals**: selections (bool array for checkbox state)
- **RegistrationSignals**: email, firstName, lastName (mutable for form binding)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 18 tests in test.sh pass when run against the Hox sample
- **SC-002**: Sample builds successfully with `dotnet build sample/Frank.Datastar.Hox`
- **SC-003**: Sample runs and serves index.html at root URL
- **SC-004**: All REST endpoints use noun-based URLs (no verb-based RPC patterns)
- **SC-005**: HTML responses contain proper element IDs for Datastar targeting (contact-view, fruits-list, items-table, users-table-container, registration-form, validation-feedback, registration-result)

## Assumptions

- The Hox NuGet package version 3.x is compatible with .NET 10.0
- The existing test.sh script uses curl and checks HTTP status codes and response content
- The wwwroot/index.html file can remain unchanged from the Basic sample (client-side code is identical)
- SSE channel architecture (MailboxProcessor pattern) is the correct approach for this sample

## Dependencies

- Frank 6.x NuGet package
- Hox 3.x NuGet package
- StarFederation.Datastar.FSharp (via Frank.Datastar project reference)
- .NET 10.0 SDK
