# Implementation Plan: Update Oxpecker Sample

**Branch**: `007-update-oxpecker-sample` | **Date**: 2026-02-02 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/007-update-oxpecker-sample/spec.md`

## Summary

Update the `Frank.Datastar.Oxpecker` sample to be a clone of `Frank.Datastar.Basic`, replacing the current MailboxProcessor-based SSE architecture with the validated broadcast channel pattern, while using Oxpecker.ViewEngine for HTML generation. All tests in `Frank.Datastar.Tests` must pass.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0
**Primary Dependencies**: Frank 6.x, Oxpecker.ViewEngine 2.x, StarFederation.Datastar.FSharp (via Frank.Datastar project reference)
**Storage**: In-memory (Dictionary, ResizeArray) - demo purposes only
**Testing**: Playwright via NUnit (Frank.Datastar.Tests)
**Target Platform**: ASP.NET Core web server (localhost:5000)
**Project Type**: Sample application (single project)
**Performance Goals**: N/A (demo application)
**Constraints**: Must produce identical HTML and behavior to Frank.Datastar.Basic
**Scale/Scope**: Single Program.fs file (~600 lines), one wwwroot/index.html file

## Constitution Check

*GATE: All principles checked and compliant.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | ✅ Pass | Uses `resource` computation expression for all endpoints |
| II. Idiomatic F# | ✅ Pass | Oxpecker.ViewEngine uses F# computation expressions |
| III. Library, Not Framework | ✅ Pass | Sample demonstrates Frank with pluggable view engine (Oxpecker) |
| IV. ASP.NET Core Native | ✅ Pass | Uses `HttpContext` directly, standard hosting |
| V. Performance Parity | ✅ Pass | Sample app, no performance-critical code |

**Technical Standards**:
- ✅ .NET 10.0 (current preview, project already targets this)
- ✅ F# 8.0+
- ✅ All tests must pass before merge
- ✅ Sample serves as integration test

## Project Structure

### Documentation (this feature)

```text
specs/007-update-oxpecker-sample/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api.md           # API endpoint documentation
└── tasks.md             # Phase 2 output (via /speckit.tasks)
```

### Source Code (repository root)

```text
sample/Frank.Datastar.Oxpecker/
├── Program.fs                    # Main application (to be updated)
├── Frank.Datastar.Oxpecker.fsproj
└── wwwroot/
    └── index.html                # Static page (to be copied from Basic)

sample/Frank.Datastar.Basic/
├── Program.fs                    # Reference implementation (read-only)
├── Frank.Datastar.Basic.fsproj
└── wwwroot/
    └── index.html                # Reference static page

sample/Frank.Datastar.Tests/
├── TestBase.fs
├── ClickToEditTests.fs
├── SearchFilterTests.fs
├── BulkUpdateTests.fs
├── StateIsolationTests.fs
└── ...                           # Other test files
```

**Structure Decision**: Single sample project with one F# source file. The Basic sample serves as the reference implementation; only the Oxpecker sample is modified.

## Implementation Approach

### Phase 1: Static Files and SSE Architecture

1. **Copy index.html from Basic to Oxpecker**
   - The Basic sample's index.html has `data-init="@get('/sse')"` on the body
   - This establishes a single SSE connection for all demo patterns
   - Current Oxpecker index.html lacks this and uses per-section SSE

2. **Replace SSE Architecture**
   - Remove MailboxProcessor-based channels (contactChannel, fruitsChannel, etc.)
   - Add SseEvent module with subscribe/unsubscribe/broadcast functions
   - Add `/sse` resource endpoint

### Phase 2: Data Types and Stores

1. **Copy data types verbatim from Basic**
   - Contact, ContactSignals
   - User, UserStatus, BulkUpdateSignals
   - Item
   - Registration, RegistrationSignals
   - SseEvent (without Close variant - Basic doesn't have it)

2. **Copy data stores verbatim from Basic**
   - contacts dictionary
   - users dictionary
   - items ResizeArray
   - fruits list
   - registrations ResizeArray

### Phase 3: Render Functions (Oxpecker.ViewEngine)

Convert each Basic sample render function from string interpolation to Oxpecker.ViewEngine:

| Function | Key Elements |
|----------|--------------|
| `renderContactView` | `div(id="contact-view")`, `p()`, `strong()`, `button().attr("data-on:click", ...)` |
| `renderContactEdit` | `.attr("data-signals", ...)`, `input().attr("data-bind:first-name", null)` |
| `renderFruitsList` | `ul(id="fruits-list")`, `style="min-height: 1em;"`, `li()` per fruit |
| `renderItemsTable` | `table(id="items-table")`, `tr(id=itemId)`, confirm dialog in data-on:click |
| `renderUsersTable` | `data-signals__ifmissing`, `data-effect`, `data-bind:selections`, checkbox array |
| `renderValidationFeedback` | Success/error divs with class |
| `renderRegistrationSuccess` | Success message with firstName |
| `renderRegistrationForm` | Form with `data-on:keydown__debounce.500ms` |

### Phase 4: Resource Handlers

Update each resource handler to:
1. Use `SseEvent.broadcast` instead of MailboxProcessor posts
2. Return 202/201/400/404 status codes matching Basic sample
3. Remove per-section SSE connection logic (GET handlers no longer establish SSE)

### Phase 5: Application Setup

1. Add `sseResource` for `/sse` endpoint
2. Add `debugPingResource` for `/debug/ping`
3. Register all resources in same order as Basic sample

## Complexity Tracking

> No constitution violations to justify. This is a straightforward port with a different view engine.

## Artifacts Generated

- [research.md](research.md) - Research findings and decisions
- [data-model.md](data-model.md) - Entity definitions
- [contracts/api.md](contracts/api.md) - API endpoint documentation
- [quickstart.md](quickstart.md) - Development and testing instructions
