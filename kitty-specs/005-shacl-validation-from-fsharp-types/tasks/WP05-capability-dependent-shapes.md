---
work_package_id: WP05
title: Capability-Dependent Shape Resolution
lane: planned
dependencies:
- WP01
subtasks: [T027, T028, T029, T030, T031]
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-013, FR-014]
---

# Work Package Prompt: WP05 -- Capability-Dependent Shape Resolution

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
spec-kitty implement WP05 --base WP03
```

Depends on WP01 (types), WP02 (shape derivation), WP03 (middleware).

---

## Objectives & Success Criteria

- Implement `ShapeResolver.fs`: runtime shape selection based on `ClaimsPrincipal` capabilities (FR-013, FR-014)
- First matching override wins; base shape (most restrictive) applied when no override matches (FR-014)
- Integrate resolver into `ValidationMiddleware` for capability-dependent validation
- Same request body produces different validation outcomes for different principals (SC-006)
- Validation failures from capability-dependent shapes return 422 (not 403) (User Story 4)

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/005-shacl-validation-from-fsharp-types/data-model.md` -- ShapeResolver, ShapeOverride, ShapeResolverConfig
- `kitty-specs/005-shacl-validation-from-fsharp-types/quickstart.md` -- `validateWithCapabilities` usage example
- `kitty-specs/005-shacl-validation-from-fsharp-types/spec.md` -- FR-013, FR-014, User Story 4, SC-006

**Key constraints**:
- Read-only dependency on `ClaimsPrincipal` from `HttpContext.User` (populated by Frank.Auth upstream)
- No coupling to `AuthRequirement` internals -- only read claims from the principal
- First-match-wins override ordering (document that most-specific overrides should be registered first)
- Base shape is always the auto-derived, most restrictive shape
- Capability-dependent validation failures are 422 Unprocessable Content (the user IS authorized, the data is invalid for their capability set)

---

## Subtasks & Detailed Guidance

### Subtask T027 -- Create `ShapeResolver.fs`

**Purpose**: Implement the shape resolution logic that selects the appropriate SHACL shape based on the authenticated principal's claims.

**Steps**:
1. Create `src/Frank.Validation/ShapeResolver.fs`
2. Implement the resolver:

```fsharp
namespace Frank.Validation

open System.Security.Claims

module ShapeResolver =
    /// Select the appropriate shape for a request based on the principal's claims.
    /// Returns the first matching override, or the base shape if no override matches.
    let resolve (config: ShapeResolverConfig) (principal: ClaimsPrincipal) : ShaclShape =
        let matchOverride (override': ShapeOverride) =
            let claimType, requiredValues = override'.RequiredClaim
            let principalValues =
                principal.Claims
                |> Seq.filter (fun c -> c.Type = claimType)
                |> Seq.map (fun c -> c.Value)
                |> Set.ofSeq
            let required = requiredValues |> Set.ofList
            // All required values must be present in the principal's claims
            Set.isSubset required principalValues

        config.Overrides
        |> List.tryFind matchOverride
        |> Option.map (fun o -> o.Shape)
        |> Option.defaultValue config.BaseShape
```

3. Add `ShapeResolver.fs` to the `.fsproj` compile list after `ShapeDerivation.fs`.

**Files**: `src/Frank.Validation/ShapeResolver.fs`
**Notes**: The resolver is a pure function -- no state, no side effects. It only reads claims from the `ClaimsPrincipal`. The `RequiredClaim` tuple is `(claimType: string, requiredValues: string list)` -- all values in the list must be present on the principal for the override to match.

### Subtask T028 -- Implement claim-based override matching

**Purpose**: The matching logic for determining if a principal satisfies an override's required claims.

**Steps**:
1. The matching is implemented in T027's `matchOverride` function
2. Additional edge cases to handle:
   - Principal with no claims -> no override matches -> base shape
   - Principal with multiple values for the same claim type (e.g., multiple roles)
   - Override requiring multiple values (e.g., `("role", ["admin"; "superuser"])`) -> principal must have ALL values
   - Empty `requiredValues` list -> always matches (catch-all override)
3. Test ordering: overrides are evaluated in list order, first match wins

```fsharp
    // Example: admin override matches principals with role=admin
    let adminOverride = {
        RequiredClaim = ("role", ["admin"])
        Shape = adminShape }

    // Example: catch-all override (empty required values) matches everyone
    let catchAll = {
        RequiredClaim = ("role", [])
        Shape = defaultShape }
```

**Files**: `src/Frank.Validation/ShapeResolver.fs`
**Notes**: First-match-wins semantics mean the override list should be ordered from most-specific to least-specific. Document this in the module's XML doc comments.

### Subtask T029 -- Implement base shape fallback

**Purpose**: When no override matches the principal's claims, return the base (most restrictive) auto-derived shape.

**Steps**:
1. The fallback is already implemented in T027 via `Option.defaultValue config.BaseShape`
2. Verify behavior for edge cases:
   - Anonymous principal (not authenticated) -> no claims -> base shape
   - Authenticated principal with claims that don't match any override -> base shape
   - Empty overrides list -> always returns base shape
3. The base shape is the auto-derived shape from `ShapeDerivation.deriveShape` -- the shape that applies ALL constraints without any relaxation.

**Files**: `src/Frank.Validation/ShapeResolver.fs`
**Notes**: The base shape is set on `ShapeResolverConfig.BaseShape` at configuration time (in the `validateWithCapabilities` CE operation, WP07). It is always the unmodified auto-derived shape.

### Subtask T030 -- Integrate ShapeResolver into ValidationMiddleware

**Purpose**: Wire the shape resolver into the validation middleware so capability-dependent shapes are selected at request time.

**Steps**:
1. In `ValidationMiddleware.fs`, modify the validation path:

```fsharp
    // In the middleware's InvokeAsync:
    let shape =
        match marker.ResolverConfig with
        | Some config ->
            let principal = ctx.User
            ShapeResolver.resolve config principal
        | None ->
            // No capability-dependent resolution; use the base derived shape
            shapeCache.GetBaseShape(marker.ShapeType)

    let shapesGraph = shapeCache.GetOrBuildShapesGraph(shape)
    // ... validate data graph against this specific shapes graph
```

2. The shape cache must support storing multiple shapes per type (base shape + override shapes)
3. Each distinct `ShaclShape` (base or override) gets its own cached `ShapesGraph`

**Files**: `src/Frank.Validation/ValidationMiddleware.fs`
**Notes**: The `ShapesGraph` for each override shape should be built and cached at startup (not per-request) for performance. The middleware's per-request cost is: resolve shape (claim matching) + lookup cached ShapesGraph + validate.

### Subtask T031 -- Create `CapabilityTests.fs`

**Purpose**: Verify capability-dependent shape selection produces different validation outcomes for the same request body with different principals.

**Steps**:
1. Create `test/Frank.Validation.Tests/CapabilityTests.fs`
2. Define test scenario (matching User Story 4):

```fsharp
type UpdateOrder = { Status: string; Notes: string option }

// Base shape: Status has sh:in ["Submitted"; "Cancelled"] (restrictive)
// Admin override: Status has no sh:in constraint (permissive)
```

3. Write tests:

**a. Admin passes with unrestricted value**:
- Principal with role=admin
- Body: `{ "Status": "Refunded" }`
- Verify: validation passes (admin shape has no sh:in on Status)

**b. Regular user fails with restricted value**:
- Principal with role=user
- Body: `{ "Status": "Refunded" }`
- Verify: validation fails with sh:in violation (422, not 403)

**c. Regular user passes with allowed value**:
- Principal with role=user
- Body: `{ "Status": "Submitted" }`
- Verify: validation passes

**d. Anonymous principal gets base shape**:
- No authentication
- Body: `{ "Status": "Refunded" }`
- Verify: validation fails (base shape is most restrictive)

**e. Override ordering: first match wins**:
- Principal with both role=admin and role=user
- Verify: first matching override (admin) is selected

**f. ShapeResolver unit tests**:
- Test `resolve` function directly with mock configs and principals
- Verify correct shape selection for various claim combinations

**Files**: `test/Frank.Validation.Tests/CapabilityTests.fs`
**Parallel?**: Yes -- depends on T027-T030 but unit tests for `resolve` can be written once T027 is done.
**Validation**: `dotnet test test/Frank.Validation.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build` to verify compilation of ShapeResolver.fs
- Run `dotnet test` for all capability tests
- Verify User Story 4 acceptance scenarios from spec.md (all 3 scenarios)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Frank.Auth integration: ClaimsPrincipal population timing | Middleware ordering ensures Auth runs first; verify with TestHost pipeline tests |
| Override shape cache memory: many overrides * many types | Lazy ShapesGraph construction: only build when first needed; typical apps have few overrides |
| First-match-wins ordering confusion | Document clearly; add startup warning if catch-all override is not last |

---

## Review Guidance

- Verify ShapeResolver is a pure function (no state, no side effects)
- Verify first-match-wins semantics in override evaluation
- Verify base shape fallback for unauthenticated/unmatched principals
- Verify 422 (not 403) for capability-dependent validation failures
- Verify middleware correctly reads `HttpContext.User` for principal
- Verify ShapesGraph caching for override shapes
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
