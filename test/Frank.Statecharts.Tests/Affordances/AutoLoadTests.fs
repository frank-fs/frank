module Frank.Affordances.Tests.AutoLoadTests

open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Reflection
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open Expecto
open Frank.Builder
open Frank.Affordances
open Frank.Tests.Shared.TestEndpointDataSource
open MiddlewareTests

type CapturingLogger() =
    let entries = ResizeArray<LogLevel * string>()
    member _.Entries: IReadOnlyList<LogLevel * string> = entries
    interface ILogger with
        member _.BeginScope<'TState>(_state: 'TState) = { new System.IDisposable with member _.Dispose() = () }
        member _.IsEnabled(_level) = true
        member _.Log<'TState>(level, _eventId, state: 'TState, ex, formatter) =
            entries.Add(level, formatter.Invoke(state, ex))

[<Tests>]
let autoLoadTests =
    testList "useAffordances auto-load" [
        testCase "loadAffordanceMapFromAssembly returns None for assembly without model.bin" <| fun _ ->
            // The test assembly has Frank.Descriptors.bin, not model.bin
            let assembly = Assembly.GetExecutingAssembly()
            let result = StartupProjection.loadAffordanceMapFromAssembly NullLogger.Instance assembly
            Expect.isNone result "should return None when no model.bin is present"

        testCase "loadUnifiedStateFromAssembly logs warning on corrupt model.bin" <| fun _ ->
            // The test assembly embeds Frank.Corrupt.model.bin (ends with model.bin)
            let assembly = Assembly.GetExecutingAssembly()
            let logger = CapturingLogger()
            let result = StartupProjection.loadUnifiedStateFromAssembly logger assembly
            Expect.isNone result "should return None for corrupt data"
            Expect.isNonEmpty (logger.Entries :> _ seq) "should have logged a warning"
            let (level, msg) = logger.Entries[0]
            Expect.equal level LogLevel.Warning "should log at Warning level"
            Expect.stringContains msg "Failed to deserialize model.bin" "warning should describe deserialization failure"

        testTask "useAffordances no-arg creates working server without error" {
            let testResource =
                resource "/items" {
                    get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("items")))
                }

            let ceBuilder = WebHostBuilder([||])
            let spec =
                ceBuilder.Yield()
                |> fun s -> ceBuilder.UseAffordances(s)
                |> fun s -> ceBuilder.Resource(s, testResource)

            // Build a minimal test server from the spec (replicates CE Run pipeline)
            let builder = WebApplication.CreateBuilder([||])
            builder.WebHost.UseTestServer() |> ignore
            spec.Services(builder.Services) |> ignore
            let app = builder.Build()
            (app :> IApplicationBuilder)
            |> spec.BeforeRoutingMiddleware
            |> fun app -> app.UseRouting()
            |> spec.Middleware
            |> ignore
            (app :> Microsoft.AspNetCore.Routing.IEndpointRouteBuilder).DataSources.Add(
                TestEndpointDataSource(spec.Endpoints))
            app.Start()
            let client = app.GetTestClient()

            try
                let! (resp: HttpResponseMessage) = client.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/items"))
                Expect.equal resp.StatusCode HttpStatusCode.OK "GET should return 200"
                let! (body: string) = resp.Content.ReadAsStringAsync()
                Expect.equal body "items" "should get handler response"
            finally
                client.Dispose()
                (app :> System.IDisposable).Dispose()
        }
    ]
