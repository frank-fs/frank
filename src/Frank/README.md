# Frank

[![NuGet Version](https://img.shields.io/nuget/v/Frank)](https://www.nuget.org/packages/Frank/)
![GitHub Release Date](https://img.shields.io/github/release-date/frank-fs/frank)
![Build status](https://github.com/frank-fs/frank/workflows/CI/badge.svg)

[F#](https://fsharp.org/) [computation expressions](https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions), or builders, for configuring the [`Microsoft.AspNetCore.Hosting.IWebHostBuilder`](https://docs.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.hosting.iwebhostbuilder?view=aspnetcore-2.0) and defining routes for HTTP resources using [`Microsoft.AspNetCore.Routing`](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/routing?view=aspnetcore-2.1).

This project was inspired by @filipw's [Building Microservices with ASP.NET Core (without MVC)](https://www.strathweb.com/2017/01/building-microservices-with-asp-net-core-without-mvc/).

## Installation

```bash
dotnet add package Frank
```

## Features

- `WebHostBuilder` - computation expression for configuring `WebHost`
- `ResourceBuilder` - computation expression for configuring resources (routing)
- **No** pre-defined view engine - use your preferred view engine implementation,
  e.g. [Falco.Markup](https://github.com/pimbrouwers/Falco.Markup),
  [Oxpecker.ViewEngine](https://lanayx.github.io/Oxpecker/src/Oxpecker.ViewEngine/),
  or [Hox](https://github.com/AngelMunoz/Hox)
- Easy extensibility - just extend the `Builder` with your own methods!

## Basic Example

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

## Content Negotiation

The `negotiate { }` computation expression performs real per-media-type dispatch: each `accepts` registers an independent representation, and the one matching the request's `Accept` header (by RFC 9110 quality and specificity rules) is the only one whose handler runs. Nothing matching means `406 Not Acceptable`, and every response carries `Vary: Accept`.

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

## Related Packages

Frank has several companion packages that build on this core: [`Frank.Auth`](https://www.nuget.org/packages/Frank.Auth/) (authorization), [`Frank.OpenApi`](https://www.nuget.org/packages/Frank.OpenApi/) (OpenAPI generation), [`Frank.JsonHome`](https://www.nuget.org/packages/Frank.JsonHome/) (hypermedia discovery), [`Frank.Datastar`](https://www.nuget.org/packages/Frank.Datastar/) (reactive SSE), [`Frank.Rdf`](https://www.nuget.org/packages/Frank.Rdf/) (linked data), and [`Frank.Analyzers`](https://www.nuget.org/packages/Frank.Analyzers/) (compile-time checks).

See the [project repository](https://github.com/frank-fs/frank) for the complete guide and sample applications.

## License

[MIT](https://github.com/frank-fs/frank/blob/master/LICENSE)
