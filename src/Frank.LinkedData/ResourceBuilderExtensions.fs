namespace Frank.LinkedData

open Frank.Builder

/// Marker placed in endpoint metadata to signal LinkedData support.
type LinkedDataMarker = LinkedDataMarker

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        /// Marks this resource for linked data content negotiation.
        /// Endpoints with this marker will have RDF serialization available
        /// when the useLinkedData middleware is active.
        [<CustomOperation("linkedData")>]
        member _.LinkedData(spec: ResourceSpec) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, fun b -> b.Metadata.Add(LinkedDataMarker))
