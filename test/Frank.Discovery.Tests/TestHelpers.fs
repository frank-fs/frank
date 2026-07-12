module Frank.Discovery.Tests.TestHelpers

open System
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank.Discovery

/// Captures all log messages emitted through the logging pipeline.
/// Add via builder.Logging.AddProvider to intercept middleware log output.
type CapturingLoggerProvider() =
    let messages = System.Collections.Concurrent.ConcurrentBag<string>()
    member _.Messages = messages |> Seq.toList

    interface ILoggerProvider with
        member _.CreateLogger(_categoryName) =
            { new ILogger with
                member _.IsEnabled _ = true

                member _.BeginScope<'TState>(state: 'TState) =
                    { new IDisposable with
                        member _.Dispose() = () }

                member _.Log<'TState>(_level, _eventId, state: 'TState, ex, formatter: Func<'TState, exn, string>) =
                    if not (isNull (box formatter)) then
                        messages.Add(formatter.Invoke(state, ex)) }

        member _.Dispose() = ()

/// Spin a TestServer with the discovery middleware in front of a couple of
/// routed endpoints. The GET /games/{id} endpoint carries ResourceRelationMetadata
/// so the middleware can build the JSON Home directory at runtime.
let startServer (config: DiscoveryConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    builder.Services.AddRouting() |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<DiscoveryMiddleware.DiscoveryMiddleware>() |> ignore

    app
        .MapMethods("/games/{id}", [| "GET" |], System.Func<string>(fun () -> "game"))
        .WithMetadata({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
    |> ignore

    app.MapMethods("/games/{id}/moves", [| "POST" |], System.Func<string>(fun () -> "moved"))
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

let linkValues (resp: HttpResponseMessage) =
    match resp.Headers.TryGetValues "Link" with
    | true, vs -> vs |> List.ofSeq
    | _ -> []

let allowValues (resp: HttpResponseMessage) =
    match resp.Content.Headers.Allow with
    | a when a.Count > 0 -> a |> List.ofSeq
    | _ ->
        match resp.Headers.TryGetValues "Allow" with
        | true, vs ->
            vs
            |> Seq.collect (fun v -> v.Split(','))
            |> Seq.map (fun s -> s.Trim())
            |> List.ofSeq
        | _ -> []

let sampleConfig =
    { ProfileUri = "/alps/test"
      HomeRoute = "/"
      AlpsDescriptors =
        [ { Id = "Game"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/Game"
            Descriptors = []
            Rt = None }
          { Id = "agent"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/agent"
            Descriptors = []
            Rt = None } ]
      DescribedByLinks = [ "<https://schema.org/Game>; rel=\"describedby\"" ]
      ResourceHrefVars = Map.ofList [ "https://schema.org/Game", Map.ofList [ "id", "https://schema.org/identifier" ] ] }

/// Spin a TestServer where /games/{id} handles both GET and POST under the same
/// ResourceRelationMetadata. Used by the multi-verb merge test (#390).
let startMultiVerbServer (config: DiscoveryConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    builder.Services.AddRouting() |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<DiscoveryMiddleware.DiscoveryMiddleware>() |> ignore

    app
        .MapMethods("/games/{id}", [| "GET" |], System.Func<string>(fun () -> "game"))
        .WithMetadata({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
    |> ignore

    app
        .MapMethods("/games/{id}", [| "POST" |], System.Func<string>(fun () -> "moved"))
        .WithMetadata({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

/// Spin a TestServer where two DIFFERENT hrefs share the SAME relation IRI.
/// Used by the duplicate-key guard test (#390 F4).
let startDuplicateRelationServer (config: DiscoveryConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    builder.Services.AddRouting() |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<DiscoveryMiddleware.DiscoveryMiddleware>() |> ignore

    app
        .MapMethods("/games/{id}", [| "GET" |], System.Func<string>(fun () -> "game"))
        .WithMetadata({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
    |> ignore

    // Second, DIFFERENT href with the SAME relation — configuration error scenario.
    app
        .MapMethods("/games/{gid}/variant", [| "POST" |], System.Func<string>(fun () -> "variant"))
        .WithMetadata({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app

/// Spin a TestServer for duplicate-relation collision with a CapturingLoggerProvider
/// already registered, so the test can assert the warning was emitted.
let startDuplicateRelationServerWithLogCapture (config: DiscoveryConfig) =
    let provider = new CapturingLoggerProvider()
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Logging.AddProvider(provider) |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<DiscoveryMiddleware.DiscoveryMiddleware>() |> ignore

    app
        .MapMethods("/games/{id}", [| "GET" |], System.Func<string>(fun () -> "game"))
        .WithMetadata({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
    |> ignore

    app
        .MapMethods("/games/{gid}/variant", [| "POST" |], System.Func<string>(fun () -> "variant"))
        .WithMetadata({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    provider, app

/// Spin a TestServer with discovery middleware AND a /tictactoe vocabulary route.
/// Used by the dereference acceptance test (item #6).
let startVocabServer (config: DiscoveryConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    builder.Services.AddRouting() |> ignore
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<DiscoveryMiddleware.DiscoveryMiddleware>() |> ignore

    app.MapGet("/tictactoe", System.Func<string>(fun () -> "ttt:square a rdfs:Class ."))
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app
