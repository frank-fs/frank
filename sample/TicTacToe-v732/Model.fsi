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

    static member TryParse: string -> SquarePosition option

[<StructuralEquality; StructuralComparison>]
[<Struct>]
type Player =
    | X
    | O

    static member TryParse: string -> Player option

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

    static member TryParse: player: string * position: string -> Move option

type StartGame = unit -> MoveResult

type MakeMove = MoveResult * Move -> MoveResult

val startGame: StartGame

val makeMove: MakeMove
