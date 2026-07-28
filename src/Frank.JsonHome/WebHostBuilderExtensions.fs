namespace Frank.JsonHome

open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc.ApiExplorer
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

type private HomeLinkProvider(options: JsonHomeOptions) =
    let links = [| WebLink.create options.Path options.Rel |]

    interface IResponseLinkProvider with
        member _.GetLinks(_) = links :> seq<_>

[<AutoOpen>]
module WebHostBuilderExtensions =

    let private install (options: JsonHomeOptions) (spec: WebHostSpec) =
        { spec with
            Services =
                spec.Services
                >> fun services ->
                    // AddEndpointsApiExplorer is what populates ApiDescription.
                    // It is independent of OpenAPI, which merely calls it too.
                    services.AddEndpointsApiExplorer() |> ignore
                    services.AddSingleton<IResponseLinkProvider>(HomeLinkProvider options) |> ignore
                    services
            BeforeRoutingMiddleware =
                spec.BeforeRoutingMiddleware
                >> fun app ->
                    // Both lambda parameters must be annotated: IApplicationBuilder.Use has
                    // Func<HttpContext, Func<Task>, Task> and Func<HttpContext, RequestDelegate, Task>
                    // overloads that F# cannot choose between otherwise.
                    app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                        if ctx.Request.Path.Equals(PathString options.Path) then
                            task {
                                let provider =
                                    ctx.RequestServices.GetRequiredService<IApiDescriptionGroupCollectionProvider>()

                                let all =
                                    provider.ApiDescriptionGroups.Items
                                    |> Seq.collect (fun g -> g.Items)
                                    |> ApiSurface.ofApiDescriptions

                                let! resources = AuthorizationFilter.apply ctx all

                                if AuthorizationFilter.varies all then
                                    // A shared cache must never serve one principal's view to another.
                                    ctx.Response.Headers.CacheControl <- "private, no-cache"
                                    ctx.Response.Headers.Vary <- "Authorization"

                                do! JsonHome.write options resources ctx
                            }
                            :> Task
                        else
                            next.Invoke ctx) }

    type WebHostBuilder with

        [<CustomOperation("useJsonHome")>]
        member _.UseJsonHome(spec: WebHostSpec) : WebHostSpec = install JsonHomeOptions.Default spec

        [<CustomOperation("useJsonHome")>]
        member _.UseJsonHome(spec: WebHostSpec, configure: JsonHomeOptions -> JsonHomeOptions) : WebHostSpec =
            install (configure JsonHomeOptions.Default) spec
