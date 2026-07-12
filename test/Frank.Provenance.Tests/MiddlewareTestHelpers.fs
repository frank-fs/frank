module Frank.Provenance.Tests.MiddlewareTestHelpers

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Frank.Provenance

type OrderPlaced = { Id: string }

/// Keys use dotted form (matching the code generator output).
/// The middleware normalises typeof<T>.FullName via Replace('+','.') at lookup time.
let orderProvConfig () : ProvenanceConfig =
    { ProvClasses =
        Map.ofList
            [ typeof<OrderPlaced>.FullName.Replace('+', '.'),
              (Frank.Semantic.ProvOClass.Activity, Some(Uri "https://schema.org/OrderAction")) ]
      KnownNamespaces = [| "https://schema.org/" |]
      PropertyClassRanges = Map.empty
      DeclaredPrefixes = []
      StoreConfig = ProvenanceStoreConfig.defaults
      MaxBodyBytes = ProvenanceConfig.defaultMaxBodyBytes }

type CapturingStore() =
    let records = System.Collections.Concurrent.ConcurrentBag<ProvenanceRecord>()
    member _.Records = records |> Seq.toList

    interface IProvenanceStore with
        member _.Append r = records.Add r
        member _.QueryByResource _ = Task.FromResult []
        member _.QueryByAgent _ = Task.FromResult []
        member _.QueryByActivityId _ = Task.FromResult None

let private configureProvenanceApp (app: WebApplication) : unit =
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

    app
        .MapPut(
            "/orders/{id}",
            Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
                ctx.Response.StatusCode <- 200
                ctx.Response.WriteAsync("{}"))
        )
        .WithMetadata(
            Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(200, typeof<OrderPlaced>, [| "application/json" |])
        )
    |> ignore

    app
        .MapMethods(
            "/orders/{id}",
            [| "PATCH" |],
            Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
                ctx.Response.StatusCode <- 200
                ctx.Response.WriteAsync("{}"))
        )
        .WithMetadata(
            Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(200, typeof<OrderPlaced>, [| "application/json" |])
        )
    |> ignore

    app
        .MapMethods(
            "/orders/{id}",
            [| "DELETE" |],
            Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
                ctx.Response.StatusCode <- 204
                ctx.Response.WriteAsync(""))
        )
        .WithMetadata(Microsoft.AspNetCore.Http.ProducesResponseTypeMetadata(204, typeof<unit>, [||]))
    |> ignore

    app.MapGet(
        "/no-produces",
        Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
            ctx.Response.StatusCode <- 200
            ctx.Response.WriteAsync("ok"))
    )
    |> ignore

    app.MapPost(
        "/reject",
        Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
            ctx.Response.StatusCode <- 422
            ctx.Response.WriteAsync("validation error"))
    )
    |> ignore

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
    configureProvenanceApp app
    app.StartAsync().GetAwaiter().GetResult()
    app

let startProvenanceServerWithStore (config: ProvenanceConfig) (store: IProvenanceStore) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    builder.Services.AddSingleton<IProvenanceStore>(store) |> ignore
    let app = builder.Build()
    configureProvenanceApp app
    app.StartAsync().GetAwaiter().GetResult()
    app

let startProvenanceServerWithThrowingEndpoint (config: ProvenanceConfig) =
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

    app.MapGet(
        "/throw-io",
        Func<HttpContext, Task>(fun _ -> Task.FromException(System.IO.IOException "simulated downstream disk error"))
    )
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app
