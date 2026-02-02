module Example

open System
open System.Text.Json
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Frank
open Frank.Builder
open Frank.Datastar

// ===========================
// DATASTAR + FRANK: RESTFUL HYPERMEDIA
// ===========================
// Key principles demonstrated:
// 1. Resource URLs (nouns): /contacts/{id}, /fruits, /items/{id}
// 2. HTTP method semantics: GET=retrieve, PUT=update, DELETE=remove, POST=create
// 3. Query parameters for filtering: /fruits?q=term
// 4. Hypermedia first: Server sends HTML via SSE
// ===========================

// ===========================
// RESTFUL RESOURCE TYPES
// ===========================

// Contact for click-to-edit pattern
type Contact =
    { Id: int
      FirstName: string
      LastName: string
      Email: string }

[<CLIMutable>]
type ContactSignals =
    { firstName: string
      lastName: string
      email: string }

// User for bulk update pattern
type UserStatus =
    | Active
    | Inactive

type User =
    { Id: int
      Name: string
      Email: string
      Status: UserStatus }

[<CLIMutable>]
type BulkUpdateSignals =
    { selections: bool array }

// Item for row deletion pattern
type Item = { Id: int; Name: string }

// Registration for form validation pattern
type Registration =
    { Id: int
      Email: string
      FirstName: string
      LastName: string }

[<CLIMutable>]
type RegistrationSignals =
    { email: string
      firstName: string
      lastName: string }

// ===========================
// IN-MEMORY DATA STORES
// ===========================

let mutable contacts: System.Collections.Generic.Dictionary<int, Contact> =
    dict
        [ 1,
          { Contact.Id = 1
            FirstName = "Joe"
            LastName = "Smith"
            Email = "joe@smith.org" } ]
    |> System.Collections.Generic.Dictionary

// ===========================
// SSE BROADCAST INFRASTRUCTURE
// ===========================
// Multiple SSE connections can exist (e.g., multiple browser tabs, parallel tests).
// Each connection subscribes to receive ALL broadcasts.

type SseEvent =
    | PatchElements of html: string
    | RemoveElement of selector: string
    | PatchSignals of json: string

/// Thread-safe collection of subscriber channels
let private subscribersLock = obj()
let private subscribers = ResizeArray<Channel<SseEvent>>()

/// Create a new subscriber channel for an SSE connection
let subscribe () : Channel<SseEvent> =
    let channel = Channel.CreateUnbounded<SseEvent>()
    lock subscribersLock (fun () -> subscribers.Add(channel))
    channel

/// Remove a subscriber channel when SSE connection closes
let unsubscribe (channel: Channel<SseEvent>) =
    lock subscribersLock (fun () -> subscribers.Remove(channel) |> ignore)
    channel.Writer.Complete()

/// Broadcast an event to ALL active SSE connections
let broadcast (event: SseEvent) =
    lock subscribersLock (fun () ->
        for ch in subscribers do
            ch.Writer.TryWrite(event) |> ignore
    )

/// Helper to write SSE events to response
let writeSseEvent (ctx: HttpContext) (event: SseEvent) =
    task {
        match event with
        | PatchElements html -> do! Datastar.patchElements html ctx
        | RemoveElement selector -> do! Datastar.removeElement selector ctx
        | PatchSignals json -> do! Datastar.patchSignals json ctx
    }

// ===========================
// STATIC DATA
// ===========================

let fruits =
    [ "Apple"
      "Apricot"
      "Banana"
      "Blueberry"
      "Cherry"
      "Coconut"
      "Date"
      "Fig"
      "Grape"
      "Kiwi"
      "Lemon"
      "Lime"
      "Mango"
      "Orange"
      "Papaya"
      "Peach"
      "Pear"
      "Pineapple"
      "Plum"
      "Raspberry"
      "Strawberry"
      "Watermelon" ]

let items =
    ResizeArray
        [ { Id = 1; Name = "Item 1" }
          { Id = 2; Name = "Item 2" }
          { Id = 3; Name = "Item 3" }
          { Id = 4; Name = "Item 4" } ]

let mutable users =
    dict
        [ 1,
          { Id = 1
            Name = "Joe Smith"
            Email = "joe@smith.org"
            Status = Active }
          2,
          { Id = 2
            Name = "Jane Doe"
            Email = "jane@doe.com"
            Status = Inactive }
          3,
          { Id = 3
            Name = "Bob Wilson"
            Email = "bob@wilson.net"
            Status = Active }
          4,
          { Id = 4
            Name = "Alice Brown"
            Email = "alice@brown.io"
            Status = Inactive } ]
    |> System.Collections.Generic.Dictionary

let registrations = ResizeArray<Registration>()
let mutable nextRegistrationId = 1

// ===========================
// RESTFUL RESOURCE PATTERNS
// ===========================

// --- Click-to-Edit Pattern (Contact Resource) ---

let inline renderContactView (contact: Contact) : string =
    $"""<div id="contact-view">
        <p><strong>First Name:</strong> {contact.FirstName}</p>
        <p><strong>Last Name:</strong> {contact.LastName}</p>
        <p><strong>Email:</strong> {contact.Email}</p>
        <button data-on:click="@get('/contacts/{contact.Id}/edit')" data-indicator:_fetching data-attr:disabled="$_fetching">Edit</button>
    </div>"""

let inline renderContactEdit (contact: Contact) : string =
    $"""<div id="contact-view" data-signals="{{'firstName': '{contact.FirstName}', 'lastName': '{contact.LastName}', 'email': '{contact.Email}'}}">
        <label>First Name <input type="text" data-bind:first-name data-attr:disabled="$_fetching" /></label>
        <label>Last Name <input type="text" data-bind:last-name data-attr:disabled="$_fetching" /></label>
        <label>Email <input type="email" data-bind:email data-attr:disabled="$_fetching" /></label>
        <button data-on:click="@put('/contacts/{contact.Id}')" data-indicator:_fetching data-attr:disabled="$_fetching">Save</button>
        <button data-on:click="@get('/contacts/{contact.Id}')" data-indicator:_fetching data-attr:disabled="$_fetching">Cancel</button>
    </div>"""

// Contact resource - GET retrieves data, PUT updates data, broadcasts to channel
let contactResource =
    resource "/contacts/{id}" {
        name "Contact"

        get (fun (ctx: HttpContext) ->
            task {
                let id = ctx.Request.RouteValues["id"] |> string |> int

                match contacts.TryGetValue(id) with
                | true, (contact: Contact) ->
                    broadcast (PatchElements(renderContactView contact))
                    ctx.Response.StatusCode <- 202
                | false, _ -> ctx.Response.StatusCode <- 404
            })

        put (fun (ctx: HttpContext) ->
            task {
                let id = ctx.Request.RouteValues["id"] |> string |> int

                match contacts.TryGetValue(id) with
                | true, _ ->
                    let! signals = Datastar.tryReadSignals<ContactSignals> ctx

                    match signals with
                    | ValueSome s ->
                        let updated: Contact =
                            { Id = id
                              FirstName = s.firstName
                              LastName = s.lastName
                              Email = s.email }

                        contacts[id] <- updated
                        broadcast (PatchElements(renderContactView updated))
                        ctx.Response.StatusCode <- 202
                    | ValueNone -> ctx.Response.StatusCode <- 400
                | false, _ -> ctx.Response.StatusCode <- 404
            })
    }

// Contact edit - broadcasts edit form to channel
let contactEditResource =
    resource "/contacts/{id}/edit" {
        name "ContactEdit"

        get (fun (ctx: HttpContext) ->
            task {
                let id = ctx.Request.RouteValues["id"] |> string |> int

                match contacts.TryGetValue(id) with
                | true, (contact: Contact) ->
                    broadcast (PatchElements(renderContactEdit contact))
                    ctx.Response.StatusCode <- 202
                | false, _ -> ctx.Response.StatusCode <- 404
            })
    }

// --- Search Pattern (Fruits Collection) ---

let inline renderFruitsList (filteredFruits: string list) : string =
    let items =
        filteredFruits |> List.map (fun f -> $"<li>{f}</li>") |> String.concat ""
    // Use min-height to ensure empty list remains visible for Playwright
    $"""<ul id="fruits-list" style="min-height: 1em;">{items}</ul>"""

// Fruits search - broadcasts filtered or full fruit list
let fruitsResource =
    resource "/fruits" {
        name "Fruits"

        get (fun (ctx: HttpContext) ->
            task {
                let query = ctx.Request.Query["q"].ToString()

                let filtered =
                    if String.IsNullOrEmpty(query) then
                        fruits
                    else
                        fruits |> List.filter (fun f -> f.Contains(query, StringComparison.OrdinalIgnoreCase))

                broadcast (PatchElements(renderFruitsList filtered))
                ctx.Response.StatusCode <- 202
            })
    }

// --- Delete Pattern (Items Collection) ---

let inline renderItemsTable (itemsList: ResizeArray<Item>) : string =
    let rows =
        itemsList
        |> Seq.map (fun item ->
            $"""<tr id="item-{item.Id}">
                <td>{item.Name}</td>
                <td><button data-on:click="confirm('Are you sure?') && @delete('/items/{item.Id}')" data-indicator:_fetching data-attr:disabled="$_fetching">Delete</button></td>
            </tr>""")
        |> String.concat ""

    $"""<table id="items-table">
        <thead><tr><th>Name</th><th>Actions</th></tr></thead>
        <tbody id="items-list">{rows}</tbody>
    </table>"""

// Debug endpoint to test if input events fire
let debugPingResource =
    resource "/debug/ping" {
        name "DebugPing"

        get (fun (ctx: HttpContext) ->
            task {
                printfn "DEBUG: Input event received!"
                ctx.Response.StatusCode <- 200
            })
    }

// GET /items - Fire-and-forget: broadcasts items table to SSE channel
let itemsResource =
    resource "/items" {
        name "Items"

        get (fun (ctx: HttpContext) ->
            task {
                broadcast (PatchElements(renderItemsTable items))
                ctx.Response.StatusCode <- 202
            })
    }

// DELETE /items/{id} - Fire-and-forget: removes item, broadcasts to channel
let itemResource =
    resource "/items/{id}" {
        name "Item"

        delete (fun (ctx: HttpContext) ->
            task {
                let id = ctx.Request.RouteValues["id"] |> string |> int

                match items |> Seq.tryFindIndex (fun i -> i.Id = id) with
                | Some idx ->
                    items.RemoveAt(idx)
                    broadcast (RemoveElement $"#item-{id}")
                    ctx.Response.StatusCode <- 202
                | None -> ctx.Response.StatusCode <- 404
            })
    }

// --- Bulk Update Pattern (Users Collection) ---

let inline renderUsersTable (usersList: System.Collections.Generic.Dictionary<int, User>) : string =
    let rows =
        usersList.Values
        |> Seq.map (fun user ->
            let statusClass =
                if user.Status = Active then
                    "status-active"
                else
                    "status-inactive"

            let statusText = if user.Status = Active then "Active" else "Inactive"

            $"""<tr>
                <td><input type="checkbox" data-bind:selections data-attr:disabled="$_fetching" /></td>
                <td>{user.Name}</td>
                <td>{user.Email}</td>
                <td class="{statusClass}">{statusText}</td>
            </tr>""")
        |> String.concat ""

    // Use data-signals__ifmissing to initialize selections array (Datastar pattern)
    // data-bind:selections on checkboxes automatically manages the boolean array
    $"""<div id="users-table-container" data-signals__ifmissing="{{_fetching: false, selections: Array(4).fill(false)}}">
        <table>
            <thead>
                <tr>
                    <th><input type="checkbox" data-bind:_all data-on:change="$selections = Array(4).fill($_all)" data-effect="$selections; $_all = $selections.every(Boolean)" data-attr:disabled="$_fetching" /></th>
                    <th>Name</th>
                    <th>Email</th>
                    <th>Status</th>
                </tr>
            </thead>
            <tbody id="users-list">{rows}</tbody>
        </table>
        <button data-on:click="@put('/users/bulk?status=active')" data-indicator:_fetching data-attr:disabled="$_fetching">Activate Selected</button>
        <button data-on:click="@put('/users/bulk?status=inactive')" data-indicator:_fetching data-attr:disabled="$_fetching">Deactivate Selected</button>
    </div>"""

// GET /users - Fire-and-forget: broadcasts users table to SSE channel
let usersResource =
    resource "/users" {
        name "Users"

        get (fun (ctx: HttpContext) ->
            task {
                broadcast (PatchElements(renderUsersTable users))
                ctx.Response.StatusCode <- 202
            })
    }

// PUT /users/bulk - Fire-and-forget: updates selected users, broadcasts to channel
let usersBulkResource =
    resource "/users/bulk" {
        name "UsersBulk"

        put (fun (ctx: HttpContext) ->
            task {
                let status = ctx.Request.Query["status"].ToString()

                // Read selections array from signals using Datastar's standard pattern
                let! signals = Datastar.tryReadSignals<BulkUpdateSignals> ctx

                let selections =
                    match signals with
                    | ValueSome s -> s.selections
                    | ValueNone -> [||]

                let newStatus = if status = "active" then Active else Inactive
                let userIds = users.Keys |> Seq.toArray

                for i, selected in selections |> Array.indexed do
                    if selected && i < userIds.Length then
                        let userId = userIds[i]

                        users[userId] <-
                            { users[userId] with
                                Status = newStatus }

                broadcast (PatchElements(renderUsersTable users))
                ctx.Response.StatusCode <- 202
            })
    }

// --- Form Validation Pattern (Registration) ---

let validateRegistration (signals: RegistrationSignals) : string list =
    let errors = ResizeArray<string>()

    if String.IsNullOrWhiteSpace(signals.email) then
        errors.Add("Email is required")
    elif not (signals.email.Contains("@")) then
        errors.Add("Email must be valid")

    if String.IsNullOrWhiteSpace(signals.firstName) then
        errors.Add("First name is required")

    if String.IsNullOrWhiteSpace(signals.lastName) then
        errors.Add("Last name is required")

    errors |> Seq.toList

let inline renderValidationFeedback (errors: string list) : string =
    if errors.IsEmpty then
        """<div id="validation-feedback" class="success">All fields valid!</div>"""
    else
        let errorList = errors |> List.map (fun e -> $"<li>{e}</li>") |> String.concat ""
        $"""<div id="validation-feedback" class="error"><ul>{errorList}</ul></div>"""

let inline renderRegistrationSuccess (firstName: string) : string =
    $"""<div id="registration-result" class="success">Registration successful! Welcome, {firstName}.</div>"""

let inline renderRegistrationForm () : string =
    """<div id="registration-form" data-signals="{'email': '', 'firstName': '', 'lastName': ''}">
        <div>
            <label>Email <input type="email" data-bind:email data-on:keydown__debounce.500ms="@post('/registrations/validate')" data-attr:disabled="$_fetching" /></label>
        </div>
        <div>
            <label>First Name <input type="text" data-bind:first-name data-on:keydown__debounce.500ms="@post('/registrations/validate')" data-attr:disabled="$_fetching" /></label>
        </div>
        <div>
            <label>Last Name <input type="text" data-bind:last-name data-on:keydown__debounce.500ms="@post('/registrations/validate')" data-attr:disabled="$_fetching" /></label>
        </div>
        <div id="validation-feedback"></div>
        <button data-on:click="@post('/registrations')" data-indicator:_fetching data-attr:disabled="$_fetching">Register</button>
        <div id="registration-result"></div>
    </div>"""

// GET /registrations/form - Fire-and-forget: broadcasts registration form to channel
let registrationFormResource =
    resource "/registrations/form" {
        name "RegistrationForm"

        get (fun (ctx: HttpContext) ->
            task {
                broadcast (PatchElements(renderRegistrationForm ()))
                ctx.Response.StatusCode <- 202
            })
    }

// POST /registrations/validate - Fire-and-forget: validates and broadcasts to channel
let registrationValidateResource =
    resource "/registrations/validate" {
        name "RegistrationValidate"

        post (fun (ctx: HttpContext) ->
            task {
                let! signals = Datastar.tryReadSignals<RegistrationSignals> ctx

                match signals with
                | ValueSome s ->
                    let errors = validateRegistration s
                    broadcast (PatchElements(renderValidationFeedback errors))
                    ctx.Response.StatusCode <- 202
                | ValueNone -> ctx.Response.StatusCode <- 400
            })
    }

// POST /registrations - Fire-and-forget: creates registration, broadcasts to channel
let registrationsResource =
    resource "/registrations" {
        name "Registrations"

        post (fun (ctx: HttpContext) ->
            task {
                let! signals = Datastar.tryReadSignals<RegistrationSignals> ctx

                match signals with
                | ValueSome s ->
                    let errors = validateRegistration s

                    if errors.IsEmpty then
                        // Check for duplicate email
                        let isDuplicate = registrations |> Seq.exists (fun r -> r.Email = s.email)

                        if isDuplicate then
                            broadcast (
                                PatchElements(
                                    """<div id="registration-result" class="error">Email already registered.</div>"""
                                )
                            )
                            ctx.Response.StatusCode <- 409
                        else
                            let newReg: Registration =
                                { Id = nextRegistrationId
                                  Email = s.email
                                  FirstName = s.firstName
                                  LastName = s.lastName }

                            registrations.Add(newReg)
                            nextRegistrationId <- nextRegistrationId + 1
                            broadcast (PatchElements(renderRegistrationSuccess s.firstName))
                            ctx.Response.StatusCode <- 201
                    else
                        broadcast (PatchElements(renderValidationFeedback errors))
                        ctx.Response.StatusCode <- 400
                | ValueNone -> ctx.Response.StatusCode <- 400
            })
    }

// ===========================
// SINGLE SSE ENDPOINT
// ===========================
// One SSE connection per page. All fire-and-forget handlers broadcast to this channel.

let sseResource =
    resource "/sse" {
        name "SSE"

        datastar (fun (ctx: HttpContext) ->
            task {
                // Subscribe to broadcasts for this connection
                let myChannel = subscribe ()

                try
                    // No initial data push - "Load X" buttons trigger data loads
                    // Keep connection open, forwarding events from our channel
                    while not ctx.RequestAborted.IsCancellationRequested do
                        let! event = myChannel.Reader.ReadAsync(ctx.RequestAborted).AsTask()
                        do! writeSseEvent ctx event
                with
                | :? OperationCanceledException -> ()
                | :? ChannelClosedException -> ()
                | _ -> ()

                // Clean up subscription when connection closes
                unsubscribe myChannel
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

        // Single SSE endpoint for the whole page
        resource sseResource

        // RESTful Resource Patterns (fire-and-forget updates via SSE channel)
        // Click-to-Edit (Contact)
        resource contactResource
        resource contactEditResource

        // Debug
        resource debugPingResource

        // Search (Fruits)
        resource fruitsResource

        // Delete (Items)
        resource itemsResource
        resource itemResource

        // Bulk Update (Users)
        resource usersResource
        resource usersBulkResource

        // Form Validation (Registration)
        resource registrationFormResource
        resource registrationValidateResource
        resource registrationsResource
    }

    0
