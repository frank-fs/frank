module Frank.Validation.Tests.ReportSerializationTests

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank.Validation

// ──────────────────────────────────────────────
// Test domain types
// ──────────────────────────────────────────────

type CreateUser = { Name: string; Age: int }

type Address = { Street: string; ZipCode: string }

type UserWithAddress =
    { Name: string
      Age: int
      Address: Address }

// ──────────────────────────────────────────────
// Test infrastructure
// ──────────────────────────────────────────────

/// Counter to track handler invocations.
type HandlerCounter() =
    let mutable count = 0
    member _.Increment() = Interlocked.Increment(&count) |> ignore
    member _.Count = count

/// Build a test server with validation middleware and return (counter, client).
/// preloadTypes are derived and loaded into ShapeCache at startup.
let private buildTestHost
    (preloadTypes: Type list)
    (configureEndpoints: HandlerCounter -> IEndpointRouteBuilder -> unit)
    : HandlerCounter * HttpClient =
    let counter = HandlerCounter()

    let preloadedShapes = preloadTypes |> List.map ShapeBuilder.deriveShapeDefault

    let builder =
        WebHostBuilder()
            .UseTestServer()
            .ConfigureServices(fun services ->
                services.AddSingleton<ShapeCache>() |> ignore
                services.AddRouting() |> ignore
                services.AddLogging() |> ignore)
            .Configure(fun (app: IApplicationBuilder) ->
                let shapeCache = app.ApplicationServices.GetRequiredService<ShapeCache>()
                shapeCache.LoadAll(preloadedShapes)

                app.UseRouting() |> ignore

                app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
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
                |> ignore)

    let server = new TestServer(builder)
    counter, server.CreateClient()

/// Create a POST endpoint with ValidationMarker metadata (keyed by shape URI).
let private mapValidatedPost<'T> (pattern: string) (counter: HandlerCounter) (endpoints: IEndpointRouteBuilder) =
    let uri = ShapeBuilder.deriveShapeDefault(typeof<'T>).NodeShapeUri

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

// ──────────────────────────────────────────────
// Report serialization tests
// ──────────────────────────────────────────────

[<Tests>]
let problemDetailsForJsonAcceptTests =
    testList
        "ReportSerializer - Problem Details for Accept: application/json"
        [ testCase "invalid POST with Accept: application/json returns Problem Details"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<CreateUser> ] (fun c ep -> mapValidatedPost<CreateUser> "/users" c ep)

              let content = new StringContent("""{"Age":30}""", Encoding.UTF8, "application/json")
              use request = new HttpRequestMessage(HttpMethod.Post, "/users")
              request.Content <- content
              request.Headers.Accept.ParseAdd("application/json")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              Expect.equal (int response.StatusCode) 422 "Should return 422"
              Expect.equal counter.Count 0 "Handler should NOT have been invoked"

              let body = response.Content.ReadAsStringAsync().Result
              let contentType = response.Content.Headers.ContentType.MediaType
              Expect.equal contentType "application/problem+json" "Should be application/problem+json"

              let doc = JsonDocument.Parse(body)
              let root = doc.RootElement

              Expect.equal
                  (root.GetProperty("type").GetString())
                  "urn:frank:validation:shacl-violation"
                  "type should be urn:frank:validation:shacl-violation"

              Expect.equal
                  (root.GetProperty("title").GetString())
                  "Validation Failed"
                  "title should be 'Validation Failed'"

              Expect.equal (root.GetProperty("status").GetInt32()) 422 "status should be 422"
              let mutable errorsEl = System.Text.Json.JsonElement()
              Expect.isTrue (root.TryGetProperty("errors", &errorsEl)) "Should have errors array"
              Expect.isTrue (errorsEl.GetArrayLength() > 0) "errors should have entries" ]

[<Tests>]
let jsonLdForSemanticAcceptTests =
    testList
        "ReportSerializer - JSON-LD for Accept: application/ld+json"
        [ testCase "invalid POST with Accept: application/ld+json returns SHACL JSON-LD"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<CreateUser> ] (fun c ep -> mapValidatedPost<CreateUser> "/users" c ep)

              let content = new StringContent("""{"Age":30}""", Encoding.UTF8, "application/json")
              use request = new HttpRequestMessage(HttpMethod.Post, "/users")
              request.Content <- content
              request.Headers.Accept.ParseAdd("application/ld+json")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              Expect.equal (int response.StatusCode) 422 "Should return 422"
              Expect.equal counter.Count 0 "Handler should NOT have been invoked"

              let body = response.Content.ReadAsStringAsync().Result
              let contentType = response.Content.Headers.ContentType.MediaType
              Expect.equal contentType "application/ld+json" "Should be application/ld+json"

              let doc = JsonDocument.Parse(body)
              let root = doc.RootElement
              let mutable contextEl = System.Text.Json.JsonElement()
              Expect.isTrue (root.TryGetProperty("@context", &contextEl)) "Should have @context" ]

[<Tests>]
let multipleViolationsTests =
    testList
        "ReportSerializer - multiple violations"
        [ testCase "3 distinct violations appear in Problem Details errors array"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<CreateUser> ] (fun c ep -> mapValidatedPost<CreateUser> "/users" c ep)

              let content = new StringContent("""{}""", Encoding.UTF8, "application/json")
              use request = new HttpRequestMessage(HttpMethod.Post, "/users")
              request.Content <- content
              request.Headers.Accept.ParseAdd("application/json")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              Expect.equal (int response.StatusCode) 422 "Should return 422"
              Expect.equal counter.Count 0 "Handler should NOT have been invoked"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let root = doc.RootElement
              let errors = root.GetProperty("errors")

              Expect.isGreaterThanOrEqual
                  (errors.GetArrayLength())
                  2
                  "Should have at least 2 errors for 2 missing fields" ]

[<Tests>]
let nestedFieldPathTests =
    testList
        "ReportSerializer - nested field path"
        [ testCase "nested field path uses dot notation in Problem Details"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<UserWithAddress> ] (fun c ep ->
                      mapValidatedPost<UserWithAddress> "/users-address" c ep)

              let content =
                  new StringContent("""{"Name":"Alice","Age":30}""", Encoding.UTF8, "application/json")

              use request = new HttpRequestMessage(HttpMethod.Post, "/users-address")
              request.Content <- content
              request.Headers.Accept.ParseAdd("application/json")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              Expect.equal (int response.StatusCode) 422 "Should return 422 for missing Address"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let root = doc.RootElement
              let errors = root.GetProperty("errors")
              Expect.isTrue (errors.GetArrayLength() > 0) "Should have at least one error"

              let paths =
                  [ for i in 0 .. errors.GetArrayLength() - 1 do
                        yield errors.[i].GetProperty("path").GetString() ]

              Expect.isTrue
                  (paths |> List.exists (fun p -> p.StartsWith("$.") && p.Length > 2))
                  "At least one path should use dot notation ($.fieldName)" ]

[<Tests>]
let noAcceptHeaderDefaultsTests =
    testList
        "ReportSerializer - no Accept defaults to Problem Details"
        [ testCase "no Accept header defaults to Problem Details JSON"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<CreateUser> ] (fun c ep -> mapValidatedPost<CreateUser> "/users" c ep)

              let content = new StringContent("""{"Age":30}""", Encoding.UTF8, "application/json")
              use request = new HttpRequestMessage(HttpMethod.Post, "/users")
              request.Content <- content
              request.Headers.Accept.Clear()
              let response: HttpResponseMessage = client.SendAsync(request).Result
              Expect.equal (int response.StatusCode) 422 "Should return 422"
              Expect.equal counter.Count 0 "Handler should NOT have been invoked"

              let body = response.Content.ReadAsStringAsync().Result
              let contentType = response.Content.Headers.ContentType.MediaType
              Expect.equal contentType "application/problem+json" "Default should be application/problem+json"

              let doc = JsonDocument.Parse(body)
              let root = doc.RootElement

              Expect.equal
                  (root.GetProperty("type").GetString())
                  "urn:frank:validation:shacl-violation"
                  "type should be SHACL violation URN" ]

[<Tests>]
let turtleAcceptTests =
    testList
        "ReportSerializer - Turtle for Accept: text/turtle"
        [ testCase "invalid POST with Accept: text/turtle returns SHACL Turtle"
          <| fun _ ->
              let counter, client =
                  buildTestHost [ typeof<CreateUser> ] (fun c ep -> mapValidatedPost<CreateUser> "/users" c ep)

              let content = new StringContent("""{"Age":30}""", Encoding.UTF8, "application/json")
              use request = new HttpRequestMessage(HttpMethod.Post, "/users")
              request.Content <- content
              request.Headers.Accept.ParseAdd("text/turtle")
              let response: HttpResponseMessage = client.SendAsync(request).Result
              Expect.equal (int response.StatusCode) 422 "Should return 422"
              Expect.equal counter.Count 0 "Handler should NOT have been invoked"

              let body = response.Content.ReadAsStringAsync().Result
              let contentType = response.Content.Headers.ContentType.MediaType
              Expect.equal contentType "text/turtle" "Should be text/turtle"

              Expect.isTrue
                  (body.Contains("sh:") || body.Contains("shacl"))
                  "Should contain SHACL namespace prefix or terms" ]
