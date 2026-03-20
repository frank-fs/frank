/// Datastar SSE handler demonstrating the correct push pattern:
/// - Long-lived SSE connection via store.Subscribe + Channel<T>
/// - Affordance-driven conditional rendering (POST allowed = show move button)
/// - No polling — state changes push through the MailboxProcessorStore's
///   BehaviorSubject subscription directly to the SSE response.
module Frank.TicTacToe.Sample.SseHandlers

open System
open System.Threading.Channels
open System.Threading.Tasks
open FSharp.Reflection
open Microsoft.AspNetCore.Http
open Frank.Resources.Model
open Frank.Statecharts
open Frank.TicTacToe.Sample.Domain

/// Extract the DU case name for use as a state key, matching StateKeyExtractor behavior.
let stateKeyOf (state: TicTacToeState) : string =
    let tagReader = FSharpValue.PreComputeUnionTagReader(typeof<TicTacToeState>)
    let cases = FSharpType.GetUnionCases(typeof<TicTacToeState>, true)
    let caseNames = cases |> Array.map (fun c -> c.Name)
    caseNames.[tagReader (box state)]

/// Render HTML for the game board based on current state and affordances.
/// When POST is in the allowed methods (from the affordance map), render move buttons.
/// When only GET is allowed, render a read-only board.
let renderBoard (stateKey: string) (moveCount: int) : string =
    let entry =
        gameAffordanceMap.Entries
        |> List.tryFind (fun e -> e.StateKey = stateKey)

    let canMove =
        entry
        |> Option.map (fun e -> List.contains "POST" e.AllowedMethods)
        |> Option.defaultValue false

    let statusText =
        match stateKey with
        | "XTurn" -> "X's turn"
        | "OTurn" -> "O's turn"
        | "Won" -> "Game over!"
        | "Draw" -> "It's a draw!"
        | _ -> "Unknown state"

    let moveButtons =
        if canMove then
            $"""<div id="game-controls">
                <p>Moves made: {moveCount}</p>
                <button data-on:click="@post('/games/game1')">Make Move</button>
            </div>"""
        else
            $"""<div id="game-controls">
                <p>Moves made: {moveCount}. Game is over.</p>
            </div>"""

    $"""<div id="game-board">
        <h2 id="game-status">{statusText}</h2>
        {moveButtons}
    </div>"""

/// Long-lived SSE handler that subscribes to the store for state change notifications.
/// The store's BehaviorSubject semantics ensure the current state is sent immediately
/// on subscribe, then updates are pushed on every state transition.
///
/// Flow: POST -> statechart middleware -> store.SetState -> subscriber.OnNext -> Channel -> SSE push
let streamGameBoard (ctx: HttpContext) : Task =
    task {
        let store =
            ctx.RequestServices.GetService(typeof<IStateMachineStore<TicTacToeState, int>>)
            :?> IStateMachineStore<TicTacToeState, int>

        let channel = Channel.CreateUnbounded<string>()

        // Subscribe to the store for "game1". BehaviorSubject semantics:
        // if state already exists, we get it immediately via OnNext.
        // Every subsequent SetState (from statechart middleware after a move)
        // triggers another OnNext.
        let subscription =
            store.Subscribe
                "game1"
                { new IObserver<TicTacToeState * int> with
                    member _.OnNext((state, moveCount)) =
                        let key = stateKeyOf state
                        let html = renderBoard key moveCount
                        channel.Writer.TryWrite(html) |> ignore

                    member _.OnError(_) = channel.Writer.Complete()
                    member _.OnCompleted() = channel.Writer.Complete() }

        try
            // Loop until the client disconnects. Each channel read blocks
            // until the store notifies us of a state change.
            while not ctx.RequestAborted.IsCancellationRequested do
                let! html = channel.Reader.ReadAsync(ctx.RequestAborted).AsTask()
                do! Frank.Datastar.Datastar.patchElements html ctx
        with
        | :? OperationCanceledException -> ()
        | :? ChannelClosedException -> ()

        subscription.Dispose()
    }
    :> Task
