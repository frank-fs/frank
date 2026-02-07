# API Surface Contract: Frank.Datastar Native SSE

## Namespace: Frank.Datastar

### Module: DatastarExtensions (AutoOpen)

Extends `ResourceBuilder` with the `datastar` custom operation.

```fsharp
type ResourceBuilder with
    /// SSE stream with GET method (default)
    [<CustomOperation("datastar")>]
    member Datastar: spec:ResourceSpec * operation:(HttpContext -> Task<unit>) -> ResourceSpec

    /// SSE stream with specified HTTP method
    [<CustomOperation("datastar")>]
    member Datastar: spec:ResourceSpec * method:string * operation:(HttpContext -> Task<unit>) -> ResourceSpec
```

**Contract**: Unchanged from current API. Both overloads automatically start the SSE stream before executing the user's operation handler.

---

### Module: Datastar

High-level helper functions for use inside `datastar` handlers. All functions are `inline`.

```fsharp
/// Patch HTML elements into the DOM (hypermedia-first primary pattern)
val inline patchElements: html:string -> ctx:HttpContext -> Task

/// Patch HTML elements with custom options
val inline patchElementsWithOptions: options:PatchElementsOptions -> html:string -> ctx:HttpContext -> Task

/// Patch client-side signals (JSON merge patch semantics)
val inline patchSignals: signals:string -> ctx:HttpContext -> Task

/// Patch signals with custom options
val inline patchSignalsWithOptions: options:PatchSignalsOptions -> signals:string -> ctx:HttpContext -> Task

/// Remove an element by CSS selector
val inline removeElement: selector:string -> ctx:HttpContext -> Task

/// Remove an element with custom options
val inline removeElementWithOptions: options:RemoveElementOptions -> selector:string -> ctx:HttpContext -> Task

/// Execute JavaScript on the client
val inline executeScript: script:string -> ctx:HttpContext -> Task

/// Execute JavaScript with custom options
val inline executeScriptWithOptions: options:ExecuteScriptOptions -> script:string -> ctx:HttpContext -> Task

/// Read and deserialize signals from the request
val inline tryReadSignals<'T> : ctx:HttpContext -> Task<'T voption>

/// Read and deserialize signals with custom JSON options
val inline tryReadSignalsWithOptions<'T> : jsonOptions:JsonSerializerOptions -> ctx:HttpContext -> Task<'T voption>
```

**Contract**: Unchanged from current API. All functions delegate to `ServerSentEventGenerator` static methods.

---

### Type: ServerSentEventGenerator (NEW - public per FR-014)

ADR-compliant SSE generator for advanced users.

```fsharp
/// Static methods for direct SSE event writing (no instance state needed)
type ServerSentEventGenerator =

    /// Initialize SSE stream: set headers, flush response
    static member StartServerEventStreamAsync:
        httpResponse:HttpResponse
        * ?cancellationToken:CancellationToken
        -> Task

    /// Send patch-elements event
    static member PatchElementsAsync:
        httpResponse:HttpResponse
        * elements:string
        * ?options:PatchElementsOptions
        * ?cancellationToken:CancellationToken
        -> Task

    /// Send patch-elements event with remove mode
    static member RemoveElementAsync:
        httpResponse:HttpResponse
        * selector:string
        * ?options:RemoveElementOptions
        * ?cancellationToken:CancellationToken
        -> Task

    /// Send patch-signals event
    static member PatchSignalsAsync:
        httpResponse:HttpResponse
        * signals:string
        * ?options:PatchSignalsOptions
        * ?cancellationToken:CancellationToken
        -> Task

    /// Send execute-script event (via patch-elements with script tag)
    static member ExecuteScriptAsync:
        httpResponse:HttpResponse
        * script:string
        * ?options:ExecuteScriptOptions
        * ?cancellationToken:CancellationToken
        -> Task

    /// Read signals from request (raw JSON string)
    static member ReadSignalsAsync:
        httpRequest:HttpRequest
        * ?cancellationToken:CancellationToken
        -> Task<string>

    /// Read and deserialize signals into typed object
    static member ReadSignalsAsync<'T> :
        httpRequest:HttpRequest
        * ?jsonSerializerOptions:JsonSerializerOptions
        * ?cancellationToken:CancellationToken
        -> Task<'T voption>
```

**Contract**: New public API per ADR compliance (FR-014). Static methods match the upstream StarFederation pattern used by the `Datastar` module functions.

---

### Enumerations

```fsharp
/// How elements are patched into the DOM
type ElementPatchMode =
    | Outer    // Default — morph entire element
    | Inner    // Morph inner HTML only
    | Remove   // Remove from DOM
    | Replace  // Replace entirely
    | Prepend  // Insert at beginning inside target
    | Append   // Insert at end inside target
    | Before   // Insert before target
    | After    // Insert after target

/// Namespace for element creation
type PatchElementNamespace =
    | Html     // Default
    | Svg
    | MathMl
```

---

### Option Types (all `[<Struct>]`)

```fsharp
[<Struct>]
type PatchElementsOptions = {
    Selector: string voption
    PatchMode: ElementPatchMode
    UseViewTransition: bool
    Namespace: PatchElementNamespace
    EventId: string voption
    Retry: TimeSpan
} with static member Defaults: PatchElementsOptions

[<Struct>]
type PatchSignalsOptions = {
    OnlyIfMissing: bool
    EventId: string voption
    Retry: TimeSpan
} with static member Defaults: PatchSignalsOptions

[<Struct>]
type RemoveElementOptions = {
    UseViewTransition: bool
    EventId: string voption
    Retry: TimeSpan
} with static member Defaults: RemoveElementOptions

[<Struct>]
type ExecuteScriptOptions = {
    AutoRemove: bool
    Attributes: string[]       // NEW: pre-formed attribute strings, written verbatim
    EventId: string voption
    Retry: TimeSpan
} with static member Defaults: ExecuteScriptOptions
```

---

## Breaking Changes from 7.x

| Change | Impact | Migration |
|--------|--------|-----------|
| Target framework: `net8.0;net9.0;net10.0` → `net10.0` | Consumers on .NET 8/9 cannot use this version | Use Frank core + StarFederation.Datastar.FSharp directly on older targets |
| `ExecuteScriptOptions.Attributes`: `KeyValuePair<string,string> list` → `string[]` | Binary-breaking for anyone using Attributes directly | Change `[KeyValuePair("key","val")]` to `[|"key=\"val\""|]` |
| Removed transitive dependency on StarFederation.Datastar.FSharp | Tests importing `StarFederation.Datastar.FSharp` must change imports | Remove `open StarFederation.Datastar.FSharp`; types now in `Frank.Datastar` |
| `ServerSentEventGenerator` now public | Additive change, not breaking | No migration needed |
