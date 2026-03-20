module Frank.Validation.Tests.MiddlewareTests

open System
open System.Net.Http
open System.Text
open System.Threading
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Frank.Validation

// ──────────────────────────────────────────────
// Test domain types
// ──────────────────────────────────────────────

type CreateUser = { Name: string; Age: int }

type SearchQuery = { Q: string; Page: int }

// ──────────────────────────────────────────────
// Test infrastructure
// ──────────────────────────────────────────────

/// Counter to track handler invocations.
type HandlerCounter() =
    let mutable count = 0
    member _.Increment() = Interlocked.Increment(&count) |> ignore
    member _.Count = count

/// Derive a shape for 'T and return its URI.
let private shapeUriFor<'T> () =
    let shape = ShapeBuilder.deriveShapeDefault typeof<'T>
    shape.NodeShapeUri

/// Build a test server with validation middleware and return (counter, client).
/// Accepts a list of types to pre-load into ShapeCache before the test runs.
let private buildTestHost
    (preloadTypes: Type list)
    (configureEndpoints: HandlerCounter -> IEndpointRouteBuilder -> unit)
    : HandlerCounter * HttpClient =
    let counter = HandlerCounter()

    // Pre-compute shapes so we can load them into ShapeCache at startup.
    let preloadedShapes = preloadTypes |> List.map ShapeBuilder.deriveShapeDefault

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton<ShapeCache>() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddLogging() |> ignore
    let app = builder.Build()

    // Pre-populate the shape cache with all registered shapes.
    let shapeCache = app.Services.GetRequiredService<ShapeCache>()
    shapeCache.LoadAll(preloadedShapes)

    app.UseRouting() |> ignore

    (app :> IApplicationBuilder).Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
        let sc = ctx.RequestServices.GetRequiredService<ShapeCache>()

        let loggerFactory = ctx.RequestServices.GetRequiredService<ILoggerFactory>()

        let logger = loggerFactory.CreateLogger("Frank.Validation.ValidationMiddleware")

        let typedLogger =
            { new ILogger<ValidationMiddleware> with
                member _.Log(logLevel, eventId, state, ex, formatter) =
                    logger.Log(logLevel, eventId, state, ex, formatter)

                member _.IsEnabled(logLevel) = logger.IsEnabled(logLevel)
                member _.BeginScope(state) = logger.BeginScope(state) }

        let middleware = ValidationMiddleware(next, sc, typedLogger)
        middleware.InvokeAsync(ctx))
    |> ignore

    app.UseEndpoints(fun endpoints -> configureEndpoints counter endpoints)
    |> ignore

    app.Start()
    counter, app.GetTestClient()

/// Create a POST endpoint with ValidationMarker metadata (keyed by shape URI).
let private mapValidatedPost<'T> (pattern: string) (counter: HandlerCounter) (endpoints: IEndpointRouteBuilder) =
    let uri = shapeUriFor<'T> ()

    endpoints
        .MapPost(
            pattern,
            RequestDelegate(fun ctx ->
                counter.Increment()
                ctx.Response.StatusCode <- 201
                System.Threading.Tasks.Task.CompletedTask)
        )
        .WithMetadata(
            { ShapeUri = uri
              CustomConstraints = []
              ResolverConfig = None }
            : ValidationMarker
        )
    |> ignore

/// Create a POST endpoint WITHOUT ValidationMarker (no validation).
let private mapUnvalidatedPost (pattern: string) (counter: HandlerCounter) (endpoints: IEndpointRouteBuilder) =
    endpoints.MapPost(
        pattern,
        RequestDelegate(fun ctx ->
            counter.Increment()
            ctx.Response.StatusCode <- 200
            System.Threading.Tasks.Task.CompletedTask)
    )
    |> ignore

/// Create a GET endpoint with ValidationMarker metadata (keyed by shape URI).
let private mapValidatedGet<'T> (pattern: string) (counter: HandlerCounter) (endpoints: IEndpointRouteBuilder) =
    let uri = shapeUriFor<'T> ()

    endpoints
        .MapGet(
            pattern,
            RequestDelegate(fun ctx ->
                counter.Increment()
                ctx.Response.StatusCode <- 200
                System.Threading.Tasks.Task.CompletedTask)
        )
        .WithMetadata(
            { ShapeUri = uri
              CustomConstraints = []
              ResolverConfig = None }
            : ValidationMarker
        )
    |> ignore

// ──────────────────────────────────────────────
// T021: Middleware integration tests
// ──────────────────────────────────────────────

[<Tests>]
let validPostTests =
    testList
        "ValidationMiddleware - valid POST"
        [ testCase "valid POST passes through to handler"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<CreateUser> ] (fun c ep -> mapValidatedPost<CreateUser> "/users" c ep)

              let content =
                  new StringContent("""{"Name":"Alice","Age":30}""", Encoding.UTF8, "application/json")

              let response = client.PostAsync("/users", content).Result
              Expect.equal (int response.StatusCode) 201 "Should return 201 from handler"
              Expect.equal counter.Count 1 "Handler should have been invoked once" ]

[<Tests>]
let invalidPostTests =
    testList
        "ValidationMiddleware - invalid POST"
        [ testCase "invalid POST returns 422"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<CreateUser> ] (fun c ep -> mapValidatedPost<CreateUser> "/users" c ep)

              let content = new StringContent("""{"Age":30}""", Encoding.UTF8, "application/json")
              let response = client.PostAsync("/users", content).Result
              Expect.equal (int response.StatusCode) 422 "Should return 422 for invalid data"
              Expect.equal counter.Count 0 "Handler should NOT have been invoked"

          testCase "completely empty JSON object returns 422"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<CreateUser> ] (fun c ep -> mapValidatedPost<CreateUser> "/users" c ep)

              let content = new StringContent("""{}""", Encoding.UTF8, "application/json")
              let response = client.PostAsync("/users", content).Result
              Expect.equal (int response.StatusCode) 422 "Should return 422 for empty object"
              Expect.equal counter.Count 0 "Handler should NOT have been invoked" ]

[<Tests>]
let missingBodyTests =
    testList
        "ValidationMiddleware - missing body"
        [ testCase "missing body on POST returns 422"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<CreateUser> ] (fun c ep -> mapValidatedPost<CreateUser> "/users" c ep)

              let content = new StringContent("", Encoding.UTF8, "application/json")
              let response = client.PostAsync("/users", content).Result
              Expect.equal (int response.StatusCode) 422 "Should return 422 for missing body"
              Expect.equal counter.Count 0 "Handler should NOT have been invoked" ]

[<Tests>]
let nonValidatedEndpointTests =
    testList
        "ValidationMiddleware - non-validated endpoint"
        [ testCase "non-validated endpoint passes through"
          <| fun _ ->
              let counter, client =
                  buildTestHost [] (fun c ep -> mapUnvalidatedPost "/unvalidated" c ep)

              let content =
                  new StringContent("""{"anything":"goes"}""", Encoding.UTF8, "application/json")

              let response = client.PostAsync("/unvalidated", content).Result
              Expect.equal (int response.StatusCode) 200 "Should return 200 from handler"
              Expect.equal counter.Count 1 "Handler should have been invoked" ]

[<Tests>]
let getValidationTests =
    testList
        "ValidationMiddleware - GET query param validation"
        [ testCase "valid GET with query params passes through"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<SearchQuery> ] (fun c ep -> mapValidatedGet<SearchQuery> "/search" c ep)

              let response = client.GetAsync("/search?Q=hello&Page=1").Result
              Expect.equal (int response.StatusCode) 200 "Should return 200 for valid query"
              Expect.equal counter.Count 1 "Handler should have been invoked" ]
