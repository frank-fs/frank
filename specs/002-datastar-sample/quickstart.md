# Quickstart: Frank.Datastar Sample Application

**Feature Branch**: `002-datastar-sample`
**Date**: 2026-01-27

## Prerequisites

- .NET 8.0, 9.0, or 10.0 SDK installed
- Git (for cloning)

## Running the Sample

```bash
# Clone the repository
git clone https://github.com/frank-fs/frank.git
cd frank

# Run the sample
dotnet run --project sample/Frank.Datastar.Basic
```

The application starts at `http://localhost:5000`.

## Sample Structure

```
sample/Frank.Datastar.Basic/
├── Program.fs              # All examples and routing
├── Frank.Datastar.Basic.fsproj
└── wwwroot/
    └── index.html          # HTML with Datastar attributes
```

## Examples Overview

### Existing Examples (Preserved)

| Example | Pattern | Description |
|---------|---------|-------------|
| displayDate | patchElements | Simple date/time display with removal |
| removeDate | removeElement | Element removal via CSS selector |
| searchItems | patchElements | Server-driven search filtering |
| loadItemsPage | patchElements | Paginated list |
| greetUser | patchElements | Server decides HTML based on input |
| loadProducts | patchElements | Product catalog with filtering |
| clock | patchElements | Real-time updates every second |
| dashboardRefresh | patchElements | Multiple progressive HTML patches |
| viewProfile | patchElements | Server-driven UI based on permissions |
| counter | patchSignals | Minimal signal usage for UI state |

### New RESTful Examples

| Example | Pattern | HTTP Methods | Description |
|---------|---------|--------------|-------------|
| Click-to-Edit | Contact resource | GET, PUT | Edit contact inline |
| Search | Fruits collection | GET + query params | Filter with debounce |
| Delete Row | Items resource | DELETE | Remove items from list |
| Bulk Update | Users collection | PUT | Multi-select status change |
| Form Validation | Registration | POST | Real-time validation |

## Code Examples

### Click-to-Edit (Contact)

```fsharp
// View contact
resource "/contacts/{id}" {
    get (fun ctx -> task {
        let id = ctx.Request.RouteValues["id"] |> string |> int
        match contacts.TryGetValue(id) with
        | true, contact ->
            do! patchElements $"""
                <div id="contact-{id}">
                    <p><strong>First Name:</strong> {contact.FirstName}</p>
                    <p><strong>Last Name:</strong> {contact.LastName}</p>
                    <p><strong>Email:</strong> {contact.Email}</p>
                    <button data-on:click="@get('/contacts/{id}/edit')">Edit</button>
                </div>
            """ ctx
        | false, _ ->
            ctx.Response.StatusCode <- 404
            do! patchElements $"""<div id="contact-{id}" class="error">Contact not found.</div>""" ctx
    })

    put (fun ctx -> task {
        let id = ctx.Request.RouteValues["id"] |> string |> int
        match! tryReadSignals<ContactSignals> ctx with
        | ValueSome signals ->
            contacts[id] <- { Id = id; FirstName = signals.firstName; ... }
            do! patchElements (viewContact contacts[id]) ctx
        | ValueNone ->
            ctx.Response.StatusCode <- 400
    })
}
```

### Search (Fruits)

```fsharp
resource "/fruits" {
    datastar (fun ctx -> task {
        let q = ctx.Request.Query["q"] |> Seq.tryHead |> Option.defaultValue ""
        let filtered = fruits |> List.filter (fun f -> f.Contains(q, StringComparison.OrdinalIgnoreCase))
        do! patchElements $"""
            <ul id="fruits-list">
                {filtered |> List.map (fun f -> $"<li>{f}</li>") |> String.concat ""}
            </ul>
        """ ctx
    })
}
```

### Delete Row (Items)

```fsharp
resource "/items/{id}" {
    delete (fun ctx -> task {
        let id = ctx.Request.RouteValues["id"] |> string |> int
        match items |> Seq.tryFindIndex (fun i -> i.Id = id) with
        | Some idx ->
            items.RemoveAt(idx)
            do! removeElement $"#item-{id}" ctx
        | None ->
            ctx.Response.StatusCode <- 404
    })
}
```

### Bulk Update (Users)

```fsharp
resource "/users/bulk" {
    put (fun ctx -> task {
        let status = ctx.Request.Query["status"] |> Seq.head
        match! tryReadSignals<BulkUpdateSignals> ctx with
        | ValueSome signals ->
            for i, selected in signals.selections |> Array.indexed do
                if selected then
                    let userId = i + 1
                    users[userId] <- { users[userId] with Status = if status = "active" then Active else Inactive }
            do! patchElements (renderUsersTable users) ctx
        | ValueNone -> ()
    })
}
```

### Form Validation (Registration)

```fsharp
resource "/registrations/validate" {
    datastar HttpMethods.Post (fun ctx -> task {
        match! tryReadSignals<RegistrationSignals> ctx with
        | ValueSome signals ->
            let errors = validateRegistration signals
            do! patchElements (renderValidationFeedback errors) ctx
        | ValueNone -> ()
    })
}

resource "/registrations" {
    datastar HttpMethods.Post (fun ctx -> task {
        match! tryReadSignals<RegistrationSignals> ctx with
        | ValueSome signals ->
            let errors = validateRegistration signals
            if errors.IsEmpty then
                registrations.Add({ Id = nextId; ... })
                do! patchElements (renderSuccess signals.firstName) ctx
            else
                ctx.Response.StatusCode <- 400
                do! patchElements (renderErrors errors) ctx
        | ValueNone ->
            ctx.Response.StatusCode <- 400
    })
}
```

## Key Patterns Demonstrated

1. **Resource-Oriented URLs**: `/contacts/1` not `/getContact?id=1`
2. **Proper HTTP Methods**: GET retrieves, PUT updates, DELETE removes
3. **Query Parameters for Filtering**: `/fruits?q=apple` not `/searchFruits`
4. **Sub-resources for Representations**: `/contacts/1/edit` for edit form
5. **Hypermedia-First**: Server sends HTML, not JSON
6. **F# String Templates**: All HTML generated with `$"..."` interpolation

## Testing

Run tests with:

```bash
dotnet test test/Frank.Datastar.Tests
```

Tests verify:
- Frank.Datastar library functionality
- SSE event formatting
- Signal deserialization
- Edge cases (empty streams, unused signals)
