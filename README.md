# Frank

[![NuGet Version](https://img.shields.io/badge/nuget-v7.2.0-blue)](https://www.nuget.org/packages/Frank/7.2.0)
![GitHub Release Date](https://img.shields.io/github/release-date/frank-fs/frank)
![Build status](https://github.com/frank-fs/frank/workflows/CI/badge.svg)

A web framework for building applications where resources are the primary abstraction, invalid states are structurally impossible, and the application itself is the API documentation. Frank uses [F#](https://fsharp.org/) [computation expressions](https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions) as a declarative, extensible layer over [ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/).

Frank is built on four ideas:

**Resources, not routes.** HTTP resources are the unit of design. You define what a resource is and what it can do — the framework handles routing, method dispatch, and metadata. This is REST as Fielding described it, not the "REST" that became a synonym for JSON-over-HTTP.

**Make invalid states unrepresentable.** Statechart-enforced state machines govern resource behavior at the framework level. If a transition isn't legal, it isn't available — in the response headers, in the HTML controls, in the API surface. No defensive coding required.

**Built for the age of agents.** Frank provides CLI tooling and extension libraries that layer semantic metadata onto your application — ALPS profiles, Link headers, JSON Home documents, OWL ontologies. Developers and agents can reflect on a running application, understand its capabilities, and refine it continuously.

**Discovery is a first-class concern.** A Frank application is understandable from a cold start. JSON Home documents advertise available resources. `Link` headers connect them. `Allow` headers declare what's possible in the current state. ALPS profiles define what things mean. Semantic web vocabularies give structure a shared language. No SDK, no out-of-band documentation — the application explains itself through standard HTTP, content negotiation, and open standards that clients (human or machine) can navigate without prior knowledge.

```fsharp
let home =
    resource "/" {
        name "Home"

        get (fun (ctx: HttpContext) ->
            ctx.Response.WriteAsync("Welcome!"))
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        resource home
    }
    0
```

---

## Getting Started

Frank was inspired by @filipw's [Building Microservices with ASP.NET Core (without MVC)](https://www.strathweb.com/2017/01/building-microservices-with-asp-net-core-without-mvc/).

---

## Packages

| Package             | Description                                             | NuGet                                                                                                            |
| ------------------- | ------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------- |
| **Frank**           | Core computation expressions for WebHost and routing    | [![NuGet](https://img.shields.io/badge/nuget-v7.2.0-blue)](https://www.nuget.org/packages/Frank/7.2.0)           |
| **Frank.Auth**      | Resource-level authorization extensions                 | [![NuGet](https://img.shields.io/badge/nuget-v7.2.0-blue)](https://www.nuget.org/packages/Frank.Auth/7.2.0)      |
| **Frank.OpenApi**   | Native OpenAPI document generation with F# type schemas | [![NuGet](https://img.shields.io/badge/nuget-v7.2.0-blue)](https://www.nuget.org/packages/Frank.OpenApi/7.2.0)   |
| **Frank.Datastar**  | Datastar SSE integration for reactive hypermedia        | [![NuGet](https://img.shields.io/badge/nuget-v7.2.0-blue)](https://www.nuget.org/packages/Frank.Datastar/7.2.0)  |
| **Frank.Analyzers** | F# Analyzers for compile-time error detection           | [![NuGet](https://img.shields.io/badge/nuget-v7.2.0-blue)](https://www.nuget.org/packages/Frank.Analyzers/7.2.0) |

### Package Dependency Graph

```
Frank (core)
│   ETag / conditional request middleware
│
├── Frank.Auth ────────────────── Frank
│
├── Frank.OpenApi ─────────────── Frank
│
├── Frank.Datastar ────────────── Frank
│
└── Frank.Analyzers ──────────── (FSharp.Analyzers.SDK analyzer, no runtime dependency)
```

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

Frank.Auth provides resource-level authorization for Frank applications, integrating with ASP.NET Core's built-in authorization infrastructure.

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

| Pattern                 | Operation                        | Behavior                                     |
| ----------------------- | -------------------------------- | -------------------------------------------- |
| Authenticated user      | `requireAuth`                    | 401 if unauthenticated, 200 if authenticated |
| Claim (single value)    | `requireClaim "type" "value"`    | 403 if claim missing or wrong value          |
| Claim (multiple values) | `requireClaim "type" ["a"; "b"]` | 200 if user has any listed value (OR)        |
| Role                    | `requireRole "Admin"`            | 403 if user not in role                      |
| Named policy            | `requirePolicy "PolicyName"`     | Delegates to registered policy               |
| Multiple requirements   | Stack multiple `require*`        | AND semantics — all must pass                |
| No requirements         | (default)                        | Publicly accessible, zero overhead           |

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

| Operation                                        | Description                                                        |
| ------------------------------------------------ | ------------------------------------------------------------------ |
| `name "operationId"`                             | Sets the OpenAPI operationId                                       |
| `summary "text"`                                 | Brief summary of the operation                                     |
| `description "text"`                             | Detailed description                                               |
| `tags [ "Tag1"; "Tag2" ]`                        | Categorize endpoints                                               |
| `produces typeof<T> statusCode`                  | Define response type and status code                               |
| `produces typeof<T> statusCode ["content/type"]` | Response with content negotiation                                  |
| `producesEmpty statusCode`                       | Empty responses (204, 404, etc.)                                   |
| `accepts typeof<T>`                              | Define request body type                                           |
| `accepts typeof<T> ["content/type"]`             | Request with content negotiation                                   |
| `handle (fun ctx -> ...)`                        | Handler function (supports Task, Task<'a>, Async<unit>, Async<'a>) |

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
        useOpenApi  // Adds /openapi/v1.json endpoint

        resource productsResource
    }
    0
```

The OpenAPI document will be available at `/openapi/v1.json`.

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

### Backward Compatibility

Frank.OpenApi is fully backward compatible with existing Frank applications. You can:

- Mix `HandlerDefinition` and plain `RequestDelegate` handlers in the same resource
- Add OpenAPI metadata incrementally without changing existing code
- Use the library only where you need API documentation

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

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for setup, build instructions, design principles, and pull request guidelines.

---

## Sample Applications

The `sample/` directory contains several example applications:

| Sample                    | Description                                                                                     |
| ------------------------- | ----------------------------------------------------------------------------------------------- |
| `Sample`                  | Basic Frank application                                                                         |
| `Frank.OpenApi.Sample`    | Product Catalog API demonstrating OpenAPI document generation                                   |
| `Frank.Datastar.Basic`    | Datastar integration with minimal HTML                                                          |
| `Frank.Datastar.Hox`      | Datastar with [Hox](https://github.com/AngelMunoz/Hox) view engine                              |
| `Frank.Datastar.Oxpecker` | Datastar with [Oxpecker.ViewEngine](https://lanayx.github.io/Oxpecker/src/Oxpecker.ViewEngine/) |
| `Frank.Falco`             | Frank with [Falco.Markup](https://github.com/pimbrouwers/Falco.Markup)                          |
| `Frank.Giraffe`           | Frank with [Giraffe.ViewEngine](https://github.com/giraffe-fsharp/Giraffe.ViewEngine)           |
| `Frank.Oxpecker`          | Frank with [Oxpecker.ViewEngine](https://lanayx.github.io/Oxpecker/src/Oxpecker.ViewEngine/)    |

---

## References

- [Design Documents](docs/) — Design philosophy, vision, and architecture documents
- [Frank.Statecharts Guide](docs/STATECHARTS.md) — Core concepts, hierarchical statechart support, guards, and test coverage overview
- [Semantic Resources Vision](docs/SEMANTIC-RESOURCES.md) — Agent-legible applications and the self-describing app architecture
- [Spec Pipeline](docs/SPEC-PIPELINE.md) — Bidirectional design spec pipeline (WSD, SCXML, ALPS)
- [How is this different from Webmachine or Freya?](docs/COMPARISON.md) — Detailed comparison of Frank.Statecharts with Webmachine and Freya's approach to HTTP resource state machines

---

## License

[MIT](LICENSE)
