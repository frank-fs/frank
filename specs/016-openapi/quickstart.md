# Quickstart: Frank.OpenApi

**Feature Branch**: `016-openapi`
**Date**: 2026-02-09

## Prerequisites

- .NET 9.0+ SDK installed
- Existing Frank 7.x application (or new project)

## Installation

Add the Frank.OpenApi NuGet package to your project:

```
dotnet add package Frank.OpenApi
```

This brings in Frank.OpenApi and its dependency FSharp.Data.JsonSchema.OpenApi.

## Basic Usage — Enable OpenAPI with Existing Endpoints

The simplest way to enable OpenAPI: add `useOpenApi` to your web host. Existing endpoints appear in the document with route templates and HTTP methods.

```fsharp
open Frank
open Frank.Builder
open Frank.OpenApi

let products =
    resource "/products" {
        name "Products"
        get (fun ctx -> ctx.Response.WriteAsJsonAsync({| id = 1; name = "Widget" |}))
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        useOpenApi           // <-- one line to enable OpenAPI
        resource products
    }
    0
```

Visit `http://localhost:5000/openapi/v1.json` to see the generated OpenAPI document.

## Rich Metadata — Using the Handler Builder

For detailed API documentation, use the `handler` builder to attach operation names, descriptions, tags, and request/response type information:

```fsharp
open Frank
open Frank.Builder
open Frank.OpenApi

type Product = { Id: int; Name: string; Price: decimal }
type CreateProductRequest = { Name: string; Price: decimal }
type ErrorResponse = { Message: string; Code: string }

let listProducts =
    handler {
        name "ListProducts"
        description "Returns all products"
        tags ["Products"]
        produces<Product list> 200
        handle (fun ctx -> ctx.Response.WriteAsJsonAsync(products))
    }

let createProduct =
    handler {
        name "CreateProduct"
        description "Creates a new product"
        tags ["Products"]
        accepts<CreateProductRequest>
        produces<Product> 201
        produces<ErrorResponse> 400
        producesEmpty 404
        handle (fun ctx ->
            task {
                let! req = ctx.Request.ReadFromJsonAsync<CreateProductRequest>()
                // ... create product ...
                ctx.Response.StatusCode <- 201
                return! ctx.Response.WriteAsJsonAsync(newProduct)
            })
    }

let productList =
    resource "/products" {
        name "Products"
        get listProducts           // HandlerDefinition with metadata
        post createProduct         // HandlerDefinition with metadata
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        useOpenApi
        resource productList
    }
    0
```

## Custom OpenAPI Configuration

Pass a configuration callback to `useOpenApi` for advanced scenarios:

```fsharp
open FSharp.Data.JsonSchema.Core
open FSharp.Data.JsonSchema.OpenApi

webHost args {
    useDefaults
    useOpenApi (fun options ->
        // Custom schema transformer with non-default DU encoding
        let config = { SchemaGeneratorConfig.defaults with
                        DiscriminatorPropertyName = "type" }
        options.AddSchemaTransformer(FSharpSchemaTransformer(config))
    )
    resource products
}
```

## Mixing Handler Styles

Plain handler functions and handler builder definitions can coexist in the same resource:

```fsharp
let myResource =
    resource "/items" {
        name "Items"
        get listItems                    // HandlerDefinition with metadata
        delete (fun ctx ->               // Plain handler function (no metadata)
            task {
                ctx.Response.StatusCode <- 204
            })
    }
```

Plain handlers appear in the OpenAPI document with their route and HTTP method, but without detailed type information or operation metadata.
