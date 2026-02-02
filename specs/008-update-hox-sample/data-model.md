# Data Model: Frank.Datastar.Hox Sample

**Feature**: 008-update-hox-sample
**Date**: 2026-02-02

## Overview

The data model is identical to Frank.Datastar.Basic - only the HTML rendering differs.

## Entities

### Contact

Represents a person for the click-to-edit pattern.

| Field | Type | Notes |
|-------|------|-------|
| Id | int | Primary key |
| FirstName | string | Editable |
| LastName | string | Editable |
| Email | string | Editable |

**Initial Data**: Single contact (Id=1, Joe Smith, joe@smith.org)

**Storage**: `Dictionary<int, Contact>` (mutable)

### User

Represents a user account for the bulk update pattern.

| Field | Type | Notes |
|-------|------|-------|
| Id | int | Primary key |
| Name | string | Display name |
| Email | string | Email address |
| Status | UserStatus | Active or Inactive |

**UserStatus** (Discriminated Union):
- `Active`
- `Inactive`

**Initial Data**: 4 users with mixed statuses

**Storage**: `Dictionary<int, User>` (mutable)

### Item

Represents a deletable row item.

| Field | Type | Notes |
|-------|------|-------|
| Id | int | Primary key |
| Name | string | Display name |

**Initial Data**: 4 items (Item 1-4)

**Storage**: `ResizeArray<Item>` (mutable)

### Registration

Represents a form submission.

| Field | Type | Notes |
|-------|------|-------|
| Id | int | Auto-incremented |
| Email | string | Must be unique |
| FirstName | string | Required |
| LastName | string | Required |

**Initial Data**: Empty

**Storage**: `ResizeArray<Registration>` + `mutable nextRegistrationId`

### Fruits

Static list of 22 fruit names for search demonstration.

**Storage**: `string list` (immutable)

## Signal Types (CLIMutable)

These types are used for deserializing Datastar signals from POST/PUT requests.

### ContactSignals

```fsharp
[<CLIMutable>]
type ContactSignals =
    { firstName: string
      lastName: string
      email: string }
```

### BulkUpdateSignals

```fsharp
[<CLIMutable>]
type BulkUpdateSignals = { selections: bool array }
```

### RegistrationSignals

```fsharp
[<CLIMutable>]
type RegistrationSignals =
    { email: string
      firstName: string
      lastName: string }
```

## SSE Event Types

### SseEvent

Discriminated union for SSE broadcast events.

```fsharp
type SseEvent =
    | PatchElements of html: string
    | RemoveElement of selector: string
    | PatchSignals of json: string
```

## State Transitions

### Contact State

```
View Mode ──[Edit click]──> Edit Mode
Edit Mode ──[Save click]──> View Mode (data updated)
Edit Mode ──[Cancel click]──> View Mode (data unchanged)
```

### User Status

```
Active ──[Deactivate]──> Inactive
Inactive ──[Activate]──> Active
```

### Item Lifecycle

```
Exists ──[Delete]──> Removed (row removed from table)
```

### Registration Lifecycle

```
(empty) ──[Valid Submit]──> Created (201)
(any) ──[Duplicate Email]──> Rejected (409)
(any) ──[Invalid Data]──> Rejected (400)
```
