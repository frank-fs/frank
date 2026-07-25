namespace Frank

module Builder =

    open System
    open System.IO
    open System.Threading
    open System.Threading.Tasks
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Http
    open Microsoft.AspNetCore.Routing
    open Microsoft.Extensions.Caching.Memory
    open Microsoft.Extensions.DependencyInjection
    open Microsoft.Extensions.FileProviders
    open Microsoft.Extensions.Hosting

    let private rootName = "Root"

    let private isRouteParam (seg: string) =
        seg.StartsWith('{') && seg.EndsWith('}')

    let private titleCase (s: string) =
        if String.IsNullOrEmpty s then
            s
        else
            string (Char.ToUpperInvariant s[0]) + s.Substring(1).ToLowerInvariant()

    let private singularize (s: string) =
        if
            s.Length > 1
            && s.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            && not (s.EndsWith("ss", StringComparison.OrdinalIgnoreCase))
        then
            s.Substring(0, s.Length - 1)
        else
            s

    /// Discards every byte written to it, retaining only a running count — the zero-buffer
    /// sink `suppressBodyForHead` swaps in for `ctx.Response.Body` on a HEAD request, so the
    /// wrapped GET handler's body writes cost no memory (no backing byte array, unlike a
    /// MemoryStream). Write-only and forward-only: Read/Seek/SetLength/Position-set are
    /// unsupported, matching CanRead=false/CanSeek=false.
    type private CountingSinkStream() =
        inherit Stream()
        let mutable written = 0L

        member _.WrittenLength = written

        override _.CanRead = false
        override _.CanSeek = false
        override _.CanWrite = true
        override _.Length = written

        override _.Position
            with get () = written
            and set _ = raise (NotSupportedException "CountingSinkStream is forward-only")

        override _.Flush() = ()
        override _.FlushAsync(_cancellationToken: CancellationToken) = Task.CompletedTask

        override _.Read(_buffer: byte[], _offset: int, _count: int) =
            raise (NotSupportedException "CountingSinkStream is write-only")

        override _.Seek(_offset: int64, _origin: SeekOrigin) : int64 =
            raise (NotSupportedException "CountingSinkStream is forward-only")

        override _.SetLength(_value: int64) =
            raise (NotSupportedException "CountingSinkStream is forward-only")

        override _.Write(_buffer: byte[], _offset: int, count: int) = written <- written + int64 count

        override _.WriteAsync(_buffer: byte[], _offset: int, count: int, _cancellationToken: CancellationToken) : Task =
            written <- written + int64 count
            Task.CompletedTask

        override _.WriteAsync(buffer: ReadOnlyMemory<byte>, _cancellationToken: CancellationToken) : ValueTask =
            written <- written + int64 buffer.Length
            ValueTask.CompletedTask

    /// When a GET endpoint also answers HEAD (RFC 7231 §7.4.1), the underlying handler still
    /// runs so it computes the same headers (ETag, Content-Type, Content-Length) GET would —
    /// but the body itself must never reach the client. ASP.NET Core does not suppress this
    /// automatically for an arbitrary RequestDelegate (only individual middleware, e.g.
    /// StaticFileMiddleware, special-cases HEAD this way internally); this wrapper applies
    /// that same swap once at the source so handler authors never need to check
    /// ctx.Request.Method themselves. The discard target is a CountingSinkStream (above),
    /// never a MemoryStream — the handler's body bytes are counted, not retained.
    let private suppressBodyForHead (inner: RequestDelegate) : RequestDelegate =
        RequestDelegate(fun ctx ->
            task {
                if not (HttpMethods.IsHead ctx.Request.Method) then
                    do! inner.Invoke ctx
                else
                    let originalBody = ctx.Response.Body
                    use discardedBody = new CountingSinkStream()
                    ctx.Response.Body <- discardedBody

                    try
                        do! inner.Invoke ctx
                    finally
                        ctx.Response.Body <- originalBody

                    // HasStarted guard: a handler that already flushed headers before this
                    // wrapper regains control (e.g. streamed/chunked output) cannot have its
                    // Content-Length backfilled after the fact — ASP.NET Core throws
                    // InvalidOperationException on any header mutation once the response has
                    // started, so the backfill is skipped rather than crashing a HEAD request
                    // whose GET counterpart streams.
                    if
                        not ctx.Response.HasStarted
                        && not (ctx.Response.Headers.ContainsKey "Content-Length")
                    then
                        ctx.Response.ContentLength <- discardedBody.WrittenLength
            }
            :> Task)

    let private inferNameFromRoute (routeTemplate: string) =
        let trimmed = routeTemplate.TrimStart('/')

        if String.IsNullOrEmpty trimmed then
            rootName
        else
            let segments = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries)

            // Singularize collection segments that precede a path parameter,
            // so /users/{id} becomes "User" not "Users"
            let processed =
                segments
                |> Array.indexed
                |> Array.collect (fun (i, seg) ->
                    if isRouteParam seg then
                        Array.empty
                    else
                        let nextIsParam = i + 1 < segments.Length && isRouteParam segments[i + 1]

                        let normalized = if nextIsParam then singularize seg else seg

                        normalized.Split([| '-'; '_' |], StringSplitOptions.RemoveEmptyEntries)
                        |> Array.map titleCase)

            if Array.isEmpty processed then
                rootName
            else
                String.Join(" ", processed)

    [<Struct>]
    type Resource = { Endpoints: Endpoint[] }

    /// Media type metadata for HTTP discovery (OPTIONS + Link headers).
    /// Extensions add instances to endpoint metadata to advertise supported content types.
    [<Struct>]
    type DiscoveryMediaType =
        {
            /// The content type string (e.g., "application/ld+json", "text/turtle").
            MediaType: string
            /// The link relation type for Link header generation (e.g., "describedby").
            Rel: string
        }

    /// Metadata contributed by extension packages for JSON Home document generation.
    /// Register via DI; Frank.Discovery reads it at startup to build the home document.
    type JsonHomeMetadata =
        {
            /// API title (e.g., from OpenAPI info)
            Title: string option
            /// URL for API documentation (e.g., Scalar UI at /scalar/v1)
            DocsUrl: string option
            /// Base URI for ALPS profiles (e.g., "http://example.com/alps/games").
            /// Used to build link relation URIs: {AlpsBaseUri}#{resourceSlug}
            AlpsBaseUri: string option
            /// ALPS descriptor URIs keyed by (resourceSlug, descriptorId).
            /// Enables semantic hrefVars in JSON Home.
            AlpsDescriptors: Map<string, Map<string, string>> option
        }

        static member Empty =
            { Title = None
              DocsUrl = None
              AlpsBaseUri = None
              AlpsDescriptors = None }

    /// Marker metadata indicating a resource is an entry point for the JSON Home document.
    /// When any endpoints carry this metadata, only those endpoints appear in the home document.
    /// When no endpoints carry it, all non-internal endpoints appear (backward compat fallback).
    /// Not a struct because EndpointMetadataCollection.GetMetadata<T> requires reference semantics.
    type EntryPointMetadata = { IsEntryPoint: bool }

    type ResourceSpec =
        { Name: string option
          Handlers: (string * RequestDelegate) list
          Metadata: (EndpointBuilder -> unit) list }

        static member Empty =
            { Name = None
              Handlers = []
              Metadata = [] }

        member spec.Build(routeTemplate) =
            let { Name = name
                  Handlers = handlers
                  Metadata = metadata } =
                spec

            let resolvedName =
                match name with
                | Some n -> n
                | None -> inferNameFromRoute routeTemplate

            let routePattern = Patterns.RoutePatternFactory.Parse routeTemplate

            // RFC 7231 §7.4.1: HEAD is GET without a body. Fold HEAD into the GET endpoint's
            // registered methods (unless the spec already declares an explicit HEAD handler)
            // so the REGISTERED method set matches what OPTIONS/JSON-Home advertise — one source
            // of truth, not a middleware-side patch (see DiscoveryMiddleware.advertisedMethods).
            let hasExplicitHead =
                handlers |> List.exists (fun (httpMethod, _) -> HttpMethods.IsHead httpMethod)

            let endpoints =
                [| for httpMethod, handler in handlers ->
                       let displayName = httpMethod + " " + resolvedName
                       let foldsHead = HttpMethods.IsGet httpMethod && not hasExplicitHead

                       let registeredMethods =
                           if foldsHead then
                               [| httpMethod; HttpMethods.Head |]
                           else
                               [| httpMethod |]

                       let effectiveHandler = if foldsHead then suppressBodyForHead handler else handler

                       let builder = RouteEndpointBuilder(effectiveHandler, routePattern, 0)
                       builder.DisplayName <- displayName
                       builder.Metadata.Add(HttpMethodMetadata registeredMethods)
                       // Metadata reflects the ORIGINAL handler (not the HEAD body-suppression
                       // wrapper), so ApiExplorer/OpenAPI/etc. still see the real handler's
                       // MethodInfo — the wrapper is purely a runtime body-write concern.
                       builder.Metadata.Add(handler.Method)

                       for convention in metadata do
                           convention builder

                       builder.Build() |]

            { Endpoints = endpoints }

    [<Sealed>]
    type ResourceBuilder(routeTemplate) =
        static let methodNotAllowed (ctx: HttpContext) =
            ctx.Response.StatusCode <- 405
            Task.FromResult(Some ctx)

        member __.Run(spec: ResourceSpec) : Resource = spec.Build(routeTemplate)

        member __.Yield(_) = ResourceSpec.Empty

        [<CustomOperation("name")>]
        member __.Name(spec, name) = { spec with Name = Some name }

        /// Marks this resource as a JSON Home entry point.
        /// Only entry-point resources appear in the home document when any are designated.
        [<CustomOperation("entryPoint")>]
        member __.EntryPoint(spec) =
            ResourceBuilder.AddMetadata(spec, fun b -> b.Metadata.Add({ IsEntryPoint = true }: EntryPointMetadata))

        static member AddMetadata(spec: ResourceSpec, convention: EndpointBuilder -> unit) : ResourceSpec =
            { spec with
                Metadata = spec.Metadata @ [ convention ] }

        static member AddHandler(httpMethod, spec, handler) =
            { spec with
                Handlers = (httpMethod, handler) :: spec.Handlers }

        static member AddHandler(httpMethod, spec, handler: HttpContext -> Task<'a>) =
            { spec with
                Handlers = (httpMethod, RequestDelegate(fun ctx -> handler ctx :> Task)) :: spec.Handlers }

        static member AddHandler
            (
                httpMethod,
                spec,
                handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>
            ) =
            { spec with
                Handlers =
                    (httpMethod, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task))
                    :: spec.Handlers }

        static member AddHandler(httpMethod, spec, handler: HttpContext -> Async<'a>) =
            { spec with
                Handlers =
                    (httpMethod, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task))
                    :: spec.Handlers }

        static member AddHandler(httpMethod, spec, handler: HttpContext -> unit) =
            { spec with
                Handlers =
                    (httpMethod, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task))
                    :: spec.Handlers }

        [<CustomOperation("connect")>]
        member __.Connect(spec, handler: RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

        member __.Connect(spec, handler: HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

        member __.Connect
            (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
            =
            ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

        member __.Connect(spec, handler: HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

        member __.Connect(spec, handler: HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

        [<CustomOperation("delete")>]
        member __.Delete(spec, handler: RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

        member __.Delete(spec, handler: HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

        member __.Delete
            (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
            =
            ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

        member __.Delete(spec, handler: HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

        member __.Delete(spec, handler: HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

        [<CustomOperation("get")>]
        member __.Get(spec, handler: RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        member __.Get(spec, handler: HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        member __.Get
            (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
            =
            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        member __.Get(spec, handler: HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        member __.Get(spec, handler: HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

        [<CustomOperation("head")>]
        member __.Head(spec, handler: RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

        member __.Head(spec, handler: HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

        member __.Head
            (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
            =
            ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

        member __.Head(spec, handler: HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

        member __.Head(spec, handler: HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

        [<CustomOperation("options")>]
        member __.Options(spec, handler: RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

        member __.Options(spec, handler: HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

        member __.Options
            (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
            =
            ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

        member __.Options(spec, handler: HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

        member __.Options(spec, handler: HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

        [<CustomOperation("patch")>]
        member __.Patch(spec, handler: RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

        member __.Patch(spec, handler: HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

        member __.Patch
            (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
            =
            ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

        member __.Patch(spec, handler: HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

        member __.Patch(spec, handler: HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

        [<CustomOperation("post")>]
        member __.Post(spec, handler: RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

        member __.Post(spec, handler: HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

        member __.Post
            (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
            =
            ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

        member __.Post(spec, handler: HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

        member __.Post(spec, handler: HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

        [<CustomOperation("put")>]
        member __.Put(spec, handler: RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

        member __.Put(spec, handler: HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

        member __.Put
            (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
            =
            ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

        member __.Put(spec, handler: HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

        member __.Put(spec, handler: HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

        [<CustomOperation("trace")>]
        member __.Trace(spec, handler: RequestDelegate) =
            ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

        member __.Trace(spec, handler: HttpContext -> Task<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

        member __.Trace
            (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
            =
            ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

        member __.Trace(spec, handler: HttpContext -> Async<'a>) =
            ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

        member __.Trace(spec, handler: HttpContext -> unit) =
            ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

    let resource routeTemplate = ResourceBuilder(routeTemplate)

    /// The EndpointDataSource wrapping Frank's own composed Endpoint[] — the SAME array
    /// spec.Endpoints holds after full webHost CE composition. WebHostBuilder.Run registers
    /// this exact instance as a narrowly-typed DI singleton (#411), separately from the
    /// generic EndpointDataSource it also adds to IEndpointRouteBuilder.DataSources.
    /// Extension packages (e.g. Frank.Discovery's DiscoveryMiddleware) constructor-inject
    /// this concrete type when they need to read ONLY Frank-declared endpoints — never any
    /// non-Frank endpoint that might share the generic composite EndpointDataSource. The
    /// constructor is internal — only WebHostBuilder.Run constructs one; the public sealed
    /// type with an internal constructor already prevents external code from spoofing an
    /// instance into DI.
    [<Sealed>]
    type ResourceEndpointDataSource internal (endpoints: Endpoint[]) =
        inherit EndpointDataSource()

        override __.Endpoints = endpoints :> _
        override __.GetChangeToken() = NullChangeToken.Singleton :> _

    /// #468: shared SizeLimit for every keyed IMemoryCache region registered by
    /// registerBoundedMemoryCaches below — mirrors the prior hand-rolled cache's
    /// DefaultCapacity value (1000) exactly, same worst-case-retained-memory ceiling per
    /// cache, new mechanism. Asserted against by Discovery/Validation/LinkedData/Frank's own
    /// test suites (the capacity-constant name/location AT7 requires tests to reference).
    [<Literal>]
    let CacheCapacity = 1000

    let private newBoundedMemoryCache () : IMemoryCache =
        new MemoryCache(MemoryCacheOptions(SizeLimit = System.Nullable(int64 CacheCapacity))) :> IMemoryCache

    /// #468: registers the four independently-budgeted IMemoryCache regions consumed by
    /// DiscoveryMiddleware's resolvedAlpsCache/resolvedHomeResourcesCache,
    /// ValidationMiddleware's hostRelativeShapesCache, and LinkedDataMiddleware's
    /// staticBodyCache via keyed DI. Each key gets its OWN MemoryCache instance (own
    /// SizeLimit accounting) — a flood against one must never evict another's entries, a
    /// stronger guarantee than grouping by middleware (Discovery's two caches don't share a
    /// budget either). Called from BOTH WebHostBuilder.Run TFM branches below AND from test
    /// helpers that mirror Run's own wiring sequence on a TestServer (e.g.
    /// Frank.Discovery.Tests.runWebHostSpecOnTestServer) so the two can never drift
    /// (Constitution #8: no duplicated logic).
    let registerBoundedMemoryCaches (services: IServiceCollection) : IServiceCollection =
        services.AddKeyedSingleton<IMemoryCache>("discovery:alps", (fun _ _ -> newBoundedMemoryCache ()))
        |> ignore

        services.AddKeyedSingleton<IMemoryCache>("discovery:home", (fun _ _ -> newBoundedMemoryCache ()))
        |> ignore

        services.AddKeyedSingleton<IMemoryCache>("validation:shapes", (fun _ _ -> newBoundedMemoryCache ()))
        |> ignore

        services.AddKeyedSingleton<IMemoryCache>("linkeddata:staticbody", (fun _ _ -> newBoundedMemoryCache ()))
        |> ignore

        services

    type WebHostSpec =
        { Host: (IWebHostBuilder -> IWebHostBuilder)
          BeforeRoutingMiddleware: (IApplicationBuilder -> IApplicationBuilder)
          Middleware: (IApplicationBuilder -> IApplicationBuilder)
          Endpoints: Endpoint[]
          Services: (IServiceCollection -> IServiceCollection)
          UseDefaults: bool }

        static member Empty =
            { Host = id
              BeforeRoutingMiddleware = id
              Middleware = id
              Endpoints = [||]
              Services =
                (fun services ->
                    services.AddMvcCore(fun options -> options.ReturnHttpNotAcceptable <- true)
                    |> ignore

                    services)
              UseDefaults = false }

    /// Validates composed endpoints before the app starts serving.
    /// Implementations throw <c>invalidOp</c> (InvalidOperationException) to signal a
    /// validation failure that should fail the build. Register via DI as IStartupValidator.
    type IStartupValidator =
        abstract member Validate: EndpointDataSource -> unit

    let private collectValidationErrors (services: IServiceProvider) (dataSource: EndpointDataSource) : string list =
        [ for v in services.GetServices<IStartupValidator>() do
              try
                  v.Validate dataSource
              with :? InvalidOperationException as ex ->
                  yield ex.Message ]

    let private runValidateMode (services: IServiceProvider) (dataSource: EndpointDataSource) : unit =
        let validators = services.GetServices<IStartupValidator>() |> Seq.toList

        if validators.IsEmpty then
            ()
        else
            let errors = collectValidationErrors services dataSource

            if errors.IsEmpty then
                eprintfn "FRANK_VALIDATE: OK"
                Environment.Exit 0
            else
                for e in errors do
                    eprintfn "FRANK_VALIDATE error: %s" e

                Environment.Exit 1

    [<Sealed>]
    type WebHostBuilder(args: string[]) =

        member __.Run(spec: WebHostSpec) =
#if NET10_0_OR_GREATER
            let builder =
                if spec.UseDefaults then
                    WebApplication.CreateBuilder(args)
                else
                    WebApplication.CreateSlimBuilder(args)

            // #411: built and registered as a distinct, narrowly-typed DI singleton
            // BEFORE Build() — spec.Endpoints is fully composed by the time the webHost
            // CE block finishes (Run is its terminal member), so this is safe regardless
            // of where useDiscoveryWith sits relative to `resource` in the block.
            // Frank.Discovery's DiscoveryMiddleware constructor-injects this narrow type
            // for ALPS Type correlation, reading Endpoint.Metadata directly — no
            // ApiExplorer/reflection walk involved (#411).
            let dataSource = ResourceEndpointDataSource(spec.Endpoints)
            builder.Services.AddSingleton<ResourceEndpointDataSource>(dataSource) |> ignore
            registerBoundedMemoryCaches builder.Services |> ignore

            spec.Host(builder.WebHost) |> ignore
            spec.Services(builder.Services) |> ignore
            let app = builder.Build()

            (app :> IApplicationBuilder)
            |> spec.BeforeRoutingMiddleware
            |> fun app -> app.UseRouting()
            |> spec.Middleware
            |> ignore

            // The generic EndpointDataSource registration is a separate, post-Build()-only
            // step (IEndpointRouteBuilder.DataSources is only reachable after Build()) —
            // this is the ONE piece of endpoint-source wiring that structurally cannot move
            // earlier (confirmed: registering an EndpointDataSource singleton into DI before
            // Build() does not get auto-wired into IEndpointRouteBuilder.DataSources).
            (app :> IEndpointRouteBuilder).DataSources.Add(dataSource)

            if Environment.GetEnvironmentVariable "FRANK_VALIDATE" = "1" then
                runValidateMode app.Services (dataSource :> EndpointDataSource)

            app.Run()
#else
            let builder = Host.CreateDefaultBuilder(args)
            let dataSource = ResourceEndpointDataSource(spec.Endpoints)

            let config =
                Action<_>(fun webBuilder ->
                    spec
                        .Host(webBuilder)
                        .ConfigureServices(fun services ->
                            spec.Services services |> ignore
                            services.AddSingleton<ResourceEndpointDataSource>(dataSource) |> ignore
                            registerBoundedMemoryCaches services |> ignore)
                        .Configure(fun app ->
                            app
                            |> spec.BeforeRoutingMiddleware
                            |> fun app -> app.UseRouting()
                            |> spec.Middleware
                            |> fun app -> app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(dataSource))
                            |> ignore)
                    |> ignore)

            let configured =
                if spec.UseDefaults then
                    builder.ConfigureWebHostDefaults(config)
                else
                    builder.ConfigureWebHost(config)

            let host = configured.Build()

            if Environment.GetEnvironmentVariable "FRANK_VALIDATE" = "1" then
                runValidateMode host.Services (dataSource :> EndpointDataSource)

            host.Run()
#endif

        member __.Yield(_) = WebHostSpec.Empty

        [<CustomOperation("configure")>]
        member __.Configure(spec, f) = { spec with Host = spec.Host >> f }

        [<CustomOperation("plugBeforeRouting")>]
        member __.PlugBeforeRouting(spec, f) =
            { spec with
                BeforeRoutingMiddleware = spec.BeforeRoutingMiddleware >> f }

        [<CustomOperation("plugBeforeRoutingWhen")>]
        member __.PlugBeforeRoutingWhen(spec, cond, f) =
            { spec with
                BeforeRoutingMiddleware =
                    fun app ->
                        if cond app then
                            f (spec.BeforeRoutingMiddleware(app))
                        else
                            spec.BeforeRoutingMiddleware(app) }

        [<CustomOperation("plugBeforeRoutingWhenNot")>]
        member __.PlugBeforeRoutingWhenNot(spec, cond, f) =
            __.PlugBeforeRoutingWhen(spec, not << cond, f)

        [<CustomOperation("plug")>]
        member __.Plug(spec, f) =
            { spec with
                Middleware = spec.Middleware >> f }

        [<CustomOperation("plugWhen")>]
        member __.PlugWhen(spec, cond, f) =
            { spec with
                Middleware =
                    fun app ->
                        if cond app then
                            f (spec.Middleware(app))
                        else
                            spec.Middleware(app) }

        [<CustomOperation("plugWhenNot")>]
        member __.PlugWhenNot(spec, cond, f) = __.PlugWhen(spec, not << cond, f)

        [<CustomOperation("resource")>]
        member __.Resource(spec, resource: Resource) : WebHostSpec =
            { spec with
                Endpoints = Array.append spec.Endpoints resource.Endpoints }

        [<CustomOperation("service")>]
        member __.Service(spec, f) =
            { spec with
                Services = spec.Services >> f }

        [<CustomOperation("useDefaults")>]
        member __.UseDefaults(spec) = { spec with UseDefaults = true }

    let webHost args = WebHostBuilder(args)
