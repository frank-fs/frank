# Quickstart: Frank.Datastar

## Installation

```bash
dotnet add package Frank.Datastar
```

## Basic Usage

### Stream Multiple Updates

The core use case: send multiple HTML updates over a single SSE connection using the `datastar` custom operation with `Datastar.*` helper functions.

```fsharp
open Frank
open Frank.Builder
open Frank.Datastar
open System.Threading.Tasks

let progressUpdates =
    resource "/progress" {
        name "ProgressUpdates"
        datastar (fun ctx -> task {
            for i in 1..10 do
                let html = $"""<div id="progress">Loading... {i * 10}%%</div>"""
                do! Datastar.patchElements html ctx
                do! Task.Delay(500)
        })
    }
```

### Read Client Signals

Read JSON signals sent from the Datastar client within your streaming handler:

```fsharp
[<CLIMutable>]
type SearchSignals = { query: string }

let search =
    resource "/search" {
        name "Search"
        datastar (fun ctx -> task {
            let! signals = Datastar.tryReadSignals<SearchSignals> ctx

            match signals with
            | ValueSome s ->
                let html = $"""<div id="results">Results for: {s.query}</div>"""
                do! Datastar.patchElements html ctx
            | ValueNone ->
                do! Datastar.patchElements """<div id="results">No query provided</div>""" ctx
        })
    }
```

### Multiple Operations in One Handler

Combine different Datastar operations within a single streaming handler:

```fsharp
let dashboard =
    resource "/dashboard" {
        name "Dashboard"
        datastar (fun ctx -> task {
            // Update header
            do! Datastar.patchElements """<div id="header">Dashboard Loading...</div>""" ctx
            do! Task.Delay(100)

            // Update main content
            do! Datastar.patchElements """<div id="content">Data loaded!</div>""" ctx

            // Update a signal (use sparingly - prefer HTML)
            do! Datastar.patchSignals """{"loaded": true}""" ctx

            // Remove loading indicator
            do! Datastar.removeElement "#loading-spinner" ctx
        })
    }
```

## Client Setup (HTML)

```html
<!DOCTYPE html>
<html>
<head>
    <script type="module" src="https://cdn.jsdelivr.net/npm/@starfederation/datastar"></script>
</head>
<body>
    <!-- Trigger streaming update on click -->
    <button data-on-click="@get('/progress')">Start</button>

    <!-- Target for updates -->
    <div id="progress">Click to start</div>
</body>
</html>
```

## Key Patterns

### Hypermedia First

Send HTML from the server (primary pattern):

```fsharp
do! Datastar.patchElements "<div id='user-list'>...</div>" ctx
```

### Minimal Signals

Use signals only for ephemeral UI state:

```fsharp
do! Datastar.patchSignals """{"formDirty": true}""" ctx
```

### Server as Source of Truth

Server decides what HTML to display - client signals are inputs, not outputs.

## Full Application Example

```fsharp
module App

open System
open System.Threading.Tasks
open Frank
open Frank.Builder
open Frank.Datastar

let home =
    resource "/" {
        name "Home"
        get (fun ctx -> ctx.Response.WriteAsync("Welcome!"))
    }

let updates =
    resource "/updates" {
        name "Updates"
        datastar (fun ctx -> task {
            for i in 1..5 do
                let html = $"""<div id="status">Update {i}</div>"""
                do! Datastar.patchElements html ctx
                do! Task.Delay(1000)
        })
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        resource home
        resource updates
    }
    0
```

## API Summary

**ResourceBuilder custom operation:**
- `datastar` - Start SSE stream and execute your handler

**Datastar module helpers (use inside `datastar` handler):**
- `Datastar.patchElements` - Send HTML fragment
- `Datastar.patchSignals` - Update client signals (use sparingly)
- `Datastar.removeElement` - Remove DOM element
- `Datastar.executeScript` - Run JavaScript (use sparingly)
- `Datastar.tryReadSignals<'T>` - Read signals from request

## Important Notes

1. **One custom operation**: Only `datastar` exists on ResourceBuilder. This is by design (FR-005).
2. **Hypermedia-first**: Prefer `patchElements` over `patchSignals`.
3. **webHost builder**: Use Frank's `webHost` computation expression for hosting.
4. **Cancellation**: Access `ctx.RequestAborted` to detect client disconnection.
