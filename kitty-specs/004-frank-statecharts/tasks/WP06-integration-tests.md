---
work_package_id: "WP06"
subtasks:
  - "T026"
  - "T027"
  - "T028"
  - "T029"
  - "T030"
  - "T031"
title: "Integration Tests & Tic-Tac-Toe Validation"
phase: "Phase 3 - Integration"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP04", "WP05"]
requirement_refs: ["FR-001", "FR-002", "FR-003", "FR-004", "FR-005", "FR-014"]
history:
  - timestamp: "2026-03-06T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP06 -- Integration Tests & Tic-Tac-Toe Validation

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP06 --base WP05
```

Depends on WP04 (middleware) and WP05 (extensions).

---

## Objectives & Success Criteria

- End-to-end integration tests using TestHost with the full statecharts pipeline
- Simplified tic-tac-toe state machine as test fixture validates the complete stack
- Complete request lifecycle works: state-dependent methods, guard evaluation, transition hooks, filtered affordances
- HTTP status codes verified: 200, 405, 403, 409, 400
- Store lifecycle tested: create instance, transition through states, verify final state

---

## Context & Constraints

**Reference code**:
- `../tic-tac-toe/src/TicTacToe.Engine/Model.fs` -- Original tic-tac-toe state machine (DU with 5 states)
- `test/Frank.Auth.Tests/AuthorizationTests.fs` -- TestHost-based integration test pattern
- `test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj` -- Test project structure

**Simplified tic-tac-toe for testing**:
- 4 states: `XTurn`, `OTurn`, `Won`, `Draw`
- 1 event: `MakeMove` (carries position or simplified data)
- 2 guards: `isPlayerX` (checks claim), `isPlayerO` (checks claim)
- Transition: `XTurn + MakeMove -> OTurn` (or `Won`/`Draw`), `OTurn + MakeMove -> XTurn` (or `Won`/`Draw`)

---

## Subtasks & Detailed Guidance

### Subtask T026 -- Create `StatefulResourceTests.fs` with TestHost setup

**Purpose**: Set up the integration test infrastructure with ASP.NET Core TestHost.

**Steps**:
1. Create `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
2. Add to test `.fsproj` `<Compile>` list (before `Program.fs`)
3. Set up TestHost configuration:

```fsharp
module StatefulResourceTests

open System
open System.Net
open System.Net.Http
open System.Security.Claims
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Expecto
open Frank.Builder
open Frank.Statecharts

// Helper to create a test server with statecharts
let createTestServer (gameResource: Resource) =
    let builder = WebHostBuilder()
    builder
        .ConfigureServices(fun services ->
            services.AddStateMachineStore<TicTacToeState, GameContext>() |> ignore
            services.AddRouting() |> ignore)
        .Configure(fun app ->
            app
                .UseRouting()
                .UseMiddleware<StateMachineMiddleware>()
                .UseEndpoints(fun endpoints ->
                    // Register resource endpoints
                    ...)
            |> ignore)
    new TestServer(builder)

// Helper to create authenticated HttpClient
let createClientWithClaims (server: TestServer) (claims: Claim list) =
    let client = server.CreateClient()
    // Configure test authentication to inject claims
    client
```

**Files**: `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
**Notes**:
- Use `Microsoft.AspNetCore.TestHost` like existing Frank tests
- May need to register test authentication scheme to inject `ClaimsPrincipal`
- TestServer should be disposed after each test (use `use` binding)

### Subtask T027 -- Implement simplified tic-tac-toe state machine as test fixture

**Purpose**: Create a realistic but simplified state machine that exercises all features.

**Steps**:
1. Define the types and machine in the test file:

```fsharp
type TicTacToeState =
    | XTurn
    | OTurn
    | Won of winner: string
    | Draw

type TicTacToeEvent =
    | MakeMove of position: int

type GameContext =
    { Board: int list  // simplified: list of moves
      MoveCount: int }
    static member Empty = { Board = []; MoveCount = 0 }

let gameTransition (state: TicTacToeState) (event: TicTacToeEvent) (ctx: GameContext) =
    match state, event with
    | XTurn, MakeMove pos ->
        let newCtx = { Board = pos :: ctx.Board; MoveCount = ctx.MoveCount + 1 }
        if newCtx.MoveCount >= 5 then  // simplified win condition
            Transitioned(Won "X", newCtx)
        else
            Transitioned(OTurn, newCtx)
    | OTurn, MakeMove pos ->
        let newCtx = { Board = pos :: ctx.Board; MoveCount = ctx.MoveCount + 1 }
        if newCtx.MoveCount >= 6 then
            Transitioned(Won "O", newCtx)
        elif newCtx.MoveCount >= 9 then
            Transitioned(Draw, newCtx)
        else
            Transitioned(XTurn, newCtx)
    | Won _, _ -> TransitionResult.Blocked(BlockReason.InvalidTransition)
    | Draw, _ -> TransitionResult.Blocked(BlockReason.InvalidTransition)

let isPlayerX : Guard<TicTacToeState, TicTacToeEvent, GameContext> =
    { Name = "isPlayerX"
      Predicate = fun ctx ->
          if ctx.User.HasClaim("player", "X") then Allowed
          else GuardResult.Blocked(NotYourTurn) }

let isPlayerO : Guard<TicTacToeState, TicTacToeEvent, GameContext> =
    { Name = "isPlayerO"
      Predicate = fun ctx ->
          if ctx.User.HasClaim("player", "O") then Allowed
          else GuardResult.Blocked(NotYourTurn) }

let gameMachine : StateMachine<TicTacToeState, TicTacToeEvent, GameContext> =
    { Initial = XTurn
      Transition = gameTransition
      Guards = [isPlayerX; isPlayerO]
      StateMetadata = Map.empty }  // Populated by CE build

let handleMove (ctx: HttpContext) : Task =
    task {
        // Parse move from request body (simplified)
        StateMachineContext.setEvent ctx (MakeMove 0)
        ctx.Response.StatusCode <- 200
    }

let getGameState (ctx: HttpContext) : Task =
    task {
        ctx.Response.StatusCode <- 200
        do! ctx.Response.WriteAsync("game state")
    }
```

2. Build the stateful resource:

```fsharp
let gameResource = statefulResource "/games/{gameId}" {
    machine gameMachine
    resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
    inState (forState XTurn [post handleMove; get getGameState])
    inState (forState OTurn [post handleMove; get getGameState])
    inState (forState (Won "X") [get getGameState])
    inState (forState (Won "O") [get getGameState])
    inState (forState Draw [get getGameState])
}
```

**Files**: `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
**Notes**:
- Keep the tic-tac-toe logic simple -- it's a test fixture, not a full game
- The `Won` state carries winner but the guard logic is the interesting part
- Guards in the test machine should be state-specific (isPlayerX only for XTurn, isPlayerO for OTurn)

### Subtask T028 -- Test state-dependent method availability

**Purpose**: Verify that HTTP methods are filtered by current state.

**Steps**:
```fsharp
[<Tests>]
let methodFilteringTests =
    testList "State-dependent methods" [
        testTask "POST allowed in XTurn state" {
            use server = createTestServer gameResource
            let client = server.CreateClient()
            // Game starts in XTurn (Initial)
            let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", null)
            Expect.equal response.StatusCode HttpStatusCode.OK "POST should be allowed"
        }

        testTask "DELETE returns 405 in XTurn state" {
            use server = createTestServer gameResource
            let client = server.CreateClient()
            let! (response: HttpResponseMessage) = client.DeleteAsync("/games/game1")
            Expect.equal response.StatusCode HttpStatusCode.MethodNotAllowed "DELETE should be 405"
            // Verify Allow header lists GET, POST
            let allow = response.Content.Headers.Allow
            Expect.contains allow "GET" "Allow should include GET"
            Expect.contains allow "POST" "Allow should include POST"
        }

        testTask "POST returns 405 in Won state" {
            use server = createTestServer gameResource
            // Set state to Won first via store
            // ...
            let! (response: HttpResponseMessage) = client.PostAsync("/games/game1", null)
            Expect.equal response.StatusCode HttpStatusCode.MethodNotAllowed "POST in Won should be 405"
        }
    ]
```

**Files**: `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
**Parallel?**: Yes -- independent of T029-T031.

### Subtask T029 -- Test guard evaluation with ClaimsPrincipal

**Purpose**: Verify guards correctly evaluate `ClaimsPrincipal` and return appropriate HTTP status codes.

**Steps**:
```fsharp
[<Tests>]
let guardTests =
    testList "Guard evaluation" [
        testTask "Player X can POST in XTurn state" {
            // Create client with player=X claim
            // POST to /games/game1
            // Expect 200
        }

        testTask "Player O gets 409 in XTurn state" {
            // Create client with player=O claim
            // POST to /games/game1 (state is XTurn)
            // Expect 409 Conflict (NotYourTurn)
        }

        testTask "Unauthenticated user gets 403" {
            // Create client with no player claim
            // POST to /games/game1
            // Expect 403 Forbidden (NotAllowed)
        }
    ]
```

**Files**: `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
**Parallel?**: Yes -- independent of T028, T030, T031.
**Notes**: May need test authentication middleware to inject claims into `HttpContext.User`

### Subtask T030 -- Test transition hooks

**Purpose**: Verify `onTransition` observers receive correct `TransitionEvent` data.

**Steps**:
```fsharp
[<Tests>]
let transitionHookTests =
    testList "Transition hooks" [
        testTask "onTransition fires after successful move" {
            let mutable receivedEvent = None
            let gameResourceWithHook = statefulResource "/games/{gameId}" {
                machine gameMachine
                resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
                onTransition (fun evt -> receivedEvent <- Some evt)
                inState (forState XTurn [post handleMove; get getGameState])
                inState (forState OTurn [post handleMove; get getGameState])
            }

            use server = createTestServer gameResourceWithHook
            let client = createClientWithClaims server [Claim("player", "X")]
            let! _ = client.PostAsync("/games/game1", null)

            Expect.isSome receivedEvent "should have received transition event"
            let evt = receivedEvent.Value
            Expect.equal evt.PreviousState XTurn "previous state"
            Expect.equal evt.NewState OTurn "new state"
        }

        testTask "onTransition does not fire on blocked request" {
            let mutable fired = false
            // Register observer, send request from wrong player
            // Verify observer was NOT called
            Expect.isFalse fired "should not fire on blocked request"
        }
    ]
```

**Files**: `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
**Parallel?**: Yes -- independent of T028, T029, T031.

### Subtask T031 -- Test filtered affordances

**Purpose**: Verify that responses include only available transitions per state (affordance filtering).

The core concept: a GET handler should be able to discover which methods/transitions are available in the current state and include that information in the response. This is analogous to returning `(currentState, allowedTransitions)` -- the handler knows what actions are possible and communicates them to the client. The exact format is an implementation decision for this WP.

**Steps**:
```fsharp
[<Tests>]
let affordanceTests =
    testList "Filtered affordances" [
        testTask "GET in XTurn returns state with available methods" {
            // GET /games/game1 in XTurn state
            // Handler should be able to query StateMachineMetadata for current state's
            // allowed methods and include them in the response.
            // Verify response body or headers contain POST as an available action.
        }

        testTask "GET in Won returns no POST affordance" {
            // GET /games/game1 in Won state
            // Handler queries available methods for Won state -- only GET.
            // Verify response does NOT include POST as available.
        }

        testTask "Handler can access allowed transitions for current state" {
            // Verify that a handler can programmatically query which HTTP methods
            // are available in the current state via StateMachineMetadata.
            // This enables handlers to build hypermedia responses with only
            // valid action links.
        }
    ]
```

**Files**: `test/Frank.Statecharts.Tests/StatefulResourceTests.fs`
**Parallel?**: Yes -- independent of T028-T030.
**Notes**:
- The key requirement is that handlers can discover per-state allowed methods at runtime
- The exact response format (Link headers, JSON body with `_links`, etc.) is an implementation decision -- not prescribed here
- Consider providing a helper function (e.g., `StateMachineContext.getAllowedMethods`) that handlers can call to query the current state's available methods from `StateMachineMetadata`
- This test validates the mechanism, not a specific hypermedia format; exact affordance serialization may evolve with LinkedData integration

---

## Test Strategy

- All tests use ASP.NET Core TestHost
- Test authentication: register a test auth scheme that populates `ClaimsPrincipal` from a custom header or configured claims
- `dotnet test test/Frank.Statecharts.Tests/` must pass with all integration tests green
- Test the complete pipeline: request -> middleware -> state lookup -> guard -> handler -> transition -> hook

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| TestHost setup complexity | Reference `Frank.Auth.Tests` for patterns |
| ClaimsPrincipal mocking in TestHost | Use `ConfigureTestServices` to register test auth handler |
| Store state across requests in tests | Use `IServiceProvider` to resolve store and pre-populate state |
| Affordance format undefined | Focus on verifying method availability, not exact format |

---

## Review Guidance

- Verify all HTTP status codes are tested: 200, 405, 403, 409, 400
- Verify guard tests use realistic `ClaimsPrincipal` (not mocked interfaces)
- Verify transition hooks test both positive (fires) and negative (doesn't fire on blocked)
- Verify store lifecycle: create instance, transition, verify final state
- Verify TestHost properly disposes resources
- Run full test suite: `dotnet test test/Frank.Statecharts.Tests/`

---

## Activity Log

- 2026-03-06T00:00:00Z -- system -- lane=planned -- Prompt created.
