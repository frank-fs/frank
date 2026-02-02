# Research: Hox Integration for Frank.Datastar Sample

**Feature**: 003-datastar-hox-sample
**Date**: 2026-01-27

## Research Questions

### 1. How to generate HTML with specific element IDs using Hox?

**Decision**: Use Hox's CSS selector syntax: `h("div#element-id.classname", children)`

**Rationale**: Hox's `h()` function accepts CSS selectors as the first argument. The syntax `h("tag#id.class [attr=value]", children)` creates elements with the specified tag, id, classes, and attributes.

**Example**:
```fsharp
// Creates: <div id="contact-view" class="error">...</div>
h("div#contact-view.error", [ Text "Contact not found." ])
```

**Alternatives considered**:
- Using `.attr("id", "value")` extension - Works but less concise
- Building elements programmatically - More verbose, less idiomatic

### 2. How to set Datastar-specific attributes (data-on:click, data-bind, data-signals)?

**Decision**: Use bracket notation in CSS selector: `h("button [data-on:click=@get('/path')]", children)`

**Rationale**: Hox's selector parser supports attribute syntax. For complex attribute values with special characters, the bracket notation handles them correctly.

**Example**:
```fsharp
// Creates: <button data-on:click="@get('/contacts/1/edit')">Edit</button>
h($"button [data-on:click=@get('/contacts/{id}/edit')]", [ Text "Edit" ])
```

**Alternatives considered**:
- Using `.attr()` extension method - Works for dynamic values but less readable
- String interpolation in selector - Works well for dynamic paths

### 3. How to handle Hox's async rendering with Datastar's patchElements?

**Decision**: Use `Render.asString` to convert Hox nodes to HTML strings, then pass to `Datastar.patchElements`

**Rationale**: Hox provides `Render.asString : Node -> Task<string>` which asynchronously renders a node tree to a string. This integrates cleanly with Frank.Datastar's `patchElements` which expects a string.

**Example**:
```fsharp
let node = h("div#contact-view", [ ... ])
let! html = Render.asString node
do! Datastar.patchElements html ctx
```

**Alternatives considered**:
- `Render.asStringAsync` - Same result, different API shape
- Streaming render - Not needed for small HTML fragments

### 4. How to represent JSON-like data-signals attribute values?

**Decision**: Use string interpolation with escaped single quotes in the selector

**Rationale**: The data-signals attribute requires a JSON-like object. Hox's selector parser handles attribute values with single quotes.

**Example**:
```fsharp
// Creates: <div id="users-table-container" data-signals="{'selections': [false, false, false, false]}">
h($"div#users-table-container [data-signals={{'selections': [false, false, false, false]}}]", children)
```

**Alternatives considered**:
- Using `.attr()` for complex values - More explicit but breaks selector pattern
- Pre-encoding JSON - Adds complexity without benefit

### 5. How to generate lists/sequences of elements (e.g., table rows)?

**Decision**: Use `fragment` function with sequence expressions

**Rationale**: Hox provides `fragment` to combine multiple nodes without a wrapper element. Combined with F# sequence expressions, this creates clean list generation.

**Example**:
```fsharp
let rows =
    items
    |> Seq.map (fun item ->
        h($"tr#item-{item.Id}", [
            h("td", [ Text item.Name ])
            h("td", [ h($"button [data-on:click=...]", [ Text "Delete" ]) ])
        ]))

h("tbody#items-list", fragment rows)
```

**Alternatives considered**:
- Nested h() calls - Works but less readable for lists
- List.map then fragment - Same result, slightly different syntax

## Key Patterns Summary

| Basic Sample Pattern | Hox Equivalent |
|---------------------|----------------|
| `$"""<div id="x">...</div>"""` | `h("div#x", [...])` |
| `$"<p>{value}</p>"` | `h("p", [ Text value ])` |
| `data-on:click="..."` | `[data-on:click=...]` in selector |
| `data-bind:field` | `[data-bind:field]` in selector |
| `class="error"` | `.error` in selector |
| List concatenation | `fragment (seq {...})` |

## Dependencies Verified

- **Hox 3.x**: Compatible with .NET 10.0
- **Hox.Rendering**: Provides `Render.asString` for async string rendering
- **Hox.Core**: Provides `Node`, `h()`, `fragment`, `Text`

## Integration Pattern

```fsharp
// 1. Import Hox
open Hox
open Hox.Core
open Hox.Rendering

// 2. Create Hox node
let node = h("div#contact-view", [
    h("p", [ h("strong", [ Text "Name:" ]); Text " John" ])
    h($"button [data-on:click=@get('/contacts/1/edit')]", [ Text "Edit" ])
])

// 3. Render to string
let! html = Render.asString node

// 4. Send via Datastar
do! Datastar.patchElements html ctx
```

## No Outstanding Questions

All technical questions have been resolved. The implementation can proceed.
