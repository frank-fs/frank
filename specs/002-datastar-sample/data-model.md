# Data Model: Frank.Datastar Sample Application

**Feature Branch**: `002-datastar-sample`
**Date**: 2026-01-27

## Entities

### Contact

Represents a person for the click-to-edit pattern demonstration.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | Primary key, auto-increment | Unique identifier |
| FirstName | string | Required, max 50 chars | Contact's first name |
| LastName | string | Required, max 50 chars | Contact's last name |
| Email | string | Required, valid email format | Contact's email address |

**State Transitions**: None (simple CRUD)

**Validation Rules**:
- FirstName: Not empty, max 50 characters
- LastName: Not empty, max 50 characters
- Email: Not empty, valid email format

---

### Fruit

Represents a searchable item in a collection.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Name | string | Required | Fruit name |

**State Transitions**: None (read-only collection)

**Notes**: Stored as a simple string list. No ID needed as search returns filtered views, not individual items.

---

### Item

Represents a deletable list item.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | Primary key | Unique identifier |
| Name | string | Required | Item display name |

**State Transitions**:
- Exists -> Deleted (via DELETE)

**Notes**: Once deleted, item cannot be recovered (demo simplicity).

---

### User

Represents a user for bulk status updates.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | Primary key | Unique identifier |
| Name | string | Required | User's display name |
| Email | string | Required | User's email |
| Status | UserStatus | Required | Active or Inactive |

**UserStatus Enum**:
- `Active`
- `Inactive`

**State Transitions**:
- Active <-> Inactive (via bulk PUT)

---

### Registration

Represents a user registration with validation.

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| Id | int | Primary key, auto-increment | Unique identifier |
| Email | string | Required, unique, valid format | Registration email |
| FirstName | string | Required, max 50 chars | First name |
| LastName | string | Required, max 50 chars | Last name |

**Validation Rules**:
- Email: Required, valid email format, must be unique
- FirstName: Required, non-empty
- LastName: Required, non-empty

**Notes**: Validation occurs at `/registrations/validate` before creation at `/registrations`.

---

## Relationships

```
┌─────────────┐     ┌─────────────┐
│   Contact   │     │    Fruit    │
│  (editable) │     │ (searchable)│
└─────────────┘     └─────────────┘
      │                   │
      │ Click-to-Edit     │ Search/Filter
      │ Pattern           │ Pattern
      ▼                   ▼
┌─────────────────────────────────────────┐
│          Sample Application             │
│  Demonstrates RESTful Datastar Patterns │
└─────────────────────────────────────────┘
      ▲                   ▲
      │ Delete            │ Bulk Update
      │ Pattern           │ Pattern
      │                   │
┌─────────────┐     ┌─────────────┐
│    Item     │     │    User     │
│ (deletable) │     │(bulk status)│
└─────────────┘     └─────────────┘
                          │
                          │ Form Validation
                          │ Pattern
                          ▼
                   ┌─────────────┐
                   │Registration │
                   │ (validated) │
                   └─────────────┘
```

---

## F# Type Definitions

```fsharp
// Contact for click-to-edit
type Contact = {
    Id: int
    FirstName: string
    LastName: string
    Email: string
}

// User status for bulk updates
type UserStatus = Active | Inactive

// User for bulk status updates
type User = {
    Id: int
    Name: string
    Email: string
    Status: UserStatus
}

// Item for row deletion
type Item = {
    Id: int
    Name: string
}

// Registration for form validation
type Registration = {
    Id: int
    Email: string
    FirstName: string
    LastName: string
}

// Signal types for Datastar communication
type ContactSignals = {
    firstName: string
    lastName: string
    email: string
}

type BulkUpdateSignals = {
    selections: bool[]
    status: string  // "active" or "inactive"
}

type RegistrationSignals = {
    email: string
    firstName: string
    lastName: string
}

type SearchSignals = {
    search: string
}
```

---

## In-Memory Storage

```fsharp
// Initial seed data
let mutable contacts =
    dict [
        1, { Id = 1; FirstName = "Joe"; LastName = "Smith"; Email = "joe@smith.org" }
    ]

let fruits = [ "Apple"; "Apricot"; "Banana"; "Blueberry"; "Cherry"; "Coconut";
               "Date"; "Fig"; "Grape"; "Kiwi"; "Lemon"; "Lime"; "Mango";
               "Orange"; "Papaya"; "Peach"; "Pear"; "Pineapple"; "Plum";
               "Raspberry"; "Strawberry"; "Watermelon" ]

let mutable items =
    ResizeArray [
        { Id = 1; Name = "Item 1" }
        { Id = 2; Name = "Item 2" }
        { Id = 3; Name = "Item 3" }
        { Id = 4; Name = "Item 4" }
    ]

let mutable users =
    dict [
        1, { Id = 1; Name = "Joe Smith"; Email = "joe@smith.org"; Status = Active }
        2, { Id = 2; Name = "Jane Doe"; Email = "jane@doe.com"; Status = Inactive }
        3, { Id = 3; Name = "Bob Wilson"; Email = "bob@wilson.net"; Status = Active }
        4, { Id = 4; Name = "Alice Brown"; Email = "alice@brown.io"; Status = Inactive }
    ]

let mutable registrations = ResizeArray<Registration>()
let mutable nextRegistrationId = 1
```
