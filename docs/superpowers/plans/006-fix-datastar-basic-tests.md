---
source: specs/006-fix-datastar-basic-tests
type: plan
---

# Implementation Plan: Fix Frank.Datastar.Basic Sample Tests

**Branch**: `006-fix-datastar-basic-tests` | **Date**: 2026-02-01 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/006-fix-datastar-basic-tests/spec.md`

## Summary

Fix 8 failing Playwright tests in Frank.Datastar.Tests by refactoring the Frank.Datastar.Basic sample application from multiple per-resource SSE channels to a single shared SSE channel per page. This architectural change ensures all fire-and-forget operations broadcast through the channel the client is actively subscribed to, respecting browser connection limits and enabling proper SSE-driven UI updates.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0
**Primary Dependencies**: Frank 6.x, StarFederation.Datastar.FSharp (via Frank.Datastar project reference), ASP.NET Core
**Storage**: In-memory (Dictionary, ResizeArray) - demo purposes only
**Testing**: Microsoft.Playwright.NUnit (1.57.0+), NUnit 3.x/4.x - Playwright browser automation tests
**Target Platform**: ASP.NET Core web server (localhost:5000 for tests)
**Project Type**: Sample web application demonstrating Frank + Datastar integration
**Performance Goals**: SSE updates visible within 500ms-1s of user action
**Constraints**: Must maintain all 12 currently passing tests while fixing 8 failing tests
**Scale/Scope**: Single-page sample app with 5 demo sections (contact, fruits, items, users, registration)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | Sample demonstrates resources with HTTP method semantics |
| II. Idiomatic F# | PASS | Uses computation expressions, pipelines, discriminated unions |
| III. Library, Not Framework | PASS | Sample uses Frank for routing only; no framework lock-in |
| IV. ASP.NET Core Native | PASS | Exposes HttpContext directly, uses standard hosting |
| V. Performance Parity | PASS | No performance-sensitive changes; sample code only |

**Gate Status**: PASS - No violations requiring justification.

## Project Structure

### Documentation (this feature)

```text
specs/006-fix-datastar-basic-tests/
├── plan.md              # This file
├── research.md          # Phase 0 output - SSE channel architecture research
├── data-model.md        # Phase 1 output - Channel/subscription model
├── quickstart.md        # Phase 1 output - How to run tests
├── contracts/           # Phase 1 output - N/A (no new API contracts)
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
sample/
├── Frank.Datastar.Basic/
│   ├── Program.fs           # Main application - PRIMARY CHANGE TARGET
│   └── wwwroot/index.html   # Static HTML (may need minor updates)
└── Frank.Datastar.Tests/
    ├── TestBase.fs          # Playwright test infrastructure
    ├── TestConfiguration.fs # Environment-based config
    ├── TestHelpers.fs       # Wait/assertion helpers
    ├── ClickToEditTests.fs  # 4 tests (2 failing)
    ├── SearchFilterTests.fs # 4 tests (2 failing)
    ├── BulkUpdateTests.fs   # 6 tests (4 failing)
    ├── StateIsolationTests.fs # 3 tests (passing)
    └── ConfigurationTests.fs  # 6 tests (passing)
```

**Structure Decision**: Single sample project with associated test project. Changes isolated to `sample/Frank.Datastar.Basic/Program.fs` with possible minor HTML updates.

## Complexity Tracking

No violations requiring justification. The change simplifies the architecture by consolidating 5 SSE channels into 1.

## Post-Design Constitution Check

*Re-evaluated after Phase 1 design artifacts completed.*

| Principle | Status | Post-Design Notes |
|-----------|--------|-------------------|
| I. Resource-Oriented Design | PASS | Resources unchanged; only internal channel routing changes |
| II. Idiomatic F# | PASS | Single channel uses same F# patterns (MailboxProcessor, DU) |
| III. Library, Not Framework | PASS | No new framework constraints introduced |
| IV. ASP.NET Core Native | PASS | HttpContext still exposed directly; standard SSE via response stream |
| V. Performance Parity | PASS | Single channel reduces connection overhead vs 5 channels |

**Post-Design Gate Status**: PASS - Design aligns with constitution principles.

## Generated Artifacts

| Artifact | Path | Description |
|----------|------|-------------|
| Research | [research.md](./research.md) | SSE channel architecture analysis |
| Data Model | [data-model.md](./data-model.md) | Channel/subscription model |
| Quickstart | [quickstart.md](./quickstart.md) | How to run tests |
| Contracts | [contracts/](./contracts/) | N/A (no new API endpoints) |

## Next Steps

Run `/speckit.tasks` to generate implementation tasks based on this plan.
