# Feature Specification: Add WithOptions Variants for Datastar Helper Functions

**Feature Branch**: `010-datastar-patch-mode`
**Created**: 2026-02-03
**Status**: Draft
**Input**: User description: "The Datastar module exposes only a patchElements with the default mode embedded in the call to the StarFederation.Datastar library. Add a patchElementsWithMode and expose the `mode` parameter. Use the same DU from StarFederation.Datastar to specify the mode. The function should be inline and work similarly to the existing Datastar.patchElements function."

## Clarifications

### Session 2026-02-03

- Q: API design approach for exposing additional options? → A: Approach B - Keep simple helpers for common case, add single `WithOptions` variant per function taking full options record. Avoids combinatorial explosion of permutation functions.
- Q: Scope of WithOptions variants? → A: Apply pattern to all helper functions with options overloads: patchElements, patchSignals, removeElement, executeScript, tryReadSignals.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Use Custom Options with Any Datastar Helper (Priority: P1)

As a developer using Frank.Datastar, I want to use custom options with any Datastar helper function so that I can control the full range of SSE event parameters without being limited to defaults.

**Why this priority**: This is the core feature request. Each helper currently only supports defaults, limiting developers who need fine-grained control over SSE events.

**Independent Test**: Can be fully tested by calling each `WithOptions` variant with various option combinations and verifying the correct SSE events are generated.

**Acceptance Scenarios**:

1. **Given** an active Datastar SSE stream, **When** I call `Datastar.patchElementsWithOptions` with custom `PatchElementsOptions`, **Then** the SSE event reflects all specified options.
2. **Given** an active Datastar SSE stream, **When** I call `Datastar.patchSignalsWithOptions` with `{ PatchSignalsOptions.Defaults with OnlyIfMissing = true }`, **Then** the SSE event includes `onlyIfMissing true`.
3. **Given** an active Datastar SSE stream, **When** I call `Datastar.removeElementWithOptions` with `{ RemoveElementOptions.Defaults with UseViewTransition = true }`, **Then** the SSE event includes `useViewTransition true`.
4. **Given** an active Datastar SSE stream, **When** I call `Datastar.executeScriptWithOptions` with `{ ExecuteScriptOptions.Defaults with AutoRemove = false }`, **Then** the script tag does not include the auto-remove data attribute.
5. **Given** an HTTP request with signals, **When** I call `Datastar.tryReadSignalsWithOptions` with custom `JsonSerializerOptions`, **Then** deserialization uses the provided options.

---

### User Story 2 - Maintain Existing API Compatibility (Priority: P2)

As a developer with existing Frank.Datastar code, I want all existing helper functions to continue working unchanged so that my current codebase requires no modifications.

**Why this priority**: Backward compatibility ensures existing users are not impacted. The new functions are additive.

**Independent Test**: Can be tested by running existing helper calls and verifying they produce the same output as before.

**Acceptance Scenarios**:

1. **Given** existing code using `Datastar.patchElements`, **When** the library is updated, **Then** existing code compiles and runs without modification.
2. **Given** existing code using `Datastar.patchSignals`, **When** the library is updated, **Then** existing code compiles and runs without modification.
3. **Given** existing code using `Datastar.removeElement`, **When** the library is updated, **Then** existing code compiles and runs without modification.
4. **Given** existing code using `Datastar.executeScript`, **When** the library is updated, **Then** existing code compiles and runs without modification.
5. **Given** existing code using `Datastar.tryReadSignals`, **When** the library is updated, **Then** existing code compiles and runs without modification.

---

### Edge Cases

- What happens when any `WithOptions` function is called with `*.Defaults`?
  - The result should be equivalent to calling the simple helper.
- What happens when multiple non-default options are combined?
  - All specified options should be included in the SSE event data lines.
- What happens when empty strings are passed to helpers?
  - Functions should still send SSE events with specified options (consistent with existing behavior).

## Requirements *(mandatory)*

### Functional Requirements

#### patchElementsWithOptions
- **FR-001**: The Datastar module MUST expose `patchElementsWithOptions` that accepts `PatchElementsOptions`, HTML string, and HttpContext.
- **FR-002**: The function MUST be marked `inline` and delegate to `ServerSentEventGenerator.PatchElementsAsync` with the provided options.

#### patchSignalsWithOptions
- **FR-003**: The Datastar module MUST expose `patchSignalsWithOptions` that accepts `PatchSignalsOptions`, signals string, and HttpContext.
- **FR-004**: The function MUST be marked `inline` and delegate to `ServerSentEventGenerator.PatchSignalsAsync` with the provided options.

#### removeElementWithOptions
- **FR-005**: The Datastar module MUST expose `removeElementWithOptions` that accepts `RemoveElementOptions`, selector string, and HttpContext.
- **FR-006**: The function MUST be marked `inline` and delegate to `ServerSentEventGenerator.RemoveElementAsync` with the provided options.

#### executeScriptWithOptions
- **FR-007**: The Datastar module MUST expose `executeScriptWithOptions` that accepts `ExecuteScriptOptions`, script string, and HttpContext.
- **FR-008**: The function MUST be marked `inline` and delegate to `ServerSentEventGenerator.ExecuteScriptAsync` with the provided options.

#### tryReadSignalsWithOptions
- **FR-009**: The Datastar module MUST expose `tryReadSignalsWithOptions<'T>` that accepts `JsonSerializerOptions` and HttpContext.
- **FR-010**: The function MUST be marked `inline` and delegate to `ServerSentEventGenerator.ReadSignalsAsync<'T>` with the provided options.

#### General
- **FR-011**: All existing simple helper functions MUST remain unchanged and continue to use their respective `*.Defaults`.
- **FR-012**: All `WithOptions` function signatures MUST follow the pattern: `(options) -> (data) -> (ctx: HttpContext) -> Task` (or `Task<voption<'T>>` for tryReadSignals).

### Constraints & Tradeoffs

- **Rejected Alternative**: Adding individual functions per option (e.g., `patchElementsWithMode`, `patchSignalsOnlyIfMissing`) was rejected to avoid combinatorial explosion. Users needing specific options use `{ *.Defaults with ... }` syntax.
- **Design Principle**: Simple case stays simple (existing helpers), full power available when needed (`WithOptions` variants).

### Key Entities

- **PatchElementsOptions**: Options for patching elements:
  - `Selector: Selector voption` - CSS selector targeting specific element(s)
  - `PatchMode: ElementPatchMode` - How HTML is merged (default: Outer)
  - `UseViewTransition: bool` - Whether to use view transitions (default: false)
  - `Namespace: PatchElementNamespace` - HTML/SVG/MathML namespace (default: Html)
  - `EventId: string voption` - Optional SSE event ID
  - `Retry: TimeSpan` - SSE retry duration (default: 1 second)

- **PatchSignalsOptions**: Options for patching signals:
  - `OnlyIfMissing: bool` - Only patch if signal doesn't exist (default: false)
  - `EventId: string voption` - Optional SSE event ID
  - `Retry: TimeSpan` - SSE retry duration (default: 1 second)

- **RemoveElementOptions**: Options for removing elements:
  - `UseViewTransition: bool` - Whether to use view transitions (default: false)
  - `EventId: string voption` - Optional SSE event ID
  - `Retry: TimeSpan` - SSE retry duration (default: 1 second)

- **ExecuteScriptOptions**: Options for executing scripts:
  - `AutoRemove: bool` - Auto-remove script after execution (default: true)
  - `Attributes: KeyValuePair<string, string> list` - Additional script tag attributes
  - `EventId: string voption` - Optional SSE event ID
  - `Retry: TimeSpan` - SSE retry duration (default: 1 second)

- **ElementPatchMode**: DU with 8 variants: Outer, Inner, Remove, Replace, Prepend, Append, Before, After

- **PatchElementNamespace**: DU with 3 variants: Html, Svg, MathMl

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Developers can specify any combination of supported options for each helper using `{ *.Defaults with ... }` syntax.
- **SC-002**: All existing code using simple helpers continues to work without modification.
- **SC-003**: All new functions follow the same usage patterns as existing Datastar helper functions.
- **SC-004**: All functions compile and work in all supported target frameworks (.NET 8.0, 9.0, 10.0).
- **SC-005**: Each `WithOptions` variant produces identical output to the simple helper when called with `*.Defaults`.

## Assumptions

- The `StarFederation.Datastar.FSharp` library exposes all options types (`PatchElementsOptions`, `PatchSignalsOptions`, `RemoveElementOptions`, `ExecuteScriptOptions`) publicly with `Defaults` static members.
- The `ServerSentEventGenerator` has overloads accepting each options type.
- `System.Text.Json.JsonSerializerOptions` is used for signal deserialization options.
