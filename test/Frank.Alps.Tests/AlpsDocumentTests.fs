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
          }

          test "unboundTransitions reports a nested transition nothing binds, and never a semantic one" {
              // A transition with no bound endpoint drops out of the served document (authorization
              // could never be evaluated for it). Semantic descriptors routinely have no binding --
              // they're vocabulary -- so they must never be reported.
              let bound = safe "viewGame"
              let unbound = unsafe "makeMove"

              let profile =
                  [ semantic "game" |> contains [ bound; unbound ]; semantic "orphanVocabulary" ]

              let endpoint = EndpointSurfaceTests.makeEndpoint "/games/{id}" []

              Expect.equal
                  (AlpsDocument.unboundTransitions profile [ endpoint, bound ] |> List.map (fun d -> d.Id))
                  [ "makeMove" ]
                  "Only the unbound, non-semantic descriptor is reported"

              Expect.equal
                  (AlpsDocument.unboundTransitions profile [ endpoint, bound; endpoint, unbound ])
                  []
                  "Nothing is reported once every transition is bound"
          } ]
