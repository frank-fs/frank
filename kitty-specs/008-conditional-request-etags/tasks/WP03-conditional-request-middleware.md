---
work_package_id: WP03
title: Conditional Request Middleware
lane: done
dependencies:
- WP01
- WP02
subtasks: [T011, T012, T013, T014, T015, T016, T017]
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-003, FR-004, FR-005, FR-006, FR-011, FR-012, FR-013, FR-015, FR-016]
---

# Work Package Prompt: WP03 -- Conditional Request Middleware

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

Depends on WP01 (ETagMetadata, ETagComparison, ETagFormat) and WP02 (ETagCache).

---

## Objectives & Success Criteria

- Implement `ConditionalRequestMiddleware` that intercepts requests to ETag-enabled resources
- Evaluate `If-None-Match` on GET/HEAD requests: return 304 Not Modified when ETag matches
- Evaluate `If-Match` on POST/PUT/DELETE requests: return 412 Precondition Failed when ETag does not match
- Support wildcard `*` in both `If-None-Match` and `If-Match` per RFC 9110
- Set `ETag` response header on successful responses for ETag-enabled resources
- Invalidate cache entries after successful mutations
- Pass through non-ETag-enabled resources with zero overhead (single metadata check)
- Provide `useConditionalRequests` plug for WebHostBuilder and `AddETagCache` DI extension
- Integration tests via TestHost validate 304, 412, pass-through, and wildcard scenarios

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/008-conditional-request-etags/spec.md` -- FR-001 through FR-015
- `kitty-specs/008-conditional-request-etags/data-model.md` -- ConditionalRequestMiddleware entity definition and request processing flow
- `kitty-specs/008-conditional-request-etags/research.md` -- Decision 3: Middleware Pipeline Position
- `kitty-specs/008-conditional-request-etags/quickstart.md` -- Usage examples

**Key constraints**:
- New file: `src/Frank/ConditionalRequestMiddleware.fs` -- added after `ETagCache.fs` in Frank.fsproj
- Middleware runs after `UseRouting()` (needs endpoint metadata) but before handler execution
- Registered via `plug` custom operation on WebHostBuilder (same pattern as Frank.Auth)
- Non-ETag resources: single `ETagMetadata` check per request, then pass-through -- zero overhead
- 304 responses MUST include the `ETag` header (clients need it for subsequent requests)
- 304 responses MUST NOT include a body
- 412 responses SHOULD NOT include a body (just the status code)
- Requests without conditional headers proceed normally regardless of ETag enablement
- After successful mutation (2xx status), invalidate cache entry to force recomputation

---

## Subtasks & Detailed Guidance

### Subtask T011 -- Create `ConditionalRequestMiddleware.fs` with middleware skeleton

**Purpose**: Scaffold the middleware with endpoint metadata discovery and pass-through for non-ETag resources.

**Steps**:
1. Create `src/Frank/ConditionalRequestMiddleware.fs`
2. Use namespace `Frank`
3. Define the middleware class:

```fsharp
namespace Frank

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging

type ConditionalRequestMiddleware(next: RequestDelegate,
                                   cache: ETagCache,
                                   providerFactory: IETagProviderFactory,
                                   logger: ILogger<ConditionalRequestMiddleware>) =

    member _.Invoke(ctx: HttpContext) : Task = task {
        // 1. Get the matched endpoint
        let endpoint = ctx.GetEndpoint()
        if isNull endpoint then
            do! next.Invoke(ctx)
            return ()

        // 2. Check for ETagMetadata -- if absent, pass through
        let etagMetadata = endpoint.Metadata.GetMetadata<ETagMetadata>()
        if isNull etagMetadata then
            do! next.Invoke(ctx)
            return ()

        // 3. Resolve provider
        match providerFactory.CreateProvider(endpoint) with
        | None ->
            do! next.Invoke(ctx)
            return ()
        | Some provider ->
            // ... conditional request processing (T012-T015)
            do! next.Invoke(ctx)
    }
```

4. Add `ConditionalRequestMiddleware.fs` to `Frank.fsproj` after `ETagCache.fs` and before `Builder.fs`

**Files**: `src/Frank/ConditionalRequestMiddleware.fs`, `src/Frank/Frank.fsproj` (modified)
**Notes**:
- The middleware uses constructor injection for `ETagCache`, `IETagProviderFactory`, and `ILogger`
- The skeleton handles three pass-through cases: no endpoint, no ETagMetadata, no provider
- The actual conditional logic (T012-T015) will be added inside the `Some provider` branch

**Implementation note**: If `IETagProvider.ComputeETag` throws, log via `ILogger.LogError` before propagating the exception (Constitution Principle VII -- no silent exception swallowing).

### Subtask T012 -- Implement If-None-Match evaluation (304 Not Modified)

**Purpose**: For GET/HEAD requests with `If-None-Match`, compare against the current ETag and return 304 if it matches.

**Steps**:
1. Inside the `Some provider` branch, after resolving the instance ID and current ETag:

```fsharp
        | Some provider ->
            let instanceId = etagMetadata.ResolveInstanceId ctx
            let resourceKey = ctx.Request.Path.Value

            // Get current ETag (from cache or computed fresh)
            let! cachedETag = cache.GetETag(resourceKey) |> Async.StartAsTask
            let! currentETag =
                match cachedETag with
                | Some etag -> Task.FromResult(Some etag)
                | None -> task {
                    let! computed = provider.ComputeETag(instanceId)
                    match computed with
                    | Some etag ->
                        cache.SetETag(resourceKey, etag)
                        return Some etag
                    | None -> return None
                }

            let method = ctx.Request.Method

            // If-None-Match evaluation (GET/HEAD)
            if HttpMethods.IsGet(method) || HttpMethods.IsHead(method) then
                let ifNoneMatch = ctx.Request.Headers.IfNoneMatch.ToString()
                if not (System.String.IsNullOrWhiteSpace(ifNoneMatch)) then
                    let headerETags = ETagComparison.parseIfNoneMatch ifNoneMatch
                    if ETagComparison.anyMatch currentETag headerETags then
                        ctx.Response.StatusCode <- StatusCodes.Status304NotModified
                        // Set ETag header even on 304
                        match currentETag with
                        | Some etag -> ctx.Response.Headers.ETag <- etag
                        | None -> ()
                        return ()  // Short-circuit -- do not call next
```

**Files**: `src/Frank/ConditionalRequestMiddleware.fs`
**Notes**:
- The resource key uses the resolved request path (e.g., `/games/42`), not the route pattern
- Cache miss triggers `IETagProvider.ComputeETag` and stores the result
- 304 response: set status code, set ETag header, return (no body, no `next` invocation)
- `HttpMethods.IsGet`/`IsHead` avoids string comparison allocation
- `ctx.Request.Headers.IfNoneMatch` is a `StringValues` -- `.ToString()` handles single and multi-value

### Subtask T013 -- Implement If-Match evaluation (412 Precondition Failed)

**Purpose**: For mutation requests (POST/PUT/DELETE) with `If-Match`, reject if the ETag does not match.

**Steps**:
1. After the If-None-Match block, add If-Match evaluation:

```fsharp
            // If-Match evaluation (POST/PUT/DELETE)
            if HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method) then
                let ifMatch = ctx.Request.Headers.IfMatch.ToString()
                if not (System.String.IsNullOrWhiteSpace(ifMatch)) then
                    let headerETags = ETagComparison.parseIfMatch ifMatch
                    if not (ETagComparison.anyMatch currentETag headerETags) then
                        ctx.Response.StatusCode <- StatusCodes.Status412PreconditionFailed
                        return ()  // Short-circuit -- reject stale mutation
```

**Files**: `src/Frank/ConditionalRequestMiddleware.fs`
**Notes**:
- `If-Match` is the inverse of `If-None-Match`: the request proceeds if ETags MATCH, rejected if they DON'T
- 412 response: set status code, return (no body, no `next` invocation)
- Requests without `If-Match` header proceed normally (conditional headers are optional per FR-006)
- `If-Match: *` matches any existing resource (currentETag is Some) -- handled by `anyMatch`

### Subtask T014 -- Implement wildcard `*` handling

**Purpose**: Ensure wildcard `*` in both `If-None-Match` and `If-Match` follows RFC 9110 semantics.

**Steps**:
1. Verify `ETagComparison.anyMatch` already handles wildcards correctly:
   - `If-None-Match: *` with `currentETag = Some _` -> returns `true` (triggers 304)
   - `If-None-Match: *` with `currentETag = None` -> returns `false` (no 304 for non-existent resource)
   - `If-Match: *` with `currentETag = Some _` -> returns `true` (request proceeds)
   - `If-Match: *` with `currentETag = None` -> returns `false` (triggers 412)

2. This subtask is primarily a verification task -- the wildcard logic lives in `ETagComparison.anyMatch` (WP01 T004). Ensure the middleware passes the correct values to `anyMatch` for all wildcard cases.

3. Add specific test cases for wildcard behavior in `ConditionalRequestTests.fs` (T017).

**Files**: `src/Frank/ConditionalRequestMiddleware.fs` (verification only)
**Notes**:
- RFC 9110 Section 13.1.2: `If-None-Match: *` condition is true if the origin server has a current representation for the target resource
- RFC 9110 Section 13.1.1: `If-Match: *` condition is true if the origin server has a current representation for the target resource
- "Has a current representation" maps to `currentETag = Some _` (provider returned an ETag)

### Subtask T015 -- Implement ETag header setting and cache invalidation

**Purpose**: Set the `ETag` response header on successful responses and invalidate cache after mutations.

**Steps**:
1. After the conditional checks pass (no short-circuit), invoke the handler and handle the response:

```fsharp
            // No short-circuit -- proceed with the handler
            do! next.Invoke(ctx)

            // After handler execution:
            let statusCode = ctx.Response.StatusCode

            // Set ETag header on successful GET/HEAD responses
            if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
               && statusCode >= 200 && statusCode < 300 then
                match currentETag with
                | Some etag -> ctx.Response.Headers.ETag <- etag
                | None ->
                    // Compute fresh ETag after handler (state may have been set)
                    let! freshETag = provider.ComputeETag(instanceId)
                    match freshETag with
                    | Some etag ->
                        cache.SetETag(resourceKey, etag)
                        ctx.Response.Headers.ETag <- etag
                    | None -> ()

            // Invalidate cache after successful mutations
            if (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsDelete(method))
               && statusCode >= 200 && statusCode < 300 then
                cache.Invalidate(resourceKey)
                // Compute and set new ETag on mutation response
                let! newETag = provider.ComputeETag(instanceId)
                match newETag with
                | Some etag ->
                    cache.SetETag(resourceKey, etag)
                    ctx.Response.Headers.ETag <- etag
                | None -> ()
```

**Files**: `src/Frank/ConditionalRequestMiddleware.fs`
**Notes**:
- ETag header on GET: use the cached value (already computed before conditional checks)
- ETag header on mutation response: compute fresh (state has changed), cache the new value
- Cache invalidation before recomputation ensures stale values are not served between invalidate and set
- Only set ETag on 2xx responses -- error responses (4xx, 5xx) should not include ETags
- Note: setting headers after `next.Invoke` requires that the response has not started streaming. For Frank resources, response bodies are typically written in the handler, so headers can still be set. If this is a problem, consider buffering or moving the header set before `next.Invoke` for GET requests.

### Subtask T016 -- Add useConditionalRequests plug and AddETagCache DI extension

**Purpose**: Provide developer-facing registration API for the conditional request middleware.

**Steps**:
1. In `ConditionalRequestMiddleware.fs` (or a separate extensions section), add:

```fsharp
open Microsoft.Extensions.DependencyInjection
open Microsoft.AspNetCore.Builder

/// DI registration for the ETag cache.
type IServiceCollection with
    /// Register the ETag cache as a singleton service.
    member services.AddETagCache(?maxEntries: int) : IServiceCollection =
        let max = defaultArg maxEntries 10_000
        services.AddSingleton<ETagCache>(fun sp ->
            let logger = sp.GetRequiredService<ILogger<ETagCache>>()
            new ETagCache(max, logger))

/// Middleware registration for conditional request handling.
[<AutoOpen>]
module ConditionalRequestMiddlewareExtensions =
    /// Register the conditional request middleware in the ASP.NET Core pipeline.
    /// Must be called after UseRouting() -- use via `plug useConditionalRequests`.
    let useConditionalRequests (app: IApplicationBuilder) =
        app.UseMiddleware<ConditionalRequestMiddleware>()
```

**Files**: `src/Frank/ConditionalRequestMiddleware.fs`
**Notes**:
- `AddETagCache` has an optional `maxEntries` parameter (default 10,000)
- `useConditionalRequests` is a plain function suitable for the `plug` custom operation: `plug useConditionalRequests`
- The `[<AutoOpen>]` module makes `useConditionalRequests` available when `Frank` namespace is opened
- `ConditionalRequestMiddleware` is resolved via `UseMiddleware<T>()` which uses DI constructor injection
- The developer must also register an `IETagProviderFactory` -- typically via `AddStatechartETagProvider` (WP04) or a custom registration

### Subtask T017 -- Create ConditionalRequestTests.fs with TestHost integration tests

**Purpose**: Validate the middleware end-to-end with realistic HTTP requests via TestHost.

**Steps**:
1. Create `test/Frank.Tests/ConditionalRequestTests.fs`
2. Add to `Frank.Tests.fsproj` before `Program.fs`
3. Write Expecto tests covering:

**a. Pass-through (no ETagMetadata)**:
- Request to a resource without ETagMetadata: response is normal (200, no ETag header)

**b. ETag generation on GET**:
- GET to an ETag-enabled resource: response includes `ETag` header
- ETag is a quoted string in the correct format

**c. 304 Not Modified**:
- GET with `If-None-Match` matching current ETag: returns 304, no body
- GET with `If-None-Match` not matching: returns 200 with full body and ETag
- GET with `If-None-Match: *` on existing resource: returns 304

**d. 412 Precondition Failed**:
- POST with `If-Match` matching current ETag: request proceeds (200)
- POST with `If-Match` not matching: returns 412
- POST with `If-Match: *` on existing resource: request proceeds

**e. No conditional headers**:
- GET without `If-None-Match`: returns 200 with ETag (no conditional behavior)
- POST without `If-Match`: request proceeds normally

**f. Wildcard edge cases**:
- `If-None-Match: *` on non-existent resource (provider returns None): returns 200
- `If-Match: *` on non-existent resource: returns 412

**Test infrastructure**:
- Create a mock `IETagProvider` that returns configurable ETags
- Create a mock `IETagProviderFactory` that returns the mock provider for test endpoints
- Use ASP.NET Core TestHost with minimal pipeline: routing + ConditionalRequestMiddleware + test endpoint

```fsharp
// Example mock provider
type MockETagProvider(etagByInstanceId: Map<string, string>) =
    interface IETagProvider with
        member _.ComputeETag(instanceId) = task {
            return Map.tryFind instanceId etagByInstanceId
        }
```

**Files**: `test/Frank.Tests/ConditionalRequestTests.fs`, `test/Frank.Tests/Frank.Tests.fsproj` (modified)
**Validation**: `dotnet test test/Frank.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build src/Frank/` to verify compilation on all 3 targets
- Run `dotnet test test/Frank.Tests/` to verify all middleware tests pass
- Run `dotnet build Frank.sln` to verify solution-level build succeeds

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Response headers already sent before ETag can be set | For GET, set ETag before calling next if we have a cached value; recompute only on first request |
| Middleware ordering with Auth/Statecharts | Document recommended order: ETag middleware before Auth and Statecharts in `plug` chain |
| Race between ETag check and handler execution | Inherent to optimistic concurrency; MailboxProcessor serializes cache; 412 on next attempt |
| Multiple `If-None-Match` values with whitespace | `ETagComparison.parseIfNoneMatch` trims whitespace around commas |
| `ctx.Request.Headers.IfNoneMatch` returns empty StringValues | `.ToString()` returns empty string; `IsNullOrWhiteSpace` check handles this |

---

## Review Guidance

- Verify `ConditionalRequestMiddleware.fs` is in `Frank.fsproj` after `ETagCache.fs`
- Verify middleware checks for `ETagMetadata` on endpoint -- no processing for non-ETag resources
- Verify 304 response includes `ETag` header but no body
- Verify 412 response has no body
- Verify cache invalidation happens AFTER successful mutation (not before handler runs)
- Verify `useConditionalRequests` is compatible with `plug` custom operation pattern
- Verify `AddETagCache` registers as singleton with configurable maxEntries
- Verify tests cover all acceptance scenarios from spec.md
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
