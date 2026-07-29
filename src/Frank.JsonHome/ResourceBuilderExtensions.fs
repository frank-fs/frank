namespace Frank.JsonHome

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =

    type ResourceBuilder with

        [<CustomOperation("rel")>]
        member _.Rel(spec: ResourceSpec, rel: string) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Rel = rel }))

        [<CustomOperation("hrefVar")>]
        member _.HrefVar(spec: ResourceSpec, name: string, uri: string) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Name = name; Uri = uri }))

        [<CustomOperation("docs")>]
        member _.Docs(spec: ResourceSpec, uri: string) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add({ DocsMetadata.Uri = uri })))

        [<CustomOperation("deprecated")>]
        member _.Deprecated(spec: ResourceSpec) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Status = ResourceStatus.Deprecated }))

        [<CustomOperation("gone")>]
        member _.Gone(spec: ResourceSpec) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Status = ResourceStatus.Gone }))

        [<CustomOperation("acceptRanges")>]
        member _.AcceptRanges(spec: ResourceSpec, units: string list) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Units = units }))

        [<CustomOperation("acceptPrefer")>]
        member _.AcceptPrefer(spec: ResourceSpec, preferences: string list) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Preferences = preferences }))

        [<CustomOperation("preconditionRequired")>]
        member _.PreconditionRequired(spec: ResourceSpec, preconditions: Precondition list) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Preconditions = preconditions }))

        [<CustomOperation("authScheme")>]
        member _.AuthScheme(spec: ResourceSpec, scheme: string, realms: string list) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, (fun b -> b.Metadata.Add { Scheme = scheme; Realms = realms }))
