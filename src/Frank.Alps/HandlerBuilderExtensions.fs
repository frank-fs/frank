namespace Frank.Alps

open Frank.Builder

[<AutoOpen>]
module HandlerBuilderExtensions =
    type HandlerBuilder with

        [<CustomOperation("binds")>]
        member _.Binds(def: HandlerDefinition, descriptor: Descriptor) : HandlerDefinition =
            HandlerDefinition.addMetadata descriptor def
