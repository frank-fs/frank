# Implementation Plan: Role Definition Schema

**Branch**: `033-role-definition-schema` | **Date**: 2026-03-21 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `kitty-specs/033-role-definition-schema/spec.md`

## Summary

Add declarative role definitions to the `statefulResource` CE that map authentication claims to named protocol roles. Two-tier type design: portable `RoleInfo` in `Frank.Resources.Model` (zero-dep, hierarchy-neutral), platform-specific `RoleDefinition` in `Frank.Statecharts` (carries `ClaimsPrincipal -> bool` predicate). Roles resolved eagerly per request via separate `IRoleFeature` typed feature interface. Guard context extended with `Roles: Set<string>` field and `HasRole` member method. Spec pipeline integration via `ExtractedStatechart.Roles` field.

## Technical Context

**Language/Version**: F# 8.0+ on .NET 8.0/9.0/10.0 (multi-target for src/, net10.0 for tests)
**Primary Dependencies**: ASP.NET Core (`HttpContext.Features`, `ClaimsPrincipal`), Expecto (testing)
**Storage**: N/A (roles are in-memory per-resource declarations, resolved per-request)
**Testing**: Expecto + ASP.NET Core TestHost (`testTask` for async, `testCase` for pure)
**Target Platform**: .NET 8.0+ (LTS), cross-platform
**Project Type**: Multi-project F# library
**Performance Goals**: Zero additional allocations beyond the `Set<string>` for resolved roles. O(n) predicate evaluation where n is typically 2-5 roles per resource — negligible vs HTTP stack overhead.
**Constraints**: No new project dependencies. No breaking changes to existing `statefulResource` API (FR-010). No `obj` boxing for role data (Wadler: typed boundary).
**Scale/Scope**: 3 assemblies modified, ~200-300 lines of new code, ~150 lines of tests

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | Roles enhance resource definitions via `statefulResource` CE. `role` operation is a resource-level declaration. |
| II. Idiomatic F# | PASS | CE custom operation `role`, record types for `RoleDefinition`/`RoleInfo`, `Set<string>` for resolved roles, member method `HasRole` on record. |
| III. Library, Not Framework | PASS | Uses ASP.NET Core's `ClaimsPrincipal` directly. No separate auth system. Roles are declarative labels, not an authorization framework. |
| IV. ASP.NET Core Native | PASS | `IRoleFeature` on `HttpContext.Features` follows standard typed feature pattern (Fowler). `ClaimsPrincipal` is the identity authority. |
| V. Performance Parity | PASS | Eager resolution adds O(n) predicate calls (n=2-5) once per request. Resolved `Set<string>` is immutable, zero-alloc lookups via `Contains`. No hot-path allocations beyond initial set construction. |
| VI. Resource Disposal Discipline | PASS | No `IDisposable` types introduced. |
| VII. No Silent Exception Swallowing | PASS | Role predicate exceptions logged via `ILogger` (spec edge case). No bare `with _ ->`. |
| VIII. No Duplicated Logic | PASS | Single `RoleDefinition` type, single resolution point in middleware. Guard name extraction pattern reused for role names in spec pipeline. |

No violations. No complexity tracking needed.

## Project Structure

### Documentation (this feature)

```
kitty-specs/033-role-definition-schema/
├── plan.md              # This file
├── research.md          # Phase 0: integration point analysis
├── data-model.md        # Phase 1: type definitions
├── quickstart.md        # Phase 1: usage examples
└── tasks.md             # Phase 2 output (created by /spec-kitty.tasks)
```

### Source Code (files modified)

```
src/Frank.Resources.Model/
└── ResourceTypes.fs          # Add RoleInfo type, add Roles field to ExtractedStatechart

src/Frank.Statecharts/
├── Types.fs                  # Add RoleDefinition type (alongside Guard, AccessControlContext)
├── StatechartFeature.fs      # Add IRoleFeature interface, SetRoles extension method
├── StatefulResourceBuilder.fs # Add role CE operation, update StatefulResourceSpec accumulator,
│                              # update StateMachineMetadata, update evaluateGuards closure
└── Middleware.fs              # Add role resolution step (between state resolution and guard eval)

src/Frank.Cli.Core/
├── Statechart/StatechartSourceExtractor.fs  # Thread role names through extraction
└── Unified/UnifiedExtractor.fs              # Thread role names through unified extraction

test/Frank.Statecharts.Tests/
└── StatefulResourceTests.fs   # Test role declaration, resolution, guard integration
```

**Structure Decision**: No new projects. Changes are additive to existing files across 3 assemblies (`Frank.Resources.Model`, `Frank.Statecharts`, `Frank.Cli.Core`) plus tests. Follows the established pattern where portable types live in `Frank.Resources.Model` and platform-specific types live in `Frank.Statecharts`.

### Key Integration Points

| Component | File | Lines | Pattern to Follow |
|-----------|------|-------|-------------------|
| `ExtractedStatechart` construction (factory) | `StatechartExtractor.fs` | ~10-21 | Add `roles` parameter |
| `ExtractedStatechart` construction (unified) | `UnifiedExtractor.fs` | ~617 | Pass role names |
| `ExtractedStatechart` construction (source) | `StatechartSourceExtractor.fs` | ~331 | Pass role names |
| `AccessControlContext` construction | `StatefulResourceBuilder.fs` | ~226-229 | Add `Roles` field from `IRoleFeature` |
| `EventValidationContext` construction | `StatefulResourceBuilder.fs` | ~247-251 | Add `Roles` field from `IRoleFeature` |
| `StatefulResourceSpec.Yield()` | `StatefulResourceBuilder.fs` | ~138-144 | Add `Roles = []` |
| Guard name extraction | `StatefulResourceBuilder.fs` | ~294-298 | Follow same pattern for role names |
| Feature registration | `StatechartFeature.fs` | ~29-38 | Follow dual-registration pattern |
