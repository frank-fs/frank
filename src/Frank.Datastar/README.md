# Frank.Datastar

An F# library that integrates [Datastar](https://github.com/starfederation/datastar)'s Server-Sent Events (SSE) capabilities with the [Frank](https://github.com/frank-fs/frank) web framework through idiomatic computation expression builders.

## Features

- **Seamless Integration**: Extends Frank's resource builder with Datastar-specific operations
- **Type-Safe**: Leverages F# type system for safe signal handling
- **Computation Expressions**: Provides idiomatic F# computation expression syntax
- **Full Datastar SDK Support**: Implements all core Datastar operations:
  - Element patching and removal
  - Signal (state) management
  - JavaScript execution
  - SSE streaming
- **Multiple API Styles**: Use helper functions, computation expressions, or direct API calls

## Installation

```bash
dotnet add package Frank.Datastar
```

Or add to your `.fsproj`:

```xml
<PackageReference Include="Frank.Datastar" Version="1.0.0" />
```

## Quick Start

### Hypermedia-First: Patching Elements (Primary Pattern)

The core Datastar pattern is sending HTML from the server. This keeps the server as the source of truth.

```fsharp
open Frank
open Frank.Builder
open Frank.Datastar

let displayTime =
    resource "/time" {
        name "DisplayTime"

        get (fun _ ->
            let time = DateTime.Now.ToString("HH:mm:ss")
            patchElements $"""<div id="time">{time}</div>""")
    }

let searchResults =
    resource "/search" {
        name "Search"

        get (fun ctx ->
            patchElements (fun httpCtx -> task {
                let query = httpCtx.Request.Query.["q"].ToString()
                let! results = searchDatabaseAsync query

                let html = results
                           |> List.map (fun r -> $"""<div class="result">{r.title}</div>""")
                           |> String.concat ""

                return $"""<div id="results">{html}</div>"""
            }))
    }
```

### Minimal Signals for Form Inputs (Supporting Pattern)

Use signals sparingly, primarily for form inputs and ephemeral client-side state:

```fsharp
// Client HTML: <input data-bind:searchTerm />

let liveSearch =
    resource "/live-search" {
        name "LiveSearch"

        post (fun _ ->
            readSignals<{| searchTerm: string |}> (fun ctx signalsOpt -> task {
                match signalsOpt with
                | ValueSome signals ->
                    let! results = searchAsync signals.searchTerm
                    let html = renderResultsHtml results
                    do! Datastar.patchElements html ctx
                | ValueNone -> ()
            }))
    }
```

## API Reference

### Datastar Philosophy

**Hypermedia First**: The primary pattern in Datastar is sending HTML from the server. The server is the source of truth, and HTML is sent to update the UI.

**Minimal Signals**: Signals should be used sparingly, primarily for:

- Form input bindings (`<input data-bind:field>`)
- Ephemeral UI state (toggle switches, tabs)
- Passing small amounts of data to the server

**What to avoid**:

- ❌ Storing application data in signals
- ❌ Managing complex state on the client
- ❌ Using signals as a database
- ❌ Replacing HTML updates with signal updates

### Operation Priority

1. **Primary**: `patchElements` - Send HTML to update the UI
2. **Supporting**: `readSignals` - Read form inputs to decide what HTML to send
3. **Rare**: `patchSignals` - Update minimal client state (counters, flags)
4. **Special**: `executeScript`, `removeElement` - For specific use cases

### Computation Expression Operations

#### `datastar`

Execute a custom Datastar operation with full control:

```fsharp
resource "/custom" {
    get (fun _ ->
        datastar (fun ctx -> task {
            do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, "<div>Custom</div>")
            do! ServerSentEventGenerator.ExecuteScriptAsync(ctx.Response, "console.log('Done')", false)
        }))
}
```

#### `patchElements`

Update HTML elements in the DOM:

```fsharp
// Static HTML
patchElements "<div id='target'>Hello</div>"

// Context-aware HTML
patchElements (fun ctx -> $"<div>User: {ctx.User.Identity.Name}</div>")

// Async HTML generation
patchElements (fun ctx -> task {
    let! data = fetchDataAsync()
    return $"<div>{data}</div>"
})
```

#### `removeElement`

Remove an element from the DOM by selector:

```fsharp
// Static selector
removeElement "#target"

// Dynamic selector
removeElement (fun ctx -> $"#item-{ctx.Request.Query.["id"]}")
```

#### `removeFragments`

Remove multiple elements matching a selector:

```fsharp
removeFragments ".temp-item"
```

#### `patchSignals`

Update client-side state (signals):

```fsharp
// Static JSON
patchSignals """{"count": 42}"""

// Context-aware JSON
patchSignals (fun ctx ->
    JsonSerializer.Serialize({ count = int ctx.Request.Query.["value"] }))

// Async JSON generation
patchSignals (fun ctx -> task {
    let! data = computeDataAsync()
    return JsonSerializer.Serialize(data)
})
```

#### `executeScript`

Execute JavaScript in the browser:

```fsharp
// Without auto-remove
executeScript "console.log('Hello')"

// With auto-remove
executeScript "alert('Done')" true
```

#### `readSignals`

Read and process signals from the request:

```fsharp
readSignals<MySignals> (fun ctx signalsOpt ->
    match signalsOpt with
    | ValueSome signals ->
        // Process signals
        patchElements $"<div>{signals.data}</div>" ctx
    | ValueNone ->
        Task.CompletedTask)
```

#### `transformSignals`

Read signals, transform them, and patch back in one operation:

```fsharp
transformSignals<InputType, OutputType>
    (fun input -> { output = input.value * 2 })
    JsonSerializer.Serialize
```

### Helper Functions

The `Datastar` module provides helper functions for common operations:

```fsharp
open Frank.Datastar

// In a resource handler
get (fun ctx -> task {
    do! Datastar.patchElements "<div>Hello</div>" ctx
    do! Datastar.removeElement "#temp" ctx
    do! Datastar.patchSignals """{"updated": true}""" ctx
    do! Datastar.executeScript "console.log('Done')" false ctx
})
```

## Complete Examples

### Hypermedia-First Patterns

The following examples demonstrate the primary Datastar pattern: sending HTML from the server.

#### Dynamic Content Updates

```fsharp
let loadProducts =
    resource "/products" {
        name "LoadProducts"

        get (fun _ ->
            patchElements (fun ctx -> task {
                let category = ctx.Request.Query.["category"].ToString()
                let! products = getProductsByCategoryAsync category

                let html = products |> List.map (fun p ->
                    $"""
                    <div class="product-card">
                        <h3>{p.name}</h3>
                        <p>${p.price:F2}</p>
                        <button data-on:click="@post('/cart/add/{p.id}')">
                            Add to Cart
                        </button>
                    </div>
                    """) |> String.concat ""

                return $"""<div id="product-grid">{html}</div>"""
            }))
    }
```

#### Paginated Lists

```fsharp
let loadPage =
    resource "/items/page/{page}" {
        name "LoadPage"

        get (fun _ ->
            patchElements (fun ctx -> task {
                let page = int ctx.Request.RouteValues.["page"]
                let! items = getItemsPageAsync page

                let itemsHtml = items |> List.map (fun item ->
                    $"""<li class="item">{item.name}</li>""") |> String.concat ""

                let paginationHtml =
                    $"""
                    <div class="pagination">
                        <button data-on:click="@get('/items/page/{page - 1}')"
                                {if page = 1 then "disabled" else ""}>
                            Previous
                        </button>
                        <span>Page {page}</span>
                        <button data-on:click="@get('/items/page/{page + 1}')">
                            Next
                        </button>
                    </div>
                    """

                return $"""
                    <div id="items-container">
                        <ul>{itemsHtml}</ul>
                        {paginationHtml}
                    </div>
                    """
            }))
    }
```

#### Search with Server-Side Filtering

```fsharp
let searchItems =
    resource "/search" {
        name "SearchItems"

        get (fun _ ->
            patchElements (fun ctx -> task {
                let query = ctx.Request.Query.["q"].ToString()
                let! results = searchDatabaseAsync query

                if results.IsEmpty then
                    return """<div id="results">No results found</div>"""
                else
                    let html = results |> List.map (fun r ->
                        $"""
                        <div class="result">
                            <h4>{r.title}</h4>
                            <p>{r.snippet}</p>
                        </div>
                        """) |> String.concat ""

                    return $"""<div id="results">{html}</div>"""
            }))
    }
```

#### Multi-Element Updates

```fsharp
let updateDashboard =
    resource "/dashboard/refresh" {
        name "RefreshDashboard"

        get (fun _ ->
            datastar (fun ctx -> task {
                // Update multiple independent sections
                let! stats = getStatsAsync()
                let statsHtml = $"""
                    <div id="stats">
                        <div>Users: {stats.users}</div>
                        <div>Revenue: ${stats.revenue:F2}</div>
                    </div>
                    """
                do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, statsHtml)

                let! recentActivity = getRecentActivityAsync()
                let activityHtml = recentActivity |> List.map (fun a ->
                    $"""<li>{a.message}</li>""") |> String.concat ""
                do! ServerSentEventGenerator.PatchElementsAsync(
                    ctx.Response,
                    $"""<ul id="activity">{activityHtml}</ul>""")
            }))
    }
```

### Minimal Signal Usage (When Needed)

Use signals only for form inputs and ephemeral UI state. The server should remain the source of truth.

#### Form Input with Live Feedback

```fsharp
[<CLIMutable>]
type SearchForm = { query: string }

let liveSearch =
    resource "/live-search" {
        name "LiveSearch"

        post (fun _ ->
            readSignals<SearchForm> (fun ctx signalsOpt -> task {
                match signalsOpt with
                | ValueSome form when not (String.IsNullOrWhiteSpace(form.query)) ->
                    let! results = searchAsync form.query
                    let html = renderSearchResults results
                    do! Datastar.patchElements html ctx
                | _ ->
                    do! Datastar.patchElements """<div id="results"></div>""" ctx
            }))
    }
```

#### Form Validation (Server-Decides)

```fsharp
[<CLIMutable>]
type RegistrationForm = {
    email: string
    password: string
}

let validateRegistration =
    resource "/validate" {
        name "ValidateRegistration"

        post (fun _ ->
            readSignals<RegistrationForm> (fun ctx signalsOpt -> task {
                match signalsOpt with
                | ValueSome form ->
                    let errors = validateForm form

                    if errors.IsEmpty then
                        // Send success HTML
                        let html = """
                            <div id="validation-result" class="success">
                                Form is valid! <button data-on:click="@post('/register')">
                                    Submit
                                </button>
                            </div>
                            """
                        do! Datastar.patchElements html ctx
                    else
                        // Send error HTML
                        let errorsHtml = errors |> List.map (fun e ->
                            $"""<li class="error">{e}</li>""") |> String.concat ""
                        do! Datastar.patchElements
                            $"""<div id="validation-result">
                                <ul>{errorsHtml}</ul>
                            </div>""" ctx
                | ValueNone -> ()
            }))
    }
```

#### Shopping Cart (Counter Example)

```fsharp
[<CLIMutable>]
type CartSignals = { itemCount: int }

let updateCartCount =
    resource "/cart/add/{id}" {
        name "AddToCart"

        post (fun _ ->
            readSignals<CartSignals> (fun ctx signalsOpt -> task {
                let productId = ctx.Request.RouteValues.["id"] |> string
                let! added = addToCartAsync productId

                if added then
                    // Update the cart display with HTML (primary)
                    let! cart = getCartAsync()
                    let html = renderCartSummary cart
                    do! Datastar.patchElements html ctx

                    // Also update the counter signal (minimal)
                    do! Datastar.patchSignals
                        (JsonSerializer.Serialize({| itemCount = cart.items.Length |}))
                        ctx
                else
                    do! Datastar.patchElements
                        """<div id="cart-error">Could not add item</div>"""
                        ctx
            }))
    }
```

### Real-World Pattern: Server-Driven UI

```fsharp
// The server decides what HTML to send based on state
let loadUserProfile =
    resource "/profile/{userId}" {
        name "LoadProfile"

        get (fun _ ->
            patchElements (fun ctx -> task {
                let userId = ctx.Request.RouteValues.["userId"] |> string |> int
                let! user = getUserAsync userId
                let! canEdit = canUserEditAsync ctx.User userId

                // Server decides what UI to show
                let html =
                    if canEdit then
                        $"""
                        <div id="profile">
                            <h2>{user.name}</h2>
                            <p>{user.bio}</p>
                            <button data-on:click="@get('/profile/{userId}/edit')">
                                Edit Profile
                            </button>
                        </div>
                        """
                    else
                        $"""
                        <div id="profile">
                            <h2>{user.name}</h2>
                            <p>{user.bio}</p>
                        </div>
                        """

                return html
            }))
    }
```

## Architecture

Frank.Datastar is built on top of:

1. **Frank**: Provides the computation expression framework for defining HTTP resources
2. **StarFederation.Datastar.FSharp**: Implements the core Datastar SDK functionality
3. **ASP.NET Core**: The underlying web framework

The library extends Frank's `ResourceBuilder` with custom operations that automatically handle:

- SSE stream initialization
- Response header configuration
- Serialization and deserialization
- Error handling

## Best Practices

### 1. Hypermedia First - Send HTML, Not State

The server should send HTML as the primary mechanism. Avoid managing complex state on the client.

```fsharp
// ✅ Good: Server sends HTML
let showProducts =
    resource "/products" {
        get (fun _ -> patchElements (fun _ -> task {
            let! products = getProductsAsync()
            return renderProductGrid products
        }))
    }

// ❌ Avoid: Managing complex state in signals
let showProductsWrong =
    resource "/products" {
        get (fun _ -> patchSignals (fun _ -> task {
            let! products = getProductsAsync()
            return JsonSerializer.Serialize(products) // Don't do this
        }))
    }
```

### 2. Use Signals Minimally

Signals are for form inputs and ephemeral UI state only. The server remains the source of truth.

```fsharp
// ✅ Good: Signal for form input, server renders result
readSignals<{| query: string |}> (fun ctx signalsOpt ->
    match signalsOpt with
    | ValueSome s ->
        let! results = searchAsync s.query
        Datastar.patchElements (renderResults results) ctx
    | ValueNone -> Task.CompletedTask
)

// ❌ Avoid: Storing data in signals
patchSignals (JsonSerializer.Serialize({|
    users = [...];
    products = [...];
    orders = [...]
|}))
```

### 3. Server Decides UI

Let the server control what HTML to display based on application state.

```fsharp
let loadContent =
    resource "/content" {
        get (fun _ -> patchElements (fun ctx -> task {
            let! user = getCurrentUserAsync ctx

            // Server decides what to show
            if user.IsAdmin then
                return """<div id="content">Admin Dashboard</div>"""
            else
                return """<div id="content">User Dashboard</div>"""
        }))
    }
```

### 4. Handle Missing Signals Gracefully

Always handle the `ValueNone` case when reading signals:

```fsharp
readSignals<MySignals> (fun ctx signalsOpt ->
    match signalsOpt with
    | ValueSome signals -> // Process signals
    | ValueNone -> Task.CompletedTask // Handle missing signals
)
```

### 5. Use `transformSignals` for Simple Cases

For simple read-transform-write operations on form state:

```fsharp
transformSignals<Input, Output> transform serialize
```

### 6. Compose Operations

Use the `datastar` operation for complex multi-step operations:

```fsharp
datastar (fun ctx -> task {
    do! operation1 ctx
    do! operation2 ctx
    do! operation3 ctx
})
```

### 7. Progressive Enhancement

Load critical content first, then enhance:

```fsharp
datastar (fun ctx -> task {
    // Send essential content immediately
    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, essentialHtml)

    // Then enhance with additional content
    let! extraContent = loadExtraContentAsync()
    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, extraContent)
})
```

### 8. Keep Responses Focused

Each endpoint should update specific parts of the page:

```fsharp
// Good: Focused update
patchElements """<div id="search-results">...</div>"""

// Avoid: Updating unrelated elements
datastar (fun ctx -> task {
    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, searchResults)
    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, navigationMenu)
    do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, footer)
})
```

## Performance Considerations

- SSE streams are automatically flushed after each operation
- Signal deserialization uses `System.Text.Json` for performance
- Large HTML payloads are streamed efficiently
- Consider batching multiple updates when possible

## Contributing

Contributions are welcome! Please ensure:

1. Code follows F# conventions
2. All public APIs are documented
3. Examples are updated for new features
4. Tests are included (when applicable)

## License

MIT License - see LICENSE file for details

## Hox Integration

Frank.Datastar works seamlessly with [Hox](https://github.com/AngelMunoz/Hox), an async HTML rendering library for F#. Hox provides a powerful DSL for building HTML with support for async operations, making it perfect for complex server-side rendering scenarios.

### Why Use Hox with Frank.Datastar?

- **Async-First**: Hox natively supports async/task operations, perfect for database queries and API calls
- **Composable**: Build reusable HTML components as F# functions
- **Type-Safe**: Full F# type inference and compile-time checking
- **Streaming**: Support for `IAsyncEnumerable<string>` for progressive rendering
- **CSS Selector Syntax**: Intuitive `h("div#id.class[attr=value]")` syntax

### Installation

```bash
dotnet add package Hox
```

### Basic Example with Hox

```fsharp
open Hox
open Hox.Rendering
open Frank.Datastar

// Define a component
let userCard (user: User) =
    h("div.card",
        h("div.card-content",
            h("h3.title", text user.name),
            h("p.subtitle", text user.email)
        )
    )

// Use in a Frank resource with async data
let loadUsers =
    resource "/users" {
        name "LoadUsers"

        get (fun _ ->
            patchElements (fun ctx -> task {
                // Fetch data asynchronously
                let! users = fetchUsersFromDb()

                // Render with Hox
                let node = h("div#user-list",
                    fragment [
                        for user in users do
                            userCard user
                    ]
                )

                // Convert to HTML string
                let! html = Render.AsStringAsync(node)
                return html
            }))
    }
```

### Complex Multi-Fragment Example

```fsharp
// Dashboard component with async aggregation
let dashboardStats () = task {
    let! stats = getStatsFromDb()

    return h("div.stats-grid",
        h("div.stat-card",
            h("h4", text "Total Users"),
            h("p.value", text (string stats.totalUsers))
        ),
        h("div.stat-card",
            h("h4", text "Revenue"),
            h("p.value", text $"${stats.revenue:F2}")
        )
    )
}

// Product grid with filtering
let productGrid (filters: FilterOptions) = task {
    let! products = queryProductsAsync filters

    return h("div#products.grid",
        fragment [
            for product in products do
                h("div.product-card",
                    h("img[src=" + product.imageUrl + "]"),
                    h("h3", text product.name),
                    h("p.price", text $"${product.price:F2}"),
                    h("button.buy-btn[data-on:click=@post('/add-cart')]",
                        text "Add to Cart"
                    )
                )
        ]
    )
}

// Resource that sends multiple fragments
let refreshDashboard =
    resource "/refresh" {
        name "RefreshDashboard"

        get (fun _ ->
            datastar (fun ctx -> task {
                // Render multiple sections concurrently
                let! statsNode = dashboardStats()
                let! statsHtml = Render.AsStringAsync(statsNode)

                let! productsNode = productGrid defaultFilters
                let! productsHtml = Render.AsStringAsync(productsNode)

                // Send both fragments via SSE
                do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, statsHtml)
                do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, productsHtml)
            }))
    }
```

### Hox Features for Complex Rendering

#### 1. Fragment Composition

```fsharp
let tableRow (cells: string list) =
    h("tr",
        fragment [
            for cell in cells do
                h("td", text cell)
        ]
    )

let dataTable (headers: string list) (rows: string list list) =
    h("table.table",
        h("thead",
            h("tr",
                fragment [
                    for header in headers do
                        h("th", text header)
                ]
            )
        ),
        h("tbody",
            fragment [
                for row in rows do
                    tableRow row
            ]
        )
    )
```

#### 2. Async Component Loading

```fsharp
let asyncUserList (count: int) = task {
    // Async data fetch
    let! users = fetchUsersAsync count

    // Async image processing
    let! processedUsers = users |> List.map (fun u -> task {
        let! avatar = processAvatarAsync u.avatarUrl
        return { u with processedAvatar = avatar }
    }) |> Task.WhenAll

    return h("div.user-list",
        fragment [
            for user in processedUsers do
                h("div.user-card",
                    h("img[src=" + user.processedAvatar + "]"),
                    h("span", text user.name)
                )
        ]
    )
}
```

#### 3. Streaming Large Outputs

```fsharp
let streamLargeReport =
    resource "/report" {
        get (fun _ ->
            datastar (fun ctx -> task {
                // Create a large report with many sections
                let! reportNode = generateLargeReportAsync()

                // Stream render (useful for very large HTML)
                let stream = Render.AsAsyncEnumerable(reportNode)
                let mutable html = ""

                for chunk in stream do
                    let! chunkStr = chunk
                    html <- html + chunkStr

                do! ServerSentEventGenerator.PatchElementsAsync(ctx.Response, html)
            }))
    }
```

#### 4. Conditional Rendering

```fsharp
let conditionalContent (isLoggedIn: bool) (user: User option) =
    if isLoggedIn then
        match user with
        | Some u ->
            h("div.user-panel",
                h("span", text $"Welcome, {u.name}"),
                h("button.logout", text "Logout")
            )
        | None ->
            h("div.error", text "Invalid session")
    else
        h("div.guest-panel",
            h("a.login-link[href=/login]", text "Login"),
            h("a.signup-link[href=/signup]", text "Sign Up")
        )
```

### Complete Hox Example

See [HoxExample.fs](HoxExample.fs) for a comprehensive example demonstrating:

- ✅ Async data loading from multiple sources
- ✅ Complex component composition
- ✅ Multiple fragment updates in single SSE stream
- ✅ Filter signals with dynamic rendering
- ✅ Progressive enhancement patterns
- ✅ Real-time updates with notifications
- ✅ Conditional rendering based on signals
- ✅ Data table generation
- ✅ Dashboard with aggregated statistics
- ✅ Streaming large layouts

### Hox Best Practices

1. **Keep Components Pure**: Components should be pure functions that return `Node` or `Task<Node>`
2. **Use Fragment for Lists**: Use `fragment` to group multiple elements without a wrapper
3. **Async When Needed**: Only use `task {}` when you actually need async operations
4. **CSS Selector Syntax**: Leverage the concise `h("div#id.class[attr=value]")` syntax
5. **Compose Aggressively**: Build larger components from smaller, reusable ones

## Related Projects

- [Frank](https://github.com/frank-fs/frank) - F# web framework
- [Datastar](https://github.com/starfederation/datastar) - Hypermedia framework
- [datastar-dotnet](https://github.com/starfederation/datastar-dotnet) - .NET SDK for Datastar
- [Hox](https://github.com/AngelMunoz/Hox) - Async HTML rendering library for F#

## Support

- Issues: [GitHub Issues](https://github.com/frank-fs/frank/issues)
- Discussions: [GitHub Discussions](https://github.com/frank-fs/frank/discussions)
- Datastar Documentation: [Datastar Docs](https://data-star.dev)
- Hox Documentation: [Hox Docs](https://hox.tunaxor.me)
