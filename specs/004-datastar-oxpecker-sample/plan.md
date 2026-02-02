# Implementation Plan: Frank.Datastar.Oxpecker Sample

**Branch**: `004-datastar-oxpecker-sample` | **Date**: 2026-01-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/004-datastar-oxpecker-sample/spec.md`

## Summary

Create a new sample project `sample/Frank.Datastar.Oxpecker/` that demonstrates Datastar integration using Oxpecker.ViewEngine for HTML generation. The sample must be functionally identical to `Frank.Datastar.Basic` and `Frank.Datastar.Hox`, implementing all five RESTful patterns (Contact CRUD, Fruits search, Items delete, Users bulk update, Registration validation) while using Oxpecker's computation expression syntax for type-safe HTML generation.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0
**Primary Dependencies**: Frank 6.x, Oxpecker.ViewEngine 2.x, StarFederation.Datastar.FSharp (via Frank.Datastar project reference)
**Storage**: In-memory (Dictionary, ResizeArray) - demo purposes only
**Testing**: test.sh script (bash/curl-based HTTP testing)
**Target Platform**: ASP.NET Core web server (cross-platform)
**Project Type**: Sample web application
**Performance Goals**: N/A (demonstration sample)
**Constraints**: Must match existing samples' behavior exactly (verified by test.sh)
**Scale/Scope**: Single-file sample (~700 lines), 5 resource patterns, 18 test cases

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Evidence |
|-----------|--------|----------|
| **I. Resource-Oriented Design** | PASS | Sample uses `resource` computation expression for all endpoints |
| **II. Idiomatic F#** | PASS | Oxpecker.ViewEngine uses F# computation expressions; follows existing sample patterns |
| **III. Library, Not Framework** | PASS | Sample demonstrates integration with external view engine (Oxpecker), not bundled |
| **IV. ASP.NET Core Native** | PASS | Uses standard `HttpContext`, `webHost` builder, middleware pipeline |
| **V. Performance Parity** | N/A | This is a sample, not core library code |

**Gate Result**: PASS - No violations. This sample aligns with Constitution by demonstrating view engine choice (Principle III) using idiomatic F# (Principle II).

## Project Structure

### Documentation (this feature)

```text
specs/004-datastar-oxpecker-sample/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
sample/Frank.Datastar.Oxpecker/
├── Frank.Datastar.Oxpecker.fsproj   # Project file (net10.0, Oxpecker.ViewEngine 2.x)
├── Program.fs                        # Main application (~700 lines)
└── test.sh                           # Copied from Frank.Datastar.Basic
```

**Structure Decision**: Single-project sample matching existing `Frank.Datastar.Basic` and `Frank.Datastar.Hox` structure. No separate files needed - all code in Program.fs for easy comparison.

## Complexity Tracking

> No violations requiring justification.

| Item | Rationale |
|------|-----------|
| Single Program.fs | Matches existing samples; enables side-by-side comparison |
| Oxpecker.ViewEngine NuGet | Published package on NuGet (v2.0.0), not local reference |

## Post-Design Constitution Re-Check

*After Phase 1 design artifacts completed.*

| Principle | Status | Post-Design Evidence |
|-----------|--------|---------------------|
| **I. Resource-Oriented Design** | PASS | All 10 endpoints defined as Frank resources in contracts/api.md |
| **II. Idiomatic F#** | PASS | Oxpecker.ViewEngine computation expressions align with Frank's `resource` and `webHost` patterns |
| **III. Library, Not Framework** | PASS | Oxpecker.ViewEngine is external NuGet package; sample shows composition |
| **IV. ASP.NET Core Native** | PASS | Uses `HttpContext` directly, standard SSE headers, middleware pipeline |
| **V. Performance Parity** | N/A | Sample code, not core library |

**Post-Design Gate Result**: PASS - Design artifacts confirm no Constitution violations.

## Generated Artifacts

| Artifact | Path | Purpose |
|----------|------|---------|
| Research | [research.md](research.md) | Oxpecker.ViewEngine patterns, Datastar attribute handling |
| Data Model | [data-model.md](data-model.md) | Entity definitions, SSE channel model |
| API Contracts | [contracts/api.md](contracts/api.md) | HTTP endpoint specifications |
| Quickstart | [quickstart.md](quickstart.md) | Build/run/test instructions |

## Next Steps

Run `/speckit.tasks` to generate implementation tasks from this plan.
