<!--
SYNC IMPACT REPORT
==================
Version change: N/A → 1.0.0 (initial)
Modified principles: N/A (initial constitution)
Added sections:
  - Core Principles (5 principles)
  - Technical Standards
  - Development Workflow
  - Governance
Removed sections: N/A
Templates requiring updates:
  - .specify/templates/plan-template.md ✅ (Constitution Check section compatible)
  - .specify/templates/spec-template.md ✅ (no changes needed)
  - .specify/templates/tasks-template.md ✅ (no changes needed)
Follow-up TODOs: None
-->

# Frank Constitution

## Core Principles

### I. Resource-Oriented Design

HTTP resources are the primary abstraction in Frank. The API MUST make it natural to
think in terms of resources with uniform interface semantics (GET, POST, PUT, DELETE),
not URL patterns with handlers attached.

- The `resource` computation expression is the central API concept
- Resource definitions MUST support all standard HTTP methods as first-class operations
- Hypermedia enables evolvability; static specifications (OpenAPI) create coupling
- New features MUST NOT push users toward route-centric thinking
- Link relations and content negotiation SHOULD be easy to express

**Rationale**: REST as defined in Fielding's dissertation treats resources as the key
abstraction. Hypermedia as the engine of application state (HATEOAS) provides
evolvability that static API specifications cannot. Frank's design follows HTTP's
design.

### II. Idiomatic F#

All public APIs MUST use F# idioms. Frank is an F# library for F# developers.

- Computation expressions for configuration and resource definition
- Discriminated unions for modeling choices
- Option types instead of nulls
- Pipeline-friendly function signatures
- Declarative style over imperative configuration

**Rationale**: F# developers choose Frank because it feels like F#, not because it
wraps a C# library with F# syntax. The API should be discoverable through F#
conventions.

### III. Library, Not Framework

Frank provides routing and resource definition. Nothing more.

- No view engine (use Falco.Markup, Oxpecker.ViewEngine, or any other)
- No ORM or data access
- No authentication system (use ASP.NET Core's)
- No opinions beyond HTTP resource modeling
- Easy to adopt incrementally; easy to remove

**Rationale**: Frameworks impose structure and create lock-in. Libraries compose.
Users MUST be able to use Frank for one resource in an existing ASP.NET Core app
without adopting a "Frank way" of doing everything else.

### IV. ASP.NET Core Native

Frank builds on ASP.NET Core, not around it.

- Expose `HttpContext` directly in handlers
- Use `IWebHostBuilder` and standard hosting patterns
- Integrate with ASP.NET Core middleware pipeline
- MUST NOT create abstractions that hide the underlying platform
- Users' ASP.NET Core knowledge transfers directly

**Rationale**: ASP.NET Core is well-designed and well-documented. Frank adds F#
ergonomics for resource definition; it does not replace the platform. When users
need to do something Frank doesn't directly support, they use ASP.NET Core APIs.

### V. Performance Parity

Frank MUST perform as well as comparable F# web libraries.

- Benchmark against Giraffe, Falco, and raw ASP.NET Core routing
- Performance regressions in PRs MUST be justified and documented
- Avoid allocations in hot paths
- Prefer struct types where appropriate

**Rationale**: Developers should not have to choose between ergonomics and
performance. Frank's computation expression syntax MUST NOT impose runtime overhead
compared to direct ASP.NET Core routing.

## Technical Standards

- **Target Framework**: .NET 8.0+ (current LTS)
- **F# Version**: F# 8.0+
- **Dependencies**: Minimize external dependencies; prefer ASP.NET Core built-ins
- **Nullability**: Treat warnings as errors; use Option types in F# APIs
- **Testing**: All public APIs MUST have tests; examples in README MUST compile

## Development Workflow

- **Branches**: Feature branches from `master`; PRs required for all changes
- **Tests**: All tests MUST pass before merge
- **Benchmarks**: Performance-sensitive changes MUST include benchmark results
- **Breaking Changes**: MAJOR version bump required; document migration path
- **Examples**: The `samples/` directory contains working applications that serve
  as integration tests

## Governance

This constitution defines non-negotiable principles for Frank development. All PRs
and code reviews MUST verify compliance with these principles.

**Amendment Process**:
1. Propose change via GitHub issue with rationale
2. Discussion period (minimum 7 days for principle changes)
3. Update constitution with new version number
4. Document migration impact if principles change

**Version Policy**:
- MAJOR: Principle removal or backward-incompatible redefinition
- MINOR: New principle added or existing principle materially expanded
- PATCH: Clarifications, typo fixes, non-semantic refinements

**Compliance**: When in doubt, refer to this constitution. Complexity MUST be
justified against these principles. "It would be nice to have" is not justification.

**Version**: 1.0.0 | **Ratified**: 2025-01-25 | **Last Amended**: 2025-01-25
