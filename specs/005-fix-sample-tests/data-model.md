# Data Model: Enhanced Sample Test Validation

**Feature**: 005-fix-sample-tests
**Date**: 2026-01-27

## Overview

This document defines the test data model - the entities, expected values, and state transitions that tests validate. Tests do not create new data structures; they verify the existing sample application data.

## Test Entities

### Contact (Click-to-Edit)

**Seed Data**:
| ID | First Name | Last Name | Email |
|----|------------|-----------|-------|
| 1 | Joe | Smith | joe@smith.org |

**States**:
- View mode: Displays contact info with "Edit" button
- Edit mode: Shows form with `data-bind` inputs pre-populated with current values

**State Transitions**:
```
View → (GET /contacts/1/edit) → Edit
Edit → (PUT /contacts/1) → View (with updated values)
Edit → (GET /contacts/1) → View (cancel, original values)
```

**Validation Points**:
- Edit form shows current values in `data-signals` attribute
- After PUT, view shows updated values
- After cancel, view shows original values

### Fruits (Search Filtering)

**Seed Data**: 22 fruits alphabetically ordered
- Apple, Apricot, Banana, Blueberry, Cherry, Coconut, Date, Fig, Grape, Kiwi, Lemon, Lime, Mango, Orange, Papaya, Peach, Pear, Pineapple, Plum, Raspberry, Strawberry, Watermelon

**Search Behavior**:
| Query | Expected Results | Not Included |
|-------|------------------|--------------|
| "ap" | Apple, Apricot, Grape, Papaya | Banana, Cherry, etc. |
| "berry" | Blueberry, Raspberry, Strawberry | Apple, etc. |
| "xyz" | (empty) | All items |
| "" | All 22 fruits | None |

**Validation Points**:
- Search returns matching items (case-insensitive contains)
- Search does not return non-matching items
- Empty/cleared search returns full list

### Users (Bulk Update)

**Seed Data**:
| ID | Name | Email | Initial Status |
|----|------|-------|----------------|
| 1 | Joe Smith | joe@smith.org | Active |
| 2 | Jane Doe | jane@doe.com | Inactive |
| 3 | Bob Wilson | bob@wilson.net | Active |
| 4 | Alice Brown | alice@brown.io | Inactive |

**Bulk Update Behavior**:
- `PUT /users/bulk?status=active` with `selections: [true, false, true, false]` activates users 1 and 3
- `PUT /users/bulk?status=inactive` with `selections: [false, true, false, true]` deactivates users 2 and 4

**Validation Points**:
- After bulk activate, selected users show "Active" status
- After bulk deactivate, selected users show "Inactive" status
- Non-selected users retain their original status

### Items (Row Deletion)

**Seed Data**:
| ID | Name |
|----|------|
| 1 | Item 1 |
| 2 | Item 2 |
| 3 | Item 3 |
| 4 | Item 4 |

**Deletion Behavior**:
- `DELETE /items/{id}` removes item from list
- Subsequent GET does not include deleted item

**Validation Points**:
- After DELETE, item no longer appears in table
- DELETE of non-existent ID returns 404

### Registration (Form Validation)

**Seed Data**: Empty (no pre-existing registrations)

**Validation Rules**:
| Field | Rule |
|-------|------|
| email | Required, must contain "@" |
| firstName | Required |
| lastName | Required |

**State Transitions**:
```
Empty form → (POST /registrations/validate with empty) → Error feedback
Empty form → (POST /registrations/validate with valid) → Success feedback
Valid form → (POST /registrations) → 201 Created, success message
Duplicate email → (POST /registrations) → 409 Conflict
```

**Validation Points**:
- Empty fields produce error messages
- Valid fields produce success message
- Successful registration returns 201
- Duplicate email returns 409

## State Isolation Requirements

Tests must verify that these entities maintain separate state:

| Entity A | Entity B | Isolation Requirement |
|----------|----------|----------------------|
| Registration form | Contact edit form | email field in registration does not affect contact email |
| Contact signals | Registration signals | firstName/lastName are independent |

**Validation Points**:
- Submitting registration form does not change contact data
- Editing contact does not affect registration form values

## Test Sequence

Tests are sequential and stateful. Recommended order:

1. **Prerequisite checks**: Server running, seed data present
2. **Click-to-edit tests**: View → Edit → Save → Verify
3. **Search tests**: Full list → Filter → Clear → Verify
4. **Bulk update tests**: Initial state → Bulk activate → Verify → Bulk deactivate → Verify
5. **Item deletion tests**: Initial count → Delete → Verify count decreased
6. **Registration tests**: Validate empty → Validate valid → Create → Duplicate
7. **State isolation tests**: Cross-feature verification
8. **405 tests**: Method not allowed responses
