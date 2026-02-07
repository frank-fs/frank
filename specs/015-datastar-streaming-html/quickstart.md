# Quickstart: Frank.Datastar Streaming HTML Generation

**Date**: 2026-02-07
**Feature**: [spec.md](spec.md)

## Overview

Frank.Datastar 8.1.0 adds stream-based overloads for all SSE event operations. Instead of materializing HTML as an intermediate string, you can write directly to a `TextWriter` that emits SSE `data:` lines on the fly. This reduces memory allocations in high-throughput scenarios (1000+ events/sec).

## String-Based (existing — unchanged)

```fsharp
open Frank.Datastar

resource "/sse" {
    name "SSE"
    datastar (fun ctx ->
        task {
            let html = $"<div id=\"feed\">Hello at {DateTime.Now}</div>"
            do! Datastar.patchElements html ctx
        })
}
```

## Stream-Based (new)

```fsharp
open Frank.Datastar

resource "/sse" {
    name "SSE"
    datastar (fun ctx ->
        task {
            do! Datastar.streamPatchElements (fun tw ->
                tw.Write($"<div id=\"feed\">Hello at {DateTime.Now}</div>")
                Task.CompletedTask) ctx
        })
}
```

## When to Use Stream-Based

Use stream-based when:
- Generating large HTML templates (500+ bytes)
- High throughput (1000+ events/sec)
- View engine supports `TextWriter` output (future Hox/Oxpecker streaming)
- Serializing JSON signals directly (avoids intermediate string)

Use string-based when:
- HTML is small or static
- View engine only supports string output (current Hox `Render.asString`, Oxpecker `Render.toString`)
- Simplicity is preferred over allocation optimization

## JSON Signal Streaming

Stream-based `patchSignals` enables direct JSON serialization to the response:

```fsharp
do! Datastar.streamPatchSignals (fun tw ->
    JsonSerializer.Serialize(tw, mySignals)
    Task.CompletedTask) ctx
```

## Async Writer Callbacks

For async operations during rendering (e.g., data fetching):

```fsharp
do! Datastar.streamPatchElements (fun tw ->
    task {
        let! data = loadDataAsync()
        tw.Write($"<div id=\"result\">{data.Value}</div>")
    }) ctx
```

## With Options

```fsharp
let opts = { PatchElementsOptions.Defaults with PatchMode = Inner }

do! Datastar.streamPatchElementsWithOptions opts (fun tw ->
    tw.Write("<span>Updated content</span>")
    Task.CompletedTask) ctx
```

## Future: View Engine Streaming

When view engines add TextWriter support, the integration is direct:

```fsharp
// Future Hox streaming (when Hox adds Render.toTextWriter)
do! Datastar.streamPatchElements (fun tw ->
    Hox.Render.toTextWriter(myNode, tw)) ctx

// Future Oxpecker streaming (when Oxpecker adds TextWriter support)
do! Datastar.streamPatchElements (fun tw ->
    Oxpecker.Render.toTextWriter(myElement, tw)) ctx
```

## Performance Characteristics

| Metric | String-Based | Stream-Based |
|--------|-------------|-------------|
| Allocations per event | Full HTML string (500-2000 bytes) | TextWriter object (~48 bytes) |
| Line buffer | None (StringTokenizer is zero-alloc) | Rented from ArrayPool (~0 after warmup) |
| Encoding | Batch (full string → UTF-8) | Per-line (line → UTF-8) |
| Best for | Small/static content | Large templates, high throughput |
