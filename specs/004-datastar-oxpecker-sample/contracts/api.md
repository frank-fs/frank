# API Contracts: Frank.Datastar.Oxpecker Sample

**Date**: 2026-01-27
**Feature**: 004-datastar-oxpecker-sample

## Overview

These contracts are identical to `Frank.Datastar.Basic` and `Frank.Datastar.Hox`. All endpoints return SSE (text/event-stream) or fire-and-forget responses (HTTP 202).

---

## Contact Resources (Click-to-Edit)

### GET /contacts/{id}
**Purpose**: Establish SSE connection, send contact view, listen for updates

| Aspect | Value |
|--------|-------|
| Response Type | `text/event-stream` |
| Success | SSE stream with `datastar-merge-fragments` events |
| Not Found | HTTP 404 + error HTML fragment |

### GET /contacts/{id}/edit
**Purpose**: Fire-and-forget, posts edit form HTML to channel

| Aspect | Value |
|--------|-------|
| Response Type | Empty (fire-and-forget) |
| Success | HTTP 202 |
| Not Found | HTTP 404 |

### PUT /contacts/{id}
**Purpose**: Update contact, post updated view to channel

| Aspect | Value |
|--------|-------|
| Request Body | `ContactSignals` JSON |
| Response Type | Empty (fire-and-forget) |
| Success | HTTP 202 |
| Invalid Signals | HTTP 400 |
| Not Found | HTTP 404 |

---

## Fruits Resource (Search)

### GET /fruits
**Purpose**: Initial list (SSE) or search filter (fire-and-forget)

| Aspect | Without `?q=` | With `?q=term` |
|--------|---------------|----------------|
| Response Type | `text/event-stream` | Empty |
| Success | SSE stream | HTTP 202 |

---

## Items Resources (Delete)

### GET /items
**Purpose**: Establish SSE connection, send items table

| Aspect | Value |
|--------|-------|
| Response Type | `text/event-stream` |
| Success | SSE stream with table HTML |

### DELETE /items/{id}
**Purpose**: Remove item, post removeElement event to channel

| Aspect | Value |
|--------|-------|
| Response Type | Empty (fire-and-forget) |
| Success | HTTP 202 |
| Not Found | HTTP 404 |

---

## Users Resources (Bulk Update)

### GET /users
**Purpose**: Establish SSE connection, send users table

| Aspect | Value |
|--------|-------|
| Response Type | `text/event-stream` |
| Success | SSE stream with table HTML |

### PUT /users/bulk?status={active|inactive}
**Purpose**: Update selected users' status

| Aspect | Value |
|--------|-------|
| Request Body | `BulkUpdateSignals` JSON (selections array) |
| Response Type | Empty (fire-and-forget) |
| Success | HTTP 202 |
| Invalid Signals | HTTP 400 |

---

## Registration Resources (Form Validation)

### GET /registrations/form
**Purpose**: Establish SSE connection, send registration form

| Aspect | Value |
|--------|-------|
| Response Type | `text/event-stream` |
| Success | SSE stream with form HTML |

### POST /registrations/validate
**Purpose**: Validate fields, post feedback to channel

| Aspect | Value |
|--------|-------|
| Request Body | `RegistrationSignals` JSON |
| Response Type | Empty (fire-and-forget) |
| Success | HTTP 202 |
| Invalid Signals | HTTP 400 |

### POST /registrations
**Purpose**: Create registration if valid

| Aspect | Value |
|--------|-------|
| Request Body | `RegistrationSignals` JSON |
| Response Type | Empty (fire-and-forget) |
| Created | HTTP 201 |
| Validation Errors | HTTP 400 |
| Duplicate Email | HTTP 409 |

---

## HTTP Method Enforcement

All resources return HTTP 405 Method Not Allowed for unsupported methods.

| Resource | Allowed Methods |
|----------|----------------|
| /contacts/{id} | GET, PUT |
| /contacts/{id}/edit | GET |
| /fruits | GET |
| /items | GET |
| /items/{id} | DELETE |
| /users | GET |
| /users/bulk | PUT |
| /registrations/form | GET |
| /registrations/validate | POST |
| /registrations | POST |

---

## Signal Payload Contracts

### ContactSignals
```json
{
  "firstName": "string",
  "lastName": "string",
  "email": "string"
}
```

### BulkUpdateSignals
```json
{
  "selections": [true, false, true, false]
}
```

### RegistrationSignals
```json
{
  "email": "string",
  "firstName": "string",
  "lastName": "string"
}
```
