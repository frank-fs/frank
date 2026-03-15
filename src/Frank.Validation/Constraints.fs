namespace Frank.Validation

open System

/// Kinds of custom constraints that can extend auto-derived shapes.
type ConstraintKind =
    | PatternConstraint of regex: string
    | MinInclusiveConstraint of value: obj
    | MaxInclusiveConstraint of value: obj
    | MinExclusiveConstraint of value: obj
    | MaxExclusiveConstraint of value: obj
    | MinLengthConstraint of length: int
    | MaxLengthConstraint of length: int
    | InValuesConstraint of values: string list
    | SparqlConstraint of query: string
    | CustomShaclConstraint of predicateUri: Uri * value: obj

/// A developer-provided custom constraint that extends an auto-derived shape.
type CustomConstraint =
    { PropertyPath: string
      Constraint: ConstraintKind }

/// Shape override for capability-dependent validation.
type ShapeOverride =
    { RequiredClaim: string * string list
      Shape: ShaclShape }

/// Configuration for capability-dependent shape resolution.
type ShapeResolverConfig =
    { BaseShape: ShaclShape
      Overrides: ShapeOverride list }

/// Endpoint metadata marker placed by the `validate` CE custom operation.
/// Read by ValidationMiddleware to determine if validation applies.
/// ShapeUri identifies the pre-computed SHACL shape in the URI-keyed ShapeCache.
type ValidationMarker =
    { ShapeUri: Uri
      CustomConstraints: CustomConstraint list
      ResolverConfig: ShapeResolverConfig option }
