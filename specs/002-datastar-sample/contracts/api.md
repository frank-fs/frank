# API Contracts: Frank.Datastar Sample Application

**Feature Branch**: `002-datastar-sample`
**Date**: 2026-01-27

## Overview

This document defines the HTTP API contracts for the Frank.Datastar sample application.

### SSE Connection Architecture

SSE connections are long-lived. Opening multiple per page exhausts browser connection limits. The architecture uses:

| Endpoint Type | Establishes SSE? | Response | UI Updates Via |
|---------------|------------------|----------|----------------|
| **SSE Endpoints** (GET, POST, PUT) | Yes | `text/event-stream` | Same connection |
| **Fire-and-forget** (DELETE, some PUT) | No | HTTP status only | Existing SSE connection |

**SSE Headers** (for endpoints establishing SSE):
- `Content-Type: text/event-stream`
- `Cache-Control: no-cache`
- `Connection: keep-alive`

**Fire-and-forget Headers**:
- Standard HTTP response (no body or minimal body)

---

## Contact Resource (Click-to-Edit Pattern)

### GET /contacts/{id}

**Establishes SSE**: Yes - sends view and keeps connection for edit/save cycle.

Retrieves the read-only view of a contact.

**Parameters**:
- `id` (path, required): Contact ID (integer)

**Response 200**: HTML fragment with contact details in view mode
```html
<div id="contact-{id}">
  <p><strong>First Name:</strong> {firstName}</p>
  <p><strong>Last Name:</strong> {lastName}</p>
  <p><strong>Email:</strong> {email}</p>
  <button data-on:click="@get('/contacts/{id}/edit')">Edit</button>
</div>
```

**Response 404**: Contact not found
```html
<div id="contact-{id}" class="error">Contact not found.</div>
```

---

### GET /contacts/{id}/edit

Retrieves the edit form for a contact.

**Parameters**:
- `id` (path, required): Contact ID (integer)

**Response 200**: HTML fragment with editable form
```html
<div id="contact-{id}" data-signals="{firstName: '{firstName}', lastName: '{lastName}', email: '{email}'}">
  <label>First Name <input type="text" data-bind:first-name /></label>
  <label>Last Name <input type="text" data-bind:last-name /></label>
  <label>Email <input type="email" data-bind:email /></label>
  <button data-on:click="@put('/contacts/{id}')">Save</button>
  <button data-on:click="@get('/contacts/{id}')">Cancel</button>
</div>
```

**Response 404**: Contact not found
```html
<div id="contact-{id}" class="error">Contact not found.</div>
```

---

### PUT /contacts/{id}

**Establishes SSE**: Could be either pattern:
- **Option A**: Fire-and-forget (200 OK, view update via existing SSE from GET)
- **Option B**: PUT establishes SSE to stream validation errors or success view

Updates a contact with new data.

**Parameters**:
- `id` (path, required): Contact ID (integer)

**Request Body** (via Datastar signals):
```json
{
  "firstName": "string",
  "lastName": "string",
  "email": "string"
}
```

**Response** (depends on pattern chosen):
- **Fire-and-forget**: 200 OK (no body), updated view sent over existing SSE
- **SSE**: Streams validation errors or updated view

**Response 400**: Validation error (could stream via SSE)
```html
<div id="contact-{id}" class="error">
  <ul>
    <li>First name is required</li>
    <li>Email format is invalid</li>
  </ul>
</div>
```

**Response 404**: Contact not found

---

## Fruits Resource (Search Pattern)

### GET /fruits

**Establishes SSE**: Yes - sends search results and keeps connection for subsequent searches.

Retrieves the searchable fruits collection.

**Parameters**:
- `q` (query, optional): Search term to filter fruits

**Response 200**: HTML fragment with search input and filtered results
```html
<div id="fruits-demo">
  <input type="text" data-bind:search data-on:input__debounce.200ms="@get('/fruits')" placeholder="Search fruits..." />
  <ul id="fruits-list">
    <li>Apple</li>
    <li>Apricot</li>
    <!-- ... filtered results ... -->
  </ul>
</div>
```

**Behavior**:
- Empty `q`: Returns all fruits
- Non-empty `q`: Returns fruits containing the search term (case-insensitive)

---

## Items Resource (Delete Pattern)

### GET /items

**Establishes SSE**: Yes - sends initial list and keeps connection open for delete updates.

Retrieves the list of deletable items and establishes the SSE connection for this section.

**Response 200** (SSE): HTML table with items and delete buttons
```html
<table id="items-table">
  <thead>
    <tr><th>Name</th><th>Actions</th></tr>
  </thead>
  <tbody id="items-list">
    <tr id="item-1">
      <td>Item 1</td>
      <td><button data-on:click="confirm('Delete?') && @delete('/items/1')">Delete</button></td>
    </tr>
    <!-- ... more items ... -->
  </tbody>
</table>
```

Connection remains open for subsequent `removeElement` commands.

---

### DELETE /items/{id}

**Fire-and-forget**: Returns HTTP status only. UI update sent over existing SSE connection.

Deletes a specific item.

**Parameters**:
- `id` (path, required): Item ID (integer)

**Response 202 Accepted**: Item deleted (no body)

**Side Effect**: Server sends `removeElement #item-{id}` over the SSE connection established by GET /items.

**Response 404**: Item not found (already deleted) - no body or simple error message

---

## Users Resource (Bulk Update Pattern)

### GET /users

**Establishes SSE**: Yes - sends initial table and keeps connection for bulk updates.

Retrieves the users table with selection checkboxes.

**Response 200**: HTML table with checkboxes and bulk action buttons
```html
<div id="users-demo" data-signals="{selections: [false, false, false, false]}">
  <table>
    <thead>
      <tr>
        <th><input type="checkbox" data-bind:_all data-on:change="$selections = Array(4).fill($_all)" /></th>
        <th>Name</th>
        <th>Email</th>
        <th>Status</th>
      </tr>
    </thead>
    <tbody id="users-list">
      <!-- rows with data-bind:selections checkboxes -->
    </tbody>
  </table>
  <button data-on:click="@put('/users/bulk?status=active')">Activate</button>
  <button data-on:click="@put('/users/bulk?status=inactive')">Deactivate</button>
</div>
```

---

### PUT /users/bulk

**Establishes SSE**: Could be either pattern:
- **Option A**: Fire-and-forget (202 Accepted, table update via existing SSE from GET /users)
- **Option B**: PUT establishes SSE to stream updated table

Updates status for selected users.

**Parameters**:
- `status` (query, required): Target status ("active" or "inactive")

**Request Body** (via Datastar signals):
```json
{
  "selections": [true, false, true, false]
}
```

**Response** (depends on pattern chosen):
- **Fire-and-forget**: 202 Accepted (no body), table update sent over existing SSE
- **SSE**: Streams updated table body

```html
<tbody id="users-list">
  <tr>
    <td><input type="checkbox" data-bind:selections /></td>
    <td>Joe Smith</td>
    <td>joe@smith.org</td>
    <td class="status-active">Active</td>
  </tr>
  <!-- ... updated rows ... -->
</tbody>
```

---

## Registration Resource (Form Validation Pattern)

### POST /registrations/validate

**Establishes SSE**: Yes - streams validation feedback.

Validates registration form fields without creating a registration.

**Request Body** (via Datastar signals):
```json
{
  "email": "string",
  "firstName": "string",
  "lastName": "string"
}
```

**Response 200** (SSE): Streams validation feedback HTML
```html
<div id="validation-feedback">
  <p class="error">Email is required</p>
  <!-- or -->
  <p class="success">All fields valid!</p>
</div>
```

**Validation Rules**:
- Email: Required, valid format, unique
- FirstName: Required, non-empty
- LastName: Required, non-empty

---

### POST /registrations

**Establishes SSE**: Yes - streams validation, success message, and optional redirect.

Creates a new registration.

**Request Body** (via Datastar signals):
```json
{
  "email": "string",
  "firstName": "string",
  "lastName": "string"
}
```

**Response 200** (SSE): Streams multiple events:

1. **Validation errors** (if any):
```html
<div id="registration-result" class="error">
  <ul>
    <li>Email is already registered</li>
  </ul>
</div>
```

2. **Success message** (if valid):
```html
<div id="registration-result" class="success">
  Registration successful! Welcome, {firstName}.
</div>
```

3. **Optional delayed redirect** (via executeScript):
```
event: datastar-execute-script
data: script window.location.href = '/welcome'
```

---

## Error Response Format

All error responses follow this pattern:

```html
<div id="{target-element}" class="error">
  {error message or list}
</div>
```

**HTTP Status Codes**:
- `200 OK`: Success (including HTML fragments)
- `400 Bad Request`: Validation errors
- `404 Not Found`: Resource does not exist
- `500 Internal Server Error`: Unexpected server error

---

## SSE Event Format

All responses use Datastar's SSE event format:

```
event: datastar-patch
data: selector #target-id
data: merge morph
data: fragment <div>HTML content</div>

```

For removal:
```
event: datastar-remove
data: selector #item-id

```
