namespace Frank.Alps

open Frank.Builder

/// Adds `binds` to `handler { }`: attaches the transition `Descriptor` this handler implements, so
/// `EndpointSurface` (Task 13) and `AlpsDocument`'s startup validation (Task 14) can retrieve it back
/// via `HandlerDefinition.tryFind<Descriptor>`/`Endpoint.Metadata.GetOrderedMetadata<Descriptor>()`.
[<AutoOpen>]
module HandlerBuilderExtensions =
    type HandlerBuilder with

        [<CustomOperation("binds")>]
        member Binds: def: HandlerDefinition * descriptor: Descriptor -> HandlerDefinition
