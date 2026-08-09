namespace Frank.Builder

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
type Resource = { Endpoints: Endpoint[] }

type ResourceSpec =
    { Name: string
      Handlers: (string * RequestDelegate * obj list) list
      Metadata: (EndpointBuilder -> unit) list }

    static member Empty =
        { Name = Unchecked.defaultof<_>
          Handlers = []
          Metadata = [] }

    member spec.Build(routeTemplate) =
        let { Name = name
              Handlers = handlers
              Metadata = metadata } =
            spec

        let routePattern = Patterns.RoutePatternFactory.Parse routeTemplate

        let endpoints =
            [| for httpMethod, handler, ownMetadata in handlers ->
                   let displayName =
                       httpMethod + " " + (if String.IsNullOrEmpty name then routeTemplate else name)

                   let builder = RouteEndpointBuilder(handler, routePattern, 0)
                   builder.DisplayName <- displayName
                   builder.Metadata.Add(HttpMethodMetadata [| httpMethod |])
                   builder.Metadata.Add(handler.Method)

                   for m in ownMetadata do
                       builder.Metadata.Add m

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

    member inline __.Yield(_) = ResourceSpec.Empty

    [<CustomOperation("name")>]
    member inline __.Name(spec: ResourceSpec, name: string) = { spec with Name = name }

    [<CustomOperation("link")>]
    member __.Link(spec: ResourceSpec, target: string, rel: string) : ResourceSpec =
        __.Link(spec, fun (_: HttpContext) -> Seq.singleton { Target = target; Rel = rel; Params = [] })

    [<CustomOperation("link")>]
    member __.Link(spec: ResourceSpec, provider: HttpContext -> WebLink seq) : ResourceSpec =
        ResourceBuilder.AddMetadata(spec, fun builder -> builder.Metadata.Add(ResourceLinkProvider provider))

    static member AddMetadata(spec: ResourceSpec, convention: EndpointBuilder -> unit) : ResourceSpec =
        { spec with
            Metadata = spec.Metadata @ [ convention ] }

    static member AddMethodMetadata
        (
            httpMethod: string,
            spec: ResourceSpec,
            convention: EndpointBuilder -> unit
        ) : ResourceSpec =
        // ResourceSpec.Metadata conventions run against every endpoint in the
        // resource. Build() adds HttpMethodMetadata before running them, so a
        // convention can scope itself by inspecting the builder.
        let methodScoped (builder: EndpointBuilder) =
            let matches =
                builder.Metadata
                |> Seq.tryPick (fun m ->
                    match m with
                    | :? HttpMethodMetadata as meta -> Some meta
                    | _ -> None)
                |> Option.map (fun meta -> meta.HttpMethods |> Seq.contains httpMethod)
                |> Option.defaultValue false

            if matches then
                convention builder

        ResourceBuilder.AddMetadata(spec, methodScoped)

    static member AddHandlerDefinition(httpMethod: string, spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec =
        { spec with
            Handlers = (httpMethod, def.Handler, def.Metadata) :: spec.Handlers }

    static member AddHandlerDefinitions
        (
            httpMethod: string,
            spec: ResourceSpec,
            defs: HandlerDefinition list
        ) : ResourceSpec =
        // AddHandlerDefinition prepends, so folding left-to-right would reverse the
        // batch relative to its declaration order; fold over the reversed list so the
        // final Handlers order matches `defs` order (endpoint 0 = first def, etc.).
        defs
        |> List.rev
        |> List.fold (fun s def -> ResourceBuilder.AddHandlerDefinition(httpMethod, s, def)) spec

    static member AddHandler(httpMethod, spec, handler) =
        { spec with
            Handlers = (httpMethod, handler, []) :: spec.Handlers }

    static member AddHandler(httpMethod, spec, handler: HttpContext -> Task<'a>) =
        { spec with
            Handlers = (httpMethod, RequestDelegate(fun ctx -> handler ctx :> Task), []) :: spec.Handlers }

    static member AddHandler
        (httpMethod, spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
        { spec with
            Handlers =
                (httpMethod, RequestDelegate(fun ctx -> handler methodNotAllowed ctx :> Task), [])
                :: spec.Handlers }

    static member AddHandler(httpMethod, spec, handler: HttpContext -> Async<'a>) =
        { spec with
            Handlers =
                (httpMethod, RequestDelegate(fun ctx -> handler ctx |> Async.StartAsTask :> Task), [])
                :: spec.Handlers }

    static member AddHandler(httpMethod, spec, handler: HttpContext -> unit) =
        { spec with
            Handlers =
                (httpMethod, RequestDelegate(fun ctx -> Task.FromResult(handler ctx) :> Task), [])
                :: spec.Handlers }

    [<CustomOperation("connect")>]
    member inline __.Connect(spec, handler: RequestDelegate) =
        ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

    member inline __.Connect(spec, handler: HttpContext -> Task<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

    member inline __.Connect
        (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
        =
        ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

    member inline __.Connect(spec, handler: HttpContext -> Async<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

    member inline __.Connect(spec, handler: HttpContext -> unit) =
        ResourceBuilder.AddHandler(HttpMethods.Connect, spec, handler)

    [<CustomOperation("delete")>]
    member inline __.Delete(spec, handler: RequestDelegate) =
        ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

    member inline __.Delete(spec, handler: HttpContext -> Task<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

    member inline __.Delete
        (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
        =
        ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

    member inline __.Delete(spec, handler: HttpContext -> Async<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

    member inline __.Delete(spec, handler: HttpContext -> unit) =
        ResourceBuilder.AddHandler(HttpMethods.Delete, spec, handler)

    member inline _.Delete(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Delete, spec, handlerDef)

    member inline _.Delete(spec: ResourceSpec, handlerDefs: HandlerDefinition list) =
        ResourceBuilder.AddHandlerDefinitions(HttpMethods.Delete, spec, handlerDefs)

    [<CustomOperation("get")>]
    member inline __.Get(spec, handler: RequestDelegate) =
        ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

    member inline __.Get(spec, handler: HttpContext -> Task<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

    member inline __.Get(spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
        ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

    member inline __.Get(spec, handler: HttpContext -> Async<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

    member inline __.Get(spec, handler: HttpContext -> unit) =
        ResourceBuilder.AddHandler(HttpMethods.Get, spec, handler)

    member inline _.Get(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Get, spec, handlerDef)

    member inline _.Get(spec: ResourceSpec, handlerDefs: HandlerDefinition list) =
        ResourceBuilder.AddHandlerDefinitions(HttpMethods.Get, spec, handlerDefs)

    [<CustomOperation("head")>]
    member inline __.Head(spec, handler: RequestDelegate) =
        ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

    member inline __.Head(spec, handler: HttpContext -> Task<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

    member inline __.Head
        (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
        =
        ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

    member inline __.Head(spec, handler: HttpContext -> Async<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

    member inline __.Head(spec, handler: HttpContext -> unit) =
        ResourceBuilder.AddHandler(HttpMethods.Head, spec, handler)

    member inline _.Head(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Head, spec, handlerDef)

    member inline _.Head(spec: ResourceSpec, handlerDefs: HandlerDefinition list) =
        ResourceBuilder.AddHandlerDefinitions(HttpMethods.Head, spec, handlerDefs)

    [<CustomOperation("options")>]
    member inline __.Options(spec, handler: RequestDelegate) =
        ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

    member inline __.Options(spec, handler: HttpContext -> Task<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

    member inline __.Options
        (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
        =
        ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

    member inline __.Options(spec, handler: HttpContext -> Async<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

    member inline __.Options(spec, handler: HttpContext -> unit) =
        ResourceBuilder.AddHandler(HttpMethods.Options, spec, handler)

    member inline _.Options(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Options, spec, handlerDef)

    member inline _.Options(spec: ResourceSpec, handlerDefs: HandlerDefinition list) =
        ResourceBuilder.AddHandlerDefinitions(HttpMethods.Options, spec, handlerDefs)

    [<CustomOperation("patch")>]
    member inline __.Patch(spec, handler: RequestDelegate) =
        ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

    member inline __.Patch(spec, handler: HttpContext -> Task<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

    member inline __.Patch
        (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
        =
        ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

    member inline __.Patch(spec, handler: HttpContext -> Async<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

    member inline __.Patch(spec, handler: HttpContext -> unit) =
        ResourceBuilder.AddHandler(HttpMethods.Patch, spec, handler)

    member inline _.Patch(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Patch, spec, handlerDef)

    member inline _.Patch(spec: ResourceSpec, handlerDefs: HandlerDefinition list) =
        ResourceBuilder.AddHandlerDefinitions(HttpMethods.Patch, spec, handlerDefs)

    [<CustomOperation("post")>]
    member inline __.Post(spec, handler: RequestDelegate) =
        ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

    member inline __.Post(spec, handler: HttpContext -> Task<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

    member inline __.Post
        (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
        =
        ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

    member inline __.Post(spec, handler: HttpContext -> Async<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

    member inline __.Post(spec, handler: HttpContext -> unit) =
        ResourceBuilder.AddHandler(HttpMethods.Post, spec, handler)

    member inline _.Post(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Post, spec, handlerDef)

    member inline _.Post(spec: ResourceSpec, handlerDefs: HandlerDefinition list) =
        ResourceBuilder.AddHandlerDefinitions(HttpMethods.Post, spec, handlerDefs)

    [<CustomOperation("put")>]
    member inline __.Put(spec, handler: RequestDelegate) =
        ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

    member inline __.Put(spec, handler: HttpContext -> Task<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

    member inline __.Put(spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) =
        ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

    member inline __.Put(spec, handler: HttpContext -> Async<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

    member inline __.Put(spec, handler: HttpContext -> unit) =
        ResourceBuilder.AddHandler(HttpMethods.Put, spec, handler)

    member inline _.Put(spec: ResourceSpec, handlerDef: HandlerDefinition) =
        ResourceBuilder.AddHandlerDefinition(HttpMethods.Put, spec, handlerDef)

    member inline _.Put(spec: ResourceSpec, handlerDefs: HandlerDefinition list) =
        ResourceBuilder.AddHandlerDefinitions(HttpMethods.Put, spec, handlerDefs)

    [<CustomOperation("trace")>]
    member inline __.Trace(spec, handler: RequestDelegate) =
        ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

    member inline __.Trace(spec, handler: HttpContext -> Task<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

    member inline __.Trace
        (spec, handler: (HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>)
        =
        ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

    member inline __.Trace(spec, handler: HttpContext -> Async<'a>) =
        ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

    member inline __.Trace(spec, handler: HttpContext -> unit) =
        ResourceBuilder.AddHandler(HttpMethods.Trace, spec, handler)

[<AutoOpen>]
module ResourceFunctions =
    let resource routeTemplate = ResourceBuilder(routeTemplate)

[<Sealed>]
type internal ResourceEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()

    override __.Endpoints = endpoints :> _
    override __.GetChangeToken() = NullChangeToken.Singleton :> _
