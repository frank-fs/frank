namespace Test

open System.Collections.Generic
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

module ResourceHandler =
    let methodNotAllowed =
        RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 405
            upcast Task.FromResult())

type ResourceHandler (handlers:IDictionary<string, RequestDelegate>) =
    new (handlers) =
        ResourceHandler(dict [|for m, h in handlers -> m, RequestDelegate h|])

    member __.GetRequestHandler(httpContext:HttpContext, routeData:RouteData) =
        match handlers.TryGetValue(httpContext.Request.Method) with
        | true, handler -> handler
        | _ -> ResourceHandler.methodNotAllowed

    interface IRouteHandler with
        member this.GetRequestHandler(httpContext, routeData) =
            this.GetRequestHandler(httpContext, routeData)
    
    interface IRouter with
        member __.GetVirtualPath(_:VirtualPathContext) = Unchecked.defaultof<VirtualPathData>
        member this.RouteAsync(context:RouteContext) =
            context.Handler <- this.GetRequestHandler(context.HttpContext, context.RouteData)
            Task.CompletedTask
    
module ApplicationBuilderExtensions =
    type IApplicationBuilder with
        member app.UseResource(name, template, handlers:seq<string * (HttpContext -> Task)>) =
            let resourceHandler = ResourceHandler(handlers=handlers)
            let resourceBuilder = RouteBuilder(app, resourceHandler)
            resourceBuilder.MapRoute(name=name, template=template) |> ignore
            let resourceRoutes = resourceBuilder.Build()
            app.UseRouter(resourceRoutes) |> ignore
