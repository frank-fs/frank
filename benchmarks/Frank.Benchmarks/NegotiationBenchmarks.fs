namespace Frank.Benchmarks

open System.Net.Http
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Matching
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Frank.Builder

type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

/// Shared scenario wiring. BOTH sides of every comparison in this file are a full
/// `TestServer` HTTP round-trip against the same route, differing only in HOW the
/// representation is resolved -- by a `selectRepresentation` scan inside a single
/// endpoint's `RequestDelegate` (the pre-branch architecture) versus by
/// `FrankProducesMatcherPolicy` at the routing layer among one endpoint per
/// representation (the current one). Comparing the bare function call against a
/// round-trip would only measure TestServer/HTTP overhead, which the old code paid
/// too -- it ran inside a routed handler, on top of the same pipeline.
module private Scenario =

    let private routePattern () = Patterns.RoutePatternFactory.Parse "/x"

    /// The pre-branch architecture: ONE endpoint carrying no `ProducesMediaTypeMetadata`,
    /// whose `RequestDelegate` scans the registered media types itself. Reproduces what the
    /// deleted `Negotiation.dispatch` did per request -- append `Vary: Accept`, set
    /// `Content-Type` from the winner unless it is a wildcard, bodyless 406 on no match.
    let directDispatchEndpoint (representations: (string * string) list) : Endpoint =
        let mediaTypes = representations |> List.map fst
        let bodies = representations |> List.map snd

        let handler =
            RequestDelegate(fun ctx ->
                ctx.Response.Headers.Append("Vary", "Accept")
                let acceptValues = ctx.Request.Headers.Accept |> Array.ofSeq

                match MediaTypeNegotiation.selectRepresentation acceptValues mediaTypes with
                | Some index ->
                    let mediaType = mediaTypes.[index]

                    if not (MediaTypeNegotiation.isWildcard mediaType) then
                        ctx.Response.ContentType <- mediaType

                    ctx.Response.WriteAsync(bodies.[index])
                | None ->
                    ctx.Response.StatusCode <- StatusCodes.Status406NotAcceptable
                    Task.CompletedTask)

        let builder = RouteEndpointBuilder(handler, routePattern (), 0)
        builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
        builder.Build()

    /// The current architecture: one `RouteEndpoint` per representation, each tagged with
    /// its `ProducesMediaTypeMetadata`, dispatched by `FrankProducesMatcherPolicy`.
    let routedEndpoints (representations: (string * string) list) : Endpoint[] =
        representations
        |> List.mapi (fun ordinal (mediaType, body) ->
            let handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync(body))
            let builder = RouteEndpointBuilder(handler, routePattern (), 0)
            builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
            builder.Metadata.Add(ProducesMediaTypeMetadata(mediaType, ordinal))
            builder.Build())
        |> Array.ofList

    /// The matcher policy is registered only for the routed variant -- the direct variant
    /// must not pay for a policy the pre-branch architecture never had.
    let startHost (withMatcherPolicy: bool) (endpoints: Endpoint[]) : IHost =
        let host =
            Host
                .CreateDefaultBuilder([||])
                .ConfigureWebHost(fun wb ->
                    wb
                        .UseTestServer()
                        .ConfigureServices(fun services ->
                            services.AddRouting() |> ignore

                            if withMatcherPolicy then
                                services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore)
                        .Configure(fun app ->
                            app.UseRouting() |> ignore
                            app.UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                            |> ignore)
                    |> ignore)
                .Build()

        host.StartAsync().GetAwaiter().GetResult()
        host

    /// One GET /x round-trip carrying `acceptValues` as its Accept header (none at all when
    /// the list is empty), sent identically to both hosts.
    let get (client: HttpClient) (acceptValues: string list) =
        use request = new HttpRequestMessage(HttpMethod.Get, "/x")

        for a in acceptValues do
            request.Headers.Accept.ParseAdd(a)

        client.SendAsync(request).GetAwaiter().GetResult()

/// One BenchmarkDotNet class per scenario shape (single / N=3-first / N=3-last /
/// wildcard / 406 / default), each comparing in-handler `selectRepresentation` dispatch
/// (the baseline -- what `Negotiation.dispatch` did before its deletion) against
/// `FrankProducesMatcherPolicy` dispatch, both through a real `TestServer`, so
/// BenchmarkDotNet's own summary table does the side-by-side comparison.
[<MemoryDiagnoser>]
type SingleRepresentationBenchmarks() =

    let representations = [ "application/json", "json" ]
    let acceptValues = [ "application/json" ]

    let mutable directHost: IHost = Unchecked.defaultof<_>
    let mutable directClient: HttpClient = Unchecked.defaultof<_>
    let mutable routedHost: IHost = Unchecked.defaultof<_>
    let mutable routedClient: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        directHost <- Scenario.startHost false [| Scenario.directDispatchEndpoint representations |]
        directClient <- directHost.GetTestClient()
        routedHost <- Scenario.startHost true (Scenario.routedEndpoints representations)
        routedClient <- routedHost.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        directClient.Dispose()
        directHost.Dispose()
        routedClient.Dispose()
        routedHost.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() = Scenario.get directClient acceptValues

    [<Benchmark>]
    member _.RoutingLayerDispatch() = Scenario.get routedClient acceptValues

/// N=3 representations registered, Accept picks the first-registered (ordinal 0) one.
[<MemoryDiagnoser>]
type ThreeRepresentationsAcceptFirstBenchmarks() =

    let representations =
        [ "application/json", "json"; "text/html", "html"; "application/xml", "xml" ]

    let acceptValues = [ "application/json" ]

    let mutable directHost: IHost = Unchecked.defaultof<_>
    let mutable directClient: HttpClient = Unchecked.defaultof<_>
    let mutable routedHost: IHost = Unchecked.defaultof<_>
    let mutable routedClient: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        directHost <- Scenario.startHost false [| Scenario.directDispatchEndpoint representations |]
        directClient <- directHost.GetTestClient()
        routedHost <- Scenario.startHost true (Scenario.routedEndpoints representations)
        routedClient <- routedHost.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        directClient.Dispose()
        directHost.Dispose()
        routedClient.Dispose()
        routedHost.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() = Scenario.get directClient acceptValues

    [<Benchmark>]
    member _.RoutingLayerDispatch() = Scenario.get routedClient acceptValues

/// N=3 representations registered, Accept picks the last-registered (ordinal 2) one.
[<MemoryDiagnoser>]
type ThreeRepresentationsAcceptLastBenchmarks() =

    let representations =
        [ "application/json", "json"; "text/html", "html"; "application/xml", "xml" ]

    let acceptValues = [ "application/xml" ]

    let mutable directHost: IHost = Unchecked.defaultof<_>
    let mutable directClient: HttpClient = Unchecked.defaultof<_>
    let mutable routedHost: IHost = Unchecked.defaultof<_>
    let mutable routedClient: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        directHost <- Scenario.startHost false [| Scenario.directDispatchEndpoint representations |]
        directClient <- directHost.GetTestClient()
        routedHost <- Scenario.startHost true (Scenario.routedEndpoints representations)
        routedClient <- routedHost.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        directClient.Dispose()
        directHost.Dispose()
        routedClient.Dispose()
        routedHost.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() = Scenario.get directClient acceptValues

    [<Benchmark>]
    member _.RoutingLayerDispatch() = Scenario.get routedClient acceptValues

/// A `*/*` fallback representation alongside a specific one; Accept requests a media
/// type neither specific representation declares, so the wildcard wins.
[<MemoryDiagnoser>]
type WildcardFallbackBenchmarks() =

    let representations = [ "application/json", "json"; "*/*", "fallback" ]
    let acceptValues = [ "image/png" ]

    let mutable directHost: IHost = Unchecked.defaultof<_>
    let mutable directClient: HttpClient = Unchecked.defaultof<_>
    let mutable routedHost: IHost = Unchecked.defaultof<_>
    let mutable routedClient: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        directHost <- Scenario.startHost false [| Scenario.directDispatchEndpoint representations |]
        directClient <- directHost.GetTestClient()
        routedHost <- Scenario.startHost true (Scenario.routedEndpoints representations)
        routedClient <- routedHost.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        directClient.Dispose()
        directHost.Dispose()
        routedClient.Dispose()
        routedHost.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() = Scenario.get directClient acceptValues

    [<Benchmark>]
    member _.RoutingLayerDispatch() = Scenario.get routedClient acceptValues

/// Accept requests a media type no registered representation declares -- both variants
/// pay the full round-trip and answer 406, one deciding that inside the handler, the
/// other at the routing layer.
[<MemoryDiagnoser>]
type NotAcceptableBenchmarks() =

    let representations = [ "application/json", "json" ]
    let acceptValues = [ "application/xml" ]

    let mutable directHost: IHost = Unchecked.defaultof<_>
    let mutable directClient: HttpClient = Unchecked.defaultof<_>
    let mutable routedHost: IHost = Unchecked.defaultof<_>
    let mutable routedClient: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        directHost <- Scenario.startHost false [| Scenario.directDispatchEndpoint representations |]
        directClient <- directHost.GetTestClient()
        routedHost <- Scenario.startHost true (Scenario.routedEndpoints representations)
        routedClient <- routedHost.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        directClient.Dispose()
        directHost.Dispose()
        routedClient.Dispose()
        routedHost.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() = Scenario.get directClient acceptValues

    [<Benchmark>]
    member _.RoutingLayerDispatch() = Scenario.get routedClient acceptValues

/// No Accept header sent to either host -- the default/ordinal-0 representation wins.
[<MemoryDiagnoser>]
type DefaultRepresentationBenchmarks() =

    let representations = [ "application/json", "json"; "text/html", "html" ]
    let acceptValues: string list = []

    let mutable directHost: IHost = Unchecked.defaultof<_>
    let mutable directClient: HttpClient = Unchecked.defaultof<_>
    let mutable routedHost: IHost = Unchecked.defaultof<_>
    let mutable routedClient: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        directHost <- Scenario.startHost false [| Scenario.directDispatchEndpoint representations |]
        directClient <- directHost.GetTestClient()
        routedHost <- Scenario.startHost true (Scenario.routedEndpoints representations)
        routedClient <- routedHost.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        directClient.Dispose()
        directHost.Dispose()
        routedClient.Dispose()
        routedHost.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() = Scenario.get directClient acceptValues

    [<Benchmark>]
    member _.RoutingLayerDispatch() = Scenario.get routedClient acceptValues
