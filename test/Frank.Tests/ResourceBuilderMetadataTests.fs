module Frank.Tests.ResourceBuilderMetadataTests

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Expecto
open Frank.Builder

[<AllowNullLiteral>]
type MethodMarker(label: string) =
    member _.Label = label

let private endpointFor (resource: Resource) (httpMethod: string) =
    resource.Endpoints
    |> Array.find (fun e ->
        match e.Metadata.GetMetadata<HttpMethodMetadata>() with
        | null -> false
        | meta -> meta.HttpMethods |> Seq.contains httpMethod)

[<Tests>]
let tests =
    testList
        "ResourceBuilder.AddMethodMetadata"
        [ test "method-scoped metadata lands only on the matching endpoint" {
              let spec =
                  ResourceSpec.Empty
                  |> fun s -> ResourceBuilder.AddHandler("GET", s, RequestDelegate(fun _ -> Task.CompletedTask))
                  |> fun s -> ResourceBuilder.AddHandler("POST", s, RequestDelegate(fun _ -> Task.CompletedTask))
                  |> fun s ->
                      ResourceBuilder.AddMethodMetadata(
                          "POST",
                          s,
                          fun b -> b.Metadata.Add(MethodMarker "post-only")
                      )

              let resource = spec.Build("/things")

              let postMarker = (endpointFor resource "POST").Metadata.GetMetadata<MethodMarker>()
              Expect.isNotNull postMarker "POST endpoint should carry the marker"
              Expect.equal postMarker.Label "post-only" "Marker label should match"

              let getMarker = (endpointFor resource "GET").Metadata.GetMetadata<MethodMarker>()
              Expect.isNull getMarker "GET endpoint should not carry the marker"
          }

          test "resource-wide metadata still lands on every endpoint" {
              let spec =
                  ResourceSpec.Empty
                  |> fun s -> ResourceBuilder.AddHandler("GET", s, RequestDelegate(fun _ -> Task.CompletedTask))
                  |> fun s -> ResourceBuilder.AddHandler("POST", s, RequestDelegate(fun _ -> Task.CompletedTask))
                  |> fun s -> ResourceBuilder.AddMetadata(s, fun b -> b.Metadata.Add(MethodMarker "everywhere"))

              let resource = spec.Build("/things")

              for httpMethod in [ "GET"; "POST" ] do
                  let marker = (endpointFor resource httpMethod).Metadata.GetMetadata<MethodMarker>()
                  Expect.isNotNull marker (httpMethod + " endpoint should carry the marker")
          }

          test "handler definition metadata is scoped to its own HTTP method" {
              let listing =
                  handler {
                      name "listThings"
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let creating =
                  handler {
                      name "createThing"
                      handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                  }

              let built =
                  resource "/things" {
                      get listing
                      post creating
                  }

              let getName =
                  (endpointFor built "GET").Metadata.GetMetadata<Microsoft.AspNetCore.Routing.EndpointNameMetadata>()

              Expect.isNotNull getName "GET endpoint should carry a name"
              Expect.equal getName.EndpointName "listThings" "GET should carry its own name only"

              let postName =
                  (endpointFor built "POST").Metadata.GetMetadata<Microsoft.AspNetCore.Routing.EndpointNameMetadata>()

              Expect.isNotNull postName "POST endpoint should carry a name"
              Expect.equal postName.EndpointName "createThing" "POST should carry its own name only"
          } ]
