# Frank.Datastar API Contract

**Version**: 1.0.0
**Namespace**: `Frank.Datastar`

## Module: DatastarExtensions

Auto-opened module providing a single extension to `Frank.Builder.ResourceBuilder`.

### Custom Operations

#### datastar (GET default)

```fsharp
[<CustomOperation("datastar")>]
member Datastar: spec:ResourceSpec * operation:(HttpContext -> Task<unit>) -> ResourceSpec
```

Execute Datastar operations with automatic SSE stream management using GET method (default). The stream is started once, then the user's operation executes. Use `Datastar.*` helper functions within the operation to send events.

#### datastar (with HTTP method)

```fsharp
[<CustomOperation("datastar")>]
member Datastar: spec:ResourceSpec * method:string * operation:(HttpContext -> Task<unit>) -> ResourceSpec
```

Execute Datastar operations with automatic SSE stream management using the specified HTTP method. Useful for POST-based streaming (e.g., reading signals from request body).

**Example:**
```fsharp
resource "/updates" {
    name "Updates"
    datastar (fun ctx -> task {
        do! Datastar.patchElements "<div id='status'>Loading...</div>" ctx
        do! Task.Delay(1000)
        do! Datastar.patchElements "<div id='status'>Complete!</div>" ctx
    })
}
```

**Note:** This is the ONLY custom operation on ResourceBuilder. Per FR-005, one-off convenience operations are explicitly forbidden.

---

## Module: Datastar

Helper functions for use inside the `datastar` handler or for standalone SSE streaming.

### Functions

#### patchElements

```fsharp
val patchElements: html:string -> ctx:HttpContext -> Task
```

Send HTML fragment to patch DOM elements. Assumes stream already started (use inside `datastar` handler or after `stream`).

#### patchSignals

```fsharp
val patchSignals: signals:string -> ctx:HttpContext -> Task
```

Send signal updates to client. Use sparingly - prefer `patchElements` (hypermedia-first).

#### removeElement

```fsharp
val removeElement: selector:string -> ctx:HttpContext -> Task
```

Remove DOM element by CSS selector.

#### executeScript

```fsharp
val executeScript: script:string -> ctx:HttpContext -> Task
```

Execute JavaScript on the client. Use very sparingly.

#### tryReadSignals

```fsharp
val tryReadSignals<'T>: ctx:HttpContext -> Task<voption<'T>>
```

Read and deserialize signals from request body. Returns `ValueNone` for invalid/missing JSON.

---

## Dependencies

- **Frank** >= 6.0.0
- **StarFederation.Datastar.FSharp** (latest stable)

## Target Frameworks

- net8.0
- net9.0
- net10.0

## Usage Pattern

```fsharp
open Frank
open Frank.Builder
open Frank.Datastar

let myResource =
    resource "/my-endpoint" {
        name "MyEndpoint"
        datastar (fun ctx -> task {
            // Read signals if needed
            let! signals = Datastar.tryReadSignals<MySignals> ctx

            // Send multiple updates
            do! Datastar.patchElements "<div id='step1'>Step 1</div>" ctx
            do! Task.Delay(100)
            do! Datastar.patchElements "<div id='step2'>Step 2</div>" ctx

            // Optionally update signals (use sparingly)
            do! Datastar.patchSignals """{"complete": true}""" ctx
        })
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        resource myResource
    }
    0
```

## Error Handling

| Scenario | Behavior |
|----------|----------|
| Invalid JSON in signals | `tryReadSignals` returns `ValueNone` (no exception) |
| Client disconnects | `HttpContext.RequestAborted` is cancelled |
| Large HTML fragment | Sent as-is (SSE handles chunking) |
| Exception in handler | Stream terminates; error handling is user's responsibility |

## Design Rationale (FR-005)

Per the feature specification:

> "The library MUST NOT provide one-off convenience operations that send a single response."

This means:
- **NO** `patchElements`, `removeElement`, `patchSignals`, `executeScript` as ResourceBuilder custom operations
- **YES** `datastar` as the single streaming handler custom operation
- **YES** `Datastar.*` module functions for use inside the handler

The rationale: Recent Datastar versions support regular HTTP responses for single-update interactions. Use standard Frank `resource` handlers for one-off interactions. This library focuses exclusively on SSE streaming (0-n responses).
