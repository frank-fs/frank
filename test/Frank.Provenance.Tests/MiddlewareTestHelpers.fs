module Frank.Provenance.Tests.MiddlewareTestHelpers

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Frank.Provenance

type OrderPlaced = { Id: string }

/// typeof<OrderPlaced>.FullName = "Frank.Provenance.Tests.MiddlewareTestHelpers+OrderPlaced"
/// because OrderPlaced is a nested type inside the compiled module class.
let orderProvConfig () : ProvenanceConfig =
    { ProvClasses =
        Map.ofList
            [ typeof<OrderPlaced>.FullName,
              (Frank.Semantic.ProvOClass.Activity, Some(Uri "https://schema.org/OrderAction")) ]
      KnownNamespaces = [| "https://schema.org/" |]
      StoreConfig = ProvenanceStoreConfig.defaults }

let startProvenanceServer (config: ProvenanceConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore

    builder.Services.AddSingleton<IProvenanceStore>(fun sp ->
        let loggerFactory =
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()

        new MailboxProcessorProvenanceStore(config.StoreConfig, loggerFactory.CreateLogger("prov")) :> IProvenanceStore)
    |> ignore

    let app = builder.Build()
    app.UseMiddleware<ProvenanceMiddleware>() |> ignore

    app
        .MapPost(
            "/orders",
            Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
                ctx.Response.StatusCode <- 201
                ctx.Response.WriteAsync("{}"))
        )
        .WithMetadata(
            Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(201, typeof<OrderPlaced>, [| "application/json" |])
        )
    |> ignore

    app.MapGet(
        "/no-produces",
        Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
            ctx.Response.StatusCode <- 200
            ctx.Response.WriteAsync("ok"))
    )
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app
