# API Contracts: Frank.Datastar Streaming HTML Generation

**Date**: 2026-02-07
**Feature**: [../spec.md](../spec.md)

## ServerSentEventGenerator — New Stream-Based Overloads

All new methods follow the existing 3-level overload pattern:
1. Full signature with `CancellationToken`
2. Without token (uses `httpResponse.HttpContext.RequestAborted`)
3. Without options (uses `*.Defaults`)

### PatchElementsAsync (stream)

```fsharp
/// Full signature
static member PatchElementsAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task,
    options: PatchElementsOptions,
    cancellationToken: CancellationToken) : Task

/// Without CancellationToken
static member PatchElementsAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task,
    options: PatchElementsOptions) : Task

/// Without options (uses PatchElementsOptions.Defaults)
static member PatchElementsAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task) : Task
```

**TextWriter data line type**: `Bytes.DatalineElements` (`"elements"B`)

### RemoveElementAsync (stream)

```fsharp
/// Full signature — writer provides the selector
static member RemoveElementAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task,
    options: RemoveElementOptions,
    cancellationToken: CancellationToken) : Task

/// Without CancellationToken
static member RemoveElementAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task,
    options: RemoveElementOptions) : Task

/// Without options
static member RemoveElementAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task) : Task
```

**TextWriter data line type**: `Bytes.DatalineSelector` (`"selector"B`)
**Note**: Writer output is treated as the selector value. Mode is hardcoded to `remove`.

### PatchSignalsAsync (stream)

```fsharp
/// Full signature — writer provides JSON signals
static member PatchSignalsAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task,
    options: PatchSignalsOptions,
    cancellationToken: CancellationToken) : Task

/// Without CancellationToken
static member PatchSignalsAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task,
    options: PatchSignalsOptions) : Task

/// Without options
static member PatchSignalsAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task) : Task
```

**TextWriter data line type**: `Bytes.DatalineSignals` (`"signals"B`)
**Benefit**: Enables `JsonSerializer.Serialize(textWriter, value)` for zero-string JSON serialization.

### ExecuteScriptAsync (stream)

```fsharp
/// Full signature — writer provides script body (wrapped in <script> tags)
static member ExecuteScriptAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task,
    options: ExecuteScriptOptions,
    cancellationToken: CancellationToken) : Task

/// Without CancellationToken
static member ExecuteScriptAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task,
    options: ExecuteScriptOptions) : Task

/// Without options
static member ExecuteScriptAsync(
    httpResponse: HttpResponse,
    writer: TextWriter -> Task) : Task
```

**TextWriter data line type**: `Bytes.DatalineElements` (`"elements"B`)
**Note**: `<script>` open/close tags are emitted by the method, not the writer callback.

## Datastar Module — New Stream-Based Functions

All new functions are `inline` and follow the existing curried pattern: `content -> ctx -> Task`.

```fsharp
module Datastar =
    // Stream-based (new)
    val inline streamPatchElements:
        writer:(TextWriter -> Task) -> ctx:HttpContext -> Task

    val inline streamPatchElementsWithOptions:
        options:PatchElementsOptions -> writer:(TextWriter -> Task) -> ctx:HttpContext -> Task

    val inline streamRemoveElement:
        writer:(TextWriter -> Task) -> ctx:HttpContext -> Task

    val inline streamRemoveElementWithOptions:
        options:RemoveElementOptions -> writer:(TextWriter -> Task) -> ctx:HttpContext -> Task

    val inline streamPatchSignals:
        writer:(TextWriter -> Task) -> ctx:HttpContext -> Task

    val inline streamPatchSignalsWithOptions:
        options:PatchSignalsOptions -> writer:(TextWriter -> Task) -> ctx:HttpContext -> Task

    val inline streamExecuteScript:
        writer:(TextWriter -> Task) -> ctx:HttpContext -> Task

    val inline streamExecuteScriptWithOptions:
        options:ExecuteScriptOptions -> writer:(TextWriter -> Task) -> ctx:HttpContext -> Task
```

## SseDataLineWriter — Internal API

```fsharp
/// Internal TextWriter subclass for SSE line-buffered output
type internal SseDataLineWriter =
    inherit TextWriter

    /// Create a new writer targeting the given buffer with the specified data line type
    new: bufferWriter:IBufferWriter<byte>
       * dataLineType:byte[]
       * cancellationToken:CancellationToken
      -> SseDataLineWriter

    /// UTF-8 encoding
    override Encoding: Encoding

    /// Buffer char, emit line on '\n'
    override Write: value:char -> unit

    /// Buffer string, emit lines on '\n' boundaries
    override Write: value:string -> unit

    /// Emit any remaining buffered content as a final data line
    override Flush: unit -> unit

    /// IDisposable: flush + return char buffer to ArrayPool
    override Dispose: disposing:bool -> unit
```

## Behavioral Contract

### Byte-for-byte equivalence (SC-004)

For any HTML content `html`:
```
StringBased:  PatchElementsAsync(response, html)
StreamBased:  PatchElementsAsync(response, fun tw -> tw.Write(html); Task.CompletedTask)
```
These MUST produce identical bytes in the response body.

### SSE line format

Each line emitted by `SseDataLineWriter` follows the format:
```
data: <dataLineType> <line-content>\n
```
Where:
- `data: ` is the SSE data prefix (6 bytes: `"data: "B`)
- `<dataLineType>` is the pre-allocated byte literal (e.g., `"elements"B`)
- ` ` is a space separator (1 byte)
- `<line-content>` is the UTF-8 encoded line (variable)
- `\n` is a newline terminator (1 byte)

### Empty line filtering

Lines with zero content (from consecutive newlines like `\n\n`) are skipped, matching the existing `String.splitLinesToSegments` behavior that filters `segment.Length > 0`.
