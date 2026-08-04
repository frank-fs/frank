module Frank.Alps.Sample.Program

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging.Abstractions
open VDS.RDF
open Frank.Builder
open Frank.Rdf
open Frank.Provenance
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

// frank-fs/frank#493: CurrentStateResolver backed by Frank.Provenance, demonstrating the seam both
// packages' design docs named and deferred on each other. This is glue code living here in the
// sample, not a dependency added to either package in either direction -- Frank.Alps still has no
// reference to Frank.Provenance and vice versa; only this application references both.
let private baseUri = "https://tictactoe.example"

let private resourceIriFor (path: string) : string = baseUri + path

let private store: IProvenanceStore =
    new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance) :> IProvenanceStore

/// This sample's own convention for what "current state" means: the domain rdf:type
/// (ProvenanceRecord.ActivityType) asserted on the most recently ended activity that
/// prov:wasGeneratedBy this resource. Recording makeMove's activity typed
/// `https://tictactoe.example/states/closed` is what later makes this resolver report "closed" --
/// there is no other notion of "state" in Frank.Provenance's own model, so a consuming application
/// has to pick one; this is this sample's pick, not a rule either package enforces.
let private stateResolver: CurrentStateResolver =
    fun path ->
        let resourceIri = resourceIriFor path

        match store.Query(ProvenanceQuery.Latest resourceIri) with
        | SparqlQueryResult.Bindings _ -> [] // Latest always compiles to a CONSTRUCT query -- unreachable in practice
        | SparqlQueryResult.Graph g ->
            let resourceNode = Uri resourceIri
            let rdfTypePredicate = g.CreateUriNode(Uri RdfTypeIri)
            let provActivityClass = Uri(ProvClass.toIri ProvClass.Activity)

            g.GetTriplesWithPredicate(rdfTypePredicate)
            |> Seq.choose (fun t ->
                match t.Subject, t.Object with
                | (:? IUriNode as s), (:? IUriNode as o) when s.Uri <> resourceNode && o.Uri <> provActivityClass ->
                    Some o.Uri
                | _ -> None)
            |> List.ofSeq

let private getGameJson (ctx: HttpContext) : Task =
    ctx.Response.WriteAsJsonAsync {| id = ctx.Request.RouteValues.["id"] |}

/// Records a "this game was moved" activity, typed as the state the move transitions the game
/// INTO (Catalog.closedState's own `def` IRI) -- see stateResolver above for why that typing is
/// what later makes filtering treat the game as closed.
let private makeMoveHandler (ctx: HttpContext) : Task =
    task {
        let now = DateTimeOffset.UtcNow

        store.Append(
            { Activity = Node.Iri $"{baseUri}/activities/{Guid.NewGuid()}"
              Resource = Node.Iri(resourceIriFor ctx.Request.Path.Value)
              Agent = Node.Iri $"{baseUri}/agents/anonymous"
              StartedAt = now
              EndedAt = now
              ActivityType = Catalog.closedState.Def
              Properties = [] }
        )

        do! ctx.Response.WriteAsJsonAsync {| ok = true |}
    }
    :> Task

/// `get` negotiates two representations at the SAME `/games/{id}` url: the plain-JSON
/// primary representation (bound to Catalog.viewGame via `binds`, so `Alps.excerpt` and
/// `useAlps`'s startup validation both see it) and the ALPS excerpt itself, served by
/// `Alps.excerpt (Some stateResolver)` -- a real CurrentStateResolver backed by the provenance
/// store above (frank-fs/frank#493), so `makeMove`'s `from [ openState ]` guard is genuinely
/// filtered out of the excerpt once a move has been recorded against this game. `link` advertises
/// the excerpt via a resource-scoped Link header on every response this resource returns,
/// mirroring Frank.Rdf.Sample.Program's own `link` usage for its "alternate" JSON-LD
/// representation.
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

                accepts "application/alps+json" (Alps.excerpt (Some stateResolver))
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
