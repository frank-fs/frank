module Frank.JsonHome.Tests.IntegrationTests

open System
open System.Net.Http
open System.Security.Claims
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Options
open Expecto
open Frank.Builder
open Frank.Auth
open Frank.JsonHome

let [<Literal>] TestScheme = "TestScheme"

type TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

type TestAuthHandler(options, logger, encoder) =
    inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

    override this.HandleAuthenticateAsync() =
        let user = this.Request.Headers["X-Test-User"].ToString()

        if String.IsNullOrEmpty user then
            Task.FromResult(AuthenticateResult.NoResult())
        else
            let claims = ResizeArray [ Claim(ClaimTypes.Name, user) ]
            let roles = this.Request.Headers["X-Test-Roles"].ToString()

            if not (String.IsNullOrEmpty roles) then
                for role in roles.Split ';' do
                    claims.Add(Claim(ClaimTypes.Role, role))

            let identity = ClaimsIdentity(claims, TestScheme)
            let ticket = AuthenticationTicket(ClaimsPrincipal identity, TestScheme)
            Task.FromResult(AuthenticateResult.Success ticket)

let private options = JsonHomeOptions.Default

/// Same composition useJsonHome performs: the document is one more resource,
/// dispatched through the same routing/UseEndpoints stage as everything else
/// -- after UseAuthentication/UseAuthorization, not before. Returns the IHost
/// itself, unstarted, so callers can assert on `host.Start()` directly
/// (startup-validation tests) as well as via `createServer`'s TestClient
/// (request/response tests).
let private buildHost (homeOptions: JsonHomeOptions) (resources: Resource list) : IHost =
    let spec = (webHost [||]).UseJsonHome(WebHostSpec.Empty, fun _ -> homeOptions)
    let endpoints =
        (List.ofArray spec.Endpoints @ (resources |> List.collect (fun r -> List.ofArray r.Endpoints)))
        |> Array.ofList

    Host
        .CreateDefaultBuilder([||])
        .ConfigureWebHost(fun webBuilder ->
            webBuilder
                .UseTestServer()
                .ConfigureServices(fun services ->
                    services.AddRouting() |> ignore
                    spec.Services services |> ignore

                    services
                        .AddAuthentication(TestScheme)
                        .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, fun _ -> ())
                    |> ignore

                    services.AddAuthorization() |> ignore

                    // ApiExplorer discovers endpoints through registered data sources.
                    services.AddSingleton<EndpointDataSource>(TestEndpointDataSource endpoints)
                    |> ignore)
                .Configure(fun app ->
                    // The same middleware useJsonHome installs. WebHostBuilder.Run
                    // builds and blocks, so the pipeline is wired by hand, but the
                    // code under test is the real thing rather than a copy.
                    app
                    |> WebLink.useAppWideLinks spec.LinkProviders
                    |> spec.BeforeRoutingMiddleware
                    |> fun app -> app.UseRouting()
                    |> WebLink.useResourceScopedLinks
                    |> fun app ->
                        app
                            .UseAuthentication()
                            .UseAuthorization()
                            .UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                    |> ignore)
            |> ignore)
        .Build()

let private createServer (homeOptions: JsonHomeOptions) (resources: Resource list) =
    let host = buildHost homeOptions resources
    host.Start()
    host.GetTestClient()

let private ok: RequestDelegate = RequestDelegate(fun ctx -> ctx.Response.WriteAsync "OK")

/// A minimal pipeline with UseExceptionHandler ahead of the link-header
/// middleware, mirroring a standard production setup, and a handler that
/// always throws.
let private createFailingServer () =
    let spec = (webHost [||]).UseJsonHome(WebHostSpec.Empty)

    let host =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .Configure(fun app ->
                        app.UseExceptionHandler(fun errApp ->
                            errApp.Run(fun ctx ->
                                ctx.Response.StatusCode <- 500
                                ctx.Response.WriteAsync "error"))
                        |> ignore

                        app
                        |> WebLink.useAppWideLinks spec.LinkProviders
                        |> fun app -> app.Run(fun _ -> failwith "boom"))
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()

[<Tests>]
let tests =
    testList
        "JSON Home integration"
        [ testTask "serves the document with the json-home media type" {
              let products =
                  resource "/products" {
                      rel "tag:example.com,2026:products"
                      get ok
                  }

              let client = createServer options [ products ]
              let! (response: HttpResponseMessage) = client.GetAsync options.Path

              Expect.equal (response.Content.Headers.ContentType.MediaType) "application/json-home" "Media type"

              let! (body: string) = response.Content.ReadAsStringAsync()
              let root = JsonDocument.Parse(body).RootElement

              Expect.isTrue
                  (fst (root.GetProperty("resources").TryGetProperty "tag:example.com,2026:products"))
                  "Resource is present"
          }

          testTask "advertises the document with a Link header, including on 404s" {
              let products =
                  resource "/products" {
                      rel "tag:example.com,2026:products"
                      get ok
                  }

              let client = createServer options [ products ]

              let! (found: HttpResponseMessage) = client.GetAsync "/products"
              let! (missing: HttpResponseMessage) = client.GetAsync "/nope"

              let expected = "</.well-known/home.json>; rel=\"home\""

              Expect.contains (found.Headers.GetValues "Link") expected "Link on a matched route"
              Expect.contains (missing.Headers.GetValues "Link") expected "Link on a 404"
          }

          testTask "an authenticated principal that satisfies a guard sees the guarded resource" {
              // Regression test: the document must be dispatched after
              // UseAuthentication/UseAuthorization populate ctx.User, or
              // AuthorizationFilter.apply always evaluates against an
              // anonymous principal and every guarded resource disappears --
              // even for a request that legitimately satisfies the guard.
              let adminResource =
                  resource "/admin" {
                      rel "tag:example.com,2026:admin"
                      requireRole "admin"
                      get ok
                  }

              let client = createServer options [ adminResource ]

              let! (anonymous: HttpResponseMessage) = client.GetAsync options.Path
              let! (anonymousBody: string) = anonymous.Content.ReadAsStringAsync()
              let anonymousRoot = JsonDocument.Parse(anonymousBody).RootElement

              Expect.isFalse
                  (fst (anonymousRoot.GetProperty("resources").TryGetProperty "tag:example.com,2026:admin"))
                  "Anonymous request does not see the admin resource"

              // A shared cache must never serve one principal's view to another
              // -- these headers apply regardless of what the requester can see,
              // because the app has a guarded resource at all.
              Expect.isTrue anonymous.Headers.CacheControl.Private "Cache-Control: private on the anonymous response"
              Expect.isTrue anonymous.Headers.CacheControl.NoCache "Cache-Control: no-cache on the anonymous response"

              Expect.contains
                  (List.ofSeq anonymous.Headers.Vary)
                  "Authorization"
                  "Vary on the anonymous response"

              let request = new HttpRequestMessage(HttpMethod.Get, options.Path)
              request.Headers.Add("X-Test-User", "alice")
              request.Headers.Add("X-Test-Roles", "admin")
              let! (asAdmin: HttpResponseMessage) = client.SendAsync request
              let! (adminBody: string) = asAdmin.Content.ReadAsStringAsync()
              let adminRoot = JsonDocument.Parse(adminBody).RootElement

              Expect.isTrue
                  (fst (adminRoot.GetProperty("resources").TryGetProperty "tag:example.com,2026:admin"))
                  "An authenticated admin sees the admin resource"

              Expect.isTrue asAdmin.Headers.CacheControl.Private "Cache-Control: private on the admin response too"
              Expect.isTrue asAdmin.Headers.CacheControl.NoCache "Cache-Control: no-cache on the admin response too"

              Expect.contains
                  (List.ofSeq asAdmin.Headers.Vary)
                  "Authorization"
                  "Vary on the admin response too"
          }

          testTask "the Link header survives an exception handler clearing the response" {
              // Regression test: UseExceptionHandler-style middleware typically
              // calls Response.Clear() before regenerating its own response,
              // which wipes out a header appended directly but not one
              // registered via Response.OnStarting.
              let client = createFailingServer ()
              let! (response: HttpResponseMessage) = client.GetAsync "/x"

              Expect.equal (int response.StatusCode) 500 "The exception handler produced the response"
              Expect.isTrue (response.Headers.Contains "Link") "Link header survives Response.Clear()"
          }

          testTask "a configured path, rel, title, and links all take effect" {
              let custom =
                  { Path = "/discovery.json"
                    Rel = "discovery"
                    Title = Some "Sample API"
                    Links = [ "author", "mailto:api-admin@example.com" ] }

              let products =
                  resource "/products" {
                      rel "tag:example.com,2026:products"
                      get ok
                  }

              let client = createServer custom [ products ]

              let! (matched: HttpResponseMessage) = client.GetAsync "/products"

              Expect.contains
                  (matched.Headers.GetValues "Link")
                  "</discovery.json>; rel=\"discovery\""
                  "Link header uses the configured path and rel"

              let! (response: HttpResponseMessage) = client.GetAsync custom.Path
              let! (body: string) = response.Content.ReadAsStringAsync()
              let root = JsonDocument.Parse(body).RootElement

              Expect.equal (root.GetProperty("api").GetProperty("title").GetString()) "Sample API" "api.title"

              Expect.equal
                  (root.GetProperty("api").GetProperty("links").GetProperty("author").GetString())
                  "mailto:api-admin@example.com"
                  "api.links.author"

              Expect.isTrue
                  (fst (root.GetProperty("resources").TryGetProperty "tag:example.com,2026:products"))
                  "Resource is present at the configured path"
          }

          testTask "hints.allow reflects only the methods the current principal can call" {
              let widgets =
                  resource "/widgets" {
                      rel "tag:example.com,2026:widgets"
                      get ok
                      delete (handler {
                          requireRole "admin"
                          handle (fun (ctx: HttpContext) -> ctx.Response.WriteAsync "OK")
                      })
                  }

              let client = createServer options [ widgets ]
              let allowFor (response: HttpResponseMessage) =
                  task {
                      let! body = response.Content.ReadAsStringAsync()
                      let root = JsonDocument.Parse(body).RootElement
                      let resource = root.GetProperty("resources").GetProperty("tag:example.com,2026:widgets")
                      let allow = resource.GetProperty("hints").GetProperty("allow")
                      return [ for e in allow.EnumerateArray() -> e.GetString() ]
                  }

              let! (anonymous: HttpResponseMessage) = client.GetAsync options.Path
              let! anonymousAllow = allowFor anonymous
              Expect.equal anonymousAllow [ "GET" ] "Anonymous request sees only GET"

              let request = new HttpRequestMessage(HttpMethod.Get, options.Path)
              request.Headers.Add("X-Test-User", "alice")
              request.Headers.Add("X-Test-Roles", "admin")
              let! (asAdmin: HttpResponseMessage) = client.SendAsync request
              let! adminAllow = allowFor asAdmin
              // Method order here comes from ApiExplorer's ApiDescription grouping
              // (ApiSurface.ofApiDescriptions, Task 3 -- out of scope for this task),
              // not from AuthorizationFilter itself, which preserves whatever order
              // resource.Methods already had. Compare as sets so this test doesn't
              // couple to that incidental ordering.
              Expect.equal (Set.ofList adminAllow) (Set.ofList [ "GET"; "DELETE" ]) "Admin request sees both methods"
          }

          test "two resources sharing a rel fail host startup, naming both routes" {
              // End-to-end proof that Task 2's wiring (AddOptionsWithValidateOnStart<JsonHomeOptions>
              // plus DuplicateRelValidator registered via TryAddEnumerable) actually fires during
              // Host.StartAsync against a real DI container and real EndpointDataSource -- not just
              // DuplicateRelValidatorTests.fs's direct, provider-only unit tests.
              let widgetA =
                  resource "/widgets-a" {
                      rel "widget"
                      get ok
                  }

              let widgetB =
                  resource "/widgets-b" {
                      rel "widget"
                      get ok
                  }

              let host = buildHost options [ widgetA; widgetB ]

              Expect.throwsC (fun () -> host.Start()) (fun ex ->
                  match ex with
                  | :? OptionsValidationException as ove ->
                      Expect.stringContains ove.Message "/widgets-a" "Failure names the first route"
                      Expect.stringContains ove.Message "/widgets-b" "Failure names the second route"
                  | other -> failwith $"Expected OptionsValidationException, got %s{other.GetType().FullName}")
          }

          test "three resources, only two sharing a rel, still fail startup without over-flagging the third" {
              // Guards the real host/DI pipeline against the same over-flagging risk
              // DuplicateRelValidatorTests.fs already checks in isolation.
              let widgetA =
                  resource "/widgets-a" {
                      rel "widget"
                      get ok
                  }

              let widgetB =
                  resource "/widgets-b" {
                      rel "widget"
                      get ok
                  }

              let gadget =
                  resource "/gadgets" {
                      rel "gadget"
                      get ok
                  }

              let host = buildHost options [ widgetA; widgetB; gadget ]

              Expect.throwsC (fun () -> host.Start()) (fun ex ->
                  match ex with
                  | :? OptionsValidationException as ove ->
                      Expect.stringContains ove.Message "/widgets-a" "Failure names the first route"
                      Expect.stringContains ove.Message "/widgets-b" "Failure names the second route"
                      Expect.isFalse (ove.Message.Contains "/gadgets") "Non-colliding third resource is not reported"
                  | other -> failwith $"Expected OptionsValidationException, got %s{other.GetType().FullName}")
          } ]
