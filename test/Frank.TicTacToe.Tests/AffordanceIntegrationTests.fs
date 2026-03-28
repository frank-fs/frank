/// Integration tests for the tic-tac-toe sample app exercising the full unified
/// resource pipeline: statechart middleware, affordance middleware, and Datastar SSE.
module Frank.TicTacToe.Tests.AffordanceIntegrationTests

open System
open System.Collections.Generic
open System.Net
open System.Net.Http
open System.Security.Claims
open System.Threading.Tasks
open FSharp.Reflection
open Expecto
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Primitives
open Frank.Builder
open Frank.Statecharts
open Frank.Affordances
open Frank.Resources.Model
open Frank.Tests.Shared.TestEndpointDataSource

// === Domain types (self-contained for tests) ===

type TicTacToeState =
    | XTurn
    | OTurn
    | Won of winner: string
    | Draw

type TicTacToeEvent = MakeMove of position: int

let stateKeyOf (state: TicTacToeState) : string =
    let tagReader = FSharpValue.PreComputeUnionTagReader(typeof<TicTacToeState>)
    let cases = FSharpType.GetUnionCases(typeof<TicTacToeState>, true)
    let caseNames = cases |> Array.map (fun c -> c.Name)
    caseNames.[tagReader (box state)]

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

let gameMachine: StateMachine<TicTacToeState, TicTacToeEvent, int> =
    { Initial = XTurn
      InitialContext = 0
      Transition = gameTransition
      Guards = []
      StateMetadata = Map.empty }

// === Affordance map for testing ===

let testAffordanceMap: AffordanceMap =
    { Version = AffordanceMap.currentVersion
      Entries =
        [ { RouteTemplate = "/games/{gameId}"
            StateKey = "XTurn"
            AllowedMethods = [ "GET"; "POST" ]
            LinkRelations =
              [ { Rel = "makeMove"
                  Href = "/games/{gameId}"
                  Method = "POST"
                  Title = Some "Make a move"
                  Roles = [] } ]
            ProfileUrl = "" }
          { RouteTemplate = "/games/{gameId}"
            StateKey = "OTurn"
            AllowedMethods = [ "GET"; "POST" ]
            LinkRelations =
              [ { Rel = "makeMove"
                  Href = "/games/{gameId}"
                  Method = "POST"
                  Title = Some "Make a move"
                  Roles = [] } ]
            ProfileUrl = "" }
          { RouteTemplate = "/games/{gameId}"
            StateKey = "Won"
            AllowedMethods = [ "GET" ]
            LinkRelations = []
            ProfileUrl = "" }
          { RouteTemplate = "/games/{gameId}"
            StateKey = "Draw"
            AllowedMethods = [ "GET" ]
            LinkRelations = []
            ProfileUrl = "" } ] }

// === Handlers ===

/// GET handler that returns current game state. The state key was already
/// set on IStatechartFeature by resolveStateKeyMiddleware before this runs.
let getGameState (ctx: HttpContext) : Task =
    task {
        let store =
            ctx.RequestServices.GetService(typeof<IStateMachineStore<TicTacToeState, int>>)
            :?> IStateMachineStore<TicTacToeState, int>

        let instanceId = ctx.Request.RouteValues["gameId"] :?> string
        let! stateResult = store.GetState(instanceId)

        let state, moveCount =
            match stateResult with
            | Some(s, c) -> (s, c)
            | None -> (gameMachine.Initial, gameMachine.InitialContext)

        let key = stateKeyOf state
        do! ctx.Response.WriteAsync($"state={key};moves={moveCount}")
    }
    :> Task

let handleMove (ctx: HttpContext) : Task =
    task {
        let store =
            ctx.RequestServices.GetService(typeof<IStateMachineStore<TicTacToeState, int>>)
            :?> IStateMachineStore<TicTacToeState, int>

        let instanceId = ctx.Request.RouteValues["gameId"] :?> string
        let! stateResult = store.GetState(instanceId)

        let _state, _ =
            match stateResult with
            | Some(s, c) -> (s, c)
            | None -> (gameMachine.Initial, gameMachine.InitialContext)

        StateMachineContext.setEvent ctx (MakeMove 0)
    }
    :> Task

// === Resource ===

let buildGameResource () =
    statefulResource "/games/{gameId}" {
        machine gameMachine
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
        inState (forState XTurn [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ])
        inState (forState OTurn [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ])
        inState (forState (Won "X") [ StateHandlerBuilder.get getGameState ])
        inState (forState (Won "O") [ StateHandlerBuilder.get getGameState ])
        inState (forState Draw [ StateHandlerBuilder.get getGameState ])
    }

// === Test infrastructure ===

/// Middleware shim that resolves the statechart state key from the store
/// and sets IStatechartFeature on HttpContext.Features for the affordance middleware to read.
/// GetCurrentStateKey calls SetStatechartState internally, so no explicit Items write is needed.
let resolveStateKeyMiddleware (ctx: HttpContext) (next: Func<Task>) =
    task {
        let endpoint = ctx.GetEndpoint()

        if not (isNull endpoint) then
            let metadata = endpoint.Metadata.GetMetadata<StateMachineMetadata>()

            if not (obj.ReferenceEquals(metadata, null)) then
                let instanceId = metadata.ResolveInstanceId ctx
                let! _stateKey = metadata.GetCurrentStateKey ctx.RequestServices ctx instanceId
                ()

        do! next.Invoke()
    }
    :> Task

let buildTestServer (resource: Resource) =
    let preComputed = AffordancePreCompute.preCompute testAffordanceMap

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddLogging() |> ignore
    builder.Services.AddStateMachineStore<TicTacToeState, int>() |> ignore
    let app = builder.Build()

    app.UseRouting() |> ignore

    // 1. Resolve state key from store and set IStatechartFeature
    (app :> IApplicationBuilder).Use(Func<HttpContext, Func<Task>, Task>(resolveStateKeyMiddleware))
    |> ignore

    // 2. Affordance middleware reads IStatechartFeature and injects Link/Allow headers
    (app :> IApplicationBuilder).UseMiddleware<AffordanceMiddleware>(preComputed)
    |> ignore

    // 3. Statechart middleware handles state-dependent dispatch
    (app :> IApplicationBuilder).UseMiddleware<StateMachineMiddleware>() |> ignore

    app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource(resource.Endpoints)))
    |> ignore

    app.Start()
    app.GetTestServer()

let prePopulateState (server: TestServer) instanceId (state: TicTacToeState) (moveCount: int) =
    let store =
        server.Services.GetRequiredService<IStateMachineStore<TicTacToeState, int>>()

    (store.SetState instanceId state moveCount).GetAwaiter().GetResult()

/// Get a header value from the response (checks both response and content headers).
let getHeaderValues (response: HttpResponseMessage) (name: string) : string list =
    let mutable values = Seq.empty

    if response.Headers.TryGetValues(name, &values) then
        values |> Seq.toList
    elif
        not (isNull response.Content)
        && response.Content.Headers.TryGetValues(name, &values)
    then
        values |> Seq.toList
    else
        []

let hasHeader (response: HttpResponseMessage) (name: string) : bool =
    getHeaderValues response name |> List.isEmpty |> not

let withServer (f: TestServer -> HttpClient -> Task) =
    task {
        let resource = buildGameResource ()
        use server = buildTestServer resource
        use client = server.CreateClient()

        do! f server client
    }
    :> Task

/// Build a test server with ProjectedProfileMiddleware for role-specific Link header testing.
let buildTestServerWithProfiles (resource: Resource) (roleLookup: RoleProfileLookup) =
    let preComputed = AffordancePreCompute.preCompute testAffordanceMap

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddLogging() |> ignore
    builder.Services.AddStateMachineStore<TicTacToeState, int>() |> ignore
    let app = builder.Build()

    app.UseRouting() |> ignore

    (app :> IApplicationBuilder).Use(Func<HttpContext, Func<Task>, Task>(resolveStateKeyMiddleware))
    |> ignore

    (app :> IApplicationBuilder).UseMiddleware<AffordanceMiddleware>(preComputed)
    |> ignore

    (app :> IApplicationBuilder).UseMiddleware<ProjectedProfileMiddleware>(roleLookup)
    |> ignore

    (app :> IApplicationBuilder).UseMiddleware<StateMachineMiddleware>() |> ignore

    app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource(resource.Endpoints)))
    |> ignore

    app.Start()
    app.GetTestServer()

/// Middleware shim that sets IRoleFeature for testing projected profile middleware.
let setRolesMiddleware (roles: Set<string>) (ctx: HttpContext) (next: Func<Task>) =
    task {
        ctx.SetRoles(roles)
        do! next.Invoke()
    }
    :> Task

/// Build a test server with role injection + projected profile middleware.
let buildTestServerWithRolesAndProfiles (resource: Resource) (roleLookup: RoleProfileLookup) (roles: Set<string>) =
    let preComputed = AffordancePreCompute.preCompute testAffordanceMap

    let builder = WebApplication.CreateBuilder([||])
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddRouting() |> ignore
    builder.Services.AddLogging() |> ignore
    builder.Services.AddStateMachineStore<TicTacToeState, int>() |> ignore
    let app = builder.Build()

    app.UseRouting() |> ignore

    // Inject roles before affordance/profile middleware
    (app :> IApplicationBuilder).Use(Func<HttpContext, Func<Task>, Task>(setRolesMiddleware roles))
    |> ignore

    (app :> IApplicationBuilder).Use(Func<HttpContext, Func<Task>, Task>(resolveStateKeyMiddleware))
    |> ignore

    (app :> IApplicationBuilder).UseMiddleware<AffordanceMiddleware>(preComputed)
    |> ignore

    (app :> IApplicationBuilder).UseMiddleware<ProjectedProfileMiddleware>(roleLookup)
    |> ignore

    (app :> IApplicationBuilder).UseMiddleware<StateMachineMiddleware>() |> ignore

    app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource(resource.Endpoints)))
    |> ignore

    app.Start()
    app.GetTestServer()

// === Tests ===

[<Tests>]
let statechartMiddlewareTests =
    testList
        "Statechart middleware (tic-tac-toe)"
        [ testCase "GET in initial XTurn state returns 200 with state info"
          <| fun () ->
              (withServer (fun _server client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should succeed in XTurn"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.stringContains body "state=XTurn" "Should report XTurn state"
                      Expect.stringContains body "moves=0" "Should report 0 moves"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "POST triggers transition from XTurn to OTurn"
          <| fun () ->
              (withServer (fun _server client ->
                  task {
                      let content = new StringContent("")
                      let! (postResponse: HttpResponseMessage) = client.PostAsync("/games/game1", content)
                      Expect.equal postResponse.StatusCode HttpStatusCode.OK "POST should succeed"

                      let! (getResponse: HttpResponseMessage) = client.GetAsync("/games/game1")
                      let! body = getResponse.Content.ReadAsStringAsync()
                      Expect.stringContains body "state=OTurn" "Should be in OTurn after move"
                      Expect.stringContains body "moves=1" "Should report 1 move"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "DELETE returns 405 (not a registered method)"
          <| fun () ->
              (withServer (fun _server client ->
                  task {
                      let! (response: HttpResponseMessage) = client.DeleteAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.MethodNotAllowed "DELETE should be 405"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "POST returns 405 in Won state"
          <| fun () ->
              (withServer (fun server client ->
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

          testCase "GET works in Won state"
          <| fun () ->
              (withServer (fun server client ->
                  task {
                      prePopulateState server "game1" (Won "X") 5
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "GET should work in Won state"
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.stringContains body "state=Won" "Should report Won state"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "POST returns 405 in Draw state"
          <| fun () ->
              (withServer (fun server client ->
                  task {
                      prePopulateState server "game1" Draw 9
                      let content = new StringContent("")
                      let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", content)

                      Expect.equal
                          response.StatusCode
                          HttpStatusCode.MethodNotAllowed
                          "POST should be 405 in Draw state"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Full game plays through multiple state transitions"
          <| fun () ->
              (withServer (fun _server client ->
                  task {
                      // Move 1: XTurn -> OTurn
                      let! _ = client.PostAsync("/games/full-game", new StringContent(""))
                      // Move 2: OTurn -> XTurn
                      let! _ = client.PostAsync("/games/full-game", new StringContent(""))
                      // Move 3: XTurn -> OTurn
                      let! _ = client.PostAsync("/games/full-game", new StringContent(""))
                      // Move 4: OTurn -> XTurn
                      let! _ = client.PostAsync("/games/full-game", new StringContent(""))
                      // Move 5: XTurn -> Won "X" (moveCount reaches 5)
                      let! _ = client.PostAsync("/games/full-game", new StringContent(""))

                      let! (response: HttpResponseMessage) = client.GetAsync("/games/full-game")
                      let! body = response.Content.ReadAsStringAsync()
                      Expect.stringContains body "state=Won" "Should be in Won state after 5 moves"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let affordanceHeaderTests =
    testList
        "Affordance middleware (Link + Allow headers)"
        [ testCase "XTurn state injects Allow: GET, POST header"
          <| fun () ->
              (withServer (fun _server client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let allow = getHeaderValues response "Allow"
                      Expect.isNonEmpty allow "Should have Allow header"
                      let allowValue = allow |> String.concat ", "
                      Expect.isTrue (allowValue.Contains("GET")) "Allow should include GET"
                      Expect.isTrue (allowValue.Contains("POST")) "Allow should include POST"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "XTurn state injects Link header with makeMove relation"
          <| fun () ->
              (withServer (fun _server client ->
                  task {
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      let links = getHeaderValues response "Link"
                      Expect.isNonEmpty links "Should have Link header"
                      let allLinks = links |> String.concat " "
                      Expect.isTrue (allLinks.Contains("rel=\"makeMove\"")) "Link should contain makeMove relation"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Won state injects Allow: GET only"
          <| fun () ->
              (withServer (fun server client ->
                  task {
                      prePopulateState server "game1" (Won "X") 5
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let allow = getHeaderValues response "Allow"
                      Expect.isNonEmpty allow "Should have Allow header"
                      let allowValue = allow |> String.concat ", "
                      Expect.isTrue (allowValue.Contains("GET")) "Allow should include GET"
                      Expect.isFalse (allowValue.Contains("POST")) "Allow should NOT include POST in Won state"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Won state has no makeMove link relation"
          <| fun () ->
              (withServer (fun server client ->
                  task {
                      prePopulateState server "game1" (Won "X") 5
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      let links = getHeaderValues response "Link"

                      if not (List.isEmpty links) then
                          let allLinks = links |> String.concat " "
                          Expect.isFalse (allLinks.Contains("makeMove")) "Won state should not have makeMove link"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "OTurn state injects Allow: GET, POST header"
          <| fun () ->
              (withServer (fun server client ->
                  task {
                      prePopulateState server "game1" OTurn 1
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                      Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                      let allow = getHeaderValues response "Allow"
                      Expect.isNonEmpty allow "Should have Allow header"
                      let allowValue = allow |> String.concat ", "
                      Expect.isTrue (allowValue.Contains("GET")) "Allow should include GET"
                      Expect.isTrue (allowValue.Contains("POST")) "Allow should include POST"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Draw state injects Allow: GET only"
          <| fun () ->
              (withServer (fun server client ->
                  task {
                      prePopulateState server "game1" Draw 9
                      let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")

                      let allow = getHeaderValues response "Allow"
                      Expect.isNonEmpty allow "Should have Allow header"
                      let allowValue = allow |> String.concat ", "
                      Expect.isTrue (allowValue.Contains("GET")) "Allow should include GET"
                      Expect.isFalse (allowValue.Contains("POST")) "Allow should NOT include POST in Draw state"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Headers change after state transition"
          <| fun () ->
              (withServer (fun _server client ->
                  task {
                      // Initial state: XTurn -> should have POST
                      let! (response1: HttpResponseMessage) = client.GetAsync("/games/transition-test")
                      let allow1 = getHeaderValues response1 "Allow" |> String.concat ", "
                      Expect.isTrue (allow1.Contains("POST")) "XTurn should allow POST"

                      // Make 5 moves to reach Won state
                      for _ in 1..5 do
                          let! _ = client.PostAsync("/games/transition-test", new StringContent(""))
                          ()

                      // After Won: should NOT have POST
                      let! (response2: HttpResponseMessage) = client.GetAsync("/games/transition-test")
                      let allow2 = getHeaderValues response2 "Allow" |> String.concat ", "
                      Expect.isFalse (allow2.Contains("POST")) "Won state should not allow POST"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let endToEndPipelineTests =
    testList
        "End-to-end pipeline"
        [ testCase "Statechart + Affordance pipeline: state drives both method filtering and headers"
          <| fun () ->
              (withServer (fun server client ->
                  task {
                      // XTurn: POST allowed, header says POST allowed
                      let! (getResp: HttpResponseMessage) = client.GetAsync("/games/pipeline-test")
                      let allow = getHeaderValues getResp "Allow" |> String.concat ", "
                      Expect.isTrue (allow.Contains("POST")) "XTurn: Allow header should include POST"

                      let! (postResp: HttpResponseMessage) =
                          client.PostAsync("/games/pipeline-test", new StringContent(""))

                      Expect.equal postResp.StatusCode HttpStatusCode.OK "XTurn: POST should succeed"

                      // Transition to Won state
                      prePopulateState server "pipeline-test" (Won "X") 5

                      // Won: POST blocked, header says GET only
                      let! (getResp2: HttpResponseMessage) = client.GetAsync("/games/pipeline-test")
                      let allow2 = getHeaderValues getResp2 "Allow" |> String.concat ", "
                      Expect.isFalse (allow2.Contains("POST")) "Won: Allow header should NOT include POST"

                      let! (postResp2: HttpResponseMessage) =
                          client.PostAsync("/games/pipeline-test", new StringContent(""))

                      Expect.equal postResp2.StatusCode HttpStatusCode.MethodNotAllowed "Won: POST should be 405"
                  }))
                  .GetAwaiter()
                  .GetResult()

          testCase "Different game instances have independent state"
          <| fun () ->
              (withServer (fun server client ->
                  task {
                      // Game A starts in XTurn
                      let! (respA: HttpResponseMessage) = client.GetAsync("/games/gameA")
                      let! bodyA = respA.Content.ReadAsStringAsync()
                      Expect.stringContains bodyA "state=XTurn" "Game A should be in XTurn"

                      // Pre-populate Game B to Won state
                      prePopulateState server "gameB" (Won "O") 7

                      // Game B is in Won state
                      let! (respB: HttpResponseMessage) = client.GetAsync("/games/gameB")
                      let! bodyB = respB.Content.ReadAsStringAsync()
                      Expect.stringContains bodyB "state=Won" "Game B should be in Won state"

                      // Game A is still in XTurn (independent)
                      let! (respA2: HttpResponseMessage) = client.GetAsync("/games/gameA")
                      let! bodyA2 = respA2.Content.ReadAsStringAsync()
                      Expect.stringContains bodyA2 "state=XTurn" "Game A should still be in XTurn"
                  }))
                  .GetAwaiter()
                  .GetResult() ]

[<Tests>]
let projectedProfileTests =
    testList
        "Projected profile middleware (role-specific Link headers)"
        [ testCase "PlayerX role gets role-specific profile Link header"
          <| fun () ->
              let roleLookup = RoleProfileLookup(StringComparer.Ordinal)

              let innerMap = Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

              innerMap.["PlayerX"] <- "</alps/games-playerx>; rel=\"profile\""

              innerMap.["PlayerO"] <- "</alps/games-playero>; rel=\"profile\""

              roleLookup.["/games/{gameId}"] <- innerMap

              // Affordance map with a profile URL so AffordanceMiddleware emits a base profile Link
              let profileAffordanceMap: AffordanceMap =
                  { Version = AffordanceMap.currentVersion
                    Entries =
                      testAffordanceMap.Entries
                      |> List.map (fun e -> { e with ProfileUrl = "/alps/games" }) }

              let preComputed = AffordancePreCompute.preCompute profileAffordanceMap
              let resource = buildGameResource ()

              let builder = WebApplication.CreateBuilder([||])
              builder.WebHost.UseTestServer() |> ignore
              builder.Services.AddRouting() |> ignore
              builder.Services.AddLogging() |> ignore
              builder.Services.AddStateMachineStore<TicTacToeState, int>() |> ignore
              let app = builder.Build()

              app.UseRouting() |> ignore

              (app :> IApplicationBuilder)
                  .Use(Func<HttpContext, Func<Task>, Task>(setRolesMiddleware (Set.ofList [ "PlayerX" ])))
              |> ignore

              (app :> IApplicationBuilder).Use(Func<HttpContext, Func<Task>, Task>(resolveStateKeyMiddleware))
              |> ignore

              (app :> IApplicationBuilder).UseMiddleware<AffordanceMiddleware>(preComputed)
              |> ignore

              (app :> IApplicationBuilder).UseMiddleware<ProjectedProfileMiddleware>(roleLookup)
              |> ignore

              (app :> IApplicationBuilder).UseMiddleware<StateMachineMiddleware>() |> ignore

              app.UseEndpoints(fun endpoints -> endpoints.DataSources.Add(TestEndpointDataSource(resource.Endpoints)))
              |> ignore

              app.Start()
              use server = app.GetTestServer()
              use client = server.CreateClient()

              (task {
                  let! (response: HttpResponseMessage) = client.GetAsync("/games/game1")
                  Expect.equal response.StatusCode HttpStatusCode.OK "Should return 200"

                  let links = getHeaderValues response "Link"
                  let allLinks = links |> String.concat " "

                  Expect.isTrue
                      (allLinks.Contains("alps/games-playerx"))
                      "PlayerX should get role-specific profile link"

                  Expect.isFalse
                      (allLinks.Contains("alps/games-playero"))
                      "PlayerX should NOT get PlayerO's profile link"

                  // Vary header should include Authorization
                  let vary = getHeaderValues response "Vary"
                  let varyValue = vary |> String.concat ", "
                  Expect.isTrue (varyValue.Contains("Authorization")) "Vary should include Authorization"
              })
                  .GetAwaiter()
                  .GetResult() ]
