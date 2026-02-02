# API Contract: Frank.Datastar.Hox Sample

**Feature**: 003-datastar-hox-sample
**Date**: 2026-01-27

## Overview

This API contract mirrors the Frank.Datastar.Basic sample exactly. All endpoints, methods, status codes, and response formats are identical. The only difference is that HTML is generated using Hox instead of F# string templates.

## Base URL

```
http://localhost:5000
```

## Content Types

- **Request**: `application/json` (for signals data)
- **Response**: `text/event-stream` (SSE) or empty (fire-and-forget)

---

## Contact Resources

### GET /contacts/{id}

**Purpose**: Establish SSE connection and display contact view

**Parameters**:
- `id` (path): Contact ID (integer)

**Responses**:
| Status | Description | Response |
|--------|-------------|----------|
| 200 | SSE stream established | `text/event-stream` with initial contact HTML |
| 404 | Contact not found | SSE event with error HTML |

**SSE Events**:
- `datastar-merge-fragments`: HTML with `id="contact-view"`

### GET /contacts/{id}/edit

**Purpose**: Fire-and-forget request to switch to edit mode

**Parameters**:
- `id` (path): Contact ID (integer)

**Responses**:
| Status | Description |
|--------|-------------|
| 202 | Accepted - edit form pushed to channel |
| 404 | Contact not found |

### PUT /contacts/{id}

**Purpose**: Fire-and-forget request to save contact changes

**Parameters**:
- `id` (path): Contact ID (integer)

**Request Body**:
```json
{
  "firstName": "string",
  "lastName": "string",
  "email": "string"
}
```

**Responses**:
| Status | Description |
|--------|-------------|
| 202 | Accepted - view mode pushed to channel |
| 400 | Invalid signals data |
| 404 | Contact not found |

---

## Fruits Resource

### GET /fruits

**Purpose**: Establish SSE connection with full list OR search (fire-and-forget)

**Parameters**:
- `q` (query, optional): Search term

**Behavior**:
- Without `q`: Establish SSE, send full list, keep open
- With `q`: Fire-and-forget search, push filtered results

**Responses**:
| Status | Description | Response |
|--------|-------------|----------|
| 200 | SSE stream (no query) | `text/event-stream` with fruits list |
| 202 | Search accepted (with query) | Empty body |

**SSE Events**:
- `datastar-merge-fragments`: HTML with `id="fruits-list"`

---

## Items Resources

### GET /items

**Purpose**: Establish SSE connection and display items table

**Responses**:
| Status | Description | Response |
|--------|-------------|----------|
| 200 | SSE stream established | `text/event-stream` with items table |

**SSE Events**:
- `datastar-merge-fragments`: HTML with `id="items-table"`

### DELETE /items/{id}

**Purpose**: Fire-and-forget request to delete item

**Parameters**:
- `id` (path): Item ID (integer)

**Responses**:
| Status | Description |
|--------|-------------|
| 202 | Accepted - remove event pushed to channel |
| 404 | Item not found |

**SSE Events** (pushed to channel):
- `datastar-remove-fragments`: Selector `#item-{id}`

---

## Users Resources

### GET /users

**Purpose**: Establish SSE connection and display users table

**Responses**:
| Status | Description | Response |
|--------|-------------|----------|
| 200 | SSE stream established | `text/event-stream` with users table |

**SSE Events**:
- `datastar-merge-fragments`: HTML with `id="users-table-container"`

### PUT /users/bulk

**Purpose**: Fire-and-forget request to update selected users

**Parameters**:
- `status` (query): Target status (`active` or `inactive`)

**Request Body**:
```json
{
  "selections": [true, false, true, false]
}
```

**Responses**:
| Status | Description |
|--------|-------------|
| 202 | Accepted - updated table pushed to channel |
| 400 | Invalid signals data |

---

## Registration Resources

### GET /registrations/form

**Purpose**: Establish SSE connection and display registration form

**Responses**:
| Status | Description | Response |
|--------|-------------|----------|
| 200 | SSE stream established | `text/event-stream` with form HTML |

**SSE Events**:
- `datastar-merge-fragments`: HTML with `id="registration-form"`

### POST /registrations/validate

**Purpose**: Fire-and-forget validation request

**Request Body**:
```json
{
  "email": "string",
  "firstName": "string",
  "lastName": "string"
}
```

**Responses**:
| Status | Description |
|--------|-------------|
| 202 | Accepted - validation feedback pushed to channel |
| 400 | Invalid signals data |

**SSE Events** (pushed to channel):
- `datastar-merge-fragments`: HTML with `id="validation-feedback"`

### POST /registrations

**Purpose**: Fire-and-forget registration submission

**Request Body**:
```json
{
  "email": "string",
  "firstName": "string",
  "lastName": "string"
}
```

**Responses**:
| Status | Description |
|--------|-------------|
| 201 | Created - success message pushed to channel |
| 400 | Validation failed - errors pushed to channel |
| 409 | Conflict - email already registered |

**SSE Events** (pushed to channel):
- `datastar-merge-fragments`: HTML with `id="registration-result"` or `id="validation-feedback"`

---

## Method Not Allowed

Any unsupported method returns:

| Status | Description |
|--------|-------------|
| 405 | Method Not Allowed |

**Examples**:
- DELETE /fruits → 405
- POST /contacts/1 → 405
- PUT /fruits → 405

---

## HTML Element IDs (Datastar Targets)

These IDs must be present in rendered HTML for Datastar to target correctly:

| Element ID | Resource | Purpose |
|------------|----------|---------|
| `contact-view` | /contacts/{id} | Contact view/edit container |
| `fruits-list` | /fruits | Fruits list container |
| `items-table` | /items | Items table wrapper |
| `items-list` | /items | Items table body |
| `item-{id}` | /items/{id} | Individual item row |
| `users-table-container` | /users | Users section wrapper |
| `users-list` | /users | Users table body |
| `registration-form` | /registrations/form | Registration form container |
| `validation-feedback` | /registrations/* | Validation message area |
| `registration-result` | /registrations | Success/error message area |
