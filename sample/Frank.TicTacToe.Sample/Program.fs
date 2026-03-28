/// Tic-tac-toe reference application demonstrating the unified resource pipeline:
/// - Stateful resource with state machine (Frank.Statecharts)
/// - Affordance middleware for Link/Allow headers (Frank.Affordances)
/// - Datastar SSE handler for reactive UI updates (Frank.Datastar)
///
/// The Datastar SSE path is the primary consumption channel. POST handlers
/// are fire-and-forget (202 Accepted) — state updates push through the
/// store's BehaviorSubject subscription to all connected SSE clients.
/// GET handlers exist as a degraded fallback for non-Datastar clients.
///
/// Pipeline order (after routing):
///   1. State key resolver — reads state from store, sets IStatechartFeature on HttpContext.Features
///   2. Affordance middleware — reads IStatechartFeature, injects Link + Allow headers
///   3. Projected profile middleware — role-specific ALPS profile links
///   4. Statechart middleware — dispatches to state-specific handler
module Frank.TicTacToe.Sample.Program

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Frank.Builder
open Frank.Datastar
open Frank.Statecharts
open Frank.Affordances
open Frank.TicTacToe.Sample.Domain
open Frank.TicTacToe.Sample.SseHandlers

// === State key bridge middleware ===

/// Resolves the statechart state key from the store and sets IStatechartFeature
/// on HttpContext.Features so the affordance middleware can look it up.
/// Must run AFTER routing (to have endpoint metadata) and BEFORE
/// the affordance middleware (which reads the feature).
let resolveStateKey (app: IApplicationBuilder) =
    app.Use(
        Func<HttpContext, Func<Task>, Task>(fun ctx next ->
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
            :> Task)
    )

// === Handlers ===

/// GET handler: returns the current game state as text.
/// This is a degraded fallback for non-Datastar clients (curl, direct URL access).
/// The primary consumption path is the SSE endpoint.
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

/// POST handler: triggers a move event. Fire-and-forget (202 Accepted).
/// The statechart middleware processes the transition and calls store.SetState,
/// which notifies all SSE subscribers via the store's BehaviorSubject.
/// No broadcast code here — the store subscription handles delivery.
let handleMove (ctx: HttpContext) : Task =
    StateMachineContext.setEvent ctx (MakeMove 0)
    ctx.Response.StatusCode <- 202
    Task.CompletedTask

// === Resource definition ===

let gameResource =
    statefulResource "/games/{gameId}" {
        machine gameMachine
        resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
        role "PlayerX" (fun user -> user.HasClaim("player", "X"))
        role "PlayerO" (fun user -> user.HasClaim("player", "O"))
        role "Spectator" (fun _user -> true)
        inState (forState XTurn [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ])
        inState (forState OTurn [ StateHandlerBuilder.get getGameState; StateHandlerBuilder.post handleMove ])
        inState (forState (Won "X") [ StateHandlerBuilder.get getGameState ])
        inState (forState (Won "O") [ StateHandlerBuilder.get getGameState ])
        inState (forState Draw [ StateHandlerBuilder.get getGameState ])
    }

/// SSE endpoint for streaming game board updates via Datastar.
/// Long-lived connection: subscribes to the store and pushes HTML fragments
/// on every state change until the client disconnects.
let sseResource =
    resource "/games/{gameId}/sse" {
        name "GameSSE"

        datastar (fun (ctx: HttpContext) -> task { do! streamGameBoard ctx })
    }

// === Seed initial game state ===

/// Seeds the initial game state into the store on application startup.
/// Without this, the first SSE subscriber would see no state until
/// the first POST creates it via the statechart middleware.
let seedInitialState (app: IApplicationBuilder) =
    let lifetime =
        app.ApplicationServices.GetRequiredService<IHostApplicationLifetime>()

    let store =
        app.ApplicationServices.GetRequiredService<IStateMachineStore<TicTacToeState, int>>()

    lifetime.ApplicationStarted.Register(fun () ->
        store.SetState "game1" gameMachine.Initial gameMachine.InitialContext
        |> fun t -> t.Wait())
    |> ignore

    app

// === Application entry point ===

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        service (fun services ->
            services.AddStateMachineStore<TicTacToeState, int>() |> ignore
            services)

        // Pipeline order matters:
        // 1. State key resolver (reads store, sets IStatechartFeature on HttpContext.Features)
        plug resolveStateKey
        // 2. Affordance middleware (reads IStatechartFeature, injects Link + Allow headers)
        useAffordances
        // 3. Projected profile middleware (role-specific ALPS profile links)
        useProjectedProfiles
        // 4. Statechart middleware (state-dependent handler dispatch)
        useStatecharts

        // Seed game state after services are built
        plug seedInitialState

        resource gameResource
        resource sseResource
    }

    0
