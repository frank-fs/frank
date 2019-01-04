module Frank.Giraffe.Extensions

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

let isDevelopment (app:IApplicationBuilder) =
    app.ApplicationServices
       .GetService<IHostingEnvironment>()
       .IsDevelopment()

type Frank.Builder.WebHostBuilder with

    [<CustomOperation("logging")>]
    member __.Logging(spec:Frank.Builder.WebHostSpec, f:ILoggingBuilder -> ILoggingBuilder) =
        { spec with
            Services = fun services ->
                spec.Services(services).AddLogging(fun builder -> f builder |> ignore) }
