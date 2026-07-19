module Frank.Discovery.Tests.OpenApiCorrelationTests

open Expecto
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Primitives
open Frank.Discovery
open Frank.Discovery.Tests.TestHelpers

/// Counts every access to `.Endpoints` — the #400 AC1 instrumentation point.
/// Frank.Discovery's ALPS-Type correlation and Microsoft.AspNetCore.OpenApi's own
/// document generation must share ONE walk of the live endpoint set (via the shared,
/// DI-cached IApiDescriptionGroupCollectionProvider), not one walk per component.
type private CountingEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    let mutable count = 0
    member _.AccessCount = count

    override _.Endpoints =
        count <- count + 1
        endpoints :> _

    override _.GetChangeToken() = NullChangeToken.Singleton :> _

// moveEndpoint() (a single POST /games/{id} endpoint carrying both correlation signals
// — relation + accepts, enough for methodsByRelation and methodsByRequestType to each
// find a match) is now TestHelpers.routeEndpoint (#400 /simplify Fix 4): same
// "RouteEndpoint + HttpMethodMetadata + handler.Method + extra metadata" construction as
// AlpsTypeReconciliationTests.fs's routeEndpoint.

[<Tests>]
let tests =
    testList
        "DiscoveryMiddleware — #400 AC1: single shared IApiDescriptionGroupCollectionProvider walk"
        [ test
              "Frank.Discovery's correlation + a second independent .ApiDescriptionGroups access (what Microsoft.AspNetCore.OpenApi's own document generation performs) walk EndpointDataSource.Endpoints exactly once total, not once per consumer" {
              let countingDs =
                  CountingEndpointDataSource(
                      [| routeEndpoint
                             "/games/{id}"
                             [| "POST" |]
                             [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata)
                               box (AcceptsMetadata([| "application/json" |], typeof<string>, false) :> obj) ] |]
                  )

              let provider = apiDescriptionProviderFor countingDs

              // Consumer #1: Frank.Discovery's own ALPS-Type correlation (DiscoveryMiddleware's
              // cachedAlpsDescriptors calls both of these against the same injected provider).
              DiscoveryMiddleware.methodsByRelation provider |> ignore
              DiscoveryMiddleware.methodsByRequestType provider |> ignore

              // Consumer #2: a second, independent access to the SAME provider instance —
              // exactly what Microsoft.AspNetCore.OpenApi's OpenApiDocumentService performs
              // internally when building its OpenApiDocument (it reads the same injected
              // IApiDescriptionGroupCollectionProvider via ApplyTransformersAsync/
              // GetOpenApiPathsAsync), simulated here without a full document-generation round trip.
              provider.ApiDescriptionGroups |> ignore

              Expect.equal
                  countingDs.AccessCount
                  1
                  "EndpointDataSource.Endpoints must be walked exactly once total, shared by every consumer of the DI-cached IApiDescriptionGroupCollectionProvider (TryAddSingleton, version-checked cache) — not once per component (#400 AC1)"
          } ]
