# Frank.Datastar

An F# library that integrates [Datastar](https://github.com/starfederation/datastar)'s Server-Sent Events (SSE) capabilities with the [Frank](https://github.com/frank-fs/frank) web framework through idiomatic computation expression builders.

## Features

- **Single Stream Handler**: Provides one `datastar` custom operation for SSE streaming
- **Helper Functions**: Clean `Datastar.*` helper functions for common operations
- **Stream-Based Rendering**: Zero-allocation HTML streaming via `TextWriter` for high-throughput scenarios
- **Type-Safe**: Leverages F# type system for safe signal handling
- **Hypermedia-First**: Designed around sending HTML (not managing client state)
- **HTTP Method Flexibility**: Supports GET, POST, and other HTTP methods

## Installation

```bash
dotnet add package Frank.Datastar
```

Or add to your `.fsproj`:

```xml
<PackageReference Include="Frank.Datastar" Version="1.0.0" />
```

## Quick Start

### Basic Streaming Example

```fsharp
open Frank
open Frank.Builder
open Frank.Datastar

let displayTime =
    resource "/time" {
        name "DisplayTime"
        datastar (fun ctx -> task {
            let time = System.DateTime.Now.ToString("HH:mm:ss")
            do! Datastar.patchElements $"""<div id="time">{time}</div>""" ctx
        })
    }
```

### Multiple Progressive Updates

The primary use case for Datastar is streaming multiple HTML updates:

```fsharp
let loadDashboard =
    resource "/dashboard" {
        name "LoadDashboard"
        datastar (fun ctx -> task {
            // Send header first
            do! Datastar.patchElements """<div id="header">Loading...</div>""" ctx

            // Fetch and send stats
            let! stats = fetchStatsAsync()
            do! Datastar.patchElements $"""<div id="stats">{renderStats stats}</div>""" ctx

            // Fetch and send activity
            let! activity = fetchActivityAsync()
            do! Datastar.patchElements $"""<div id="activity">{renderActivity activity}</div>""" ctx
        })
    }
```

### POST with Signal Reading

```fsharp
[<CLIMutable>]
type FormSignals = { query: string }

let submitSearch =
    resource "/search" {
        name "SubmitSearch"
        datastar HttpMethods.Post (fun ctx -> task {
            let! signals = Datastar.tryReadSignals<FormSignals> ctx

            match signals with
            | ValueSome form ->
                let! results = searchAsync form.query
                do! Datastar.patchElements (renderResults results) ctx
            | ValueNone ->
                do! Datastar.patchElements """<div id="error">Invalid form</div>""" ctx
        })
    }
```

### Stream-Based Rendering (High Performance)

For high-throughput scenarios or large HTML payloads, use stream-based overloads to eliminate string allocations:

```fsharp
// With manual TextWriter usage
let loadDashboardStreaming =
    resource "/dashboard-stream" {
        name "LoadDashboardStreaming"
        datastar (fun ctx -> task {
            do! Datastar.streamPatchElements (fun writer -> task {
                do! writer.WriteAsync("<div id='stats'>")
                do! writer.WriteAsync("Users: 1,234")
                do! writer.WriteAsync("</div>")
            }) ctx
        })
    }

// With view engine supporting TextWriter (e.g., Hox)
open Hox.Rendering

let loadUsersStreaming =
    resource "/users-stream" {
        name "LoadUsersStreaming"
        datastar (fun ctx -> task {
            let! users = fetchUsersAsync()
            let node = h("div#user-list", fragment [ for user in users do userCard user ])

            // Stream directly to response - no intermediate string allocation
            do! Datastar.streamPatchElements (fun writer ->
                Render.toTextWriter writer node
            ) ctx
        })
    }
```

## API Reference

### Datastar Philosophy

**Hypermedia First**: The primary pattern in Datastar is sending HTML from the server. The server is the source of truth.

**Minimal Signals**: Signals should be used sparingly, primarily for:
- Form input bindings (`<input data-bind:field>`)
- Ephemeral UI state (toggle switches, tabs)
- Passing small amounts of data to the server

### The `datastar` Custom Operation

The **only** custom operation on `ResourceBuilder`. Starts an SSE stream and executes your handler:

```fsharp
// Default: GET method
resource "/endpoint" {
    datastar (fun ctx -> task {
        do! Datastar.patchElements "<div>Content</div>" ctx
    })
}

// With specific HTTP method
resource "/endpoint" {
    datastar HttpMethods.Post (fun ctx -> task {
        let! signals = Datastar.tryReadSignals<MySignals> ctx
        // Process and respond...
    })
}
```

### Helper Functions

The `Datastar` module provides helper functions for use inside the `datastar` handler:

```fsharp
module Datastar =
    // PRIMARY: Send HTML to update the UI
    let patchElements (html: string) (ctx: HttpContext) : Task<unit>

    // STREAM-BASED: Zero-allocation HTML streaming
    let streamPatchElements (writer: TextWriter -> Task) (ctx: HttpContext) : Task<unit>

    // SECONDARY: Update ephemeral client state
    let patchSignals (signals: string) (ctx: HttpContext) : Task<unit>
    let streamPatchSignals (writer: TextWriter -> Task) (ctx: HttpContext) : Task<unit>

    // Remove an element by CSS selector
    let removeElement (selector: string) (ctx: HttpContext) : Task<unit>
    let streamRemoveElement (writer: TextWriter -> Task) (ctx: HttpContext) : Task<unit>

    // Execute JavaScript (use sparingly)
    let executeScript (script: string) (ctx: HttpContext) : Task<unit>
    let streamExecuteScript (writer: TextWriter -> Task) (ctx: HttpContext) : Task<unit>

    // Read and deserialize signals from request body
    let tryReadSignals<'T> (ctx: HttpContext) : Task<voption<'T>>
```

#### Stream-Based vs String-Based

**Use stream-based overloads when:**
- High throughput required (1000+ events/sec)
- Large HTML payloads (reducing allocations matters)
- View engine supports `TextWriter` output (e.g., Hox `Render.toTextWriter`)

**Use string-based API when:**
- Simple scenarios with small HTML strings
- View engine only produces strings
- Allocation profile is not a concern

Stream-based operations eliminate full HTML string materialization, providing 50%+ allocation reduction in high-throughput scenarios.

### Usage Priority

1. **Primary**: `Datastar.patchElements` - Send HTML to update the UI
2. **Supporting**: `Datastar.tryReadSignals` - Read form inputs to decide what HTML to send
3. **Rare**: `Datastar.patchSignals` - Update minimal client state (counters, flags)
4. **Special**: `Datastar.executeScript`, `Datastar.removeElement` - For specific use cases

## Complete Example

```fsharp
open System
open Frank
open Frank.Builder
open Frank.Datastar
open Microsoft.AspNetCore.Builder

[<CLIMutable>]
type SearchSignals = { query: string }

let displayDate =
    resource "/displayDate" {
        name "DisplayDate"
        datastar (fun ctx -> task {
            let today = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            do! Datastar.patchElements $"""<div id='target'><b>{today}</b></div>""" ctx
        })
    }

let searchItems =
    resource "/search" {
        name "SearchItems"
        datastar (fun ctx -> task {
            let query = ctx.Request.Query.["q"].ToString()

            let results =
                [ "Apple"; "Banana"; "Cherry"; "Date" ]
                |> List.filter (fun item -> item.Contains(query, StringComparison.OrdinalIgnoreCase))

            let html =
                if results.IsEmpty then
                    """<div id='results'>No results</div>"""
                else
                    let items = results |> List.map (fun r -> $"<li>{r}</li>") |> String.concat ""
                    $"""<ul id='results'>{items}</ul>"""

            do! Datastar.patchElements html ctx
        })
    }

let loadDashboard =
    resource "/dashboard" {
        name "LoadDashboard"
        datastar (fun ctx -> task {
            // Progressive loading: send updates as they become available
            do! Datastar.patchElements """<div id='header'>Dashboard</div>""" ctx
            do! Task.Delay(100)
            do! Datastar.patchElements """<div id='stats'>Users: 1,234</div>""" ctx
            do! Task.Delay(100)
            do! Datastar.patchElements """<div id='activity'>3 new items</div>""" ctx
        })
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        plug StaticFileExtensions.UseStaticFiles

        resource displayDate
        resource searchItems
        resource loadDashboard
    }
    0
```

## Architecture

Frank.Datastar is built on:

1. **Frank**: Provides the computation expression framework for defining HTTP resources
2. **StarFederation.Datastar.FSharp**: Implements the core Datastar SDK functionality
3. **ASP.NET Core**: The underlying web framework

The library extends Frank's `ResourceBuilder` with the `datastar` custom operation that:
- Starts the SSE stream automatically
- Executes your handler function
- Manages response headers and flushing

## Hox Integration

Frank.Datastar works with [Hox](https://github.com/AngelMunoz/Hox) for type-safe HTML rendering:

```fsharp
open Hox
open Hox.Core
open Hox.Rendering

let userCard (user: User) =
    h("div.user-card",
        [ h($"img [src={user.Avatar}] [alt={user.Name}]", [])
          h("div.user-info",
              [ h("h3", [ Text user.Name ])
                h("p", [ Text user.Email ]) ]) ])

// String-based rendering
let loadUsers =
    resource "/users" {
        name "LoadUsers"
        datastar (fun ctx -> task {
            let! users = fetchUsersAsync()
            let node = h("div#user-list", fragment [ for user in users do userCard user ])
            let! html = Render.asString node
            do! Datastar.patchElements html ctx
        })
    }

// Stream-based rendering (zero allocations)
let loadUsersStreaming =
    resource "/users-streaming" {
        name "LoadUsersStreaming"
        datastar (fun ctx -> task {
            let! users = fetchUsersAsync()
            let node = h("div#user-list", fragment [ for user in users do userCard user ])
            // Stream directly to response - no string allocation
            do! Datastar.streamPatchElements (fun writer ->
                Render.toTextWriter writer node
            ) ctx
        })
    }
```

Note: Hox uses CSS selector notation for attributes: `[attr=value]`

**Performance Tip:** Use `Render.toTextWriter` with `streamPatchElements` for zero-allocation HTML streaming in high-throughput scenarios.

## Related Projects

- [Frank](https://github.com/frank-fs/frank) - F# web framework
- [Datastar](https://github.com/starfederation/datastar) - Hypermedia framework
- [datastar-dotnet](https://github.com/starfederation/datastar-dotnet) - .NET SDK for Datastar
- [Hox](https://github.com/AngelMunoz/Hox) - Async HTML rendering library for F#

## License

MIT License - see LICENSE file for details
