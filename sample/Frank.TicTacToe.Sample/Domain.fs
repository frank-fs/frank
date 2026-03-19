/// Tic-tac-toe domain types and state machine definition.
/// Adapted from StatefulResourceTests for use as a reference application.
module Frank.TicTacToe.Sample.Domain

open System.Security.Claims
open Frank.Statecharts

// === Domain types ===

type TicTacToeState =
    | XTurn
    | OTurn
    | Won of winner: string
    | Draw

type TicTacToeEvent = MakeMove of position: int

// === Transition function ===

let gameTransition (state: TicTacToeState) (_event: TicTacToeEvent) (moveCount: int) =
    match state with
    | XTurn ->
        let n = moveCount + 1

        if n >= 5 then
            TransitionResult.Transitioned(Won "X", n)
        else
            TransitionResult.Transitioned(OTurn, n)
    | OTurn ->
        let n = moveCount + 1

        if n >= 9 then
            TransitionResult.Transitioned(Draw, n)
        else
            TransitionResult.Transitioned(XTurn, n)
    | Won _ -> TransitionResult.Invalid "Game already over"
    | Draw -> TransitionResult.Invalid "Game already over"

// === Guard ===

let turnGuard: Guard<TicTacToeState, TicTacToeEvent, int> =
    AccessControl(
        "TurnGuard",
        fun ctx ->
            match ctx.CurrentState with
            | XTurn ->
                if ctx.User.HasClaim("player", "X") then
                    Allowed
                elif ctx.User.HasClaim("player", "O") then
                    Blocked NotYourTurn
                else
                    // Anonymous users can play in this sample
                    Allowed
            | OTurn ->
                if ctx.User.HasClaim("player", "O") then
                    Allowed
                elif ctx.User.HasClaim("player", "X") then
                    Blocked NotYourTurn
                else
                    Allowed
            | Won _
            | Draw -> Allowed
    )

// === State machine definition ===

let gameMachine: StateMachine<TicTacToeState, TicTacToeEvent, int> =
    { Initial = XTurn
      InitialContext = 0
      Transition = gameTransition
      Guards = [ turnGuard ]
      StateMetadata =
        Map.ofList
            [ XTurn,
              { AllowedMethods = [ "GET"; "POST" ]
                IsFinal = false
                Description = Some "X's turn to play" }
              OTurn,
              { AllowedMethods = [ "GET"; "POST" ]
                IsFinal = false
                Description = Some "O's turn to play" }
              Won "X",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = Some "Game over: X wins" }
              Won "O",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = Some "Game over: O wins" }
              Draw,
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = Some "Game over: draw" } ] }

// === Affordance map (pre-computed at startup) ===

open Frank.Affordances

let gameAffordanceMap: AffordanceMap =
    { Version = AffordanceMap.currentVersion
      Entries =
        [ { RouteTemplate = "/games/{gameId}"
            StateKey = "XTurn"
            AllowedMethods = [ "GET"; "POST" ]
            LinkRelations =
              [ { Rel = "makeMove"
                  Href = "/games/{gameId}"
                  Method = "POST"
                  Title = Some "Make a move (X's turn)" } ]
            ProfileUrl = "" }
          { RouteTemplate = "/games/{gameId}"
            StateKey = "OTurn"
            AllowedMethods = [ "GET"; "POST" ]
            LinkRelations =
              [ { Rel = "makeMove"
                  Href = "/games/{gameId}"
                  Method = "POST"
                  Title = Some "Make a move (O's turn)" } ]
            ProfileUrl = "" }
          { RouteTemplate = "/games/{gameId}"
            StateKey = "Won"
            AllowedMethods = [ "GET" ]
            LinkRelations = []
            ProfileUrl = "" }
          { RouteTemplate = "/games/{gameId}"
            StateKey = "Draw"
            AllowedMethods = [ "GET" ]
            LinkRelations = []
            ProfileUrl = "" } ] }
