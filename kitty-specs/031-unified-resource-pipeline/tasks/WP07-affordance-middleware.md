---
work_package_id: WP07
title: Runtime Affordance Middleware -- Link + Allow Headers
lane: "doing"
dependencies:
- WP01
base_branch: 031-unified-resource-pipeline-WP01
base_commit: bd0722e27d0fa12e9ab2ad5cdbbcb7b8e6d5fed6
created_at: '2026-03-19T03:24:56.162137+00:00'
subtasks:
- T038
- T039
- T040
- T041
- T042
- T043
- T044
phase: Phase 2 - Runtime
assignee: ''
agent: "claude-opus-wp07"
shell_pid: "17777"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-017
- FR-018
- FR-019
- FR-020
- FR-021
---

# Work Package Prompt: WP07 -- Runtime Affordance Middleware -- Link + Allow Headers

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````python`, ````bash`

---

## Implementation Command

Depends on WP01 (unified model types) and WP06 (affordance map generation):

```bash
spec-kitty implement WP07 --base WP06
```

---

## Objectives & Success Criteria

1. Create `Frank.Affordances` project with `AffordanceMap.fs` -- deserialize MessagePack binary from embedded resource, build pre-computed `Dictionary<string, AffordanceEntry>`.
2. Create `AffordanceMiddleware.fs` with `UseAffordances()` extension method on `IApplicationBuilder`.
3. At request time: read state key from `HttpContext.Items`, look up composite key, inject `Allow` and `Link` headers.
4. Handle plain resources (no state key) using wildcard `*` state key.
5. Degrade gracefully when no affordance map is available.
6. Pre-compute Link header strings at startup for zero allocation per request beyond the header value string.
7. Pass integration tests with TestHost verifying Link/Allow headers change based on state.

**Success**: A Frank app with `useAffordances` in the `webHost` CE emits correct `Allow` and `Link` headers that reflect the current statechart state, with zero per-request allocation beyond header string assignment.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` -- User Story 5 (FR-017, FR-018, FR-019, FR-020, FR-021)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` -- Project Structure (`src/Frank.Affordances/`)
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` -- `AffordanceMapEntry`, `AffordanceLinkRelation`
- **Contract Schema**: `kitty-specs/031-unified-resource-pipeline/contracts/affordance-map-schema.json`
- **Existing statechart middleware**: `src/Frank.Statecharts/Middleware.fs` -- `StateMachineMiddleware` resolves the current state key and stores it in `HttpContext.Items`. The affordance middleware runs AFTER the statechart middleware in the pipeline and reads the state key from `HttpContext.Items["statechart.stateKey"]`.
- **Performance target**: FR-020 requires zero per-request allocation. Pre-compute all header strings at startup. The only per-request work is dictionary lookup + header assignment.
- **No dependency on Frank.Statecharts**: `Frank.Affordances` must NOT reference `Frank.Statecharts`. The coupling is via `HttpContext.Items` keys (string convention). This keeps the affordance middleware usable without statecharts.

---

## Subtasks & Detailed Guidance

### Subtask T038 -- `AffordanceMap.fs`: Binary Deserialization + Pre-Computed Dictionary

- **Purpose**: Load the affordance map from an embedded resource binary stream, deserialize it, and build a pre-computed dictionary indexed by composite key for O(1) request-time lookup.
- **Steps**:
  1. Create `src/Frank.Affordances/Frank.Affordances.fsproj`:
     ```xml
     <Project Sdk="Microsoft.NET.Sdk">
       <PropertyGroup>
         <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
       </PropertyGroup>
       <ItemGroup>
         <FrameworkReference Include="Microsoft.AspNetCore.App" />
       </ItemGroup>
       <ItemGroup>
         <PackageReference Include="MessagePack" Version="3.*" />
         <PackageReference Include="MessagePack.FSharpExtensions" Version="4.*" />
       </ItemGroup>
     </Project>
     ```
  2. Create `src/Frank.Affordances/AffordanceMap.fs`:
     ```fsharp
     namespace Frank.Affordances

     open System.Collections.Generic
     open System.IO
     open System.Reflection

     /// A single link relation in the affordance map.
     type AffordanceLinkRelation =
         { Rel: string
           Href: string
           Method: string
           Title: string option }

     /// An entry in the affordance map for a (route, state) pair.
     type AffordanceEntry =
         { AllowedMethods: string list
           LinkRelations: AffordanceLinkRelation list
           ProfileUrl: string }

     /// Pre-computed affordance lookup, built at startup from embedded binary.
     type AffordanceMapLookup =
         { Entries: Dictionary<string, AffordanceEntry>
           BaseUri: string }
     ```
  3. Implement the loader:
     ```fsharp
     module AffordanceMap =
         let private embeddedResourceName = "Frank.Affordances.unified-state.bin"

         let tryLoadFromAssembly (assembly: Assembly) : AffordanceMapLookup option =
             use stream = assembly.GetManifestResourceStream(embeddedResourceName)
             if isNull stream then None
             else
                 // Deserialize MessagePack binary → UnifiedExtractionState
                 // Project → AffordanceMapEntry list
                 // Build Dictionary<string, AffordanceEntry>
                 ...
     ```
  4. The dictionary key is the composite key `"{routeTemplate}|{stateKey}"` (same format as generated by WP06).
  5. Pre-compute Link header strings at this point:
     ```fsharp
     type PreComputedAffordance =
         { AllowHeaderValue: string           // "GET, POST"
           LinkHeaderValues: string list       // ["<url>; rel=\"self\"", "<url>; rel=\"profile\""]
           ProfileLinkHeader: string           // "<profileUrl>; rel=\"profile\""
         }
     ```
     Build a `Dictionary<string, PreComputedAffordance>` so the middleware can assign pre-built strings directly to response headers.

- **Files**: `src/Frank.Affordances/Frank.Affordances.fsproj` (NEW), `src/Frank.Affordances/AffordanceMap.fs` (NEW, ~80-120 lines)
- **Notes**:
  - The embedded resource logical name `Frank.Affordances.unified-state.bin` must match what the MSBuild target in WP09 produces.
  - The `PreComputedAffordance` type is internal -- the middleware uses it, but consumers don't see it.
  - For the Link header format, use RFC 8288 syntax: `<URL>; rel="relation-type"`. Multiple Link values can be combined with `, ` or set as separate header values.
  - Consider using `Microsoft.Extensions.Primitives.StringValues` for multi-value headers.

### Subtask T039 -- `AffordanceMiddleware.fs`: `useAffordances` WebHost CE Custom Operation

- **Purpose**: Create the ASP.NET Core middleware and `useAffordances` custom operation on the `webHost` CE (same pattern as `useOpenApi` in `Frank.OpenApi/WebHostBuilderExtensions.fs`). This registers the middleware through the CE pipeline, NOT as a standalone `IApplicationBuilder` extension.
- **Steps**:
  1. Create `src/Frank.Affordances/AffordanceMiddleware.fs`:
     ```fsharp
     namespace Frank.Affordances

     open System
     open System.Threading.Tasks
     open Microsoft.AspNetCore.Builder
     open Microsoft.AspNetCore.Http
     open Microsoft.Extensions.Logging
     open Microsoft.Extensions.Primitives

     type AffordanceMiddleware(next: RequestDelegate, lookup: PreComputedAffordance Dictionary, logger: ILogger<AffordanceMiddleware>) =

         member _.InvokeAsync(ctx: HttpContext) : Task =
             // ... lookup and inject headers ...
             next.Invoke(ctx)
     ```
  2. Extension method:
     ```fsharp
     [<AutoOpen>]
     module AffordanceMiddlewareExtensions =
         type IApplicationBuilder with
             member app.UseAffordances() =
                 let assembly = Assembly.GetEntryAssembly()
                 match AffordanceMap.tryLoadFromAssembly assembly with
                 | None ->
                     let logger = app.ApplicationServices.GetService<ILogger<AffordanceMiddleware>>()
                     if not (isNull logger) then
                         logger.LogWarning("No affordance map found in assembly. Affordance headers will not be injected.")
                     app
                 | Some lookup ->
                     let preComputed = AffordanceMap.preCompute lookup
                     app.UseMiddleware<AffordanceMiddleware>(preComputed)
     ```
  3. The middleware must run AFTER the statechart middleware (which resolves the state key) and BEFORE the endpoint handler (which writes the response). Registration order in `Program.fs`:
     ```fsharp
     app.UseStateMachines()   // Resolves state key → HttpContext.Items
     app.UseAffordances()     // Reads state key, injects headers
     // Endpoint handlers run after middleware
     ```

- **Files**: `src/Frank.Affordances/AffordanceMiddleware.fs` (NEW, ~60-100 lines)
- **Notes**:
  - The middleware class must follow ASP.NET Core middleware conventions: constructor takes `RequestDelegate` + dependencies, `InvokeAsync` takes `HttpContext`.
  - The `PreComputedAffordance` dictionary is injected via constructor (not from DI) -- it's computed at startup in `UseAffordances()` and passed directly.
  - Do NOT use `IMiddleware` (requires registration in DI). Use the convention-based middleware pattern.

### Subtask T040 -- Request-Time Header Injection for Stateful Resources

- **Purpose**: At request time, read the state key from `HttpContext.Items`, construct the composite key, look up the pre-computed affordance, and inject `Allow` and `Link` headers.
- **Steps**:
  1. In `InvokeAsync`, extract the state key:
     ```fsharp
     let stateKey =
         match ctx.Items.TryGetValue("statechart.stateKey") with
         | true, (:? string as key) -> Some key
         | _ -> None
     ```
  2. Get the route template from the endpoint metadata:
     ```fsharp
     let routeTemplate =
         let endpoint = ctx.GetEndpoint()
         if isNull endpoint then None
         else
             match endpoint with
             | :? Microsoft.AspNetCore.Routing.RouteEndpoint as re ->
                 Some re.RoutePattern.RawText
             | _ -> None
     ```
  3. Construct the composite key and look up:
     ```fsharp
     match routeTemplate, stateKey with
     | Some rt, Some sk ->
         let key = sprintf "%s|%s" rt sk
         match lookup.TryGetValue(key) with
         | true, preComputed ->
             ctx.Response.Headers["Allow"] <- StringValues(preComputed.AllowHeaderValue)
             for linkValue in preComputed.LinkHeaderValues do
                 ctx.Response.Headers.Append("Link", StringValues(linkValue))
         | false, _ -> () // No affordance entry for this state
     | _ -> () // No route template or state key -- pass through
     ```
  4. Call `next.Invoke(ctx)` regardless of whether headers were injected -- the middleware is additive, not blocking.

- **Files**: `src/Frank.Affordances/AffordanceMiddleware.fs` (part of T039's `InvokeAsync`)
- **Notes**:
  - The `HttpContext.Items["statechart.stateKey"]` key name must match exactly what `StateMachineMiddleware` sets. Verify by reading `src/Frank.Statecharts/Middleware.fs` -- look for where the state key is stored in `HttpContext.Items`. If the existing middleware doesn't store it there, document the gap and propose the key name as a convention.
  - **IMPORTANT**: Check whether the statechart middleware currently stores the state key in `HttpContext.Items`. If not, this is a cross-cutting dependency that needs coordination. The statechart middleware's `GetCurrentStateKey` call returns the state key string -- it needs to be stored in `ctx.Items["statechart.stateKey"]` for the affordance middleware to read it.
  - Headers must be set BEFORE the response body starts. Since the middleware calls `next.Invoke(ctx)` after setting headers, and the headers are set on `ctx.Response.Headers` before the handler runs, this is correct.
  - The `Link` header supports multiple values. Use `ctx.Response.Headers.Append("Link", ...)` for each link value to avoid overwriting.

### Subtask T041 -- Handle Plain Resources (No State Key)

- **Purpose**: For resources without statecharts, look up affordances using the wildcard `*` state key.
- **Steps**:
  1. When `stateKey` is `None` (no statechart middleware resolved a state), try the wildcard lookup:
     ```fsharp
     | Some rt, None ->
         let wildcardKey = sprintf "%s|*" rt
         match lookup.TryGetValue(wildcardKey) with
         | true, preComputed ->
             ctx.Response.Headers["Allow"] <- StringValues(preComputed.AllowHeaderValue)
             for linkValue in preComputed.LinkHeaderValues do
                 ctx.Response.Headers.Append("Link", StringValues(linkValue))
         | false, _ -> ()
     ```
  2. This ensures plain `resource` CEs also get affordance headers (Allow + Link) if they were included in the affordance map.
  3. The wildcard entry was generated by WP06 (T033) with state key `"*"`.

- **Files**: `src/Frank.Affordances/AffordanceMiddleware.fs` (extends T040 logic)
- **Notes**:
  - The wildcard lookup is the fallback when no state key is present. It should NOT be used when a state key IS present but doesn't match any entry -- that's a different case (stale affordance map).
  - If neither the stateful key nor the wildcard key matches, the middleware passes through silently. No error, no warning at request time (warnings are logged at startup per T042).

### Subtask T042 -- Graceful Degradation When No Affordance Map

- **Purpose**: When the embedded affordance map is missing (assembly has no embedded resource), log a warning at startup and pass all requests through unmodified.
- **Steps**:
  1. In `UseAffordances()` (T039), when `tryLoadFromAssembly` returns `None`:
     - Log a warning via `ILogger<AffordanceMiddleware>`: `"No affordance map found in assembly '{assemblyName}'. Affordance headers will not be injected. Run 'frank-cli extract' and rebuild to generate the affordance map."`
     - Return `app` without registering the middleware at all. No middleware = zero overhead.
  2. When the affordance map IS loaded but a specific request's composite key is not found:
     - Do NOT log per-request. This is normal for endpoints not in the affordance map (e.g., middleware endpoints, health checks not tracked by the CLI).
  3. At startup, if the affordance map version doesn't match the expected version:
     - Log a warning: `"Affordance map version '{mapVersion}' does not match expected version '1.0'. Affordance headers may be incorrect."`
     - Still load the map -- forward compatibility per FR-028.

- **Files**: `src/Frank.Affordances/AffordanceMiddleware.fs` (startup path in `UseAffordances()`)
- **Notes**:
  - The `ILogger` should be resolved from `app.ApplicationServices`. If the logger service is not registered (unlikely but possible in minimal APIs), use a null check.
  - FR-021 is explicit: "degrade gracefully" means no crash, no error response, just pass-through. The application works normally without affordance headers.

### Subtask T043 -- Pre-Compute Link Header Strings at Startup

- **Purpose**: Achieve zero per-request allocation beyond header string assignment by computing all possible Link header values at startup.
- **Steps**:
  1. In `AffordanceMap.preCompute`, for each entry build the pre-computed strings:
     ```fsharp
     let preCompute (lookup: AffordanceMapLookup) : Dictionary<string, PreComputedAffordance> =
         let dict = Dictionary<string, PreComputedAffordance>()
         for kvp in lookup.Entries do
             let entry = kvp.Value
             let allowHeader = String.Join(", ", entry.AllowedMethods)
             let linkHeaders =
                 [ // Profile link
                   sprintf "<%s>; rel=\"profile\"" entry.ProfileUrl
                   // Transition links
                   for lr in entry.LinkRelations do
                       sprintf "<%s>; rel=\"%s\"" lr.Href lr.Rel
                 ]
             dict.[kvp.Key] <-
                 { AllowHeaderValue = allowHeader
                   LinkHeaderValues = linkHeaders
                   ProfileLinkHeader = sprintf "<%s>; rel=\"profile\"" entry.ProfileUrl }
         dict
     ```
  2. The `AllowHeaderValue` and `LinkHeaderValues` are computed once and stored as immutable strings.
  3. At request time, the middleware assigns these strings directly to `ctx.Response.Headers` -- no string formatting, no concatenation, no allocation beyond the `StringValues` wrapper.
  4. `StringValues` is a struct, so assigning it to a header does not allocate on the heap.

- **Files**: `src/Frank.Affordances/AffordanceMap.fs` (extends T038's loader)
- **Notes**:
  - The `Link` header format follows RFC 8288: `<URI-Reference>; rel="relation-type"`.
  - `StringValues` wraps either a single string or a string array. For a single value, it's zero-alloc. For multiple values, the string array is allocated once at startup.
  - Profile link (`rel="profile"`) and describedby link (`rel="describedby"`) are always present if the entry has a `profileUrl`. Transition links vary by state.
  - Verify that `ctx.Response.Headers.Append("Link", ...)` does not allocate a new collection per call. If it does, pre-combine all Link values into a single comma-separated string.

### Subtask T044 -- Integration Tests with TestHost

- **Purpose**: Verify Link/Allow headers change based on state for tic-tac-toe-style endpoints, using `Microsoft.AspNetCore.TestHost`.
- **Steps**:
  1. Create `test/Frank.Affordances.Tests/Frank.Affordances.Tests.fsproj`:
     ```xml
     <Project Sdk="Microsoft.NET.Sdk">
       <PropertyGroup>
         <TargetFramework>net10.0</TargetFramework>
       </PropertyGroup>
       <ItemGroup>
         <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0" />
         <PackageReference Include="Expecto" Version="10.2.3" />
         <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.3" />
         <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
       </ItemGroup>
       <ItemGroup>
         <ProjectReference Include="../../src/Frank.Affordances/Frank.Affordances.fsproj" />
       </ItemGroup>
     </Project>
     ```
  2. Create test file `test/Frank.Affordances.Tests/AffordanceMiddlewareTests.fs`.
  3. Build a TestHost with the affordance middleware:
     ```fsharp
     let buildTestHost (affordances: Dictionary<string, PreComputedAffordance>) =
         WebHostBuilder()
             .Configure(fun app ->
                 // Simulate statechart middleware by setting HttpContext.Items
                 app.Use(fun ctx next ->
                     ctx.Items["statechart.stateKey"] <- "XTurn"
                     next.Invoke()
                 ) |> ignore
                 // Register affordance middleware with pre-computed lookup
                 app.UseMiddleware<AffordanceMiddleware>(affordances) |> ignore
                 app.Run(fun ctx -> ctx.Response.WriteAsync("OK"))
             )
             .Build()
     ```
  4. Test cases:
     - **State XTurn**: Set `HttpContext.Items["statechart.stateKey"] = "XTurn"`, request `/games/123`. Assert `Allow: GET, POST` and `Link` contains `rel="profile"` and a POST transition link.
     - **State Won**: Set state key to `"Won"`, request `/games/123`. Assert `Allow: GET` only and no POST link.
     - **No state key (plain resource)**: Don't set any state key, request `/health`. Assert wildcard lookup works and `Allow: GET` is set.
     - **No affordance map**: Create a TestHost without registering affordance middleware. Assert requests pass through with no extra headers.
     - **Missing entry**: Set state key to `"UnknownState"`, request `/games/123`. Assert no affordance headers are injected (pass-through).

- **Files**: `test/Frank.Affordances.Tests/Frank.Affordances.Tests.fsproj` (NEW), `test/Frank.Affordances.Tests/AffordanceMiddlewareTests.fs` (NEW, ~150-200 lines)
- **Notes**:
  - The TestHost approach avoids needing a real statechart or embedded resource. Inject the pre-computed dictionary directly.
  - For the route template lookup, the test must register endpoints with known route templates. Use `app.UseRouting()` + `app.UseEndpoints(fun ep -> ep.MapGet("/games/{gameId}", handler))` to create endpoints with route patterns.
  - Alternatively, bypass route template detection and test the middleware's internal logic directly by passing a mock composite key.
  - Use `Expecto` assertions: `Expect.equal`, `Expect.contains`, `Expect.isTrue`.

---

## Test Strategy

- **Unit tests**: Test `compositeKey` generation, `preCompute` header string formatting.
- **Integration tests**: TestHost-based tests (T044) verifying end-to-end header injection.
- **Performance tests**: Optional -- use `BenchmarkDotNet` to verify zero allocation per request. Measure `AffordanceMiddleware.InvokeAsync` with a pre-computed dictionary.

Run tests:
```bash
dotnet test test/Frank.Affordances.Tests/
```

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| `HttpContext.Items["statechart.stateKey"]` not set by existing statechart middleware | Inspect `src/Frank.Statecharts/Middleware.fs` for where state key is stored. If not stored, coordinate with spec 026 to add `ctx.Items["statechart.stateKey"] <- stateKey` after `GetCurrentStateKey`. |
| Route template not available at middleware time | Use `ctx.GetEndpoint()` to get the `RouteEndpoint` and read `RoutePattern.RawText`. This requires `UseRouting()` to have run before the middleware. |
| MessagePack deserialization of F# types | Test with `MessagePack.FSharpExtensions` resolver. Ensure `ContractlessStandardResolver` combined with `FSharpResolver` handles `option`, `list`, DU types. |
| Header injection after response started | Set headers BEFORE calling `next.Invoke(ctx)`. The middleware runs before the handler, so headers are set on the response before any body is written. |
| `Append` on Link header creating allocations | Pre-combine all Link values into a single `StringValues` from a pre-allocated string array at startup, then assign once: `ctx.Response.Headers["Link"] <- preComputed.CombinedLinkHeader` |

---

## Review Guidance

- Verify `Frank.Affordances.fsproj` has NO reference to `Frank.Statecharts`. The only coupling is the `HttpContext.Items` key convention.
- Verify `UseAffordances()` is an `IApplicationBuilder` extension, not `IEndpointRouteBuilder`.
- Verify the middleware passes through (calls `next`) in ALL code paths -- it's additive, never blocking.
- Verify pre-computed dictionary is truly immutable after startup -- no mutations at request time.
- Verify `Link` header format follows RFC 8288.
- Verify graceful degradation: no crash when affordance map is missing.
- Verify `dotnet build` and `dotnet test` pass cleanly.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

### How to Add Activity Log Entries

**When adding an entry**:
1. Scroll to the bottom of this file (Activity Log section below "Valid lanes")
2. **APPEND the new entry at the END** (do NOT prepend or insert in middle)
3. Use exact format: `- YYYY-MM-DDTHH:MM:SSZ -- agent_id -- lane=<lane> -- <action>`
4. Timestamp MUST be current time in UTC (check with `date -u "+%Y-%m-%dT%H:%M:%SZ"`)
5. Lane MUST match the frontmatter `lane:` field exactly
6. Agent ID should identify who made the change (claude-sonnet-4-5, codex, etc.)

**Format**:
```
- YYYY-MM-DDTHH:MM:SSZ -- <agent_id> -- lane=<lane> -- <brief action description>
```

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

**Initial entry**:
- 2026-03-19T02:15:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-19T03:24:56Z – claude-opus-wp07 – shell_pid=17777 – lane=doing – Assigned agent via workflow command
