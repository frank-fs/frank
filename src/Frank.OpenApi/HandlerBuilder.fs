namespace Frank.OpenApi

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

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
        { def with Handler = RequestDelegate(handler) }

    [<CustomOperation("handle")>]
    member _.Handle(def: HandlerDefinition, handler: HttpContext -> Task<'a>) =
        { def with Handler = RequestDelegate(fun ctx -> handler ctx :> Task) }

    [<CustomOperation("handle")>]
    member _.Handle(def: HandlerDefinition, handler: HttpContext -> Async<unit>) =
        { def with Handler = RequestDelegate(fun ctx -> Async.StartAsTask(handler ctx) :> Task) }

    [<CustomOperation("handle")>]
    member _.Handle(def: HandlerDefinition, handler: HttpContext -> Async<'a>) =
        { def with Handler = RequestDelegate(fun ctx -> Async.StartAsTask(handler ctx) :> Task) }

    // Metadata operations
    [<CustomOperation("name")>]
    member _.Name(def: HandlerDefinition, name: string) =
        { def with Name = Some name }

    [<CustomOperation("summary")>]
    member _.Summary(def: HandlerDefinition, summary: string) =
        { def with Summary = Some summary }

    [<CustomOperation("description")>]
    member _.Description(def: HandlerDefinition, description: string) =
        { def with Description = Some description }

    [<CustomOperation("tags")>]
    member _.Tags(def: HandlerDefinition, tags: string list) =
        { def with Tags = tags }

    // Response type operations
    [<CustomOperation("produces")>]
    member _.Produces(def: HandlerDefinition, responseType: Type, statusCode: int) =
        let info =
            { StatusCode = statusCode
              ResponseType = Some responseType
              ContentTypes = [ "application/json" ]
              Description = None }
        { def with Produces = def.Produces @ [ info ] }

    [<CustomOperation("produces")>]
    member _.Produces(def: HandlerDefinition, responseType: Type, statusCode: int, contentTypes: string list) =
        let info =
            { StatusCode = statusCode
              ResponseType = Some responseType
              ContentTypes = contentTypes
              Description = None }
        { def with Produces = def.Produces @ [ info ] }

    [<CustomOperation("producesEmpty")>]
    member _.ProducesEmpty(def: HandlerDefinition, statusCode: int) =
        let info =
            { StatusCode = statusCode
              ResponseType = None
              ContentTypes = []
              Description = None }
        { def with Produces = def.Produces @ [ info ] }

    // Request type operation
    [<CustomOperation("accepts")>]
    member _.Accepts(def: HandlerDefinition, requestType: Type) =
        let info =
            { RequestType = requestType
              ContentTypes = [ "application/json" ]
              IsOptional = false }
        { def with Accepts = def.Accepts @ [ info ] }

    [<CustomOperation("accepts")>]
    member _.Accepts(def: HandlerDefinition, requestType: Type, contentTypes: string list) =
        let info =
            { RequestType = requestType
              ContentTypes = contentTypes
              IsOptional = false }
        { def with Accepts = def.Accepts @ [ info ] }

[<AutoOpen>]
module HandlerBuilderInstance =
    /// Module-level handler builder instance
    let handler = HandlerBuilder()
