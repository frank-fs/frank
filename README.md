# Frank

[![NuGet Version](https://img.shields.io/nuget/v/Frank)](https://www.nuget.org/packages/Frank/)
![GitHub Release Date](https://img.shields.io/github/release-date/frank-fs/frank)
![Build status](https://github.com/frank-fs/frank/workflows/CI/badge.svg)

[F#](https://fsharp.org/) [computation expressions](https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions), or builders, for configuring the [`Microsoft.AspNetCore.Hosting.IWebHostBuilder`](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhostbuilder?view=aspnetcore-2.0) and defining routes for HTTP resources using [`Microsoft.AspNetCore.Routing`](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-2.1).

This project was inspired by @filipw's [Building Microservices with ASP.NET Core (without MVC)](https://www.strathweb.com/2017/01/building-microservices-with-asp-net-core-without-mvc/).

---

## Packages

| Package | Description | NuGet |
|---------|-------------|-------|
| **Frank** | Core computation expressions for WebHost and routing | [![NuGet](https://img.shields.io/nuget/v/Frank)](https://www.nuget.org/packages/Frank/) |
| **Frank.Auth** | Resource- and handler-level authorization extensions | [![NuGet](https://img.shields.io/nuget/v/Frank.Auth)](https://www.nuget.org/packages/Frank.Auth/) |
| **Frank.OpenApi** | Native OpenAPI document generation with F# type schemas | [![NuGet](https://img.shields.io/nuget/v/Frank.OpenApi)](https://www.nuget.org/packages/Frank.OpenApi/) |
| **Frank.JsonHome** | JSON Home discovery document, filtered by authorization | [![NuGet](https://img.shields.io/nuget/v/Frank.JsonHome)](https://www.nuget.org/packages/Frank.JsonHome/) |
| **Frank.Datastar** | Datastar SSE integration for reactive hypermedia | [![NuGet](https://img.shields.io/nuget/v/Frank.Datastar)](https://www.nuget.org/packages/Frank.Datastar/) |
| **Frank.Analyzers** | F# Analyzers for compile-time error detection | [![NuGet](https://img.shields.io/nuget/v/Frank.Analyzers)](https://www.nuget.org/packages/Frank.Analyzers/) |

---

## Features

- `WebHostBuilder` - computation expression for configuring `WebHost`
- `ResourceBuilder` - computation expression for configuring resources (routing)
- **No** pre-defined view engine - use your preferred view engine implementation,
  e.g. [Falco.Markup](https://github.com/pimbrouwers/Falco.Markup),
  [Oxpecker.ViewEngine](https://lanayx.github.io/Oxpecker/src/Oxpecker.ViewEngine/),
  or [Hox](https://github.com/AngelMunoz/Hox)
- Easy extensibility - just extend the `Builder` with your own methods!

### Basic Example

```fsharp
module Program

open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Internal
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank
open Frank.Builder

let home =
    resource "/" {
        name "Home"

        get (fun (ctx:HttpContext) ->
            ctx.Response.WriteAsync("Welcome!"))
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        logging (fun options-> options.AddConsole().AddDebug())

        plugWhen isDevelopment DeveloperExceptionPageExtensions.UseDeveloperExceptionPage
        plugWhenNot isDevelopment HstsBuilderExtensions.UseHsts

        plugBeforeRouting HttpsPolicyBuilderExtensions.UseHttpsRedirection
        plugBeforeRouting StaticFileExtensions.UseStaticFiles

        resource home
    }

    0
```

---

## Middleware Pipeline

Frank provides two middleware operations with different positions in the ASP.NET Core pipeline:

```
Request → plugBeforeRouting → UseRouting → plug → Endpoints → Response
```

### `plugBeforeRouting`

Use for middleware that must run **before** routing decisions are made:

- **HttpsRedirection** - redirect before routing
- **StaticFiles** - serve static files without routing overhead
- **ResponseCompression** - compress all responses
- **ResponseCaching** - cache before routing

```fsharp
webHost args {
    plugBeforeRouting HttpsPolicyBuilderExtensions.UseHttpsRedirection
    plugBeforeRouting StaticFileExtensions.UseStaticFiles
    resource myResource
}
```

### `plug`

Use for middleware that needs routing information (e.g., the matched endpoint):

- **Authentication** - may need endpoint metadata
- **Authorization** - requires endpoint to check policies
- **CORS** - may use endpoint-specific policies

```fsharp
webHost args {
    plug AuthenticationBuilderExtensions.UseAuthentication
    plug AuthorizationAppBuilderExtensions.UseAuthorization
    resource protectedResource
}
```

### Conditional Middleware

Both `plugWhen` and `plugWhenNot` run in the `plug` position (after routing):

```fsharp
webHost args {
    plugWhen isDevelopment DeveloperExceptionPageExtensions.UseDeveloperExceptionPage
    plugWhenNot isDevelopment HstsBuilderExtensions.UseHsts
    resource myResource
}
```

### Conditional Before-Routing Middleware

Both `plugBeforeRoutingWhen` and `plugBeforeRoutingWhenNot` run in the `plugBeforeRouting` position (before routing):

```fsharp
let isDevelopment (app: IApplicationBuilder) =
    app.ApplicationServices
        .GetService<IWebHostEnvironment>()
        .IsDevelopment()

webHost args {
    // Only redirect to HTTPS in production
    plugBeforeRoutingWhenNot isDevelopment HttpsPolicyBuilderExtensions.UseHttpsRedirection

    // Only serve static files locally in development (CDN in production)
    plugBeforeRoutingWhen isDevelopment StaticFileExtensions.UseStaticFiles

    resource myResource
}
```

---

## Frank.Auth

Frank.Auth provides resource- and handler-level authorization for Frank applications, integrating with ASP.NET Core's built-in authorization infrastructure.

### Installation

```bash
dotnet add package Frank.Auth
```

### Protecting Resources

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

### Protecting Individual Methods

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

### Application Wiring

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

### Authorization Patterns

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

---

## Frank.OpenApi

Frank.OpenApi provides native OpenAPI document generation for Frank applications, with first-class support for F# types and declarative metadata using computation expressions.

### Installation

```bash
dotnet add package Frank.OpenApi
```

### HandlerBuilder Computation Expression

Define handlers with embedded OpenAPI metadata using the `handler` computation expression:

```fsharp
open Frank.Builder
open Frank.OpenApi

type Product = { Name: string; Price: decimal }
type CreateProductRequest = { Name: string; Price: decimal }

let createProductHandler =
    handler {
        name "createProduct"
        summary "Create a new product"
        description "Creates a new product in the catalog"
        tags [ "Products"; "Admin" ]
        produces typeof<Product> 201
        accepts typeof<CreateProductRequest>
        handle (fun (ctx: HttpContext) -> task {
            let! request = ctx.Request.ReadFromJsonAsync<CreateProductRequest>()
            let product = { Name = request.Name; Price = request.Price }
            ctx.Response.StatusCode <- 201
            do! ctx.Response.WriteAsJsonAsync(product)
        })
    }

let productsResource =
    resource "/products" {
        name "Products"
        post createProductHandler
    }
```

### HandlerBuilder Operations

| Operation | Description |
|-----------|-------------|
| `name "operationId"` | Sets the OpenAPI operationId |
| `summary "text"` | Brief summary of the operation |
| `description "text"` | Detailed description |
| `tags [ "Tag1"; "Tag2" ]` | Categorize endpoints |
| `produces typeof<T> statusCode` | Define response type and status code |
| `produces typeof<T> statusCode ["content/type"]` | Response with content negotiation |
| `producesEmpty statusCode` | Empty responses (204, 404, etc.) |
| `accepts typeof<T>` | Define request body type |
| `accepts typeof<T> ["content/type"]` | Request with content negotiation |
| `handle (fun ctx -> ...)` | Handler function (supports Task, Task<'a>, Async<unit>, Async<'a>) |

### F# Type Schema Generation

Frank.OpenApi automatically generates JSON schemas for F# types:

```fsharp
// F# records with required and optional fields
type User = {
    Id: Guid
    Name: string
    Email: string option  // Becomes nullable in schema
}

// Discriminated unions (anyOf/oneOf)
type Response =
    | Success of data: string
    | Error of code: int * message: string

// Collections
type Products = {
    Items: Product list
    Tags: Set<string>
    Metadata: Map<string, string>
}
```

### WebHostBuilder Integration

Enable OpenAPI document generation in your application:

```fsharp
[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        useOpenApi  // Adds /.well-known/openapi.json endpoint

        resource productsResource
    }
    0
```

The OpenAPI document will be available at `/.well-known/openapi.json`.

### Content Negotiation

Define multiple content types for requests and responses:

```fsharp
handler {
    name "getProduct"
    produces typeof<Product> 200 [ "application/json"; "application/xml" ]
    accepts typeof<ProductQuery> [ "application/json"; "application/xml" ]
    handle (fun ctx -> task { (* ... *) })
}
```

These operations only *describe* the content types in the generated OpenAPI document — they
don't dispatch on `Accept` at runtime. For that, use the `negotiate { }` CE below.

### The `negotiate { }` Computation Expression

`negotiate { }` lives in Frank core (`Frank.Builder`) — no Frank.OpenApi needed — and performs
real per-media-type dispatch: each `accepts` registers an independent representation, and the
one matching the request's `Accept` header (by RFC 9110 quality and specificity rules) is the
only one whose handler runs. Nothing matching means `406 Not Acceptable`, and every response
carries `Vary: Accept`.

```fsharp
resource "/products/{id}" {
    get (negotiate {
        accepts "application/json" (fun ctx -> task {
            do! ctx.Response.WriteAsJsonAsync(product)
        })
        accepts "text/html" (fun ctx -> task {
            do! ctx.Response.WriteAsync($"<h1>{product.Name}</h1>")
        })
    })
}
```

`accepts` also takes a `handler { }` definition (whose `produces` metadata flows into the
OpenAPI document), a media-type *list* to register one representation per type
(`accepts [ "application/json"; "application/xml" ] getProduct`), and a wildcard catch-all
(`accepts "*/*" ...` or `accepts "type/*" ...`). Register wildcards **last** — registration
order breaks ties, so a wildcard registered first shadows everything after it. A wildcard
representation must set its own `Content-Type`; Frank won't set an invalid wildcard one.

For representations that should reuse ASP.NET Core MVC's existing `IOutputFormatter` registry
(`AddMvcCore()`, `AddXmlSerializerFormatters()`, …) instead of a hand-written producer, use the
`Frank.ContentNegotiation.viaOutputFormatter` bridge — or just return a value from the handler
(`accepts "application/json" (fun ctx -> task { return product })`), which pipes through it
automatically.

> **Name collision:** `Frank.Builder.negotiate` (this CE) and the older
> `Frank.ContentNegotiation.negotiate` function (`statusCode -> body -> ctx -> Task`, which
> delegates the whole response to MVC's formatter registry) share the identifier `negotiate`.
> With both modules opened, F#'s ordinary shadowing rules apply and the last `open` wins.
> Qualify one of them, or use the non-colliding `ctx.Negotiate(200, body)` extension member.

### Backward Compatibility

Frank.OpenApi is fully backward compatible with existing Frank applications. You can:
- Mix `HandlerDefinition` and plain `RequestDelegate` handlers in the same resource
- Add OpenAPI metadata incrementally without changing existing code
- Use the library only where you need API documentation

---

## Frank.JsonHome

Frank.JsonHome serves a [JSON Home](https://datatracker.ietf.org/doc/html/draft-nottingham-json-home-06) document describing an application's entry-point resources — a machine-readable directory a client can discover once and use to find everything else, instead of hardcoding URLs. It has no dependency on `Frank.Auth` or `Frank.OpenApi`, and adds no NuGet dependency of its own.

### Installation

```bash
dotnet add package Frank.JsonHome
```

### Declaring Discoverable Resources

Resources opt in with `rel`; anything without one is omitted, so the document stays a curated entry point rather than a sitemap of everything the app happens to serve:

```fsharp
open Frank.Builder
open Frank.JsonHome

let products =
    resource "/products" {
        rel "tag:example.com,2026:products"
        docs "https://example.com/docs/products"
        get listProducts
        post createProduct
    }

let productById =
    resource "/products/{id}" {
        rel "tag:example.com,2026:product"
        hrefVar "id" "https://example.com/param/product-id"
        get getProduct
        put updateProduct
    }

// Signals a resource is on its way out, or already gone
let legacyExport =
    resource "/legacy/export" {
        rel "tag:example.com,2026:legacy-export"
        deprecated
        get legacyExportHandler
    }
```

### Application Wiring

```fsharp
[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        useJsonHome  // Serves /.well-known/home.json, advertises it via a Link header on every response

        resource products
        resource productById
        resource legacyExport
    }
    0
```

Configure the path, relation type, and `api` metadata with the overload that takes a function:

```fsharp
useJsonHome (fun options ->
    { options with
        Path = "/discovery.json"
        Rel = "discovery"
        Title = Some "Example API"
        Links = [ "author", "mailto:api-admin@example.com" ] })
```

### Discovery Operations

| Operation | Description |
|-----------|-------------|
| `rel "..."` | Link relation type keying this resource in the document — required for the resource to appear at all |
| `hrefVar "name" "uri"` | Absolute URI identifying a route variable's semantics, for templated resources |
| `docs "uri"` | Documentation link for this resource's relation type |
| `deprecated` | Marks `status: "deprecated"` |
| `gone` | Marks `status: "gone"` |
| `acceptRanges [ "bytes" ]` | HTTP range-specifiers this resource accepts |
| `acceptPrefer [ "return=minimal" ]` | RFC 7240 preferences this resource supports |
| `preconditionRequired [ Precondition.ETag ]` | Preconditions required on state-changing requests |
| `authScheme "Basic" [ "private" ]` | An HTTP authentication scheme this resource accepts, with its protection spaces |

### Authorization Filtering

If [`Frank.Auth`](#frankauth) is in use, guard a resource the same way you would anywhere else — `Frank.JsonHome` reads the stock `IAuthorizeData`/`AuthorizationPolicy` metadata `Frank.Auth` attaches, with no reference between the two packages:

```fsharp
let adminReports =
    resource "/admin/reports" {
        rel "tag:example.com,2026:admin-reports"
        requireRole "admin"  // Frank.Auth
        get getAdminReports
    }
```

An anonymous request's `/.well-known/home.json` omits `tag:example.com,2026:admin-reports` entirely; an authenticated admin's includes it. This also works with a plain ASP.NET Core `[<Authorize>]`-equivalent, without `Frank.Auth` at all.

Filtering is per HTTP method, not per whole resource — a handler-level requirement (see [Protecting Individual Methods](#frankauth)) only hides the method it's on, not the resource:

```fsharp
let widgets =
    resource "/widgets" {
        rel "tag:example.com,2026:widgets"
        get listWidgets                                    // public
        delete (handler { requireRole "admin"; handle deleteWidget })
    }
```

An anonymous request's `hints.allow` for `tag:example.com,2026:widgets` shows `["GET"]`; an authenticated admin's shows `["GET", "DELETE"]`. A resource left with no visible methods for the current principal is omitted entirely, same as before. Whenever any resource is guarded, every response carries `Cache-Control: private, no-cache` and `Vary: Authorization`, so a shared cache can never serve one principal's document to another. See `sample/Frank.JsonHome.Sample` for a runnable demonstration, including curl output for both cases.

---

## Frank.Datastar

Frank.Datastar provides seamless integration with [Datastar](https://data-star.dev/), enabling reactive hypermedia applications using Server-Sent Events (SSE).

**Version 7.1.0** features a **native SSE implementation** with zero external dependencies, delivering high-performance Server-Sent Events directly via ASP.NET Core's `IBufferWriter<byte>` API. Supports .NET 8.0, 9.0, and 10.0.

### Installation

```bash
dotnet add package Frank.Datastar
```

### Example

```fsharp
open Frank.Builder
open Frank.Datastar

let updates =
    resource "/updates" {
        name "Updates"

        datastar (fun ctx -> task {
            // SSE stream starts automatically
            do! Datastar.patchElements "<div id='status'>Loading...</div>" ctx
            do! Task.Delay(500)
            do! Datastar.patchElements "<div id='status'>Complete!</div>" ctx
        })
    }

// With explicit HTTP method
let submit =
    resource "/submit" {
        name "Submit"

        datastar HttpMethods.Post (fun ctx -> task {
            let! signals = Datastar.tryReadSignals<FormData> ctx
            match signals with
            | ValueSome data ->
                do! Datastar.patchElements $"<div id='result'>Received: {data.Name}</div>" ctx
            | ValueNone ->
                do! Datastar.patchElements "<div id='error'>Invalid data</div>" ctx
        })
    }
```

### Available Operations

- `Datastar.patchElements` - Update HTML elements in the DOM
- `Datastar.patchSignals` - Update client-side signals
- `Datastar.removeElement` - Remove elements by CSS selector
- `Datastar.executeScript` - Execute JavaScript on the client
- `Datastar.tryReadSignals<'T>` - Read and deserialize signals from request

Each operation also has a `WithOptions` variant for advanced customization.

---

## Frank.Analyzers

Frank.Analyzers provides compile-time static analysis to catch common mistakes in Frank applications.

### Installation

```bash
dotnet add package Frank.Analyzers
```

### Available Analyzers

#### FRANK001: Duplicate HTTP Handler Detection

Detects when multiple handlers for the same HTTP method are defined on a single resource. Only the last handler would be used at runtime, so this is almost always a mistake.

```fsharp
// This will produce a warning:
resource "/example" {
    name "Example"
    get (fun ctx -> ctx.Response.WriteAsync("First"))   // Warning: FRANK001
    get (fun ctx -> ctx.Response.WriteAsync("Second"))  // This one takes effect
}
```

#### FRANK002: Duplicate Accepts Media Type

Detects when the same media type is registered more than once inside a single `negotiate { }`
block. Only the first registration can ever be selected — later ones for the same media type
are unreachable — so this is almost always a mistake.

```fsharp
// This will produce a warning:
resource "/test" {
    get (negotiate {
        accepts "application/json" jsonHandler
        accepts "application/json" anotherJsonHandler  // Warning: FRANK002 (unreachable)
    })
}
```

### IDE Integration

Frank.Analyzers works with:
- **Ionide** (VS Code)
- **Visual Studio** with F# support
- **JetBrains Rider**

Warnings appear inline as you type, helping catch issues before you even compile.

---

## Building

Make sure the following **requirements** are installed in your system:

- [dotnet SDK](https://dotnet.microsoft.com/en-us/download) 8.0 or higher

```
dotnet build
```

---

## Sample Applications

The `sample/` directory contains several example applications:

| Sample | Description |
|--------|-------------|
| `Sample` | Basic Frank application |
| `Frank.OpenApi.Sample` | Product Catalog API demonstrating OpenAPI document generation |
| `Frank.JsonHome.Sample` | JSON Home discovery document, filtered by authorization for anonymous vs. authenticated principals |
| `Frank.Datastar.Basic` | Datastar integration with minimal HTML |
| `Frank.Datastar.Hox` | Datastar with [Hox](https://github.com/AngelMunoz/Hox) view engine |
| `Frank.Datastar.Oxpecker` | Datastar with [Oxpecker.ViewEngine](https://lanayx.github.io/Oxpecker/src/Oxpecker.ViewEngine/) |
| `Frank.Falco` | Frank with [Falco.Markup](https://github.com/pimbrouwers/Falco.Markup) |
| `Frank.Giraffe` | Frank with [Giraffe.ViewEngine](https://github.com/giraffe-fsharp/Giraffe.ViewEngine) |
| `Frank.Oxpecker` | Frank with [Oxpecker.ViewEngine](https://lanayx.github.io/Oxpecker/src/Oxpecker.ViewEngine/) |

---

## License

[Apache 2.0](LICENSE)
