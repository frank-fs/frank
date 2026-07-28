namespace Frank.JsonHome

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =

    type ResourceBuilder with

        /// Declares the link relation type keying this resource in the home
        /// document. Resources without one are omitted.
        [<CustomOperation("rel")>]
        member Rel: spec: ResourceSpec * rel: string -> ResourceSpec

        /// Declares the absolute URI identifying a route variable's semantics.
        [<CustomOperation("hrefVar")>]
        member HrefVar: spec: ResourceSpec * name: string * uri: string -> ResourceSpec

        /// Declares documentation for this resource's link relation type.
        [<CustomOperation("docs")>]
        member Docs: spec: ResourceSpec * uri: string -> ResourceSpec

        /// Marks this resource deprecated.
        [<CustomOperation("deprecated")>]
        member Deprecated: spec: ResourceSpec -> ResourceSpec

        /// Marks this resource gone.
        [<CustomOperation("gone")>]
        member Gone: spec: ResourceSpec -> ResourceSpec
