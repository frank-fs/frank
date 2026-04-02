module MiddlewareTests

open System
open System.Net
open System.Net.Http
open System.Security.Claims
open System.Threading.Tasks
open Expecto
open Frank.Builder
open Frank.Statecharts
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Frank.Tests.Shared.TestEndpointDataSource

// --- Test domain ---

type TestState =
    | Active
    | Completed

type TestEvent =
    | DoAction
    | Complete

let testTransition (state: TestState) (event: TestEvent) (_ctx: int) =
    match state, event with
    | Active, DoAction -> TransitionResult.Transitioned(Active, 1)
    | Active, Complete -> TransitionResult.Transitioned(Completed, 0)
    | Completed, _ -> TransitionResult.Invalid "Cannot act on completed resource"

let testMachine: StateMachine<TestState, TestEvent, int> =
    { Initial = Active
      InitialContext = 0
      Transition = testTransition
      Guards = []
      StateMetadata = Map.empty }

let guardedMachine: StateMachine<TestState, TestEvent, int> =
    { testMachine with
        Guards =
            [ AccessControl(
                  "RequireAdmin",
                  fun ctx ->
                      if ctx.User.IsInRole("admin") then
                          Allowed
                      else
                          Blocked NotAllowed
              )
              AccessControl(
                  "CheckOwner",
                  fun ctx ->
                      let ownerClaim = ctx.User.FindFirst("owner")

                      if not (isNull ownerClaim) && ownerClaim.Value = "true" then
                          Allowed
                      else
                          Blocked NotYourTurn
              ) ] }

// --- Test infrastructure ---

let buildTestServer
    (resource: Resource)
    (configureServices: IServiceCollection -> unit)
    (configureUser: ClaimsPrincipal option)
    =
    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddLogging() |> ignore
    configureServices builder.Services
    let app = builder.Build()

    match configureUser with
    | Some user ->
        (app :> IApplicationBuilder).Use(fun ctx (next: RequestDelegate) ->
            ctx.User <- user
            next.Invoke(ctx))
        |> ignore
    | None -> ()

    app.UseRouting() |> ignore
    (app :> IApplicationBuilder).UseMiddleware<StateMachineMiddleware>() |> ignore

    app.UseEndpoints(fun endpoints ->
        endpoints.DataSources.Add(TestEndpointDataSource(resource.Endpoints)))
    |> ignore

    app.Start()
    app.GetTestServer()

let adminUser () =
    ClaimsPrincipal(ClaimsIdentity([| Claim(ClaimTypes.Role, "admin"); Claim("owner", "true") |], "test"))

let nonAdminUser () =
    ClaimsPrincipal(ClaimsIdentity([| Claim("owner", "false") |], "test"))

let addStore (services: IServiceCollection) =
    services.AddStatechartsStore<TestState, int>() |> ignore

/// Run a test with a TestServer and HttpClient, ensuring proper disposal.
let withServer resource configServices configUser (f: HttpClient -> Task) =
    task {
        let server = buildTestServer resource configServices configUser
        let client = server.CreateClient()

        try
            do! f client
        finally
            client.Dispose()
            server.Dispose()
    }
    :> Task

[<Tests>]
let middlewareTests =
    testList
        "StateMachineMiddleware"
        [ testCase "Non-stateful resource passes through middleware"
          <| fun () ->
              let plainResource =
                  resource "/plain/{id}" { get (RequestDelegate(fun ctx -> ctx.Response.WriteAsync("plain"))) }

              (withServer plainResource ignore None (fun client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/plain/1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should pass through"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.equal body "plain" "Should get handler response"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Returns 405 for disallowed method in current state"
          <| fun () ->
              let res =
                  statefulResource "/items/{id}" {
                      machine testMachine

                      inState (
                          forState Active [ StateHandlerBuilder.post (fun ctx -> ctx.Response.WriteAsync("posted")) ]
                      )

                      inState (
                          forState Completed [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("done")) ]
                      )
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      // Active state only has POST; GET should be 405
                      let! (response: HttpResponseMessage) = client.GetAsync("/items/1")
                      Expect.equal response.StatusCode HttpStatusCode.MethodNotAllowed "Should return 405"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Returns 403 when guard blocks with NotAllowed"
          <| fun () ->
              let res =
                  statefulResource "/guarded/{id}" {
                      machine guardedMachine

                      inState (forState Active [ StateHandlerBuilder.post (fun ctx -> ctx.Response.WriteAsync("ok")) ])
                  }

              (withServer res.Resource addStore (Some(nonAdminUser ())) (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/guarded/1", content)
                      Expect.equal response.StatusCode HttpStatusCode.Forbidden "Should return 403"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Returns 409 when guard blocks with NotYourTurn"
          <| fun () ->
              let notYourTurnMachine =
                  { testMachine with
                      Guards =
                          [ AccessControl(
                                "CheckOwner",
                                fun ctx ->
                                    let ownerClaim = ctx.User.FindFirst("owner")

                                    if not (isNull ownerClaim) && ownerClaim.Value = "true" then
                                        Allowed
                                    else
                                        Blocked NotYourTurn
                            ) ] }

              let res =
                  statefulResource "/turn/{id}" {
                      machine notYourTurnMachine

                      inState (forState Active [ StateHandlerBuilder.post (fun ctx -> ctx.Response.WriteAsync("ok")) ])
                  }

              (withServer res.Resource addStore (Some(nonAdminUser ())) (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/turn/1", content)
                      Expect.equal response.StatusCode HttpStatusCode.Conflict "Should return 409"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Successful handler triggers state transition"
          <| fun () ->
              let res =
                  statefulResource "/action/{id}" {
                      machine testMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx ->
                                    StateMachineContext.setEvent ctx Complete
                                    ctx.Response.WriteAsync("completing")) ]
                      )

                      inState (
                          forState Completed [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("done")) ]
                      )
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      // POST to transition from Active to Completed
                      let content = new StringContent("")
                      let! (postResponse: HttpResponseMessage) = client.PostAsync("/action/1", content)
                      Expect.equal postResponse.StatusCode HttpStatusCode.OK "POST should succeed"

                      // Now GET should work (Completed state has GET handler)
                      let! (getResponse: HttpResponseMessage) = client.GetAsync("/action/1")
                      Expect.equal getResponse.StatusCode HttpStatusCode.OK "GET should succeed after transition"
                      let! body = getResponse.Content.ReadAsStringAsync()
                      Expect.equal body "done" "Should get Completed state handler response"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "onTransition hook fires after successful transition"
          <| fun () ->
              let mutable capturedEvent: TransitionEvent<TestState, TestEvent, int> option = None

              let res =
                  statefulResource "/hook/{id}" {
                      machine testMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx ->
                                    StateMachineContext.setEvent ctx DoAction
                                    Task.CompletedTask) ]
                      )

                      onTransition (fun evt -> capturedEvent <- Some evt)
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (_response: HttpResponseMessage) = client.PostAsync("/hook/1", content)

                      Expect.isSome capturedEvent "onTransition should have fired"
                      let evt = capturedEvent.Value
                      Expect.equal evt.PreviousState Active "Previous state should be Active"
                      Expect.equal evt.NewState Active "New state should be Active (DoAction stays Active)"
                      Expect.equal evt.Event (Some DoAction) "Event should be Some DoAction"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "New instance uses Initial state from machine"
          <| fun () ->
              let res =
                  statefulResource "/new/{id}" {
                      machine testMachine

                      inState (
                          forState Active [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("active")) ]
                      )

                      inState (
                          forState Completed [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("done")) ]
                      )
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      // Request with unknown instance ID - should default to Initial (Active)
                      let! (response: HttpResponseMessage) = client.GetAsync("/new/never-seen-before")
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should use Initial state"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.equal body "active" "Should use Active state handler"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Guards pass for authorized user"
          <| fun () ->
              let res =
                  statefulResource "/auth/{id}" {
                      machine guardedMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx ->
                                    StateMachineContext.setEvent ctx DoAction
                                    ctx.Response.WriteAsync("ok")) ]
                      )
                  }

              (withServer res.Resource addStore (Some(adminUser ())) (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/auth/1", content)
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should allow authorized user"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "TransitionResult.Invalid returns 400 with message"
          <| fun () ->
              let res =
                  statefulResource "/invalid/{id}" {
                      machine testMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx ->
                                    StateMachineContext.setEvent ctx Complete
                                    Task.CompletedTask) ]
                      )

                      inState (
                          forState
                              Completed
                              [ StateHandlerBuilder.post (fun ctx ->
                                    StateMachineContext.setEvent ctx DoAction
                                    Task.CompletedTask) ]
                      )
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      // First POST to transition to Completed
                      let content1 = new StringContent("")
                      let! (_: HttpResponseMessage) = client.PostAsync("/invalid/1", content1)

                      // Second POST should fail with Invalid
                      let content2 = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/invalid/1", content2)
                      Expect.equal response.StatusCode HttpStatusCode.BadRequest "Should return 400"
                      let! body = response.Content.ReadAsStringAsync()

                      Expect.stringContains body "Cannot act on completed resource" "Should contain error message"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Custom BlockReason returns custom status code and message"
          <| fun () ->
              let customGuardMachine =
                  { testMachine with
                      Guards =
                          [ AccessControl("CustomBlock", fun _ -> Blocked(Custom(429, "Rate limited"))) ] }

              let res =
                  statefulResource "/custom/{id}" {
                      machine customGuardMachine

                      inState (forState Active [ StateHandlerBuilder.post (fun _ -> Task.CompletedTask) ])
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/custom/1", content)
                      Expect.equal (int response.StatusCode) 429 "Should return custom status code"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.equal body "Rate limited" "Should return custom message"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "GET without event does not trigger transition"
          <| fun () ->
              let mutable transitioned = false

              let res =
                  statefulResource "/readonly/{id}" {
                      machine testMachine

                      inState (
                          forState Active [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("reading")) ]
                      )

                      onTransition (fun _ -> transitioned <- true)
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/readonly/1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should succeed"
                      Expect.isFalse transitioned "onTransition should NOT have fired for GET"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let accessControlGuardTests =
    testList
        "AccessControl guards (pre-handler)"
        [ testCase "AccessControl guard blocks before handler runs"
          <| fun () ->
              let mutable handlerRan = false

              let blockedMachine =
                  { testMachine with
                      Guards = [ AccessControl("AlwaysBlock", fun _ -> Blocked NotAllowed) ] }

              let res =
                  statefulResource "/ac-block/{id}" {
                      machine blockedMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx ->
                                    handlerRan <- true
                                    ctx.Response.WriteAsync("should not reach")) ]
                      )
                  }

              (withServer res.Resource addStore (Some(adminUser ())) (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/ac-block/1", content)
                      Expect.equal response.StatusCode HttpStatusCode.Forbidden "Should return 403"
                      Expect.isFalse handlerRan "Handler should NOT have run when AccessControl guard blocks"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "AccessControl guard passes allows handler to proceed"
          <| fun () ->
              let mutable handlerRan = false

              let allowedMachine =
                  { testMachine with
                      Guards = [ AccessControl("AlwaysAllow", fun _ -> Allowed) ] }

              let res =
                  statefulResource "/ac-allow/{id}" {
                      machine allowedMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx ->
                                    handlerRan <- true
                                    ctx.Response.WriteAsync("ok")) ]
                      )
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/ac-allow/1", content)
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                      Expect.isTrue handlerRan "Handler should have run when AccessControl guard allows"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let eventValidationGuardTests =
    testList
        "EventValidation guards (post-handler)"
        [ testCase "EventValidation guard receives actual event value"
          <| fun () ->
              let mutable receivedEvent: TestEvent option = None

              let evMachine =
                  { testMachine with
                      Guards =
                          [ EventValidation(
                                "CaptureEvent",
                                fun ctx ->
                                    receivedEvent <- Some ctx.Event
                                    Allowed
                            ) ] }

              let res =
                  statefulResource "/ev-capture/{id}" {
                      machine evMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx ->
                                    StateMachineContext.setEvent ctx DoAction
                                    ctx.Response.WriteAsync("ok")) ]
                      )
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/ev-capture/1", content)
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                      Expect.equal receivedEvent (Some DoAction) "EventValidation guard should receive actual event"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "EventValidation guard blocking suppresses transition"
          <| fun () ->
              let mutable transitioned = false

              let evBlockMachine =
                  { testMachine with
                      Guards =
                          [ EventValidation(
                                "BlockEvent",
                                fun _ -> Blocked PreconditionFailed
                            ) ] }

              let res =
                  statefulResource "/ev-block/{id}" {
                      machine evBlockMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx ->
                                    StateMachineContext.setEvent ctx Complete
                                    Task.CompletedTask) ]
                      )

                      inState (
                          forState Completed [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("done")) ]
                      )

                      onTransition (fun _ -> transitioned <- true)
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/ev-block/1", content)
                      Expect.equal response.StatusCode (HttpStatusCode.PreconditionFailed) "Should return 412"
                      Expect.isFalse transitioned "Transition should NOT have fired when event guard blocks"

                      // State should still be Active (transition was suppressed)
                      let! (getResponse: HttpResponseMessage) = client.GetAsync("/ev-block/1")
                      Expect.equal getResponse.StatusCode HttpStatusCode.MethodNotAllowed "Should still be in Active state (no GET handler for Active)"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Mixed AccessControl and EventValidation guards in one list"
          <| fun () ->
              let mutable handlerRan = false
              let mutable eventGuardCalled = false

              let mixedMachine =
                  { testMachine with
                      Guards =
                          [ AccessControl(
                                "AllowAccess",
                                fun _ -> Allowed
                            )
                            EventValidation(
                                "CheckEvent",
                                fun ctx ->
                                    eventGuardCalled <- true
                                    match ctx.Event with
                                    | DoAction -> Allowed
                                    | Complete -> Blocked PreconditionFailed
                            ) ] }

              let res =
                  statefulResource "/mixed/{id}" {
                      machine mixedMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx ->
                                    handlerRan <- true
                                    StateMachineContext.setEvent ctx DoAction
                                    ctx.Response.WriteAsync("ok")) ]
                      )
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/mixed/1", content)
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"
                      Expect.isTrue handlerRan "Handler should have run"
                      Expect.isTrue eventGuardCalled "EventValidation guard should have been called"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "EventValidation guards are skipped on GET (no event set)"
          <| fun () ->
              let mutable eventGuardCalled = false

              let evMachine =
                  { testMachine with
                      Guards =
                          [ EventValidation(
                                "ShouldNotRun",
                                fun _ ->
                                    eventGuardCalled <- true
                                    Blocked PreconditionFailed
                            ) ] }

              let res =
                  statefulResource "/ev-skip/{id}" {
                      machine evMachine

                      inState (
                          forState Active [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("reading")) ]
                      )
                  }

              (withServer res.Resource addStore None (fun client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/ev-skip/1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should succeed"
                      Expect.isFalse eventGuardCalled "EventValidation guard should NOT be called on GET"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let httpComplianceTests =
    testList
        "HTTP compliance (RFC 9110)"
        [ testCase "405 includes Allow header when ResolveHandlers returns None"
          <| fun () ->
              // Active registered with empty handlers — ResolveHandlers returns None
              let res =
                  statefulResource "/no-handlers/{id}" {
                      machine testMachine
                      inState (forState Active [])

                      inState (
                          forState
                              Completed
                              [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("done")) ]
                      )
                  }

              (withServer res addStore None (fun client ->
                  task {
                      // Initial state is Active with no handlers — hits the None branch
                      let! (response: HttpResponseMessage) = client.GetAsync("/no-handlers/1")
                      Expect.equal response.StatusCode HttpStatusCode.MethodNotAllowed "Should return 405"

                      // RFC 9110 Section 15.5.6: Allow header MUST be present on 405
                      Expect.isTrue
                          (response.Content.Headers.Contains("Allow"))
                          "Allow header must be present on 405 even when no handlers exist"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "405 includes Allow header listing available methods"
          <| fun () ->
              let res =
                  statefulResource "/method-mismatch/{id}" {
                      machine testMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx -> ctx.Response.WriteAsync("posted")) ]
                      )

                      inState (
                          forState
                              Completed
                              [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("done")) ]
                      )
                  }

              (withServer res addStore None (fun client ->
                  task {
                      // Active state only has POST; GET should be 405 with Allow: POST
                      let! (response: HttpResponseMessage) = client.GetAsync("/method-mismatch/1")
                      Expect.equal response.StatusCode HttpStatusCode.MethodNotAllowed "Should return 405"

                      // Allow header must list available methods
                      let allowValues = response.Content.Headers.Allow |> Seq.toList
                      Expect.contains allowValues "POST" "Allow should list POST"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "202 response includes Content-Location after transition"
          <| fun () ->
              let res =
                  statefulResource "/cloc/{id}" {
                      machine testMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.post (fun ctx ->
                                    StateMachineContext.setEvent ctx DoAction
                                    ctx.Response.StatusCode <- 202
                                    Task.CompletedTask) ]
                      )
                  }

              (withServer res addStore None (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/cloc/1", content)
                      Expect.equal (int response.StatusCode) 202 "Should return 202"

                      // RFC 9110 Section 15.3.3: 202 should include Content-Location
                      Expect.isNotNull
                          response.Content.Headers.ContentLocation
                          "Content-Location must be present on 202 after transition"

                      Expect.stringContains
                          (response.Content.Headers.ContentLocation.ToString())
                          "/cloc/1"
                          "Content-Location should point to resource URI"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Successful response includes Allow header"
          <| fun () ->
              let res =
                  statefulResource "/with-allow/{id}" {
                      machine testMachine

                      inState (
                          forState
                              Active
                              [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("reading"))
                                StateHandlerBuilder.post (fun ctx -> ctx.Response.WriteAsync("posting")) ]
                      )
                  }

              (withServer res addStore None (fun client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/with-allow/1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      // Allow header should be present on all responses for HATEOAS
                      let allowValues = response.Content.Headers.Allow |> Seq.toList
                      Expect.contains allowValues "GET" "Allow should list GET"
                      Expect.contains allowValues "POST" "Allow should list POST"
                  }))
                  .GetAwaiter()
                  .GetResult() ]
