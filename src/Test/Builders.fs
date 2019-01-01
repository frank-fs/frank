namespace Test

module Builder =

    open System.Threading.Tasks
    open Microsoft.AspNetCore
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Http

    type ResourceBuilder (app, name, template) =

        member __.Run(handlers:(string * (HttpContext -> Task)) list) =
            let handler =
                handlers
                |> List.distinctBy fst
                |> ResourceHandler
                :> Routing.IRouter
            let builder = Routing.RouteBuilder(applicationBuilder=app, defaultHandler=handler)
            builder.MapRoute(name=name, template=template) |> ignore
            let router = builder.Build()
            app.UseRouter(router) |> ignore
        
        member __.Yield(handler) : (string * (HttpContext -> Task)) list = []

        [<CustomOperation("head")>]
        member __.Head(handlers, handler:HttpContext -> Task) =
            (HttpMethods.Head, handler)::handlers

        member __.Head(handlers, handler:HttpContext -> Task<'a>) =
            (HttpMethods.Head, (fun ctx -> handler ctx :> Task))::handlers

        member __.Head(handlers, handler:HttpContext -> Async<'a>) =
            (HttpMethods.Head, (fun ctx -> handler ctx |> Async.StartAsTask :> Task))::handlers

        member __.Head(handlers, handler:HttpContext -> unit) =
            (HttpMethods.Head, (fun ctx -> Task.FromResult(handler ctx) :> Task))::handlers

        [<CustomOperation("get")>]
        member __.Get(handlers, handler:HttpContext -> Task) =
            (HttpMethods.Get, handler)::handlers

        member __.Get(handlers, handler:HttpContext -> Task<'a>) =
            (HttpMethods.Get, (fun ctx -> handler ctx :> Task))::handlers

        member __.Get(handlers, handler:HttpContext -> Async<'a>) =
            (HttpMethods.Get, (fun ctx -> handler ctx |> Async.StartAsTask :> Task))::handlers

        member __.Get(handlers, handler:HttpContext -> unit) =
            (HttpMethods.Get, (fun ctx -> Task.FromResult(handler ctx) :> Task))::handlers

        [<CustomOperation("post")>]
        member __.Post(handlers, handler:HttpContext -> Task) =
            (HttpMethods.Post, handler)::handlers

        member __.Post(handlers, handler:HttpContext -> Task<'a>) =
            (HttpMethods.Post, (fun ctx -> handler ctx :> Task))::handlers

        member __.Post(handlers, handler:HttpContext -> Async<'a>) =
            (HttpMethods.Post, (fun ctx -> handler ctx |> Async.StartAsTask :> Task))::handlers

        member __.Post(handlers, handler:HttpContext -> unit) =
            (HttpMethods.Post, (fun ctx -> Task.FromResult(handler ctx) :> Task))::handlers

        [<CustomOperation("put")>]
        member __.Put(handlers, handler:HttpContext -> Task) =
            (HttpMethods.Put, handler)::handlers

        member __.Put(handlers, handler:HttpContext -> Task<'a>) =
            (HttpMethods.Put, (fun ctx -> handler ctx :> Task))::handlers

        member __.Put(handlers, handler:HttpContext -> Async<'a>) =
            (HttpMethods.Put, (fun ctx -> handler ctx |> Async.StartAsTask :> Task))::handlers

        member __.Put(handlers, handler:HttpContext -> unit) =
            (HttpMethods.Put, (fun ctx -> Task.FromResult(handler ctx) :> Task))::handlers

        [<CustomOperation("patch")>]
        member __.Patch(handlers, handler:HttpContext -> Task) =
            (HttpMethods.Patch, handler)::handlers

        member __.Patch(handlers, handler:HttpContext -> Task<'a>) =
            (HttpMethods.Patch, (fun ctx -> handler ctx :> Task))::handlers

        member __.Patch(handlers, handler:HttpContext -> Async<'a>) =
            (HttpMethods.Patch, (fun ctx -> handler ctx |> Async.StartAsTask :> Task))::handlers

        member __.Patch(handlers, handler:HttpContext -> unit) =
            (HttpMethods.Patch, (fun ctx -> Task.FromResult(handler ctx) :> Task))::handlers

        [<CustomOperation("delete")>]
        member __.Delete(handlers, handler:HttpContext -> Task) =
            (HttpMethods.Delete, handler)::handlers

        member __.Delete(handlers, handler:HttpContext -> Task<'a>) =
            (HttpMethods.Delete, (fun ctx -> handler ctx :> Task))::handlers

        member __.Delete(handlers, handler:HttpContext -> Async<'a>) =
            (HttpMethods.Delete, (fun ctx -> handler ctx |> Async.StartAsTask :> Task))::handlers

        member __.Delete(handlers, handler:HttpContext -> unit) =
            (HttpMethods.Delete, (fun ctx -> Task.FromResult(handler ctx) :> Task))::handlers

        [<CustomOperation("options")>]
        member __.Options(handlers, handler:HttpContext -> Task) =
            (HttpMethods.Options, handler)::handlers

        member __.Options(handlers, handler:HttpContext -> Task<'a>) =
            (HttpMethods.Options, (fun ctx -> handler ctx :> Task))::handlers

        member __.Options(handlers, handler:HttpContext -> Async<'a>) =
            (HttpMethods.Options, (fun ctx -> handler ctx |> Async.StartAsTask :> Task))::handlers

        member __.Options(handlers, handler:HttpContext -> unit) =
            (HttpMethods.Options, (fun ctx -> Task.FromResult(handler ctx) :> Task))::handlers

        [<CustomOperation("trace")>]
        member __.Trace(handlers, handler:HttpContext -> Task) =
            (HttpMethods.Trace, handler)::handlers

        member __.Trace(handlers, handler:HttpContext -> Task<'a>) =
            (HttpMethods.Trace, (fun ctx -> handler ctx :> Task))::handlers

        member __.Trace(handlers, handler:HttpContext -> Async<'a>) =
            (HttpMethods.Trace, (fun ctx -> handler ctx |> Async.StartAsTask :> Task))::handlers

        member __.Trace(handlers, handler:HttpContext -> unit) =
            (HttpMethods.Trace, (fun ctx -> Task.FromResult(handler ctx) :> Task))::handlers

    let resource app name template = ResourceBuilder(app, name, template)
