namespace Frank.Web.Http

open System
open System.Collections.Generic
open System.IO
open System.Net
open System.Net.Http
open System.Net.Http.Formatting
open System.Net.Http.Headers
open System.Text
open System.Web.Http
open Frank
open Frank.Web.Http
open NUnit.Framework 
open Swensen.Unquote.Assertions

[<Route("api/testcontroller")>]
type TestApi() =
    inherit ApiController()
    member this.Get() = "Hello, world!"

[<Route("api/test")>]
module Test =
    let get (request: HttpRequestMessage) = "Hello, world!"

module Test2Api =
    [<Route("api/test2")>]
    let get (request: HttpRequestMessage) = "Hello, world!"

module Tests =
    let assembliesResolver = Dispatcher.DefaultAssembliesResolver()

    [<Test>]
    let ``test FlexControllerTypeResolver allows use of modules for controllers`` () =
        let resolver = BetterDefaultHttpControllerTypeResolver()
        let types = resolver.GetControllerTypes(assembliesResolver) |> Seq.map (fun x -> x.Name) |> Set.ofSeq
        types =? set ["Test"; "TestApi"; "Test2Api"]

    [<Test; Ignore("DefaultHttpControllerSelector fails to handle modules and static classes")>]
    let ``test DefaultHttpControllerSelector can find module controllers``() =
        use config = new HttpConfiguration()
        config.Services.Replace(typeof<Dispatcher.IHttpControllerTypeResolver>, BetterDefaultHttpControllerTypeResolver())
        let controllerSelector = config.Services.GetHttpControllerSelector()
        let controllerMapping = controllerSelector.GetControllerMapping()
        controllerMapping.Count >? 0
        //controllerMapping.Keys |> Set.ofSeq =? set ["Test"; "TestApi"; "Test2Api"]

    [<Test; Ignore("DefaultHttpControllerSelector fails to handle modules and static classes")>]
    [<TestCase("api/test", "Test")>]
    [<TestCase("api/test2", "Test2Api")>]
    [<TestCase("api/testcontroller", "TestApi")>]
    let ``test DefaultHttpControllerSelector can select a module controller`` (routeTemplate: string, expected: string) =
        use config = new HttpConfiguration()
        config.Services.Replace(typeof<Dispatcher.IHttpControllerTypeResolver>, BetterDefaultHttpControllerTypeResolver())
        let controllerSelector = config.Services.GetHttpControllerSelector()
        let route = Routing.HttpRoute(routeTemplate)
        let routeData = Routing.HttpRouteData(route)
        use request = new HttpRequestMessage(HttpMethod.Get, "http://example.org/" + routeTemplate)
        request.SetRouteData(routeData)

        let controllerDescriptor = controllerSelector.SelectController(request)

        controllerDescriptor.ControllerType.Name =? expected
        controllerDescriptor.ControllerName =? expected

    [<Test; Ignore("DefaultHttpControllerSelector fails to handle modules and static classes")>]
    let ``test in-memory server with FlexControllerTypeResolver runs modules as controllers`` () =
        use config = new HttpConfiguration()
        config.Services.Replace(typeof<Dispatcher.IHttpControllerTypeResolver>, BetterDefaultHttpControllerTypeResolver())
        use server = new HttpServer(config)
        use client = new HttpClient(server)
        client.BaseAddress <- Uri("http://example.org/")
        let actual =
            async {
                let! response = client.GetAsync("api/test") |> Async.AwaitTask
                return! response.Content.ReadAsStringAsync() |> Async.AwaitTask
            } |> Async.RunSynchronously
        actual =? "Hello, world!"
