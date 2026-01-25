module HoxExample

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Frank
open Frank.Builder
open Frank.Datastar
open Hox
open Hox.Core
open Hox.Rendering

// ===========================
// DATASTAR + HOX INTEGRATION
// ===========================
// Hox provides async-first HTML rendering with a clean F# API.
// Combined with Datastar's SSE, this enables reactive server-driven UIs
// with composable, type-safe HTML components.
//
// KEY PATTERNS:
// - patchElements is PRIMARY (hypermedia-first)
// - patchSignals is SECONDARY (only for ephemeral UI state)
//
// HOX ATTRIBUTE SYNTAX:
// Uses CSS selector notation: h("tag#id.class [attr=value]", children)
// ===========================

// Domain models
type User = {
    Id: int
    Name: string
    Email: string
    Avatar: string
}

type Product = {
    Id: int
    Name: string
    Price: decimal
    Category: string
    InStock: bool
}

type DashboardStats = {
    TotalUsers: int
    ActiveOrders: int
    Revenue: decimal
}

// Simulated data sources (replace with actual DB calls)
let fetchUsersAsync count =
    task {
        do! Task.Delay(100) // Simulate async I/O

        return
            [ for i in 1..count do
                  { Id = i
                    Name = $"User {i}"
                    Email = $"user{i}@example.com"
                    Avatar = $"https://i.pravatar.cc/150?img={i}" } ]
    }

let fetchProductsAsync category =
    task {
        do! Task.Delay(100)

        let allProducts =
            [ { Id = 1
                Name = "Laptop"
                Price = 999M
                Category = "electronics"
                InStock = true }
              { Id = 2
                Name = "Phone"
                Price = 699M
                Category = "electronics"
                InStock = true }
              { Id = 3
                Name = "Desk"
                Price = 299M
                Category = "furniture"
                InStock = false }
              { Id = 4
                Name = "Chair"
                Price = 199M
                Category = "furniture"
                InStock = true }
              { Id = 5
                Name = "Monitor"
                Price = 349M
                Category = "electronics"
                InStock = true } ]

        return
            if String.IsNullOrWhiteSpace(category) then
                allProducts
            else
                allProducts |> List.filter (fun p -> p.Category = category)
    }

let fetchDashboardStatsAsync () =
    task {
        do! Task.Delay(100)

        return
            { TotalUsers = 1234
              ActiveOrders = 567
              Revenue = 89012.50M }
    }

// ===========================
// HOX COMPONENTS (Async HTML Rendering)
// ===========================
// Hox uses CSS selector notation for attributes: [attr=value]

// User card component - using Hox CSS selector syntax for attributes
let userCard (user: User) =
    h(
        "div.user-card",
        [ h($"img.avatar [src={user.Avatar}] [alt={user.Name}]", [])
          h(
              "div.user-info",
              [ h("h3", [ Text user.Name ])
                h("p", [ Text user.Email ])
                h($"button [data-on-click=@get('/user/{user.Id}/details')]", [ Text "View Details" ]) ]
          ) ]
    )

// User list fragment (async)
let userListFragment count =
    task {
        let! users = fetchUsersAsync count
        return h("div#user-list.grid", fragment [ for user in users do userCard user ])
    }

// Product card component
let productCard (product: Product) =
    let stockClass = if product.InStock then "in-stock" else "out-of-stock"
    let stockText = if product.InStock then "In Stock" else "Out of Stock"

    h(
        $"div.product-card.{stockClass}",
        [ h("h3", [ Text product.Name ])
          h("p.category", [ Text $"Category: {product.Category}" ])
          h("p.price", [ Text $"${product.Price}" ])
          h($"span.stock-status.{stockClass}", [ Text stockText ])
          if product.InStock then
              h($"button.add-to-cart [data-on-click=@post('/cart/add?id={product.Id}')]", [ Text "Add to Cart" ]) ]
    )

// Product grid fragment (async)
let productGridFragment category =
    task {
        let! products = fetchProductsAsync category
        return h("div#product-grid.grid", fragment [ for product in products do productCard product ])
    }

// Dashboard stats component
let dashboardStatsFragment () =
    task {
        let! stats = fetchDashboardStatsAsync ()
        let revenue = stats.Revenue.ToString("N2")

        return
            h(
                "div#dashboard-stats",
                [ h(
                      "div.stat-card",
                      [ h("h4", [ Text "Total Users" ])
                        h("p.stat-value", [ Text(string stats.TotalUsers) ]) ]
                  )
                  h(
                      "div.stat-card",
                      [ h("h4", [ Text "Active Orders" ])
                        h("p.stat-value", [ Text(string stats.ActiveOrders) ]) ]
                  )
                  h(
                      "div.stat-card",
                      [ h("h4", [ Text "Revenue" ])
                        h("p.stat-value", [ Text $"${revenue}" ]) ]
                  ) ]
            )
    }

// Table row component
let tableRow (data: string list) =
    h("tr", fragment [ for cell in data do h("td", [ Text cell ]) ])

// Data table fragment (async)
let dataTableFragment () =
    task {
        do! Task.Delay(200) // Simulate data loading

        let data =
            [ [ "Order #1001"; "2024-01-15"; "$299.99"; "Shipped" ]
              [ "Order #1002"; "2024-01-15"; "$149.50"; "Processing" ]
              [ "Order #1003"; "2024-01-14"; "$599.00"; "Delivered" ]
              [ "Order #1004"; "2024-01-14"; "$89.99"; "Pending" ] ]

        return
            h(
                "table#orders-table",
                [ h(
                      "thead",
                      [ h(
                            "tr",
                            [ h("th", [ Text "Order ID" ])
                              h("th", [ Text "Date" ])
                              h("th", [ Text "Amount" ])
                              h("th", [ Text "Status" ]) ]
                        ) ]
                  )
                  h("tbody", fragment [ for row in data do tableRow row ]) ]
            )
    }

// ===========================
// DATASTAR ENDPOINTS WITH HOX
// ===========================

// Example 1: Load user list (PRIMARY PATTERN: patchElements)
let loadUsers =
    resource "/users/load" {
        name "LoadUsers"
        datastar (fun ctx ->
            task {
                let count =
                    match ctx.Request.Query.TryGetValue("count") with
                    | true, values -> int values.[0]
                    | false, _ -> 6

                let! node = userListFragment count
                let! html = Render.asString node
                do! Datastar.patchElements html ctx
            })
    }

// Example 2: Load products by category (PRIMARY PATTERN: patchElements)
let loadProductsByCategory =
    resource "/products/load" {
        name "LoadProductsByCategory"
        datastar (fun ctx ->
            task {
                let category = ctx.Request.Query.["category"].ToString()
                let! node = productGridFragment category
                let! html = Render.asString node
                do! Datastar.patchElements html ctx
            })
    }

// Example 3: Load dashboard stats (PRIMARY PATTERN: patchElements)
let loadDashboardStats =
    resource "/dashboard/stats" {
        name "LoadDashboardStats"
        datastar (fun ctx ->
            task {
                let! node = dashboardStatsFragment ()
                let! html = Render.asString node
                do! Datastar.patchElements html ctx
            })
    }

// Example 4: Load data table (PRIMARY PATTERN: patchElements)
let loadOrdersTable =
    resource "/orders/load" {
        name "LoadOrdersTable"
        datastar (fun ctx ->
            task {
                let! node = dataTableFragment ()
                let! html = Render.asString node
                do! Datastar.patchElements html ctx
            })
    }

// Example 5: Progressive dashboard loading (multiple fragments)
// This demonstrates the PRIMARY USE CASE: streaming multiple HTML updates
let loadFullDashboard =
    resource "/dashboard/load" {
        name "LoadFullDashboard"
        datastar (fun ctx ->
            task {
                // 1. Load stats first
                let! statsNode = dashboardStatsFragment ()
                let! statsHtml = Render.asString statsNode
                do! Datastar.patchElements statsHtml ctx

                // 2. Load user list
                let! usersNode = userListFragment 4
                let! usersHtml = Render.asString usersNode
                do! Datastar.patchElements usersHtml ctx

                // 3. Load product grid
                let! productsNode = productGridFragment ""
                let! productsHtml = Render.asString productsNode
                do! Datastar.patchElements productsHtml ctx
            })
    }

// Example 6: User details modal (async data + render)
let loadUserDetails =
    resource "/user/{id}/details" {
        name "LoadUserDetails"
        datastar (fun ctx ->
            task {
                let userId = ctx.Request.RouteValues.["id"] |> string |> int

                // Simulate fetching user details
                do! Task.Delay(150)

                let user =
                    { Id = userId
                      Name = $"User {userId}"
                      Email = $"user{userId}@example.com"
                      Avatar = $"https://i.pravatar.cc/150?img={userId}" }

                let modal =
                    h(
                        "div#user-modal.modal",
                        [ h(
                              "div.modal-content",
                              [ h(
                                    "div.modal-header",
                                    [ h("h2", [ Text "User Details" ])
                                      h("button.close [data-on-click=@get('/modal/close')]", [ Text "×" ]) ]
                                )
                                h(
                                    "div.modal-body",
                                    [ h($"img [src={user.Avatar}] [alt={user.Name}]", [])
                                      h("h3", [ Text user.Name ])
                                      h("p", [ Text $"Email: {user.Email}" ])
                                      h("p", [ Text $"User ID: {user.Id}" ]) ]
                                ) ]
                          ) ]
                    )

                let! html = Render.asString modal
                do! Datastar.patchElements html ctx
            })
    }

// Example 7: Search results with Hox rendering
let searchWithHox =
    resource "/search/hox" {
        name "SearchWithHox"
        datastar (fun ctx ->
            task {
                let query = ctx.Request.Query.["q"].ToString()

                // Simulate search
                do! Task.Delay(100)

                let results =
                    [ "Apple"; "Banana"; "Cherry"; "Date"; "Elderberry" ]
                    |> List.filter (fun item -> item.Contains(query, StringComparison.OrdinalIgnoreCase))

                let resultsNode =
                    if results.IsEmpty then
                        h("div#search-results.empty", [ h("p", [ Text "No results found" ]) ])
                    else
                        h(
                            "div#search-results",
                            [ h("h3", [ Text $"Found {results.Length} results" ])
                              h(
                                  "ul",
                                  fragment
                                      [ for result in results do
                                            h(
                                                "li.search-result",
                                                [ Text result
                                                  h($"button.select [data-on-click=@get('/item/select?name={result}')]", [ Text "Select" ]) ]
                                            ) ]
                              ) ]
                        )

                let! html = Render.asString resultsNode
                do! Datastar.patchElements html ctx
            })
    }

// Example 8: Form submission with server-side rendering
let submitContactForm =
    resource "/contact/submit" {
        name "SubmitContactForm"
        datastar (fun ctx ->
            task {
                let name = ctx.Request.Query.["name"].ToString()
                let email = ctx.Request.Query.["email"].ToString()
                let message = ctx.Request.Query.["message"].ToString()

                // Server-side validation
                let isValid =
                    not (String.IsNullOrWhiteSpace(name)) && email.Contains("@") && message.Length >= 10

                let responseNode =
                    if isValid then
                        h(
                            "div#form-response.success",
                            [ h("h3", [ Text "Message Sent!" ])
                              h("p", [ Text $"Thank you, {name}. We'll respond to {email} soon." ]) ]
                        )
                    else
                        h(
                            "div#form-response.error",
                            [ h("h3", [ Text "Validation Error" ])
                              h(
                                  "ul",
                                  fragment
                                      [ if String.IsNullOrWhiteSpace(name) then
                                            h("li", [ Text "Name is required" ])
                                        if not (email.Contains("@")) then
                                            h("li", [ Text "Invalid email address" ])
                                        if message.Length < 10 then
                                            h("li", [ Text "Message must be at least 10 characters" ]) ]
                              ) ]
                        )

                let! html = Render.asString responseNode
                do! Datastar.patchElements html ctx
            })
    }

// Example 9: Streaming updates (simulate real-time data)
// Demonstrates PRIMARY USE CASE: multiple progressive HTML updates
let streamUpdates =
    resource "/stream/updates" {
        name "StreamUpdates"
        datastar (fun ctx ->
            task {
                // Simulate streaming multiple updates
                for i in 1..5 do
                    do! Task.Delay(500)

                    let timestamp = DateTime.Now.ToString("HH:mm:ss")

                    let updateNode =
                        h(
                            "div.update-item",
                            [ h("span.timestamp", [ Text timestamp ])
                              h("span.message", [ Text $"Update #{i}: System running normally" ]) ]
                        )

                    let! html = Render.asString updateNode
                    let fullHtml = $"""<div id="updates-container">{html}</div>"""
                    do! Datastar.patchElements fullHtml ctx
            })
    }

// Example 10: Complex nested components
let loadNestedComponents =
    resource "/components/nested" {
        name "LoadNestedComponents"
        datastar (fun ctx ->
            task {
                let! users = fetchUsersAsync 3
                let! products = fetchProductsAsync "electronics"

                let page =
                    h(
                        "div#nested-demo",
                        [ h(
                              "section.users-section",
                              [ h("h2", [ Text "Recent Users" ])
                                h("div.user-grid", fragment [ for user in users do userCard user ]) ]
                          )
                          h(
                              "section.products-section",
                              [ h("h2", [ Text "Featured Products" ])
                                h("div.product-grid", fragment [ for product in products do productCard product ]) ]
                          ) ]
                    )

                let! html = Render.asString page
                do! Datastar.patchElements html ctx
            })
    }

// ===========================
// APPLICATION SETUP
// ===========================

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        plug StaticFileExtensions.UseStaticFiles

        // Single element loads (PRIMARY PATTERN: patchElements)
        resource loadUsers
        resource loadProductsByCategory
        resource loadDashboardStats
        resource loadOrdersTable

        // Progressive streaming (PRIMARY USE CASE)
        resource loadFullDashboard
        resource streamUpdates

        // Interactive components
        resource loadUserDetails
        resource searchWithHox
        resource submitContactForm
        resource loadNestedComponents
    }

    0
