module Frank.Validation.Tests.ResourceBuilderExtensionsTests

open System
open Expecto
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

[<Tests>]
let tests =
    testList
        "useValidation (resource{ })"
        [ test "useValidation attaches ValidationMetadata to every endpoint the resource builds" {
              let shapesGraph =
                  Shacl.toShapesGraph [ recordShape (targetClass (Uri "https://schema.org/MoveAction")) [] ]

              let built =
                  resource "/games/{id}/moves" {
                      useValidation shapesGraph
                      post (RequestDelegate(fun (_: HttpContext) -> System.Threading.Tasks.Task.CompletedTask))
                  }

              Expect.hasLength built.Endpoints 1 "one endpoint (POST)"
              let metadata = built.Endpoints.[0].Metadata.GetMetadata<ValidationMetadata>()
              Expect.isNotNull (box metadata) "ValidationMetadata attached"

              match metadata with
              | ValidationMetadata sg -> Expect.equal sg shapesGraph "the exact ShapesGraph passed to useValidation"
          }

          test "a resource without useValidation has no ValidationMetadata (opt-in, never implicit)" {
              let built =
                  resource "/games/{id}" {
                      get (RequestDelegate(fun (_: HttpContext) -> System.Threading.Tasks.Task.CompletedTask))
                  }

              let metadata = built.Endpoints.[0].Metadata.GetMetadata<ValidationMetadata>()
              Expect.isTrue (obj.ReferenceEquals(metadata, null)) "no metadata when useValidation isn't called"
          } ]
