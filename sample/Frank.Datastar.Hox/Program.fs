module HoxExample

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Frank
open Frank.Builder
open Frank.Datastar
open StarFederation.Datastar.FSharp
open Hox
open Hox.Core
open Hox.Rendering

// ===========================
// DATASTAR + HOX INTEGRATION
// ===========================
// Hox provides async-first HTML rendering with a clean F# API.
// Combined with Datastar's SSE, this enables reactive server-driven UIs
// with composable, type-safe HTML components.
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
let fetchUsersAsync count = task {
    do! Task.Delay(100) // Simulate async I/O
    return [
        for i in 1..count do
            { Id = i
              Name = $"User {i}"
              Email = $"user{i}@example.com"
              Avatar = $"https://i.pravatar.cc/150?img={i}" }
    ]
}

let fetchProductsAsync category = task {
    do! Task.Delay(100)
    let allProducts = [
        { Id = 1; Name = "Laptop"; Price = 999M; Category = "electronics"; InStock = true }
        { Id = 2; Name = "Phone"; Price = 699M; Category = "electronics"; InStock = true }
        { Id = 3; Name = "Desk"; Price = 299M; Category = "furniture"; InStock = false }
        { Id = 4; Name = "Chair"; Price = 199M; Category = "furniture"; InStock = true }
        { Id = 5; Name = "Monitor"; Price = 349M; Category = "electronics"; InStock = true }
    ]
    
    return 
        if String.IsNullOrWhiteSpace(category) then
            allProducts
        else
            allProducts |> List.filter (fun p -> p.Category = category)
}

let fetchDashboardStatsAsync() = task {
    do! Task.Delay(100)
    return {
        TotalUsers = 1234
        ActiveOrders = 567
        Revenue = 89012.50M
    }
}

// ===========================
// HOX COMPONENTS (Async HTML Rendering)
// ===========================

// User card component
let userCard (user: User) =
    h("div.user-card", [
        h("img.avatar", [Attr("src", user.Avatar); Attr("alt", user.Name)])
        h("div.user-info", [
            h("h3", [Text user.Name])
            h("p", [Text user.Email])
            h("button", [
                Attr("data-on-click", $"@get('/user/{user.Id}/details')")
                Text "View Details"
            ])
        ])
    ])

// User list fragment (async)
let userListFragment count = task {
    let! users = fetchUsersAsync count
    return h("div#user-list.grid", 
        fragment [for user in users do userCard user])
}

// Product card component
let productCard (product: Product) =
    let stockClass = if product.InStock then "in-stock" else "out-of-stock"
    let stockText = if product.InStock then "In Stock" else "Out of Stock"
    
    h($"div.product-card.{stockClass}", [
        h("h3", [Text product.Name])
        h("p.category", [Text $"Category: {product.Category}"])
        h("p.price", [Text $"${product.Price}"])
        h($"span.stock-status.{stockClass}", [Text stockText])
        if product.InStock then
            h("button.add-to-cart", [
                Attr("data-on-click", $"@post('/cart/add?id={product.Id}')")
                Text "Add to Cart"
            ])
    ])

// Product grid fragment (async)
let productGridFragment category = task {
    let! products = fetchProductsAsync category
    return h("div#product-grid.grid",
        fragment [for product in products do productCard product])
}

// Dashboard stats component
let dashboardStatsFragment() = task {
    let! stats = fetchDashboardStatsAsync()
    return h("div#dashboard-stats", [
        h("div.stat-card", [
            h("h4", [Text "Total Users"])
            h("p.stat-value", [Text (string stats.TotalUsers)])
        ])
        h("div.stat-card", [
            h("h4", [Text "Active Orders"])
            h("p.stat-value", [Text (string stats.ActiveOrders)])
        ])
        h("div.stat-card", [
            h("h4", [Text "Revenue"])
            h("p.stat-value", [Text $"${stats.Revenue:N2}"])
        ])
    ])
}

// Table row component
let tableRow (data: string list) =
    h("tr", fragment [for cell in data do h("td", [Text cell])])

// Data table fragment (async)
let dataTableFragment() = task {
    do! Task.Delay(200) // Simulate data loading
    let data = [
        ["Order #1001"; "2024-01-15"; "$299.99"; "Shipped"]
        ["Order #1002"; "2024-01-15"; "$149.50"; "Processing"]
        ["Order #1003"; "2024-01-14"; "$599.00"; "Delivered"]
        ["Order #1004"; "2024-01-14"; "$89.99"; "Pending"]
    ]
    
    return h("table#orders-table", [
        h("thead", [
            h("tr", [
                h("th", [Text "Order ID"])
                h("th", [Text "Date"])
                h("th", [Text "Amount"])
                h("th", [Text "Status"])
            ])
        ])
        h("tbody", fragment [for row in data do tableRow row])
    ])
}

// ===========================
// DATASTAR ENDPOINTS WITH HOX
// ===========================

// Example 1: Load user list
let loadUsers =
    resource "/users/load" {
        name "LoadUsers"
        patchElements (fun ctx -> task {
            let count = 
                match ctx.Request.Query.TryGetValue("count") with
                | true, values -> int values.[0]
                | false, _ -> 6
            
            let! node = userListFragment count
            return! Render.AsStringAsync(node)
        })
    }

// Example 2: Load products by category
let loadProductsByCategory =
    resource "/products/load" {
        name "LoadProductsByCategory"
        patchElements (fun ctx -> task {
            let category = ctx.Request.Query.["category"].ToString()
            let! node = productGridFragment category
            return! Render.AsStringAsync(node)
        })
    }

// Example 3: Load dashboard stats
let loadDashboardStats =
    resource "/dashboard/stats" {
        name "LoadDashboardStats"
        patchElements (fun ctx -> task {
            let! node = dashboardStatsFragment()
            return! Render.AsStringAsync(node)
        })
    }

// Example 4: Load data table
let loadOrdersTable =
    resource "/orders/load" {
        name "LoadOrdersTable"
        patchElements (fun ctx -> task {
            let! node = dataTableFragment()
            return! Render.AsStringAsync(node)
        })
    }

// Example 5: Progressive dashboard loading (multiple fragments)
let loadFullDashboard =
    resource "/dashboard/load" {
        name "LoadFullDashboard"
        datastar (fun ctx -> task {
            // Load multiple fragments progressively with SSE
            
            // 1. Load stats first
            let! statsNode = dashboardStatsFragment()
            let! statsHtml = Render.AsStringAsync(statsNode)
            do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, statsHtml)
            
            // 2. Load user list
            let! usersNode = userListFragment 4
            let! usersHtml = Render.AsStringAsync(usersNode)
            do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, usersHtml)
            
            // 3. Load product grid
            let! productsNode = productGridFragment ""
            let! productsHtml = Render.AsStringAsync(productsNode)
            do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, productsHtml)
        })
    }

// Example 6: User details modal (async data + render)
let loadUserDetails =
    resource "/user/{id}/details" {
        name "LoadUserDetails"
        patchElements (fun ctx -> task {
            let userId = ctx.Request.RouteValues.["id"] |> string |> int
            
            // Simulate fetching user details
            do! Task.Delay(150)
            let user = {
                Id = userId
                Name = $"User {userId}"
                Email = $"user{userId}@example.com"
                Avatar = $"https://i.pravatar.cc/150?img={userId}"
            }
            
            let modal = h("div#user-modal.modal", [
                h("div.modal-content", [
                    h("div.modal-header", [
                        h("h2", [Text "User Details"])
                        h("button.close", [
                            Attr("data-on-click", "@get('/modal/close')")
                            Text "×"
                        ])
                    ])
                    h("div.modal-body", [
                        h("img", [Attr("src", user.Avatar); Attr("alt", user.Name)])
                        h("h3", [Text user.Name])
                        h("p", [Text $"Email: {user.Email}"])
                        h("p", [Text $"User ID: {user.Id}"])
                    ])
                ])
            ])
            
            return! Render.AsStringAsync(modal)
        })
    }

// Example 7: Search results with Hox rendering
let searchWithHox =
    resource "/search/hox" {
        name "SearchWithHox"
        patchElements (fun ctx -> task {
            let query = ctx.Request.Query.["q"].ToString()
            
            // Simulate search
            do! Task.Delay(100)
            let results = 
                ["Apple"; "Banana"; "Cherry"; "Date"; "Elderberry"]
                |> List.filter (fun item -> 
                    item.Contains(query, StringComparison.OrdinalIgnoreCase))
            
            let resultsNode = 
                if results.IsEmpty then
                    h("div#search-results.empty", [
                        h("p", [Text "No results found"])
                    ])
                else
                    h("div#search-results", [
                        h("h3", [Text $"Found {results.Length} results"])
                        h("ul", 
                            fragment [
                                for result in results do
                                    h("li.search-result", [
                                        Text result
                                        h("button.select", [
                                            Attr("data-on-click", $"@get('/item/select?name={result}')")
                                            Text "Select"
                                        ])
                                    ])
                            ])
                    ])
            
            return! Render.AsStringAsync(resultsNode)
        })
    }

// Example 8: Form submission with server-side rendering
let submitContactForm =
    resource "/contact/submit" {
        name "SubmitContactForm"
        patchElements (fun ctx -> task {
            let name = ctx.Request.Query.["name"].ToString()
            let email = ctx.Request.Query.["email"].ToString()
            let message = ctx.Request.Query.["message"].ToString()
            
            // Server-side validation
            let isValid = 
                not (String.IsNullOrWhiteSpace(name)) &&
                email.Contains("@") &&
                message.Length >= 10
            
            let responseNode = 
                if isValid then
                    h("div#form-response.success", [
                        h("h3", [Text "Message Sent!"])
                        h("p", [Text $"Thank you, {name}. We'll respond to {email} soon."])
                    ])
                else
                    h("div#form-response.error", [
                        h("h3", [Text "Validation Error"])
                        h("ul", 
                            fragment [
                                if String.IsNullOrWhiteSpace(name) then
                                    h("li", [Text "Name is required"])
                                if not (email.Contains("@")) then
                                    h("li", [Text "Invalid email address"])
                                if message.Length < 10 then
                                    h("li", [Text "Message must be at least 10 characters"])
                            ])
                    ])
            
            return! Render.AsStringAsync(responseNode)
        })
    }

// Example 9: Streaming updates (simulate real-time data)
let streamUpdates =
    resource "/stream/updates" {
        name "StreamUpdates"
        datastar (fun ctx -> task {
            // Simulate streaming multiple updates
            for i in 1..5 do
                do! Task.Delay(500)
                
                let updateNode = h("div.update-item", [
                    h("span.timestamp", [Text (DateTime.Now.ToString("HH:mm:ss"))])
                    h("span.message", [Text $"Update #{i}: System running normally"])
                ])
                
                let! html = Render.AsStringAsync(updateNode)
                let fullHtml = $"""<div id="updates-container">{html}</div>"""
                do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, fullHtml)
        })
    }

// Example 10: Complex nested components
let loadNestedComponents =
    resource "/components/nested" {
        name "LoadNestedComponents"
        patchElements (fun ctx -> task {
            let! users = fetchUsersAsync 3
            let! products = fetchProductsAsync "electronics"
            
            let page = h("div#nested-demo", [
                h("section.users-section", [
                    h("h2", [Text "Recent Users"])
                    h("div.user-grid", 
                        fragment [for user in users do userCard user])
                ])
                h("section.products-section", [
                    h("h2", [Text "Featured Products"])
                    h("div.product-grid",
                        fragment [for product in products do productCard product])
                ])
            ])
            
            return! Render.AsStringAsync(page)
        })
    }

// ===========================
// APPLICATION SETUP
// ===========================

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    
    let app = builder.Build()
    
    app.UseStaticFiles() |> ignore
    
    // Register all routes
    app.UseResource(loadUsers)
    app.UseResource(loadProductsByCategory)
    app.UseResource(loadDashboardStats)
    app.UseResource(loadOrdersTable)
    app.UseResource(loadFullDashboard)
    app.UseResource(loadUserDetails)
    app.UseResource(searchWithHox)
    app.UseResource(submitContactForm)
    app.UseResource(streamUpdates)
    app.UseResource(loadNestedComponents)
    
    // Serve index page
    app.MapGet("/", Func<string>(fun () -> System.IO.File.ReadAllText("wwwroot/index.html"))) |> ignore
    
    app.Run()
    0
