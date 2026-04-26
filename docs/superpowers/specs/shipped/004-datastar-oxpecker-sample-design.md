---
source: specs/004-datastar-oxpecker-sample
status: complete
type: spec
---

# Feature Specification: Frank.Datastar.Oxpecker Sample

**Feature Branch**: `004-datastar-oxpecker-sample`
**Created**: 2026-01-27
**Status**: Draft
**Input**: User description: "Generate a sample matching the implementation of sample/Frank.Datastar.Basic and sample/Frank.Datastar.Hox in sample/Frank.Datastar.Oxpecker. Use the same test.sh script as is used in those projects to verify behavior. Use the Oxpecker.ViewEngine library."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - F# Developer Evaluates Oxpecker.ViewEngine with Datastar (Priority: P1)

An F# developer wants to see how Oxpecker.ViewEngine (computation expression-based HTML DSL) integrates with Frank and Datastar for building reactive web applications. They clone the Frank repository and run the Oxpecker sample to understand the patterns.

**Acceptance Scenarios**:

1. **Given** a developer has cloned the Frank repository, **When** they navigate to sample/Frank.Datastar.Oxpecker and run `dotnet build`, **Then** the project compiles without errors.
2. **Given** the sample is running, **When** the developer runs `./test.sh`, **Then** all tests pass (same tests as Frank.Datastar.Basic and Frank.Datastar.Hox).
3. **Given** the sample is running, **When** the developer opens the application in a browser, **Then** they see a functional demo with all Datastar patterns working.

---

### User Story 2 - Developer Compares View Engine Approaches (Priority: P2)

A developer wants to compare the Oxpecker.ViewEngine approach (computation expressions) with the Hox approach (CSS selector DSL) and raw string templates for generating HTML in Datastar applications.

**Acceptance Scenarios**:

1. **Given** the Oxpecker sample exists, **When** a developer compares it with Frank.Datastar.Basic, **Then** both implement the same RESTful resource patterns (contacts, fruits, items, users, registrations).
2. **Given** the Oxpecker sample exists, **When** a developer compares it with Frank.Datastar.Hox, **Then** the only significant difference is the HTML rendering approach (Oxpecker.ViewEngine vs Hox).

---

### User Story 3 - Sample Passes Automated Testing (Priority: P3)

**Acceptance Scenarios**:

1. **Given** the Oxpecker sample is running on port 5000, **When** `./test.sh` is executed, **Then** tests 11-28 all pass (same test numbers as other samples).

---

### Edge Cases

- What happens when an invalid contact ID is requested? Returns HTTP 404 with error message.
- What happens when a duplicate email is submitted for registration? Returns HTTP 409 conflict.
- What happens when signals JSON is malformed? Returns HTTP 400 bad request.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a sample project at `sample/Frank.Datastar.Oxpecker/` with identical functionality to `sample/Frank.Datastar.Basic` and `sample/Frank.Datastar.Hox`.
- **FR-002**: System MUST use `Oxpecker.ViewEngine` library for HTML generation instead of raw F# string templates or Hox.
- **FR-003**: Sample MUST implement all five RESTful resource patterns: Click-to-Edit (Contact), Search (Fruits), Delete (Items), Bulk Update (Users), Form Validation (Registration).
- **FR-004**: Sample MUST use the same test.sh script from the other samples to verify behavior.
- **FR-005**: Sample MUST target .NET 10.0 to match the other Datastar samples.
- **FR-006**: Sample MUST reference `Frank.Datastar` project (via ProjectReference) for Datastar SSE functionality.
- **FR-007**: Sample MUST use Oxpecker.ViewEngine's `Render.toString` function (or equivalent) to convert HtmlElement to string for SSE responses.
- **FR-008**: Sample MUST use Oxpecker.ViewEngine's computation expression syntax (e.g., `div(id="foo") { p() { "text" } }`) for HTML generation.
- **FR-009**: Sample MUST support custom data-* attributes for Datastar bindings using the `.attr()` or `.data()` extension methods.

### Key Entities

- **Contact**: Represents a contact with Id, FirstName, LastName, Email - used for click-to-edit pattern.
- **Item**: Represents a deletable item with Id, Name - used for row deletion pattern.
- **User**: Represents a user with Id, Name, Email, Status (Active/Inactive) - used for bulk update pattern.
- **Registration**: Represents a registration with Id, Email, FirstName, LastName - used for form validation pattern.
- **Fruits**: Static list of fruit names - used for search/filter pattern.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 18 tests in test.sh pass when run against the Oxpecker sample (tests 11-28).
- **SC-002**: The project compiles without errors or warnings using `dotnet build`.
- **SC-003**: The sample demonstrates Oxpecker.ViewEngine's computation expression syntax clearly in all render functions.
- **SC-004**: The sample code structure mirrors the other Datastar samples for easy comparison.
- **SC-005**: No Hox or raw HTML string templates are used - all HTML is generated via Oxpecker.ViewEngine.

## Research

# Research: Frank.Datastar.Oxpecker Sample

**Date**: 2026-01-27
**Feature**: 004-datastar-oxpecker-sample

## Research Summary

This sample has minimal unknowns since it replicates existing implementations. Research focuses on Oxpecker.ViewEngine API patterns for Datastar integration.

---

## 1. Oxpecker.ViewEngine HTML Generation

**Decision**: Use `Oxpecker.ViewEngine.Render.toString` for synchronous HTML string generation.

**Rationale**:
- Oxpecker.ViewEngine's `Render.toString` function converts any `HtmlElement` to a string synchronously
- This matches Datastar SSE pattern where small HTML fragments are sent
- Simpler than Hox's async `Render.asString` - no `task {}` wrapper needed for rendering

**Code Pattern**:
```fsharp
open Oxpecker.ViewEngine
open Oxpecker.ViewEngine.Render

let view = div(id="contact-view") { p() { "Hello" } }
let html = toString view  // Synchronous, returns string
do! Datastar.patchElements html ctx
```

---

## 2. Datastar Custom Attributes

**Decision**: Use `.attr()` extension method for `data-*` attributes.

**Rationale**:
- Oxpecker.ViewEngine provides `.attr(name, value)` for arbitrary attributes
- Datastar attributes like `data-on:click`, `data-bind:firstName` require colon in name
- `.attr("data-on:click", "@get('/path')")` works correctly

**Code Pattern**:
```fsharp
// For data-on:click attribute
button().attr("data-on:click", "@get('/contacts/1/edit')") { "Edit" }

// For data-bind:firstName attribute
input(type'="text").attr("data-bind:firstName", null)

// For data-signals (JSON object)
div(id="form").attr("data-signals", "{'firstName': '', 'lastName': ''}") { ... }
```

---

## 3. Oxpecker.ViewEngine Syntax Patterns

**Key Patterns**:

| Pattern | Oxpecker.ViewEngine Syntax |
|---------|---------------------------|
| Element with ID | `div(id="foo") { ... }` |
| Element with class | `div(class'="card") { ... }` (note apostrophe - `class'`) |
| Text content | `p() { "text" }` or `p() { $"Hello {name}" }` |
| Void element | `input(type'="text")` (no braces) |
| Loop | `for item in items do li() { item.Name }` |
| Fragment | `Fragment() { child1; child2 }` |

**Note on Reserved Words**:
- `class` is F# keyword, use `class'`
- `type` is F# keyword, use `type'`

---

## 4. Converting Hox Patterns to Oxpecker

| Hox Pattern | Oxpecker.ViewEngine Equivalent |
|-------------|-------------------------------|
| `h("div#id.class", [...])` | `div(id="id", class'="class") { ... }` |
| `h("button [data-on:click=@get('/x')]", [...])` | `button().attr("data-on:click", "@get('/x')") { ... }` |
| `Text "hello"` | `"hello"` (implicit in computation expression) |
| `fragment [...]` | `Fragment() { ... }` or inline children |

---

## 5. Project Configuration

**Decision**: Reference Oxpecker.ViewEngine 2.x from NuGet.

## Research Conclusion

All technical decisions are resolved. Implementation can proceed with:
1. Standard Oxpecker.ViewEngine computation expressions
2. `.attr()` extension for Datastar-specific attributes
3. `Render.toString` for synchronous HTML generation
4. Direct translation from Hox sample patterns
