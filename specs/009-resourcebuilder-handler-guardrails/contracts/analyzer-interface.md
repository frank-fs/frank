# Analyzer Interface Contract

**Date**: 2026-02-03
**Feature**: 009-resourcebuilder-handler-guardrails

## Overview

This feature is a compile-time analyzer with no runtime API. This document describes the interface contract between the analyzer and the FSharp.Analyzers.SDK framework.

---

## Analyzer Functions

### Editor Analyzer

```fsharp
[<EditorAnalyzer "DuplicateHandlerAnalyzer">]
val editorAnalyzer : EditorContext -> Async<Diagnostic list>
```

**Input**: `EditorContext` provided by FSAC
- `FileName: string` - Path to the file being analyzed
- `ParseTree: ParsedInput` - Untyped AST
- `CheckFileResults: FSharpCheckFileResults option` - Typed AST (optional)

**Output**: `Async<Diagnostic list>` - Diagnostics found in the file

### CLI Analyzer

```fsharp
[<CliAnalyzer "DuplicateHandlerAnalyzer">]
val cliAnalyzer : CliContext -> Diagnostic list
```

**Input**: `CliContext` provided by fsharp-analyzers CLI
- `FileName: string` - Path to the file being analyzed
- `ParseTree: ParsedInput` - Untyped AST
- `CheckFileResults: FSharpCheckFileResults` - Typed AST (always present)

**Output**: `Diagnostic list` - Diagnostics found in the file

---

## Diagnostic Contract

### Structure

```fsharp
type Diagnostic = {
    Type: string
    Message: string
    Code: string
    Severity: Severity
    Range: Range
    Fixes: CodeFix list
}
```

### FRANK001: Duplicate HTTP Handler

| Field | Value |
|-------|-------|
| Type | `"Duplicate HTTP handler"` |
| Message | `"HTTP method '{METHOD}' handler is already defined for this resource at line {LINE}. Only one handler per HTTP method is allowed."` |
| Code | `"FRANK001"` |
| Severity | `Warning` |
| Range | Location of the duplicate (second occurrence) |
| Fixes | `[]` (empty for MVP) |

**Message Variables**:
- `{METHOD}`: The HTTP method name (GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS, CONNECT, TRACE)
- `{LINE}`: Line number of the first registration

---

## Detection Contract

### Input Patterns Detected

The analyzer detects these patterns in the untyped AST:

1. **Direct method registration**:
   ```fsharp
   resource "/path" {
       get handler
       get handler  // Detected
   }
   ```

2. **Datastar conflicts**:
   ```fsharp
   resource "/path" {
       datastar handler  // Registers GET by default
       get handler       // Detected as duplicate GET
   }
   ```

### Output Guarantees

- **No false positives**: Valid code with one handler per method produces no diagnostics
- **Complete coverage**: All 9 HTTP methods are covered
- **Per-resource isolation**: Each `resource` block is analyzed independently

---

## Version Compatibility

| Frank.Analyzers | FSharp.Analyzers.SDK | .NET |
|-----------------|----------------------|------|
| 1.x | 0.35.x | 8.0+ |

---

## Error Codes Reserved

| Code | Description | Status |
|------|-------------|--------|
| FRANK001 | Duplicate HTTP handler | Implemented |
| FRANK002-FRANK099 | Reserved for future Frank analyzers | Reserved |
