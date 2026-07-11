module TestFixtures.VocabWithRouteAndTttRef

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// Both a /tictactoe route AND explicit ttt: CURIE references.
// Used for AT-route-hint and AT-authority-fixture tests (GAP 3 scope pinning).
let tttThing = "ttt:Thing"
let tttMove = "ttt:Move"

let tttResource =
    resource "/tictactoe" {
        get handler
    }
