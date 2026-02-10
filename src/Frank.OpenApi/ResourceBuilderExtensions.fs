namespace Frank.OpenApi

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =

    /// Helper to add a HandlerDefinition to a ResourceSpec
    let private addHandlerDefinition (httpMethod: string) (spec: ResourceSpec) (def: HandlerDefinition) : ResourceSpec =
        // Add the handler
        let specWithHandler = { spec with Handlers = (httpMethod, def.Handler) :: spec.Handlers }
        // Wrap metadata conventions to only apply to this specific HTTP method
        let conventions = HandlerDefinitionMetadata.toConventions def
        let methodSpecificConventions =
            conventions |> List.map (fun conv ->
                fun (builder: EndpointBuilder) ->
                    // Find HttpMethodMetadata in the builder's metadata list
                    let httpMethodMeta =
                        builder.Metadata
                        |> Seq.tryFind (fun m -> m :? HttpMethodMetadata)
                        |> Option.map (fun m -> m :?> HttpMethodMetadata)
                    match httpMethodMeta with
                    | Some meta when meta.HttpMethods |> Seq.contains httpMethod ->
                        conv builder
                    | _ -> ()
            )
        { specWithHandler with Metadata = specWithHandler.Metadata @ methodSpecificConventions }

    type ResourceBuilder with

        // GET overload for HandlerDefinition
        [<CustomOperation("get")>]
        member _.Get(spec: ResourceSpec, handlerDef: HandlerDefinition) : ResourceSpec =
            addHandlerDefinition HttpMethods.Get spec handlerDef

        // POST overload for HandlerDefinition
        [<CustomOperation("post")>]
        member _.Post(spec: ResourceSpec, handlerDef: HandlerDefinition) : ResourceSpec =
            addHandlerDefinition HttpMethods.Post spec handlerDef

        // PUT overload for HandlerDefinition
        [<CustomOperation("put")>]
        member _.Put(spec: ResourceSpec, handlerDef: HandlerDefinition) : ResourceSpec =
            addHandlerDefinition HttpMethods.Put spec handlerDef

        // DELETE overload for HandlerDefinition
        [<CustomOperation("delete")>]
        member _.Delete(spec: ResourceSpec, handlerDef: HandlerDefinition) : ResourceSpec =
            addHandlerDefinition HttpMethods.Delete spec handlerDef

        // PATCH overload for HandlerDefinition
        [<CustomOperation("patch")>]
        member _.Patch(spec: ResourceSpec, handlerDef: HandlerDefinition) : ResourceSpec =
            addHandlerDefinition HttpMethods.Patch spec handlerDef

        // HEAD overload for HandlerDefinition
        [<CustomOperation("head")>]
        member _.Head(spec: ResourceSpec, handlerDef: HandlerDefinition) : ResourceSpec =
            addHandlerDefinition HttpMethods.Head spec handlerDef

        // OPTIONS overload for HandlerDefinition
        [<CustomOperation("options")>]
        member _.Options(spec: ResourceSpec, handlerDef: HandlerDefinition) : ResourceSpec =
            addHandlerDefinition HttpMethods.Options spec handlerDef
