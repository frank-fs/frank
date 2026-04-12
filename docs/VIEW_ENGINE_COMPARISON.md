# View Engine Comparison for Frank.Datastar

A comprehensive comparison of HTML rendering approaches for use with Frank and Frank.Datastar SSE responses.

## Approaches Compared

| Approach | Library | Style |
|----------|---------|-------|
| F# String Templates | None (built-in) | `$"""<div>...</div>"""` |
| Hox | Hox 3.x | `h("div#id.class", [...])` |
| Oxpecker.ViewEngine | Oxpecker.ViewEngine 2.x | `div(id="x") { ... }` |

## Syntax Comparison

### F# String Templates (Basic)

```fsharp
let renderContactView (contact: Contact) : string =
    $"""<div id="contact-view">
        <p><strong>First Name:</strong> {contact.FirstName}</p>
        <p><strong>Last Name:</strong> {contact.LastName}</p>
        <button data-on:click="@get('/contacts/{contact.Id}/edit')"
                data-indicator:_fetching
                data-attr:disabled="$_fetching">Edit</button>
    </div>"""
```

### Oxpecker.ViewEngine (Computation Expressions)

```fsharp
let renderContactView (contact: Contact) : string =
    div(id = "contact-view") {
        p() {
            strong() { "First Name:" }
            raw $" {contact.FirstName}"
        }
        p() {
            strong() { "Last Name:" }
            raw $" {contact.LastName}"
        }
        button()
            .attr("data-on:click", $"@get('/contacts/{contact.Id}/edit')")
            .attr("data-indicator:_fetching", "")
            .attr("data-attr:disabled", "$_fetching") { "Edit" }
    }
    |> Render.toString
```

### Hox (CSS Selector DSL)

```fsharp
let renderContactView (contact: Contact) : ValueTask<string> =
    let node =
        h("div#contact-view", [
            h("p", [ h("strong", [ Text "First Name:" ]); Text $" {contact.FirstName}" ])
            h("p", [ h("strong", [ Text "Last Name:" ]); Text $" {contact.LastName}" ])
            h("button", [ Text "Edit" ])
                .attr("data-on:click", $"@get('/contacts/{contact.Id}/edit')")
                .attr("data-indicator:_fetching", "")
                .attr("data-attr:disabled", "$_fetching")
        ])
    Render.asString node
```

## Detailed Comparison

### 1. F# Idiomaticity

| Library | Approach | Rating |
|---------|----------|--------|
| **F# String Templates** | Native F# interpolated strings | ★★★★★ |
| **Oxpecker.ViewEngine** | Native F# computation expressions | ★★★★★ |
| **Hox** | String-based CSS selector DSL | ★★★☆☆ |

- **F# String Templates**: Most direct - zero learning curve, uses standard F# features
- **Oxpecker.ViewEngine**: Uses computation expressions like Frank's `resource` and `webHost` builders
- **Hox**: CSS selector strings are clever but less discoverable and lack IDE completion

### 2. IDE Support & Type Safety

| Library | Autocomplete | Error Detection | Type Safety |
|---------|--------------|-----------------|-------------|
| **F# String Templates** | None (strings) | Runtime only | None |
| **Oxpecker.ViewEngine** | Full support for attributes | Compile-time | Full |
| **Hox** | None (strings) | Runtime only | Limited |

**Note on Reserved Words** (Oxpecker.ViewEngine):
- `class` is F# keyword, use `class'`
- `type` is F# keyword, use `type'`

### 3. Code Verbosity

| Pattern | String Templates | Hox | Oxpecker.ViewEngine |
|---------|------------------|-----|---------------------|
| Simple element | `$"<div class=\"card\">...</div>"` | `h("div.card", [...])` | `div(class'="card") { ... }` |
| With ID+class | `$"<div id=\"x\" class=\"y\">...</div>"` | `h("div#x.y", [...])` | `div(id="x", class'="y") { ... }` |
| With attributes | `$"<input type=\"text\" value=\"{v}\" />"` | `h("input[type=text]", []).attr("value", v)` | `input(type'="text", value=v)` |
| Text content | `$"<p>hello</p>"` | `h("p", [ Text "hello" ])` | `p() { "hello" }` |
| Loop | `items \|> List.map ... \|> String.concat ""` | `fragment [ for item in items do ... ]` | `for item in items do li() { ... }` |

**Verdict**:
- String templates are most compact for simple cases but verbose for complex nesting
- Hox is compact for elements with multiple classes (`div.a.b.c`)
- Oxpecker is cleanest for attributes and text content with native F# control flow

### 4. Rendering Model

| Library | Rendering Model | Return Type | Characteristics |
|---------|-----------------|-------------|-----------------|
| **F# String Templates** | Synchronous | `string` | Direct string interpolation, zero overhead |
| **Oxpecker.ViewEngine** | Synchronous | `string` | Direct `StringBuilder` manipulation, no async overhead |
| **Hox** | Async-first | `ValueTask<string>` | Supports streaming via `IAsyncEnumerable<string>` |

**Verdict**:
- String templates and Oxpecker are simpler for Datastar SSE (small HTML fragments)
- Hox's async model adds overhead for simple renders but excels at streaming large documents

### 5. Datastar Attribute Handling

Datastar uses attributes with colons and special characters (e.g., `data-on:click`, `data-bind:firstName`, `data-signals__ifmissing`).

| Library | Method | Example |
|---------|--------|---------|
| **String Templates** | Direct in string | `data-on:click="@get('/path')"` |
| **Oxpecker.ViewEngine** | `.attr()` extension | `.attr("data-on:click", "@get('/path')")` |
| **Hox** | `.attr()` extension | `.attr("data-on:click", "@get('/path')")` |

**Note**: Hox's bracket notation (`[data-on:click=...]`) works but `.attr()` is more explicit for complex values.

## Integration with Frank.Datastar

### F# String Templates

```fsharp
open Frank.Datastar

let myHandler ctx =
    task {
        let html = $"""<div id="content"><p>Hello, World!</p></div>"""
        do! Datastar.patchElements html ctx
    }
```

### Oxpecker.ViewEngine

```fsharp
open Oxpecker.ViewEngine
open Frank.Datastar

let myHandler ctx =
    task {
        let html =
            div(id="content") { p() { "Hello, World!" } }
            |> Render.toString
        do! Datastar.patchElements html ctx
    }
```

### Hox

```fsharp
open Hox
open Hox.Core
open Hox.Rendering
open Frank.Datastar

let myHandler ctx =
    task {
        let node = h("div#content", [ h("p", [ Text "Hello, World!" ]) ])
        let! html = Render.asString node
        do! Datastar.patchElements html ctx
    }
```

## Real-World Pattern Comparison

### Click-to-Edit with Datastar Signals

**F# String Templates**:
```fsharp
let renderContactEdit (contact: Contact) : string =
    $"""<div id="contact-view" data-signals="{{'firstName': '{contact.FirstName}', 'lastName': '{contact.LastName}'}}">
        <label>First Name <input type="text" data-bind:first-name data-attr:disabled="$_fetching" /></label>
        <button data-on:click="@put('/contacts/{contact.Id}')" data-indicator:_fetching>Save</button>
    </div>"""
```

**Oxpecker.ViewEngine**:
```fsharp
let renderContactEdit (contact: Contact) : string =
    div(id = "contact-view")
        .attr("data-signals", $"{{'firstName': '{contact.FirstName}', 'lastName': '{contact.LastName}'}}") {
        label() {
            raw "First Name "
            input(type' = "text")
                .attr("data-bind:first-name", "")
                .attr("data-attr:disabled", "$_fetching")
        }
        button()
            .attr("data-on:click", $"@put('/contacts/{contact.Id}')")
            .attr("data-indicator:_fetching", "") { "Save" }
    }
    |> Render.toString
```

**Hox**:
```fsharp
let renderContactEdit (contact: Contact) : ValueTask<string> =
    let node =
        h("div#contact-view", [
            h("label", [
                Text "First Name "
                h("input[type=text]", [])
                    .attr("data-bind:first-name", "")
                    .attr("data-attr:disabled", "$_fetching")
            ])
            h("button", [ Text "Save" ])
                .attr("data-on:click", $"@put('/contacts/{contact.Id}')")
                .attr("data-indicator:_fetching", "")
        ]).attr("data-signals", $"{{'firstName': '{contact.FirstName}', 'lastName': '{contact.LastName}'}}")
    Render.asString node
```

### Table with Dynamic Rows

**F# String Templates**:
```fsharp
let renderItemsTable (items: ResizeArray<Item>) : string =
    let rows =
        items
        |> Seq.map (fun item ->
            $"""<tr id="item-{item.Id}">
                <td>{item.Name}</td>
                <td><button data-on:click="@delete('/items/{item.Id}')">Delete</button></td>
            </tr>""")
        |> String.concat ""
    $"""<table id="items-table"><tbody id="items-list">{rows}</tbody></table>"""
```

**Oxpecker.ViewEngine**:
```fsharp
let renderItemsTable (items: ResizeArray<Item>) : string =
    table(id = "items-table") {
        tbody(id = "items-list") {
            for item in items do
                tr(id = $"item-{item.Id}") {
                    td() { item.Name }
                    td() {
                        button()
                            .attr("data-on:click", $"@delete('/items/{item.Id}')") { "Delete" }
                    }
                }
        }
    }
    |> Render.toString
```

**Hox**:
```fsharp
let renderItemsTable (items: ResizeArray<Item>) : ValueTask<string> =
    let node =
        h("table#items-table", [
            h("tbody#items-list",
                fragment [
                    for item in items do
                        h($"tr#item-{item.Id}", [
                            h("td", [ Text item.Name ])
                            h("td", [
                                h("button", [ Text "Delete" ])
                                    .attr("data-on:click", $"@delete('/items/{item.Id}')")
                            ])
                        ])
                ])
        ])
    Render.asString node
```

## Summary

| Aspect | F# String Templates | Oxpecker.ViewEngine | Hox |
|--------|---------------------|---------------------|-----|
| **F# idiomaticity** | ★★★★★ | ★★★★★ | ★★★☆☆ |
| **IDE support** | ★★☆☆☆ | ★★★★★ | ★★☆☆☆ |
| **Type safety** | ★★☆☆☆ | ★★★★★ | ★★★☆☆ |
| **Verbosity (simple)** | ★★★★★ | ★★★★☆ | ★★★★☆ |
| **Verbosity (complex)** | ★★★☆☆ | ★★★★★ | ★★★★☆ |
| **Sync performance** | ★★★★★ | ★★★★★ | ★★★★☆ |
| **Async support** | ★★★☆☆ | ★★★☆☆ | ★★★★★ |
| **Learning curve** | ★★★★★ | ★★★★☆ | ★★★☆☆ |
| **Datastar attributes** | ★★★★★ | ★★★★☆ | ★★★★☆ |

## Recommendations for Frank.Datastar

### Best Overall: Oxpecker.ViewEngine

For most Frank.Datastar applications, **Oxpecker.ViewEngine** is the recommended choice because:

1. **Consistency with Frank** - Uses computation expressions like Frank's `resource` and `webHost` builders
2. **Type safety** - Strong typing catches errors at compile time
3. **IDE support** - Full autocomplete for elements and attributes
4. **Clean syntax** - Native F# control flow (`for`, `if`) works naturally inside views
5. **Synchronous rendering** - Matches Datastar SSE fragment pattern (small HTML chunks)

### Good Alternative: F# String Templates

Choose **F# String Templates** when:

1. **Simplicity is paramount** - Zero learning curve, no dependencies
2. **Simple HTML fragments** - Datastar typically sends small fragments
3. **Team familiarity** - Everyone knows interpolated strings
4. **Quick prototyping** - Fastest path to working code

### Specialized Use: Hox

Choose **Hox** when:

1. **Streaming large documents** - Hox's async-native design excels here
2. **Progressive rendering** - Mix async data fetching with rendering
3. **CSS selector familiarity** - Team prefers CSS-style element creation

## Project Configuration

### Oxpecker.ViewEngine

```xml
<PackageReference Include="Oxpecker.ViewEngine" Version="2.*" />
```

### Hox

```xml
<PackageReference Include="Hox" Version="3.*" />
```

### F# String Templates

No additional packages required.

## References

- [Oxpecker GitHub](https://github.com/Lanayx/Oxpecker)
- [Hox GitHub](https://github.com/AngelMunoz/Hox)
- [Hox Documentation](https://hox.tunaxor.me/)
- [Frank GitHub](https://github.com/frank-fs/frank)
- [Datastar Documentation](https://data-star.dev/)
