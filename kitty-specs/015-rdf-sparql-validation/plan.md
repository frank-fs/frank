# Implementation Plan: RDF SPARQL Validation

**Branch**: `015-rdf-sparql-validation` | **Date**: 2026-03-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/kitty-specs/015-rdf-sparql-validation/spec.md`

## Summary

This is a test-only feature that validates Frank's RDF output (produced by Frank.LinkedData and Frank.Provenance) is well-formed, produces coherent graphs, and is queryable via SPARQL. The deliverable is a single test project (`Frank.RdfValidation.Tests`) that uses ASP.NET Core TestHost to spin up Frank apps with linked data and provenance enabled, makes HTTP requests with RDF Accept headers, loads responses into dotNetRdf in-memory graphs, and executes SPARQL queries to assert correctness. No runtime library code is produced. Test cases serve as documented examples of useful SPARQL query patterns.

## Technical Context

**Language/Version**: F# 8.0+ targeting .NET 10.0 (single target, matching existing test projects)
**Primary Dependencies**: dotNetRdf.Core 3.5.1 (already a Frank dependency -- provides RDF parsing, in-memory SPARQL via `LeviathanQueryProcessor`/`InMemoryDataset`), Microsoft.AspNetCore.TestHost 10.0.0, Expecto 10.2.3, YoloDev.Expecto.TestSdk 0.14.3, Microsoft.NET.Test.Sdk 17.14.1
**Storage**: N/A (in-memory dotNetRdf graphs only)
**Testing**: Expecto (matching all other Frank test projects)
**Target Platform**: .NET 10.0 (test execution only)
**Project Type**: Test project -- no packaged output
**Performance Goals**: N/A (test-time only; in-memory SPARQL on small test graphs)
**Constraints**: Depends on Frank.LinkedData (spec 001, #75) and Frank.Provenance (spec 006, #77) being implemented and producing RDF output
**Scale/Scope**: ~4 test modules covering 4 user stories, ~15-20 test cases total

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Resource-Oriented Design | PASS | Tests validate RDF output of resource-oriented APIs; does not introduce route-centric patterns |
| II. Idiomatic F# | PASS | Test project uses F# idioms (Expecto, pipeline operators, pattern matching) |
| III. Library, Not Framework | PASS | Test-only project; no runtime code or opinions imposed |
| IV. ASP.NET Core Native | PASS | Uses standard TestHost + HttpClient patterns |
| V. Performance Parity | N/A | Test project; no runtime performance impact |
| VI. Resource Disposal Discipline | PASS | All `IGraph`, `ITripleStore`, `HttpClient`, `TestServer`, `StreamReader` values must use `use` bindings |
| VII. No Silent Exception Swallowing | PASS | Test code; exceptions propagate naturally as test failures |
| VIII. No Duplicated Logic Across Modules | PASS | Shared test helpers (graph loading, SPARQL execution, TestHost creation) extracted to a common helpers module |

## Project Structure

### Documentation (this feature)

```
kitty-specs/015-rdf-sparql-validation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output (minimal -- test-only feature)
├── quickstart.md        # Phase 1 output
└── tasks.md             # Phase 2 output (/spec-kitty.tasks command)
```

### Source Code (repository root)

```
test/
└── Frank.RdfValidation.Tests/
    ├── Frank.RdfValidation.Tests.fsproj   # Test project file
    ├── TestHelpers.fs                      # Shared helpers: TestHost setup, graph loading, SPARQL execution
    ├── RdfParsingTests.fs                  # US1: RDF output parses in all three formats, isomorphism
    ├── SparqlResourceQueryTests.fs         # US2: SPARQL SELECT/ASK against resource graph
    ├── ProvenanceGraphTests.fs             # US3: Named graph isolation for provenance
    ├── GraphCoherenceTests.fs              # US4: Cross-resource graph coherence
    └── Program.fs                          # Expecto entry point
```

**Structure Decision**: Single test project under `test/` following the existing convention (e.g., `test/Frank.LinkedData.Tests/`, `test/Frank.Provenance.Tests/`). The project references both `Frank.LinkedData` and `Frank.Provenance` as project references. One module per user story keeps test organization clear. A shared `TestHelpers.fs` module avoids duplicated logic (Constitution VIII) for common operations: creating TestHost instances, loading RDF response bodies into graphs, and executing SPARQL queries with result assertions.

## Design Decisions

### D1: TestHost Integration Test Approach

Tests use `Microsoft.AspNetCore.TestHost` to spin up a Frank application with LinkedData and Provenance middleware enabled. HTTP requests with appropriate `Accept` headers (`text/turtle`, `application/ld+json`, `application/rdf+xml`) retrieve RDF serializations. Response bodies are parsed into dotNetRdf `IGraph` instances for SPARQL validation.

**Rationale**: This validates the full pipeline (resource definition -> handler -> middleware -> content negotiation -> RDF serialization) rather than testing serialization in isolation. Matches the confirmed test approach (PQ1: B).

### D2: In-Memory SPARQL Execution via dotNetRdf

SPARQL queries execute against in-memory graphs using dotNetRdf's `LeviathanQueryProcessor` with `InMemoryDataset`. No external triple store is needed.

**Rationale**: dotNetRdf.Core 3.5.1 is already a Frank dependency. Its in-memory SPARQL 1.1 engine supports SELECT, ASK, and GRAPH clause queries -- everything the spec requires. This keeps the test project self-contained.

### D3: Graph Isomorphism for Cross-Format Validation

Triple equivalence across JSON-LD, Turtle, and RDF/XML formats uses dotNetRdf's `GraphDiff` or manual triple-count + subject-set comparison (accounting for blank node renaming).

**Rationale**: The spec explicitly requires isomorphism checks (FR-004). dotNetRdf provides `GraphDiff` which handles blank node renaming.

### D4: Named Graphs for Provenance Isolation

Provenance RDF is loaded into a dotNetRdf `TripleStore` (which implements `ITripleStore`) with named graphs. SPARQL queries use the `GRAPH` clause to verify isolation between resource and provenance data.

**Rationale**: The spec requires named graph isolation (FR-009, FR-010). dotNetRdf's `TripleStore` with `InMemoryDataset` supports named graph SPARQL queries natively.

### D5: Test Names as Documentation

Each test case has a descriptive name following the pattern `"US#-SC#: <description of what the SPARQL query validates>"`. Inline comments in the SPARQL query strings explain the query purpose and expected results.

**Rationale**: The spec explicitly requires test cases to serve as documented examples (FR-013, SC-006). No separate docs file is needed.

## Complexity Tracking

*No constitution violations. Test-only project with minimal complexity.*
