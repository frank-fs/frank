module Frank.Alps.Sample.TrafficLight

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Alps

/// Compound transitions: orthogonal (AND) regions, structural AND-guards, and unconditional
/// fan-out with `History` restore -- the frank-fs/frank#489 primitives (`StateGuard`,
/// `TransitionTarget`, `guardedBy`, `entersRegions`, `Excerpt.satisfiesGuard`) proven over real
/// HTTP, to the same standard `PingPong` proves a single `from`-state guard to. A signaled
/// intersection has TWO simultaneously active regions (`vehicleSignal`/`pedestrianSignal`), not
/// one lineage of states -- `walk` requires both at once (`All [ State vehicleRed; State
/// pedWaiting ]`), and `emergencyOverride`/`emergencyClear` enter/leave both regions together.
/// Mirrors the design doc's sketch (docs/superpowers/specs/2026-08-08-frank-alps-compound-
/// transitions-design.md, *Sketch: traffic light + pedestrian crossing*) with `def` IRIs added on
/// every leaf state -- `Excerpt.satisfiesGuard` and this sample's own `CurrentStateResolver`
/// compare states by `Uri`, so an authored guard needs something to compare against.
let vehicleGreen =
    semantic "vehicleGreen" |> initial |> def "https://trafficlight.example/states/vehicleGreen"
let vehicleRed = semantic "vehicleRed" |> def "https://trafficlight.example/states/vehicleRed"
let vehicleFlashing =
    semantic "vehicleFlashing" |> def "https://trafficlight.example/states/vehicleFlashing"
let vehicleSignal = semantic "vehicleSignal" |> contains [ vehicleGreen; vehicleRed ]

let pedWaiting =
    semantic "pedWaiting" |> initial |> def "https://trafficlight.example/states/pedWaiting"
let pedWalk = semantic "pedWalk" |> def "https://trafficlight.example/states/pedWalk"
let pedFlashing = semantic "pedFlashing" |> def "https://trafficlight.example/states/pedFlashing"
let pedestrianSignal = semantic "pedestrianSignal" |> contains [ pedWaiting; pedWalk ]

let intersection = semantic "intersection" |> doc "A signaled intersection" |> regions [ vehicleSignal; pedestrianSignal ]

let viewIntersection = safe "viewIntersection" |> rt intersection
let createIntersection = unsafe "createIntersection" |> rt intersection

/// The one guard-gated transition: an AND-guard over BOTH regions at once, not a single
/// `from`-state match like PingPong's `ping`/`pong`. `Excerpt.satisfiesGuard` folds
/// `StateGuard.All` structurally against the resolver's active-state list -- this is the
/// actual new proof this sample exists to deliver.
let walk =
    unsafe "walk"
    |> guardedBy (StateGuard.All [ StateGuard.State vehicleRed; StateGuard.State pedWaiting ])
    |> rt pedWalk

/// Unconditional fan-out: no guard at all (`ofProfile`'s "unconditional fan-out" derivation --
/// only `ToTargets` non-empty is required once `FromGuard` is independently optional), entering
/// BOTH regions' flashing state in a single transition.
let emergencyOverride =
    unsafe "emergencyOverride"
    |> entersRegions [ TransitionTarget.EnterState vehicleFlashing; TransitionTarget.EnterState pedFlashing ]

/// `History` targets: resumes whatever each region was ACTUALLY doing before the override, not
/// a hardcoded reset to the initial substate.
let emergencyClear =
    unsafe "emergencyClear"
    |> entersRegions [ TransitionTarget.History vehicleSignal; TransitionTarget.History pedestrianSignal ]

let profile =
    [ vehicleGreen; vehicleRed; vehicleFlashing; vehicleSignal
      pedWaiting; pedWalk; pedFlashing; pedestrianSignal
      intersection; viewIntersection; createIntersection; walk; emergencyOverride; emergencyClear ]

/// In-memory intersection directory -- demo purposes only, same convention as `PingPong`'s
/// `sessionIds`. Deliberately NOT `Frank.Provenance`-backed, unlike the game and ping/pong
/// resolvers: `Frank.Provenance`'s own model has exactly one notion of "state" (the domain type on
/// the most recently ended activity), which fits one lineage of states, not two SIMULTANEOUSLY
/// active orthogonal regions. Tracking both regions' current AND prior substate is a genuinely
/// different shape this sample doesn't force onto `Frank.Provenance`.
type private IntersectionState =
    { Vehicle: Uri
      Pedestrian: Uri
      PriorVehicle: Uri option
      PriorPedestrian: Uri option }

let private intersections = System.Collections.Concurrent.ConcurrentDictionary<Guid, IntersectionState>()

/// Mirrors `sessionPathOf`'s style of simple string manipulation: the last path segment is always
/// the intersection id, whether the request is "/intersections/{id}", ".../{id}/walk",
/// ".../{id}/emergencyOverride", or ".../{id}/emergencyClear".
let private intersectionIdOf (path: string) : Guid =
    let segments = path.Split('/') |> Array.filter (fun s -> s <> "")
    let idSegment = if segments.Length >= 2 then segments.[1] else segments.[0]
    Guid idSegment

/// Two elements, one per active orthogonal region -- matching `CurrentStateResolver`'s documented
/// contract (design doc, *State-based filtering*) directly, no `Frank.Provenance` graph walk needed
/// since `intersections` already holds exactly the shape the contract wants.
let private trafficLightResolver: CurrentStateResolver =
    fun path ->
        match intersections.TryGetValue(intersectionIdOf path) with
        | true, s -> [ s.Vehicle; s.Pedestrian ]
        | false, _ -> []

/// Resolves a raw state `Uri` back to its human-readable descriptor id, so `viewIntersectionHandler`
/// can report e.g. `"vehicleRed"` rather than the bare IRI.
let private stateNameOf (uri: Uri) : string =
    profile
    |> List.tryFind (fun d -> d.Def = Some uri)
    |> Option.map (fun d -> d.Id)
    |> Option.defaultValue (string uri)

/// Seeds `{ Vehicle = vehicleRed; Pedestrian = pedWaiting }` -- DELIBERATE: `walk`'s AND-guard is
/// satisfied immediately at creation, so the very first excerpt shows `walk` and the first POST
/// succeeds; a second POST then genuinely fails because the guard is no longer satisfied
/// (pedestrian has moved to `pedWalk`). Proves both the positive and negative case with no extra
/// transitions invented beyond the design doc's sketch.
let private createIntersectionHandler (ctx: HttpContext) : Task =
    task {
        let id = Guid.NewGuid()

        intersections.[id] <-
            { Vehicle = vehicleRed.Def.Value
              Pedestrian = pedWaiting.Def.Value
              PriorVehicle = None
              PriorPedestrian = None }

        do! ctx.Response.WriteAsJsonAsync {| id = string id |}
    }
    :> Task

let private viewIntersectionHandler (ctx: HttpContext) : Task =
    task {
        match intersections.TryGetValue(intersectionIdOf ctx.Request.Path.Value) with
        | true, s ->
            do! ctx.Response.WriteAsJsonAsync {| vehicle = stateNameOf s.Vehicle; pedestrian = stateNameOf s.Pedestrian |}
        | false, _ ->
            ctx.Response.StatusCode <- 404
    }
    :> Task

/// The structural-guard proof: evaluates `Excerpt.satisfiesGuard` against the intersection's
/// current active states -- NOT a single `from`-based state match like `pingPongMoveHandler`
/// -- and only mutates state on success, mirroring `pingPongMoveHandler`'s 409/no-side-effect
/// convention on failure.
let private walkHandler (ctx: HttpContext) : Task =
    task {
        let id = intersectionIdOf ctx.Request.Path.Value

        match intersections.TryGetValue id with
        | true, state ->
            let guard = walk.Guard |> Option.get

            if Excerpt.satisfiesGuard [ state.Vehicle; state.Pedestrian ] guard then
                intersections.[id] <- { state with Pedestrian = pedWalk.Def.Value }
                do! ctx.Response.WriteAsJsonAsync {| ok = true |}
            else
                ctx.Response.StatusCode <- 409
                do! ctx.Response.WriteAsJsonAsync {| error = "intersection is not in the required state (vehicleRed and pedWaiting)" |}
        | false, _ ->
            ctx.Response.StatusCode <- 404
    }
    :> Task

/// Unconditional (`emergencyOverride.Guard` is `None`, matching `ofProfile`'s "unconditional
/// fan-out" derivation) -- captures each region's current state as its prior state before
/// overwriting it, so `emergencyClearHandler` below has something real to restore.
let private emergencyOverrideHandler (ctx: HttpContext) : Task =
    task {
        let id = intersectionIdOf ctx.Request.Path.Value

        match intersections.TryGetValue id with
        | true, state ->
            intersections.[id] <-
                { state with
                    Vehicle = vehicleFlashing.Def.Value
                    Pedestrian = pedFlashing.Def.Value
                    PriorVehicle = Some state.Vehicle
                    PriorPedestrian = Some state.Pedestrian }

            do! ctx.Response.WriteAsJsonAsync {| ok = true |}
        | false, _ ->
            ctx.Response.StatusCode <- 404
    }
    :> Task

/// Unconditional. The real proof of `History` semantics: restores whatever was ACTUALLY active
/// before the override (which may be mid-cycle, e.g. vehicleRed/pedWalk after a `walk` already
/// fired), not a hardcoded reset to the initial state.
let private emergencyClearHandler (ctx: HttpContext) : Task =
    task {
        let id = intersectionIdOf ctx.Request.Path.Value

        match intersections.TryGetValue id with
        | true, state ->
            intersections.[id] <-
                { state with
                    Vehicle = state.PriorVehicle |> Option.defaultValue vehicleGreen.Def.Value
                    Pedestrian = state.PriorPedestrian |> Option.defaultValue pedWaiting.Def.Value
                    PriorVehicle = None
                    PriorPedestrian = None }

            do! ctx.Response.WriteAsJsonAsync {| ok = true |}
        | false, _ ->
            ctx.Response.StatusCode <- 404
    }
    :> Task

let intersectionsResource =
    resource "/intersections" {
        post (handler {
            handle createIntersectionHandler
            binds createIntersection
        })
    }

let intersectionResource =
    resource "/intersections/{id}" {
        get (
            negotiate {
                accepts "application/json" (handler {
                    handle viewIntersectionHandler
                    binds viewIntersection
                })

                accepts "application/alps+json" (Alps.excerpt (Some trafficLightResolver))
            }
        )
    }

/// GET here serves only the ALPS excerpt (there is no plain-JSON representation of "the walk
/// action") -- same shape as `pingResource`/`pongResource`, and for the same reason:
/// `Alps.excerpt`'s `descriptorsForRoute` matches an endpoint's bound descriptors by EXACT route
/// pattern (`EndpointSurface.descriptorsForRoute`), so `walk`'s guard-filtered presence can only be
/// observed over HTTP by GETting the SAME url its POST is bound to -- `/intersections/{id}`'s own
/// excerpt (a different route pattern) would never see it, no matter its guard.
let walkResource =
    resource "/intersections/{id}/walk" {
        get (Alps.excerpt (Some trafficLightResolver))

        post (handler {
            handle walkHandler
            binds walk
        })
    }

let emergencyOverrideResource =
    resource "/intersections/{id}/emergencyOverride" {
        get (Alps.excerpt (Some trafficLightResolver))

        post (handler {
            handle emergencyOverrideHandler
            binds emergencyOverride
        })
    }

let emergencyClearResource =
    resource "/intersections/{id}/emergencyClear" {
        get (Alps.excerpt (Some trafficLightResolver))

        post (handler {
            handle emergencyClearHandler
            binds emergencyClear
        })
    }
