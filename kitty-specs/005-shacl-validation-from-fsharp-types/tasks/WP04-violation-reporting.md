---
work_package_id: WP04
title: Violation Reporting & Content Negotiation
lane: "doing"
dependencies:
- WP01
subtasks: [T022, T023, T024, T025, T026]
agent: "claude-opus-reviewer"
shell_pid: "42258"
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-010, FR-011, FR-012]
---

# Work Package Prompt: WP04 -- Violation Reporting & Content Negotiation

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
spec-kitty implement WP04 --base WP03
```

Depends on WP01 (types), WP03 (validator produces ValidationReport).

---

## Objectives & Success Criteria

- Implement `ReportSerializer.fs`: dual-path serialization for validation failure responses (FR-010, FR-011, FR-012)
- Semantic clients (`Accept: application/ld+json`, `text/turtle`, `application/rdf+xml`): SHACL ValidationReport via Frank.LinkedData (FR-011)
- Standard clients (`Accept: application/json`): RFC 9457 Problem Details JSON (FR-012)
- Every ValidationResult includes sh:focusNode, sh:resultPath, sh:value, sh:sourceConstraintComponent, sh:resultMessage (FR-010, SC-003)
- Nested field paths rendered correctly (e.g., `customer.address.zipCode`) (SC-003)
- Content negotiation produces valid, parseable output for all formats (SC-005)

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/005-shacl-validation-from-fsharp-types/research.md` -- Decision 4 (dual-path serialization)
- `kitty-specs/005-shacl-validation-from-fsharp-types/quickstart.md` -- Example JSON-LD and Problem Details responses
- `kitty-specs/005-shacl-validation-from-fsharp-types/spec.md` -- FR-010 through FR-012, User Story 3

**Key constraints**:
- Semantic serialization delegates to Frank.LinkedData's existing content negotiation infrastructure
- Problem Details uses `application/problem+json` Content-Type
- `IGraph` instances created per-request for semantic serialization must be disposed via `use` bindings
- Error type URI: `urn:frank:validation:shacl-violation`
- Constitution VI (Resource Disposal Discipline): dispose all `IGraph` instances after serialization

---

## Subtasks & Detailed Guidance

### Subtask T022 -- Create `ReportSerializer.fs` with content-negotiation dispatch

**Purpose**: Top-level serialization module that inspects the `Accept` header and dispatches to the appropriate serialization path.

**Steps**:
1. Create `src/Frank.Validation/ReportSerializer.fs`
2. Implement the dispatch logic:

```fsharp
namespace Frank.Validation

open Microsoft.AspNetCore.Http

module ReportSerializer =
    let private semanticMediaTypes = [
        "application/ld+json"
        "text/turtle"
        "application/rdf+xml"
    ]

    /// Serialize a ValidationReport to the HTTP response, content-negotiated.
    let writeReport (ctx: HttpContext) (report: ValidationReport) = task {
        let accept = ctx.Request.Headers.Accept.ToString()
        let isSemantic = semanticMediaTypes |> List.exists (fun mt -> accept.Contains(mt))
        if isSemantic then
            do! writeShaclReport ctx report
        else
            do! writeProblemDetails ctx report
    }
```

3. Integrate into `ValidationMiddleware.fs` (replace the placeholder from WP03).
4. Add `ReportSerializer.fs` to the `.fsproj` compile list after `Validator.fs`.

**Files**: `src/Frank.Validation/ReportSerializer.fs`
**Notes**: The `Accept` header parsing should handle quality values (e.g., `application/ld+json;q=1.0, application/json;q=0.9`). For the initial implementation, a simple `Contains` check is acceptable; Frank.LinkedData may provide more sophisticated negotiation.

### Subtask T023 -- Implement SHACL ValidationReport -> IGraph for semantic serialization

**Purpose**: Convert our F# `ValidationReport` into a dotNetRdf `IGraph` containing SHACL ValidationReport triples, then delegate to Frank.LinkedData for format-specific serialization.

**Steps**:
1. In `ReportSerializer.fs`, implement:

```fsharp
    let private buildReportGraph (report: ValidationReport) : IGraph =
        let g = new Graph()
        g.NamespaceMap.AddNamespace("sh", UriFactory.Create("http://www.w3.org/ns/shacl#"))
        g.NamespaceMap.AddNamespace("xsd", UriFactory.Create("http://www.w3.org/2001/XMLSchema#"))

        let reportNode = g.CreateBlankNode("report")
        let rdfType = g.CreateUriNode(UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))

        // sh:ValidationReport type
        g.Assert(reportNode, rdfType,
            g.CreateUriNode(UriFactory.Create("http://www.w3.org/ns/shacl#ValidationReport")))

        // sh:conforms
        let shConforms = g.CreateUriNode(UriFactory.Create("http://www.w3.org/ns/shacl#conforms"))
        g.Assert(reportNode, shConforms,
            g.CreateLiteralNode(report.Conforms.ToString().ToLower(), UriFactory.Create("http://www.w3.org/2001/XMLSchema#boolean")))

        // sh:result entries
        let shResult = g.CreateUriNode(UriFactory.Create("http://www.w3.org/ns/shacl#result"))
        for result in report.Results do
            let resultNode = g.CreateBlankNode()
            g.Assert(reportNode, shResult, resultNode)
            g.Assert(resultNode, rdfType,
                g.CreateUriNode(UriFactory.Create("http://www.w3.org/ns/shacl#ValidationResult")))
            // Add sh:focusNode, sh:resultPath, sh:value, sh:sourceConstraintComponent, sh:resultMessage, sh:resultSeverity
            ...

        g

    let private writeShaclReport (ctx: HttpContext) (report: ValidationReport) = task {
        use graph = buildReportGraph report
        // Delegate to Frank.LinkedData for content-negotiated RDF serialization
        // This depends on Frank.LinkedData exposing graph serialization as a public API
        ...
    }
```

2. Map each `ValidationResult` field to SHACL triples:
   - `FocusNode` -> `sh:focusNode`
   - `ResultPath` -> `sh:resultPath` (as URI)
   - `Value` -> `sh:value` (as typed literal)
   - `SourceConstraint` -> `sh:sourceConstraintComponent` (as URI)
   - `Message` -> `sh:resultMessage` (as string literal)
   - `Severity` -> `sh:resultSeverity` (as URI: `sh:Violation`, `sh:Warning`, `sh:Info`)

**Files**: `src/Frank.Validation/ReportSerializer.fs`
**Notes**: The `use graph` binding ensures disposal after serialization (Constitution VI). Frank.LinkedData's content negotiation API must be verified -- check if it accepts an `IGraph` and writes to `HttpResponse`.

### Subtask T024 -- Implement ValidationReport -> RFC 9457 Problem Details JSON

**Purpose**: Serialize validation failures as RFC 9457 Problem Details for standard JSON clients.

**Steps**:
1. In `ReportSerializer.fs`, implement:

```fsharp
    let private writeProblemDetails (ctx: HttpContext) (report: ValidationReport) = task {
        ctx.Response.ContentType <- "application/problem+json"
        let errors =
            report.Results
            |> List.map (fun r ->
                {| path = sprintf "$.%s" r.ResultPath
                   ``constraint`` = r.SourceConstraint
                   message = r.Message
                   value = r.Value |> Option.defaultValue null |})
        let problemDetails =
            {| ``type`` = "urn:frank:validation:shacl-violation"
               title = "Validation Failed"
               status = 422
               detail = sprintf "Request body violates %d SHACL constraint(s)" report.Results.Length
               errors = errors |}
        do! ctx.Response.WriteAsJsonAsync(problemDetails)
    }
```

2. The `errors` array contains one entry per `ValidationResult` with:
   - `path`: JSON path (e.g., `$.name`, `$.address.zipCode`)
   - `constraint`: SHACL constraint component (e.g., `sh:minCount`, `sh:datatype`)
   - `message`: Human-readable error message
   - `value`: The offending value (null if field was missing)

**Files**: `src/Frank.Validation/ReportSerializer.fs`
**Notes**: Use `System.Text.Json` via `HttpResponse.WriteAsJsonAsync` for serialization. Anonymous records (`{| ... |}`) work well for ad-hoc JSON structures. The `constraint` field name conflicts with F# keyword -- use double backticks.

### Subtask T025 -- Implement nested field path serialization

**Purpose**: Ensure nested field paths (e.g., `customer.address.zipCode`) are correctly represented in both SHACL and Problem Details formats.

**Steps**:
1. For SHACL serialization (`sh:resultPath`):
   - Simple fields: `sh:resultPath` is a single URI (e.g., `urn:frank:property:Name`)
   - Nested fields: `sh:resultPath` is a SHACL property path sequence
   - Implement as a list of URIs connected via `sh:alternativePath` or SHACL sequence path syntax

2. For Problem Details JSON:
   - Simple fields: `$.Name`
   - Nested fields: `$.customer.address.zipCode` (dot-separated JSON path)

3. In `ValidationResult`, the `ResultPath` field stores the dot-separated path. The serializers convert to appropriate format:
   - SHACL: split on `.`, construct URI sequence
   - Problem Details: prepend `$.`

**Files**: `src/Frank.Validation/ReportSerializer.fs`
**Notes**: SHACL property paths can be complex (sequences, alternatives, inverse). For Frank.Validation, use simple sequence paths: `sh:resultPath ( urn:frank:property:customer urn:frank:property:address urn:frank:property:zipCode )`.

### Subtask T026 -- Create `ReportSerializationTests.fs`

**Purpose**: Verify content negotiation and serialization for both semantic and standard clients.

**Steps**:
1. Create `test/Frank.Validation.Tests/ReportSerializationTests.fs`
2. Write tests using TestHost:

**a. Problem Details for `Accept: application/json`**:
- Send invalid request with `Accept: application/json`
- Verify response Content-Type is `application/problem+json`
- Verify response body is valid RFC 9457 Problem Details
- Verify `type`, `title`, `status`, `detail`, `errors` fields present
- Verify `errors` array has correct count and structure

**b. JSON-LD for `Accept: application/ld+json`**:
- Send same invalid request with `Accept: application/ld+json`
- Verify response Content-Type is `application/ld+json`
- Verify response body contains `sh:ValidationReport` type
- Verify `sh:conforms` is false
- Verify `sh:result` array has correct count

**c. Multiple violations**:
- Send request with 3 distinct violations (missing field, wrong type, invalid DU value)
- Verify exactly 3 error entries in both formats

**d. Nested field path**:
- Send request with violation on nested field (e.g., `address.zipCode`)
- Verify Problem Details path is `$.address.zipCode`
- Verify SHACL resultPath contains property URI sequence

**e. No Accept header (default to Problem Details)**:
- Send invalid request with no Accept header
- Verify response is Problem Details JSON

**Files**: `test/Frank.Validation.Tests/ReportSerializationTests.fs`
**Parallel?**: Yes -- depends on T022-T025 but can be scaffolded early.
**Validation**: `dotnet test test/Frank.Validation.Tests/` passes with all tests green.

---

## Implementation Guidance

**Content negotiation note**: Content negotiation precedence for violation responses: (1) If Accept includes `application/ld+json`, `text/turtle`, or `application/rdf+xml`, use Frank.LinkedData serialization for SHACL ValidationReport. (2) If Accept includes `application/problem+json` or `application/json`, use RFC 9457 Problem Details. (3) If Accept is `*/*` or absent, default to Problem Details JSON.

---

## Test Strategy

- Run `dotnet build` to verify compilation of ReportSerializer.fs
- Run `dotnet test` for all serialization and content negotiation tests
- Verify User Story 3 acceptance scenarios from spec.md

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Frank.LinkedData content negotiation API not public | Check LinkedData's public API surface; if IGraph serialization is internal, request an API addition or use dotNetRdf writers directly |
| JSON-LD @context construction complexity | Use a static SHACL context; dotNetRdf's JSON-LD writer may handle this automatically |
| RFC 9457 compliance details | Follow the spec exactly: `type`, `title`, `status`, `detail` are standard members; `errors` is an extension member |
| Anonymous record serialization with System.Text.Json | Verify F# anonymous records serialize correctly; may need `JsonFSharpConverter` options |

---

## Review Guidance

- Verify dual-path serialization: semantic clients get SHACL ValidationReport, standard clients get Problem Details
- Verify `IGraph` disposal after serialization (Constitution VI)
- Verify Problem Details structure matches RFC 9457 and quickstart.md examples exactly
- Verify JSON-LD structure matches quickstart.md examples
- Verify nested field paths render correctly in both formats
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-15T19:35:43Z – unknown – lane=for_review – Moved to for_review
- 2026-03-15T19:44:44Z – claude-opus-reviewer – shell_pid=42258 – lane=doing – Started review via workflow command
