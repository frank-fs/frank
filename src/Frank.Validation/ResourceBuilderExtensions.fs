namespace Frank.Validation

open Frank.Builder

type internal ValidationMetadata = ValidationMetadata of VDS.RDF.Shacl.ShapesGraph

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        [<CustomOperation("useValidation")>]
        member _.UseValidation(spec: ResourceSpec, shapesGraph: VDS.RDF.Shacl.ShapesGraph) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add(ValidationMetadata shapesGraph)))
