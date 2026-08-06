module Frank.OpenApi.Tests.NegotiateMetadataTests

open System.Net.Http.Json
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Expecto
open Frank.Builder
open Frank.OpenApi.Tests.OpenApiDocumentTests

type Product = { Name: string; Price: decimal }

// NOTE (Task 5 finding -- see task-5-report.md): this test originally FAILED, and not
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
// response's `content` originally failed: only the LAST-registered representation's
// metadata survived. This reproduces with plain minimal APIs with no Frank involved at
// all (`.Produces<T>(200, "text/html").Produces<T>(200, "application/json")` on a bare
// `MapGet`) -- so it is a Microsoft.AspNetCore.OpenApi behavior (last
// IProducesResponseTypeMetadata per status code wins; it does not merge sibling
// metadata objects targeting the same status code), not a Frank defect. Endpoint-level
// metadata WAS already correctly merged and present (verified directly on
// `Resource.Endpoints.[].Metadata` before the OpenAPI document is generated) -- Frank's
// own pipeline (Tasks 1-4 + existing HandlerDefinition/ResourceBuilder/OpenApi wiring)
// worked exactly as designed. The gap was downstream, in Microsoft.AspNetCore.OpenApi's
// document assembly.
//
// Task 8 fixed it: `Negotiation.mergeProducesMetadata` (in NegotiateBuilder.fs) now
// merges representations that share both status code AND response type into one
// `ProducesResponseTypeMetadata` with a unioned `ContentTypes` array -- the
// one-object-many-content-types shape that already reached the document correctly --
// before `NegotiateBuilder.Run` ever hands the metadata list to
// Microsoft.AspNetCore.OpenApi. This test now PASSES and is kept as the regression test
// for that fix. The narrower case -- representations sharing a status code with
// genuinely DIFFERENT response types -- remains a documented, unfixed limitation (see
// NegotiateBuilder.fs's doc comment and NegotiateBuilderTests.fs's "is NOT merged" test).
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
          }

          // Regression test for dotnet/aspnetcore#58329: Microsoft.AspNetCore.OpenApi
          // collapses multiple RouteEndpoints sharing the same (path, method) key down
          // to ONE operation, keeping only the LAST-registered endpoint's metadata. A
          // `negotiate { }` block with N representations registers N separate
          // RouteEndpoints at the exact same path+verb (dispatch among them happens at
          // the routing layer, via FrankProducesMatcherPolicy, not by folding them into
          // one endpoint) -- so without the broadcast-merge in `NegotiateBuilder.Run`,
          // this framework bug would silently drop every representation but the last
          // from the generated OpenAPI document. This test proves both that the two
          // RouteEndpoints really are separate (the premise of the bug) and that the
          // resulting operation still lists every content type (the mitigation).
          testCaseAsync "N separate RouteEndpoints from negotiate { } still produce ONE OpenAPI operation listing every content type -- mitigates dotnet/aspnetcore#58329"
          <| async {
              let negotiatedHandler =
                  negotiate {
                      accepts "application/json" (handler {
                          produces typeof<Product> 200 [ "application/json" ]
                          handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                      })
                      accepts "text/html" (handler {
                          produces typeof<Product> 200 [ "text/html" ]
                          handle (fun (ctx: HttpContext) -> Task.CompletedTask)
                      })
                  }

              let resourceSpec =
                  resource "/products/{id}" {
                      get negotiatedHandler
                  }

              // Premise: negotiate { } really did register two SEPARATE RouteEndpoints
              // at the same path+verb, not one endpoint -- otherwise this test would not
              // be exercising the framework's collapse bug at all.
              let routeEndpoints =
                  resourceSpec.Endpoints
                  |> Array.map (fun e -> e :?> RouteEndpoint)

              Expect.equal routeEndpoints.Length 2 "negotiate { } with two representations should register two separate RouteEndpoints"
              Expect.allEqual
                  (routeEndpoints |> Array.map (fun e -> e.RoutePattern.RawText))
                  routeEndpoints.[0].RoutePattern.RawText
                  "Both RouteEndpoints should share the same path"
              Expect.allEqual
                  (routeEndpoints |> Array.map (fun e -> e.Metadata.GetMetadata<HttpMethodMetadata>().HttpMethods |> Seq.exactlyOne))
                  "GET"
                  "Both RouteEndpoints should share the same HTTP method"

              let client = createOpenApiTestServer [ resourceSpec ]
              let! json = client.GetStringAsync(openApiRoutePattern) |> Async.AwaitTask
              use doc = JsonDocument.Parse(json)

              let responses =
                  doc.RootElement
                      .GetProperty("paths")
                      .GetProperty("/products/{id}")
                      .GetProperty("get")
                      .GetProperty("responses")
                      .GetProperty("200")
                      .GetProperty("content")

              let contentTypes =
                  responses.EnumerateObject()
                  |> Seq.map (fun p -> p.Name)
                  |> Set.ofSeq

              Expect.equal
                  contentTypes
                  (Set.ofList [ "application/json"; "text/html" ])
                  "Both content types must appear despite two separate RouteEndpoints at the same path+verb -- proves the broadcast-merge in NegotiateBuilder.Run works around the framework's last-write-wins collapse"
          } ]
