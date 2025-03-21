module Frank.Oxpecker.Extensions

open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Cors.Infrastructure
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging

let isDevelopment (app: IApplicationBuilder) =
    app.ApplicationServices.GetService<IWebHostEnvironment>().IsDevelopment()

type Frank.Builder.WebHostBuilder with

    /// Extension to the WebHostBuilder computation expression
    /// to support logging through the ILoggingBuilder.
    [<CustomOperation("logging")>]
    member __.Logging(spec: Frank.Builder.WebHostSpec, f: ILoggingBuilder -> ILoggingBuilder) =
        { spec with
            Services = fun services -> spec.Services(services).AddLogging(fun builder -> f builder |> ignore) }

    [<CustomOperation("useCors")>]
    member __.UseCors(spec: Frank.Builder.WebHostSpec, corsPolicyBuilder: CorsPolicyBuilder -> unit) =
        { spec with
            Services = (fun services -> spec.Services(services).AddCors())
            Middleware = (fun app -> spec.Middleware(app).UseCors(corsPolicyBuilder)) }

    [<CustomOperation("useErrorHandler")>]
    member __.UseErrorHandler(spec: Frank.Builder.WebHostSpec, errorHandler: HttpContext -> RequestDelegate -> Task) =
        { spec with
            Middleware = (fun app -> spec.Middleware(app).Use(errorHandler)) }
