module Example

open System
open System.IO
open System.Threading
open System.Threading.Channels
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Frank
open Frank.Builder
open Frank.Datastar
open Oxpecker.ViewEngine

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
type BulkUpdateSignals = { selections: bool array }

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
    | StreamPatchElements of writer: (TextWriter -> Task)
    | RemoveElement of selector: string
    | PatchSignals of json: string

module SseEvent =

    /// Thread-safe collection of subscriber channels
    let private subscribersLock = obj ()
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
                ch.Writer.TryWrite(event) |> ignore)

    /// Helper to write SSE events to response
    let writeSseEvent (ctx: HttpContext) (event: SseEvent) =
        task {
            match event with
            | PatchElements html -> do! Datastar.patchElements html ctx
            | StreamPatchElements writer -> do! Datastar.streamPatchElements writer ctx
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

let renderContactView (contact: Contact) : string =
    div(id = "contact-view") {
        p() {
            strong() { "First Name:" }
            raw $" {contact.FirstName}"
        }
        p() {
            strong() { "Last Name:" }
            raw $" {contact.LastName}"
        }
        p() {
            strong() { "Email:" }
            raw $" {contact.Email}"
        }
        button()
            .attr("data-on:click", $"@get('/contacts/{contact.Id}/edit')")
            .attr("data-indicator:_fetching", "")
            .attr("data-attr:disabled", "$_fetching") { "Edit" }
    }
    |> Render.toString

let renderContactEdit (contact: Contact) : string =
    div(id = "contact-view")
        .attr("data-signals", $"{{'firstName': '{contact.FirstName}', 'lastName': '{contact.LastName}', 'email': '{contact.Email}'}}") {
        label() {
            raw "First Name "
            input(type' = "text")
                .attr("data-bind:first-name", "")
                .attr("data-attr:disabled", "$_fetching")
        }
        label() {
            raw "Last Name "
            input(type' = "text")
                .attr("data-bind:last-name", "")
                .attr("data-attr:disabled", "$_fetching")
        }
        label() {
            raw "Email "
            input(type' = "email")
                .attr("data-bind:email", "")
                .attr("data-attr:disabled", "$_fetching")
        }
        button()
            .attr("data-on:click", $"@put('/contacts/{contact.Id}')")
            .attr("data-indicator:_fetching", "")
            .attr("data-attr:disabled", "$_fetching") { "Save" }
        button()
            .attr("data-on:click", $"@get('/contacts/{contact.Id}')")
            .attr("data-indicator:_fetching", "")
            .attr("data-attr:disabled", "$_fetching") { "Cancel" }
    }
    |> Render.toString

// Contact resource - GET retrieves data, PUT updates data, broadcasts to channel
let contactResource =
    resource "/contacts/{id}" {
        name "Contact"

        get (fun (ctx: HttpContext) ->
            task {
                let id = ctx.Request.RouteValues["id"] |> string |> int

                match contacts.TryGetValue(id) with
                | true, (contact: Contact) ->
                    SseEvent.broadcast (PatchElements(renderContactView contact))
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
                        SseEvent.broadcast (PatchElements(renderContactView updated))
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
                    SseEvent.broadcast (PatchElements(renderContactEdit contact))
                    ctx.Response.StatusCode <- 202
                | false, _ -> ctx.Response.StatusCode <- 404
            })
    }

// --- Search Pattern (Fruits Collection) ---

let renderFruitsList (filteredFruits: string list) : string =
    // Use min-height to ensure empty list remains visible for Playwright
    ul(id = "fruits-list", style = "min-height: 1em;") {
        for f in filteredFruits do
            li() { f }
    }
    |> Render.toString

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
                        fruits
                        |> List.filter (fun f -> f.Contains(query, StringComparison.OrdinalIgnoreCase))

                SseEvent.broadcast (PatchElements(renderFruitsList filtered))
                ctx.Response.StatusCode <- 202
            })
    }

// --- Delete Pattern (Items Collection) ---

let renderItemsTable (itemsList: ResizeArray<Item>) : string =
    table(id = "items-table") {
        thead() {
            tr() {
                th() { "Name" }
                th() { "Actions" }
            }
        }
        tbody(id = "items-list") {
            for item in itemsList do
                tr(id = $"item-{item.Id}") {
                    td() { item.Name }
                    td() {
                        button()
                            .attr("data-on:click", $"confirm('Are you sure?') && @delete('/items/{item.Id}')")
                            .attr("data-indicator:_fetching", "")
                            .attr("data-attr:disabled", "$_fetching") { "Delete" }
                    }
                }
        }
    }
    |> Render.toString

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
                SseEvent.broadcast (PatchElements(renderItemsTable items))
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
                    SseEvent.broadcast (RemoveElement $"#item-{id}")
                    ctx.Response.StatusCode <- 202
                | None -> ctx.Response.StatusCode <- 404
            })
    }

// --- Bulk Update Pattern (Users Collection) ---

let usersTableElement (usersList: System.Collections.Generic.Dictionary<int, User>) =
    // Use data-signals__ifmissing to initialize selections array (Datastar pattern)
    // data-bind:selections on checkboxes automatically manages the boolean array
    div(id = "users-table-container")
        .attr("data-signals__ifmissing", "{_fetching: false, selections: Array(4).fill(false)}") {
        table() {
            thead() {
                tr() {
                    th() {
                        input(type' = "checkbox")
                            .attr("data-bind:_all", "")
                            .attr("data-on:change", "$selections = Array(4).fill($_all)")
                            .attr("data-effect", "$selections; $_all = $selections.every(Boolean)")
                            .attr("data-attr:disabled", "$_fetching")
                    }
                    th() { "Name" }
                    th() { "Email" }
                    th() { "Status" }
                }
            }
            tbody(id = "users-list") {
                for user in usersList.Values do
                    let statusClass = if user.Status = Active then "status-active" else "status-inactive"
                    let statusText = if user.Status = Active then "Active" else "Inactive"
                    tr() {
                        td() {
                            input(type' = "checkbox")
                                .attr("data-bind:selections", "")
                                .attr("data-attr:disabled", "$_fetching")
                        }
                        td() { user.Name }
                        td() { user.Email }
                        td(class' = statusClass) { statusText }
                    }
            }
        }
        button()
            .attr("data-on:click", "@put('/users/bulk?status=active')")
            .attr("data-indicator:_fetching", "")
            .attr("data-attr:disabled", "$_fetching") { "Activate Selected" }
        button()
            .attr("data-on:click", "@put('/users/bulk?status=inactive')")
            .attr("data-indicator:_fetching", "")
            .attr("data-attr:disabled", "$_fetching") { "Deactivate Selected" }
    }

// Stream-based: Oxpecker renders directly to TextWriter (zero intermediate string).
// The TextWriter is Frank.Datastar's SseDataLineWriter, which encodes to UTF-8
// directly into the response buffer — no HTML string materialization.
let streamUsersTable (usersList: System.Collections.Generic.Dictionary<int, User>) : TextWriter -> Task =
    let element = usersTableElement usersList
    fun tw -> Render.toTextWriterAsync tw element

// GET /users - Fire-and-forget: broadcasts users table to SSE channel
let usersResource =
    resource "/users" {
        name "Users"

        get (fun (ctx: HttpContext) ->
            task {
                SseEvent.broadcast (StreamPatchElements(streamUsersTable users))
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

                SseEvent.broadcast (StreamPatchElements(streamUsersTable users))
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

let renderValidationFeedback (errors: string list) : string =
    if errors.IsEmpty then
        div(id = "validation-feedback", class' = "success") { "All fields valid!" }
        |> Render.toString
    else
        div(id = "validation-feedback", class' = "error") {
            ul() {
                for e in errors do
                    li() { e }
            }
        }
        |> Render.toString

let renderRegistrationSuccess (firstName: string) : string =
    div(id = "registration-result", class' = "success") { $"Registration successful! Welcome, {firstName}." }
    |> Render.toString

let renderRegistrationForm () : string =
    div(id = "registration-form")
        .attr("data-signals", "{'email': '', 'firstName': '', 'lastName': ''}") {
        div() {
            label() {
                raw "Email "
                input(type' = "email")
                    .attr("data-bind:email", "")
                    .attr("data-on:keydown__debounce.500ms", "@post('/registrations/validate')")
                    .attr("data-attr:disabled", "$_fetching")
            }
        }
        div() {
            label() {
                raw "First Name "
                input(type' = "text")
                    .attr("data-bind:first-name", "")
                    .attr("data-on:keydown__debounce.500ms", "@post('/registrations/validate')")
                    .attr("data-attr:disabled", "$_fetching")
            }
        }
        div() {
            label() {
                raw "Last Name "
                input(type' = "text")
                    .attr("data-bind:last-name", "")
                    .attr("data-on:keydown__debounce.500ms", "@post('/registrations/validate')")
                    .attr("data-attr:disabled", "$_fetching")
            }
        }
        div(id = "validation-feedback") { () }
        button()
            .attr("data-on:click", "@post('/registrations')")
            .attr("data-indicator:_fetching", "")
            .attr("data-attr:disabled", "$_fetching") { "Register" }
        div(id = "registration-result") { () }
    }
    |> Render.toString

// GET /registrations/form - Fire-and-forget: broadcasts registration form to channel
let registrationFormResource =
    resource "/registrations/form" {
        name "RegistrationForm"

        get (fun (ctx: HttpContext) ->
            task {
                SseEvent.broadcast (PatchElements(renderRegistrationForm ()))
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
                    SseEvent.broadcast (PatchElements(renderValidationFeedback errors))
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
                            let errorHtml =
                                div(id = "registration-result", class' = "error") { "Email already registered." }
                                |> Render.toString
                            SseEvent.broadcast (PatchElements errorHtml)
                            ctx.Response.StatusCode <- 409
                        else
                            let newReg: Registration =
                                { Id = nextRegistrationId
                                  Email = s.email
                                  FirstName = s.firstName
                                  LastName = s.lastName }

                            registrations.Add(newReg)
                            nextRegistrationId <- nextRegistrationId + 1
                            SseEvent.broadcast (PatchElements(renderRegistrationSuccess s.firstName))
                            ctx.Response.StatusCode <- 201
                    else
                        SseEvent.broadcast (PatchElements(renderValidationFeedback errors))
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
                let myChannel = SseEvent.subscribe ()

                try
                    // No initial data push - "Load X" buttons trigger data loads
                    // Keep connection open, forwarding events from our channel
                    while not ctx.RequestAborted.IsCancellationRequested do
                        let! event = myChannel.Reader.ReadAsync(ctx.RequestAborted).AsTask()
                        do! SseEvent.writeSseEvent ctx event
                with
                | :? OperationCanceledException -> ()
                | :? ChannelClosedException -> ()
                | _ -> ()

                // Clean up subscription when connection closes
                SseEvent.unsubscribe myChannel
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
