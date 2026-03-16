---
work_package_id: "WP03"
subtasks:
  - "T009"
  - "T010"
  - "T011"
title: "Link Header Middleware"
phase: "Phase 1 - Core Implementation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP02"]
requirement_refs: ["FR-004", "FR-005", "FR-006", "FR-010", "FR-014"]
history:
  - timestamp: "2026-03-16T01:20:58Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP03 -- Link Header Middleware

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

- Implement `LinkHeaderMiddleware` that appends RFC 8288 Link headers to 2xx GET/HEAD responses from endpoints with `DiscoveryMediaType` metadata.
- Add `useLinkHeaders` and `useDiscovery` custom operations to `WebHostBuilderExtensions.fs`.
- Write acceptance tests covering US2, US3, and US4 scenarios from the spec.
- Link headers are NOT added to error responses (4xx/5xx), non-GET/HEAD methods, or resources without `DiscoveryMediaType` metadata.
- `dotnet build` and `dotnet test` succeed.

## Context & Constraints

- **Spec**: `kitty-specs/019-options-link-discovery/spec.md` -- US2, US3, US4 acceptance scenarios
- **Plan**: `kitty-specs/019-options-link-discovery/plan.md` -- AD-02 (separate middleware), AD-06 (per-resource opt-in is implicit via metadata presence)
- **Data Model**: `kitty-specs/019-options-link-discovery/data-model.md` -- `LinkHeaderMiddleware` request-time behavior
- **Research**: `kitty-specs/019-options-link-discovery/research.md` -- R-02 (RFC 8288 Link header format)
- **Quickstart**: `kitty-specs/019-options-link-discovery/quickstart.md` -- expected Link header output format

**Key design decision (AD-06)**: There is NO separate `DiscoveryMarker` or `discoverable` custom operation. The presence of `DiscoveryMediaType` entries in endpoint metadata is sufficient to trigger Link header emission. This means US3 (per-resource opt-in) is satisfied automatically -- if a resource has `DiscoveryMediaType` metadata, it gets Link headers; if not, it doesn't.

**Implementation command**: `spec-kitty implement WP03 --base WP02`

---

## Subtasks & Detailed Guidance

### Subtask T009 -- Create `src/Frank.Discovery/LinkHeaderMiddleware.fs`

- **Purpose**: Implement the middleware that inspects endpoint metadata for `DiscoveryMediaType` entries and appends RFC 8288 Link headers to successful GET/HEAD responses.

- **Steps**:
  1. Create `src/Frank.Discovery/LinkHeaderMiddleware.fs`.
  2. Use namespace `Frank.Discovery`.
  3. The middleware needs these constructor dependencies:
     - `next: RequestDelegate` -- the next middleware in the pipeline
     - `logger: ILogger<LinkHeaderMiddleware>` -- for logging

  4. Implement the `Invoke(ctx: HttpContext)` method with this logic:

     ```
     a. Get matched endpoint: ctx.GetEndpoint()
     b. If endpoint is null -> call next, return (no route match, zero overhead)
     c. Collect DiscoveryMediaType entries from endpoint metadata
     d. If no entries -> call next, return (zero overhead for unmarked resources -- SC-007)
     e. Check if request method is GET or HEAD
        -> if not, call next, return (Link headers only on GET/HEAD)
     f. Call next to execute the handler
     g. After handler execution, check response status code
     h. If status is 2xx (200-299):
        -> Deduplicate DiscoveryMediaType entries by (MediaType, Rel) tuple
        -> For each entry, append a Link header value
     i. If status is not 2xx -> do nothing (FR-010)
     ```

  5. For step (c), collecting `DiscoveryMediaType` entries from endpoint metadata:
     ```fsharp
     let endpoint = ctx.GetEndpoint()
     // Try GetOrderedMetadata first (for struct types registered in metadata)
     let mediaTypes =
         endpoint.Metadata
         |> Seq.choose (fun m ->
             match m with
             | :? DiscoveryMediaType as d -> Some d
             | _ -> None)
         |> Seq.toList
     ```

  6. For step (h), formatting Link headers per RFC 8288 (R-02):
     ```fsharp
     let requestPath = ctx.Request.Path.Value
     for mt in dedupedMediaTypes do
         let linkValue = sprintf "<%s>; rel=\"%s\"; type=\"%s\"" requestPath mt.Rel mt.MediaType
         ctx.Response.Headers.Append("Link", linkValue)
     ```

     **RFC 8288 format**: `<URI>; rel="relation-type"; type="media-type"`
     - Target URI is the request path (the resource's own URI).
     - `rel` is the link relation (e.g., `"describedby"`).
     - `type` is the media type hint.
     - Each `DiscoveryMediaType` gets its own separate `Link` header value (per research R-02: separate headers are simpler and equally valid).

  7. For the 2xx status check:
     ```fsharp
     let isSuccess = ctx.Response.StatusCode >= 200 && ctx.Response.StatusCode < 300
     ```

  8. **Important**: The middleware must call `next` BEFORE checking the status code, because the handler sets the status code. The flow is:
     ```fsharp
     // 1. Check metadata and method BEFORE calling next
     // 2. Call next (handler executes, sets status code)
     // 3. After next returns, check status and add headers
     ```

  9. **Header timing**: `ctx.Response.Headers` can be modified AFTER calling `next` as long as the response has not started streaming. For most cases, the response body is buffered and headers can still be added. However, if the response has already started (e.g., streaming), adding headers will throw. Use `ctx.Response.HasStarted` to guard:
     ```fsharp
     if not ctx.Response.HasStarted && isSuccess then
         // Add Link headers
     ```

  10. Update `src/Frank.Discovery/Frank.Discovery.fsproj` compile order. `LinkHeaderMiddleware.fs` must come after `OptionsDiscoveryMiddleware.fs` and before `WebHostBuilderExtensions.fs`:
      ```xml
      <ItemGroup>
        <Compile Include="OptionsDiscoveryMiddleware.fs" />
        <Compile Include="LinkHeaderMiddleware.fs" />
        <Compile Include="WebHostBuilderExtensions.fs" />
      </ItemGroup>
      ```

- **Files**: `src/Frank.Discovery/LinkHeaderMiddleware.fs`, `src/Frank.Discovery/Frank.Discovery.fsproj`
- **Parallel?**: No -- T010 depends on this file.
- **Notes**:
  - Zero overhead for unmarked resources: The metadata check happens BEFORE calling `next`. If no `DiscoveryMediaType` entries exist, `next` is called immediately with no interception.
  - Link headers on HEAD: HEAD responses are GET responses without a body. The middleware treats HEAD the same as GET for Link header purposes (FR-010).
  - `ctx.Response.Headers.Append` adds a new header value (not replacing existing ones). This correctly handles the case where the handler or other middleware already set Link headers.

### Subtask T010 -- Add `useLinkHeaders` and `useDiscovery` to `WebHostBuilderExtensions.fs`

- **Purpose**: Complete the WebHostBuilder custom operations for Link header middleware and the combined convenience operation.

- **Steps**:
  1. Open `src/Frank.Discovery/WebHostBuilderExtensions.fs` (created in WP02/T007).
  2. Add two additional custom operations to the existing `WebHostBuilder` type extension:

     ```fsharp
     /// Registers the Link header middleware. Responses to GET/HEAD requests
     /// from endpoints with DiscoveryMediaType metadata will include
     /// RFC 8288 Link headers (on 2xx responses only).
     [<CustomOperation("useLinkHeaders")>]
     member _.UseLinkHeaders(spec: WebHostSpec) : WebHostSpec =
         { spec with
             Middleware = spec.Middleware >> fun app ->
                 app.UseMiddleware<LinkHeaderMiddleware>() }

     /// Convenience: registers both OPTIONS discovery and Link header middlewares.
     [<CustomOperation("useDiscovery")>]
     member this.UseDiscovery(spec: WebHostSpec) : WebHostSpec =
         spec
         |> this.UseOptionsDiscovery
         |> this.UseLinkHeaders
     ```

  3. **IMPORTANT**: The `useDiscovery` operation must chain `UseOptionsDiscovery` then `UseLinkHeaders` in that order. The OPTIONS middleware should run before the Link header middleware in the pipeline (though they handle different request methods, ordering for consistency is good practice).

  4. **Note on `this` vs `_`**: The `useDiscovery` operation calls other instance members, so it needs `this` (or `__.`) as the self-identifier. `useOptionsDiscovery` and `useLinkHeaders` can use `_` since they don't call other instance methods.

     Actually, `useDiscovery` cannot call `this.UseOptionsDiscovery(spec)` because custom operations are not regular methods that can be called directly. Instead, compose the middleware registrations directly:
     ```fsharp
     [<CustomOperation("useDiscovery")>]
     member _.UseDiscovery(spec: WebHostSpec) : WebHostSpec =
         { spec with
             Middleware = spec.Middleware >> fun app ->
                 app.UseMiddleware<OptionsDiscoveryMiddleware>()
                     .UseMiddleware<LinkHeaderMiddleware>() }
     ```

- **Files**: `src/Frank.Discovery/WebHostBuilderExtensions.fs`
- **Parallel?**: No -- depends on T009 (LinkHeaderMiddleware type must exist).
- **Notes**:
  - The `useLinkHeaders` middleware is registered in `spec.Middleware` (after routing), same as `useOptionsDiscovery`. Both middlewares need `ctx.GetEndpoint()` to be available, which requires routing to have run.
  - The `useDiscovery` convenience operation matches the API shown in quickstart.md.

### Subtask T011 -- Create `test/Frank.Discovery.Tests/LinkHeaderTests.fs`

- **Purpose**: Write acceptance tests for User Stories 2, 3, and 4 (Link header discovery).

- **Steps**:
  1. Create `test/Frank.Discovery.Tests/LinkHeaderTests.fs`.
  2. Reuse the `TestEndpointDataSource` and test server helper from `OptionsDiscoveryTests.fs`. Consider extracting shared helpers into a `TestHelpers.fs` module if needed.

  3. Create a test server builder that includes the Link header middleware:
     ```fsharp
     let createLinkTestServer (resources: Resource list) =
         // Similar to OptionsDiscoveryTests but with LinkHeaderMiddleware
         // registered via app.UseMiddleware<LinkHeaderMiddleware>()
     ```

  4. Implement these test cases:

     **US2 Tests (Link headers on GET responses)**:

     **Test 1 -- GET request to resource with DiscoveryMediaType metadata**:
     - Register a resource at `/items` with a `get` handler and `DiscoveryMediaType` entries for `application/ld+json`, `text/turtle`, `application/rdf+xml` (all with `rel="describedby"`).
     - Send `GET /items`.
     - Assert response status is 200.
     - Assert response contains `Link` headers with values:
       - `</items>; rel="describedby"; type="application/ld+json"`
       - `</items>; rel="describedby"; type="text/turtle"`
       - `</items>; rel="describedby"; type="application/rdf+xml"`
     - Assert normal response body is present.

     **Test 2 -- GET request to resource with no semantic markers**:
     - Register a resource at `/health` with only a `get` handler, no `DiscoveryMediaType` metadata.
     - Send `GET /health`.
     - Assert response status is 200.
     - Assert NO `Link` headers are present.

     **US3 Tests (Per-resource opt-in)**:

     **Test 3 -- Two resources, one with metadata, one without**:
     - Register `/items` with `DiscoveryMediaType` metadata and `/health` without.
     - Send `GET /items` -- assert Link headers present.
     - Send `GET /health` -- assert no Link headers.

     **US4 Tests (Global enablement via useDiscovery/useLinkHeaders)**:

     **Test 4 -- Global enablement with multiple resources**:
     - Register multiple resources: one with LinkedData metadata, one with Statecharts metadata, one with no metadata.
     - Use `useLinkHeaders` globally.
     - Send GET to each.
     - Assert Link headers on marked resources only, not on unmarked.

     **Additional behavioral tests**:

     **Test 5 -- No Link headers on error responses**:
     - Register a resource that returns 404 (or 500).
     - Send GET.
     - Assert NO Link headers despite `DiscoveryMediaType` metadata being present.

     **Test 6 -- No Link headers on POST/PUT/DELETE**:
     - Register a resource with `DiscoveryMediaType` metadata and a `post` handler.
     - Send POST.
     - Assert NO Link headers (Link headers are GET/HEAD only).

     **Test 7 -- Link headers on HEAD requests**:
     - Register a resource with `DiscoveryMediaType` metadata and a `get` handler.
     - Send HEAD (or GET -- HEAD may need explicit handler).
     - Assert Link headers are present.
     - Note: If Frank doesn't auto-generate HEAD from GET, you may need an explicit `head` handler.

  5. Update `test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj` compile order:
     ```xml
     <ItemGroup>
       <Compile Include="OptionsDiscoveryTests.fs" />
       <Compile Include="LinkHeaderTests.fs" />
       <Compile Include="Program.fs" />
     </ItemGroup>
     ```

  6. Run all tests: `dotnet test test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`

- **Files**: `test/Frank.Discovery.Tests/LinkHeaderTests.fs`, `test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`
- **Parallel?**: No -- depends on T009 and T010.
- **Notes**:
  - To verify Link headers in tests, use `response.Headers.GetValues("Link")` which returns all Link header values. Alternatively, check `response.Headers.Contains("Link")` and iterate.
  - For HEAD requests: ASP.NET Core automatically handles HEAD for GET endpoints (returns headers only, no body). So testing HEAD may just work with a GET handler. If not, add an explicit `head` handler.
  - Consider extracting the `TestEndpointDataSource` and test server builder into a shared `TestHelpers.fs` file (compiled first in the test project) to avoid duplication between `OptionsDiscoveryTests.fs` and `LinkHeaderTests.fs`.

---

## Risks & Mitigations

- **Risk**: Response headers may not be modifiable after `next()` if the response has started streaming. **Mitigation**: Guard with `ctx.Response.HasStarted` check. For most Frank handlers (small response bodies), headers are still modifiable after the handler returns.
- **Risk**: `ctx.Response.Headers.Append("Link", ...)` may not work as expected on all .NET versions. **Mitigation**: Test on net10.0 (the test target). The `IHeaderDictionary.Append` method is available in all supported .NET versions.
- **Risk**: HEAD request behavior may vary. **Mitigation**: Test explicitly. ASP.NET Core's `UseRouting()` typically handles HEAD by routing to GET endpoints automatically.

## Review Guidance

- Verify Link header format matches RFC 8288: `<URI>; rel="..."; type="..."` (angle brackets around URI, semicolons between parameters, quoted values).
- Verify zero overhead for unmarked resources (metadata check before calling `next`).
- Verify Link headers are NOT added on error responses (4xx/5xx).
- Verify Link headers are added on both GET and HEAD 2xx responses.
- Verify `useDiscovery` registers both middlewares.
- Verify deduplication by `(MediaType, Rel)` tuple.
- Check test coverage: US2 scenarios 1-3, US3 scenarios 1-2, US4 scenarios 1-2.

## Activity Log

- 2026-03-16T01:20:58Z -- system -- lane=planned -- Prompt created.
