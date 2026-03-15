---
work_package_id: WP07
title: ResourceBuilder & WebHost Extensions
lane: "doing"
dependencies:
- WP11
- WP12
- WP06
base_branch: 005-shacl-validation-from-fsharp-types-WP07-merge-base
base_commit: 00905c3c3ce2493efc8ec85dff2edefea3d47770
created_at: '2026-03-15T19:14:01.335107+00:00'
subtasks: [T037, T038, T039, T040, T041, T042]
shell_pid: "27778"
agent: "claude-opus"
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
- timestamp: '2026-03-14T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Amended per build-time SHACL unification design
amendment_ref: docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md
requirement_refs: [FR-017, FR-018, FR-019]
---

# Work Package Prompt: WP07 -- ResourceBuilder & WebHost Extensions

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

## Amendment (2026-03-14): Build-Time SHACL Unification

> This WP is amended per the [build-time SHACL unification design](../../../docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md). Key changes:
>
> - **`useValidation` initializes `ShapeLoader`** instead of shape derivation. Shape cache warm-up calls `ShapeLoader.loadFromAssembly` instead of reflecting over types.
> - **`validate` CE operation** populates `ValidationMarker.ShapeUri` (now a `Uri`, not `Type`) with the NodeShape URI from the loaded shapes.
> - **`ValidationOptions.MaxDerivationDepth`** is no longer relevant at runtime (depth is controlled at build time); rename to `MaxShapeDepth` and pass it to the CLI configuration.
> - **Startup warnings for framework types** (FR-017) move to build-time diagnostics (emitted by the MSBuild target, WP11). Runtime startup still warns if the embedded resource is missing.
> - **Dependencies updated**: now depends on WP11 (MSBuild auto-invoke), WP12 (ShapeLoader), and WP06 (custom constraints).

---

## Implementation Command

```bash
spec-kitty implement WP07 --base WP12
```

Depends on WP11 (MSBuild auto-invoke), WP12 (ShapeLoader), WP06 (shape merger), WP03 (middleware), WP04 (report serializer), WP05 (shape resolver).

---

## Objectives & Success Criteria

- Implement `ResourceBuilderExtensions.fs`: `validate`, `customConstraint`, `validateWithCapabilities` custom operations on `ResourceBuilder` (FR-018)
- Implement `WebHostBuilderExtensions.fs`: `useValidation` extension for middleware registration and DI setup
- Shape cache initialization at startup: load all shapes from embedded Turtle resource via `ShapeLoader`
- Response validation opt-in for diagnostic mode (FR-019)
- `validate typeof<MyRecord>` in a `resource` CE compiles and adds `ValidationMarker` to endpoint metadata
- `useValidation` registers middleware in the correct pipeline position
- Existing Frank applications without Frank.Validation experience zero behavioral changes (SC-008)

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/005-shacl-validation-from-fsharp-types/quickstart.md` -- Usage examples for `validate`, `customConstraint`, `validateWithCapabilities`, `useValidation`
- `kitty-specs/005-shacl-validation-from-fsharp-types/plan.md` -- Constitution check III (Library, Not Framework), IV (ASP.NET Core Native)
- `kitty-specs/005-shacl-validation-from-fsharp-types/spec.md` -- FR-017, FR-018, FR-019

**Key constraints**:
- Follow `[<AutoOpen>] module` + `[<CustomOperation>]` extension pattern from Frank.Auth and Frank.LinkedData exactly
- `validate` is opt-in per resource -- resources without it are unaffected
- Pipeline ordering: `useAuth` -> `useValidation` -> handler dispatch
- Framework types (HttpContext, HttpRequest) -> skip validation, log startup warning if `validate` was explicit (FR-017)
- Response validation is diagnostic-only: logs violations via `ILogger`, never blocks responses (FR-019)
- .fsproj compilation order: Types -> Constraints -> UriConventions -> TypeMapping -> ShapeLoader -> ShapeCache -> ShapeGraphBuilder -> DataGraphBuilder -> ShapeMerger -> ShapeResolver -> Validator -> ReportSerializer -> ValidationMiddleware -> ResourceBuilderExtensions -> WebHostBuilderExtensions

---

## Subtasks & Detailed Guidance

### Subtask T037 -- Create `ResourceBuilderExtensions.fs` with `validate` custom operation

**Purpose**: Extend the `ResourceBuilder` CE with a `validate` custom operation that adds `ValidationMarker` to endpoint metadata.

**Steps**:
1. Create `src/Frank.Validation/ResourceBuilderExtensions.fs`
2. Implement the extension:

```fsharp
namespace Frank.Validation

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =

    type ResourceBuilder with
        /// Enable SHACL validation for this resource.
        /// Derives a NodeShape from the specified F# type at startup.
        [<CustomOperation("validate")>]
        member _.Validate(state: ResourceSpec, shapeUri: System.Uri) =
            let marker = { ShapeUri = shapeUri
                           CustomConstraints = []
                           ResolverConfig = None }
            // Add ValidationMarker to the resource's metadata
            { state with Metadata = (box marker) :: state.Metadata }
```

3. Verify that `validate (Uri "urn:frank:shape:MyRecord")` compiles inside a `resource` CE. Consider a helper `validateType<'T>` that constructs the URI from the type name for convenience.
4. Verify that the `ValidationMarker` appears in the built endpoint's metadata collection.

**Files**: `src/Frank.Validation/ResourceBuilderExtensions.fs`
**Notes**: Check how Frank.Auth and Frank.LinkedData add metadata to `ResourceSpec`. The `Metadata` field on `ResourceSpec` should be a `obj list` or similar that gets transferred to `EndpointBuilder.Metadata` at build time. Match the existing pattern exactly. If `ResourceSpec` doesn't have a `Metadata` field, check if metadata is added via `EndpointBuilder` directly.

### Subtask T038 -- Implement `customConstraint` custom operation

**Purpose**: Allow developers to add custom constraints to a validated resource.

**Steps**:
1. In `ResourceBuilderExtensions.fs`, add:

```fsharp
        /// Add a custom constraint to a validated resource.
        /// Must be called after `validate`.
        [<CustomOperation("customConstraint")>]
        member _.CustomConstraint(state: ResourceSpec, propertyPath: string, constraint: ConstraintKind) =
            // Find the ValidationMarker in metadata and append the custom constraint
            let custom = { PropertyPath = propertyPath; Constraint = constraint }
            // Update the ValidationMarker's CustomConstraints list
            ...
```

2. The operation must find the existing `ValidationMarker` in the resource's metadata and update its `CustomConstraints` list.
3. If `validate` was not called before `customConstraint`, raise a configuration error at startup.

**Files**: `src/Frank.Validation/ResourceBuilderExtensions.fs`
**Notes**: Since F# records are immutable, updating the marker requires creating a new marker with the constraint appended. The metadata list needs the old marker replaced with the updated one. Consider using a mutable accumulator pattern similar to how other Frank extensions accumulate configuration.

### Subtask T039 -- Implement `validateWithCapabilities` custom operation

**Purpose**: Enable capability-dependent shape resolution for a resource.

**Steps**:
1. In `ResourceBuilderExtensions.fs`, add:

```fsharp
        /// Enable capability-dependent SHACL validation.
        /// Shapes are selected based on the authenticated principal's claims.
        [<CustomOperation("validateWithCapabilities")>]
        member _.ValidateWithCapabilities(state: ResourceSpec, shapeType: System.Type,
                                          overrides: ShapeOverride list) =
            let baseShape = ... // Will be derived at startup
            let config = { BaseShape = baseShape; Overrides = overrides }
            let marker = { ShapeType = shapeType
                           CustomConstraints = []
                           ResolverConfig = Some config }
            { state with Metadata = (box marker) :: state.Metadata }
```

2. Provide helper functions for defining overrides:

```fsharp
    /// Define a shape override for principals with specific claim values.
    let forClaim (claimType: string) (values: string list) (shapeFn: ShaclShape -> ShaclShape) : ShapeOverride =
        // The shapeFn modifies the base auto-derived shape
        // Actual application happens at startup when shapes are derived
        ...
```

3. The `forClaim` helper from the quickstart example should compose naturally in the CE.

**Files**: `src/Frank.Validation/ResourceBuilderExtensions.fs`
**Notes**: The `ShapeOverride.Shape` must be constructed at startup (not at CE definition time) because the base shape is derived from the type at startup. The CE records the configuration; the startup initialization applies `shapeFn` to the derived base shape to produce override shapes. Consider storing the `shapeFn` in the marker and applying it during initialization.

### Subtask T040 -- Create `WebHostBuilderExtensions.fs` with `useValidation`

**Purpose**: Register the validation middleware and shape cache in the application pipeline.

**Steps**:
1. Create `src/Frank.Validation/WebHostBuilderExtensions.fs`
2. Implement the extension:

```fsharp
namespace Frank.Validation

open Frank.Builder
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection

[<AutoOpen>]
module WebHostBuilderExtensions =

    type WebHostBuilder with
        /// Register SHACL validation middleware.
        /// Must be called after useAuth and before resource definitions.
        [<CustomOperation("useValidation")>]
        member _.UseValidation(state: WebHostSpec) =
            let configureServices (services: IServiceCollection) =
                services.AddSingleton<ShapeCache>() |> ignore
                // Register ValidationMiddleware
                services

            let configureApp (app: IApplicationBuilder) =
                app.UseMiddleware<ValidationMiddleware>() |> ignore
                app

            { state with
                ConfigureServices = state.ConfigureServices >> configureServices
                ConfigureApp = state.ConfigureApp >> configureApp }
```

3. Optionally accept a configuration callback for `ValidationOptions`:

```fsharp
        [<CustomOperation("useValidation")>]
        member _.UseValidation(state: WebHostSpec, configure: ValidationOptions -> unit) =
            // Apply user configuration (e.g., MaxDerivationDepth, EnableResponseValidation)
            ...
```

**Files**: `src/Frank.Validation/WebHostBuilderExtensions.fs`
**Notes**: Follow `Frank.Auth.WebHostBuilderExtensions` and `Frank.LinkedData.WebHostBuilderExtensions` patterns exactly. Check how they register middleware and services. The `ShapeCache` is a singleton that holds the `ConcurrentDictionary<Type, ShapesGraph>`.

### Subtask T041 -- Implement shape cache initialization at startup via ShapeLoader

**Purpose**: At application startup, load pre-computed SHACL shapes from the embedded Turtle resource and populate the ShapeCache.

**Steps**:
1. `ShapeCache` is already refactored in WP12 to be `Uri`-keyed with a `LoadAll` method.
2. In the `useValidation` middleware registration, initialize eagerly:

```fsharp
let configureApp (app: IApplicationBuilder) =
    let shapeCache = app.ApplicationServices.GetRequiredService<ShapeCache>()
    // Load shapes from the entry assembly's embedded resource
    let assembly = Assembly.GetEntryAssembly()
    let shapes = ShapeLoader.loadFromAssembly assembly
    shapeCache.LoadAll(shapes)
    app.UseMiddleware<ValidationMiddleware>() |> ignore
    app
```

3. Apply custom constraints (via ShapeMerger, WP06) to pre-loaded shapes where `ValidationMarker` has `CustomConstraints`.
4. Build-time diagnostics (FR-017 framework type warnings) are handled by WP11's MSBuild target. Runtime startup focuses on loading and constraint merging.
5. Log startup info: number of shapes loaded, number of custom constraints applied.

**Files**: `src/Frank.Validation/WebHostBuilderExtensions.fs`
**Notes**: Eager initialization is required to surface configuration errors (missing resource, constraint conflicts) early. The `ShapeLoader` fails fast with `InvalidOperationException` if the embedded resource is missing.

### Subtask T042 -- Implement response validation opt-in (diagnostic mode)

**Purpose**: When enabled, validate handler return types against output shapes and log violations without blocking the response. (FR-019)

**Steps**:
1. Add `ValidationOptions` type:

```fsharp
type ValidationOptions() =
    /// Maximum type derivation depth for recursive types (default 5).
    member val MaxDerivationDepth = 5 with get, set

    /// Enable response validation in diagnostic mode.
    /// When true, handler responses are validated against output shapes
    /// and violations are logged via ILogger but never block the response.
    member val EnableResponseValidation = false with get, set
```

2. In `ValidationMiddleware`, if `EnableResponseValidation` is true:
   - After the handler executes, read the response body
   - Validate against the output shape (derived from handler return type)
   - If violations found, log via `ILogger<ValidationMiddleware>` at Warning level
   - Never modify the response or status code

3. Response validation is diagnostic-only in this phase -- full enforcement is deferred.

**Files**: `src/Frank.Validation/ValidationMiddleware.fs`, `src/Frank.Validation/WebHostBuilderExtensions.fs`
**Notes**: Response body reading requires response buffering, which has performance implications. This should be clearly documented as a diagnostic tool, not for production use. The `ILogger` usage follows Constitution VII (no silent exception swallowing).

---

## Test Strategy

- Run `dotnet build` to verify compilation of all extension files
- Verify `validate typeof<T>` compiles in a `resource` CE
- Verify `useValidation` registers middleware correctly
- Verify shape cache populates at startup
- Run `dotnet test` for extension-specific tests
- Verify existing Frank tests still pass (SC-008)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| ResourceSpec.Metadata field may not exist or differ from expected | Check Frank core's ResourceSpec type; adapt metadata storage pattern to match |
| WebHostBuilder extension pattern may differ between Frank versions | Reference Frank.Auth and Frank.LinkedData extensions as authoritative examples |
| Shape cache concurrency: multiple threads deriving same type | ConcurrentDictionary.GetOrAdd is thread-safe; factory runs at most once |
| Response validation performance impact | Document as diagnostic-only; default disabled; warn about buffering overhead |
| Middleware ordering not enforced | Document recommended order; add startup warning if useAuth is not registered when useValidation is |

---

## Review Guidance

- Verify `[<AutoOpen>]` and `[<CustomOperation>]` patterns match Frank.Auth/LinkedData exactly
- Verify `validate` adds `ValidationMarker` to endpoint metadata
- Verify `useValidation` registers middleware and ShapeCache
- Verify .fsproj compilation order places extensions last
- Verify response validation is log-only (never blocks responses)
- Verify existing Frank tests pass (no behavioral changes for apps without Frank.Validation)
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-15T19:14:01Z – claude-opus – shell_pid=27778 – lane=doing – Assigned agent via workflow command
