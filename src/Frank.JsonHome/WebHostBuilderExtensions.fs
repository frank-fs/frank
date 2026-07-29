namespace Frank.JsonHome

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =

    let private install (options: JsonHomeOptions) (spec: WebHostSpec) =
        let run = JsonHome.middleware options

        { spec with
            Services =
                spec.Services
                >> fun services ->
                    // AddEndpointsApiExplorer is what populates ApiDescription.
                    // It is independent of OpenAPI, which merely calls it too.
                    services.AddEndpointsApiExplorer() |> ignore
                    services
            BeforeRoutingMiddleware =
                spec.BeforeRoutingMiddleware
                >> fun app ->
                    // Both lambda parameters must be annotated: IApplicationBuilder.Use has
                    // Func<HttpContext, Func<Task>, Task> and Func<HttpContext, RequestDelegate, Task>
                    // overloads that F# cannot choose between otherwise.
                    app.Use(fun (ctx: HttpContext) (next: RequestDelegate) -> run ctx (fun () -> next.Invoke ctx)) }

    type WebHostBuilder with

        [<CustomOperation("useJsonHome")>]
        member _.UseJsonHome(spec: WebHostSpec) : WebHostSpec = install JsonHomeOptions.Default spec

        [<CustomOperation("useJsonHome")>]
        member _.UseJsonHome(spec: WebHostSpec, configure: JsonHomeOptions -> JsonHomeOptions) : WebHostSpec =
            install (configure JsonHomeOptions.Default) spec
