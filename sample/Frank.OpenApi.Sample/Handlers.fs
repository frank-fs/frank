module Sample.OpenApi.Handlers

open System
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.OpenApi
open Sample.OpenApi

/// List all products
let listProducts =
    handler {
        name "listProducts"
        summary "List all products"
        description "Returns a list of all products in the catalog"
        tags [ "Products" ]
        produces typeof<Product list> 200
        handle (fun (ctx: HttpContext) -> task {
            let products = ProductStore.getAll()
            do! ctx.Response.WriteAsJsonAsync(products)
        })
    }

/// Get a single product by ID
let getProduct =
    handler {
        name "getProduct"
        summary "Get product by ID"
        description "Returns a single product by its unique identifier"
        tags [ "Products" ]
        produces typeof<Product> 200
        produces typeof<ErrorResponse> 404
        handle (fun (ctx: HttpContext) -> task {
            let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
            match ProductStore.getById id with
            | Some product ->
                do! ctx.Response.WriteAsJsonAsync(product)
            | None ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsJsonAsync({
                    Code = "NOT_FOUND"
                    Message = $"Product with ID {id} not found"
                    Details = None
                })
        })
    }

/// Create a new product
let createProduct =
    handler {
        name "createProduct"
        summary "Create a new product"
        description "Creates a new product in the catalog and returns the created product"
        tags [ "Products"; "Admin" ]
        produces typeof<Product> 201
        produces typeof<ErrorResponse> 400
        accepts typeof<CreateProductRequest>
        handle (fun (ctx: HttpContext) -> task {
            try
                let! request = ctx.Request.ReadFromJsonAsync<CreateProductRequest>()
                let product = ProductStore.create request
                ctx.Response.StatusCode <- 201
                do! ctx.Response.WriteAsJsonAsync(product)
            with ex ->
                ctx.Response.StatusCode <- 400
                do! ctx.Response.WriteAsJsonAsync({
                    Code = "INVALID_REQUEST"
                    Message = "Failed to create product"
                    Details = Some ex.Message
                })
        })
    }

/// Update an existing product
let updateProduct =
    handler {
        name "updateProduct"
        summary "Update a product"
        description "Updates an existing product with partial data (only provided fields are updated)"
        tags [ "Products"; "Admin" ]
        produces typeof<Product> 200
        produces typeof<ErrorResponse> 404
        produces typeof<ErrorResponse> 400
        accepts typeof<UpdateProductRequest>
        handle (fun (ctx: HttpContext) -> task {
            try
                let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
                let! request = ctx.Request.ReadFromJsonAsync<UpdateProductRequest>()
                match ProductStore.update id request with
                | Some product ->
                    do! ctx.Response.WriteAsJsonAsync(product)
                | None ->
                    ctx.Response.StatusCode <- 404
                    do! ctx.Response.WriteAsJsonAsync({
                        Code = "NOT_FOUND"
                        Message = $"Product with ID {id} not found"
                        Details = None
                    })
            with ex ->
                ctx.Response.StatusCode <- 400
                do! ctx.Response.WriteAsJsonAsync({
                    Code = "INVALID_REQUEST"
                    Message = "Failed to update product"
                    Details = Some ex.Message
                })
        })
    }

/// Delete a product
let deleteProduct =
    handler {
        name "deleteProduct"
        summary "Delete a product"
        description "Deletes a product from the catalog"
        tags [ "Products"; "Admin" ]
        producesEmpty 204
        produces typeof<ErrorResponse> 404
        handle (fun (ctx: HttpContext) -> task {
            let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
            if ProductStore.delete id then
                ctx.Response.StatusCode <- 204
            else
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsJsonAsync({
                    Code = "NOT_FOUND"
                    Message = $"Product with ID {id} not found"
                    Details = None
                })
        })
    }

/// Search products with query parameters
let searchProducts =
    handler {
        name "searchProducts"
        summary "Search products"
        description "Search products with filters for category, price range, and stock status"
        tags [ "Products"; "Search" ]
        produces typeof<Product list> 200
        accepts typeof<ProductQuery>
        handle (fun (ctx: HttpContext) -> task {
            let! query = ctx.Request.ReadFromJsonAsync<ProductQuery>()
            let results = ProductStore.query query
            do! ctx.Response.WriteAsJsonAsync(results)
        })
    }

/// Content negotiation example -- genuinely returns a different body for JSON vs. HTML
let getProductNegotiated =
    negotiate {
        accepts "application/json" (handler {
            name "getProductNegotiatedJson"
            produces typeof<Product> 200
            produces typeof<ErrorResponse> 404
            handle (fun (ctx: HttpContext) -> task {
                let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
                match ProductStore.getById id with
                | Some product -> do! ctx.Response.WriteAsJsonAsync(product)
                | None ->
                    ctx.Response.StatusCode <- 404
                    do! ctx.Response.WriteAsJsonAsync({
                        Code = "NOT_FOUND"
                        Message = $"Product with ID {id} not found"
                        Details = None
                    })
            })
        })
        accepts "text/html" (fun (ctx: HttpContext) -> task {
            let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
            match ProductStore.getById id with
            | Some product ->
                do! ctx.Response.WriteAsync(
                    $"<html><body><h1>{product.Name}</h1><p>${product.Price}</p></body></html>")
            | None ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsync($"<html><body><h1>Not found</h1><p>{id}</p></body></html>")
        })
    }

/// Wire-friendly shape for `getProductBridged`'s found-product JSON/XML representation.
///
/// `Product` itself can't go through `viaOutputFormatter` as-is: `Category` is a plain
/// discriminated union with no built-in `System.Text.Json` support (throws
/// `System.NotSupportedException`), and `Product` isn't `[<CLIMutable>]` -- required by
/// `AddXmlSerializerFormatters()`'s `XmlSerializer`, which also can't introspect the DU
/// or `Set<string>` fields on its own. `XmlSerializer` has no built-in support for F#
/// discriminated unions, `option`, or `Set<'T>` -- a real .NET interop limitation, not
/// something Frank's content negotiation caused, and not something worth papering over
/// here. `Domain.fs` is out of scope for this change, so this DTO -- `Category` as its
/// case name, `Tags` as a plain array -- exists purely to give the *found* case a shape
/// both formatters can actually handle. It stays an honest, single-purpose DTO for a
/// product; the not-found case below deliberately does NOT go through it (or through
/// `viaOutputFormatter` at all) -- see `getProductBridged`.
[<CLIMutable>]
type ProductWire =
    { Id: Guid
      Name: string
      Description: string
      Price: decimal
      Category: string
      Tags: string[]
      InStock: bool }

module ProductWire =
    let ofProduct (p: Product) : ProductWire =
        { Id = p.Id
          Name = p.Name
          Description = p.Description |> Option.toObj
          Price = p.Price
          Category = string p.Category
          Tags = p.Tags |> Set.toArray
          InStock = p.InStock }

/// Content negotiation with the IOutputFormatter bridge -- JSON and XML each reuse MVC's
/// formatter registry (requires AddMvcCore().AddXmlSerializerFormatters(), wired up in
/// Program.fs) for the found case, calling `ContentNegotiation.viaOutputFormatter`
/// directly on a plain `ProductWire`. JSON and XML are registered as two separate
/// `accepts` entries rather than one shared handler, because the not-found case needs
/// its own body per content type: the domain `ErrorResponse` type isn't `[<CLIMutable>]`
/// and has an `option` field, so it can't go through `XmlSerializer` either. Each
/// representation writes its own 404 body directly instead -- the same pattern every
/// other 404 in this file already uses (`getProduct`, `getProductNegotiated`,
/// `updateProduct`, `deleteProduct`), and the same pattern the `text/html` representation
/// below already uses for its whole response.
let getProductBridged =
    negotiate {
        accepts "application/json" (fun (ctx: HttpContext) -> task {
            let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
            match ProductStore.getById id with
            | Some product ->
                do! Frank.ContentNegotiation.viaOutputFormatter "application/json" (ProductWire.ofProduct product) ctx
            | None ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsJsonAsync({
                    Code = "NOT_FOUND"
                    Message = $"Product with ID {id} not found"
                    Details = None
                })
        })
        accepts "application/xml" (fun (ctx: HttpContext) -> task {
            let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
            match ProductStore.getById id with
            | Some product ->
                do! Frank.ContentNegotiation.viaOutputFormatter "application/xml" (ProductWire.ofProduct product) ctx
            | None ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsync(
                    $"""<?xml version="1.0" encoding="utf-8"?><Error><Code>NOT_FOUND</Code><Message>Product with ID {id} not found</Message></Error>""")
        })
        accepts "text/html" (fun (ctx: HttpContext) -> task {
            let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
            match ProductStore.getById id with
            | Some product ->
                do! ctx.Response.WriteAsync(
                    $"<html><body><h1>{product.Name}</h1><p>${product.Price}</p></body></html>")
            | None ->
                ctx.Response.StatusCode <- 404
                do! ctx.Response.WriteAsync($"<html><body><h1>Not found</h1><p>{id}</p></body></html>")
        })
    }

/// Health check endpoint (plain handler, not using HandlerDefinition)
let healthCheck (ctx: HttpContext) =
    task {
        do! ctx.Response.WriteAsJsonAsync({| status = "healthy"; timestamp = DateTime.UtcNow |})
    }
