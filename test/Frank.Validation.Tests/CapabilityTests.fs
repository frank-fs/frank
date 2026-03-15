module Frank.Validation.Tests.CapabilityTests

open System
open System.Security.Claims
open Expecto
open Frank.Validation

let private makeShape name =
    { ShaclShape.TargetType = None
      NodeShapeUri = Uri($"urn:test:shape:{name}")
      Properties = []
      Closed = false
      Description = Some name
      SparqlConstraints = [] }

let private baseShape = makeShape "base"
let private adminShape = makeShape "admin"
let private editorShape = makeShape "editor"
let private catchAllShape = makeShape "catchAll"

let private makePrincipal (claims: (string * string) list) =
    let identity =
        ClaimsIdentity(claims |> List.map (fun (t, v) -> Claim(t, v)), "TestAuth")

    ClaimsPrincipal(identity)

let private anonPrincipal = ClaimsPrincipal()

let private adminConfig =
    { ShapeResolverConfig.BaseShape = baseShape
      Overrides =
        [ { RequiredClaim = ("role", [ "admin" ])
            Shape = adminShape } ] }

[<Tests>]
let capabilityTests =
    testList
        "ShapeResolver"
        [ test "matching claim returns override shape" {
              let principal = makePrincipal [ ("role", "admin") ]
              let result = ShapeResolver.resolve adminConfig principal
              Expect.equal result.NodeShapeUri adminShape.NodeShapeUri "admin shape selected"
          }

          test "no matching claim returns base shape" {
              let principal = makePrincipal [ ("role", "user") ]
              let result = ShapeResolver.resolve adminConfig principal
              Expect.equal result.NodeShapeUri baseShape.NodeShapeUri "base shape selected"
          }

          test "empty overrides returns base shape" {
              let config =
                  { ShapeResolverConfig.BaseShape = baseShape
                    Overrides = [] }

              let principal = makePrincipal [ ("role", "admin") ]
              let result = ShapeResolver.resolve config principal
              Expect.equal result.NodeShapeUri baseShape.NodeShapeUri "base shape"
          }

          test "anonymous principal returns base shape" {
              let result = ShapeResolver.resolve adminConfig anonPrincipal
              Expect.equal result.NodeShapeUri baseShape.NodeShapeUri "base shape for anonymous"
          }

          test "first-match-wins with multiple overrides" {
              let config =
                  { ShapeResolverConfig.BaseShape = baseShape
                    Overrides =
                      [ { RequiredClaim = ("role", [ "admin" ])
                          Shape = adminShape }
                        { RequiredClaim = ("role", [ "editor" ])
                          Shape = editorShape } ] }

              let principal = makePrincipal [ ("role", "admin"); ("role", "editor") ]
              let result = ShapeResolver.resolve config principal
              Expect.equal result.NodeShapeUri adminShape.NodeShapeUri "admin wins (first match)"
          }

          test "multiple required values must all be present" {
              let config =
                  { ShapeResolverConfig.BaseShape = baseShape
                    Overrides =
                      [ { RequiredClaim = ("role", [ "admin"; "superuser" ])
                          Shape = adminShape } ] }

              let hasOne = makePrincipal [ ("role", "admin") ]
              let hasBoth = makePrincipal [ ("role", "admin"); ("role", "superuser") ]

              let result1 = ShapeResolver.resolve config hasOne
              let result2 = ShapeResolver.resolve config hasBoth

              Expect.equal result1.NodeShapeUri baseShape.NodeShapeUri "one value not enough"
              Expect.equal result2.NodeShapeUri adminShape.NodeShapeUri "both values match"
          }

          test "empty required values is catch-all" {
              let config =
                  { ShapeResolverConfig.BaseShape = baseShape
                    Overrides =
                      [ { RequiredClaim = ("role", [])
                          Shape = catchAllShape } ] }

              let principal = makePrincipal []
              let result = ShapeResolver.resolve config principal
              Expect.equal result.NodeShapeUri catchAllShape.NodeShapeUri "catch-all matches"
          }

          test "catch-all after specific override" {
              let config =
                  { ShapeResolverConfig.BaseShape = baseShape
                    Overrides =
                      [ { RequiredClaim = ("role", [ "admin" ])
                          Shape = adminShape }
                        { RequiredClaim = ("role", [])
                          Shape = catchAllShape } ] }

              let admin = makePrincipal [ ("role", "admin") ]
              let user = makePrincipal [ ("role", "user") ]

              Expect.equal (ShapeResolver.resolve config admin).NodeShapeUri adminShape.NodeShapeUri "admin first"

              Expect.equal
                  (ShapeResolver.resolve config user).NodeShapeUri
                  catchAllShape.NodeShapeUri
                  "catch-all for others"
          }

          test "principal with unrelated claims returns base" {
              let principal = makePrincipal [ ("email", "test@example.com") ]
              let result = ShapeResolver.resolve adminConfig principal
              Expect.equal result.NodeShapeUri baseShape.NodeShapeUri "unrelated claims → base"
          } ]
