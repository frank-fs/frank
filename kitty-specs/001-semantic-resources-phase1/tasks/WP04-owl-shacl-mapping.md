---
work_package_id: WP04
title: OWL/SHACL Mapping Engine
lane: "doing"
dependencies:
- WP02
- WP03
base_branch: 001-semantic-resources-phase1-WP02
base_commit: 315030704c62f190579e2cb728841588bb44cd61
created_at: '2026-03-05T19:31:25.894255+00:00'
subtasks:
- T018
- T019
- T020
- T021
- T022
- T023
phase: Phase 1 - Mapping
assignee: ''
agent: ''
shell_pid: "94273"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-04T22:10:13Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-002
- FR-003
- FR-004
- FR-005
- FR-006
- FR-007
---

# WP04: OWL/SHACL Mapping Engine

## Implementation Command

```
spec-kitty implement WP04 --base WP03
```

## Objectives

Transform analyzed F# types and routes (output of WP03 analyzers) into OWL ontology classes/properties and SHACL shapes stored in dotNetRdf `IGraph` instances.

## Context

- Input types come from WP03: `AnalyzedType`, `AnalyzedResource`, `AnalyzedEndpoint`
- Output is a pair of dotNetRdf `IGraph` instances: an ontology graph (OWL) and a shapes graph (SHACL)
- Mapping rules are defined in `data-model.md`'s OntologyMapping section — read this before implementing
- Use `dotNetRdf.Ontology`'s `OntologyGraph` for building class hierarchies when it simplifies the API; fall back to raw triple assertion (using WP02's `FSharpRdf` wrappers) for fine-grained control
- Vocabulary constants from WP02's `Vocabularies` module must be used throughout — no hardcoded URI strings
- Default vocabulary set: schema.org + Hydra; configurable via a `MappingConfig` record

## Subtask Details

### T018: TypeMapper.fs — F# Types to OWL Classes

**Module**: `Frank.Cli.Core.Extraction.TypeMapper`

**File location**: `src/Frank.Cli.Core/Extraction/TypeMapper.fs`

Define configuration:

```fsharp
type MappingConfig = {
    BaseUri: Uri
    Vocabularies: string list  // e.g. ["schema.org"; "hydra"]
}
```

Define output:

```fsharp
type MappedClass = {
    ClassUri: Uri
    Label: string
    Properties: MappedProperty list
    SuperClasses: Uri list
}

and MappedProperty = {
    PropertyUri: Uri
    Label: string
    Domain: Uri
    Range: Uri    // OWL XSD datatype or class URI
    IsObjectProperty: bool
}
```

URI generation conventions:
- Class URI: `{config.BaseUri}/types/{TypeName}`
- Property URI: `{config.BaseUri}/properties/{TypeName}/{FieldName}`

F# DU mapping rules:
- The union type itself becomes an `owl:Class` declared with `rdf:type owl:Class`
- Each case becomes a subclass: `rdf:type owl:Class` + `rdfs:subClassOf {unionClassUri}`
- If a case has fields (inline record), generate `owl:DatatypeProperty` or `owl:ObjectProperty` for each field scoped to the case class
- Apply `rdfs:label` to each class using the type/case name

F# Record mapping rules:
- Record type → `owl:Class`
- Each field → `owl:DatatypeProperty` (for primitive `FieldKind`) or `owl:ObjectProperty` (for `Reference` or `Collection` FieldKind)
- Set `rdfs:domain` to the record class URI
- Set `rdfs:range` to the XSD datatype URI (primitives) or the referenced class URI (object properties)
- For `Collection` fields: use `rdfs:range` pointing to the element type; add `owl:someValuesFrom` restriction if needed

`FieldKind` to OWL range mapping:
- `Primitive "xsd:string"` → `http://www.w3.org/2001/XMLSchema#string`
- `Primitive "xsd:integer"` → `http://www.w3.org/2001/XMLSchema#integer`
- `Primitive "xsd:double"` → `http://www.w3.org/2001/XMLSchema#double`
- `Primitive "xsd:boolean"` → `http://www.w3.org/2001/XMLSchema#boolean`
- `Primitive "xsd:dateTime"` → `http://www.w3.org/2001/XMLSchema#dateTime`
- `Optional inner` → use inner's range, mark as optional in SHACL (not here)
- `Collection element` → use element's range; property is an `owl:ObjectProperty` or `owl:DatatypeProperty` depending on element type
- `Reference typeName` → `{config.BaseUri}/types/{typeName}`

Main function:
- `mapTypes : MappingConfig -> AnalyzedType list -> IGraph` — returns an OWL ontology graph populated with all class and property declarations

Assert all triples using WP02's `FSharpRdf.assertTriple`. Do not use dotNetRdf fluent API directly.

### T019: RouteMapper.fs — Routes to RDF Resources

**Module**: `Frank.Cli.Core.Extraction.RouteMapper`

**File location**: `src/Frank.Cli.Core/Extraction/RouteMapper.fs`

Define output:

```fsharp
type MappedResource = {
    ResourceUri: Uri
    RouteTemplate: string
    UriTemplate: string   // RFC 6570 template: {baseUri}/resources/products/{id}
    Name: string option
    LinkedClass: Uri option  // if determinable from handler return type
}
```

URI template generation:
- Take a Frank route template like `/products/{id}` and produce `{config.BaseUri}/resources/products/{id}`
- Route parameter placeholders (`{id}`, `{name}`, etc.) are preserved verbatim in the URI template

RDF triple assertions per route:
- `<resourceUri> rdf:type hydra:Resource`
- `<resourceUri> rdfs:label "routeName"` (use the `Name` field if present, otherwise derive from route template)
- `<resourceUri> hydra:template "{uriTemplate}"^^xsd:string`
- If `LinkedClass` is known: `<resourceUri> hydra:supportedClass <classUri>`

Main function:
- `mapRoutes : MappingConfig -> AnalyzedResource list -> IGraph` — returns a graph with `hydra:Resource` individuals for each route

For determining `LinkedClass`: attempt to match the route name or template against known type names from `AnalyzedType list`. Accept an optional `AnalyzedType list` parameter for cross-referencing.

### T020: CapabilityMapper.fs — HTTP Methods to RDF Operations

**Module**: `Frank.Cli.Core.Extraction.CapabilityMapper`

**File location**: `src/Frank.Cli.Core/Extraction/CapabilityMapper.fs`

Map each HTTP method to both a Schema.org action and a Hydra operation:

| HTTP Method | Schema.org Action | Hydra method literal |
|-------------|-------------------|----------------------|
| GET | `schema:ReadAction` | `"GET"` |
| POST | `schema:CreateAction` | `"POST"` |
| PUT | `schema:UpdateAction` | `"PUT"` |
| DELETE | `schema:DeleteAction` | `"DELETE"` |
| PATCH | `schema:UpdateAction` | `"PATCH"` |

For each resource+method combination, create a `hydra:Operation` blank node and assert:
- `<operation> rdf:type hydra:Operation`
- `<operation> rdf:type schema:{ActionType}`
- `<operation> hydra:method "{METHOD}"^^xsd:string`

Link the operation to its resource:
- `<resourceUri> hydra:supportedOperation <operation>`

Main function:
- `mapCapabilities : MappingConfig -> AnalyzedResource list -> IGraph` — returns graph with all operations linked to their resources

Operations use blank nodes (generate unique blank node IDs per operation, e.g., `_:op_{routeSlug}_{method}`).

### T021: ShapeGenerator.fs — SHACL Shapes from OWL Classes

**Module**: `Frank.Cli.Core.Extraction.ShapeGenerator`

**File location**: `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs`

For each `AnalyzedType` (records and DU cases), generate a `sh:NodeShape`:

```
<shapeUri> rdf:type sh:NodeShape .
<shapeUri> sh:targetClass <classUri> .
<shapeUri> sh:property <propertyConstraint> .
```

Shape URI convention: `{config.BaseUri}/shapes/{TypeName}Shape`

For each field, create a `sh:PropertyShape` blank node:
```
<propConstraint> rdf:type sh:PropertyShape .
<propConstraint> sh:path <propertyUri> .
<propConstraint> sh:datatype <xsdType> .          // for primitives
<propConstraint> sh:class <classUri> .             // for object properties
<propConstraint> sh:minCount "1"^^xsd:integer .   // for required fields
<propConstraint> sh:minCount "0"^^xsd:integer .   // for Optional fields
```

Cardinality rules derived from `FieldKind`:
- `Primitive _` (not wrapped in Optional): `sh:minCount 1`
- `Optional _`: `sh:minCount 0` (no maxCount)
- `Collection _`: `sh:minCount 0` (no maxCount — collections can be empty)
- `Reference _`: `sh:minCount 1` and use `sh:class` instead of `sh:datatype`

For DUs: generate a `sh:NodeShape` for each case class (from WP03's DuCase). The union superclass gets a shape with `sh:or` pointing to each case shape if desired for validation; otherwise, individual case shapes are sufficient for Phase 1.

Main function:
- `generateShapes : MappingConfig -> AnalyzedType list -> IGraph` — returns a SHACL shapes graph

### T022: VocabularyAligner.fs — Cross-Vocabulary Alignment

**Module**: `Frank.Cli.Core.Extraction.VocabularyAligner`

**File location**: `src/Frank.Cli.Core/Extraction/VocabularyAligner.fs`

Post-processing pass that adds `owl:equivalentProperty` triples linking project-specific property URIs to well-known vocabulary terms.

Known field-name-to-schema.org alignments (case-insensitive field name matching):

| Field name pattern | Schema.org term |
|--------------------|-----------------|
| name, title | `schema:name` |
| description, summary, body | `schema:description` |
| email, emailAddress | `schema:email` |
| url, uri, website, homepage | `schema:url` |
| price, cost, amount | `schema:price` |
| createdAt, dateCreated, created | `schema:dateCreated` |
| updatedAt, dateModified, modified | `schema:dateModified` |
| image, imageUrl, photo | `schema:image` |
| telephone, phone | `schema:telephone` |

For each property in the ontology graph where the local name matches one of the patterns, assert:
```
<propertyUri> owl:equivalentProperty <schemaOrgTerm> .
```

Vocabulary filtering: only assert alignments to vocabularies that appear in `config.Vocabularies`. If `"schema.org"` is not in the list, skip schema.org alignments.

Main function:
- `alignVocabularies : MappingConfig -> IGraph -> IGraph` — takes an existing ontology graph, adds alignment triples, returns the modified graph (or a new graph with the alignment triples only — either is acceptable)

Field name matching: normalize to lowercase before comparing. Split camelCase on uppercase boundaries for multi-word matching (e.g., `emailAddress` → `email address` → matches `email` pattern).

### T023: Unit Tests for All Mappers

**Project**: Frank.Cli.Core.Tests

Create a shared test fixture builder that produces consistent `AnalyzedType` and `AnalyzedResource` values for all mapper tests.

`test/Frank.Cli.Core.Tests/Extraction/TypeMapperTests.fs`:
- Input: `AnalyzedType` for a simple DU `type Status = Active | Inactive`
- Verify ontology graph contains: `<types/Status> rdf:type owl:Class`, `<types/Active> rdfs:subClassOf <types/Status>`, `<types/Inactive> rdfs:subClassOf <types/Status>`
- Input: `AnalyzedType` for a record `type Product = { Id: int; Name: string; Price: float option }`
- Verify: `<types/Product> rdf:type owl:Class`, `<properties/Product/Id> rdf:type owl:DatatypeProperty`, `<properties/Product/Id> rdfs:range xsd:integer`
- Verify: `<properties/Product/Price>` exists (for optional field — presence in graph, cardinality handled in SHACL)

`test/Frank.Cli.Core.Tests/Extraction/RouteMapperTests.fs`:
- Input: `AnalyzedResource { RouteTemplate = "/products/{id}"; Name = Some "Product Detail"; HttpMethods = [Get]; HasLinkedData = false }`
- Verify graph contains: a resource node with `rdf:type hydra:Resource`
- Verify `hydra:template` triple has value containing `"/resources/products/{id}"`

`test/Frank.Cli.Core.Tests/Extraction/CapabilityMapperTests.fs`:
- Input: resource with `HttpMethods = [Get; Post; Delete]`
- Verify graph contains 3 operation blank nodes
- Verify GET operation has `rdf:type schema:ReadAction` and `hydra:method "GET"`
- Verify POST operation has `rdf:type schema:CreateAction` and `hydra:method "POST"`
- Verify DELETE operation has `rdf:type schema:DeleteAction` and `hydra:method "DELETE"`

`test/Frank.Cli.Core.Tests/Extraction/ShapeGeneratorTests.fs`:
- Input: record type with `Name: string` (required) and `Email: string option` (optional)
- Verify SHACL shape has two `sh:property` constraints
- Verify required field constraint has `sh:minCount "1"^^xsd:integer`
- Verify optional field constraint has `sh:minCount "0"^^xsd:integer`

`test/Frank.Cli.Core.Tests/Extraction/VocabularyAlignerTests.fs`:
- Create ontology graph with a property `<baseUri>/properties/Customer/email rdf:type owl:DatatypeProperty`
- Call `alignVocabularies` with config including `"schema.org"` in vocabularies list
- Verify graph now contains `<baseUri>/properties/Customer/email owl:equivalentProperty schema:email`
- Test with `"schema.org"` NOT in config vocabularies → verify no alignment triple added

**RDF validity check**: In each test that produces a graph, parse the graph's Turtle serialization back using dotNetRdf's `TurtleParser` and assert it parses without errors. This catches malformed URIs and invalid literal syntax.

## Risks

- OWL/SHACL correctness: generated triples may be syntactically valid RDF but semantically incorrect OWL/SHACL. Validate generated output:
  - Use dotNetRdf's `ShapesGraph` class to validate a sample instance against the generated SHACL shapes
  - Use dotNetRdf's `OntologyGraph` to verify class hierarchy is navigable
- URI collisions: if two types have the same short name but different namespaces, URI generation will collide. Use `FullName` for URI generation if `ShortName` is ambiguous.
- Blank node identity: blank node IDs must be unique within a graph. Generate deterministic IDs (e.g., hash of route+method) rather than sequential integers to avoid collisions when graphs are merged.

## Review Guidance

- After each mapper test, serialize the output graph to Turtle and log it for manual inspection
- Check that generated SHACL shape URIs do not clash with ontology class URIs (use `/shapes/` prefix vs `/types/` prefix)
- Verify the vocabulary aligner does not add duplicate `owl:equivalentProperty` triples when called multiple times on the same graph
- Confirm all vocabulary URI constants used in triples come from the `Vocabularies` module (no inline string literals)
