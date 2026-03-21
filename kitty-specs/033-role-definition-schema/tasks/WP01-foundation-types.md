---
work_package_id: WP01
title: Foundation Types & Compilation Fixes
lane: "doing"
dependencies: []
base_branch: master
base_commit: 0e0df98aa30b8773d78dcaa11f11eac841a09de5
created_at: '2026-03-21T19:15:20.344707+00:00'
subtasks:
- T001
- T002
- T003
- T004
- T005
- T006
- T007
- T008
phase: Phase 0 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "96979"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-21T18:59:09Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-003, FR-004, FR-007, FR-008, FR-010]
---

# Work Package Prompt: WP01 – Foundation Types & Compilation Fixes

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
spec-kitty implement WP01
```

No dependencies — this is the starting work package.

---

## Objectives & Success Criteria

- Add all new types (`RoleInfo`, `RoleDefinition`, `IRoleFeature`) and updated record fields (`ExtractedStatechart.Roles`, `AccessControlContext.Roles`, `EventValidationContext.Roles`) across 3 assemblies
- Update ALL existing construction sites with default values so `dotnet build Frank.sln` succeeds
- All existing tests pass unchanged (`dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"`)
- No runtime behavior changes — all new fields initialize to empty defaults

## Context & Constraints

- **Constitution**: Idiomatic F# (II), ASP.NET Core Native (IV), No Duplicated Logic (VIII)
- **Plan**: [plan.md](../plan.md) — see "Key Integration Points" table for exact file locations
- **Data Model**: [data-model.md](../data-model.md) — complete type definitions
- **Key constraint**: F# records are structural — adding a field breaks ALL construction sites. Every site must be updated in this WP.
- **Compile order**: Types must be defined before use. `Types.fs` compiles before `StatefulResourceBuilder.fs`. `ResourceTypes.fs` compiles before extractors.

## Subtasks & Detailed Guidance

### Subtask T001 – Add `RoleInfo` type to `ResourceTypes.fs`

- **Purpose**: Portable, zero-dependency role representation for spec pipeline consumers.
- **Steps**:
  1. Open `src/Frank.Resources.Model/ResourceTypes.fs`
  2. Add the `RoleInfo` type after `StateInfo` (around line 9), before `ProjectedProfiles`:
     ```fsharp
     type RoleInfo =
         { Name: string
           Description: string option }
     ```
  3. Verify no additional imports needed — all types are primitives
- **Files**: `src/Frank.Resources.Model/ResourceTypes.fs`
- **Parallel?**: Yes — independent of other subtasks
- **Notes**: Follow `StateInfo` pattern exactly. No methods, no dependencies. This is a pure data type.

### Subtask T002 – Add `Roles` field to `ExtractedStatechart`

- **Purpose**: Spec pipeline metadata carries role information alongside guards and state data.
- **Steps**:
  1. In `src/Frank.Resources.Model/ResourceTypes.fs`, find `ExtractedStatechart` (around line 39)
  2. Add `Roles: RoleInfo list` as the last field:
     ```fsharp
     type ExtractedStatechart =
         { RouteTemplate: string
           StateNames: string list
           InitialStateKey: string
           GuardNames: string list
           StateMetadata: Map<string, StateInfo>
           Roles: RoleInfo list }
     ```
  3. This WILL break construction sites — T003 and T004 fix them
- **Files**: `src/Frank.Resources.Model/ResourceTypes.fs`
- **Parallel?**: No — T003 and T004 depend on this
- **Notes**: Field order matters for readability but not for compilation. Place `Roles` after `StateMetadata` for logical grouping with other metadata.

### Subtask T003 – Update `StatechartExtractor.toExtractedStatechart` factory

- **Purpose**: The factory function needs a `roles` parameter to construct `ExtractedStatechart` with the new field.
- **Steps**:
  1. Find `StatechartExtractor.toExtractedStatechart` — likely in `src/Frank.Cli.Core/Statechart/StatechartExtractor.fs`
  2. Add `roles: RoleInfo list` parameter to the function signature
  3. Include `Roles = roles` in the record construction
  4. If the function uses named parameters or pipeline style, match the existing convention
- **Files**: `src/Frank.Cli.Core/Statechart/StatechartExtractor.fs` (verify exact location)
- **Parallel?**: No — depends on T002; T004 depends on this
- **Notes**: Check if this is a module function or a static method. Match the existing style.

### Subtask T004 – Update CLI extraction call sites to pass `Roles = []`

- **Purpose**: Fix compilation at the two sites that call `toExtractedStatechart`.
- **Steps**:
  1. In `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs` (around line 617), add `[]` for the roles parameter
  2. In `src/Frank.Cli.Core/Statechart/StatechartSourceExtractor.fs` (around line 331), add `[]` for the roles parameter
  3. Both sites pass empty list — source-level role extraction is a placeholder (same pattern as `guardNames`)
- **Files**:
  - `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs`
  - `src/Frank.Cli.Core/Statechart/StatechartSourceExtractor.fs`
- **Parallel?**: Yes — independent of T005-T008
- **Notes**: Verify the function call style at each site (positional vs named args). Match existing convention.

### Subtask T005 – Add `RoleDefinition` type to `Types.fs`

- **Purpose**: Platform-specific role type with claims predicate for runtime evaluation.
- **Steps**:
  1. Open `src/Frank.Statecharts/Types.fs`
  2. Ensure `open System.Security.Claims` is present (may already be there for `ClaimsPrincipal` in `AccessControlContext`)
  3. Add `RoleDefinition` after `Guard` type (or before `AccessControlContext` — logically part of the behavioral type family):
     ```fsharp
     /// Named role with identity-matching predicate.
     /// Per-resource, not global. The predicate is the source of truth for runtime evaluation.
     type RoleDefinition =
         { Name: string
           ClaimsPredicate: ClaimsPrincipal -> bool }
     ```
- **Files**: `src/Frank.Statecharts/Types.fs`
- **Parallel?**: Yes — independent of T001-T004
- **Notes**: Place near `Guard` type for conceptual grouping (same behavioral type family — Seemann/Fowler guidance). The function field `ClaimsPredicate` means `RoleDefinition` does NOT have structural equality — this is acceptable since role definitions are compared by `Name` for duplicate detection, not by structural equality.

### Subtask T006 – Update `AccessControlContext` with `Roles` and `HasRole`

- **Purpose**: Guards can check role membership via `ctx.HasRole "PlayerX"`.
- **Steps**:
  1. In `src/Frank.Statecharts/Types.fs`, find `AccessControlContext<'State, 'Context>` (around line 22)
  2. Add `Roles: Set<string>` field:
     ```fsharp
     type AccessControlContext<'State, 'Context> =
         { User: ClaimsPrincipal
           CurrentState: 'State
           Context: 'Context
           Roles: Set<string> }
         member this.HasRole(roleName: string) = this.Roles.Contains(roleName)
     ```
  3. In `src/Frank.Statecharts/StatefulResourceBuilder.fs`, find the `evaluateGuards` closure (around line 226-229) where `AccessControlContext` is constructed
  4. Add `Roles = Set.empty` to the record construction:
     ```fsharp
     let guardCtx: AccessControlContext<'S, 'C> =
         { User = ctx.User
           CurrentState = state
           Context = context
           Roles = Set.empty }
     ```
     (WP03 will replace `Set.empty` with actual resolved roles)
- **Files**:
  - `src/Frank.Statecharts/Types.fs`
  - `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Parallel?**: No — must update construction site in same step
- **Notes**: Adding `Roles: Set<string>` preserves structural equality on the record (Wadler guidance). `HasRole` is a member method — F# records support `member` declarations.

### Subtask T007 – Update `EventValidationContext` with `Roles` and `HasRole`

- **Purpose**: Symmetric with `AccessControlContext` — event validation guards also see roles.
- **Steps**:
  1. In `src/Frank.Statecharts/Types.fs`, find `EventValidationContext<'State, 'Event, 'Context>` (around line 28)
  2. Add `Roles: Set<string>` field and `HasRole` member:
     ```fsharp
     type EventValidationContext<'State, 'Event, 'Context> =
         { User: ClaimsPrincipal
           CurrentState: 'State
           Event: 'Event
           Context: 'Context
           Roles: Set<string> }
         member this.HasRole(roleName: string) = this.Roles.Contains(roleName)
     ```
  3. In `src/Frank.Statecharts/StatefulResourceBuilder.fs`, find the `evaluateEventGuards` closure (around line 247-251)
  4. Add `Roles = Set.empty` to the record construction
- **Files**:
  - `src/Frank.Statecharts/Types.fs`
  - `src/Frank.Statecharts/StatefulResourceBuilder.fs`
- **Parallel?**: No — must update construction site in same step
- **Notes**: Same pattern as T006 but for the post-handler guard context.

### Subtask T008 – Add `IRoleFeature` interface and `SetRoles` extension

- **Purpose**: Typed feature interface for caching resolved roles on `HttpContext.Features`.
- **Steps**:
  1. Open `src/Frank.Statecharts/StatechartFeature.fs`
  2. Add `IRoleFeature` interface BEFORE `IStatechartFeature` (or after — compile order just needs it before the extension module):
     ```fsharp
     /// Typed feature for resolved roles, registered on HttpContext.Features.
     /// Non-generic — roles are always Set<string>.
     type IRoleFeature =
         abstract Roles: Set<string>
     ```
  3. Add `SetRoles` extension method in the `HttpContext` extension module (alongside `SetStatechartState`):
     ```fsharp
     member ctx.SetRoles(roles: Set<string>) =
         let feature =
             { new IRoleFeature with
                 member _.Roles = roles }
         ctx.Features.Set<IRoleFeature>(feature)
     ```
  4. Verify the extension module has `[<AutoOpen>]` or is in scope where needed
- **Files**: `src/Frank.Statecharts/StatechartFeature.fs`
- **Parallel?**: Yes — independent of T001-T007
- **Notes**: Unlike `IStatechartFeature`, `IRoleFeature` does NOT need a generic variant — roles are always `Set<string>` regardless of state machine type parameters. Single registration (not dual) is sufficient.

## Risks & Mitigations

- **Record field additions break compilation**: This is expected and handled — every construction site is updated with defaults in this WP. Run `dotnet build Frank.sln` after each subtask group to verify.
- **F# compile order in fsproj**: If `IRoleFeature` is referenced before it's defined, the build fails. Verify file ordering in `Frank.Statecharts.fsproj`.
- **Import dependencies**: `RoleDefinition` needs `System.Security.Claims`. Verify the import exists in `Types.fs`.

## Review Guidance

- Verify all new types match `data-model.md` exactly
- Verify `dotnet build Frank.sln` succeeds with no warnings related to new types
- Verify `dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"` passes unchanged
- Check that `HasRole` is a member method, not an extension — this preserves type inference
- Check that `IRoleFeature` is non-generic (no type parameters)
- Check that all construction sites use empty defaults (`[]`, `Set.empty`)

## Activity Log

- 2026-03-21T18:59:09Z – system – lane=planned – Prompt created.
- 2026-03-21T19:15:20Z – claude-opus – shell_pid=96979 – lane=doing – Assigned agent via workflow command
