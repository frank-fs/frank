module Frank.Alps.Tests.AuthorizationFilterTests

open System.Security.Claims
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Patterns
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Primitives
open Expecto
open Frank.Alps

let private noopDelegate: RequestDelegate = RequestDelegate(fun _ -> System.Threading.Tasks.Task.CompletedTask)

let private makeEndpoint (routePattern: string) (metadata: obj list) : Endpoint =
    RouteEndpoint(noopDelegate, Patterns.RoutePatternFactory.Parse routePattern, 0, EndpointMetadataCollection(metadata), routePattern)

let private makeDescriptor id = safe id

let private adminOnly =
    let builder = AuthorizationPolicyBuilder()
    builder.RequireRole "admin" |> ignore
    builder.Build()

let private superAdminOnly =
    let builder = AuthorizationPolicyBuilder()
    builder.RequireRole "superadmin" |> ignore
    builder.Build()

let private contextForWith (roles: string list) (configurePolicies: AuthorizationOptions -> unit) =
    let services = ServiceCollection()
    services.AddAuthorization(configurePolicies) |> ignore
    services.AddLogging() |> ignore

    let ctx = DefaultHttpContext()
    ctx.RequestServices <- services.BuildServiceProvider()

    let claims = roles |> List.map (fun r -> Claim(ClaimTypes.Role, r))
    ctx.User <- ClaimsPrincipal(ClaimsIdentity(claims, "Test"))
    ctx

let private contextFor (roles: string list) = contextForWith roles ignore

/// No AddAuthorization() -- IAuthorizationPolicyProvider and
/// IAuthorizationService are deliberately left unregistered, so resolving
/// either throws InvalidOperationException.
let private contextWithoutAuthorizationServices () =
    let services = ServiceCollection()
    services.AddLogging() |> ignore

    let ctx = DefaultHttpContext()
    ctx.RequestServices <- services.BuildServiceProvider()
    ctx

[<Tests>]
let tests =
    testList
        "AuthorizationFilter"
        [ testTask "an endpoint with no auth metadata is always allowed" {
              let ctx = contextFor []
              let endpoint = makeEndpoint "/public" []

              let! result = AuthorizationFilter.isAllowed ctx endpoint

              Expect.isTrue result "Endpoint with no auth metadata is always allowed"
          }

          testTask "AllowAnonymous metadata is always allowed regardless of auth state" {
              let ctx = contextFor []
              // Combine AllowAnonymous WITH restrictive metadata on the same endpoint:
              // this proves the AllowAnonymous short-circuit overrides an otherwise-failing policy.
              let endpoint = makeEndpoint "/anon" [ AllowAnonymousAttribute(); AuthorizeAttribute(); adminOnly ]

              let! result = AuthorizationFilter.isAllowed ctx endpoint

              Expect.isTrue result "AllowAnonymous overrides restrictive policy for anonymous principal"
          }

          testTask "IAuthorizeData present, principal satisfies it -> allowed" {
              let ctx = contextFor [ "admin" ]
              let endpoint = makeEndpoint "/admin" [ AuthorizeAttribute(); adminOnly ]

              let! result = AuthorizationFilter.isAllowed ctx endpoint

              Expect.isTrue result "Principal with admin role satisfies requirement"
          }

          testTask "IAuthorizeData present, principal does not satisfy it -> denied" {
              let ctx = contextFor []
              let endpoint = makeEndpoint "/admin" [ AuthorizeAttribute(); adminOnly ]

              let! result = AuthorizationFilter.isAllowed ctx endpoint

              Expect.isFalse result "Principal without admin role is denied"
          }

          testTask "an evaluation error (e.g. unresolvable policy) fails closed -- denied, not thrown" {
              // Fail closed: an app whose DI container can't resolve
              // IAuthorizationPolicyProvider/IAuthorizationService for a
              // guarded resource must deny it, not surface it or crash.
              let ctx = contextWithoutAuthorizationServices ()
              let endpoint = makeEndpoint "/admin" [ AuthorizeAttribute() ]

              let! result = AuthorizationFilter.isAllowed ctx endpoint

              Expect.isFalse result "Evaluation error is denied, not thrown"
          }

          testTask "filter keeps only the Descriptors whose endpoint is allowed, in order" {
              let ctx = contextFor [ "admin" ]
              let publicEndpoint = makeEndpoint "/public" []
              let adminEndpoint = makeEndpoint "/admin" [ AuthorizeAttribute(); adminOnly ]
              // restrictedEndpoint requires superadmin role, which the test principal lacks
              let restrictedEndpoint = makeEndpoint "/restricted" [ AuthorizeAttribute(); superAdminOnly ]

              let pairs =
                  [ publicEndpoint, makeDescriptor "public"
                    adminEndpoint, makeDescriptor "admin"
                    restrictedEndpoint, makeDescriptor "restricted" ]

              let! result = AuthorizationFilter.filter ctx pairs

              Expect.equal
                  (result |> List.map (fun d -> d.Id))
                  [ "public"; "admin" ]
                  "Allowed descriptors are kept, denied ones excluded, order preserved"
          }

          test "varies is true when any pair's endpoint carries auth metadata" {
              let publicEndpoint = makeEndpoint "/public" []
              let publicDescriptor = makeDescriptor "public"
              let adminEndpoint = makeEndpoint "/admin" [ AuthorizeAttribute() ]
              let adminDescriptor = makeDescriptor "admin"

              let allPublic = [ publicEndpoint, publicDescriptor ]
              let withGuarded = [ publicEndpoint, publicDescriptor; adminEndpoint, adminDescriptor ]

              Expect.isFalse (AuthorizationFilter.varies allPublic) "No auth metadata -> varies is false"
              Expect.isTrue (AuthorizationFilter.varies withGuarded) "Any auth metadata -> varies is true"
          }

          test "varies is false when no pair's endpoint carries auth metadata" {
              let endpoint1 = makeEndpoint "/public1" []
              let descriptor1 = makeDescriptor "public1"
              let endpoint2 = makeEndpoint "/public2" []
              let descriptor2 = makeDescriptor "public2"

              let pairs = [ endpoint1, descriptor1; endpoint2, descriptor2 ]

              Expect.isFalse (AuthorizationFilter.varies pairs) "No auth metadata in any endpoint -> varies is false"
          } ]
