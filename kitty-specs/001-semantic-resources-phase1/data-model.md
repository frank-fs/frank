# Data Model: Semantic Resources Phase 1

**Date**: 2026-03-04
**Feature**: 001-semantic-resources-phase1

## Entity Relationships

```
┌─────────────────┐     extracts      ┌──────────────────┐
│  F# Source +    │────────────────────▶│  Extraction      │
│  Compiled Asm   │                    │  State           │
└─────────────────┘                    │  (obj/frank-cli/)│
                                       └────────┬─────────┘
                                                │ compile
                                                ▼
                                       ┌──────────────────┐
                                       │  Semantic        │
                                       │  Artifacts       │
                                       │  (embedded res)  │
                                       └────────┬─────────┘
                                                │ loaded by
                                                ▼
┌─────────────────┐   linkedData CE    ┌──────────────────┐
│  Frank Resource │◄───────────────────│  Frank.LinkedData│
│  (runtime)      │    reflects on     │  (serializers)   │
└─────────────────┘    return type     └──────────────────┘
```

## Core Entities

### ExtractionState

Persisted in `obj/frank-cli/` between CLI commands.

- **ontology**: OWL ontology graph (dotNetRdf `IGraph`)
- **shapes**: SHACL shapes graph (dotNetRdf `IGraph`)
- **sourceMap**: Mapping from ontology elements back to F# source locations
- **clarifications**: Resolved clarification decisions (key-value)
- **extractionMetadata**: Timestamp, source hash, tool version, base URI, vocabularies used
- **unmappedTypes**: F# types that could not be automatically mapped

**Lifecycle**: Created by `extract`, read by `clarify`/`validate`/`diff`/`compile`. Destroyed by `dotnet clean`.

### OntologyMapping (frank-cli internal)

Maps F# type system concepts to OWL/SHACL constructs:

- **F# Discriminated Union** → `owl:Class` hierarchy (union type = abstract class, cases = subclasses)
- **F# Record Type** → `owl:Class` with `owl:DatatypeProperty` per field
- **F# Record Field** → `owl:DatatypeProperty` or `owl:ObjectProperty` (depending on field type)
- **F# Option<'T>** → SHACL `sh:minCount 0` (vs `sh:minCount 1` for required)
- **F# List/Array<'T>** → SHACL `sh:maxCount` unbounded
- **Frank Route Definition** → RDF resource identity (URI derived from route pattern + base URI)
- **HTTP Method Handler** → `schema:Action` subclass + `hydra:Operation`

### SemanticArtifact

The compiled output embedded in the assembly:

- **ontology.owl.xml**: OWL/XML serialization of the full ontology
- **shapes.shacl.ttl**: Turtle serialization of SHACL shapes
- **manifest.json**: Metadata (version, base URI, source hash, vocabularies, generation timestamp)

**Embedded resource naming**: `Frank.Semantic.ontology.owl.xml`, `Frank.Semantic.shapes.shacl.ttl`, `Frank.Semantic.manifest.json`

### LinkedDataConfig (Frank.LinkedData runtime)

Configuration derived from the `linkedData` CE operation:

- **enabled**: bool (presence of the operation)
- **ontologyGraph**: Loaded from embedded `ontology.owl.xml` at startup
- **shapesGraph**: Loaded from embedded `shapes.shacl.ttl` at startup
- **baseUri**: Extracted from manifest
- **supportedMediaTypes**: `application/ld+json`, `text/turtle`, `application/rdf+xml`

### ResourceRdfProjection (Frank.LinkedData runtime)

Per-request projection of a resource's handler return value to RDF:

- **instanceGraph**: `IGraph` built via reflection on handler return type, using ontology as schema map
- **resourceUri**: Derived from route pattern + request URI
- **triples**: The handler return value's fields mapped to ontology properties

## CLI Command State Transitions

```
(no state) ──extract──▶ ExtractionState
                              │
              ┌───clarify─────┤
              │               │
              ▼               │
     (JSON questions)   ◄─────┘
              │
     extract (with params)──▶ ExtractionState (updated)
                              │
              ┌───validate────┤
              ▼               │
     (JSON report)            │
                              │
              ┌───diff────────┤
              ▼               │
     (JSON changes)           │
                              │
              └───compile─────▶ SemanticArtifact (in obj/)
                                      │
                              dotnet build
                                      │
                                      ▼
                              Embedded Resources (in assembly)
```

## Validation Rules

- `extract` MUST fail if no compiled assembly found (FR-007a)
- `clarify`/`validate`/`diff`/`compile` MUST fail if no extraction state exists
- `compile` SHOULD warn if `validate` has not been run
- `linkedData` CE MUST fail at startup if embedded resources not found (FR-021)
- MSBuild target MUST warn at build time if `obj/frank-cli/` directory missing when `Frank.Cli.MSBuild` is referenced (build-time validation)
