# Data Model: Frank.Datastar.Oxpecker Sample

**Date**: 2026-01-27
**Feature**: 004-datastar-oxpecker-sample

## Overview

The data model is identical to `Frank.Datastar.Basic` and `Frank.Datastar.Hox`. All entities use in-memory storage (Dictionary, ResizeArray) for demonstration purposes.

---

## Entities

### Contact

Used for the Click-to-Edit pattern.

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

**Storage**: `Dictionary<int, Contact>` with initial data (Id=1, Joe Smith)

**State Transitions**:
- View mode → Edit mode (GET /contacts/{id}/edit)
- Edit mode → View mode (PUT /contacts/{id} or GET /contacts/{id})

---

### User

Used for the Bulk Update pattern.

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
type BulkUpdateSignals = { selections: bool[] }
```

**Storage**: `Dictionary<int, User>` with 4 initial users

**State Transitions**:
- Status: Active ↔ Inactive (via PUT /users/bulk)

---

### Item

Used for the Row Deletion pattern.

```fsharp
type Item = { Id: int; Name: string }
```

**Storage**: `ResizeArray<Item>` with 4 initial items

**State Transitions**:
- Exists → Deleted (DELETE /items/{id})

---

### Registration

Used for the Form Validation pattern.

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

**Storage**: `ResizeArray<Registration>` (starts empty)

**Validation Rules**:
- Email: Required, must contain "@"
- FirstName: Required (non-whitespace)
- LastName: Required (non-whitespace)
- Email: Must be unique (no duplicates)

**State Transitions**:
- Form submitted → Validated → Created (201) or Error (400/409)

---

### Fruits

Used for the Search/Filter pattern.

```fsharp
let fruits: string list = [ "Apple"; "Apricot"; ... ]
```

**Storage**: Static immutable list (22 fruits)

**No State Transitions** - read-only data

---

## SSE Channel Model

Each demo section has its own SSE channel implemented as a MailboxProcessor.

```fsharp
type SseEvent =
    | PatchElements of html: string
    | RemoveElement of selector: string
    | PatchSignals of json: string
    | Close

type SseChannelMsg =
    | Subscribe of replyChannel: AsyncReplyChannel<SseEvent>
    | Broadcast of SseEvent
```

**Channels**:
- `contactChannel` - Contact edit/view updates
- `fruitsChannel` - Search result updates
- `itemsChannel` - Item deletion updates
- `usersChannel` - Bulk update results
- `registrationChannel` - Validation feedback and results

---

## Entity Relationships

```text
┌─────────────┐     ┌─────────────┐
│   Contact   │     │    User     │
│ (1:1 edit)  │     │ (bulk sel)  │
└─────────────┘     └─────────────┘
       │                   │
       ▼                   ▼
┌─────────────────────────────────┐
│          SSE Channels           │
│  (MailboxProcessor per section) │
└─────────────────────────────────┘
       ▲                   ▲
       │                   │
┌─────────────┐     ┌─────────────┐
│    Item     │     │Registration │
│  (delete)   │     │  (create)   │
└─────────────┘     └─────────────┘
```

No foreign key relationships exist between entities - each pattern is independent.
