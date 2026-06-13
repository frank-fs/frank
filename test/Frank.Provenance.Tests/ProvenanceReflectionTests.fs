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
open Frank.Provenance.ProvenanceStore
open Frank.Provenance.ProvenanceMiddleware

[<Tests>]
let provenanceReflectionTests =
    testList
        "ProvenanceReflection: useProvenance zero-arg reflection path"
        [ test "executing assembly contains GeneratedProvenance type" {
              let asm = Assembly.GetExecutingAssembly()
              Expect.isNotNull asm "executing assembly should not be null"
              let t = asm.GetType("GeneratedProvenance")
              Expect.isNotNull t "GeneratedProvenance type should be found in entry assembly"
          }

          test "GeneratedProvenance.provClasses property returns Map.empty" {
              let asm = Assembly.GetExecutingAssembly()
              let t = asm.GetType("GeneratedProvenance")
              let prop = t.GetProperty("provClasses")
              Expect.isNotNull prop "provClasses property should exist on GeneratedProvenance"
              let provClasses = prop.GetValue(null) :?> Map<string, ProvOClass>

              Expect.isEmpty
                  provClasses
                  "provClasses is Map.empty — generator emits empty map when no provClass annotations are present"
          }

          test "GeneratedProvenance.typeIris property returns Map.empty" {
              let asm = Assembly.GetExecutingAssembly()
              let t = asm.GetType("GeneratedProvenance")
              let prop = t.GetProperty("typeIris")
              Expect.isNotNull prop "typeIris property should exist on GeneratedProvenance"
              let typeIris = prop.GetValue(null) :?> Map<string, string>

              Expect.isEmpty
                  typeIris
                  "typeIris is Map.empty — generator emits empty map when no provClass annotations are present"
          }

          testTask
              "useProvenance zero-arg: GeneratedProvenance found, middleware still emits prov:wasGeneratedBy Link header" {
              let asm = Assembly.GetExecutingAssembly()
              let t = asm.GetType("GeneratedProvenance")

              Expect.isNotNull
                  t
                  "GeneratedProvenance must be found — this test exercises the reflection path, not the null fallback"

              let config = ProvenanceConfig.Default
              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              builder.Services.AddSingleton<ProvenanceConfig>(config) |> ignore
              builder.Services.AddSingleton<ProvenanceStore>(config.Store) |> ignore
              let app = builder.Build()
              (app :> IApplicationBuilder).UseRouting() |> ignore
              app.UseMiddleware<ProvenanceMiddleware>() |> ignore

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
                  "prov:wasGeneratedBy Link header must be emitted even when GeneratedProvenance is found in assembly"
          } ]
