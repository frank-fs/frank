namespace Frank

module Builder =

    open System
    open System.Threading.Tasks
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Http
    open Microsoft.AspNetCore.Routing
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.Extensions.FileProviders
    open Microsoft.Extensions.Hosting

    [<Struct>]
    type Resource = { Endpoints : Endpoint[] }

    type ResourceSpec =
        { Name : string; Handlers : (string * RequestDelegate) list }
        static member Empty = { Name = Unchecked.defaultof<_>; Handlers = [] }
        member spec.Build(routeTemplate) =
            let {Name=name; Handlers=handlers} = spec
            let routePattern = Patterns.RoutePatternFactory.Parse routeTemplate
            let endpoints =
                [| for httpMethod, handler in handlers ->
                    let displayName = httpMethod+" "+(if String.IsNullOrEmpty name then routeTemplate else name)
                    let metadata = EndpointMetadataCollection(HttpMethodMetadata [|httpMethod|])
                    RouteEndpoint(
                        requestDelegate=handler,
                        routePattern=routePattern,
                        order=0,
                        metadata=metadata,
                        displayName=displayName) :> Endpoint |]
            { Endpoints = endpoints }

    [<Sealed>]
    type ResourceBuilder (routeTemplate) =
        static let methodNotAllowed (ctx:HttpContext) =
            ctx.Response.StatusCode <- 405
            Task.FromResult(Some ctx)

        member __.Run(spec:ResourceSpec) : Resource =
            spec.Build(routeTemplate)
        
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

    let resource routeTemplate = ResourceBuilder(routeTemplate)

    [<Sealed>]
    type internal ResourceEndpointDataSource(endpoints:Endpoint[]) =
        inherit EndpointDataSource()

        override __.Endpoints = endpoints :> _
        override __.GetChangeToken() = NullChangeToken.Singleton :> _

    type WebHostSpec =
        { Host: (IWebHostBuilder -> IWebHostBuilder)
          Middleware: (IApplicationBuilder -> IApplicationBuilder)
          Endpoints: Endpoint[]
          Services: (IServiceCollection -> IServiceCollection)
          UseDefaults: bool }
        static member Empty =
            { Host=id
              Middleware=id
              Endpoints=[||]
              Services=(fun services ->
                services.AddMvcCore(fun options -> options.ReturnHttpNotAcceptable <- true) |> ignore
                services)
              UseDefaults=false }

    [<Sealed>]
    type WebHostBuilder (args) =

        member __.Run(spec:WebHostSpec) =
            let builder = Host.CreateDefaultBuilder(args)
            let config = Action<_>(fun webBuilder ->
                spec.Host(webBuilder)
                    .ConfigureServices(spec.Services >> ignore)
                    .Configure(fun app ->
                        app.UseRouting()
                           .UseEndpoints(fun endpoints ->
                               let dataSource = ResourceEndpointDataSource(spec.Endpoints)
                               endpoints.DataSources.Add(dataSource))
                        |> spec.Middleware
                        |> ignore)
                    |> ignore)
            let configured =
                if spec.UseDefaults then
                    builder.ConfigureWebHostDefaults(config)
                else
                    builder.ConfigureWebHost(config)
            configured.Build().Run()

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
            __.PlugWhen(spec, not << cond, f)

        [<CustomOperation("resource")>]
        member __.Resource(spec, resource:Resource) : WebHostSpec =
            { spec with Endpoints = Array.append spec.Endpoints resource.Endpoints }

        [<CustomOperation("service")>]
        member __.Service(spec, f) =
            { spec with Services = spec.Services >> f }
        
        [<CustomOperation("useDefaults")>]
        member __.UseDefaults(spec) =
            { spec with UseDefaults = true }

    let webHost args = WebHostBuilder(args)
