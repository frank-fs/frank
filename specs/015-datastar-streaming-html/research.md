# Research: Frank.Datastar Streaming HTML Generation

**Date**: 2026-02-07
**Feature**: [spec.md](spec.md)

## R-001: Custom TextWriter for SSE Line-Buffering

**Decision**: Create an internal `SseDataLineWriter` subclass of `System.IO.TextWriter` that wraps `IBufferWriter<byte>` and auto-emits SSE `data:` lines on newlines.

**Rationale**: TextWriter is the public API surface chosen for view engine compatibility (Hox `Render.toTextWriter`, Oxpecker future streaming). Internally, the writer bridges the char-level TextWriter API to Frank.Datastar's existing byte-level `IBufferWriter<byte>` write pipeline. This preserves the zero-copy write path for SSE while exposing a familiar API to callers.

**Alternatives considered**:
- **Direct `IBufferWriter<byte>` callback**: More performant but incompatible with view engines that expect TextWriter. Rejected because it sacrifices the primary adoption benefit.
- **`Stream` callback**: Less structured than TextWriter, no line-oriented API. TextWriter is strictly better for text output.
- **`PipeWriter` callback**: ASP.NET Core-specific, not supported by any view engine. Too low-level for callers.

## R-002: TextWriter Internal Buffer Strategy

**Decision**: Use `ArrayPool<char>.Shared.Rent(256)` for the internal line buffer. Return to pool on `Dispose()`.

**Rationale**: Typical HTML lines are under 256 chars. ArrayPool renting is effectively zero-allocation after warmup (pool reuses buffers). If a line exceeds capacity, rent a larger buffer and copy. This matches the `~256 byte` line buffer described in the spec.

**Alternatives considered**:
- **StringBuilder**: Allocates internal `char[]` that cannot be returned to a pool. Worse allocation profile.
- **stackalloc**: Cannot be used in async contexts or stored in fields. TextWriter is a class with mutable state, so stack allocation is not viable.
- **Fixed `char[256]`**: Simple but wastes memory for short lines and cannot grow. ArrayPool is strictly better.

**Allocation profile**:
| Component | String-based | Stream-based |
|-----------|-------------|-------------|
| Full HTML string | 500-2000 bytes | 0 (eliminated) |
| TextWriter object | 0 | ~48 bytes (one-time per event) |
| Line buffer | 0 | ~0 (rented from ArrayPool, returned on Dispose) |
| **Net per event** | **500-2000 bytes** | **~48 bytes** |

## R-003: TextWriter-to-IBufferWriter Bridge Pattern

**Decision**: On each newline, encode the buffered char line directly to `IBufferWriter<byte>` using `Encoding.UTF8.GetBytes(charSpan, byteSpan)` — the same pattern already used by `ServerSentEvent.writeUtf8String`.

**Rationale**: This avoids creating intermediate `string` objects. The char buffer content is encoded directly into the `IBufferWriter<byte>` span, matching Frank.Datastar's existing zero-copy write pipeline.

**Implementation sketch**:
1. Caller writes chars to TextWriter (sync or async)
2. TextWriter buffers chars until `\n`
3. On `\n`, TextWriter calls internal `emitLine()`:
   - Writes `data: ` prefix (pre-allocated `byte[]`)
   - Writes `dataLineType` (e.g., `"elements"B`, pre-allocated)
   - Writes space
   - Encodes buffered `char[]` to UTF-8 directly into `IBufferWriter<byte>` span
   - Writes newline byte
   - Resets char buffer position to 0
4. On `Flush()`/`Dispose()`: emit any remaining buffered chars as a final line

## R-004: Overload Resolution Strategy

**Decision**: Use method overloading on `ServerSentEventGenerator` (type supports overloads), use `stream` prefix on `Datastar` module functions (F# modules don't support overloads).

**Rationale**: F# `type` members support overloading by parameter type. `string` vs `TextWriter -> Task` are distinct types, so overload resolution is unambiguous. F# `module` `let` bindings cannot be overloaded, so a naming convention is needed.

**Naming convention**:
- `ServerSentEventGenerator.PatchElementsAsync(response, writer: TextWriter -> Task, options, ct)` — overloaded
- `Datastar.streamPatchElements (writer: TextWriter -> Task) (ctx: HttpContext)` — prefixed

**Alternatives considered**:
- **Separate method names on ServerSentEventGenerator** (e.g., `StreamPatchElementsAsync`): Unnecessary since type overloading works. Doubles the API surface names without benefit.
- **`write` prefix on module functions** (e.g., `writePatchElements`): Less clear intent. `stream` communicates the performance motivation.

## R-005: View Engine Streaming Compatibility

**Decision**: Frank.Datastar provides `TextWriter -> Task` callbacks. View engine streaming adoption is external and incremental.

**Rationale**: Research confirms:
- **Hox 3.x**: Currently exposes `Render.asString : Node -> ValueTask<string>`. Does NOT yet expose `Render.toTextWriter`. Would need upstream addition.
- **Oxpecker.ViewEngine 2.x**: Currently exposes `Render.toString : HtmlElement -> string`. Synchronous only. Would need upstream addition of TextWriter support.
- **Both engines**: The `TextWriter -> Task` signature is the natural target for future streaming support. TextWriter is the standard .NET abstraction for text output.

**No action required in Frank.Datastar**: The streaming API is ready for view engines to adopt at their own pace. String-based APIs remain for engines without streaming support.

## R-006: Error Handling During Streaming

**Decision**: If the writer callback throws, catch the exception after the callback completes. Do NOT attempt to write a partial SSE event terminator if bytes have already been written to the buffer. Instead, let the exception propagate to the caller (the `datastar` computation expression handler), which controls the SSE connection lifecycle.

**Rationale**: SSE events are framed by blank lines. If a writer callback throws mid-line, the buffer may contain partial `data:` line bytes that have already been committed to `IBufferWriter<byte>`. Attempting recovery would produce malformed SSE. The safest approach is to let the exception propagate and rely on the existing connection error handling (the `try/with` in sample SSE handlers).

**Alternatives considered**:
- **Flush a blank line to terminate the partial event**: Risky — partial `data:` line bytes may already be committed, producing malformed SSE.
- **Wrap in try/catch and swallow**: Hides errors from callers. Violates principle of least surprise.

## R-007: Cancellation During Streaming

**Decision**: Pass `CancellationToken` to the `SseDataLineWriter` constructor. Check cancellation before each line emission. If cancelled, throw `OperationCanceledException`.

**Rationale**: Follows the existing `HttpContext.RequestAborted` pattern used by all `ServerSentEventGenerator` methods. The writer callback may run for extended periods (e.g., streaming a large template), so cancellation must be checked at line boundaries.
