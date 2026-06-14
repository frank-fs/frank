module Frank.Discovery.Tests.TestHelpers

open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Frank.Discovery

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
            Href = Some "https://schema.org/Game" }
          { Id = "agent"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/agent" } ]
      DescribedByLinks = [ "<https://schema.org/Game>; rel=\"describedby\"" ] }
