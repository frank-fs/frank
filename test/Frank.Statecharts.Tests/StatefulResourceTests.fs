module StatefulResourceTests

open System
open System.Net
open System.Net.Http
open System.Security.Claims
open System.Threading.Tasks
open FSharp.Reflection
open Expecto
open Frank.Builder
open Frank.Statecharts
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection

/// Extract the DU case name for use as a state key, matching StateKeyExtractor behavior.
let stateKeyOf<'S> (state: 'S) : string =
    if FSharpType.IsUnion(typeof<'S>, true) then
        let tagReader = FSharpValue.PreComputeUnionTagReader(typeof<'S>)
        let cases = FSharpType.GetUnionCases(typeof<'S>, true)
        let caseNames = cases |> Array.map (fun c -> c.Name)
        caseNames.[tagReader (box state)]
    else
        state.ToString()

// === Tic-tac-toe domain ===

type TicTacToeState =
    | XTurn
    | OTurn
    | Won of winner: string
    | Draw

type TicTacToeEvent = MakeMove of position: int

let gameTransition (state: TicTacToeState) (_event: TicTacToeEvent) (moveCount: int) =
    match state with
    | XTurn ->
        let n = moveCount + 1

        if n >= 5 then
            TransitionResult.Transitioned(Won "X", n)
        else
            TransitionResult.Transitioned(OTurn, n)
    | OTurn ->
        let n = moveCount + 1

        if n >= 9 then
            TransitionResult.Transitioned(Draw, n)
        else
            TransitionResult.Transitioned(XTurn, n)
    | Won _ -> TransitionResult.Invalid "Game already over"
    | Draw -> TransitionResult.Invalid "Game already over"

let turnGuard: Guard<TicTacToeState, TicTacToeEvent, int> =
    AccessControl(
        "TurnGuard",
        fun ctx ->
            match ctx.CurrentState with
            | XTurn ->
                if ctx.User.HasClaim("player", "X") then
                    Allowed
                elif ctx.User.HasClaim("player", "O") then
                    Blocked NotYourTurn
                else
                    Blocked Forbidden
            | OTurn ->
                if ctx.User.HasClaim("player", "O") then
                    Allowed
                elif ctx.User.HasClaim("player", "X") then
                    Blocked NotYourTurn
                else
                    Blocked Forbidden
            | Won _
            | Draw -> Allowed
    )

let gameMachine: StateMachine<TicTacToeState, TicTacToeEvent, int> =
    { Initial = XTurn
      InitialContext = 0
      Transition = gameTransition
      Guards = []
      StateMetadata = Map.empty }

let guardedGameMachine =
    { gameMachine with
        Guards = [ turnGuard ] }

// === Handlers ===

let handleMove (ctx: HttpContext) : Task =
    StateMachineContext.setEvent ctx (MakeMove 0)
    Task.CompletedTask

let getGameState (ctx: HttpContext) : Task = ctx.Response.WriteAsync("game state")

/// Handler that discovers per-state affordances from endpoint metadata.
let getWithAffordances (myState: TicTacToeState) (ctx: HttpContext) : Task =
    task {
        let endpoint = ctx.GetEndpoint()
        let metadata = endpoint.Metadata.GetMetadata<StateMachineMetadata>()
        let key = stateKeyOf myState

        let methods =
            match Map.tryFind key metadata.StateHandlerMap with
            | Some handlers -> handlers |> List.map fst
            | None -> []

        let methodsStr = String.Join(",", methods)
        do! ctx.Response.WriteAsync($"state={key};methods={methodsStr}")
    }
    :> Task

// === Resource builders ===

let buildGameResource (sm: StateMachine<TicTacToeState, TicTacToeEvent, int>) =
    statefulResource "/games/{gameId}" {
        machine sm
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
        inState (forState XTurn [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ])
        inState (forState OTurn [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ])
        inState (forState (Won "X") [ StateHandlerBuilder.get getGameState ])
        inState (forState Draw [ StateHandlerBuilder.get getGameState ])
    }

let buildAffordanceResource () =
    statefulResource "/games/{gameId}" {
        machine gameMachine
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)

        inState (
            forState
                XTurn
                [ StateHandlerBuilder.get (getWithAffordances XTurn)
                  StateHandlerBuilder.post handleMove ]
        )

        inState (
            forState
                OTurn
                [ StateHandlerBuilder.get (getWithAffordances OTurn)
                  StateHandlerBuilder.post handleMove ]
        )

        inState (forState (Won "X") [ StateHandlerBuilder.get (getWithAffordances (Won "X")) ])
        inState (forState Draw [ StateHandlerBuilder.get (getWithAffordances Draw) ])
    }

// === Test infrastructure ===

let playerX () =
    ClaimsPrincipal(ClaimsIdentity([| Claim("player", "X") |], "test"))

let playerO () =
    ClaimsPrincipal(ClaimsIdentity([| Claim("player", "O") |], "test"))

let spectator () =
    ClaimsPrincipal(ClaimsIdentity([||], "test"))

let addGameStore (services: IServiceCollection) =
    services.AddStatechartsStore<TicTacToeState, int>() |> ignore

let withGameServer (resource: Resource) configUser (f: TestServer -> HttpClient -> Task) =
    task {
        let server = MiddlewareTests.buildTestServer resource addGameStore configUser
        let client = server.CreateClient()

        try
            do! f server client
        finally
            client.Dispose()
            server.Dispose()
    }
    :> Task

let prePopulateState (server: TestServer) instanceId (state: TicTacToeState) (moveCount: int) =
    let store =
        server.Services.GetRequiredService<IStatechartsStore<TicTacToeState, int>>()

    (store.Save
        instanceId
        { State = state
          Context = moveCount
          HierarchyConfig = ActiveStateConfiguration.empty
          HistoryRecord = HistoryRecord.empty })
        .GetAwaiter()
        .GetResult()

// === Tests ===

[<Tests>]
let methodFilteringTests =
    testList
        "State-dependent methods"
        [ testCase "POST allowed in XTurn state"
          <| fun () ->
              let res = buildGameResource gameMachine

              (withGameServer res.Resource None (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal response.StatusCode HttpStatusCode.OK "POST should be allowed in XTurn"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "GET allowed in XTurn state"
          <| fun () ->
              let res = buildGameResource gameMachine

              (withGameServer res.Resource None (fun _server client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should be allowed in XTurn"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.equal body "game state" "Should return game state"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "DELETE returns 405 in XTurn state"
          <| fun () ->
              let res = buildGameResource gameMachine

              (withGameServer res.Resource None (fun _server client ->
                  task {
                      let! (response: HttpResponseMessage) = client.DeleteAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.MethodNotAllowed "DELETE should be 405"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "POST returns 405 in Won state"
          <| fun () ->
              let res = buildGameResource gameMachine

              (withGameServer res.Resource None (fun server client ->
                  task {
                      prePopulateState server "game1" (Won "X") 5
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      Expect.equal
                          response.StatusCode
                          HttpStatusCode.MethodNotAllowed
                          "POST should be 405 in Won state"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "GET allowed in Won state"
          <| fun () ->
              let res = buildGameResource gameMachine

              (withGameServer res.Resource None (fun server client ->
                  task {
                      prePopulateState server "game1" (Won "X") 5
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should work in Won state"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let guardTests =
    testList
        "Guard evaluation"
        [ testCase "Player X can POST in XTurn state"
          <| fun () ->
              let res = buildGameResource guardedGameMachine

              (withGameServer res.Resource (Some(playerX ())) (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal response.StatusCode HttpStatusCode.OK "Player X should be allowed in XTurn"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Player O gets 409 in XTurn state"
          <| fun () ->
              let res = buildGameResource guardedGameMachine

              (withGameServer res.Resource (Some(playerO ())) (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      Expect.equal response.StatusCode HttpStatusCode.Conflict "Player O should get 409 NotYourTurn"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Spectator gets 403 in XTurn state"
          <| fun () ->
              let res = buildGameResource guardedGameMachine

              (withGameServer res.Resource (Some(spectator ())) (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal response.StatusCode HttpStatusCode.Forbidden "Spectator should get 403"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Player O can POST in OTurn state"
          <| fun () ->
              let res = buildGameResource guardedGameMachine

              (withGameServer res.Resource (Some(playerO ())) (fun server client ->
                  task {
                      prePopulateState server "game1" OTurn 1
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal response.StatusCode HttpStatusCode.OK "Player O should be allowed in OTurn"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Player X gets 409 in OTurn state"
          <| fun () ->
              let res = buildGameResource guardedGameMachine

              (withGameServer res.Resource (Some(playerX ())) (fun server client ->
                  task {
                      prePopulateState server "game1" OTurn 1
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      Expect.equal response.StatusCode HttpStatusCode.Conflict "Player X should get 409 in OTurn"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Spectator gets 403 in OTurn state"
          <| fun () ->
              let res = buildGameResource guardedGameMachine

              (withGameServer res.Resource (Some(spectator ())) (fun server client ->
                  task {
                      prePopulateState server "game1" OTurn 1
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal response.StatusCode HttpStatusCode.Forbidden "Spectator should get 403 in OTurn"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Guards allow GET in Won state with guarded machine"
          <| fun () ->
              let res = buildGameResource guardedGameMachine

              (withGameServer res.Resource (Some(spectator ())) (fun server client ->
                  task {
                      prePopulateState server "game1" (Won "X") 5
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "Spectator should GET Won state"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Context-aware guard blocks when move count exceeds limit"
          <| fun () ->
              let moveCountLimitMachine =
                  { gameMachine with
                      Guards =
                          [ AccessControl(
                                "MoveCountLimit",
                                fun ctx ->
                                    if ctx.Context >= 4 then
                                        Blocked(Custom(429, "Too many moves"))
                                    else
                                        Allowed
                            ) ] }

              let res = buildGameResource moveCountLimitMachine

              (withGameServer res.Resource None (fun server client ->
                  task {
                      prePopulateState server "game1" XTurn 4
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal (int response.StatusCode) 429 "Should return 429 when move count at limit"
                      let ct = response.Content.Headers.ContentType.MediaType
                      Expect.equal ct "application/problem+json" "Should be problem+json"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.stringContains body "Too many moves" "Should return custom message in detail"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Context-aware guard allows when move count below limit"
          <| fun () ->
              let moveCountLimitMachine =
                  { gameMachine with
                      Guards =
                          [ AccessControl(
                                "MoveCountLimit",
                                fun ctx ->
                                    if ctx.Context >= 4 then
                                        Blocked(Custom(429, "Too many moves"))
                                    else
                                        Allowed
                            ) ] }

              let res = buildGameResource moveCountLimitMachine

              (withGameServer res.Resource None (fun server client ->
                  task {
                      prePopulateState server "game1" XTurn 2
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should allow when move count below limit"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let transitionHookTests =
    testList
        "Transition hooks"
        [ testCase "onTransition fires after successful move"
          <| fun () ->
              let mutable capturedEvent: TransitionEvent<TicTacToeState, TicTacToeEvent, int> option =
                  None

              let res =
                  statefulResource "/games/{gameId}" {
                      machine gameMachine
                      resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
                      onTransition (fun evt -> capturedEvent <- Some evt)

                      inState (
                          forState XTurn [ StateHandlerBuilder.post handleMove; StateHandlerBuilder.get getGameState ]
                      )

                      inState (
                          forState OTurn [ StateHandlerBuilder.post handleMove; StateHandlerBuilder.get getGameState ]
                      )
                  }

              (withGameServer res.Resource None (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      Expect.isSome capturedEvent "onTransition should have fired"
                      let evt = capturedEvent.Value
                      Expect.equal evt.PreviousState XTurn "Previous state should be XTurn"
                      Expect.equal evt.NewState OTurn "New state should be OTurn"
                      Expect.equal evt.Event (Some(MakeMove 0)) "Event should be Some(MakeMove 0)"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "onTransition does not fire on blocked request"
          <| fun () ->
              let mutable fired = false

              let res =
                  statefulResource "/games/{gameId}" {
                      machine guardedGameMachine
                      resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
                      onTransition (fun _ -> fired <- true)
                      inState (forState XTurn [ StateHandlerBuilder.post handleMove ])
                  }

              (withGameServer res.Resource (Some(spectator ())) (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.isFalse fired "onTransition should NOT fire on blocked request"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Store reflects state after transition"
          <| fun () ->
              let res = buildGameResource gameMachine

              (withGameServer res.Resource None (fun server client ->
                  task {
                      let content = new StringContent("")
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      let store =
                          server.Services.GetRequiredService<IStatechartsStore<TicTacToeState, int>>()

                      let! stateOpt = store.Load "game1"
                      Expect.isSome stateOpt "Store should have state for game1"
                      let snapshot = stateOpt.Value
                      Expect.equal snapshot.State OTurn "State should be OTurn after first move"
                      Expect.equal snapshot.Context 1 "Move count should be 1"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let affordanceTests =
    testList
        "Filtered affordances"
        [ testCase "Handler discovers GET and POST in XTurn state"
          <| fun () ->
              let res = buildAffordanceResource ()

              (withGameServer res.Resource None (fun _server client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should succeed"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.stringContains body "GET" "Should list GET as available"
                      Expect.stringContains body "POST" "Should list POST as available"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Handler discovers only GET in Won state"
          <| fun () ->
              let res = buildAffordanceResource ()

              (withGameServer res.Resource None (fun server client ->
                  task {
                      prePopulateState server "game1" (Won "X") 5
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should succeed"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.stringContains body "GET" "Should list GET as available"
                      Expect.isFalse (body.Contains("POST")) "Should NOT list POST in Won state"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let storeLifecycleTests =
    testList
        "Store lifecycle"
        [ testCase "Full game: create, transition through states, verify final"
          <| fun () ->
              let res = buildGameResource gameMachine

              (withGameServer res.Resource None (fun server client ->
                  task {
                      let store =
                          server.Services.GetRequiredService<IStatechartsStore<TicTacToeState, int>>()

                      // Move 1: XTurn -> OTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      let! s1 = store.Load "game1"
                      Expect.equal s1.Value.State OTurn "After move 1: OTurn"

                      // Move 2: OTurn -> XTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      let! s2 = store.Load "game1"
                      Expect.equal s2.Value.State XTurn "After move 2: XTurn"

                      // Move 3: XTurn -> OTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      let! s3 = store.Load "game1"
                      Expect.equal s3.Value.State OTurn "After move 3: OTurn"

                      // Move 4: OTurn -> XTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      let! s4 = store.Load "game1"
                      Expect.equal s4.Value.State XTurn "After move 4: XTurn"

                      // Move 5: XTurn -> Won "X" (moveCount >= 5)
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      let! s5 = store.Load "game1"
                      Expect.equal s5.Value.State (Won "X") "After move 5: Won X"
                      Expect.equal s5.Value.Context 5 "Move count should be 5"

                      // Won state blocks POST (405)
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))

                      Expect.equal
                          response.StatusCode
                          HttpStatusCode.MethodNotAllowed
                          "POST should be 405 in Won state"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let multipleInstanceTests =
    testList
        "Multiple instances"
        [ testCase "Two game instances maintain independent state"
          <| fun () ->
              let res = buildGameResource gameMachine

              (withGameServer res.Resource None (fun server client ->
                  task {
                      let store =
                          server.Services.GetRequiredService<IStatechartsStore<TicTacToeState, int>>()

                      // Move game1: XTurn -> OTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      // Move game2: XTurn -> OTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game2", new StringContent(""))
                      // Move game2 again: OTurn -> XTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game2", new StringContent(""))

                      // game1 should be OTurn (1 move)
                      let! s1 = store.Load "game1"
                      Expect.equal s1.Value.State OTurn "game1 should be OTurn"
                      Expect.equal s1.Value.Context 1 "game1 move count should be 1"

                      // game2 should be XTurn (2 moves)
                      let! s2 = store.Load "game2"
                      Expect.equal s2.Value.State XTurn "game2 should be XTurn"
                      Expect.equal s2.Value.Context 2 "game2 move count should be 2"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let transitionBlockedTests =
    testList
        "TransitionResult.Blocked from transition function"
        [ testCase "Transition returning Blocked maps to correct HTTP status"
          <| fun () ->
              let blockingTransition (state: TicTacToeState) (_event: TicTacToeEvent) (_moveCount: int) =
                  match state with
                  | XTurn -> TransitionResult.Blocked(PreconditionFailed)
                  | _ -> TransitionResult.Transitioned(OTurn, 0)

              let blockingMachine =
                  { gameMachine with
                      Transition = blockingTransition }

              let res = buildGameResource blockingMachine

              (withGameServer res.Resource None (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      Expect.equal
                          response.StatusCode
                          HttpStatusCode.PreconditionFailed
                          "Transition Blocked(PreconditionFailed) should return 412"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Transition returning Blocked with Custom reason"
          <| fun () ->
              let blockingTransition (state: TicTacToeState) (_event: TicTacToeEvent) (_moveCount: int) =
                  match state with
                  | XTurn -> TransitionResult.Blocked(Custom(503, "Service unavailable"))
                  | _ -> TransitionResult.Transitioned(OTurn, 0)

              let blockingMachine =
                  { gameMachine with
                      Transition = blockingTransition }

              let res = buildGameResource blockingMachine

              (withGameServer res.Resource None (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal (int response.StatusCode) 503 "Should return custom 503"
                      let ct = response.Content.Headers.ContentType.MediaType
                      Expect.equal ct "application/problem+json" "Should be problem+json"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.stringContains body "Service unavailable" "Should return custom message in detail"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let responseStartedTests =
    testList
        "Response already started"
        [ testCase "Late transition failure logged when response already started"
          <| fun () ->
              let blockingTransition (_state: TicTacToeState) (_event: TicTacToeEvent) (_moveCount: int) =
                  TransitionResult.Blocked(InvalidTransition)

              let blockingMachine =
                  { gameMachine with
                      Transition = blockingTransition }

              let handlerThatWritesBody (ctx: HttpContext) : Task =
                  task {
                      do! ctx.Response.WriteAsync("response body written")
                      StateMachineContext.setEvent ctx (MakeMove 0)
                  }
                  :> Task

              let res =
                  statefulResource "/games/{gameId}" {
                      machine blockingMachine
                      resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)

                      inState (
                          forState
                              XTurn
                              [ StateHandlerBuilder.post handlerThatWritesBody
                                StateHandlerBuilder.get getGameState ]
                      )
                  }

              // Response should still be 200 because it was already started by the handler
              (withGameServer res.Resource None (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      Expect.equal
                          response.StatusCode
                          HttpStatusCode.OK
                          "Status should be 200 because response already started"

                      let! body = response.Content.ReadAsStringAsync()
                      Expect.stringContains body "response body written" "Handler body should be in response"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let multipleGuardTests =
    testList
        "Multiple guards"
        [ testCase "First blocking guard short-circuits evaluation"
          <| fun () ->
              let mutable secondGuardCalled = false

              let firstGuard: Guard<TicTacToeState, TicTacToeEvent, int> =
                  AccessControl("AlwaysBlocks", fun _ -> Blocked Forbidden)

              let secondGuard: Guard<TicTacToeState, TicTacToeEvent, int> =
                  AccessControl(
                      "NeverReached",
                      fun _ ->
                          secondGuardCalled <- true
                          Allowed
                  )

              let multiGuardMachine =
                  { gameMachine with
                      Guards = [ firstGuard; secondGuard ] }

              let res = buildGameResource multiGuardMachine

              (withGameServer res.Resource None (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal response.StatusCode HttpStatusCode.Forbidden "First guard should block with 403"
                      Expect.isFalse secondGuardCalled "Second guard should NOT be called"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "All guards pass when none block"
          <| fun () ->
              let mutable bothCalled = 0

              let guard1: Guard<TicTacToeState, TicTacToeEvent, int> =
                  AccessControl(
                      "PassOne",
                      fun _ ->
                          bothCalled <- bothCalled + 1
                          Allowed
                  )

              let guard2: Guard<TicTacToeState, TicTacToeEvent, int> =
                  AccessControl(
                      "PassTwo",
                      fun _ ->
                          bothCalled <- bothCalled + 1
                          Allowed
                  )

              let multiGuardMachine =
                  { gameMachine with
                      Guards = [ guard1; guard2 ] }

              let res = buildGameResource multiGuardMachine

              (withGameServer res.Resource None (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should pass when all guards allow"
                      Expect.equal bothCalled 2 "Both guards should have been evaluated"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let observerResilienceTests =
    testList
        "Observer error resilience"
        [ testCase "Throwing observer does not break response"
          <| fun () ->
              let mutable secondObserverCalled = false

              let res =
                  statefulResource "/games/{gameId}" {
                      machine gameMachine
                      resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
                      onTransition (fun _ -> failwith "Observer explosion!")
                      onTransition (fun _ -> secondObserverCalled <- true)

                      inState (
                          forState XTurn [ StateHandlerBuilder.post handleMove; StateHandlerBuilder.get getGameState ]
                      )

                      inState (
                          forState OTurn [ StateHandlerBuilder.post handleMove; StateHandlerBuilder.get getGameState ]
                      )
                  }

              (withGameServer res.Resource None (fun server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      Expect.equal
                          response.StatusCode
                          HttpStatusCode.OK
                          "Response should succeed despite observer error"

                      // Verify state was still persisted
                      let store =
                          server.Services.GetRequiredService<IStatechartsStore<TicTacToeState, int>>()

                      let! stateOpt = store.Load "game1"
                      Expect.equal stateOpt.Value.State OTurn "Transition should still be persisted"

                      // Verify second observer was still called (observers are independent)
                      Expect.isTrue secondObserverCalled "Second observer should run despite first throwing"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

// === Multi-level statechart domain (mirrors tic-tac-toe SCXML) ===
//
// The SCXML defines:
//   Playing (parallel)
//     ├── GamePlay: XTurn | OTurn | Won | Draw
//     └── PlayerIdentity: Unassigned | XOnly | OOnly | BothAssigned
//   Disposed (final)
//
// F# models the hierarchy via nested DU for the state tree
// and folds the parallel PlayerIdentity region into context.

[<Struct>]
type PlayerAssignment =
    | Unassigned
    | XOnly of xId: string
    | OOnly of oId: string
    | BothAssigned of playerX: string * playerO: string

type PlayingSubState =
    | XTurn
    | OTurn
    | Won of winner: string
    | Draw

type HierarchicalGameState =
    | Playing of PlayingSubState
    | Disposed

type HierarchicalGameEvent =
    | MakeMove of player: string * position: int
    | DisposeGame of userId: string

type HierarchicalContext =
    { MoveCount: int
      Assignment: PlayerAssignment }

let initialContext =
    { MoveCount = 0
      Assignment = Unassigned }

let assignPlayer (userId: string) (isXMove: bool) (assignment: PlayerAssignment) =
    match assignment, isXMove with
    | Unassigned, true -> XOnly userId
    | Unassigned, false -> OOnly userId
    | XOnly xId, false when xId <> userId -> BothAssigned(xId, userId)
    | OOnly oId, true when oId <> userId -> BothAssigned(userId, oId)
    | other, _ -> other

let hierarchicalTransition (state: HierarchicalGameState) (event: HierarchicalGameEvent) (ctx: HierarchicalContext) =
    match state, event with
    // Transitions within Playing (sub-state changes)
    | Playing XTurn, MakeMove(player, _) ->
        let newAssignment = assignPlayer player true ctx.Assignment
        let n = ctx.MoveCount + 1

        let newCtx =
            { MoveCount = n
              Assignment = newAssignment }

        if n >= 5 then
            TransitionResult.Transitioned(Playing(Won "X"), newCtx)
        else
            TransitionResult.Transitioned(Playing OTurn, newCtx)

    | Playing OTurn, MakeMove(player, _) ->
        let newAssignment = assignPlayer player false ctx.Assignment
        let n = ctx.MoveCount + 1

        let newCtx =
            { MoveCount = n
              Assignment = newAssignment }

        if n >= 9 then
            TransitionResult.Transitioned(Playing Draw, newCtx)
        else
            TransitionResult.Transitioned(Playing XTurn, newCtx)

    // Transition OUT of Playing to Disposed (top-level state change)
    | Playing _, DisposeGame _ -> TransitionResult.Transitioned(Disposed, ctx)

    // Terminal states block all transitions
    | Playing(Won _), MakeMove _ -> TransitionResult.Invalid "Game already over"
    | Playing Draw, MakeMove _ -> TransitionResult.Invalid "Game is a draw"
    | Disposed, _ -> TransitionResult.Invalid "Game disposed"

let hierarchicalTurnGuard: Guard<HierarchicalGameState, HierarchicalGameEvent, HierarchicalContext> =
    AccessControl(
        "HierarchicalTurnGuard",
        fun ctx ->
            match ctx.CurrentState with
            | Playing XTurn ->
                if ctx.User.HasClaim("player", "X") then
                    Allowed
                elif ctx.User.HasClaim("player", "O") then
                    Blocked NotYourTurn
                else
                    Blocked Forbidden
            | Playing OTurn ->
                if ctx.User.HasClaim("player", "O") then
                    Allowed
                elif ctx.User.HasClaim("player", "X") then
                    Blocked NotYourTurn
                else
                    Blocked Forbidden
            | _ -> Allowed
    )

/// Guard that checks player assignment from context (parallel region data)
let participantGuard: Guard<HierarchicalGameState, HierarchicalGameEvent, HierarchicalContext> =
    AccessControl(
        "ParticipantGuard",
        fun ctx ->
            match ctx.CurrentState with
            | Playing(Won _)
            | Playing Draw
            | Disposed -> Allowed // anyone can view terminal states
            | Playing _ ->
                match ctx.Context.Assignment with
                | Unassigned -> Allowed // open game, anyone can join
                | XOnly _ -> Allowed // one slot open
                | OOnly _ -> Allowed
                | BothAssigned(xId, oId) ->
                    let userId = ctx.User.FindFirst("userId")

                    if isNull userId then Blocked Forbidden
                    elif userId.Value = xId || userId.Value = oId then Allowed
                    else Blocked(Custom(403, "Game is full"))
    )

let hierarchicalMachine: StateMachine<HierarchicalGameState, HierarchicalGameEvent, HierarchicalContext> =
    { Initial = Playing XTurn
      InitialContext = initialContext
      Transition = hierarchicalTransition
      Guards = [ hierarchicalTurnGuard ]
      StateMetadata = Map.empty }

let hierarchicalMachineWithParticipant =
    { hierarchicalMachine with
        Guards = [ hierarchicalTurnGuard; participantGuard ] }

let handleHierarchicalMove (ctx: HttpContext) : Task =
    StateMachineContext.setEvent ctx (MakeMove("X", 0))
    Task.CompletedTask

let handleHierarchicalMoveO (ctx: HttpContext) : Task =
    StateMachineContext.setEvent ctx (MakeMove("O", 0))
    Task.CompletedTask

let handleDispose (ctx: HttpContext) : Task =
    StateMachineContext.setEvent ctx (DisposeGame "user1")
    Task.CompletedTask

let getHierarchicalState (ctx: HttpContext) : Task =
    ctx.Response.WriteAsync("hierarchical state")

/// Combined handler for all Playing sub-states. POST dispatches based on actual sub-state
/// since parameterized key extraction maps all Playing variants to the same key.
let handlePlayingPost (ctx: HttpContext) : Task =
    let feature = ctx.Features.Get<IStatechartFeature<HierarchicalGameState, HierarchicalContext>>()
    let state = feature.State.Value

    match state with
    | Playing XTurn -> handleHierarchicalMove ctx
    | Playing OTurn -> handleHierarchicalMoveO ctx
    | _ ->
        // POST not meaningful for Won/Draw sub-states -- middleware handles 405
        // but since handlers are merged, we just return completed
        Task.CompletedTask

let handlePlayingDelete (ctx: HttpContext) : Task =
    let feature = ctx.Features.Get<IStatechartFeature<HierarchicalGameState, HierarchicalContext>>()
    let state = feature.State.Value

    match state with
    | Playing Draw -> handleDispose ctx
    | _ -> Task.CompletedTask

let buildHierarchicalResource (sm: StateMachine<HierarchicalGameState, HierarchicalGameEvent, HierarchicalContext>) =
    statefulResource "/hgames/{gameId}" {
        machine sm
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)

        // All Playing sub-states now share the same key ("Playing") due to
        // parameterized DU case-name extraction. Handlers dispatch internally.
        inState (
            forState
                (Playing XTurn)
                [ StateHandlerBuilder.get getHierarchicalState
                  StateHandlerBuilder.post handlePlayingPost
                  StateHandlerBuilder.delete handlePlayingDelete ]
        )

        inState (forState Disposed [ StateHandlerBuilder.get getHierarchicalState ])
    }

let addHierarchicalStore (services: IServiceCollection) =
    services.AddStatechartsStore<HierarchicalGameState, HierarchicalContext>()
    |> ignore

let withHierarchicalServer (resource: Resource) configUser (f: TestServer -> HttpClient -> Task) =
    task {
        let server =
            MiddlewareTests.buildTestServer resource addHierarchicalStore configUser

        let client = server.CreateClient()

        try
            do! f server client
        finally
            client.Dispose()
            server.Dispose()
    }
    :> Task

let prePopulateHierarchical (server: TestServer) instanceId (state: HierarchicalGameState) (ctx: HierarchicalContext) =
    let store =
        server.Services.GetRequiredService<IStatechartsStore<HierarchicalGameState, HierarchicalContext>>()

    (store.Save
        instanceId
        { State = state
          Context = ctx
          HierarchyConfig = ActiveStateConfiguration.empty
          HistoryRecord = HistoryRecord.empty })
        .GetAwaiter()
        .GetResult()

[<Tests>]
let hierarchicalStatechartTests =
    testList
        "Multi-level statecharts"
        [ testCase "Nested DU: initial state is Playing XTurn"
          <| fun () ->
              let res = buildHierarchicalResource hierarchicalMachine

              (withHierarchicalServer res.Resource (Some(playerX ())) (fun _server client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/hgames/g1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should work in Playing XTurn"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Sub-state transition: Playing XTurn -> Playing OTurn"
          <| fun () ->
              let res = buildHierarchicalResource hierarchicalMachine

              (withHierarchicalServer res.Resource (Some(playerX ())) (fun server client ->
                  task {
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/hgames/g1", content)
                      Expect.equal response.StatusCode HttpStatusCode.OK "POST should succeed"

                      let store =
                          server.Services.GetRequiredService<
                              IStatechartsStore<HierarchicalGameState, HierarchicalContext>
                           >()

                      let! stateOpt = store.Load "g1"
                      Expect.equal stateOpt.Value.State (Playing OTurn) "Should transition to Playing OTurn"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Sub-state transition preserves context across levels"
          <| fun () ->
              let res = buildHierarchicalResource hierarchicalMachine

              (withHierarchicalServer res.Resource (Some(playerX ())) (fun server client ->
                  task {
                      let content = new StringContent("")
                      let! (_: HttpResponseMessage) = client.PostAsync("/hgames/g1", content)

                      let store =
                          server.Services.GetRequiredService<
                              IStatechartsStore<HierarchicalGameState, HierarchicalContext>
                           >()

                      let! stateOpt = store.Load "g1"
                      let ctx = stateOpt.Value.Context
                      Expect.equal ctx.MoveCount 1 "Move count should be 1"

                      match ctx.Assignment with
                      | XOnly _ -> ()
                      | other -> failtest $"Expected XOnly assignment, got {other}"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Top-level transition: Playing -> Disposed"
          <| fun () ->
              let res = buildHierarchicalResource hierarchicalMachine

              (withHierarchicalServer res.Resource None (fun server client ->
                  task {
                      // Pre-populate as Draw (which has DELETE handler for disposal)
                      prePopulateHierarchical
                          server
                          "g1"
                          (Playing Draw)
                          { MoveCount = 9
                            Assignment = BothAssigned("u1", "u2") }

                      let! (response: HttpResponseMessage) = client.DeleteAsync("/hgames/g1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "DELETE should trigger dispose"

                      let store =
                          server.Services.GetRequiredService<
                              IStatechartsStore<HierarchicalGameState, HierarchicalContext>
                           >()

                      let! stateOpt = store.Load "g1"
                      Expect.equal stateOpt.Value.State Disposed "Should transition to Disposed"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Disposed state only allows GET"
          <| fun () ->
              let res = buildHierarchicalResource hierarchicalMachine

              (withHierarchicalServer res.Resource None (fun server client ->
                  task {
                      prePopulateHierarchical server "g1" Disposed initialContext

                      let! (getResp: HttpResponseMessage) = client.GetAsync("/hgames/g1")
                      Expect.equal getResp.StatusCode HttpStatusCode.OK "GET should work on Disposed"

                      let! (postResp: HttpResponseMessage) = client.PostAsync("/hgames/g1", new StringContent(""))

                      Expect.equal postResp.StatusCode HttpStatusCode.MethodNotAllowed "POST should be 405 on Disposed"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "All Playing sub-states share handlers (parameterized key matching)"
          <| fun () ->
              let res = buildHierarchicalResource hierarchicalMachine

              (withHierarchicalServer res.Resource None (fun server client ->
                  task {
                      // Won sub-state: POST handler is available but does nothing
                      // (handlePlayingPost checks actual sub-state and skips Won)
                      prePopulateHierarchical
                          server
                          "g1"
                          (Playing(Won "X"))
                          { MoveCount = 5
                            Assignment = BothAssigned("u1", "u2") }

                      let! (postResp: HttpResponseMessage) = client.PostAsync("/hgames/g1", new StringContent(""))

                      Expect.equal
                          postResp.StatusCode
                          HttpStatusCode.OK
                          "POST succeeds on Playing Won (no-op, no event set)"

                      let! (getResp: HttpResponseMessage) = client.GetAsync("/hgames/g1")
                      Expect.equal getResp.StatusCode HttpStatusCode.OK "GET should work on Playing Won"

                      // Draw sub-state: DELETE dispatches to dispose handler
                      prePopulateHierarchical
                          server
                          "g2"
                          (Playing Draw)
                          { MoveCount = 9
                            Assignment = BothAssigned("u1", "u2") }

                      let! (delResp: HttpResponseMessage) = client.DeleteAsync("/hgames/g2")
                      Expect.equal delResp.StatusCode HttpStatusCode.OK "DELETE should work on Playing Draw"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Guards work across nested state hierarchy"
          <| fun () ->
              let res = buildHierarchicalResource hierarchicalMachine

              (withHierarchicalServer res.Resource (Some(playerO ())) (fun _server client ->
                  task {
                      // Initial state is Playing XTurn — Player O should get 409
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/hgames/g1", content)

                      Expect.equal
                          response.StatusCode
                          HttpStatusCode.Conflict
                          "Player O should get 409 in Playing XTurn"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Full hierarchical lifecycle with player assignment in context"
          <| fun () ->
              let res = buildHierarchicalResource hierarchicalMachine

              (withHierarchicalServer res.Resource (Some(playerX ())) (fun server client ->
                  task {
                      let store =
                          server.Services.GetRequiredService<
                              IStatechartsStore<HierarchicalGameState, HierarchicalContext>
                           >()

                      // Move 1: Playing XTurn -> Playing OTurn (assigns player X)
                      let! (_: HttpResponseMessage) = client.PostAsync("/hgames/g1", new StringContent(""))
                      let! s1 = store.Load "g1"
                      Expect.equal s1.Value.State (Playing OTurn) "Move 1: Playing OTurn"

                      match s1.Value.Context.Assignment with
                      | XOnly id -> Expect.equal id "X" "Player X should be assigned"
                      | other -> failtest $"Expected XOnly, got {other}"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Participant guard uses context to check player assignment"
          <| fun () ->
              let res = buildHierarchicalResource hierarchicalMachineWithParticipant

              let thirdPlayer =
                  ClaimsPrincipal(ClaimsIdentity([| Claim("player", "X"); Claim("userId", "user3") |], "test"))

              (withHierarchicalServer res.Resource (Some thirdPlayer) (fun server client ->
                  task {
                      // Pre-populate with both players assigned
                      prePopulateHierarchical
                          server
                          "g1"
                          (Playing XTurn)
                          { MoveCount = 2
                            Assignment = BothAssigned("user1", "user2") }

                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/hgames/g1", content)

                      // turnGuard passes (has player=X), but participantGuard blocks
                      // because userId=user3 is neither user1 nor user2
                      Expect.equal (int response.StatusCode) 403 "Third player should be blocked by participant guard"

                      let ct = response.Content.Headers.ContentType.MediaType
                      Expect.equal ct "application/problem+json" "Should be problem+json"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.stringContains body "Game is full" "Should get custom message from participant guard in detail"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

// === T005: Parameterized DU state matching tests ===

[<Tests>]
let parameterizedStateKeyTests =
    testList
        "Parameterized DU state key extraction"
        [ test "stateKeyOf extracts case name for parameterized TicTacToe DU cases" {
              Expect.equal (stateKeyOf (TicTacToeState.Won "X")) "Won" "Won 'X' should map to 'Won'"
              Expect.equal (stateKeyOf (TicTacToeState.Won "O")) "Won" "Won 'O' should map to 'Won'"
              Expect.equal (stateKeyOf (TicTacToeState.Won "Z")) "Won" "Won 'Z' should map to 'Won'"
          }

          test "stateKeyOf extracts case name for simple TicTacToe DU cases" {
              Expect.equal (stateKeyOf TicTacToeState.XTurn) "XTurn" "XTurn should map to 'XTurn'"
              Expect.equal (stateKeyOf TicTacToeState.OTurn) "OTurn" "OTurn should map to 'OTurn'"
              Expect.equal (stateKeyOf TicTacToeState.Draw) "Draw" "Draw should map to 'Draw'"
          }

          test "Won 'X' and Won 'O' produce the same handler key" {
              Expect.equal
                  (stateKeyOf (TicTacToeState.Won "X"))
                  (stateKeyOf (TicTacToeState.Won "O"))
                  "Won variants should produce same key"
          }

          test "XTurn does NOT match Won key" {
              Expect.notEqual
                  (stateKeyOf TicTacToeState.XTurn)
                  (stateKeyOf (TicTacToeState.Won "X"))
                  "XTurn and Won should be different keys"
          }

          testCase "Single Won handler matches both Won 'X' and Won 'O' via HTTP"
          <| fun () ->
              let getWonState (ctx: HttpContext) : Task =
                  ctx.Response.WriteAsync("won state handler")

              let res =
                  statefulResource "/games/{gameId}" {
                      machine gameMachine
                      resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)

                      inState (
                          forState
                              TicTacToeState.XTurn
                              [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ]
                      )

                      inState (
                          forState
                              TicTacToeState.OTurn
                              [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ]
                      )

                      // Register handler for Won using any parameter value
                      inState (forState (TicTacToeState.Won "X") [ StateHandlerBuilder.get getWonState ])
                      inState (forState TicTacToeState.Draw [ StateHandlerBuilder.get getGameState ])
                  }

              (withGameServer res.Resource None (fun server client ->
                  task {
                      // Transition to Won "X"
                      prePopulateState server "game1" (TicTacToeState.Won "X") 5
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should work for Won 'X'"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.equal body "won state handler" "Should use the Won handler for Won 'X'"

                      // Also test Won "O" -- same handler should match
                      prePopulateState server "game2" (TicTacToeState.Won "O") 5
                      let! (response2: HttpResponseMessage) = client.GetAsync("/games/game2")
                      Expect.equal response2.StatusCode HttpStatusCode.OK "GET should work for Won 'O'"
                      let! body2 = response2.Content.ReadAsStringAsync()
                      Expect.equal body2 "won state handler" "Same Won handler should match Won 'O'"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Handlers registered for Won 'X' and Won 'O' separately merge into single Won entry"
          <| fun () ->
              let getWonX (ctx: HttpContext) : Task = ctx.Response.WriteAsync("won-x")
              let postWonO (ctx: HttpContext) : Task = ctx.Response.WriteAsync("won-o-post")

              let res =
                  statefulResource "/games/{gameId}" {
                      machine gameMachine
                      resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)

                      inState (
                          forState
                              TicTacToeState.XTurn
                              [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ]
                      )

                      inState (
                          forState
                              TicTacToeState.OTurn
                              [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ]
                      )

                      // Register GET via Won "X"
                      inState (forState (TicTacToeState.Won "X") [ StateHandlerBuilder.get getWonX ])
                      // Register POST via Won "O" -- should merge into same "Won" key
                      inState (forState (TicTacToeState.Won "O") [ StateHandlerBuilder.post postWonO ])
                      inState (forState TicTacToeState.Draw [ StateHandlerBuilder.get getGameState ])
                  }

              (withGameServer res.Resource None (fun server client ->
                  task {
                      prePopulateState server "game1" (TicTacToeState.Won "X") 5
                      // GET should work (registered via Won "X")
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should work for Won"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.equal body "won-x" "Should use the Won GET handler"

                      // POST should also work (registered via Won "O", merged into same key)
                      let! (response2: HttpResponseMessage) =
                          client.PostAsync("/games/game1", new StringContent(""))

                      Expect.equal
                          response2.StatusCode
                          HttpStatusCode.OK
                          "POST should work for Won (merged handlers)"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Full game lifecycle ending with Won, using parameterized matching"
          <| fun () ->
              let getWonState (ctx: HttpContext) : Task =
                  ctx.Response.WriteAsync("game won!")

              let res =
                  statefulResource "/games/{gameId}" {
                      machine gameMachine
                      resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)

                      inState (
                          forState
                              TicTacToeState.XTurn
                              [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ]
                      )

                      inState (
                          forState
                              TicTacToeState.OTurn
                              [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ]
                      )

                      inState (forState (TicTacToeState.Won "anyone") [ StateHandlerBuilder.get getWonState ])
                      inState (forState TicTacToeState.Draw [ StateHandlerBuilder.get getGameState ])
                  }

              (withGameServer res.Resource None (fun server client ->
                  task {
                      let store =
                          server.Services.GetRequiredService<IStatechartsStore<TicTacToeState, int>>()

                      // Play through 5 moves to reach Won "X"
                      for _ in 1..5 do
                          let! (_: HttpResponseMessage) =
                              client.PostAsync("/games/game1", new StringContent(""))

                          ()

                      let! finalState = store.Load "game1"

                      Expect.equal
                          finalState.Value.State
                          (TicTacToeState.Won "X")
                          "Should reach Won 'X' after 5 moves"

                      // GET in Won state should use the Won handler
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should work in Won state"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.equal body "game won!" "Should use Won handler regardless of parameter"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

// === T006: Simple DU backward compatibility tests ===

[<Tests>]
let simpleDuBackwardCompatibilityTests =
    testList
        "Simple DU backward compatibility"
        [ test "Simple case key extraction produces case name" {
              Expect.equal (stateKeyOf TicTacToeState.XTurn) "XTurn" "XTurn key"
              Expect.equal (stateKeyOf TicTacToeState.OTurn) "OTurn" "OTurn key"
              Expect.equal (stateKeyOf TicTacToeState.Draw) "Draw" "Draw key"
          }

          test "Non-parameterized DU keys match across MiddlewareTests types" {
              Expect.equal (stateKeyOf MiddlewareTests.Active) "Active" "Active key"
              Expect.equal (stateKeyOf MiddlewareTests.Completed) "Completed" "Completed key"
          }

          test "Turnstile states extract correctly" {
              Expect.equal (stateKeyOf TypeTests.Locked) "Locked" "Locked key"
              Expect.equal (stateKeyOf TypeTests.Unlocked) "Unlocked" "Unlocked key"
          }

          test "Non-DU type falls back to ToString" {
              // String is not a DU
              Expect.equal (stateKeyOf "hello") "hello" "String should use ToString()"
              Expect.equal (stateKeyOf 42) "42" "Int should use ToString()"
          }

          testCase "Existing simple state handlers work without modification"
          <| fun () ->
              // This test verifies that the existing buildGameResource function
              // (which registers handlers for XTurn, OTurn, Won, Draw) still works.
              let res = buildGameResource gameMachine

              (withGameServer res.Resource None (fun _server client ->
                  task {
                      // XTurn (initial state) should have GET + POST
                      let! (getResp: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal getResp.StatusCode HttpStatusCode.OK "GET should work in XTurn"

                      let! (postResp: HttpResponseMessage) =
                          client.PostAsync("/games/game1", new StringContent(""))

                      Expect.equal postResp.StatusCode HttpStatusCode.OK "POST should work in XTurn"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Nested DU states extract top-level case name"
          <| fun () ->
              // HierarchicalGameState uses nested DUs: Playing XTurn, Playing OTurn, etc.
              // All Playing variants map to the same key "Playing" (top-level case name).
              Expect.equal (stateKeyOf (Playing PlayingSubState.XTurn)) "Playing" "Playing XTurn -> Playing"
              Expect.equal (stateKeyOf (Playing PlayingSubState.OTurn)) "Playing" "Playing OTurn -> Playing"

              Expect.equal
                  (stateKeyOf (Playing(PlayingSubState.Won "X")))
                  "Playing"
                  "Playing (Won X) -> Playing"

              Expect.equal (stateKeyOf Disposed) "Disposed" "Disposed -> Disposed" ]

// === Role Definition Tests ===

let private getRolesHandler (ctx: HttpContext) : Task =
    let roles = ctx.GetRoles()
    ctx.Response.WriteAsync(String.Join(",", roles |> Set.toList |> List.sort))

let private addRoleTestStore (services: IServiceCollection) =
    services.AddStatechartsStore<MiddlewareTests.TestState, int>() |> ignore

let private withRoleServer (resource: Resource) configUser (f: HttpClient -> Task) =
    task {
        let server = MiddlewareTests.buildTestServer resource addRoleTestStore configUser
        let client = server.CreateClient()

        try
            do! f client
        finally
            client.Dispose()
            server.Dispose()
    }
    :> Task

[<Tests>]
let roleDefinitionTests =
    testList
        "Role definitions"
        [ testCase "T019: role declarations appear in endpoint metadata"
          <| fun () ->
              let resource =
                  statefulResource "/test/{id}" {
                      machine MiddlewareTests.testMachine
                      resolveInstanceId (fun ctx -> "1")
                      role "RoleA" (fun claims -> claims.HasClaim("role", "a"))
                      role "RoleB" (fun claims -> claims.HasClaim("role", "b"))

                      inState (
                          forState MiddlewareTests.Active [ StateHandlerBuilder.get (fun _ -> Task.CompletedTask) ]
                      )
                  }

              let endpoint = resource.Resource.Endpoints[0]
              let metadata = endpoint.Metadata.GetMetadata<StateMachineMetadata>()
              Expect.isNotNull (box metadata) "should have StateMachineMetadata"
              let roleNames = metadata.Roles |> List.map (fun r -> r.Name) |> List.sort
              Expect.equal roleNames [ "RoleA"; "RoleB" ] "should have both roles"

          testCase "T020: duplicate role names rejected at startup"
          <| fun () ->
              try
                  statefulResource "/test/{id}" {
                      machine MiddlewareTests.testMachine
                      resolveInstanceId (fun ctx -> "1")
                      role "Same" (fun _ -> true)
                      role "Same" (fun _ -> false)

                      inState (
                          forState MiddlewareTests.Active [ StateHandlerBuilder.get (fun _ -> Task.CompletedTask) ]
                      )
                  }
                  |> ignore

                  failtest "should have thrown on duplicate role names"
              with ex ->
                  Expect.stringContains ex.Message "Duplicate role names" "error should mention duplicate names"

          testCase "T021: role resolution with different identities"
          <| fun () ->
              let resource =
                  statefulResource "/roles/{id}" {
                      machine MiddlewareTests.testMachine
                      resolveInstanceId (fun _ -> "1")
                      role "RoleA" (fun claims -> claims.HasClaim("role", "a"))
                      role "RoleB" (fun claims -> claims.HasClaim("role", "b"))
                      role "Observer" (fun _ -> true)

                      inState (
                          forState MiddlewareTests.Active [ StateHandlerBuilder.get getRolesHandler ]
                      )
                  }

              // User with role=a should get RoleA and Observer
              let userA =
                  ClaimsPrincipal(ClaimsIdentity([| Claim("role", "a") |], "test"))

              (withRoleServer resource.Resource (Some userA) (fun client ->
                  task {
                      let! (resp: HttpResponseMessage) = client.GetAsync("/roles/1")
                      Expect.equal resp.StatusCode HttpStatusCode.OK "should be 200 for role=a user"
                      let! body = resp.Content.ReadAsStringAsync()
                      let parts = body.Split(',') |> Set.ofArray
                      Expect.isTrue (parts.Contains "RoleA") "RoleA should be resolved for role=a"
                      Expect.isTrue (parts.Contains "Observer") "Observer should always resolve"
                      Expect.isFalse (parts.Contains "RoleB") "RoleB should not resolve for role=a"
                  }))
                  .GetAwaiter()
                  .GetResult()

              // User with role=b should get RoleB and Observer
              let userB =
                  ClaimsPrincipal(ClaimsIdentity([| Claim("role", "b") |], "test"))

              (withRoleServer resource.Resource (Some userB) (fun client ->
                  task {
                      let! (resp: HttpResponseMessage) = client.GetAsync("/roles/1")
                      Expect.equal resp.StatusCode HttpStatusCode.OK "should be 200 for role=b user"
                      let! body = resp.Content.ReadAsStringAsync()
                      let parts = body.Split(',') |> Set.ofArray
                      Expect.isTrue (parts.Contains "RoleB") "RoleB should be resolved for role=b"
                      Expect.isTrue (parts.Contains "Observer") "Observer should always resolve"
                      Expect.isFalse (parts.Contains "RoleA") "RoleA should not resolve for role=b"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "T021B: anonymous user matches only predicate-based roles"
          <| fun () ->
              let resource =
                  statefulResource "/roles/{id}" {
                      machine MiddlewareTests.testMachine
                      resolveInstanceId (fun _ -> "1")
                      role "RoleA" (fun claims -> claims.HasClaim("role", "a"))
                      role "RoleB" (fun claims -> claims.HasClaim("role", "b"))
                      role "Observer" (fun _ -> true)

                      inState (
                          forState MiddlewareTests.Active [ StateHandlerBuilder.get getRolesHandler ]
                      )
                  }

              // Anonymous user (no auth type) should match only Observer
              let anonymous = ClaimsPrincipal(ClaimsIdentity())

              (withRoleServer resource.Resource (Some anonymous) (fun client ->
                  task {
                      let! (resp: HttpResponseMessage) = client.GetAsync("/roles/1")
                      Expect.equal resp.StatusCode HttpStatusCode.OK "should be 200 for anonymous user"
                      let! body = resp.Content.ReadAsStringAsync()
                      let parts = body.Split(',') |> Array.filter (fun s -> s <> "") |> Set.ofArray
                      Expect.isTrue (parts.Contains "Observer") "Observer should resolve for anonymous user"
                      Expect.isFalse (parts.Contains "RoleA") "RoleA should not resolve for anonymous user"
                      Expect.isFalse (parts.Contains "RoleB") "RoleB should not resolve for anonymous user"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "T021C: user with all claims matches all roles"
          <| fun () ->
              let resource =
                  statefulResource "/roles/{id}" {
                      machine MiddlewareTests.testMachine
                      resolveInstanceId (fun _ -> "1")
                      role "RoleA" (fun claims -> claims.HasClaim("role", "a"))
                      role "RoleB" (fun claims -> claims.HasClaim("role", "b"))
                      role "Observer" (fun _ -> true)

                      inState (
                          forState MiddlewareTests.Active [ StateHandlerBuilder.get getRolesHandler ]
                      )
                  }

              // User with both claims should match all three roles
              let superUser =
                  ClaimsPrincipal(ClaimsIdentity([| Claim("role", "a"); Claim("role", "b") |], "test"))

              (withRoleServer resource.Resource (Some superUser) (fun client ->
                  task {
                      let! (resp: HttpResponseMessage) = client.GetAsync("/roles/1")
                      Expect.equal resp.StatusCode HttpStatusCode.OK "should be 200 for user with all claims"
                      let! body = resp.Content.ReadAsStringAsync()
                      Expect.equal body "Observer,RoleA,RoleB" "all three roles should be resolved and sorted"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "T022: HasRole in guards allows matching role and blocks others"
          <| fun () ->
              let roleGuardMachine: StateMachine<MiddlewareTests.TestState, MiddlewareTests.TestEvent, int> =
                  { MiddlewareTests.testMachine with
                      Guards =
                          [ AccessControl(
                                "RequireAllowed",
                                fun ctx ->
                                    if ctx.HasRole "Allowed" then
                                        Allowed
                                    else
                                        Blocked Forbidden
                            ) ] }

              let resource =
                  statefulResource "/guarded-role/{id}" {
                      machine roleGuardMachine
                      resolveInstanceId (fun _ -> "1")
                      role "Allowed" (fun claims -> claims.HasClaim("access", "granted"))

                      inState (
                          forState
                              MiddlewareTests.Active
                              [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("ok")) ]
                      )
                  }

              // User with access=granted should get Allowed role -> 200
              let allowedUser =
                  ClaimsPrincipal(ClaimsIdentity([| Claim("access", "granted") |], "test"))

              (withRoleServer resource.Resource (Some allowedUser) (fun client ->
                  task {
                      let! (resp: HttpResponseMessage) = client.GetAsync("/guarded-role/1")
                      Expect.equal resp.StatusCode HttpStatusCode.OK "allowed user should get 200"
                  }))
                  .GetAwaiter()
                  .GetResult()

              // User without access=granted should not get Allowed role -> 403
              let deniedUser =
                  ClaimsPrincipal(ClaimsIdentity([| Claim("access", "denied") |], "test"))

              (withRoleServer resource.Resource (Some deniedUser) (fun client ->
                  task {
                      let! (resp: HttpResponseMessage) = client.GetAsync("/guarded-role/1")
                      Expect.equal resp.StatusCode HttpStatusCode.Forbidden "denied user should get 403"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "T022C: HasRole with undeclared role name always blocks"
          <| fun () ->
              let undeclaredRoleGuardMachine: StateMachine<MiddlewareTests.TestState, MiddlewareTests.TestEvent, int> =
                  { MiddlewareTests.testMachine with
                      Guards =
                          [ AccessControl(
                                "RequireNonexistent",
                                fun ctx ->
                                    if ctx.HasRole "NonexistentRole" then
                                        Allowed
                                    else
                                        Blocked Forbidden
                            ) ] }

              let resource =
                  statefulResource "/undeclared-role/{id}" {
                      machine undeclaredRoleGuardMachine
                      resolveInstanceId (fun _ -> "1")
                      role "Allowed" (fun claims -> claims.HasClaim("access", "granted"))

                      inState (
                          forState
                              MiddlewareTests.Active
                              [ StateHandlerBuilder.get (fun ctx -> ctx.Response.WriteAsync("ok")) ]
                      )
                  }

              // Even a user with access=granted should get 403 because NonexistentRole is never declared
              let allowedUser =
                  ClaimsPrincipal(ClaimsIdentity([| Claim("access", "granted") |], "test"))

              (withRoleServer resource.Resource (Some allowedUser) (fun client ->
                  task {
                      let! (resp: HttpResponseMessage) = client.GetAsync("/undeclared-role/1")
                      Expect.equal resp.StatusCode HttpStatusCode.Forbidden "undeclared role should never match -> 403"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "T023: backward compatibility — resource without roles"
          <| fun () ->
              let resource =
                  statefulResource "/noRoles/{id}" {
                      machine MiddlewareTests.testMachine
                      resolveInstanceId (fun _ -> "1")

                      inState (
                          forState MiddlewareTests.Active [ StateHandlerBuilder.get getRolesHandler ]
                      )
                  }

              (withRoleServer resource.Resource None (fun client ->
                  task {
                      let! (resp: HttpResponseMessage) = client.GetAsync("/noRoles/1")
                      Expect.equal resp.StatusCode HttpStatusCode.OK "no-role resource should be 200"
                      let! body = resp.Content.ReadAsStringAsync()
                      Expect.equal body "" "GetRoles() should return empty set when no roles declared"
                  }))
                  .GetAwaiter()
                  .GetResult()

              // Also verify HasRole returns false
              let metadata =
                  resource.Resource.Endpoints[0].Metadata.GetMetadata<StateMachineMetadata>()

              Expect.isEmpty metadata.Roles "Roles list should be empty"

          testCase "T024: predicate exception handling — bad predicate skipped, good role still resolves"
          <| fun () ->
              let resource =
                  statefulResource "/exception-role/{id}" {
                      machine MiddlewareTests.testMachine
                      resolveInstanceId (fun _ -> "1")
                      role "BadRole" (fun _ -> failwith "boom")
                      role "GoodRole" (fun _ -> true)

                      inState (
                          forState MiddlewareTests.Active [ StateHandlerBuilder.get getRolesHandler ]
                      )
                  }

              let user = ClaimsPrincipal(ClaimsIdentity([||], "test"))

              (withRoleServer resource.Resource (Some user) (fun client ->
                  task {
                      let! (resp: HttpResponseMessage) = client.GetAsync("/exception-role/1")
                      // Request should still succeed — bad predicate is logged and skipped
                      Expect.equal resp.StatusCode HttpStatusCode.OK "request should succeed despite bad predicate"
                      let! body = resp.Content.ReadAsStringAsync()
                      // GoodRole should resolve; BadRole should be absent
                      let parts = body.Split(',') |> Array.filter (fun s -> s <> "") |> Set.ofArray
                      Expect.isTrue (parts.Contains "GoodRole") "GoodRole should still resolve"
                      Expect.isFalse (parts.Contains "BadRole") "BadRole should be skipped on exception"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "GAP-1: EventValidation guard context receives resolved roles"
          <| fun () ->
              let guardCalled = ref false
              let receivedRoles = ref Set.empty

              let evGuard: Guard<MiddlewareTests.TestState, MiddlewareTests.TestEvent, int> =
                  EventValidation(
                      "CheckRoles",
                      fun ctx ->
                          guardCalled.Value <- true
                          receivedRoles.Value <- ctx.Roles
                          Allowed
                  )

              let evGuardMachine =
                  { MiddlewareTests.testMachine with
                      Guards = [ evGuard ] }

              let resource =
                  statefulResource "/ev-guard/{id}" {
                      machine evGuardMachine
                      resolveInstanceId (fun _ -> "1")
                      role "TestRole" (fun _ -> true)

                      inState (
                          forState
                              MiddlewareTests.Active
                              [ StateHandlerBuilder.get (fun _ -> Task.CompletedTask)
                                StateHandlerBuilder.post (fun ctx ->
                                    StateMachineContext.setEvent ctx MiddlewareTests.DoAction
                                    Task.CompletedTask) ]
                      )
                  }

              (withRoleServer resource.Resource None (fun client ->
                  task {
                      let! (_: HttpResponseMessage) = client.PostAsync("/ev-guard/1", new StringContent(""))
                      Expect.isTrue guardCalled.Value "EventValidation guard should have been called"

                      Expect.isTrue
                          (receivedRoles.Value.Contains("TestRole"))
                          "EventValidation guard ctx.Roles should contain resolved roles"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "GAP-3: empty role name rejected at startup"
          <| fun () ->
              try
                  statefulResource "/test/{id}" {
                      machine MiddlewareTests.testMachine
                      resolveInstanceId (fun _ -> "1")
                      role "" (fun _ -> true)

                      inState (
                          forState MiddlewareTests.Active [ StateHandlerBuilder.get (fun _ -> Task.CompletedTask) ]
                      )
                  }
                  |> ignore

                  failtest "should have thrown for empty role name"
              with ex ->
                  Expect.stringContains ex.Message "Empty role name" "error should mention empty name" ]
