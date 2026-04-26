---
source: specs/003-datastar-hox-sample
type: plan
---

# Implementation Plan: Frank.Datastar.Hox Sample Application

**Branch**: `003-datastar-hox-sample` | **Date**: 2026-01-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/003-datastar-hox-sample/spec.md`

## Summary

Replace the existing Frank.Datastar.Hox sample implementation with a new version that mirrors Frank.Datastar.Basic's RESTful patterns using Hox's CSS-selector-based DSL for HTML generation instead of F# string templates. The implementation must pass the same test.sh validation script.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0
**Primary Dependencies**: Frank 6.x, Hox 3.x, StarFederation.Datastar.FSharp (via Frank.Datastar project reference)
**Storage**: In-memory (Dictionary, ResizeArray) - demo purposes only
**Testing**: Manual test.sh script (curl-based HTTP tests)
**Target Platform**: .NET 10.0 web server (localhost:5000)
**Project Type**: Single project (sample application)
**Performance Goals**: N/A (sample application)
**Constraints**: Must pass all 18 tests in test.sh without modification
**Scale/Scope**: Demo application with 5 entity types, 6 RESTful patterns

## Constitution Check

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | All endpoints use resource URLs (nouns); HTTP methods match semantics |
| II. Idiomatic F# | PASS | Uses Hox DSL (h function), computation expressions, Option types |
| III. Library, Not Framework | PASS | Uses Hox for HTML rendering - external view engine as prescribed |
| IV. ASP.NET Core Native | PASS | Uses HttpContext directly, standard hosting patterns |
| V. Performance Parity | PASS | Hox's async rendering should not impact performance significantly |

**GATE RESULT**: PASS - No violations. Proceed with implementation.

## Project Structure

### Source Code (repository root)

```text
sample/Frank.Datastar.Hox/
├── Program.fs           # Main application (to be replaced)
├── Frank.Datastar.Hox.fsproj  # Project file (existing)
└── wwwroot/
    └── index.html       # Client-side HTML (copy from Basic sample)
```

**Structure Decision**: Single project sample application. The Hox sample mirrors the Basic sample's structure exactly - one Program.fs file with types, data stores, render functions, and resources.

## Complexity Tracking

> No Constitution violations requiring justification.

N/A - Implementation follows existing patterns from Frank.Datastar.Basic.
