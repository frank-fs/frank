# Quickstart: Frank.Auth

**Feature**: 013-frank-auth
**Date**: 2026-02-05

## Prerequisites

- .NET 8.0+ SDK
- Frank 6.5.0+ (with `Metadata` extensibility point on `ResourceSpec`)

## 1. Add Frank.Auth Reference

Add a project reference (or NuGet reference once published):

```xml
<ProjectReference Include="../../src/Frank.Auth/Frank.Auth.fsproj" />
```

## 2. Open the Namespaces

```fsharp
open Frank.Builder
open Frank.Auth
```

The `[<AutoOpen>]` modules in Frank.Auth automatically bring the `ResourceBuilder` and `WebHostBuilder` extensions into scope.

## 3. Protect a Resource

Add `requireAuth` to any resource definition:

```fsharp
let dashboard =
    resource "/dashboard" {
        name "Dashboard"
        requireAuth
        get (fun ctx -> ctx.Response.WriteAsync("Welcome to Dashboard"))
    }
```

## 4. Add Claim-Based Authorization

```fsharp
let adminPanel =
    resource "/admin" {
        name "Admin"
        requireClaim "role" "admin"
        get (fun ctx -> ctx.Response.WriteAsync("Admin Panel"))
    }
```

Multiple values (OR — user needs at least one):

```fsharp
let apiData =
    resource "/api/data" {
        name "API Data"
        requireClaim "scope" ["read"; "write"]
        get (fun ctx -> ctx.Response.WriteAsync("Data"))
    }
```

## 5. Compose Requirements (AND)

```fsharp
let sensitive =
    resource "/api/sensitive" {
        name "Sensitive"
        requireAuth
        requireClaim "scope" "admin"
        requireRole "Engineering"
        get (fun ctx -> ctx.Response.WriteAsync("Sensitive data"))
    }
```

All three requirements must be satisfied.

## 6. Wire Up the Application

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
        resource apiData
        resource sensitive
    }
    0
```

## Key Behaviors

| Scenario | Result |
|----------|--------|
| No `require*` on resource | Publicly accessible, zero overhead |
| `requireAuth` + unauthenticated request | 401 Unauthorized |
| `requireAuth` + authenticated request | 200 OK (proceeds to handler) |
| `requireClaim` + missing claim | 403 Forbidden |
| `requireClaim` with values + has any value | 200 OK |
| Multiple `require*` + all satisfied | 200 OK |
| Multiple `require*` + any unsatisfied | 403 Forbidden |

## Running Tests

```bash
dotnet test test/Frank.Auth.Tests/
```
