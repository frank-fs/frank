module Example

open System
open System.Text.Json
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Frank
open Frank.Builder
open Frank.Datastar
open StarFederation.Datastar.FSharp

// ===========================
// DATASTAR PHILOSOPHY
// ===========================
// 1. HYPERMEDIA FIRST: Send HTML from the server (primary pattern)
// 2. MINIMAL SIGNALS: Only for form inputs and ephemeral UI state
// 3. SERVER AS SOURCE OF TRUTH: Server decides what HTML to display
//
// Examples are organized to demonstrate the recommended patterns first,
// followed by minimal signal usage examples.
// ===========================

// Example signal types (use sparingly!)
[<CLIMutable>]
type CounterSignals = {
    count: int
}

[<CLIMutable>]
type SearchSignals = {
    query: string
}

// ===========================
// PRIMARY PATTERN: Patch Elements (Hypermedia-First)
// ===========================

// Example 1: Simple single element patching
let displayDate =
    resource "/displayDate" {
        name "DisplayDate"
        patchElements (fun ctx -> 
            let today = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            $"""<div id='target'><b>{today}</b></div>""")
    }

// Example 2: Element removal
let removeDate =
    resource "/removeDate" {
        name "RemoveDate"
        removeElement "#date"
    }

// Example 3: Server-driven search (no signals needed - uses query string)
let searchItems =
    resource "/search" {
        name "SearchItems"
        patchElements (fun ctx ->
            let query = ctx.Request.Query.["q"].ToString()
            // Server filters and returns HTML
            let results = 
                if String.IsNullOrWhiteSpace(query) then
                    []
                else
                    ["Apple"; "Banana"; "Cherry"; "Date"; "Elderberry"; "Fig"]
                    |> List.filter (fun item -> item.Contains(query, StringComparison.OrdinalIgnoreCase))
            
            let html = 
                if results.IsEmpty then
                    """<div id='search-results'>No results found</div>"""
                else
                    let items = results |> List.map (fun r -> $"<li>{r}</li>") |> String.concat ""
                    $"""<ul id='search-results'>{items}</ul>"""
            
            html)
    }

// Example 4: Paginated list (server sends complete page HTML)
let loadItemsPage =
    resource "/load-items/{page}" {
        name "LoadItemsPage"
        patchElements (fun ctx ->
            let page = ctx.Request.RouteValues.["page"] |> string |> int
            let itemsPerPage = 5
            let allItems = [1..20] |> List.map (fun i -> $"Item {i}")
            
            let items = 
                allItems 
                |> List.skip ((page - 1) * itemsPerPage)
                |> List.truncate itemsPerPage
            
            let itemsHtml = items |> List.map (fun item -> $"<li>{item}</li>") |> String.concat ""
            
            let prevButton = 
                if page > 1 then
                    $"""<button data-on-click="@get('/load-items/{page - 1}')">Previous</button>"""
                else
                    ""
            
            let nextButton = 
                if page * itemsPerPage < allItems.Length then
                    $"""<button data-on-click="@get('/load-items/{page + 1}')">Next</button>"""
                else
                    ""
            
            $"""
            <div id='items-container'>
                <ul>{itemsHtml}</ul>
                <div class='pagination'>
                    {prevButton}
                    <span>Page {page}</span>
                    {nextButton}
                </div>
            </div>
            """)
    }

// Example 5: Product catalog with filtering (server-side logic)
let loadProducts =
    resource "/products" {
        name "LoadProducts"
        patchElements (fun ctx ->
            let category = ctx.Request.Query.["category"].ToString()
            
            let products = 
                [
                    ("Laptop", "electronics", 999)
                    ("Phone", "electronics", 699)
                    ("Desk", "furniture", 299)
                    ("Chair", "furniture", 199)
                    ("Headphones", "electronics", 149)
                ]
            
            let filtered = 
                if String.IsNullOrWhiteSpace(category) then
                    products
                else
                    products |> List.filter (fun (_, cat, _) -> cat = category)
            
            let productHtml = 
                filtered 
                |> List.map (fun (name, cat, price) -> 
                    $"""<div class='product'>
                        <h3>{name}</h3>
                        <p>Category: {cat}</p>
                        <p>Price: ${price}</p>
                    </div>""")
                |> String.concat ""
            
            $"""<div id='product-list'>{productHtml}</div>""")
    }

// Example 6: Dashboard with multiple sections (multiple patches)
let loadDashboard =
    resource "/dashboard" {
        name "LoadDashboard"
        datastar (fun ctx -> task {
            // Multiple element patches - stream starts once automatically
            
            // Patch header
            let headerHtml = $"""<div id='header'><h2>Dashboard - {DateTime.Now.ToString("HH:mm:ss")}</h2></div>"""
            do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, headerHtml)
            
            // Patch stats
            let statsHtml = """
                <div id='stats'>
                    <div class='stat'>Users: 1,234</div>
                    <div class='stat'>Orders: 567</div>
                    <div class='stat'>Revenue: $89,012</div>
                </div>
                """
            do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, statsHtml)
            
            // Patch recent activity
            let activityHtml = """
                <div id='activity'>
                    <h3>Recent Activity</h3>
                    <ul>
                        <li>Order #123 completed</li>
                        <li>New user registered</li>
                        <li>Product updated</li>
                    </ul>
                </div>
                """
            do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, activityHtml)
        })
    }

// Example 7: User profile editor (server validates and returns updated HTML)
let updateProfile =
    resource "/profile/update" {
        name "UpdateProfile"
        patchElements (fun ctx ->
            let name = ctx.Request.Query.["name"].ToString()
            let email = ctx.Request.Query.["email"].ToString()
            
            // Server-side validation
            let isValid = not (String.IsNullOrWhiteSpace(name)) && email.Contains("@")
            
            if isValid then
                $"""
                <div id='profile' class='success'>
                    <h3>Profile Updated</h3>
                    <p>Name: {name}</p>
                    <p>Email: {email}</p>
                </div>
                """
            else
                """
                <div id='profile' class='error'>
                    <h3>Validation Error</h3>
                    <p>Please provide valid name and email</p>
                </div>
                """)
    }

// ===========================
// MINIMAL SIGNAL USAGE: For Ephemeral UI State Only
// ===========================

// Example 8: Form validation feedback (signals for input state only)
let validateForm =
    resource "/validate-form" {
        name "ValidateForm"
        readSignals (fun ctx signals ->
            task {
                match signals with
                | ValueSome (s: SearchSignals) ->
                    // Validate and send back HTML feedback (not signal manipulation)
                    let feedbackHtml = 
                        if String.IsNullOrWhiteSpace(s.query) then
                            """<div id='validation' class='error'>Query cannot be empty</div>"""
                        elif s.query.Length < 3 then
                            """<div id='validation' class='warning'>Query too short (min 3 chars)</div>"""
                        else
                            """<div id='validation' class='success'>Valid query</div>"""
                    
                    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, feedbackHtml)
                | ValueNone ->
                    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, 
                        """<div id='validation' class='error'>Invalid data</div>""")
            })
    }

// Example 9: Simple counter (ephemeral state)
let incrementCounter =
    resource "/counter/increment" {
        name "IncrementCounter"
        transformSignals (fun ctx (signals: CounterSignals) ->
            task {
                // Only use signals for temporary UI state like counters
                let newCount = signals.count + 1
                return {| count = newCount |}
            })
    }

// Example 10: Counter with display (combines signal + HTML)
let incrementCounterWithDisplay =
    resource "/counter/increment-display" {
        name "IncrementCounterWithDisplay"
        datastar (fun ctx -> task {
            let signals = ServerSentEventGenerator.TryReadSignals<CounterSignals>(ctx.Request)
            
            match signals with
            | ValueSome s ->
                let newCount = s.count + 1
                
                // Update signal
                let signalsJson = JsonSerializer.Serialize({| count = newCount |})
                do! ServerSentEventGenerator.PatchSignalsAsync(ctx.Response, signalsJson)
                
                // Also send HTML with the count (preferred approach)
                let displayHtml = $"""<div id='counter-display'>Count: {newCount}</div>"""
                do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, displayHtml)
            | ValueNone ->
                do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, 
                    """<div id='error'>Invalid counter state</div>""")
        })
    }

// ===========================
// Application Setup
// ===========================

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder(args)
    
    let app = builder.Build()
    
    app.UseStaticFiles() |> ignore
    
    // Register all routes
    app.UseResource(displayDate)
    app.UseResource(removeDate)
    app.UseResource(searchItems)
    app.UseResource(loadItemsPage)
    app.UseResource(loadProducts)
    app.UseResource(loadDashboard)
    app.UseResource(updateProfile)
    app.UseResource(validateForm)
    app.UseResource(incrementCounter)
    app.UseResource(incrementCounterWithDisplay)
    
    // Serve index page
    app.MapGet("/", Func<string>(fun () -> System.IO.File.ReadAllText("wwwroot/index.html"))) |> ignore
    
    app.Run()
    0
