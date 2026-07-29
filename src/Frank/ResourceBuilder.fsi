namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Builder

[<Struct>]
type Resource = { Endpoints: Endpoint[] }

type ResourceSpec =
    { Name: string
      Handlers: (string * RequestDelegate) list
      Metadata: (EndpointBuilder -> unit) list }

    static member Empty: ResourceSpec
    member Build: routeTemplate: string -> Resource

[<Sealed>]
type ResourceBuilder =
    new: routeTemplate: string -> ResourceBuilder

    member Run: spec: ResourceSpec -> Resource

    member Yield: 'T -> ResourceSpec

    [<CustomOperation("name")>]
    member Name: spec: ResourceSpec * name: string -> ResourceSpec

    static member AddMetadata: spec: ResourceSpec * convention: (EndpointBuilder -> unit) -> ResourceSpec

    /// Scopes a convention to endpoints registered under the given HTTP method, by
    /// inspecting the `HttpMethodMetadata` already present on the endpoint builder.
    /// Granularity is per-method, not per-handler: if a resource registers multiple
    /// handlers under the same HTTP method, the convention applies to all of them,
    /// not just one.
    static member AddMethodMetadata:
        httpMethod: string * spec: ResourceSpec * convention: (EndpointBuilder -> unit) -> ResourceSpec

    /// Shared helper behind the `Get`/`Post`/etc. `HandlerDefinition` overloads: adds
    /// the handler and projects `HandlerDefinitionMetadata.toConventions` through
    /// `AddMethodMetadata`, so the definition's metadata only applies to endpoints
    /// registered under the given HTTP method.
    static member AddHandlerDefinition:
        httpMethod: string * spec: ResourceSpec * def: HandlerDefinition -> ResourceSpec

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
    member Delete: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec

    [<CustomOperation("get")>]
    member Get: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member Get: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member Get:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member Get: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member Get: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member Get: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec

    [<CustomOperation("head")>]
    member Head: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member Head: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member Head:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member Head: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member Head: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member Head: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec

    [<CustomOperation("options")>]
    member Options: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member Options: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member Options:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member Options: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member Options: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member Options: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec

    [<CustomOperation("patch")>]
    member Patch: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member Patch: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member Patch:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member Patch: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member Patch: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member Patch: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec

    [<CustomOperation("post")>]
    member Post: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member Post: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member Post:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member Post: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member Post: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member Post: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec

    [<CustomOperation("put")>]
    member Put: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member Put: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member Put:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member Put: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member Put: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec
    member Put: spec: ResourceSpec * handlerDef: HandlerDefinition -> ResourceSpec

    [<CustomOperation("trace")>]
    member Trace: spec: ResourceSpec * handler: RequestDelegate -> ResourceSpec

    member Trace: spec: ResourceSpec * handler: (HttpContext -> Task<'a>) -> ResourceSpec

    member Trace:
        spec: ResourceSpec *
        handler: ((HttpContext -> Task<HttpContext option>) -> HttpContext -> Task<HttpContext option>) ->
            ResourceSpec

    member Trace: spec: ResourceSpec * handler: (HttpContext -> Async<'a>) -> ResourceSpec
    member Trace: spec: ResourceSpec * handler: (HttpContext -> unit) -> ResourceSpec

[<AutoOpen>]
module ResourceFunctions =
    val resource: routeTemplate: string -> ResourceBuilder

[<Sealed>]
type internal ResourceEndpointDataSource =
    inherit EndpointDataSource
    new: endpoints: Endpoint[] -> ResourceEndpointDataSource
    override Endpoints: System.Collections.Generic.IReadOnlyList<Endpoint>
    override GetChangeToken: unit -> Microsoft.Extensions.Primitives.IChangeToken
