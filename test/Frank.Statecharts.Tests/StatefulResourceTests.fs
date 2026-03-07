module StatefulResourceTests

open System
open System.Net
open System.Net.Http
open System.Security.Claims
open System.Threading.Tasks
open Expecto
open Frank.Builder
open Frank.Statecharts
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection

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
    { Name = "TurnGuard"
      Predicate =
        fun ctx ->
            match ctx.CurrentState with
            | XTurn ->
                if ctx.User.HasClaim("player", "X") then
                    Allowed
                elif ctx.User.HasClaim("player", "O") then
                    Blocked NotYourTurn
                else
                    Blocked NotAllowed
            | OTurn ->
                if ctx.User.HasClaim("player", "O") then
                    Allowed
                elif ctx.User.HasClaim("player", "X") then
                    Blocked NotYourTurn
                else
                    Blocked NotAllowed
            | Won _
            | Draw -> Allowed }

let gameMachine: StateMachine<TicTacToeState, TicTacToeEvent, int> =
    { Initial = XTurn
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
        let stateKey = myState.ToString()

        let methods =
            match Map.tryFind stateKey metadata.StateHandlerMap with
            | Some handlers -> handlers |> List.map fst
            | None -> []

        let methodsStr = String.Join(",", methods)
        do! ctx.Response.WriteAsync($"state={stateKey};methods={methodsStr}")
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
    services.AddStateMachineStore<TicTacToeState, int>() |> ignore

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
        server.Host.Services.GetRequiredService<IStateMachineStore<TicTacToeState, int>>()

    (store.SetState instanceId state moveCount).GetAwaiter().GetResult()

// === Tests ===

[<Tests>]
let methodFilteringTests =
    testList
        "State-dependent methods"
        [ testCase "POST allowed in XTurn state"
          <| fun () ->
              let res = buildGameResource gameMachine

              (withGameServer res None (fun _server client ->
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

              (withGameServer res None (fun _server client ->
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

              (withGameServer res None (fun _server client ->
                  task {
                      let! (response: HttpResponseMessage) = client.DeleteAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.MethodNotAllowed "DELETE should be 405"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "POST returns 405 in Won state"
          <| fun () ->
              let res = buildGameResource gameMachine

              (withGameServer res None (fun server client ->
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

              (withGameServer res None (fun server client ->
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

              (withGameServer res (Some(playerX ())) (fun _server client ->
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

              (withGameServer res (Some(playerO ())) (fun _server client ->
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

              (withGameServer res (Some(spectator ())) (fun _server client ->
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

              (withGameServer res (Some(playerO ())) (fun server client ->
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

              (withGameServer res (Some(playerX ())) (fun server client ->
                  task {
                      prePopulateState server "game1" OTurn 1
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      Expect.equal response.StatusCode HttpStatusCode.Conflict "Player X should get 409 in OTurn"
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

              (withGameServer res None (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      Expect.isSome capturedEvent "onTransition should have fired"
                      let evt = capturedEvent.Value
                      Expect.equal evt.PreviousState XTurn "Previous state should be XTurn"
                      Expect.equal evt.NewState OTurn "New state should be OTurn"
                      Expect.equal evt.Event (MakeMove 0) "Event should be MakeMove 0"
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

              (withGameServer res (Some(spectator ())) (fun _server client ->
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

              (withGameServer res None (fun server client ->
                  task {
                      let content = new StringContent("")
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      let store =
                          server.Host.Services.GetRequiredService<IStateMachineStore<TicTacToeState, int>>()

                      let! stateOpt = store.GetState "game1"
                      Expect.isSome stateOpt "Store should have state for game1"
                      let (state, moveCount) = stateOpt.Value
                      Expect.equal state OTurn "State should be OTurn after first move"
                      Expect.equal moveCount 1 "Move count should be 1"
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

              (withGameServer res None (fun _server client ->
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

              (withGameServer res None (fun server client ->
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

              (withGameServer res None (fun server client ->
                  task {
                      let store =
                          server.Host.Services.GetRequiredService<IStateMachineStore<TicTacToeState, int>>()

                      // Move 1: XTurn -> OTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      let! s1 = store.GetState "game1"
                      Expect.equal (fst s1.Value) OTurn "After move 1: OTurn"

                      // Move 2: OTurn -> XTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      let! s2 = store.GetState "game1"
                      Expect.equal (fst s2.Value) XTurn "After move 2: XTurn"

                      // Move 3: XTurn -> OTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      let! s3 = store.GetState "game1"
                      Expect.equal (fst s3.Value) OTurn "After move 3: OTurn"

                      // Move 4: OTurn -> XTurn
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      let! s4 = store.GetState "game1"
                      Expect.equal (fst s4.Value) XTurn "After move 4: XTurn"

                      // Move 5: XTurn -> Won "X" (moveCount >= 5)
                      let! (_: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))
                      let! s5 = store.GetState "game1"
                      Expect.equal (fst s5.Value) (Won "X") "After move 5: Won X"
                      Expect.equal (snd s5.Value) 5 "Move count should be 5"

                      // Won state blocks POST (405)
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", new StringContent(""))

                      Expect.equal
                          response.StatusCode
                          HttpStatusCode.MethodNotAllowed
                          "POST should be 405 in Won state"
                  }))
                  .GetAwaiter()
                  .GetResult() ]
