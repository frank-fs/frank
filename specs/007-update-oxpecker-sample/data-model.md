# Data Model: Update Oxpecker Sample

**Feature**: 007-update-oxpecker-sample
**Date**: 2026-02-02

## Overview

This sample uses in-memory data stores for demonstration purposes. All types are copied from the Basic sample to ensure behavioral parity.

## Entities

### Contact (Click-to-Edit Pattern)

```fsharp
type Contact =
    { Id: int
      FirstName: string
      LastName: string
      Email: string }

[<CLIMutable>]
type ContactSignals =
    { firstName: string
      lastName: string
      email: string }
```

**Store**: `Dictionary<int, Contact>` initialized with one contact (Id=1, Joe Smith)

**Relationships**: None

**Validation**: None (signals are trusted from client)

### User (Bulk Update Pattern)

```fsharp
type UserStatus =
    | Active
    | Inactive

type User =
    { Id: int
      Name: string
      Email: string
      Status: UserStatus }

[<CLIMutable>]
type BulkUpdateSignals = { selections: bool array }
```

**Store**: `Dictionary<int, User>` initialized with 4 users

**Relationships**: None

**State Transitions**: Active ↔ Inactive (via bulk update)

### Item (Row Deletion Pattern)

```fsharp
type Item = { Id: int; Name: string }
```

**Store**: `ResizeArray<Item>` initialized with 4 items

**Relationships**: None

**Deletion**: Items are removed from ResizeArray by index lookup

### Registration (Form Validation Pattern)

```fsharp
type Registration =
    { Id: int
      Email: string
      FirstName: string
      LastName: string }

[<CLIMutable>]
type RegistrationSignals =
    { email: string
      firstName: string
      lastName: string }
```

**Store**: `ResizeArray<Registration>` (starts empty)

**Auto-increment**: `mutable nextRegistrationId = 1`

**Validation Rules**:
- Email: Required, must contain "@"
- FirstName: Required
- LastName: Required
- Email uniqueness: Checked on submission (409 Conflict if duplicate)

## SSE Event Types

```fsharp
type SseEvent =
    | PatchElements of html: string
    | RemoveElement of selector: string
    | PatchSignals of json: string
```

**Usage**:
- `PatchElements`: Replace/insert HTML via `Datastar.patchElements`
- `RemoveElement`: Remove element by CSS selector via `Datastar.removeElement`
- `PatchSignals`: Update client signals via `Datastar.patchSignals`

## Static Data

### Fruits (Search Pattern)

```fsharp
let fruits = [ "Apple"; "Apricot"; "Banana"; ... ] // 22 fruits
```

**Filtering**: Case-insensitive substring match on query parameter `q`

## Data Flow

```
Client Action → HTTP Handler → SseEvent.broadcast → SSE Endpoint → Client
                                      ↓
                            All subscribed connections
```

1. Client initiates action via Datastar attribute (`data-on:click`, etc.)
2. Handler processes request, updates in-memory store if needed
3. Handler broadcasts `SseEvent` to all subscribers
4. SSE endpoint forwards event to connected clients
5. Datastar on client applies the patch
