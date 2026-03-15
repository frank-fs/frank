# Data Model: Frank.Validation

**Feature**: 005-shacl-validation-from-fsharp-types
**Date**: 2026-03-07

## Entity Relationship Overview

```
ShaclShape (NodeShape)
       │
       │ 1:N
       v
PropertyShape ──> XsdDatatype (via TypeMapping)
       │
       │ 0..1 (nested)
       v
ShaclShape (recursive reference)

ShapeDerivation
       │
       │ produces
       v
ShaclShape ──> cached in ShapeCache (ConcurrentDictionary<Type, ShaclShape>)

ShapeResolver
       │ selects based on
       │ ClaimsPrincipal capabilities
       v
ShaclShape (base or capability-specific variant)

CustomConstraint ──> merged into ──> ShaclShape (via ShapeMerger)

Validator
       │ validates
       │ data graph against shapes graph
       v
ValidationReport
       │
       │ 1:N
       v
ValidationResult (one per violation)
```

## Core Entities

### ShaclShape

A SHACL NodeShape derived from an F# type definition. Represents the complete set of constraints that valid data of this type must satisfy.

| Field | Type | Description |
|-------|------|-------------|
| TargetType | System.Type | The F# type this shape was derived from |
| NodeShapeUri | Uri | The URI identifying this NodeShape (e.g., `urn:frank:shape:MyApp.Customer`) |
| Properties | PropertyShape list | One property shape per record field |
| Closed | bool | Whether additional properties are disallowed (default true for records) |
| Description | string option | Human-readable description of the shape |

**F# type signature**:

```fsharp
type ShaclShape =
    { TargetType: Type
      NodeShapeUri: Uri
      Properties: PropertyShape list
      Closed: bool
      Description: string option }
```

**Identity**: Unique per `TargetType`. Cached in `ConcurrentDictionary<Type, ShaclShape>`.
**Lifecycle**: Created at application startup, immutable thereafter.

### PropertyShape

A SHACL property shape corresponding to a single field of an F# record. Carries datatype, cardinality, and optional value constraints.

| Field | Type | Description |
|-------|------|-------------|
| Path | string | The property path (field name, used as sh:path) |
| Datatype | XsdDatatype option | XSD datatype constraint (None for nested node references) |
| MinCount | int | Minimum cardinality (1 for required, 0 for option) |
| MaxCount | int option | Maximum cardinality (None for unbounded collections) |
| NodeReference | Uri option | sh:node reference for nested record types |
| InValues | string list option | sh:in constraint for simple DU cases |
| OrShapes | Uri list option | sh:or constraint for DU cases with payloads |
| Pattern | string option | sh:pattern regex constraint (auto-derived for Guid, custom for others) |
| MinInclusive | obj option | sh:minInclusive numeric constraint (custom only) |
| MaxInclusive | obj option | sh:maxInclusive numeric constraint (custom only) |
| Description | string option | sh:description for this property |

**F# type signature**:

```fsharp
type PropertyShape =
    { Path: string
      Datatype: XsdDatatype option
      MinCount: int
      MaxCount: int option
      NodeReference: Uri option
      InValues: string list option
      OrShapes: Uri list option
      Pattern: string option
      MinInclusive: obj option
      MaxInclusive: obj option
      Description: string option }
```

**Derived from**: `FSharpType.GetRecordFields` -> `PropertyInfo` -> `PropertyShape`.

### XsdDatatype

The XSD datatype assigned to a property shape, derived from the F# CLR type.

```fsharp
type XsdDatatype =
    | XsdString
    | XsdInteger
    | XsdLong
    | XsdDouble
    | XsdDecimal
    | XsdBoolean
    | XsdDateTimeStamp
    | XsdDateTime
    | XsdDate
    | XsdTime
    | XsdDuration
    | XsdAnyUri
    | XsdBase64Binary
    | Custom of Uri
```

**Used by**: `TypeMapping.mapType : Type -> XsdDatatype option`

### ValidationReport

A W3C SHACL ValidationReport produced when request data violates one or more constraints. Serializable as RDF (via Frank.LinkedData) or as RFC 9457 Problem Details.

| Field | Type | Description |
|-------|------|-------------|
| Conforms | bool | Whether the data graph conforms to all shapes |
| Results | ValidationResult list | One entry per constraint violation |
| ShapeUri | Uri | The NodeShape that was validated against |

**F# type signature**:

```fsharp
type ValidationReport =
    { Conforms: bool
      Results: ValidationResult list
      ShapeUri: Uri }
```

**Lifecycle**: Created per-request during validation. Disposed after response serialization.
**Serialization**: Content-negotiated -- SHACL ValidationReport (JSON-LD, Turtle, RDF/XML) for semantic clients; RFC 9457 Problem Details for `application/json` clients.

### ValidationResult

A single constraint violation within a ValidationReport. Identifies what failed, where, and why.

| Field | Type | Description |
|-------|------|-------------|
| FocusNode | string | The data node being validated (typically the root request object) |
| ResultPath | string | The property path that failed (e.g., "name", "address.zipCode") |
| Value | obj option | The offending value (None if the field was missing) |
| SourceConstraint | string | The SHACL constraint component that was violated (e.g., "sh:minCount", "sh:datatype") |
| Message | string | Human-readable error message |
| Severity | ValidationSeverity | Violation, Warning, or Info |

**F# type signature**:

```fsharp
type ValidationSeverity =
    | Violation
    | Warning
    | Info

type ValidationResult =
    { FocusNode: string
      ResultPath: string
      Value: obj option
      SourceConstraint: string
      Message: string
      Severity: ValidationSeverity }
```

### ShapeDerivation

The startup-time process that maps F# types to SHACL shapes via .NET reflection. Not a persisted entity -- a module of pure functions.

**F# function signatures**:

```fsharp
module ShapeDerivation =
    /// Derive a ShaclShape from an F# type. Handles records, DUs, option, collections,
    /// nested types, and recursive types (with cycle detection).
    val deriveShape : maxDepth:int -> typ:Type -> ShaclShape

    /// Derive a PropertyShape from a single record field.
    val deriveProperty : maxDepth:int -> derivationStack:Set<Type> -> field:PropertyInfo -> PropertyShape

    /// Check if a type is a derivable domain type (record or DU), excluding
    /// framework types like HttpContext, HttpRequest, CancellationToken.
    val isDerivableType : typ:Type -> bool
```

**Key behaviors**:
- Maintains a `Set<Type>` derivation stack for cycle detection
- Caps recursion at `maxDepth` (configurable, default 5)
- Skips framework/infrastructure types (`HttpContext`, `HttpRequest`, `CancellationToken`, etc.)
- Expands generic types at point of use (e.g., `PagedResult<Customer>` -> concrete shape)

### ShapeResolver

The runtime component that selects the appropriate SHACL shape for a given request, considering capability-dependent overrides from Frank.Auth.

**F# type signature**:

```fsharp
type ShapeOverride =
    { RequiredClaim: string * string list
      Shape: ShaclShape }

type ShapeResolverConfig =
    { BaseShape: ShaclShape
      Overrides: ShapeOverride list }

module ShapeResolver =
    /// Select the appropriate shape for a request, given the authenticated principal.
    /// Returns the most specific matching override, or the base shape if no override matches.
    val resolve : config:ShapeResolverConfig -> principal:ClaimsPrincipal -> ShaclShape
```

**Selection logic**:
1. Iterate overrides in registration order
2. First override whose `RequiredClaim` matches the principal's claims is selected
3. If no override matches, return `BaseShape` (the auto-derived, most restrictive shape)

### CustomConstraint

A developer-provided SHACL constraint that extends an auto-derived shape. Additive only -- cannot weaken auto-derived constraints.

| Field | Type | Description |
|-------|------|-------------|
| PropertyPath | string | The field this constraint applies to |
| Constraint | ConstraintKind | The type of constraint being added |

**F# type signature**:

```fsharp
type ConstraintKind =
    | PatternConstraint of regex:string
    | MinInclusiveConstraint of value:obj
    | MaxInclusiveConstraint of value:obj
    | MinExclusiveConstraint of value:obj
    | MaxExclusiveConstraint of value:obj
    | MinLengthConstraint of length:int
    | MaxLengthConstraint of length:int
    | InValuesConstraint of values:string list
    | SparqlConstraint of query:string
    | CustomShaclConstraint of predicateUri:Uri * value:obj

type CustomConstraint =
    { PropertyPath: string
      Constraint: ConstraintKind }
```

**Merging rules** (enforced by `ShapeMerger`):
- Custom constraints are merged with auto-derived constraints at startup
- A custom `MinCount 0` on a field with auto-derived `MinCount 1` raises `InvalidOperationException` (contradiction)
- A custom `Pattern` on a field with auto-derived `Pattern` adds both (AND semantics)
- A custom `InValues` on a field with auto-derived `InValues` intersects the sets (tighter constraint)

### ValidationMarker

Endpoint metadata marker placed by the `validate` CE custom operation. Read by `ValidationMiddleware` to determine if validation applies.

```fsharp
type ValidationMarker =
    { ShapeType: Type
      CustomConstraints: CustomConstraint list
      ResolverConfig: ShapeResolverConfig option }
```

**Pattern**: Same as `LinkedDataMarker` in Frank.LinkedData -- a metadata object added to `EndpointBuilder.Metadata` during CE build, read by middleware at request time.

## Relationships

1. **ShaclShape -> PropertyShape**: One NodeShape contains one or more property shapes (1:N). Each property shape corresponds to a record field.

2. **PropertyShape -> ShaclShape**: A property shape may reference another NodeShape via `NodeReference` for nested record types. This can be self-referential (recursive types, cycle-capped).

3. **PropertyShape -> XsdDatatype**: Each property shape has at most one XSD datatype, determined by `TypeMapping.mapType`. Node references (nested records) have no datatype.

4. **ShapeDerivation -> ShaclShape**: The derivation process produces one `ShaclShape` per F# type. Output is cached for reuse.

5. **ShapeResolver -> ShaclShape**: The resolver selects one `ShaclShape` per request from the base shape or capability-dependent overrides.

6. **CustomConstraint -> ShaclShape**: Custom constraints are merged into auto-derived shapes at startup by `ShapeMerger`. The merge is one-time and produces a new immutable `ShaclShape`.

7. **Validator -> ValidationReport**: The validator takes a `ShaclShape` and request data, produces a `ValidationReport`. The report's `Conforms` field determines pass/fail.

8. **ValidationReport -> ValidationResult**: One report contains zero or more results (1:N). Zero results means `Conforms = true`.

9. **ValidationMarker -> ValidationMiddleware**: The middleware reads `ValidationMarker` from endpoint metadata to determine the target type, custom constraints, and resolver configuration.

10. **ShapeResolver -> Frank.Auth**: The resolver accesses `ClaimsPrincipal` from `HttpContext.User` to select capability-dependent shape overrides. This is a read-only dependency -- no coupling to `AuthRequirement` internals.
