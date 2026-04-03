module Frank.Affordances.Tests.AffordanceTestHelpers

open System
open System.Collections.Generic
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Frank.Affordances
open Frank.Resources.Model

/// Build a pre-computed affordance lookup dictionary for test scenarios.
let buildAffordanceLookup (entries: (string * PreComputedAffordance) list) =
    let dict = Dictionary<string, PreComputedAffordance>(StringComparer.Ordinal)

    for key, value in entries do
        dict.[key] <- value

    dict

/// Get a header value from the response, checking both response and content headers.
let getHeaderValues (response: HttpResponseMessage) (name: string) : string list =
    let mutable values = Seq.empty

    if response.Headers.TryGetValues(name, &values) then
        values |> Seq.toList
    elif not (isNull response.Content) && response.Content.Headers.TryGetValues(name, &values) then
        values |> Seq.toList
    else
        []

/// Check whether a header exists in either response or content headers.
let hasHeader (response: HttpResponseMessage) (name: string) : bool =
    getHeaderValues response name |> List.isEmpty |> not

/// Shared test affordance fixtures.
let xTurnAffordance =
    { AllowHeaderValue = StringValues("GET, POST")
      LinkHeaderValues =
        StringValues(
            [| "<https://example.com/alps/games>; rel=\"profile\""
               "</games/123/move>; rel=\"makeMove\"" |]
        )
      HasTemplateLinks = false
      Methods = [ "GET"; "POST" ] }

let wonAffordance =
    { AllowHeaderValue = StringValues("GET")
      LinkHeaderValues = StringValues([| "<https://example.com/alps/games>; rel=\"profile\"" |])
      HasTemplateLinks = false
      Methods = [ "GET" ] }

let healthAffordance =
    { AllowHeaderValue = StringValues("GET")
      LinkHeaderValues = StringValues([| "<https://example.com/alps/health>; rel=\"profile\"" |])
      HasTemplateLinks = false
      Methods = [ "GET" ] }

/// Run a test against a configured test server with AffordanceMiddleware,
/// disposing all resources on completion.
let withAffordanceServer
    (lookup: Dictionary<string, PreComputedAffordance>)
    (featureSetter: HttpContext -> unit)
    (configureEndpoints: IEndpointRouteBuilder -> unit)
    (f: HttpClient -> Task)
    =
    task {
        let builder = WebApplication.CreateBuilder([||])
        builder.WebHost.UseTestServer() |> ignore
        builder.Services.AddRouting() |> ignore
        builder.Services.AddSingleton<Dictionary<string, PreComputedAffordance>>(lookup) |> ignore
        let app = builder.Build()

        app.UseRouting() |> ignore

        (app :> IApplicationBuilder)
            .Use(fun ctx (next: Func<Task>) ->
                featureSetter ctx
                next.Invoke())
        |> ignore

        (app :> IApplicationBuilder).UseMiddleware<AffordanceMiddleware>()
        |> ignore

        app.UseEndpoints(configureEndpoints) |> ignore

        app.Start()
        let server = app.GetTestServer()
        let client = server.CreateClient()

        try
            do! f client
        finally
            client.Dispose()
            server.Dispose()
            (app :> IDisposable).Dispose()
    }
    :> Task

/// Default test endpoints: /games/{gameId} and /health.
let defaultEndpoints (endpoints: IEndpointRouteBuilder) =
    endpoints.MapGet(
        "/games/{gameId}",
        RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK"))
    )
    |> ignore

    endpoints.MapGet(
        "/health",
        RequestDelegate(fun ctx -> ctx.Response.WriteAsync("healthy"))
    )
    |> ignore
