# Research: Update Oxpecker Sample

**Feature**: 007-update-oxpecker-sample
**Date**: 2026-02-02

## Research Summary

This feature has a validated reference implementation (Frank.Datastar.Basic), so research focuses on:
1. Understanding the architectural differences between current Oxpecker and Basic samples
2. Oxpecker.ViewEngine syntax for Datastar attributes

## Key Findings

### 1. SSE Architecture Difference

**Decision**: Replace Oxpecker's per-section MailboxProcessor channels with Basic's unified broadcast channel pattern.

**Rationale**:
- Basic sample uses a single `/sse` endpoint that the page connects to via `data-init="@get('/sse')"` on the body
- All fire-and-forget handlers broadcast to a shared `SseEvent` channel using a subscribe/broadcast pattern
- Current Oxpecker sample uses 5 separate MailboxProcessor-based channels (one per demo section)
- The unified pattern is simpler, tested, and matches what the tests expect

**Alternatives Considered**:
- Keep per-section channels: Rejected because tests expect the Basic sample's behavior
- Hybrid approach: Rejected as unnecessarily complex

### 2. Static Files (index.html)

**Decision**: Copy Basic sample's index.html to Oxpecker sample

**Rationale**:
- Basic: Uses `data-init="@get('/sse')"` on `<body>` - single SSE connection
- Oxpecker: Missing this - individual per-section SSE connections via "Load X" buttons
- The tests expect the Basic sample's HTML structure and behavior

**Differences Observed**:
- Basic has `button:disabled` CSS, Oxpecker doesn't
- Basic uses `data-bind:search` for fruit search, Oxpecker uses `$el.value`
- Both reference same Datastar CDN version (v1.0.0-RC.7)

### 3. Oxpecker.ViewEngine Syntax for Datastar

**Decision**: Use `.attr()` method for all `data-*` attributes since Oxpecker doesn't have built-in Datastar support.

**Rationale**:
- Standard attributes via constructor: `div(id="foo", class'="bar")`
- Custom attributes via `.attr()`: `button().attr("data-on:click", "@get('/sse')")`
- Datastar binding syntax (kebab-case): `input().attr("data-bind:first-name", null)`
- Boolean/valueless attributes: Pass `null` as value (e.g., `.attr("data-bind:email", null)`)

**Patterns from Oxpecker.ViewEngine docs**:
```fsharp
// Standard element
div(id="contact-view") { ... }

// With custom data-* attribute
button().attr("data-on:click", "@put('/contacts/1')") { "Save" }

// Input with binding (null for valueless attribute)
input(type'="text").attr("data-bind:firstName", null)

// Rendering to string
|> Render.toString
```

### 4. HTML Parity Requirements

**Decision**: Oxpecker render functions must produce identical HTML structure to Basic sample string templates.

**Critical Elements**:
| Basic HTML | Oxpecker Equivalent |
|------------|---------------------|
| `id="contact-view"` | `div(id="contact-view")` |
| `data-signals="{'firstName': 'Joe', ...}"` | `.attr("data-signals", "{'firstName': 'Joe', ...}")` |
| `data-on:click="@put('/contacts/1')"` | `.attr("data-on:click", "@put('/contacts/1')")` |
| `data-bind:first-name` | `.attr("data-bind:first-name", null)` |
| `data-indicator:_fetching` | `.attr("data-indicator:_fetching", null)` |
| `data-attr:disabled="$_fetching"` | `.attr("data-attr:disabled", "$_fetching")` |

### 5. Data Types and Module Structure

**Decision**: Copy data types and SseEvent module verbatim from Basic sample.

**Rationale**:
- Types are identical between samples (Contact, User, Item, Registration, etc.)
- SseEvent discriminated union should be identical
- SseEvent module functions (subscribe, unsubscribe, broadcast, writeSseEvent) should be identical
- Only render functions differ (string interpolation vs Oxpecker.ViewEngine)

## No Remaining Unknowns

All technical questions have been resolved through examination of:
- Frank.Datastar.Basic/Program.fs (reference implementation)
- Frank.Datastar.Oxpecker/Program.fs (current implementation)
- Oxpecker.ViewEngine documentation
- Frank.Datastar.Tests (test expectations)
