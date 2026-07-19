module Frank.Discovery.Tests.AlpsTypeReconciliationTests

open Expecto
open Microsoft.AspNetCore.Http.Metadata
open Frank.Discovery
open Frank.Discovery.Tests.TestHelpers
open Frank.Tests.Shared.TestEndpointDataSource

/// #400 AC2 (adversarial-review finding): MoveRequestFixture (TestHelpers.fs) is a
/// module-nested fixture type. F# compiles a type declared textually inside a `module`
/// as a NESTED CLR type of that module's compiled class, so CLR reflection separates it
/// with '+' (e.g. "...TestHelpers+MoveRequestFixture"). DiscoveryEmitter's real codegen
/// output (FCS's FSharpEntity.TryFullName, Frank.Cli.Core/Extractor.fs) bakes the SAME
/// logical name with '.' throughout instead ("...TestHelpers.MoveRequestFixture") — F#
/// source syntax never distinguishes module-nesting from namespace-nesting. Constructed
/// here as a literal string, NOT via `typeof<MoveRequestFixture>.FullName.Replace('+', '.')`
/// or any other transform of the SAME reflection call used to build the live
/// AcceptsMetadata below — otherwise both sides would agree by construction and this
/// test would prove nothing (the exact masking that hid the original defect).
let private moveRequestFixtureFcsStyleName =
    "Frank.Discovery.Tests.TestHelpers.MoveRequestFixture"

/// #400 /simplify Fix 2 (maintainer-requested correctness hardening): a closed generic
/// fixture type. CLR reflection appends a backtick-arity marker AND, for a closed
/// generic, bracketed assembly-qualified type arguments (e.g.
/// "...GenericRequestFixture`1[[System.Int32, ...]]"), while FCS's TryFullName for the
/// same (open, unapplied) generic type definition keeps the backtick-arity marker but
/// never the bracketed type arguments ("...GenericRequestFixture`1") — confirmed via a
/// `dotnet fsi` probe against FSharpChecker.ParseAndCheckProject (see Frank.ClrTypeName
/// module doc), not assumed. Constructed here as a literal string for the same
/// masking-proof reason as moveRequestFixtureFcsStyleName above.
type private GenericRequestFixture<'T> = { Payload: 'T }

let private genericRequestFixtureFcsStyleName =
    "Frank.Discovery.Tests.AlpsTypeReconciliationTests.GenericRequestFixture`1"

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

// ── #400 AC1: methodsByRelation / methodsByRequestType (over IApiDescriptionGroupCollectionProvider) ──
// #397's original version of these tests drove EndpointDataSource directly; #400 sources
// correlation from IApiDescriptionGroupCollectionProvider instead (the shared provider
// Microsoft.AspNetCore.OpenApi's own document generation also reads — see
// DiscoveryMiddleware.fs's module-level rationale comment). routeEndpoint (TestHelpers.fs)
// here must stamp a MethodInfo (mirroring Frank's real ResourceSpec.Build, which adds
// `handler.Method`) — EndpointMetadataApiDescriptionProvider silently skips any endpoint
// lacking one.

[<Tests>]
let methodsByRelationTests =
    testList
        "DiscoveryMiddleware — #400 methodsByRelation / methodsByRequestType (IApiDescriptionGroupCollectionProvider)"
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
              let provider = apiDescriptionProviderFor ds
              let result = DiscoveryMiddleware.methodsByRelation provider

              Expect.equal
                  (Map.find "https://schema.org/Game" result)
                  (Set.ofList [ "GET"; "POST" ])
                  "methods unioned across endpoints sharing a relation"
          }

          test "methodsByRelation: endpoint without ResourceRelationMetadata is excluded" {
              let ep = routeEndpoint "/plain" [| "GET" |] []
              let ds = TestEndpointDataSource([| ep |])
              let provider = apiDescriptionProviderFor ds
              let result = DiscoveryMiddleware.methodsByRelation provider
              Expect.isEmpty result "no relation metadata -> no entries"
          }

          test "methodsByRequestType keys by the accepted request CLR type full name" {
              let ep =
                  routeEndpoint
                      "/games/{id}"
                      [| "POST" |]
                      [ box (AcceptsMetadata([| "application/json" |], typeof<string>, false) :> obj) ]

              let ds = TestEndpointDataSource([| ep |])
              let provider = apiDescriptionProviderFor ds
              let result = DiscoveryMiddleware.methodsByRequestType provider

              Expect.equal
                  (Map.find typeof<string>.FullName result)
                  (Set.ofList [ "POST" ])
                  "keyed by RequestType.FullName"
          }

          test "methodsByRequestType: endpoint without IAcceptsMetadata is excluded" {
              let ep = routeEndpoint "/plain" [| "GET" |] []
              let ds = TestEndpointDataSource([| ep |])
              let provider = apiDescriptionProviderFor ds
              let result = DiscoveryMiddleware.methodsByRequestType provider
              Expect.isEmpty result "no accepts metadata -> no entries"
          }

          test
              "methodsByRequestType normalizes a module-nested CLR type's '+' to '.' to match codegen's FCS-derived RequestClrTypeName convention (#400 AC2 adversarial-review finding)" {
              // Sanity checks first: prove this fixture actually exercises the real
              // mismatch (a vacuous fixture — e.g. a non-nested type, or one where CLR
              // and FCS conventions happen to already agree — would let this test pass
              // for the wrong reason, the exact "same-derivation-both-sides" masking
              // that let the original bug hide behind #397's/#400's earlier tests).
              Expect.stringContains
                  typeof<MoveRequestFixture>.FullName
                  "+"
                  "sanity: a type declared inside a module is CLR-reflected as nested ('+') — proves this fixture is genuinely module-nested, not a vacuous top-level type"

              Expect.isFalse
                  (typeof<MoveRequestFixture>.FullName = moveRequestFixtureFcsStyleName)
                  "sanity: the raw CLR FullName must NOT already equal the FCS-style dotted form — otherwise normalization couldn't be distinguished from a no-op"

              let ep =
                  routeEndpoint
                      "/games/{id}"
                      [| "POST" |]
                      [ box (AcceptsMetadata([| "application/json" |], typeof<MoveRequestFixture>, false) :> obj) ]

              let ds = TestEndpointDataSource([| ep |])
              let provider = apiDescriptionProviderFor ds
              let result = DiscoveryMiddleware.methodsByRequestType provider

              Expect.equal
                  (Map.find moveRequestFixtureFcsStyleName result)
                  (Set.ofList [ "POST" ])
                  "methodsByRequestType's map key must be the FCS-style dotted form (matching real codegen's RequestClrTypeName) — not the raw CLR '+'-nested reflection value — or reconciliation silently no-ops for every module-nested request type in production"
          }

          test
              "methodsByRequestType normalizes a closed generic CLR type's backtick-arity + bracketed type-args to the FCS-equivalent form (#400 /simplify Fix 2)" {
              // Sanity checks first, same rigor as the module-nested case above: prove
              // this fixture genuinely exercises BOTH the '+' nesting AND the
              // backtick-arity/bracketed-args mismatch, not a vacuous generic that
              // happens to already agree with the FCS-style form.
              let rawFullName = typeof<GenericRequestFixture<int>>.FullName

              Expect.stringContains rawFullName "+" "sanity: nested inside this file's module — CLR-reflected with '+'"

              Expect.stringContains rawFullName "`1" "sanity: generic backtick-arity marker present"

              Expect.stringContains
                  rawFullName
                  "[["
                  "sanity: bracketed, assembly-qualified type arguments present (closed generic)"

              Expect.isFalse
                  (rawFullName = genericRequestFixtureFcsStyleName)
                  "sanity: the raw CLR FullName must NOT already equal the FCS-style form — otherwise normalization couldn't be distinguished from a no-op"

              let ep =
                  routeEndpoint
                      "/games/{id}"
                      [| "POST" |]
                      [ box (
                            AcceptsMetadata([| "application/json" |], typeof<GenericRequestFixture<int>>, false) :> obj
                        ) ]

              let ds = TestEndpointDataSource([| ep |])
              let provider = apiDescriptionProviderFor ds
              let result = DiscoveryMiddleware.methodsByRequestType provider

              Expect.equal
                  (Map.find genericRequestFixtureFcsStyleName result)
                  (Set.ofList [ "POST" ])
                  "methodsByRequestType's map key must strip the closed generic's bracketed type-args (keeping the backtick-arity marker) and normalize '+' to '.' — matching codegen's FCS-derived RequestClrTypeName for a generic request type"
          } ]
