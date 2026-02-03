# Feature Specification: ResourceBuilder Handler Guardrails

**Feature Branch**: `009-resourcebuilder-handler-guardrails`
**Created**: 2026-02-03
**Status**: Draft
**Input**: User description: "Add guardrails to the Frank ResourceBuilder such that only one handler may be registered per HTTP method, e.g. one call to set the Get handler. Consider using tools such as FSharp.Analyzers.SDK if appropriate. This request addresses https://github.com/frank-fs/frank/issues/59."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Compile-Time Duplicate Handler Detection (Priority: P1)

A developer accidentally registers multiple GET handlers for the same resource:

```fsharp
resource "/contacts/{id}" {
    name "Contact"
    get (fun ctx -> task { ... })
    get (fun ctx -> task { ... })  // Duplicate - should be detected
}
```

The developer receives immediate feedback in their IDE (via F# Analyzer) that a duplicate handler has been registered, preventing the mistake before runtime.

**Why this priority**: This is the core value proposition. Detecting errors at compile-time provides superior developer experience compared to runtime failures, as stated in the linked GitHub issue. Early detection prevents bugs from reaching production.

**Independent Test**: Can be fully tested via CLI test script with fixture files containing duplicate handlers, verifying the analyzer produces warnings. Delivers immediate value by catching the most common mistake.

**Acceptance Scenarios**:

1. **Given** a resource builder with a single GET handler, **When** the developer adds a second GET handler in the same resource block, **Then** the F# Analyzer reports a warning at the location of the duplicate handler.

2. **Given** a resource builder with handlers for GET and POST, **When** the developer adds a second POST handler, **Then** the F# Analyzer reports a warning only for the duplicate POST handler (not the existing GET handler).

3. **Given** a resource builder with handlers for different HTTP methods (GET, POST, PUT, DELETE), **When** the code is analyzed, **Then** no warnings or errors are reported because each method appears only once.

---

### User Story 2 - Clear Diagnostic Messages (Priority: P2)

When a duplicate handler is detected, the developer sees a helpful diagnostic message that explains the problem and suggests a fix.

**Why this priority**: Detection alone is not enough; developers need actionable guidance. Clear messages reduce debugging time and improve the overall developer experience.

**Independent Test**: Can be tested by triggering the analyzer on duplicate handler code and verifying the diagnostic message includes: the HTTP method that was duplicated, the line/position of both occurrences, and guidance that only one handler per method is allowed.

**Acceptance Scenarios**:

1. **Given** a resource builder with duplicate GET handlers, **When** the analyzer reports the issue, **Then** the diagnostic message identifies "GET" as the duplicated method and indicates which line contains the duplicate.

2. **Given** a resource builder with duplicate handlers, **When** the diagnostic is displayed in an IDE (VS Code, Visual Studio, Rider), **Then** the duplicate handler line is underlined or highlighted as a warning.

---

### User Story 3 - All HTTP Methods Covered (Priority: P2)

The guardrail applies to all nine HTTP methods supported by ResourceBuilder: GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, CONNECT, and TRACE.

**Why this priority**: Consistency is important for developer trust. Partial coverage would create confusion about which methods are protected and which are not.

**Independent Test**: Can be tested by creating resources with duplicate handlers for each of the 9 HTTP methods and verifying the analyzer detects duplicates for all of them.

**Acceptance Scenarios**:

1. **Given** a resource builder, **When** duplicate handlers are added for any of the 9 supported HTTP methods (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, CONNECT, TRACE), **Then** the analyzer detects and reports the duplication.

2. **Given** a resource builder with one handler each for all 9 HTTP methods, **When** the code is analyzed, **Then** no warnings or errors are reported.

---

### User Story 4 - Datastar Extension Compatibility (Priority: P3)

The guardrails work correctly with the Frank.Datastar extension's `datastar` custom operation, which internally registers handlers for HTTP methods.

**Why this priority**: The Datastar extension is an active part of the Frank ecosystem. Users combining standard handlers with datastar operations should receive the same protections.

**Independent Test**: Can be tested by creating a resource that uses both `datastar` (which registers GET by default) and an explicit `get` handler, and verifying the analyzer detects this as a duplicate.

**Acceptance Scenarios**:

1. **Given** a resource builder using `datastar` (which defaults to GET), **When** the developer also adds an explicit `get` handler, **Then** the analyzer reports a duplicate GET handler.

2. **Given** a resource builder using `datastar HttpMethods.Post`, **When** the developer also adds an explicit `post` handler, **Then** the analyzer reports a duplicate POST handler.

3. **Given** a resource builder using `datastar HttpMethods.Get` and a separate `post` handler, **When** the code is analyzed, **Then** no warnings are reported (different methods).

---

### User Story 5 - CLI and CI/CD Support (Priority: P2)

The analyzer can be run from the command line using the `fsharp-analyzers` dotnet tool, enabling integration into CI/CD pipelines and automated build processes.

**Why this priority**: CI/CD enforcement ensures duplicate handler issues are caught even if a developer doesn't have analyzer support enabled in their IDE. This provides a safety net for the entire team.

**Independent Test**: Can be tested via a shell script that runs the `fsharp-analyzers` CLI tool against test fixture files containing duplicate handlers and verifies the expected warnings are produced.

**Acceptance Scenarios**:

1. **Given** a project with duplicate GET handlers, **When** the `fsharp-analyzers` CLI tool is run against the project, **Then** the tool outputs a warning message identifying the duplicate and returns a non-zero exit code (or configurable behavior).

2. **Given** a project with no duplicate handlers, **When** the `fsharp-analyzers` CLI tool is run against the project, **Then** the tool produces no warnings related to duplicate handlers.

3. **Given** a CI/CD pipeline configured to run `fsharp-analyzers`, **When** a developer pushes code with duplicate handlers, **Then** the pipeline fails or warns based on configuration.

---

### Edge Cases

- What happens when handlers are registered in separate `Combine` operations (e.g., conditional handler registration)? The analyzer should detect duplicates across the entire resource block, regardless of how the computation expression is structured.
- How does the analyzer handle handlers registered via helper functions that call the HTTP method operations? The analyzer operates on the AST and should detect calls to the custom operations regardless of the call site.
- What if a handler is registered inside a conditional expression (`if/else`)? If both branches register the same HTTP method, this should be flagged as a potential duplicate (warning level, since it may be intentional).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST detect when multiple handlers are registered for the same HTTP method within a single `resource` computation expression.
- **FR-002**: System MUST report duplicate handler registrations as compiler warnings via an F# Analyzer (developers may promote to errors via standard compiler settings).
- **FR-003**: System MUST support detection for all 9 HTTP methods: GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, CONNECT, and TRACE.
- **FR-004**: System MUST provide diagnostic messages that identify the duplicated HTTP method and the location(s) of the duplicate registrations.
- **FR-005**: System MUST integrate with standard F# development tooling (VS Code with Ionide, Visual Studio, JetBrains Rider) for inline warning display, and with the `fsharp-analyzers` CLI tool for command-line and CI/CD usage.
- **FR-006**: System MUST detect duplicates introduced by the `datastar` custom operation (which registers handlers for HTTP methods).
- **FR-007**: System MUST NOT report false positives for resources that have exactly one handler per HTTP method.
- **FR-008**: System MUST operate at compile-time (via FSharp.Analyzers.SDK) rather than runtime.

### Key Entities

- **ResourceBuilder**: The computation expression builder type that constructs HTTP resources. Contains custom operations for each HTTP method.
- **F# Analyzer**: A compile-time code analysis component that examines the AST of resource builder expressions to detect duplicate handler registrations.
- **Diagnostic**: A warning message produced by the analyzer, containing the HTTP method name, location, and remediation guidance.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 9 HTTP methods are covered by duplicate detection with 100% consistency.
- **SC-002**: Developers see duplicate handler warnings in their IDE within the standard code analysis refresh cycle (typically under 2 seconds after code change).
- **SC-003**: Zero false positives when resources have exactly one handler per HTTP method.
- **SC-004**: Diagnostic messages include the HTTP method name and line location, enabling developers to identify and fix issues without additional investigation.
- **SC-005**: The solution integrates with standard F# development environments without requiring additional configuration beyond enabling the analyzer.

## Testing Approach

Analyzer functionality will be verified using a CLI-based test script rather than IDE-based testing:

1. **Test fixtures**: A set of `.fs` files containing various scenarios (duplicate handlers, valid single handlers, edge cases with conditionals, datastar combinations)
2. **Test script**: A shell script that:
   - Runs `dotnet fsharp-analyzers` against each test fixture
   - Parses the CLI output for expected warnings
   - Verifies correct warnings are produced for invalid code
   - Verifies no false positives for valid code
   - Reports pass/fail status with clear output
3. **CI integration**: The test script runs as part of the standard CI pipeline

This approach is preferred over IDE-based testing because:
- Fully automatable and reproducible
- No IDE dependencies in the test environment
- Clear pass/fail semantics for CI/CD
- Easier to maintain and extend

## Clarifications

### Session 2026-02-03

- Q: Implementation approach - analyzer vs runtime validation vs both? → A: F# Analyzer (FSharp.Analyzers.SDK) for compile-time detection only. Current runtime behavior (last handler wins/overwrites) is acceptable fallback for users without analyzer support.
- Q: Diagnostic severity level (warning vs error)? → A: Warning. Code compiles; developers can promote to errors via `<TreatWarningsAsErrors>` if desired.
- Q: Analyzer packaging/distribution? → A: Separate NuGet package (`Frank.Analyzers`). Users opt-in explicitly; core Frank package remains lightweight.
- Q: CLI/CI support and testing approach? → A: Yes, support `fsharp-analyzers` CLI tool. Test via shell script against fixture files rather than IDE-based testing.

## Assumptions

- Developers using Frank are familiar with F# Analyzers and have analyzer support enabled in their development environment.
- The FSharp.Analyzers.SDK provides sufficient AST access to detect computation expression custom operation calls within resource builder blocks.
- Runtime validation is out of scope for this feature; the focus is compile-time detection via analyzers as recommended in the GitHub issue discussion.
- The analyzer will be distributed as a separate NuGet package (`Frank.Analyzers`) that users opt-in to explicitly.
- The current ResourceBuilder runtime behavior (last registered handler overwrites previous) remains unchanged and serves as an acceptable fallback for developers without analyzer support.
