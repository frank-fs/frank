namespace Frank.Builder

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

[<Sealed>]
type HandlerBuilder =
    new : unit -> HandlerBuilder

    member inline Yield : 'T -> HandlerDefinition

    member inline Run : def:HandlerDefinition -> HandlerDefinition

    // Handle operations - multiple overloads for different handler signatures
    [<CustomOperation("handle")>]
    member inline Handle : def:HandlerDefinition * handler:(HttpContext -> Task) -> HandlerDefinition

    [<CustomOperation("handle")>]
    member inline Handle : def:HandlerDefinition * handler:(HttpContext -> Task<'a>) -> HandlerDefinition

    [<CustomOperation("handle")>]
    member inline Handle : def:HandlerDefinition * handler:(HttpContext -> Async<unit>) -> HandlerDefinition

    [<CustomOperation("handle")>]
    member inline Handle : def:HandlerDefinition * handler:(HttpContext -> Async<'a>) -> HandlerDefinition

    // Metadata operations
    [<CustomOperation("name")>]
    member inline Name : def:HandlerDefinition * name:string -> HandlerDefinition

    [<CustomOperation("summary")>]
    member inline Summary : def:HandlerDefinition * summary:string -> HandlerDefinition

    [<CustomOperation("description")>]
    member inline Description : def:HandlerDefinition * description:string -> HandlerDefinition

    [<CustomOperation("tags")>]
    member inline Tags : def:HandlerDefinition * tags:string list -> HandlerDefinition

    // Response type operations
    [<CustomOperation("produces")>]
    member inline Produces : def:HandlerDefinition * responseType:Type * statusCode:int -> HandlerDefinition

    [<CustomOperation("produces")>]
    member inline Produces : def:HandlerDefinition * responseType:Type * statusCode:int * contentTypes:string list -> HandlerDefinition

    [<CustomOperation("producesEmpty")>]
    member inline ProducesEmpty : def:HandlerDefinition * statusCode:int -> HandlerDefinition

    // Request type operation
    [<CustomOperation("accepts")>]
    member inline Accepts : def:HandlerDefinition * requestType:Type -> HandlerDefinition

    [<CustomOperation("accepts")>]
    member inline Accepts : def:HandlerDefinition * requestType:Type * contentTypes:string list -> HandlerDefinition

[<AutoOpen>]
module HandlerBuilderInstance =
    /// Module-level handler builder instance
    val handler : HandlerBuilder
