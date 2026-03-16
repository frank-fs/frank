---
work_package_id: WP04
title: Extension Integration and Edge Cases
lane: "done"
dependencies:
- WP02
- WP03
subtasks:
- T012
- T013
- T014
- T015
phase: Phase 2 - Integration
assignee: ''
agent: ''
shell_pid: ''
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-16T01:20:58Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-009, FR-011, FR-012]
---

# Work Package Prompt: WP04 -- Extension Integration and Edge Cases

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

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````xml`

---

## Objectives & Success Criteria

- Modify `Frank.LinkedData` to add `DiscoveryMediaType` entries when the `linkedData` marker is applied.
- Modify `Frank.Statecharts` to add `DiscoveryMediaType` entries when `stateMachine` metadata is applied.
- Write edge case tests covering CORS coexistence, explicit OPTIONS handler precedence, media type deduplication, HEAD request behavior, and Link headers on error responses.
- Ensure the final test project `.fsproj` includes all test files in correct compile order.
- `dotnet build Frank.sln` and `dotnet test` succeed with all tests passing.

## Context & Constraints

- **Spec**: `kitty-specs/019-options-link-discovery/spec.md` -- FR-011 (LinkedData media types), FR-012 (Statecharts media types), edge cases section
- **Plan**: `kitty-specs/019-options-link-discovery/plan.md` -- AD-05 (CORS coexistence), AD-04 (explicit handler precedence)
- **Data Model**: `kitty-specs/019-options-link-discovery/data-model.md` -- media types contributed by each extension
- **Research**: `kitty-specs/019-options-link-discovery/research.md` -- R-03 (CORS preflight detection), R-04 (explicit handler detection)

**Key file references**:
- `src/Frank.LinkedData/ResourceBuilderExtensions.fs` -- modify the `linkedData` custom operation
- `src/Frank.Statecharts/ResourceBuilderExtensions.fs` -- modify the `stateMachine` custom operation
- `src/Frank.Statecharts/Types.fs` -- `StateMachineMetadata` type definition
- `src/Frank/Builder.fs` -- `DiscoveryMediaType` struct (from WP01)

**Implementation command**: `spec-kitty implement WP04 --base WP03`

---

## Subtasks & Detailed Guidance

### Subtask T012 -- Modify Frank.LinkedData to add `DiscoveryMediaType` entries [P]

- **Purpose**: When the `linkedData` marker is applied to a resource, automatically register the RDF media types as `DiscoveryMediaType` endpoint metadata so they are discoverable via OPTIONS and Link headers.

- **Steps**:
  1. Open `src/Frank.LinkedData/ResourceBuilderExtensions.fs`.
  2. Modify the `linkedData` custom operation to add `DiscoveryMediaType` entries alongside the existing `LinkedDataMarker`:

     Current code:
     ```fsharp
     [<CustomOperation("linkedData")>]
     member _.LinkedData(spec: ResourceSpec) : ResourceSpec =
         ResourceBuilder.AddMetadata(spec, fun b -> b.Metadata.Add(LinkedDataMarker))
     ```

     Updated code:
     ```fsharp
     [<CustomOperation("linkedData")>]
     member _.LinkedData(spec: ResourceSpec) : ResourceSpec =
         ResourceBuilder.AddMetadata(spec, fun b ->
             b.Metadata.Add(LinkedDataMarker)
             b.Metadata.Add({ MediaType = "application/ld+json"; Rel = "describedby" } : DiscoveryMediaType)
             b.Metadata.Add({ MediaType = "text/turtle"; Rel = "describedby" } : DiscoveryMediaType)
             b.Metadata.Add({ MediaType = "application/rdf+xml"; Rel = "describedby" } : DiscoveryMediaType))
     ```

  3. The `DiscoveryMediaType` type is in the `Frank.Builder` module (which `Frank.LinkedData` already opens via `open Frank.Builder`). The type may need an explicit `open Frank` or the full qualification `Frank.Builder.DiscoveryMediaType` depending on how the module is structured. Check that the type resolves.

  4. The three media types match `src/Frank.LinkedData/WebHostBuilderExtensions.fs` line 22: `"application/ld+json"`, `"text/turtle"`, `"application/rdf+xml"`. All use `rel="describedby"` per the data model.

  5. Build to verify: `dotnet build src/Frank.LinkedData/Frank.LinkedData.fsproj`

- **Files**: `src/Frank.LinkedData/ResourceBuilderExtensions.fs`
- **Parallel?**: Yes -- independent of T013 (Statecharts).
- **Notes**:
  - This is a 3-line addition to an existing function. The change is minimal.
  - The `LinkedDataMarker` must remain -- it is used by the LinkedData content negotiation middleware. The `DiscoveryMediaType` entries are *additional* metadata for the discovery middleware.
  - The type annotation `: DiscoveryMediaType` may be needed on the struct record literal since F# can be ambiguous about struct records. If the compiler can't infer the type, add the annotation.
  - **FR-011 validation**: Verify that these three media types exactly match what the LinkedData middleware supports.

### Subtask T013 -- Modify Frank.Statecharts to add `DiscoveryMediaType` entries [P]

- **Purpose**: When the `stateMachine` metadata is applied to a resource, register the statechart spec media types as `DiscoveryMediaType` endpoint metadata.

- **Steps**:
  1. Open `src/Frank.Statecharts/ResourceBuilderExtensions.fs`.
  2. Examine what media types Statecharts actually supports. Check:
     - `src/Frank.Statecharts/Middleware.fs` -- what content types does the statechart middleware handle?
     - `src/Frank.Statecharts/Wsd/` directory -- WSD (Web Sequence Diagram) support
     - The data model suggests `application/scxml+xml` as a candidate.

  3. Based on findings, modify the `stateMachine` custom operation to add `DiscoveryMediaType` entries:

     Current code:
     ```fsharp
     [<CustomOperation("stateMachine")>]
     member _.StateMachine(spec: ResourceSpec, metadata: StateMachineMetadata) : ResourceSpec =
         ResourceBuilder.AddMetadata(spec, fun builder -> builder.Metadata.Add(metadata))
     ```

     Updated code (example with SCXML):
     ```fsharp
     [<CustomOperation("stateMachine")>]
     member _.StateMachine(spec: ResourceSpec, metadata: StateMachineMetadata) : ResourceSpec =
         ResourceBuilder.AddMetadata(spec, fun builder ->
             builder.Metadata.Add(metadata)
             builder.Metadata.Add({ MediaType = "application/scxml+xml"; Rel = "describedby" } : DiscoveryMediaType))
     ```

  4. **Important**: The exact media types depend on what the Statecharts package actually supports. Inspect the Statecharts source code to determine the correct set. Common candidates:
     - `application/scxml+xml` -- SCXML (State Chart XML)
     - Other formats listed in the Wsd directory

     If the Statecharts package doesn't currently have content negotiation for any of these formats, add only `application/scxml+xml` as a starting point (it's the W3C standard for state machine serialization).

  5. The `DiscoveryMediaType` type needs to be accessible. Since `Frank.Statecharts` already references `Frank` (core), the type should be available via `open Frank.Builder`. Verify.

  6. Build to verify: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`

- **Files**: `src/Frank.Statecharts/ResourceBuilderExtensions.fs`
- **Parallel?**: Yes -- independent of T012 (LinkedData).
- **Notes**:
  - The change is 1-3 lines (one `DiscoveryMediaType` entry per supported media type).
  - **FR-012 validation**: The media types should represent formats that the Statecharts package can actually serve. If it doesn't currently serve any semantic format, add `application/scxml+xml` as a placeholder that can be activated when SCXML content negotiation is implemented.
  - Check `src/Frank.Statecharts/Frank.Statecharts.fsproj` to confirm it has a `ProjectReference` to `Frank.fsproj`.

### Subtask T014 -- Create edge case tests

- **Purpose**: Cover the edge cases enumerated in the spec to ensure robustness.

- **Steps**:
  1. Create `test/Frank.Discovery.Tests/EdgeCaseTests.fs`.
  2. Reuse `TestEndpointDataSource` and test server helpers from earlier test files.

  3. Implement these edge case tests:

     **Test 1 -- CORS preflight pass-through**:
     - Register a resource with `get` handler and `DiscoveryMediaType` metadata.
     - Register BOTH the discovery middleware and a simple CORS-like handler.
     - Send `OPTIONS /items` with headers:
       - `Origin: http://example.com`
       - `Access-Control-Request-Method: GET`
     - Assert the discovery middleware does NOT intercept (it passes through due to CORS preflight detection).
     - The key assertion: the response does NOT have the `Allow` header set by the discovery middleware.

     **Test 2 -- Explicit OPTIONS handler precedence**:
     - Register a resource with both a `get` handler and an explicit `options` handler:
       ```fsharp
       resource "/items" {
           get (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("get"))
           options (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("custom OPTIONS"))
       }
       ```
     - Add `DiscoveryMediaType` metadata to the resource.
     - Enable discovery middleware.
     - Send `OPTIONS /items`.
     - Assert the response body is `"custom OPTIONS"` (the explicit handler ran, not the discovery middleware).
     - Assert the `Allow` header is NOT set by the discovery middleware (or if it is, the explicit handler's output takes precedence).

     **Test 3 -- Media type deduplication**:
     - Register a resource with duplicate `DiscoveryMediaType` entries (e.g., two entries for `application/ld+json` with `rel="describedby"`).
     - Send `OPTIONS /items`.
     - Assert the `application/ld+json` media type appears only once (not duplicated).
     - Send `GET /items` (with Link middleware enabled).
     - Assert only one `Link` header for `application/ld+json`.

     **Test 4 -- Link headers on HEAD requests**:
     - Register a resource with a `get` handler and `DiscoveryMediaType` metadata.
     - Enable Link header middleware.
     - Send `HEAD /items`.
     - Assert Link headers are present.
     - Assert response body is empty (HEAD semantics).

     **Test 5 -- No Link headers on error responses**:
     - Register a resource with `DiscoveryMediaType` metadata and a handler that returns 404:
       ```fsharp
       get (fun (ctx: HttpContext) ->
           ctx.Response.StatusCode <- 404
           ctx.Response.WriteAsync("not found"))
       ```
     - Enable Link header middleware.
     - Send `GET /items`.
     - Assert response status is 404.
     - Assert NO `Link` headers are present (FR-010: 2xx only).

     **Test 6 -- Resource with no handlers (empty handler list)**:
     - This is an unusual edge case. A resource with no handlers but with the discovery middleware active.
     - If routing matches the endpoint (which requires at least one handler), the OPTIONS response would include `Allow: OPTIONS`.
     - If no routing match occurs, the middleware passes through.
     - This test may be skipped if Frank requires at least one handler per resource.

     **Test 7 -- Multiple extensions contributing media types**:
     - Register a resource with `DiscoveryMediaType` entries from both "LinkedData" and "Statecharts" (simulated with direct metadata addition).
     - Send `OPTIONS /workflow`.
     - Assert all media types from both extensions appear in the response.
     - Send `GET /workflow`.
     - Assert `Link` headers include entries from both extensions.

  4. Run all tests: `dotnet test test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`

- **Files**: `test/Frank.Discovery.Tests/EdgeCaseTests.fs`
- **Parallel?**: No -- depends on both middlewares and extensions being in place.
- **Notes**:
  - The CORS test (Test 1) doesn't require actual CORS middleware to be registered. The discovery middleware's pass-through is based solely on the `Access-Control-Request-Method` header being present. The test just verifies the middleware doesn't intercept.
  - The explicit OPTIONS handler test (Test 2) requires understanding how Frank routes OPTIONS requests when an explicit handler is defined. The key is that Frank creates an endpoint with `HttpMethodMetadata(["OPTIONS"])`, which the discovery middleware detects.
  - For Test 4 (HEAD), ASP.NET Core routing may or may not automatically create a HEAD endpoint from a GET handler. Test behavior may vary. If HEAD doesn't route to the GET handler, the test may need adjustment.

### Subtask T015 -- Update test `.fsproj` compile order

- **Purpose**: Ensure all test source files are included in the correct F# compilation order.

- **Steps**:
  1. Open `test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`.
  2. Update the `Compile` item group to include all test files in the correct order:
     ```xml
     <ItemGroup>
       <Compile Include="OptionsDiscoveryTests.fs" />
       <Compile Include="LinkHeaderTests.fs" />
       <Compile Include="EdgeCaseTests.fs" />
       <Compile Include="Program.fs" />
     </ItemGroup>
     ```
  3. If shared test helpers (like `TestEndpointDataSource` and server builder) were extracted into a `TestHelpers.fs` file, it must come FIRST:
     ```xml
     <ItemGroup>
       <Compile Include="TestHelpers.fs" />
       <Compile Include="OptionsDiscoveryTests.fs" />
       <Compile Include="LinkHeaderTests.fs" />
       <Compile Include="EdgeCaseTests.fs" />
       <Compile Include="Program.fs" />
     </ItemGroup>
     ```
  4. `Program.fs` must always be LAST (it's the entry point that discovers tests in the assembly).
  5. Verify: `dotnet build test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`
  6. Run all tests: `dotnet test test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`

- **Files**: `test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`
- **Parallel?**: No -- must be done after all test files are created.
- **Notes**: F# compilation order matters. Files that define types used by later files must come first. `Program.fs` must be last.

---

## Risks & Mitigations

- **Risk**: `DiscoveryMediaType` struct may not be pattern-matchable from boxed `obj` in endpoint metadata. **Mitigation**: Metadata items are stored as `obj` in `IList<obj>`. Pattern matching with `:? DiscoveryMediaType as d` on a boxed struct works in F# because the compiler handles struct unboxing. If it doesn't work, use `Seq.filter` with a type check.
- **Risk**: The Statecharts package may not have clearly defined media types. **Mitigation**: Add `application/scxml+xml` as the standard SCXML type. The discovery mechanism is composable, so additional types can be added later.
- **Risk**: HEAD request routing behavior may differ from expectations. **Mitigation**: ASP.NET Core 8+ automatically handles HEAD for GET endpoints. Test confirms behavior.
- **Risk**: CORS and discovery middleware ordering may cause unexpected behavior in production. **Mitigation**: Document in the quickstart that CORS middleware should be registered BEFORE discovery middleware.

## Review Guidance

- Verify Frank.LinkedData adds exactly three `DiscoveryMediaType` entries matching its supported media types.
- Verify Frank.Statecharts adds at least one `DiscoveryMediaType` entry for a meaningful media type.
- Verify edge case tests cover: CORS pass-through, explicit handler precedence, deduplication, HEAD requests, error responses.
- Verify the test project `.fsproj` has correct compile order with `Program.fs` last.
- Run `dotnet build Frank.sln` to verify the entire solution builds.
- Run `dotnet test test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj` to verify all tests pass.
- Cross-check: `dotnet test test/Frank.LinkedData.Tests/Frank.LinkedData.Tests.fsproj` still passes (LinkedData changes don't break existing tests).
- Cross-check: `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` still passes (Statecharts changes don't break existing tests).

## Activity Log

- 2026-03-16T01:20:58Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T14:33:09Z – unknown – lane=done – Moved to done
