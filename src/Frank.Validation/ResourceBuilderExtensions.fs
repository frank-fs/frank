namespace Frank.Validation

open System
open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =

    /// Mutable cell used during CE construction so that `customConstraint` and
    /// `validateWithCapabilities` can amend the ValidationMarker established by `validate`.
    /// Stored as a tagged metadata item on the endpoint and captured by reference in closures.
    [<AllowNullLiteral>]
    type internal ValidationMarkerRef(initial: ValidationMarker) =
        let mutable current = initial
        member _.Current = current

        member _.AddConstraint(c: CustomConstraint) =
            current <-
                { current with
                    CustomConstraints = current.CustomConstraints @ [ c ] }

        member _.SetResolverConfig(config: ShapeResolverConfig) =
            current <-
                { current with
                    ResolverConfig = Some config }

    /// Accumulator attached to each validate call, so subsequent
    /// customConstraint/validateWithCapabilities calls can find the ref.
    /// Safe because CE construction is single-threaded (one resource at a time).
    let mutable private currentMarkerRef: ValidationMarkerRef = null

    type ResourceBuilder with

        /// Mark this resource endpoint for SHACL validation using the shape identified by
        /// the given URI.
        ///
        /// The shape must be pre-loaded into ShapeCache at startup via `useValidation`.
        /// Endpoints without this operation are unaffected (zero overhead).
        [<CustomOperation("validate")>]
        member _.Validate(spec: ResourceSpec, shapeUri: Uri) : ResourceSpec =
            let marker =
                { ShapeUri = shapeUri
                  CustomConstraints = []
                  ResolverConfig = None }

            let markerRef = ValidationMarkerRef(marker)
            currentMarkerRef <- markerRef

            ResourceBuilder.AddMetadata(
                spec,
                fun b ->
                    // Add the final (potentially mutated) marker at build time.
                    b.Metadata.Add(box markerRef.Current)
            )

        /// Append a custom SHACL constraint to the ValidationMarker registered by the
        /// preceding `validate` call.
        ///
        /// Must be called after `validate` on the same resource.
        /// Constraints are additive only — they can tighten but never weaken the base shape.
        [<CustomOperation("customConstraint")>]
        member _.CustomConstraint
            (spec: ResourceSpec, propertyPath: string, constraintKind: ConstraintKind)
            : ResourceSpec =
            if isNull currentMarkerRef then
                raise (
                    InvalidOperationException(
                        "customConstraint must follow a `validate` call on the same resource. No ValidationMarker found."
                    )
                )

            currentMarkerRef.AddConstraint(
                { PropertyPath = propertyPath
                  Constraint = constraintKind }
            )

            spec

        /// Enable capability-dependent shape resolution.
        ///
        /// The resolver selects the appropriate shape per-request based on the caller's
        /// claims. Must be called after `validate` on the same resource.
        [<CustomOperation("validateWithCapabilities")>]
        member _.ValidateWithCapabilities(spec: ResourceSpec, resolverConfig: ShapeResolverConfig) : ResourceSpec =
            if isNull currentMarkerRef then
                raise (
                    InvalidOperationException(
                        "validateWithCapabilities must follow a `validate` call on the same resource. No ValidationMarker found."
                    )
                )

            currentMarkerRef.SetResolverConfig(resolverConfig)
            spec
