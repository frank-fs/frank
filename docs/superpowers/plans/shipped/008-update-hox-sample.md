---
source: specs/008-update-hox-sample
type: plan
---

# Implementation Plan: Update Frank.Datastar.Hox Sample

**Branch**: `008-update-hox-sample` | **Date**: 2026-02-02 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/008-update-hox-sample/spec.md`

## Summary

Update the Frank.Datastar.Hox sample application to clone the validated Frank.Datastar.Basic implementation architecture while using Hox's CSS-selector-based DSL (`h()` function) for HTML generation. The key change is replacing the per-section MailboxProcessor channels with a single `/sse` endpoint using the broadcast channel pattern from Basic.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0
**Primary Dependencies**: Frank 6.x, Hox 3.x, StarFederation.Datastar.FSharp (via Frank.Datastar project reference)
**Storage**: In-memory (Dictionary, ResizeArray) - demo purposes only
**Testing**: Playwright via Frank.Datastar.Tests (NUnit + Microsoft.Playwright.NUnit)
**Target Platform**: ASP.NET Core web server (localhost:5000)
**Project Type**: Single F# web application (sample)
**Performance Goals**: N/A (sample application, must match Basic sample behavior)
**Constraints**: Must pass all existing Playwright tests without modification
**Scale/Scope**: Single Program.fs file (~600 lines), replaces existing implementation

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Resource-Oriented Design** | PASS | Uses Frank's `resource` computation expression for all endpoints (contacts, fruits, items, users, registrations, sse) |
| **II. Idiomatic F#** | PASS | Uses computation expressions, discriminated unions (SseEvent, UserStatus), Option types |
| **III. Library, Not Framework** | PASS | Uses Hox as view engine (external library, easily swappable); Frank provides routing only |
| **IV. ASP.NET Core Native** | PASS | Exposes HttpContext directly in handlers; uses standard hosting patterns |
| **V. Performance Parity** | PASS | Sample application; no performance requirements beyond Basic sample parity |

**Pre-Phase 0 Gate**: PASSED - No violations

## Project Structure

### Documentation (this feature)

```text
specs/008-update-hox-sample/
├── plan.md              # This file
├── research.md          # Phase 0 output - Hox API patterns
├── data-model.md        # Phase 1 output - Entity mappings
├── quickstart.md        # Phase 1 output - Build/run instructions
├── contracts/           # Phase 1 output - API contracts (same as Basic)
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
sample/Frank.Datastar.Hox/
├── Program.fs           # Main application (to be rewritten)
├── Frank.Datastar.Hox.fsproj  # Project file (already configured)
└── wwwroot/
    └── index.html       # Client-side code (copy from Basic)

sample/Frank.Datastar.Basic/
├── Program.fs           # Reference implementation (read-only)
└── wwwroot/
    └── index.html       # Reference client code

sample/Frank.Datastar.Tests/
├── *.fs                 # Playwright test files (unchanged)
└── Frank.Datastar.Tests.fsproj
```

**Structure Decision**: Single sample project with one Program.fs file. The project structure already exists; only Program.fs content and wwwroot/index.html need updating.

## Complexity Tracking

> No violations requiring justification.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| (none) | - | - |

## Post-Phase 1 Constitution Re-Check

| Principle | Status | Notes |
|-----------|--------|-------|
| **I. Resource-Oriented Design** | PASS | All endpoints use `resource` CE; noun-based URLs (contacts, fruits, items, users, registrations) |
| **II. Idiomatic F#** | PASS | Computation expressions, discriminated unions, pipeline-friendly patterns |
| **III. Library, Not Framework** | PASS | Hox is external view engine; easily replaceable (as demonstrated by Basic vs Hox samples) |
| **IV. ASP.NET Core Native** | PASS | HttpContext exposed directly; standard hosting via `webHost` |
| **V. Performance Parity** | PASS | Sample application; Hox rendering adds minimal overhead |

**Post-Phase 1 Gate**: PASSED

## Generated Artifacts

| Artifact | Path | Status |
|----------|------|--------|
| Implementation Plan | `plan.md` | Complete |
| Research | `research.md` | Complete |
| Data Model | `data-model.md` | Complete |
| API Contract | `contracts/api.md` | Complete |
| Quickstart | `quickstart.md` | Complete |
| Tasks | `tasks.md` | Not created (Phase 2 - `/speckit.tasks`) |

## Next Steps

Run `/speckit.tasks` to generate the implementation task list.
