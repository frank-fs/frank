module MultiFile.Types

type GameState =
    | Playing
    | Won
    | Draw

type GameEvent = MakeMove of position: int

/// Minimal StateMachine record that mirrors Frank.Statecharts.StateMachine
/// for testing cross-file extraction without requiring the full Frank dependency chain.
type StateMachine<'State, 'Event, 'Context> =
    { Initial: 'State
      InitialContext: 'Context }
