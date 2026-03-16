---
work_package_id: "WP02"
subtasks:
  - "T006"
  - "T007"
  - "T008"
title: "OPTIONS Discovery Middleware"
phase: "Phase 1 - Core Implementation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs: ["FR-001", "FR-002", "FR-006", "FR-007", "FR-008", "FR-009", "FR-013"]
history:
  - timestamp: "2026-03-16T01:20:58Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- OPTIONS Discovery Middleware

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

- Implement `OptionsDiscoveryMiddleware` that generates implicit OPTIONS responses for Frank resources.
- The middleware builds an `Allow` header from all HTTP methods registered for the matched route.
- The middleware aggregates and deduplicates `DiscoveryMediaType` entries from endpoint metadata.
- The middleware correctly passes through for CORS preflight requests, explicit OPTIONS handlers, and unmatched routes.
- Implement the `useOptionsDiscovery` custom operation on `WebHostBuilder`.
- Write acceptance tests covering US1 scenarios from the spec.
- `dotnet build` and `dotnet test` succeed.

## Context & Constraints

- **Spec**: `kitty-specs/019-options-link-discovery/spec.md` -- US1 acceptance scenarios
- **Plan**: `kitty-specs/019-options-link-discovery/plan.md` -- AD-02 (two separate middlewares), AD-03 (endpoint enumeration), AD-04 (explicit handler detection), AD-05 (CORS coexistence)
- **Data Model**: `kitty-specs/019-options-link-discovery/data-model.md` -- `OptionsDiscoveryMiddleware` constructor dependencies and request-time behavior
- **Research**: `kitty-specs/019-options-link-discovery/research.md` -- R-01 (sibling enumeration), R-03 (CORS detection), R-04 (explicit handler detection)
- **Quickstart**: `kitty-specs/019-options-link-discovery/quickstart.md` -- expected usage and response format

**IMPORTANT**: The `ResourceEndpointDataSource` in Frank core is `internal`. Tests cannot access it directly. Tests must create their own `EndpointDataSource` subclass (see `TestEndpointDataSource` in `test/Frank.Auth.Tests/AuthorizationTests.fs` for the pattern).

**Implementation command**: `spec-kitty implement WP02 --base WP01`

---

## Subtasks & Detailed Guidance

### Subtask T006 -- Create `src/Frank.Discovery/OptionsDiscoveryMiddleware.fs`

- **Purpose**: Implement the core middleware that intercepts OPTIONS requests and builds implicit discovery responses.

- **Steps**:
  1. Create `src/Frank.Discovery/OptionsDiscoveryMiddleware.fs`.
  2. Use the namespace `Frank.Discovery`.
  3. The middleware class needs these constructor dependencies (injected via DI):
     - `next: RequestDelegate` -- the next middleware in the pipeline
     - `logger: ILogger<OptionsDiscoveryMiddleware>` -- for logging
  4. The middleware also needs access to all endpoints. Inject `EndpointDataSource` via DI. Since Frank uses a single `ResourceEndpointDataSource`, injecting `EndpointDataSource` (singular) works. However, for robustness, consider injecting `IEnumerable<EndpointDataSource>` and aggregating endpoints from all sources. Choose whichever approach compiles cleanly.

  5. Implement the `Invoke(ctx: HttpContext)` method with this logic:

     ```
     a. If request method is not OPTIONS -> call next, return
     b. If Access-Control-Request-Method header is present -> call next, return (CORS preflight)
     c. Get matched endpoint: ctx.GetEndpoint()
     d. If endpoint is null -> call next, return (no route match)
     e. Check if endpoint has HttpMethodMetadata containing "OPTIONS"
        -> if yes, call next, return (explicit handler takes precedence)
     f. Cast endpoint to RouteEndpoint, extract RoutePattern.RawText
     g. Enumerate all endpoints from the data source(s)
     h. Filter to RouteEndpoint instances where RoutePattern.RawText matches
     i. Collect HTTP methods from HttpMethodMetadata on each sibling
     j. Add "OPTIONS" to the method set
     k. Collect all DiscoveryMediaType entries from all sibling endpoint metadata
     l. Deduplicate DiscoveryMediaType by (MediaType, Rel) tuple
     m. Set response status code to 200
     n. Set Allow header: join methods with ", " (e.g., "GET, POST, OPTIONS")
     o. Return (empty body)
     ```

  6. For step (g), to enumerate endpoints:
     ```fsharp
     // If injecting EndpointDataSource directly:
     let endpoints = dataSource.Endpoints
     // If injecting IEnumerable<EndpointDataSource>:
     let endpoints = dataSources |> Seq.collect (fun ds -> ds.Endpoints)
     ```

  7. For step (h), matching by route pattern:
     ```fsharp
     let matchedRoute = (endpoint :?> RouteEndpoint)
     let pattern = matchedRoute.RoutePattern.RawText
     endpoints
     |> Seq.choose (fun ep ->
         match ep with
         | :? RouteEndpoint as re when re.RoutePattern.RawText = pattern -> Some re
         | _ -> None)
     ```

  8. For step (i), collecting HTTP methods:
     ```fsharp
     let methods =
         siblings
         |> Seq.collect (fun ep ->
             let httpMethodMeta = ep.Metadata.GetMetadata<HttpMethodMetadata>()
             if isNull httpMethodMeta then Seq.empty
             else httpMethodMeta.HttpMethods :> seq<_>)
         |> Set.ofSeq
         |> Set.add "OPTIONS"
     ```

  9. For step (k), collecting `DiscoveryMediaType` entries:
     ```fsharp
     let mediaTypes =
         siblings
         |> Seq.collect (fun ep ->
             ep.Metadata.GetOrderedMetadata<DiscoveryMediaType>())
         |> Seq.distinctBy (fun mt -> mt.MediaType, mt.Rel)
         |> Seq.toList
     ```

  10. Note: `GetOrderedMetadata<T>()` returns `IReadOnlyList<T>` for struct types in endpoint metadata. If this doesn't work with the `DiscoveryMediaType` struct (because metadata is stored as `obj`), use `ep.Metadata |> Seq.choose (fun m -> match m with :? DiscoveryMediaType as d -> Some d | _ -> None)` instead.

  11. Update `src/Frank.Discovery/Frank.Discovery.fsproj` to include:
      ```xml
      <ItemGroup>
        <Compile Include="OptionsDiscoveryMiddleware.fs" />
      </ItemGroup>
      ```

- **Files**: `src/Frank.Discovery/OptionsDiscoveryMiddleware.fs`, `src/Frank.Discovery/Frank.Discovery.fsproj`
- **Parallel?**: No -- T007 depends on this file.
- **Notes**:
  - The middleware is registered **after** routing middleware (`UseRouting()`), so `ctx.GetEndpoint()` is available.
  - The middleware should use `IMiddleware` interface OR be a conventional middleware class. Frank.LinkedData uses `app.Use(Func<HttpContext, RequestDelegate, Task>(...))` which is the simpler pattern. For OPTIONS discovery, a class-based middleware may be cleaner since it needs DI (EndpointDataSource). Use whichever approach works; the registration code in T007 must match.
  - The `Allow` header should list methods in a consistent order. Consider sorting alphabetically for determinism: `DELETE, GET, OPTIONS, POST, PUT`.
  - FR-013: OPTIONS response MUST return 200 with empty body. Do not write any response body.

### Subtask T007 -- Create `src/Frank.Discovery/WebHostBuilderExtensions.fs` with `useOptionsDiscovery`

- **Purpose**: Register the OPTIONS discovery middleware via a `WebHostBuilder` custom operation.

- **Steps**:
  1. Create `src/Frank.Discovery/WebHostBuilderExtensions.fs`.
  2. Use namespace `Frank.Discovery` with `[<AutoOpen>] module WebHostBuilderExtensions`.
  3. Add a type extension on `WebHostBuilder` with a custom operation:
     ```fsharp
     namespace Frank.Discovery

     open Microsoft.AspNetCore.Builder
     open Frank.Builder

     [<AutoOpen>]
     module WebHostBuilderExtensions =
         type WebHostBuilder with
             /// Registers the OPTIONS discovery middleware. Endpoints respond to
             /// OPTIONS with an Allow header listing registered HTTP methods and
             /// aggregated DiscoveryMediaType information.
             [<CustomOperation("useOptionsDiscovery")>]
             member _.UseOptionsDiscovery(spec: WebHostSpec) : WebHostSpec =
                 { spec with
                     Middleware = spec.Middleware >> fun app ->
                         app.UseMiddleware<OptionsDiscoveryMiddleware>() }
     ```
  4. If the middleware is not a class-based middleware (uses `app.Use(...)` pattern instead), adjust the registration accordingly. The class-based approach with `UseMiddleware<T>()` automatically resolves DI dependencies.

  5. Update `src/Frank.Discovery/Frank.Discovery.fsproj` compile order:
     ```xml
     <ItemGroup>
       <Compile Include="OptionsDiscoveryMiddleware.fs" />
       <Compile Include="WebHostBuilderExtensions.fs" />
     </ItemGroup>
     ```
     **F# compile order matters**: `WebHostBuilderExtensions.fs` must come after `OptionsDiscoveryMiddleware.fs` since it references the middleware type.

- **Files**: `src/Frank.Discovery/WebHostBuilderExtensions.fs`, `src/Frank.Discovery/Frank.Discovery.fsproj`
- **Parallel?**: No -- depends on T006.
- **Notes**: Follow the exact pattern from `src/Frank.Auth/WebHostBuilderExtensions.fs` and `src/Frank.LinkedData/WebHostBuilderExtensions.fs`. The key difference is that auth middleware uses both `Services` and `Middleware`, while discovery only needs `Middleware` (no DI service registration needed unless the middleware class requires explicit registration).

### Subtask T008 -- Create `test/Frank.Discovery.Tests/OptionsDiscoveryTests.fs`

- **Purpose**: Write acceptance tests for User Story 1 (OPTIONS discovery) covering all four acceptance scenarios.

- **Steps**:
  1. Create `test/Frank.Discovery.Tests/OptionsDiscoveryTests.fs`.
  2. Follow the test host pattern from `test/Frank.Auth.Tests/AuthorizationTests.fs`:
     - Create a `TestEndpointDataSource` (since `ResourceEndpointDataSource` is `internal`).
     - Build resources using Frank's `resource` CE.
     - Create a test host with `Host.CreateDefaultBuilder` + `UseTestServer` + routing + discovery middleware.

  3. Create a helper function to build a test server:
     ```fsharp
     let createDiscoveryTestServer (resources: Resource list) =
         let allEndpoints =
             resources
             |> List.collect (fun r -> r.Endpoints |> Array.toList)
             |> List.toArray
         Host.CreateDefaultBuilder([||])
             .ConfigureWebHost(fun webBuilder ->
                 webBuilder
                     .UseTestServer()
                     .ConfigureServices(fun services ->
                         services.AddRouting() |> ignore
                         services.AddSingleton<EndpointDataSource>(TestEndpointDataSource(allEndpoints)) |> ignore)
                     .Configure(fun app ->
                         app.UseRouting()
                             .UseMiddleware<OptionsDiscoveryMiddleware>()
                             .UseEndpoints(fun endpoints ->
                                 endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                         |> ignore)
                 |> ignore)
             .Build()
     ```

     **IMPORTANT**: The middleware needs to access `EndpointDataSource` from DI to enumerate endpoints. You must register the `TestEndpointDataSource` as a DI service AND add it to the endpoint route builder. Verify the DI resolution works during test execution. If `EndpointDataSource` isn't resolved from DI, the middleware may need to use `IEnumerable<EndpointDataSource>` instead.

  4. Implement these test cases (mapping to US1 acceptance scenarios):

     **Test 1 -- Resource with GET and POST handlers and DiscoveryMediaType metadata**:
     - Register a resource at `/items` with `get` and `post` handlers.
     - Add `DiscoveryMediaType` metadata entries (simulating LinkedData markers):
       ```fsharp
       resource "/items" {
           name "Items"
           get (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("items"))
           post (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("created"))
       }
       ```
       Then add `DiscoveryMediaType` metadata using `ResourceBuilder.AddMetadata`.
     - Send `OPTIONS /items`.
     - Assert response status is 200.
     - Assert `Allow` header contains `GET`, `POST`, and `OPTIONS`.
     - Assert response body is empty.

     **Test 2 -- Resource with GET only, no semantic markers**:
     - Register a resource at `/health` with only a `get` handler, no `DiscoveryMediaType` metadata.
     - Send `OPTIONS /health`.
     - Assert `Allow: GET, OPTIONS` (no extra media types).
     - Assert response body is empty.

     **Test 3 -- CORS preflight pass-through**:
     - Register a resource at `/items` with a `get` handler.
     - Send `OPTIONS /items` with `Access-Control-Request-Method: GET` header.
     - Assert the middleware passes through (does NOT return a 200 with Allow header -- the response depends on whether CORS middleware is registered, but the key is the discovery middleware does not intercept it).

     **Test 4 -- No middleware effect when not registered**:
     - This is an implicit test: if the middleware is not in the pipeline, OPTIONS behaves as default (typically 405). No explicit test needed; the middleware's opt-in nature is structural.

  5. Update `test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj` to include:
     ```xml
     <ItemGroup>
       <Compile Include="OptionsDiscoveryTests.fs" />
       <Compile Include="Program.fs" />
     </ItemGroup>
     ```
     **Compile order**: Test files before `Program.fs`.

  6. Run tests: `dotnet test test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`

- **Files**: `test/Frank.Discovery.Tests/OptionsDiscoveryTests.fs`, `test/Frank.Discovery.Tests/Frank.Discovery.Tests.fsproj`
- **Parallel?**: No -- depends on T006 and T007.
- **Notes**:
  - The `ResourceEndpointDataSource` is `internal` to Frank core. Tests MUST create their own `TestEndpointDataSource` (identical to the one in Frank.Auth.Tests).
  - To add `DiscoveryMediaType` metadata to test resources, use `ResourceBuilder.AddMetadata(spec, fun b -> b.Metadata.Add({ MediaType = "application/ld+json"; Rel = "describedby" }))`. You may need to create a custom resource builder extension for tests, or add metadata directly when building the resource.
  - The test project needs a `ProjectReference` to `../../src/Frank.Discovery/Frank.Discovery.fsproj` (already in the .fsproj from T003). It also needs to reference Frank core types (`DiscoveryMediaType`). Since Frank.Discovery references Frank, transitive references should make `DiscoveryMediaType` available. If not, add an explicit project reference to Frank.
  - Use `Expecto`'s `testList` and `testCaseAsync` for async test cases with `HttpClient`.
  - Use `let! (response: HttpResponseMessage) = ...` pattern for type annotations on `let!` bindings (per project memory).

---

## Risks & Mitigations

- **Risk**: `EndpointDataSource` DI resolution may differ between .NET 8/9/10. The middleware class constructor receives it via DI. If it's not registered, use `IServiceProvider.GetService<EndpointDataSource>()` at request time as a fallback.
  **Mitigation**: Register `TestEndpointDataSource` as `EndpointDataSource` in test DI. In production, ASP.NET Core registers `EndpointDataSource` via `AddRouting()`.
- **Risk**: Route pattern matching may fail if `RoutePattern.RawText` is null or empty for some endpoints.
  **Mitigation**: Add a null check before comparing route patterns. Log a warning if an endpoint has no route pattern.
- **Risk**: `HttpMethodMetadata` may not be present on some endpoints (e.g., endpoints added by other middleware).
  **Mitigation**: Handle null `HttpMethodMetadata` gracefully -- skip those endpoints when collecting methods.

## Review Guidance

- Verify the middleware's pass-through conditions are complete: non-OPTIONS method, CORS preflight, no route match, explicit OPTIONS handler.
- Verify `Allow` header includes "OPTIONS" even if no explicit OPTIONS handler is registered.
- Verify the response body is empty (FR-013).
- Verify `DiscoveryMediaType` deduplication works correctly (by `(MediaType, Rel)` tuple).
- Verify tests cover all four US1 acceptance scenarios.
- Check that `dotnet build` and `dotnet test` pass.

## Activity Log

- 2026-03-16T01:20:58Z -- system -- lane=planned -- Prompt created.
