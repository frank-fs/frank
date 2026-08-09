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

    member inline Yield: 'T -> WebHostSpec

    [<CustomOperation("configure")>]
    member inline Configure: spec: WebHostSpec * f: (IWebHostBuilder -> IWebHostBuilder) -> WebHostSpec

    [<CustomOperation("plugBeforeRouting")>]
    member inline PlugBeforeRouting: spec: WebHostSpec * f: (IApplicationBuilder -> IApplicationBuilder) -> WebHostSpec

    [<CustomOperation("plugBeforeRoutingWhen")>]
    member inline PlugBeforeRoutingWhen:
        spec: WebHostSpec * cond: (IApplicationBuilder -> bool) * f: (IApplicationBuilder -> IApplicationBuilder) ->
            WebHostSpec

    [<CustomOperation("plugBeforeRoutingWhenNot")>]
    member inline PlugBeforeRoutingWhenNot:
        spec: WebHostSpec * cond: (IApplicationBuilder -> bool) * f: (IApplicationBuilder -> IApplicationBuilder) ->
            WebHostSpec

    /// Registers an app-wide Link header contribution: present on every
    /// response, including unmatched routes (404) and responses regenerated
    /// by exception-handling middleware. Two forms: `link target rel` is
    /// sugar for a static entry; `link (fun ctx -> ...)` is the general form
    /// for a provider whose value depends on the request or on configuration.
    [<CustomOperation("link")>]
    member inline Link: spec: WebHostSpec * provider: (HttpContext -> WebLink seq) -> WebHostSpec

    [<CustomOperation("link")>]
    member inline Link: spec: WebHostSpec * target: string * rel: string -> WebHostSpec

    [<CustomOperation("plug")>]
    member inline Plug: spec: WebHostSpec * f: (IApplicationBuilder -> IApplicationBuilder) -> WebHostSpec

    [<CustomOperation("plugWhen")>]
    member inline PlugWhen:
        spec: WebHostSpec * cond: (IApplicationBuilder -> bool) * f: (IApplicationBuilder -> IApplicationBuilder) ->
            WebHostSpec

    [<CustomOperation("plugWhenNot")>]
    member inline PlugWhenNot:
        spec: WebHostSpec * cond: (IApplicationBuilder -> bool) * f: (IApplicationBuilder -> IApplicationBuilder) ->
            WebHostSpec

    [<CustomOperation("resource")>]
    member inline Resource: spec: WebHostSpec * resource: Resource -> WebHostSpec

    [<CustomOperation("service")>]
    member inline Service: spec: WebHostSpec * f: (IServiceCollection -> IServiceCollection) -> WebHostSpec

    [<CustomOperation("useDefaults")>]
    member inline UseDefaults: spec: WebHostSpec -> WebHostSpec

[<AutoOpen>]
module WebHostFunctions =
    val webHost: args: string[] -> WebHostBuilder
