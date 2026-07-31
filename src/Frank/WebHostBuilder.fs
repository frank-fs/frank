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

type WebHostSpec =
    { Host: (IWebHostBuilder -> IWebHostBuilder)
      BeforeRoutingMiddleware: (IApplicationBuilder -> IApplicationBuilder)
      Middleware: (IApplicationBuilder -> IApplicationBuilder)
      Endpoints: Endpoint[]
      Services: (IServiceCollection -> IServiceCollection)
      LinkProviders: (HttpContext -> WebLink seq) list
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
          LinkProviders = []
          UseDefaults = false }

[<Sealed>]
type WebHostBuilder(args) =

    member __.Run(spec: WebHostSpec) =
        let builder = Host.CreateDefaultBuilder(args)

        let config =
            Action<_>(fun webBuilder ->
                spec
                    .Host(webBuilder)
                    .ConfigureServices(spec.Services >> ignore)
                    .Configure(fun app ->
                        app
                        |> WebLink.useAppWideLinks spec.LinkProviders
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> WebLink.useResourceScopedLinks
                        |> spec.Middleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                let dataSource = ResourceEndpointDataSource(spec.Endpoints)
                                endpoints.DataSources.Add(dataSource))
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

    [<CustomOperation("link")>]
    member __.Link(spec: WebHostSpec, provider: HttpContext -> WebLink seq) : WebHostSpec =
        { spec with LinkProviders = spec.LinkProviders @ [ provider ] }

    [<CustomOperation("link")>]
    member __.Link(spec: WebHostSpec, target: string, rel: string) : WebHostSpec =
        __.Link(spec, fun (_: HttpContext) -> Seq.singleton { Target = target; Rel = rel; Params = [] })

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

[<AutoOpen>]
module WebHostFunctions =
    let webHost args = WebHostBuilder(args)
