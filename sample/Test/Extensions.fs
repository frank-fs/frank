module Test.Extensions

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc
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

    [<CustomOperation("useContentNegotiation")>]
    member __.UseContentNegotiation(spec:Frank.Builder.WebHostSpec) =
        { spec with
            Services = fun services ->
                spec.Services(services)
                    .AddMvcCore(fun options -> options.ReturnHttpNotAcceptable <- true)
                    .SetCompatibilityVersion(CompatibilityVersion.Version_2_2)
                    .AddXmlDataContractSerializerFormatters()
                    .AddJsonFormatters()
                    |> ignore
                services }
