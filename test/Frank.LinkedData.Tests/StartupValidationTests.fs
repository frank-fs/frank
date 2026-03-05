module Frank.LinkedData.Tests.StartupValidationTests

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
open VDS.RDF
open Frank.Builder
open Frank.LinkedData
open Frank.LinkedData.Rdf

[<Tests>]
let tests =
    testList "StartupValidation" [
        testCase "valid configuration starts successfully via TestHost" <| fun _ ->
            // The test assembly has embedded resources (manifest, ontology, shapes)
            // so useLinkedDataWith with a valid config should start fine.
            let ontology = new Graph()
            let s = ontology.CreateUriNode(UriFactory.Root.Create("http://example.org/api/properties/Test/Name"))
            let p = ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type"))
            let o = ontology.CreateUriNode(UriFactory.Root.Create("http://www.w3.org/2002/07/owl#DatatypeProperty"))
            ontology.Assert(Triple(s, p, o)) |> ignore

            let manifest : SemanticManifest =
                { Version = "1.0.0"
                  BaseUri = "http://example.org/api"
                  SourceHash = "abc"
                  Vocabularies = []
                  GeneratedAt = DateTimeOffset.UtcNow }
            let config : LinkedDataConfig =
                { OntologyGraph = ontology
                  ShapesGraph = new Graph()
                  BaseUri = "http://example.org/api"
                  Manifest = manifest }

            let hostBuilder =
                HostBuilder()
                    .ConfigureWebHost(fun webBuilder ->
                        webBuilder
                            .UseTestServer()
                            .ConfigureServices(fun services ->
                                services.AddSingleton<LinkedDataConfig>(config) |> ignore
                                services.AddRouting() |> ignore)
                            .Configure(fun (app: IApplicationBuilder) ->
                                app.UseRouting() |> ignore
                                app.Use(Func<HttpContext, RequestDelegate, Task>(
                                    WebHostBuilderExtensions.linkedDataMiddleware)) |> ignore
                                app.UseEndpoints(fun endpoints ->
                                    endpoints.MapGet("/test", RequestDelegate(fun ctx ->
                                        ctx.Response.WriteAsync("ok"))) |> ignore) |> ignore)
                        |> ignore)
            use host = hostBuilder.Build()
            host.Start()
            let server = host.GetTestServer()
            use client = server.CreateClient()
            let response : HttpResponseMessage = client.GetAsync("/test").Result
            Expect.equal (int response.StatusCode) 200 "App should start and serve requests"

        testCase "missing embedded resources produces descriptive error" <| fun _ ->
            // Use an assembly that doesn't have the Frank.Semantic.* embedded resources
            let assembly = typeof<int>.Assembly
            let result = GraphLoader.load assembly
            Expect.isError result "Should fail for assembly without embedded resources"
            match result with
            | Error msg ->
                Expect.stringContains msg "Frank.Semantic.manifest.json"
                    "Error should mention the expected resource name"
                Expect.stringContains msg (assembly.GetName().Name)
                    "Error should mention the assembly name"
                Expect.stringContains msg "frank-cli compile"
                    "Error should suggest running frank-cli compile"
            | _ -> failwith "unreachable"

        testCase "app without useLinkedData starts normally with no false positives" <| fun _ ->
            // An app that does NOT register LinkedData middleware should start fine
            // even when no embedded resources exist
            let hostBuilder =
                HostBuilder()
                    .ConfigureWebHost(fun webBuilder ->
                        webBuilder
                            .UseTestServer()
                            .ConfigureServices(fun services ->
                                services.AddRouting() |> ignore)
                            .Configure(fun (app: IApplicationBuilder) ->
                                app.UseRouting() |> ignore
                                app.UseEndpoints(fun endpoints ->
                                    endpoints.MapGet("/hello", RequestDelegate(fun ctx ->
                                        ctx.Response.WriteAsync("world"))) |> ignore) |> ignore)
                        |> ignore)
            use host = hostBuilder.Build()
            host.Start()
            let server = host.GetTestServer()
            use client = server.CreateClient()
            let response : HttpResponseMessage = client.GetAsync("/hello").Result
            let body = response.Content.ReadAsStringAsync().Result
            Expect.equal body "world" "App without useLinkedData should work normally"
    ]
