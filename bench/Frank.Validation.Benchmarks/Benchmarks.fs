module Benchmarks

open System
open System.Net.Http
open System.Text
open System.Threading.Tasks
open BenchmarkDotNet.Attributes
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Frank.Semantic
open Frank.Validation

/// SHACL shape requiring `totalPaymentDue` (xsd:decimal, min 1) on schema:Order — same
/// shape used by ValidationMiddleware's static-shape gate on the 422 path.
let private orderConfig () : ValidationConfig =
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

    { Shapes = shapes
      ContextLoader = JsonLdLoader.synthesizing [ "https://schema.org/" ]
      MaxBodyBytes = ValidationConfig.defaultMaxBodyBytes
      HostRelativeProperties = [] }

let private validOrderBody =
    """{
  "@context": "https://schema.org",
  "@type": "Order",
  "@id": "https://example.org/order/1",
  "totalPaymentDue": {"@value": "100", "@type": "http://www.w3.org/2001/XMLSchema#decimal"}
}"""

/// Missing datatype on totalPaymentDue → static SHACL shape rejects → drives the full
/// 422 path: parse → merge graphs → SHACL validate → re-store (Normalised graph) → serialize.
let private invalidOrderBody =
    """{
  "@context": "https://schema.org",
  "@type": "Order",
  "@id": "https://example.org/order/1",
  "totalPaymentDue": "not-a-number"
}"""

[<MemoryDiagnoser>]
[<SimpleJob(warmupCount = 3, iterationCount = 15)>]
type ValidationBenchmarks() =

    let mutable app: Microsoft.AspNetCore.Builder.WebApplication =
        Unchecked.defaultof<_>

    let mutable client: HttpClient = Unchecked.defaultof<_>

    let post (body: string) : Task<HttpResponseMessage> =
        let content = new StringContent(body, Encoding.UTF8, "application/ld+json")
        client.PostAsync("/echo", content)

    [<GlobalSetup>]
    member _.Setup() =
        let builder = WebApplication.CreateBuilder()
        builder.WebHost.UseTestServer() |> ignore
        builder.Services.AddSingleton(orderConfig ()) |> ignore
        app <- builder.Build()
        app.UseMiddleware<ValidationMiddleware>() |> ignore

        app.MapPost(
            "/echo",
            Func<HttpContext, Task<string>>(fun ctx ->
                task {
                    use reader = new System.IO.StreamReader(ctx.Request.Body)
                    let! body = reader.ReadToEndAsync()
                    return $"downstream: {body.Length} bytes"
                })
        )
        |> ignore

        app.StartAsync().GetAwaiter().GetResult()
        client <- app.GetTestClient()

    [<GlobalCleanup>]
    member _.Cleanup() =
        client.Dispose()
        app.DisposeAsync().AsTask().GetAwaiter().GetResult()

    /// Baseline: valid body, passes through to handler (200). No SHACL rejection, no
    /// report serialization — parse + merge + static validate (conforms) + next.Invoke only.
    [<Benchmark(Baseline = true)>]
    member _.PassThrough200() : Task<HttpResponseMessage> = post validOrderBody

    /// The heavier path per #373: parse → merge graphs → SHACL validate (rejects) →
    /// re-store the Normalised report graph → serialize the report as JSON-LD → 422.
    [<Benchmark>]
    member _.Reject422() : Task<HttpResponseMessage> = post invalidOrderBody
