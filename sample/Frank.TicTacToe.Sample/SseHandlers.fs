/// Datastar SSE handler that uses affordance data for conditional rendering.
/// Demonstrates how the affordance middleware's headers can inform SSE responses.
module Frank.TicTacToe.Sample.SseHandlers

open System.Threading.Tasks
open FSharp.Reflection
open Microsoft.AspNetCore.Http
open Frank.Affordances
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

/// SSE handler that streams the current game board.
/// Reads state from the store and uses the affordance map to determine rendering.
let streamGameBoard (ctx: HttpContext) : Task =
    task {
        let store =
            ctx.RequestServices.GetService(typeof<IStateMachineStore<TicTacToeState, int>>)
            :?> IStateMachineStore<TicTacToeState, int>

        let! stateResult = store.GetState "game1"

        let state, moveCount =
            match stateResult with
            | Some(s, c) -> (s, c)
            | None -> (gameMachine.Initial, gameMachine.InitialContext)

        let key = stateKeyOf state
        let html = renderBoard key moveCount
        do! Frank.Datastar.Datastar.patchElements html ctx
    }
    :> Task
