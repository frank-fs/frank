# Feature Specification: Datastar SSE Streaming Support

**Feature Branch**: `001-datastar-support`
**Created**: 2025-01-25
**Status**: Draft
**Input**: User description: "Add datastar support to Frank. This should expose helpers for interacting with the ServerSentEventGenerator exposed from Starfederation.Datastar. The helper should provide a way to start a server-sent event stream with a handler for 0-n responses. Helpers for one-off interactions should not be generated as those are indistinguishable from HTTP request/response patterns."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stream Multiple Updates to Client (Priority: P1)

A developer wants to send multiple DOM updates to a client over a single SSE connection. This is the core Datastar use case: real-time collaborative updates, progress indicators, live feeds, or any scenario where the server pushes multiple updates over time.

**Why this priority**: This is the fundamental value proposition of Datastar integration. Without streaming support for multiple updates, developers would just use standard HTTP responses.

**Independent Test**: Can be fully tested by creating a resource that streams 3+ updates with delays, verifying each update arrives as a separate SSE event.

**Acceptance Scenarios**:

1. **Given** a Frank resource with a Datastar streaming handler, **When** the handler sends multiple patch operations over time, **Then** each patch is delivered as a separate SSE event to the client.

2. **Given** a streaming handler that loops 10 times with delays, **When** a client connects, **Then** the client receives all 10 updates progressively (not batched at the end).

3. **Given** a streaming handler, **When** the client disconnects mid-stream, **Then** the server detects the disconnection via cancellation token and stops processing.

---

### User Story 2 - Read Client Signals During Stream (Priority: P2)

A developer wants to read signals sent from the Datastar client (e.g., form inputs, ephemeral UI state) and respond with multiple updates. The client sends signals via request body, and the server processes them within a streaming response. Note: Signals are secondary to hypermedia—the response should typically be patch-elements (HTML), not patch-signals.

**Why this priority**: Signal reading enables interactive patterns where client state influences server responses. This completes the bidirectional communication model, though the primary response pattern remains hypermedia (HTML fragments).

**Independent Test**: Can be tested by sending a request with JSON signals in the body, verifying the handler receives and can deserialize them.

**Acceptance Scenarios**:

1. **Given** a request with JSON signals in the body, **When** the streaming handler reads signals, **Then** the signals are deserialized into the specified type.

2. **Given** malformed JSON in the request body, **When** the handler attempts to read signals, **Then** the handler receives a "none" value (not an exception).

3. **Given** signals containing user input, **When** the handler processes and responds, **Then** the response can include transformed data based on those signals.

---

### User Story 3 - Standalone Streaming Functions (Priority: P3)

A developer wants to use Datastar streaming outside of Frank's `resource` computation expression—for example, in a raw ASP.NET Core endpoint or a more complex handler composition.

**Why this priority**: Flexibility for advanced use cases. Not all developers will use the computation expression syntax; standalone functions enable composition with other patterns.

**Independent Test**: Can be tested by invoking `Datastar.*` helper functions directly with a mock HttpContext, verifying they produce correct SSE output independent of Frank's `resource` computation expression.

**Acceptance Scenarios**:

1. **Given** a mock HttpContext and standalone `Datastar.*` functions, **When** the developer calls helpers directly (e.g., `Datastar.patchElements html ctx`), **Then** the output is valid SSE format written to the response stream.

2. **Given** the standalone `Datastar` module, **When** composing multiple helper calls on the same HttpContext, **Then** each helper executes independently and writes to the response in sequence.

**Note**: "Standalone" means the helper functions can be used with any HttpContext, including those from raw ASP.NET Core endpoints. Unit tests with mock HttpContext validate this contract.

---

### Edge Cases

- What happens when the handler completes without sending any events? The stream closes gracefully with no events (empty stream is valid).
- What happens when an exception occurs mid-stream? The stream terminates; error handling is the developer's responsibility within the handler.
- What happens with very large HTML fragments? They are sent as-is; chunking is handled by SSE protocol.
- What happens when the client sends signals but the handler doesn't read them? The signals are ignored; no error occurs.

**Testing Scope**: Edge cases #1 (empty stream) and #4 (unused signals) are validated via unit tests. Edge cases #2-3 are documented behavior only: #2 relies on standard .NET exception handling, #3 relies on SSE protocol/ASP.NET Core response buffering.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The library MUST provide a way to start an SSE stream and execute a user-provided async handler that can send 0 or more Datastar events.

- **FR-002**: The streaming handler MUST receive access to the HTTP context and a cancellation token for detecting client disconnection.

- **FR-003**: Within a streaming handler, developers MUST be able to call operations to: patch HTML elements, patch signals, remove elements, and execute scripts.

- **FR-004**: The library MUST provide a way to read signals from the client request body, returning a typed result or a "none" value for invalid/missing signals.

- **FR-005**: The library MUST NOT provide one-off convenience operations that send a single response. **Rationale**: Recent Datastar versions support regular HTTP responses for single-update interactions. Developers should use standard Frank `resource` handlers for these cases. This extension library focuses exclusively on SSE streaming (0-n responses), which requires special stream lifecycle management that Frank doesn't provide natively.

- **FR-006**: The library MUST integrate with Frank's `resource` computation expression as a custom operation.

- **FR-007**: The library MUST expose a `Datastar` helper module with functions (`patchElements`, `patchSignals`, `removeElement`, `executeScript`, `tryReadSignals`) that can be called from within the `datastar` streaming handler. These same functions enable standalone usage outside Frank when paired with manual SSE stream initialization via `ServerSentEventGenerator`.

- **FR-008**: The library MUST use the latest stable version of the `StarFederation.Datastar` package.

- **FR-009**: All public APIs MUST follow F# idioms: computation expressions for the builder integration, functions with curried signatures for standalone use.

### Key Entities

- **Streaming Handler**: An async function that receives the HTTP context and can send multiple Datastar events over time.
- **Signals**: Client-side state sent in the request body as JSON, deserialized into a user-specified type.
- **Datastar Events**: SSE events for patching elements, patching signals, removing elements, or executing scripts.

## Clarifications

### Session 2025-01-25

- Q: Should HTTP method support be limited to GET only (native SSE) or support other methods? → A: Any HTTP method allowed. Datastar uses fetch-event-source (fetch API-based), not native SSE, so any HTTP method is valid. The implementation should allow the same flexibility.
- Q: Would single-use helpers be useful for regular HTTP request/response patterns? → A: No. Recent Datastar versions support regular HTTP responses for single-update interactions. Standard Frank `resource` handlers should be used for one-off interactions. This extension focuses exclusively on SSE streaming.
- Q: How should the library be validated? → A: Two sample applications demonstrating streaming with (1) simple F# string templates and (2) the Hox library for type-safe HTML rendering.
- Q: Which Datastar pattern is more idiomatic? → A: patch-elements (hypermedia-first) is the primary pattern. patch-signals should be used sparingly for ephemeral UI state only. Samples should emphasize patch-elements while demonstrating idiomatic patch-signals usage.

## Assumptions

- Developers understand SSE and Datastar concepts before using this library.
- The `StarFederation.Datastar` package handles the low-level SSE formatting and protocol details.
- One-off responses should use standard Frank HTTP handlers, not Datastar helpers.
- Datastar is hypermedia-first: patch-elements is the primary/idiomatic pattern; patch-signals is secondary and should be used sparingly for ephemeral UI state only.
- Datastar uses fetch-event-source (@microsoft/fetch-event-source), which supports any HTTP method, not just GET. The streaming handler should not restrict HTTP methods.
- This is an extension library that adapts StarFederation.Datastar to Frank's computation expressions—it adds no additional features beyond this adaptation.
- This library is separate from Frank core and should remain an extension package.
- No deprecation or migration strategy is needed as this feature has not yet been released.

## Validation

The library MUST be validated through unit tests covering all public API functions. Minimum: one test per helper function, one test per user story acceptance scenario, and one test per success criterion that specifies "(Validated via unit test)".

### Unit Test Coverage

- Multi-event streaming (10 progressive updates with delays)
- Single stream start per request enforcement
- Client disconnection detection via cancellation token
- Signal deserialization (valid JSON)
- Malformed JSON handling (returns ValueNone)
- Each helper function (`patchElements`, `patchSignals`, `removeElement`, `executeScript`)
- HTTP method flexibility (GET and POST support)

### Validation Criteria

- All unit tests MUST pass
- Library MUST build without errors across all target frameworks (net8.0, net9.0, net10.0)
- Library MUST NOT expose one-off convenience operations on ResourceBuilder (per FR-005)

**Note**: Sample applications demonstrating real-world usage patterns will be added as separate feature specs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Developers can create a streaming resource that sends 10 progressive updates with 100ms delays, and all 10 updates arrive at the client progressively. (Validated via unit test)

- **SC-002**: The streaming API requires exactly one "start stream" call per request, preventing the common mistake of starting multiple streams. (Validated via unit test)

- **SC-003**: 100% of public APIs use F# idioms (no C#-style method overloads, no nullable types, computation expressions where appropriate).

- **SC-004**: The library compiles against and uses the latest stable `StarFederation.Datastar` NuGet package version.

- **SC-005**: Signal reading handles malformed JSON without throwing exceptions, returning a "none" value instead. (Validated via unit test)

- **SC-006**: The library integrates naturally with Frank's existing `resource` computation expression syntax.
