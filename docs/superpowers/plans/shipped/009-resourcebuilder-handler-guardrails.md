---
source: specs/009-resourcebuilder-handler-guardrails
type: plan
---

# Implementation Plan: ResourceBuilder Handler Guardrails

**Branch**: `009-resourcebuilder-handler-guardrails` | **Date**: 2026-02-03 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/009-resourcebuilder-handler-guardrails/spec.md`

## Summary

Create an F# Analyzer that detects duplicate HTTP method handler registrations in Frank's `resource` computation expression at compile-time. The analyzer will be distributed as a separate NuGet package (`Frank.Analyzers`) and integrate with both IDE tooling (Ionide, Visual Studio, Rider) and the `fsharp-analyzers` CLI tool for CI/CD usage.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank)
**Primary Dependencies**: FSharp.Analyzers.SDK (0.35.0+), FSharp.Compiler.Service (bundled with SDK)
**Storage**: N/A (static analysis tool, no persistence)
**Testing**: Shell script with test fixtures; CLI-based verification via `fsharp-analyzers` tool
**Target Platform**: Cross-platform (.NET runtime); IDE integration via FSAC protocol
**Project Type**: Single library project with accompanying test fixtures
**Performance Goals**: Analysis completes within IDE refresh cycle (<2 seconds per file)
**Constraints**: Must not add runtime dependencies to Frank core; warning severity (not error)
**Scale/Scope**: Analyze computation expression blocks; typically <100 resource definitions per project

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | Enhances resource definition experience; reinforces one-handler-per-method semantics |
| II. Idiomatic F# | PASS | F# Analyzers are standard F# ecosystem tooling |
| III. Library, Not Framework | PASS | Separate optional package; users opt-in explicitly |
| IV. ASP.NET Core Native | PASS | No runtime changes; analyzer is development-time tooling only |
| V. Performance Parity | PASS | No runtime overhead; analyzer runs at compile-time only |

**Technical Standards Check:**
- Target Framework: .NET 8.0+ (matches Frank) - PASS
- F# Version: F# 8.0+ - PASS
- Dependencies: Single dependency (FSharp.Analyzers.SDK) - PASS
- Testing: CLI-based test script with fixtures - PASS

## Project Structure

### Documentation (this feature)

```text
specs/009-resourcebuilder-handler-guardrails/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── Frank/                           # Existing - no changes
│   ├── Builder.fs
│   └── Frank.fsproj
├── Frank.Datastar/                  # Existing - no changes
│   ├── Frank.Datastar.fs
│   └── Frank.Datastar.fsproj
└── Frank.Analyzers/                 # NEW - analyzer project
    ├── DuplicateHandlerAnalyzer.fs  # Core analyzer logic
    └── Frank.Analyzers.fsproj       # Project file with SDK reference

test/
├── Frank.Datastar.Tests/            # Existing - no changes
└── Frank.Analyzers.Tests/           # NEW - test fixtures and script
    ├── fixtures/                    # F# files with various scenarios
    │   ├── DuplicateGet.fs          # Duplicate GET handler (should warn)
    │   ├── DuplicatePost.fs         # Duplicate POST handler (should warn)
    │   ├── ValidSingleHandlers.fs   # Valid resource (no warnings)
    │   ├── MultipleResources.fs     # Multiple resources, each valid
    │   ├── DatastarConflict.fs      # datastar + explicit get (should warn)
    │   └── AllMethodsOnce.fs        # One of each method (no warnings)
    ├── run-analyzer-tests.sh        # Test script that runs CLI and verifies output
    └── Frank.Analyzers.Tests.fsproj # Minimal project to compile fixtures
```

**Structure Decision**: Single new project (`Frank.Analyzers`) following the existing `src/` pattern with `Frank.Datastar` as a sibling. Test fixtures in `test/Frank.Analyzers.Tests/` with shell script for CLI-based verification.

## Complexity Tracking

No violations to justify. The implementation follows existing Frank patterns and adds minimal complexity:
- One new project (analyzer)
- One new test directory (fixtures + script)
- No changes to existing code
