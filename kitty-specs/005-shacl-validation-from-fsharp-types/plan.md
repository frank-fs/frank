# Implementation Plan: Frank.Validation -- SHACL Shape Validation from F# Types

**Branch**: `005-shacl-validation-from-fsharp-types` | **Date**: 2026-03-07 | **Spec**: [spec.md](spec.md)
**Input**: Phase 2 of #80 (Semantic Metadata-Augmented Resources). SHACL shapes as semantic request/response constraints, derived automatically from F# types.
**Research**: [research.md](research.md) | **Data Model**: [data-model.md](data-model.md) | **Quickstart**: [quickstart.md](quickstart.md)
**GitHub Issue**: #76

## Summary

Implement `Frank.Validation`, an extension library that derives W3C SHACL NodeShapes from F# record types and discriminated unions at application startup, then validates incoming HTTP requests against those shapes before handler dispatch. Provides a `validate` custom operation on the `ResourceBuilder` CE, returns 422 Unprocessable Content with SHACL ValidationReport (content-negotiated via Frank.LinkedData) or RFC 9457 Problem Details for non-semantic clients. Composes with Frank.Auth for capability-dependent shape selection and supports developer-supplied custom constraints that are additive to auto-derived shapes. Shape derivation uses .NET reflection (no FSharp.Compiler.Service dependency); runtime validation uses dotNetRdf.Core's SHACL engine.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 8.0/9.0/10.0 (multi-targeting, matching Frank core)
**Primary Dependencies**: Frank (project reference), Frank.LinkedData (project reference), Frank.Auth (project reference), dotNetRdf.Core (NuGet, shared with Frank.LinkedData), Microsoft.AspNetCore.App (framework reference)
**Storage**: N/A (no persistence -- shapes are derived at startup and cached in memory; validation is stateless per-request)
**Testing**: Expecto + ASP.NET Core TestHost (matching existing Frank test patterns)
**Target Platform**: .NET multi-target library (net8.0;net9.0;net10.0)
**Project Type**: Library extension for Frank
**Performance Goals**: Validation middleware adds less than 1ms overhead per request for shapes with up to 20 property constraints (SC-004). Shape derivation is startup-only cost.
**Constraints**: Shape derivation via .NET reflection only (no FSharp.Compiler.Service). Custom constraints additive only -- cannot weaken auto-derived constraints. Recursive type derivation capped at configurable depth (default 5).
**Scale/Scope**: Shapes with up to 20-50 property constraints. Nested types up to 5 levels deep. Capability-dependent shape variants per resource.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Resource-Oriented Design -- PASS

The `validate` custom operation extends the `resource` CE, keeping validation as a resource-level concern. SHACL shapes describe the constraints a resource's data must satisfy -- this is semantic metadata about the resource, not a route-level concern. Validation failure responses are content-negotiated resource representations (SHACL ValidationReport), not opaque error strings. The 422 status code correctly communicates "syntactically valid but semantically invalid" within HTTP's uniform interface.

### II. Idiomatic F# -- PASS

- `validate` is a `[<CustomOperation>]` on `ResourceBuilder` (CE pattern)
- Shape derivation uses F# reflection metadata (records, DUs, option types)
- `ShaclShape`, `PropertyShape`, `ValidationReport`, `ValidationResult` are F# record types
- `ShapeConstraint` uses discriminated unions for constraint variants
- Pipeline-friendly: `Type -> ShaclShape` derivation is a pure function
- `option` types used for optional fields, not nulls

### III. Library, Not Framework -- PASS

Frank.Validation is an opt-in library extension. Resources without `validate` are unaffected. No opinions on view engines, data access, or authentication beyond composing with Frank.Auth's capability model when explicitly configured. Users can use Frank.Validation for some resources and not others. The `validate` operation is additive -- removing the dependency removes the behavior.

### IV. ASP.NET Core Native -- PASS

- Uses ASP.NET Core middleware pipeline for request interception (same pattern as Frank.LinkedData, Frank.Auth)
- `ValidationMarker` endpoint metadata marker (same pattern as `LinkedDataMarker`)
- `useValidation` registers middleware via `IApplicationBuilder`
- Content negotiation uses ASP.NET Core's `Accept` header parsing
- `HttpContext` exposed directly -- no wrapping abstractions

### V. Performance Parity -- PASS

- Shape derivation is a one-time startup cost (reflection + SHACL graph construction)
- Derived shapes cached in a `ConcurrentDictionary<Type, ShaclShape>`
- Runtime validation is one dotNetRdf SHACL validation pass per request
- No allocations in the "valid request" hot path beyond the validation check itself
- Requests to non-validated resources have zero overhead (metadata check only)

### VI. Resource Disposal Discipline -- PASS

- dotNetRdf `IGraph` instances used in shape derivation are held for the application lifetime (singleton, not disposable)
- `ValidationReport` graph instances created per-request are disposed after response serialization via `use` bindings
- No `StreamReader`/`StreamWriter` usage in core validation path -- serialization delegates to Frank.LinkedData

### VII. No Silent Exception Swallowing -- PASS

- Shape derivation errors at startup propagate as `InvalidOperationException` with descriptive messages (e.g., conflicting custom constraints, unsupported type)
- Runtime validation errors are logged via `ILogger<ValidationMiddleware>` and result in 500 Internal Server Error, never silently swallowed
- Deserialization failures produce explicit `ValidationResult` entries, not catch-and-ignore

### VIII. No Duplicated Logic -- PASS

- F# type-to-XSD datatype mapping defined once in `TypeMapping.fs`, used by both shape derivation and validation report generation
- URI construction uses `RdfUriHelpers` from Frank.LinkedData (shared module, no duplication)
- Content negotiation for ValidationReport responses delegates to Frank.LinkedData's existing negotiation infrastructure
- Custom constraint merging logic in one place (`ShapeMerger.fs`), used at startup only

## Project Structure

### Documentation (this feature)

```
kitty-specs/005-shacl-validation-from-fsharp-types/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Research decisions and rationale
├── data-model.md        # Entity model definitions
├── quickstart.md        # Developer quickstart guide
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── research/
    ├── evidence-log.csv
    └── source-register.csv
```

### Source Code (repository root)

```
src/
├── Frank/                          # Existing core library
│   └── Builder.fs                  # ResourceBuilder, ResourceSpec (extended by validate)
│
├── Frank.LinkedData/               # Existing (content negotiation, RdfUriHelpers)
│   └── Rdf/RdfUriHelpers.fs        # Shared URI construction (reused, not duplicated)
│
├── Frank.Auth/                     # Existing (capability model for shape resolution)
│   └── AuthRequirement.fs          # ClaimsPrincipal-based capabilities
│
├── Frank.Validation/               # NEW: SHACL validation library
│   ├── Frank.Validation.fsproj     # Multi-target: net8.0;net9.0;net10.0
│   ├── TypeMapping.fs              # F# type -> XSD datatype mapping
│   ├── ShapeDerivation.fs          # Type reflection -> ShaclShape derivation
│   ├── ShapeMerger.fs              # Custom constraint merging + conflict detection
│   ├── ShapeResolver.fs            # Runtime shape selection (capability-dependent)
│   ├── Validator.fs                # dotNetRdf SHACL validation execution
│   ├── ReportSerializer.fs         # ValidationReport -> JSON-LD / Problem Details
│   ├── ValidationMiddleware.fs     # ASP.NET Core middleware (pipeline integration)
│   ├── ResourceBuilderExtensions.fs # [<AutoOpen>] validate CE custom operation
│   └── WebHostBuilderExtensions.fs  # [<AutoOpen>] useValidation extension
│
└── Frank.Auth/                     # Existing (guard context uses ClaimsPrincipal)

test/
└── Frank.Validation.Tests/         # NEW: Test project
    ├── Frank.Validation.Tests.fsproj  # Target: net10.0
    ├── TypeMappingTests.fs          # F# type -> XSD datatype mapping tests
    ├── ShapeDerivationTests.fs      # Shape derivation from records, DUs, nested types
    ├── ShapeMergerTests.fs          # Custom constraint merging + conflict detection
    ├── ValidatorTests.fs            # Validation pass/fail with dotNetRdf
    ├── MiddlewareTests.fs           # Pipeline integration tests (TestHost)
    ├── ReportSerializationTests.fs  # Content negotiation for violation reports
    ├── CapabilityTests.fs           # Capability-dependent shape selection
    └── Program.fs                   # Expecto entry point
```

**Structure Decision**: Single library project + single test project. Follows the same pattern as Frank.LinkedData, Frank.Auth, and Frank.Statecharts. The library depends on both Frank.LinkedData (content negotiation, URI helpers) and Frank.Auth (capability model) as project references.

## Parallel Work Analysis

### Dependency Graph

```
WP01: Project Scaffold & Core Types
         |
         v
WP02: Type Mapping & Shape Derivation
         |
         ├──> WP03: Validator & Middleware
         |         |
         |         v
         |    WP04: Violation Reporting
         |
         ├──> WP05: Capability-Dependent Shapes
         |
         └──> WP06: Custom Constraints
                    |
                    v
         WP07: Builder Extensions <── depends on WP02-WP06
                    |
                    v
         WP08: Integration Tests <── depends on all prior WPs
```

### Parallelism Opportunities

- **WP01 + WP02**: TypeMapping and ShapeDerivation can be started concurrently (WP02 calls WP01 but can stub the mapping initially)
- **WP03 + WP04 + WP05**: ShapeMerger, Validator, and ShapeResolver are independent once WP02 is complete
- **WP06 + WP07**: Middleware and CE extensions depend on WP03-05 but can be developed together
- **WP08**: Integration tests require all prior WPs

### Critical Path

WP01->WP02->WP03->WP04->WP07->WP08

## Complexity Tracking

No constitution violations requiring justification. The design adds one new library project following established patterns (Frank.LinkedData, Frank.Auth).

## Risks and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| dotNetRdf SHACL validation performance for complex shapes | Medium | Cache derived shapes; benchmark with 20-property shapes; short-circuit on first violation if configured |
| F# reflection metadata gaps (e.g., DU case field names) | Medium | Prototype shape derivation early (WP02); fall back to `FSharpType`/`FSharpValue` APIs which expose full metadata |
| Circular dependency between Frank.Validation and Frank.LinkedData | High | Frank.Validation depends on Frank.LinkedData (one-way); serialization delegates to LinkedData's negotiation infrastructure |
| Custom constraint conflict detection false positives | Low | Conservative conflict detection: only flag direct contradictions (e.g., minCount 0 on required field), not semantic tensions |
| Middleware ordering sensitivity (Auth -> Validation -> Handler) | Low | Document recommended middleware order; test Auth+Validation composition explicitly |
