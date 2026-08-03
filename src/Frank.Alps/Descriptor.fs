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
