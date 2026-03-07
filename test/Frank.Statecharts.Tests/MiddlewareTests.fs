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
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.FileProviders
open Microsoft.Extensions.Primitives

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
            [ { Name = "RequireAdmin"
                Predicate =
                  fun ctx ->
                      if ctx.User.IsInRole("admin") then
                          Allowed
                      else
                          Blocked NotAllowed }
              { Name = "CheckOwner"
                Predicate =
                  fun ctx ->
                      let ownerClaim = ctx.User.FindFirst("owner")

                      if not (isNull ownerClaim) && ownerClaim.Value = "true" then
                          Allowed
                      else
                          Blocked NotYourTurn } ] }

// --- Test infrastructure ---

type TestEndpointDataSource(endpoints: Endpoint[]) =
    inherit EndpointDataSource()
    override _.Endpoints = endpoints :> _
    override _.GetChangeToken() = NullChangeToken.Singleton :> _

let buildTestServer
    (resource: Resource)
    (configureServices: IServiceCollection -> unit)
    (configureUser: ClaimsPrincipal option)
    =
    let builder =
        (WebHostBuilder())
            .ConfigureServices(fun services ->
                services.AddRouting() |> ignore
                services.AddLogging() |> ignore
                configureServices services)
            .Configure(fun app ->
                match configureUser with
                | Some user ->
                    app.Use(fun ctx (next: RequestDelegate) ->
                        ctx.User <- user
                        next.Invoke(ctx))
                    |> ignore
                | None -> ()

                app.UseRouting() |> ignore
                app.UseMiddleware<StateMachineMiddleware>() |> ignore

                app.UseEndpoints(fun endpoints ->
                    endpoints.DataSources.Add(TestEndpointDataSource(resource.Endpoints)))
                |> ignore)

    new TestServer(builder)

let adminUser () =
    ClaimsPrincipal(ClaimsIdentity([| Claim(ClaimTypes.Role, "admin"); Claim("owner", "true") |], "test"))

let nonAdminUser () =
    ClaimsPrincipal(ClaimsIdentity([| Claim("owner", "false") |], "test"))

let addStore (services: IServiceCollection) =
    services.AddStateMachineStore<TestState, int>() |> ignore

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

              (withServer res addStore None (fun client ->
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

              (withServer res addStore (Some(nonAdminUser ())) (fun client ->
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
                          [ { Name = "CheckOwner"
                              Predicate =
                                fun ctx ->
                                    let ownerClaim = ctx.User.FindFirst("owner")

                                    if not (isNull ownerClaim) && ownerClaim.Value = "true" then
                                        Allowed
                                    else
                                        Blocked NotYourTurn } ] }

              let res =
                  statefulResource "/turn/{id}" {
                      machine notYourTurnMachine

                      inState (forState Active [ StateHandlerBuilder.post (fun ctx -> ctx.Response.WriteAsync("ok")) ])
                  }

              (withServer res addStore (Some(nonAdminUser ())) (fun client ->
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

              (withServer res addStore None (fun client ->
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

              (withServer res addStore None (fun client ->
                  task {
                      let content = new StringContent("")
                      let! (_response: HttpResponseMessage) = client.PostAsync("/hook/1", content)

                      Expect.isSome capturedEvent "onTransition should have fired"
                      let evt = capturedEvent.Value
                      Expect.equal evt.PreviousState Active "Previous state should be Active"
                      Expect.equal evt.NewState Active "New state should be Active (DoAction stays Active)"
                      Expect.equal evt.Event DoAction "Event should be DoAction"
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

              (withServer res addStore None (fun client ->
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

              (withServer res addStore (Some(adminUser ())) (fun client ->
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

              (withServer res addStore None (fun client ->
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
                          [ { Name = "CustomBlock"
                              Predicate = fun _ -> Blocked(Custom(429, "Rate limited")) } ] }

              let res =
                  statefulResource "/custom/{id}" {
                      machine customGuardMachine

                      inState (forState Active [ StateHandlerBuilder.post (fun _ -> Task.CompletedTask) ])
                  }

              (withServer res addStore None (fun client ->
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

              (withServer res addStore None (fun client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/readonly/1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should succeed"
                      Expect.isFalse transitioned "onTransition should NOT have fired for GET"
                  }))
                  .GetAwaiter()
                  .GetResult() ]
