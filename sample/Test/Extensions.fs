module Test.Extensions

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Mvc
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

/// Helper function to determine whether the application
/// is running in a development environment.
let isDevelopment (app:IApplicationBuilder) =
    app.ApplicationServices
       .GetService<IHostingEnvironment>()
       .IsDevelopment()

type Frank.Builder.WebHostBuilder with

    /// Extension to the WebHostBuilder computation expression
    /// to support logging through the ILoggingBuilder.
    [<CustomOperation("logging")>]
    member __.Logging(spec:Frank.Builder.WebHostSpec, f:ILoggingBuilder -> ILoggingBuilder) =
        { spec with
            Services = fun services ->
                spec.Services(services).AddLogging(fun builder -> f builder |> ignore) }

    /// Extension to the WebHostBuilder computation expression
    /// to enable content negotiation with XML and JSON.
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
