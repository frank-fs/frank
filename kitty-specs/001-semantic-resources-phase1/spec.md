# Feature Specification: Semantic Resources Phase 1

**Feature Branch**: `001-semantic-resources-phase1`
**Created**: 2026-03-04
**Status**: Draft
**Input**: Implement Phase 1 of milestone #2 (tracking issue #80) — parallel workstreams for frank-cli (#79) and Frank.LinkedData (#75)
**Milestone**: v7.3.0 — Semantic Metadata-Augmented Resources
**GitHub Issues**: #80 (tracking), #79 (frank-cli), #75 (Frank.LinkedData)

---

## Clarifications

### Session 2026-03-04

- Q: How should frank-cli extract read F# source? → A: Both AST (FSharp.Compiler.Service) for type structure and reflection for route/handler registration. Compiled assembly required as precondition to ensure source validity before extraction.
- Q: How should the ontology namespace be determined? → A: Use standard vocabularies (schema.org by default) for well-known concepts. Derive project-specific namespace from assembly name with `--base-uri` CLI override. Provide `--vocabularies` parameter (default: schema.org, hydra) to allow adding domain-specific vocabularies explicitly.
- Q: Where does frank-cli persist intermediate extraction state between commands? → A: On disk in `obj/frank-cli/`, alongside other build artifacts. Cleaned by `dotnet clean`.
- Q: Which RDF vocabulary for HTTP method capability semantics? → A: Both Schema.org Actions and Hydra as defaults. Schema.org for general capability semantics, Hydra for hypermedia-specific concepts (API documentation, supported operations, link relations). Both included by default via `--vocabularies`.
- Q: How does LinkedData produce instance-level RDF from a resource? → A: Automatic serialization via reflection on the handler's return type, using the compiled ontology as the schema map. No developer-provided mapping function needed.
- Q: How does frank-cli compile hook into dotnet build? → A: Generate MSBuild .props/.targets files that auto-embed resources when the tool package is referenced — zero manual .fsproj changes needed.

---

## User Scenarios & Testing

### User Story 1 — Extract Ontology from Frank Source (Priority: P1)

A developer using an LLM coding assistant (Claude Code, GitHub Copilot) asks the assistant to generate semantic definitions for their Frank application. The assistant invokes `frank-cli extract` against the project, producing a candidate OWL ontology and SHACL shapes derived from the F# type system — discriminated unions become class hierarchies, option types become cardinality constraints, route definitions become resource identities.

**Why this priority**: This is the foundation — without extraction, no other semantic capability exists. The ontology is the source of truth that all downstream features (LinkedData, Validation, Provenance, SPARQL) depend on.

**Independent Test**: Run `frank-cli extract` against a Frank project with known types and routes; verify the output ontology contains the expected OWL classes, properties, and SHACL shapes.

**Acceptance Scenarios**:

1. **Given** a Frank project with discriminated unions and resource definitions, **When** `frank-cli extract` is invoked, **Then** an OWL ontology is produced with classes corresponding to each discriminated union and properties corresponding to record fields.
2. **Given** a Frank project with route definitions (e.g., `resource "/products/{id}"`), **When** `frank-cli extract` is invoked, **Then** the ontology contains resource identities corresponding to each route pattern.
3. **Given** a Frank resource with HTTP method handlers (GET, POST, PUT, DELETE), **When** `frank-cli extract` is invoked, **Then** the ontology captures capability semantics (read, create, update, delete) for each handler.
4. **Given** F# option types in resource handler signatures, **When** `frank-cli extract` is invoked, **Then** SHACL shapes reflect the correct cardinality constraints (optional vs. required).

---

### User Story 2 — Iterative Refinement via Clarify and Validate (Priority: P1)

After initial extraction, the LLM assistant invokes `frank-cli clarify` to discover ambiguities (e.g., "Is `ProductStatus` a closed or open enumeration?"). The assistant resolves these and re-extracts. It then runs `frank-cli validate` to confirm completeness and `frank-cli diff` to review changes before finalizing.

**Why this priority**: The iterative refinement loop is what makes the tool useful for LLM agents — a one-shot extraction without the ability to clarify and validate would produce low-quality ontologies.

**Independent Test**: Run extract → clarify → extract (with clarification parameters) → validate → diff; verify each command produces structured JSON output that enables the next step.

**Acceptance Scenarios**:

1. **Given** an extracted ontology with ambiguous type mappings, **When** `frank-cli clarify` is invoked, **Then** it returns a JSON array of structured decision points, each with a question, context, and suggested options.
2. **Given** clarification answers provided as parameters, **When** `frank-cli extract` is re-invoked with those parameters, **Then** the updated ontology reflects the clarified decisions.
3. **Given** an extracted ontology, **When** `frank-cli validate` is invoked, **Then** it returns a JSON report of completeness (unmapped types, missing relationships) and consistency checks.
4. **Given** two successive extractions, **When** `frank-cli diff` is invoked, **Then** it returns a structured diff showing added, removed, and modified ontology elements.

---

### User Story 3 — Compile Semantic Definitions into Assembly (Priority: P1)

Once the ontology is satisfactory, the LLM assistant invokes `frank-cli compile` to generate the final OWL/XML and SHACL artifacts as embedded resources in the project's compiled assembly. The generated files appear in `obj/` but are not committed to source control.

**Why this priority**: Without compile, the semantic definitions cannot be consumed at runtime by Frank.LinkedData or other downstream libraries.

**Independent Test**: Run `frank-cli compile` after a successful extraction; verify embedded resources appear in `obj/` and are accessible as assembly resources after `dotnet build`.

**Acceptance Scenarios**:

1. **Given** a validated ontology extraction, **When** `frank-cli compile` is invoked, **Then** OWL/XML and SHACL files are generated in the project's `obj/` directory.
2. **Given** compiled semantic definitions, **When** the project is built with `dotnet build`, **Then** the OWL/XML and SHACL files are embedded as assembly resources.
3. **Given** a compiled assembly with embedded semantic definitions, **When** the assembly is inspected at runtime, **Then** the ontology and shapes can be loaded from embedded resources.

---

### User Story 4 — Opt-in Linked Data Content Negotiation (Priority: P2)

A developer adds the `linkedData` custom operation to their Frank resource builder computation expression. At runtime, when a client sends an `Accept` header requesting `application/ld+json`, `text/turtle`, or `application/rdf+xml`, the resource responds with the appropriate semantic representation. The library automatically serializes the handler's return type to RDF triples via reflection, using the compiled ontology as the schema map — no developer-provided mapping function is required.

**Why this priority**: This is the runtime consumer of the frank-cli output — it makes semantic representations available over HTTP. It depends on the extraction/compile pipeline (P1) being functional first.

**Independent Test**: Define a Frank resource with `linkedData` enabled, make HTTP requests with semantic `Accept` headers, and verify correct RDF responses.

**Acceptance Scenarios**:

1. **Given** a Frank resource with `linkedData` enabled and embedded semantic definitions, **When** a client requests `Accept: application/ld+json`, **Then** the resource responds with a JSON-LD representation.
2. **Given** a Frank resource with `linkedData` enabled, **When** a client requests `Accept: text/turtle`, **Then** the resource responds with a Turtle representation.
3. **Given** a Frank resource with `linkedData` enabled, **When** a client requests `Accept: application/rdf+xml`, **Then** the resource responds with an RDF/XML representation.
4. **Given** a Frank resource with `linkedData` enabled, **When** a client requests `Accept: text/html` (standard), **Then** the existing HTML/JSON content negotiation behavior is unchanged.
5. **Given** a Frank resource with `linkedData` enabled, **When** no semantic definitions exist in the assembly, **Then** an error is raised at startup (not silently ignored).

---

### User Story 5 — Build-time and Runtime Validation of Semantic Definitions (Priority: P2)

A developer adds the `linkedData` operation to a resource but has not run `frank-cli compile` to generate the semantic definitions. The MSBuild target from `Frank.Cli.MSBuild` emits a build warning when semantic artifacts are missing. If the developer builds and runs anyway, the runtime raises a startup error indicating that `linkedData` is enabled but no embedded definitions were found.

**Why this priority**: Two-layer validation catches missing definitions both at build time (MSBuild warning) and at runtime startup (fail-fast error), without requiring an analyzer dependency.

**Independent Test**: Build a Frank project referencing `Frank.Cli.MSBuild` without running `frank-cli compile`; verify MSBuild warning. Then run the app with `linkedData` enabled and no embedded resources; verify startup error.

**Acceptance Scenarios**:

1. **Given** a project referencing `Frank.Cli.MSBuild` without semantic artifacts in `obj/frank-cli/`, **When** `dotnet build` runs, **Then** a build warning is emitted indicating missing artifacts.
2. **Given** a Frank resource using `linkedData` without embedded semantic definitions in the assembly, **When** the application starts, **Then** a startup error is raised with a clear message (FR-021).
3. **Given** a Frank resource using `linkedData` with valid embedded semantic definitions, **When** the application starts, **Then** no error is raised.

---

### Edge Cases

- What happens when `frank-cli extract` encounters F# types it cannot map (e.g., function types, opaque external types)? It should report them in the `clarify` output as unmapped items.
- How does the system handle partial extractions — e.g., a project with some resources having semantic definitions and others not? Only resources with `linkedData` enabled require definitions; others are unaffected.
- What happens when semantic definitions are stale (source changed after last `compile`)? The `validate` command should detect drift by comparing a source file hash against the extraction state hash.
- What happens when multiple content negotiation formatters conflict (e.g., both MVC JSON formatter and JSON-LD formatter match `application/json`)? JSON-LD is served only for `application/ld+json`; `application/json` continues to use the existing MVC formatter.
- What happens when the embedded ontology references types from external assemblies? Phase 1 scopes extraction to the current project only; cross-assembly references are flagged as external and deferred to later phases.
- What happens when `dotnet clean` is run? Intermediate state in `obj/frank-cli/` is removed; the next `extract` starts fresh. Compiled embedded resources are also removed, requiring a re-compile.

## Requirements

### Functional Requirements

**frank-cli (dotnet tool)**

- **FR-001**: The tool MUST be distributed as a `dotnet tool` installable via `dotnet tool install`.
- **FR-002**: The `extract` command MUST map F# discriminated unions to OWL class hierarchies.
- **FR-003**: The `extract` command MUST map F# record types to OWL classes with datatype properties for each field.
- **FR-004**: The `extract` command MUST map F# option types to SHACL cardinality constraints (minCount 0 vs 1).
- **FR-005**: The `extract` command MUST map Frank route definitions to RDF resource identities.
- **FR-006**: The `extract` command MUST map HTTP method handlers to capability semantics in the ontology.
- **FR-007**: The `extract` command MUST accept parameters that direct extraction scope and methods (whole project, single file, single resource).
- **FR-007a**: *DROPPED — compiled assembly precondition removed to avoid two-pass build workflow. FCS AST provides all needed information for Phase 1 scope (static CE route definitions). See analysis session 2026-03-04.*
- **FR-007b**: The `extract` command MUST use FSharp.Compiler.Service for both AST-level route/handler detection (untyped AST) and type structure analysis (typed AST). No compiled assembly is required.
- **FR-007c**: The `extract` command MUST accept a `--base-uri` parameter to override the default project-specific namespace (derived from assembly name).
- **FR-007d**: The `extract` command MUST accept a `--vocabularies` parameter (default: schema.org, hydra) to specify which standard vocabularies to use for well-known concept alignment.
- **FR-007e**: The `extract` command MUST map HTTP method capabilities using Schema.org Actions and Hydra vocabularies by default — Schema.org for general capability semantics, Hydra for hypermedia-specific concepts (operations, parameters, link relations).
- **FR-007f**: The `extract` command MUST persist intermediate extraction state in `obj/frank-cli/` for use by subsequent commands (clarify, validate, diff, compile).
- **FR-008**: The `clarify` command MUST return structured JSON describing ambiguities with questions, context, and suggested options.
- **FR-009**: The `validate` command MUST check completeness (unmapped types, missing relationships) and consistency of the extracted ontology.
- **FR-010**: The `diff` command MUST compare the current extraction against the previous version and return structured changes (added, removed, modified).
- **FR-011**: The `compile` command MUST generate OWL/XML and SHACL artifacts in the project's `obj/` directory.
- **FR-012**: The `compile` command MUST configure the artifacts as embedded resources so they are included in the compiled assembly by `dotnet build`.
- **FR-012a**: The tool package MUST include MSBuild `.props`/`.targets` files that automatically embed the compiled artifacts — zero manual `.fsproj` changes required.
- **FR-013**: All commands MUST output structured JSON for machine consumption by default.
- **FR-014**: All commands MUST support a human-readable text output mode.

**Frank.LinkedData (extension library)**

- **FR-015**: The library MUST provide a `linkedData` custom operation on the `ResourceBuilder` computation expression. No developer-provided mapping function is required.
- **FR-016**: The library MUST extend content negotiation to serve `application/ld+json` (JSON-LD) representations.
- **FR-017**: The library MUST extend content negotiation to serve `text/turtle` (Turtle) representations.
- **FR-018**: The library MUST extend content negotiation to serve `application/rdf+xml` (RDF/XML) representations.
- **FR-019**: The library MUST NOT break existing content negotiation behavior for standard media types (HTML, JSON, XML).
- **FR-020**: The library MUST load semantic definitions from embedded assembly resources at startup.
- **FR-020a**: The library MUST automatically serialize handler return types to RDF triples via reflection, using the compiled ontology as the schema map.
- **FR-021**: The library MUST raise an error at startup if `linkedData` is enabled but semantic definitions are not found in the assembly.
- **FR-022**: The library MUST use `dotNetRdf.Core` as its RDF triple model, shared with frank-cli. This is a justified dependency per constitution review — writing custom serializers for 3 formats would be more code and more bugs than the dependency itself, and Phase 4 SPARQL requires dotNetRDF regardless.

**Frank.Analyzer (extension)**

*Analyzer requirements (formerly numbered 023/024) were DROPPED during planning — replaced by MSBuild target warning + runtime startup validation. See planning clarification session.*

### Key Entities

- **OWL Ontology**: A formal description of the application's type system as OWL classes, properties, and relationships. Serialized as OWL/XML.
- **SHACL Shapes**: Constraint definitions describing the expected shape of RDF data — cardinality, value types, patterns. Derived from F# type constraints.
- **RDF Triple**: The atomic unit of the semantic model — subject, predicate, object. Implemented as a minimal custom type in Frank.LinkedData.
- **Semantic Definition Artifact**: The compiled OWL/XML and SHACL files embedded as assembly resources, produced by `frank-cli compile`.
- **Resource Identity**: The RDF URI corresponding to a Frank route definition, mapping route patterns to semantic identifiers. Project-specific namespace derived from assembly name (overridable via `--base-uri`).
- **Standard Vocabulary**: An external RDF vocabulary (e.g., schema.org, Hydra, Dublin Core) used to align extracted concepts with well-known semantic definitions. Configurable via `--vocabularies`; schema.org and Hydra are the defaults.

## Success Criteria

### Measurable Outcomes

- **SC-001**: An LLM coding assistant can invoke the five frank-cli commands (extract, clarify, validate, diff, compile) in sequence to produce a complete ontology from a Frank project without human intervention beyond answering clarification prompts.
- **SC-002**: A Frank resource with `linkedData` enabled correctly serves all three semantic media types (JSON-LD, Turtle, RDF/XML) when requested via content negotiation.
- **SC-003**: Existing Frank applications without `linkedData` experience zero behavioral changes — all current tests continue to pass.
- **SC-004**: The MSBuild target warns at build time when `obj/frank-cli/` is missing or empty, and the runtime raises an error at startup if `linkedData` is enabled without embedded semantic definitions (FR-021).
- **SC-005**: The complete extract-to-serve pipeline (frank-cli extract → compile → Frank.LinkedData serving) works end-to-end on a sample Frank application.
- **SC-006**: All frank-cli commands produce valid, parseable JSON output that can be consumed by standard tool-use protocols.

## Assumptions

- The frank-cli tool targets the same .NET versions as Frank core (net8.0, net9.0, net10.0 multi-targeting).
- The frank-cli tool depends on FSharp.Compiler.Service for both AST parsing (untyped) and type analysis (typed). No compiled assembly is required for extraction.
- Intermediate extraction state is stored in `obj/frank-cli/` and is cleaned by `dotnet clean`.
- The OWL/XML and SHACL output formats follow W3C specifications (OWL 2, SHACL 1.0).
- RDF-star support is deferred to Phase 3 (Provenance) as noted in the tracking issue.
- Cross-assembly type extraction is out of scope for Phase 1; only types defined in the target project are mapped.
- The `linkedData` custom operation follows the same extension pattern as `Frank.Auth` and `Frank.OpenApi` (AutoOpen module with type extensions on ResourceBuilder).
- The `dotNetRdf.Core` library is adopted from Phase 1 as the shared RDF triple model for both frank-cli and Frank.LinkedData. This provides consistent serializers (JSON-LD, Turtle, RDF/XML) and a clear path to Phase 4 SPARQL support.
