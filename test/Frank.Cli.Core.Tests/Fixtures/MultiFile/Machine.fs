module MultiFile.Machine

open MultiFile.Types

let gameMachine: StateMachine<GameState, GameEvent, int> =
    { Initial = Playing
      InitialContext = 0 }
