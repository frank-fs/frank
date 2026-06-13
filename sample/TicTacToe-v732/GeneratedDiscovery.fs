/// FRANK-STUB(AT-S1,AT-S2,AT-S3): hand-authored stand-in for the generated
/// discovery artifact. `frank semantic` + MSBuild will emit this into obj/ from
/// the F# types + vocabulary { using "schema" } lock file (round 2). schema.org
/// IRIs here must match what the generator produces.
module TicTacToe.GeneratedDiscovery

open Frank.Discovery

let discoveryConfig: DiscoveryConfig =
    { ProfileUri = "/alps/tictactoe"
      HomeRoute = "/"
      AlpsDescriptors =
        [ { Id = "Game"
            Type = "semantic"
            Doc = Some "A tic-tac-toe game"
            Href = Some "https://schema.org/Game" }
          { Id = "position"
            Type = "semantic"
            Doc = Some "Board square of a move"
            Href = Some "https://schema.org/position" }
          { Id = "agent"
            Type = "semantic"
            Doc = Some "The player making a move"
            Href = Some "https://schema.org/agent" }
          { Id = "game"
            Type = "safe"
            Doc = Some "Read current game state"
            Href = None }
          { Id = "makeMove"
            Type = "unsafe"
            Doc = Some "Submit a move"
            Href = None } ]
      DescribedByLinks = [ "<https://schema.org/Game>; rel=\"describedby\"" ]
      HomeResources =
        [ { Relation = "https://schema.org/Game"
            Href = "/games/{id}"
            Allow = [ "GET"; "HEAD"; "OPTIONS" ] }
          { Relation = "https://schema.org/Action"
            Href = "/games/{id}/moves"
            Allow = [ "POST"; "OPTIONS" ] } ] }
