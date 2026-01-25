module Example

open System
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Frank
open Frank.Builder
open Frank.Datastar

// ===========================
// DATASTAR PHILOSOPHY
// ===========================
// 1. HYPERMEDIA FIRST: Send HTML from the server (primary pattern)
// 2. MINIMAL SIGNALS: Only for form inputs and ephemeral UI state
// 3. SERVER AS SOURCE OF TRUTH: Server decides what HTML to display
//
// Examples demonstrate the recommended patterns:
// - patchElements (primary) for HTML updates
// - patchSignals (secondary) only for ephemeral UI state
// ===========================

// Example signal types (use sparingly!)
[<CLIMutable>]
type CounterSignals = { count: int }

[<CLIMutable>]
type SearchSignals = { query: string }

// ===========================
// PRIMARY PATTERN: Patch Elements (Hypermedia-First)
// ===========================

// Example 1: Simple date display using streaming
let displayDate =
    resource "/displayDate" {
        name "DisplayDate"
        datastar (fun ctx ->
            task {
                let today = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
                do! Datastar.patchElements $"""<div id='target'><b>{today}</b></div>""" ctx
            })
    }

// Example 2: Element removal
let removeDate =
    resource "/removeDate" {
        name "RemoveDate"
        datastar (fun ctx ->
            task { do! Datastar.removeElement "#date" ctx })
    }

// Example 3: Server-driven search (no signals needed - uses query string)
let searchItems =
    resource "/search" {
        name "SearchItems"
        datastar (fun ctx ->
            task {
                let query = ctx.Request.Query.["q"].ToString()

                let results =
                    if String.IsNullOrWhiteSpace(query) then
                        []
                    else
                        [ "Apple"; "Banana"; "Cherry"; "Date"; "Elderberry"; "Fig" ]
                        |> List.filter (fun item -> item.Contains(query, StringComparison.OrdinalIgnoreCase))

                let html =
                    if results.IsEmpty then
                        """<div id='search-results'>No results found</div>"""
                    else
                        let items = results |> List.map (fun r -> $"<li>{r}</li>") |> String.concat ""
                        $"""<ul id='search-results'>{items}</ul>"""

                do! Datastar.patchElements html ctx
            })
    }

// Example 4: Paginated list (server sends complete page HTML)
let loadItemsPage =
    resource "/load-items/{page}" {
        name "LoadItemsPage"
        datastar (fun ctx ->
            task {
                let page = ctx.Request.RouteValues.["page"] |> string |> int
                let itemsPerPage = 5
                let allItems = [ 1..20 ] |> List.map (fun i -> $"Item {i}")

                let items =
                    allItems |> List.skip ((page - 1) * itemsPerPage) |> List.truncate itemsPerPage

                let itemsHtml = items |> List.map (fun item -> $"<li>{item}</li>") |> String.concat ""

                let prevPage = page - 1
                let nextPage = page + 1

                let prevButton =
                    if page > 1 then
                        $"""<button data-on-click="@get('/load-items/{prevPage}')">Previous</button>"""
                    else
                        ""

                let nextButton =
                    if page * itemsPerPage < allItems.Length then
                        $"""<button data-on-click="@get('/load-items/{nextPage}')">Next</button>"""
                    else
                        ""

                let html =
                    $"""
                    <div id='items-container'>
                        <ul>{itemsHtml}</ul>
                        <div class='pagination'>
                            {prevButton}
                            <span>Page {page}</span>
                            {nextButton}
                        </div>
                    </div>
                    """

                do! Datastar.patchElements html ctx
            })
    }

// Example 5: Product catalog with filtering (server-side logic)
let loadProducts =
    resource "/products" {
        name "LoadProducts"
        datastar (fun ctx ->
            task {
                let category = ctx.Request.Query.["category"].ToString()

                let products =
                    [ ("Laptop", "electronics", 999)
                      ("Phone", "electronics", 699)
                      ("Desk", "furniture", 299)
                      ("Chair", "furniture", 199)
                      ("Headphones", "electronics", 149) ]

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

                do! Datastar.patchElements $"""<div id='product-list'>{productHtml}</div>""" ctx
            })
    }

// Example 6: Dashboard with multiple sections (multiple patches - primary use case)
// This demonstrates the core streaming capability: sending multiple progressive updates
let loadDashboard =
    resource "/dashboard" {
        name "LoadDashboard"
        datastar (fun ctx ->
            task {
                // Patch header
                let time = DateTime.Now.ToString("HH:mm:ss")

                do!
                    Datastar.patchElements $"""<div id='header'><h2>Dashboard - {time}</h2></div>""" ctx

                do! Task.Delay(100) // Simulate async data fetch

                // Patch stats
                do!
                    Datastar.patchElements
                        """
                    <div id='stats'>
                        <div class='stat'>Users: 1,234</div>
                        <div class='stat'>Orders: 567</div>
                        <div class='stat'>Revenue: $89,012</div>
                    </div>
                    """
                        ctx

                do! Task.Delay(100)

                // Patch recent activity
                do!
                    Datastar.patchElements
                        """
                    <div id='activity'>
                        <h3>Recent Activity</h3>
                        <ul>
                            <li>Order #123 completed</li>
                            <li>New user registered</li>
                            <li>Product updated</li>
                        </ul>
                    </div>
                    """
                        ctx
            })
    }

// Example 7: User profile editor (server validates and returns updated HTML)
let updateProfile =
    resource "/profile/update" {
        name "UpdateProfile"
        datastar (fun ctx ->
            task {
                let name = ctx.Request.Query.["name"].ToString()
                let email = ctx.Request.Query.["email"].ToString()

                // Server-side validation
                let isValid = not (String.IsNullOrWhiteSpace(name)) && email.Contains("@")

                let html =
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
                        """

                do! Datastar.patchElements html ctx
            })
    }

// ===========================
// POST-based streaming example (demonstrates HTTP method flexibility)
// ===========================

// Example 8: Form submission with streaming response (POST method)
let submitForm =
    resource "/form/submit" {
        name "SubmitForm"
        datastar HttpMethods.Post (fun ctx ->
            task {
                // Read signals from POST body
                let! signals = Datastar.tryReadSignals<SearchSignals> ctx

                match signals with
                | ValueSome s ->
                    // Show processing feedback
                    do!
                        Datastar.patchElements
                            $"""<div id='form-status'>Processing query: {s.query}...</div>"""
                            ctx

                    do! Task.Delay(500) // Simulate processing

                    // Show success
                    do!
                        Datastar.patchElements
                            $"""<div id='form-status' class='success'>Query "{s.query}" submitted successfully!</div>"""
                            ctx
                | ValueNone ->
                    do!
                        Datastar.patchElements
                            """<div id='form-status' class='error'>Invalid form data</div>"""
                            ctx
            })
    }

// ===========================
// MINIMAL SIGNAL USAGE: For Ephemeral UI State Only
// ===========================

// Example 9: Simple counter (ephemeral state) - demonstrates signal reading
// Note: patchSignals is secondary to patchElements
let incrementCounter =
    resource "/counter/increment" {
        name "IncrementCounter"
        datastar (fun ctx ->
            task {
                let! signals = Datastar.tryReadSignals<CounterSignals> ctx

                match signals with
                | ValueSome s ->
                    let newCount = s.count + 1
                    let signalsJson = JsonSerializer.Serialize({| count = newCount |})
                    // Update signal (secondary pattern)
                    do! Datastar.patchSignals signalsJson ctx
                    // Also send HTML with the count (primary pattern - preferred)
                    do! Datastar.patchElements $"""<div id='counter-display'>Count: {newCount}</div>""" ctx
                | ValueNone ->
                    do! Datastar.patchElements """<div id='error'>Invalid counter state</div>""" ctx
            })
    }

// Example 10: Form validation feedback (signals for input state only)
let validateForm =
    resource "/validate-form" {
        name "ValidateForm"
        datastar (fun ctx ->
            task {
                let! signals = Datastar.tryReadSignals<SearchSignals> ctx

                match signals with
                | ValueSome s ->
                    // Validate and send back HTML feedback (not signal manipulation)
                    let feedbackHtml =
                        if String.IsNullOrWhiteSpace(s.query) then
                            """<div id='validation' class='error'>Query cannot be empty</div>"""
                        elif s.query.Length < 3 then
                            """<div id='validation' class='warning'>Query too short (min 3 chars)</div>"""
                        else
                            """<div id='validation' class='success'>Valid query</div>"""

                    do! Datastar.patchElements feedbackHtml ctx
                | ValueNone ->
                    do!
                        Datastar.patchElements
                            """<div id='validation' class='error'>Invalid data</div>"""
                            ctx
            })
    }

// ===========================
// Application Setup using Frank's webHost builder
// ===========================

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        plug StaticFileExtensions.UseStaticFiles

        // Primary pattern examples (patchElements - hypermedia-first)
        resource displayDate
        resource removeDate
        resource searchItems
        resource loadItemsPage
        resource loadProducts
        resource loadDashboard
        resource updateProfile

        // POST-based streaming example
        resource submitForm

        // Secondary pattern examples (signal reading - use sparingly)
        resource incrementCounter
        resource validateForm
    }

    0
