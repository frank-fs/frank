module Sample.OpenApi.Program

open System.Text.Json
open System.Text.Json.Serialization
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank
open Frank.Builder
open Frank.OpenApi
open Sample.OpenApi
open Sample.OpenApi.Extensions
open Sample.OpenApi.Handlers

/// System.Text.Json has no built-in support for F# discriminated unions (Category is
/// one), and Domain.fs is out of scope for this change -- so `Product`'s JSON
/// representation (used directly by `getProductNegotiated`'s JSON branch, and by the
/// pre-existing listProducts/getProduct/etc. handlers) needs this converter registered
/// for `WriteAsJsonAsync` to work at all. `getProductBridged`'s viaOutputFormatter path
/// sidesteps this instead, by mapping to a wire-friendly DTO in Handlers.fs.
type private CategoryJsonConverter() =
    inherit JsonConverter<Category>()

    override _.Read(reader, _typeToConvert, _options) =
        match reader.GetString() with
        | "Electronics" -> Electronics
        | "Books" -> Books
        | "Clothing" -> Clothing
        | "Home" -> Home
        | other -> failwithf "Unknown Category '%s'" other

    override _.Write(writer, value, _options) = writer.WriteStringValue(string value)

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

let contentNegotiationBridgedResource =
    resource "/api/products/{id}/negotiate-bridged" {
        name "ProductContentNegotiationBridged"
        get getProductBridged
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
                openApiUrl = "/.well-known/openapi.json"
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

        // getProductBridged's JSON/XML representations go through
        // Frank.ContentNegotiation.viaOutputFormatter, which needs AddMvcCore() (already
        // registered by useDefaults) plus AddXmlSerializerFormatters() for application/xml.
        service (fun (services: IServiceCollection) ->
            services.AddMvcCore().AddXmlSerializerFormatters() |> ignore
            services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(fun (options: Microsoft.AspNetCore.Http.Json.JsonOptions) ->
                options.SerializerOptions.Converters.Add(CategoryJsonConverter()))
            |> ignore
            services)

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
        resource contentNegotiationBridgedResource
    }

    0
