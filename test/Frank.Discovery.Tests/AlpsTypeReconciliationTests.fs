module Frank.Discovery.Tests.AlpsTypeReconciliationTests

open Expecto
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Metadata
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Frank.Discovery
open Frank.Tests.Shared.TestEndpointDataSource

// ── #397 AC1: alpsTypeForMethods (pure) ───────────────────────────────────────

[<Tests>]
let alpsTypeForMethodsTests =
    testList
        "DiscoveryMiddleware — #397 alpsTypeForMethods"
        [ test "{GET} -> safe" {
              Expect.equal (DiscoveryMiddleware.alpsTypeForMethods (Set.ofList [ "GET" ])) (Some "safe") "GET is safe"
          }

          test "{GET; POST} -> safe (GET present wins regardless of what else is registered)" {
              Expect.equal
                  (DiscoveryMiddleware.alpsTypeForMethods (Set.ofList [ "GET"; "POST" ]))
                  (Some "safe")
                  "GET present is safe even alongside POST"
          }

          test "{PUT} -> idempotent" {
              Expect.equal
                  (DiscoveryMiddleware.alpsTypeForMethods (Set.ofList [ "PUT" ]))
                  (Some "idempotent")
                  "PUT is idempotent"
          }

          test "{DELETE} -> idempotent" {
              Expect.equal
                  (DiscoveryMiddleware.alpsTypeForMethods (Set.ofList [ "DELETE" ]))
                  (Some "idempotent")
                  "DELETE is idempotent"
          }

          test "{POST} -> unsafe" {
              Expect.equal
                  (DiscoveryMiddleware.alpsTypeForMethods (Set.ofList [ "POST" ]))
                  (Some "unsafe")
                  "POST is unsafe"
          }

          test "{} (no live endpoint) -> None (no override)" {
              Expect.equal (DiscoveryMiddleware.alpsTypeForMethods Set.empty) None "empty set is unresolvable"
          }

          test "{PUT; POST} (ambiguous multi-write, no GET) -> None (no override, never guessed)" {
              Expect.equal
                  (DiscoveryMiddleware.alpsTypeForMethods (Set.ofList [ "PUT"; "POST" ]))
                  None
                  "ambiguous multi-write combination is not guessed"
          }

          test "{PATCH} (outside ALPS safe/idempotent/unsafe taxonomy) -> None" {
              Expect.equal
                  (DiscoveryMiddleware.alpsTypeForMethods (Set.ofList [ "PATCH" ]))
                  None
                  "PATCH is outside the taxonomy — not guessed"
          } ]

// ── #397 AC1: reconcileAlpsTypes (pure) ───────────────────────────────────────

let private mkDescriptor id classIri requestType type_ =
    { Id = id
      Type = type_
      Doc = None
      Href = Some "https://example.org/x"
      Descriptors = []
      Rt = None
      ClassIri = classIri
      RequestClrTypeName = requestType }

[<Tests>]
let reconcileAlpsTypesTests =
    testList
        "DiscoveryMiddleware — #397 reconcileAlpsTypes"
        [ test "RequestClrTypeName match takes precedence over ClassIri match" {
              // Deliberately conflicting maps: relation says idempotent, request-type says unsafe.
              // The precise per-verb signal (request type) must win.
              let methodsByRel = Map.ofList [ "https://schema.org/Game", Set.ofList [ "PUT" ] ]
              let methodsByType = Map.ofList [ "App.Move", Set.ofList [ "POST" ] ]

              let d =
                  mkDescriptor "Move" (Some "https://schema.org/Game") (Some "App.Move") "semantic"

              let result = DiscoveryMiddleware.reconcileAlpsTypes methodsByRel methodsByType [ d ]

              Expect.equal
                  result.[0].Type
                  "unsafe"
                  "RequestClrTypeName match (unsafe) wins over ClassIri match (idempotent)"
          }

          test "falls back to ClassIri match when RequestClrTypeName has no live match" {
              let methodsByRel = Map.ofList [ "https://schema.org/Game", Set.ofList [ "GET" ] ]
              let methodsByType = Map.empty

              let d =
                  mkDescriptor "Game" (Some "https://schema.org/Game") (Some "App.Game") "unsafe"

              let result = DiscoveryMiddleware.reconcileAlpsTypes methodsByRel methodsByType [ d ]

              Expect.equal
                  result.[0].Type
                  "safe"
                  "falls back to relation-based safe, overriding the wrong codegen default"
          }

          test "neither signal resolvable -> codegen default Type is untouched" {
              let d = mkDescriptor "Outcome" (Some "https://schema.org/Outcome") None "semantic"

              let result = DiscoveryMiddleware.reconcileAlpsTypes Map.empty Map.empty [ d ]
              Expect.equal result.[0].Type "semantic" "no live match — codegen default survives"
          }

          test "nested child descriptors are reconciled recursively" {
              let child = mkDescriptor "field" None None "semantic"

              let parent =
                  { mkDescriptor "Game" (Some "https://schema.org/Game") None "unsafe" with
                      Descriptors = [ child ] }

              let methodsByRel = Map.ofList [ "https://schema.org/Game", Set.ofList [ "GET" ] ]

              let result =
                  DiscoveryMiddleware.reconcileAlpsTypes methodsByRel Map.empty [ parent ]

              Expect.equal result.[0].Type "safe" "parent reconciled"

              Expect.equal
                  result.[0].Descriptors.[0].Type
                  "semantic"
                  "child (no ClassIri/RequestClrTypeName) keeps its own default"
          }

          test "reconciliation preserves all other fields (Id, Href, Rt, Descriptors structure)" {
              let d =
                  { mkDescriptor "Game" (Some "https://schema.org/Game") None "unsafe" with
                      Rt = Some "https://schema.org/Other" }

              let methodsByRel = Map.ofList [ "https://schema.org/Game", Set.ofList [ "GET" ] ]

              let result =
                  DiscoveryMiddleware.reconcileAlpsTypes methodsByRel Map.empty [ d ] |> List.head

              Expect.equal result.Id "Game" "Id unchanged"
              Expect.equal result.Href (Some "https://example.org/x") "Href unchanged"
              Expect.equal result.Rt (Some "https://schema.org/Other") "Rt unchanged"
          } ]

// ── #397 AC1: methodsByRelation / methodsByRequestType (pure, over a real EndpointDataSource) ──

let private routeEndpoint (pattern: string) (methods: string[]) (metadata: obj list) : RouteEndpoint =
    let builder = RoutePatternFactory.Parse pattern

    let metadataCollection =
        EndpointMetadataCollection((box (HttpMethodMetadata(methods))) :: metadata)

    RouteEndpoint(
        RequestDelegate(fun _ -> System.Threading.Tasks.Task.CompletedTask),
        builder,
        0,
        metadataCollection,
        null
    )

[<Tests>]
let methodsByRelationTests =
    testList
        "DiscoveryMiddleware — #397 methodsByRelation / methodsByRequestType (real EndpointDataSource)"
        [ test "methodsByRelation groups methods by relation IRI across multiple endpoints" {
              let ep1 =
                  routeEndpoint
                      "/games/{id}"
                      [| "GET" |]
                      [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ]

              let ep2 =
                  routeEndpoint
                      "/games/{id}"
                      [| "POST" |]
                      [ box ({ Relation = "https://schema.org/Game" }: ResourceRelationMetadata) ]

              let ds = TestEndpointDataSource([| ep1; ep2 |])
              let result = DiscoveryMiddleware.methodsByRelation ds

              Expect.equal
                  (Map.find "https://schema.org/Game" result)
                  (Set.ofList [ "GET"; "POST" ])
                  "methods unioned across endpoints sharing a relation"
          }

          test "methodsByRelation: endpoint without ResourceRelationMetadata is excluded" {
              let ep = routeEndpoint "/plain" [| "GET" |] []
              let ds = TestEndpointDataSource([| ep |])
              let result = DiscoveryMiddleware.methodsByRelation ds
              Expect.isEmpty result "no relation metadata -> no entries"
          }

          test "methodsByRequestType keys by the accepted request CLR type full name" {
              let ep =
                  routeEndpoint
                      "/games/{id}"
                      [| "POST" |]
                      [ box (AcceptsMetadata([| "application/json" |], typeof<string>, false) :> obj) ]

              let ds = TestEndpointDataSource([| ep |])
              let result = DiscoveryMiddleware.methodsByRequestType ds

              Expect.equal
                  (Map.find typeof<string>.FullName result)
                  (Set.ofList [ "POST" ])
                  "keyed by RequestType.FullName"
          }

          test "methodsByRequestType: endpoint without IAcceptsMetadata is excluded" {
              let ep = routeEndpoint "/plain" [| "GET" |] []
              let ds = TestEndpointDataSource([| ep |])
              let result = DiscoveryMiddleware.methodsByRequestType ds
              Expect.isEmpty result "no accepts metadata -> no entries"
          } ]
