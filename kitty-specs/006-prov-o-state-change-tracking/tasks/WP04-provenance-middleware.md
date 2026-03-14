---
work_package_id: WP04
title: Provenance Middleware (Content Negotiation)
lane: done
dependencies:
- WP02
subtasks: [T017, T018, T019, T020, T021]
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-010]
---

# Work Package Prompt: WP04 -- Provenance Middleware (Content Negotiation)

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

Depends on WP02 (store for querying) and WP03 (graph builder for serialization).

---

## Objectives & Success Criteria

- Middleware intercepts GET requests where `Accept` header matches `application/vnd.frank.provenance+*`
- Supported media types: `+json` (JSON-LD), `+ld+json` (JSON-LD alias), `+turtle` (Turtle), `+rdf+xml` (RDF/XML)
- Middleware queries `IProvenanceStore.QueryByResource` for the request path
- Response is serialized via GraphBuilder + Frank.LinkedData's `writeRdf` (or direct dotNetRdf writers)
- Standard Accept headers (e.g., `application/json`) pass through to normal handler (zero overhead)
- Empty provenance returns 200 with empty graph (not 404)
- Middleware runs before Frank.LinkedData middleware in the pipeline
- Content-Type on response uses the original `vnd.frank.provenance+*` media type

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/006-prov-o-state-change-tracking/research.md` -- Decision 5 (custom media type registration)
- `kitty-specs/006-prov-o-state-change-tracking/spec.md` -- User Story 2 (content-negotiated provenance responses)
- `kitty-specs/006-prov-o-state-change-tracking/quickstart.md` -- curl examples showing expected behavior

**Key constraints**:
- Middleware pattern: check Accept header prefix -> intercept or pass through
- Only intercept GET requests (mutations do not return provenance)
- Media type mapping for serialization:
  - `application/vnd.frank.provenance+json` -> JSON-LD writer
  - `application/vnd.frank.provenance+ld+json` -> JSON-LD writer (alias)
  - `application/vnd.frank.provenance+turtle` -> Turtle writer
  - `application/vnd.frank.provenance+rdf+xml` -> RDF/XML writer
- Resource URI extraction: use `HttpContext.Request.Path` (the route path, not the full URL)
- `IProvenanceStore` resolved from DI via `HttpContext.RequestServices.GetRequiredService<IProvenanceStore>()`
- Follow Frank.LinkedData's middleware pattern for reference (check `src/Frank.LinkedData/WebHostBuilderExtensions.fs`)
- Constitution VII: log errors, do not silently swallow exceptions during serialization

---

## Subtasks & Detailed Guidance

### Subtask T017 -- Create `Middleware.fs` with Accept header parsing

**Purpose**: Create the middleware module with the core Accept header matching logic.

**Steps**:
1. Create `src/Frank.Provenance/Middleware.fs`
2. Define the media type constants and matching function:

```fsharp
namespace Frank.Provenance

open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

/// Custom media types for provenance content negotiation.
[<RequireQualifiedAccess>]
module ProvenanceMediaTypes =
    [<Literal>]
    let ProvenanceJson = "application/vnd.frank.provenance+json"
    [<Literal>]
    let ProvenanceLdJson = "application/vnd.frank.provenance+ld+json"
    [<Literal>]
    let ProvenanceTurtle = "application/vnd.frank.provenance+turtle"
    [<Literal>]
    let ProvenanceRdfXml = "application/vnd.frank.provenance+rdf+xml"

    /// Prefix shared by all provenance media types
    [<Literal>]
    let Prefix = "application/vnd.frank.provenance+"

/// Provenance content negotiation middleware.
module ProvenanceMiddleware =

    /// Check if the Accept header contains a provenance media type.
    /// Returns the matched media type string or None.
    let tryMatchProvenanceAccept (acceptHeader: string) : string option =
        if isNull acceptHeader then None
        else
            // Check for exact matches (most specific first)
            let types = [
                ProvenanceMediaTypes.ProvenanceLdJson   // must check before +json
                ProvenanceMediaTypes.ProvenanceJson
                ProvenanceMediaTypes.ProvenanceTurtle
                ProvenanceMediaTypes.ProvenanceRdfXml
            ]
            types |> List.tryFind (fun t -> acceptHeader.Contains(t))
```

3. Add `Middleware.fs` to `.fsproj` after `GraphBuilder.fs`:
   ```xml
   <Compile Include="Middleware.fs" />
   ```

**Files**: `src/Frank.Provenance/Middleware.fs`
**Notes**:
- Check `+ld+json` before `+json` to avoid false match (both contain "+json")
- `acceptHeader.Contains` handles `Accept` headers with multiple types (e.g., `text/html, application/vnd.frank.provenance+turtle`)
- Null check on `acceptHeader` prevents NullReferenceException for requests without Accept header
- The prefix constant enables a fast-path check: if `acceptHeader` does not contain the prefix, skip all type checks

### Subtask T018 -- Implement media type mapping and response serialization

**Purpose**: Map provenance media types to dotNetRdf serializers and write the graph to the response.

**Steps**:
1. Add serialization logic to `ProvenanceMiddleware`:

```fsharp
open System.IO
open VDS.RDF
open VDS.RDF.Writing

/// Map a provenance media type to a dotNetRdf writer and content type for the response.
let private getWriter (mediaType: string) : IRdfWriter * string =
    match mediaType with
    | ProvenanceMediaTypes.ProvenanceJson
    | ProvenanceMediaTypes.ProvenanceLdJson ->
        // JSON-LD serialization
        (new JsonLdWriter() :> IRdfWriter, mediaType)
    | ProvenanceMediaTypes.ProvenanceTurtle ->
        (new CompressingTurtleWriter() :> IRdfWriter, mediaType)
    | ProvenanceMediaTypes.ProvenanceRdfXml ->
        (new RdfXmlWriter() :> IRdfWriter, mediaType)
    | _ ->
        // Fallback (should not happen due to tryMatchProvenanceAccept)
        (new CompressingTurtleWriter() :> IRdfWriter, ProvenanceMediaTypes.ProvenanceTurtle)

/// Serialize an IGraph to the HTTP response.
let private writeGraphToResponse (context: HttpContext) (graph: IGraph) (writer: IRdfWriter) (contentType: string) = task {
    context.Response.ContentType <- contentType
    context.Response.StatusCode <- 200
    use stringWriter = new StringWriter()
    writer.Save(graph, stringWriter)
    let content = stringWriter.ToString()
    do! context.Response.WriteAsync(content)
}
```

**Files**: `src/Frank.Provenance/Middleware.fs`
**Notes**:
- `CompressingTurtleWriter` produces readable Turtle with prefix declarations
- `JsonLdWriter` produces JSON-LD (verify this class exists in dotNetRdf.Core 3.5.1; if not, use `Newtonsoft.Json`-based JSON-LD writer or check for `VDS.RDF.Writing.JsonLdWriter`)
- The response Content-Type is the ORIGINAL provenance media type (not `text/turtle`), per spec -- clients requested `vnd.frank.provenance+turtle`, they get that back
- `StringWriter` approach is simple; for production perf, could write directly to response stream, but this is acceptable for provenance responses (typically small graphs)
- Check if Frank.LinkedData has a `writeRdf` helper that can be reused instead of direct dotNetRdf writer usage

### Subtask T019 -- Implement resource URI extraction and store query

**Purpose**: Extract the resource URI from the request, query the provenance store, and build the graph.

**Steps**:
1. Implement the core middleware function:

```fsharp
/// The middleware request delegate.
let provenanceMiddleware (next: RequestDelegate) (context: HttpContext) = task {
    // Only intercept GET requests
    if context.Request.Method <> HttpMethods.Get then
        do! next.Invoke(context)
    else
        let acceptHeader = context.Request.Headers.Accept.ToString()

        match tryMatchProvenanceAccept acceptHeader with
        | None ->
            // Not a provenance request -- pass through (zero overhead path)
            do! next.Invoke(context)

        | Some mediaType ->
            let logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Frank.Provenance.Middleware")
            let store = context.RequestServices.GetRequiredService<IProvenanceStore>()

            try
                // Extract resource URI from request path
                let resourceUri = context.Request.Path.Value

                // Query provenance store
                let! records = store.QueryByResource(resourceUri)

                // Build RDF graph
                let graph = GraphBuilder.toGraph records

                // Serialize and write response
                let writer, contentType = getWriter mediaType
                do! writeGraphToResponse context graph writer contentType

                logger.LogDebug("Served provenance for {ResourceUri} with {RecordCount} records as {MediaType}",
                                resourceUri, records.Length, mediaType)
            with ex ->
                logger.LogError(ex, "Failed to serve provenance for {Path}", context.Request.Path.Value)
                context.Response.StatusCode <- 500
                do! context.Response.WriteAsync("Internal server error serving provenance")
}
```

**Files**: `src/Frank.Provenance/Middleware.fs`
**Notes**:
- The `None` branch is the fast path: just `string.Contains` check on Accept header, then pass through
- `context.Request.Path.Value` gives the path portion (e.g., `/orders/42`). This must match what the TransitionObserver stores as `ResourceUri`.
- Error handling: log the exception and return 500 (Constitution VII: no silent swallowing)
- The `store` is resolved from DI per request. If no store is registered, `GetRequiredService` throws -- this is correct (provenance was not configured).

### Subtask T020 -- Implement empty graph handling

**Purpose**: Ensure resources with no provenance records return 200 with an empty graph, not 404.

**Steps**:
1. The implementation in T019 already handles this correctly:
   - `store.QueryByResource` returns empty list for unknown resource URIs
   - `GraphBuilder.toGraph []` produces an empty graph
   - Empty graph serializes to minimal valid output (just namespace prefixes for Turtle)
   - Response status is 200

2. Add explicit test verification (in T021) that empty provenance returns:
   - Status code 200
   - Valid content (not empty body -- at minimum namespace declarations for Turtle)
   - Correct Content-Type header

3. Verify the behavior by adding a log message:
```fsharp
if records.IsEmpty then
    logger.LogDebug("No provenance records for {ResourceUri}, returning empty graph", resourceUri)
```

**Files**: `src/Frank.Provenance/Middleware.fs`
**Notes**: The spec explicitly states "empty graph is returned (200 with empty collection, not 404)" in User Story 2, Scenario 3. This is the correct REST behavior -- the provenance representation exists but contains no records.

### Subtask T021 -- Create `MiddlewareTests.fs` with TestHost integration tests

**Purpose**: Integration tests verifying the full middleware pipeline with TestHost.

**Steps**:
1. Create `test/Frank.Provenance.Tests/MiddlewareTests.fs`
2. Set up TestHost with provenance middleware and a mock store:

```fsharp
let createTestHost (store: IProvenanceStore) =
    WebHostBuilder()
        .ConfigureServices(fun services ->
            services.AddSingleton<IProvenanceStore>(store) |> ignore)
        .Configure(fun app ->
            app.UseMiddleware<...>(ProvenanceMiddleware.provenanceMiddleware) |> ignore
            // ... or use app.Use pattern
            app.Run(fun ctx -> ctx.Response.WriteAsync("normal response")))
        .UseTestServer()
```

3. Write tests covering:

**a. Provenance Turtle response**: Seed store with records, GET with `Accept: application/vnd.frank.provenance+turtle`. Verify 200, Content-Type matches, body contains `prov:Activity`.

**b. Provenance JSON-LD response**: Same setup, GET with `Accept: application/vnd.frank.provenance+ld+json`. Verify 200, Content-Type matches, body contains JSON.

**c. Standard Accept passthrough**: GET with `Accept: application/json`. Verify response is "normal response" (middleware passed through).

**d. Non-GET passthrough**: POST with provenance Accept header. Verify middleware passes through (only GET is intercepted).

**e. Empty provenance**: GET with provenance Accept on resource with no records. Verify 200 (not 404), valid body.

**f. No Accept header**: GET with no Accept header. Verify middleware passes through.

**g. Mixed Accept header**: GET with `Accept: text/html, application/vnd.frank.provenance+turtle`. Verify provenance is served (turtle type is present in Accept).

4. Add `MiddlewareTests.fs` to test `.fsproj`

**Files**: `test/Frank.Provenance.Tests/MiddlewareTests.fs`
**Validation**: `dotnet test test/Frank.Provenance.Tests/` passes with all middleware tests green.
**Notes**: For the mock store, create a simple implementation of `IProvenanceStore` that returns pre-seeded records. Alternatively, use a real `MailboxProcessorProvenanceStore` seeded with test records (depends on WP02 being complete).

---

## Test Strategy

- Run `dotnet build` to verify compilation on all targets
- Run `dotnet test test/Frank.Provenance.Tests/` -- all middleware tests pass
- Verify middleware does not interfere with non-provenance requests (passthrough tests)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| `JsonLdWriter` may not exist in dotNetRdf.Core 3.5.1 | Check `VDS.RDF.Writing` namespace. If absent, use `JsonLdWriter` from `dotNetRdf` full package or `VDS.RDF.Writing.StringWriter` with manual JSON-LD construction. Frank.LinkedData may have a JSON-LD writer already. |
| Middleware ordering with Frank.LinkedData | Provenance middleware checks for `vnd.frank.provenance+*` prefix specifically; LinkedData checks for standard RDF types. No conflict if provenance runs first. Document ordering requirement. |
| Response body encoding | Use `context.Response.WriteAsync` which handles UTF-8 encoding correctly. |
| `IProvenanceStore` not registered in DI | `GetRequiredService` throws clear exception. This is by design -- provenance was not configured. |

---

## Review Guidance

- Verify Accept header matching checks `+ld+json` before `+json`
- Verify only GET requests are intercepted (POST/PUT/DELETE pass through)
- Verify empty provenance returns 200 (not 404)
- Verify Content-Type on response is the provenance media type (not `text/turtle`)
- Verify error handling logs and returns 500 (no silent swallowing)
- Verify the fast path (no provenance Accept) has minimal overhead (just a string prefix check)
- Check Frank.LinkedData source for reusable `writeRdf` patterns
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
