# Research: Role Definition Schema

**Date**: 2026-03-21
**Feature**: 033-role-definition-schema

## Decisions

### 1. Typed Feature Interface Design

**Decision**: Separate `IRoleFeature` interface, non-generic.

**Rationale**: Roles are `Set<string>` — no dependency on statechart type parameters `'S`, `'C`. Keeping `IRoleFeature` separate from `IStatechartFeature` means:
- Content negotiation and ALPS middleware can read roles without a statechart dependency (Fowler)
- Clean pure data boundary — resolved roles are `Set<string>`, not closures (Seemann)
- No generic parameter pollution on the role interface (Syme)

**Alternatives considered**:
- Extend `IStatechartFeature` with `Roles` property: couples concerns, forces role-aware consumers to depend on statechart types
- Combined `IStatefulResourceFeature` inheriting both: more interface ceremony for no practical benefit

### 2. Guard Context API

**Decision**: Add `Roles: Set<string>` field + `HasRole` member method to `AccessControlContext` and `EventValidationContext`.

**Rationale**:
- `Set<string>` preserves structural equality on the record (Wadler: value semantics)
- Enumerable role set enables future projection operator to reason about guard-role relationships (Harel)
- `HasRole` member method provides ergonomic `ctx.HasRole "PlayerX"` API matching the issue proposal
- Adding to both guard contexts ensures symmetry and forward compatibility

**Alternatives considered**:
- `HasRole: string -> bool` function field: breaks structural equality, makes role set opaque and non-enumerable
- `Roles` field only (no `HasRole`): less ergonomic, forces `ctx.Roles.Contains("PlayerX")` everywhere

### 3. Spec Pipeline Placement

**Decision**: Add `Roles: RoleInfo list` to `ExtractedStatechart` in `Frank.Resources.Model`.

**Rationale**:
- Roles only apply to stateful resources; `ExtractedStatechart` is `option` on `UnifiedResource` — illegal states unrepresentable (Syme)
- Guards are already in `ExtractedStatechart` as `GuardNames`; role names belong alongside (cohesion)
- Enables role-guard consistency validation in cross-format pipeline (Miller)
- Enables per-role ALPS profile generation (Amundsen)

**Alternatives considered**:
- On `UnifiedResource` directly: allows roles without statechart (invalid state)
- On `DerivedResourceFields`: roles are declared, not derived — wrong category

### 4. RoleDefinition File Location

**Decision**: `Types.fs` in `Frank.Statecharts`, alongside `Guard` and `AccessControlContext`.

**Rationale**:
- Same behavioral type family as guards and guard contexts (Seemann: group by abstraction)
- `Types.fs` compiles before `StatefulResourceBuilder.fs` — maximum flexibility (Syme)
- Discoverable: developers looking at guard types find role types nearby (Fowler)

**Alternatives considered**:
- In `StatefulResourceBuilder.fs`: buries type in 350+ lines of CE machinery, creates ordering dependencies
- New `RoleTypes.fs`: fragments a small type family across too many files

## Integration Points Verified

| Point | Location | Verified |
|-------|----------|----------|
| `ExtractedStatechart` factory | `StatechartExtractor.toExtractedStatechart` | 2 call sites (UnifiedExtractor.fs:617, StatechartSourceExtractor.fs:331) |
| `AccessControlContext` construction | `StatefulResourceBuilder.fs` evaluateGuards closure (~line 226) | Single construction site |
| `EventValidationContext` construction | `StatefulResourceBuilder.fs` evaluateEventGuards closure (~line 247) | Single construction site |
| `StatefulResourceSpec` accumulator | `StatefulResourceBuilder.fs` Yield() (~line 138) | Follows `with` record update pattern |
| Guard name extraction pattern | `StatefulResourceBuilder.fs` Run() (~line 294) | `machine.Guards \|> List.map` — reusable for roles |
| Feature registration pattern | `StatechartFeature.fs` SetStatechartState (~line 29) | Dual registration: non-generic + generic keys |

## No Outstanding Clarifications

All design decisions resolved through expert consultation during planning interrogation. No NEEDS CLARIFICATION items remain.
