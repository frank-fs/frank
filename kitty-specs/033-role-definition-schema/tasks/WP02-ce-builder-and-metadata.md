---
work_package_id: WP02
title: CE Builder & Metadata Construction
lane: "doing"
dependencies: [WP01]
base_branch: 033-role-definition-schema-WP01
base_commit: 053c53f26a85877e2e8bf21d29c9ca7013d8efc4
created_at: '2026-03-21T20:19:35.147868+00:00'
subtasks:
- T009
- T010
- T011
- T012
- T013
- T014
phase: Phase 1 - Core Implementation
assignee: ''
agent: ''
shell_pid: "2394"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-21T18:59:09Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-005, FR-006]
---

# Work Package Prompt: WP02 – CE Builder & Metadata Construction

## ⚠️ IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP02 --base WP01
```

Depends on WP01 (needs `RoleDefinition`, `RoleInfo`, `IRoleFeature` types).

---

## Objectives & Success Criteria

- `role "Name" predicate` custom operation works in `statefulResource` CE
- Duplicate role names on the same resource are rejected at startup with a descriptive error
- `StateMachineMetadata` contains `Roles: RoleDefinition list` and `ResolveRoles: HttpContext -> Set<string>` closure
- Role names are extractable from metadata as `RoleInfo list`
- `dotnet build Frank.sln` succeeds; all existing tests pass

## Context & Constraints

- **Constitution**: Idiomatic F# (II — CE custom operation), No Silent Exception Swallowing (VII — log predicate failures), No Duplicated Logic (VIII)
- **Plan**: [plan.md](../plan.md) — `StatefulResourceBuilder.fs` is the primary file
- **Data Model**: [data-model.md](../data-model.md) — `StatefulResourceSpec`, `StateMachineMetadata` updates
- **Quickstart**: [quickstart.md](../quickstart.md) — target API for `role` operation
- **Key file**: `src/Frank.Statecharts/StatefulResourceBuilder.fs` (~350 lines currently)
- **Pattern**: Follow `onTransition` custom operation pattern for `role`; follow guard name extraction pattern for role name extraction

## Subtasks & Detailed Guidance

### Subtask T009 – Add `Roles` to `StatefulResourceSpec` and update `Yield()`

- **Purpose**: The CE accumulator needs a field to collect role declarations during evaluation.
- **Steps**:
  1. In `src/Frank.Statecharts/StatefulResourceBuilder.fs`, find `StatefulResourceSpec<'State, 'Event, 'Context>` type definition (around line 86-92)
  2. Add `Roles: RoleDefinition list` field:
     ```fsharp
     type StatefulResourceSpec<'State, 'Event, 'Context> =
         { RouteTemplate: string
           Machine: StateMachine<'State, 'Event, 'Context> option
           StateHandlerMap: Map<string, (string * RequestDelegate) list>
           TransitionObservers: (TransitionEvent<'State, 'Event, 'Context> -> unit) list
           ResolveInstanceId: (HttpContext -> string) option
           Metadata: (EndpointBuilder -> unit) list
           Roles: RoleDefinition list }
     ```
  3. Update `Yield()` (around line 138-144) to initialize `Roles = []`
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Parallel?**: No — T010 depends on this
- **Notes**: `RoleDefinition` is defined in `Types.fs` (WP01) — verify it's in scope.

### Subtask T010 – Add `role` custom operation to `StatefulResourceBuilder`

- **Purpose**: Developers declare roles using `role "Name" predicate` in the CE.
- **Steps**:
  1. In `StatefulResourceBuilder`, add a new `[<CustomOperation>]` method after `resolveInstanceId` (around line 172):
     ```fsharp
     [<CustomOperation("role")>]
     member _.Role(spec: StatefulResourceSpec<'S, 'E, 'C>, name: string, predicate: ClaimsPrincipal -> bool) =
         { spec with Roles = { Name = name; ClaimsPredicate = predicate } :: spec.Roles }
     ```
  2. Ensure `open System.Security.Claims` is at the top of the file (for `ClaimsPrincipal` in the method signature)
  3. The role is prepended to the list (order doesn't matter — roles are an unordered set)
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Parallel?**: No — depends on T009
- **Notes**:
  - The method name is `Role` (PascalCase) but the CE operation is `role` (lowercase) — the `[<CustomOperation("role")>]` attribute handles the mapping
  - Follow the exact pattern of `onTransition` operation: takes spec, prepends to list, returns updated spec
  - F# should resolve the overload cleanly since `(string * (ClaimsPrincipal -> bool))` is an unambiguous signature

### Subtask T011 – Add duplicate role name validation in `Run()`

- **Purpose**: Fail fast at startup if duplicate role names are declared (FR-005).
- **Steps**:
  1. In the `Run()` method (around line 174), after extracting `machine` and `resolveId`, add validation:
     ```fsharp
     // Validate role definitions
     let roleNames = spec.Roles |> List.map (fun r -> r.Name)
     let duplicates = roleNames |> List.groupBy id |> List.filter (fun (_, g) -> g.Length > 1) |> List.map fst
     if not (List.isEmpty duplicates) then
         failwithf "Duplicate role names on resource '%s': %s" routeTemplate (String.concat ", " duplicates)
     ```
  2. Place this validation BEFORE any closure construction — fail fast
  3. Also validate role names are non-empty:
     ```fsharp
     let emptyNames = spec.Roles |> List.filter (fun r -> String.IsNullOrWhiteSpace r.Name)
     if not (List.isEmpty emptyNames) then
         failwithf "Empty role name on resource '%s'" routeTemplate
     ```
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Parallel?**: No — depends on T009
- **Notes**: Use `failwithf` for descriptive errors (standard F# pattern). Include the route template in the error message for debugging context.

### Subtask T012 – Add `Roles` and `ResolveRoles` to `StateMachineMetadata`

- **Purpose**: Endpoint metadata carries role definitions for spec pipeline extraction and a resolution closure for middleware.
- **Steps**:
  1. Find `StateMachineMetadata` type definition (around line 58-84)
  2. Add two new fields:
     ```fsharp
     /// Role definitions for this resource (for spec pipeline extraction)
     Roles: RoleDefinition list
     /// Closure: evaluates role predicates against ctx.User, returns Set<string> of matching role names
     ResolveRoles: HttpContext -> Set<string>
     ```
  3. Update metadata construction (around line 306-319) with defaults:
     ```fsharp
     Roles = []
     ResolveRoles = fun _ -> Set.empty
     ```
     (T013 and T014 will replace these with real values)
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Parallel?**: Yes — modifies the type definition, independent of T009/T010
- **Notes**: `ResolveRoles` is a closure (like `EvaluateGuards`) because it captures the typed role definitions from the CE builder scope. The closure bridges the generic gap — `StateMachineMetadata` is non-generic, but role definitions come from the generic `StatefulResourceSpec<'S,'E,'C>`.

### Subtask T013 – Create `ResolveRoles` closure in `Run()`

- **Purpose**: The closure that evaluates all role predicates against the current user's claims, with per-role exception handling.
- **Steps**:
  1. In `Run()`, after validation (T011) and before metadata construction, create the closure:
     ```fsharp
     let resolveRoles (ctx: HttpContext) : Set<string> =
         if List.isEmpty spec.Roles then
             Set.empty
         else
             let logger = ctx.RequestServices.GetService(typeof<ILoggerFactory>) :?> ILoggerFactory
             let log = logger.CreateLogger("Frank.Statecharts.RoleResolution")
             spec.Roles
             |> List.choose (fun role ->
                 try
                     if role.ClaimsPredicate ctx.User then Some role.Name
                     else None
                 with ex ->
                     log.LogWarning(ex, "Role predicate '{RoleName}' threw an exception for resource '{RouteTemplate}'", role.Name, routeTemplate)
                     None)
             |> Set.ofList
     ```
  2. Update metadata construction to use this closure:
     ```fsharp
     ResolveRoles = resolveRoles
     ```
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Parallel?**: No — depends on T011 (validation) and T012 (metadata field)
- **Notes**:
  - `ILoggerFactory` resolution follows ASP.NET Core pattern — resolve from `IServiceProvider` inside the closure
  - Per-role exception handling: catch, log with `LogWarning` (not `LogError` — the request can proceed), and skip the role. This satisfies Constitution VII (No Silent Exception Swallowing)
  - Short-circuit: if `spec.Roles` is empty, return `Set.empty` immediately (no service resolution overhead)
  - `List.choose` cleanly combines the predicate evaluation and exception handling

### Subtask T014 – Extract `RoleInfo list` for metadata

- **Purpose**: Convert `RoleDefinition list` to `RoleInfo list` for spec pipeline consumers.
- **Steps**:
  1. In `Run()`, after role validation, extract role info:
     ```fsharp
     let roleInfos: RoleInfo list =
         spec.Roles
         |> List.map (fun r -> { Name = r.Name; Description = None })
     ```
  2. Update metadata construction:
     ```fsharp
     Roles = spec.Roles  // Full definitions for runtime
     ```
  3. Wire `roleInfos` into any `ExtractedStatechart`-compatible metadata if constructed here.
     If `ExtractedStatechart` is not constructed in the builder (it's constructed in CLI extractors), then `roleInfos` is available for future consumers via `StateMachineMetadata.Roles |> List.map toRoleInfo`.
- **Files**: `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Parallel?**: No — depends on T009 (spec.Roles field)
- **Notes**: `Description = None` for now — role descriptions could be added to the CE API later (e.g., `roleWithDescription "PlayerX" "The X player" predicate`). The portable `RoleInfo` type supports it but the CE operation doesn't expose it yet. This is intentional — keep the API minimal.

## Risks & Mitigations

- **CE overload resolution**: If F# struggles to resolve `role` operation overload, add explicit type annotation on the method parameter: `predicate: ClaimsPrincipal -> bool`. The issue's proposed API uses a lambda so type inference should work, but monitor compiler output.
- **ILoggerFactory resolution**: The `ResolveRoles` closure resolves `ILoggerFactory` from `IServiceProvider` per request. This is a service locator pattern (Seemann would note this). It's acceptable because: (a) `ILoggerFactory` is always registered by ASP.NET Core, (b) the alternative (injecting logger at build time) requires changing the CE builder API, (c) this matches the existing `GetCurrentStateKey` closure pattern.
- **Thread safety**: `ResolveRoles` is called on the request thread. `ClaimsPrincipal` and role predicates should be thread-safe (standard ASP.NET Core assumption). `Set.ofList` creates an immutable set — safe to cache on `HttpContext.Features`.

## Review Guidance

- Verify `role` CE operation matches the quickstart API: `role "PlayerX" (fun claims -> claims.HasClaim("player", "X"))`
- Verify duplicate name detection catches both duplicates and empty names
- Verify `ResolveRoles` closure handles exceptions per-role (not all-or-nothing)
- Verify `ResolveRoles` short-circuits for empty roles (no `ILoggerFactory` resolution)
- Verify `StateMachineMetadata.Roles` carries `RoleDefinition list` (with predicates, for runtime)
- Check that `dotnet build Frank.sln` succeeds and all existing tests pass

## Activity Log

- 2026-03-21T18:59:09Z – system – lane=planned – Prompt created.
