namespace Frank.Benchmarks

open System.Net.Http
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

/// One BenchmarkDotNet class per scenario shape (single / N=3-first / N=3-last /
/// wildcard / 406 / default), each comparing `MediaTypeNegotiation.selectRepresentation`
/// (the direct function call the old `Negotiation.dispatch` used before its deletion)
/// against `FrankProducesMatcherPolicy` dispatch through a real `TestServer`, so
/// BenchmarkDotNet's own summary table does the side-by-side comparison.
[<MemoryDiagnoser>]
type SingleRepresentationBenchmarks() =

    let mediaTypes = [ "application/json" ]
    let acceptValues = [ "application/json" ]

    let mutable host: IHost = Unchecked.defaultof<_>
    let mutable client: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        let handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("json"))
        let pattern = Patterns.RoutePatternFactory.Parse "/x"
        let builder = RouteEndpointBuilder(handler, pattern, 0)
        builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
        builder.Metadata.Add(ProducesMediaTypeMetadata("application/json", 0))
        let endpoint = builder.Build()

        host <-
            Host
                .CreateDefaultBuilder([||])
                .ConfigureWebHost(fun wb ->
                    wb
                        .UseTestServer()
                        .ConfigureServices(fun services ->
                            services.AddRouting() |> ignore
                            services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore)
                        .Configure(fun app ->
                            app.UseRouting() |> ignore
                            app.UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource [| endpoint |]))
                            |> ignore)
                    |> ignore)
                .Build()

        host.StartAsync().GetAwaiter().GetResult()
        client <- host.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        client.Dispose()
        host.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() =
        MediaTypeNegotiation.selectRepresentation acceptValues mediaTypes

    [<Benchmark>]
    member _.RoutingLayerDispatch() =
        client.GetAsync("/x").GetAwaiter().GetResult()

/// N=3 representations registered, Accept picks the first-registered (ordinal 0) one.
[<MemoryDiagnoser>]
type ThreeRepresentationsAcceptFirstBenchmarks() =

    let mediaTypes = [ "application/json"; "text/html"; "application/xml" ]
    let acceptValues = [ "application/json" ]

    let mutable host: IHost = Unchecked.defaultof<_>
    let mutable client: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        let buildEndpoint (mediaType: string) (ordinal: int) (body: string) =
            let handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync(body))
            let pattern = Patterns.RoutePatternFactory.Parse "/x"
            let builder = RouteEndpointBuilder(handler, pattern, 0)
            builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
            builder.Metadata.Add(ProducesMediaTypeMetadata(mediaType, ordinal))
            builder.Build()

        let endpoints =
            [| buildEndpoint "application/json" 0 "json"
               buildEndpoint "text/html" 1 "html"
               buildEndpoint "application/xml" 2 "xml" |]

        host <-
            Host
                .CreateDefaultBuilder([||])
                .ConfigureWebHost(fun wb ->
                    wb
                        .UseTestServer()
                        .ConfigureServices(fun services ->
                            services.AddRouting() |> ignore
                            services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore)
                        .Configure(fun app ->
                            app.UseRouting() |> ignore
                            app.UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                            |> ignore)
                    |> ignore)
                .Build()

        host.StartAsync().GetAwaiter().GetResult()
        client <- host.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        client.Dispose()
        host.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() =
        MediaTypeNegotiation.selectRepresentation acceptValues mediaTypes

    [<Benchmark>]
    member _.RoutingLayerDispatch() =
        use request = new HttpRequestMessage(HttpMethod.Get, "/x")

        for a in acceptValues do
            request.Headers.Accept.ParseAdd(a)

        client.SendAsync(request).GetAwaiter().GetResult()

/// N=3 representations registered, Accept picks the last-registered (ordinal 2) one.
[<MemoryDiagnoser>]
type ThreeRepresentationsAcceptLastBenchmarks() =

    let mediaTypes = [ "application/json"; "text/html"; "application/xml" ]
    let acceptValues = [ "application/xml" ]

    let mutable host: IHost = Unchecked.defaultof<_>
    let mutable client: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        let buildEndpoint (mediaType: string) (ordinal: int) (body: string) =
            let handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync(body))
            let pattern = Patterns.RoutePatternFactory.Parse "/x"
            let builder = RouteEndpointBuilder(handler, pattern, 0)
            builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
            builder.Metadata.Add(ProducesMediaTypeMetadata(mediaType, ordinal))
            builder.Build()

        let endpoints =
            [| buildEndpoint "application/json" 0 "json"
               buildEndpoint "text/html" 1 "html"
               buildEndpoint "application/xml" 2 "xml" |]

        host <-
            Host
                .CreateDefaultBuilder([||])
                .ConfigureWebHost(fun wb ->
                    wb
                        .UseTestServer()
                        .ConfigureServices(fun services ->
                            services.AddRouting() |> ignore
                            services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore)
                        .Configure(fun app ->
                            app.UseRouting() |> ignore
                            app.UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                            |> ignore)
                    |> ignore)
                .Build()

        host.StartAsync().GetAwaiter().GetResult()
        client <- host.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        client.Dispose()
        host.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() =
        MediaTypeNegotiation.selectRepresentation acceptValues mediaTypes

    [<Benchmark>]
    member _.RoutingLayerDispatch() =
        use request = new HttpRequestMessage(HttpMethod.Get, "/x")

        for a in acceptValues do
            request.Headers.Accept.ParseAdd(a)

        client.SendAsync(request).GetAwaiter().GetResult()

/// A `*/*` fallback representation alongside a specific one; Accept requests a media
/// type neither specific representation declares, so the wildcard wins.
[<MemoryDiagnoser>]
type WildcardFallbackBenchmarks() =

    let mediaTypes = [ "application/json"; "*/*" ]
    let acceptValues = [ "image/png" ]

    let mutable host: IHost = Unchecked.defaultof<_>
    let mutable client: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        let buildEndpoint (mediaType: string) (ordinal: int) (body: string) =
            let handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync(body))
            let pattern = Patterns.RoutePatternFactory.Parse "/x"
            let builder = RouteEndpointBuilder(handler, pattern, 0)
            builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
            builder.Metadata.Add(ProducesMediaTypeMetadata(mediaType, ordinal))
            builder.Build()

        let endpoints =
            [| buildEndpoint "application/json" 0 "json"
               buildEndpoint "*/*" 1 "fallback" |]

        host <-
            Host
                .CreateDefaultBuilder([||])
                .ConfigureWebHost(fun wb ->
                    wb
                        .UseTestServer()
                        .ConfigureServices(fun services ->
                            services.AddRouting() |> ignore
                            services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore)
                        .Configure(fun app ->
                            app.UseRouting() |> ignore
                            app.UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                            |> ignore)
                    |> ignore)
                .Build()

        host.StartAsync().GetAwaiter().GetResult()
        client <- host.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        client.Dispose()
        host.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() =
        MediaTypeNegotiation.selectRepresentation acceptValues mediaTypes

    [<Benchmark>]
    member _.RoutingLayerDispatch() =
        use request = new HttpRequestMessage(HttpMethod.Get, "/x")

        for a in acceptValues do
            request.Headers.Accept.ParseAdd(a)

        client.SendAsync(request).GetAwaiter().GetResult()

/// Accept requests a media type no registered representation declares -- baseline
/// measures `selectRepresentation` returning `None` (a miss); routed measures the
/// full 406 round-trip through the matcher policy.
[<MemoryDiagnoser>]
type NotAcceptableBenchmarks() =

    let mediaTypes = [ "application/json" ]
    let acceptValues = [ "application/xml" ]

    let mutable host: IHost = Unchecked.defaultof<_>
    let mutable client: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        let handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("json"))
        let pattern = Patterns.RoutePatternFactory.Parse "/x"
        let builder = RouteEndpointBuilder(handler, pattern, 0)
        builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
        builder.Metadata.Add(ProducesMediaTypeMetadata("application/json", 0))
        let endpoint = builder.Build()

        host <-
            Host
                .CreateDefaultBuilder([||])
                .ConfigureWebHost(fun wb ->
                    wb
                        .UseTestServer()
                        .ConfigureServices(fun services ->
                            services.AddRouting() |> ignore
                            services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore)
                        .Configure(fun app ->
                            app.UseRouting() |> ignore
                            app.UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource [| endpoint |]))
                            |> ignore)
                    |> ignore)
                .Build()

        host.StartAsync().GetAwaiter().GetResult()
        client <- host.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        client.Dispose()
        host.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() =
        MediaTypeNegotiation.selectRepresentation acceptValues mediaTypes

    [<Benchmark>]
    member _.RoutingLayerDispatch() =
        use request = new HttpRequestMessage(HttpMethod.Get, "/x")

        for a in acceptValues do
            request.Headers.Accept.ParseAdd(a)

        client.SendAsync(request).GetAwaiter().GetResult()

/// No Accept header sent on either side -- the default/ordinal-0 representation wins.
[<MemoryDiagnoser>]
type DefaultRepresentationBenchmarks() =

    let mediaTypes = [ "application/json"; "text/html" ]
    let acceptValues: string list = []

    let mutable host: IHost = Unchecked.defaultof<_>
    let mutable client: HttpClient = Unchecked.defaultof<_>

    [<GlobalSetup>]
    member _.Setup() =
        let buildEndpoint (mediaType: string) (ordinal: int) (body: string) =
            let handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync(body))
            let pattern = Patterns.RoutePatternFactory.Parse "/x"
            let builder = RouteEndpointBuilder(handler, pattern, 0)
            builder.Metadata.Add(HttpMethodMetadata [| "GET" |])
            builder.Metadata.Add(ProducesMediaTypeMetadata(mediaType, ordinal))
            builder.Build()

        let endpoints =
            [| buildEndpoint "application/json" 0 "json"
               buildEndpoint "text/html" 1 "html" |]

        host <-
            Host
                .CreateDefaultBuilder([||])
                .ConfigureWebHost(fun wb ->
                    wb
                        .UseTestServer()
                        .ConfigureServices(fun services ->
                            services.AddRouting() |> ignore
                            services.AddSingleton<MatcherPolicy, FrankProducesMatcherPolicy>() |> ignore)
                        .Configure(fun app ->
                            app.UseRouting() |> ignore
                            app.UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                            |> ignore)
                    |> ignore)
                .Build()

        host.StartAsync().GetAwaiter().GetResult()
        client <- host.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        client.Dispose()
        host.Dispose()

    [<Benchmark(Baseline = true)>]
    member _.DirectFunctionDispatch() =
        MediaTypeNegotiation.selectRepresentation acceptValues mediaTypes

    [<Benchmark>]
    member _.RoutingLayerDispatch() =
        // No Accept header set on the HttpRequestMessage -- acceptValues is empty.
        use request = new HttpRequestMessage(HttpMethod.Get, "/x")

        for a in acceptValues do
            request.Headers.Accept.ParseAdd(a)

        client.SendAsync(request).GetAwaiter().GetResult()
