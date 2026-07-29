module Frank.JsonHome.Tests.ResourceMetadataTests

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Expecto
open Frank.Builder
open Frank.JsonHome

let private noop: RequestDelegate = RequestDelegate(fun _ -> Task.CompletedTask)

[<Tests>]
let tests =
    testList
        "Resource discovery metadata"
        [ test "rel is attached to every endpoint in the resource" {
              let built =
                  resource "/products" {
                      rel "tag:example.com,2026:products"
                      get noop
                      post noop
                  }

              Expect.hasLength built.Endpoints 2 "Two endpoints"

              for endpoint in built.Endpoints do
                  let meta = endpoint.Metadata.GetMetadata<RelMetadata>()
                  Expect.isNotNull (box meta) "Every endpoint carries the rel"
                  Expect.equal meta.Rel "tag:example.com,2026:products" "Rel value matches"
          }

          test "hrefVar, docs, and status are attached" {
              let built =
                  resource "/products/{id}" {
                      rel "tag:example.com,2026:product"
                      hrefVar "id" "https://example.com/param/product-id"
                      docs "https://example.com/docs/products"
                      deprecated
                      get noop
                  }

              let endpoint = built.Endpoints.[0]

              let hrefVars = endpoint.Metadata.GetOrderedMetadata<HrefVarMetadata>()
              Expect.hasLength hrefVars 1 "One hrefVar"
              Expect.equal hrefVars.[0].Name "id" "Variable name"
              Expect.equal hrefVars.[0].Uri "https://example.com/param/product-id" "Variable URI"

              let docsMeta = endpoint.Metadata.GetMetadata<DocsMetadata>()
              Expect.equal docsMeta.Uri "https://example.com/docs/products" "Docs URI"

              let status = endpoint.Metadata.GetMetadata<StatusMetadata>()
              Expect.equal status.Status ResourceStatus.Deprecated "Status is deprecated"
          }

          test "the optional hint operations attach metadata" {
              let built =
                  resource "/files/{name}" {
                      rel "tag:example.com,2026:file"
                      acceptRanges [ "bytes" ]
                      acceptPrefer [ "return=minimal" ]
                      preconditionRequired [ Precondition.ETag ]
                      authScheme "Basic" [ "private" ]
                      authScheme "Bearer" []
                      get noop
                  }

              let endpoint = built.Endpoints.[0]

              Expect.equal
                  (endpoint.Metadata.GetMetadata<AcceptRangesMetadata>()).Units
                  [ "bytes" ]
                  "acceptRanges units"

              Expect.equal
                  (endpoint.Metadata.GetMetadata<AcceptPreferMetadata>()).Preferences
                  [ "return=minimal" ]
                  "acceptPrefer preferences"

              Expect.equal
                  (endpoint.Metadata.GetMetadata<PreconditionRequiredMetadata>()).Preconditions
                  [ Precondition.ETag ]
                  "preconditionRequired preconditions"

              let schemes = endpoint.Metadata.GetOrderedMetadata<AuthSchemeMetadata>()
              Expect.hasLength schemes 2 "Two auth schemes, in declaration order"
              Expect.equal schemes.[0].Scheme "Basic" "First scheme"
              Expect.equal schemes.[0].Realms [ "private" ] "First scheme's realms"
              Expect.equal schemes.[1].Scheme "Bearer" "Second scheme"
              Expect.isEmpty schemes.[1].Realms "Second scheme has no realms"
          }

          test "resources without a rel carry no rel metadata" {
              let built = resource "/internal" { get noop }

              Expect.isNull (box (built.Endpoints.[0].Metadata.GetMetadata<RelMetadata>())) "No rel metadata"
          } ]
