namespace Frank.Alps

open System

[<AutoOpen>]
module DescriptorFunctions =
    let private makeDescriptor (id: string) (descriptorType: DescriptorType) : Descriptor =
        { Id = id
          Name = None
          Type = descriptorType
          Def = None
          Doc = None
          Ext = []
          InheritsFrom = None
          From = []
          Guard = None
          Rt = None
          Targets = []
          Rel = None
          Tag = []
          Link = []
          Descriptors = [] }

    let semantic (id: string) : Descriptor = makeDescriptor id DescriptorType.Semantic
    let safe (id: string) : Descriptor = makeDescriptor id DescriptorType.Safe
    let unsafe (id: string) : Descriptor = makeDescriptor id DescriptorType.Unsafe
    let idempotent (id: string) : Descriptor = makeDescriptor id DescriptorType.Idempotent

    let doc (text: string) (d: Descriptor) : Descriptor =
        { d with
            Doc =
                Some
                    { Value = text
                      Href = None
                      Format = None
                      ContentType = None
                      Tag = [] } }

    let docWith (doc: Doc) (d: Descriptor) : Descriptor = { d with Doc = Some doc }

    let def (iri: string) (d: Descriptor) : Descriptor = { d with Def = Some(Uri iri) }

    let tag (value: string) (d: Descriptor) : Descriptor = { d with Tag = d.Tag @ [ value ] }

    let rel (relation: string) (d: Descriptor) : Descriptor = { d with Rel = Some relation }

    let named (name: string) (d: Descriptor) : Descriptor = { d with Name = Some name }

    let ext (id: string) (value: string) (d: Descriptor) : Descriptor =
        { d with
            Ext =
                d.Ext
                @ [ { Id = id
                      Href = None
                      Value = Some value
                      Tag = [] } ] }

    let extWith (ext: Ext) (d: Descriptor) : Descriptor = { d with Ext = d.Ext @ [ ext ] }

    let link (href: string) (rel: string) (d: Descriptor) : Descriptor =
        { d with
            Link =
                d.Link
                @ [ { Href = Uri href
                      Rel = rel
                      Title = None
                      Tag = [] } ] }

    let linkWith (link: Link) (d: Descriptor) : Descriptor = { d with Link = d.Link @ [ link ] }

    [<Literal>]
    let InitialExtId = "https://frank-fs.github.io/alps-ext/initial"

    [<Literal>]
    let OrthogonalExtId = "https://frank-fs.github.io/alps-ext/orthogonal"

    let internal hasExtId (extId: string) (d: Descriptor) : bool =
        d.Ext |> List.exists (fun e -> e.Id = extId)

    let contains (children: Descriptor list) (d: Descriptor) : Descriptor =
        let initialCount = children |> List.filter (hasExtId InitialExtId) |> List.length

        if initialCount > 1 then
            failwithf
                "Frank.Alps: descriptor '%s' has %d children marked `initial`, at most one is allowed"
                d.Id
                initialCount

        { d with Descriptors = children }

    let initial (d: Descriptor) : Descriptor =
        { d with
            Ext =
                d.Ext
                @ [ { Id = InitialExtId
                      Href = None
                      Value = None
                      Tag = [] } ] }

    let regions (children: Descriptor list) (d: Descriptor) : Descriptor =
        { d with
            Descriptors = children
            Ext =
                d.Ext
                @ [ { Id = OrthogonalExtId
                      Href = None
                      Value = None
                      Tag = [] } ] }

    let rt (target: Descriptor) (d: Descriptor) : Descriptor = { d with Rt = Some target }

    let href (target: Descriptor) (d: Descriptor) : Descriptor =
        { d with
            InheritsFrom = Some(DescriptorRef.Local target) }

    let hrefExternal (uri: string) (d: Descriptor) : Descriptor =
        { d with
            InheritsFrom = Some(DescriptorRef.External(Uri uri)) }

    let from (sources: Descriptor list) (d: Descriptor) : Descriptor = { d with From = sources }

[<RequireQualifiedAccess>]
type StateComposition =
    | Leaf
    | Alternatives of Descriptor list
    | Regions of Descriptor list

[<RequireQualifiedAccess>]
module StateComposition =
    let ofDescriptor (d: Descriptor) : StateComposition =
        match d.Descriptors with
        | [] -> StateComposition.Leaf
        | children when DescriptorFunctions.hasExtId DescriptorFunctions.OrthogonalExtId d -> StateComposition.Regions children
        | children -> StateComposition.Alternatives children

    let initialChild (d: Descriptor) : Descriptor option =
        match ofDescriptor d with
        | StateComposition.Alternatives children -> children |> List.tryFind (DescriptorFunctions.hasExtId DescriptorFunctions.InitialExtId)
        | StateComposition.Regions _
        | StateComposition.Leaf -> None
