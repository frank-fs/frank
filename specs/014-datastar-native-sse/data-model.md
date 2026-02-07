# Data Model: Frank.Datastar Native SSE

## Enumerations

### ElementPatchMode

Discriminated union defining how elements are patched into the DOM.

| Variant | Morphed | Description |
|---------|---------|-------------|
| `Outer` | Yes | Morph entire element, preserving state (DEFAULT) |
| `Inner` | Yes | Morph inner HTML only, preserving state |
| `Remove` | No | Remove target element from DOM |
| `Replace` | No | Replace entire element, reset state |
| `Prepend` | No | Insert at beginning inside target |
| `Append` | No | Insert at end inside target |
| `Before` | No | Insert before target element |
| `After` | No | Insert after target element |

### PatchElementNamespace

Discriminated union for element creation namespace.

| Variant | Wire Value | Description |
|---------|------------|-------------|
| `Html` | `html` | HTML namespace (DEFAULT) |
| `Svg` | `svg` | SVG namespace |
| `MathMl` | `mathml` | MathML namespace |

## Option Types (all `[<Struct>]`)

### PatchElementsOptions

| Field | Type | Default | ADR Data Line |
|-------|------|---------|---------------|
| `Selector` | `string voption` | `ValueNone` | `data: selector <value>` |
| `PatchMode` | `ElementPatchMode` | `Outer` | `data: mode <value>` (omit if default) |
| `UseViewTransition` | `bool` | `false` | `data: useViewTransition true` (omit if false) |
| `Namespace` | `PatchElementNamespace` | `Html` | `data: namespace <value>` (omit if default) |
| `EventId` | `string voption` | `ValueNone` | `id: <value>` |
| `Retry` | `TimeSpan` | `1 second` | `retry: <milliseconds>` (omit if default) |

### PatchSignalsOptions

| Field | Type | Default | ADR Data Line |
|-------|------|---------|---------------|
| `OnlyIfMissing` | `bool` | `false` | `data: onlyIfMissing true` (omit if false) |
| `EventId` | `string voption` | `ValueNone` | `id: <value>` |
| `Retry` | `TimeSpan` | `1 second` | `retry: <milliseconds>` (omit if default) |

### RemoveElementOptions

| Field | Type | Default | ADR Data Line |
|-------|------|---------|---------------|
| `UseViewTransition` | `bool` | `false` | `data: useViewTransition true` (omit if false) |
| `EventId` | `string voption` | `ValueNone` | `id: <value>` |
| `Retry` | `TimeSpan` | `1 second` | `retry: <milliseconds>` (omit if default) |

### ExecuteScriptOptions

| Field | Type | Default | ADR Data Line |
|-------|------|---------|---------------|
| `AutoRemove` | `bool` | `true` | `data-effect="el.remove()"` attribute on `<script>` tag |
| `Attributes` | `string[]` | `[||]` | Additional attributes on `<script>` tag, written verbatim |
| `EventId` | `string voption` | `ValueNone` | `id: <value>` |
| `Retry` | `TimeSpan` | `1 second` | `retry: <milliseconds>` (omit if default) |

## Type Aliases

| Alias | Underlying Type | Purpose |
|-------|----------------|---------|
| `Signals` | `string` | JSON string wrapping signal data |
| `Selector` | `string` | CSS selector string |

## Constants

| Constant | Value | Purpose |
|----------|-------|---------|
| `DatastarKey` | `"datastar"` | Query parameter name for GET signal reading |
| `DefaultSseRetryDuration` | `TimeSpan.FromSeconds(1.0)` | SSE retry duration (1000ms per ADR) |

## Pre-allocated Byte Arrays (internal)

All SSE field prefixes and enum string representations are pre-allocated as `byte[]` literals using F#'s `"..."B` syntax to avoid runtime string-to-byte encoding.

| Category | Examples |
|----------|----------|
| Event types | `"datastar-patch-elements"B`, `"datastar-patch-signals"B` |
| Data line keys | `"selector"B`, `"mode"B`, `"elements"B`, `"signals"B`, etc. |
| Enum values | `"outer"B`, `"inner"B`, `"html"B`, `"svg"B`, etc. |
| Script tags | `"<script>"B`, `"</script>"B`, `"<script data-effect=\"el.remove()\">"B` |
| Primitives | `"true"B`, `"false"B`, space, newline |

## SSE Event Format (wire format per ADR)

### PatchElements

```
event: datastar-patch-elements\n
[id: <eventId>\n]
[retry: <milliseconds>\n]
[data: selector <selector>\n]
[data: mode <patchMode>\n]
[data: useViewTransition true\n]
[data: namespace <namespace>\n]
data: elements <html-line-1>\n
data: elements <html-line-2>\n
\n
```

### PatchSignals

```
event: datastar-patch-signals\n
[id: <eventId>\n]
[retry: <milliseconds>\n]
[data: onlyIfMissing true\n]
data: signals <json-line-1>\n
data: signals <json-line-2>\n
\n
```

### ExecuteScript (uses PatchElements event type)

```
event: datastar-patch-elements\n
[id: <eventId>\n]
[retry: <milliseconds>\n]
data: selector body\n
data: mode append\n
data: elements <script [attributes] [data-effect="el.remove()"]>\n
data: elements <script-line-1>\n
data: elements <script-line-2>\n
data: elements </script>\n
\n
```

### RemoveElement (uses PatchElements event type)

```
event: datastar-patch-elements\n
[id: <eventId>\n]
[retry: <milliseconds>\n]
data: mode remove\n
data: selector <selector>\n
[data: useViewTransition true\n]
\n
```
