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

    let private methodNotAllowedHandler =
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
                    target=methodNotAllowedHandler,
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
        static let methodNotAllowed (ctx:HttpContext) =
            ctx.Response.StatusCode <- 405
            Task.FromResult(Some ctx)

        member __.Run(spec:ResourceSpec) =
            spec.Build(applicationBuilder.ApplicationServices, routeTemplate)
        
        member __.Yield(_) = ResourceSpec.Empty

        [<CustomOperation("name")>]
        member __.Name(spec, name) = { spec with Name = name }

        static member AddHandler(httpMethod, spec, handler) =
            { spec with Handlers=(httpMethod, handler)::spec.Handlers }

        static member AddHandler(httpMethod, spec, handler:HttpContext -> Task<'a>) =
            { spec with Handlers=(httpMethod, RequestDelegate(fun ctx -> handler ctx :> Task))::spec.Handlers }

        static member AddHandler(httpMethod, spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            { spec with Handlers=(httpMethod, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))::spec.Handlers }

        static member AddHandler(httpMethod, spec, handler:HttpContext -> Async<'a>) =
            { spec with Handlers=(httpMethod, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))::spec.Handlers }

        static member AddHandler(httpMethod, spec, handler:HttpContext -> unit) =
            { spec with Handlers=(httpMethod, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))::spec.Handlers }

        [<CustomOperation("connect")>]
        member __.Connect(spec, handler:RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

        member __.Connect(spec, handler:HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

        member __.Connect(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

        member __.Connect(spec, handler:HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

        member __.Connect(spec, handler:HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

        [<CustomOperation("delete")>]
        member __.Delete(spec, handler:RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

        member __.Delete(spec, handler:HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

        member __.Delete(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

        member __.Delete(spec, handler:HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

        member __.Delete(spec, handler:HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

        [<CustomOperation("get")>]
        member __.Get(spec, handler:RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        member __.Get(spec, handler:HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        member __.Get(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        member __.Get(spec, handler:HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        member __.Get(spec, handler:HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        [<CustomOperation("head")>]
        member __.Head(spec, handler:RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

        member __.Head(spec, handler:HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

        member __.Head(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

        member __.Head(spec, handler:HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

        member __.Head(spec, handler:HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

        [<CustomOperation("options")>]
        member __.Options(spec, handler:RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

        member __.Options(spec, handler:HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

        member __.Options(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

        member __.Options(spec, handler:HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

        member __.Options(spec, handler:HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

        [<CustomOperation("patch")>]
        member __.Patch(spec, handler:RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

        member __.Patch(spec, handler:HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

        member __.Patch(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

        member __.Patch(spec, handler:HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

        member __.Patch(spec, handler:HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

        [<CustomOperation("post")>]
        member __.Post(spec, handler:RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

        member __.Post(spec, handler:HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

        member __.Post(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

        member __.Post(spec, handler:HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

        member __.Post(spec, handler:HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

        [<CustomOperation("put")>]
        member __.Put(spec, handler:RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

        member __.Put(spec, handler:HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

        member __.Put(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

        member __.Put(spec, handler:HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

        member __.Put(spec, handler:HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

        [<CustomOperation("trace")>]
        member __.Trace(spec, handler:RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

        member __.Trace(spec, handler:HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

        member __.Trace(spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

        member __.Trace(spec, handler:HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

        member __.Trace(spec, handler:HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

    let resource routeTemplate applicationBuilder = ResourceBuilder(routeTemplate, applicationBuilder)

    type WebHostSpec =
        { Host : (IWebHostBuilder -> IWebHostBuilder)
          Middleware : (IApplicationBuilder -> IApplicationBuilder)
          Routes : (IApplicationBuilder -> IRouter) list
          Services : (IServiceCollection -> IServiceCollection) }
        static member Empty =
            { Host = id
              Middleware = id
              Routes = []
              Services = (fun services -> services.AddRouting()) }

    [<Sealed>]
    type WebHostBuilder (hostBuilder:IWebHostBuilder) =

        member __.Run(spec:WebHostSpec) =
            spec.Host(hostBuilder)
                .ConfigureServices(spec.Services >> ignore)
                .Configure(fun app ->
                    let routes = RouteCollection()
                    for router in List.rev spec.Routes do
                        routes.Add(router app)
                    app.UseRouter(routes)
                    |> spec.Middleware
                    |> ignore)

        member __.Yield(_) = WebHostSpec.Empty

        [<CustomOperation("configure")>]
        member __.Configure(spec, f) =
            { spec with Host = spec.Host >> f }

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
