module Frank.Tests.MetadataTests

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Expecto
open Frank.Builder

[<Tests>]
let metadataTests =
    testList
        "ResourceSpec Metadata"
        [ test "Empty ResourceSpec has empty metadata list" {
              let spec = ResourceSpec.Empty
              Expect.isEmpty spec.Metadata "Metadata should be empty by default"
          }

          test "AddMetadata appends convention functions" {
              let convention1: EndpointBuilder -> unit = fun _ -> ()
              let convention2: EndpointBuilder -> unit = fun _ -> ()

              let spec =
                  ResourceSpec.Empty
                  |> fun s -> ResourceBuilder.AddMetadata(s, convention1)
                  |> fun s -> ResourceBuilder.AddMetadata(s, convention2)

              Expect.equal spec.Metadata.Length 2 "Should have two metadata conventions"
          }

          test "Build applies convention functions - metadata objects appear in endpoint" {
              let marker = obj ()
              let convention: EndpointBuilder -> unit = fun b -> b.Metadata.Add(marker)

              let spec =
                  { ResourceSpec.Empty with
                      Handlers = [ "GET", RequestDelegate(fun ctx -> Task.CompletedTask) ]
                      Metadata = [ convention ] }

              let resource = spec.Build("/test")
              let endpoint = resource.Endpoints[0]

              let found =
                  endpoint.Metadata |> Seq.exists (fun m -> Object.ReferenceEquals(m, marker))

              Expect.isTrue found "Marker metadata should be present in endpoint metadata"
          }

          test "Build without metadata produces endpoints with correct HTTP method and display name" {
              let handler = RequestDelegate(fun ctx -> Task.CompletedTask)

              let spec =
                  { ResourceSpec.Empty with
                      Name = Some "TestResource"
                      Handlers = [ "GET", handler ] }

              let resource = spec.Build("/test")
              let endpoint = resource.Endpoints[0] :?> RouteEndpoint
              Expect.equal endpoint.DisplayName "GET TestResource" "Display name should match"
              let httpMethodMetadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()
              Expect.isNotNull httpMethodMetadata "Should have HttpMethodMetadata"
              Expect.contains (httpMethodMetadata.HttpMethods |> Seq.toList) "GET" "Should contain GET method"
              Expect.equal endpoint.RoutePattern.RawText "/test" "Route pattern should match"
          }

          test "Build without metadata produces functionally identical endpoints to previous behavior" {
              let handler = RequestDelegate(fun ctx -> Task.CompletedTask)

              let spec =
                  { ResourceSpec.Empty with
                      Name = Some "MyResource"
                      Handlers = [ "POST", handler; "GET", handler ] }

              let resource = spec.Build("/api/items")
              Expect.equal resource.Endpoints.Length 2 "Should produce one endpoint per handler"

              for endpoint in resource.Endpoints do
                  let re = endpoint :?> RouteEndpoint
                  Expect.equal re.RoutePattern.RawText "/api/items" "Route pattern should match"
                  let httpMethod = re.Metadata.GetMetadata<HttpMethodMetadata>()
                  Expect.isNotNull httpMethod "Should have HttpMethodMetadata"
          } ]
