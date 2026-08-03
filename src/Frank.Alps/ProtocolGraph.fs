namespace Frank.Alps

type ProtocolTransition =
    { FromState: Descriptor
      Transition: Descriptor
      ToState: Descriptor }

module ProtocolGraph =
    let rec private flatten (d: Descriptor) : Descriptor list = d :: (d.Descriptors |> List.collect flatten)

    let ofProfile (profile: Descriptor list) : ProtocolTransition list =
        profile
        |> List.collect flatten
        |> List.collect (fun d ->
            match d.Rt with
            | Some toState when not (List.isEmpty d.From) ->
                d.From
                |> List.map (fun fromState ->
                    { FromState = fromState
                      Transition = d
                      ToState = toState })
            | _ -> [])
