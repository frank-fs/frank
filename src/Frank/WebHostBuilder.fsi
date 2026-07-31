namespace Frank.Builder

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection

type WebHostSpec =
    { Host: (IWebHostBuilder -> IWebHostBuilder)
      BeforeRoutingMiddleware: (IApplicationBuilder -> IApplicationBuilder)
      Middleware: (IApplicationBuilder -> IApplicationBuilder)
      Endpoints: Endpoint[]
      Services: (IServiceCollection -> IServiceCollection)
      LinkProviders: (HttpContext -> WebLink seq) list
      UseDefaults: bool }

    static member Empty: WebHostSpec

[<Sealed>]
type WebHostBuilder =
    new: args: string[] -> WebHostBuilder

    member Run: spec: WebHostSpec -> unit

    member Yield: 'T -> WebHostSpec

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

    [<CustomOperation("link")>]
    member Link: spec: WebHostSpec * provider: (HttpContext -> WebLink seq) -> WebHostSpec

    member Link: spec: WebHostSpec * target: string * rel: string -> WebHostSpec

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

[<AutoOpen>]
module WebHostFunctions =
    val webHost: args: string[] -> WebHostBuilder
