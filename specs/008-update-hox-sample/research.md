# Research: Hox API Patterns for Frank.Datastar.Hox

**Feature**: 008-update-hox-sample
**Date**: 2026-02-02

## Summary

Research on translating Frank.Datastar.Basic's F# string templates to Hox `h()` calls with Datastar attributes.

## Decision 1: Hox CSS-Selector Syntax for Datastar Attributes

**Decision**: Use Hox's CSS-selector notation `[attr=value]` for all Datastar attributes

**Rationale**:
- Hox parser is very permissive - allows JSON, quotes, special characters within `[attr=value]` syntax
- Supports multiple attributes in sequence: `[data-on:click=...] [data-bind:name] [data-indicator:_fetching]`
- Compatible with all Datastar attribute patterns including complex JavaScript expressions

**Alternatives considered**:
- `.attr()` fluent API - more verbose, requires method chaining
- `attrs` record type - less readable for complex attributes

**Syntax patterns**:
```fsharp
// Simple attribute
h("button [data-on:click=@get('/path')]", [ Text "Click" ])

// Multiple attributes
h("input [type=text] [data-bind:firstName] [data-attr:disabled=$_fetching]", [])

// JSON in data-signals (use single quotes for JSON, F# double quotes for string)
h($"div [data-signals={{'firstName': '{value}', 'email': '{email}'}}]", [...])

// Complex JavaScript expressions
h("button [data-on:click=confirm('Delete?') && @delete('/items/{id}')]", [...])
```

## Decision 2: Render Function Signature

**Decision**: All render functions return `ValueTask<string>` via `Render.asString`

**Rationale**:
- `Render.asString` always returns `ValueTask<string>` (async)
- Callers must await the result before broadcasting
- Consistent pattern across all render functions

**Pattern**:
```fsharp
let renderContactView (contact: Contact) : ValueTask<string> =
    let node = h("div#contact-view", [...])
    Render.asString node

// Usage in handler:
task {
    let! html = renderContactView contact
    SseEvent.broadcast (PatchElements html)
}
```

## Decision 3: List Rendering with `fragment`

**Decision**: Use `fragment` function for rendering collections

**Rationale**:
- `fragment` handles parentless node collections
- Works with F# list comprehensions
- Clean syntax for table rows, list items, etc.

**Pattern**:
```fsharp
h("ul#fruits-list",
    fragment [ for f in fruits do h("li", [ Text f ]) ])

h("tbody#users-list",
    fragment [
        for user in users.Values do
            h("tr", [...])
    ])
```

## Decision 4: Text Content

**Decision**: Use `Text` wrapper for all text content

**Rationale**:
- Explicit text nodes prevent ambiguity
- Supports interpolated strings for dynamic content
- Consistent with Hox node model

**Pattern**:
```fsharp
h("p", [
    h("strong", [ Text "Label:" ])
    Text $" {value}"
])
```

## Decision 5: HTML Structure Matching

**Decision**: Generate identical HTML structure to Frank.Datastar.Basic

**Rationale**:
- Playwright tests depend on specific element IDs and selectors
- Client-side index.html expects specific container IDs
- Ensures test compatibility without modification

**Critical IDs to preserve**:
- `#contact-view` - Click-to-edit container
- `#fruits-list` - Search results list
- `#items-table`, `#items-list` - Delete demo table
- `#users-table-container`, `#users-list` - Bulk update table
- `#registration-form`, `#validation-feedback`, `#registration-result` - Form validation

## Translation Reference

### Basic Sample → Hox Sample Patterns

| Basic (string template) | Hox (h() call) |
|------------------------|----------------|
| `$"""<div id="x">"""` | `h("div#x", [...])` |
| `$"""<p><strong>Label:</strong> {value}</p>"""` | `h("p", [ h("strong", [ Text "Label:" ]); Text $" {value}" ])` |
| `$"""<button data-on:click="@get('/x')">"""` | `h("button [data-on:click=@get('/x')]", [ Text "..." ])` |
| `$"""<div data-signals="{{...}}">"""` | `h($"div [data-signals={{...}}]", [...])` |
| `data-bind:first-name` | `[data-bind:first-name]` |
| `data-indicator:_fetching` | `[data-indicator:_fetching]` |
| `data-attr:disabled="$_fetching"` | `[data-attr:disabled=$_fetching]` |

### Datastar Attribute Patterns

```fsharp
// Click handler
[data-on:click=@get('/contacts/{id}/edit')]

// Form binding (kebab-case for multi-word signals)
[data-bind:first-name]
[data-bind:email]

// Array binding
[data-bind:selections]
[data-bind:selections[{idx}]]

// Loading indicator
[data-indicator:_fetching]

// Conditional disable
[data-attr:disabled=$_fetching]

// Debounced events
[data-on:input__debounce.500ms=@post('/validate')]
[data-on:keydown__debounce.500ms=@post('/validate')]

// Signals initialization
[data-signals={'email': '', 'firstName': '', 'lastName': ''}]
[data-signals__ifmissing={_fetching: false, selections: Array(4).fill(false)}]

// Effects
[data-effect=$selections; $_all = $selections.every(Boolean)]

// Confirm dialog
[data-on:click=confirm('Delete?') && @delete('/items/{id}')]
```

## Open Issues

None - all patterns validated against existing Hox sample and parser tests.
