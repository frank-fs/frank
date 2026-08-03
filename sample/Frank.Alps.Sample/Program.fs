module Frank.Alps.Sample.Program

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Alps

/// The ALPS profile: two states (a game is either accepting moves or finished), one
/// semantic descriptor for the resource itself, and two transitions -- `viewGame` (safe,
/// bound to GET) and `makeMove` (unsafe, bound to POST, only valid from the "open" state).
module Catalog =
    let openState = semantic "open" |> doc "Accepting moves" |> def "https://tictactoe.example/states/open"
    let closedState = semantic "closed" |> doc "Game finished" |> def "https://tictactoe.example/states/closed"
    let game = semantic "game" |> doc "A tic-tac-toe game"

    let viewGame = safe "viewGame" |> rt game
    let makeMove = unsafe "makeMove" |> from [ openState ] |> rt closedState

let private getGameJson (ctx: HttpContext) : Task =
    ctx.Response.WriteAsJsonAsync {| id = ctx.Request.RouteValues.["id"] |}

let private makeMoveHandler (ctx: HttpContext) : Task =
    ctx.Response.WriteAsJsonAsync {| ok = true |}

/// `get` negotiates two representations at the SAME `/games/{id}` url: the plain-JSON
/// primary representation (bound to Catalog.viewGame via `binds`, so `Alps.excerpt` and
/// `useAlps`'s startup validation both see it) and the ALPS excerpt itself, served by
/// `Alps.excerpt None` (no CurrentStateResolver -- this sample has no provenance/event
/// store to ask "what state is this game in", so `from`-state filtering simply does not
/// apply here; see Excerpt.fsi). `link` advertises the excerpt via a resource-scoped
/// Link header on every response this resource returns, mirroring
/// Frank.Rdf.Sample.Program's own `link` usage for its "alternate" JSON-LD representation.
let private gameResource =
    resource "/games/{id}" {
        link (fun ctx ->
            Seq.singleton
                { Target = string ctx.Request.Path
                  Rel = "profile"
                  Params = [ "type", "application/alps+json" ] })

        get (
            negotiate {
                accepts "application/json" (handler {
                    handle getGameJson
                    binds Catalog.viewGame
                })

                accepts "application/alps+json" (Alps.excerpt None)
            }
        )

        post (handler {
            handle makeMoveHandler
            binds Catalog.makeMove
        })
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults
        resource gameResource

        useAlps [ Catalog.openState; Catalog.closedState; Catalog.game; Catalog.viewGame; Catalog.makeMove ]
    }

    0
