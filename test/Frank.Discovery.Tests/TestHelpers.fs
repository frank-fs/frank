module Frank.Discovery.Tests.TestHelpers

open System
open System.Net.Http
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank.Builder
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

/// #397: fixture request-body type for AcceptsMetadata-correlation tests (stands in
/// for a generated MoveRequest-style type).
type MoveRequestFixture = { Position: string }

/// Build a RouteEndpoint stamping HttpMethodMetadata + the handler's own MethodInfo
/// (mirrors Frank's real ResourceSpec.Build, which adds `handler.Method`) plus any extra
/// caller-supplied metadata. #411: DiscoveryMiddleware's ALPS Type correlation now reads
/// Endpoint.Metadata directly (no ApiExplorer inclusion filter), but handler.Method is
/// still stamped here for parity with real Frank-built endpoints.
let routeEndpoint (pattern: string) (methods: string[]) (metadata: obj list) : RouteEndpoint =
    let builder = RoutePatternFactory.Parse pattern
    let handler = RequestDelegate(fun _ -> System.Threading.Tasks.Task.CompletedTask)

    let metadataCollection =
        EndpointMetadataCollection(box (HttpMethodMetadata(methods)) :: box handler.Method :: metadata)

    RouteEndpoint(handler, builder, 0, metadataCollection, null)

/// Build a WebApplication wired for discovery-middleware testing: TestServer, routing,
/// DiscoveryConfig registered, `endpoints` wrapped in a real
/// Frank.Builder.ResourceEndpointDataSource (#411 — the SAME concrete type
/// DiscoveryMiddleware's production constructor receives via WebHostBuilder.Run),
/// registered both as a DI singleton (ALPS Type correlation) and added to
/// IEndpointRouteBuilder.DataSources (actual routing) — mirroring WebHostBuilder.Run's own
/// sequence. `configureBuilder` (when given) runs before `Build()` — e.g. to register a
/// logging provider.
let private buildDiscoveryApp
    (configureBuilder: (WebApplicationBuilder -> unit) option)
    (config: DiscoveryConfig)
    (endpoints: Endpoint[])
    : WebApplication =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    builder.Services.AddRouting() |> ignore
    let dataSource = ResourceEndpointDataSource(endpoints)
    builder.Services.AddSingleton<ResourceEndpointDataSource>(dataSource) |> ignore
    configureBuilder |> Option.iter (fun f -> f builder)
    let app = builder.Build()
    app.UseRouting() |> ignore
    app.UseMiddleware<DiscoveryMiddleware.DiscoveryMiddleware>() |> ignore
    (app :> IEndpointRouteBuilder).DataSources.Add(dataSource)
    app

/// Spin a TestServer with the discovery middleware in front of a couple of
/// routed endpoints. The GET /games/{id} endpoint carries ResourceRelationMetadata
/// so the middleware can build the JSON Home directory at runtime.
let startServer (config: DiscoveryConfig) =
    let endpoints: Endpoint[] =
        [| routeEndpoint
               "/games/{id}"
               [| "GET" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ]
           routeEndpoint "/games/{id}/moves" [| "POST" |] [] |]

    let app = buildDiscoveryApp None config endpoints
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
            Rt = None
            ClassIri = Some "https://schema.org/Game"
            RequestClrTypeName = None }
          { Id = "agent"
            Type = "semantic"
            Doc = None
            Href = Some "https://schema.org/agent"
            Descriptors = []
            Rt = None
            ClassIri = None
            RequestClrTypeName = None } ]
      DescribedByLinks =
        [ { ClassIri = "https://schema.org/Game"
            Link = "<https://schema.org/Game>; rel=\"describedby\"" } ]
      ResourceHrefVars = Map.ofList [ "https://schema.org/Game", Map.ofList [ "id", "https://schema.org/identifier" ] ] }

/// Spin a TestServer where /games/{id} handles both GET and POST under the same
/// ResourceRelationMetadata. Used by the multi-verb merge test (#390).
let startMultiVerbServer (config: DiscoveryConfig) =
    let endpoints: Endpoint[] =
        [| routeEndpoint
               "/games/{id}"
               [| "GET" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ]
           routeEndpoint
               "/games/{id}"
               [| "POST" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ] |]

    let app = buildDiscoveryApp None config endpoints
    app.StartAsync().GetAwaiter().GetResult()
    app

/// Spin a TestServer where two DIFFERENT hrefs share the SAME relation IRI.
/// Used by the duplicate-key guard test (#390 F4).
let startDuplicateRelationServer (config: DiscoveryConfig) =
    let endpoints: Endpoint[] =
        [| routeEndpoint
               "/games/{id}"
               [| "GET" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ]
           // Second, DIFFERENT href with the SAME relation — configuration error scenario.
           routeEndpoint
               "/games/{gid}/variant"
               [| "POST" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ] |]

    let app = buildDiscoveryApp None config endpoints
    app.StartAsync().GetAwaiter().GetResult()
    app

/// Spin a TestServer for duplicate-relation collision with a CapturingLoggerProvider
/// already registered, so the test can assert the warning was emitted.
let startDuplicateRelationServerWithLogCapture (config: DiscoveryConfig) =
    let provider = new CapturingLoggerProvider()

    let endpoints: Endpoint[] =
        [| routeEndpoint
               "/games/{id}"
               [| "GET" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ]
           routeEndpoint
               "/games/{gid}/variant"
               [| "POST" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ] |]

    let app =
        buildDiscoveryApp (Some(fun b -> b.Logging.AddProvider(provider) |> ignore)) config endpoints

    app.StartAsync().GetAwaiter().GetResult()
    provider, app

/// Spin a TestServer with GET /games/{id} (relation=Game), PUT and DELETE /widgets/{id}
/// (single-method relations), and POST /games/{id} carrying IAcceptsMetadata for
/// MoveRequestFixture on the SAME route as the GET (#390 multi-verb) — the AC1 fixture
/// for #397's HTTP-method reconciliation.
let startAlpsTypeServer (config: DiscoveryConfig) =
    let endpoints: Endpoint[] =
        [| routeEndpoint
               "/games/{id}"
               [| "GET" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ]
           routeEndpoint
               "/games/{id}"
               [| "POST" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
                 box (
                     AcceptsMetadata([| "application/json" |], typeof<MoveRequestFixture>, false)
                     :> IAcceptsMetadata
                 ) ]
           routeEndpoint
               "/widgets/{id}"
               [| "PUT" |]
               [ box ({ Relation = "https://schema.org/Widget" }: ResourceRelationMetadata) ]
           routeEndpoint
               "/gadgets/{id}"
               [| "DELETE" |]
               [ box ({ Relation = "https://schema.org/Gadget" }: ResourceRelationMetadata) ] |]

    let app = buildDiscoveryApp None config endpoints
    app.StartAsync().GetAwaiter().GetResult()
    app

/// Spin a TestServer with discovery middleware AND a /tictactoe vocabulary route.
/// Used by the dereference acceptance test (item #6).
let startVocabServer (config: DiscoveryConfig) =
    let endpoints: Endpoint[] = [| routeEndpoint "/tictactoe" [| "GET" |] [] |]
    let app = buildDiscoveryApp None config endpoints
    app.StartAsync().GetAwaiter().GetResult()
    app

/// Spin a TestServer with THREE routes carrying different relation exposure:
///   - "/tictactoe" — routed, but declares NO ResourceRelationMetadata.
///   - "/games/{id}" — declares relation "https://schema.org/Game".
///   - "/" is left unmapped entirely (served only by DiscoveryMiddleware's own
///     JSON Home/OPTIONS handling, never a RouteEndpoint).
/// Used by the rel="type" per-resource scoping acceptance test (#398 AC2).
let startScopedRelationServer (config: DiscoveryConfig) =
    let endpoints: Endpoint[] =
        [| routeEndpoint "/tictactoe" [| "GET" |] []
           routeEndpoint
               "/games/{id}"
               [| "GET" |]
               [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ] |]

    let app = buildDiscoveryApp None config endpoints
    app.StartAsync().GetAwaiter().GetResult()
    app
