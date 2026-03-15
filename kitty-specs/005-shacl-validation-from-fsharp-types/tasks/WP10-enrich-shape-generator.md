---
work_package_id: WP10
title: Enrich ShapeGenerator with Full SHACL Constraints
lane: done
dependencies:
- WP09
subtasks: [T055, T056, T057, T058, T059]
history:
- timestamp: '2026-03-14T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated from build-time SHACL unification design spec
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-015, FR-020]
design_ref: docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md
---

# Work Package Prompt: WP10 -- Enrich ShapeGenerator with Full SHACL Constraints

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP10 --base WP09
```

Depends on WP09 (enriched TypeAnalyzer metadata).

---

## Objectives & Success Criteria

- Extend `Frank.Cli.Core.Extraction.ShapeGenerator` to emit validation-grade SHACL triples
- Adopt `urn:frank:*` URI scheme for `sh:path` and `sh:NodeShape` URIs (matching Frank.Validation's conventions)
- Emit `sh:maxCount 1` for scalar fields, absent for collections
- Emit `sh:pattern` for Guid fields (UUID RFC 4122 regex)
- Emit `sh:in` for simple DUs (case names as string literals)
- Emit `sh:or` with per-case `sh:NodeShape` for payload DUs
- Emit `sh:closed true` for records
- Emit `sh:nodeKind` where appropriate
- Emit cycle detection markers for recursive types (configurable depth, default 5)
- Emit custom constraint triples from attribute metadata (WP09)
- Round-trip test: generate Turtle, parse with dotNetRdf, verify all constraint triples

---

## Context & Constraints

**Reference documents**:
- `docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md` -- WP10 section, URI scheme decision
- `src/Frank.Validation/ShapeDerivation.fs` -- **Primary reference implementation** for constraint logic
- `src/Frank.Validation/ShapeGraphBuilder.fs` -- Reference for how constraints become SHACL triples
- `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs` -- Current implementation to extend
- `src/Frank.Cli.Core/Extraction/UriHelpers.fs` -- Current URI construction (to be amended)

**Key constraints**:
- The shapes graph uses `urn:frank:shape:{assembly}:{encoded-type-name}` for NodeShape URIs and `urn:frank:property:{fieldName}` for `sh:path` URIs -- matching Frank.Validation's `ShapeDerivation.buildNodeShapeUri` and `buildPropertyPathUri`
- The ontology graph retains the configurable `{baseUri}/` scheme for OWL classes and properties (separate graph, not affected)
- `sh:targetNode urn:frank:validation:request` must be emitted on each NodeShape so the validator's data graph matches
- Use `ShapeDerivation.fs` as the authoritative reference for constraint logic -- port it, don't reinvent it

---

## Subtasks & Detailed Guidance

### Subtask T055 -- Adopt `urn:frank:*` URI scheme for shapes graph

**Purpose**: Switch the ShapeGenerator's shape and property URIs to the `urn:frank:*` scheme that Frank.Validation's data graph builder expects.

**Steps**:
1. Add new URI construction functions for the shapes graph (keep existing `{baseUri}/` functions for ontology):

```fsharp
module ShapeGenerator =
    /// Build a NodeShape URI using the urn:frank:shape scheme.
    /// Pattern: urn:frank:shape:{assembly-name}:{type-full-name}
    let private validationShapeUri (analyzedType: AnalyzedType) =
        let encoded = Uri.EscapeDataString(analyzedType.FullName)
        Uri(sprintf "urn:frank:shape:%s" encoded)

    /// Build a property path URI using the urn:frank:property scheme.
    let private validationPropertyUri (fieldName: string) =
        Uri(sprintf "urn:frank:property:%s" fieldName)
```

2. Use these URIs in all shapes graph construction (the `generateShapes` function).
3. The ontology graph (`TypeMapper.mapTypes`, `RouteMapper.mapRoutes`) continues using the configurable `{baseUri}/` URIs via `UriHelpers`.
4. Add `sh:targetNode <urn:frank:validation:request>` to each NodeShape so validation data graphs match.

**Files**: `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs`
**Notes**: The assembly name may not be available from `AnalyzedType` alone. If needed, pass it as a parameter to `generateShapes` from the extraction pipeline.

### Subtask T056 -- Emit full property shape constraints

**Purpose**: Extend `assertPropertyShape` to emit all validation-grade SHACL constraints.

**Steps**:
1. Emit `sh:maxCount 1` for scalar fields (where `AnalyzedField.IsScalar = true`):

```fsharp
if field.IsScalar then
    assertTriple graph
        (psNode,
         createUriNode graph (Uri Shacl.MaxCount),
         createLiteralNode graph "1" (Some(Uri Xsd.Integer)))
```

2. Emit `sh:pattern` for Guid fields:

```fsharp
match field.Kind with
| Guid ->
    let uuidPattern = "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"
    assertTriple graph
        (psNode,
         createUriNode graph (Uri Shacl.Pattern),
         createLiteralNode graph uuidPattern None)
| _ -> ()
```

3. Emit custom constraint triples from `AnalyzedField.Constraints`:

```fsharp
for constraint in field.Constraints do
    match constraint with
    | PatternAttr regex ->
        assertTriple graph (psNode, createUriNode graph (Uri Shacl.Pattern), createLiteralNode graph regex None)
    | MinInclusiveAttr value ->
        assertTriple graph (psNode, createUriNode graph (Uri Shacl.MinInclusive), createLiteralNode graph (string value) (Some(Uri Xsd.Decimal)))
    | MaxInclusiveAttr value ->
        assertTriple graph (psNode, createUriNode graph (Uri Shacl.MaxInclusive), createLiteralNode graph (string value) (Some(Uri Xsd.Decimal)))
    | MinLengthAttr length ->
        assertTriple graph (psNode, createUriNode graph (Uri Shacl.MinLength), createLiteralNode graph (string length) (Some(Uri Xsd.Integer)))
    | MaxLengthAttr length ->
        assertTriple graph (psNode, createUriNode graph (Uri Shacl.MaxLength), createLiteralNode graph (string length) (Some(Uri Xsd.Integer)))
```

**Files**: `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs`

### Subtask T057 -- Emit DU constraints (sh:in and sh:or)

**Purpose**: Generate SHACL constraints for discriminated unions -- `sh:in` for simple DUs (no payload) and `sh:or` for payload DUs.

**Steps**:
1. For simple DUs (all cases have no fields), emit `sh:in`:

```fsharp
// Build an RDF list of case names as string literals
let caseNames = cases |> List.map (fun c -> c.Name)
let valueNodes = caseNames |> List.map (fun name -> createLiteralNode graph name None)
let rdfList = buildRdfList graph valueNodes
assertTriple graph (psNode, createUriNode graph (Uri Shacl.In), rdfList)
```

2. For payload DUs (cases with fields), emit `sh:or` with per-case NodeShapes:

```fsharp
// Generate a NodeShape for each case
let caseShapeUris =
    cases
    |> List.filter (fun c -> not c.Fields.IsEmpty)
    |> List.map (fun c ->
        let caseShapeUri = Uri(sprintf "urn:frank:shape:%s.%s" analyzedType.FullName c.Name)
        // Emit the case NodeShape with its fields
        generateCaseShape graph config caseShapeUri c
        caseShapeUri)
// Emit sh:or linking to case shapes
let shapeNodes = caseShapeUris |> List.map (fun uri -> createUriNode graph uri)
let rdfList = buildRdfList graph shapeNodes
assertTriple graph (psNode, createUriNode graph (Uri Shacl.Or), rdfList)
```

3. Port the `buildRdfList` helper from `ShapeGraphBuilder.fs` (or reference it if shared).

**Files**: `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs`
**Notes**: Reference `ShapeDerivation.deriveDuConstraint` and `ShapeGraphBuilder.buildRdfList` for the exact logic.

### Subtask T058 -- Emit sh:closed and cycle detection

**Purpose**: Emit `sh:closed true` for records and handle recursive type cycles.

**Steps**:
1. For records (`AnalyzedType.IsClosed = true`), emit `sh:closed true`:

```fsharp
if analyzedType.IsClosed then
    assertTriple graph
        (shapeNode,
         createUriNode graph (Uri Shacl.Closed),
         createLiteralNode graph "true" (Some(Uri Xsd.Boolean)))
```

2. For recursive types, implement cycle detection matching `ShapeDerivation.fs`:
   - Maintain a `Set<string>` of type names currently being processed
   - When encountering a type already in the set, emit an `sh:node` reference back to the existing NodeShape URI
   - Apply configurable max depth (default 5)
   - At depth limit, emit a minimal NodeShape with no properties

3. Pass depth and stack through the recursive `generateShapeForType` calls.

**Files**: `src/Frank.Cli.Core/Extraction/ShapeGenerator.fs`
**Notes**: The current ShapeGenerator has no cycle detection because it only emits basic structural shapes. The enriched version must handle recursive types like `TreeNode = { Value: string; Children: TreeNode list }`.

### Subtask T059 -- Create round-trip tests

**Purpose**: Verify that generated SHACL Turtle survives serialization and deserialization with all constraint types intact.

**Steps**:
1. Create test file in Frank.Cli.Core tests.
2. Test pipeline: `AnalyzedType[] -> ShapeGenerator.generateShapes -> IGraph -> Turtle string -> dotNetRdf parse -> IGraph -> verify triples`

**a. Basic record shape**:
- Input: Record with string, int, bool fields
- Verify: sh:NodeShape, sh:property for each field, sh:datatype, sh:minCount, sh:maxCount 1, sh:closed true, sh:targetNode

**b. Optional field**:
- Input: Record with `option<string>` field
- Verify: sh:minCount 0 (not 1)

**c. Guid field**:
- Input: Record with Guid field
- Verify: sh:datatype xsd:string AND sh:pattern with UUID regex

**d. Simple DU**:
- Input: DU with no-payload cases (e.g., `Red | Green | Blue`)
- Verify: sh:in with RDF list of case names

**e. Payload DU**:
- Input: DU with payload cases
- Verify: sh:or with per-case NodeShape URIs

**f. Nested record**:
- Input: Record containing another record field
- Verify: sh:node reference to nested NodeShape

**g. Recursive type**:
- Input: `TreeNode = { Value: string; Children: TreeNode list }`
- Verify: Finite graph (no infinite expansion), sh:node back-reference

**h. Collection field**:
- Input: Record with `string list` field
- Verify: No sh:maxCount (collections are unbounded)

**i. Custom constraint attributes**:
- Input: Record with `[<Pattern("...")>]` and `[<MinInclusive(0)>]` fields
- Verify: sh:pattern and sh:minInclusive triples present

**j. URI scheme**:
- Verify all NodeShape URIs match `urn:frank:shape:*` pattern
- Verify all property path URIs match `urn:frank:property:*` pattern

**Files**: Test file in `test/Frank.Cli.Core.Tests/`
**Validation**: `dotnet build` and `dotnet test` pass.

---

## Test Strategy

- Run `dotnet build` to verify Frank.Cli.Core compiles
- Run `dotnet test` for all shape generation tests
- Verify existing extraction pipeline tests still pass
- Round-trip tests are the primary validation: generate -> serialize -> parse -> verify

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| RDF list construction for sh:in and sh:or is error-prone | Port `buildRdfList` directly from `ShapeGraphBuilder.fs`; test with dotNetRdf parser |
| Cycle detection adds complexity to the generator | Port logic from `ShapeDerivation.fs` which is already tested and working |
| URI scheme change breaks existing ontology consumers | Shapes graph uses `urn:frank:*`; ontology graph retains `{baseUri}/`; they are separate IGraph instances |
| dotNetRdf Turtle serialization may reorder triples | Use semantic triple queries (not string matching) in round-trip tests |

---

## Review Guidance

- Verify every constraint type from `ShapeDerivation.fs` has a corresponding emission in `ShapeGenerator.fs`
- Verify URI scheme is `urn:frank:*` for shapes, not `{baseUri}/`
- Verify `sh:targetNode urn:frank:validation:request` is emitted on each NodeShape
- Verify cycle detection matches `ShapeDerivation.fs` behavior
- Verify round-trip tests cover every constraint variant
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-14T00:00:00Z -- system -- lane=planned -- Prompt created from build-time SHACL unification design.
