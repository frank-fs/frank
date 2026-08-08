module Frank.Alps.TrafficLightSample.Program

open Frank.Alps

let vehicleGreen = semantic "vehicleGreen" |> initial |> def "https://frank-fs.github.io/alps-samples/traffic-light#vehicleGreen"
let vehicleRed = semantic "vehicleRed" |> def "https://frank-fs.github.io/alps-samples/traffic-light#vehicleRed"
let vehicleSignal = semantic "vehicleSignal" |> contains [ vehicleGreen; vehicleRed ]

let pedWaiting = semantic "pedWaiting" |> initial |> def "https://frank-fs.github.io/alps-samples/traffic-light#pedWaiting"
let pedWalk = semantic "pedWalk" |> def "https://frank-fs.github.io/alps-samples/traffic-light#pedWalk"
let pedestrianSignal = semantic "pedestrianSignal" |> contains [ pedWaiting; pedWalk ]

let intersection = semantic "intersection" |> regions [ vehicleSignal; pedestrianSignal ]

let walk =
    unsafe "walk"
    |> guardedBy (StateGuard.All [ StateGuard.State vehicleRed; StateGuard.State pedWaiting ])
    |> rt pedWalk

let vehicleFlashing = semantic "vehicleFlashing"
let pedFlashing = semantic "pedFlashing"

let emergencyOverride =
    unsafe "emergencyOverride"
    |> entersRegions [ TransitionTarget.EnterState vehicleFlashing; TransitionTarget.EnterState pedFlashing ]

let emergencyClear =
    unsafe "emergencyClear"
    |> entersRegions [ TransitionTarget.History vehicleSignal; TransitionTarget.History pedestrianSignal ]

let profile =
    [ intersection; walk; emergencyOverride; emergencyClear ]

[<EntryPoint>]
let main _ =
    let edges = ProtocolGraph.ofProfile profile

    let describe (e: ProtocolTransition) =
        let guard =
            match e.FromGuard with
            | None -> "unconditional"
            | Some g -> sprintf "%A" g
        let targets = e.ToTargets |> List.map (sprintf "%A") |> String.concat ", "
        sprintf "%s -- guard: %s -- targets: %s" e.Transition.Id guard targets

    edges |> List.iter (describe >> printfn "%s")

    assert (edges |> List.exists (fun e -> e.Transition.Id = "walk" && e.FromGuard.IsSome))
    assert (edges |> List.exists (fun e -> e.Transition.Id = "emergencyOverride" && e.FromGuard.IsNone && e.ToTargets.Length = 2))
    assert (edges |> List.exists (fun e -> e.Transition.Id = "emergencyClear" && (e.ToTargets |> List.forall (function TransitionTarget.History _ -> true | _ -> false))))

    let step1ActiveStates = [ vehicleRed.Def.Value; pedWaiting.Def.Value ]
    let walkGuard = walk.Guard |> Option.get
    printfn "walk enabled at step1: %b" (Excerpt.satisfiesGuard step1ActiveStates walkGuard)

    0
