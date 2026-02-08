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
// - CSS selectors for tag/id/class: h("div#my-id.my-class", [...])
// - .attr() extension for Datastar attributes with colons/underscores
// ===========================

module HoxExample

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
// SSE BROADCAST INFRASTRUCTURE
// ===========================
// Multiple SSE connections can exist (e.g., multiple browser tabs, parallel tests).
// Each connection subscribes to receive ALL broadcasts.

type SseEvent =
    | PatchElements of html: string
    | StreamPatchElements of writer: (Stream -> Task)
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
            | StreamPatchElements writer -> do! Datastar.streamPatchElementsToStream writer ctx
            | RemoveElement selector -> do! Datastar.removeElement selector ctx
            | PatchSignals json -> do! Datastar.patchSignals json ctx
        }

// ===========================
// CONTACT RESOURCES (Click-to-Edit Pattern)
// ===========================

let renderContactView (contact: Contact) : ValueTask<string> =
    let node =
        h (
            "div#contact-view",
            [ h ("p", [ h ("strong", [ Text "First Name:" ]); Text $" {contact.FirstName}" ])
              h ("p", [ h ("strong", [ Text "Last Name:" ]); Text $" {contact.LastName}" ])
              h ("p", [ h ("strong", [ Text "Email:" ]); Text $" {contact.Email}" ])
              h("button", [ Text "Edit" ])
                  .attr("data-on:click", $"@get('/contacts/{contact.Id}/edit')")
                  .attr("data-indicator:_fetching", "")
                  .attr ("data-attr:disabled", "$_fetching") ]
        )

    Render.asString node

let renderContactEdit (contact: Contact) : ValueTask<string> =
    let node =
        h(
            "div#contact-view",
            [ h (
                  "label",
                  [ Text "First Name "
                    h("input[type=text]", []).attr("data-bind:first-name", "").attr ("data-attr:disabled", "$_fetching") ]
              )
              h (
                  "label",
                  [ Text "Last Name "
                    h("input[type=text]", []).attr("data-bind:last-name", "").attr ("data-attr:disabled", "$_fetching") ]
              )
              h (
                  "label",
                  [ Text "Email "
                    h("input[type=email]", []).attr("data-bind:email", "").attr ("data-attr:disabled", "$_fetching") ]
              )
              h("button", [ Text "Save" ])
                  .attr("data-on:click", $"@put('/contacts/{contact.Id}')")
                  .attr("data-indicator:_fetching", "")
                  .attr ("data-attr:disabled", "$_fetching")
              h("button", [ Text "Cancel" ])
                  .attr("data-on:click", $"@get('/contacts/{contact.Id}')")
                  .attr("data-indicator:_fetching", "")
                  .attr ("data-attr:disabled", "$_fetching") ]
        )
            .attr (
                "data-signals",
                $"{{'firstName': '{contact.FirstName}', 'lastName': '{contact.LastName}', 'email': '{contact.Email}'}}"
            )

    Render.asString node

// Contact resource - GET retrieves data, PUT updates data, broadcasts to channel
let contactResource =
    resource "/contacts/{id}" {
        name "Contact"

        get (fun (ctx: HttpContext) ->
            task {
                let id = ctx.Request.RouteValues["id"] |> string |> int

                match contacts.TryGetValue(id) with
                | true, (contact: Contact) ->
                    let! html = renderContactView contact
                    SseEvent.broadcast (PatchElements html)
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
                        let! html = renderContactView updated
                        SseEvent.broadcast (PatchElements html)
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
                    let! html = renderContactEdit contact
                    SseEvent.broadcast (PatchElements html)
                    ctx.Response.StatusCode <- 202
                | false, _ -> ctx.Response.StatusCode <- 404
            })
    }

// ===========================
// FRUITS RESOURCE (Search Pattern)
// ===========================

let renderFruitsList (filteredFruits: string list) : ValueTask<string> =
    let node =
        // Use min-height to ensure empty list remains visible for Playwright
        h (
            "ul#fruits-list[style=min-height: 1em;]",
            fragment
                [ for f in filteredFruits do
                      h ("li", [ Text f ]) ]
        )

    Render.asString node

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

                let! html = renderFruitsList filtered
                SseEvent.broadcast (PatchElements html)
                ctx.Response.StatusCode <- 202
            })
    }

// ===========================
// ITEMS RESOURCES (Delete Pattern)
// ===========================

let renderItemsTable (itemsList: ResizeArray<Item>) : ValueTask<string> =
    let node =
        h (
            "table#items-table",
            [ h ("thead", [ h ("tr", [ h ("th", [ Text "Name" ]); h ("th", [ Text "Actions" ]) ]) ])
              h (
                  "tbody#items-list",
                  fragment
                      [ for item in itemsList do
                            h (
                                $"tr#item-{item.Id}",
                                [ h ("td", [ Text item.Name ])
                                  h (
                                      "td",
                                      [ h("button", [ Text "Delete" ])
                                            .attr(
                                                "data-on:click",
                                                $"confirm('Are you sure?') && @delete('/items/{item.Id}')"
                                            )
                                            .attr("data-indicator:_fetching", "")
                                            .attr ("data-attr:disabled", "$_fetching") ]
                                  ) ]
                            ) ]
              ) ]
        )

    Render.asString node

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
                let! html = renderItemsTable items
                SseEvent.broadcast (PatchElements html)
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

// ===========================
// USERS RESOURCES (Bulk Update Pattern)
// ===========================

let usersTableNode (usersList: System.Collections.Generic.Dictionary<int, User>) =
    // Use data-signals__ifmissing to initialize selections array (Datastar pattern)
    // data-bind:selections on checkboxes automatically manages the boolean array
    h(
        "div#users-table-container",
        [ h (
              "table",
              [ h (
                    "thead",
                    [ h (
                          "tr",
                          [ h (
                                "th",
                                [ h("input[type=checkbox]", [])
                                      .attr("data-bind:_all", "")
                                      .attr("data-on:change", "$selections = Array(4).fill($_all)")
                                      .attr("data-effect", "$selections; $_all = $selections.every(Boolean)")
                                      .attr ("data-attr:disabled", "$_fetching") ]
                            )
                            h ("th", [ Text "Name" ])
                            h ("th", [ Text "Email" ])
                            h ("th", [ Text "Status" ]) ]
                      ) ]
                )
                h (
                    "tbody#users-list",
                    fragment
                        [ for user in usersList.Values do
                              let statusClass =
                                  if user.Status = Active then
                                      "status-active"
                                  else
                                      "status-inactive"

                              let statusText = if user.Status = Active then "Active" else "Inactive"

                              h (
                                  "tr",
                                  [ h (
                                        "td",
                                        [ h("input[type=checkbox]", [])
                                              .attr("data-bind:selections", "")
                                              .attr ("data-attr:disabled", "$_fetching") ]
                                    )
                                    h ("td", [ Text user.Name ])
                                    h ("td", [ Text user.Email ])
                                    h ($"td.{statusClass}", [ Text statusText ]) ]
                              ) ]
                ) ]
          )
          h("button", [ Text "Activate Selected" ])
              .attr("data-on:click", "@put('/users/bulk?status=active')")
              .attr("data-indicator:_fetching", "")
              .attr ("data-attr:disabled", "$_fetching")
          h("button", [ Text "Deactivate Selected" ])
              .attr("data-on:click", "@put('/users/bulk?status=inactive')")
              .attr("data-indicator:_fetching", "")
              .attr ("data-attr:disabled", "$_fetching") ]
    )
        .attr ("data-signals__ifmissing", "{_fetching: false, selections: Array(4).fill(false)}")

let renderUsersTable (usersList: System.Collections.Generic.Dictionary<int, User>) : ValueTask<string> =
    Render.asString (usersTableNode usersList)

/// Stream users table directly to SSE using Hox's Render.toStream (zero-string-materialization).
let streamUsersTable (usersList: System.Collections.Generic.Dictionary<int, User>) : Stream -> Task =
    let node = usersTableNode usersList
    fun stream -> Render.toStream (node, stream)

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
            h ("div#validation-feedback.success", [ Text "All fields valid!" ])
        else
            h (
                "div#validation-feedback.error",
                [ h (
                      "ul",
                      fragment
                          [ for e in errors do
                                h ("li", [ Text e ]) ]
                  ) ]
            )

    Render.asString node

let renderRegistrationSuccess (firstName: string) : ValueTask<string> =
    let node =
        h ("div#registration-result.success", [ Text $"Registration successful! Welcome, {firstName}." ])

    Render.asString node

let renderRegistrationForm () : ValueTask<string> =
    let node =
        h(
            "div#registration-form",
            [ h (
                  "div",
                  [ h (
                        "label",
                        [ Text "Email "
                          h("input[type=email]", [])
                              .attr("data-bind:email", "")
                              .attr("data-on:keydown__debounce.500ms", "@post('/registrations/validate')")
                              .attr ("data-attr:disabled", "$_fetching") ]
                    ) ]
              )
              h (
                  "div",
                  [ h (
                        "label",
                        [ Text "First Name "
                          h("input[type=text]", [])
                              .attr("data-bind:first-name", "")
                              .attr("data-on:keydown__debounce.500ms", "@post('/registrations/validate')")
                              .attr ("data-attr:disabled", "$_fetching") ]
                    ) ]
              )
              h (
                  "div",
                  [ h (
                        "label",
                        [ Text "Last Name "
                          h("input[type=text]", [])
                              .attr("data-bind:last-name", "")
                              .attr("data-on:keydown__debounce.500ms", "@post('/registrations/validate')")
                              .attr ("data-attr:disabled", "$_fetching") ]
                    ) ]
              )
              h ("div#validation-feedback", [])
              h("button", [ Text "Register" ])
                  .attr("data-on:click", "@post('/registrations')")
                  .attr("data-indicator:_fetching", "")
                  .attr ("data-attr:disabled", "$_fetching")
              h ("div#registration-result", []) ]
        )
            .attr ("data-signals", "{'email': '', 'firstName': '', 'lastName': ''}")

    Render.asString node

// GET /registrations/form - Fire-and-forget: broadcasts registration form to channel
let registrationFormResource =
    resource "/registrations/form" {
        name "RegistrationForm"

        get (fun (ctx: HttpContext) ->
            task {
                let! html = renderRegistrationForm ()
                SseEvent.broadcast (PatchElements html)
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
                    let! html = renderValidationFeedback errors
                    SseEvent.broadcast (PatchElements html)
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
                            let errorNode =
                                h ("div#registration-result.error", [ Text "Email already registered." ])

                            let! html = Render.asString errorNode
                            SseEvent.broadcast (PatchElements html)
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
                            SseEvent.broadcast (PatchElements html)
                            ctx.Response.StatusCode <- 201
                    else
                        let! html = renderValidationFeedback errors
                        SseEvent.broadcast (PatchElements html)
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
        plugBeforeRouting DefaultFilesExtensions.UseDefaultFiles
        plugBeforeRouting StaticFileExtensions.UseStaticFiles

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
