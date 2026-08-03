namespace Frank.Alps

type ProtocolTransition =
    { FromState: Descriptor
      Transition: Descriptor
      ToState: Descriptor }

module ProtocolGraph =
    let ofProfile (profile: Descriptor list) : ProtocolTransition list =
        // DescriptorTree.flattenAll is this module's own former private `flatten`, promoted so the
        // authorization filtering in AlpsDocument/Excerpt walks the tree exactly the same way.
        DescriptorTree.flattenAll profile
        |> List.collect (fun d ->
            match d.Rt with
            | Some toState when not (List.isEmpty d.From) ->
                d.From
                |> List.map (fun fromState ->
                    { FromState = fromState
                      Transition = d
                      ToState = toState })
            | _ -> [])
