# Frank.Auth

[![NuGet Version](https://img.shields.io/nuget/v/Frank.Auth)](https://www.nuget.org/packages/Frank.Auth/)

Resource- and handler-level authorization for [Frank](https://www.nuget.org/packages/Frank/) applications, integrating with ASP.NET Core's built-in authorization infrastructure.

## Installation

```bash
dotnet add package Frank.Auth
```

## Protecting Resources

Add authorization requirements directly to resource definitions:

```fsharp
open Frank.Builder
open Frank.Auth

// Require any authenticated user
let dashboard =
    resource "/dashboard" {
        name "Dashboard"
        requireAuth
        get (fun ctx -> ctx.Response.WriteAsync("Welcome to Dashboard"))
    }

// Require a specific claim
let adminPanel =
    resource "/admin" {
        name "Admin"
        requireClaim "role" "admin"
        get (fun ctx -> ctx.Response.WriteAsync("Admin Panel"))
    }

// Require a role
let engineering =
    resource "/engineering" {
        name "Engineering"
        requireRole "Engineering"
        get (fun ctx -> ctx.Response.WriteAsync("Engineering Portal"))
    }

// Reference a named policy
let reports =
    resource "/reports" {
        name "Reports"
        requirePolicy "CanViewReports"
        get (fun ctx -> ctx.Response.WriteAsync("Reports"))
    }

// Compose requirements (AND semantics — all must pass)
let sensitive =
    resource "/api/sensitive" {
        name "Sensitive"
        requireAuth
        requireClaim "scope" "admin"
        requireRole "Engineering"
        get (fun ctx -> ctx.Response.WriteAsync("Sensitive data"))
    }
```

## Protecting Individual Methods

Add authorization requirements to a single handler instead of the whole resource, using the `handler { }` computation expression — the resource stays public except for the method that opts in:

```fsharp
// GET is public, DELETE requires the admin role
let widgets =
    resource "/widgets" {
        name "Widgets"
        get listWidgets
        delete (handler {
            requireRole "admin"
            handle (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("deleted"))
        })
    }
```

Handler-level requirements compose with resource-level ones (AND semantics) — a resource marked `requireAuth` with a handler that adds `requireRole "admin"` needs both. To let one method opt back out entirely, use `allowAnonymous`:

```fsharp
// The resource requires authentication, but this one method stays public
let profile =
    resource "/profile" {
        name "Profile"
        requireAuth
        get (handler {
            allowAnonymous
            handle (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("public summary"))
        })
        put updateProfile
    }
```

`allowAnonymous` is a full bypass, not a downgrade — via ASP.NET Core's own `IAllowAnonymous` semantics, it skips every authorization requirement on that handler, resource-level and handler-level alike, even ones declared alongside it.

## Application Wiring

Configure authentication and authorization services using Frank's builder syntax:

```fsharp
[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        useAuthentication (fun auth ->
            // Configure your authentication scheme here
            auth)

        useAuthorization

        authorizationPolicy "CanViewReports" (fun policy ->
            policy.RequireClaim("scope", "reports:read") |> ignore)

        resource dashboard
        resource adminPanel
        resource reports
    }
    0
```

## Authorization Patterns

Every `require*` pattern below works identically at the resource level (`resource { requireRole ... }`) and the handler level (`handler { requireRole ... }`), and composes with AND semantics when used at both levels on the same endpoint.

| Pattern | Operation | Behavior |
|---------|-----------|----------|
| Authenticated user | `requireAuth` | 401 if unauthenticated, 200 if authenticated |
| Claim (single value) | `requireClaim "type" "value"` | 403 if claim missing or wrong value |
| Claim (multiple values) | `requireClaim "type" ["a"; "b"]` | 200 if user has any listed value (OR) |
| Role | `requireRole "Admin"` | 403 if user not in role |
| Named policy | `requirePolicy "PolicyName"` | Delegates to registered policy |
| Bypass (handler-level only) | `allowAnonymous` | Skips all authorization on that one handler — resource- and handler-level requirements alike |
| Multiple requirements | Stack multiple `require*` | AND semantics — all must pass |
| No requirements | (default) | Publicly accessible, zero overhead |

## Related Packages

Requires [`Frank`](https://www.nuget.org/packages/Frank/). Pairs well with [`Frank.JsonHome`](https://www.nuget.org/packages/Frank.JsonHome/), which reads the same authorization metadata to filter its discovery document per principal.

See the [project repository](https://github.com/frank-fs/frank) for the complete guide and sample applications.

## License

[MIT](https://github.com/frank-fs/frank/blob/master/LICENSE)
