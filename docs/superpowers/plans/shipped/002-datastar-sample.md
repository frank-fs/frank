---
source: specs/002-datastar-sample
type: plan
---

# Implementation Plan: Frank.Datastar Sample Application

**Branch**: `002-datastar-sample` | **Date**: 2026-01-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-datastar-sample/spec.md`

## Summary

Enhance the Frank.Datastar sample application to demonstrate RESTful HTTP resource patterns combined with Datastar's hypermedia-first approach. Add five new examples (Click-to-Edit, Search, Delete Row, Bulk Update, Form Validation) using proper resource-oriented URLs and HTTP methods, while preserving all 10 existing examples. All HTML generation uses F# string templates.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting)
**Primary Dependencies**: Frank 6.x, StarFederation.Datastar.FSharp (latest), ASP.NET Core
**Storage**: In-memory (Dictionary, ResizeArray) - demo purposes only
**Testing**: Expecto via `dotnet test` - tests Frank.Datastar library, not sample app
**Target Platform**: Cross-platform server (.NET runtime)
**Project Type**: Library + Sample application
**Performance Goals**: N/A (educational sample)
**Constraints**: Must preserve existing 10 examples per FR-009
**Scale/Scope**: Single sample application with ~15 examples total

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### Principle I: Resource-Oriented Design - **PASS**

- All new endpoints use resource URLs (`/contacts/{id}`, `/fruits`, `/items/{id}`, `/users`)
- HTTP methods match semantics: GET=retrieve, PUT=update, DELETE=remove, POST=create
- No RPC-style URLs (`/doSomething`, `/performAction`)
- Query parameters used for filtering (`/fruits?q=term`), not action specification

### Principle II: Idiomatic F# - **PASS**

- Uses `resource` computation expression for all endpoint definitions
- F# string templates (`$"..."`) for HTML generation
- Option types for handling missing resources
- Pipeline-friendly helper functions (`patchElements`, `removeElement`)

### Principle III: Library, Not Framework - **PASS**

- No view engine dependency - uses raw F# string templates
- No ORM - uses simple in-memory collections
- No authentication system
- Sample demonstrates Frank usage without imposing additional dependencies

### Principle IV: ASP.NET Core Native - **PASS**

- Uses `HttpContext` directly in handlers
- Integrates with ASP.NET Core routing
- No abstractions hiding the platform
- Standard `IWebHostBuilder` patterns

### Principle V: Performance Parity - **PASS (N/A)**

- Sample application - performance benchmarking not applicable
- Uses `inline` helper functions for zero overhead
- No new abstractions that could impact performance

### Post-Design Re-check - **PASS**

All new patterns maintain constitution compliance:
- Click-to-Edit: Resource at `/contacts/{id}` with GET/PUT
- Search: Collection at `/fruits` with query parameter filtering
- Delete: Resource at `/items/{id}` with DELETE
- Bulk Update: Collection operation at `/users/bulk` with PUT
- Validation: Separate validation resource at `/registrations/validate`

## Project Structure

### Documentation (this feature)

```text
specs/002-datastar-sample/
├── plan.md              # This file
├── research.md          # Phase 0 output - COMPLETE
├── data-model.md        # Phase 1 output - COMPLETE
├── quickstart.md        # Phase 1 output - COMPLETE
├── contracts/           # Phase 1 output - COMPLETE
│   └── api.md           # HTTP API contracts
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
sample/Frank.Datastar.Basic/
├── Program.fs                    # Main application - ADD new examples
├── Frank.Datastar.Basic.fsproj   # Project file - NO CHANGES
└── wwwroot/
    └── index.html                # HTML page - ADD new sections

src/Frank.Datastar/
├── Frank.Datastar.fs             # Library - NO CHANGES (already complete)
└── Frank.Datastar.fsproj         # Project file - NO CHANGES

test/Frank.Datastar.Tests/
├── DatastarTests.fs              # Tests - NO NEW TESTS (per assumptions)
└── Frank.Datastar.Tests.fsproj   # Project file - NO CHANGES
```

**Structure Decision**: Single sample project structure. All new code goes into existing `Program.fs` and `index.html` files, preserving the existing examples and adding new sections.

## Complexity Tracking

No violations requiring justification. All changes stay within existing project structure.

## Phase Summary

### Phase 0: Research - COMPLETE

- Researched Datastar patterns from official documentation
- Identified RESTful URL structures for each example
- Documented signal types and data models
- See `research.md` for full details

### Phase 1: Design & Contracts - COMPLETE

- Defined F# types for all entities (Contact, User, Item, Registration)
- Designed API contracts for all endpoints
- Created quickstart documentation
- See `data-model.md`, `contracts/api.md`, `quickstart.md`

### Phase 2: Task Generation - PENDING

Run `/speckit.tasks` to generate implementation tasks.

## Implementation Notes

### Preserving Existing Examples (FR-009)

The following existing examples MUST be preserved in Program.fs:
1. displayDate
2. removeDate
3. searchItems
4. loadItemsPage
5. greetUser
6. loadProducts
7. clock
8. dashboardRefresh
9. viewProfile
10. counter (increment/decrement/reset)

New examples will be added AFTER the existing examples with clear section headers.

### SSE Architecture

**Key constraint**: SSE connections are long-lived. Opening multiple per page exhausts browser connection limits.

| Endpoint | Establishes SSE? | Notes |
|----------|------------------|-------|
| GET /contacts/{id} | Yes | Keeps connection for edit/save cycle |
| PUT /contacts/{id} | Fire-and-forget or SSE | Updates sent over existing connection or new stream |
| GET /fruits | Yes | Keeps connection for search updates |
| GET /items | Yes | Keeps connection for delete updates |
| DELETE /items/{id} | No (fire-and-forget) | Returns 202, removal sent over existing SSE |
| GET /users | Yes | Keeps connection for bulk updates |
| PUT /users/bulk | Fire-and-forget or SSE | Updates sent over existing connection or new stream |
| POST /registrations | Yes | Streams validation, success, optional redirect |

### URL Patterns Summary

| Pattern | Resource | URLs | Methods |
|---------|----------|------|---------|
| Click-to-Edit | Contact | `/contacts/{id}`, `/contacts/{id}/edit` | GET, PUT |
| Search | Fruits | `/fruits?q={term}` | GET |
| Delete | Item | `/items/{id}` | GET (SSE), DELETE (fire-and-forget) |
| Bulk Update | Users | `/users`, `/users/bulk?status={status}` | GET (SSE), PUT |
| Validation | Registration | `/registrations/validate`, `/registrations` | POST (SSE) |

### HTML Sections to Add in index.html

1. **RESTful Patterns** header section
2. Click-to-Edit Contact demo
3. Searchable Fruits demo
4. Deletable Items demo
5. Bulk Update Users demo
6. Form Validation Registration demo

### Dependencies

No new NuGet packages required. All functionality uses existing:
- Frank 6.x
- Frank.Datastar (local)
- StarFederation.Datastar.FSharp

## Next Steps

1. Run `/speckit.tasks` to generate detailed implementation tasks
2. Implement tasks in dependency order
3. Test each pattern manually
4. Verify all existing tests still pass
