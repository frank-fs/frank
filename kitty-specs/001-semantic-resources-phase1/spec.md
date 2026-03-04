# Feature Specification: Semantic Resources Phase 1

**Feature Branch**: `001-semantic-resources-phase1`
**Created**: 2026-03-04
**Status**: Draft
**Input**: Implement Phase 1 of milestone #2 (tracking issue #80) — parallel workstreams for frank-cli (#79) and Frank.LinkedData (#75)
**Milestone**: v7.3.0 — Semantic Metadata-Augmented Resources
**GitHub Issues**: #80 (tracking), #79 (frank-cli), #75 (Frank.LinkedData)

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

A developer adds the `linkedData` custom operation to their Frank resource builder computation expression. At runtime, when a client sends an `Accept` header requesting `application/ld+json`, `text/turtle`, or `application/rdf+xml`, the resource responds with the appropriate semantic representation derived from its embedded ontology and current state.

**Why this priority**: This is the runtime consumer of the frank-cli output — it makes semantic representations available over HTTP. It depends on the extraction/compile pipeline (P1) being functional first.

**Independent Test**: Define a Frank resource with `linkedData` enabled, make HTTP requests with semantic `Accept` headers, and verify correct RDF responses.

**Acceptance Scenarios**:

1. **Given** a Frank resource with `linkedData` enabled and embedded semantic definitions, **When** a client requests `Accept: application/ld+json`, **Then** the resource responds with a JSON-LD representation.
2. **Given** a Frank resource with `linkedData` enabled, **When** a client requests `Accept: text/turtle`, **Then** the resource responds with a Turtle representation.
3. **Given** a Frank resource with `linkedData` enabled, **When** a client requests `Accept: application/rdf+xml`, **Then** the resource responds with an RDF/XML representation.
4. **Given** a Frank resource with `linkedData` enabled, **When** a client requests `Accept: text/html` (standard), **Then** the existing HTML/JSON content negotiation behavior is unchanged.
5. **Given** a Frank resource with `linkedData` enabled, **When** no semantic definitions exist in the assembly, **Then** an error is raised at startup (not silently ignored).

---

### User Story 5 — Analyzer Enforcement of Semantic Definitions (Priority: P2)

A developer adds the `linkedData` operation to a resource but has not run `frank-cli compile` to generate the semantic definitions. The Frank.Analyzer reports a diagnostic warning, alerting the developer that the semantic definitions are missing.

**Why this priority**: Enforces correctness at development time — prevents runtime errors by catching missing definitions during the build.

**Independent Test**: Create a Frank project with `linkedData` enabled but no embedded semantic definitions; run the analyzer and verify the diagnostic is reported.

**Acceptance Scenarios**:

1. **Given** a Frank resource using `linkedData` without corresponding semantic definitions, **When** the Frank.Analyzer runs, **Then** it reports a diagnostic warning with a clear message and the resource location.
2. **Given** a Frank resource using `linkedData` with valid semantic definitions present, **When** the Frank.Analyzer runs, **Then** no diagnostic is reported for that resource.

---

### Edge Cases

- What happens when `frank-cli extract` encounters F# types it cannot map (e.g., function types, opaque external types)? It should report them in the `clarify` output as unmapped items.
- How does the system handle partial extractions — e.g., a project with some resources having semantic definitions and others not? Only resources with `linkedData` enabled require definitions; others are unaffected.
- What happens when semantic definitions are stale (source changed after last `compile`)? The `validate` command should detect drift between source and compiled artifacts.
- What happens when multiple content negotiation formatters conflict (e.g., both MVC JSON formatter and JSON-LD formatter match `application/json`)? JSON-LD is served only for `application/ld+json`; `application/json` continues to use the existing MVC formatter.
- What happens when the embedded ontology references types from external assemblies? Phase 1 scopes extraction to the current project only; cross-assembly references are flagged as external and deferred to later phases.

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
- **FR-008**: The `clarify` command MUST return structured JSON describing ambiguities with questions, context, and suggested options.
- **FR-009**: The `validate` command MUST check completeness (unmapped types, missing relationships) and consistency of the extracted ontology.
- **FR-010**: The `diff` command MUST compare the current extraction against the previous version and return structured changes (added, removed, modified).
- **FR-011**: The `compile` command MUST generate OWL/XML and SHACL artifacts in the project's `obj/` directory.
- **FR-012**: The `compile` command MUST configure the artifacts as embedded resources so they are included in the compiled assembly by `dotnet build`.
- **FR-013**: All commands MUST output structured JSON for machine consumption by default.
- **FR-014**: All commands MUST support a human-readable text output mode.

**Frank.LinkedData (extension library)**

- **FR-015**: The library MUST provide a `linkedData` custom operation on the `ResourceBuilder` computation expression.
- **FR-016**: The library MUST extend content negotiation to serve `application/ld+json` (JSON-LD) representations.
- **FR-017**: The library MUST extend content negotiation to serve `text/turtle` (Turtle) representations.
- **FR-018**: The library MUST extend content negotiation to serve `application/rdf+xml` (RDF/XML) representations.
- **FR-019**: The library MUST NOT break existing content negotiation behavior for standard media types (HTML, JSON, XML).
- **FR-020**: The library MUST load semantic definitions from embedded assembly resources at startup.
- **FR-021**: The library MUST raise an error at startup if `linkedData` is enabled but semantic definitions are not found in the assembly.
- **FR-022**: The library MUST use a minimal custom RDF triple model — no external RDF library dependency in Phase 1.

**Frank.Analyzer (extension)**

- **FR-023**: The analyzer MUST detect when `linkedData` is used in a resource CE without corresponding semantic definitions.
- **FR-024**: The analyzer MUST report a diagnostic with a clear message, diagnostic code, and source location when semantic definitions are missing.

### Key Entities

- **OWL Ontology**: A formal description of the application's type system as OWL classes, properties, and relationships. Serialized as OWL/XML.
- **SHACL Shapes**: Constraint definitions describing the expected shape of RDF data — cardinality, value types, patterns. Derived from F# type constraints.
- **RDF Triple**: The atomic unit of the semantic model — subject, predicate, object. Implemented as a minimal custom type in Frank.LinkedData.
- **Semantic Definition Artifact**: The compiled OWL/XML and SHACL files embedded as assembly resources, produced by `frank-cli compile`.
- **Resource Identity**: The RDF URI corresponding to a Frank route definition, mapping route patterns to semantic identifiers.

## Success Criteria

### Measurable Outcomes

- **SC-001**: An LLM coding assistant can invoke the five frank-cli commands (extract, clarify, validate, diff, compile) in sequence to produce a complete ontology from a Frank project without human intervention beyond answering clarification prompts.
- **SC-002**: A Frank resource with `linkedData` enabled correctly serves all three semantic media types (JSON-LD, Turtle, RDF/XML) when requested via content negotiation.
- **SC-003**: Existing Frank applications without `linkedData` experience zero behavioral changes — all current tests continue to pass.
- **SC-004**: The Frank.Analyzer detects 100% of cases where `linkedData` is used without semantic definitions and reports a clear diagnostic.
- **SC-005**: The complete extract-to-serve pipeline (frank-cli extract → compile → Frank.LinkedData serving) works end-to-end on a sample Frank application.
- **SC-006**: All frank-cli commands produce valid, parseable JSON output that can be consumed by standard tool-use protocols.

## Assumptions

- The frank-cli tool targets the same .NET versions as Frank core (net8.0, net9.0, net10.0 multi-targeting).
- The OWL/XML and SHACL output formats follow W3C specifications (OWL 2, SHACL 1.0).
- RDF-star support is deferred to Phase 3 (Provenance) as noted in the tracking issue.
- Cross-assembly type extraction is out of scope for Phase 1; only types defined in the target project are mapped.
- The `linkedData` custom operation follows the same extension pattern as `Frank.Auth` and `Frank.OpenApi` (AutoOpen module with type extensions on ResourceBuilder).
- The custom RDF triple model in Phase 1 is intentionally minimal; it may be replaced by dotNetRDF or similar in Phase 4 when SPARQL support is needed.
