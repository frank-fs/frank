---
work_package_id: WP05
title: WebHost & ResourceBuilder Extensions
lane: "doing"
dependencies: [WP03, WP04]
base_branch: 004-frank-statecharts-WP05-merge-base
base_commit: 19e9f5a2e73472bbea8b31e3c58894284d7ddb94
created_at: '2026-03-07T17:06:06.105207+00:00'
subtasks:
- T022
- T023
- T024
- T025
phase: Phase 3 - Integration
assignee: ''
agent: ''
shell_pid: "58899"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-06T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-011]
---

# Work Package Prompt: WP05 -- WebHost & ResourceBuilder Extensions

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP05 --base WP04
```

Depends on WP03 (CE) and WP04 (middleware).

---

## Objectives & Success Criteria

- Create `[<AutoOpen>]` extension modules for `WebHostBuilder` and `ResourceBuilder`
- `useStatecharts` registers middleware and DI services in `WebHostBuilder` CE
- Optional `stateMachine` operation on `ResourceBuilder` for simple metadata-only annotation
- Verify compilation order in `.fsproj`
- Extensions compose correctly with existing Frank extensions (Auth, LinkedData)

---

## Context & Constraints

**Reference code** (follow these patterns exactly):
- `src/Frank.Auth/WebHostBuilderExtensions.fs` -- `useAuthentication`, `useAuthorization` custom operations on `WebHostBuilder`
- `src/Frank.Auth/ResourceBuilderExtensions.fs` -- `requireAuth`, `requireClaim` custom operations on `ResourceBuilder`
- `src/Frank/Builder.fs:223-316` -- `WebHostSpec` structure with `Services`, `Middleware`, `BeforeRoutingMiddleware`

**Key patterns from Frank.Auth**:
```fsharp
// WebHostBuilderExtensions.fs
[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with
        [<CustomOperation("useXxx")>]
        member _.UseXxx(spec: WebHostSpec, ...) : WebHostSpec =
            { spec with
                Services = spec.Services >> fun services -> ...
                Middleware = spec.Middleware >> fun app -> ... }
```

```fsharp
// ResourceBuilderExtensions.fs
[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        [<CustomOperation("xxxOperation")>]
        member _.XxxOperation(spec: ResourceSpec, ...) : ResourceSpec =
            EndpointXxx.applyXxx config spec
```

---

## Subtasks & Detailed Guidance

### Subtask T022 -- Create `ResourceBuilderExtensions.fs`

**Purpose**: Add `stateMachine` custom operation to standard `ResourceBuilder` for cases where the full `statefulResource` CE is not needed (simple metadata-only annotation).

**Steps**:
1. Create `src/Frank.Statecharts/ResourceBuilderExtensions.fs`
2. Add to `.fsproj` `<Compile>` list after `Middleware.fs`

```fsharp
namespace Frank.Statecharts

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        /// Attach a state machine definition to a standard resource.
        /// Use this for simple metadata annotation when the full
        /// statefulResource CE is not needed.
        [<CustomOperation("stateMachine")>]
        member _.StateMachine(spec: ResourceSpec, metadata: StateMachineMetadata) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, fun builder ->
                builder.Metadata.Add(metadata))
```

**Files**: `src/Frank.Statecharts/ResourceBuilderExtensions.fs`
**Parallel?**: Yes -- can proceed in parallel with T023.
**Notes**:
- This is a lighter-weight alternative to `statefulResource` CE
- Users pass a pre-built `StateMachineMetadata` record
- Follows `Frank.Auth`'s `ResourceBuilderExtensions` pattern exactly

### Subtask T023 -- Create `WebHostBuilderExtensions.fs`

**Purpose**: Add `useStatecharts` custom operation to `WebHostBuilder` that registers middleware and DI services.

**Steps**:
1. Create `src/Frank.Statecharts/WebHostBuilderExtensions.fs`
2. Add to `.fsproj` `<Compile>` list after `ResourceBuilderExtensions.fs`

```fsharp
namespace Frank.Statecharts

open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with
        /// Register Frank.Statecharts middleware and default services.
        /// Middleware runs after routing, before endpoint execution.
        /// Recommended order: useAuthentication -> useStatecharts -> (LinkedData)
        [<CustomOperation("useStatecharts")>]
        member _.UseStatecharts(spec: WebHostSpec) : WebHostSpec =
            { spec with
                Middleware = spec.Middleware >> fun app ->
                    app.UseMiddleware<StateMachineMiddleware>() }

        /// Register Frank.Statecharts with a custom store configuration.
        member _.UseStatecharts(spec: WebHostSpec, configureStore: IServiceCollection -> IServiceCollection) : WebHostSpec =
            { spec with
                Services = spec.Services >> configureStore
                Middleware = spec.Middleware >> fun app ->
                    app.UseMiddleware<StateMachineMiddleware>() }
```

**Files**: `src/Frank.Statecharts/WebHostBuilderExtensions.fs`
**Parallel?**: Yes -- can proceed in parallel with T022.
**Notes**:
- Middleware registered via `Middleware` (after routing), NOT `BeforeRoutingMiddleware`
- This is critical: the middleware needs `GetEndpoint()` to work, which requires routing to have already run
- The overload with `configureStore` allows custom store registration (e.g., Redis-backed store)
- Default store registration is handled by `IServiceCollection.AddStateMachineStore()` (from WP02) -- users call this in their `service` block or via the `configureStore` callback
- Document recommended middleware order: Auth -> Statecharts -> LinkedData

### Subtask T024 -- Implement DI registration in `useStatecharts`

**Purpose**: Ensure `useStatecharts` sets up everything needed for the middleware to function.

**Steps**:
1. The `useStatecharts` overload without configuration should register the middleware only (stores are registered per-type by the user)
2. The overload with `configureStore` registers both services and middleware
3. Verify that `StateMachineMiddleware` can be resolved by ASP.NET Core's DI container (it takes `RequestDelegate next` in constructor, which ASP.NET Core provides automatically)

**Usage example**:
```fsharp
let app = webHost [||] {
    useDefaults
    useAuthentication (fun auth -> auth.AddCookie())
    useStatecharts
    service (fun s -> s.AddStateMachineStore<GameState, GameContext>())
    resource gameResource
}
```

Or with inline store configuration:
```fsharp
let app = webHost [||] {
    useDefaults
    useStatecharts (fun services ->
        services.AddStateMachineStore<GameState, GameContext>())
    resource gameResource
}
```

**Files**: `src/Frank.Statecharts/WebHostBuilderExtensions.fs`
**Notes**: Keep it simple -- don't over-engineer the DI registration. The store is a singleton per type, registered via the extension method from WP02.

### Subtask T025 -- Verify compilation order in `.fsproj`

**Purpose**: Ensure the `.fsproj` file has the correct F# compilation order.

**Steps**:
1. The final `.fsproj` `<Compile>` order must be:

```xml
<ItemGroup>
    <Compile Include="Types.fs" />
    <Compile Include="Store.fs" />
    <Compile Include="StatefulResourceBuilder.fs" />
    <Compile Include="Middleware.fs" />
    <Compile Include="ResourceBuilderExtensions.fs" />
    <Compile Include="WebHostBuilderExtensions.fs" />
</ItemGroup>
```

2. Verify by running `dotnet build src/Frank.Statecharts/` -- any compilation order issues will surface as errors
3. Run `dotnet build Frank.sln` to verify solution-level build

**Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
**Notes**:
- Types.fs MUST be first (all other files depend on it)
- Store.fs before StatefulResourceBuilder.fs (CE references store types)
- StatefulResourceBuilder.fs before Middleware.fs (middleware references metadata types)
- Extensions last (they reference all other types)

---

## Test Strategy

- `dotnet build src/Frank.Statecharts/` compiles successfully
- `dotnet build Frank.sln` compiles successfully
- Write a simple integration test that uses `webHost` CE with `useStatecharts`:

```fsharp
testTask "useStatecharts registers middleware" {
    // Build a TestServer with useStatecharts
    // Verify stateful resource works end-to-end
}
```

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Middleware ordering conflicts with LinkedData/Auth | Document recommended order; test all combinations |
| `UseMiddleware<T>` requires parameterless constructor or DI-resolvable | `StateMachineMiddleware` takes `RequestDelegate next` which ASP.NET Core injects |
| Missing store registration at runtime | Middleware should give clear error if store not registered |

---

## Review Guidance

- Verify `[<AutoOpen>]` on both extension modules
- Verify `useStatecharts` uses `Middleware` (not `BeforeRoutingMiddleware`)
- Verify `.fsproj` compilation order matches the required order above
- Verify extension patterns match `Frank.Auth` extensions exactly
- Verify `dotnet build Frank.sln` succeeds
- Check that both `useStatecharts` overloads are available

---

## Activity Log

- 2026-03-06T00:00:00Z -- system -- lane=planned -- Prompt created.
