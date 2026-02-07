# Feature Specification: Frank.Datastar Native SSE Implementation

**Feature Branch**: `014-datastar-native-sse`
**Created**: 2026-02-07
**Status**: Draft
**Input**: User description: "Frank.Datastar currently relies upon the StarFederation.Datastar dependency. .NET 10 introduced native SSE support via System.Net.ServerSentEvents. Evaluate whether Frank.Datastar should replace the external dependency with a purpose-built implementation leveraging .NET 10's native SSE APIs for improved efficiency."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Send Datastar SSE Events Without External Dependencies (Priority: P1)

As a Frank.Datastar library consumer, I want to send Datastar SSE events (patch-elements, patch-signals, remove-element, execute-script) to the browser without requiring the StarFederation.Datastar.FSharp NuGet package, so that the library has fewer external dependencies and can leverage .NET 10's built-in SSE support for better performance.

**Why this priority**: This is the core value proposition. Removing the external dependency eliminates a transitive dependency chain and allows Frank.Datastar to directly control how bytes are written to the response stream, enabling more efficient resource usage. This is the foundation all other stories depend on.

**Independent Test**: Can be fully tested by sending each Datastar event type through the new implementation and verifying the SSE output conforms to the Datastar SDK ADR specification. Delivers a self-contained Frank.Datastar library with no third-party SSE dependencies.

**Acceptance Scenarios**:

1. **Given** a Frank.Datastar resource handler, **When** the handler calls `patchElements` with an HTML string, **Then** the response body contains a well-formed SSE message with `event: datastar-patch-elements`, properly split multi-line `data: elements` lines, and a blank-line terminator.
2. **Given** a Frank.Datastar resource handler, **When** the handler calls `patchSignals` with a JSON string, **Then** the response body contains a well-formed SSE message with `event: datastar-patch-signals` and properly formatted data lines.
3. **Given** a Frank.Datastar resource handler, **When** the handler calls `removeElement` with a CSS selector, **Then** the response body contains a well-formed SSE message that removes the targeted element.
4. **Given** a Frank.Datastar resource handler, **When** the handler calls `executeScript` with a JavaScript string, **Then** the response body contains a well-formed SSE message that delivers the script for execution.
5. **Given** a Frank.Datastar resource handler, **When** the handler calls `tryReadSignals` on a GET request with a `datastar` query parameter, **Then** the incoming JSON signals are deserialized correctly.
6. **Given** a Frank.Datastar resource handler, **When** the handler calls `tryReadSignals` on a POST request with a JSON body, **Then** the incoming signals are deserialized correctly from the request body.

---

### User Story 2 - Preserve Existing Frank.Datastar Public API (Priority: P1)

As an existing Frank.Datastar user, I want the library's public API to remain unchanged so that I do not need to modify any application code when upgrading.

**Why this priority**: API compatibility is equally critical to the core implementation. Breaking the public API would impose migration costs on every consumer and undermine trust in the library's stability.

**Independent Test**: Can be tested by compiling existing Frank.Datastar sample projects (Basic, Hox, Oxpecker) and test suite against the new implementation without any source code changes. Delivers seamless upgrade experience.

**Acceptance Scenarios**:

1. **Given** the existing Frank.Datastar.Basic sample project, **When** it is compiled against the new Frank.Datastar implementation, **Then** compilation succeeds without any source changes.
2. **Given** the existing Frank.Datastar.Hox sample project, **When** it is compiled against the new Frank.Datastar implementation, **Then** compilation succeeds without any source changes.
3. **Given** the existing Frank.Datastar.Oxpecker sample project, **When** it is compiled against the new Frank.Datastar implementation, **Then** compilation succeeds without any source changes.
4. **Given** the existing Frank.Datastar.Tests test project, **When** all tests are run against the new implementation (after updating namespace imports to `Frank.Datastar`), **Then** all existing test assertions pass without modification to test logic.

---

### User Story 3 - Efficient Resource Usage via Direct Buffer Writing (Priority: P2)

As a developer running Frank.Datastar at scale, I want the SSE implementation to write directly to the response buffer using zero-copy techniques so that memory allocations per event are minimized and throughput is maximized.

**Why this priority**: Performance is the secondary driver for this change. The current StarFederation.Datastar implementation already uses `IBufferWriter<byte>` and inline functions, so the new implementation must match or exceed that baseline. This story validates the performance characteristics of the replacement.

**Independent Test**: Can be tested by benchmarking the new implementation against the existing one using a standardized workload (e.g., 10,000 events sent to a mock response stream) and comparing allocation counts and throughput. Delivers measurable performance validation.

**Acceptance Scenarios**:

1. **Given** the new SSE implementation, **When** writing event type prefixes and data line prefixes, **Then** pre-allocated byte arrays are used instead of runtime string-to-byte conversions.
2. **Given** the new SSE implementation, **When** splitting multi-line HTML or JSON payloads, **Then** zero-allocation string segmentation is used.
3. **Given** the new SSE implementation, **When** writing to the response, **Then** it writes directly to the response buffer without creating intermediate string or byte array copies.

---

### User Story 4 - .NET 10 Only Target for Frank.Datastar (Priority: P2)

As the Frank.Datastar library maintainer, I want Frank.Datastar to target only .NET 10 so that it can use modern .NET APIs without conditional compilation or polyfills, keeping the codebase simple.

**Why this priority**: Simplifying the target framework reduces maintenance burden and testing matrix. Since Frank.Datastar is a separate package from Frank core (which continues multi-targeting), consumers on older frameworks can continue using Frank core directly with StarFederation.Datastar if needed.

**Independent Test**: Can be tested by verifying the project file targets net10.0 only and builds successfully. Delivers a simpler build and dependency graph.

**Acceptance Scenarios**:

1. **Given** the Frank.Datastar project file, **When** the target framework is inspected, **Then** it targets only `net10.0`.
2. **Given** a consumer project targeting .NET 10, **When** it references Frank.Datastar, **Then** it resolves all dependencies without requiring StarFederation.Datastar.FSharp.
3. **Given** the Frank core library, **When** inspected, **Then** it continues to multi-target net8.0/net9.0/net10.0 (unchanged).

---

### Edge Cases

- What happens when the client disconnects mid-stream? The implementation must respect `HttpContext.RequestAborted` cancellation tokens and stop writing without throwing unhandled exceptions.
- What happens when an empty string is passed to `patchElements` or `patchSignals`? The implementation should send a valid SSE event with no data lines (matching current behavior).
- What happens when multi-line HTML contains `\r\n` (Windows-style) line endings? The line splitter must handle both `\n` and `\r\n` correctly.
- What happens when `tryReadSignals` receives an empty request body or missing query parameter? It should return `ValueNone` without throwing.
- What happens when the SSE stream is started multiple times within a single request? Only the first call should set response headers and flush; subsequent calls should be no-ops.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Frank.Datastar MUST implement both Datastar SSE event types (`datastar-patch-elements` and `datastar-patch-signals`) and the higher-level operations built on them: PatchElements (DOM patching including element removal via `mode: remove`), PatchSignals (signal store updates), ExecuteScript (script injection via `datastar-patch-elements` with a `<script>` tag appended to `body`), and ReadSignals (incoming signal parsing).
- **FR-002**: Frank.Datastar MUST conform to the Datastar SDK ADR specification for SSE message format, including `event:`, `id:`, `retry:`, and `data:` field ordering and line termination.
- **FR-003**: Frank.Datastar MUST set the response headers `Content-Type: text/event-stream`, `Cache-Control: no-cache`, and (for HTTP/1.1) `Connection: keep-alive` when starting an SSE stream.
- **FR-004**: Frank.Datastar MUST preserve the existing public API surface: the `datastar` custom operation on `ResourceBuilder`, and all functions in the `Datastar` module (`patchElements`, `patchElementsWithOptions`, `patchSignals`, `patchSignalsWithOptions`, `tryReadSignals`, `tryReadSignalsWithOptions`, `removeElement`, `removeElementWithOptions`, `executeScript`, `executeScriptWithOptions`).
- **FR-014**: Frank.Datastar MUST expose a public SSE generator (equivalent to the ADR's `ServerSentEventGenerator`) providing `PatchElements`, `PatchSignals`, `ExecuteScript`, and `ReadSignals` operations, along with SSE stream initialization. This allows advanced users to build custom Datastar operations outside of the `Datastar` module helpers.
- **FR-005**: Frank.Datastar MUST read incoming signals from the `datastar` query parameter on GET requests and from the request body on other HTTP methods.
- **FR-006**: Frank.Datastar MUST handle multi-line payloads (HTML, JSON, script) by splitting on newline characters and emitting each non-empty line as a separate `data:` field with the appropriate prefix.
- **FR-007**: Frank.Datastar MUST write SSE output using direct buffer writing techniques to minimize memory allocations.
- **FR-008**: Frank.Datastar MUST target .NET 10 only (`net10.0`).
- **FR-009**: Frank.Datastar MUST NOT depend on the StarFederation.Datastar.FSharp NuGet package.
- **FR-010**: Frank.Datastar MUST ensure that SSE stream initialization (header setting and initial flush) occurs exactly once per request, even if the handler calls multiple event-sending functions.
- **FR-011**: Frank.Datastar MUST respect cancellation tokens (primarily `HttpContext.RequestAborted`) and gracefully stop writing when the client disconnects.
- **FR-012**: Frank.Datastar MUST implement all option types as value types with these fields (all types also carry shared `EventId` and `Retry` fields per the ADR): `PatchElementsOptions` (`Selector`, `PatchMode`/`ElementPatchMode`, `UseViewTransition`, `Namespace`), `PatchSignalsOptions` (`OnlyIfMissing`), `RemoveElementOptions` (`UseViewTransition`), `ExecuteScriptOptions` (`AutoRemove`, `Attributes` as `string[]` of pre-formed attribute strings written verbatim). Each type MUST provide a static `Defaults` with ADR-specified default values.
- **FR-013**: Frank.Datastar MUST flush the response buffer after each event to ensure immediate delivery to the client.

### Key Entities

- **SSE Event**: A single server-sent event consisting of an event type, optional event ID, optional retry duration, and one or more data lines. Written directly to the response buffer in Datastar SDK ADR format.
- **Datastar Options**: Configuration types (PatchElementsOptions, PatchSignalsOptions, RemoveElementOptions, ExecuteScriptOptions) controlling event behavior such as patch mode, selector targeting, and view transitions.
- **Signals**: JSON-encoded client state sent from the browser to the server, read from query parameters (GET) or request body (POST/PUT/etc.) and deserialized into user-defined types.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All existing Frank.Datastar tests pass without modification against the new implementation.
- **SC-002**: All three sample projects (Basic, Hox, Oxpecker) compile and function correctly against the new implementation without source changes.
- **SC-003**: The Frank.Datastar package has zero external NuGet dependencies beyond the framework reference and the Frank core project reference.
- **SC-004**: SSE output for each event type matches the Datastar SDK ADR specification format when verified against reference test vectors.
- **SC-005**: The new implementation allocates no more memory per event than the existing StarFederation.Datastar.FSharp implementation, as measured by allocation profiling on a standardized workload.
- **SC-006**: Response headers and initial flush occur exactly once per SSE stream, regardless of how many events are sent.

## Clarifications

### Session 2026-02-07

- Q: Should we add the `Attributes` field to `ExecuteScriptOptions` for full ADR compliance? → A: Yes, add `Attributes` (`string[]`, default empty array) to `ExecuteScriptOptions`. This is an additive change; existing code compiles unchanged.
- Q: Should the new implementation expose a low-level SSE generator as public API? → A: Yes, expose the generator as public API for full ADR compliance. Advanced users can construct custom SSE events directly.
- Q: What type should `ExecuteScriptOptions.Attributes` use? → A: `string array` (`string[]`), matching the ADR's `[]string` semantics. Contiguous, storable in struct fields, iterated once for write-only output. Default: empty array.
- Q: Should the library HTML-encode attribute strings or write them verbatim? → A: Write verbatim. Each string is a complete, pre-formed attribute written as-is into the `<script>` tag. Caller is responsible for safe content and formatting.

## Assumptions

- Frank core will continue to multi-target net8.0/net9.0/net10.0; only Frank.Datastar changes to net10.0-only.
- Consumers who need .NET 8 or .NET 9 support can use Frank core directly with the StarFederation.Datastar.FSharp package as before.
- The existing `ResourceBuilder.AddHandler` mechanism in Frank core is sufficient for the `datastar` custom operation; no changes to Frank core are required.
- The Datastar SDK ADR specification is the authoritative reference for SSE message format, and the current StarFederation.Datastar.FSharp implementation faithfully implements it.
- The option types (PatchElementsOptions, etc.) will be re-implemented within Frank.Datastar rather than imported from the external dependency, maintaining identical field names and default values.
