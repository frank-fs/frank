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
          Rt = None
          From = []
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

    let contains (children: Descriptor list) (d: Descriptor) : Descriptor = { d with Descriptors = children }

    let rt (target: Descriptor) (d: Descriptor) : Descriptor = { d with Rt = Some target }

    let href (target: Descriptor) (d: Descriptor) : Descriptor =
        { d with
            InheritsFrom = Some(DescriptorRef.Local target) }

    let hrefExternal (uri: string) (d: Descriptor) : Descriptor =
        { d with
            InheritsFrom = Some(DescriptorRef.External(Uri uri)) }
