# Frank.OpenApi

[![NuGet Version](https://img.shields.io/nuget/v/Frank.OpenApi)](https://www.nuget.org/packages/Frank.OpenApi/)

Native OpenAPI document generation for [Frank](https://www.nuget.org/packages/Frank/) applications, with first-class support for F# types and declarative metadata using computation expressions.

## Installation

```bash
dotnet add package Frank.OpenApi
```

## HandlerBuilder Computation Expression

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

## HandlerBuilder Operations

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

## F# Type Schema Generation

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

## WebHostBuilder Integration

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

## Content Negotiation

Define multiple content types for requests and responses:

```fsharp
handler {
    name "getProduct"
    produces typeof<Product> 200 [ "application/json"; "application/xml" ]
    accepts typeof<ProductQuery> [ "application/json"; "application/xml" ]
    handle (fun ctx -> task { (* ... *) })
}
```

These operations only *describe* the content types in the generated OpenAPI document — they don't dispatch on `Accept` at runtime. For that, use the `negotiate { }` computation expression, which lives in Frank core (`Frank.Builder`, no Frank.OpenApi needed):

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

`negotiate { }` performs real per-media-type dispatch: each `accepts` registers an independent representation, and the one matching the request's `Accept` header (by RFC 9110 quality and specificity rules) is the only one whose handler runs. Nothing matching means `406 Not Acceptable`, and every response carries `Vary: Accept`.

> **Name collision:** `Frank.Builder.negotiate` (this CE) and the older `Frank.ContentNegotiation.negotiate` function (`statusCode -> body -> ctx -> Task`, which delegates the whole response to MVC's formatter registry) share the identifier `negotiate`. With both modules opened, F#'s ordinary shadowing rules apply and the last `open` wins. Qualify one of them, or use the non-colliding `ctx.Negotiate(200, body)` extension member.

## Backward Compatibility

Frank.OpenApi is fully backward compatible with existing Frank applications. You can:
- Mix `HandlerDefinition` and plain `RequestDelegate` handlers in the same resource
- Add OpenAPI metadata incrementally without changing existing code
- Use the library only where you need API documentation

## Related Packages

Requires [`Frank`](https://www.nuget.org/packages/Frank/). See [`sample/Frank.OpenApi.Sample`](https://github.com/frank-fs/frank/tree/master/sample/Frank.OpenApi.Sample) for a runnable Product Catalog API.

See the [project repository](https://github.com/frank-fs/frank) for the complete guide and sample applications.

## License

[MIT](https://github.com/frank-fs/frank/blob/master/LICENSE)
