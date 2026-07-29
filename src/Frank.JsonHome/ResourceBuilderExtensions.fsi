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

        /// Declares the HTTP range-specifiers this resource accepts, typically
        /// "bytes".
        [<CustomOperation("acceptRanges")>]
        member AcceptRanges: spec: ResourceSpec * units: string list -> ResourceSpec

        /// Declares the RFC 7240 preferences this resource supports. A server
        /// remains free to ignore any of them.
        [<CustomOperation("acceptPrefer")>]
        member AcceptPrefer: spec: ResourceSpec * preferences: string list -> ResourceSpec

        /// Declares that state-changing requests to this resource must carry a
        /// precondition.
        [<CustomOperation("preconditionRequired")>]
        member PreconditionRequired: spec: ResourceSpec * preconditions: Precondition list -> ResourceSpec

        /// Declares an HTTP authentication scheme this resource accepts, with
        /// the protection spaces it belongs to. May be used more than once.
        /// Pass an empty list when the scheme covers no named realm.
        [<CustomOperation("authScheme")>]
        member AuthScheme: spec: ResourceSpec * scheme: string * realms: string list -> ResourceSpec
