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
type BulkUpdateSignals = { selections: bool[] }

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
// SSE CHANNELS (MailboxProcessor per section)
// ===========================
// Each demo section has its own SSE channel. The initial GET establishes
// the SSE connection and awaits on the MailboxProcessor. Fire-and-forget
// endpoints post updates to the MailboxProcessor.

type SseEvent =
    | PatchElements of html: string
    | RemoveElement of selector: string
    | PatchSignals of json: string
    | Close

type SseChannelMsg =
    | Subscribe of replyChannel: AsyncReplyChannel<SseEvent>
    | Broadcast of SseEvent

/// Creates a new SSE channel (MailboxProcessor) for a demo section
let createSseChannel () =
    MailboxProcessor.Start(fun inbox ->
        let subscribers = ResizeArray<AsyncReplyChannel<SseEvent>>()
        let pendingEvents = System.Collections.Generic.Queue<SseEvent>()

        let rec loop () =
            async {
                let! msg = inbox.Receive()

                match msg with
                | Subscribe replyChannel ->
                    if pendingEvents.Count > 0 then
                        replyChannel.Reply(pendingEvents.Dequeue())
                    else
                        subscribers.Add(replyChannel)
                | Broadcast event ->
                    if subscribers.Count > 0 then
                        let subscriber = subscribers.[0]
                        subscribers.RemoveAt(0)
                        subscriber.Reply(event)
                    else
                        pendingEvents.Enqueue(event)

                return! loop ()
            }

        loop ())

// SSE channels for each RESTful demo section
let contactChannel = createSseChannel ()
let fruitsChannel = createSseChannel ()
let itemsChannel = createSseChannel ()
let usersChannel = createSseChannel ()
let registrationChannel = createSseChannel ()

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
// These examples demonstrate proper HTTP resource semantics:
// - Resource URLs (nouns, not verbs): /contacts/{id}, /fruits, /items/{id}
// - HTTP methods match semantics: GET=retrieve, PUT=update, DELETE=remove, POST=create
// - Query parameters for filtering: /fruits?q=term
// - Sub-resources for representations: /contacts/{id}/edit
// ===========================

// --- Click-to-Edit Pattern (Contact Resource) ---
// Demonstrates: GET establishes SSE, edit/save are fire-and-forget via channel

let inline renderContactView (contact: Contact) : string =
    $"""<div id="contact-view">
        <p><strong>First Name:</strong> {contact.FirstName}</p>
        <p><strong>Last Name:</strong> {contact.LastName}</p>
        <p><strong>Email:</strong> {contact.Email}</p>
        <button data-on:click="@get('/contacts/{contact.Id}/edit')">Edit</button>
    </div>"""

let inline renderContactEdit (contact: Contact) : string =
    $"""<div id="contact-view" data-signals="{{'firstName': '{contact.FirstName}', 'lastName': '{contact.LastName}', 'email': '{contact.Email}'}}">
        <label>First Name <input type="text" data-bind:firstName /></label>
        <label>Last Name <input type="text" data-bind:lastName /></label>
        <label>Email <input type="email" data-bind:email /></label>
        <button data-on:click="@put('/contacts/{contact.Id}')">Save</button>
        <button data-on:click="@get('/contacts/{contact.Id}')">Cancel</button>
    </div>"""

// Helper to write SSE events to response
let writeSseEvent (ctx: HttpContext) (event: SseEvent) =
    task {
        match event with
        | PatchElements html -> do! Datastar.patchElements html ctx
        | RemoveElement selector -> do! Datastar.removeElement selector ctx
        | PatchSignals json -> do! Datastar.patchSignals json ctx
        | Close -> ()
    }

// GET /contacts/{id} - Establishes SSE, sends initial view, awaits channel for updates
let contactResource =
    resource "/contacts/{id}" {
        name "Contact"

        get (fun (ctx: HttpContext) ->
            task {
                let id = ctx.Request.RouteValues["id"] |> string |> int

                // Set up SSE response
                ctx.Response.Headers.ContentType <- "text/event-stream"
                ctx.Response.Headers.CacheControl <- "no-cache"

                match contacts.TryGetValue(id) with
                | true, (contact: Contact) ->
                    // Send initial view
                    do! Datastar.patchElements (renderContactView contact) ctx

                    // Keep connection open, await updates from channel
                    let mutable keepOpen = true

                    while keepOpen && not ctx.RequestAborted.IsCancellationRequested do
                        let! event = contactChannel.PostAndAsyncReply(Subscribe) |> Async.StartAsTask

                        match event with
                        | Close -> keepOpen <- false
                        | _ -> do! writeSseEvent ctx event
                | false, _ ->
                    ctx.Response.StatusCode <- 404
                    do! Datastar.patchElements """<div id="contact-view" class="error">Contact not found.</div>""" ctx
            })

        // PUT /contacts/{id} - Fire-and-forget, updates data, posts to channel
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
                        contactChannel.Post(Broadcast(PatchElements(renderContactView updated)))
                        ctx.Response.StatusCode <- 202
                    | ValueNone -> ctx.Response.StatusCode <- 400
                | false, _ -> ctx.Response.StatusCode <- 404
            })
    }

// GET /contacts/{id}/edit - Fire-and-forget, posts edit form to channel
let contactEditResource =
    resource "/contacts/{id}/edit" {
        name "ContactEdit"

        get (fun (ctx: HttpContext) ->
            task {
                let id = ctx.Request.RouteValues["id"] |> string |> int

                match contacts.TryGetValue(id) with
                | true, (contact: Contact) ->
                    contactChannel.Post(Broadcast(PatchElements(renderContactEdit contact)))
                    ctx.Response.StatusCode <- 202
                | false, _ -> ctx.Response.StatusCode <- 404
            })
    }

// --- Search Pattern (Fruits Collection) ---
// Demonstrates: GET establishes SSE with initial list, search queries post filtered results

let inline renderFruitsList (filteredFruits: string list) : string =
    let items =
        filteredFruits |> List.map (fun f -> $"<li>{f}</li>") |> String.concat ""

    $"""<ul id="fruits-list">{items}</ul>"""

// GET /fruits - Establishes SSE or handles search query
let fruitsResource =
    resource "/fruits" {
        name "Fruits"

        get (fun (ctx: HttpContext) ->
            task {
                let query = ctx.Request.Query["q"].ToString()

                // If this is a search query (has q param), post to channel (fire-and-forget)
                if not (String.IsNullOrEmpty(query)) then
                    let filtered =
                        fruits
                        |> List.filter (fun f -> f.Contains(query, StringComparison.OrdinalIgnoreCase))

                    fruitsChannel.Post(Broadcast(PatchElements(renderFruitsList filtered)))
                    ctx.Response.StatusCode <- 202
                else
                    // Initial load - establish SSE, send full list, keep open
                    ctx.Response.Headers.ContentType <- "text/event-stream"
                    ctx.Response.Headers.CacheControl <- "no-cache"

                    // Send initial full list
                    do! Datastar.patchElements (renderFruitsList fruits) ctx

                    // Keep connection open for search updates
                    let mutable keepOpen = true

                    while keepOpen && not ctx.RequestAborted.IsCancellationRequested do
                        let! event = fruitsChannel.PostAndAsyncReply(Subscribe) |> Async.StartAsTask

                        match event with
                        | Close -> keepOpen <- false
                        | _ -> do! writeSseEvent ctx event
            })
    }

// --- Delete Pattern (Items Collection) ---
// Demonstrates: GET establishes SSE with list, DELETE is fire-and-forget

let inline renderItemsTable (itemsList: ResizeArray<Item>) : string =
    let rows =
        itemsList
        |> Seq.map (fun item ->
            $"""<tr id="item-{item.Id}">
                <td>{item.Name}</td>
                <td><button data-on:click="confirm('Delete this item?') && @delete('/items/{item.Id}')">Delete</button></td>
            </tr>""")
        |> String.concat ""

    $"""<table id="items-table">
        <thead><tr><th>Name</th><th>Actions</th></tr></thead>
        <tbody id="items-list">{rows}</tbody>
    </table>"""

// GET /items - Establishes SSE, sends table, keeps open for delete updates
let itemsCollectionResource =
    resource "/items" {
        name "Items"

        get (fun (ctx: HttpContext) ->
            task {
                ctx.Response.Headers.ContentType <- "text/event-stream"
                ctx.Response.Headers.CacheControl <- "no-cache"

                // Send initial table
                do! Datastar.patchElements (renderItemsTable items) ctx

                // Keep connection open for delete updates
                let mutable keepOpen = true

                while keepOpen && not ctx.RequestAborted.IsCancellationRequested do
                    let! event = itemsChannel.PostAndAsyncReply(Subscribe) |> Async.StartAsTask

                    match event with
                    | Close -> keepOpen <- false
                    | _ -> do! writeSseEvent ctx event
            })
    }

// DELETE /items/{id} - Fire-and-forget, removes item, posts removeElement to channel
let itemResource =
    resource "/items/{id}" {
        name "Item"

        delete (fun (ctx: HttpContext) ->
            task {
                let id = ctx.Request.RouteValues["id"] |> string |> int

                match items |> Seq.tryFindIndex (fun i -> i.Id = id) with
                | Some idx ->
                    items.RemoveAt(idx)
                    itemsChannel.Post(Broadcast(RemoveElement $"#item-{id}"))
                    ctx.Response.StatusCode <- 202
                | None -> ctx.Response.StatusCode <- 404
            })
    }

// --- Bulk Update Pattern (Users Collection) ---
// Demonstrates: GET establishes SSE with table, PUT bulk is fire-and-forget

let inline renderUsersTable (usersList: System.Collections.Generic.Dictionary<int, User>) : string =
    let rows =
        usersList.Values
        |> Seq.mapi (fun idx user ->
            let statusClass =
                if user.Status = Active then
                    "status-active"
                else
                    "status-inactive"

            let statusText = if user.Status = Active then "Active" else "Inactive"

            $"""<tr>
                <td><input type="checkbox" data-bind:selections[{idx}] /></td>
                <td>{user.Name}</td>
                <td>{user.Email}</td>
                <td class="{statusClass}">{statusText}</td>
            </tr>""")
        |> String.concat ""

    $"""<div id="users-table-container" data-signals="{{'selections': [false, false, false, false]}}">
        <table>
            <thead>
                <tr>
                    <th><input type="checkbox" data-on:change="$selections = Array({usersList.Count}).fill($el.checked)" /></th>
                    <th>Name</th>
                    <th>Email</th>
                    <th>Status</th>
                </tr>
            </thead>
            <tbody id="users-list">{rows}</tbody>
        </table>
        <button data-on:click="@put('/users/bulk?status=active')">Activate Selected</button>
        <button data-on:click="@put('/users/bulk?status=inactive')">Deactivate Selected</button>
    </div>"""

// GET /users - Establishes SSE, sends table, keeps open
let usersCollectionResource =
    resource "/users" {
        name "Users"

        get (fun (ctx: HttpContext) ->
            task {
                ctx.Response.Headers.ContentType <- "text/event-stream"
                ctx.Response.Headers.CacheControl <- "no-cache"

                do! Datastar.patchElements (renderUsersTable users) ctx

                let mutable keepOpen = true

                while keepOpen && not ctx.RequestAborted.IsCancellationRequested do
                    let! event = usersChannel.PostAndAsyncReply(Subscribe) |> Async.StartAsTask

                    match event with
                    | Close -> keepOpen <- false
                    | _ -> do! writeSseEvent ctx event
            })
    }

// PUT /users/bulk - Fire-and-forget, updates selected users, posts new table to channel
let usersBulkResource =
    resource "/users/bulk" {
        name "UsersBulk"

        put (fun (ctx: HttpContext) ->
            task {
                let status = ctx.Request.Query["status"].ToString()
                let! signals = Datastar.tryReadSignals<BulkUpdateSignals> ctx

                match signals with
                | ValueSome s ->
                    let newStatus = if status = "active" then Active else Inactive
                    let userIds = users.Keys |> Seq.toArray

                    for i, selected in s.selections |> Array.indexed do
                        if selected && i < userIds.Length then
                            let userId = userIds[i]

                            users[userId] <-
                                { users[userId] with
                                    Status = newStatus }

                    usersChannel.Post(Broadcast(PatchElements(renderUsersTable users)))
                    ctx.Response.StatusCode <- 202
                | ValueNone -> ctx.Response.StatusCode <- 400
            })
    }

// --- Form Validation Pattern (Registration) ---
// Demonstrates: POST /validate streams feedback, POST /registrations creates with validation

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
            <label>Email <input type="email" data-bind:email data-on:input__debounce.500ms="@post('/registrations/validate')" /></label>
        </div>
        <div>
            <label>First Name <input type="text" data-bind:firstName data-on:input__debounce.500ms="@post('/registrations/validate')" /></label>
        </div>
        <div>
            <label>Last Name <input type="text" data-bind:lastName data-on:input__debounce.500ms="@post('/registrations/validate')" /></label>
        </div>
        <div id="validation-feedback"></div>
        <button data-on:click="@post('/registrations')">Register</button>
        <div id="registration-result"></div>
    </div>"""

// GET /registrations/form - Returns the registration form and establishes SSE
let registrationFormResource =
    resource "/registrations/form" {
        name "RegistrationForm"

        get (fun (ctx: HttpContext) ->
            task {
                ctx.Response.Headers.ContentType <- "text/event-stream"
                ctx.Response.Headers.CacheControl <- "no-cache"

                do! Datastar.patchElements (renderRegistrationForm ()) ctx

                let mutable keepOpen = true

                while keepOpen && not ctx.RequestAborted.IsCancellationRequested do
                    let! event = registrationChannel.PostAndAsyncReply(Subscribe) |> Async.StartAsTask

                    match event with
                    | Close -> keepOpen <- false
                    | _ -> do! writeSseEvent ctx event
            })
    }

// POST /registrations/validate - Fire-and-forget, validates and posts feedback to channel
let registrationValidateResource =
    resource "/registrations/validate" {
        name "RegistrationValidate"

        post (fun (ctx: HttpContext) ->
            task {
                let! signals = Datastar.tryReadSignals<RegistrationSignals> ctx

                match signals with
                | ValueSome s ->
                    let errors = validateRegistration s
                    registrationChannel.Post(Broadcast(PatchElements(renderValidationFeedback errors)))
                    ctx.Response.StatusCode <- 202
                | ValueNone -> ctx.Response.StatusCode <- 400
            })
    }

// POST /registrations - Fire-and-forget, creates registration, posts result to channel
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
                            registrationChannel.Post(
                                Broadcast(
                                    PatchElements(
                                        """<div id="registration-result" class="error">Email already registered.</div>"""
                                    )
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
                            registrationChannel.Post(Broadcast(PatchElements(renderRegistrationSuccess s.firstName)))
                            ctx.Response.StatusCode <- 201
                    else
                        registrationChannel.Post(Broadcast(PatchElements(renderValidationFeedback errors)))
                        ctx.Response.StatusCode <- 400
                | ValueNone -> ctx.Response.StatusCode <- 400
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

        // RESTful Resource Patterns
        // Click-to-Edit (Contact)
        resource contactResource
        resource contactEditResource

        // Search (Fruits)
        resource fruitsResource

        // Delete (Items)
        resource itemsCollectionResource
        resource itemResource

        // Bulk Update (Users)
        resource usersCollectionResource
        resource usersBulkResource

        // Form Validation (Registration)
        resource registrationFormResource
        resource registrationValidateResource
        resource registrationsResource
    }

    0
