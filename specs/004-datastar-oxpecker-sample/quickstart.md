# Quickstart: Frank.Datastar.Oxpecker Sample

**Date**: 2026-01-27
**Feature**: 004-datastar-oxpecker-sample

## Prerequisites

- .NET 10.0 SDK
- Frank repository cloned

## Build & Run

```bash
# Navigate to sample directory
cd sample/Frank.Datastar.Oxpecker

# Build
dotnet build

# Run (default port 5000)
dotnet run
```

## Test

```bash
# In a separate terminal, while the app is running
./test.sh

# Or specify a different port
./test.sh 5001
```

Expected output: All tests (11-28) should show "PASS".

## Key Patterns Demonstrated

### 1. Oxpecker.ViewEngine HTML Generation

```fsharp
open Oxpecker.ViewEngine
open Oxpecker.ViewEngine.Render

let view =
    div(id="contact-view") {
        p() {
            strong() { "First Name:" }
            $" {contact.FirstName}"
        }
        button().attr("data-on:click", "@get('/contacts/1/edit')") { "Edit" }
    }

let html = toString view
```

### 2. Datastar Attributes

```fsharp
// data-on:click for actions
button().attr("data-on:click", "@put('/contacts/1')") { "Save" }

// data-bind for two-way binding
input(type'="text").attr("data-bind:firstName", null)

// data-signals for initial state
div(id="form").attr("data-signals", "{'email': '', 'name': ''}") { ... }
```

### 3. SSE Response Pattern

```fsharp
let handler (ctx: HttpContext) =
    task {
        ctx.Response.Headers.ContentType <- "text/event-stream"
        ctx.Response.Headers.CacheControl <- "no-cache"

        let view = div(id="content") { "Hello" }
        do! Datastar.patchElements (toString view) ctx

        // Keep connection open for updates...
    }
```

## Files

| File | Purpose |
|------|---------|
| `Frank.Datastar.Oxpecker.fsproj` | Project configuration |
| `Program.fs` | All application code (~700 lines) |
| `test.sh` | Automated HTTP tests |

## Comparison with Other Samples

| Sample | View Engine | Syntax Style |
|--------|-------------|--------------|
| Frank.Datastar.Basic | F# string templates | `$"""<div id="x">...</div>"""` |
| Frank.Datastar.Hox | Hox DSL | `h("div#x", [...])` |
| **Frank.Datastar.Oxpecker** | Oxpecker.ViewEngine | `div(id="x") { ... }` |

All three samples implement identical functionality - only the HTML generation differs.
