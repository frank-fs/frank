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
