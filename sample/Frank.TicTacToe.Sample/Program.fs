/// Tic-tac-toe reference application demonstrating the unified resource pipeline:
/// - Stateful resource with state machine (Frank.Statecharts)
/// - Affordance middleware for Link/Allow headers (Frank.Affordances)
/// - Datastar SSE handler for reactive UI updates (Frank.Datastar)
///
/// Pipeline order (after routing):
///   1. State key resolver — reads state from store, sets ctx.Items["statechart.stateKey"]
///   2. Affordance middleware — reads Items key, injects Link + Allow headers
///   3. Statechart middleware — dispatches to state-specific handler
module Frank.TicTacToe.Sample.Program

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Frank.Builder
open Frank.Datastar
open Frank.Statecharts
open Frank.Affordances
open Frank.TicTacToe.Sample.Domain
open Frank.TicTacToe.Sample.SseHandlers

// === State key bridge middleware ===

/// Resolves the statechart state key from the store and places it in
/// HttpContext.Items so the affordance middleware can look it up.
/// Must run AFTER routing (to have endpoint metadata) and BEFORE
/// the affordance middleware (which reads the Items key).
let resolveStateKey (app: IApplicationBuilder) =
    app.Use(Func<HttpContext, Func<Task>, Task>(fun ctx next ->
        task {
            let endpoint = ctx.GetEndpoint()

            if not (isNull endpoint) then
                let metadata = endpoint.Metadata.GetMetadata<StateMachineMetadata>()

                if not (obj.ReferenceEquals(metadata, null)) then
                    let instanceId = metadata.ResolveInstanceId ctx
                    let! stateKey = metadata.GetCurrentStateKey ctx.RequestServices ctx instanceId
                    ctx.Items.[AffordanceMap.StateKeyItemsKey] <- stateKey

            do! next.Invoke()
        }
        :> Task))

// === Handlers ===

/// GET handler: returns the current game state as text.
let getGameState (ctx: HttpContext) : Task =
    task {
        let store =
            ctx.RequestServices.GetService(typeof<IStateMachineStore<TicTacToeState, int>>)
            :?> IStateMachineStore<TicTacToeState, int>

        let! stateResult = store.GetState(ctx.Request.RouteValues["gameId"] :?> string)

        let state, moveCount =
            match stateResult with
            | Some(s, c) -> (s, c)
            | None -> (gameMachine.Initial, gameMachine.InitialContext)

        let key = stateKeyOf state
        do! ctx.Response.WriteAsync($"state={key};moves={moveCount}")
    }
    :> Task

/// POST handler: triggers a move event.
let handleMove (ctx: HttpContext) : Task =
    StateMachineContext.setEvent ctx (MakeMove 0)
    Task.CompletedTask

// === Resource definition ===

let gameResource =
    statefulResource "/games/{gameId}" {
        machine gameMachine
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
        inState (forState XTurn [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ])
        inState (forState OTurn [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ])
        inState (forState (Won "X") [ StateHandlerBuilder.get getGameState ])
        inState (forState (Won "O") [ StateHandlerBuilder.get getGameState ])
        inState (forState Draw [ StateHandlerBuilder.get getGameState ])
    }

/// SSE endpoint for streaming game board updates via Datastar.
let sseResource =
    resource "/games/{gameId}/sse" {
        name "GameSSE"

        datastar (fun (ctx: HttpContext) ->
            task { do! streamGameBoard ctx })
    }

// === Application entry point ===

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        service (fun services ->
            services.AddStateMachineStore<TicTacToeState, int>() |> ignore
            services)

        // Pipeline order matters:
        // 1. State key resolver (bridge between store and affordance Items convention)
        plug resolveStateKey
        // 2. Affordance middleware (injects Link + Allow headers)
        useAffordances gameAffordanceMap
        // 3. Statechart middleware (state-dependent handler dispatch)
        useStatecharts

        resource gameResource
        resource sseResource
    }

    0
