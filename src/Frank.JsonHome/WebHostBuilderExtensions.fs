namespace Frank.JsonHome

open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =

    let private install (options: JsonHomeOptions) (spec: WebHostSpec) =
        let document = JsonHome.documentResource options

        { spec with
            Services =
                spec.Services
                >> fun services ->
                    // AddEndpointsApiExplorer is what populates ApiDescription.
                    // It is independent of OpenAPI, which merely calls it too.
                    services.AddEndpointsApiExplorer() |> ignore

                    // Fails startup if two resources declare the same rel (#475).
                    // Deliberately an IStartupFilter, not AddOptionsWithValidateOnStart:
                    // see DuplicateRelStartupFilter.fsi for why the Options-validation
                    // hook fires too early to see Frank's endpoints.
                    services.AddSingleton<IStartupFilter, DuplicateRelStartupFilter>() |> ignore

                    // Fails startup if a hrefVar doesn't match its resource's route
                    // template variables, in either direction (#474). Same
                    // IStartupFilter reasoning as DuplicateRelStartupFilter above.
                    services.AddSingleton<IStartupFilter, HrefVarStartupFilter>() |> ignore

                    services
            LinkProviders =
                spec.LinkProviders
                @ [ fun (_: HttpContext) -> Seq.singleton { Target = options.Path; Rel = options.Rel; Params = [] } ]
            // Dispatched through the app's own, single, structurally-last
            // UseEndpoints(...) call in WebHostBuilder.Run -- after every
            // Middleware-composed stage, including useAuthentication and
            // useAuthorization, regardless of where useJsonHome sits in the
            // webHost {} block. AuthorizationFilter.apply reads ctx.User, and
            // that must already reflect the real principal by the time it runs.
            Endpoints = Array.append spec.Endpoints document.Endpoints }

    type WebHostBuilder with

        [<CustomOperation("useJsonHome")>]
        member _.UseJsonHome(spec: WebHostSpec) : WebHostSpec = install JsonHomeOptions.Default spec

        [<CustomOperation("useJsonHome")>]
        member _.UseJsonHome(spec: WebHostSpec, configure: JsonHomeOptions -> JsonHomeOptions) : WebHostSpec =
            install (configure JsonHomeOptions.Default) spec
