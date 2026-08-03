namespace Frank.Alps

[<RequireQualifiedAccess>]
module DescriptorTree =
    let rec flatten (d: Descriptor) : Descriptor list = d :: (d.Descriptors |> List.collect flatten)

    let flattenAll (profile: Descriptor list) : Descriptor list = profile |> List.collect flatten

    let rec private pruneOne (allowedIds: Set<string>) (d: Descriptor) : Descriptor option =
        if d.Type = DescriptorType.Semantic || Set.contains d.Id allowedIds then
            Some
                { d with
                    Descriptors = d.Descriptors |> List.choose (pruneOne allowedIds) }
        else
            None

    let prune (allowedIds: Set<string>) (profile: Descriptor list) : Descriptor list =
        profile |> List.choose (pruneOne allowedIds)
