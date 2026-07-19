namespace Frank

module Builder =

    open System.Threading.Tasks
    open Microsoft.AspNetCore.Builder
    open Microsoft.AspNetCore.Hosting
    open Microsoft.AspNetCore.Http
    open Microsoft.AspNetCore.Routing
    open Microsoft.Extensions.DependencyInjection

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

        static member Empty: JsonHomeMetadata

    /// Marker metadata indicating a resource is an entry point for the JSON Home document.
    /// When any endpoints carry this metadata, only those endpoints appear in the home document.
    /// When no endpoints carry it, all non-internal endpoints appear (backward compat fallback).
    /// Not a struct because EndpointMetadataCollection.GetMetadata<T> requires reference semantics.
    type EntryPointMetadata = { IsEntryPoint: bool }

    type ResourceSpec =
        { Name: string option
          Handlers: (string * RequestDelegate) list
          Metadata: (EndpointBuilder -> unit) list }

        static member Empty: ResourceSpec

        member Build: routeTemplate: string -> Resource

    [<Sealed>]
    type ResourceBuilder =
        new: routeTemplate: string -> ResourceBuilder

        member Run: spec: ResourceSpec -> Resource

        member Yield: unit -> ResourceSpec

        [<CustomOperation("name")>]
        member Name: spec: ResourceSpec * name: string -> ResourceSpec

        /// Marks this resource as a JSON Home entry point.
        /// Only entry-point resources appear in the home document when any are designated.
        [<CustomOperation("entryPoint")>]
        member EntryPoint: spec: ResourceSpec -> ResourceSpec

        static member AddMetadata: spec: ResourceSpec * convention: (EndpointBuilder -> unit) -> ResourceSpec

        static member AddHandler: httpMethod: string * spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

        static member AddHandler:
            httpMethod: string * spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

        static member AddHandler:
            httpMethod: string *
            spec: ResourceSpec *
            handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
                ResourceSpec

        static member AddHandler:
            httpMethod: string * spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec

        static member AddHandler:
            httpMethod: string * spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

        [<CustomOperation("connect")>]
        member Connect: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

        member Connect: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

        member Connect:
            spec: ResourceSpec *
            handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
                ResourceSpec

        member Connect: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
        member Connect: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

        [<CustomOperation("delete")>]
        member Delete: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

        member Delete: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

        member Delete:
            spec: ResourceSpec *
            handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
                ResourceSpec

        member Delete: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
        member Delete: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

        [<CustomOperation("get")>]
        member Get: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

        member Get: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

        member Get:
            spec: ResourceSpec *
            handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
                ResourceSpec

        member Get: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
        member Get: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

        [<CustomOperation("head")>]
        member Head: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

        member Head: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

        member Head:
            spec: ResourceSpec *
            handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
                ResourceSpec

        member Head: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
        member Head: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

        [<CustomOperation("options")>]
        member Options: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

        member Options: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

        member Options:
            spec: ResourceSpec *
            handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
                ResourceSpec

        member Options: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
        member Options: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

        [<CustomOperation("patch")>]
        member Patch: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

        member Patch: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

        member Patch:
            spec: ResourceSpec *
            handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
                ResourceSpec

        member Patch: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
        member Patch: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

        [<CustomOperation("post")>]
        member Post: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

        member Post: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

        member Post:
            spec: ResourceSpec *
            handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
                ResourceSpec

        member Post: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
        member Post: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

        [<CustomOperation("put")>]
        member Put: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

        member Put: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

        member Put:
            spec: ResourceSpec *
            handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
                ResourceSpec

        member Put: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
        member Put: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

        [<CustomOperation("trace")>]
        member Trace: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

        member Trace: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

        member Trace:
            spec: ResourceSpec *
            handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
                ResourceSpec

        member Trace: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
        member Trace: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

    val resource: routeTemplate: string -> ResourceBuilder

    /// The EndpointDataSource wrapping Frank's own composed Endpoint[] — the SAME array
    /// spec.Endpoints holds after full webHost CE composition. WebHostBuilder.Run registers
    /// this exact instance as a narrowly-typed DI singleton (#411), separately from the
    /// generic EndpointDataSource it also adds to IEndpointRouteBuilder.DataSources —
    /// Frank.Discovery's DiscoveryMiddleware constructor-injects this narrow type
    /// specifically for ALPS Type correlation, reading Endpoint.Metadata directly with no
    /// ApiExplorer/reflection dependency.
    [<Sealed>]
    type ResourceEndpointDataSource =
        new: endpoints: Endpoint[] -> ResourceEndpointDataSource
        inherit EndpointDataSource

    type WebHostSpec =
        { Host: (IWebHostBuilder -> IWebHostBuilder)
          BeforeRoutingMiddleware: (IApplicationBuilder -> IApplicationBuilder)
          Middleware: (IApplicationBuilder -> IApplicationBuilder)
          Endpoints: Endpoint[]
          Services: (IServiceCollection -> IServiceCollection)
          UseDefaults: bool }

        static member Empty: WebHostSpec

    /// Validates composed endpoints before the app starts serving.
    /// Implementations throw <c>invalidOp</c> (InvalidOperationException) to signal a
    /// validation failure that should fail the build. Register via DI as IStartupValidator.
    type IStartupValidator =
        abstract member Validate: EndpointDataSource -> unit

    [<Sealed>]
    type WebHostBuilder =
        new: args: string[] -> WebHostBuilder

        member Run: spec: WebHostSpec -> unit

        member Yield: unit -> WebHostSpec

        [<CustomOperation("configure")>]
        member Configure: spec: WebHostSpec * f: (IWebHostBuilder -> IWebHostBuilder) -> WebHostSpec

        [<CustomOperation("plugBeforeRouting")>]
        member PlugBeforeRouting: spec: WebHostSpec * f: (IApplicationBuilder -> IApplicationBuilder) -> WebHostSpec

        [<CustomOperation("plugBeforeRoutingWhen")>]
        member PlugBeforeRoutingWhen:
            spec: WebHostSpec * cond: (IApplicationBuilder -> bool) * f: (IApplicationBuilder -> IApplicationBuilder) ->
                WebHostSpec

        [<CustomOperation("plugBeforeRoutingWhenNot")>]
        member PlugBeforeRoutingWhenNot:
            spec: WebHostSpec * cond: (IApplicationBuilder -> bool) * f: (IApplicationBuilder -> IApplicationBuilder) ->
                WebHostSpec

        [<CustomOperation("plug")>]
        member Plug: spec: WebHostSpec * f: (IApplicationBuilder -> IApplicationBuilder) -> WebHostSpec

        [<CustomOperation("plugWhen")>]
        member PlugWhen:
            spec: WebHostSpec * cond: (IApplicationBuilder -> bool) * f: (IApplicationBuilder -> IApplicationBuilder) ->
                WebHostSpec

        [<CustomOperation("plugWhenNot")>]
        member PlugWhenNot:
            spec: WebHostSpec * cond: (IApplicationBuilder -> bool) * f: (IApplicationBuilder -> IApplicationBuilder) ->
                WebHostSpec

        [<CustomOperation("resource")>]
        member Resource: spec: WebHostSpec * resource: Resource -> WebHostSpec

        [<CustomOperation("service")>]
        member Service: spec: WebHostSpec * f: (IServiceCollection -> IServiceCollection) -> WebHostSpec

        [<CustomOperation("useDefaults")>]
        member UseDefaults: spec: WebHostSpec -> WebHostSpec

    val webHost: args: string[] -> WebHostBuilder
