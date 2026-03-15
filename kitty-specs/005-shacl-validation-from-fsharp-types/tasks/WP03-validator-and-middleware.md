---
work_package_id: WP03
title: Validator & Validation Middleware
lane: "for_review"
dependencies:
- WP01
- WP02
subtasks: [T015, T016, T017, T018, T019, T020, T021]
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-008, FR-009]
---

# Work Package Prompt: WP03 -- Validator & Validation Middleware

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
spec-kitty implement WP03 --base WP02
```

Depends on WP01 (core types), WP02 (shape derivation, type mapping).

---

## Objectives & Success Criteria

- Implement `Validator.fs`: execute SHACL validation using dotNetRdf's `ShapesGraph.Validate()` (FR-008, FR-009)
- Implement `ValidationMiddleware.fs`: ASP.NET Core middleware that intercepts requests, validates against derived shapes, and short-circuits with 422 on failure (FR-008, FR-009)
- Convert `ShaclShape` to dotNetRdf `ShapesGraph` (one-time at startup, cached)
- Convert request data (JSON body or query parameters) to dotNetRdf `IGraph` (per-request)
- Validate GET query parameters against derived shapes (FR-008)
- Handler never executes for invalid requests (SC-002)
- Middleware adds less than 1ms overhead for the "valid request" path (SC-004)

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/005-shacl-validation-from-fsharp-types/research.md` -- Decision 2 (dotNetRdf SHACL API), Decision 5 (middleware pattern)
- `kitty-specs/005-shacl-validation-from-fsharp-types/spec.md` -- FR-008, FR-009, SC-002, SC-004, User Story 2
- `kitty-specs/005-shacl-validation-from-fsharp-types/plan.md` -- Pipeline ordering: Auth -> Validation -> Handler

**Key constraints**:
- Use `VDS.RDF.Shacl.ShapesGraph` for validation (dotNetRdf.Core)
- `ShapesGraph` constructed once at startup, reused per request
- Data graph constructed per-request, disposed after validation via `use` binding
- Middleware checks `endpoint.Metadata.GetMetadata<ValidationMarker>()` -- null means pass through (zero overhead)
- Pipeline ordering: after `useAuth`, before handler dispatch
- Missing body on validated endpoint -> validation failure, not deserialization error
- Framework types (HttpContext, HttpRequest) -> skip validation

---

## Subtasks & Detailed Guidance

### Subtask T015 -- Create `Validator.fs`

**Purpose**: Core validation logic that takes a SHACL shape and request data, executes dotNetRdf SHACL validation, and returns a `ValidationReport`.

**Steps**:
1. Create `src/Frank.Validation/Validator.fs`
2. Implement the validation function:

```fsharp
namespace Frank.Validation

open VDS.RDF
open VDS.RDF.Shacl

module Validator =
    /// Validate a data graph against a shapes graph.
    /// Returns a ValidationReport indicating conformance and any violations.
    let validate (shapesGraph: ShapesGraph) (dataGraph: IGraph) : ValidationReport =
        let report = shapesGraph.Validate(dataGraph)
        { Conforms = report.Conforms
          Results =
            report.Results
            |> Seq.map (fun r ->
                { FocusNode = r.FocusNode.ToString()
                  ResultPath = r.ResultPath |> Option.ofObj |> Option.map string |> Option.defaultValue ""
                  Value = r.ResultValue |> Option.ofObj |> Option.map box
                  SourceConstraint = r.SourceConstraintComponent.ToString()
                  Message = r.Message
                  Severity = mapSeverity r.Severity })
            |> Seq.toList
          ShapeUri = ... }
```

3. Add a helper to map dotNetRdf severity to `ValidationSeverity`:

```fsharp
    let private mapSeverity (severity: INode option) : ValidationSeverity =
        // Map sh:Violation, sh:Warning, sh:Info to ValidationSeverity DU
        Violation // default
```

**Files**: `src/Frank.Validation/Validator.fs`
**Notes**: The dotNetRdf `Report` type is `VDS.RDF.Shacl.Validation.Report`. Its `Results` property returns a collection of `Result` objects. Adapt field names to match dotNetRdf's actual API (verify property names at implementation time). The `ValidationReport` returned is our F# record type, not dotNetRdf's internal type.

### Subtask T016 -- Implement ShaclShape -> ShapesGraph conversion

**Purpose**: Convert our F# `ShaclShape` type into a dotNetRdf `IGraph` containing SHACL triples, then wrap as `ShapesGraph` for validation.

**Steps**:
1. In `Validator.fs` or a separate `ShapeGraphBuilder.fs`, implement:

```fsharp
module ShapeGraphBuilder =
    /// Build a dotNetRdf IGraph containing SHACL shape triples from a ShaclShape.
    let buildShapesGraph (shape: ShaclShape) : ShapesGraph =
        let g = new Graph()
        // Add namespace prefixes
        g.NamespaceMap.AddNamespace("sh", UriFactory.Create("http://www.w3.org/ns/shacl#"))
        g.NamespaceMap.AddNamespace("xsd", UriFactory.Create("http://www.w3.org/2001/XMLSchema#"))

        // Create NodeShape node
        let shapeNode = g.CreateUriNode(shape.NodeShapeUri)
        let rdfType = g.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))
        let nodeShapeType = g.CreateUriNode(UriFactory.Create("http://www.w3.org/ns/shacl#NodeShape"))
        g.Assert(shapeNode, rdfType, nodeShapeType)

        // Add property shapes
        for prop in shape.Properties do
            let propNode = g.CreateBlankNode()
            let shProperty = g.CreateUriNode(UriFactory.Create("http://www.w3.org/ns/shacl#property"))
            g.Assert(shapeNode, shProperty, propNode)
            // Add sh:path, sh:datatype, sh:minCount, sh:maxCount, etc.
            ...

        ShapesGraph(g)
```

2. Handle all PropertyShape fields:
   - `Path` -> `sh:path` (as URI: `urn:frank:property:{Path}`)
   - `Datatype` -> `sh:datatype` (XSD URI from `TypeMapping.xsdUri`)
   - `MinCount` -> `sh:minCount`
   - `MaxCount` -> `sh:maxCount` (if Some)
   - `NodeReference` -> `sh:node`
   - `InValues` -> `sh:in` (RDF list)
   - `OrShapes` -> `sh:or` (RDF list of node references)
   - `Pattern` -> `sh:pattern`

**Files**: `src/Frank.Validation/Validator.fs` or `src/Frank.Validation/ShapeGraphBuilder.fs`
**Notes**: dotNetRdf uses `IGraph.Assert(subject, predicate, object)` to add triples. RDF lists (for `sh:in`, `sh:or`) require the `rdf:first`/`rdf:rest`/`rdf:nil` pattern. dotNetRdf may have helper methods for list construction.

### Subtask T017 -- Implement request data -> IGraph conversion

**Purpose**: Convert the deserialized request data (JSON body or query parameters) into a dotNetRdf `IGraph` for validation against the shapes graph.

**Steps**:
1. In `Validator.fs` or `DataGraphBuilder.fs`, implement:

```fsharp
module DataGraphBuilder =
    /// Build a data graph from a JSON body, using the shape's property paths
    /// to map JSON properties to RDF predicates.
    let buildFromJsonBody (shape: ShaclShape) (json: JsonElement) : IGraph =
        let g = new Graph()
        let focusNode = g.CreateBlankNode("request")
        for prop in shape.Properties do
            match json.TryGetProperty(prop.Path) with
            | true, value ->
                let predicate = g.CreateUriNode(UriFactory.Create(sprintf "urn:frank:property:%s" prop.Path))
                let obj = jsonValueToNode g prop value
                g.Assert(focusNode, predicate, obj)
            | false, _ ->
                () // Missing property -- minCount validation will catch this
        g

    /// Build a data graph from query string parameters.
    let buildFromQueryParams (shape: ShaclShape) (query: IQueryCollection) : IGraph =
        let g = new Graph()
        let focusNode = g.CreateBlankNode("request")
        for prop in shape.Properties do
            match query.TryGetValue(prop.Path) with
            | true, values ->
                let predicate = g.CreateUriNode(UriFactory.Create(sprintf "urn:frank:property:%s" prop.Path))
                for v in values do
                    let obj = stringValueToNode g prop (v.ToString())
                    g.Assert(focusNode, predicate, obj)
            | false, _ -> ()
        g
```

2. Convert JSON values to appropriate RDF literal nodes based on the property's XSD datatype.
3. Handle nested objects by recursively building sub-graphs.
4. Dispose the data graph after validation via `use` binding.

**Files**: `src/Frank.Validation/Validator.fs` or `src/Frank.Validation/DataGraphBuilder.fs`
**Notes**: Use `System.Text.Json.JsonElement` for JSON parsing (ASP.NET Core default). Map JSON types to RDF literals: string -> `xsd:string` literal, number -> appropriate numeric literal, boolean -> `xsd:boolean` literal, null -> absent (minCount handles it).

### Subtask T018 -- Create `ValidationMiddleware.fs`

**Purpose**: ASP.NET Core middleware that intercepts requests to validated endpoints, runs SHACL validation, and short-circuits with 422 on failure.

**Steps**:
1. Create `src/Frank.Validation/ValidationMiddleware.fs`
2. Implement the middleware following Frank's existing patterns:

```fsharp
namespace Frank.Validation

open Microsoft.AspNetCore.Http

type ValidationMiddleware(next: RequestDelegate, shapeCache: ShapeCache) =
    member _.InvokeAsync(ctx: HttpContext) = task {
        let endpoint = ctx.GetEndpoint()
        match endpoint with
        | null -> do! next.Invoke(ctx)
        | ep ->
            let marker = ep.Metadata.GetMetadata<ValidationMarker>()
            match marker with
            | null ->
                // No validation configured for this endpoint
                do! next.Invoke(ctx)
            | marker ->
                // 1. Get or derive the shapes graph from cache
                let shapesGraph = shapeCache.GetOrAdd(marker.ShapeType)
                // 2. Read and parse request data
                // 3. Build data graph from body (POST/PUT/PATCH) or query (GET)
                // 4. Validate
                let report = Validator.validate shapesGraph dataGraph
                if report.Conforms then
                    do! next.Invoke(ctx)
                else
                    // Short-circuit with 422
                    ctx.Response.StatusCode <- 422
                    // Serialize report (delegated to ReportSerializer in WP04)
                    // For now, write a placeholder JSON response
                    ...
    }
```

3. Handle different HTTP methods:
   - POST/PUT/PATCH: validate request body
   - GET: validate query parameters
   - DELETE/HEAD/OPTIONS: skip validation (no body or query shape)
4. Enable request body buffering so the body can be read by both middleware and handler.
5. Handle missing body on POST/PUT/PATCH: produce a validation failure, not a deserialization error.

**Files**: `src/Frank.Validation/ValidationMiddleware.fs`
**Notes**: Request body must be buffered (`ctx.Request.EnableBuffering()`) so the handler can re-read it after validation. Reset `ctx.Request.Body.Position = 0` after reading in the middleware. The report serialization is a placeholder here -- WP04 implements the full content-negotiated serialization.

### Subtask T019 -- Implement query parameter validation for GET requests

**Purpose**: Validate GET request query parameters against derived shapes. (FR-008)

**Steps**:
1. In `ValidationMiddleware.fs`, for GET requests:
   - Read query parameters from `ctx.Request.Query`
   - Build data graph from query parameters (via `DataGraphBuilder.buildFromQueryParams`)
   - Validate against the same shape used for body validation
2. Query parameters are string-typed by default; the data graph builder must attempt type coercion based on the property's expected XSD datatype.
3. Multiple values for the same query parameter map to multiple RDF triples for the same predicate.

**Files**: `src/Frank.Validation/ValidationMiddleware.fs`
**Notes**: Not all shapes make sense for GET validation (e.g., deeply nested records). The middleware should validate whatever properties are present in the query string and skip properties that are not present (missing optional fields are allowed).

**Implementation note**: Query parameters are deserialized using parameter name mapping: a parameter `name` maps to the record field `Name` (case-insensitive). Nested fields use dot notation in the query string (e.g., `?address.zipCode` maps to the `ZipCode` field of the nested `Address` record). Validation failure on any query parameter produces a validation error with resultPath indicating the parameter name. If the resource requires both body and query parameter validation, both are executed; a violation in either causes a 422 short-circuit.

### Subtask T020 -- Create `ValidatorTests.fs`

**Purpose**: Test the core validation logic: shapes graph construction, data graph construction, and SHACL validation pass/fail.

**Steps**:
1. Create `test/Frank.Validation.Tests/ValidatorTests.fs`
2. Write tests:

**a. Valid data conformance**:
- Build a shape for `{ Name: string; Age: int }` (both required)
- Build a data graph with both properties present and correctly typed
- Validate: `report.Conforms = true`, `report.Results = []`

**b. Missing required field**:
- Same shape, data graph missing `Name`
- Validate: `report.Conforms = false`, one result with `sh:minCount` violation

**c. Wrong datatype**:
- Same shape, `Age` has a string value instead of integer
- Validate: `report.Conforms = false`, one result with `sh:datatype` violation

**d. sh:in constraint violation**:
- Shape with `InValues = Some ["A"; "B"; "C"]`
- Data graph with value "D"
- Validate: violation on `sh:in`

**e. Optional field absent**:
- Shape with `minCount = 0` for a field
- Data graph with field absent
- Validate: `report.Conforms = true`

**f. Multiple violations**:
- Data graph with multiple issues
- Validate: `report.Results.Length` matches expected count

**Files**: `test/Frank.Validation.Tests/ValidatorTests.fs`
**Parallel?**: Yes -- can be scaffolded once T015/T016 are defined.

### Subtask T021 -- Create `MiddlewareTests.fs`

**Purpose**: Integration tests using TestHost to verify the middleware pipeline behavior.

**Steps**:
1. Create `test/Frank.Validation.Tests/MiddlewareTests.fs`
2. Set up a TestHost with a validated resource endpoint
3. Write tests:

**a. Valid POST passes through to handler**:
- Send a valid JSON body
- Verify handler executes (e.g., returns 201)

**b. Invalid POST returns 422**:
- Send a body missing required fields
- Verify 422 status code
- Verify handler did NOT execute (use a counter/flag)

**c. Valid GET with query parameters**:
- Send GET with valid query params
- Verify handler executes

**d. Non-validated endpoint passes through**:
- Send request to an endpoint without `validate`
- Verify handler executes with zero overhead

**e. Missing body on POST returns 422**:
- Send POST with empty/missing body to validated endpoint
- Verify 422 (not 400 or 500)

**Files**: `test/Frank.Validation.Tests/MiddlewareTests.fs`
**Parallel?**: Depends on T018 but can be scaffolded early.
**Validation**: `dotnet test test/Frank.Validation.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build` to verify compilation of Validator.fs and ValidationMiddleware.fs
- Run `dotnet test` for all validator and middleware tests
- Verify User Story 2 acceptance scenarios from spec.md

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| dotNetRdf SHACL validation performance | Cache ShapesGraph; construct only data graph per-request; benchmark with 20-property shapes |
| dotNetRdf API surface differences from research assumptions | Prototype ShapesGraph construction early; verify property names on `Report` and `Result` types |
| Request body buffering in middleware | Use `ctx.Request.EnableBuffering()` and reset position after reading |
| Data graph construction from JSON: type coercion complexity | Start with string literals for all values; add typed literals incrementally |

---

## Review Guidance

- Verify Validator.fs correctly converts between our F# types and dotNetRdf types
- Verify middleware checks `ValidationMarker` metadata (null check = no validation = pass through)
- Verify request body is buffered and position reset for handler re-read
- Verify 422 status code on validation failure (not 400)
- Verify handler counter test confirms zero invocations on invalid requests
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-15T19:35:42Z – unknown – lane=for_review – Moved to for_review
