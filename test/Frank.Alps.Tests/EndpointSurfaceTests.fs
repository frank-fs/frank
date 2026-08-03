module Frank.Alps.Tests.EndpointSurfaceTests

open System
open System.Collections.Generic
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Expecto
open Frank.Builder
open Frank.Alps

/// Simple endpoint data source for tests (copied from Frank.Auth.Tests.AuthorizationTests)
type TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

let private noopDelegate: RequestDelegate = RequestDelegate(fun _ -> System.Threading.Tasks.Task.CompletedTask)

let makeEndpoint (routePattern: string) (metadata: obj list) : Endpoint =
    RouteEndpoint(noopDelegate, Patterns.RoutePatternFactory.Parse routePattern, 0, EndpointMetadataCollection(metadata), routePattern)

let private servicesWith (endpoints: Endpoint[]) : System.IServiceProvider =
    let services = ServiceCollection()
    services.AddSingleton<EndpointDataSource>(TestEndpointDataSource(endpoints) :> EndpointDataSource) |> ignore
    services.BuildServiceProvider() :> System.IServiceProvider

[<Tests>]
let tests =
    testList
        "EndpointSurface"
        [ test "allDescriptors finds a Descriptor attached to one endpoint's metadata" {
              let d = safe "listProducts"
              let services = servicesWith [| makeEndpoint "/products" [ box d ] |]

              let result = EndpointSurface.allDescriptors services

              Expect.equal (result |> List.map snd) [ d ] ""
          }

          test "allDescriptors skips endpoints with no Descriptor metadata" {
              let services = servicesWith [| makeEndpoint "/health" [] |]
              Expect.equal (EndpointSurface.allDescriptors services) [] ""
          }

          test "allDescriptors collects across multiple endpoints" {
              let a, b = safe "listProducts", unsafe "createProduct"
              let services = servicesWith [| makeEndpoint "/products" [ box a ]; makeEndpoint "/products" [ box b ] |]

              // Sort by ID: "createProduct" < "listProducts" alphabetically
              Expect.equal (EndpointSurface.allDescriptors services |> List.map snd |> List.sortBy (fun d -> d.Id)) [ b; a ] ""
          }

          test "descriptorsForRoute filters to endpoints sharing exactly that route pattern" {
              let a, b, c = safe "listProducts", unsafe "createProduct", safe "listOrders"

              let services =
                  servicesWith
                      [| makeEndpoint "/products" [ box a ]
                         makeEndpoint "/products" [ box b ]
                         makeEndpoint "/orders" [ box c ] |]

              let result = EndpointSurface.descriptorsForRoute services "/products" |> List.map snd |> List.sortBy (fun d -> d.Id)

              // Sort by ID: "createProduct" < "listProducts" alphabetically
              Expect.equal result [ b; a ] ""
          }

          test "descriptorsForRoute against an unknown route pattern is empty" {
              let services = servicesWith [| makeEndpoint "/products" [ box (safe "listProducts") ] |]
              Expect.equal (EndpointSurface.descriptorsForRoute services "/orders") [] ""
          } ]
