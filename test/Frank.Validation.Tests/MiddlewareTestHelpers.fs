module Frank.Validation.Tests.MiddlewareTestHelpers

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open VDS.RDF
open Frank.Semantic
open Frank.Validation

let private schemaOrgContextJson =
    """{"@context":{"@vocab":"https://schema.org/"}}"""

let private offlineLoader =
    JsonLdLoader.seeded
        [ "https://schema.org", schemaOrgContextJson
          "https://schema.org/", schemaOrgContextJson ]

let orderConfig () : ValidationConfig =
    let shapes =
        Shapes.toShapesGraph
            [ RecordShape(
                  Uri "https://schema.org/Order",
                  [ { Path = Uri "https://schema.org/totalPaymentDue"
                      Datatype = Some XsdDecimal
                      MinCount = 1
                      MaxCount = None
                      Pattern = None } ]
              ) ]

    { Shapes = shapes; ContextLoader = offlineLoader }

let startValidationServer (config: ValidationConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    let app = builder.Build()
    app.UseMiddleware<ValidationMiddleware>() |> ignore

    app.MapGet("/echo", System.Func<string>(fun () -> "downstream")) |> ignore

    app.MapPost(
        "/echo",
        System.Func<Microsoft.AspNetCore.Http.HttpContext, System.Threading.Tasks.Task<string>>(fun ctx ->
            task {
                use reader = new System.IO.StreamReader(ctx.Request.Body)
                let! body = reader.ReadToEndAsync()
                return $"downstream: {body.Length} bytes"
            })
    )
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app
