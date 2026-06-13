module TicTacToe.GameStore

open System.Collections.Concurrent
open TicTacToe.Types

let private games = ConcurrentDictionary<string, Game>()

let getOrCreate (id: string) =
    games.GetOrAdd(id, fun key -> Game.create key)

let tryGet (id: string) =
    match games.TryGetValue(id) with
    | true, g -> Some g
    | _ -> None

let update (game: Game) =
    games.[game.Id] <- game
    game
