namespace Frank.OpenApi

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =

    type ResourceBuilder with

        // GET overload for HandlerDefinition
        [<CustomOperation("get")>]
        member Get : spec:ResourceSpec * handlerDef:HandlerDefinition -> ResourceSpec

        // POST overload for HandlerDefinition
        [<CustomOperation("post")>]
        member Post : spec:ResourceSpec * handlerDef:HandlerDefinition -> ResourceSpec

        // PUT overload for HandlerDefinition
        [<CustomOperation("put")>]
        member Put : spec:ResourceSpec * handlerDef:HandlerDefinition -> ResourceSpec

        // DELETE overload for HandlerDefinition
        [<CustomOperation("delete")>]
        member Delete : spec:ResourceSpec * handlerDef:HandlerDefinition -> ResourceSpec

        // PATCH overload for HandlerDefinition
        [<CustomOperation("patch")>]
        member Patch : spec:ResourceSpec * handlerDef:HandlerDefinition -> ResourceSpec

        // HEAD overload for HandlerDefinition
        [<CustomOperation("head")>]
        member Head : spec:ResourceSpec * handlerDef:HandlerDefinition -> ResourceSpec

        // OPTIONS overload for HandlerDefinition
        [<CustomOperation("options")>]
        member Options : spec:ResourceSpec * handlerDef:HandlerDefinition -> ResourceSpec
