# Implementation Plan: Enhanced Sample Test Validation

**Branch**: `005-fix-sample-tests` | **Date**: 2026-01-27 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/005-fix-sample-tests/spec.md`

## Summary

Enhance the `test.sh` scripts for Frank.Datastar sample applications (Basic, Hox, Oxpecker) to validate actual behavioral correctness, not just HTTP status codes. Current tests pass when endpoints return expected status codes but fail to detect bugs like click-to-edit showing empty values, search clearing the list, and bulk updates not persisting changes. The enhanced tests will verify response content matches expected state changes.

## Technical Context

**Language/Version**: Bash (POSIX-compatible shell scripting)
**Primary Dependencies**: curl (HTTP client), grep/sed (text parsing), standard Unix tools
**Storage**: N/A (tests read from sample applications' in-memory stores)
**Testing**: Self-testing bash scripts with pass/fail assertions
**Target Platform**: macOS, Linux (standard Unix-like environments)
**Project Type**: Script enhancement (no source structure changes)
**Performance Goals**: Tests complete in under 30 seconds per sample
**Constraints**: Tests must be sequential/stateful; single-user model; curl timeouts for SSE
**Scale/Scope**: 3 sample applications, ~20-30 test cases each

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | N/A | Tests validate resources; don't define new ones |
| II. Idiomatic F# | N/A | Bash scripts, not F# code |
| III. Library, Not Framework | PASS | Tests are standalone; no framework dependencies |
| IV. ASP.NET Core Native | N/A | Tests external to application |
| V. Performance Parity | PASS | Tests validate behavior, not performance |

**Technical Standards Check:**
- Tests align with "samples/ directory contains working applications that serve as integration tests"
- This feature enhances integration test coverage

**Result**: All applicable gates pass. No violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/005-fix-sample-tests/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
sample/
├── Frank.Datastar.Basic/
│   ├── test.sh          # MODIFY: Enhanced test script
│   └── Program.fs       # READ-ONLY: Understand seed data/endpoints
├── Frank.Datastar.Hox/
│   ├── test.sh          # MODIFY: Enhanced test script
│   └── Program.fs       # READ-ONLY: Understand seed data/endpoints
└── Frank.Datastar.Oxpecker/
    ├── test.sh          # MODIFY: Enhanced test script
    └── Program.fs       # READ-ONLY: Understand seed data/endpoints
```

**Structure Decision**: Script enhancement only. No new directories. Each sample's `test.sh` is modified in-place. May extract shared test utilities to a common file if significant duplication occurs.

## Complexity Tracking

> No violations requiring justification.

## Post-Design Constitution Check

*Re-evaluated after Phase 1 design completion.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | Tests validate existing resource semantics |
| II. Idiomatic F# | N/A | Bash scripts only |
| III. Library, Not Framework | PASS | Tests are standalone utilities |
| IV. ASP.NET Core Native | N/A | External to application |
| V. Performance Parity | PASS | Tests validate behavior, no perf overhead added |

**Technical Standards Check:**
- Tests serve as integration tests per "samples/ directory" guidance
- No new F# code; no nullability concerns
- All test patterns documented in quickstart.md

**Result**: All applicable gates pass. Design approved for task generation.

## Phase 0-1 Artifacts

| Artifact | Path | Status |
|----------|------|--------|
| Research | [research.md](./research.md) | Complete |
| Data Model | [data-model.md](./data-model.md) | Complete |
| Quickstart | [quickstart.md](./quickstart.md) | Complete |

## Next Steps

Run `/speckit.tasks` to generate actionable tasks from this plan.
