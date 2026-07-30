module Frank.JsonHome.Tests.AuthorizationFilterTests

open System.Security.Claims
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Expecto
open Frank.JsonHome

let private adminOnly =
    let builder = AuthorizationPolicyBuilder()
    builder.RequireRole "admin" |> ignore
    builder.Build()

let private describeMethods rel (methodMetadata: (string * obj list) list) =
    { Rel = rel
      Href = "/" + rel
      IsTemplated = false
      HrefVars = []
      Methods = methodMetadata |> List.map fst
      Formats = []
      Accepts = []
      AcceptRanges = []
      AcceptPrefer = []
      PreconditionRequired = []
      AuthSchemes = []
      Docs = None
      Status = None
      Metadata = methodMetadata |> List.collect snd
      MethodMetadata = methodMetadata }

let private describe rel (metadata: obj list) =
    describeMethods rel [ "GET", metadata ]

let private contextFor (roles: string list) =
    let services = ServiceCollection()
    services.AddAuthorization() |> ignore
    services.AddLogging() |> ignore

    let ctx = DefaultHttpContext()
    ctx.RequestServices <- services.BuildServiceProvider()

    let claims = roles |> List.map (fun r -> Claim(ClaimTypes.Role, r))
    ctx.User <- ClaimsPrincipal(ClaimsIdentity(claims, "Test"))
    ctx

/// No AddAuthorization() -- IAuthorizationPolicyProvider and
/// IAuthorizationService are deliberately left unregistered, so resolving
/// either throws InvalidOperationException.
let private contextWithoutAuthorizationServices () =
    let services = ServiceCollection()
    services.AddLogging() |> ignore

    let ctx = DefaultHttpContext()
    ctx.RequestServices <- services.BuildServiceProvider()
    ctx

let private contextForWithPolicy (roles: string list) (configurePolicies: AuthorizationOptions -> unit) =
    let services = ServiceCollection()
    services.AddAuthorization(configurePolicies) |> ignore
    services.AddLogging() |> ignore

    let ctx = DefaultHttpContext()
    ctx.RequestServices <- services.BuildServiceProvider()

    let claims = roles |> List.map (fun r -> Claim(ClaimTypes.Role, r))
    ctx.User <- ClaimsPrincipal(ClaimsIdentity(claims, "Test"))
    ctx

[<Tests>]
let tests =
    testList
        "AuthorizationFilter"
        [ testTask "resources without authorization metadata are always included" {
              let ctx = contextFor []
              let! result = AuthorizationFilter.apply ctx [ describe "public" [] ]

              Expect.hasLength result 1 "Public resource is included"
          }

          testTask "resources the principal cannot reach are omitted" {
              let ctx = contextFor []
              let guarded = describe "admin" [ AuthorizeAttribute(); adminOnly ]
              let! result = AuthorizationFilter.apply ctx [ describe "public" []; guarded ]

              Expect.equal (result |> List.map (fun (r: ResourceDescription) -> r.Rel)) [ "public" ] "Guarded resource omitted"
          }

          testTask "resources the principal can reach are included" {
              let ctx = contextFor [ "admin" ]
              let guarded = describe "admin" [ AuthorizeAttribute(); adminOnly ]
              let! result = AuthorizationFilter.apply ctx [ describe "public" []; guarded ]

              Expect.equal (result |> List.map (fun (r: ResourceDescription) -> r.Rel)) [ "public"; "admin" ] "Both included"
          }

          testTask "evaluation failures deny rather than throw or fail open" {
              // Fail closed: an app whose DI container can't resolve
              // IAuthorizationPolicyProvider/IAuthorizationService for a
              // guarded resource must exclude it, not surface it or crash.
              let ctx = contextWithoutAuthorizationServices ()
              let guarded = describe "admin" [ AuthorizeAttribute() ]
              let! result = AuthorizationFilter.apply ctx [ describe "public" []; guarded ]

              Expect.equal
                  (result |> List.map (fun (r: ResourceDescription) -> r.Rel))
                  [ "public" ]
                  "Guarded resource denied on evaluation failure"
          }

          testTask "methods are filtered independently -- a mixed resource keeps only what the principal can reach" {
              let ctx = contextFor []
              let mixed = describeMethods "widgets" [ "GET", []; "DELETE", [ AuthorizeAttribute(); adminOnly ] ]

              let! (result: ResourceDescription list) = AuthorizationFilter.apply ctx [ mixed ]

              Expect.hasLength result 1 "Resource still present"
              Expect.equal result.[0].Methods [ "GET" ] "Only the public method survives"
          }

          testTask "a resource with every method hidden is dropped entirely" {
              let ctx = contextFor []
              let guarded = describeMethods "admin-only" [ "GET", [ AuthorizeAttribute(); adminOnly ] ]

              let! result = AuthorizationFilter.apply ctx [ guarded ]

              Expect.isEmpty result "Resource with zero visible methods does not appear"
          }

          testTask "AllowAnonymous on one method keeps it visible even when the resource is otherwise restricted" {
              let ctx = contextFor []
              let mixed =
                  describeMethods
                      "settings"
                      [ "GET", [ AllowAnonymousAttribute() ]
                        "PUT", [ AuthorizeAttribute(); adminOnly ] ]

              let! (result: ResourceDescription list) = AuthorizationFilter.apply ctx [ mixed ]

              Expect.hasLength result 1 "Resource still present"
              Expect.equal result.[0].Methods [ "GET" ] "AllowAnonymous method survives, restricted one doesn't"
          }

          testTask "Accepts entries are filtered to the methods that remain visible" {
              let ctx = contextFor []
              let mixed =
                  { describeMethods "orders" [ "GET", []; "POST", [ AuthorizeAttribute(); adminOnly ] ] with
                      Accepts = [ "GET", [ "text/html" ]; "POST", [ "application/json" ] ] }

              let! (result: ResourceDescription list) = AuthorizationFilter.apply ctx [ mixed ]

              Expect.equal result.[0].Methods [ "GET" ] "Only GET remains"
              Expect.equal result.[0].Accepts [ "GET", [ "text/html" ] ] "POST's accept entry is dropped with it"
          }

          testTask "Formats is cleared once GET is no longer visible" {
              let ctx = contextFor []
              let mixed =
                  { describeMethods "orders" [ "GET", [ AuthorizeAttribute(); adminOnly ]; "POST", [] ] with
                      Formats = [ "application/json" ] }

              let! (result: ResourceDescription list) = AuthorizationFilter.apply ctx [ mixed ]

              Expect.equal result.[0].Methods [ "POST" ] "Only POST remains"
              Expect.isEmpty result.[0].Formats "Formats was derived from the now-hidden GET"
          }

          testTask "a named policy composed with an explicit AuthorizationPolicy -- satisfying only the explicit one still denies" {
              // Regression: resolvePolicy used to discard all IAuthorizeData
              // (including named-policy AuthorizeAttributes) whenever an
              // explicit AuthorizationPolicy was also present in the same
              // method's metadata -- exactly the shape requirePolicy
              // (resource-level) + requireRole (handler-level) produces
              // together. An admin who fails the named policy's own claim
              // requirement must still be denied, not waved through on role
              // alone.
              let ctx =
                  contextForWithPolicy
                      [ "admin" ]
                      (fun options -> options.AddPolicy("CanViewReports", fun p -> p.RequireClaim("scope", "reports:read") |> ignore))

              let guarded =
                  describeMethods "reports" [ "DELETE", [ AuthorizeAttribute("CanViewReports"); AuthorizeAttribute(); adminOnly ] ]

              let! result = AuthorizationFilter.apply ctx [ guarded ]

              Expect.isEmpty result "Admin who lacks the named policy's claim must still be denied, not allowed on role alone"
          } ]
