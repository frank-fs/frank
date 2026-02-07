# Research: Frank.Datastar Native SSE

## R1: SSE Writing Strategy

**Decision**: Write directly to `IBufferWriter<byte>` via `HttpResponse.BodyWriter` (PipeWriter), using pre-allocated byte arrays and inline functions.

**Rationale**: This is the same approach used by the existing StarFederation.Datastar.FSharp implementation. The PipeWriter backing ASP.NET Core's response body implements `IBufferWriter<byte>`, enabling zero-copy writes directly into Kestrel's output pipeline. .NET 10's `SseFormatter` also uses this pattern internally, confirming it as the idiomatic high-performance path.

**Alternatives considered**:
- `SseFormatter.WriteAsync` with `IAsyncEnumerable<SseItem<T>>`: Elegant but oriented toward simple event streaming. Datastar's custom `data:` line format (e.g., `data: mode inner`, `data: selector #feed`, `data: elements <html>`) requires per-line control that `SseFormatter` doesn't expose. The formatter handles the `event:`, `id:`, `retry:` framing but the `data:` payload is a single blob, whereas Datastar needs multiple structured data lines per event.
- `TextWriter`/`StreamWriter`: Would require char-to-byte encoding overhead. SSE is UTF-8, so working at the byte level is correct and more efficient.
- `System.Net.ServerSentEvents.SseFormatter` with custom `Action<SseItem<T>, IBufferWriter<byte>>` formatter: The custom formatter callback does receive `IBufferWriter<byte>`, but the API shapes the data as a single `SseItem<T>.Data` per event. Datastar needs to emit multiple `data:` lines with different prefixes (`selector`, `mode`, `elements`, etc.) which doesn't map cleanly to the single-data-field model.

## R2: .NET 10 API Utilization

**Decision**: Target .NET 10 only but do NOT use `System.Net.ServerSentEvents.SseFormatter` for writing. Use `System.Net.ServerSentEvents.SseParser` only if needed for testing/validation.

**Rationale**: The `SseFormatter` API is designed for simple string-data or single-blob events. Datastar's SSE format requires multiple structured `data:` lines per event with different prefixes. Writing directly to `IBufferWriter<byte>` gives full control over the format and matches the ADR specification exactly. Targeting .NET 10 still benefits from framework improvements (PipeWriter optimizations, latest ASP.NET Core) even without using the SSE-specific APIs.

**Alternatives considered**:
- Use `SseFormatter` and encode all Datastar data lines into a single string: Would work but defeats the zero-allocation purpose by requiring string concatenation before passing to the formatter.
- Multi-target with polyfills for .NET 8/9: Increases maintenance burden with no benefit since the core writing strategy (IBufferWriter) is available on all targets. The decision to go .NET 10 only is driven by simplicity, not API availability.

## R3: Thread Safety and Event Ordering

**Decision**: Use the same `ConcurrentQueue<unit -> Task>` pattern as StarFederation.Datastar.FSharp for the instance-based `ServerSentEventGenerator`, with a lock-protected initialization guard.

**Rationale**: The pattern is proven, lightweight, and ensures ordered delivery without blocking concurrent callers. The static methods (used by Frank.Datastar's `Datastar` module) don't need the queue since the `datastar` custom operation already serializes handler execution.

**Alternatives considered**:
- `SemaphoreSlim`: More overhead for a simple serialization need. The ConcurrentQueue pattern is lock-free for enqueue operations.
- `Channel<T>`: More complex API surface for a simple producer-consumer pattern. ConcurrentQueue is sufficient since events are processed synchronously after dequeue.
- No queue (static methods only): Would work for the Frank.Datastar wrapper but violates ADR requirement for a public `ServerSentEventGenerator` that supports concurrent usage.

## R4: Option Type Design

**Decision**: Re-implement all option types as `[<Struct>]` F# records with `static member Defaults`, matching the existing StarFederation.Datastar.FSharp API exactly (except `ExecuteScriptOptions.Attributes` changes from `KeyValuePair<string, string> list` to `string[]`).

**Rationale**: Struct records minimize allocation. The `Defaults` pattern enables `{ SomeOptions.Defaults with Field = value }` syntax, which is idiomatic F# and preserves backward compatibility with existing usage patterns. The `Attributes` type change aligns with the ADR's `[]string` specification and the clarification decision.

**Alternatives considered**:
- Class-based option types: Would add heap allocation per options instance. Struct records are stack-allocated.
- Builder pattern: More verbose, less idiomatic F#. The `with` copy-and-update syntax is cleaner.
- Single large options type: Would couple unrelated concerns (patch options vs signal options).

## R5: Test Impact

**Decision**: Tests require only an import change: `open StarFederation.Datastar.FSharp` → no explicit import needed (types re-exported from `Frank.Datastar` namespace).

**Rationale**: Analysis of the test file shows all StarFederation types used (option records, enums) are accessed via `open StarFederation.Datastar.FSharp`. The sample projects do NOT import StarFederation directly. Since Frank.Datastar will define these types in its own namespace and the tests already `open Frank.Datastar`, removing the StarFederation import and ensuring the types are accessible from `Frank.Datastar` is sufficient.

**Changes needed**:
- `DatastarTests.fs` line 13: Remove `open StarFederation.Datastar.FSharp` (types available via `open Frank.Datastar`)
- All sample projects: No changes (they only use `open Frank.Datastar`)
- Test assertions: No changes (string-based SSE format assertions are implementation-agnostic)

## R6: Version Implications

**Decision**: This is a MAJOR version bump (7.x → 8.0.0) for Frank.Datastar due to target framework restriction and Attributes type change.

**Rationale**:
- Dropping net8.0 and net9.0 targets is a breaking change for consumers on those platforms.
- Changing `ExecuteScriptOptions.Attributes` from `KeyValuePair<string, string> list` to `string[]` is a binary-breaking change.
- However, since Frank.Datastar 7.0.0 did not expose `Attributes` in its public API (the field existed only in the upstream dependency), consumers using only the `Datastar` module functions are unaffected at the source level.

**Alternatives considered**:
- Keep multi-target and use `#if NET10_0_OR_GREATER` conditionals: Increases maintenance complexity without benefit; the whole point is to remove the external dependency.
- Ship as a separate package (e.g., Frank.Datastar.Native): Fragmenting the package ecosystem adds confusion; a clean version bump is clearer.
