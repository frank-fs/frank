module Frank.Provenance.Tests.ProvenanceReflectionTests

open System
open System.Net
open System.Net.Http
open System.Reflection
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Semantic
open Frank.Builder
open Frank.Provenance
open Frank.Provenance.ProvenanceStore
open Frank.Provenance.ProvenanceMiddleware

[<Tests>]
let provenanceReflectionTests =
    testList
        "ProvenanceReflection: useProvenance zero-arg CE loads GeneratedProvenance"
        [ test "executing assembly contains GeneratedProvenance type" {
              let t = Assembly.GetExecutingAssembly().GetType("GeneratedProvenance")
              Expect.isNotNull t "GeneratedProvenance type should be found in executing assembly"
          }

          test "useProvenance CE: loads provClasses and typeIris from GeneratedProvenance (AppDomain scan)" {
              let wh = WebHostBuilder([||])
              let configuredSpec = wh.UseProvenance(WebHostSpec.Empty)
              let services = new ServiceCollection()
              configuredSpec.Services(services) |> ignore
              use provider = services.BuildServiceProvider()
              let config = provider.GetRequiredService<ProvenanceConfig>()

              Expect.isEmpty
                  config.ProvClasses
                  "provClasses should be Map.empty — GeneratedProvenance found via AppDomain scan, not null fallback"

              Expect.isEmpty
                  config.TypeIris
                  "typeIris should be Map.empty — GeneratedProvenance found via AppDomain scan, not null fallback"
          }

          testTask "useProvenance CE: GeneratedProvenance found, middleware still emits prov:wasGeneratedBy Link header" {
              let wh = WebHostBuilder([||])
              let configuredSpec = wh.UseProvenance(WebHostSpec.Empty)

              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              configuredSpec.Services(builder.Services) |> ignore
              let app = builder.Build()
              (app :> IApplicationBuilder).UseRouting() |> ignore
              configuredSpec.Middleware(app :> IApplicationBuilder) |> ignore

              app.MapGet(
                  "/resource",
                  Func<HttpContext, Task>(fun ctx -> task { do! ctx.Response.WriteAsync("ok") } :> Task)
              )
              |> ignore

              app.StartAsync() |> Async.AwaitTask |> Async.RunSynchronously
              let client = app.GetTestClient()

              let! (response: HttpResponseMessage) = client.GetAsync("/resource")
              Expect.equal response.StatusCode HttpStatusCode.OK "should return 200"

              let hasProvLink =
                  response.Headers.Contains("Link")
                  && (response.Headers.GetValues("Link")
                      |> Seq.exists (fun v -> v.Contains("prov#wasGeneratedBy") || v.Contains("prov:wasGeneratedBy")))

              Expect.isTrue
                  hasProvLink
                  "prov:wasGeneratedBy Link header must be emitted — useProvenance found GeneratedProvenance via AppDomain scan"
          } ]
