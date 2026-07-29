namespace Frank.Builder

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing

[<Sealed>]
type HandlerBuilder() =

    member _.Yield(_) = HandlerDefinition.Empty

    member _.Run(def: HandlerDefinition) =
        // Validate that a handler has been set
        if obj.ReferenceEquals(def.Handler, Unchecked.defaultof<RequestDelegate>) then
            failwith "Handler must be set using the 'handle' operation"

        def

    // Handle operations - multiple overloads for different handler signatures
    [<CustomOperation("handle")>]
    member _.Handle(def: HandlerDefinition, handler: HttpContext -> Task) =
        { def with
            Handler = RequestDelegate(handler) }

    [<CustomOperation("handle")>]
    member _.Handle(def: HandlerDefinition, handler: HttpContext -> Task<'a>) =
        { def with
            Handler = RequestDelegate(fun ctx -> handler ctx :> Task) }

    [<CustomOperation("handle")>]
    member _.Handle(def: HandlerDefinition, handler: HttpContext -> Async<unit>) =
        { def with
            Handler = RequestDelegate(fun ctx -> Async.StartAsTask(handler ctx) :> Task) }

    [<CustomOperation("handle")>]
    member _.Handle(def: HandlerDefinition, handler: HttpContext -> Async<'a>) =
        { def with
            Handler = RequestDelegate(fun ctx -> Async.StartAsTask(handler ctx) :> Task) }

    // Metadata operations
    [<CustomOperation("name")>]
    member _.Name(def: HandlerDefinition, name: string) =
        HandlerDefinition.addMetadata (EndpointNameMetadata(name)) def

    [<CustomOperation("summary")>]
    member _.Summary(def: HandlerDefinition, summary: string) =
        HandlerDefinition.addMetadata (EndpointSummaryAttribute(summary)) def

    [<CustomOperation("description")>]
    member _.Description(def: HandlerDefinition, description: string) =
        HandlerDefinition.addMetadata (EndpointDescriptionAttribute(description)) def

    [<CustomOperation("tags")>]
    member _.Tags(def: HandlerDefinition, tags: string list) =
        if List.isEmpty tags then
            def
        else
            HandlerDefinition.addMetadata (TagsAttribute(tags |> List.toArray)) def

    // Response type operations
    [<CustomOperation("produces")>]
    member _.Produces(def: HandlerDefinition, responseType: Type, statusCode: int) =
        HandlerDefinition.addMetadata
            (ProducesResponseTypeMetadata(statusCode, responseType, [| ApplicationJson |]))
            def

    [<CustomOperation("produces")>]
    member _.Produces(def: HandlerDefinition, responseType: Type, statusCode: int, contentTypes: string list) =
        let contentTypes =
            if List.isEmpty contentTypes then
                [| ApplicationJson |]
            else
                contentTypes |> Array.ofList

        HandlerDefinition.addMetadata (ProducesResponseTypeMetadata(statusCode, responseType, contentTypes)) def

    [<CustomOperation("producesEmpty")>]
    member _.ProducesEmpty(def: HandlerDefinition, statusCode: int) =
        HandlerDefinition.addMetadata
            (ProducesResponseTypeMetadata(statusCode, typeof<Void>, [| ApplicationJson |]))
            def

    // Request type operation
    [<CustomOperation("accepts")>]
    member _.Accepts(def: HandlerDefinition, requestType: Type) =
        HandlerDefinition.addMetadata (AcceptsMetadata([| ApplicationJson |], requestType, false)) def

    [<CustomOperation("accepts")>]
    member _.Accepts(def: HandlerDefinition, requestType: Type, contentTypes: string list) =
        let contentTypes =
            if List.isEmpty contentTypes then
                [| ApplicationJson |]
            else
                contentTypes |> Array.ofList

        HandlerDefinition.addMetadata (AcceptsMetadata(contentTypes, requestType, false)) def

[<AutoOpen>]
module HandlerBuilderInstance =
    /// Module-level handler builder instance
    let handler = HandlerBuilder()
