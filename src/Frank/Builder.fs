namespace Frank

module Builder =

    open System
    open System.Text.RegularExpressions
    open System.Threading.Tasks
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Http
    open Microsoft.AspNetCore.Routing
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.Extensions.FileProviders
    open Microsoft.OpenApi
    open Microsoft.OpenApi.Extensions
    open Microsoft.OpenApi.Models

    /// Defines an HTTP resource with optional Open API documentation.
    [<Struct>]
    type Resource = { Endpoints : Endpoint[] }

    /// Defines the Open API operation response documentation.
    type OperationResponseMetadata =
        { StatusCode : int 
          Description : string
          Body : Type }
        
    /// Defines the Open API operation documentation for a given request handler.
    type OperationMetadata =
        { Description : string option
          Tag : string option
          Body : Type option
          Responses : OperationResponseMetadata[] }
        static member Empty =
            { Description = None; Tag = None; Body = None; Responses = [||] }
    
    module OperationType =
        let ofHttpMethod httpMethod =
            if HttpMethods.IsGet httpMethod then OperationType.Get
            elif HttpMethods.IsPut httpMethod then OperationType.Put
            elif HttpMethods.IsPost httpMethod then OperationType.Post
            elif HttpMethods.IsDelete httpMethod then OperationType.Delete
            elif HttpMethods.IsOptions httpMethod then OperationType.Options
            elif HttpMethods.IsHead httpMethod then OperationType.Head
            elif HttpMethods.IsPatch httpMethod then OperationType.Patch
            elif HttpMethods.IsTrace httpMethod then OperationType.Trace
            else failwithf "Unrecognized Open API HTTP method %s." httpMethod

    // Design Notes:
    // -------------
    // Ultimate goal is to be able to specify OperationMetadata as:
    //
    //     { Description : string option
    //       Tag : string option
    //       Signature : 'TRequest -> Choice<OperationResponseMetadata<'TResponse>,...> }
    //
    // where each 'TRequest is a class or record with attributes on each field
    // indicating `FromPath`, `FromQuery`, `FromBody`, `FromHeader`, etc.
    // and with each `Choice` type representing a different response option.
    // I suspect this will only be achievable with well-defined overloads that
    // map into the above type definitions; otherwise, the generics won't align
    // for all handlers.

    /// Resource specification type for registering routes and corresponding handlers.
    type ResourceSpec =
        { Name : string
          Summary : string option
          Description : string option
          Handlers : (string * RequestDelegate * OperationMetadata option) list }

        static member Empty =
            { Name = Unchecked.defaultof<_>
              Summary = None
              Description = None
              Handlers = [] }

        member spec.Build(routeTemplate) =
            let {Name=name; Summary=summary; Description=description; Handlers=handlers} = spec
            let name = if String.IsNullOrEmpty name then routeTemplate else name
            let routePattern = Patterns.RoutePatternFactory.Parse routeTemplate
            let endpoints =
                [| for httpMethod, handler, metadata in handlers ->
                    let displayName = httpMethod+" "+name
                    let operationId = Regex.Replace(displayName, @"[^A-Za-z0-9_-]", "-").ToLower()
                    let op =
                        match summary, description with
                        | Some s, Some d -> OpenApiOperation(Summary=s, Description=d, OperationId=operationId)
                        | Some s, None -> OpenApiOperation(Summary=s, OperationId=operationId)
                        | None, Some d -> OpenApiOperation(Summary=name, Description=d, OperationId=operationId)
                        | None, None -> OpenApiOperation(OperationId=operationId)
                    // TODO: collect metadata into OpenApiOperation
                    let metadata =
                        EndpointMetadataCollection(
                            HttpMethodMetadata [|httpMethod|],
                            EndpointNameMetadata(name),
                            RouteNameMetadata(name),
                            routePattern,
                            op)
                    RouteEndpoint(
                        requestDelegate=handler,
                        routePattern=routePattern,
                        order=0,
                        metadata=metadata,
                        displayName=displayName) :> Endpoint |]
            { Endpoints = endpoints }

    /// Computation expression builder for generating resource-oriented routes for
    /// a specified URI template.
    [<Sealed>]
    type ResourceBuilder (routeTemplate) =
        static let methodNotAllowed (ctx:HttpContext) =
            ctx.Response.StatusCode <- 405
            Task.FromResult(Some ctx)

        member __.Run(spec:ResourceSpec) : Resource =
            spec.Build(routeTemplate)
        
        member __.Yield(_) = ResourceSpec.Empty

        [<CustomOperation("name")>]
        member __.Name(spec, name) =
            { spec with Name = name }

        [<CustomOperation("summary")>]
        member __.Summary(spec, summary) =
            { spec with Summary = summary }

        [<CustomOperation("description")>]
        member __.Description(spec:ResourceSpec, description) =
            { spec with Description = description }

        static member AddHandler(httpMethod, spec, handler) =
            { spec with Handlers=(httpMethod, handler, None)::spec.Handlers }

        static member AddHandler(httpMethod, spec, operation, handler) =
            { spec with Handlers=(httpMethod, operation, handler)::spec.Handlers }

        static member AddHandler(httpMethod, spec, handler:HttpContext -> Task<'a>) =
            { spec with Handlers=(httpMethod, RequestDelegate(fun ctx -> handler ctx :> Task), None)::spec.Handlers }

        static member AddHandler(httpMethod, spec, handler:(HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
            { spec with Handlers=(httpMethod, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task), None)::spec.Handlers }

        static member AddHandler(httpMethod, spec, handler:HttpContext -> Async<'a>) =
            { spec with Handlers=(httpMethod, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task), None)::spec.Handlers }

        static member AddHandler(httpMethod, spec, handler:HttpContext -> unit) =
            { spec with Handlers=(httpMethod, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task), None)::spec.Handlers }

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

    /// Resource-oriented routing computation expression.
    let resource routeTemplate = ResourceBuilder(routeTemplate)

    [<Sealed>]
    type internal ResourceEndpointDataSource(endpoints:Endpoint[]) =
        inherit EndpointDataSource()

        override __.Endpoints = endpoints :> _
        override __.GetChangeToken() = NullChangeToken.Singleton :> _

    /// Specification for configuring a WebHost.
    type WebHostSpec =
        { Description : string option
          Title : string option
          Version : string
          Host : (IWebHostBuilder -> IWebHostBuilder)
          Middleware : (IApplicationBuilder -> IApplicationBuilder)
          Endpoints : Endpoint[]
          Services : (IServiceCollection -> IServiceCollection) }

        static member Empty =
            { Description = None
              Title = None
              Version = "1.0.0"
              Host = id
              Middleware = id
              Endpoints = [||]
              Services = (fun services ->
                services.AddMvcCore(fun options -> options.ReturnHttpNotAcceptable <- true) |> ignore
                services)
              }

    /// Computation expression builder for configuring a WebHost.
    [<Sealed>]
    type WebHostBuilder (hostBuilder:IWebHostBuilder) =

        member __.Run(spec:WebHostSpec) =
            spec.Host(hostBuilder)
                .ConfigureServices(spec.Services >> ignore)
                .Configure(fun app ->
                    let apiDescription =
                        OpenApiDocument(
                            Info = OpenApiInfo(Version = spec.Version),
                            Servers = ResizeArray<OpenApiServer>(),
                            Paths = OpenApiPaths())
                    spec.Description |> Option.iter (fun desc -> apiDescription.Info.Description <- desc)
                    spec.Title |> Option.iter (fun title -> apiDescription.Info.Title <- title)

                    spec.Endpoints
                    |> Array.groupBy (fun e -> e.Metadata.GetMetadata<Patterns.RoutePattern>())
                    |> Array.iter (fun (route, endpoints) ->
                        let routeTemplate = route.RawText
                        let item = OpenApiPathItem()
                        for endpoint in endpoints do
                            let httpMethod = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()
                            let op = endpoint.Metadata.GetMetadata<OpenApiOperation>()
                            item.AddOperation(OperationType.ofHttpMethod httpMethod.HttpMethods.[0], op)
                        apiDescription.Paths.Add(routeTemplate, item))
                        
                    let openApiHandler = RequestDelegate(fun ctx ->
                        // TODO: check Accept header and negotiate json or yaml.
                        ctx.Response.ContentType <- "application/json; charset=utf-8"
                        apiDescription.SerializeAsJson(ctx.Response.Body, OpenApiSpecVersion.OpenApi3_0)
                        Task.CompletedTask)

                    let openApiEndpoint =
                        RouteEndpoint(
                            requestDelegate=openApiHandler,
                            routePattern=Patterns.RoutePatternFactory.Parse "openapi",
                            order=0,
                            metadata=EndpointMetadataCollection(HttpMethodMetadata [|HttpMethods.Get|]),
                            displayName="OpenAPI") :> Endpoint

                    app.UseRouting()
                       .UseEndpoints(fun endpoints ->
                           let dataSource = ResourceEndpointDataSource([|yield openApiEndpoint; yield! spec.Endpoints|])
                           endpoints.DataSources.Add(dataSource))
                    |> spec.Middleware
                    |> ignore)

        member __.Yield(_) = WebHostSpec.Empty
        
        [<CustomOperation("description")>]
        member __.Description(spec:WebHostSpec, description) =
            if String.IsNullOrEmpty description then spec
            else { spec with Description = Some description }

        [<CustomOperation("title")>]
        member __.Title(spec, title) =
            if String.IsNullOrEmpty title then spec
            else { spec with Title = Some title }

        [<CustomOperation("version")>]
        member __.Version(spec, version) =
            if String.IsNullOrEmpty version then spec
            else { spec with Version = version }

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

        [<CustomOperation("resource")>]
        member __.Resource(spec, resource:Resource) : WebHostSpec =
            { spec with Endpoints = Array.append spec.Endpoints resource.Endpoints }

        [<CustomOperation("service")>]
        member __.Service(spec, f) =
            { spec with Services = spec.Services >> f }

    /// Computation expression for configuring a WebHost.
    let webHost hostBuilder = WebHostBuilder(hostBuilder)
