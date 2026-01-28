// ===========================
// FRANK.DATASTAR.HOX SAMPLE
// ===========================
// This sample demonstrates the same RESTful Datastar patterns as Frank.Datastar.Basic,
// but uses Hox for HTML generation instead of F# string templates.
//
// KEY PATTERNS:
// - Resource URLs (nouns): /contacts/{id}, /fruits, /items/{id}
// - HTTP method semantics: GET=retrieve, PUT=update, DELETE=remove, POST=create
// - Query parameters for filtering: /fruits?q=term
// - Hypermedia first: Server sends HTML via SSE
//
// HOX ATTRIBUTE SYNTAX:
// Uses CSS selector notation: h("tag#id.class [attr=value]", children)
// ===========================

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
// SSE HELPER
// ===========================

let writeSseEvent (ctx: HttpContext) (event: SseEvent) =
    task {
        match event with
        | PatchElements html -> do! Datastar.patchElements html ctx
        | RemoveElement selector -> do! Datastar.removeElement selector ctx
        | PatchSignals json -> do! Datastar.patchSignals json ctx
        | Close -> ()
    }

// ===========================
// CONTACT RESOURCES (Click-to-Edit Pattern)
// ===========================

let renderContactView (contact: Contact) : ValueTask<string> =
    let node =
        h("div#contact-view", [
            h("p", [ h("strong", [ Text "First Name:" ]); Text $" {contact.FirstName}" ])
            h("p", [ h("strong", [ Text "Last Name:" ]); Text $" {contact.LastName}" ])
            h("p", [ h("strong", [ Text "Email:" ]); Text $" {contact.Email}" ])
            h($"button [data-on:click=@get('/contacts/{contact.Id}/edit')]", [ Text "Edit" ])
        ])
    Render.asString node

let renderContactEdit (contact: Contact) : ValueTask<string> =
    let node =
        h($"div#contact-view [data-signals={{'firstName': '{contact.FirstName}', 'lastName': '{contact.LastName}', 'email': '{contact.Email}'}}]", [
            h("label", [ Text "First Name "; h("input [type=text] [data-bind:firstName]", []) ])
            h("label", [ Text "Last Name "; h("input [type=text] [data-bind:lastName]", []) ])
            h("label", [ Text "Email "; h("input [type=email] [data-bind:email]", []) ])
            h($"button [data-on:click=@put('/contacts/{contact.Id}')]", [ Text "Save" ])
            h($"button [data-on:click=@get('/contacts/{contact.Id}')]", [ Text "Cancel" ])
        ])
    Render.asString node

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
                    let! html = renderContactView contact
                    do! Datastar.patchElements html ctx

                    // Keep connection open, await updates from channel
                    let mutable keepOpen = true

                    while keepOpen && not ctx.RequestAborted.IsCancellationRequested do
                        let! event = contactChannel.PostAndAsyncReply(Subscribe) |> Async.StartAsTask

                        match event with
                        | Close -> keepOpen <- false
                        | _ -> do! writeSseEvent ctx event
                | false, _ ->
                    ctx.Response.StatusCode <- 404
                    let errorNode = h("div#contact-view.error", [ Text "Contact not found." ])
                    let! html = Render.asString errorNode
                    do! Datastar.patchElements html ctx
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
                        let! html = renderContactView updated
                        contactChannel.Post(Broadcast(PatchElements html))
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
                    let! html = renderContactEdit contact
                    contactChannel.Post(Broadcast(PatchElements html))
                    ctx.Response.StatusCode <- 202
                | false, _ -> ctx.Response.StatusCode <- 404
            })
    }

// ===========================
// FRUITS RESOURCE (Search Pattern)
// ===========================

let renderFruitsList (filteredFruits: string list) : ValueTask<string> =
    let node =
        h("ul#fruits-list",
            fragment [ for f in filteredFruits do h("li", [ Text f ]) ])
    Render.asString node

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

                    let! html = renderFruitsList filtered
                    fruitsChannel.Post(Broadcast(PatchElements html))
                    ctx.Response.StatusCode <- 202
                else
                    // Initial load - establish SSE, send full list, keep open
                    ctx.Response.Headers.ContentType <- "text/event-stream"
                    ctx.Response.Headers.CacheControl <- "no-cache"

                    // Send initial full list
                    let! html = renderFruitsList fruits
                    do! Datastar.patchElements html ctx

                    // Keep connection open for search updates
                    let mutable keepOpen = true

                    while keepOpen && not ctx.RequestAborted.IsCancellationRequested do
                        let! event = fruitsChannel.PostAndAsyncReply(Subscribe) |> Async.StartAsTask

                        match event with
                        | Close -> keepOpen <- false
                        | _ -> do! writeSseEvent ctx event
            })
    }

// ===========================
// ITEMS RESOURCES (Delete Pattern)
// ===========================

let renderItemsTable (itemsList: ResizeArray<Item>) : ValueTask<string> =
    let node =
        h("table#items-table", [
            h("thead", [
                h("tr", [
                    h("th", [ Text "Name" ])
                    h("th", [ Text "Actions" ])
                ])
            ])
            h("tbody#items-list",
                fragment [
                    for item in itemsList do
                        h($"tr#item-{item.Id}", [
                            h("td", [ Text item.Name ])
                            h("td", [
                                h($"button [data-on:click=confirm('Delete this item?') && @delete('/items/{item.Id}')]", [ Text "Delete" ])
                            ])
                        ])
                ])
        ])
    Render.asString node

// GET /items - Establishes SSE, sends table, keeps open for delete updates
let itemsCollectionResource =
    resource "/items" {
        name "Items"

        get (fun (ctx: HttpContext) ->
            task {
                ctx.Response.Headers.ContentType <- "text/event-stream"
                ctx.Response.Headers.CacheControl <- "no-cache"

                // Send initial table
                let! html = renderItemsTable items
                do! Datastar.patchElements html ctx

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

// ===========================
// USERS RESOURCES (Bulk Update Pattern)
// ===========================

let renderUsersTable (usersList: System.Collections.Generic.Dictionary<int, User>) : ValueTask<string> =
    let node =
        h("div#users-table-container [data-signals={'selections': [false, false, false, false]}]", [
            h("table", [
                h("thead", [
                    h("tr", [
                        h("th", [
                            h($"input [type=checkbox] [data-on:change=$selections = Array({usersList.Count}).fill($el.checked)]", [])
                        ])
                        h("th", [ Text "Name" ])
                        h("th", [ Text "Email" ])
                        h("th", [ Text "Status" ])
                    ])
                ])
                h("tbody#users-list",
                    fragment [
                        for idx, user in usersList.Values |> Seq.indexed do
                            let statusClass = if user.Status = Active then "status-active" else "status-inactive"
                            let statusText = if user.Status = Active then "Active" else "Inactive"
                            h("tr", [
                                h("td", [
                                    h($"input [type=checkbox] [data-bind:selections[{idx}]]", [])
                                ])
                                h("td", [ Text user.Name ])
                                h("td", [ Text user.Email ])
                                h($"td.{statusClass}", [ Text statusText ])
                            ])
                    ])
            ])
            h("button [data-on:click=@put('/users/bulk?status=active')]", [ Text "Activate Selected" ])
            h("button [data-on:click=@put('/users/bulk?status=inactive')]", [ Text "Deactivate Selected" ])
        ])
    Render.asString node

// GET /users - Establishes SSE, sends table, keeps open
let usersCollectionResource =
    resource "/users" {
        name "Users"

        get (fun (ctx: HttpContext) ->
            task {
                ctx.Response.Headers.ContentType <- "text/event-stream"
                ctx.Response.Headers.CacheControl <- "no-cache"

                let! html = renderUsersTable users
                do! Datastar.patchElements html ctx

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

                    let! html = renderUsersTable users
                    usersChannel.Post(Broadcast(PatchElements html))
                    ctx.Response.StatusCode <- 202
                | ValueNone -> ctx.Response.StatusCode <- 400
            })
    }

// ===========================
// REGISTRATION RESOURCES (Form Validation Pattern)
// ===========================

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

let renderValidationFeedback (errors: string list) : ValueTask<string> =
    let node =
        if errors.IsEmpty then
            h("div#validation-feedback.success", [ Text "All fields valid!" ])
        else
            h("div#validation-feedback.error", [
                h("ul", fragment [ for e in errors do h("li", [ Text e ]) ])
            ])
    Render.asString node

let renderRegistrationSuccess (firstName: string) : ValueTask<string> =
    let node = h("div#registration-result.success", [ Text $"Registration successful! Welcome, {firstName}." ])
    Render.asString node

let renderRegistrationForm () : ValueTask<string> =
    let node =
        h("div#registration-form [data-signals={'email': '', 'firstName': '', 'lastName': ''}]", [
            h("div", [
                h("label", [
                    Text "Email "
                    h("input [type=email] [data-bind:email] [data-on:input__debounce.500ms=@post('/registrations/validate')]", [])
                ])
            ])
            h("div", [
                h("label", [
                    Text "First Name "
                    h("input [type=text] [data-bind:firstName] [data-on:input__debounce.500ms=@post('/registrations/validate')]", [])
                ])
            ])
            h("div", [
                h("label", [
                    Text "Last Name "
                    h("input [type=text] [data-bind:lastName] [data-on:input__debounce.500ms=@post('/registrations/validate')]", [])
                ])
            ])
            h("div#validation-feedback", [])
            h("button [data-on:click=@post('/registrations')]", [ Text "Register" ])
            h("div#registration-result", [])
        ])
    Render.asString node

// GET /registrations/form - Returns the registration form and establishes SSE
let registrationFormResource =
    resource "/registrations/form" {
        name "RegistrationForm"

        get (fun (ctx: HttpContext) ->
            task {
                ctx.Response.Headers.ContentType <- "text/event-stream"
                ctx.Response.Headers.CacheControl <- "no-cache"

                let! html = renderRegistrationForm ()
                do! Datastar.patchElements html ctx

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
                    let! html = renderValidationFeedback errors
                    registrationChannel.Post(Broadcast(PatchElements html))
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
                            let errorNode = h("div#registration-result.error", [ Text "Email already registered." ])
                            let! html = Render.asString errorNode
                            registrationChannel.Post(Broadcast(PatchElements html))
                            ctx.Response.StatusCode <- 409
                        else
                            let newReg: Registration =
                                { Id = nextRegistrationId
                                  Email = s.email
                                  FirstName = s.firstName
                                  LastName = s.lastName }

                            registrations.Add(newReg)
                            nextRegistrationId <- nextRegistrationId + 1
                            let! html = renderRegistrationSuccess s.firstName
                            registrationChannel.Post(Broadcast(PatchElements html))
                            ctx.Response.StatusCode <- 201
                    else
                        let! html = renderValidationFeedback errors
                        registrationChannel.Post(Broadcast(PatchElements html))
                        ctx.Response.StatusCode <- 400
                | ValueNone -> ctx.Response.StatusCode <- 400
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
