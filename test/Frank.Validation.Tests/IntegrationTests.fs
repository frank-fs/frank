module Frank.Validation.Tests.IntegrationTests

// ──────────────────────────────────────────────
// T043-T049: End-to-end validation pipeline tests
//
// These tests exercise the full validation pipeline using TestHost,
// covering the CreateCustomer domain type defined in the WP spec.
// ──────────────────────────────────────────────

open System
open System.Net.Http
open System.Security.Claims
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Expecto
open Frank.Validation

// ──────────────────────────────────────────────
// T043: Domain types
// ──────────────────────────────────────────────

/// Primary domain type used across T043-T049.
type CreateCustomer =
    { Name: string
      Email: string
      Age: int
      Notes: string option }

/// Simple product type for custom constraint tests (T048).
type ProductOrder =
    { ProductName: string
      Quantity: int
      UnitPrice: decimal }

/// Restricted customer type used for capability tests (T047).
/// Admins receive this unrestricted shape; regular users receive CreateCustomer.
type AdminCreateCustomer =
    { Name: string
      Email: string
      Age: int
      Notes: string option
      InternalCode: string }

// ──────────────────────────────────────────────
// T043: Test infrastructure
// ──────────────────────────────────────────────

/// Tracks how many times a handler was invoked during a test.
type HandlerCounter() =
    let mutable count = 0
    member _.Increment() = Interlocked.Increment(&count) |> ignore
    member _.Count = count

    member _.Reset() =
        Interlocked.Exchange(&count, 0) |> ignore

/// Test scheme name for capability-dependent tests (T047).
[<Literal>]
let private TestScheme = "IntegrationTestScheme"

/// Authentication handler that reads the caller's identity from X-Test-User
/// and claims from X-Test-Claims (format: "type=value;type2=value2").
type private TestAuthHandler(options, logger, encoder) =
    inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

    override this.HandleAuthenticateAsync() =
        let headerValue = this.Request.Headers["X-Test-User"].ToString()

        if String.IsNullOrEmpty headerValue then
            Task.FromResult(AuthenticateResult.NoResult())
        else
            let claims = ResizeArray<Claim>()
            claims.Add(Claim(ClaimTypes.Name, headerValue))

            let claimsHeader = this.Request.Headers["X-Test-Claims"].ToString()

            if not (String.IsNullOrEmpty claimsHeader) then
                for part in claimsHeader.Split(';') do
                    let kv = part.Split('=', 2)

                    if kv.Length = 2 then
                        claims.Add(Claim(kv[0], kv[1]))

            let identity = ClaimsIdentity(claims, TestScheme)
            let principal = ClaimsPrincipal(identity)
            let ticket = AuthenticationTicket(principal, TestScheme)
            Task.FromResult(AuthenticateResult.Success(ticket))

/// Derive and return the SHACL shape URI for type 'T.
let private shapeUriFor<'T> () =
    (ShapeBuilder.deriveShapeDefault typeof<'T>).NodeShapeUri

/// Build a minimal test host with the validation middleware.
///
/// preloadTypes: shapes to derive and load into ShapeCache at startup.
/// configureEndpoints: callback to register endpoints on the IEndpointRouteBuilder.
/// withAuth: when true, registers the TestAuthHandler so X-Test-User/Claims are honoured.
let private buildTestHost
    (preloadTypes: Type list)
    (configureEndpoints: HandlerCounter -> IEndpointRouteBuilder -> unit)
    (withAuth: bool)
    : HandlerCounter * HttpClient =

    let counter = HandlerCounter()
    let preloadedShapes = preloadTypes |> List.map ShapeBuilder.deriveShapeDefault

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton<ShapeCache>() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddLogging() |> ignore

    if withAuth then
        builder.Services
            .AddAuthentication(TestScheme)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, fun _ -> ())
        |> ignore

    let app = builder.Build()

    // Pre-populate the shape cache before middleware runs.
    let shapeCache = app.Services.GetRequiredService<ShapeCache>()
    shapeCache.LoadAll(preloadedShapes)

    app.UseRouting() |> ignore

    if withAuth then
        app.UseAuthentication() |> ignore

    // Wire ValidationMiddleware manually so tests work without
    // GetEntryAssembly (which is null in test hosts).
    (app :> IApplicationBuilder).Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
        let sc = ctx.RequestServices.GetRequiredService<ShapeCache>()
        let loggerFactory = ctx.RequestServices.GetRequiredService<ILoggerFactory>()

        let innerLogger =
            loggerFactory.CreateLogger("Frank.Validation.ValidationMiddleware")

        let typedLogger =
            { new ILogger<ValidationMiddleware> with
                member _.Log(logLevel, eventId, state, ex, formatter) =
                    innerLogger.Log(logLevel, eventId, state, ex, formatter)

                member _.IsEnabled(logLevel) = innerLogger.IsEnabled(logLevel)
                member _.BeginScope(state) = innerLogger.BeginScope(state) }

        let middleware = ValidationMiddleware(next, sc, typedLogger)
        middleware.InvokeAsync(ctx))
    |> ignore

    app.UseEndpoints(fun endpoints -> configureEndpoints counter endpoints)
    |> ignore

    app.Start()
    counter, app.GetTestClient()

/// Register a validated POST endpoint for type 'T at the given pattern.
let private mapValidatedPost<'T>
    (pattern: string)
    (statusOnSuccess: int)
    (counter: HandlerCounter)
    (endpoints: IEndpointRouteBuilder)
    =
    let uri = shapeUriFor<'T> ()

    endpoints
        .MapPost(
            pattern,
            RequestDelegate(fun ctx ->
                counter.Increment()
                ctx.Response.StatusCode <- statusOnSuccess
                Task.CompletedTask)
        )
        .WithMetadata(
            { ShapeUri = uri
              CustomConstraints = []
              ResolverConfig = None }
            : ValidationMarker
        )
    |> ignore

/// Register a validated POST endpoint that resolves shapes based on caller claims.
let private mapCapabilityPost
    (pattern: string)
    (resolverConfig: ShapeResolverConfig)
    (counter: HandlerCounter)
    (endpoints: IEndpointRouteBuilder)
    =
    let baseUri = resolverConfig.BaseShape.NodeShapeUri

    endpoints
        .MapPost(
            pattern,
            RequestDelegate(fun ctx ->
                counter.Increment()
                ctx.Response.StatusCode <- 200
                Task.CompletedTask)
        )
        .WithMetadata(
            { ShapeUri = baseUri
              CustomConstraints = []
              ResolverConfig = Some resolverConfig }
            : ValidationMarker
        )
    |> ignore

/// Register an unvalidated POST endpoint (no ValidationMarker).
let private mapUnvalidatedPost (pattern: string) (counter: HandlerCounter) (endpoints: IEndpointRouteBuilder) =
    endpoints.MapPost(
        pattern,
        RequestDelegate(fun ctx ->
            counter.Increment()
            ctx.Response.StatusCode <- 200
            Task.CompletedTask)
    )
    |> ignore

/// POST helper: send JSON body to path, optionally with an Accept header.
let private postJson (client: HttpClient) (path: string) (json: string) (accept: string option) =
    task {
        use request = new HttpRequestMessage(HttpMethod.Post, path)
        request.Content <- new StringContent(json, Encoding.UTF8, "application/json")

        match accept with
        | Some a -> request.Headers.Accept.ParseAdd(a)
        | None -> ()

        return! client.SendAsync(request)
    }

/// POST helper with X-Test-User and X-Test-Claims headers for auth-enabled tests.
let private postJsonWithUser
    (client: HttpClient)
    (path: string)
    (json: string)
    (user: string option)
    (claimsHeader: string option)
    =
    task {
        use request = new HttpRequestMessage(HttpMethod.Post, path)
        request.Content <- new StringContent(json, Encoding.UTF8, "application/json")
        user |> Option.iter (fun u -> request.Headers.Add("X-Test-User", u))
        claimsHeader |> Option.iter (fun c -> request.Headers.Add("X-Test-Claims", c))
        return! client.SendAsync(request)
    }

// ──────────────────────────────────────────────
// T044: Valid request passes through to handler
// ──────────────────────────────────────────────

[<Tests>]
let validRequestTests =
    testList
        "T044 - Valid request passes through to handler"
        [ testCase "valid POST with all required fields returns 200 and invokes handler"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              let json = """{"Name":"Alice","Email":"alice@example.com","Age":30,"Notes":null}"""

              let response: HttpResponseMessage = (postJson client "/customers" json None).Result
              Expect.equal (int response.StatusCode) 200 "Valid request should return 200"
              Expect.equal counter.Count 1 "Handler should have been invoked exactly once"

          testCase "valid POST with optional field (Notes) included returns 200"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              let json =
                  """{"Name":"Bob","Email":"bob@example.com","Age":25,"Notes":"VIP customer"}"""

              let response: HttpResponseMessage = (postJson client "/customers" json None).Result
              Expect.equal (int response.StatusCode) 200 "Valid request with Notes should return 200"
              Expect.equal counter.Count 1 "Handler should have been invoked"

          testCase "valid POST with optional field (Notes) omitted returns 200"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              // Notes is option<string> so omitting it is valid.
              let json = """{"Name":"Carol","Email":"carol@example.com","Age":40}"""

              let response: HttpResponseMessage = (postJson client "/customers" json None).Result
              Expect.equal (int response.StatusCode) 200 "Valid request without Notes should return 200"
              Expect.equal counter.Count 1 "Handler should have been invoked" ]

// ──────────────────────────────────────────────
// T045: Invalid request returns 422 with ValidationReport
// ──────────────────────────────────────────────

[<Tests>]
let invalidRequestTests =
    testList
        "T045 - Invalid request returns 422 with ValidationReport"
        [ testCase "missing required Name field returns 422 and handler is NOT invoked"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              let json = """{"Email":"alice@example.com","Age":30}"""

              let response: HttpResponseMessage =
                  (postJson client "/customers" json (Some "application/json")).Result

              Expect.equal (int response.StatusCode) 422 "Missing Name should return 422"
              Expect.equal counter.Count 0 "Handler must NOT be invoked on invalid request"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let root = doc.RootElement
              let mutable errorsEl = JsonElement()
              Expect.isTrue (root.TryGetProperty("errors", &errorsEl)) "Response should have errors array"
              Expect.isTrue (errorsEl.GetArrayLength() > 0) "errors array should be non-empty"

          testCase "missing required Email field returns 422 without invoking handler"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              let json = """{"Name":"Alice","Age":30}"""

              let response: HttpResponseMessage = (postJson client "/customers" json None).Result
              Expect.equal (int response.StatusCode) 422 "Missing Email should return 422"
              Expect.equal counter.Count 0 "Handler must NOT be invoked"

          testCase "multiple missing fields returns 422 with multiple violations"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              // All required fields missing (Notes is optional so 3 violations expected).
              let json = """{}"""

              let response: HttpResponseMessage =
                  (postJson client "/customers" json (Some "application/json")).Result

              Expect.equal (int response.StatusCode) 422 "Empty object should return 422"
              Expect.equal counter.Count 0 "Handler must NOT be invoked"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let errors = doc.RootElement.GetProperty("errors")

              Expect.isGreaterThanOrEqual
                  (errors.GetArrayLength())
                  3
                  "Should have at least 3 violations for 3 missing required fields (Name, Email, Age)"

          testCase "empty body returns 422 with violations for all required fields"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              let response: HttpResponseMessage = (postJson client "/customers" "" None).Result

              Expect.equal (int response.StatusCode) 422 "Empty body should return 422"
              Expect.equal counter.Count 0 "Handler must NOT be invoked"

          testCase "sh:minCount violation appears in error response"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              let json = """{"Email":"alice@example.com","Age":30}"""

              let response: HttpResponseMessage =
                  (postJson client "/customers" json (Some "application/json")).Result

              Expect.equal (int response.StatusCode) 422 "Should return 422"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let root = doc.RootElement

              // Verify the response type field identifies the error category.
              Expect.equal
                  (root.GetProperty("type").GetString())
                  "urn:frank:validation:shacl-violation"
                  "type should identify SHACL violation" ]

// ──────────────────────────────────────────────
// T046: Content negotiation for violation responses
// ──────────────────────────────────────────────

[<Tests>]
let contentNegotiationTests =
    testList
        "T046 - Content negotiation for violation responses"
        [ testCase "Accept: application/json returns application/problem+json (RFC 9457)"
          <| fun _ ->
              let _counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              let json = """{"Age":30}"""

              let response: HttpResponseMessage =
                  (postJson client "/customers" json (Some "application/json")).Result

              Expect.equal (int response.StatusCode) 422 "Should return 422"
              let ct = response.Content.Headers.ContentType.MediaType
              Expect.equal ct "application/problem+json" "Accept: application/json → application/problem+json"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let root = doc.RootElement
              let mutable typeEl = JsonElement()
              let mutable titleEl = JsonElement()
              let mutable statusEl = JsonElement()
              Expect.isTrue (root.TryGetProperty("type", &typeEl)) "Problem Details must have type"
              Expect.isTrue (root.TryGetProperty("title", &titleEl)) "Problem Details must have title"
              Expect.isTrue (root.TryGetProperty("status", &statusEl)) "Problem Details must have status"

          testCase "Accept: application/ld+json returns JSON-LD with sh:ValidationReport"
          <| fun _ ->
              let _counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              let json = """{"Age":30}"""

              let response: HttpResponseMessage =
                  (postJson client "/customers" json (Some "application/ld+json")).Result

              Expect.equal (int response.StatusCode) 422 "Should return 422"
              let ct = response.Content.Headers.ContentType.MediaType
              Expect.equal ct "application/ld+json" "Accept: application/ld+json → application/ld+json"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let root = doc.RootElement
              let mutable contextEl = JsonElement()
              Expect.isTrue (root.TryGetProperty("@context", &contextEl)) "JSON-LD must have @context"

          testCase "no Accept header defaults to Problem Details JSON"
          <| fun _ ->
              let _counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              use request = new HttpRequestMessage(HttpMethod.Post, "/customers")
              request.Content <- new StringContent("""{"Age":30}""", Encoding.UTF8, "application/json")
              request.Headers.Accept.Clear()
              let response: HttpResponseMessage = client.SendAsync(request).Result

              Expect.equal (int response.StatusCode) 422 "Should return 422"
              let ct = response.Content.Headers.ContentType.MediaType
              Expect.equal ct "application/problem+json" "Default (no Accept) → application/problem+json" ]

// ──────────────────────────────────────────────
// T047: Capability-dependent shape validation
// ──────────────────────────────────────────────

[<Tests>]
let capabilityValidationTests =
    testList
        "T047 - Capability-dependent shape validation"
        [ testCase "admin with unrestricted (admin) shape: valid request including InternalCode passes"
          <| fun _ ->
              // Derive both shapes.
              let adminShape = ShapeBuilder.deriveShapeDefault typeof<AdminCreateCustomer>
              let baseShape = ShapeBuilder.deriveShapeDefault typeof<CreateCustomer>

              let resolverConfig =
                  { BaseShape = baseShape
                    Overrides =
                      [ { RequiredClaim = ("role", [ "admin" ])
                          Shape = adminShape } ] }

              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer>; typeof<AdminCreateCustomer> ]
                      (fun c ep -> mapCapabilityPost "/customers" resolverConfig c ep)
                      true

              // Admin sends full payload including InternalCode.
              let json =
                  """{"Name":"Alice","Email":"alice@example.com","Age":30,"Notes":null,"InternalCode":"INT-001"}"""

              let response: HttpResponseMessage =
                  (postJsonWithUser client "/customers" json (Some "admin") (Some "role=admin")).Result

              Expect.equal (int response.StatusCode) 200 "Admin with full payload should pass"
              Expect.equal counter.Count 1 "Handler should be invoked for admin"

          testCase "regular user with base (restricted) shape: valid base-shape payload passes"
          <| fun _ ->
              let adminShape = ShapeBuilder.deriveShapeDefault typeof<AdminCreateCustomer>
              let baseShape = ShapeBuilder.deriveShapeDefault typeof<CreateCustomer>

              let resolverConfig =
                  { BaseShape = baseShape
                    Overrides =
                      [ { RequiredClaim = ("role", [ "admin" ])
                          Shape = adminShape } ] }

              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer>; typeof<AdminCreateCustomer> ]
                      (fun c ep -> mapCapabilityPost "/customers" resolverConfig c ep)
                      true

              // Regular user sends payload valid for the base (CreateCustomer) shape.
              let json = """{"Name":"Bob","Email":"bob@example.com","Age":25,"Notes":null}"""

              let response: HttpResponseMessage =
                  (postJsonWithUser client "/customers" json (Some "bob") None).Result

              Expect.equal (int response.StatusCode) 200 "Regular user with valid base payload should pass"
              Expect.equal counter.Count 1 "Handler should be invoked"

          testCase "regular user with missing required field gets 422 (base shape enforced)"
          <| fun _ ->
              let adminShape = ShapeBuilder.deriveShapeDefault typeof<AdminCreateCustomer>
              let baseShape = ShapeBuilder.deriveShapeDefault typeof<CreateCustomer>

              let resolverConfig =
                  { BaseShape = baseShape
                    Overrides =
                      [ { RequiredClaim = ("role", [ "admin" ])
                          Shape = adminShape } ] }

              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer>; typeof<AdminCreateCustomer> ]
                      (fun c ep -> mapCapabilityPost "/customers" resolverConfig c ep)
                      true

              // Regular user omits required Name field.
              let json = """{"Email":"bob@example.com","Age":25}"""

              let response: HttpResponseMessage =
                  (postJsonWithUser client "/customers" json (Some "bob") None).Result

              Expect.equal (int response.StatusCode) 422 "Regular user with invalid payload should get 422"
              Expect.equal counter.Count 0 "Handler must NOT be invoked"

          testCase "unauthenticated request falls back to base shape and validates"
          <| fun _ ->
              let adminShape = ShapeBuilder.deriveShapeDefault typeof<AdminCreateCustomer>
              let baseShape = ShapeBuilder.deriveShapeDefault typeof<CreateCustomer>

              let resolverConfig =
                  { BaseShape = baseShape
                    Overrides =
                      [ { RequiredClaim = ("role", [ "admin" ])
                          Shape = adminShape } ] }

              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer>; typeof<AdminCreateCustomer> ]
                      (fun c ep -> mapCapabilityPost "/customers" resolverConfig c ep)
                      true

              // Anonymous user sends valid base-shape payload.
              let json = """{"Name":"Anon","Email":"anon@example.com","Age":18,"Notes":null}"""

              let response: HttpResponseMessage =
                  (postJsonWithUser client "/customers" json None None).Result

              Expect.equal (int response.StatusCode) 200 "Unauthenticated valid request should pass with base shape"
              Expect.equal counter.Count 1 "Handler should be invoked" ]

// ──────────────────────────────────────────────
// T048: Custom constraints end-to-end
// ──────────────────────────────────────────────

[<Tests>]
let customConstraintTests =
    testList
        "T048 - Custom constraints in end-to-end pipeline"
        [ testCase "pattern violation on Email returns 422"
          <| fun _ ->
              // Derive the base shape for CreateCustomer and add an email pattern constraint.
              let baseShape = ShapeBuilder.deriveShapeDefault typeof<CreateCustomer>

              let emailPattern = "^[^@]+@[^@]+\\.[^@]+$"

              let shapeWithPattern =
                  ShapeMerger.mergeConstraints
                      baseShape
                      [ { PropertyPath = "Email"
                          Constraint = PatternConstraint emailPattern } ]

              // Build a test host that preloads the merged shape.
              let counter = HandlerCounter()

              let appBuilder = WebApplication.CreateBuilder([||])
              appBuilder.WebHost.UseTestServer() |> ignore
              appBuilder.Services.AddSingleton<ShapeCache>() |> ignore
              appBuilder.Services.AddRouting() |> ignore
              appBuilder.Services.AddLogging() |> ignore
              let app = appBuilder.Build()

              let shapeCache = app.Services.GetRequiredService<ShapeCache>()
              shapeCache.LoadAll [ shapeWithPattern ]

              app.UseRouting() |> ignore

              (app :> IApplicationBuilder).Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                  let sc = ctx.RequestServices.GetRequiredService<ShapeCache>()
                  let loggerFactory = ctx.RequestServices.GetRequiredService<ILoggerFactory>()

                  let innerLogger =
                      loggerFactory.CreateLogger("Frank.Validation.ValidationMiddleware")

                  let typedLogger =
                      { new ILogger<ValidationMiddleware> with
                          member _.Log(logLevel, eventId, state, ex, formatter) =
                              innerLogger.Log(logLevel, eventId, state, ex, formatter)

                          member _.IsEnabled(logLevel) = innerLogger.IsEnabled(logLevel)
                          member _.BeginScope(state) = innerLogger.BeginScope(state) }

                  let middleware = ValidationMiddleware(next, sc, typedLogger)
                  middleware.InvokeAsync(ctx))
              |> ignore

              app.UseEndpoints(fun endpoints ->
                  endpoints
                      .MapPost(
                          "/customers",
                          RequestDelegate(fun ctx ->
                              counter.Increment()
                              ctx.Response.StatusCode <- 200
                              Task.CompletedTask)
                      )
                      .WithMetadata(
                          { ShapeUri = shapeWithPattern.NodeShapeUri
                            CustomConstraints = []
                            ResolverConfig = None }
                          : ValidationMarker
                      )
                  |> ignore)
              |> ignore

              app.Start()
              let client = app.GetTestClient()

              // Invalid email (no domain extension).
              let badJson = """{"Name":"Alice","Email":"notanemail","Age":30,"Notes":null}"""

              let response: HttpResponseMessage =
                  (postJson client "/customers" badJson (Some "application/json")).Result

              Expect.equal (int response.StatusCode) 422 "Invalid email pattern should return 422"
              Expect.equal counter.Count 0 "Handler must NOT be invoked"

          testCase "valid email pattern passes through"
          <| fun _ ->
              let baseShape = ShapeBuilder.deriveShapeDefault typeof<CreateCustomer>
              let emailPattern = "^[^@]+@[^@]+\\.[^@]+$"

              let shapeWithPattern =
                  ShapeMerger.mergeConstraints
                      baseShape
                      [ { PropertyPath = "Email"
                          Constraint = PatternConstraint emailPattern } ]

              let counter = HandlerCounter()

              let appBuilder = WebApplication.CreateBuilder([||])
              appBuilder.WebHost.UseTestServer() |> ignore
              appBuilder.Services.AddSingleton<ShapeCache>() |> ignore
              appBuilder.Services.AddRouting() |> ignore
              appBuilder.Services.AddLogging() |> ignore
              let app = appBuilder.Build()

              let shapeCache = app.Services.GetRequiredService<ShapeCache>()
              shapeCache.LoadAll [ shapeWithPattern ]

              app.UseRouting() |> ignore

              (app :> IApplicationBuilder).Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                  let sc = ctx.RequestServices.GetRequiredService<ShapeCache>()
                  let loggerFactory = ctx.RequestServices.GetRequiredService<ILoggerFactory>()

                  let innerLogger =
                      loggerFactory.CreateLogger("Frank.Validation.ValidationMiddleware")

                  let typedLogger =
                      { new ILogger<ValidationMiddleware> with
                          member _.Log(logLevel, eventId, state, ex, formatter) =
                              innerLogger.Log(logLevel, eventId, state, ex, formatter)

                          member _.IsEnabled(logLevel) = innerLogger.IsEnabled(logLevel)
                          member _.BeginScope(state) = innerLogger.BeginScope(state) }

                  let middleware = ValidationMiddleware(next, sc, typedLogger)
                  middleware.InvokeAsync(ctx))
              |> ignore

              app.UseEndpoints(fun endpoints ->
                  endpoints
                      .MapPost(
                          "/customers",
                          RequestDelegate(fun ctx ->
                              counter.Increment()
                              ctx.Response.StatusCode <- 200
                              Task.CompletedTask)
                      )
                      .WithMetadata(
                          { ShapeUri = shapeWithPattern.NodeShapeUri
                            CustomConstraints = []
                            ResolverConfig = None }
                          : ValidationMarker
                      )
                  |> ignore)
              |> ignore

              app.Start()
              let client = app.GetTestClient()

              let goodJson =
                  """{"Name":"Alice","Email":"alice@example.com","Age":30,"Notes":null}"""

              let response: HttpResponseMessage =
                  (postJson client "/customers" goodJson None).Result

              Expect.equal (int response.StatusCode) 200 "Valid email should pass"
              Expect.equal counter.Count 1 "Handler should be invoked"

          testCase "MinInclusive violation on Age returns 422"
          <| fun _ ->
              let baseShape = ShapeBuilder.deriveShapeDefault typeof<CreateCustomer>

              let shapeWithMin =
                  ShapeMerger.mergeConstraints
                      baseShape
                      [ { PropertyPath = "Age"
                          Constraint = MinInclusiveConstraint(box 18) } ]

              let counter = HandlerCounter()

              let appBuilder = WebApplication.CreateBuilder([||])
              appBuilder.WebHost.UseTestServer() |> ignore
              appBuilder.Services.AddSingleton<ShapeCache>() |> ignore
              appBuilder.Services.AddRouting() |> ignore
              appBuilder.Services.AddLogging() |> ignore
              let app = appBuilder.Build()

              let shapeCache = app.Services.GetRequiredService<ShapeCache>()
              shapeCache.LoadAll [ shapeWithMin ]

              app.UseRouting() |> ignore

              (app :> IApplicationBuilder).Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                  let sc = ctx.RequestServices.GetRequiredService<ShapeCache>()
                  let loggerFactory = ctx.RequestServices.GetRequiredService<ILoggerFactory>()

                  let innerLogger =
                      loggerFactory.CreateLogger("Frank.Validation.ValidationMiddleware")

                  let typedLogger =
                      { new ILogger<ValidationMiddleware> with
                          member _.Log(logLevel, eventId, state, ex, formatter) =
                              innerLogger.Log(logLevel, eventId, state, ex, formatter)

                          member _.IsEnabled(logLevel) = innerLogger.IsEnabled(logLevel)
                          member _.BeginScope(state) = innerLogger.BeginScope(state) }

                  let middleware = ValidationMiddleware(next, sc, typedLogger)
                  middleware.InvokeAsync(ctx))
              |> ignore

              app.UseEndpoints(fun endpoints ->
                  endpoints
                      .MapPost(
                          "/orders",
                          RequestDelegate(fun ctx ->
                              counter.Increment()
                              ctx.Response.StatusCode <- 200
                              Task.CompletedTask)
                      )
                      .WithMetadata(
                          { ShapeUri = shapeWithMin.NodeShapeUri
                            CustomConstraints = []
                            ResolverConfig = None }
                          : ValidationMarker
                      )
                  |> ignore)
              |> ignore

              app.Start()
              let client = app.GetTestClient()

              // Age 16 < MinInclusive 18.
              let json = """{"Name":"Young","Email":"young@example.com","Age":16,"Notes":null}"""

              let response: HttpResponseMessage = (postJson client "/orders" json None).Result
              Expect.equal (int response.StatusCode) 422 "Age below minimum should return 422"
              Expect.equal counter.Count 0 "Handler must NOT be invoked"

          testCase "both auto-derived (minCount) and custom (pattern) violations appear in same response"
          <| fun _ ->
              let baseShape = ShapeBuilder.deriveShapeDefault typeof<CreateCustomer>

              let mergedShape =
                  ShapeMerger.mergeConstraints
                      baseShape
                      [ { PropertyPath = "Email"
                          Constraint = PatternConstraint "^[^@]+@[^@]+\\.[^@]+$" } ]

              let counter = HandlerCounter()

              let appBuilder = WebApplication.CreateBuilder([||])
              appBuilder.WebHost.UseTestServer() |> ignore
              appBuilder.Services.AddSingleton<ShapeCache>() |> ignore
              appBuilder.Services.AddRouting() |> ignore
              appBuilder.Services.AddLogging() |> ignore
              let app = appBuilder.Build()

              let shapeCache = app.Services.GetRequiredService<ShapeCache>()
              shapeCache.LoadAll [ mergedShape ]

              app.UseRouting() |> ignore

              (app :> IApplicationBuilder).Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                  let sc = ctx.RequestServices.GetRequiredService<ShapeCache>()
                  let loggerFactory = ctx.RequestServices.GetRequiredService<ILoggerFactory>()

                  let innerLogger =
                      loggerFactory.CreateLogger("Frank.Validation.ValidationMiddleware")

                  let typedLogger =
                      { new ILogger<ValidationMiddleware> with
                          member _.Log(logLevel, eventId, state, ex, formatter) =
                              innerLogger.Log(logLevel, eventId, state, ex, formatter)

                          member _.IsEnabled(logLevel) = innerLogger.IsEnabled(logLevel)
                          member _.BeginScope(state) = innerLogger.BeginScope(state) }

                  let middleware = ValidationMiddleware(next, sc, typedLogger)
                  middleware.InvokeAsync(ctx))
              |> ignore

              app.UseEndpoints(fun endpoints ->
                  endpoints
                      .MapPost(
                          "/customers",
                          RequestDelegate(fun ctx ->
                              counter.Increment()
                              ctx.Response.StatusCode <- 200
                              Task.CompletedTask)
                      )
                      .WithMetadata(
                          { ShapeUri = mergedShape.NodeShapeUri
                            CustomConstraints = []
                            ResolverConfig = None }
                          : ValidationMarker
                      )
                  |> ignore)
              |> ignore

              app.Start()
              let client = app.GetTestClient()

              // Missing Name (auto-derived minCount) AND invalid Email (custom pattern).
              let json = """{"Email":"notvalid","Age":30}"""

              let response: HttpResponseMessage =
                  (postJson client "/customers" json (Some "application/json")).Result

              Expect.equal (int response.StatusCode) 422 "Should return 422 for combined violations"
              Expect.equal counter.Count 0 "Handler must NOT be invoked"

              let body = response.Content.ReadAsStringAsync().Result
              let doc = JsonDocument.Parse(body)
              let errors = doc.RootElement.GetProperty("errors")

              Expect.isGreaterThanOrEqual
                  (errors.GetArrayLength())
                  2
                  "Should report at least 2 violations (missing Name + invalid Email pattern)" ]

// ──────────────────────────────────────────────
// T049: Edge cases
// ──────────────────────────────────────────────

[<Tests>]
let edgeCaseTests =
    testList
        "T049 - Edge cases"
        [ testCase "empty body on validated endpoint returns 422"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              let response: HttpResponseMessage = (postJson client "/customers" "" None).Result

              Expect.equal (int response.StatusCode) 422 "Empty body should return 422"
              Expect.equal counter.Count 0 "Handler must NOT be invoked for empty body"

          testCase "non-validated endpoint passes through regardless of body content"
          <| fun _ ->
              let counter, client =
                  buildTestHost [] (fun c ep -> mapUnvalidatedPost "/unvalidated" c ep) false

              // Send an incomplete/invalid object — middleware should not intercept this.
              let json = """{"random":"garbage"}"""

              let response: HttpResponseMessage =
                  (postJson client "/unvalidated" json None).Result

              Expect.equal (int response.StatusCode) 200 "Non-validated endpoint must not be intercepted"
              Expect.equal counter.Count 1 "Handler should be invoked on unvalidated endpoint"

          testCase "non-validated endpoint passes with empty body"
          <| fun _ ->
              let counter, client =
                  buildTestHost [] (fun c ep -> mapUnvalidatedPost "/unvalidated" c ep) false

              let response: HttpResponseMessage = (postJson client "/unvalidated" "" None).Result
              Expect.equal (int response.StatusCode) 200 "Non-validated endpoint with empty body must pass"
              Expect.equal counter.Count 1 "Handler should be invoked"

          testCase "option type field (Notes) omitted is treated as optional — does not cause 422"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              // Notes is option<string>: omitting it entirely is valid.
              let json = """{"Name":"Dave","Email":"dave@example.com","Age":22}"""

              let response: HttpResponseMessage = (postJson client "/customers" json None).Result
              Expect.equal (int response.StatusCode) 200 "Omitting an option<T> field should not cause 422"
              Expect.equal counter.Count 1 "Handler should be invoked"

          testCase "option type field (Notes) explicitly null is treated as absent — does not cause 422"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              // Notes: null in JSON should map to None and be valid.
              let json = """{"Name":"Eve","Email":"eve@example.com","Age":29,"Notes":null}"""

              let response: HttpResponseMessage = (postJson client "/customers" json None).Result
              Expect.equal (int response.StatusCode) 200 "null for option<T> field should not cause 422"
              Expect.equal counter.Count 1 "Handler should be invoked"

          testCase "validated endpoint with completely wrong content type body still returns 422 for empty parse"
          <| fun _ ->
              let counter, client =
                  buildTestHost
                      [ typeof<CreateCustomer> ]
                      (fun c ep -> mapValidatedPost<CreateCustomer> "/customers" 200 c ep)
                      false

              // Sending plain text (not JSON) as the body — middleware sees empty/unparseable body → 422.
              let response: HttpResponseMessage =
                  task {
                      use request = new HttpRequestMessage(HttpMethod.Post, "/customers")
                      request.Content <- new StringContent("not json at all", Encoding.UTF8, "text/plain")
                      return! client.SendAsync(request)
                  }
                  |> (fun t -> t.Result)

              // The middleware cannot parse the body as JSON, so it treats it as missing → 422.
              Expect.equal (int response.StatusCode) 422 "Unparseable body should return 422"
              Expect.equal counter.Count 0 "Handler must NOT be invoked" ]
