# Data Model: RDF SPARQL Validation

**Feature**: 015-rdf-sparql-validation
**Date**: 2026-03-15

## Overview

This is a test-only feature. There are no new domain entities, database tables, or runtime types. The "data model" describes the test infrastructure types and the RDF graph structures being validated.

## Test Infrastructure Types

### TestApp Configuration

The test project creates TestHost instances with the following configuration:

| Component | Type | Source |
|-----------|------|--------|
| Ontology graph | `VDS.RDF.IGraph` | Constructed in-test with OWL property declarations |
| LinkedData config | `Frank.LinkedData.LinkedDataConfig` | Wraps ontology graph + base URI + manifest |
| Provenance store | `Frank.Provenance.IProvenanceStore` | In-memory store seeded with test records |
| Transition subject | `IObservable<TransitionEvent>` | For pushing provenance events in tests |

### SPARQL Query Patterns Validated

These are the query shapes executed against loaded graphs. Each maps to a spec requirement:

| Pattern | SPARQL Type | Target | Requirement |
|---------|-------------|--------|-------------|
| All resources with types | SELECT | Default graph | FR-005 |
| Unsafe transitions (POST/PUT/DELETE) | SELECT | Default graph | FR-006 |
| ALPS semantic descriptors | SELECT | Default graph | FR-007 |
| Resource existence check | ASK | Default graph | FR-005 |
| Provenance activities with agents | SELECT | Named graph (GRAPH clause) | FR-010 |
| Cross-resource link traversal | SELECT | Combined graph | FR-008 |
| Orphaned blank node detection | SELECT | Combined graph | FR-011 |
| Consistent namespace predicates | SELECT | Combined graph | FR-012 |

## RDF Graph Structures Under Test

### Resource Graph (from Frank.LinkedData)

Triples produced by `projectJsonToRdf` follow the pattern:
```
<resource-uri> <ontology-property-uri> "literal-value" .
<resource-uri> rdf:type <ontology-class-uri> .
```

Namespace prefixes registered by LinkedData:
- Standard: `rdf:`, `rdfs:`, `owl:`, `xsd:`
- Application-specific: based on `LinkedDataConfig.BaseUri`

### Provenance Graph (from Frank.Provenance)

Triples produced by `GraphBuilder.toGraph` follow PROV-O patterns:
```
<activity-uri> rdf:type prov:Activity .
<activity-uri> prov:wasAssociatedWith <agent-uri> .
<activity-uri> prov:used <entity-uri> .
<activity-uri> prov:startedAtTime "datetime"^^xsd:dateTime .
<entity-uri> rdf:type prov:Entity .
<entity-uri> prov:wasGeneratedBy <activity-uri> .
<entity-uri> prov:wasDerivedFrom <prev-entity-uri> .
<agent-uri> rdf:type prov:Person .  (or prov:SoftwareAgent)
```

Namespace prefixes registered by Provenance (`GraphBuilder.registerPrefixes`):
- `prov:` -> `http://www.w3.org/ns/prov#`
- `frank:` -> `https://frank-web.dev/ns/provenance#`
- `xsd:` -> `http://www.w3.org/2001/XMLSchema#`
- `rdf:` -> `http://www.w3.org/1999/02/22-rdf-syntax-ns#`
- `rdfs:` -> `http://www.w3.org/2000/01/rdf-schema#`

## Relationships

```
Frank.RdfValidation.Tests
  ├── references ──> Frank.LinkedData (project ref)
  │                   └── produces resource RDF via LinkedDataMiddleware
  ├── references ──> Frank.Provenance (project ref)
  │                   └── produces provenance RDF via ProvenanceMiddleware + GraphBuilder
  └── uses ────────> dotNetRdf.Core 3.5.1 (transitive + possibly explicit)
                      ├── IGraph, TripleStore (RDF storage)
                      ├── TurtleParser, RdfXmlParser, JsonLdParser (parsing)
                      ├── SparqlQueryParser (query parsing)
                      ├── LeviathanQueryProcessor (query execution)
                      └── InMemoryDataset (query dataset)
```
