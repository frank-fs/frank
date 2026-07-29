module Frank.JsonHome.Tests.ApiSurfaceTests

open Microsoft.AspNetCore.Mvc.Abstractions
open Microsoft.AspNetCore.Mvc.ApiExplorer
open Expecto
open Frank.JsonHome

/// Builds an ApiDescription the way ApiExplorer would for one endpoint+method.
let private describe (relativePath: string) (httpMethod: string) (metadata: obj list) =
    let action = ActionDescriptor()
    action.EndpointMetadata <- ResizeArray metadata

    let description = ApiDescription()
    description.RelativePath <- relativePath
    description.HttpMethod <- httpMethod
    description.ActionDescriptor <- action
    description

[<Tests>]
let tests =
    testList
        "ApiSurface"
        [ test "groups descriptions by route template and collects methods" {
              let metadata: obj list = [ { Rel = "tag:example.com,2026:products" } ]

              let surface =
                  ApiSurface.ofApiDescriptions
                      [ describe "products" "GET" metadata
                        describe "products" "POST" metadata ]

              Expect.hasLength surface 1 "One resource"
              Expect.equal surface.[0].Rel "tag:example.com,2026:products" "Rel carried through"
              Expect.equal surface.[0].Href "/products" "Leading slash restored"
              Expect.isFalse surface.[0].IsTemplated "No variables"
              Expect.equal surface.[0].Methods [ "GET"; "POST" ] "Methods collected in order"
          }

          test "templated routes are translated and carry hrefVars" {
              let metadata: obj list =
                  [ { Rel = "tag:example.com,2026:product" }
                    { Name = "id"; Uri = "https://example.com/param/product-id" } ]

              let surface = ApiSurface.ofApiDescriptions [ describe "products/{id:guid}" "GET" metadata ]

              Expect.hasLength surface 1 "One resource"
              Expect.isTrue surface.[0].IsTemplated "Has a variable"
              Expect.equal surface.[0].Href "/products/{id}" "Constraint stripped"

              Expect.equal
                  surface.[0].HrefVars
                  [ "id", "https://example.com/param/product-id" ]
                  "hrefVars carried through"
          }

          test "two resources declaring the same rel on different route templates both pass through" {
              // Grouping is by route template, not by rel, so this is not
              // deduplicated or rejected here -- that is tracked separately
              // (#475). This locks in the current, actual behavior: both
              // entries survive, each still carrying the shared rel.
              let metadata: obj list = [ { Rel = "tag:example.com,2026:dup" } ]

              let surface =
                  ApiSurface.ofApiDescriptions [ describe "first" "GET" metadata; describe "second" "GET" metadata ]

              Expect.hasLength surface 2 "Both route templates produce separate entries"
              Expect.isTrue (surface |> List.forall (fun r -> r.Rel = "tag:example.com,2026:dup")) "Both carry the shared rel"

              Expect.equal
                  (surface |> List.map (fun r -> r.Href) |> List.sort)
                  [ "/first"; "/second" ]
                  "Distinct hrefs, not merged"
          }

          test "resources without a rel are excluded" {
              let surface = ApiSurface.ofApiDescriptions [ describe "internal" "GET" [] ]

              Expect.isEmpty surface "No rel means no entry"
          } ]
