# View Engine Comparison: Oxpecker.ViewEngine vs Hox

A comparison of F# HTML rendering libraries for use with Frank.Datastar SSE responses.

## Syntax Comparison

### Oxpecker.ViewEngine (Computation Expressions)

```fsharp
div(class'="card") {
    h1(id="title") { "Hello" }
    p() { $"Welcome, {name}!" }
    ul() {
        for item in items do
            li() { item.Name }
    }
}
```

### Hox (CSS Selector DSL)

```fsharp
h("div.card",
    [ h("h1#title", [ Text "Hello" ])
      h("p", [ Text $"Welcome, {name}!" ])
      h("ul", [ for item in items do h("li", [ Text item.Name ]) ]) ])
```

## Detailed Comparison

### 1. F# Idiomaticity

| Library | Approach | Rating |
|---------|----------|--------|
| **Oxpecker.ViewEngine** | Native F# computation expressions | ★★★★★ |
| **Hox** | String-based CSS selector DSL | ★★★☆☆ |

Oxpecker uses computation expressions, which are native F# syntax. Hox's CSS selector strings are clever but less discoverable and lack IDE completion for attributes.

### 2. Code Verbosity

| Pattern | Hox | Oxpecker |
|---------|-----|----------|
| Simple element | `h("div.card", [...])` | `div(class'="card") { ... }` |
| With ID+class | `h("div#id.class", [...])` | `div(id="id", class'="class") { ... }` |
| With attributes | `h("input [type=text] [value=foo]", [])` | `input(type'="text", value="foo")` |
| Text content | `Text "hello"` | `"hello"` (implicit) |

**Verdict:** Mixed. Hox is more compact for elements with multiple classes (`div.a.b.c`), but Oxpecker is cleaner for attributes and text content.

### 3. Performance

| Library | Rendering Model | Characteristics |
|---------|-----------------|-----------------|
| **Oxpecker.ViewEngine** | Synchronous | Direct `StringBuilder` manipulation, no async overhead |
| **Hox** | Async-first | Uses `ValueTask`, supports streaming via `IAsyncEnumerable<string>` |

**Verdict:** Oxpecker likely faster for simple renders (no async machinery). Hox better for streaming large documents or when async data fetching is interleaved with rendering.

### 4. Async Rendering

**Hox** - Built-in async support:
```fsharp
let! html = Render.asString node  // Task<string>
```
Can mix async nodes within sync parents - designed for progressive rendering.

**Oxpecker.ViewEngine** - Synchronous by design:
```fsharp
let sb = StringBuilder()
element.Render(sb)
let html = sb.ToString()
```
For async, fetch data first, then render synchronously.

**Verdict:** Hox wins for async-heavy scenarios. For Datastar SSE (where you're already in an async context), Hox's model fits naturally. Oxpecker requires explicit async/sync boundary management.

### 5. Type Safety

| Library | Attribute Handling | Compile-time Safety |
|---------|-------------------|---------------------|
| **Oxpecker.ViewEngine** | Strongly typed function parameters | Full - IDE autocomplete, compile errors for typos |
| **Hox** | String-based selectors parsed at runtime | Limited - typos in `h("div.calss", ...)` not caught |

**Verdict:** Oxpecker wins for type safety.

### 6. IDE Support

| Library | Autocomplete | Error Detection |
|---------|--------------|-----------------|
| **Oxpecker.ViewEngine** | Full support for attributes (`id=`, `class'=`, `style=`) | Compile-time |
| **Hox** | None (strings) | Runtime only |

## Summary

| Aspect | Oxpecker.ViewEngine | Hox |
|--------|---------------------|-----|
| **F# idiomaticity** | ★★★★★ | ★★★☆☆ |
| **IDE support** | ★★★★★ | ★★☆☆☆ |
| **Verbosity** | ★★★★☆ | ★★★★☆ |
| **Sync performance** | ★★★★★ | ★★★★☆ |
| **Async support** | ★★★☆☆ | ★★★★★ |
| **Type safety** | ★★★★★ | ★★★☆☆ |
| **Streaming** | ★★★★☆ | ★★★★★ |

## Recommendation for Frank.Datastar

For **Frank's style**, Oxpecker.ViewEngine is the better match because:

1. **Consistency** - Frank uses computation expressions (`resource`, `webHost`)
2. **Type safety** - Strong typing aligns with Frank's approach
3. **Simplicity** - Sync rendering is sufficient for SSE fragments (small HTML chunks)

However, if you're doing **progressive streaming of large documents**, Hox's async-native design has advantages.

## Usage with Frank.Datastar

### Oxpecker.ViewEngine

```fsharp
open System.Text
open Oxpecker.ViewEngine
open Frank.Datastar

let renderToString (element: HtmlElement) =
    let sb = StringBuilder()
    element.Render(sb)
    sb.ToString()

let myHandler ctx =
    task {
        let view = div(id="content") { p() { "Hello, World!" } }
        let html = renderToString view
        do! Datastar.patchElements html ctx
    }
```

### Hox

```fsharp
open Hox
open Hox.Rendering
open Frank.Datastar

let myHandler ctx =
    task {
        let view = h("div#content", [ h("p", [ Text "Hello, World!" ]) ])
        let! html = Render.asString view
        do! Datastar.patchElements html ctx
    }
```

## References

- [Oxpecker GitHub](https://github.com/Lanayx/Oxpecker)
- [Hox GitHub](https://github.com/AngelMunoz/Hox)
- [Hox Documentation](https://hox.tunaxor.me/)
