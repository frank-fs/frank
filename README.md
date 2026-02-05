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

## Frank.Datastar

Frank.Datastar provides seamless integration with [Datastar](https://data-star.dev/), enabling reactive hypermedia applications using Server-Sent Events (SSE).

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

---

## Sample Applications

The `sample/` directory contains several example applications:

| Sample | Description |
|--------|-------------|
| `Sample` | Basic Frank application |
| `Frank.Datastar.Basic` | Datastar integration with minimal HTML |
| `Frank.Datastar.Hox` | Datastar with [Hox](https://github.com/AngelMunoz/Hox) view engine |
| `Frank.Datastar.Oxpecker` | Datastar with [Oxpecker.ViewEngine](https://lanayx.github.io/Oxpecker/src/Oxpecker.ViewEngine/) |
| `Frank.Falco` | Frank with [Falco.Markup](https://github.com/pimbrouwers/Falco.Markup) |
| `Frank.Giraffe` | Frank with [Giraffe.ViewEngine](https://github.com/giraffe-fsharp/Giraffe.ViewEngine) |
| `Frank.Oxpecker` | Frank with [Oxpecker.ViewEngine](https://lanayx.github.io/Oxpecker/src/Oxpecker.ViewEngine/) |

---

## License

[Apache 2.0](LICENSE)
