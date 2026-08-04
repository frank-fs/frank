namespace Frank.Validation

open Frank.Builder

/// Metadata attached per-resource by `useValidation`; read back by the interceptor middleware
/// (WebHostBuilderExtensions.fs) via ctx.GetEndpoint().Metadata.GetMetadata<ValidationMetadata>().
/// Internal, exactly like Frank's own ResourceLinkProvider -- not a public contract.
type internal ValidationMetadata = ValidationMetadata of VDS.RDF.Shacl.ShapesGraph

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        /// Declares which ShapesGraph validates this resource's POST/PUT/PATCH application/ld+json
        /// bodies. Declarative only -- does nothing at request time by itself; requires
        /// `webHost { useValidation }` (WebHostBuilderExtensions.fs) to actually intercept requests.
        [<CustomOperation("useValidation")>]
        member UseValidation: spec: ResourceSpec * shapesGraph: VDS.RDF.Shacl.ShapesGraph -> ResourceSpec
