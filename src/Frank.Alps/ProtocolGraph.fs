namespace Frank.Alps

type ProtocolTransition =
    { FromGuard: StateGuard option
      Transition: Descriptor
      ToTargets: TransitionTarget list }

module ProtocolGraph =
    let private deriveGuard (d: Descriptor) : StateGuard option =
        match d.Guard with
        | Some g -> Some g
        | None ->
            match d.From with
            | [] -> None
            | [ x ] -> Some(StateGuard.State x)
            | xs -> Some(StateGuard.Any(xs |> List.map StateGuard.State))

    let private deriveTargets (d: Descriptor) : TransitionTarget list =
        match d.Targets with
        | [] ->
            match d.Rt with
            | Some t -> [ TransitionTarget.EnterState t ]
            | None -> []
        | ts -> ts

    let ofProfile (profile: Descriptor list) : ProtocolTransition list =
        DescriptorTree.flattenAll profile
        |> List.choose (fun d ->
            match deriveTargets d with
            | [] -> None
            | targets ->
                Some
                    { FromGuard = deriveGuard d
                      Transition = d
                      ToTargets = targets })
