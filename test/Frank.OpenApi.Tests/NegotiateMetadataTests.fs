module Frank.OpenApi.Tests.NegotiateMetadataTests

open System.Net.Http.Json
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Builder
open Frank.OpenApi.Tests.OpenApiDocumentTests

type Product = { Name: string; Price: decimal }

// NOTE (Task 5 finding -- see task-5-report.md): this test currently FAILS, and not
// because of a wrong JSON-path guess. Two adjustments beyond the brief's literal code
// were required just to get it running:
//   1. `handler { produces ... }` with no `handle` throws at CE-Run time ("Handler must
//      be set using the 'handle' operation"), so a `handle` was added to each representation.
//   2. `producesEmpty` always declares content-type "application/json" (see
//      HandlerBuilder.fs), so the brief's "text/html" representation was, as written,
//      declaring metadata that also claimed "application/json" -- colliding with the
//      other representation's metadata for the same status code and making the failure
//      impossible to attribute to either concern. Both representations below now
//      explicitly declare their own matching content type via `produces ... [ctype]`.
//
// Even with both fixes, the assertion that BOTH media types appear under the 200
// response's `content` fails: only the LAST-registered representation's metadata
// survives. This reproduces with plain minimal APIs with no Frank involved at all
// (`.Produces<T>(200, "text/html").Produces<T>(200, "application/json")` on a bare
// `MapGet`) -- so it is a Microsoft.AspNetCore.OpenApi behavior (last
// IProducesResponseTypeMetadata per status code wins; it does not merge sibling
// metadata objects targeting the same status code), not a Frank defect. Endpoint-level
// metadata IS correctly merged and present (verified directly on `Resource.Endpoints.[].Metadata`
// before the OpenAPI document is generated) -- Frank's own pipeline (Tasks 1-4 +
// existing HandlerDefinition/ResourceBuilder/OpenApi wiring) works exactly as designed.
// The gap is downstream, in Microsoft.AspNetCore.OpenApi's document assembly.
[<Tests>]
let tests =
    testList
        "Negotiate metadata reaches the OpenAPI document"
        [ testCaseAsync "a resource using negotiate { } with handler{}-declared representations lists both media types"
          <| async {
              let negotiatedHandler =
                  negotiate {
                      accepts "text/html" (handler {
                          produces typeof<Product> 200 [ "text/html" ]
                          handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                      })
                      accepts "application/json" (handler {
                          produces typeof<Product> 200 [ "application/json" ]
                          handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                      })
                  }

              let resourceSpec =
                  resource "/negotiated-products/{id}" {
                      get negotiatedHandler
                  }

              let client = createOpenApiTestServer [ resourceSpec ]
              let! json = client.GetStringAsync(openApiRoutePattern) |> Async.AwaitTask
              use doc = JsonDocument.Parse(json)

              let responses =
                  doc.RootElement
                      .GetProperty("paths")
                      .GetProperty("/negotiated-products/{id}")
                      .GetProperty("get")
                      .GetProperty("responses")
                      .GetProperty("200")
                      .GetProperty("content")

              Expect.isTrue (responses.TryGetProperty("application/json") |> fst) "JSON representation's metadata should appear"
              Expect.isTrue (responses.TryGetProperty("text/html") |> fst) "HTML representation's metadata should appear"
          } ]
