namespace Sample.OpenApi

open System

/// Product category
type Category =
    | Electronics
    | Books
    | Clothing
    | Home

/// Product entity with F# type features
type Product = {
    Id: Guid
    Name: string
    Description: string option  // Optional field
    Price: decimal
    Category: Category
    Tags: Set<string>
    InStock: bool
}

/// Request to create a new product
type CreateProductRequest = {
    Name: string
    Description: string option
    Price: decimal
    Category: Category
    Tags: string list
}

/// Request to update an existing product
type UpdateProductRequest = {
    Name: string option
    Description: string option
    Price: decimal option
    Category: Category option
    Tags: string list option
    InStock: bool option
}

/// Query parameters for listing products
type ProductQuery = {
    Category: Category option
    MinPrice: decimal option
    MaxPrice: decimal option
    InStockOnly: bool
}

/// Response for product operations
type ProductResponse =
    | Created of product: Product
    | Updated of product: Product
    | Deleted
    | NotFound

/// Error response
type ErrorResponse = {
    Code: string
    Message: string
    Details: string option
}

/// In-memory product store
module ProductStore =
    open System.Collections.Concurrent

    let private products = ConcurrentDictionary<Guid, Product>()

    // Seed some initial data
    do
        let sampleProducts = [
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111")
                Name = "Laptop"
                Description = Some "High-performance laptop"
                Price = 1299.99m
                Category = Electronics
                Tags = Set.ofList ["tech"; "portable"]
                InStock = true
            }
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222")
                Name = "F# Programming Book"
                Description = Some "Learn functional programming with F#"
                Price = 49.99m
                Category = Books
                Tags = Set.ofList ["programming"; "fsharp"]
                InStock = true
            }
        ]
        for p in sampleProducts do
            products.TryAdd(p.Id, p) |> ignore

    let getAll () = products.Values |> Seq.toList

    let getById (id: Guid) = products.TryGetValue(id) |> function true, p -> Some p | _ -> None

    let create (request: CreateProductRequest) =
        let product = {
            Id = Guid.NewGuid()
            Name = request.Name
            Description = request.Description
            Price = request.Price
            Category = request.Category
            Tags = Set.ofList request.Tags
            InStock = true
        }
        products.TryAdd(product.Id, product) |> ignore
        product

    let update (id: Guid) (request: UpdateProductRequest) =
        match products.TryGetValue(id) with
        | true, existing ->
            let updated = {
                existing with
                    Name = request.Name |> Option.defaultValue existing.Name
                    Description = request.Description |> Option.orElse existing.Description
                    Price = request.Price |> Option.defaultValue existing.Price
                    Category = request.Category |> Option.defaultValue existing.Category
                    Tags = request.Tags |> Option.map Set.ofList |> Option.defaultValue existing.Tags
                    InStock = request.InStock |> Option.defaultValue existing.InStock
            }
            products.[id] <- updated
            Some updated
        | _ -> None

    let delete (id: Guid) =
        products.TryRemove(id) |> function true, _ -> true | _ -> false

    let query (q: ProductQuery) =
        getAll()
        |> List.filter (fun p ->
            let categoryMatch =
                match q.Category with
                | Some cat -> p.Category = cat
                | None -> true

            let priceMatch =
                let minMatch = q.MinPrice |> Option.map (fun min -> p.Price >= min) |> Option.defaultValue true
                let maxMatch = q.MaxPrice |> Option.map (fun max -> p.Price <= max) |> Option.defaultValue true
                minMatch && maxMatch

            let stockMatch =
                if q.InStockOnly then p.InStock else true

            categoryMatch && priceMatch && stockMatch
        )
