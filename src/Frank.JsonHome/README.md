# Frank.JsonHome

[![NuGet Version](https://img.shields.io/nuget/v/Frank.JsonHome)](https://www.nuget.org/packages/Frank.JsonHome/)

Serves a [JSON Home](https://datatracker.ietf.org/doc/html/draft-nottingham-json-home-06) document describing a [Frank](https://www.nuget.org/packages/Frank/) application's entry-point resources — a machine-readable directory a client can discover once and use to find everything else, instead of hardcoding URLs. It has no dependency on `Frank.Auth` or `Frank.OpenApi`, and adds no NuGet dependency of its own.

## Installation

```bash
dotnet add package Frank.JsonHome
```

## Declaring Discoverable Resources

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

## Application Wiring

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

## Discovery Operations

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

## Authorization Filtering

If [`Frank.Auth`](https://www.nuget.org/packages/Frank.Auth/) is in use, guard a resource the same way you would anywhere else — `Frank.JsonHome` reads the stock `IAuthorizeData`/`AuthorizationPolicy` metadata `Frank.Auth` attaches, with no reference between the two packages:

```fsharp
let adminReports =
    resource "/admin/reports" {
        rel "tag:example.com,2026:admin-reports"
        requireRole "admin"  // Frank.Auth
        get getAdminReports
    }
```

An anonymous request's `/.well-known/home.json` omits `tag:example.com,2026:admin-reports` entirely; an authenticated admin's includes it. This also works with a plain ASP.NET Core `[<Authorize>]`-equivalent, without `Frank.Auth` at all.

Filtering is per HTTP method, not per whole resource — a handler-level requirement only hides the method it's on, not the resource:

```fsharp
let widgets =
    resource "/widgets" {
        rel "tag:example.com,2026:widgets"
        get listWidgets                                    // public
        delete (handler { requireRole "admin"; handle deleteWidget })
    }
```

An anonymous request's `hints.allow` for `tag:example.com,2026:widgets` shows `["GET"]`; an authenticated admin's shows `["GET", "DELETE"]`. A resource left with no visible methods for the current principal is omitted entirely, same as before. Whenever any resource is guarded, every response carries `Cache-Control: private, no-cache` and `Vary: Authorization`, so a shared cache can never serve one principal's document to another.

See [`sample/Frank.JsonHome.Sample`](https://github.com/frank-fs/frank/tree/master/sample/Frank.JsonHome.Sample) for a runnable demonstration, including curl output for both cases.

## Related Packages

Requires [`Frank`](https://www.nuget.org/packages/Frank/). Optionally combines with [`Frank.Auth`](https://www.nuget.org/packages/Frank.Auth/) for authorization-filtered discovery.

See the [project repository](https://github.com/frank-fs/frank) for the complete guide and sample applications.

## License

[MIT](https://github.com/frank-fs/frank/blob/master/LICENSE)
