module Frank.Tests.ResponseLinkTests

open System
open System.Net.Http
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder

/// Wires WebLink.useAppWideLinks and WebLink.useResourceScopedLinks the same
/// way WebHostBuilder.Run will (Task 3) -- before and after UseRouting,
/// respectively -- without going through the webHost {} CE, since Run blocks.
let private createTestServer (providers: (HttpContext -> WebLink seq) list) =
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                    .Configure(fun app ->
                        app
                        |> WebLink.useAppWideLinks providers
                        |> fun app -> app.UseRouting()
                        |> WebLink.useResourceScopedLinks
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                endpoints.MapGet(
                                    "/test",
                                    Func<HttpContext, Task>(fun ctx -> ctx.Response.WriteAsync "OK"))
                                |> ignore)
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

let private createTestServerWithExceptionHandler (providers: (HttpContext -> WebLink seq) list) =
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services -> services.AddRouting() |> ignore)
                    .Configure(fun app ->
                        app.UseExceptionHandler(fun errApp ->
                            errApp.Run(fun ctx ->
                                ctx.Response.StatusCode <- 500
                                ctx.Response.WriteAsync "error"))
                        |> ignore

                        app
                        |> WebLink.useAppWideLinks providers
                        |> fun app -> app.Run(fun _ -> failwith "boom"))
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

[<Tests>]
let appWideLinkTests =
    testList "WebLink.useAppWideLinks" [
        testTask "no providers registered adds no Link header" {
            let client = createTestServer []
            let! (response: HttpResponseMessage) = client.GetAsync("/test")
            Expect.isFalse (response.Headers.Contains "Link") "No Link header"
        }

        testTask "a single provider's link appears on the response" {
            let providers = [ fun (_: HttpContext) -> Seq.singleton { Target = "/x"; Rel = "x"; Params = [] } ]
            let client = createTestServer providers
            let! (response: HttpResponseMessage) = client.GetAsync("/test")
            Expect.isTrue (response.Headers.Contains "Link") "Link header present"
            Expect.contains (response.Headers.GetValues "Link" |> List.ofSeq) "</x>; rel=\"x\"" "Correct value"
        }

        testTask "two providers combine into one Link header carrying both values" {
            let providers =
                [ fun (_: HttpContext) -> Seq.singleton { Target = "/a"; Rel = "a"; Params = [] }
                  fun (_: HttpContext) -> Seq.singleton { Target = "/b"; Rel = "b"; Params = [] } ]
            let client = createTestServer providers
            let! (response: HttpResponseMessage) = client.GetAsync("/test")
            let values = response.Headers.GetValues "Link" |> List.ofSeq
            Expect.contains values "</a>; rel=\"a\"" "First provider's value present"
            Expect.contains values "</b>; rel=\"b\"" "Second provider's value present"
        }

        testTask "a provider returning an empty sequence contributes nothing" {
            let providers = [ fun (_: HttpContext) -> Seq.empty ]
            let client = createTestServer providers
            let! (response: HttpResponseMessage) = client.GetAsync("/test")
            Expect.isFalse (response.Headers.Contains "Link") "No Link header from an empty contribution"
        }

        testTask "app-wide links appear on an unmatched route (404)" {
            let providers = [ fun (_: HttpContext) -> Seq.singleton { Target = "/x"; Rel = "x"; Params = [] } ]
            let client = createTestServer providers
            let! (response: HttpResponseMessage) = client.GetAsync("/nope")
            Expect.isTrue (response.Headers.Contains "Link") "Link header present on a 404"
        }

        testTask "app-wide links survive UseExceptionHandler regenerating the response" {
            let providers = [ fun (_: HttpContext) -> Seq.singleton { Target = "/x"; Rel = "x"; Params = [] } ]
            let client = createTestServerWithExceptionHandler providers
            let! (response: HttpResponseMessage) = client.GetAsync("/boom")
            Expect.equal (int response.StatusCode) 500 "Exception handler produced the response"
            Expect.isTrue (response.Headers.Contains "Link") "Link header survives Response.Clear()"
        }
    ]
