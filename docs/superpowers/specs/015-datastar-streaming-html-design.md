---
source: specs/015-datastar-streaming-html
status: released
type: spec
---

# Feature Specification: Frank.Datastar Streaming HTML Generation

**Feature Branch**: `015-datastar-streaming-html`
**Created**: 2026-02-07
**Status**: Draft
**Input**: User description: "Add stream-based overloads to write directly to the response stream rather than to strings. From the previous spec: Spec 015: Frank.Datastar Streaming HTML Generation - Goal: Zero-allocation HTML rendering for Datastar SSE events - Requires: Coordination with Hox and Oxpecker maintainers - Benefit: Reduced allocations for high-throughput scenarios (1000+ events/sec)"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stream HTML Directly to SSE Response (Priority: P1)

As a Frank.Datastar library consumer generating dynamic HTML in high-throughput scenarios (1000+ events/sec), I want to stream HTML directly to the SSE response buffer without materializing the full HTML as an intermediate string, so that I can reduce memory allocations and improve performance under load.

**Why this priority**: This is the core value proposition. Eliminating string materialization removes a major allocation bottleneck in high-throughput scenarios. This foundational capability enables all other streaming scenarios.

**Independent Test**: Can be fully tested by sending HTML via the new stream-based API and measuring allocation counts compared to the string-based baseline. Delivers immediate allocation reduction for any consumer using the new API.

**Acceptance Scenarios**:

1. **Given** a Frank.Datastar SSE handler with dynamic HTML content, **When** the handler calls the stream-based `patchElements` overload with a writer function, **Then** the HTML is written directly to the response buffer without creating an intermediate string
2. **Given** multi-line HTML content, **When** written via the stream-based API, **Then** the content is correctly split into SSE `data:` lines without allocating string segments
3. **Given** a high-throughput scenario (1000 events/sec), **When** using stream-based overloads, **Then** allocation counts are measurably lower than string-based equivalents
4. **Given** an error during streaming HTML generation, **When** the writer function throws, **Then** the SSE stream handles the error gracefully without corrupting the response

---

### User Story 2 - View Engine Integration (Priority: P2)

As a developer using Hox or Oxpecker view engines with Frank.Datastar, I want to continue using the string-based API if my view engine does not yet support streaming, and switch to the stream-based API when it does, without changing how I interact with Frank.Datastar.

**Why this priority**: This ensures the feature is adoptable incrementally. View engine maintainers add streaming at their own pace; Frank.Datastar consumers are never blocked. However, until view engines add native streaming support, the sample projects continue to use string-based rendering.

**Independent Test**: Can be tested by (a) verifying sample projects using string-based rendering still work unchanged, and (b) if a view engine supports streaming, verifying that the stream-based API produces correct HTML. Delivers a smooth incremental adoption path.

**Acceptance Scenarios**:

1. **Given** a view engine that does NOT support streaming, **When** a developer uses Frank.Datastar, **Then** the existing string-based API works exactly as before with no changes required
2. **Given** a view engine that DOES support streaming, **When** a developer uses the stream-based Frank.Datastar overload with the engine's streaming renderer, **Then** the HTML is written directly to the SSE buffer without string materialization
3. **Given** a complex nested view template rendered via streaming, **When** streamed to SSE, **Then** the output matches the string-based rendering exactly
4. **Given** a developer using both string-based and stream-based calls in the same application (different view engines or patterns), **When** both are called, **Then** both produce correct SSE output without interference

---

### User Story 3 - Backward Compatibility (Priority: P1)

As an existing Frank.Datastar user, I want the string-based API to remain available and unchanged, so that I can upgrade to the new version without breaking my existing code.

**Why this priority**: Backward compatibility is critical for library adoption. Breaking existing consumers would force migration work and reduce upgrade willingness. This is equally important to the new streaming API.

**Independent Test**: Can be tested by running the existing Frank.Datastar test suite and all sample projects against the new version with zero code changes. Delivers seamless upgrade path.

**Acceptance Scenarios**:

1. **Given** existing Frank.Datastar code using string-based `patchElements`, **When** compiled against the new version, **Then** compilation succeeds without changes
2. **Given** the existing test suite, **When** run against the new version, **Then** all tests pass without modifications
3. **Given** sample projects (Basic, Hox, Oxpecker), **When** built against the new version, **Then** they compile and run without changes
4. **Given** a consumer using both string-based and stream-based APIs in the same application, **When** both are called, **Then** both work correctly without interference

---

### Edge Cases

- What happens when the HTML writer function throws an exception mid-stream? The SSE response may be partially written - how is this handled?
- What happens if a view engine template is too large to fit in memory when rendered as a string, but fits when streamed? The streaming API should handle this gracefully.
- What happens when a developer accidentally mixes string-based and stream-based calls in the same SSE event? The system should either prevent this or handle it safely.
- What happens if the view engine streaming implementation has different whitespace/formatting than string rendering? Output must be functionally identical.
- What happens when the client disconnects mid-stream during HTML generation? The cancellation token should be respected and resources cleaned up.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Frank.Datastar MUST provide stream-based overloads for all SSE event operations that accept string content: `patchElements`, `patchElementsWithOptions`, `patchSignals`, `patchSignalsWithOptions`, `removeElement`, `removeElementWithOptions`, `executeScript`, `executeScriptWithOptions`. This includes `patchSignals` (benefits from direct JSON serialization to stream) and `removeElement` (API consistency) alongside HTML-producing operations.
- **FR-002**: Stream-based overloads MUST accept an async writer function (`TextWriter -> Task`) that receives a `TextWriter`. Sync callers return `Task.CompletedTask`. The `TextWriter` implementation MUST internally line-buffer content and auto-emit SSE `data:` lines on each newline, hiding SSE protocol details from the caller. This is compatible with view engines that already support `TextWriter` output.
- **FR-003**: Stream-based HTML generation MUST correctly split multi-line content into separate SSE `data:` lines with appropriate prefixes, matching the behavior of string-based APIs
- **FR-004**: Stream-based operations MUST propagate cancellation tokens to allow graceful shutdown when clients disconnect
- **FR-005**: Stream-based operations MUST handle exceptions from writer functions by completing the SSE event cleanly or terminating the stream safely
- **FR-006**: The string-based API MUST remain unchanged and continue to work exactly as before (backward compatibility guarantee)
- **FR-007**: Performance gains from streaming MUST be measurable via allocation profiling, showing reduced allocations compared to string-based baseline
- **FR-008**: View engine streaming support requires upstream changes to the view engines themselves. Frank.Datastar MUST NOT implement view engine adapter code. View engines that do not yet support streaming MUST continue to work via the existing string-based API. Frank.Datastar provides both string-based and stream-based overloads so that consumers can adopt streaming incrementally as their view engine adds support.
- **FR-009**: Documentation MUST include examples showing when to use stream-based vs string-based APIs and expected performance characteristics
- **FR-010**: The public `ServerSentEventGenerator` MUST expose stream-based static methods (accepting `TextWriter -> Task` writer functions) alongside existing string-based methods

### Key Entities

- **Stream Writer Function**: A user-provided async callback (`TextWriter -> Task`) that receives a `TextWriter` and writes HTML content to it. Sync callers return `Task.CompletedTask`. The `TextWriter` internally line-buffers and emits SSE `data:` lines on newlines. The caller writes HTML naturally (including newlines) without awareness of SSE framing.
- **SSE Event Boundary**: The point at which HTML content must be split into SSE `data:` lines. Stream-based implementation must handle this boundary correctly even when HTML is written incrementally.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Applications using stream-based APIs allocate at least 50% fewer bytes per SSE event compared to string-based equivalents, as measured by allocation profiling. (Note: a small internal line buffer is expected; the savings come from eliminating full HTML string materialization.)
- **SC-002**: Applications handling 1000+ events/sec show measurable throughput improvements (events/sec increase or latency reduction) when using stream-based APIs
- **SC-003**: All existing Frank.Datastar tests and sample projects pass without modification after adding stream-based overloads
- **SC-004**: Stream-based SSE output is byte-for-byte identical to string-based output for the same HTML content (functional equivalence)
- **SC-005**: View engine templates that support `TextWriter` output can be streamed to SSE with minimal allocation overhead (only the internal line buffer, not the full rendered HTML string)
- **SC-006**: Documentation includes at least 3 working examples demonstrating stream-based usage with different view engines and scenarios

## Clarifications

### Session 2026-02-07

- Q: Does Frank.Datastar implement view engine streaming adapters, or require upstream changes? -> A: Require upstream changes to Hox/Oxpecker. Frank.Datastar does NOT implement adapter code. View engines that do not support streaming continue to use the string-based API. Frank.Datastar provides both string-based and stream-based overloads so consumers can adopt streaming incrementally.
- Q: What input model should the stream-based writer function use? -> A: The writer function receives a `TextWriter` that internally line-buffers and auto-emits SSE `data:` lines on each newline. This hides SSE line-splitting complexity from callers and is compatible with view engines that already support `TextWriter` output (e.g., Hox `Render.toTextWriter`).
- Q: Should the spec acknowledge the line buffer tradeoff and adjust "zero-allocation" language? -> A: Yes. Reframe from "zero-allocation" to "minimal-allocation." The `TextWriter` line buffer (~256 bytes) is a necessary tradeoff for SSE compliance. The real win is eliminating full HTML string materialization (500-2000+ bytes), not achieving absolute zero allocations.
- Q: Should stream-based overloads be limited to operations that accept multi-line HTML? -> A: No. Provide stream overloads for ALL operations including `patchSignals` (benefits from direct JSON serialization to stream) and `removeElement` (consistency). Full uniform API surface.
- Q: Should the writer function be sync or async? -> A: Async only: `TextWriter -> Task`. Sync callers return `Task.CompletedTask` (cached singleton, zero allocation). TextWriter itself has both sync `Write()` and async `WriteAsync()` methods, so callers choose at the TextWriter API level without needing separate overloads. This halves the API surface while supporting both sync and async scenarios.

## Assumptions

- **Hox and Oxpecker view engines** may or may not support streaming output today. Frank.Datastar does not gate its streaming API on view engine support; consumers using view engines without streaming continue to use the string-based API. View engine maintainers adopt streaming at their own pace.
- **High-throughput threshold** is defined as 1000+ events/sec based on the user's stated benefit. This is a typical benchmark for server-side rendering performance.
- **Allocation reduction target** of 50% is achievable by eliminating full string materialization for HTML content. A small internal line buffer (~256 bytes) is required by the `TextWriter` implementation for SSE line splitting. The savings come from not allocating the full rendered HTML string (typically 500-2000 bytes).
- **Backward compatibility** is non-negotiable - the string-based API must remain unchanged to avoid breaking existing consumers.
- **Error handling strategy** for streaming failures defaults to terminating the SSE stream gracefully rather than attempting recovery, to avoid corrupted responses.
- **Cancellation token support** uses the existing `HttpContext.RequestAborted` pattern established in Frank.Datastar 8.0.0 (spec 014).

## Research

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
