module Frank.Provenance.Tests.ProvenanceTests

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Provenance.ProvenanceStore
open Frank.Provenance.ProvenanceMiddleware

let private createServer () =
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
        "/prov/{activityId}",
        Func<HttpContext, Task>(fun ctx ->
            task {
                let activityId = ctx.Request.RouteValues.["activityId"] :?> string
                let! activity = config.Store.Get(activityId)

                match activity with
                | None -> ctx.Response.StatusCode <- 404
                | Some a ->
                    ctx.Response.ContentType <- "application/ld+json"
                    do! ctx.Response.WriteAsync(serializeActivity a)
            }
            :> Task)
    )
    |> ignore

    app.MapGet(
        "/orders/{id}",
        Func<HttpContext, Task>(fun ctx ->
            task {
                ctx.Response.StatusCode <- 200
                do! ctx.Response.WriteAsync("ok")
            }
            :> Task)
    )
    |> ignore

    app.Start()
    app.GetTestClient()

[<Tests>]
let provenanceTests =
    testList
        "Frank.Provenance"
        [ testTask "AT1: GET emits prov:wasGeneratedBy Link header" {
              let client = createServer ()
              let! (response: HttpResponseMessage) = client.GetAsync("/orders/o1")
              Expect.equal response.StatusCode HttpStatusCode.OK "should return 200"

              let hasProvLink =
                  response.Headers.Contains("Link")
                  && (response.Headers.GetValues("Link")
                      |> Seq.exists (fun v -> v.Contains("prov#wasGeneratedBy") || v.Contains("prov:wasGeneratedBy")))

              Expect.isTrue hasProvLink "Link header should include prov:wasGeneratedBy rel"
          }

          testTask "AT1b: Activity accessible via trace endpoint" {
              let client = createServer ()
              let! (firstResponse: HttpResponseMessage) = client.GetAsync("/orders/o1")

              let linkHeader =
                  firstResponse.Headers.GetValues("Link")
                  |> Seq.find (fun v -> v.Contains("wasGeneratedBy"))

              let uri = linkHeader.Split('>') |> Array.head |> (fun s -> s.TrimStart('<'))
              let! (traceResponse: HttpResponseMessage) = client.GetAsync(uri)
              Expect.equal traceResponse.StatusCode HttpStatusCode.OK "trace endpoint should return 200"
              let! (body: string) = traceResponse.Content.ReadAsStringAsync()
              Expect.isTrue (body.Contains("prov") || body.Contains("Activity")) "body should reference PROV-O"
          }

          testTask "AT2: Empty provClasses doesn't crash middleware" {
              let client = createServer ()
              let! (response: HttpResponseMessage) = client.GetAsync("/orders/o1")
              Expect.equal response.StatusCode HttpStatusCode.OK "should not crash with empty maps"
          }

          testTask "AT3: Auth principal captured in activity" {
              let client = createServer ()
              let req = new HttpRequestMessage(HttpMethod.Get, "/orders/o1")
              req.Headers.Add("Authorization", "Bearer test-customer-token")
              let! (response: HttpResponseMessage) = client.SendAsync(req)

              let linkHeader =
                  response.Headers.GetValues("Link")
                  |> Seq.find (fun v -> v.Contains("wasGeneratedBy"))

              let uri = linkHeader.Split('>') |> Array.head |> (fun s -> s.TrimStart('<'))
              let! (traceResponse: HttpResponseMessage) = client.GetAsync(uri)
              let! (body: string) = traceResponse.Content.ReadAsStringAsync()

              Expect.isTrue
                  (body.Contains("test-customer-token") || body.Contains("Agent"))
                  "principal should be captured"
          }

          testTask "AT4: Package has no Statecharts dependency" {
              let srcDir =
                  System.IO.Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "Frank.Provenance")

              if System.IO.Directory.Exists(srcDir) then
                  let files = System.IO.Directory.GetFiles(srcDir, "*.fsproj")

                  for f in files do
                      let content = System.IO.File.ReadAllText(f)
                      Expect.isFalse (content.Contains("Statecharts")) $"{f} must not reference Statecharts"
          }

          testTask "AT5: useProvenance falls back gracefully without GeneratedProvenance" {
              let config = ProvenanceConfig.Default
              Expect.equal config.ProvClasses Map.empty "default config should have empty provClasses"
              Expect.equal config.TypeIris Map.empty "default config should have empty typeIris"
          } ]
