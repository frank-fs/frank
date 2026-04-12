---
source: kitty-specs/015-rdf-sparql-validation
status: complete
type: spec
---

# Feature Specification: RDF SPARQL Validation

**Feature Branch**: `015-rdf-sparql-validation`
**Created**: 2026-03-15
**Status**: Done
**GitHub Issue**: #78
**Dependencies**: Frank.LinkedData (#75, spec 001), Frank.Provenance (#77, spec 006)
**Input**: Validate that Frank's RDF output is well-formed and queryable by SPARQL, ensuring interoperability with external triple stores

## Clarifications

### Session 2026-03-15

- Q: How should validation against external triple stores be implemented? → A: Use `dotNetRdf.Core`'s in-memory SPARQL engine (already a Frank dependency) to parse and query all RDF output. No external tooling (Docker, Jena, Oxigraph) required.
- Q: Does this spec produce runtime library code? → A: No. This is a test-only and documentation feature. Deliverables are a test project and documented SPARQL query examples (inline as test cases).
- Q: Where should example SPARQL queries live? → A: Inline as documented test cases in the test project. Test names and comments serve as the documentation. No separate `docs/` file.

## Overview

This feature validates that Frank's RDF output -- produced by Frank.LinkedData (content negotiation for `application/ld+json`, `text/turtle`, `application/rdf+xml`) and Frank.Provenance (PROV-O state change annotations) -- is well-formed, produces coherent graphs, and is queryable via SPARQL. No runtime library code is produced. The deliverable is a test project that loads Frank's RDF output into `dotNetRdf.Core` in-memory graphs, executes SPARQL queries, and asserts correctness. The test cases themselves serve as documented examples of useful SPARQL patterns against Frank's RDF model.

## User Scenarios & Testing

### User Story 1 - RDF Output Parses Successfully in All Three Formats (Priority: P1)

A developer producing RDF output via Frank.LinkedData needs confidence that all three serialization formats (JSON-LD, Turtle, RDF/XML) are syntactically valid and parseable. The test project loads each format into a dotNetRdf in-memory graph and verifies no parse errors occur and the resulting triples are equivalent across formats.

**Why this priority**: If the RDF output cannot be parsed, no downstream SPARQL queries or external tool integrations are possible. This is the foundation for all other validation.

**Independent Test**: Serialize a Frank resource to all three RDF formats, parse each into a dotNetRdf graph, and verify triple counts and subjects match across all three.

**Acceptance Scenarios**:

1. **Given** a Frank resource with linked data enabled, **When** its JSON-LD representation is loaded into a dotNetRdf graph, **Then** the graph contains the expected number of triples with no parse errors.
2. **Given** the same Frank resource, **When** its Turtle representation is loaded into a dotNetRdf graph, **Then** the resulting triples are isomorphic to the JSON-LD graph.
3. **Given** the same Frank resource, **When** its RDF/XML representation is loaded into a dotNetRdf graph, **Then** the resulting triples are isomorphic to the JSON-LD and Turtle graphs.
4. **Given** an RDF representation with prefixed namespaces, **When** parsed, **Then** all prefixes resolve to valid URIs and no undefined-prefix errors occur.

---

### User Story 2 - SPARQL Queries Against Resource Graph (Priority: P1)

A developer (or agent) wants to query Frank's RDF model to discover resource capabilities, relationships, and structure. The test project demonstrates that standard SPARQL SELECT and ASK queries return correct results when executed against the loaded graph.

**Why this priority**: SPARQL queryability is the core value proposition of this feature -- proving Frank's RDF output is not just parseable but practically useful for graph queries.

**Independent Test**: Load Frank's RDF output into a dotNetRdf graph, execute SPARQL queries for common patterns (find resources, find transitions, find capabilities), and assert expected result sets.

**Acceptance Scenarios**:

1. **Given** a loaded RDF graph from Frank resources, **When** a SPARQL SELECT query asks for all resources with their types, **Then** the result set contains all defined resources with correct `rdf:type` values.
2. **Given** a loaded RDF graph with HTTP method capabilities, **When** a SPARQL SELECT query asks for all resources with unsafe transitions (POST, PUT, DELETE), **Then** only resources with those method handlers appear in results.
3. **Given** a loaded RDF graph with ALPS descriptors, **When** a SPARQL SELECT query asks for resources with semantic descriptors, **Then** the result set includes descriptor types and link relations.
4. **Given** a loaded RDF graph, **When** a SPARQL ASK query checks whether a specific resource exists with a specific capability, **Then** the query returns true for existing capabilities and false for non-existent ones.

---

### User Story 3 - Provenance Graph Coherence and Named Graph Isolation (Priority: P2)

A developer using Frank.Provenance needs assurance that provenance triples (PROV-O annotations) form a coherent sub-graph that can be queried independently via named graphs, without interfering with the resource model graph.

**Why this priority**: Named graph isolation is important for provenance use cases (audit, compliance) but depends on the resource graph validation (P1) being correct first.

**Independent Test**: Load both resource RDF and provenance RDF into a dotNetRdf dataset with named graphs, execute SPARQL queries scoped to specific named graphs, and verify isolation between resource and provenance data.

**Acceptance Scenarios**:

1. **Given** a resource with provenance records, **When** both resource and provenance RDF are loaded into named graphs in a dotNetRdf dataset, **Then** a SPARQL query against the provenance named graph returns only PROV-O triples (not resource triples).
2. **Given** a provenance named graph, **When** a SPARQL query asks for all `prov:Activity` instances with their agents and timestamps, **Then** the result set matches the recorded state transitions.
3. **Given** multiple resources with provenance, **When** provenance graphs are loaded, **Then** each resource's provenance is scoped to its own named graph (no cross-resource leakage).
4. **Given** a SPARQL query using `GRAPH` clause to target a specific provenance named graph, **When** executed against the full dataset, **Then** only triples from that named graph are returned.

---

### User Story 4 - Graph Coherence Across Related Resources (Priority: P2)

A developer building a multi-resource Frank application needs confidence that the combined RDF graph from all resources forms a coherent whole -- consistent URI schemes, proper blank node scoping, and valid cross-resource references.

**Why this priority**: Individual resource validation (P1) does not guarantee coherence when resources are combined. This validates the aggregate graph.

**Independent Test**: Load RDF from multiple related Frank resources into a single graph, execute SPARQL queries that traverse cross-resource relationships, and verify link integrity.

**Acceptance Scenarios**:

1. **Given** multiple Frank resources loaded into a single RDF graph, **When** a SPARQL query traverses a link relation from resource A to resource B, **Then** the target URI in resource A matches the subject URI of resource B.
2. **Given** a combined graph, **When** a SPARQL query checks for orphaned blank nodes (blank nodes not referenced by any named resource), **Then** no orphaned blank nodes exist.
3. **Given** a combined graph with resources using the same ontology namespace, **When** a SPARQL query asks for all distinct predicates, **Then** predicates use consistent namespace prefixes (no mixed absolute/prefixed URIs for the same predicate).

---

### Edge Cases

- What happens when a resource has no handlers (empty resource definition)? The RDF output should still be parseable (empty or minimal graph), and SPARQL queries should return empty result sets without errors.
- How does the system handle blank nodes in JSON-LD vs Turtle vs RDF/XML? Blank node identifiers are format-specific; isomorphism checks must account for blank node renaming across formats.
- What happens when provenance is enabled but no state transitions have occurred? The provenance named graph should exist but be empty, and SPARQL queries should return empty results (not errors).
- What happens when a resource URI contains special characters (encoded path segments)? RDF URIs must be properly percent-encoded, and SPARQL queries using those URIs must match.
- How does the system handle very large graphs (hundreds of resources)? The in-memory dotNetRdf engine should handle test-scale graphs without issue; production-scale validation is out of scope.

## Requirements

### Functional Requirements

- **FR-001**: The test project MUST parse Frank's JSON-LD output (`application/ld+json`) into a dotNetRdf in-memory graph without errors.
- **FR-002**: The test project MUST parse Frank's Turtle output (`text/turtle`) into a dotNetRdf in-memory graph without errors.
- **FR-003**: The test project MUST parse Frank's RDF/XML output (`application/rdf+xml`) into a dotNetRdf in-memory graph without errors.
- **FR-004**: The test project MUST verify that the three serialization formats produce isomorphic graphs (same triples modulo blank node identity).
- **FR-005**: The test project MUST execute SPARQL SELECT queries against loaded graphs and assert expected result sets for resource discovery patterns.
- **FR-006**: The test project MUST execute SPARQL SELECT queries that discover HTTP method capabilities (safe, unsafe, idempotent transitions) on resources.
- **FR-007**: The test project MUST execute SPARQL SELECT queries that retrieve ALPS semantic descriptors and link relations from the loaded graph.
- **FR-008**: The test project MUST validate that resource relationships produce consistent cross-resource URI references in the combined graph.
- **FR-009**: The test project MUST load provenance RDF into named graphs within a dotNetRdf dataset and verify isolation from the resource model graph.
- **FR-010**: The test project MUST execute SPARQL queries scoped to provenance named graphs using the `GRAPH` clause, returning only PROV-O triples.
- **FR-011**: The test project MUST verify that blank nodes are properly scoped (no cross-resource blank node leakage in the combined graph).
- **FR-012**: The test project MUST verify that all namespace prefixes in the RDF output resolve to valid URIs.
- **FR-013**: Test cases MUST serve as documented examples of useful SPARQL query patterns, with descriptive test names and inline comments explaining the query purpose and expected results.

### Key Entities

- **RDF Graph**: An in-memory dotNetRdf `IGraph` loaded from Frank's serialized RDF output. The unit of validation for parse correctness and SPARQL queryability.
- **RDF Dataset**: A dotNetRdf `ITripleStore` containing multiple named graphs, used to validate provenance named graph isolation and cross-graph SPARQL queries.
- **SPARQL Query**: A W3C SPARQL 1.1 SELECT or ASK query executed against the loaded graph/dataset via dotNetRdf's in-memory query processor.
- **Triple Isomorphism**: Structural equivalence of two RDF graphs -- same triples modulo blank node identity. Used to validate cross-format consistency.

## Success Criteria

### Measurable Outcomes

- **SC-001**: All three RDF serialization formats (JSON-LD, Turtle, RDF/XML) produced by Frank.LinkedData parse without errors and produce isomorphic graphs.
- **SC-002**: SPARQL queries for resource discovery, capability inspection, and descriptor retrieval return correct, non-empty result sets against the loaded graph.
- **SC-003**: Provenance triples are isolated in named graphs -- SPARQL queries scoped to a provenance named graph return zero resource-model triples, and vice versa.
- **SC-004**: The combined graph from multiple resources has zero orphaned blank nodes and uses consistent namespace prefixes throughout.
- **SC-005**: All test cases pass under `dotnet test` targeting net10.0 (matching existing test projects).
- **SC-006**: Test names and inline comments provide sufficient documentation of SPARQL query patterns that a developer can understand the query purpose without external documentation.

## Assumptions

- Frank.LinkedData (spec 001, #75) and Frank.Provenance (spec 006, #77) are implemented and producing RDF output before this test project can execute meaningful tests.
- The test project depends on `dotNetRdf.Core` (already a Frank dependency) for RDF parsing and in-memory SPARQL query execution.
- The test project targets net10.0 only, consistent with other Frank test projects.
- Named graphs for provenance follow the convention established in spec 006 (PROV-O State Change Tracking).
- The SPARQL queries use SPARQL 1.1 syntax, which dotNetRdf supports.
- Test data is generated programmatically from Frank resource definitions within the test project -- no external test data files are needed.
- This spec produces no NuGet package -- the test project is for internal validation only.


## Research

# Research: RDF SPARQL Validation

**Feature**: 015-rdf-sparql-validation
**Date**: 2026-03-15

## R1: dotNetRdf In-Memory SPARQL Query Execution

**Decision**: Use `LeviathanQueryProcessor` with `InMemoryDataset` for all SPARQL query execution in tests.

**Rationale**: dotNetRdf.Core 3.5.1 (already a Frank dependency) provides a complete SPARQL 1.1 query engine via `LeviathanQueryProcessor`. The `InMemoryDataset` wraps a `TripleStore` (for named graph support) or a single `IGraph`. Query parsing uses `SparqlQueryParser` (already used in `Frank.Validation.ShapeMerger`). This approach requires no additional dependencies.

**Usage pattern** (from dotNetRdf documentation):
```fsharp
open VDS.RDF
open VDS.RDF.Query
open VDS.RDF.Query.Datasets
open VDS.RDF.Parsing

// Single graph query
let graph = new Graph()
// ... load triples ...
let dataset = InMemoryDataset(graph)
let processor = LeviathanQueryProcessor(dataset)
let parser = SparqlQueryParser()
let query = parser.ParseFromString("SELECT ?s ?p ?o WHERE { ?s ?p ?o }")
let results = processor.ProcessQuery(query)
// results is SparqlResultSet for SELECT, bool for ASK

// Named graph query
let store = new TripleStore()
store.Add(graph)  // default graph
store.Add(provenanceGraph, namedGraphUri)  // named graph
let dataset2 = InMemoryDataset(store)
let processor2 = LeviathanQueryProcessor(dataset2)
// Use GRAPH clause in SPARQL to target named graphs
```

**Alternatives considered**:
- External SPARQL endpoint (Jena, Oxigraph via Docker): Rejected per spec clarification -- adds complexity and CI infrastructure requirements
- Direct triple enumeration without SPARQL: Rejected -- the spec's core value is proving SPARQL queryability, not just triple existence

## R2: Graph Isomorphism in dotNetRdf

**Decision**: Use `GraphDiff` from dotNetRdf for cross-format isomorphism checks.

**Rationale**: dotNetRdf provides `GraphDiff` which computes the difference between two graphs, handling blank node renaming. Two graphs are isomorphic when `GraphDiff` reports no added or removed triples.

**Usage pattern**:
```fsharp
open VDS.RDF

let diff = graph1.Difference(graph2)
// diff.AreEqual -> true if isomorphic (handles blank node renaming)
// diff.AddedTriples / diff.RemovedTriples for diagnostics
```

**Alternatives considered**:
- Manual triple count + subject comparison: Insufficient -- does not handle blank node identity differences across serialization formats
- Canonical serialization comparison: Fragile -- formatting differences between parsers would cause false negatives

## R3: TestHost Pattern for RDF Content Negotiation

**Decision**: Create TestHost instances with both LinkedData and Provenance middleware enabled, using the existing patterns from `Frank.LinkedData.Tests.ContentNegotiationTests` and `Frank.Provenance.Tests.IntegrationTests`.

**Rationale**: Both existing test projects demonstrate the pattern: `HostBuilder` + `UseTestServer` + `ConfigureServices` + `Configure` with middleware + endpoint registration. The RDF validation tests need to combine both middlewares and register Frank resource definitions with linked data markers.

**Key pattern elements**:
1. Register `LinkedDataConfig` with ontology graph in DI
2. Register `IObservable<TransitionEvent>` and `IProvenanceStore` in DI
3. Apply `linkedDataMiddleware` and `provenanceMiddleware` in the pipeline
4. Mark endpoints with `LinkedDataMarker` metadata
5. Use `HttpClient.SendAsync` with Accept headers to request specific RDF formats
6. Parse response bodies using format-specific dotNetRdf parsers (`TurtleParser`, `RdfXmlParser`, `JsonLdParser`)

**Alternatives considered**:
- Direct API calls to serialization functions: Rejected -- user confirmed TestHost approach (PQ1: B) to validate full pipeline
- WebApplicationFactory: Rejected -- existing Frank test projects use `HostBuilder` + `TestServer` directly, which is simpler for middleware-level testing

## R4: Named Graph Conventions for Provenance

**Decision**: Follow the convention from Frank.Provenance where provenance graphs use resource-scoped named graph URIs.

**Rationale**: The Provenance middleware (spec 006) serializes provenance records into named graphs based on the resource URI. The test project loads these into a `TripleStore` with the same named graph URIs, enabling SPARQL `GRAPH` clause queries.

**From `Frank.Provenance.GraphBuilder`**: The `toGraph` function creates a single `IGraph` from a list of `ProvenanceRecord` values. For named graph testing, each resource's provenance records should be loaded into a separate named graph within a `TripleStore`.

**Alternatives considered**:
- Single default graph for all provenance: Rejected -- spec explicitly requires named graph isolation (US3, FR-009, FR-010)

## R5: dotNetRdf Package -- No Additional NuGet Needed

**Decision**: Reference `dotNetRdf.Core` 3.5.1 as a transitive dependency via `Frank.LinkedData` and `Frank.Provenance` project references. If needed, add an explicit `PackageReference` to ensure SPARQL query types are available.

**Rationale**: Both `Frank.LinkedData.fsproj` and `Frank.Provenance.fsproj` already reference `dotNetRdf.Core` version 3.5.1. The test project references both as project dependencies, so dotNetRdf types should be transitively available. However, an explicit package reference may be needed for `VDS.RDF.Query` namespace types (`SparqlQueryParser`, `LeviathanQueryProcessor`, `InMemoryDataset`) if transitive exposure is insufficient.

**Alternatives considered**:
- Full `dotNetRdf` metapackage: Rejected -- only `dotNetRdf.Core` is needed; the metapackage includes unnecessary database providers
