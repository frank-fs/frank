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

let private createServer (resources: Resource list) =
    // Same composition useJsonHome performs: the document is one more
    // resource, dispatched through the same routing/UseEndpoints stage as
    // everything else -- after UseAuthentication/UseAuthorization, not before.
    let allResources = JsonHome.documentResource options :: resources
    let endpoints = allResources |> List.collect (fun r -> List.ofArray r.Endpoints) |> Array.ofList

    let host =
        Host
            .CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        services.AddEndpointsApiExplorer() |> ignore

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
                        let runLinkHeader = JsonHome.linkHeaderMiddleware options

                        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                            runLinkHeader ctx (fun () -> next.Invoke ctx))
                        |> ignore

                        app
                            .UseRouting()
                            .UseAuthentication()
                            .UseAuthorization()
                            .UseEndpoints(fun e -> e.DataSources.Add(TestEndpointDataSource endpoints))
                        |> ignore)
                |> ignore)
            .Build()

    host.Start()
    host.GetTestClient()

let private ok: RequestDelegate = RequestDelegate(fun ctx -> ctx.Response.WriteAsync "OK")

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

              let client = createServer [ products ]
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

              let client = createServer [ products ]

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

              let client = createServer [ adminResource ]

              let! (anonymous: HttpResponseMessage) = client.GetAsync options.Path
              let! (anonymousBody: string) = anonymous.Content.ReadAsStringAsync()
              let anonymousRoot = JsonDocument.Parse(anonymousBody).RootElement

              Expect.isFalse
                  (fst (anonymousRoot.GetProperty("resources").TryGetProperty "tag:example.com,2026:admin"))
                  "Anonymous request does not see the admin resource"

              let request = new HttpRequestMessage(HttpMethod.Get, options.Path)
              request.Headers.Add("X-Test-User", "alice")
              request.Headers.Add("X-Test-Roles", "admin")
              let! (asAdmin: HttpResponseMessage) = client.SendAsync request
              let! (adminBody: string) = asAdmin.Content.ReadAsStringAsync()
              let adminRoot = JsonDocument.Parse(adminBody).RootElement

              Expect.isTrue
                  (fst (adminRoot.GetProperty("resources").TryGetProperty "tag:example.com,2026:admin"))
                  "An authenticated admin sees the admin resource"
          } ]
