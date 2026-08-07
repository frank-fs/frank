module Frank.Alps.Sample.Program

open System
open System.Collections.Generic
open System.Security.Claims
open System.Text.Encodings.Web
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging.Abstractions
open VDS.RDF
open Frank.Builder
open Frank.Auth
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

    let viewGame = descriptor "viewGame" { safe; rt game }
    let makeMove = descriptor "makeMove" { unsafe; from [ openState ]; rt closedState }

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

/// The ping-pong protocol: two states (`awaitingPing`/`awaitingPong`), a `session` resource, and
/// two role-gated transitions (`ping`/`pong`) that alternate between them. `participant` is
/// deliberately never `binds`-bound to any endpoint -- it exists only to be `href`-referenced from
/// `ping`/`pong`, so it only ever lives in the full document at `/.well-known/alps.json`, and every
/// excerpt that references it (both `/sessions/{id}/ping` and `/sessions/{id}/pong`) has to resolve
/// that reference through Task 1's cross-document `resolveRef` fallback -- there is no other way for
/// it to be present. Added alongside `Catalog`, not merged into it: a separate protocol, sharing
/// nothing with tic-tac-toe except this one `useAlps` document and the `Frank.Provenance` glue
/// pattern below.
module PingPong =
    let participant = semantic "participant" |> doc "A session participant"

    let awaitingPing =
        semantic "awaitingPing" |> doc "Waiting for a ping"
        |> def "https://pingpong.example/states/awaitingPing"
    let awaitingPong =
        semantic "awaitingPong" |> doc "Waiting for a pong"
        |> def "https://pingpong.example/states/awaitingPong"
    let session = semantic "session" |> doc "A ping-pong session"

    let listSessions = safe "listSessions" |> rt session
    let createSession = unsafe "createSession" |> rt session
    let viewSession = safe "viewSession" |> rt session

    let ping = unsafe "ping" |> from [ awaitingPing ] |> rt awaitingPong |> href participant
    let pong = unsafe "pong" |> from [ awaitingPong ] |> rt awaitingPing |> href participant

/// Demo-only authentication for the ping/pong endpoints -- verbatim shape of
/// sample/Frank.JsonHome.Sample/ApiKeyAuth.fs (an "X-Api-Key" header mapped to a hardcoded
/// user/roles table), a separate scheme/table from that sample's own since this app has its own
/// two test principals rather than admin/anonymous.
module PingPongAuth =
    [<Literal>]
    let SchemeName = "PingPongApiKey"

    let private users: IDictionary<string, string * string list> =
        dict [ "pinger-key", ("pinger", [ "pinger" ]); "ponger-key", ("ponger", [ "ponger" ]) ]

    type ApiKeyAuthHandler(options, logger, encoder: UrlEncoder) =
        inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

        override this.HandleAuthenticateAsync() =
            let key = this.Request.Headers["X-Api-Key"].ToString()

            match users.TryGetValue key with
            | true, (name, roles) ->
                let claims = Claim(ClaimTypes.Name, name) :: (roles |> List.map (fun r -> Claim(ClaimTypes.Role, r)))
                let identity = ClaimsIdentity(claims, SchemeName)
                let ticket = AuthenticationTicket(ClaimsPrincipal identity, SchemeName)
                Task.FromResult(AuthenticateResult.Success ticket)
            | false, _ -> Task.FromResult(AuthenticateResult.NoResult())

// Ping-pong's own Frank.Provenance glue, reusing the SAME `store` instance declared above for
// tic-tac-toe rather than standing up a second one (judgment call from the task brief): the
// existing stateResolver's convention -- "the domain rdf:type asserted on the most recently ended
// activity that prov:wasGeneratedBy this resource" -- says nothing about there being only one state
// machine in the store. It extends cleanly to ping-pong's two alternating states as long as
// ping-pong's resource IRIs never collide with tic-tac-toe's, which `pingPongBaseUri` (distinct
// from `baseUri` above) guarantees. A second store would only be justified by an isolation
// requirement (e.g. separate persistence/retention) neither protocol has here.
let private pingPongBaseUri = "https://pingpong.example"

/// Ping-pong's three routes ("/sessions/{id}", ".../ping", ".../pong") name ONE session identity.
/// Stripping the action suffix is what makes a POST to ".../ping" and a later GET excerpt at
/// ".../pong" (or the plain "/sessions/{id}" view) resolve to the SAME resource IRI in the
/// provenance store -- without it, each route would look like a separate, never-moved resource.
let private sessionPathOf (path: string) : string =
    if path.EndsWith("/ping") then path.Substring(0, path.Length - "/ping".Length)
    elif path.EndsWith("/pong") then path.Substring(0, path.Length - "/pong".Length)
    else path

let private pingPongResourceIriFor (path: string) : string = pingPongBaseUri + sessionPathOf path

/// Same query/graph-walk as the tic-tac-toe `stateResolver` above, over `pingPongResourceIriFor`
/// instead of `resourceIriFor`, EXCEPT for what a session with no recorded activity yet resolves
/// to. Unlike tic-tac-toe (whose game has no meaningful state prior to any move -- `[]` correctly
/// means "no opinion, don't filter"), ping-pong's design DOES declare an initial state: "the
/// session's initial state (before any move) is awaitingPing" (design doc, *Sample: ping/pong*).
/// `Alps.excerpt` treats a resolver's `[]` as "state filtering does not apply at all" (see
/// Excerpt.fs's `| [] -> authAllowed` branch) -- NOT as "the state is empty" -- so returning `[]`
/// here would have let `pong` (guard `from [ awaitingPong ]`) show up in a fresh session's excerpt
/// even though that guard is never satisfied pre-first-move. Falling back to `awaitingPing.Def`
/// makes a fresh session correctly show `ping` and hide `pong`, actually proving the state-gating
/// story this sample exists to demonstrate.
let private pingPongStateResolver: CurrentStateResolver =
    fun path ->
        let resourceIri = pingPongResourceIriFor path

        let recordedStates =
            match store.Query(ProvenanceQuery.Latest resourceIri) with
            | SparqlQueryResult.Bindings _ -> []
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

        match recordedStates with
        | [] -> [ PingPong.awaitingPing.Def.Value ]
        | states -> states

/// Records a "this session was moved" activity typed as the state the move transitions INTO --
/// mirrors makeMoveHandler's convention exactly (ping types the activity `awaitingPong.Def`, pong
/// types it `awaitingPing.Def`). Shared by both handlers below since the only difference between a
/// ping and a pong is which state they transition into.
let private recordPingPongMove (ctx: HttpContext) (targetStateDef: Uri option) : Task =
    task {
        let now = DateTimeOffset.UtcNow

        store.Append(
            { Activity = Node.Iri $"{pingPongBaseUri}/activities/{Guid.NewGuid()}"
              Resource = Node.Iri(pingPongResourceIriFor ctx.Request.Path.Value)
              Agent = Node.Iri $"{pingPongBaseUri}/agents/anonymous"
              StartedAt = now
              EndedAt = now
              ActivityType = targetStateDef
              Properties = [] }
        )

        do! ctx.Response.WriteAsJsonAsync {| ok = true |}
    }
    :> Task

/// Task 5 (post-hoc addendum): unlike `makeMoveHandler` above -- which is deliberately left with no
/// server-side state enforcement -- ping/pong's design doc calls for a genuine 403/409 on a
/// wrong-turn call, not just an excerpt that quietly stops listing the transition. Reuses
/// `pingPongStateResolver` (the SAME resolver `Alps.excerpt` calls for this session) rather than a
/// second state-lookup mechanism, so "what the excerpt would show" and "what the POST enforces" can
/// never disagree.
let private currentStateSatisfies (path: string) (requiredState: Descriptor) : bool =
    match requiredState.Def with
    | Some target -> pingPongStateResolver path |> List.contains target
    | None -> false

/// Gates `recordPingPongMove` on the session's current state: proceeds (and returns whatever
/// `recordPingPongMove` returns, `{| ok = true |}` / 200) only if `requiredState` is satisfied,
/// otherwise 409s with no Provenance activity appended -- an invalid move has no recorded side
/// effect.
let private pingPongMoveHandler (requiredState: Descriptor) (targetStateDef: Uri option) (ctx: HttpContext) : Task =
    task {
        if currentStateSatisfies ctx.Request.Path.Value requiredState then
            do! recordPingPongMove ctx targetStateDef
        else
            ctx.Response.StatusCode <- 409
            do! ctx.Response.WriteAsJsonAsync {| error = $"session is not in the required state ({requiredState.Id})" |}
    }
    :> Task

let private pingHandler (ctx: HttpContext) : Task =
    pingPongMoveHandler PingPong.awaitingPing PingPong.awaitingPong.Def ctx

let private pongHandler (ctx: HttpContext) : Task =
    pingPongMoveHandler PingPong.awaitingPong PingPong.awaitingPing.Def ctx

/// In-memory session directory -- demo purposes only, same convention as this repo's other
/// in-memory samples (e.g. 002-datastar-sample). Not provenance's job: the store answers "what state
/// is this session in", not "which session ids exist".
let private sessionIds = System.Collections.Concurrent.ConcurrentBag<Guid>()

let private listSessionsHandler (ctx: HttpContext) : Task =
    ctx.Response.WriteAsJsonAsync {| sessions = sessionIds |> Seq.map string |> Seq.toList |}

let private createSessionHandler (ctx: HttpContext) : Task =
    task {
        let id = Guid.NewGuid()
        sessionIds.Add id
        do! ctx.Response.WriteAsJsonAsync {| id = string id |}
    }
    :> Task

let private getSessionHandler (ctx: HttpContext) : Task =
    ctx.Response.WriteAsJsonAsync {| id = ctx.Request.RouteValues.["id"] |}

let sessionsResource =
    resource "/sessions" {
        get (handler {
            handle listSessionsHandler
            binds PingPong.listSessions
        })

        post (handler {
            handle createSessionHandler
            binds PingPong.createSession
        })
    }

let sessionResource =
    resource "/sessions/{id}" {
        get (
            negotiate {
                accepts "application/json" (handler {
                    handle getSessionHandler
                    binds PingPong.viewSession
                })

                accepts "application/alps+json" (Alps.excerpt (Some pingPongStateResolver))
            }
        )
    }

/// GET here serves only the ALPS excerpt (there is no plain-JSON representation of "the ping
/// action") -- `requireRole "pinger"` gates BOTH methods on this resource, so an unauthorized
/// `GET .../ping?Accept=application/alps+json` 403s before it ever reaches Alps.excerpt, exactly
/// like the POST does.
let pingResource =
    resource "/sessions/{id}/ping" {
        requireRole "pinger"
        get (Alps.excerpt (Some pingPongStateResolver))

        post (handler {
            handle pingHandler
            binds PingPong.ping
        })
    }

let pongResource =
    resource "/sessions/{id}/pong" {
        requireRole "ponger"
        get (Alps.excerpt (Some pingPongStateResolver))

        post (handler {
            handle pongHandler
            binds PingPong.pong
        })
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        useAuthentication (fun auth ->
            // Same DefaultScheme rationale as sample/Frank.JsonHome.Sample/Program.fs: lets
            // UseAuthentication populate ctx.User without every requireRole-guarded resource
            // having to name a scheme explicitly.
            auth.Services.Configure<AuthenticationOptions>(fun (o: AuthenticationOptions) ->
                o.DefaultScheme <- PingPongAuth.SchemeName
                o.DefaultAuthenticateScheme <- PingPongAuth.SchemeName)
            |> ignore

            auth.AddScheme<AuthenticationSchemeOptions, PingPongAuth.ApiKeyAuthHandler>(
                PingPongAuth.SchemeName,
                fun _ -> ()
            ))

        useAuthorization

        resource gameResource
        resource sessionsResource
        resource sessionResource
        resource pingResource
        resource pongResource

        useAlps
            [ Catalog.openState
              Catalog.closedState
              Catalog.game
              Catalog.viewGame
              Catalog.makeMove
              PingPong.participant
              PingPong.awaitingPing
              PingPong.awaitingPong
              PingPong.session
              PingPong.listSessions
              PingPong.createSession
              PingPong.viewSession
              PingPong.ping
              PingPong.pong ]
    }

    0
