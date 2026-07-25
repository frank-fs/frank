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

[<AutoOpen>]
module ResourceFunctions =
    val resource: routeTemplate: string -> ResourceBuilder

[<Sealed>]
type internal ResourceEndpointDataSource =
    inherit EndpointDataSource
    new: endpoints: Endpoint[] -> ResourceEndpointDataSource
    override Endpoints: System.Collections.Generic.IReadOnlyList<Endpoint>
    override GetChangeToken: unit -> Microsoft.Extensions.Primitives.IChangeToken
