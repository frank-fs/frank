# Data Model: Frank.Datastar Streaming HTML Generation

**Date**: 2026-02-07
**Feature**: [spec.md](spec.md)

## Entities

### SseDataLineWriter (Internal)

Custom `TextWriter` subclass that bridges caller text output to SSE-formatted `data:` lines on `IBufferWriter<byte>`.

| Field | Type | Description |
|-------|------|-------------|
| `bufferWriter` | `IBufferWriter<byte>` | Target buffer (typically `HttpResponse.BodyWriter`) |
| `dataLineType` | `byte[]` | Pre-allocated SSE data line type (e.g., `"elements"B`, `"signals"B`) |
| `charBuffer` | `char[]` | Rented line buffer from `ArrayPool<char>.Shared` |
| `position` | `int` (mutable) | Current write position in `charBuffer` |
| `cancellationToken` | `CancellationToken` | Token for cancellation checks at line boundaries |

**Lifecycle**: Created per streaming call, used for one writer callback invocation, disposed after callback completes.

**State transitions**:
1. **Created** → `charBuffer` rented from pool, `position = 0`
2. **Writing** → Characters accumulate in `charBuffer`. On `\n`, line is emitted to `bufferWriter` and `position` resets to 0. If `charBuffer` fills, a larger buffer is rented.
3. **Flushing** → Any remaining chars in `charBuffer` (after last `\n`) are emitted as a final `data:` line.
4. **Disposed** → `charBuffer` returned to `ArrayPool<char>.Shared`.

### Existing Entities (Unchanged)

These entities from spec 014 are used by the streaming API but not modified:

| Entity | Role in Streaming |
|--------|------------------|
| `PatchElementsOptions` | Passed to stream-based `PatchElementsAsync` overload (same options) |
| `RemoveElementOptions` | Passed to stream-based `RemoveElementAsync` overload |
| `PatchSignalsOptions` | Passed to stream-based `PatchSignalsAsync` overload |
| `ExecuteScriptOptions` | Passed to stream-based `ExecuteScriptAsync` overload |
| `ServerSentEventGenerator` | Extended with stream-based static method overloads |

## Relationships

```
ServerSentEventGenerator
  ├── PatchElementsAsync(string)        [existing - unchanged]
  ├── PatchElementsAsync(TextWriter→Task) [new - creates SseDataLineWriter]
  ├── RemoveElementAsync(string)        [existing - unchanged]
  ├── RemoveElementAsync(TextWriter→Task) [new - creates SseDataLineWriter]
  ├── PatchSignalsAsync(string)         [existing - unchanged]
  ├── PatchSignalsAsync(TextWriter→Task)  [new - creates SseDataLineWriter]
  ├── ExecuteScriptAsync(string)        [existing - unchanged]
  └── ExecuteScriptAsync(TextWriter→Task) [new - creates SseDataLineWriter]

SseDataLineWriter
  ├── wraps: IBufferWriter<byte> (from HttpResponse.BodyWriter)
  ├── uses: ServerSentEvent.writeUtf8Literal (for data: prefix)
  ├── uses: Encoding.UTF8.GetBytes (for char→byte encoding)
  └── returns to: ArrayPool<char>.Shared (on Dispose)

Datastar module
  ├── patchElements / streamPatchElements
  ├── removeElement / streamRemoveElement
  ├── patchSignals / streamPatchSignals
  ├── executeScript / streamExecuteScript
  └── (WithOptions variants for all of the above)
```

## Validation Rules

- `charBuffer` initial capacity: 256 chars (covers typical HTML line lengths)
- `charBuffer` growth: rent larger buffer from pool, copy existing content, return old buffer
- Empty lines (consecutive `\n\n`) are filtered out (matching `String.splitLinesToSegments` behavior which filters `segment.Length > 0`)
- `\r\n` and `\r` are treated as line boundaries (matching existing `newLineChars = [| '\r'; '\n' |]`)
- Cancellation is checked before each line emission, not mid-line
