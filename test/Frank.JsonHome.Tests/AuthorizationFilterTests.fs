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

let private describe rel (metadata: obj list) =
    { Rel = rel
      Href = "/" + rel
      IsTemplated = false
      HrefVars = []
      Methods = [ "GET" ]
      Formats = []
      Accepts = []
      Docs = None
      Status = None
      Metadata = metadata }

let private contextFor (roles: string list) =
    let services = ServiceCollection()
    services.AddAuthorization() |> ignore
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
          } ]
