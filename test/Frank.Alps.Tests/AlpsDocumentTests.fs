module Frank.Alps.Tests.AlpsDocumentTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "AlpsDocument.validate"
        [ test "a safe transition bound to GET passes" {
              let endpoint = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "GET" ]) ]
              AlpsDocument.validate [ endpoint, safe "x" ]
          }

          test "a safe transition bound to POST raises" {
              let endpoint = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "POST" ]) ]
              Expect.throws (fun () -> AlpsDocument.validate [ endpoint, safe "x" ]) ""
          }

          test "an idempotent transition bound to PUT or DELETE passes, bound to GET raises" {
              let put = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "PUT" ]) ]
              AlpsDocument.validate [ put, idempotent "x" ]

              let get = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "GET" ]) ]
              Expect.throws (fun () -> AlpsDocument.validate [ get, idempotent "x" ]) ""
          }

          test "an unsafe transition bound to POST passes, bound to GET raises" {
              let post = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "POST" ]) ]
              AlpsDocument.validate [ post, unsafe "x" ]

              let get = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "GET" ]) ]
              Expect.throws (fun () -> AlpsDocument.validate [ get, unsafe "x" ]) ""
          }

          test "semantic descriptors are never validated against a bound method" {
              let endpoint = EndpointSurfaceTests.makeEndpoint "/x" [ box (Microsoft.AspNetCore.Routing.HttpMethodMetadata [ "POST" ]) ]
              AlpsDocument.validate [ endpoint, semantic "x" ]
          } ]
