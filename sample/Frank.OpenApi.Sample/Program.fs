module Sample.OpenApi.Program

open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank
open Frank.Builder
open Frank.OpenApi
open Sample.OpenApi.Extensions
open Sample.OpenApi.Handlers

// Resource definitions

let productsResource =
    resource "/api/products" {
        name "Products"
        get listProducts
        post createProduct
    }

let productByIdResource =
    resource "/api/products/{id}" {
        name "ProductById"
        get getProduct
        put updateProduct
        delete deleteProduct
    }

let searchResource =
    resource "/api/products/search" {
        name "ProductSearch"
        post searchProducts
    }

let contentNegotiationResource =
    resource "/api/products/{id}/negotiate" {
        name "ProductContentNegotiation"
        get getProductNegotiated
    }

// Health check using plain handler (mixed with HandlerDefinition)
let healthResource =
    resource "/health" {
        name "Health"
        get healthCheck
    }

// Root endpoint to provide API information
let rootHandler =
    handler {
        name "apiInfo"
        summary "API Information"
        description "Returns information about the Product Catalog API"
        tags [ "Meta" ]
        produces typeof<{| name: string; version: string; openApiUrl: string; scalarUrl: string |}> 200
        handle (fun (ctx: HttpContext) -> task {
            do! ctx.Response.WriteAsJsonAsync({|
                name = "Product Catalog API"
                version = "1.0.0"
                openApiUrl = "/openapi/v1.json"
                scalarUrl = "/scalar/v1"
            |})
        })
    }

let rootResource =
    resource "/" {
        name "Root"
        get rootHandler
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        logging (fun options -> options.AddConsole().SetMinimumLevel(LogLevel.Information))

        // Enable OpenAPI document generation
        useOpenApi

        plugBeforeRoutingWhen isDevelopment DeveloperExceptionPageExtensions.UseDeveloperExceptionPage

        // Register resources
        resource rootResource
        resource healthResource
        resource productsResource
        resource productByIdResource
        resource searchResource
        resource contentNegotiationResource
    }

    0
