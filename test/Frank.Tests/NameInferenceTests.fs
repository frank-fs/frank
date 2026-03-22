module Frank.Tests.NameInferenceTests

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Expecto
open Frank.Builder

let private buildDisplayName routeTemplate (configure: ResourceSpec -> ResourceSpec) =
    let handler = RequestDelegate(fun _ -> Task.CompletedTask)

    let spec =
        { ResourceSpec.Empty with
            Handlers = [ "GET", handler ] }
        |> configure

    let resource = spec.Build(routeTemplate)
    let endpoint = resource.Endpoints[0] :?> RouteEndpoint
    endpoint.DisplayName

let private buildWithRoute routeTemplate = buildDisplayName routeTemplate id

let private buildWithExplicitName routeTemplate name =
    buildDisplayName routeTemplate (fun spec -> { spec with Name = Some name })

[<Tests>]
let nameInferenceTests =
    testList
        "ResourceSpec Name Inference"
        [ test "root path infers Root" { Expect.equal (buildWithRoute "/") "GET Root" "/ should infer Root" }

          test "simple resource infers title-cased name" {
              Expect.equal (buildWithRoute "/users") "GET Users" "/users should infer Users"
          }

          test "trailing path parameter singularizes preceding segment" {
              Expect.equal (buildWithRoute "/users/{id}") "GET User" "/users/{id} should infer User"
          }

          test "multi-segment path joins with space" {
              Expect.equal (buildWithRoute "/admin/users") "GET Admin Users" "/admin/users should infer Admin Users"
          }

          test "hyphenated segments split and title-case" {
              Expect.equal (buildWithRoute "/not-for-sale") "GET Not For Sale" "/not-for-sale should infer Not For Sale"
          }

          test "underscored segments split and title-case" {
              Expect.equal (buildWithRoute "/my_items") "GET My Items" "/my_items should infer My Items"
          }

          test "multiple path parameters strip all params" {
              Expect.equal
                  (buildWithRoute "/users/{userId}/posts/{postId}")
                  "GET User Post"
                  "Multiple trailing params singularize each preceding segment"
          }

          test "explicit name overrides inference" {
              Expect.equal
                  (buildWithExplicitName "/users" "People")
                  "GET People"
                  "Explicit name should override inference"
          }

          test "path with only parameter infers Root" {
              Expect.equal (buildWithRoute "/{id}") "GET Root" "/{id} should infer Root"
          }

          test "trailing slash is ignored" {
              Expect.equal (buildWithRoute "/users/") "GET Users" "/users/ should infer Users"
          }

          test "route constraints in parameters are treated as params" {
              Expect.equal
                  (buildWithRoute "/users/{id:int}")
                  "GET User"
                  "/users/{id:int} should treat constrained param as param"
          }

          test "empty string route infers Root" {
              Expect.equal (buildWithRoute "") "GET Root" "empty string should infer Root"
          } ]
