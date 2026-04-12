---
source: specs/009-resourcebuilder-handler-guardrails
status: complete
type: spec
---

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

## Research

# Research: ResourceBuilder Handler Guardrails

**Date**: 2026-02-03
**Feature**: 009-resourcebuilder-handler-guardrails

## Summary

This document consolidates research findings for implementing an F# Analyzer to detect duplicate HTTP method handler registrations in Frank's ResourceBuilder computation expression.

---

## 1. FSharp.Analyzers.SDK

### Decision
Use FSharp.Analyzers.SDK version 0.35.0+ as the analyzer framework.

### Rationale
- Industry-standard SDK for F# analyzers (maintained by Ionide team)
- Provides both editor (FSAC) and CLI integration out of the box
- Well-documented with active community support
- Used by production analyzers (ionide-analyzers, G-Research analyzers)
- Handles complex infrastructure (AST traversal, diagnostic publishing, IDE protocol)

### Alternatives Considered
- **Manual FCS integration**: Rejected - requires reimplementing SDK infrastructure
- **Roslyn analyzers**: Not applicable - F# uses different AST from C#
- **MSBuild task**: Rejected - less integrated IDE experience, no real-time feedback

---

## 2. AST Traversal Approach

### Decision
Use `SyntaxCollectorBase` with untyped AST traversal via `ASTCollecting.walkAst`.

### Rationale
- Computation expression custom operations are identifiable in untyped AST
- Faster than typed AST (no type-checking required)
- Sufficient for detecting duplicate identifier calls within CE blocks
- Visitor pattern simplifies implementation - override only needed methods

### Implementation Pattern
```fsharp
type DuplicateHandlerWalker() =
    inherit SyntaxCollectorBase()

    let diagnostics = ResizeArray<Diagnostic>()
    let mutable currentResourceOperations = Map.empty<string, Range>

    override __.WalkExpr(path, expr) =
        match expr with
        | SynExpr.App(_, _, funcExpr, _, range) ->
            // Check if this is a resource CE custom operation
            match tryGetHttpMethodName funcExpr with
            | Some methodName ->
                match currentResourceOperations.TryFind methodName with
                | Some firstRange ->
                    // Duplicate found - emit diagnostic
                    diagnostics.Add(createDuplicateDiagnostic methodName range firstRange)
                | None ->
                    currentResourceOperations <- currentResourceOperations.Add(methodName, range)
            | None -> ()
        | SynExpr.ComputationExpr(_, bodyExpr, range) ->
            // Reset tracking for new CE block
            let savedOps = currentResourceOperations
            currentResourceOperations <- Map.empty
            base.WalkExpr(path, bodyExpr)
            currentResourceOperations <- savedOps
        | _ -> ()
        base.WalkExpr(path, expr)

    member __.Diagnostics = diagnostics |> Seq.toList
```

### Alternatives Considered
- **Typed AST (`TypedTreeCollectorBase`)**: Rejected - more complex, slower, not needed for this use case
- **Pattern matching without walker**: Rejected - manual recursion is error-prone and harder to maintain

---

## 3. HTTP Method Detection

### Decision
Detect custom operation calls by identifier name matching known HTTP method operation names.

### HTTP Method Operations to Detect
From Frank's `ResourceBuilder` (Builder.fs lines 65-207):
- `get`, `post`, `put`, `delete`, `patch`
- `head`, `options`, `connect`, `trace`
- `datastar` (from Frank.Datastar extension)

### Detection Logic
```fsharp
let httpMethodOperations =
    Set.ofList ["get"; "post"; "put"; "delete"; "patch"; "head"; "options"; "connect"; "trace"]

let extensionOperations =
    Set.ofList ["datastar"]

let tryGetHttpMethodName (expr: SynExpr) : string option =
    match expr with
    | SynExpr.Ident(ident) when httpMethodOperations.Contains(ident.idText.ToLowerInvariant()) ->
        Some (ident.idText.ToUpperInvariant())
    | SynExpr.Ident(ident) when ident.idText = "datastar" ->
        // datastar defaults to GET, but can specify method as first arg
        Some "DATASTAR" // Special handling needed
    | _ -> None
```

### Datastar Special Handling
The `datastar` operation can register different HTTP methods:
- `datastar (fun ctx -> ...)` - defaults to GET
- `datastar HttpMethods.Post (fun ctx -> ...)` - explicit POST

For MVP, treat `datastar` as registering an HTTP method that conflicts with explicit method handlers.

### Rationale
- Name-based detection is simple and reliable for CE custom operations
- Custom operations in F# CEs are transformed into method calls with predictable names
- Case-insensitive matching handles various coding styles

### Alternatives Considered
- **Symbol-based detection (typed AST)**: Rejected - overkill for this use case; would require type information
- **Attribute-based detection**: Rejected - would require runtime reflection or complex AST analysis

---

## 4. Diagnostic Format

### Decision
Use warning severity with clear message format including method name and location.

### Diagnostic Structure
```fsharp
let createDuplicateDiagnostic (methodName: string) (duplicateRange: Range) (firstRange: Range) =
    {
        Type = "Duplicate HTTP handler"
        Message = sprintf "HTTP method '%s' handler is already defined for this resource at line %d. Only one handler per HTTP method is allowed."
                          methodName firstRange.StartLine
        Code = "FRANK001"
        Severity = Warning
        Range = duplicateRange
        Fixes = []  // Could add "Remove duplicate" fix in future
    }
```

### Message Format
```
FRANK001: HTTP method 'GET' handler is already defined for this resource at line 15. Only one handler per HTTP method is allowed.
```

### Rationale
- Clear identification of the duplicated method
- Points to both the duplicate and original location
- Actionable guidance ("only one handler per HTTP method is allowed")
- Unique code (FRANK001) for filtering/searching

### Alternatives Considered
- **Error severity**: Rejected - per clarification, warnings allow compilation and user can promote via compiler settings
- **Multiple diagnostics (one per occurrence)**: Rejected - creates noise; single diagnostic on duplicate is cleaner

---

## 5. Analyzer Registration

### Decision
Implement dual analyzer pattern (both `[<EditorAnalyzer>]` and `[<CliAnalyzer>]` attributes).

### Implementation Pattern
```fsharp
let analyzeFile (parseTree: ParsedInput) : Diagnostic list =
    let walker = DuplicateHandlerWalker()
    ASTCollecting.walkAst walker parseTree
    walker.Diagnostics

[<EditorAnalyzer "DuplicateHandlerAnalyzer">]
let editorAnalyzer (ctx: EditorContext) : Diagnostic list async =
    async {
        return analyzeFile ctx.ParseTree
    }

[<CliAnalyzer "DuplicateHandlerAnalyzer">]
let cliAnalyzer (ctx: CliContext) : Diagnostic list =
    analyzeFile ctx.ParseTree
```

### Rationale
- Shared core logic between editor and CLI modes
- EditorContext.CheckFileResults is optional; CliContext guarantees it
- Dual registration ensures analyzer works in all consumption scenarios

### Alternatives Considered
- **Editor-only**: Rejected - need CLI support for CI/CD per spec
- **CLI-only**: Rejected - real-time IDE feedback is core value proposition

---

## 6. Project Configuration

### Decision
Create `Frank.Analyzers.fsproj` as a standard F# library targeting .NET 8.0+.

### Project File Structure
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageId>Frank.Analyzers</PackageId>
    <Version>1.0.0</Version>
    <Description>F# Analyzers for Frank web framework - detects common issues at compile-time</Description>
    <PackageTags>fsharp;analyzer;frank;web</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="DuplicateHandlerAnalyzer.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="FSharp.Analyzers.SDK" Version="0.35.*" />
  </ItemGroup>

</Project>
```

### Rationale
- Multi-target matches Frank's target frameworks
- Minimal dependencies (only SDK)
- Standard NuGet package metadata for discoverability

---

## 7. Test Approach

### Decision
Shell script running `fsharp-analyzers` CLI against fixture files.

### Test Script Structure
```bash
#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
FIXTURES_DIR="$SCRIPT_DIR/fixtures"
ANALYZER_PATH="$SCRIPT_DIR/../../src/Frank.Analyzers/bin/Release"

# Build analyzer
dotnet build "$SCRIPT_DIR/../../src/Frank.Analyzers" -c Release

# Build fixtures project (required for analysis)
dotnet build "$SCRIPT_DIR" -c Release

run_test() {
    local fixture=$1
    local expect_warning=$2
    local method=$3

    output=$(dotnet fsharp-analyzers \
        --project "$SCRIPT_DIR/Frank.Analyzers.Tests.fsproj" \
        --analyzers-path "$ANALYZER_PATH" \
        --treat-as-warning FRANK001 \
        2>&1) || true

    if [[ "$expect_warning" == "true" ]]; then
        if echo "$output" | grep -q "FRANK001.*$method"; then
            echo "PASS: $fixture - Warning detected for $method"
        else
            echo "FAIL: $fixture - Expected warning for $method"
            exit 1
        fi
    else
        if echo "$output" | grep -q "FRANK001"; then
            echo "FAIL: $fixture - Unexpected warning"
            exit 1
        else
            echo "PASS: $fixture - No warnings (expected)"
        fi
    fi
}

# Run tests
run_test "DuplicateGet" true "GET"
run_test "DuplicatePost" true "POST"
run_test "ValidSingleHandlers" false ""
run_test "AllMethodsOnce" false ""

echo "All tests passed!"
```

### Rationale
- Automatable and reproducible
- No IDE dependencies
- Clear pass/fail output
- Runs in CI pipeline

---

## 8. Edge Cases Handling

### Decisions

| Edge Case | Handling |
|-----------|----------|
| Conditional handlers (`if/else`) | Report warning if same method in both branches - likely unintentional |
| Multiple resource blocks | Track per-block; reset when entering new `resource` CE |
| Nested CEs | Push/pop operation tracking stack |
| Helper functions | Out of scope for MVP - AST-level detection only |
| `datastar` + explicit method | Report as duplicate; `datastar` registers a method |

### Rationale
- MVP focuses on direct CE block analysis
- Complex flow analysis (helper functions) deferred to future version
- Conservative approach: warn on potential issues

---

## References

- [FSharp.Analyzers.SDK GitHub](https://github.com/ionide/FSharp.Analyzers.SDK)
- [FSharp.Analyzers.SDK Documentation](https://ionide.io/FSharp.Analyzers.SDK/)
- [ionide-analyzers Examples](https://github.com/ionide/ionide-analyzers)
- [G-Research F# Analyzers](https://github.com/G-Research/fsharp-analyzers)
- [F# Compiler Guide - SynExpr](https://fsharp.github.io/fsharp-compiler-docs/reference/fsharp-compiler-syntax-synexpr.html)
- [Frank Builder.fs](../../src/Frank/Builder.fs) - ResourceBuilder implementation
