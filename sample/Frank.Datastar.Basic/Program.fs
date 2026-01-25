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
type FormSignals = { email: string; password: string }

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
            task { do! Datastar.removeElement "#target" ctx })
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

// Example 5: User greeting (server decides HTML based on input)
let greetUser =
    resource "/greet" {
        name "GreetUser"
        datastar (fun ctx ->
            task {
                let name = ctx.Request.Query.["name"].ToString()

                let html =
                    if String.IsNullOrWhiteSpace(name) then
                        """<div id='greeting'>Please enter your name!</div>"""
                    else
                        $"""<div id='greeting'>Hello, <strong>{name}</strong>! Welcome to Frank.Datastar.</div>"""

                do! Datastar.patchElements html ctx
            })
    }

// Example 6: Product catalog with filtering (server-side logic)
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

                do! Datastar.patchElements $"""<div id='product-grid'>{productHtml}</div>""" ctx
            })
    }

// Example 7: Form validation with signals (POST method)
let validateForm =
    resource "/validate" {
        name "ValidateForm"
        datastar HttpMethods.Post (fun ctx ->
            task {
                let! signals = Datastar.tryReadSignals<FormSignals> ctx

                match signals with
                | ValueSome form ->
                    let errors = ResizeArray<string>()

                    if String.IsNullOrWhiteSpace(form.email) then
                        errors.Add("Email is required")
                    elif not (form.email.Contains("@")) then
                        errors.Add("Email must be valid")

                    if String.IsNullOrWhiteSpace(form.password) then
                        errors.Add("Password is required")
                    elif form.password.Length < 6 then
                        errors.Add("Password must be at least 6 characters")

                    let html =
                        if errors.Count = 0 then
                            """<div id='validation-result' class='success'>
                                ✓ Form is valid! Ready to submit.
                            </div>"""
                        else
                            let errorList = errors |> Seq.map (fun e -> $"<li>{e}</li>") |> String.concat ""
                            $"""<div id='validation-result' class='error'>
                                <ul>{errorList}</ul>
                            </div>"""

                    do! Datastar.patchElements html ctx
                | ValueNone ->
                    do! Datastar.patchElements """<div id='validation-result' class='error'>Invalid form data</div>""" ctx
            })
    }

// Example 8: Real-time clock (server sends updated HTML)
let clock =
    resource "/clock" {
        name "Clock"
        datastar (fun ctx ->
            task {
                let time = DateTime.Now.ToString("HH:mm:ss")
                do! Datastar.patchElements $"""<div id='clock'>{time}</div>""" ctx
            })
    }

// Example 9: Dashboard refresh with multiple sections (multiple patches - primary use case)
let dashboardRefresh =
    resource "/dashboard/refresh" {
        name "DashboardRefresh"
        datastar (fun ctx ->
            task {
                // Patch stats section
                let users = Random.Shared.Next(1000, 2000)
                let orders = Random.Shared.Next(100, 500)
                let revenue = Random.Shared.Next(50000, 100000)
                let statsHtml = $"""<div id='stats'>
                    <div class='stat'>Users: {users}</div>
                    <div class='stat'>Orders: {orders}</div>
                    <div class='stat'>Revenue: ${revenue}</div>
                </div>"""
                do! Datastar.patchElements statsHtml ctx

                do! Task.Delay(200) // Simulate async data fetch

                // Patch activity section
                let order1 = Random.Shared.Next(1000, 9999)
                let product1 = Random.Shared.Next(100, 999)
                let activities = [
                    $"Order #{order1} completed"
                    "New user registered"
                    $"Product #{product1} updated"
                ]
                let activityHtml = activities |> List.map (fun a -> $"<li>{a}</li>") |> String.concat ""

                do! Datastar.patchElements $"""<div id='activity'>
                    <h4>Recent Activity</h4>
                    <ul>{activityHtml}</ul>
                </div>""" ctx
            })
    }

// Example 10: Profile view (server decides UI based on user ID)
let viewProfile =
    resource "/profile/{id}" {
        name "ViewProfile"
        datastar (fun ctx ->
            task {
                let userId = ctx.Request.RouteValues.["id"] |> string |> int

                // Simulate different permissions based on user ID
                let canEdit = userId = 123

                let html =
                    if canEdit then
                        $"""<div id='profile'>
                            <h3>User Profile (ID: {userId})</h3>
                            <p>Name: John Doe</p>
                            <p>Email: john@example.com</p>
                            <button data-on-click="@get('/profile/{userId}/edit')">Edit Profile</button>
                        </div>"""
                    else
                        $"""<div id='profile'>
                            <h3>User Profile (ID: {userId})</h3>
                            <p>Name: Jane Smith</p>
                            <p>Email: jane@example.com</p>
                            <p><em>View only - no edit permission</em></p>
                        </div>"""

                do! Datastar.patchElements html ctx
            })
    }

// ===========================
// MINIMAL SIGNAL USAGE: Counter (Ephemeral UI State)
// ===========================

let increment =
    resource "/increment" {
        name "Increment"
        datastar HttpMethods.Post (fun ctx ->
            task {
                let! signals = Datastar.tryReadSignals<CounterSignals> ctx
                match signals with
                | ValueSome s ->
                    let newCount = s.count + 1
                    do! Datastar.patchSignals (JsonSerializer.Serialize({| count = newCount |})) ctx
                | ValueNone ->
                    do! Datastar.patchSignals """{"count": 1}""" ctx
            })
    }

let decrement =
    resource "/decrement" {
        name "Decrement"
        datastar HttpMethods.Post (fun ctx ->
            task {
                let! signals = Datastar.tryReadSignals<CounterSignals> ctx
                match signals with
                | ValueSome s ->
                    let newCount = max 0 (s.count - 1)
                    do! Datastar.patchSignals (JsonSerializer.Serialize({| count = newCount |})) ctx
                | ValueNone ->
                    do! Datastar.patchSignals """{"count": 0}""" ctx
            })
    }

let reset =
    resource "/reset" {
        name "Reset"
        datastar HttpMethods.Post (fun ctx ->
            task {
                do! Datastar.patchSignals """{"count": 0}""" ctx
            })
    }

// ===========================
// Application Setup using Frank's webHost builder
// ===========================

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        // UseDefaultFiles must come before UseStaticFiles to serve index.html at "/"
        plug DefaultFilesExtensions.UseDefaultFiles
        plug StaticFileExtensions.UseStaticFiles

        // Primary pattern examples (patchElements - hypermedia-first)
        resource displayDate
        resource removeDate
        resource searchItems
        resource loadItemsPage
        resource greetUser
        resource loadProducts
        resource clock
        resource dashboardRefresh
        resource viewProfile

        // Form validation (POST with signal reading)
        resource validateForm

        // Counter examples (minimal signal usage)
        resource increment
        resource decrement
        resource reset
    }

    0
