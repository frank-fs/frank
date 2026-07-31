module Frank.Tests.ResponseLinkTests

open System
open System.Net.Http
open System.Threading.Tasks
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

type private TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

/// Mirrors WebHostBuilder.Run's pipeline shape exactly (Run blocks, so tests
/// wire it by hand), letting a test configure the spec via the real CE and
/// register extra resources the way an app would.
let private createFullPipelineTestServer (configureSpec: WebHostSpec -> WebHostSpec) (resources: Resource list) =
    let spec = WebHostSpec.Empty |> configureSpec
    let testEndpoint =
        RouteEndpointBuilder(
            RequestDelegate(fun ctx -> ctx.Response.WriteAsync "OK"),
            Patterns.RoutePatternFactory.Parse "/test",
            0)
            .Build()
    let allEndpoints =
        testEndpoint :: (resources |> List.collect (fun r -> List.ofArray r.Endpoints))
        |> Array.ofList

    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        spec.Services services |> ignore)
                    .Configure(fun app ->
                        app
                        |> WebLink.useAppWideLinks spec.LinkProviders
                        |> spec.BeforeRoutingMiddleware
                        |> fun app -> app.UseRouting()
                        |> WebLink.useResourceScopedLinks
                        |> spec.Middleware
                        |> fun app ->
                            app.UseEndpoints(fun endpoints ->
                                endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

[<Tests>]
let webHostLinkOperationTests =
    testList "WebHostBuilder link operation" [
        testCase "link target rel appends a provider that always returns that link" (fun () ->
            let builder = WebHostBuilder([||])
            let spec = builder.Link(WebHostSpec.Empty, "/x", "x")
            Expect.equal (List.length spec.LinkProviders) 1 "One provider registered"
            let links = spec.LinkProviders.[0] null |> List.ofSeq
            Expect.equal links [ { Target = "/x"; Rel = "x"; Params = [] } ] "Static provider produces the configured link")

        testCase "link with a general provider appends it as-is" (fun () ->
            let builder = WebHostBuilder([||])
            let provider = fun (_: HttpContext) -> Seq.singleton { Target = "/y"; Rel = "y"; Params = [] }
            let spec = builder.Link(WebHostSpec.Empty, provider)
            Expect.equal (List.length spec.LinkProviders) 1 "One provider registered"
            Expect.equal (spec.LinkProviders.[0] null |> List.ofSeq) [ { Target = "/y"; Rel = "y"; Params = [] } ] "Provider unchanged")

        testCase "two link calls accumulate, not overwrite" (fun () ->
            let builder = WebHostBuilder([||])
            let spec =
                WebHostSpec.Empty
                |> fun s -> builder.Link(s, "/x", "x")
                |> fun s -> builder.Link(s, "/y", "y")
            Expect.equal (List.length spec.LinkProviders) 2 "Both providers registered")

        testCase "webHost CE link keyword resolves for both arities" (fun () ->
            // WebHostBuilder carries a `Run: WebHostSpec -> unit` member, so any
            // `webHost [||] { ... }` block is a full computation expression whose
            // *evaluation* auto-invokes Run -- i.e. calls .Build().Run() and
            // blocks starting a real host. That means we can't safely evaluate
            // `webHost [||] { link "/x" "x" }` in a test to pull a WebHostSpec
            // back out (unlike ResourceBuilder's Run, which just builds a value).
            // What we *can* verify safely is that the CE keyword syntax
            // type-checks at both arities -- if `link` carried
            // [<CustomOperation("link")>] on only one overload, the arity that
            // lost the attribute would fail to compile with FS3099 ("incorrect
            // number of arguments"), exactly as it did before this fix. Wrapping
            // in unexecuted functions proves the CE compiles without ever
            // invoking Run.
            let _staticArity: unit -> unit = fun () -> webHost [||] { link "/x" "x" }

            let _providerArity: unit -> unit =
                fun () ->
                    webHost [||] {
                        link (fun (_: HttpContext) -> Seq.singleton { Target = "/y"; Rel = "y"; Params = [] })
                    }

            Expect.isTrue true "Both `link` CE arities type-check under real webHost {} syntax (see comment above)")

        testTask "a response carries a link registered via WebHostSpec.LinkProviders (Run's pipeline shape)" {
            // Uses direct WebHostBuilder.Link(...) method calls -- not `webHost {}`
            // CE syntax -- to build the spec, for the same reason as above: the
            // CE would auto-invoke the blocking Run. This still exercises the
            // exact `Link` members the `link` CE keyword dispatches to, and
            // createFullPipelineTestServer reproduces Run's pipeline composition
            // by hand.
            let configure (spec: WebHostSpec) = (WebHostBuilder([||])).Link(spec, "/x", "x")
            let client = createFullPipelineTestServer configure []
            let! (response: HttpResponseMessage) = client.GetAsync("/test")
            Expect.contains (response.Headers.GetValues "Link" |> List.ofSeq) "</x>; rel=\"x\"" "Link header present with configured value"
        }
    ]
