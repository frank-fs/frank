module TicTacToe.Types

type Player = X | O

type Cell = Empty | Taken of Player

type GameStatus = InProgress | XWins | OWins | Draw

type Game = {
    Id: string
    Board: Cell[,]          // 3x3 array, row-major
    CurrentPlayer: Player
    Status: GameStatus
}

type MoveRequest = {
    Row: int
    Col: int
    Player: string          // "X" or "O"
}

module Game =
    let create id = {
        Id = id
        Board = Array2D.create 3 3 Empty
        CurrentPlayer = X
        Status = InProgress
    }

    let checkWinner (board: Cell[,]) =
        let lines = [
            // rows
            [(0,0);(0,1);(0,2)]; [(1,0);(1,1);(1,2)]; [(2,0);(2,1);(2,2)]
            // cols
            [(0,0);(1,0);(2,0)]; [(0,1);(1,1);(2,1)]; [(0,2);(1,2);(2,2)]
            // diagonals
            [(0,0);(1,1);(2,2)]; [(0,2);(1,1);(2,0)]
        ]
        lines |> List.tryFind (fun line ->
            let cells = line |> List.map (fun (r, c) -> board.[r, c])
            cells |> List.forall (fun c -> c = Taken X) ||
            cells |> List.forall (fun c -> c = Taken O))
        |> Option.map (fun line ->
            let (r, c) = List.head line
            match board.[r, c] with Taken p -> p | Empty -> X)

    let isFull (board: Cell[,]) =
        let mutable full = true
        for r in 0..2 do
            for c in 0..2 do
                if board.[r, c] = Empty then full <- false
        full

    let applyMove (game: Game) (row: int) (col: int) (player: Player) : Result<Game, string> =
        if row < 0 || row > 2 || col < 0 || col > 2 then
            Error "Row and column must be 0-2"
        elif game.Status <> InProgress then
            Error "Game is already over"
        elif game.CurrentPlayer <> player then
            Error $"It is {game.CurrentPlayer}'s turn"
        elif game.Board.[row, col] <> Empty then
            Error $"Cell ({row},{col}) is already occupied"
        else
            let newBoard = Array2D.copy game.Board
            newBoard.[row, col] <- Taken player
            let status =
                match checkWinner newBoard with
                | Some X -> XWins
                | Some O -> OWins
                | None -> if isFull newBoard then Draw else InProgress
            let next = if player = X then O else X
            Ok { game with Board = newBoard; CurrentPlayer = next; Status = status }
