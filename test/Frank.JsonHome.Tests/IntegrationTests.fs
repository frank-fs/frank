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
    let endpoints = resources |> List.collect (fun r -> List.ofArray r.Endpoints) |> Array.ofList

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
                        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                            let links = [| WebLink.create options.Path options.Rel |]

                            match
                                WebLink.middleware
                                    [| { new IResponseLinkProvider with
                                           member _.GetLinks(_) = links :> seq<_> } |]
                            with
                            | Some run -> run ctx (fun () -> next.Invoke ctx)
                            | None -> next.Invoke ctx)
                        |> ignore

                        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                            if ctx.Request.Path.Equals(PathString options.Path) then
                                task {
                                    let provider =
                                        ctx.RequestServices.GetRequiredService<Microsoft.AspNetCore.Mvc.ApiExplorer.IApiDescriptionGroupCollectionProvider>()

                                    let all =
                                        provider.ApiDescriptionGroups.Items
                                        |> Seq.collect (fun g -> g.Items)
                                        |> ApiSurface.ofApiDescriptions

                                    let! kept = AuthorizationFilter.apply ctx all
                                    do! JsonHome.write options kept ctx
                                }
                                :> Task
                            else
                                next.Invoke ctx)
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
          } ]
