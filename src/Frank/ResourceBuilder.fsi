namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Builder

[<Struct>]
type Resource = { Endpoints: Endpoint[] }

type ResourceSpec =
    { Name: string
      Handlers: (string * RequestDelegate * obj list) list
      Metadata: (EndpointBuilder -> unit) list }

    static member Empty: ResourceSpec
    member Build: routeTemplate: string -> Resource

[<Sealed>]
type ResourceBuilder =
    new: routeTemplate: string -> ResourceBuilder

    member Run: spec: ResourceSpec -> Resource

    member inline Yield: 'T -> ResourceSpec

    [<CustomOperation("name")>]
    member inline Name: spec: ResourceSpec * name: string -> ResourceSpec

    /// Registers a resource-scoped Link header contribution: present only on
    /// responses from this resource's own endpoints, never on unmatched
    /// routes or other resources. Two forms: `link target rel` is sugar for
    /// a static entry; `link (fun ctx -> ...)` is the general form for a
    /// provider whose value depends on the request.
    [<CustomOperation("link")>]
    member Link: spec: ResourceSpec * target: string * rel: string -> ResourceSpec

    [<CustomOperation("link")>]
    member Link: spec: ResourceSpec * provider: (HttpContext -> WebLink seq) -> ResourceSpec

    static member AddMetadata: spec: ResourceSpec * convention: (EndpointBuilder -> unit) -> ResourceSpec

    /// Scopes a convention to endpoints registered under the given HTTP method, by
    /// inspecting the `HttpMethodMetadata` already present on the endpoint builder.
    /// Granularity is per-method, not per-handler: if a resource registers multiple
    /// handlers under the same HTTP method, the convention applies to all of them,
    /// not just one.
    static member AddMethodMetadata:
        httpMethod: string * spec: ResourceSpec * convention: (EndpointBuilder -> unit) -> ResourceSpec

    /// Shared helper behind the `Get`/`Post`/etc. `HandlerDefinition` overloads: adds
    /// the handler with its own metadata attached directly to that handler's entry,
    /// so the definition's metadata only ever applies to its own endpoint -- not to
    /// other handlers sharing the same HTTP method.
    static member AddHandlerDefinition:
        httpMethod: string * spec: ResourceSpec * def: HandlerDefinition -> ResourceSpec

    /// Adds one handler entry per `HandlerDefinition`, each carrying only its own
    /// metadata. Shared helper behind the `Get`/`Post`/etc. `HandlerDefinition list` overloads.
    static member AddHandlerDefinitions:
        httpMethod: string * spec: ResourceSpec * defs: HandlerDefinition list -> ResourceSpec

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

    static member AddHandler: httpMethod: string * spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

    [<CustomOperation("connect")>]
    member inline Connect: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member inline Connect: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member inline Connect:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member inline Connect: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member inline Connect: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

    [<CustomOperation("delete")>]
    member inline Delete: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member inline Delete: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member inline Delete:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member inline Delete: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member inline Delete: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member inline Delete: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member inline Delete: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec

    [<CustomOperation("get")>]
    member inline Get: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member inline Get: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member inline Get:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member inline Get: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member inline Get: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member inline Get: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member inline Get: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec

    [<CustomOperation("head")>]
    member inline Head: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member inline Head: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member inline Head:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member inline Head: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member inline Head: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member inline Head: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member inline Head: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec

    [<CustomOperation("options")>]
    member inline Options: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member inline Options: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member inline Options:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member inline Options: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member inline Options: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member inline Options: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member inline Options: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec

    [<CustomOperation("patch")>]
    member inline Patch: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member inline Patch: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member inline Patch:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member inline Patch: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member inline Patch: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member inline Patch: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member inline Patch: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec

    [<CustomOperation("post")>]
    member inline Post: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member inline Post: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member inline Post:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member inline Post: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member inline Post: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member inline Post: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member inline Post: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec

    [<CustomOperation("put")>]
    member inline Put: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member inline Put: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member inline Put:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member inline Put: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member inline Put: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member inline Put: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec
    member inline Put: spec: ResourceSpec * handlerDefs: HandlerDefinition list -> ResourceSpec

    [<CustomOperation("trace")>]
    member inline Trace: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member inline Trace: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member inline Trace:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member inline Trace: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member inline Trace: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

[<AutoOpen>]
module ResourceFunctions =
    val resource: routeTemplate: string -> ResourceBuilder

[<Sealed>]
type internal ResourceEndpointDataSource =
    inherit EndpointDataSource
    new: endpoints: Endpoint[] -> ResourceEndpointDataSource
    override Endpoints: System.Collections.Generic.IReadOnlyList<Endpoint>
    override GetChangeToken: unit -> Microsoft.Extensions.Primitives.IChangeToken
