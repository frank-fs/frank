---
work_package_id: "WP10"
subtasks:
  - "T050"
  - "T051"
  - "T052"
  - "T053"
  - "T054"
title: "CLI Core Tests"
phase: "Phase 2 - Testing"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP06"]
requirement_refs: ["FR-002", "FR-003", "FR-004", "FR-005", "FR-006", "FR-007", "FR-008", "FR-009", "FR-010", "FR-011"]
history:
  - timestamp: "2026-03-04T22:10:13Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

> **Review Feedback Status**: No review feedback yet.

## Review Feedback

_No feedback recorded._

> **Markdown Formatting Note**: All prose in this file uses plain Markdown. Code samples use fenced code blocks with language tags. Lists use `-` bullets.

## Objectives & Success Criteria

Provide comprehensive unit tests for all extraction and mapping logic in `Frank.Cli.Core`.

**Success criteria**:
- All mapper and generator modules have direct unit test coverage
- Tests verify RDF output using dotNetRdf graph querying, not string comparison
- `AstAnalyzer` tests parse real fixture `.fs` files via FCS and verify extracted data structures
- All tests are independent and can run in parallel with Expecto
- Test project builds and all tests pass as part of `dotnet test`

## Context & Constraints

- Test project: `test/Frank.Cli.Core.Tests/` (created in WP01)
- Use Expecto 10.x + YoloDev.Expecto.TestSdk pattern from existing Frank tests (see `test/Frank.Tests/`)
- Tests must verify RDF output by querying `IGraph` objects, not by comparing serialized strings — serialization order is not guaranteed
- Reference `data-model.md` OntologyMapping section for expected mappings between F# constructs and OWL/Hydra/SHACL terms
- Fixture `.fs` files established in WP03 (T017) should already exist; create them here if missing
- Depends on WP06 (CLI Compile) because WP06 finalizes the mapper interfaces tested here

## Subtasks & Detailed Guidance

### T050 — TypeMapper tests

**File**: `test/Frank.Cli.Core.Tests/TypeMapperTests.fs`

Test that `TypeMapper` correctly produces OWL triples for F# type definitions.

Key test cases:

- **DU with 3 cases**: An F# discriminated union with 3 cases should produce 1 `owl:Class` as the superclass and 3 `owl:Class` entries as subclasses, each linked via `rdfs:subClassOf`
- **Record with 5 fields**: An F# record with 5 fields should produce 1 `owl:Class` and 5 property entries (`owl:DatatypeProperty` for primitive fields, `owl:ObjectProperty` for record/DU fields)
- **Nested record field**: A field whose type is another record should produce an `owl:ObjectProperty` with `rdfs:range` pointing to the nested class URI
- **Option field**: A field of type `option<T>` should produce no `sh:minCount` constraint (or explicit `sh:minCount 0`)
- **Required field**: A non-optional field should produce `sh:minCount 1`

Verification approach — query the graph directly:

```fsharp
let classNode = UriNode(Uri(expectedClassUri))
let rdfType = UriNode(Uri("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))
let owlClass = UriNode(Uri("http://www.w3.org/2002/07/owl#Class"))
let triples = graph.GetTriplesWithSubjectPredicate(classNode, rdfType)
Expect.exists triples (fun t -> t.Object = owlClass) "Expected owl:Class assertion"
```

Do not assert on serialized Turtle or RDF/XML strings.

---

### T051 — RouteMapper tests

**File**: `test/Frank.Cli.Core.Tests/RouteMapperTests.fs`

Test that `RouteMapper` correctly maps ASP.NET Core route templates to RDF resource URIs and Hydra vocabulary terms.

Key test cases:

- **Root route `"/"`**: Should map to the base URI resource
- **Simple parameterized route `"/products/{id}"`**: Should produce a URI template resource with `{id}` as a parameter
- **Deeply nested route `"/api/v1/users/{userId}/orders/{orderId}"`**: Should produce a correctly structured URI template with two parameters
- **Custom `--base-uri` flag**: Providing a custom base URI should override the default namespace for all generated resource URIs
- **hydra:Resource assertion**: Every mapped route should have a `hydra:Resource` type triple

Verify URI template resources carry `hydra:IriTemplate` assertions where applicable, and that parameter nodes are linked correctly.

---

### T052 — CapabilityMapper tests

**File**: `test/Frank.Cli.Core.Tests/CapabilityMapperTests.fs`

Test that `CapabilityMapper` correctly maps HTTP handlers to `schema:Action` subclasses and Hydra operation triples.

Key test cases:

- **GET handler**: Should produce `schema:ReadAction` and a `hydra:Operation` with `hydra:method "GET"`
- **POST handler**: Should produce `schema:CreateAction`
- **PUT handler**: Should produce `schema:UpdateAction`
- **DELETE handler**: Should produce `schema:DeleteAction`
- **Resource with GET and POST**: Both capabilities should be linked to the resource via `hydra:supportedOperation`; both should appear in the graph

Verify:
- Each action class is asserted as a subclass of `schema:Action`
- Hydra operation nodes carry the correct `hydra:method` literal

---

### T053 — ShapeGenerator tests

**File**: `test/Frank.Cli.Core.Tests/ShapeGeneratorTests.fs`

Test that `ShapeGenerator` produces correct SHACL shapes for F# record types.

Key test cases:

- **Required string field**: Should produce an `sh:NodeShape` with `sh:property [ sh:path <prop>; sh:datatype xsd:string; sh:minCount 1 ]`
- **Optional field**: Should produce `sh:minCount 0` (or omit the `sh:minCount` constraint entirely — check `data-model.md` for the chosen convention)
- **List field**: Should produce no `sh:maxCount` constraint (unbounded)
- **Bool field**: Should use `sh:datatype xsd:boolean`
- **Int field**: Should use `sh:datatype xsd:integer`

If `dotNetRdf.Shacl` provides a typed graph API, use it to query shapes. Otherwise query the raw `IGraph` for SHACL triple patterns directly.

---

### T054 — AstAnalyzer tests

**File**: `test/Frank.Cli.Core.Tests/AstAnalyzerTests.fs`

Test that `AstAnalyzer` correctly extracts resource declarations from F# source files using FCS.

**Fixture files** (create in `test/Frank.Cli.Core.Tests/Fixtures/` if not already from WP03 T017):

- `simple-resource.fs`: Contains `resource "/test" { get handler }` — expect route `"/test"` with method `GET`
- `multi-method.fs`: Contains `resource "/items/{id}" { get handler; post handler; delete handler }` — expect route `"/items/{id}"` with methods `GET`, `POST`, `DELETE`
- `linked-data-resource.fs`: Contains `resource "/data" { linkedData; get handler }` — expect `linkedData = true` flag on the extracted resource
- `named-resource.fs`: Contains `resource "/named" { name "Named Resource"; get handler }` — expect `name = Some "Named Resource"` on the extracted resource

Test structure:

```fsharp
let analyzeFixture name =
    let path = Path.Combine(__SOURCE_DIRECTORY__, "Fixtures", name)
    let project = // minimal FCS project for the fixture file
    AstAnalyzer.analyzeFile project path

testCase "simple resource extraction" <| fun () ->
    let resources = analyzeFixture "simple-resource.fs"
    Expect.hasLength resources 1 "Expected 1 resource"
    Expect.equal resources.[0].Route "/test" "Expected route /test"
    Expect.contains resources.[0].Methods HttpMethod.Get "Expected GET method"
```

Note: FCS-dependent tests require the fixture files to be syntactically valid F# that references the Frank CE operations. Keep fixtures minimal; they do not need to compile as full programs — FCS can parse and type-check individual files given the right project options. Verify this approach is consistent with how WP03 set up FCS project loading.

## Risks & Mitigations

- **FCS project loading complexity**: FCS requires `.fsproj`-derived project options to resolve type information. If full type-checking is needed for `AstAnalyzer`, the test project may need to reference `Frank.Core` to resolve CE operation types. Keep fixture files as simple as possible to reduce this overhead.
- **RDF graph query API**: `dotNetRdf` has multiple `GetTriples*` overloads. Confirm the correct overload for subject+predicate queries is available in the version pinned by WP02.
- **Test parallelism**: Expecto runs tests in parallel by default. Ensure no test mutates shared state; each test should create its own `IGraph` instance.

## Review Guidance

- Verify tests cover edge cases: empty DU (zero cases), empty record (zero fields), route with no handlers
- Confirm that all tests are truly independent — no shared mutable state between test cases
- Run `dotnet test test/Frank.Cli.Core.Tests/` and verify all tests pass with zero failures
- Check that test failure messages are descriptive enough to diagnose issues without a debugger

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-04T22:10:13Z | system | Prompt generated via /spec-kitty.tasks |
