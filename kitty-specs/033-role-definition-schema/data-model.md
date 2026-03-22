# Data Model: Role Definition Schema

**Date**: 2026-03-21
**Feature**: 033-role-definition-schema

## Portable Types (Frank.Resources.Model)

### RoleInfo

Zero-dependency, hierarchy-neutral representation of a role for spec pipeline consumers.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Name | string | yes | Unique role identifier within a resource |
| Description | string option | no | Human-readable description for generated specs |

**Lives in**: `src/Frank.Resources.Model/ResourceTypes.fs` alongside `StateInfo`

**Relationships**: Referenced by `ExtractedStatechart.Roles`

### ExtractedStatechart (updated)

Existing type gains a `Roles` field.

| Field | Type | Status |
|-------|------|--------|
| RouteTemplate | string | existing |
| StateNames | string list | existing |
| InitialStateKey | string | existing |
| GuardNames | string list | existing |
| StateMetadata | Map\<string, StateInfo\> | existing |
| **Roles** | **RoleInfo list** | **new** |

## Platform Types (Frank.Statecharts)

### RoleDefinition

Per-resource role with identity-matching predicate. Source of truth for runtime evaluation.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| Name | string | yes | Role identifier (must be unique within resource) |
| ClaimsPredicate | ClaimsPrincipal -> bool | yes | Evaluates whether a user holds this role |

**Lives in**: `src/Frank.Statecharts/Types.fs` alongside `Guard`, `AccessControlContext`

**Constraints**: Duplicate `Name` values on the same resource rejected at startup (FR-005)

### IRoleFeature (new interface)

Non-generic typed feature for resolved roles, registered on `HttpContext.Features`.

| Member | Type | Description |
|--------|------|-------------|
| Roles | Set\<string\> | Unordered set of role names the current user holds |

**Lives in**: `src/Frank.Statecharts/RoleFeature.fs`

**Registration**: Single registration via `ctx.Features.Set<IRoleFeature>(feature)`. No generic variant needed — roles are always `Set<string>`.

### AccessControlContext (updated)

Existing guard context type gains role fields.

| Field | Type | Status |
|-------|------|--------|
| User | ClaimsPrincipal | existing |
| CurrentState | 'State | existing |
| Context | 'Context | existing |
| **Roles** | **Set\<string\>** | **new** |

**New member**: `HasRole(roleName: string) : bool` — returns `this.Roles.Contains(roleName)`

### EventValidationContext (updated)

Symmetric with AccessControlContext.

| Field | Type | Status |
|-------|------|--------|
| User | ClaimsPrincipal | existing |
| CurrentState | 'State | existing |
| Event | 'Event | existing |
| Context | 'Context | existing |
| **Roles** | **Set\<string\>** | **new** |

**New member**: `HasRole(roleName: string) : bool` — returns `this.Roles.Contains(roleName)`

### StatefulResourceSpec (updated accumulator)

CE internal state gains a `Roles` field.

| Field | Type | Status |
|-------|------|--------|
| RouteTemplate | string | existing |
| Machine | StateMachine option | existing |
| StateHandlerMap | Map | existing |
| TransitionObservers | list | existing |
| ResolveInstanceId | option | existing |
| Metadata | list | existing |
| **Roles** | **RoleDefinition list** | **new** |

**Yield() default**: `Roles = []`

### StateMachineMetadata (updated endpoint metadata)

Existing metadata type gains role fields.

| Field | Type | Status |
|-------|------|--------|
| Machine | obj | existing |
| StateHandlerMap | Map | existing |
| ResolveInstanceId | function | existing |
| TransitionObservers | list | existing |
| InitialStateKey | string | existing |
| GuardNames | string list | existing |
| StateMetadataMap | Map | existing |
| GetCurrentStateKey | function | existing |
| EvaluateGuards | function | existing |
| EvaluateEventGuards | function | existing |
| ExecuteTransition | function | existing |
| **Roles** | **RoleDefinition list** | **new** |
| **ResolveRoles** | **HttpContext -> Set\<string\>** | **new** |

`ResolveRoles` is a closure that evaluates all role predicates against `ctx.User` and returns the set of matching role names. Called once per request by middleware, result cached on `IRoleFeature`.

## State Transitions

No state machine changes. Role definitions are static declarations — they do not transition or change during the application lifetime.

## Validation Rules

- `RoleDefinition.Name` must be non-empty
- `RoleDefinition.Name` must be unique within a single `statefulResource` (checked at startup in `Run()`)
- Duplicate names produce a startup failure (not a runtime error)
- Role predicate exceptions during evaluation are caught, logged, and the role is not resolved for that request
