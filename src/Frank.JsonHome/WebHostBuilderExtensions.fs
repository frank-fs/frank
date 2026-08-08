namespace Frank.JsonHome

open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Microsoft.Extensions.Options
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

                    // FixedJsonHomeOptionsFactory makes IOptions<JsonHomeOptions>.Value the
                    // same instance documentHandler already renders from -- no second,
                    // independently-configured copy of this useJsonHome call's options.
                    services.AddSingleton<IOptionsFactory<JsonHomeOptions>>(FixedJsonHomeOptionsFactory(options))
                    |> ignore

                    // Fails startup if two resources declare the same rel (#475) --
                    // DuplicateRelValidator.Validate runs during Host.StartAsync, before
                    // Kestrel (or any other IHostedService) starts serving.
                    services.AddOptionsWithValidateOnStart<JsonHomeOptions>() |> ignore

                    services.TryAddEnumerable(
                        ServiceDescriptor.Singleton<IValidateOptions<JsonHomeOptions>, DuplicateRelValidator>())

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
