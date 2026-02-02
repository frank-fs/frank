# API Contract: Frank.Datastar.Hox Sample

**Feature**: 008-update-hox-sample
**Date**: 2026-02-02

## Overview

The API contract is identical to Frank.Datastar.Basic. All endpoints use fire-and-forget pattern with SSE broadcasts.

## SSE Endpoint

### GET /sse

Establishes SSE connection for receiving all updates.

**Response**: `text/event-stream`
- Subscribes to broadcast channel
- Receives all PatchElements, RemoveElement, PatchSignals events
- Connection kept open until client disconnects

**Datastar Events**:
- `datastar-merge-fragments` - HTML content updates
- `datastar-remove-fragments` - Element removal
- `datastar-merge-signals` - Signal updates

## Contact Resources (Click-to-Edit)

### GET /contacts/{id}

Retrieves contact view and broadcasts to SSE.

**Path Parameters**:
- `id` (int) - Contact ID

**Response**:
- `202 Accepted` - Contact found, view broadcast
- `404 Not Found` - Contact not found

**Broadcast**: `PatchElements` with contact view HTML (`#contact-view`)

### GET /contacts/{id}/edit

Retrieves contact edit form and broadcasts to SSE.

**Path Parameters**:
- `id` (int) - Contact ID

**Response**:
- `202 Accepted` - Contact found, edit form broadcast
- `404 Not Found` - Contact not found

**Broadcast**: `PatchElements` with edit form HTML (`#contact-view`)

### PUT /contacts/{id}

Updates contact and broadcasts updated view.

**Path Parameters**:
- `id` (int) - Contact ID

**Request Body**: Datastar signals (JSON)
```json
{ "firstName": "...", "lastName": "...", "email": "..." }
```

**Response**:
- `202 Accepted` - Contact updated
- `400 Bad Request` - Invalid signals
- `404 Not Found` - Contact not found

**Broadcast**: `PatchElements` with updated contact view HTML

## Fruits Resource (Search)

### GET /fruits

Retrieves fruit list (optionally filtered) and broadcasts to SSE.

**Query Parameters**:
- `q` (string, optional) - Search filter

**Response**:
- `202 Accepted` - List broadcast

**Broadcast**: `PatchElements` with fruits list HTML (`#fruits-list`)

## Items Resources (Delete)

### GET /items

Retrieves items table and broadcasts to SSE.

**Response**:
- `202 Accepted` - Table broadcast

**Broadcast**: `PatchElements` with items table HTML (`#items-table`)

### DELETE /items/{id}

Deletes item and broadcasts removal to SSE.

**Path Parameters**:
- `id` (int) - Item ID

**Response**:
- `202 Accepted` - Item deleted
- `404 Not Found` - Item not found

**Broadcast**: `RemoveElement` with selector `#item-{id}`

## Users Resources (Bulk Update)

### GET /users

Retrieves users table and broadcasts to SSE.

**Response**:
- `202 Accepted` - Table broadcast

**Broadcast**: `PatchElements` with users table HTML (`#users-table-container`)

### PUT /users/bulk

Updates selected users' status and broadcasts updated table.

**Query Parameters**:
- `status` (string) - "active" or "inactive"

**Request Body**: Datastar signals (JSON)
```json
{ "selections": [true, false, true, false] }
```

**Response**:
- `202 Accepted` - Users updated
- `400 Bad Request` - Invalid signals

**Broadcast**: `PatchElements` with updated users table HTML

## Registration Resources (Form Validation)

### GET /registrations/form

Retrieves registration form and broadcasts to SSE.

**Response**:
- `202 Accepted` - Form broadcast

**Broadcast**: `PatchElements` with form HTML (`#registration-form`)

### POST /registrations/validate

Validates registration data and broadcasts feedback.

**Request Body**: Datastar signals (JSON)
```json
{ "email": "...", "firstName": "...", "lastName": "..." }
```

**Response**:
- `202 Accepted` - Validation complete

**Broadcast**: `PatchElements` with validation feedback HTML (`#validation-feedback`)

### POST /registrations

Creates registration and broadcasts result.

**Request Body**: Datastar signals (JSON)
```json
{ "email": "...", "firstName": "...", "lastName": "..." }
```

**Response**:
- `201 Created` - Registration successful
- `400 Bad Request` - Validation errors
- `409 Conflict` - Duplicate email

**Broadcast**: `PatchElements` with result HTML (`#registration-result` or `#validation-feedback`)

## Debug Endpoint

### GET /debug/ping

Simple endpoint for debugging input events.

**Response**:
- `200 OK`

**Side Effect**: Logs "DEBUG: Input event received!" to console
