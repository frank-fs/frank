module Frank.LinkedData.Sample.Program

open System
open System.Collections.Generic
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Frank
open Frank.Builder
open Frank.LinkedData

// ── Domain Model ──

type ProductCategory =
    | Electronics
    | Books
    | Clothing

type Product =
    { Id: int
      Name: string
      Price: decimal
      InStock: bool option
      Category: ProductCategory }

// ── In-memory store ──

let private store = Dictionary<int, Product>()

let private seed () =
    store.[1] <-
        { Id = 1
          Name = "F# in Action"
          Price = 39.99m
          InStock = Some true
          Category = Books }

    store.[2] <-
        { Id = 2
          Name = "USB-C Hub"
          Price = 29.95m
          InStock = Some true
          Category = Electronics }

    store.[3] <-
        { Id = 3
          Name = "Lambda T-Shirt"
          Price = 24.00m
          InStock = None
          Category = Clothing }

// ── Handlers ──

let private jsonOptions =
    let opts = JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase)
    opts.Converters.Add(Serialization.JsonStringEnumConverter())
    opts

let private listProducts (ctx: HttpContext) : Task =
    let products = store.Values |> Seq.toArray
    ctx.Response.ContentType <- "application/json"
    JsonSerializer.SerializeAsync(ctx.Response.Body, products, jsonOptions)

let private getProduct (ctx: HttpContext) : Task =
    let idStr = ctx.GetRouteValue("id") |> string

    match Int32.TryParse(idStr) with
    | true, id when store.ContainsKey(id) ->
        ctx.Response.ContentType <- "application/json"
        JsonSerializer.SerializeAsync(ctx.Response.Body, store.[id], jsonOptions)
    | _ ->
        ctx.Response.StatusCode <- 404
        ctx.Response.WriteAsync("Not found")

let private createProduct (ctx: HttpContext) : Task =
    task {
        let! product = JsonSerializer.DeserializeAsync<Product>(ctx.Request.Body, jsonOptions)
        store.[product.Id] <- product
        ctx.Response.StatusCode <- 201
        ctx.Response.ContentType <- "application/json"
        do! JsonSerializer.SerializeAsync(ctx.Response.Body, product, jsonOptions)
    }

let private updateProduct (ctx: HttpContext) : Task =
    task {
        let idStr = ctx.GetRouteValue("id") |> string

        match Int32.TryParse(idStr) with
        | true, id ->
            let! product = JsonSerializer.DeserializeAsync<Product>(ctx.Request.Body, jsonOptions)
            store.[id] <- { product with Id = id }
            ctx.Response.ContentType <- "application/json"
            do! JsonSerializer.SerializeAsync(ctx.Response.Body, store.[id], jsonOptions)
        | _ ->
            ctx.Response.StatusCode <- 404
            do! ctx.Response.WriteAsync("Not found")
    }

let private deleteProduct (ctx: HttpContext) : Task =
    let idStr = ctx.GetRouteValue("id") |> string

    match Int32.TryParse(idStr) with
    | true, id ->
        store.Remove(id) |> ignore
        ctx.Response.StatusCode <- 204
        Task.CompletedTask
    | _ ->
        ctx.Response.StatusCode <- 404
        ctx.Response.WriteAsync("Not found")

// ── Resources ──

let products =
    resource "/products" {
        linkedData
        name "Product Collection"
        get listProducts
        post createProduct
    }

let product =
    resource "/products/{id}" {
        linkedData
        name "Product"
        get getProduct
        put updateProduct
        delete deleteProduct
    }

[<EntryPoint>]
let main args =
    seed ()

    webHost args {
        service (fun services ->
            services
                .AddMvcCore()
                .AddJsonOptions(fun opts ->
                    opts.JsonSerializerOptions.PropertyNamingPolicy <- JsonNamingPolicy.CamelCase
                    opts.JsonSerializerOptions.Converters.Add(Serialization.JsonStringEnumConverter()))
            |> ignore

            services)

        useLinkedData

        resource products
        resource product
    }

    0
