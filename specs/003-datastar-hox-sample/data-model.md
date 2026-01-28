# Data Model: Frank.Datastar.Hox Sample

**Feature**: 003-datastar-hox-sample
**Date**: 2026-01-27

## Overview

This data model mirrors the Frank.Datastar.Basic sample exactly. All entity types, relationships, and in-memory storage patterns are identical.

## Entities

### Contact

Represents a person for the click-to-edit pattern demonstration.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | int | Primary key | Immutable after creation |
| FirstName | string | Required | Display name component |
| LastName | string | Required | Display name component |
| Email | string | Required | Contact email address |

**State Transitions**: None (simple CRUD)

**Initial Data**:
- Contact 1: Joe Smith (joe@smith.org)

### User

Represents a user account for the bulk update pattern demonstration.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | int | Primary key | Immutable after creation |
| Name | string | Required | Full display name |
| Email | string | Required | User email address |
| Status | UserStatus | Required | Active or Inactive |

**State Transitions**:
- Active → Inactive (via bulk update)
- Inactive → Active (via bulk update)

**Initial Data**:
- User 1: Joe Smith (Active)
- User 2: Jane Doe (Inactive)
- User 3: Bob Wilson (Active)
- User 4: Alice Brown (Inactive)

### UserStatus (Discriminated Union)

```fsharp
type UserStatus =
    | Active
    | Inactive
```

### Item

Represents a deletable row for the deletion pattern demonstration.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | int | Primary key | Immutable after creation |
| Name | string | Required | Display name |

**State Transitions**: None (delete removes from collection)

**Initial Data**:
- Item 1: "Item 1"
- Item 2: "Item 2"
- Item 3: "Item 3"
- Item 4: "Item 4"

### Registration

Represents a form submission for the validation pattern demonstration.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | int | Primary key, auto-increment | Assigned on creation |
| Email | string | Required, unique, must contain @ | Validation target |
| FirstName | string | Required | Validation target |
| LastName | string | Required | Validation target |

**State Transitions**: None (create only)

**Initial Data**: Empty collection

**Validation Rules**:
- Email: Must not be empty, must contain "@"
- FirstName: Must not be empty
- LastName: Must not be empty

### Fruits (Static List)

Static list of fruit names for search demonstration. Not an entity - just reference data.

```fsharp
let fruits = [
    "Apple"; "Apricot"; "Banana"; "Blueberry"; "Cherry"; "Coconut";
    "Date"; "Fig"; "Grape"; "Kiwi"; "Lemon"; "Lime"; "Mango";
    "Orange"; "Papaya"; "Peach"; "Pear"; "Pineapple"; "Plum";
    "Raspberry"; "Strawberry"; "Watermelon"
]
```

## Signal Types (Datastar Integration)

These CLIMutable types are used to deserialize JSON signals from Datastar.

### ContactSignals

```fsharp
[<CLIMutable>]
type ContactSignals = {
    firstName: string
    lastName: string
    email: string
}
```

### BulkUpdateSignals

```fsharp
[<CLIMutable>]
type BulkUpdateSignals = {
    selections: bool[]
}
```

### RegistrationSignals

```fsharp
[<CLIMutable>]
type RegistrationSignals = {
    email: string
    firstName: string
    lastName: string
}
```

## In-Memory Storage

| Entity | Storage Type | Mutability | Notes |
|--------|--------------|------------|-------|
| Contact | `Dictionary<int, Contact>` | Mutable reference | Supports update by ID |
| User | `Dictionary<int, User>` | Mutable reference | Supports bulk status updates |
| Item | `ResizeArray<Item>` | Mutable collection | Supports removal by index |
| Registration | `ResizeArray<Registration>` | Mutable collection | Append-only, duplicate check |
| Fruits | `string list` | Immutable | Static reference data |

## SSE Channel Architecture

Each demo section has its own SSE channel (MailboxProcessor) for real-time updates:

| Channel | Purpose | Event Types |
|---------|---------|-------------|
| contactChannel | Click-to-edit updates | PatchElements |
| fruitsChannel | Search result updates | PatchElements |
| itemsChannel | Delete notifications | RemoveElement |
| usersChannel | Bulk update results | PatchElements |
| registrationChannel | Validation feedback, success | PatchElements |

**SSE Event Types**:
```fsharp
type SseEvent =
    | PatchElements of html: string
    | RemoveElement of selector: string
    | PatchSignals of json: string
    | Close
```
