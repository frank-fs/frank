# API Contracts: Update Oxpecker Sample

**Feature**: 007-update-oxpecker-sample
**Date**: 2026-02-02

## Overview

All endpoints are RESTful resources. The primary SSE endpoint (`/sse`) is used for all real-time updates. Other endpoints are fire-and-forget: they broadcast updates to the SSE channel and return immediately.

## SSE Endpoint

### GET /sse

Establishes Server-Sent Events connection for all demo patterns.

**Response**: `text/event-stream`

**Behavior**:
1. Subscribes to broadcast channel
2. Keeps connection open
3. Forwards all `SseEvent` broadcasts to client
4. Cleans up subscription on disconnect

**SSE Event Format** (Datastar protocol):
```
event: datastar-merge-fragments
data: fragments <html>

event: datastar-remove-fragments
data: selector #item-1

event: datastar-merge-signals
data: signals {"key": "value"}
```

---

## Contact Resources (Click-to-Edit)

### GET /contacts/{id}

Broadcasts contact view to SSE channel.

**Path Parameters**: `id` (int) - Contact ID

**Response Codes**:
- `202 Accepted` - Broadcast sent
- `404 Not Found` - Contact not found

**Broadcast**: `PatchElements` with contact view HTML

### GET /contacts/{id}/edit

Broadcasts contact edit form to SSE channel.

**Path Parameters**: `id` (int) - Contact ID

**Response Codes**:
- `202 Accepted` - Broadcast sent
- `404 Not Found` - Contact not found

**Broadcast**: `PatchElements` with contact edit form HTML

### PUT /contacts/{id}

Updates contact from signals and broadcasts updated view.

**Path Parameters**: `id` (int) - Contact ID

**Request Body**: Datastar signals (`ContactSignals`)
```json
{
  "firstName": "string",
  "lastName": "string",
  "email": "string"
}
```

**Response Codes**:
- `202 Accepted` - Updated and broadcast sent
- `400 Bad Request` - Invalid signals
- `404 Not Found` - Contact not found

**Broadcast**: `PatchElements` with updated contact view HTML

---

## Fruits Resource (Search)

### GET /fruits

Broadcasts fruit list to SSE channel, optionally filtered.

**Query Parameters**: `q` (string, optional) - Search filter

**Response Codes**:
- `202 Accepted` - Broadcast sent

**Broadcast**: `PatchElements` with fruits list HTML

**Filtering**: Case-insensitive substring match

---

## Items Resources (Delete)

### GET /items

Broadcasts items table to SSE channel.

**Response Codes**:
- `202 Accepted` - Broadcast sent

**Broadcast**: `PatchElements` with items table HTML

### DELETE /items/{id}

Removes item and broadcasts removal.

**Path Parameters**: `id` (int) - Item ID

**Response Codes**:
- `202 Accepted` - Deleted and broadcast sent
- `404 Not Found` - Item not found

**Broadcast**: `RemoveElement` with selector `#item-{id}`

---

## Users Resources (Bulk Update)

### GET /users

Broadcasts users table to SSE channel.

**Response Codes**:
- `202 Accepted` - Broadcast sent

**Broadcast**: `PatchElements` with users table HTML

### PUT /users/bulk

Updates selected users' status and broadcasts updated table.

**Query Parameters**: `status` (string) - "active" or "inactive"

**Request Body**: Datastar signals (`BulkUpdateSignals`)
```json
{
  "selections": [true, false, true, false]
}
```

**Response Codes**:
- `202 Accepted` - Updated and broadcast sent
- `400 Bad Request` - Invalid signals

**Broadcast**: `PatchElements` with updated users table HTML

---

## Registration Resources (Form Validation)

### GET /registrations/form

Broadcasts registration form to SSE channel.

**Response Codes**:
- `202 Accepted` - Broadcast sent

**Broadcast**: `PatchElements` with registration form HTML

### POST /registrations/validate

Validates registration signals and broadcasts feedback.

**Request Body**: Datastar signals (`RegistrationSignals`)
```json
{
  "email": "string",
  "firstName": "string",
  "lastName": "string"
}
```

**Response Codes**:
- `202 Accepted` - Validation broadcast sent
- `400 Bad Request` - Invalid signals

**Broadcast**: `PatchElements` with validation feedback HTML

### POST /registrations

Creates registration if valid, broadcasts result.

**Request Body**: Datastar signals (`RegistrationSignals`)

**Response Codes**:
- `201 Created` - Registration created, success broadcast sent
- `400 Bad Request` - Validation errors, error broadcast sent
- `409 Conflict` - Email already registered, error broadcast sent

**Broadcast**: `PatchElements` with success or error HTML

---

## Debug Endpoint

### GET /debug/ping

Debug endpoint to verify input events fire.

**Response Codes**:
- `200 OK` - Logs "DEBUG: Input event received!"
