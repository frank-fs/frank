module Frank.Auth.Tests.AuthorizationTests

open System
open System.Net
open System.Net.Http
open System.Security.Claims
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.Auth
open Frank.Tests.Shared.TestEndpointDataSource

/// Test authentication scheme name
let [<Literal>] TestScheme = "TestScheme"

/// Test authentication handler that authenticates based on X-Test-User header.
type TestAuthHandler(options, logger, encoder) =
    inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

    override this.HandleAuthenticateAsync() =
        let headerValue = this.Request.Headers["X-Test-User"].ToString()
        if String.IsNullOrEmpty headerValue then
            Task.FromResult(AuthenticateResult.NoResult())
        else
            let claims = ResizeArray<Claim>()
            claims.Add(Claim(ClaimTypes.Name, headerValue))

            let claimsHeader = this.Request.Headers["X-Test-Claims"].ToString()
            if not (String.IsNullOrEmpty claimsHeader) then
                for part in claimsHeader.Split(';') do
                    let kv = part.Split('=', 2)
                    if kv.Length = 2 then
                        claims.Add(Claim(kv[0], kv[1]))

            let rolesHeader = this.Request.Headers["X-Test-Roles"].ToString()
            if not (String.IsNullOrEmpty rolesHeader) then
                for role in rolesHeader.Split(';') do
                    claims.Add(Claim(ClaimTypes.Role, role))

            let identity = ClaimsIdentity(claims, TestScheme)
            let principal = ClaimsPrincipal(identity)
            let ticket = AuthenticationTicket(principal, TestScheme)
            Task.FromResult(AuthenticateResult.Success(ticket))

/// Creates a test server with Frank resource definitions.
let createAuthTestServer (resources: Resource list) (configureServices: IServiceCollection -> unit) =
    let allEndpoints = resources |> List.collect (fun r -> r.Endpoints |> Array.toList) |> List.toArray
    let builder =
        Host.CreateDefaultBuilder([||])
            .ConfigureWebHost(fun webBuilder ->
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(fun services ->
                        services.AddRouting() |> ignore
                        services.AddAuthentication(TestScheme)
                            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestScheme, fun _ -> ())
                            |> ignore
                        services.AddAuthorization() |> ignore
                        configureServices services)
                    .Configure(fun app ->
                        app
                            .UseRouting()
                            .UseAuthentication()
                            .UseAuthorization()
                            .UseEndpoints(fun endpoints ->
                                endpoints.DataSources.Add(TestEndpointDataSource(allEndpoints)))
                        |> ignore)
                |> ignore)

    let host = builder.Build()
    host.Start()
    host.GetTestClient()

/// Helper to send a request with optional test user and claims
let sendRequest (client: HttpClient) (method: HttpMethod) (path: string) (user: string option) (claims: string option) (roles: string option) =
    task {
        let request = new HttpRequestMessage(method, path)
        user |> Option.iter (fun u -> request.Headers.Add("X-Test-User", u))
        claims |> Option.iter (fun c -> request.Headers.Add("X-Test-Claims", c))
        roles |> Option.iter (fun r -> request.Headers.Add("X-Test-Roles", r))
        return! client.SendAsync(request)
    }

let simpleHandler : RequestDelegate =
    RequestDelegate(fun ctx -> ctx.Response.WriteAsync("OK"))

// ===== US5: Application-Level Auth Configuration =====

[<Tests>]
let us5Tests =
    testList "US5 - Application-Level Auth Configuration" [
        testTask "useAuthentication + useAuthorization wiring enables the authorization pipeline" {
            let protectedResource =
                resource "/protected" {
                    name "Protected"
                    requireAuth
                    get simpleHandler
                }
            let client = createAuthTestServer [ protectedResource ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/protected" None None None
            Expect.equal response.StatusCode HttpStatusCode.Unauthorized "Unauthenticated request to protected resource should return 401"
        }

        testTask "requireAuth returns 200 for authenticated requests when auth services are configured" {
            let protectedResource =
                resource "/protected" {
                    name "Protected"
                    requireAuth
                    get simpleHandler
                }
            let client = createAuthTestServer [ protectedResource ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/protected" (Some "testuser") None None
            Expect.equal response.StatusCode HttpStatusCode.OK "Authenticated request to protected resource should return 200"
        }
    ]

// ===== US1: Restrict Resource to Authenticated Users =====

[<Tests>]
let us1Tests =
    testList "US1 - Restrict Resource to Authenticated Users" [
        testTask "requireAuth + unauthenticated -> 401" {
            let r = resource "/secure" { name "Secure"; requireAuth; get simpleHandler }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/secure" None None None
            Expect.equal response.StatusCode HttpStatusCode.Unauthorized "Should return 401"
        }

        testTask "requireAuth + authenticated -> 200 with handler executed" {
            let handler = RequestDelegate(fun ctx -> ctx.Response.WriteAsync("handler-executed"))
            let r = resource "/secure" { name "Secure"; requireAuth; get handler }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/secure" (Some "alice") None None
            Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
            let! body = response.Content.ReadAsStringAsync()
            Expect.equal body "handler-executed" "Handler should have executed"
        }

        testTask "no auth operations -> publicly accessible regardless of auth status" {
            let r = resource "/public" { name "Public"; get simpleHandler }
            let client = createAuthTestServer [ r ] ignore

            let! (response1: HttpResponseMessage) = sendRequest client HttpMethod.Get "/public" None None None
            Expect.equal response1.StatusCode HttpStatusCode.OK "Unauthenticated should access public resource"

            let! (response2: HttpResponseMessage) = sendRequest client HttpMethod.Get "/public" (Some "alice") None None
            Expect.equal response2.StatusCode HttpStatusCode.OK "Authenticated should access public resource"
        }
    ]

// ===== US2: Restrict Resource by Claim =====

[<Tests>]
let us2Tests =
    testList "US2 - Restrict Resource by Claim" [
        testTask "single-value claim match -> 200" {
            let r = resource "/admin" { name "Admin"; requireClaim "scope" "admin"; get simpleHandler }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/admin" (Some "alice") (Some "scope=admin") None
            Expect.equal response.StatusCode HttpStatusCode.OK "User with matching claim should get 200"
        }

        testTask "single-value claim mismatch -> 403" {
            let r = resource "/admin" { name "Admin"; requireClaim "scope" "admin"; get simpleHandler }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/admin" (Some "alice") (Some "scope=read") None
            Expect.equal response.StatusCode HttpStatusCode.Forbidden "User without matching claim should get 403"
        }

        testTask "multi-value claim with any match -> 200" {
            let r = resource "/data" { name "Data"; requireClaim "scope" ["read"; "write"]; get simpleHandler }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/data" (Some "alice") (Some "scope=read") None
            Expect.equal response.StatusCode HttpStatusCode.OK "User with any matching claim value should get 200"
        }

        testTask "multi-value claim with no match -> 403" {
            let r = resource "/data" { name "Data"; requireClaim "scope" ["read"; "write"]; get simpleHandler }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/data" (Some "alice") (Some "scope=delete") None
            Expect.equal response.StatusCode HttpStatusCode.Forbidden "User without any matching claim value should get 403"
        }

        testTask "two separate claim requirements with only one satisfied -> 403" {
            let r = resource "/restricted" {
                name "Restricted"
                requireClaim "scope" "admin"
                requireClaim "department" "engineering"
                get simpleHandler
            }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/restricted" (Some "alice") (Some "scope=admin") None
            Expect.equal response.StatusCode HttpStatusCode.Forbidden "User satisfying only one of two requirements should get 403"
        }

        testTask "two separate claim requirements both satisfied -> 200" {
            let r = resource "/restricted" {
                name "Restricted"
                requireClaim "scope" "admin"
                requireClaim "department" "engineering"
                get simpleHandler
            }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/restricted" (Some "alice") (Some "scope=admin;department=engineering") None
            Expect.equal response.StatusCode HttpStatusCode.OK "User satisfying both requirements should get 200"
        }
    ]

// ===== US3: Restrict Resource by Role =====

[<Tests>]
let us3Tests =
    testList "US3 - Restrict Resource by Role" [
        testTask "user in role -> 200" {
            let r = resource "/admin-panel" { name "AdminPanel"; requireRole "Admin"; get simpleHandler }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/admin-panel" (Some "alice") None (Some "Admin")
            Expect.equal response.StatusCode HttpStatusCode.OK "User in required role should get 200"
        }

        testTask "user not in role -> 403" {
            let r = resource "/admin-panel" { name "AdminPanel"; requireRole "Admin"; get simpleHandler }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/admin-panel" (Some "bob") None (Some "User")
            Expect.equal response.StatusCode HttpStatusCode.Forbidden "User not in required role should get 403"
        }
    ]

// ===== US4: Restrict Resource by Named Policy =====

[<Tests>]
let us4Tests =
    testList "US4 - Restrict Resource by Named Policy" [
        testTask "user satisfying named policy -> 200" {
            let r = resource "/reports" { name "Reports"; requirePolicy "CanViewReports"; get simpleHandler }
            let client = createAuthTestServer [ r ] (fun services ->
                services.AddAuthorization(fun options ->
                    options.AddPolicy("CanViewReports", fun policy ->
                        policy.RequireClaim("scope", "reports:read") |> ignore)) |> ignore)
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/reports" (Some "alice") (Some "scope=reports:read") None
            Expect.equal response.StatusCode HttpStatusCode.OK "User satisfying policy should get 200"
        }

        testTask "user not satisfying named policy -> 403" {
            let r = resource "/reports" { name "Reports"; requirePolicy "CanViewReports"; get simpleHandler }
            let client = createAuthTestServer [ r ] (fun services ->
                services.AddAuthorization(fun options ->
                    options.AddPolicy("CanViewReports", fun policy ->
                        policy.RequireClaim("scope", "reports:read") |> ignore)) |> ignore)
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/reports" (Some "alice") (Some "scope=reports:write") None
            Expect.equal response.StatusCode HttpStatusCode.Forbidden "User not satisfying policy should get 403"
        }
    ]

// ===== US6: Compose Multiple Authorization Requirements =====

[<Tests>]
let us6Tests =
    testList "US6 - Compose Multiple Authorization Requirements" [
        testTask "requireAuth + requireClaim + requireRole, user satisfies all three -> 200" {
            let r = resource "/sensitive" {
                name "Sensitive"
                requireAuth
                requireClaim "scope" "admin"
                requireRole "Engineering"
                get simpleHandler
            }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/sensitive" (Some "alice") (Some "scope=admin") (Some "Engineering")
            Expect.equal response.StatusCode HttpStatusCode.OK "User satisfying all requirements should get 200"
        }

        testTask "requireAuth + requireClaim + requireRole, user satisfies only two -> 403" {
            let r = resource "/sensitive" {
                name "Sensitive"
                requireAuth
                requireClaim "scope" "admin"
                requireRole "Engineering"
                get simpleHandler
            }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/sensitive" (Some "alice") (Some "scope=admin") (Some "Marketing")
            Expect.equal response.StatusCode HttpStatusCode.Forbidden "User missing one requirement should get 403"
        }
    ]

// ===== Edge Cases =====

[<Tests>]
let edgeCaseTests =
    testList "Edge Cases" [
        testTask "unauthenticated user accessing a claim-required resource -> 401 (not 403)" {
            let r = resource "/claim-protected" { name "ClaimProtected"; requireClaim "scope" "admin"; get simpleHandler }
            let client = createAuthTestServer [ r ] ignore
            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/claim-protected" None None None
            Expect.equal response.StatusCode HttpStatusCode.Unauthorized "Unauthenticated user should get 401 not 403"
        }

        testTask "empty claim values list -> requires claim type exists with any value" {
            let r = resource "/any-scope" { name "AnyScope"; requireClaim "scope" []; get simpleHandler }
            let client = createAuthTestServer [ r ] ignore

            let! (response1: HttpResponseMessage) = sendRequest client HttpMethod.Get "/any-scope" (Some "alice") (Some "scope=anything") None
            Expect.equal response1.StatusCode HttpStatusCode.OK "User with claim type present (any value) should get 200"

            let! (response2: HttpResponseMessage) = sendRequest client HttpMethod.Get "/any-scope" (Some "alice") (Some "otherclaim=value") None
            Expect.equal response2.StatusCode HttpStatusCode.Forbidden "User without claim type should get 403"
        }

        testTask "multiple claim operations with same claim type but different values -> AND semantics" {
            let r = resource "/multi-scope" {
                name "MultiScope"
                requireClaim "scope" "read"
                requireClaim "scope" "write"
                get simpleHandler
            }
            let client = createAuthTestServer [ r ] ignore

            let! (response1: HttpResponseMessage) = sendRequest client HttpMethod.Get "/multi-scope" (Some "alice") (Some "scope=read") None
            Expect.equal response1.StatusCode HttpStatusCode.Forbidden "User with only one scope value should get 403"

            let! (response2: HttpResponseMessage) = sendRequest client HttpMethod.Get "/multi-scope" (Some "alice") (Some "scope=read;scope=write") None
            Expect.equal response2.StatusCode HttpStatusCode.OK "User with both scope values should get 200"
        }
    ]
