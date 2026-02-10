module Sample.OpenApi.Handlers

open System
open Microsoft.AspNetCore.Http
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

/// Content negotiation example - supports both JSON and XML
let getProductNegotiated =
    handler {
        name "getProductNegotiated"
        summary "Get product with content negotiation"
        description "Returns a product in JSON or XML format based on Accept header"
        tags [ "Products"; "Advanced" ]
        produces typeof<Product> 200 [ "application/json"; "application/xml" ]
        produces typeof<ErrorResponse> 404 [ "application/json"; "application/xml" ]
        handle (fun (ctx: HttpContext) -> task {
            let id = ctx.Request.RouteValues.["id"] |> string |> Guid.Parse
            match ProductStore.getById id with
            | Some product ->
                // In a real app, you'd check Accept header and serialize accordingly
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

/// Health check endpoint (plain handler, not using HandlerDefinition)
let healthCheck (ctx: HttpContext) =
    task {
        do! ctx.Response.WriteAsJsonAsync({| status = "healthy"; timestamp = DateTime.UtcNow |})
    }
