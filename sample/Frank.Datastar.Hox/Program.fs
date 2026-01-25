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

// Simulated data sources
let fetchUsersAsync count =
    task {
        do! Task.Delay(100)
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
            [ { Id = 1; Name = "Laptop"; Price = 999M; Category = "electronics"; InStock = true }
              { Id = 2; Name = "Phone"; Price = 699M; Category = "electronics"; InStock = true }
              { Id = 3; Name = "Desk"; Price = 299M; Category = "furniture"; InStock = false }
              { Id = 4; Name = "Chair"; Price = 199M; Category = "furniture"; InStock = true }
              { Id = 5; Name = "Monitor"; Price = 349M; Category = "electronics"; InStock = true } ]

        return
            if String.IsNullOrWhiteSpace(category) then allProducts
            else allProducts |> List.filter (fun p -> p.Category = category)
    }

let fetchDashboardStatsAsync () =
    task {
        do! Task.Delay(100)
        return { TotalUsers = Random.Shared.Next(1000, 2000)
                 ActiveOrders = Random.Shared.Next(100, 500)
                 Revenue = decimal (Random.Shared.Next(50000, 100000)) }
    }

// ===========================
// HOX COMPONENTS (Bulma-styled)
// ===========================

let userCard (user: User) =
    h("div.card",
        [ h("div.card-image",
              [ h("figure.image.is-4by3",
                    [ h($"img [src={user.Avatar}] [alt={user.Name}]", []) ]) ])
          h("div.card-content",
              [ h("p.title.is-4", [ Text user.Name ])
                h("p.subtitle.is-6", [ Text user.Email ]) ]) ])

let productCard (product: Product) =
    let stockClass = if product.InStock then "has-text-success" else "has-text-danger"
    let stockText = if product.InStock then "In Stock" else "Out of Stock"

    h("div.card",
        [ h("div.card-content",
              [ h("p.title.is-5", [ Text product.Name ])
                h("p.subtitle.is-6", [ Text $"${product.Price}" ])
                h("p", [ Text $"Category: {product.Category}" ])
                h($"p.{stockClass}", [ Text stockText ])
                if product.InStock then
                    h($"button.button.is-primary.is-small [data-on-click=@post('/cart/add?id={product.Id}')]",
                        [ Text "Add to Cart" ]) ]) ])

let dashboardStatsComponent (stats: DashboardStats) =
    h("div.columns.is-multiline",
        [ h("div.column.is-4",
              [ h("div.notification.is-primary",
                    [ h("p.heading", [ Text "Total Users" ])
                      h("p.title", [ Text (string stats.TotalUsers) ]) ]) ])
          h("div.column.is-4",
              [ h("div.notification.is-info",
                    [ h("p.heading", [ Text "Active Orders" ])
                      h("p.title", [ Text (string stats.ActiveOrders) ]) ]) ])
          h("div.column.is-4",
              [ h("div.notification.is-success",
                    [ h("p.heading", [ Text "Revenue" ])
                      h("p.title", [ Text $"${stats.Revenue:N0}" ]) ]) ]) ])

let notificationComponent (message: string) (notifType: string) =
    let bulmaClass =
        match notifType with
        | "success" -> "is-success"
        | "warning" -> "is-warning"
        | "error" | "danger" -> "is-danger"
        | _ -> "is-info"

    h($"div#notification.notification.{bulmaClass}",
        [ h("button.delete", [])
          Text message ])

let dataTableComponent (headers: string list) (rows: string list list) =
    h("table.table.is-fullwidth.is-striped.is-hoverable",
        [ h("thead",
              [ h("tr", fragment [ for header in headers do h("th", [ Text header ]) ]) ])
          h("tbody",
              fragment [ for row in rows do
                           h("tr", fragment [ for cell in row do h("td", [ Text cell ]) ]) ]) ])

// ===========================
// DATASTAR ENDPOINTS
// ===========================

// Example 1: Load user cards (with count in path)
let loadUsersWithCount =
    resource "/load-users/{count}" {
        name "LoadUsersWithCount"
        datastar (fun ctx ->
            task {
                let count =
                    match ctx.Request.RouteValues.TryGetValue("count") with
                    | true, v -> int (string v)
                    | _ -> 6

                let! users = fetchUsersAsync count
                let node = h("div#user-list.columns.is-multiline",
                    fragment [ for user in users do
                                 h("div.column.is-4", [ userCard user ]) ])
                let! html = Render.asString node
                do! Datastar.patchElements html ctx
            })
    }

// Example 2: Load product catalog with filter form
let loadCatalog =
    resource "/load-catalog" {
        name "LoadCatalog"
        datastar (fun ctx ->
            task {
                // First send the filter form
                let filterForm = h("div#filter-form",
                    [ h("div.field",
                          [ h("label.label", [ Text "Category" ])
                            h("div.select.is-fullwidth",
                                [ h("select [data-bind-category]",
                                      [ h("option [value=]", [ Text "All" ])
                                        h("option [value=electronics]", [ Text "Electronics" ])
                                        h("option [value=furniture]", [ Text "Furniture" ]) ]) ]) ])
                      h("div.field",
                          [ h("label.checkbox",
                                [ h("input [type=checkbox] [data-bind-inStockOnly]", [])
                                  Text " In Stock Only" ]) ])
                      h("button.button.is-primary.is-fullwidth [data-on-click=@get('/products/load')]",
                          [ Text "Apply Filters" ]) ])

                let! filterHtml = Render.asString filterForm
                do! Datastar.patchElements filterHtml ctx

                do! Task.Delay(100)

                // Then send the products
                let! products = fetchProductsAsync ""
                let productGrid = h("div#product-grid.columns.is-multiline",
                    fragment [ for product in products do
                                 h("div.column.is-6", [ productCard product ]) ])
                let! productHtml = Render.asString productGrid
                do! Datastar.patchElements productHtml ctx
            })
    }

// Example 3: Load dashboard
let loadDashboard =
    resource "/load-dashboard" {
        name "LoadDashboard"
        datastar (fun ctx ->
            task {
                let! stats = fetchDashboardStatsAsync()
                let node = h("div#dashboard", [ dashboardStatsComponent stats ])
                let! html = Render.asString node
                do! Datastar.patchElements html ctx
            })
    }

// Example 4: Refresh all sections
let refreshAll =
    resource "/refresh-all" {
        name "RefreshAll"
        datastar (fun ctx ->
            task {
                // Dashboard
                let! stats = fetchDashboardStatsAsync()
                let dashNode = h("div#dashboard", [ dashboardStatsComponent stats ])
                let! dashHtml = Render.asString dashNode
                do! Datastar.patchElements dashHtml ctx

                do! Task.Delay(100)

                // Users
                let! users = fetchUsersAsync 3
                let usersNode = h("div#user-list.columns.is-multiline",
                    fragment [ for user in users do h("div.column.is-4", [ userCard user ]) ])
                let! usersHtml = Render.asString usersNode
                do! Datastar.patchElements usersHtml ctx

                do! Task.Delay(100)

                // Products
                let! products = fetchProductsAsync ""
                let productsNode = h("div#product-grid.columns.is-multiline",
                    fragment [ for product in products do h("div.column.is-6", [ productCard product ]) ])
                let! productsHtml = Render.asString productsNode
                do! Datastar.patchElements productsHtml ctx
            })
    }

// Example 5: Notifications
let notify =
    resource "/notify" {
        name "Notify"
        datastar HttpMethods.Post (fun ctx ->
            task {
                let message = ctx.Request.Query.["message"].ToString()
                let notifType = ctx.Request.Query.["type"].ToString()

                let node = notificationComponent message notifType
                let! html = Render.asString node
                do! Datastar.patchElements html ctx
            })
    }

// Example 6: Data table
let loadTable =
    resource "/load-table" {
        name "LoadTable"
        datastar (fun ctx ->
            task {
                do! Task.Delay(200)

                let headers = [ "Order ID"; "Date"; "Customer"; "Amount"; "Status" ]
                let rows = [
                    [ $"#ORD-{Random.Shared.Next(1000, 9999)}"; DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd"); "John Doe"; "$299.99"; "Shipped" ]
                    [ $"#ORD-{Random.Shared.Next(1000, 9999)}"; DateTime.Now.AddDays(-2).ToString("yyyy-MM-dd"); "Jane Smith"; "$149.50"; "Processing" ]
                    [ $"#ORD-{Random.Shared.Next(1000, 9999)}"; DateTime.Now.AddDays(-3).ToString("yyyy-MM-dd"); "Bob Wilson"; "$599.00"; "Delivered" ]
                    [ $"#ORD-{Random.Shared.Next(1000, 9999)}"; DateTime.Now.AddDays(-4).ToString("yyyy-MM-dd"); "Alice Brown"; "$89.99"; "Pending" ]
                ]

                let node = h("div#data-table", [ dataTableComponent headers rows ])
                let! html = Render.asString node
                do! Datastar.patchElements html ctx
            })
    }

// Example 7: Stream complex content
let streamContent =
    resource "/stream-content" {
        name "StreamContent"
        datastar (fun ctx ->
            task {
                // Header section
                let timestamp = DateTime.Now.ToString("HH:mm:ss")
                let header = h("div.hero.is-primary.is-small",
                    [ h("div.hero-body",
                          [ h("p.title", [ Text "Complex Layout" ])
                            h("p.subtitle", [ Text $"Generated at {timestamp}" ]) ]) ])
                let! headerHtml = Render.asString header
                do! Datastar.patchElements $"""<div id="complex-layout">{headerHtml}</div>""" ctx

                do! Task.Delay(300)

                // Stats section
                let! stats = fetchDashboardStatsAsync()
                let statsNode = dashboardStatsComponent stats
                let! statsHtml = Render.asString statsNode
                do! Datastar.patchElements $"""<div id="complex-layout">{statsHtml}</div>""" ctx

                do! Task.Delay(300)

                // Users section
                let! users = fetchUsersAsync 4
                let usersNode = h("div.columns.is-multiline",
                    fragment [ for user in users do h("div.column.is-3", [ userCard user ]) ])
                let! usersHtml = Render.asString usersNode
                do! Datastar.patchElements $"""<div id="complex-layout">{usersHtml}</div>""" ctx
            })
    }

// Example 8: Products load endpoint
let loadProductsEndpoint =
    resource "/products/load" {
        name "LoadProducts"
        datastar (fun ctx ->
            task {
                let category = ctx.Request.Query.["category"].ToString()
                let! products = fetchProductsAsync category

                let node = h("div#product-grid.columns.is-multiline",
                    fragment [ for product in products do
                                 h("div.column.is-6", [ productCard product ]) ])
                let! html = Render.asString node
                do! Datastar.patchElements html ctx
            })
    }

// Example 9: Update user view
[<CLIMutable>]
type ViewModeSignals = { viewMode: string }

let updateUserView =
    resource "/update-user-view" {
        name "UpdateUserView"
        datastar HttpMethods.Post (fun ctx ->
            task {
                let! signals = Datastar.tryReadSignals<ViewModeSignals> ctx
                let! users = fetchUsersAsync 6

                let node =
                    match signals with
                    | ValueSome s when s.viewMode = "table" ->
                        let headers = [ "ID"; "Name"; "Email" ]
                        let rows = users |> List.map (fun u -> [ string u.Id; u.Name; u.Email ])
                        h("div#user-list", [ dataTableComponent headers rows ])
                    | _ ->
                        h("div#user-list.columns.is-multiline",
                            fragment [ for user in users do h("div.column.is-4", [ userCard user ]) ])

                let! html = Render.asString node
                do! Datastar.patchElements html ctx
            })
    }

// Example 10: Real-time updates
let realtimeUpdate =
    resource "/realtime-update" {
        name "RealtimeUpdate"
        datastar (fun ctx ->
            task {
                // Send notification
                let messages = [
                    ("New order received!", "success")
                    ("User signed up", "info")
                    ("Payment processed", "success")
                    ("Stock running low", "warning")
                ]
                let (msg, typ) = messages.[Random.Shared.Next(messages.Length)]
                let notifNode = notificationComponent msg typ
                let! notifHtml = Render.asString notifNode
                do! Datastar.patchElements notifHtml ctx

                do! Task.Delay(100)

                // Update dashboard
                let! stats = fetchDashboardStatsAsync()
                let dashNode = h("div#dashboard", [ dashboardStatsComponent stats ])
                let! dashHtml = Render.asString dashNode
                do! Datastar.patchElements dashHtml ctx
            })
    }

// ===========================
// APPLICATION SETUP
// ===========================

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        // UseDefaultFiles must come before UseStaticFiles to serve index.html at "/"
        plug DefaultFilesExtensions.UseDefaultFiles
        plug StaticFileExtensions.UseStaticFiles

        // User endpoints
        resource loadUsersWithCount

        // Product endpoints
        resource loadCatalog
        resource loadProductsEndpoint

        // Dashboard
        resource loadDashboard

        // Multi-section refresh
        resource refreshAll

        // Notifications
        resource notify

        // Data table
        resource loadTable

        // Streaming content
        resource streamContent

        // View mode toggle
        resource updateUserView

        // Real-time updates
        resource realtimeUpdate
    }

    0
