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
