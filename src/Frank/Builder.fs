namespace Frank

module Builder =

    open System
    open System.Collections.Generic
    open System.Threading.Tasks
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Http
    open Microsoft.AspNetCore.Routing
    open Microsoft.AspNetCore.Routing.Constraints
    open Microsoft.Extensions.DependencyInjection

    let private methodNotAllowed =
        RequestDelegate(fun ctx ->
            ctx.Response.StatusCode <- 405
            upcast Task.FromResult())
        |> RouteHandler

    type ResourceSpec =
        { Name : string; Handlers : (string * RequestDelegate) list }
        static member Empty = { Name = Unchecked.defaultof<_>; Handlers = [] }
        member spec.Build(serviceProvider:IServiceProvider, routeTemplate) =
            let inlineConstraintResolver = serviceProvider.GetRequiredService<IInlineConstraintResolver>()
            // Create routes for current resource, similar to approach used by RouteBuilder.
            let routes = RouteCollection()
            for httpMethod, handler in spec.Handlers do
                let route =
                    Route(
                        target=RouteHandler handler,
                        routeTemplate=routeTemplate,
                        defaults=null,
                        constraints=dict [|"httpMethod", box(HttpMethodRouteConstraint([|httpMethod|]))|],
                        dataTokens=null,
                        inlineConstraintResolver=inlineConstraintResolver)
                routes.Add(route)
            let defaultRoute =
                Route(
                    target=methodNotAllowed,
                    routeName=(if String.IsNullOrEmpty spec.Name then routeTemplate else spec.Name),
                    routeTemplate=routeTemplate,
                    defaults=RouteValueDictionary(null),
                    constraints=(RouteValueDictionary(null) :> IDictionary<string, obj>),
                    dataTokens=RouteValueDictionary(null),
                    inlineConstraintResolver=inlineConstraintResolver)
            routes.Add(defaultRoute)
            routes :> IRouter

    [<Sealed>]
    type ResourceBuilder (routeTemplate, applicationBuilder:IApplicationBuilder) =
        let methodNotAllowed (ctx:HttpContext) =
            ctx.Response.StatusCode <- 405
            ctx.Response
               .WriteAsync("Method not allowed")
               .ContinueWith(fun _ -> Some ctx)

        member __.Run(spec:ResourceSpec) =
            spec.Build(applicationBuilder.ApplicationServices, routeTemplate)
        
        member __.Yield(_) = ResourceSpec.Empty

        [<CustomOperation("name")>]
        member __.Name(spec, name) = { spec with Name = name }

        member __.AddHandler(httpMethod, spec, handler) =
            { spec with Handlers=(httpMethod, handler)::spec.Handlers}

        [<CustomOperation("connect")>]
        member this.Connect(spec, handler:RequestDelegate) =
            this.AddHandler(HttpMethods.Connect, spec, handler)

        member this.Connect(spec, handler:HttpContext -> Task) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate handler)

        member this.Connect(spec, handler:HttpContext -> Task<'a>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler ctx :> Task))

        member this.Connect(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))

        member this.Connect(spec, handler:HttpContext -> Async<'a>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))

        member this.Connect(spec, handler:HttpContext -> unit) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))

        [<CustomOperation("delete")>]
        member this.Delete(spec, handler:RequestDelegate) =
            this.AddHandler(HttpMethods.Delete, spec, handler)

        member this.Delete(spec, handler:HttpContext -> Task<'a>) =
            this.AddHandler(HttpMethods.Delete, spec, RequestDelegate(fun ctx -> handler ctx :> Task))

        member this.Delete(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))

        member this.Delete(spec, handler:HttpContext -> Async<'a>) =
            this.AddHandler(HttpMethods.Delete, spec, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))

        member this.Delete(spec, handler:HttpContext -> unit) =
            this.AddHandler(HttpMethods.Delete, spec, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))

        [<CustomOperation("get")>]
        member this.Get(spec, handler:RequestDelegate) =
            this.AddHandler(HttpMethods.Get, spec, handler)

        member this.Get(spec, handler:HttpContext -> Task<'a>) =
            this.AddHandler(HttpMethods.Get, spec, RequestDelegate(fun ctx -> handler ctx :> Task))

        member this.Get(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))

        member this.Get(spec, handler:HttpContext -> Async<'a>) =
            this.AddHandler(HttpMethods.Get, spec, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))

        member this.Get(spec, handler:HttpContext -> unit) =
            this.AddHandler(HttpMethods.Get, spec, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))

        [<CustomOperation("head")>]
        member this.Head(spec, handler:RequestDelegate) =
            this.AddHandler(HttpMethods.Head, spec, handler)

        member this.Head(spec, handler:HttpContext -> Task<'a>) =
            this.AddHandler(HttpMethods.Head, spec, RequestDelegate(fun ctx -> handler ctx :> Task))

        member this.Head(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))

        member this.Head(spec, handler:HttpContext -> Async<'a>) =
            this.AddHandler(HttpMethods.Head, spec, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))

        member this.Head(spec, handler:HttpContext -> unit) =
            this.AddHandler(HttpMethods.Head, spec, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))

        [<CustomOperation("options")>]
        member this.Options(spec, handler:RequestDelegate) =
            this.AddHandler(HttpMethods.Options, spec, handler)

        member this.Options(spec, handler:HttpContext -> Task<'a>) =
            this.AddHandler(HttpMethods.Options, spec, RequestDelegate(fun ctx -> handler ctx :> Task))

        member this.Options(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))

        member this.Options(spec, handler:HttpContext -> Async<'a>) =
            this.AddHandler(HttpMethods.Options, spec, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))

        member this.Options(spec, handler:HttpContext -> unit) =
            this.AddHandler(HttpMethods.Options, spec, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))

        [<CustomOperation("patch")>]
        member this.Patch(spec, handler:RequestDelegate) =
            this.AddHandler(HttpMethods.Patch, spec, handler)

        member this.Patch(spec, handler:HttpContext -> Task<'a>) =
            this.AddHandler(HttpMethods.Patch, spec, RequestDelegate(fun ctx -> handler ctx :> Task))

        member this.Patch(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))

        member this.Patch(spec, handler:HttpContext -> Async<'a>) =
            this.AddHandler(HttpMethods.Patch, spec, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))

        member this.Patch(spec, handler:HttpContext -> unit) =
            this.AddHandler(HttpMethods.Patch, spec, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))

        [<CustomOperation("post")>]
        member this.Post(spec, handler:RequestDelegate) =
            this.AddHandler(HttpMethods.Post, spec, handler)

        member this.Post(spec, handler:HttpContext -> Task<'a>) =
            this.AddHandler(HttpMethods.Post, spec, RequestDelegate(fun ctx -> handler ctx :> Task))

        member this.Post(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))

        member this.Post(spec, handler:HttpContext -> Async<'a>) =
            this.AddHandler(HttpMethods.Post, spec, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))

        member this.Post(spec, handler:HttpContext -> unit) =
            this.AddHandler(HttpMethods.Post, spec, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))

        [<CustomOperation("put")>]
        member this.Put(spec, handler:RequestDelegate) =
            this.AddHandler(HttpMethods.Put, spec, handler)

        member this.Put(spec, handler:HttpContext -> Task<'a>) =
            this.AddHandler(HttpMethods.Put, spec, RequestDelegate(fun ctx -> handler ctx :> Task))

        member this.Put(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))

        member this.Put(spec, handler:HttpContext -> Async<'a>) =
            this.AddHandler(HttpMethods.Put, spec, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))

        member this.Put(spec, handler:HttpContext -> unit) =
            this.AddHandler(HttpMethods.Put, spec, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))

        [<CustomOperation("trace")>]
        member this.Trace(spec, handler:RequestDelegate) =
            this.AddHandler(HttpMethods.Trace, spec, handler)

        member this.Trace(spec, handler:HttpContext -> Task<'a>) =
            this.AddHandler(HttpMethods.Trace, spec, RequestDelegate(fun ctx -> handler ctx :> Task))

        member this.Trace(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            this.AddHandler(HttpMethods.Connect, spec, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))

        member this.Trace(spec, handler:HttpContext -> Async<'a>) =
            this.AddHandler(HttpMethods.Trace, spec, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))

        member this.Trace(spec, handler:HttpContext -> unit) =
            this.AddHandler(HttpMethods.Trace, spec, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))

    let resource routeTemplate applicationBuilder = ResourceBuilder(routeTemplate, applicationBuilder)

    type WebHostSpec =
        { Middleware : (IApplicationBuilder -> IApplicationBuilder)
          Routes : (IApplicationBuilder -> IRouter) list
          Services : (IServiceCollection -> IServiceCollection) }
        static member Empty =
            { Middleware = id
              Routes = []
              Services = (fun services -> services.AddRouting()) }

    [<Sealed>]
    type WebHostBuilder (hostBuilder:IWebHostBuilder) =

        member __.Run(spec:WebHostSpec) =
            hostBuilder
                .ConfigureServices(spec.Services >> ignore)
                .Configure(fun app ->
                    let routes = RouteCollection()
                    for router in List.rev spec.Routes do
                        routes.Add(router app)
                    app.UseRouter(routes)
                    |> spec.Middleware
                    |> ignore)

        member __.Yield(_) = WebHostSpec.Empty

        [<CustomOperation("plug")>]
        member __.Plug(spec, f) =
            { spec with Middleware = spec.Middleware >> f }

        [<CustomOperation("plugWhen")>]
        member __.PlugWhen(spec, cond, f) =
            { spec with
                Middleware = fun app ->
                    if cond app then
                        f(spec.Middleware(app))
                    else spec.Middleware(app) }

        [<CustomOperation("plugWhenNot")>]
        member __.PlugWhenNot(spec, cond, f) =
            { spec with
                Middleware = fun app ->
                    if not(cond app) then
                        f(spec.Middleware(app))
                    else spec.Middleware(app) }

        [<CustomOperation("route")>]
        member __.Route(spec, router:IApplicationBuilder -> IRouter) =
            { spec with Routes = router::spec.Routes }

        [<CustomOperation("service")>]
        member __.Service(spec, f) =
            { spec with Services = spec.Services >> f }

    let webHost hostBuilder = WebHostBuilder(hostBuilder)
