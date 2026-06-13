module TicTacToe.Model

open System.Collections.Generic

[<StructuralEquality; StructuralComparison>]
[<Struct>]
type SquarePosition =
    | TopLeft
    | TopCenter
    | TopRight
    | MiddleLeft
    | MiddleCenter
    | MiddleRight
    | BottomLeft
    | BottomCenter
    | BottomRight

    override this.ToString() =
        match this with
        | TopLeft -> "TopLeft"
        | TopCenter -> "TopCenter"
        | TopRight -> "TopRight"
        | MiddleLeft -> "MiddleLeft"
        | MiddleCenter -> "MiddleCenter"
        | MiddleRight -> "MiddleRight"
        | BottomLeft -> "BottomLeft"
        | BottomCenter -> "BottomCenter"
        | BottomRight -> "BottomRight"

    static member TryParse(str: string) =
        match str with
        | "TopLeft" -> Some TopLeft
        | "TopCenter" -> Some TopCenter
        | "TopRight" -> Some TopRight
        | "MiddleLeft" -> Some MiddleLeft
        | "MiddleCenter" -> Some MiddleCenter
        | "MiddleRight" -> Some MiddleRight
        | "BottomLeft" -> Some BottomLeft
        | "BottomCenter" -> Some BottomCenter
        | "BottomRight" -> Some BottomRight
        | _ -> None

[<StructuralEquality; StructuralComparison>]
[<Struct>]
type Player =
    | X
    | O

    override this.ToString() =
        match this with
        | X -> "X"
        | O -> "O"

    static member TryParse(str: string) =
        match str with
        | "X" -> Some X
        | "O" -> Some O
        | _ -> None

[<StructuralEquality; StructuralComparison>]
[<Struct>]
type SquareState =
    | Taken of Player
    | Empty

[<StructuralEquality; StructuralComparison>]
[<Struct>]
type XPosition = XPos of SquarePosition

[<StructuralEquality; StructuralComparison>]
[<Struct>]
type OPosition = OPos of SquarePosition

type ValidMovesForX = XPosition[]

type ValidMovesForO = OPosition[]

type GameState = IReadOnlyDictionary<SquarePosition, SquareState>

type MoveResult =
    | XTurn of GameState * ValidMovesForX
    | OTurn of GameState * ValidMovesForO
    | Won of GameState * Player
    | Draw of GameState
    | Error of GameState * string

[<StructuralEquality; StructuralComparison>]
[<Struct>]
type Move =
    | XMove of SquarePosition
    | OMove of SquarePosition

    static member TryParse(player: string, position: string) =
        match Player.TryParse(player), SquarePosition.TryParse(position) with
        | Some X, Some pos -> Some(XMove pos)
        | Some O, Some pos -> Some(OMove pos)
        | _ -> None

type XMove = MoveResult * XPosition -> MoveResult

type OMove = MoveResult * OPosition -> MoveResult

type StartGame = unit -> MoveResult

type MakeMove = MoveResult * Move -> MoveResult

let startGame: StartGame =
    fun () ->
        let gameState =
            [| TopLeft, Empty
               TopCenter, Empty
               TopRight, Empty
               MiddleLeft, Empty
               MiddleCenter, Empty
               MiddleRight, Empty
               BottomLeft, Empty
               BottomCenter, Empty
               BottomRight, Empty |]
            |> readOnlyDict

        let validMovesForX: ValidMovesForX =
            [| for KeyValue(pos, state) in gameState do
                   if state = Empty then
                       yield XPos pos |]

        XTurn(gameState, validMovesForX)

let winningCombinations =
    [ [ TopLeft; TopCenter; TopRight ]
      [ MiddleLeft; MiddleCenter; MiddleRight ]
      [ BottomLeft; BottomCenter; BottomRight ]
      [ TopLeft; MiddleLeft; BottomLeft ]
      [ TopCenter; MiddleCenter; BottomCenter ]
      [ TopRight; MiddleRight; BottomRight ]
      [ TopLeft; MiddleCenter; BottomRight ]
      [ TopRight; MiddleCenter; BottomLeft ] ]

let rec tryFindWinningPlayer gameState =
    tryFindWinningPlayerIter (ValueNone, winningCombinations) gameState

and tryFindWinningPlayerIter acc gameState =
    match acc with
    | ValueNone, combination :: remainingCombinations ->
        match testCombination combination gameState with
        | ValueSome player -> ValueSome player
        | ValueNone -> tryFindWinningPlayerIter (ValueNone, remainingCombinations) gameState
    | _ -> ValueNone

and testCombination combination (gameState: GameState) =
    match combination with
    | [ first; second; third ] ->
        match gameState.TryGetValue(first), gameState.TryGetValue(second), gameState.TryGetValue(third) with
        | (true, Taken player1), (true, Taken player2), (true, Taken player3) when
            player1 = player2 && player2 = player3
            ->
            ValueSome player1
        | _ -> ValueNone
    | _ -> ValueNone

let (|HasWinner|IsDraw|InProgress|) (gameState: GameState) =
    match tryFindWinningPlayer gameState with
    | ValueSome player -> HasWinner player
    | ValueNone ->
        if gameState.Values |> Seq.forall (fun state -> state <> Empty) then
            IsDraw
        else
            InProgress

let moveX: XMove =
    fun (moveResult, XPos xPosition) ->
        match moveResult with
        | XTurn(gameState, _) ->
            match gameState.TryGetValue(xPosition) with
            | true, Empty ->
                let gameState' =
                    [| for KeyValue(pos, state) in gameState -> pos, if pos = xPosition then Taken X else state |]
                    |> readOnlyDict

                // First check for a winner
                match gameState' with
                | HasWinner player -> Won(gameState', player)
                // Then check for a draw
                | IsDraw -> Draw(gameState')
                | InProgress ->
                    let validMovesForO: ValidMovesForO =
                        [| for KeyValue(pos, state) in gameState' do
                               if state = Empty then
                                   yield OPos pos |]

                    OTurn(gameState', validMovesForO)
            | _ -> Error(gameState, "Invalid move")
        | OTurn(gameState, _) -> Error(gameState, "Invalid move")
        | Won(gameState, _) -> Error(gameState, "Game already won")
        | Draw gameState -> Error(gameState, "Game over")
        | _ -> moveResult

let moveO: OMove =
    fun (moveResult, OPos oPosition) ->
        match moveResult with
        | OTurn(gameState, _) ->
            match gameState.TryGetValue(oPosition) with
            | true, Empty ->
                let gameState' =
                    [| for KeyValue(pos, state) in gameState -> pos, if pos = oPosition then Taken O else state |]
                    |> readOnlyDict

                // First check for a winner
                match gameState' with
                | HasWinner player -> Won(gameState', player)
                // Then check for a draw
                | IsDraw -> Draw(gameState')
                | InProgress ->
                    let validMovesForX: ValidMovesForX =
                        [| for KeyValue(pos, state) in gameState' do
                               if state = Empty then
                                   yield XPos pos |]

                    XTurn(gameState', validMovesForX)
            | _ -> Error(gameState, "Invalid move")
        | XTurn(gameState, _) -> Error(gameState, "Invalid move")
        | Won(gameState, _) -> Error(gameState, "Game already won")
        | Draw gameState -> Error(gameState, "Game over")
        | _ -> moveResult

let makeMove: MakeMove =
    fun (moveResult, move) ->
        match moveResult, move with
        | XTurn _, XMove pos -> moveX (moveResult, XPos pos)
        | XTurn(gameState, _), _ -> Error(gameState, "Invalid move")
        | OTurn _, OMove pos -> moveO (moveResult, OPos pos)
        | OTurn(gameState, _), _ -> Error(gameState, "Invalid move")
        | Won(gameState, _), _ -> Error(gameState, "Game already won")
        | Draw gameState, _ -> Error(gameState, "Game over")
        | _ -> moveResult
